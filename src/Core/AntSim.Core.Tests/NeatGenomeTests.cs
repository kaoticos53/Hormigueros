using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2c rodaja 1 — genomas estructurales y el cerebro de grafo:
///   1. PARIDAD BIT A BIT: la conversión v1→v2 (<see cref="NeatGenome.FromMlp"/>)
///      produce un <see cref="NeatBrain"/> cuya evaluación reproduce EXACTAMENTE
///      el <see cref="MlpBrain"/> original (mismas decisiones float — igualdad
///      por bits, como exige el contrato de determinismo del repo) en sensores
///      aleatorios, en cada topología del repo y en genomas reales del pool
///      warm-v2 del fixture.
///   2. Invariantes del genoma (ids, aciclicidad, duplicados, topes).
///   3. Determinismo de construcción (misma entrada ⇒ mismo orden de evaluación).
/// </summary>
public class NeatGenomeTests
{
    private static AntSensors RandomSensors(AntSim.Core.Sim.DeterministicRandom rng)
    {
        var s = AntSensors.Default();
        s.FoodTrailCenter = (float)(rng.NextDouble01() * 2.0 - 1.0);
        s.HomeTrailCenter = (float)(rng.NextDouble01() * 2.0 - 1.0);
        s.Energy = (float)rng.NextDouble01();
        s.HasLoad = rng.NextBool() ? 1f : 0f;
        return s;
    }

