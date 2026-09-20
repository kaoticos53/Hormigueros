using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — chip de estado por RUNWAY (contrato HUD §2). La escalera de escasez
    /// como código: los umbrales son los de la ESPECIE
    /// (<c>SpeciesDescriptor.TCrit/TCann/TOoph/TSafe</c>), los MISMOS que
    /// ColonyController usa para reabsorber huevos, comerse larvas y racionar
    /// adultas. El chip no decide nada: dice en qué tramo está la colonia.
    /// </summary>
    public sealed class RunwayVerdictTests
    {
        // ————— la escalera, por especie (tabla del contrato) —————

        [Fact]
        public void Umbrales_Lasius_40_25_15_8()
        {
            var sp = SpeciesDescriptor.LasiusNiger;
            Assert.Equal(40f, sp.TSafe);
            Assert.Equal(25f, sp.TOoph);
            Assert.Equal(15f, sp.TCann);
            Assert.Equal(8f, sp.TCrit);
            EscaleraCompleta(sp);
        }

        [Fact]
        public void Umbrales_Atta_50_30_18_10()
        {
            // Economía más cara (hongo que digerir): la escalera se mueve con ella.
            var sp = SpeciesDescriptor.Atta;
            Assert.Equal(50f, sp.TSafe);
            Assert.Equal(30f, sp.TOoph);
            Assert.Equal(18f, sp.TCann);
            Assert.Equal(10f, sp.TCrit);
            EscaleraCompleta(sp);
        }

        [Fact]
        public void Umbrales_Eciton_30_20_12_6()
        {
            var sp = SpeciesDescriptor.Eciton;
            Assert.Equal(30f, sp.TSafe);
            Assert.Equal(20f, sp.TOoph);
            Assert.Equal(12f, sp.TCann);
            Assert.Equal(6f, sp.TCrit);
            EscaleraCompleta(sp);
        }

        /// <summary>Frontera por frontera: el intervalo es [umbral, siguiente).</summary>
        private static void EscaleraCompleta(SpeciesDescriptor sp)
        {
            Assert.Equal(RunwayState.Critical, RunwayVerdict.StateOf(0f, sp));
            Assert.Equal(RunwayState.Critical, RunwayVerdict.StateOf(sp.TCrit - 0.01f, sp));
            Assert.Equal(RunwayState.Cannibalism, RunwayVerdict.StateOf(sp.TCrit, sp));
            Assert.Equal(RunwayState.Cannibalism, RunwayVerdict.StateOf(sp.TCann - 0.01f, sp));
            Assert.Equal(RunwayState.Oophagy, RunwayVerdict.StateOf(sp.TCann, sp));
            Assert.Equal(RunwayState.Oophagy, RunwayVerdict.StateOf(sp.TOoph - 0.01f, sp));
            Assert.Equal(RunwayState.Reserve, RunwayVerdict.StateOf(sp.TOoph, sp));
            Assert.Equal(RunwayState.Reserve, RunwayVerdict.StateOf(sp.TSafe - 0.01f, sp));
            Assert.Equal(RunwayState.Surplus, RunwayVerdict.StateOf(sp.TSafe, sp));
        }

        [Fact]
        public void MismaAutonomia_TramoDistintoPorEspecie()
        {
            // 9 s: Lasius ya raciona adultas (TCrit 8) mientras Atta todavía solo
            // raciona (TCrit 10) y Eciton la trata como caníbal incipiente.
            Assert.Equal(RunwayState.Cannibalism, RunwayVerdict.StateOf(9f, SpeciesDescriptor.LasiusNiger));
            Assert.Equal(RunwayState.Critical, RunwayVerdict.StateOf(9f, SpeciesDescriptor.Atta));
            Assert.Equal(RunwayState.Cannibalism, RunwayVerdict.StateOf(9f, SpeciesDescriptor.Eciton));

            // 12 s: Eciton ya pasó su TCann (12) y sube a oofagia.
            Assert.Equal(RunwayState.Cannibalism, RunwayVerdict.StateOf(12f, SpeciesDescriptor.LasiusNiger));
            Assert.Equal(RunwayState.Cannibalism, RunwayVerdict.StateOf(12f, SpeciesDescriptor.Atta));
            Assert.Equal(RunwayState.Oophagy, RunwayVerdict.StateOf(12f, SpeciesDescriptor.Eciton));
        }

        [Fact]
        public void CeroEsCritico_PeroMenosUnoYNaNNoSonMedida()
        {
            // Un runway real de 0 s es el caso MÁS grave, no ausencia de dato.
            Assert.Equal(RunwayState.Critical, RunwayVerdict.StateOf(0f, SpeciesDescriptor.LasiusNiger));
            // Los centinelas del canal A (sin medida) NO se pintan como crítico:
            // serían un chip inventado.
            Assert.Equal(RunwayState.Unknown, RunwayVerdict.StateOf(-1f, SpeciesDescriptor.LasiusNiger));
            Assert.Equal(RunwayState.Unknown, RunwayVerdict.StateOf(float.NaN, SpeciesDescriptor.LasiusNiger));
        }

        [Fact]
        public void HasMeasure_SoloConEmaViva()
        {
            Assert.False(RunwayVerdict.HasMeasure(0f));
            Assert.False(RunwayVerdict.HasMeasure(-1f));
            Assert.False(RunwayVerdict.HasMeasure(float.NaN));
            Assert.True(RunwayVerdict.HasMeasure(1f));      // valor de nacimiento
            Assert.True(RunwayVerdict.HasMeasure(0.0001f)); // consumo mínimo real
        }

        [Fact]
        public void Escalera_Monotona_EnLasTresEspecies()
        {
            foreach (var sp in new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.Atta, SpeciesDescriptor.Eciton })
            {
                var previo = RunwayState.Unknown;
                for (float t = 0f; t <= 70f; t += 0.5f)
                {
                    var st = RunwayVerdict.StateOf(t, sp);
                    Assert.True((byte)st >= (byte)previo,
                        $"{sp.Name}: {t}s dio {st} después de {previo} — escalón invertido");
                    previo = st;
                }
            }
        }

        // ————— canal A: el tramo viaja calculado, la UI no lo inventa —————

        [Fact]
        public void CanalA_EmiteAutonomiaYNivelCadaFrame_YLaEscaleraSeRecorre()
        {
            // Partida REAL de 2 colonias Lasius (mismo escenario que el smoke del
            // contrato). La línea del tick sale SIEMPRE y el canal A cada
            // frameEvery: 12000 líneas, 400 con colonias (800 muestras de colonia).
            // Medido en esta semilla: reparto de tramos 561 desahogo / 26 reserva /
            // 27 oofagia / 17 caníbal / 169 crítico; primer no-desahogo en el tick
            // 8280 y primer crítico (7,62 s) en el 9330, con runway mínimo 0 s en
            // el 9540.
            string stream = GameScenario.Run(42, ticks: 12000, colonies: 2, grid: 96,
                frameEvery: 30);

            var parser = new GameStreamParser();
            int lineas = 0, frames = 0, muestras = 0, sinMedida = 0, violaciones = 0;
            bool sawCritical = false, sawSurplus = false;
            var tramos = new HashSet<RunwayState>();
            float primerRunway = float.NaN;
            byte primerNivel = 0;

            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                lineas++;
                if (v.Colonies.Count == 0) continue;   // tick sin canal A
                frames++;
                foreach (var c in v.Colonies)
                {
                    muestras++;
                    if (frames == 1 && c.Id == 0) { primerRunway = c.Runway; primerNivel = c.RunwayLevel; }

                    // Bicondicional: nivel 0 ⟺ sin medida. Ni un frame se pinta
                    // «desahogo» por falta de dato.
                    bool hayMedida = c.Runway >= 0f;
                    if (hayMedida == (c.RunwayLevel == 0)) violaciones++;
                    if (!hayMedida) { sinMedida++; continue; }

                    // El tramo emitido tiene que ser el de SU escalera, recalculado
                    // aquí desde el runway IMPRESO: si el Core etiquetara con los
                    // umbrales de otra colonia o de otra especie, esto lo caza.
                    var esperado = (byte)RunwayVerdict.StateOf(c.Runway, SpeciesDescriptor.LasiusNiger);
                    if (c.RunwayLevel != esperado) violaciones++;

                    var tramo = (RunwayState)c.RunwayLevel;
                    tramos.Add(tramo);
                    if (tramo == RunwayState.Critical) sawCritical = true;
                    if (tramo == RunwayState.Surplus) sawSurplus = true;
                }
            }

            Assert.Equal(12000, lineas);      // el tick siempre sale (contrato F4.4)
            Assert.Equal(400, frames);        // 12000 ticks / frameEvery 30
            Assert.Equal(800, muestras);      // 2 colonias
            Assert.Equal(0, violaciones);
            Assert.Equal(0, sinMedida);       // la colonia nace con ConsumeEma = 1

            // El chip no es decoración: en una partida normal la colonia BAJA la
            // escalera entera. Si esto deja de ocurrir, el chip sobra o la
            // economía cambió de forma — las dos cosas hay que saberlas.
            Assert.True(sawCritical, "la escalera nunca llegó a crítico en 12000 ticks");
            Assert.True(sawSurplus, "la colonia nunca tuvo desahogo");
            Assert.True(tramos.Count >= 3, $"solo se vieron {tramos.Count} tramos");

            // El primer frame de canal A (tick 30) ya tiene medida — 143,48 s
            // medidos en esta semilla — y sale desahogo con la despensa llena: el
            // chip dice la verdad desde el arranque, sin inventar un «crítico».
            Assert.Equal(RunwayState.Surplus, (RunwayState)primerNivel);
            Assert.True(primerRunway > SpeciesDescriptor.LasiusNiger.TSafe,
                $"primer runway {primerRunway} por debajo de T_safe");
        }

        [Fact]
        public void CanalA_PorColonia_ConLaEscaleraDeSuEspecie()
        {
            // Dos especies en el mismo mundo: cada colonia lleva su escalera. El
            // primer frame delata cuál es cuál, porque las dos nacen con la despensa
            // llena a su StockMax y su consumo no es el mismo: medido en el tick 30,
            // Lasius 177,64 s (Stock 149,86) y Atta 235,35 s (Stock 199,83). Y el
            // tramo emitido tiene que cuadrar con la escalera de SU especie — no con
            // la de la colonia de al lado, que es el bug que este test caza.
            string stream = GameScenario.Run(7, ticks: 1200, colonies: 2, grid: 96,
                frameEvery: 30,
                species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.Atta });

            var parser = new GameStreamParser();
            var sp = new Dictionary<int, SpeciesDescriptor>
            {
                { 0, SpeciesDescriptor.LasiusNiger },
                { 1, SpeciesDescriptor.Atta },
            };
            var primerRunway = new Dictionary<int, float>();
            int frames = 0, muestras = 0, violaciones = 0;

            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                if (v.Colonies.Count == 0) continue;   // tick sin canal A
                frames++;
                foreach (var c in v.Colonies)
                {
                    muestras++;
                    if (!primerRunway.ContainsKey(c.Id)) primerRunway[c.Id] = c.Runway;
                    if (!sp.TryGetValue(c.Id, out var suya)) { violaciones++; continue; }
                    if (c.Runway < 0f) continue;
                    if (c.RunwayLevel != (byte)RunwayVerdict.StateOf(c.Runway, suya)) violaciones++;
                }
            }

            Assert.Equal(0, violaciones);
            Assert.Equal(40, frames);     // 1200 ticks / frameEvery 30
            Assert.Equal(80, muestras);   // 40 frames × 2 colonias
            Assert.True(primerRunway.ContainsKey(1), "la colonia 1 (Atta) no aparece en el stream");
            Assert.InRange(primerRunway[0], 165f, 190f);  // StockMax 150
            Assert.InRange(primerRunway[1], 220f, 250f);  // StockMax 200
        }
    }
}
