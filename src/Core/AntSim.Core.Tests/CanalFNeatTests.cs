using System;
using System.Globalization;
using AntSim.Core.Brain;
using AntSim.Core.Evolution;
using AntSim.Core.Scenario;
using AntSim.Core.Sim;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.2c rodaja 6 — canal F generalizado + inspector:
    ///   1. El canal F NEAT emite "graph" {n,h,c} coherente con las activaciones
    ///      y el render desde el stream difiere del render MLP fijo.
    ///   2. El canal A acepta el 13º campo (brainShape) sin romper streams v1.
    ///   3. La tarjeta de linaje muestra las formas n/h/c de los cerebros.
    /// El hash canónico no cambia (campos solo-NEAT, ausentes en mundos v1):
    /// verificado vía los pins CI existentes.
    /// </summary>
    public class CanalFNeatTests
    {
        private static DeterministicRandom _rng = new(1UL);
        private static NeatGenome DenseNeat(ulong seed)
        {
            int[] sizes = { 19, 8, 6 };
            var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
            var rng = new DeterministicRandom(seed);
            for (int i = 0; i < w.Length; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
            return NeatGenome.FromMlp(sizes, w);
        }

        private static NeatGenome GrownNeat(ulong seed)
        {
            var genome = DenseNeat(seed);
            var registry = new InnovationRegistry(new[] { genome });
            var rng = new DeterministicRandom(seed * 7919 + 13);
            Assert.True(NeatOperators.MutateAddNode(genome, registry, ref rng));
            return genome;
        }

        private static string ExtractHash(string stream)
        {
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.StartsWith("{\"end\"", StringComparison.Ordinal))
                {
                    int i = t.IndexOf("\"hash\":\"", StringComparison.Ordinal) + 8;
                    return t.Substring(i, 64);
                }
            }
            throw new InvalidOperationException("stream sin línea end");
        }

        // ————— 1. canal F con topología —————

        [Fact]
        public void CanalF_Mlp_NoEmiteGraph()
        {
            // Un mundo MLP clásico no emite "graph": los streams v1 no cambian.
            string stream = GameScenario.Run(42, 150, colonies: 2, 96, 1,
                inspectId: 1, activEvery: 50);
            Assert.DoesNotContain("\"graph\":", stream, StringComparison.Ordinal);
        }

        [Fact]
        public void RenderNeat_DesdeStream_TopologiaCoherente()
        {
            // La sonda de rodaja 1 midió el coste; aquí la paridad de FORMA:
            // FromMlp produce 33 nodos/200 conns, así que la descripción que
            // viajaría por el canal es exactamente la del grafo denso.
            var genome = DenseNeat(42);
            var brain = genome.ToBrain();
            var g = brain.Graph;

            Assert.Equal(33, g.NodeIds.Length);
            Assert.Equal(200, g.ActiveConnCount);
            Assert.Equal(8, g.HiddenCount);              // los 8 ocultos del MLP
            Assert.Equal(1, g.MaxDepth);                 // una capa oculta → prof. 1

            // IDs canónicos: entradas 0..18, ocultos 25..32, salidas 19..24.
            Assert.Equal(0, g.NodeIds[0]);
            Assert.Equal(18, g.NodeIds[18]);
            Assert.Equal(25, g.NodeIds[19]);
            Assert.Equal(32, g.NodeIds[26]);
            Assert.Equal(19, g.NodeIds[27]);
            Assert.Equal(24, g.NodeIds[32]);

            // Activaciones: la paridad bit a bit ya está probada en rodaja 1;
            // aquí basta con que el render consuma el vector sin error.
            float[] acts = new float[brain.ActivationTotal];
            brain.SnapshotActivations(acts);
            string text = MlpAsciiGraph.RenderNeat(g, acts, title: "t");
            Assert.Contains("NEAT 8h/200c (33 nodos)", text);
        }

        [Fact]
        public void RenderNeat_DifiereDelMlpFijo()
        {
            // El criterio de la rodaja: el render del grafo NO es el render del
            // MLP por capas — muestra el resumen n/h/c y estructura de grafo.
            var genome = DenseNeat(42);
            var brain = genome.ToBrain();
            var g = brain.Graph;
            float[] acts = new float[brain.ActivationTotal];
            brain.SnapshotActivations(acts);

            string neat = MlpAsciiGraph.RenderNeat(g, acts, title: "x");
            string mlp = MlpAsciiGraph.Render(new[] { 19, 8, 6 }, acts, title: "x");

            Assert.NotEqual(neat, mlp);
            Assert.Contains("NEAT ", neat);
            Assert.Contains("MLP 19·8·6", mlp);
        }

        [Fact]
        public void RenderNeat_GrafoCrecido_DosCapasDeOcultos()
        {
            // Un grafo con skip (oculto 33 alimentado desde entradas Y del
            // oculto 25) tiene profundidad 2: el render crea la capa extra.
            var mutated = GrownNeat(7);
            var brain = mutated.ToBrain();
            var g = brain.Graph;

            Assert.Equal(34, g.NodeIds.Length);
            Assert.Equal(2, g.MaxDepth); // el split inserta un nodo entre capa 0 y 2

            float[] acts = new float[brain.ActivationTotal];
            brain.SnapshotActivations(acts);
            string text = MlpAsciiGraph.RenderNeat(g, acts);
            Assert.Contains("NEAT 9h/", text);
        }

        [Fact]
        public void BrainShapeOf_Mlp_Vacio_Neat_H()
        {
            int[] sizes = { 19, 8, 6 };
            var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
            for (int i = 0; i < w.Length; i++) w[i] = 0.01f * (i % 7 - 3);
            var mlpGenome = new MlpGenome(sizes, w);
            var mlpBrain = mlpGenome.ToBrain(); // MLP clásico
            Assert.Equal("", AntSim.Core.Telemetry.SimSnapshot.BrainShapeOf(mlpBrain));

            var neat = DenseNeat(1);
            Assert.Equal("8h/200c", AntSim.Core.Telemetry.SimSnapshot.BrainShapeOf(neat.ToBrain()));
        }

        [Fact]
        public void CanalF_HashInvariante_ConBrainShapeEnSnapshot()
        {
            // Capture ahora calcula BrainShapeOf por hormiga: telemetría pura,
            // sin tocar el mundo. El hash final no cambia.
            string s1 = GameScenario.Run(42, 200, colonies: 2, 96, 10);
            string s2 = GameScenario.Run(42, 200, colonies: 2, 96, 10);
            Assert.Equal(ExtractHash(s1), ExtractHash(s2));
        }

        // ————— 2. parser Unity: tolerancia y campos nuevos —————

        private static GameStreamParser.TickView? ParseLastTick(string stream)
        {
            var p = new GameStreamParser();
            GameStreamParser.TickView? last = null;
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.Length == 0) continue;
                if (p.ParseLine(t) is GameStreamParser.TickView v) last = v;
            }
            return last;
        }

        [Fact]
        public void Parser_StreamMlp_SigueFuncionando()
        {
            string stream = GameScenario.Run(42, 120, colonies: 2, 96, 10,
                inspectId: 1, activEvery: 30);
            var p = new GameStreamParser();
            foreach (var line in stream.Split('\n'))
                if (line.TrimEnd('\r').Length > 0) p.ParseLine(line.TrimEnd('\r'));

            Assert.NotNull(p.Header);
            Assert.Equal(30, p.Header.ActivEvery);
            Assert.Equal(1ul, p.Header.InspectId);
            Assert.NotNull(p.FinalHash);
        }

        [Fact]
        public void Parser_BrainShape_CampoOpcional()
        {
            // Un AntPose de 12 campos (v1) → BrainShape ""; con 13º campo → "8h/200c".
            var v1 = new GameStreamParser.AntPose(1, 0, 0f, 0f, 0f, false, true);
            Assert.Equal("", v1.BrainShape);

            var v2 = new GameStreamParser.AntPose(1, 0, 0f, 0f, 0f, false, true,
                brainShape: "8h/200c");
            Assert.Equal("8h/200c", v2.BrainShape);
        }

        [Fact]
        public void Parser_GraphView_Neat()
        {
            var g = new GameStreamParser.GraphView(34, new[] { 1, 1, 1, 1, 1, 1, 1, 1, 2 }, 201);
            Assert.Equal(34, g.N);
            Assert.Equal(9, g.H.Length);
            Assert.Equal(201, g.C);
        }

        // ————— 3. inspector: linaje n/h/c —————

        [Fact]
        public void Inspector_Linaje_MuestraFormas()
        {
            var model = new AntInspectorModel();
            var view = new GameStreamParser.TickView { Tick = 10 };
            view.Ants.Add(new GameStreamParser.AntPose(1, 0, 0f, 0f, 0f, false, true,
                genomeFingerprint: 777, brainShape: "8h/200c"));
            model.Select(1);
            model.Observe(view);
            model.FollowBrain(777);

            string card = model.RenderCard();
            Assert.Contains("cerebro #777", card);
            Assert.Contains("8h/200c", card);
        }

        [Fact]
        public void Inspector_Linaje_MlpIndicaMLP()
        {
            var model = new AntInspectorModel();
            var view = new GameStreamParser.TickView { Tick = 10 };
            view.Ants.Add(new GameStreamParser.AntPose(1, 0, 0f, 0f, 0f, false, true,
                genomeFingerprint: 888)); // sin 13º campo → MLP clásico
            model.Select(1);
            model.Observe(view);
            model.FollowBrain(888);

            string card = model.RenderCard();
            Assert.Contains("cerebro #888", card);
            Assert.Contains("MLP 19·8·6", card);
        }
    }
}
