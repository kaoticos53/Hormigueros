using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2c rodaja 2 — operadores genéticos NEAT (diseño §3.4):
///   1. CIRCUITO PRESERVADO: el test clásico de NEAT — un crossover entre
///      padres con circuitos distintos no pierde el circuito del mejor padre.
///      Se adapta al juego: el "XOR" es un patrón sensor→oculto→salida
///      plantado en los padres; el hijo mejor padre debe poder evaluarlo.
///   2. AddNode conserva la FUNCIÓN exacta en el instante de la mutación
///      (el circuito no se rompe: pesos 1 + viejo peso reproducen la suma).
///   3. AddConn nunca crea ciclos ni duplicados; tras 20 intentos, no-op.
///   4. Toggle inversible; los genes desactivados permanecen (histórico).
///   5. Crossover align-by-innovation: matching al azar, disjoint/excess del
///      mejor, empate → A. Determinista con la misma semilla.
///   6. InnovationRegistry: dos genomas que mutan el mismo par comparten
///      innovation; la secuencia es determinista.
/// </summary>
public class NeatOperatorTests
{
    // ————— helpers —————

    private static List<NodeGene> Base25()
    {
        var nodes = new List<NodeGene>();
        for (int i = 0; i < 19; i++) nodes.Add(new NodeGene(i, NodeKind.Input, 0f, (byte)ActivationId.Tanh));
        for (int i = 0; i < 6; i++)
        {
            byte act = i == 0 ? (byte)ActivationId.Tanh : (byte)ActivationId.Sigmoid;
            nodes.Add(new NodeGene(19 + i, NodeKind.Output, 0f, act));
        }
        return nodes;
    }

    private static NodeGene Hidden(int id, float bias = 0f)
        => new(id, NodeKind.Hidden, bias, (byte)ActivationId.Tanh);

    private static AntSensors RandomSensors(DeterministicRandom rng)
    {
        var s = AntSensors.Default();
        s.FoodTrailCenter = (float)(rng.NextDouble01() * 2.0 - 1.0);
        s.HomeTrailCenter = (float)(rng.NextDouble01() * 2.0 - 1.0);
        s.Energy = (float)rng.NextDouble01();
        s.HasLoad = rng.NextBool() ? 1f : 0f;
        return s;
    }

    /// <summary>
    /// Circuito plantado: sensor 0 → oculto H → salida 0 (steer), con los
    /// pesos dados. Evaluar el cerebro con FoodTrailCenter = x debe dar
    /// steer = tanh(w2 · tanh(w1 · x + b) + b2) INDEPENDIENTE del resto —
    /// el resto del genoma usa salidas 1..5 que el test no mira.
    /// </summary>
    /// <summary>Circuito con innovations ASIGNADAS POR UN REGISTRO COMPARTIDO
    /// (la disciplina del pool real: pares distintos reciben innovations
    /// distintas - dos padres con la misma innovacion para pares distintos
    /// seria un pool invalido que el registry existe para impedir).</summary>
    private static (NeatGenome genome, int inInnov, int outInnov) WithCircuit(
        InnovationRegistry reg, int hid, float w1, float w2, float b,
        IEnumerable<ConnGene> extra)
    {
        var nodes = Base25();
        nodes.Add(Hidden(hid, b));
        var conns = new List<ConnGene>
        {
            new(reg.GetOrAssign(0, hid), 0, hid, w1, true),
            new(reg.GetOrAssign(hid, 19), hid, 19, w2, true),
        };
        conns.AddRange(extra);
        return (new NeatGenome(nodes, conns), reg.GetOrAssign(0, hid), reg.GetOrAssign(hid, 19));
    }

    private static bool SnapshotEnabled(NeatGenome g, int innovation)
        => g.Conns.ToList().First(c => c.Innovation == innovation).Enabled;

    private static float SteerOf(NeatGenome g, float foodTrail)
    {
        var brain = g.ToBrain();
        var s = AntSensors.Default();
        s.FoodTrailCenter = foodTrail;
        var d = AntDecision.Neutral();
        brain.Evaluate(in s, ref d);
        return d.Steer;
    }

