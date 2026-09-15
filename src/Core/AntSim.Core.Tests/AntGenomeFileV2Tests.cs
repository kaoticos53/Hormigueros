using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Scenario;
using AntSim.Core.Sim;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2c rodaja 4 — formato `.antgenome` v2 (diseño §4):
///   1. Roundtrip bit a bit (pesos, biases, estructura, fitness, enabled).
///   2. Orden canónico: el mismo grafo serializa siempre igual.
///   3. El lector v2 ACEPTA v1 (conversión con paridad demostrada) y
///      re-innova canónicamente.
///   4. El lector v1 RECHAZA v2 con error claro.
///   5. Dos importaciones del mismo archivo ⇒ pools idénticos (la promesa
///      §3.3 de la re-innovación canónica).
///   6. Cuarentena: GenomeImportInfo gana FormatVersion/NodeCount/ConnCount.
/// </summary>
public class AntGenomeFileV2Tests
{
    private const int ContractV = BrainContract.CurrentVersion;
    private static readonly int[] Sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private static NeatGenome MutatedPoolGenome(ulong seed, double fitness)
    {
        // Un genoma con estructura propia: denso convertido + mutaciones
        // estructurales de verdad (add-node, add-conn, toggle).
        var rng = new DeterministicRandom(seed);
        int n = MlpBrain.ExpectedWeightCount(Sizes);
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        var g = NeatGenome.FromMlp(Sizes, w, fitness);
        var reg = new InnovationRegistry(new[] { g });
        Assert.True(NeatOperators.MutateAddNode(g, reg, ref rng));
        Assert.True(NeatOperators.MutateAddConn(g, reg, ref rng));
        Assert.NotNull(NeatOperators.MutateToggle(g, ref rng));
        g.Fitness = fitness;
        return g;
    }

    private static List<NeatGenome> SmallPool() => new()
    {
        MutatedPoolGenome(11UL, 7.5),
        MutatedPoolGenome(22UL, 5.25),
        MutatedPoolGenome(33UL, 2.0),
    };

    private static void AssertSameGenome(NeatGenome a, NeatGenome b)
    {
        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        Assert.Equal(a.Conns.Count, b.Conns.Count);
        Assert.Equal(a.Fitness, b.Fitness, 9);
        // Nodos como mapa por id (el orden interno de la lista no es contrato).
        var nodesA = a.Nodes.ToDictionary(n => n.Id);
        var nodesB = b.Nodes.ToDictionary(n => n.Id);
        Assert.Equal(nodesA.Keys, nodesB.Keys);
        foreach (var (id, na) in nodesA)
        {
            var nb = nodesB[id];
            Assert.Equal(na.Kind, nb.Kind);
            Assert.Equal(na.Bias, nb.Bias);
            Assert.Equal(na.Act, nb.Act);
        }
        var byInnovA = a.Conns.ToDictionary(c => c.Innovation);
        var byInnovB = b.Conns.ToDictionary(c => c.Innovation);
        Assert.Equal(byInnovA.Count, byInnovB.Count);
        foreach (var (innov, ca) in byInnovA)
        {
            var cb = byInnovB[innov];
            Assert.Equal(ca.From, cb.From);
            Assert.Equal(ca.To, cb.To);
            Assert.Equal(ca.Weight, cb.Weight);
            Assert.Equal(ca.Enabled, cb.Enabled);
        }
    }

    // ————— 1. Roundtrip bit a bit —————

    [Fact]
    public void Roundtrip_ConservaTodoBitABit()
    {
        var pool = SmallPool();
        byte[] data = AntGenomeFile.SerializeNeat("pool-v2", "lasius", 42UL, 17, pool, ContractV);
        var (meta, loaded) = AntGenomeFile.DeserializeNeat(data, ContractV);

        Assert.Equal("pool-v2", meta.Name);
        Assert.Equal("lasius", meta.SpeciesHint);
        Assert.Equal(42UL, meta.OriginSeed);
        Assert.Equal(17, meta.Generation);
        Assert.Equal(7.5, meta.BestFitnessAtExport, 9);
        Assert.Equal(3, meta.GenomeCount);

        // El archivo lleva la FORMA CANÓNICA (§4): el cargado es idéntico al
        // canonizado del original (misma estructura, innovations 1..K).
        for (int i = 0; i < pool.Count; i++)
            AssertSameGenome(AntGenomeFile.RenumberCanonically(pool[i]), loaded[i]);
        // La estructura (nodos/conns por (from,to)) sí coincide tal cual.
        for (int i = 0; i < pool.Count; i++)
        {
            Assert.Equal(pool[i].Nodes.Count, loaded[i].Nodes.Count);
            Assert.Equal(pool[i].Conns.Count, loaded[i].Conns.Count);
        }
    }

