using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.2 — layout del HUD (uGUI) verificado headless con los modelos PUROS:
    /// tarjeta de colonia (semáforo del canal D, reserva, cerebros), pila de
    /// toasts (dedupe por clave, expiración, tope) y el canal D completo sobre
    /// un stream REAL del Core. Si el contrato del stream cambia, estos tests
    /// rompen ANTES de que lo haga la UI de Unity.
    /// </summary>
    public sealed class HudLayoutTests
    {
        private static List<GameStreamParser.TickView> Parse(string stream)
        {
            var parser = new GameStreamParser();
            var ticks = new List<GameStreamParser.TickView>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) ticks.Add(v);
            }
            return ticks;
        }

        // ————— canal D: el Core emite alertas y semáforo en el stream —————

        [Fact]
        public void CanalD_ElStreamTraeAlertasYSemaforoPorColonia()
        {
            // Partida real con muerte de colonia (semilla 42, 1200 ticks trae
            // mortalidad/puesta parada en grid 96): el canal D debe aparecer con
            // el esquema {k, lvl, col, x, y, t, txt} y el semáforo [col, byte].
            string stream = GameScenario.Run(42, ticks: 5200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);

            bool sawAlert = false, sawLight = false;
            foreach (var v in Parse(stream))
            {
                foreach (var a in v.Alerts)
                {
                    sawAlert = true;
                    Assert.False(string.IsNullOrEmpty(a.Key));
                    Assert.False(string.IsNullOrEmpty(a.Text));
                    Assert.InRange(a.Level, 0, 3);
                    Assert.True(a.ColonyId >= -1);
                }
                foreach (var l in v.Lights)
                {
                    sawLight = true;
                    Assert.InRange(l.Light, 0, 2); // gris/ámbar/verde de RelayVerdict
                }
            }
            Assert.True(sawLight, "el semáforo por colonia debe emitirse cada 120 ticks");
            Assert.True(sawAlert, "la partida canónica produce alertas (puesta parada/mortalidad)");
        }

        [Fact]
        public void CanalD_AlertaPorColonia_LlevaAnclaDeCamara()
        {
            // Las alertas con colonia (p. ej. laying:0) anclan al NIDO de ESA
            // colonia — el salto de cámara del toast cae sobre la colonia culpable.
            string stream = GameScenario.Run(42, ticks: 5200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var parser = new GameStreamParser();

            GameStreamParser.ColonyView? col0 = null;
            GameStreamParser.AlertView? laying0 = null;
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                foreach (var c in v.Colonies)
                    if (c.Id == 0) col0 = c;
                foreach (var a in v.Alerts)
                    if (a.Key == "laying:0" && laying0 is null) laying0 = a;
                if (laying0 is not null && col0 is not null) break;
            }

            var a0 = laying0;
            var c0 = col0;
            Assert.True(a0.HasValue, "la partida emite laying:0");
            Assert.True(c0.HasValue, "la partida emite canal A");
            Assert.Equal(c0.Value.NestX, a0.Value.X);
            Assert.Equal(c0.Value.NestY, a0.Value.Y);
            Assert.Equal(0, a0.Value.ColonyId);
        }

        // ————— tarjeta de colonia —————

        [Fact]
        public void TarjetaColonia_MuestraSemaforoReservaYCerebros()
        {
            string stream = GameScenario.Run(42, ticks: 5200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var model = new ColonyCardModel();
            foreach (var v in Parse(stream)) model.Observe(v);

            string? card = model.Render(0);
            Assert.NotNull(card);
            Assert.Contains("colonia 0", card);
            Assert.Contains("/40", card);                    // tope duro de adultas
            Assert.Contains("reserva [", card);              // barra
            Assert.Contains("cerebros:", card);              // línea permanente
            // El semáforo llega del canal D (aquí gris: sin relevo en 5200 ticks).
            Assert.Contains(ColonyCardModel.LightGlyph(0), card);
        }

        [Fact]
        public void TarjetaColonia_ReservaBaja_MarcaYFlujoDeVentana()
        {
            // Sintético con el contrato REAL del parser: reserva 8/100 (<20%) y
            // una ventana de 1 s con muertes — la tarjeta marca y muestra el flujo.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(
                colonies: "{\"id\":0,\"nest\":[128,128],\"adults\":12,\"eggs\":4,\"larvae\":2,\"pupae\":1,\"stock\":8,\"stockMax\":100,\"elite\":3}",
                colmetrics: "[0,2,1,0,3,1,0]"));
            model.Observe(TickFrom(events: "[9,0,0,0,0,0],[10,0,0,0,0,0],[9,0,0,0,0,0]"));

            string card = model.Render(0)!;
            Assert.Contains("¡RESERVA BAJA!", card);
            Assert.Contains("−3 mue", card);                 // muertes de la ventana
            Assert.Contains("cerebros: 2 élite · 1 descartados", card);
            Assert.Contains("🥚 4 · 🐛 2 · 🛑 1", card);      // chips de cría
        }

        [Fact]
        public void TarjetaColonia_SemáforoSigueAlCanalD_NoSeCalculaEnLaUI()
        {
            // La tarjeta PINTA el byte que llega: [0,2] ⇒ verde, [0,1] ⇒ ámbar,
            // sin datos ⇒ gris. Ninguna aritmética de umbrales en la UI.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0, light: "[0,2]"));
            Assert.Contains("🟢", model.Render(0));
            model.Observe(TickFrom(light: "[0,1]"));
            Assert.Contains("🟡", model.Render(0));
            var fresh = new ColonyCardModel();
            fresh.Observe(TickFrom(colonies: Colony0));
            Assert.Contains("🔘", fresh.Render(0));
        }

        private const string Colony0 =
            "{\"id\":0,\"nest\":[128,128],\"adults\":10,\"eggs\":0,\"larvae\":0,\"pupae\":0,\"stock\":50,\"stockMax\":100,\"elite\":0}";

        // ————— toasts —————

        [Fact]
        public void Toasts_DedupePorClave_TopeYExpiracion()
        {
            var toasts = new HudToastsModel(lifetime: 2f, maxStack: 3);
            toasts.Push(Toast("k1", "primera", tick: 100));
            toasts.Push(Toast("k2", "segunda", tick: 110));
            toasts.Push(Toast("k1", "primera-refrescada", tick: 120)); // reemplaza, no duplica
            Assert.Equal(2, toasts.Active.Count);
            Assert.Equal("primera-refrescada", toasts.Active[0].Text); // la nueva arriba

            toasts.Push(Toast("k3", "tercera", tick: 130));
            toasts.Push(Toast("k4", "cuarta", tick: 140));             // tope 3: sale k2
            Assert.Equal(3, toasts.Active.Count);
            Assert.DoesNotContain(toasts.Active, t => t.Key == "k2");

            toasts.Tick(2f); // expiran todas (lifetime 2)
            Assert.Empty(toasts.Active);
            // Y la clave liberada puede volver a usarse.
            toasts.Push(Toast("k2", "regresada", tick: 200));
            Assert.Single(toasts.Active);
        }

        [Fact]
        public void Toasts_DelStream_Real_ConNivelYColonia()
        {
            string stream = GameScenario.Run(42, ticks: 5200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var toasts = new HudToastsModel();
            foreach (var v in Parse(stream)) toasts.Observe(v);

            Assert.NotEmpty(toasts.Active); // la partida canónica emite alertas
            foreach (var t in toasts.Active)
            {
                Assert.InRange(t.Level, 0, 3);
                Assert.False(string.IsNullOrEmpty(t.Text));
            }
            // Y las líneas renderizadas llevan el tag de colonia cuando aplica
            // (StartsWith, no regex: los glifos emoji son pares sustitutos).
            foreach (var line in toasts.RenderLines())
            {
                Assert.True(
                    line.StartsWith("·") || line.StartsWith("🟡") ||
                    line.StartsWith("🟢") || line.StartsWith("🔴"),
                    "la línea debe empezar por su glifo de nivel: " + line);
            }
        }

        [Fact]
        public void Toasts_LaPausaNoLosConsume_SegundosDeSim()
        {
            var toasts = new HudToastsModel(lifetime: 1f, maxStack: 4);
            toasts.Push(Toast("k", "algo", tick: 10));
            toasts.Tick(0.9f);
            toasts.Tick(0.9f); // 1.8 s de sim, PERO en dos pasos: expira al cruzar 1
            Assert.Empty(toasts.Active);

            toasts.Push(Toast("k2", "pausada", tick: 20));
            toasts.Tick(0f);  // pausa: dt=0 ⇒ nada envejece
            Assert.Single(toasts.Active);
        }

        // ————— helpers: TickViews sintéticos con el contrato real del parser —————

        private static GameStreamParser.TickView TickFrom(
            string colonies = "", string colmetrics = "", string events = "", string light = "")
        {
            var sb = new System.Text.StringBuilder("{\"tick\":1");
            if (colonies.Length > 0) sb.Append(",\"colonies\":[").Append(colonies).Append(']');
            if (colmetrics.Length > 0) sb.Append(",\"colmetrics\":[").Append(colmetrics).Append(']');
            if (events.Length > 0) sb.Append(",\"events\":[").Append(events).Append(']');
            if (light.Length > 0) sb.Append(",\"light\":[").Append(light).Append(']');
            sb.Append('}');
            var v = new GameStreamParser().ParseLine(sb.ToString());
            Assert.NotNull(v);
            return v!;
        }

        private static HudToastsModel.Toast Toast(string key, string text, ulong tick)
            => new(new GameStreamParser.AlertView(key, level: 1, colonyId: 0,
                x: -1f, y: -1f, tick: tick, text: text));
    }
}
