using System;
using System.IO;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// EL TECHO DE NODRIZAS, de los dos lados del espejo.
    ///
    /// La puesta de la ronda se para cuando la cola de cría cubre lo que
    /// sostienen las nodrizas (`vacantes` con el embudo y `puesta` acotada por
    /// `adultas · NurseShare`), así que `pupae` tiene un techo duro. El HUD no
    /// lo recorta: pinta el número del stream tal cual. Por eso el techo se
    /// puede VIGILAR desde la vista — y por eso hay que fijarlo aquí, donde vive
    /// el Core, además de en la partida viva (`scripts/check-hud-pupae.sh`).
    ///
    /// Estas pruebas son lo que impide que la vista vigile un techo que ya no es
    /// el del mundo: si alguien mueve la cuota de nodrizas o el tope de adultas,
    /// `Espejo_…` falla y dice cuál de los dos números se separó.
    /// </summary>
    public class NurseCeilingTests
    {
        [Fact]
        public void Espejo_LaVistaCalculaElMismoTechoQueElCore()
        {
            Assert.Equal(ColonyController.NurseShare, NurseCeilingModel.NurseShare);
            Assert.Equal(ColonyController.MaxAdultsPerColony, NurseCeilingModel.MaxAdults);

            // La MISMA expresión que la puesta (`capacidad` en ColonyController):
            // si la vista usara otra, el guard del HUD gritaría en partidas sanas
            // o se callaría en las rotas.
            for (int adultas = 0; adultas <= ColonyController.MaxAdultsPerColony; adultas++)
            {
                int core = Math.Max(1, (int)(adultas * ColonyController.NurseShare));
                Assert.Equal(core, NurseCeilingModel.For(adultas));
            }

            // Con la tropa llena son 6: la cifra que midió el barrido de diez
            // semillas en el HUD (20 colonias, pico 6 en todas).
            Assert.Equal(6, NurseCeilingModel.Hard);
        }

        [Fact]
        public void Mundo_NuncaEmiteMasPupasQueElTecho_EnElStreamQuePintaElHud()
        {
            // La pareja del lanzador, con laberinto y reclutamiento: el escenario
            // donde la cola se apilaba (479 y 943 pupas) antes del arreglo.
            string fixture = RepoFixture("warm-v2.antgenome");
            Assert.True(File.Exists(fixture), $"fixture ausente: {fixture}");

            string stream = GameScenario.Run(42, ticks: 60000, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: fixture, drops: null,
                maze: true, recruitment: true,
                species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.Eciton });

            var parser = new GameStreamParser();
            int muestras = 0, picoPupas = 0, picoAdultas = 0;

            foreach (string raw in stream.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var v = parser.ParseLine(raw.Trim());
                if (v == null) continue;

                foreach (var c in v.Colonies)
                {
                    muestras++;
                    picoPupas = Math.Max(picoPupas, c.Pupae);
                    picoAdultas = Math.Max(picoAdultas, c.Adults);

                    Assert.True(c.Pupae <= NurseCeilingModel.Hard,
                        $"tick {v.Tick}, colonia {c.Id}: {c.Pupae} pupas con {c.Adults} adultas — " +
                        $"el techo de nodrizas es {NurseCeilingModel.Hard}");
                    Assert.True(c.Adults <= NurseCeilingModel.MaxAdults,
                        $"tick {v.Tick}, colonia {c.Id}: {c.Adults} adultas pasan el tope de diseño");
                }
            }

            Assert.True(muestras > 0, "el stream no trajo ni una línea de colonias");
            // El techo se ALCANZA: la prueba mira el régimen de tropa llena y no
            // un arranque donde la cola todavía no existe (si nunca llega a 6,
            // este test no está midiendo lo que dice medir).
            Assert.Equal(NurseCeilingModel.Hard, picoPupas);
            Assert.Equal(NurseCeilingModel.MaxAdults, picoAdultas);
        }

        private static string RepoFixture(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "tests", "fixtures", name);
        }
    }
}
