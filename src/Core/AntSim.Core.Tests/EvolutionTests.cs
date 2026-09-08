using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

public class EvolutionTests
{
    private static readonly int[] Sizes = { 19, 8, 6 };

    private static DeterministicRandom NewRng(ulong seed) => new(seed);

    [Fact]
    public void MlpGenome_Clone_IsIndependent()
    {
        var g = MlpGenome.Random(NewRng(1), Sizes);
        g.Fitness = 5.0;
        var clone = g.Clone();
        clone.Mutate(NewRng(2));

        Assert.NotEqual(clone.CopyWeights(), g.CopyWeights());
        Assert.Equal(5.0, g.Fitness);
    }

    [Fact]
    public void Crossover_IsDeterministic_AndMixesParents()
    {
        var a = MlpGenome.Random(NewRng(1), Sizes);
        var b = MlpGenome.Random(NewRng(2), Sizes);
        var c1 = MlpGenome.Crossover(a, b, NewRng(42));
        var c2 = MlpGenome.Crossover(a, b, NewRng(42));

        Assert.Equal(c1.CopyWeights(), c2.CopyWeights());
        // El hijo mezcla pesos de ambos padres (no es idéntico a ninguno en general).
        Assert.NotEqual(c1.CopyWeights(), a.CopyWeights());
        Assert.NotEqual(c1.CopyWeights(), b.CopyWeights());
    }

    [Fact]
    public void Mutate_IsDeterministic_AndChangesWeights()
    {
        var g1 = MlpGenome.Random(NewRng(7), Sizes);
        var g2 = g1.Clone();
        float[] orig = g1.CopyWeights(); // antes de mutar
        g1.Mutate(NewRng(9), 0.05f);
        g2.Mutate(NewRng(9), 0.05f);
        Assert.Equal(g1.CopyWeights(), g2.CopyWeights());

        int changed = 0;
        for (int i = 0; i < orig.Length; i++)
            if (orig[i] != g1.CopyWeights()[i]) changed++;
        Assert.True(changed > 0);
    }

    [Fact]
    public void Distance_SameGenome_IsZero_AndDifferentIsPositive()
    {
        var a = MlpGenome.Random(NewRng(3), Sizes);
        Assert.Equal(0.0, a.DistanceTo(a.Clone()));
        Assert.True(a.DistanceTo(MlpGenome.Random(NewRng(4), Sizes)) > 0.0);
    }

    [Fact]
    public void Pool_TryAdd_KeepsSortedByFitness_AndCaps()
    {
        var pool = new GenomePool(NewRng(1), Sizes, seedCount: 4);

        // Llenamos hasta la capacidad (64) con fitness bajos.
        int toFill = GenomePool.EliteCapacity - pool.EliteCount;
        for (int i = 0; i < toFill; i++)
        {
            var filler = MlpGenome.Random(NewRng((ulong)(1000 + i)), Sizes);
            filler.Fitness = 0.0;
            pool.TryAdd(filler);
        }
        Assert.Equal(GenomePool.EliteCapacity, pool.EliteCount);

        var good = MlpGenome.Random(NewRng(2), Sizes);
        good.Fitness = 100.0;
        var bad = MlpGenome.Random(NewRng(3), Sizes);
        bad.Fitness = -100.0;

        Assert.False(pool.TryAdd(bad)); // peor que el peor ⇒ no entra
        Assert.True(pool.TryAdd(good));
        Assert.Equal(100.0, pool.BestFitness);
        Assert.Equal(GenomePool.EliteCapacity, pool.EliteCount);

        // Orden descendente.
        for (int i = 1; i < pool.EliteCount; i++)
            Assert.True(pool.Elite[i - 1].Fitness >= pool.Elite[i].Fitness);
    }

    [Fact]
    public void Pool_Birth_IsDeterministic_WithSameRng()
    {
        var p1 = new GenomePool(NewRng(5), Sizes, seedCount: 8);
        var p2 = new GenomePool(NewRng(5), Sizes, seedCount: 8);

        // Los nacimientos consumen el RNG en el mismo orden ⇒ misma secuencia.
        var b1a = p1.Birth(); var b1b = p1.Birth();
        var b2a = p2.Birth(); var b2b = p2.Birth();

        Assert.Equal(b1a.CopyWeights(), b2a.CopyWeights());
        Assert.Equal(b1b.CopyWeights(), b2b.CopyWeights());
        Assert.Equal(0.0, b1a.Fitness);
    }

    [Fact]
    public void Immigrant_UsedBeforeNative_AndExpires_AfterWindow()
    {
        var pool = new GenomePool(NewRng(1), Sizes, seedCount: 4);
        var immigrant = MlpGenome.Random(NewRng(2), Sizes);
        pool.QueueImmigrant(immigrant, nowTick: 0);

        // Dentro de la ventana: el inmigrante se usa antes que un nacimiento.
        Assert.True(pool.TryNextImmigrant(nowTick: 10, out var used));
        Assert.Same(immigrant, used);
        Assert.False(pool.TryNextImmigrant(nowTick: 11, out _)); // cola vacía

        // Un inmigrante encolado demasiado tarde vence sin usarse.
        pool.QueueImmigrant(MlpGenome.Random(NewRng(3), Sizes), nowTick: 0);
        Assert.False(pool.TryNextImmigrant(nowTick: GenomePool.ImmigrantWindowTicks + 1, out _));
        Assert.Equal(1, pool.TrialsExpired);
    }