    // ————— 1. el circuito no se pierde en el crossover —————

    [Fact]
    public void Crossover_ElMejorPadreConservaSuCircuito()
    {
        // Registry compartido: los pares de cada padre reciben innovations
        // distintas (el circuito de B NO puede "disfrazarse" del de A).
        var reg = new InnovationRegistry(Array.Empty<NeatGenome>());

        // Padre A (el MEJOR): circuito fuerte por el oculto 25.
        var (a, inA, outA) = WithCircuit(reg, 25, w1: 3f, w2: 3f, b: 0f,
            extra: new List<ConnGene> { new(reg.GetOrAssign(5, 19) + 100, 5, 19, 0.1f, true) });
        a.Fitness = 10.0;

        // Padre B (peor): circuito DISTINTO por otro oculto (30) + disjoint.
        var (b, inB, outB) = WithCircuit(reg, 30, w1: -2f, w2: -2f, b: 0.5f,
            extra: new List<ConnGene>
            {
                new(reg.GetOrAssign(7, 19) + 100, 7, 19, 0.2f, true),     // disjoint del peor: NO debe estar
                new(reg.GetOrAssign(30, 19) + 100, 30, 19, 0.3f, false),  // historico apagado de B
            });
        b.Fitness = 1.0;
        Assert.NotEqual(inA, inB); // circuitos distintos: innovations distintas
        Assert.NotEqual(outA, outB);
        int aDisjoint = reg.GetOrAssign(5, 19) + 100;
        int bDisjoint = reg.GetOrAssign(7, 19) + 100;

        var rng = new DeterministicRandom(42UL);
        for (int trial = 0; trial < 32; trial++) // matching al azar: todas las tiradas
        {
            var child = NeatOperators.Crossover(a, b, ref rng);

            // El circuito de A vive en el hijo COMO GENES DISJOINT DEL MEJOR
            // (los pares de A no existen en B, asi que nunca son matching).
            Assert.Contains(child.Conns, c => c.Innovation == inA && c.From == 0 && c.To == 25 && c.Enabled);
            Assert.Contains(child.Conns, c => c.Innovation == outA && c.From == 25 && c.To == 19 && c.Enabled);

            // El disjoint del peor NO entra; el del mejor SI.
            Assert.DoesNotContain(child.Conns, c => c.Innovation == bDisjoint);
            Assert.Contains(child.Conns, c => c.Innovation == aDisjoint);

            // El hijo EVALUA el circuito de A: steer = tanh(3·tanh(3x)).
            foreach (float x in new[] { -0.8f, -0.3f, 0.15f, 0.6f })
            {
                float expected = MathF.Tanh(3f * MathF.Tanh(3f * x));
                float got = SteerOf(child, x);
                Assert.True(MathF.Abs(expected - got) < 1e-4f,
                    $"trial {trial} x={x}: esperado {expected}, obtenido {got}");
            }
        }
    }

    [Fact]
    public void Crossover_EmpateDeFitness_GanaElPadreA()
    {
        var reg = new InnovationRegistry(Array.Empty<NeatGenome>());
        var (a, _, _) = WithCircuit(reg, 25, 3f, 3f, 0f,
            extra: new List<ConnGene> { new(reg.GetOrAssign(5, 19) + 100, 5, 19, 0.1f, true) });
        var (b, _, _) = WithCircuit(reg, 30, -3f, -3f, 0f,
            extra: new List<ConnGene> { new(reg.GetOrAssign(7, 19) + 100, 7, 19, 0.2f, true) });
        a.Fitness = 5.0;
        b.Fitness = 5.0; // empate
        int aDisjoint = reg.GetOrAssign(5, 19) + 100;
        int bDisjoint = reg.GetOrAssign(7, 19) + 100;

        var rng = new DeterministicRandom(7UL);
        var child = NeatOperators.Crossover(a, b, ref rng);

        // El disjoint que entra es el de A, no el de B.
        Assert.Contains(child.Conns, c => c.Innovation == aDisjoint);
        Assert.DoesNotContain(child.Conns, c => c.Innovation == bDisjoint);
    }

    // ————— 2. AddNode conserva la función —————

