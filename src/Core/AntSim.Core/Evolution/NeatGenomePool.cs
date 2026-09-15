using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Sim;

namespace AntSim.Core.Evolution;

/// <summary>
/// Resumen de especiation del pool (F5.2c rodaja 3): nº de especies vivas,
/// tamaños y fitness medio por especie. Insumo del criterio de cierre
/// (200 generaciones sin colapsar a 1 especie) y de la telemetría futura.
/// </summary>
public sealed record PoolSpeciesStats(
    int SpeciesCount,
    IReadOnlyList<SpeciesStat> Species)
{
    /// <summary>Tamaño máx de especie (diagnóstico de dominancia).</summary>
    public int LargestSize => Species.Count > 0 ? Species.Max(s => s.Size) : 0;
}

public sealed record SpeciesStat(
    int Id,
    int Size,
    double AvgFitness,
    double BestFitness,
    /// <summary>Mejor representante (solo lectura, orden de élite).</summary>
    NeatGenome Best);

/// <summary>
/// Pool élite de genomas NEAT (F5.2c rodaja 3) — el análogo estructural de
/// <see cref="GenomePool"/> (v1), con SPECIATION por distancia genómica:
///
/// - Nacimiento: se elige la especie por CUOTA (sharing NEAT clásico: cada
///   especie cría proporcional a su fitness medio ajustado), representante
///   por torneo interno → crossover align-by-innovation dentro de la especie
///   → mutación (pesos/biases siempre; estructurales con sus probabilidades).
///   Un genoma sin especie compatible FUNDA una nueva — así las innovaciones
///   estructurales (fitness ≈ padre al nacer) no mueren en la selección.
/// - Reemplazo: igual que v1 — al morir, el genoma entra si es mejor que el
///   peor élite (la élite es global, ordenada por fitness; la especie solo
///   decide quién CRÍA, no quién VIVE).
/// - Registro de innovaciones compartido y reconstruido al inicio de cada
///   ronda de nacimientos (cero estado entre generaciones: misma semilla ⇒
///   misma secuencia — determinismo del pool intacto).
///
/// Las especies se recalculan bajo demanda (lazy, invalidadas al cambiar la
/// élite): la agrupación es O(n²) en distancia pero n ≤ 64 lo hace barato, y
/// asÍ no hay estado de especies que serializar en los checkpoints.
/// </summary>
public sealed class NeatGenomePool
{
    public const int EliteCapacity = GenomePool.EliteCapacity;
    public const float DiversityFloor = GenomePool.DiversityFloor;

    /// <summary>
    /// Umbral de especiation δt (diseño §3.5). La sonda de rodaja 3 midió el
    /// suelo del δ ESTRUCTURAL en este mundo: dos densos no emparentados
    /// (innovations canónicas idénticas 1..200, pesos al azar) quedan a
    /// δ ≈ 0.28 — el término c3·W̄ del peso NOISE satura el umbral — mientras
    /// que un skip añadido a su padre vale δ ≈ 0.005 y dos mutaciones δ ≈ 0.02:
    /// la estructura es ~50× más pequeña que el ruido de pesos. Un δt único
    /// sobre δ puro no puede separarlas (δt &gt; 0.28 = todo junto; δt &lt; 0.005
    /// = cada genoma una especie). Por eso el grupo mide PARIENTESCO, no
    /// forma: ver <see cref="GroupDistance"/>.
    /// </summary>
    public const float SpeciationThreshold = 0.4f;

    /// <summary>Distancia de AGRUPAMIENTO asignada a dos genomas SIN
    /// parientesco (supera a <see cref="SpeciationThreshold"/>: fundan especie).</summary>
    public const float UnrelatedDistance = 0.5f;

    /// <summary>El suelo de δ entre PARIENTES que la sonda midió (2 mutaciones
    /// de pesos ≈ 0.022): cualquier δ por debajo indica linaje común.</summary>
    public const float KinshipDeltaFloor = 0.05f;

    /// <summary>Probabilidades de mutación estructural (diseño §3.4).</summary>
    public const float ProbAddConn = NeatOperators.DefaultProbAddConn;
    public const float ProbAddNode = NeatOperators.DefaultProbAddNode;
    public const float ProbToggle = NeatOperators.DefaultProbToggle;

    private readonly List<NeatGenome> _elite = new();
    // No readonly: DeterministicRandom es un struct y los operadores lo piden
    // por ref — mutar una copia defensiva descartaría el avance del flujo
    // (la misma disciplina documentada en GenomePool).
    private DeterministicRandom _rng;
    private readonly float _speciationThreshold;
    private readonly float _probAddConn, _probAddNode, _probToggle;

    private List<List<NeatGenome>>? _speciesCache;
    private InnovationRegistry? _registryCache;

    public int TrialsEntered { get; private set; }
    public int TrialsDiscarded { get; private set; }

