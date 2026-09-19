using System.Collections.Generic;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.3 — la 6ª pieza del criterio de cierre: el coste del SISTEMA COMPLETO. El
    /// player lee el stream y dibuja, pero el mundo lo simula el CLI en OTRO proceso,
    /// y ese trabajo no aparece en ningún tiempo de frame del player: publicar solo
    /// su frame deja fuera la mayor parte del trabajo que la máquina hace para que
    /// ese frame exista.
    ///
    /// Aquí se prueba el MODELO PURO, que es lo que se publica. Lo que un resumen
    /// descuidado haría mal, y que estos tests cazan:
    ///   · la pata del CLI NO se puede emparejar por ventana: su CPU se gasta antes
    ///     de que los ticks lleguen al player (el stream sale en ráfaga), así que el
    ///     precio del tick es el agregado de su vida entera — emparejando por ventana
    ///     salía 0,016 ms/tick en vez de 0,2, y esto se midió antes de decidirlo;
    ///   · un frame de tablero TODAVÍA VACÍO no es un frame del juego: la pata del
    ///     player sale solo de las ventanas con el mundo en pantalla;
    ///   · si el CLI no terminó su horizonte, su CPU es parcial y el precio no vale;
    ///   · sin alguna de las dos patas esto NO es el sistema, es un player (o un CLI)
    ///     con otro nombre, y aun así cabría en el presupuesto: el falso verde.
    /// </summary>
    public class SystemCostTests
    {
        private const double Budget60 = 1000.0 / 60.0;   // 16,667 ms

        /// <summary>Ventana de SIMULACIÓN: el CLI gastando CPU y el tablero todavía
        /// sin mundo (el player dibuja un tablero vacío, 0,4 ms/frame).</summary>
        private static SystemWindow Sim(double seconds, long frames, double cliCpuMs, long ticks)
            => new(seconds, frames, 0.4, cliCpuMs, ticks, worldOnScreen: false);

        /// <summary>Ventana con el MUNDO EN PANTALLA: el CLI ya terminó y el player
        /// reproduce el mundo cargado.</summary>
        private static SystemWindow Loaded(double seconds, long frames, double playerMs)
            => new(seconds, frames, playerMs, 0.0, 0, worldOnScreen: true);

        /// <summary>Las dos fases, como son en esta arquitectura: primero el CLI
        /// simula el horizonte entero (y el player no tiene nada que dibujar), y
        /// después el player reproduce con el mundo cargado (y el CLI parado).</summary>
        private static List<SystemWindow> DosFases() => new()
        {
            Sim(1.0, 100, 300.0, 1500),      // el CPU del CLI se gasta aquí...
            Sim(1.0, 100, 250.0, 1400),
            Sim(1.0, 100, 50.0, 100),
            Loaded(1.0, 100, 2.0),
            Loaded(1.0, 100, 2.4),
            Loaded(1.0, 100, 2.2),
        };

        /// <summary>La pata del mundo, medida como se mide de verdad: toda la CPU del
        /// CLI (600 ms) entre todos los ticks que simuló (3000).</summary>
        private static SystemCostSummary Resumen(List<SystemWindow> w, double ticksPerFrame = 200.0)
            => FrameCost.SummarizeSystem(w, cliCpuMsTotal: 600.0, cliTicksTotal: 3000,
                ticksPerPresentedFrame: ticksPerFrame, cliCompleted: true);

        [Fact]
        public void Sistema_CadaPataSaleDeSuSitio_YElNumeroLasSuma()
        {
            var s = Resumen(DosFases());

            Assert.Equal(6, s.Windows);
            Assert.Equal(3, s.SimulatedWindows);
            Assert.Equal(3, s.LoadedWindows);
            Assert.True(s.Measured);

            Assert.Equal(2.2, s.PlayerMsPerFrame, 6);        // mediana de 2.0/2.4/2.2
            Assert.Equal(0.2, s.CliMsPerTick, 6);            // 600 ms / 3000 ticks
            Assert.Equal(40.0, s.CliMsPerFrame, 6);          // 0,2 × 200 ticks/frame
            Assert.Equal(42.2, s.SystemMsPerFrame, 6);       // 2,2 + 40

            Assert.Equal(2.2 / Budget60, s.PlayerFractionOfBudget, 6);
            Assert.Equal(Budget60 / 2.2, s.PlayerHeadroom, 6);
            Assert.True(s.PlayerFits);
            Assert.Equal(42.2 / Budget60, s.SystemCores, 6);   // ≈ 2,53 núcleos
            Assert.Equal(40.0 / 42.2, s.CliShareOfSystem, 6);
            Assert.Equal(300, s.Frames);
            Assert.Equal(3000, s.CliTicks);
        }

        [Fact]
        public void Sistema_ElPrecioDelTick_EsAgregado_NoLaMedianaDeVentanas()
        {
            // El caso real: la CPU del CLI se gasta ANTES de que los ticks lleguen.
            // Una ventana con casi todo el CPU y pocos ticks (ratio 5 ms/tick) y otra
            // con casi todos los ticks y poco CPU (0,034). La mediana —o la media— de
            // los ratios no dice nada del precio; el agregado sí: 600/3000 = 0,2.
            var w = new List<SystemWindow>
            {
                Sim(1.0, 100, 500.0, 100),
                Sim(1.0, 100, 100.0, 2900),
                Loaded(1.0, 100, 2.0),
                Loaded(1.0, 100, 2.2),
                Loaded(1.0, 100, 2.4),
            };
            var s = FrameCost.SummarizeSystem(w, cliCpuMsTotal: 600.0, cliTicksTotal: 3000,
                ticksPerPresentedFrame: 200, cliCompleted: true);

            Assert.Equal(0.2, s.CliMsPerTick, 6);
            Assert.NotEqual(5.0, s.CliMsPerTick, 6);
            Assert.NotEqual(0.034, s.CliMsPerTick, 3);
        }

        [Fact]
        public void Sistema_SinCLITerminado_ElPrecioPorTickNoVale()
        {
            // Con la mitad del mundo simulado, su CPU es parcial: dividirla por sus
            // ticks daría un precio falso.
            var s = FrameCost.SummarizeSystem(DosFases(), cliCpuMsTotal: 300.0,
                cliTicksTotal: 1500, ticksPerPresentedFrame: 200, cliCompleted: false);

            Assert.False(s.Measured);
            Assert.Contains("no termino su horizonte", s.Verdict);
        }

        [Fact]
        public void Sistema_SinVentanaConMundo_NoSePublicaElTableroVacio()
        {
            // El CLI simuló (su pata está medida) pero el mundo no llegó a la
            // pantalla: el coste de la vista sería el de un tablero vacío, que no es
            // el juego.
            var w = new List<SystemWindow>
            {
                Sim(1.0, 100, 50.0, 1000),
                Sim(1.0, 100, 50.0, 1000),
                Sim(1.0, 100, 50.0, 1000),
            };
            var s = Resumen(w);

            Assert.Equal(0, s.LoadedWindows);
            Assert.False(s.Measured);
            Assert.Equal(0.0, s.PlayerMsPerFrame, 6);
            Assert.Contains("no llego a estar en pantalla", s.Verdict);
        }

        [Fact]
        public void Sistema_MenosDelMinimoDeVentanas_NoEsUnaMediana()
        {
            // Dos ventanas cargadas y tres de simulación: la mediana de dos muestras
            // no es una mediana, así que no se declara medido (y la sonda suspende).
            var w = new List<SystemWindow>
            {
                Sim(1.0, 100, 300.0, 1500),
                Sim(1.0, 100, 250.0, 1400),
                Sim(1.0, 100, 50.0, 100),
                Loaded(1.0, 100, 2.0),
                Loaded(1.0, 100, 2.4),
            };
            var s = Resumen(w);

            Assert.Equal(2, s.LoadedWindows);
            Assert.False(s.Measured);
            Assert.Equal(2.2, s.PlayerMsPerFrame, 6);        // la pata sí se mide
        }

        [Fact]
        public void Sistema_SinPataDeCli_NoPublicaAlPlayerComoSistema()
        {
            // El caso peligroso: el player medido y «cabría» en el presupuesto. Sin la
            // pata del CLI eso es un player solo, no el sistema.
            var w = new List<SystemWindow>
            {
                Loaded(1.0, 100, 1.2),
                Loaded(1.0, 100, 1.3),
                Loaded(1.0, 100, 1.25),
            };
            var s = FrameCost.SummarizeSystem(w, cliCpuMsTotal: 0.0, cliTicksTotal: 0,
                ticksPerPresentedFrame: 200, cliCompleted: true);

            Assert.False(s.Measured);
            Assert.Contains("sin CPU del CLI", s.Verdict);
        }

        [Fact]
        public void Sistema_SinVentanas_NoSeDeclaraMedido()
        {
            var s = FrameCost.SummarizeSystem(new List<SystemWindow>(), 600.0, 3000, 200, true);

            Assert.False(s.Measured);
            Assert.Contains("sin ventanas", s.Verdict);
        }

        [Fact]
        public void Sistema_PataDelPlayerPorEncimaDelPresupuesto_Suspende()
        {
            var w = new List<SystemWindow>
            {
                Loaded(1.0, 100, 19.0),
                Loaded(1.0, 100, 20.0),
                Loaded(1.0, 100, 21.0),
            };
            var s = Resumen(w);

            Assert.True(s.Measured);          // todo medido...
            Assert.False(s.PlayerFits);       // ...pero la vista no cabe
            Assert.Contains("la pata del player no cabe", s.Verdict);
        }

        [Fact]
        public void Sistema_AgregadoDeLaCorrida_MezclaLasDosFases()
        {
            // El agregado SÍ incluye las ventanas de tablero vacío: es «todo el CPU de
            // la máquina entre todos los frames producidos», y depende de cuánto
            // durase cada fase — por eso es el informativo, no el número de la pieza.
            var s = Resumen(DosFases());

            // (0,4+0,4+0,4)×100 de player + 600 de CLI + (2,0+2,4+2,2)×100 = 1380 ms / 600 frames
            Assert.Equal(2.3, s.AggregateMsPerFrame, 6);
        }

        [Fact]
        public void Sistema_Describe_LlevaLasDosPatasYLasFases()
        {
            string line = FrameCost.DescribeSystem(Resumen(DosFases()));

            Assert.Contains("coste/SISTEMA", line);
            Assert.Contains("player(cargado)=2.2 ms/frame", line);
            Assert.Contains("mundo=40 ms/frame", line);
            Assert.Contains("= 42.2 ms/frame agregados", line);
            Assert.Contains("nucleos al reloj del juego", line);
            Assert.Contains("0 de solape", line);
            Assert.Contains("CLI terminado", line);
            // Una sola línea y sin tabuladores: la imprimen la sonda y los scripts.
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\t', line);
        }
    }
}
