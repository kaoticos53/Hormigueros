using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.World;

namespace AntSim.Core.Training;

/// <summary>Estadísticas de una generación (reporte determinista del trainer).</summary>
public readonly struct GenerationStats
{
    public readonly int Stage;
    public readonly int Generation;
    public readonly double BestFitness;
    public readonly double MeanFitness;
    public readonly int CompetentCount;

    public GenerationStats(int stage, int generation, double best, double mean, int competent)
    {
        Stage = stage;
        Generation = generation;
        BestFitness = best;
        MeanFitness = mean;
        CompetentCount = competent;
    }
}

/// <summary>Configuración de una etapa del currículo.</summary>
public sealed class CurriculumStage
{
    public string Name = "";
    public float MinDistance = WorldSim.NestMinSpawnDistance; // banda de distancia al nido
    public float MaxDistance = 260f;
    public int TickBudget = 3600;       // ticks máx por evaluación (~120 s)
    public double CompetenceFitness = 0.0; // fitness mínimo de COLONIA; 0 = sin umbral
    public int MinGenerations = 4;      // nunca avanzar antes de esto
    public int MaxGenerations = 60;     // tope duro: se avanza con lo que haya
}

/// <summary>
/// Trainer de pre-entrenamiento headless (Fase 3bis): entrena una población de
/// genomas sobre la arena REALISTA de WorldSim a velocidad máxima — colonia
/// completa con cría, comida a ≥ 200 u sin rastro plantado — con currículo por
/// etapas que ensancha la banda de distancia.
///
/// - Población por generación: evaluación completa en la arena (determinista),
///   selección por élite + torneo, crossover uniforme y mutación gaussiana.
/// - Etapas (Fase 3bis, arena realista): la comida está a ≥ 200 u del nido (el
///   mínimo del mundo); cada etapa ensancha la banda de distancia y alarga el
///   presupuesto para dar tiempo al relevo intergeneracional. La etapa se supera
///   cuando el mejor fitness de COLONIA ≥ umbral (si es > 0) tras MinGenerations,
///   o se corta en MaxGenerations: el techo real lo marca el tiempo, y la élite
///   ordenada por fitness es el producto de la etapa.
/// - Reporte determinista por generación vía <paramref name=\"onGeneration\"/>:
///   misma semilla ⇒ misma secuencia de estadísticas byte a byte.
/// </summary>
public sealed class CurriculumTrainer
{
    public const double EliteFraction = 0.25;
    public const int TournamentSize = 3;
    public const float MutationSigma = 0.08f;
    // Pruebas por genoma (Fase 3ter): el PRIMER ciclo completo no lo cierra una
    // fundadora (pickup a ≥200 u deja ~50–60 u de capacidad de regreso frente a
    // las ~200 necesarias — techo físico) sino el RELEVO: una descendiente
    // recoge la suelta al morir de una portadora (~170 u del nido) y completa el
    // tramo final. Ese encuentro es RARO: con una sola prueba por genoma el
    // muestreo es insuficiente para que la selección lo vea. Tres pruebas con
    // posiciones de comida distintas triplican las sueltas y los encuentros
    // (determinista: el RNG de pruebas avanza en orden fijo).
    public const int TrialsPerGenome = 3;

    private static readonly int[] Sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private readonly ulong _seed;
    // No readonly: DeterministicRandom es un struct y mutar una copia defensiva
    // descartaría el avance del flujo (bug corregido en Fase 3).
    private DeterministicRandom _rng;
    private readonly int _populationSize;
    private readonly IReadOnlyList<CurriculumStage> _stages;
    private readonly Action<GenerationStats>? _onGeneration;
    private readonly List<MlpGenome>? _seedGenomes;
    private readonly List<MlpGenome> _population = new();
    private readonly List<GenerationStats> _report = new();

    /// <summary>Secuencia completa de estadísticas por generación (determinista).</summary>
    public IReadOnlyList<GenerationStats> Report => _report;