    public NeatGenomePool(DeterministicRandom rng, float? speciationThreshold = null,
        float? probAddConn = null, float? probAddNode = null, float? probToggle = null)
    {
        _rng = rng;
        _speciationThreshold = speciationThreshold ?? SpeciationThreshold;
        _probAddConn = probAddConn ?? ProbAddConn;
        _probAddNode = probAddNode ?? ProbAddNode;
        _probToggle = probToggle ?? ProbToggle;
    }

    public int EliteCount => _elite.Count;
    public double SpeciationThresholdValue => _speciationThreshold;

    /// <summary>Élite ordenada por fitness descendente (vista de solo lectura).</summary>
    public IReadOnlyList<NeatGenome> Elite => _elite;

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

    /// <summary>Resumen de especies vivo (recalcula si la élite cambió).</summary>
    public PoolSpeciesStats Stats
    {
        get
        {
            var species = Speciate();
            var list = new List<SpeciesStat>(species.Count);
            for (int i = 0; i < species.Count; i++)
            {
                var members = species[i];
                double avg = 0.0;
                double best = double.NegativeInfinity;
                NeatGenome bestG = members[0];
                foreach (var g in members)
                {
                    avg += g.Fitness;
                    if (g.Fitness > best) { best = g.Fitness; bestG = g; }
                }
                list.Add(new SpeciesStat(i, members.Count, avg / members.Count, best, bestG));
            }
            return new PoolSpeciesStats(species.Count, list);
        }
    }

    // ————— Población —————

    /// <summary>Siembra genomas aleatorios (grafo denso convertido, topología 19-8-6).</summary>
    public void SeedRandom(int[] sizes, int count)
    {
        if (sizes is null) throw new ArgumentNullException(nameof(sizes));
        for (int i = 0; i < count; i++)
        {
            int n = MlpBrain.ExpectedWeightCount(sizes);
            var w = new float[n];
            for (int j = 0; j < n; j++) w[j] = (float)(_rng.NextDouble01() * 2.0 - 1.0);
            TryAdd(NeatGenome.FromMlp(sizes, w));
        }
    }

    /// <summary>Siembra desde genomas ya construidos (warm-start: se clonan).</summary>
    public void Seed(IReadOnlyList<NeatGenome> genomes)
    {
        if (genomes is null) throw new ArgumentNullException(nameof(genomes));
        foreach (var g in genomes)
            TryAdd(g.Clone());
    }

    /// <summary>Reemplaza la élite completa (warm-start; ordena fitness desc).</summary>
    public void ReplaceElite(IReadOnlyList<NeatGenome> genomes)
    {
        _elite.Clear();
        InvalidateCaches();
        if (genomes is null) return;
        foreach (var g in genomes)
            TryAdd(g.Clone());
    }

