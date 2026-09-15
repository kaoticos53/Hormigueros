using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;

namespace AntSim.Core.Training;

/// <summary>
/// Currículo NEAT (F5.2c rodaja 5): el MISMO protocolo de etapas del
/// <see cref="CurriculumTrainer"/> — bandas, presupuesto, semillas de arena por
/// (etapa, generación), TrialsPerGenome — pero la evolución la lleva el
/// <see cref="NeatGenomePool"/>: nacimientos con cuotas por especie (sharing),
/// crossover align-by-innovation y mutaciones ESTRUCTURALES (add-node /
/// add-conn / toggle además de pesos y biases). La selección es truncada por
/// fitness de ARENA (la misma disciplina v1: los top 25 % pasan, el resto se
/// reproduce por torneo del pool).
///
/// El warm-start acepta v1 O v2: el lector v2 convierte v1 a grafo denso con
/// la paridad de rodaja 1, así que un pool MLP entrenado entra al mundo NEAT
/// con su comportamiento EXACTO intacto.
/// </summary>
public sealed class NeatCurriculumTrainer
{
    public const double EliteFraction = CurriculumTrainer.EliteFraction;
    public const int TournamentSize = CurriculumTrainer.TournamentSize;
    public const int TrialsPerGenome = CurriculumTrainer.TrialsPerGenome;

    /// <summary>Las mutaciones estructurales activas son LA hipótesis de la
    /// rodaja: probabilidades del diseño §3.4 (más altas que los defaults del
    /// operador para que 50–200 generaciones muestren estructura).</summary>
    public const float ProbAddConn = 0.05f;
    public const float ProbAddNode = 0.03f;
    public const float ProbToggle = 0.02f;

    private readonly ulong _seed;
    private DeterministicRandom _rng; // no readonly: struct por ref (disciplina Fase 3)
    private readonly int _populationSize;
    private readonly IReadOnlyList<CurriculumStage> _stages;
    private readonly Action<GenerationStats>? _onGeneration;
    private readonly List<NeatGenome> _population = new();
    private readonly List<GenerationStats> _report = new();
    private readonly NeatGenomePool _pool;

    public NeatCurriculumTrainer(ulong seed, int populationSize,
        IReadOnlyList<CurriculumStage>? stages = null, Action<GenerationStats>? onGeneration = null,
        IReadOnlyList<NeatGenome>? seedGenomes = null)
    {
        if (populationSize < 2) throw new ArgumentOutOfRangeException(nameof(populationSize), "Mínimo 2 genomas.");
        _seed = seed;
        _rng = new DeterministicRandom(seed ^ 0x4E454154UL); // "NEAT": flujo propio, distinto del v1
        _populationSize = populationSize;
        _stages = stages ?? CurriculumTrainer.DefaultStages();
        _onGeneration = onGeneration;
        _pool = new NeatGenomePool(new DeterministicRandom(seed ^ 0x504F4F4CUL), // "POOL"
            probAddConn: ProbAddConn, probAddNode: ProbAddNode, probToggle: ProbToggle);
        _seedGenomes = seedGenomes;
    }

    private readonly IReadOnlyList<NeatGenome>? _seedGenomes;

    public IReadOnlyList<GenerationStats> Report => _report;

    /// <summary>Pool de evolución vivo (para inspección post-Run).</summary>
    public NeatGenomePool Pool => _pool;

    /// <summary>Ejecuta el currículo completo; devuelve la población final
    /// ordenada por fitness descendente.</summary>
    public IReadOnlyList<NeatGenome> Run()
    {
        SeedPopulation();

        for (int s = 0; s < _stages.Count; s++)
        {
            var stage = _stages[s];
            int gen = 0;
            while (gen < stage.MaxGenerations)
            {
                gen++;
                Evaluate(stage, s + 1, gen, out double best);

                if (stage.CompetenceFitness > 0.0 && best >= stage.CompetenceFitness && gen >= stage.MinGenerations)
                    break;

                if (gen < stage.MaxGenerations)
                    Evolve();
            }
        }

        _population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
        return _population;
    }

    private void SeedPopulation()
    {
        _population.Clear();
        _pool.ReplaceElite(Array.Empty<NeatGenome>());

        if (_seedGenomes is { Count: > 0 })
        {
            // Warm-start: la élite del pool ES la población sembrada (clones con
            // fitness del archivo como semilla del sharing), el resto variantes
            // de nacimiento del pool (mutación estructural activa desde la gen 1).
            var sorted = new List<NeatGenome>(_seedGenomes);
            sorted.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
            _pool.Seed(sorted);
            _population.AddRange(_pool.Elite);
            while (_population.Count < _populationSize)
            {
                var child = _pool.Birth()!;
                _population.Add(child);
            }
            return;
        }

        // Arranque en frío: grafos densos aleatorios (topología 19-8-6).
        int[] sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };
        for (int i = 0; i < _populationSize; i++)
        {
            int n = MlpBrain.ExpectedWeightCount(sizes);
            var w = new float[n];
            for (int k = 0; k < n; k++) w[k] = (float)(_rng.NextDouble01() * 2.0 - 1.0);
            var g = NeatGenome.FromMlp(sizes, w);
            _population.Add(g);
            _pool.TryAdd(g.Clone());
        }
    }

    private void Evaluate(CurriculumStage stage, int stageIndex, int generation, out double best)
    {
        ulong arenaSeed = unchecked(_seed
            + (ulong)stageIndex * 0x9E3779B9UL
            + (ulong)generation * 0xBF58476DUL);

        float min = stage.MinDistance, max = stage.MaxDistance;
        if (stage.MidDistance is float mid && generation % 2 == 1)
            max = mid; // currículo híbrido: impar = banda media

        var arena = new ArenaEvaluator(arenaSeed, min, max, stage.TickBudget, TrialsPerGenome,
            stage.ArenaCells);

        double sum = 0.0;
        best = double.MinValue;
        int competent = 0;
        for (int i = 0; i < _population.Count; i++)
        {
            var result = arena.EvaluateNeat(_population[i]);
            _population[i].Fitness = result.Fitness;
            sum += result.Fitness;
            if (result.Fitness > best) best = result.Fitness;
            if (result.Competent) competent++;
        }

        var stats = new GenerationStats(stageIndex, generation, best, sum / _population.Count, competent);
        _report.Add(stats);
        _onGeneration?.Invoke(stats);
    }

    /// <summary>
    /// Reproducción: truncamiento por fitness de arena (los MEJORES vuelven al
    /// pool como élite — RecordFitness —, el resto nace del pool con cuotas).
    /// La élite del pool acumula mérito entre generaciones: un linaje que
    /// encuentra estructura útil sigue criando aunque una generación concreta
    /// le dé una banda desfavorable (el papel exacto del sharing).
    /// </summary>
    private void Evolve()
    {
        _population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
        int eliteKeep = Math.Max(1, (int)(_population.Count * EliteFraction));
        for (int i = 0; i < eliteKeep && i < _population.Count; i++)
            _pool.RecordFitness(_population[i].Clone(), _population[i].Fitness);

        var next = new List<NeatGenome>(_populationSize);
        for (int i = 0; i < eliteKeep && i < _population.Count; i++)
            next.Add(_population[i].Clone());

        while (next.Count < _populationSize)
            next.Add(_pool.Birth()!);

        _population.Clear();
        _population.AddRange(next);
    }
}
