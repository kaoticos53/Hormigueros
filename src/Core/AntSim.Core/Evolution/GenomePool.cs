using System;
using System.Collections.Generic;
using AntSim.Core.Sim;

namespace AntSim.Core.Evolution;

public enum TrialResult : byte
{
    EnteredElite = 0,
    Discarded = 1
}

/// <summary>
/// Pool élite de genomas (Fase 2):
/// - Nacimiento: torneo binario entre élite → crossover uniforme → mutación σ.
/// - Reemplazo: al morir, el fitness de por vida intenta entrar (mejor que el peor).
/// - Inmigración con cuarentena: los genomas importados ocupan eclosiones dentro
///   de una ventana (120 s) en orden de mérito de origen; al morir la hormiga de
///   prueba entran a la élite si fitness ≥ p50 (o, con diversidad baja, p25 +
///   novedad). Nunca degradan el pool: se descartan si no demuestran valía.
/// </summary>
public sealed class GenomePool
{
    public const int EliteCapacity = 64;
    public const float DiversityFloor = 0.03f;
    public const int ImmigrantWindowSeconds = 120;
    public const int ImmigrantWindowTicks = ImmigrantWindowSeconds * 30;

    private readonly List<MlpGenome> _elite = new();
    private readonly List<(MlpGenome Genome, ulong QueuedTick)> _immigrants = new();
    // Modo REUSO DE CEREBROS — los nacimientos CLONAN un genoma de la élite
    // (elección determinista con el MISMO torneo que el modo cruce) en vez de
    // cruzar+mutar. Opt-in por colonia (world design consciente): con élite
    // pequeña produce varios cuerpos con la MISMA huella de genoma — el linaje
    // multi-cuerpo que la tarjeta de inspección sigue (contrato HUD §5).
    private bool _cloneFromElite;
    // No readonly: DeterministicRandom es un struct y mutar una copia defensiva
    // descartaría el avance del flujo (bug corregido en Fase 3).
    private DeterministicRandom _rng;
    private readonly int[] _sizes;

    public int TrialsEntered;
    public int TrialsDiscarded;
    public int TrialsExpired;

    // — Checkpoints (.antsave): el RNG del pool es parte del estado —

    /// <summary>Estado exacto del RNG del pool (serialización de checkpoints).</summary>
    internal (ulong S0, ulong S1, ulong S2, ulong S3) RngState => _rng.State;

    /// <summary>Restaura el RNG del pool desde su estado exacto (carga de checkpoints).</summary>
    internal void RestoreRng(in DeterministicRandom rng) => _rng = rng;

    /// <summary>Copia los inmigrantes pendientes con su tick de encolado (checkpoints).</summary>
    internal IReadOnlyList<(MlpGenome Genome, ulong QueuedTick)> CopyPendingImmigrants()
        => _immigrants.ToArray();

    public int EliteCount => _elite.Count;
    public int PendingImmigrants => _immigrants.Count;

    /// <summary>Élite ordenada por fitness descendente (vista de solo lectura).</summary>
    public IReadOnlyList<MlpGenome> Elite => _elite;

    public double BestFitness => _elite.Count > 0 ? _elite[0].Fitness : 0.0;

    public double AvgFitness
    {
        get
        {
            if (_elite.Count == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < _elite.Count; i++) sum += _elite[i].Fitness;
            return sum / _elite.Count;
        }
    }

    public GenomePool(DeterministicRandom rng, int[] sizes, int seedCount = 16,
        bool cloneFromElite = false)
    {
        _rng = rng;
        _sizes = (int[])sizes.Clone();
        _cloneFromElite = cloneFromElite;
        for (int i = 0; i < seedCount; i++)
            TryAdd(MlpGenome.Random(ref _rng, _sizes));
    }

    /// <summary>Modo reuso de cerebros activo (los nacimientos clonan élite).</summary>
    public bool CloneFromElite => _cloneFromElite;

    /// <summary>Activa/desactiva el modo clonado. Solo afecta a nacimientos
    /// POSTERIORES: los cerebros ya asignados no cambian (los cuerpos existentes
    /// conservan su huella — el linaje es histórico, no retroactivo).</summary>
    public void SetCloneFromElite(bool on) => _cloneFromElite = on;

    /// <summary>Restauración desde checkpoints (.antsave).</summary>
    internal void RestoreCloneFromElite(bool on) => _cloneFromElite = on;

    /// <summary>
    /// Nacimiento: torneo binario entre dos élite → crossover uniforme →
    /// mutación gaussiana. Fitness inicial 0.
    /// Modo clonado (CloneFromElite): el ganador del MISMO torneo se CLONA sin
    /// cruzar ni mutar (consumo de RNG: solo las 2 tiradas del torneo). La
    /// reproducción completa sigue disponible para restaurar el modo; el
    /// determinismo por semilla se mantiene dentro de cada modo y los
    /// checkpoints serializan el modo junto al RNG del pool.
    /// </summary>
    public MlpGenome Birth()
    {
        if (_cloneFromElite && _elite.Count > 0)
        {
            var pick = Tournament();
            var clone = pick.Clone();
            clone.Fitness = 0.0;
            return clone;
        }
        var a = Tournament();
        var b = Tournament();
        var child = MlpGenome.Crossover(a, b, ref _rng);
        child.Mutate(ref _rng);
        child.Fitness = 0.0;
        return child;
    }

