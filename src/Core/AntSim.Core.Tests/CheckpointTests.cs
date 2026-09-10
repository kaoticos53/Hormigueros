using System;
using System.Collections.Generic;
using System.IO;
using AntSim.Core.Serialization;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// Fase 4 — persistencia: el checkpoint .antsave reproduce el mundo bit a bit
/// ("se guarda la causa, no los efectos") y el log .antlog contrasta eventos
/// e hitos de hash.
/// </summary>
public class CheckpointTests
{
    private static WorldSim NewRun(ulong seed, int ticks, int colonies = 2, int grid = 128)
    {
        var sim = new WorldSim(seed, grid, colonies);
        for (int i = 0; i < ticks; i++) sim.Step();
        return sim;
    }

    [Fact]
    public void SaveLoad_RoundTrip_HashIdentical()
    {
        var original = NewRun(42UL, 900);
        string expectedHash = original.HashLine();

        var restored = WorldSimSave.Deserialize(WorldSimSave.Serialize(original));

        Assert.Equal(original.Tick, restored.Tick);
        Assert.Equal(original.Seed, restored.Seed);
        Assert.Equal(original.GridCells, restored.GridCells);
        Assert.Equal(original.TargetItems, restored.TargetItems);
        Assert.Equal(original.Items.Count, restored.Items.Count);
        Assert.Equal(expectedHash, restored.HashLine());
    }

    [Fact]
    public void SaveLoad_AfterLoad_WorldEvolvesIdentically()
    {
        // La prueba de fondo: cargar en T y re-ejecutar regenera EXACTAMENTE
        // la misma simulación (eventos, nacimientos, RNG de respawn y pool).
        var a = NewRun(7UL, 600);
        var b = WorldSimSave.Deserialize(WorldSimSave.Serialize(a));

        var eventsA = new List<string>();
        var eventsB = new List<string>();
        for (int i = 0; i < 600; i++)
        {
            a.Step();
            b.Step();
            if (a.Tick % 150 == 0)
            {
                eventsA.Add(a.HashLine());
                eventsB.Add(b.HashLine());
            }
        }
        Assert.Equal(eventsA, eventsB);
    }

    [Fact]
    public void SaveLoad_ColoniesAndBroodSurviveRoundTrip()
    {
        var original = NewRun(99UL, 1200, colonies: 2);
        var restored = WorldSimSave.Deserialize(WorldSimSave.Serialize(original));

        Assert.Equal(original.Colonies.Count, restored.Colonies.Count);
        for (int c = 0; c < original.Colonies.Count; c++)
        {
            var orig = original.Colonies[c];
            var rest = restored.Colonies[c];
            Assert.Equal(orig.Id, rest.Id);
            Assert.Equal(orig.Species.Name, rest.Species.Name);
            Assert.Equal(orig.NestX, rest.NestX);
            Assert.Equal(orig.NestY, rest.NestY);
            Assert.Equal(orig.Stock, rest.Stock);
            Assert.Equal(orig.Eggs.Count, rest.Eggs.Count);
            Assert.Equal(orig.Larvae.Count, rest.Larvae.Count);
            Assert.Equal(orig.Pupae.Count, rest.Pupae.Count);
            Assert.Equal(orig.AdultCountAlive, rest.AdultCountAlive);
            Assert.Equal(orig.Pool.EliteCount, rest.Pool.EliteCount);
            Assert.Equal(orig.Pool.TrialsEntered, rest.Pool.TrialsEntered);
            Assert.Equal(orig.FoodLayer.MutationCount, rest.FoodLayer.MutationCount);

            // Genomas: mismo número de pesos y fitness por adulta.
            for (int i = 0; i < orig.Adults.Count; i++)
            {
                Assert.Equal(orig.Adults[i].Id, rest.Adults[i].Id);
                Assert.Equal(orig.Adults[i].Fitness, rest.Adults[i].Fitness);
                Assert.Equal(orig.Adults[i].HasLoad, rest.Adults[i].HasLoad);
                Assert.Equal(orig.Adults[i].Genome?.WeightCount ?? 0,
                             rest.Adults[i].Genome?.WeightCount ?? 0);
            }
        }
    }

    [Fact]
    public void CorruptedData_IsRejected()
    {
        var sim = NewRun(5UL, 300);
        byte[] data = WorldSimSave.Serialize(sim);

        // Voltear un byte del cuerpo (no del hash) debe abortar la carga.
        byte[] corrupted = (byte[])data.Clone();
        corrupted[data.Length / 2] ^= 0xFF;
        Assert.Throws<FormatException>(() => WorldSimSave.Deserialize(corrupted));
    }

