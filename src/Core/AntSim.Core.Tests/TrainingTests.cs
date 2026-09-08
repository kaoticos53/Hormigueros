using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.Training;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

public class TrainingTests
{
    private static readonly int[] Sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private static MlpGenome DrawGenome(ulong seed, int index)
    {
        var rng = new DeterministicRandom(seed);
        MlpGenome g = null!;
        for (int i = 0; i <= index; i++)
            g = MlpGenome.Random(ref rng, Sizes);
        return g;
    }

    [Fact]
    public void Arena_SameGenomeAndSeed_IsDeterministic()
    {
        var genome = DrawGenome(7, 0);
        var a = new ArenaEvaluator(42UL, 12f, 2500, seedTrail: true, trials: 2);
        var b = new ArenaEvaluator(42UL, 12f, 2500, seedTrail: true, trials: 2);

        var r1 = a.Evaluate(genome);
        var r2 = b.Evaluate(genome);

        Assert.Equal(r1.Fitness, r2.Fitness);
        Assert.Equal(r1.Competent, r2.Competent);
    }

    [Fact]
    public void Arena_FindsACompetentRoundTripGenome()
    {
        // Búsqueda determinista: en la población de la semilla 7 hay genomas que
        // completan el ciclo ida-vuelta a 12 u (verificado empíricamente, 22/64).
        var rng = new DeterministicRandom(7);
        var arena = new ArenaEvaluator(99UL, 12f, 1500, seedTrail: true);
        bool found = false;
        for (int i = 0; i < 64 && !found; i++)
        {
            var g = MlpGenome.Random(ref rng, Sizes);
            if (arena.Evaluate(g).Competent) found = true;
        }
        Assert.True(found, "Debe existir al menos un genoma competente en la población semilla 7.");
    }

    [Fact]
    public void Trainer_Converges_ToCompetence_WithKnownSeed()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "corto", FoodDistance = 12f, TickBudget = 2500, CompetenceFitness = 8.5, MinGenerations = 4, MaxGenerations = 12 }
        };

        var trainer = new CurriculumTrainer(7UL, 32, stages);
        var trained = trainer.Run();

        Assert.True(trained[0].Fitness >= 8.5,
            $"El mejor fitness tras entrenar debe superar el umbral de competencia; era {trained[0].Fitness:0.00}.");
        int competentGens = 0;
        foreach (var s in trainer.Report)
            if (s.CompetentCount > 0) competentGens++;
        Assert.True(competentGens > 0, "Debe haber generaciones con genomas competentes.");
    }

    [Fact]
    public void Trainer_IsDeterministic_EndToEnd()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "corto", FoodDistance = 12f, TickBudget = 1500, CompetenceFitness = 8.5, MinGenerations = 2, MaxGenerations = 8 }
        };

        var t1 = new CurriculumTrainer(123UL, 8, stages);
        var t2 = new CurriculumTrainer(123UL, 8, stages);
        var p1 = t1.Run();
        var p2 = t2.Run();

        Assert.Equal(t1.Report.Count, t2.Report.Count);
        for (int i = 0; i < t1.Report.Count; i++)
        {
            var a = t1.Report[i];
            var b = t2.Report[i];
            Assert.Equal(a.Stage, b.Stage);
            Assert.Equal(a.Generation, b.Generation);
            Assert.Equal(a.BestFitness, b.BestFitness);
            Assert.Equal(a.MeanFitness, b.MeanFitness);
            Assert.Equal(a.CompetentCount, b.CompetentCount);
        }
        Assert.Equal(p1[0].CopyWeights(), p2[0].CopyWeights());
    }

    [Fact]
    public void TrainedPool_SeedsWorldSim_AndKeepsElite()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "corto", FoodDistance = 12f, TickBudget = 1500, CompetenceFitness = 8.5, MinGenerations = 2, MaxGenerations = 8 }
        };
        var trainer = new CurriculumTrainer(7UL, 16, stages);
        var trained = trainer.Run();

        var sim = new WorldSim(31UL, 128, 1);
        sim.SeedPoolFromGenomes(0, trained);

        var pool = sim.Colonies[0].Pool;
        Assert.Equal(trained.Count, pool.EliteCount);
        Assert.Equal(trained[0].Fitness, pool.BestFitness);

        for (int i = 0; i < 120; i++)
            sim.Step();
        Assert.True(pool.EliteCount > 0, "El pool sembrado debe seguir vivo tras simular.");
        Assert.Equal(trained[0].Fitness, pool.BestFitness);
    }
}