    /// <summary>Fitness de por vida de un genoma nativo: intenta entrar a la élite.</summary>
    public void RecordFitness(MlpGenome genome, double fitness)
    {
        if (genome is null || double.IsNaN(fitness) || double.IsInfinity(fitness)) return;
        genome.Fitness = fitness;
        TryAdd(genome);
    }

    /// <summary>
    /// Reemplaza la élite completa por una lista de genomas (Fase 3: siembra
    /// del pool con poblaciones pre-entrenadas). Inserta ordenado por fitness
    /// descendente y respeta la capacidad; los genomas se clonan para que el
    /// pool sea dueño de su estado.
    /// </summary>
    public void ReplaceElite(IReadOnlyList<MlpGenome> genomes)
    {
        _elite.Clear();
        if (genomes is null) return;
        foreach (var g in genomes)
            TryAdd(g.Clone());
    }

    /// <summary>Inserta ordenado por fitness descendente; descarta si es peor que el peor élite.</summary>
    public bool TryAdd(MlpGenome genome)
    {
        if (genome is null || double.IsNaN(genome.Fitness)) return false;
        int insert = _elite.Count;
        for (int i = 0; i < _elite.Count; i++)
        {
            if (ReferenceEquals(_elite[i], genome)) return false; // ya está
            if (genome.Fitness > _elite[i].Fitness) { insert = i; break; }
        }

        if (_elite.Count >= EliteCapacity)
        {
            if (insert >= _elite.Count) return false; // peor que el peor
            _elite.Insert(insert, genome);
            _elite.RemoveAt(_elite.Count - 1);
        }
        else
        {
            _elite.Insert(insert, genome);
        }
        return true;
    }

    /// <summary>
    /// Diversidad v1 en espacio de pesos (media de distancias de pares fijos).
    /// Determinista y sin consumir el RNG de la simulación.
    /// </summary>
    public double Diversity()
    {
        int n = _elite.Count;
        if (n < 2) return 0.0;
        double sum = 0.0;
        int pairs = 0;
        int half = n / 2;
        for (int i = 0; i < n && pairs < 256; i++)
        {
            int j1 = (i + 1) % n;
            int j2 = (i + half) % n;
            sum += _elite[i].DistanceTo(_elite[j1]);
            pairs++;
            if (j2 != j1)
            {
                sum += _elite[i].DistanceTo(_elite[j2]);
                pairs++;
            }
        }
        return sum / pairs;
    }

    /// <summary>Encola un inmigrante (orden de mérito = orden de la lista al importar).</summary>
    public void QueueImmigrant(MlpGenome genome, ulong nowTick)
    {
        if (genome is null) return;
        _immigrants.Add((genome, nowTick));
    }

    /// <summary>
    /// Devuelve el siguiente inmigrante pendiente (descartando los que vencieron
    /// la ventana). Consume el inmigrante: cada uno se prueba en una sola hormiga.
    /// </summary>
    public bool TryNextImmigrant(ulong nowTick, out MlpGenome genome)
    {
        // Poda de vencidos (en orden FIFO, determinista).
        for (int i = _immigrants.Count - 1; i >= 0; i--)
        {
            if (nowTick - _immigrants[i].QueuedTick > ImmigrantWindowTicks)
            {
                _immigrants.RemoveAt(i);
                TrialsExpired++;
            }
        }

        if (_immigrants.Count == 0)
        {
            genome = null!;
            return false;
        }

        genome = _immigrants[0].Genome;
        _immigrants.RemoveAt(0);
        return true;
    }

    /// <summary>
    /// Evalúa el resultado de un inmigrante al morir su hormiga de prueba.
    /// Entra a la élite si fitness ≥ p50; con diversidad baja (D &lt; D_floor)
    /// basta fitness ≥ p25 + novedad por encima de la diversidad media.
    /// </summary>
    public TrialResult CompleteTrial(MlpGenome trial, double fitness, ulong nowTick)
    {
        _ = nowTick;
        if (trial is null || double.IsNaN(fitness)) { TrialsDiscarded++; return TrialResult.Discarded; }

        trial.Fitness = fitness;
        bool diversityMode = Diversity() < DiversityFloor;
        double p50 = Percentile(0.5);
        double p25 = Percentile(0.25);

        bool admitted = fitness >= p50;
        if (!admitted && diversityMode && fitness >= p25)
        {
            double avg = Diversity();
            double novelty = double.MaxValue;
            for (int i = 0; i < _elite.Count; i++)
                novelty = Math.Min(novelty, trial.DistanceTo(_elite[i]));
            admitted = novelty >= avg;
        }

        if (admitted && TryAdd(trial))
        {
            TrialsEntered++;
            return TrialResult.EnteredElite;
        }

        TrialsDiscarded++;
        return TrialResult.Discarded;
    }

    private double Percentile(double q)
    {
        if (_elite.Count == 0) return 0.0;
        int idx = Math.Min(_elite.Count - 1, (int)((_elite.Count - 1) * q));
        return _elite[idx].Fitness;
    }

    private MlpGenome Tournament()
    {
        int a = _rng.NextInt(0, _elite.Count);
        int b = _rng.NextInt(0, _elite.Count);
        return _elite[a].Fitness >= _elite[b].Fitness ? _elite[a] : _elite[b];
    }
}