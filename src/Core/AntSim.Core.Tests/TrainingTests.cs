using System;
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
        var a = new ArenaEvaluator(42UL, 200f, 260f, 900, trials: 2);
        var b = new ArenaEvaluator(42UL, 200f, 260f, 900, trials: 2);

        var r1 = a.Evaluate(genome);
        var r2 = b.Evaluate(genome);

        Assert.Equal(r1.Fitness, r2.Fitness);
        Assert.Equal(r1.Pickups, r2.Pickups);
        Assert.Equal(r1.Unloads, r2.Unloads);
    }

    [Fact]
    public void Trainer_Converges_ToForaging_WithKnownSeed()
    {
        // Física verificada de la arena realista: una ida-y-vuelta SOLA a ≥ 200 u
        // es imposible dentro de la vida de una fundadora (~148 s a VMax frente a
        // ~90–110 s), de modo que la competencia observable de la generación 0 no
        // es la descarga (exige relevo + inflow) sino el FORRAJEO: pickups reales
        // del mundo + homing con carga. Este test ancla el contrato empírico
        // calibrado con la semilla 7: el entrenamiento converge a genomas que
        // alcanzan la banda de comida y cargan (arena: 24 ítems a 200–260 u).
        var stages = new List<CurriculumStage>
        {
            new() { Name = "cercana", MinDistance = 200f, MaxDistance = 260f, TickBudget = 3600, CompetenceFitness = 0.0, MinGenerations = 2, MaxGenerations = 12 }
        };

        var trainer = new CurriculumTrainer(7UL, 32, stages);
        var trained = trainer.Run();

        // La selección mejora la media de la población.
        Assert.True(trainer.Report[trainer.Report.Count - 1].MeanFitness > trainer.Report[0].MeanFitness,
            $"La media debe mejorar con el entrenamiento: gen1 {trainer.Report[0].MeanFitness:0.00} vs final {trainer.Report[trainer.Report.Count - 1].MeanFitness:0.00}.");

        // El mejor genoma forrajea de verdad: eventos Pickup del mundo (no densados).
        // Referencia empírica (calibración, semilla 7, 12 gens): best=41.5 con
        // 9 pickups; la supervivencia pura rinde 2.12 y cero pickups.
        var arena = new ArenaEvaluator(99UL, 200f, 260f, 3600);
        var result = arena.Evaluate(trained[0]);
        Assert.True(result.Pickups >= 1,
            $"El mejor genoma entrenado debe recoger comida en la arena (pickups={result.Pickups}, fitness={result.Fitness:0.00}).");
        Assert.True(result.Fitness > 31.0,
            $"El mejor fitness debe superar el techo de supervivencia+paseo (≈31): {result.Fitness:0.00}.");
    }

    [Fact]
    public void Trainer_IsDeterministic_EndToEnd()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "cercana", MinDistance = 200f, MaxDistance = 260f, TickBudget = 600, CompetenceFitness = 0.0, MinGenerations = 2, MaxGenerations = 4 }
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
    public void Trainer_WarmStart_SeedsPopulation_AndStaysDeterministic()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "cercana", MinDistance = 200f, MaxDistance = 260f, TickBudget = 600, CompetenceFitness = 0.0, MinGenerations = 2, MaxGenerations = 4 }
        };
        // Semillas de 6 genomas (fitness escalonado: orden de mérito conocido).
        var seeds = new List<MlpGenome>();
        for (int i = 0; i < 6; i++)
        {
            var g = DrawGenome(5, i);
            g.Fitness = 10.0 + i; // 10..15: el mejor al final del archivo a propósito
            seeds.Add(g);
        }

        // Misma semilla + mismas semillas ⇒ misma población inicial y mismo reporte.
        var t1 = new CurriculumTrainer(123UL, 12, stages, seedGenomes: seeds);
        var t2 = new CurriculumTrainer(123UL, 12, stages, seedGenomes: seeds);
        var p1 = t1.Run();
        var p2 = t2.Run();

        Assert.Equal(t1.Report.Count, t2.Report.Count);
        for (int i = 0; i < t1.Report.Count; i++)
        {
            Assert.Equal(t1.Report[i].BestFitness, t2.Report[i].BestFitness);
            Assert.Equal(t1.Report[i].MeanFitness, t2.Report[i].MeanFitness);
        }
        // Población completa (12) desde 6 semillas: el relleno con variantes
        // mutadas funciona y el warm-start es determinista peso a peso.
        Assert.Equal(12, p1.Count);
        Assert.Equal(p1[0].CopyWeights(), p2[0].CopyWeights());
    }

    [Fact]
    public void Trainer_WarmStart_RejectsIncompatibleTopology()
    {
        var seeds = new List<MlpGenome> { DrawGenome(5, 0) };
        // Semilla con topología inválida (capas 19→2→6 en lugar de 19→8→6).
        var bad = new MlpGenome(new[] { 19, 2, 6 }, new float[MlpBrain.ExpectedWeightCount(new[] { 19, 2, 6 })]);
        seeds.Add(bad);

        Assert.Throws<ArgumentException>(() => new CurriculumTrainer(7UL, 8, seedGenomes: seeds));
    }

    [Fact]
    public void TrainedPool_SeedsWorldSim_AndKeepsElite()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "cercana", MinDistance = 200f, MaxDistance = 260f, TickBudget = 600, CompetenceFitness = 0.0, MinGenerations = 2, MaxGenerations = 4 }
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