    /// <summary>
    /// Crea el trainer. Con <paramref name="seedGenomes"/> no nulo, la población
    /// inicial NO es aleatoria sino SEMBRADA (warm-start, Fase 3ter): se toman
    /// hasta <paramref name="populationSize"/> genomas (ordenados por fitness
    /// descendente) y el resto se completa con variantes mutadas de la élite
    /// sembrada. Permite continuar un pre-entrenamiento desde un `.antgenome`
    /// existente en lugar de empezar de cero cada vez — el pool de 60 gens ya
    /// tiene homing por brújula y la arena nueva necesita evaluarlo/re-evolverlo
    /// bajo las constantes del mundo corregido (Fase 3ter).
    /// </summary>
    public CurriculumTrainer(ulong seed, int populationSize,
        IReadOnlyList<CurriculumStage>? stages = null, Action<GenerationStats>? onGeneration = null,
        IReadOnlyList<MlpGenome>? seedGenomes = null)
    {
        if (populationSize < 2) throw new ArgumentOutOfRangeException(nameof(populationSize), "Mínimo 2 genomas.");
        _seed = seed;
        _rng = new DeterministicRandom(seed);
        _populationSize = populationSize;
        _stages = stages ?? DefaultStages();
        _onGeneration = onGeneration;
        if (seedGenomes is { Count: > 0 })
        {
            foreach (var g in seedGenomes)
            {
                if (g is null) throw new ArgumentNullException(nameof(seedGenomes), "Genoma semilla nulo.");
                if (g.Sizes.Length != Sizes.Length) throw new ArgumentException(
                    $"Topología de la semilla incompatible: {string.Join("→", g.Sizes)} ≠ {string.Join("→", Sizes)}.",
                    nameof(seedGenomes));
                for (int i = 0; i < Sizes.Length; i++)
                {
                    if (g.Sizes[i] != Sizes[i]) throw new ArgumentException(
                        $"Topología de la semilla incompatible: {string.Join("→", g.Sizes)} ≠ {string.Join("→", Sizes)}.",
                        nameof(seedGenomes));
                }
            }
            _seedGenomes = new List<MlpGenome>(seedGenomes);
        }
    }

    /// <summary>
    /// Currículo por defecto de la arena realista (Fase 3bis), CALIBRADO
    /// empíricamente: la comida SIEMPRE nace a ≥ 200 u del nido (mínimo del mundo,
    /// fuera de la visión) en la banda CERCANA 200–260 u — la que maximiza el
    /// muestreo de pickups con densidad real del mundo.
    ///
    /// NO se ensancha la banda por etapa: calibrado empíricamente, la banda ancha
    /// (200–520 u) multiplica ×27 el área del anillo, los ítems se vuelven
    /// irencontrables y el densado de exploración (récord de distancia) pasa a
    /// dominar el fitness — evolución selecciona "correr hacia fuera" y olvida el
    /// pickup aprendido en la banda cercana (el mejor de la banda ancha: 0 pickups
    /// transferidos al mundo real frente a 3 del de banda cercana). El eje del
    /// currículo es el HORIZONTE TEMPORAL: el mismo régimen, con presupuesto para
    /// que el relevo intergeneracional (muerte con carga → cría completa) madure.
    ///
    /// Física del relevo: una fundadora no puede ida-y-vuelta a 200 u dentro de su
    /// vida (~148 s a VMax 2.7 frente a ~90–110 s), así que la política aprende a
    /// orientarse a casa por brújula y a soltar la carga al morir cerca del camino;
    /// la descendencia —con inflow— completa los ciclos.
    /// </summary>
    public static IReadOnlyList<CurriculumStage> DefaultStages()
    {
        return new List<CurriculumStage>
        {
            // "cercana": comida en 200–260 u (~3–5 veces la visión); una vida de fundadora.
            new() { Name = "cercana", MinDistance = 200f, MaxDistance = 260f, TickBudget = 3600, CompetenceFitness = 0.0, MinGenerations = 4, MaxGenerations = 30 },
            // "relevo": misma banda; presupuesto para morir CON carga y ver la suelta.
            new() { Name = "relevo", MinDistance = 200f, MaxDistance = 260f, TickBudget = 5400, CompetenceFitness = 0.0, MinGenerations = 4, MaxGenerations = 40 },
            // "mundo": misma banda; horizonte completo de validación (~300 s).
            new() { Name = "mundo", MinDistance = 200f, MaxDistance = 260f, TickBudget = 9000, CompetenceFitness = 0.0, MinGenerations = 4, MaxGenerations = 60 }
        };
    }