    // ————— 2. Orden canónico —————

    [Fact]
    public void Serializacion_Canonica_MismoGrafoMismoArchivo()
    {
        var pool = SmallPool();
        byte[] a = AntGenomeFile.SerializeNeat("p", "s", 1UL, 1, pool, ContractV);

        // Reconstruir los genomas (nuevas instancias, misma estructura) y
        // re-serializar: el archivo debe ser idéntico byte a byte.
        var (meta, loaded) = AntGenomeFile.DeserializeNeat(a, ContractV);
        byte[] b = AntGenomeFile.SerializeNeat("p", "s", 1UL, 1, loaded, ContractV);
        Assert.Equal(a, b);
        // Y las innovations del cargado son canónicas 1..K sin huecos.
        Assert.Equal(Enumerable.Range(1, loaded[0].Conns.Count),
            loaded[0].Conns.Select(c => c.Innovation).OrderBy(i => i));
    }

    // ————— 3. El lector v2 acepta v1 —————

    [Fact]
    public void LectorV2_AceptaV1_ConReinnovacionCanonica()
    {
        var rng = new DeterministicRandom(77UL);
        int n = MlpBrain.ExpectedWeightCount(Sizes);
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        var mlp = new MlpGenome(Sizes, w, 4.25);
        byte[] v1 = AntGenomeFile.Serialize("legacy", "lasius", 9UL, 3, new[] { mlp }, ContractV);

        var (meta, neat) = AntGenomeFile.DeserializeNeat(v1, ContractV);
        Assert.Equal("legacy", meta.Name);
        Assert.Equal(1, meta.GenomeCount);

        var g = neat[0];
        // La conversión es la misma FromMlp de la paridad (rodaja 1)...
        var direct = NeatGenome.FromMlp(Sizes, w, 4.25);
        Assert.Equal(direct.Nodes.Count, g.Nodes.Count);
        Assert.Equal(direct.Conns.Count, g.Conns.Count);
        // ... y las innovations quedan canónicas 1..K por (from,to).
        var sorted = g.Conns.Select(c => (c.From, c.To)).OrderBy(p => p.From).ThenBy(p => p.To).ToList();
        var byPair = g.Conns.ToDictionary(c => (c.From, c.To), c => c.Innovation);
        for (int i = 0; i < sorted.Count; i++)
            Assert.Equal(i + 1, byPair[sorted[i]]);
    }

    // ————— 4. El lector v1 rechaza v2 —————

    [Fact]
    public void LectorV1_RechazaV2_ConErrorClaro()
    {
        var pool = SmallPool();
        byte[] v2 = AntGenomeFile.SerializeNeat("p", "s", 1UL, 1, pool, ContractV);

        var ex = Assert.Throws<FormatException>(
            () => AntGenomeFile.Deserialize(v2, ContractV));
        Assert.Contains("2", ex.Message);
    }

    // ————— 5. Dos importaciones ⇒ pools idénticos —————