    /// <summary>Inserta ordenado por fitness descendente; descarta si es peor que el peor élite.</summary>
    public bool TryAdd(NeatGenome genome)
    {
        if (genome is null || double.IsNaN(genome.Fitness)) return false;
        int insert = _elite.Count;
        for (int i = 0; i < _elite.Count; i++)
        {
            if (ReferenceEquals(_elite[i], genome)) return false; // ya está
            if (genome.Fitness > _elite[i].Fitness) { insert = i; break; }
        }

        InvalidateCaches();
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

    /// <summary>Fitness de por vida de un genoma nativo: intenta entrar a la élite.</summary>
    public void RecordFitness(NeatGenome genome, double fitness)
    {
        if (genome is null || double.IsNaN(fitness) || double.IsInfinity(fitness)) return;
        genome.Fitness = fitness;
        TryAdd(genome);
    }

    // ————— Especiation (diseño §3.5) —————

    /// <summary>
    /// Distancia de AGRUPAMIENTO: parienteesco (linaje compartido) si δ &lt;
    /// <see cref="KinshipDeltaFloor"/> — el suelo medido por la sonda, muy por
    /// debajo del ruido de pesos entre no emparentados — o δ estructural pura
    /// en caso contrario. El NEAT clásico necesita esta distinción cuando los
    /// genomas parten de una MISMA conversión densa (todas las innovations
    /// canónicas 1..200 iguales): sin ella, el término c3·W̄ del ruido de
    /// pesos (~0.28 entre densos al azar) satura cualquier δt razonable y la
    /// estructura (δ ≈ 0.005–0.05) resulta invisible. Con parienteesco, dos
    /// linajes que divergieron pronto quedan a <see cref="UnrelatedDistance"/>
    /// aunque su forma sea idéntica — y un grafo ajeno funda especie.
    /// </summary>
    public static double GroupDistance(NeatGenome a, NeatGenome b)
    {
        double d = a.DistanceTo(b);
        return d < KinshipDeltaFloor ? d : Math.Max(d, UnrelatedDistance);
    }

    /// <summary>
    /// Agrupa la élite por distancia de grupo &lt; δt: el primer miembro funda
    /// la especie y cada genoma posterior entra en la PRIMERA especie
    /// compatible (representación por representantes — la clásica de NEAT).
    /// Orden determinista: el orden de la élite (fitness desc). O(n²)
    /// distancias, n ≤ 64.
    /// </summary>
    private List<List<NeatGenome>> Speciate()
    {
        if (_speciesCache is not null) return _speciesCache;
        var species = new List<List<NeatGenome>>();
        foreach (var g in _elite)
        {
            List<NeatGenome>? home = null;
            foreach (var s in species)
            {
                if (GroupDistance(g, s[0]) < _speciationThreshold) { home = s; break; }
            }
            if (home is null) { home = new List<NeatGenome>(); species.Add(home); }
            home.Add(g);
        }
        _speciesCache = species;
        return species;
    }

    private void InvalidateCaches()
    {
        _speciesCache = null;
        _registryCache = null;
    }

    /// <summary>Registro de innovaciones vivo (perezoso, invalidado con la élite).</summary>
    private InnovationRegistry Registry => _registryCache ??= new InnovationRegistry(_elite);

    // ————— Nacimientos con cuotas (sharing NEAT) —————

    /// <summary>
    /// Nacimiento con cuota por especie: la especie se elige con probabilidad
    /// proporcional a su fitness medio AJUSTADO (sharing: multiplicado por su
    /// tamaño — la cuota clásica de NEAT), el representante por torneo interno
    /// binario y el segundo padre por torneo DENTRO de la misma especie
    /// (crossover intra-especie — align-by-innovation de rodaja 2). Tras el
    /// cruce: pesos/biases siempre y estructurales con sus probabilidades.
    /// Devuelve null si la élite está vacía.
    /// </summary>
    public NeatGenome? Birth()
    {
        if (_elite.Count == 0) return null;
        var species = Speciate();

        // Cuota: fitness medio × tamaño (sharing NEAT clásico). El ajuste
        // evita que la especie grande domine: con el mismo fitness medio,
        // duplicar el tamaño duplica la cuota — pero el fitness medio BAJA
        // al crecer con mediocres, frenando la expansión.
        double total = 0.0;
        var weights = new double[species.Count];
        for (int i = 0; i < species.Count; i++)
        {
            double avg = 0.0;
            foreach (var g in species[i]) avg += g.Fitness;
            avg /= species[i].Count;
            weights[i] = Math.Max(avg, 0.0) * species[i].Count; // sharing
            total += weights[i];
        }

        List<NeatGenome> chosen;
        if (total <= 0.0)
        {
            // Sin señal de fitness (arranque en frío): uniforme por especie.
            chosen = species[_rng.NextInt(0, species.Count)];
        }
        else
        {
            double roll = _rng.NextDouble01() * total;
            chosen = species[^1];
            for (int i = 0; i < species.Count; i++)
            {
                if (roll < weights[i]) { chosen = species[i]; break; }
                roll -= weights[i];
            }
        }

        var parentA = TournamentOf(chosen);
        var parentB = TournamentOf(chosen);
        var child = NeatOperators.Crossover(parentA, parentB, ref _rng);

        NeatOperators.MutateWeights(child, ref _rng);
        NeatOperators.MutateBiases(child, ref _rng);
        if (_rng.NextFloat01() < _probAddConn) NeatOperators.MutateAddConn(child, Registry, ref _rng);
        if (_rng.NextFloat01() < _probAddNode) NeatOperators.MutateAddNode(child, Registry, ref _rng);
        if (_rng.NextFloat01() < _probToggle) NeatOperators.MutateToggle(child, ref _rng);

        child.Fitness = 0.0;
        return child;
    }

    private NeatGenome TournamentOf(List<NeatGenome> species)
    {
        if (species.Count == 1) return species[0];
        int a = _rng.NextInt(0, species.Count);
        int b = _rng.NextInt(0, species.Count);
        return species[a].Fitness >= species[b].Fitness ? species[a] : species[b];
    }

    // ————— Diversidad e inmigración (mismos convenios que v1) —————

    /// <summary>Diversidad v2: media de distancia genómica estructural entre
    /// pares fijos de la élite (el análogo del Diversity() de v1, con δ NEAT).</summary>
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
            sum += GroupDistance(_elite[i], _elite[j1]);
            pairs++;
            if (j2 != j1)
            {
                sum += GroupDistance(_elite[i], _elite[j2]);
                pairs++;
            }
        }
        return sum / pairs;
    }

    /// <summary>
    /// Evalúa el resultado de un inmigrante/genoma probado: entra a la élite si
    /// fitness ≥ p50; con diversidad baja basta p25 + novedad (mismos umbrales
    /// que v1 — el genoma sin especie compatible funda nueva al entrar).
    /// </summary>
    public TrialResult CompleteTrial(NeatGenome trial, double fitness)
    {
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
                novelty = Math.Min(novelty, GroupDistance(trial, _elite[i]));
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
}