    /// <summary>
    /// Ejecuta el currículo completo. Devuelve la población final ordenada por
    /// fitness descendente (la élite que puede sembrar un pool de partida).
    /// </summary>
    public IReadOnlyList<MlpGenome> Run()
    {
        SeedPopulation();

        for (int s = 0; s < _stages.Count; s++)
        {
            var stage = _stages[s];
            int gen = 0;

            while (gen < stage.MaxGenerations)
            {
                gen++;
                Evaluate(stage, s + 1, gen, out double best, out double mean, out int competent);

                // La etapa se supera cuando el mejor fitness de colonia alcanza el
                // umbral (tras MinGenerations); con umbral 0 se agota el tope de
                // generaciones: el techo real lo marca el tiempo, y la élite final
                // es el producto de la etapa. Evolve() vive DENTRO del bucle: sin
                // selección por generación la población nunca mejora (bug corregido
                // en Fase 3).
                if (stage.CompetenceFitness > 0.0 && best >= stage.CompetenceFitness && gen >= stage.MinGenerations)
                    break;

                // Evolve SOLO si habrá otra evaluación: reproducir tras la última
                // evaluación de la etapa sustituía la población evaluada por hijos
                // con Fitness=0 — y esa población sin evaluar era la que devolvía
                // Run() y exportaba el CLI (el archivo .antgenome contenía 25%
                // de élite entrenada y 75% de mutantes sin evaluar).
                if (gen < stage.MaxGenerations)
                    Evolve();
            }
        }

        SortByFitness();
        return _population;
    }

    private void SeedPopulation()
    {
        _population.Clear();
        if (_seedGenomes is { Count: > 0 })
        {
            // Copia ordenada por fitness (el `.antgenome` ya viene ordenado por
            // mérito, pero el contrato del parámetro no debe asumirlo).
            var sorted = new List<MlpGenome>(_seedGenomes);
            sorted.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
            int take = Math.Min(_populationSize, sorted.Count);
            for (int i = 0; i < take; i++)
                _population.Add(sorted[i].Clone());

            // Relleno determinista con variantes mutadas de la élite sembrada
            // (mismo flujo de RNG ⇒ misma población inicial byte a byte).
            while (_population.Count < _populationSize)
            {
                var clone = sorted[_rng.NextInt(0, sorted.Count)].Clone();
                clone.Mutate(ref _rng, MutationSigma);
                clone.Fitness = 0.0;
                _population.Add(clone);
            }
            return;
        }
        for (int i = 0; i < _populationSize; i++)
            _population.Add(MlpGenome.Random(ref _rng, Sizes));
    }

    private void Evaluate(CurriculumStage stage, int stageIndex, int generation,
        out double best, out double mean, out int competent)
    {
        // Semilla determinista de la arena por (semilla del trainer, etapa, generación).
        // Mezcla con multiplicadores de menor magnitud (evita desbordamiento de
        // compilación y mantiene la mezcla dentro del espacio de 64 bits).
        ulong arenaSeed = unchecked(_seed
            + (ulong)stageIndex * 0x9E3779B9UL
            + (ulong)generation * 0xBF58476DUL);
        var arena = new ArenaEvaluator(arenaSeed, stage.MinDistance, stage.MaxDistance, stage.TickBudget, TrialsPerGenome);

        double sum = 0.0;
        best = double.MinValue;
        competent = 0;
        for (int i = 0; i < _population.Count; i++)
        {
            var result = arena.Evaluate(_population[i]);
            _population[i].Fitness = result.Fitness;
            sum += result.Fitness;
            if (result.Fitness > best) best = result.Fitness;
            if (result.Competent) competent++;
        }
        mean = sum / _population.Count;

        var stats = new GenerationStats(stageIndex, generation, best, mean, competent);
        _report.Add(stats);
        _onGeneration?.Invoke(stats);
    }

    private void Evolve()
    {
        SortByFitness();

        int eliteKeep = Math.Max(1, (int)(_population.Count * EliteFraction));
        var next = new List<MlpGenome>(_populationSize);
        for (int i = 0; i < eliteKeep && i < _population.Count; i++)
            next.Add(_population[i].Clone());

        while (next.Count < _populationSize)
        {
            var a = Tournament();
            var b = Tournament();
            var child = MlpGenome.Crossover(a, b, ref _rng);
            child.Mutate(ref _rng, MutationSigma);
            child.Fitness = 0.0;
            next.Add(child);
        }

        _population.Clear();
        _population.AddRange(next);
    }

    private MlpGenome Tournament()
    {
        MlpGenome best = null!;
        double bestFit = double.MinValue;
        for (int i = 0; i < TournamentSize; i++)
        {
            var g = _population[_rng.NextInt(0, _population.Count)];
            if (g.Fitness > bestFit) { bestFit = g.Fitness; best = g; }
        }
        return best;
    }

    private void SortByFitness()
    {
        _population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
    }
}