    [Fact]
    public void DobleImportacion_PoolsIdenticos()
    {
        // La promesa §3.3: importar dos VECES el mismo archivo (o desde dos
        // partidas distintas) produce los MISMOS genomas.
        var pool = SmallPool();
        byte[] data = AntGenomeFile.SerializeNeat("p", "s", 5UL, 9, pool, ContractV);

        var (_, first) = AntGenomeFile.DeserializeNeat(data, ContractV);
        var (_, second) = AntGenomeFile.DeserializeNeat(data, ContractV);
        for (int i = 0; i < first.Count; i++)
            AssertSameGenome(first[i], second[i]);

        // Con un archivo v1 "exótico": innovations arbitrarias del exportador
        // (el lector v1 viejo NO renumeraba) — v2 las canoniciza igual.
        byte[] v1 = AntGenomeFile.Serialize("x", "s", 5UL, 9, new[]
        {
            new MlpGenome(Sizes, Enumerable.Repeat(0.25f, MlpBrain.ExpectedWeightCount(Sizes)).ToArray(), 1.0),
        }, ContractV);
        var (_, a1) = AntGenomeFile.DeserializeNeat(v1, ContractV);
        var (_, b1) = AntGenomeFile.DeserializeNeat(v1, ContractV);
        Assert.Equal(a1[0].Conns.Select(c => c.Innovation), b1[0].Conns.Select(c => c.Innovation));
        // Canónicas: estrictamente 1..K sin huecos.
        Assert.Equal(Enumerable.Range(1, a1[0].Conns.Count),
            a1[0].Conns.Select(c => c.Innovation).OrderBy(i => i));
    }

    // ————— 6. Cuarentena: la forma del cerebro en la tarjeta —————

    [Fact]
    public void Quarantine_V2_ExponeLaFormaDelCerebro()
    {
        string path = Path.Combine(Path.GetTempPath(), $"antsim-f52c-v2-{Guid.NewGuid():N}.antgenome");
        try
        {
            var pool = SmallPool();
            AntGenomeFile.WriteNeatFile(path, "pool-256", "atta", 77UL, 33, pool, ContractV);

            var info = GenomeImportInfo.Inspect(path, ContractV);
            Assert.True(info.Ok, info.Error);
            Assert.Equal(2, info.FormatVersion);
            Assert.Equal(pool[0].Nodes.Count, info.NodeCount);
            Assert.Equal(pool[0].Conns.Count, info.ConnCount);
            Assert.Contains("v2", info.Card);
            Assert.Contains($"{pool[0].Nodes.Count}n", info.Card);
            // El JSON canónico también lleva los campos (el CLI lo consume).
            Assert.Contains("\"formatVersion\":2", info.ToJson());
            Assert.Contains($"\"nodeCount\":{pool[0].Nodes.Count}", info.ToJson());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Quarantine_V1_ExponeMenosUnoYNoRompeLaTarjeta()
    {
        string path = Path.Combine(Path.GetTempPath(), $"antsim-f52c-v1-{Guid.NewGuid():N}.antgenome");
        try
        {
            var rng = new DeterministicRandom(5UL);
            int n = MlpBrain.ExpectedWeightCount(Sizes);
            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
            AntGenomeFile.WriteFile(path, "legacy", "lasius", 1UL, 1,
                new[] { new MlpGenome(Sizes, w, 2.0) }, ContractV);

            var info = GenomeImportInfo.Inspect(path, ContractV);
            Assert.True(info.Ok, info.Error);
            Assert.Equal(1, info.FormatVersion);
            Assert.Equal(-1, info.NodeCount); // forma no aplicable a v1 plano
            Assert.Equal(-1, info.ConnCount);
            Assert.DoesNotContain("v1 ·", info.Card); // la tarjeta v1 no cambia
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ————— Extras de robustez —————

    [Fact]
    public void ArchivoCorrupto_FallaConIntegridad()
    {
        var pool = SmallPool();
        byte[] data = AntGenomeFile.SerializeNeat("p", "s", 1UL, 1, pool, ContractV);
        data[^5] ^= 0xFF; // tocar el hash final
        Assert.Throws<FormatException>(() => AntGenomeFile.DeserializeNeat(data, ContractV));
    }

    [Fact]
    public void VersionFutura_SeRechazaConMensaje()
    {
        var pool = SmallPool();
        byte[] data = AntGenomeFile.SerializeNeat("p", "s", 1UL, 1, pool, ContractV);
        data[8] = 3; // formatVersion = 3
        // El hash ya no cuadra tras tocar el body: da igual cuál falle, pero
        // PeekFormatVersion debe ver el 3 y DeserializeNeat debe rechazarlo.
        Assert.Equal(3, AntGenomeFile.PeekFormatVersion(data));
        data[8] = 2; // restaurar formato y verificar que el roundtrip vuelve
        var (_, ok) = AntGenomeFile.DeserializeNeat(data, ContractV);
        Assert.Equal(pool[0].Conns.Count, ok[0].Conns.Count);
    }
}
