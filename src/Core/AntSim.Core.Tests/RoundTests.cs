using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F8 — CONTRATO DE LA RONDA ENCADENADA.
    ///
    /// La partida dejó de ser UNA corrida: cuando no queda ninguna hormiga ni cría
    /// el CLI corta en ese tick (`--until-extinct`), exporta la élite y el juego
    /// abre otra ronda con un laberinto nuevo (semilla derivada) sembrada con esos
    /// cerebros. Aquí se fijan las tres piezas que deciden eso sin Unity:
    ///
    ///   · la CONDICIÓN de extinción, en el mundo (`AllColoniesExtinct`) y en el
    ///     stream (`RoundModel.AllColoniesDead`, que es lo que ve el HUD);
    ///   · la SEMILLA de la ronda siguiente (determinista, distinta en cada ronda y
    ///     nunca 0 — el centinela de «no hay ronda armada»);
    ///   · el POOL con el que se siembra (la élite exportada si existe; el mismo
    ///     pool si no, para que un fallo de exportación no deje el mundo sin
    ///     cerebros).
    ///
    /// Y una comprobación NEGATIVA de la bandera del CLI: con la colonia viva,
    /// `--until-extinct` NO debe cortar la partida antes del horizonte (un corte
    /// prematuro convertiría cada ronda en una ronda de un par de ticks).
    /// </summary>
    public class RoundTests
    {
        private static GameStreamParser.ColonyView Colony(
            int adults = 0, int eggs = 0, int larvae = 0, int pupae = 0, int id = 0)
            => new(id, 100f, 100f, 50f, 150f, adults, eggs, larvae, pupae, elite: 0);

        [Fact]
        public void AllColoniesDead_SoloSiNoQuedaNiAdultaNiCria()
        {
            Assert.True(RoundModel.AllColoniesDead(new[] { Colony(), Colony(id: 1) }));

            Assert.False(RoundModel.AllColoniesDead(new[] { Colony(adults: 1), Colony(id: 1) }));
            Assert.False(RoundModel.AllColoniesDead(new[] { Colony(eggs: 1), Colony(id: 1) }));
            Assert.False(RoundModel.AllColoniesDead(new[] { Colony(larvae: 1), Colony(id: 1) }));
            Assert.False(RoundModel.AllColoniesDead(new[] { Colony(pupae: 1), Colony(id: 1) }));

            // Sin datos no hay ronda que cerrar: vacío/null NO es extinción.
            Assert.False(RoundModel.AllColoniesDead(new GameStreamParser.ColonyView[0]));
            Assert.False(RoundModel.AllColoniesDead(null));
        }

        [Fact]
        public void NextSeed_EsDeterministaDistintaPorRondaYNuncaCero()
        {
            Assert.Equal(RoundModel.NextSeed(42, 1), RoundModel.NextSeed(42, 1));
            Assert.NotEqual(RoundModel.NextSeed(42, 1), RoundModel.NextSeed(42, 2));
            Assert.NotEqual(RoundModel.NextSeed(42, 1), 42UL);

            var vistas = new HashSet<ulong>();
            for (int ronda = 1; ronda <= 64; ronda++)
            {
                ulong s = RoundModel.NextSeed(42, ronda);
                Assert.NotEqual(0UL, s);
                Assert.True(vistas.Add(s), $"la ronda {ronda} repite semilla ({s}): el laberinto no cambiaría");
            }
        }

        [Fact]
        public void NextSeedPool_PrefiereLaEliteYSiNoConservaElPoolActual()
        {
            Assert.Equal("artifacts/evolved-7.antgenome",
                RoundModel.NextSeedPool("artifacts/evolved-7.antgenome", "artifacts/pretrain-maze-los3.antgenome"));
            Assert.Equal("artifacts/pretrain-maze-los3.antgenome",
                RoundModel.NextSeedPool("", "artifacts/pretrain-maze-los3.antgenome"));
            Assert.Equal("artifacts/pretrain-maze-los3.antgenome",
                RoundModel.NextSeedPool(null, "artifacts/pretrain-maze-los3.antgenome"));
        }

        [Fact]
        public void AllColoniesExtinct_ExigeCeroAdultasYCeroCria()
        {
            var sim = new WorldSim(42, gridCells: 32, colonyCount: 1);
            Assert.False(sim.AllColoniesExtinct()); // arranca con adultas y huevos

            var colonia = sim.Colonies[0];
            var unHuevo = colonia.Eggs[0];
            colonia.Adults.Clear();
            colonia.Eggs.Clear();
            colonia.Larvae.Clear();
            colonia.Pupae.Clear();
            Assert.True(sim.AllColoniesExtinct());

            // Cualquier cría viva (aquí una larva) mantiene la ronda abierta.
            colonia.Larvae.Add(unHuevo);
            Assert.False(sim.AllColoniesExtinct());
        }

        [Fact]
        public void SinExtincion_UntilExtinctNoCortaAntesDelHorizonte()
        {
            string stream = GameScenario.Run(42, ticks: 300, colonies: 1, grid: 32,
                untilExtinct: true);

            Assert.DoesNotContain("\"extinct\":true", stream);
            Assert.Contains("\"end\":true,\"tick\":300", stream);
        }
    }
}
