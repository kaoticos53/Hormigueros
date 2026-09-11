using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Evolution;
using AntSim.Core.Scenario;
using AntSim.Core.Serialization;
using AntSim.Core.Sim;
using AntSim.Core.Telemetry;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// Modo reuso de cerebros (F4.4): los nacimientos clonan un genoma de la
    /// élite en vez de cruzar+mutar — opt-in por colonia, determinista, con el
    /// flag serializado en .antsave y el linaje multi-cuerpo de la tarjeta de
    /// inspección como testigo en el stream real.
    /// </summary>
    public sealed class CloneFromEliteTests
    {
        [Fact]
        public void Clonado_MismaHuella_EntreCuerpos()
        {
            // Con élite de UN miembro, todo clon copia al mismo genoma: huellas
            // idénticas (el cruce nunca lo es). Con élite mayor el torneo reparte
            // padres — el reuso aparece con élite pequeña o por azar, no siempre.
            var pool = new GenomePool(DeterministicRandom.FromState(1, 2, 3, 4),
                new[] { 4, 6, 2 }, seedCount: 1, cloneFromElite: true);

            var g1 = pool.Birth();
            var g2 = pool.Birth();
            var g3 = pool.Birth();

            uint Fingerprint(MlpGenome g) => SimSnapshot.Fingerprint(g);
            Assert.Equal(Fingerprint(g1), Fingerprint(g2));
            Assert.Equal(Fingerprint(g1), Fingerprint(g3));

            // El clon es independiente del élite (editar el clon no toca la fuente).
            Assert.NotSame(g1, g2);
        }

        [Fact]
        public void SinModo_CerebrosUnicosPorNacimiento()
        {
            // El modo por defecto (cruce+mutación) produce genomas distintos:
            // 1 cerebro = 1 cuerpo, la realidad actual de los mundos.
            var pool = new GenomePool(DeterministicRandom.FromState(1, 2, 3, 4),
                new[] { 4, 6, 2 }, seedCount: 3);

            uint Fingerprint(MlpGenome g) => SimSnapshot.Fingerprint(g);
            var fps = new HashSet<uint> { Fingerprint(pool.Birth()), Fingerprint(pool.Birth()), Fingerprint(pool.Birth()) };
            Assert.Equal(3, fps.Count);
        }

        [Fact]
        public void Determinismo_MismoSeed_MismoMundo_ConYQueSinModo()
        {
            // Dentro de cada modo, misma semilla ⇒ mundo bit a bit. Los DOS modos
            // corren sobre la misma semilla y sus streams son estables.
            string a = GameScenario.Run(42, ticks: 400, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null, cloneFromElite: true);
            string b = GameScenario.Run(42, ticks: 400, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null, cloneFromElite: true);
            Assert.Equal(a, b); // bit a bit

            string c = GameScenario.Run(42, ticks: 400, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null, cloneFromElite: false);
            Assert.NotEqual(a, c); // el modo ES una decisión de diseño visible
        }

        [Fact]
        public void StreamReal_ConModo_CuerposCompartenCerebro()
        {
            // EL TESTIGO DE UI: con el modo activo, el stream genuino contiene
            // huellas compartidas por varios cuerpos — lo que el linaje de la
            // tarjeta de inspección necesita para existir en partida real.
            string stream = GameScenario.Run(42, ticks: 600, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null, cloneFromElite: true);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var seen = new Dictionary<uint, HashSet<uint>>(); // huella → cuerpos
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                foreach (var ant in v.Ants)
                {
                    if (!seen.TryGetValue(ant.GenomeFingerprint, out var set))
                        seen[ant.GenomeFingerprint] = set = new HashSet<uint>();
                    set.Add(ant.Id);
                }
            }

            Assert.Contains(seen.Values, set => set.Count > 1); // hay cerebro multi-cuerpo

            // Y el modelo de linaje de Unity lo consume: la huella más común
            // agrupa sus cuerpos en orden de aparición.
            var (fp, bodies) = seen.OrderByDescending(kv => kv.Value.Count).First();
            Assert.True(bodies.Count > 1);
            var insp = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();
            insp.FollowBrain(fp);
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) insp.Observe(v);
            }
            var lineage = insp.LineageOf(fp);
            Assert.Equal(bodies.Count, lineage.Count);

            string card = insp.RenderCard();
            Assert.StartsWith("cerebro #", card);
            Assert.Contains(bodies.Count.ToString() + " cuerpos", card);
        }

        [Fact]
        public void Checkpoint_GuardaElModo_YContinuaBitABit()
        {
            // Save/load/replay con el modo activo: el flag sobrevive (v2) y el
            // mundo continúa idéntico — el modo es parte del estado del mundo.
            var sim = new WorldSim(42, 96, colonyCount: 1, cloneFromElite: true);
            for (int i = 0; i < 300; i++) sim.Step();

            byte[] save = WorldSimSave.Serialize(sim);
            var loaded = WorldSimSave.Deserialize(save);
            Assert.True(loaded.Colonies[0].Pool.CloneFromElite, "el modo debe sobrevivir al checkpoint");

            for (int i = 0; i < 200; i++) sim.Step();
            for (int i = 0; i < 200; i++) loaded.Step();

            Assert.Equal(sim.Tick, loaded.Tick);
            Assert.Equal(sim.HashLine(), loaded.HashLine());

            // Y un checkpoint SIN modo también lo conserva (false explícito).
            var sim2 = new WorldSim(42, 96, colonyCount: 1);
            var loaded2 = WorldSimSave.Deserialize(WorldSimSave.Serialize(sim2));
            Assert.False(loaded2.Colonies[0].Pool.CloneFromElite);
        }
    }
}