    [Fact]
    public void Immigrant_BelowP50_IsDiscarded_AndAbove_EntersElite()
    {
        // Élite completa con fitness alto para que p50 sea exigente.
        var pool = new GenomePool(NewRng(1), Sizes, seedCount: 16);
        for (int i = 0; i < GenomePool.EliteCapacity; i++)
        {
            var g = MlpGenome.Random(NewRng((ulong)(100 + i)), Sizes);
            g.Fitness = 50.0 + i; // 50..113
            pool.TryAdd(g);
        }
        // Los 16 seeds (fitness 0) se caen: la élite queda con fitness ≥ 50.
        double p50 = pool.Elite[pool.EliteCount / 2].Fitness;
        Assert.True(p50 > 50.0);

        var weak = MlpGenome.Random(NewRng(7), Sizes);
        Assert.Equal(TrialResult.Discarded, pool.CompleteTrial(weak, fitness: 1.0, nowTick: 0));
        Assert.DoesNotContain(weak, pool.Elite);
        Assert.Equal(1, pool.TrialsDiscarded);

        var strong = MlpGenome.Random(NewRng(8), Sizes);
        Assert.Equal(TrialResult.EnteredElite, pool.CompleteTrial(strong, fitness: 100.0, nowTick: 0));
        Assert.Contains(strong, pool.Elite);
        Assert.Equal(1, pool.TrialsEntered);
    }

    [Fact]
    public void AntGenomeFile_RoundTrip_IsBitExact_AndPreservesMetadata()
    {
        var genomes = new List<MlpGenome>
        {
            MlpGenome.Random(NewRng(1), Sizes),
            MlpGenome.Random(NewRng(2), Sizes)
        };
        genomes[0].Fitness = 7.5;
        genomes[1].Fitness = 3.25;

        byte[] data = AntGenomeFile.Serialize("elite-lasius", "Lasius niger", 42UL, 120, genomes,
            BrainContract.CurrentVersion);
        var (meta, loaded) = AntGenomeFile.Deserialize(data, BrainContract.CurrentVersion);

        Assert.Equal("elite-lasius", meta.Name);
        Assert.Equal("Lasius niger", meta.SpeciesHint);
        Assert.Equal(42UL, meta.OriginSeed);
        Assert.Equal(120, meta.Generation);
        Assert.Equal(2, meta.GenomeCount);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(genomes[0].CopyWeights(), loaded[0].CopyWeights());
        Assert.Equal(genomes[1].CopyWeights(), loaded[1].CopyWeights());
        Assert.Equal(7.5, loaded[0].Fitness);

        // Serialización determinista: misma entrada ⇒ mismos bytes.
        byte[] again = AntGenomeFile.Serialize("elite-lasius", "Lasius niger", 42UL, 120, genomes,
            BrainContract.CurrentVersion);
        Assert.Equal(data, again);
    }

    [Fact]
    public void AntGenomeFile_RejectsCorruption_AndContractMismatch()
    {
        var genomes = new List<MlpGenome> { MlpGenome.Random(NewRng(1), Sizes) };
        byte[] data = AntGenomeFile.Serialize("n", "s", 0UL, 0, genomes, BrainContract.CurrentVersion);

        var corrupted = (byte[])data.Clone();
        corrupted[corrupted.Length - 33] ^= 0xFF; // dentro del cuerpo, no del hash
        Assert.Throws<FormatException>(() => AntGenomeFile.Deserialize(corrupted, BrainContract.CurrentVersion));

        Assert.Throws<FormatException>(() => AntGenomeFile.Deserialize(data, BrainContract.CurrentVersion + 1));
    }

    [Fact]
    public void WorldSim_WithEvolution_IsDeterministic_AndPoolsFeedOnDeath()
    {
        var a = new WorldSim(55UL, 128, 1);
        var b = new WorldSim(55UL, 128, 1);
        for (int i = 0; i < 600; i++)
        {
            a.Step();
            b.Step();
        }

        Assert.Equal(a.HashLine(), b.HashLine());

        // La colonia tiene pool élite y se registran muertes (fitness alimentado).
        var pool = a.Colonies[0].Pool;
        Assert.True(pool.EliteCount > 0);
        Assert.True(pool.BestFitness >= 0.0);
    }

    [Fact]
    public void Import_QueuesImmigrants_AndExport_MatchesElite()
    {
        var sim = new WorldSim(8UL, 128, 1);
        var genomes = new List<MlpGenome> { MlpGenome.Random(NewRng(9), Sizes), MlpGenome.Random(NewRng(10), Sizes) };
        sim.ImportGenomes(0, genomes);

        Assert.Equal(2, sim.Colonies[0].Pool.PendingImmigrants);
        Assert.Equal(sim.Colonies[0].Pool.EliteCount, sim.ExportElite(0).Count); // 16 élite por defecto
    }
}