    [Fact]
    public void SaveLoad_WithSeededPool_PreservesEliteAndBirths()
    {
        // Régimen realista: pool sembrado con genomas pre-entrenados (fitness
        // informativos) + cría en curso. La élite y los nacimientos posteriores
        // al checkpoint deben reproducirse idénticos.
        var original = new WorldSim(123UL, 128, 1);
        var genomes = new List<AntSim.Core.Evolution.MlpGenome>();
        for (int i = 0; i < 8; i++)
        {
            int[] sizes = { 19, 8, 6 };
            var w = new float[AntSim.Core.Brain.MlpBrain.ExpectedWeightCount(sizes)];
            for (int k = 0; k < w.Length; k++) w[k] = 0.01f * (k % 7 - 3);
            genomes.Add(new AntSim.Core.Evolution.MlpGenome(sizes, w, fitness: 1.5 + i));
        }
        original.SeedPoolFromGenomes(0, genomes);
        for (int i = 0; i < 800; i++) original.Step();

        var restored = WorldSimSave.Deserialize(WorldSimSave.Serialize(original));
        Assert.Equal(original.HashLine(), restored.HashLine());

        for (int i = 0; i < 400; i++) original.Step();
        for (int i = 0; i < 400; i++) restored.Step();
        Assert.Equal(original.HashLine(), restored.HashLine());
    }

    [Fact]
    public void AntEventLog_RoundTrip_PreservesEventsAndMilestones()
    {
        var sim = new WorldSim(77UL, 128, 1);
        using var log = new AntEventLog(sim);
        var expectedEvents = new List<AntEventLogFile.Entry>();
        var expectedMilestones = new List<AntEventLogFile.Milestone>();

        for (int i = 0; i < 2100; i++)
        {
            sim.Step();
            log.Observe(sim.LastEvents);
            foreach (var ev in sim.LastEvents)
                expectedEvents.Add(new AntEventLogFile.Entry(
                    ev.Tick, ev.Kind, ev.ColonyId, ev.AntId, ev.X, ev.Y, ev.Cause));
            if (sim.Tick > 0 && sim.Tick % AntEventLog.MilestoneEvery == 0)
            {
                string hash = sim.HashLine();
                log.RecordMilestone(sim.Tick, hash);
                expectedMilestones.Add(new AntEventLogFile.Milestone(sim.Tick, hash));
            }
        }

        var data = log.Finish();
        var read = AntEventLogFile.Deserialize(data);

        Assert.Equal(77UL, read.Seed);
        Assert.Equal(expectedEvents.Count, read.Events.Count);
        for (int i = 0; i < expectedEvents.Count; i++)
        {
            var exp = expectedEvents[i];
            var act = read.Events[i];
            Assert.Equal(exp.Tick, act.Tick);
            Assert.Equal(exp.Kind, act.Kind);
            Assert.Equal(exp.ColonyId, act.ColonyId);
            Assert.Equal(exp.AntId, act.AntId);
            Assert.Equal(exp.X, act.X);
            Assert.Equal(exp.Y, act.Y);
        }
        Assert.Equal(expectedMilestones.Count, read.Milestones.Count);
        for (int i = 0; i < expectedMilestones.Count; i++)
        {
            Assert.Equal(expectedMilestones[i].Tick, read.Milestones[i].Tick);
            Assert.Equal(expectedMilestones[i].HashHex, read.Milestones[i].HashHex);
        }
    }

    [Fact]
    public void AntEventLog_DoesNotAlterWorldHashes()
    {
        // El log es observación pura: los hashes del mundo son idénticos con y
        // sin recorder adjunto (mismo régimen que RelayTracker).
        var a = new WorldSim(555UL, 128, 1);
        var b = new WorldSim(555UL, 128, 1);
        using var log = new AntEventLog(b);

        for (int i = 0; i < 1100; i++)
        {
            a.Step();
            b.Step();
            log.Observe(b.LastEvents);
            if (b.Tick > 0 && b.Tick % AntEventLog.MilestoneEvery == 0)
                log.RecordMilestone(b.Tick, b.HashLine());
        }
        Assert.Equal(a.HashLine(), b.HashLine());
    }

    [Fact]
    public void AntEventLog_CorruptedData_IsRejected()
    {
        var sim = new WorldSim(9UL, 100);
        using var log = new AntEventLog(sim);
        for (int i = 0; i < 30; i++) { sim.Step(); log.Observe(sim.LastEvents); }
        byte[] data = log.Finish();

        byte[] corrupted = (byte[])data.Clone();
        corrupted[20] ^= 0xFF;
        Assert.Throws<FormatException>(() => AntEventLogFile.Deserialize(corrupted));
    }
}
