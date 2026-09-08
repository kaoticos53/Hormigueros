using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;

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
    public float FoodDistance = 120f;   // u desde el nido
    public int TickBudget = 4200;       // ticks máx por evaluación (~140 s)
    public double CompetenceFitness = 2.5; // fitness mín del mejor para superar la etapa
    public int MinGenerations = 6;      // nunca avanzar antes de esto
    public int MaxGenerations = 80;     // tope duro: se avanza con lo que haya
}

/// <summary>
/// Trainer de pre-entrenamiento headless (Fase 3): entrena una población de
/// genomas sobre la arena de WorldSim a velocidad máxima, con currículo por
/// etapas y criterio de competencia mínima (ida-vuelta con comida).
///
/// - Población por generación: evaluación completa en la arena (determinista),
///   selección por élite + torneo, crossover uniforme y mutación gaussiana.
/// - Etapas: cada una sube la distancia del ítem; la etapa se supera cuando el
///   mejor fitness ≥ umbral (tras MinGenerations) o se corta en MaxGenerations.
/// - Reporte determinista por generación vía <paramref name=\"onGeneration\"/>:
///   misma semilla ⇒ misma secuencia de estadísticas byte a byte.
/// </summary>
public sealed class CurriculumTrainer
{
    public const double EliteFraction = 0.25;
    public const int TournamentSize = 3;
    public const float MutationSigma = 0.08f;

    private static readonly int[] Sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private readonly ulong _seed;
    // No readonly: DeterministicRandom es un struct y mutar una copia defensiva
    // descartaría el avance del flujo (bug corregido en Fase 3).
    private DeterministicRandom _rng;
    private readonly int _populationSize;
    private readonly IReadOnlyList<CurriculumStage> _stages;
    private readonly Action<GenerationStats>? _onGeneration;
    private readonly List<MlpGenome> _population = new();
    private readonly List<GenerationStats> _report = new();

    /// <summary>Secuencia completa de estadísticas por generación (determinista).</summary>
    public IReadOnlyList<GenerationStats> Report => _report;

    public CurriculumTrainer(ulong seed, int populationSize,
        IReadOnlyList<CurriculumStage>? stages = null, Action<GenerationStats>? onGeneration = null)
    {
        if (populationSize < 2) throw new ArgumentOutOfRangeException(nameof(populationSize), "Mínimo 2 genomas.");
        _seed = seed;
        _rng = new DeterministicRandom(seed);
        _populationSize = populationSize;
        _stages = stages ?? DefaultStages();
        _onGeneration = onGeneration;
    }

    /// <summary>
    /// Currículo por defecto, CALIBRADO empíricamente en Fase 3: cada etapa sube
    /// la distancia del ítem y transfiere la población competente de la anterior.
    /// Las distancias verificadas (el entrenador pasa las 4 etapas con semilla 7,
    /// pop 32, en ~100 s) están muy por debajo de los 120/300 u del borrador
    /// inicial: más allá de ~80 u ningún cerebro aleatorio ni evolucionado completa
    /// el ciclo en el presupuesto de ticks (la visión no alcanza y el rastro se
    /// evapora antes de volver).
    /// </summary>
    public static IReadOnlyList<CurriculumStage> DefaultStages()
    {
        return new List<CurriculumStage>
        {
            new() { Name = "corto", FoodDistance = 12f, TickBudget = 2500, CompetenceFitness = 8.5, MinGenerations = 4, MaxGenerations = 30 },
            new() { Name = "medio", FoodDistance = 25f, TickBudget = 5000, CompetenceFitness = 8.5, MinGenerations = 4, MaxGenerations = 60 },
            new() { Name = "largo", FoodDistance = 40f, TickBudget = 7000, CompetenceFitness = 8.5, MinGenerations = 4, MaxGenerations = 80 },
            new() { Name = "muy-largo", FoodDistance = 60f, TickBudget = 9000, CompetenceFitness = 8.5, MinGenerations = 4, MaxGenerations = 100 }
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

                // La etapa se supera cuando el mejor fitness alcanza el umbral
                // (tras MinGenerations); si no, se evoluciona la población y se
                // re-evalúa. Evolve() vive DENTRO del bucle: sin selección por
                // generación la población nunca mejora (bug corregido en Fase 3).
                if (best >= stage.CompetenceFitness && gen >= stage.MinGenerations)
                    break;

                Evolve();
            }
        }

        SortByFitness();
        return _population;
    }

    private void SeedPopulation()
    {
        _population.Clear();
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
        var arena = new ArenaEvaluator(arenaSeed, stage.FoodDistance, stage.TickBudget, seedTrail: true);

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