    private static void AssertSameDecision(in AntDecision a, in AntDecision b)
    {
        // Igualdad por BITS: la paridad prometida es bit a bit, no aproximada.
        Assert.Equal(BitConverter.SingleToInt32Bits(a.Steer), BitConverter.SingleToInt32Bits(b.Steer));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.Speed), BitConverter.SingleToInt32Bits(b.Speed));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.DepositFood), BitConverter.SingleToInt32Bits(b.DepositFood));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.DepositHome), BitConverter.SingleToInt32Bits(b.DepositHome));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.DepositAlarm), BitConverter.SingleToInt32Bits(b.DepositAlarm));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.Interact), BitConverter.SingleToInt32Bits(b.Interact));
    }

    [Fact]
    public void FromMlp_ParidadBitExacta_Mlp19_8_6()
    {
        int[] sizes = { 19, 8, 6 };
        var weights = BuildWeights(sizes, 42);

        var mlp = new MlpBrain(sizes, weights);
        var genome = NeatGenome.FromMlp(sizes, weights);
        var neat = genome.ToBrain();

        Assert.Equal(BrainKind.Neat, genome.Kind);
        Assert.Equal(BrainContract.CurrentVersion, genome.ContractVersion);

        var rng = new AntSim.Core.Sim.DeterministicRandom(777UL);
        for (int trial = 0; trial < 200; trial++)
        {
            var s = RandomSensors(rng);
            var dMlp = AntDecision.Neutral();
            var dNeat = AntDecision.Neutral();
            mlp.Evaluate(in s, ref dMlp);
            neat.Evaluate(in s, ref dNeat);
            AssertSameDecision(in dMlp, in dNeat);

            // El canal F también es idéntico, elemento a elemento (float exacto).
            var aMlp = new float[mlp.ActivationTotal];
            var aNeat = new float[neat.ActivationTotal];
            mlp.SnapshotActivations(aMlp);
            neat.SnapshotActivations(aNeat);
            Assert.Equal(mlp.ActivationTotal, neat.ActivationTotal);
            for (int i = 0; i < aMlp.Length; i++)
                Assert.Equal(BitConverter.SingleToInt32Bits(aMlp[i]), BitConverter.SingleToInt32Bits(aNeat[i]));
        }
    }

    [Fact]
    public void FromMlp_ParidadEnCadaTopologiaDelRepo()
    {
        int[][] topologies =
        {
            new[] { 19, 6 },                       // sin capa oculta
            new[] { 19, 8, 6 },                    // la del mundo/trainer
            new[] { 19, 12, 10, 6 },               // profunda
            new[] { 19, 4, 4, 4, 6 },              // varias estrechas
        };

        int seed = 7;
        foreach (var sizes in topologies)
        {
            var weights = BuildWeights(sizes, seed);
            var mlp = new MlpBrain(sizes, weights);
            var neat = NeatGenome.FromMlp(sizes, weights).ToBrain();

            var rng = new AntSim.Core.Sim.DeterministicRandom((ulong)seed * 1000UL);
            for (int k = 0; k < 64; k++)
            {
                var s = RandomSensors(rng);
                var dMlp = AntDecision.Neutral();
                var dNeat = AntDecision.Neutral();
                mlp.Evaluate(in s, ref dMlp);
                neat.Evaluate(in s, ref dNeat);
                AssertSameDecision(in dMlp, in dNeat);
            }
            seed++;
        }
    }

    [Fact]
    public void FromMlp_ParidadConGenomasReales_DelPoolWarmV2()
    {
        // Genomas REALES entrenados del fixture trackeado (no aleatorios): la
        // conversión promete que cualquier .antgenome v1 se vuelve NEAT sin
        // perder ni un bit de comportamiento.
        string path = TestPaths.WarmV2PoolPath();
        Assert.True(System.IO.File.Exists(path), $"Fixture no encontrado: {path}");
        var (_, genomes) = AntGenomeFile.ReadFile(path, BrainContract.CurrentVersion);
        Assert.True(genomes.Count > 0);

        foreach (var g in genomes.Cast<MlpGenome>().Take(4))
        {
            var mlp = g.ToBrain();
            var neat = NeatGenome.FromMlp(g.Sizes, g.CopyWeights()).ToBrain();

            var rng = new AntSim.Core.Sim.DeterministicRandom(2024UL);
            for (int k = 0; k < 64; k++)
            {
                var s = RandomSensors(rng);
                var dMlp = AntDecision.Neutral();
                var dNeat = AntDecision.Neutral();
                mlp.Evaluate(in s, ref dMlp);
                neat.Evaluate(in s, ref dNeat);
                AssertSameDecision(in dMlp, in dNeat);
            }
        }
    }

    [Fact]
    public void FromMlp_InnovacionesCanonicas_OrdenadasPorFromTo()
    {
        int[] sizes = { 19, 8, 6 };
        var genome = NeatGenome.FromMlp(sizes, BuildWeights(sizes, 5));

        var conns = genome.Conns.OrderBy(c => c.Innovation).ToList();
        Assert.Equal(conns.Count, genome.Conns.Count); // 1..K sin huecos
        Assert.Equal(1, conns[0].Innovation);
        Assert.Equal(conns.Count, conns[^1].Innovation);
        for (int i = 1; i < conns.Count; i++)
        {
            // Orden canónico: (from,to) estrictamente ascendente por innovación.
            var prev = (conns[i - 1].From, conns[i - 1].To);
            var curr = (conns[i].From, conns[i].To);
            Assert.True(prev.CompareTo(curr) < 0, $"Innovación {conns[i].Innovation} fuera de orden canónico.");
        }
        // MLP denso 19-8-6: 8·19 (entrada→oculta) + 6·8 (oculta→salida) = 200
        // conexiones (los biases van a los nodos, no a conexiones).
        Assert.Equal(200, genome.Conns.Count);
    }

    [Fact]
    public void Invariantes_RechazaCiclo()
    {
        var conns = new List<ConnGene>
        {
            new(1, 0, 25, 0.5f, true),
            new(2, 25, 26, 0.5f, true),
            new(3, 26, 25, 0.5f, true), // ciclo 25→26→25
            new(4, 26, 19, 0.5f, true),
        };
        Assert.Throws<ArgumentException>(() => new NeatGenome(Base25(), conns));
    }

    [Fact]
    public void Invariantes_RechazaIdDuplicadoEInnovacionDuplicada()
    {
        var nodes = Base25();
        nodes.Add(new NodeGene(25, NodeKind.Hidden, 0f, (byte)ActivationId.Tanh)); // id duplicado
        var conns = new List<ConnGene>
        {
            new(1, 0, 25, 0.5f, true),
            new(1, 1, 25, 0.5f, true), // innovación duplicada
        };
        Assert.Throws<ArgumentException>(() => new NeatGenome(nodes, conns));
    }

    [Fact]
    public void Invariantes_RechazaConexionANodoInexistente()
    {
        var conns = new List<ConnGene> { new(1, 0, 999, 0.5f, true) };
        Assert.Throws<ArgumentException>(() => new NeatGenome(Base25(), conns));
    }

    [Fact]
    public void Invariantes_RechazaConexionActivaDuplicada()
    {
        var conns = new List<ConnGene>
        {
            new(1, 0, 25, 0.5f, true),
            new(2, 0, 25, 0.5f, true), // misma (from,to) activa
        };
        Assert.Throws<ArgumentException>(() => new NeatGenome(Base25(), conns));
    }

    [Fact]
    public void Invariantes_RechazaExcesoDeTopes()
    {
        // 501 nodos > MaxNodes=500.
        var nodes = Base25();
        for (int id = 26; nodes.Count <= NeatGenome.MaxNodes; id++)
            nodes.Add(new NodeGene(id, NodeKind.Hidden, 0f, (byte)ActivationId.Tanh));
        Assert.Throws<ArgumentException>(() => new NeatGenome(nodes, new List<ConnGene>()));
    }

    [Fact]
    public void Invariantes_RechazaCanalesIncorrectos()
    {
        // 18 entradas: el contrato v1 exige exactamente 19 (ids 0..18).
        var nodes = new List<NodeGene>();
        for (int i = 0; i < 18; i++) nodes.Add(new NodeGene(i, NodeKind.Input, 0f, (byte)ActivationId.Tanh));
        for (int i = 0; i < 6; i++) nodes.Add(new NodeGene(19 + i, NodeKind.Output, 0f, (byte)ActivationId.Tanh));
        Assert.Throws<ArgumentException>(() => new NeatGenome(nodes, new List<ConnGene>()));
    }

    [Fact]
    public void Construccion_Determinista_MismaEvaluacion()
    {
        var conns = new List<ConnGene>
        {
            new(3, 25, 19, 0.7f, true),   // desordenadas a propósito
            new(1, 0, 25, 0.5f, true),
            new(2, 5, 25, 0.2f, true),
        };
        var b1 = new NeatGenome(Base25(), conns).ToBrain();
        var b2 = new NeatGenome(Base25(), conns.Select(c => c with { })).ToBrain();
        Assert.Equal(b1.ActivationTotal, b2.ActivationTotal);

        var rng = new AntSim.Core.Sim.DeterministicRandom(11UL);
        for (int k = 0; k < 32; k++)
        {
            var s = RandomSensors(rng);
            var d1 = AntDecision.Neutral();
            var d2 = AntDecision.Neutral();
            b1.Evaluate(in s, ref d1);
            b2.Evaluate(in s, ref d2);
            AssertSameDecision(in d1, in d2);
        }
    }

    [Fact]
    public void DistanceTo_Identicos_Cero_Disjuntos_Cuentan_Incomparables_Uno()
    {
        var aConns = new List<ConnGene> { new(1, 0, 25, 0.5f, true), new(2, 25, 19, 0.3f, true) };
        var a = new NeatGenome(Base25(), aConns);
        var b = a.Clone();
        Assert.Equal(0.0, a.DistanceTo(b), 12);

        var cConns = new List<ConnGene>
        {
            new(1, 0, 25, 0.5f, true),
            new(2, 25, 19, 0.3f, true),
            new(3, 3, 25, 0.1f, true), // disjunto adicional
        };
        var c = new NeatGenome(Base25(), cConns);
        double d = a.DistanceTo(c);
        Assert.True(d > 0.0);

        Assert.Equal(1.0, a.DistanceTo(null), 12); // incomparables
    }

    [Fact]
    public void NeatBrain_ConexionDesactivada_NoFluye()
    {
        // Enabled=false: no participa ni en la topología ni en la suma.
        var connsOn = new List<ConnGene> { new(1, 0, 25, 0.5f, true), new(2, 25, 19, 1.0f, true) };
        var connsOff = new List<ConnGene> { new(1, 0, 25, 0.5f, false), new(2, 25, 19, 1.0f, true) };

        var on = new NeatGenome(Base25(), connsOn).ToBrain();
        var off = new NeatGenome(Base25(), connsOff).ToBrain();

        var s = AntSensors.Default();
        s.FoodTrailCenter = 0.8f;
        var dOn = AntDecision.Neutral();
        var dOff = AntDecision.Neutral();
        on.Evaluate(in s, ref dOn);
        off.Evaluate(in s, ref dOff);
        Assert.NotEqual(BitConverter.SingleToInt32Bits(dOn.Steer), BitConverter.SingleToInt32Bits(dOff.Steer));
    }

    private static float[] BuildWeights(int[] sizes, int seed)
    {
        var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        var rng = new AntSim.Core.Sim.DeterministicRandom((ulong)seed);
        for (int i = 0; i < w.Length; i++)
            w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        return w;
    }

    /// <summary>25 nodos fijos del contrato v1 (19 entradas + 6 salidas) + un oculto (id 25).</summary>
    private static List<NodeGene> Base25()
    {
        var nodes = new List<NodeGene>();
        for (int i = 0; i < 19; i++) nodes.Add(new NodeGene(i, NodeKind.Input, 0f, (byte)ActivationId.Tanh));
        for (int i = 0; i < 6; i++) nodes.Add(new NodeGene(19 + i, NodeKind.Output, 0f, (byte)ActivationId.Tanh));
        nodes.Add(new NodeGene(25, NodeKind.Hidden, 0f, (byte)ActivationId.Tanh));
        return nodes;
    }
}