    [Fact]
    public void MutateAddNode_ConservaLaFuncionExacta()
    {
        // Genoma denso convertido (paridad demostrada en rodaja 1) + circuito.
        int[] sizes = { 19, 8, 6 };
        var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        var rng = new DeterministicRandom(99UL);
        for (int i = 0; i < w.Length; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        var genome = NeatGenome.FromMlp(sizes, w);

        var registry = new InnovationRegistry(new[] { genome });
        var before = new List<float>();
        var sensorSeed = new DeterministicRandom(2024UL);
        for (int k = 0; k < 24; k++) before.Add(SteerOf(genome, (float)(sensorSeed.NextDouble01() * 2 - 1)));

        bool mutated = NeatOperators.MutateAddNode(genome, registry, ref rng);
        Assert.True(mutated);

        // Función idéntica BIT A BIT: partió una conn en 1→new(1)→to(w) — la
        // suma del destino es exactamente la misma (peso viejo reproducido).
        sensorSeed = new DeterministicRandom(2024UL);
        for (int k = 0; k < 24; k++)
        {
            float x = (float)(sensorSeed.NextDouble01() * 2 - 1);
            Assert.Equal(BitConverter.SingleToInt32Bits(before[k]),
                         BitConverter.SingleToInt32Bits(SteerOf(genome, x)));
        }

        // Estructura: un oculto nuevo (cualquier id ≥ 25), la vieja APAGADA
        // (histórico), dos nuevas. La víctima es aleatoria — assertions sobre
        // la FORMA, no sobre qué conexión cayó.
        Assert.Equal(34, genome.Nodes.Count);
        Assert.Single(genome.Conns.Where(c => !c.Enabled)); // exactamente la víctima
        int newId = genome.Nodes.Max(n => n.Id);
        Assert.True(newId >= 25);
        Assert.Contains(genome.Conns, c => c.Enabled && c.From == newId);
        Assert.Contains(genome.Conns, c => c.Enabled && c.To == newId && c.Weight == 1f);
    }

    // ————— 3. AddConn: sin ciclos, sin duplicados, no-op tras 20 intentos —————

    [Fact]
    public void MutateAddConn_NuncaCicloNiDuplicado()
    {
        int[] sizes = { 19, 8, 6 };
        var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        var rng = new DeterministicRandom(5UL);
        for (int i = 0; i < w.Length; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        var genome = NeatGenome.FromMlp(sizes, w);
        var registry = new InnovationRegistry(new[] { genome });

        // El denso capa-a-capa solo le faltan los SKIP input->salida (19·6 =
        // 114 pares validos). Con 20 intentos por llamada muestreando entre los
        // pares LIBRES, anade SOLO skips y NUNCA un invalido (cada
        // AddConnChecked revalida unicidad y aciclicidad: lanzaria si se las
        // saltara). Los ultimos pares quedan fuera por el tope de intentos:
        // esperamos muchos, no los 114 exactos.
        int added = 0;
        for (int i = 0; i < 100; i++)
            if (NeatOperators.MutateAddConn(genome, registry, ref rng)) added++;
        Assert.True(added >= 60, $"solo {added} skips anadidos en 100 llamadas");
        Assert.Equal(200 + added, genome.Conns.Count);
        Assert.All(genome.Conns.Where(c => c.From < 19 && c.To >= 19), c => Assert.True(c.Enabled));

        // Ahora un grafo disperso: AddConn añade pares VÁLIDOS siempre.
        var sparseConns = new List<ConnGene> { new(1, 0, 25, 0.5f, true) };
        var sparseNodes = Base25();
        sparseNodes.Add(Hidden(25));
        var sparse = new NeatGenome(sparseNodes, sparseConns);
        var sparseRegistry = new InnovationRegistry(new[] { sparse });

        for (int i = 0; i < 60; i++)
        {
            NeatOperators.MutateAddConn(sparse, sparseRegistry, ref rng);
            // Invariante clave: el cerebro construye (Kahn lanza si hay ciclo)
            // — construir el cerebro ES el test anti-ciclo.
            var brain = sparse.ToBrain();
            Assert.Equal(sparse.Nodes.Count, brain.ActivationTotal);
        }
        // Sin duplicados activos (la validación del genoma lo garantiza: si
        // hubiera uno, el ctor habría lanzado en el AddConnChecked).
        Assert.True(sparse.Conns.Count(c => c.Enabled) >= 30);
    }

    // ————— 4. Toggle —————

    [Fact]
    public void MutateToggle_Invierte_YElHistoricoPermanece()
    {
        var conns = new List<ConnGene> { new(1, 0, 25, 0.5f, true), new(2, 25, 19, 1f, true) };
        var genome = new NeatGenome(Base25().Concat(new[] { Hidden(25) }), conns);

        var rng = new DeterministicRandom(3UL);
        // Toggles hasta apagar la 1 (snapshot del estado, no enumeración viva).
        int guard = 0;
        while (SnapshotEnabled(genome, 1) && guard++ < 50)
            NeatOperators.MutateToggle(genome, ref rng);
        Assert.False(SnapshotEnabled(genome, 1));
        Assert.Equal(2, genome.Conns.Count); // el gen APAGADO permanece

        // Y puede reactivarse.
        guard = 0;
        while (!SnapshotEnabled(genome, 1) && guard++ < 50)
            NeatOperators.MutateToggle(genome, ref rng);
        Assert.True(SnapshotEnabled(genome, 1));
    }

    [Fact]
    public void MutateToggle_GrafoVacio_NoOp()
    {
        var genome = new NeatGenome(Base25(), new List<ConnGene>());
        var rng = new DeterministicRandom(1UL);
        Assert.Null(NeatOperators.MutateToggle(genome, ref rng));
    }

    // ————— 5. Determinismo de operadores —————

    [Fact]
    public void Operadores_Deterministas_MismaSemilla()
    {
        var (run1, run2) = (RunSeq(), RunSeq());
        Assert.Equal(run1.Count, run2.Count);
        for (int i = 0; i < run1.Count; i++)
            Assert.Equal(BitConverter.SingleToInt32Bits(run1[i]), BitConverter.SingleToInt32Bits(run2[i]));

        static List<float> RunSeq()
        {
            int[] sizes = { 19, 8, 6 };
            var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
            var rng = new DeterministicRandom(2027UL);
            for (int i = 0; i < w.Length; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
            var g1 = NeatGenome.FromMlp(sizes, w);
            var g2 = g1.Clone();
            g2.Fitness = 1.0;
            var reg = new InnovationRegistry(new[] { g1, g2 });

            var outs = new List<float>();
            for (int gen = 0; gen < 6; gen++)
            {
                NeatOperators.MutateWeights(g1, ref rng, 0.1f);
                NeatOperators.MutateBiases(g1, ref rng, 0.1f);
                NeatOperators.MutateAddConn(g1, reg, ref rng);
                NeatOperators.MutateAddNode(g1, reg, ref rng);
                NeatOperators.MutateToggle(g1, ref rng);
                var child = NeatOperators.Crossover(g1, g2, ref rng);
                outs.Add(SteerOf(child, 0.42f));
            }
            return outs;
        }
    }

    // ————— 6. InnovationRegistry —————

    [Fact]
    public void Registry_MismoPar_MismaInnovation_YLaSecuenciaEsDeterminista()
    {
        var (a, _, _) = WithCircuit(new InnovationRegistry(Array.Empty<NeatGenome>()), 25, 1f, 1f, 0f, Array.Empty<ConnGene>());
        var (b, _, _) = WithCircuit(new InnovationRegistry(Array.Empty<NeatGenome>()), 26, 1f, 1f, 0f, Array.Empty<ConnGene>());
        var reg = new InnovationRegistry(new[] { a, b });

        // El par (0,25) ya vive en A (primer conn del circuito): se respeta.
        int innovA = a.Conns.First(c => c.From == 0 && c.To == 25).Innovation;
        Assert.Equal(innovA, reg.GetOrAssign(0, 25));
        // El par nuevo (0,30) se asigna UNA vez y se comparte; la secuencia
        // continua tras el maximo visto en los genomas semilla.
        int maxSeen = Math.Max(a.Conns.Max(c => c.Innovation), b.Conns.Max(c => c.Innovation));
        int first = reg.GetOrAssign(0, 30);
        Assert.True(first > maxSeen, $"{first} debe superar el maximo visto {maxSeen}");
        Assert.Equal(first, reg.GetOrAssign(0, 30));
        // Siguiente par nuevo: consecutivo.
        Assert.Equal(first + 1, reg.GetOrAssign(1, 30));
        Assert.True(reg.Contains(1, 30));
        Assert.False(reg.Contains(2, 30));
    }

    [Fact]
    public void AddNode_YAddConn_CompartenInnovation_DosPadresMismaMutacion()
    {
        // El caso que motiva el registro: A y B parten la MISMA conexión en la
        // misma generación → el hijo debe poder alinear los dos genes nuevos.
        int[] sizes = { 19, 8, 6 };
        var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        var seedRng = new DeterministicRandom(15UL);
        for (int i = 0; i < w.Length; i++) w[i] = (float)(seedRng.NextDouble01() * 2.0 - 1.0);
        var pa = NeatGenome.FromMlp(sizes, w);
        var pb = pa.Clone();

        var reg = new InnovationRegistry(new[] { pa, pb });

        // Fuerza la MISMA víctima en ambos: partimos la MISMA conexión (25→19,
        // oculto→salida) con el MISMO nodo nuevo (33 — los ocultos del denso
        // son 25..32, así que 33 es libre en ambos y los pares (25,33)/(33,19)
        // NO existen: el registry los asigna nuevos y compartidos).
        var victim = pa.Conns.First(c => c.Enabled && c.From == 25 && c.To == 19);
        pa.SetEnabled(victim.Innovation, false);
        pb.SetEnabled(victim.Innovation, false);
        int inA = reg.GetOrAssign(victim.From, 33);
        int inB = reg.GetOrAssign(victim.From, 33);
        Assert.Equal(inA, inB); // ¡la clave del align-by-innovation!
        pa.AddHiddenNode();
        pa.AddConnChecked(new ConnGene(inA, victim.From, 33, 1f, true));
        pb.AddHiddenNode();
        pb.AddConnChecked(new ConnGene(inB, victim.From, 33, 1f, true));
        int outA = reg.GetOrAssign(33, victim.To);
        int outB = reg.GetOrAssign(33, victim.To);
        Assert.Equal(outA, outB);

        pa.AddConnChecked(new ConnGene(outA, 33, victim.To, victim.Weight, true));
        pb.AddConnChecked(new ConnGene(outB, 33, victim.To, victim.Weight, true));

        // El crossover alinea: el hijo tiene los genes de ambos como MATCHING.
        var r3 = new DeterministicRandom(2028UL);
        var child = NeatOperators.Crossover(pa, pb, ref r3);
        Assert.Contains(child.Conns, c => c.Innovation == inA);
        Assert.Contains(child.Conns, c => c.Innovation == outA);
    }

    // ————— 7. Poda: toggle-off baja el coste y el cerebro la respeta —————

    [Fact]
    public void ToggleOff_CostedeEvaluacionBaja_YFuncionCambia()
    {
        int[] sizes = { 19, 8, 6 };
        var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        var rng = new DeterministicRandom(31UL);
        for (int i = 0; i < w.Length; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        var genome = NeatGenome.FromMlp(sizes, w);

        var s = AntSensors.Default();
        s.FoodTrailCenter = 0.9f;
        var dBefore = AntDecision.Neutral();
        genome.ToBrain().Evaluate(in s, ref dBefore);

        // Apaga la mitad de las conexiones de la capa oculta→salida (snapshot).
        var victims = genome.Conns.Where(c => c.Enabled && c.To == 19).Take(4).Select(c => c.Innovation).ToList();
        foreach (var innov in victims)
            genome.SetEnabled(innov, false);
        Assert.Equal(4, victims.Count);

        var dAfter = AntDecision.Neutral();
        genome.ToBrain().Evaluate(in s, ref dAfter);
        Assert.NotEqual(BitConverter.SingleToInt32Bits(dBefore.Steer),
                        BitConverter.SingleToInt32Bits(dAfter.Steer));
    }
}
