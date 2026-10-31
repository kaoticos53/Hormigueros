using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// La ventana en la que el embudo tiene que volver al tope tras una
        /// eclosión: 30 000 ticks (300 s).
        ///
        /// NO es una latencia prometida, es la puerta de «el lazo CIERRA». El
        /// estado estacionario del embudo no es 6 quieto: la pupa sale a obrera
        /// cuando hay vacante y la cola baja a 5 (o a 0 si se junta con una
        /// racha de muertes), y la reina tarda en reponerla porque el huevo tiene
        /// que poner, empollar y la larva alcanzar `KFull` con el presupuesto de
        /// las 6 nodrizas (40 · 0,15 · 0,088 = 0,528 ep/s). Medido en la pareja
        /// con laberinto y reclutamiento (60 000 ticks, semillas 42 y 11): la
        /// bajada más larga fue de 11 220 ticks y la mediana de 1 300–2 200; en
        /// el barrido de tres semillas (con la 7) el peor caso subió a 16 080.
        /// El umbral deja ~2,7× ese peor caso, así que un ajuste de especie no lo
        /// rompe —no es un pin de rendimiento—; lo que NO deja pasar es un embudo
        /// que se vacía y se queda vacío —el techo corto—, que es el fallo que
        /// esta prueba existe para ver.
        /// </summary>
        private const ulong VentanaDeReposicion = 30000;

        /// <summary>
        /// EL TECHO NO SE QUEDA CORTO: el embudo que se vacía vuelve al tope.
        ///
        /// `Mundo_NuncaEmiteMasPupasQueElTecho_…` fija el pico —que el techo se
        /// ALCANCE—, pero una foto del pico también la da un embudo que se llenó
        /// una vez y no volvió a llenarse nunca. Es el fallo contrario al que ya
        /// está medido (la cola de 479 y 943 pupas) y es igual de mortal: si la
        /// puesta no reanuda al bajar la cola, la ronda pierde el relevo y la
        /// colonia se apaga con 40 adultas en el tablero y el nido vacío.
        ///
        /// QUÉ MIDE. Para cada colonia y cada bajada del embudo por debajo del
        /// techo (una eclosión, una racha de muertes), cuánto tarda en volver a
        /// `NurseCeilingModel.Hard`. Exige tres cosas: que la colonia llegue al
        /// tope (si no, no hay régimen que medir), que la cola se VACÍE al menos
        /// tres veces —el lazo gira, la foto no es la del arranque— y que cada
        /// bajada vuelva dentro de `VentanaDeReposicion`, contando la que quede
        /// abierta al cerrar la ventana (si llevara más de 300 s abierta, también
        /// falla: no cerró).
        ///
        /// Las dos semillas son la de referencia y la 11, la que se apagaba
        /// (0/0 adultas) antes del arreglo del embudo: si la reposición fuera un
        /// artefacto de una partida con suerte, aquí se vería.
        /// </summary>
        [Fact]
        public void Mundo_ReponeElEmbudoHastaElTecho_CadaVezQueSeVacia()
        {
            string fixture = RepoFixture("warm-v2.antgenome");
            Assert.True(File.Exists(fixture), $"fixture ausente: {fixture}");

            foreach (ulong seed in new ulong[] { 42, 11 })
            {
                const int ticks = 60000;
                string stream = GameScenario.Run(seed, ticks: ticks, colonies: 2, grid: 96,
                    frameEvery: 30, seedPoolPath: fixture, drops: null,
                    maze: true, recruitment: true,
                    species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.Eciton });

                var series = new Dictionary<int, List<(ulong Tick, int Pupae)>>();
                var parser = new GameStreamParser();

                foreach (string raw in stream.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var v = parser.ParseLine(raw.Trim());
                    if (v == null) continue;

                    foreach (var c in v.Colonies)
                    {
                        if (!series.TryGetValue(c.Id, out var rows))
                            series[c.Id] = rows = new List<(ulong, int)>();
                        rows.Add((v.Tick, c.Pupae));
                    }
                }

                Assert.True(series.Count > 0, $"semilla {seed}: el stream no trajo colonias");

                foreach (var kv in series)
                {
                    int id = kv.Key;
                    var rows = kv.Value;
                    int techo = NurseCeilingModel.Hard;

                    int i0 = rows.FindIndex(r => r.Pupae >= techo);
                    Assert.True(i0 >= 0,
                        $"semilla {seed}, colonia {id}: el embudo nunca llegó al techo {techo} " +
                        $"(pico {rows.Max(r => r.Pupae)}) — no hay reposición que medir");

                    int bajadas = 0;
                    int j = i0;
                    while (j < rows.Count)
                    {
                        if (rows[j].Pupae >= techo) { j++; continue; }

                        int inicio = j;
                        while (j < rows.Count && rows[j].Pupae < techo) j++;

                        // Si la bajada cierra, el último punto por debajo es j-1;
                        // si no (la ventana termina antes), es el final del stream.
                        int fin = j < rows.Count ? j - 1 : rows.Count - 1;
                        ulong t0 = rows[inicio].Tick;
                        ulong dur = rows[fin].Tick - t0;
                        int fondo = rows.GetRange(inicio, fin - inicio + 1).Min(r => r.Pupae);
                        bajadas++;

                        Assert.True(dur <= VentanaDeReposicion,
                            $"semilla {seed}, colonia {id}: el embudo bajó a {fondo} en el tick {t0} y " +
                            $"{(j < rows.Count ? "tardó " + dur + " ticks en volver" : "seguía sin volver " + dur + " ticks después")} " +
                            $"al techo {techo} (ventana {VentanaDeReposicion}) — la puesta no reanudó");
                    }

                    Assert.True(bajadas >= 3,
                        $"semilla {seed}, colonia {id}: el embudo se vació {bajadas} vez/veces tras llenarse; " +
                        "con menos de 3 el techo se alcanzó una vez y no hay régimen que diga que se repone");
                }
            }
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
