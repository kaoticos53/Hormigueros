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
            // F5.1: la línea de la ventana cabe en la tarjeta (440 px a fs 14).
            foreach (var line in card.Split('\n'))
                Assert.True(line.Length <= 60, "línea demasiado larga para la tarjeta: " + line);
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
            Assert.Contains("huevos 4", card);              // cría en texto (F5.1: sin emoji)
            Assert.Contains("larvas 2", card);
            Assert.Contains("pupas 1", card);
            Assert.Contains("<b>colonia 0", card);          // título con jerarquía (rich text)
        }

        [Fact]
        public void TarjetaColonia_SemáforoSigueAlCanalD_NoSeCalculaEnLaUI()
        {
            // La tarjeta PINTA el byte que llega: [0,2] ⇒ verde, [0,1] ⇒ ámbar,
            // sin datos ⇒ gris. Ninguna aritmética de umbrales en la UI.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0, light: "[0,2]"));
            Assert.Contains(ColonyCardModel.LightGlyph(2), model.Render(0));
            model.Observe(TickFrom(light: "[0,1]"));
            Assert.Contains(ColonyCardModel.LightGlyph(1), model.Render(0));
            var fresh = new ColonyCardModel();
            fresh.Observe(TickFrom(colonies: Colony0));
            Assert.Contains(ColonyCardModel.LightGlyph(0), fresh.Render(0));
        }

        private const string Colony0 =
            "{\"id\":0,\"nest\":[128,128],\"adults\":10,\"eggs\":0,\"larvae\":0,\"pupae\":0,\"stock\":50,\"stockMax\":100,\"elite\":0}";

        [Fact]
        public void BarraDeEstado_ResumeTickVelocidadYColonias()
        {
            // F5.1: el renglón superior sale del modelo puro (tick + reloj de
            // reproducción + semáforo/adultas/reserva por colonia). Antes la barra
            // no existía y la UI no tenía dónde decir en qué tick iba la partida.
            var model = new ColonyCardModel();
            Assert.Null(model.StatusLine(1, 1f));               // aún sin canal A

            model.Observe(TickFrom(colonies: Colony0, light: "[0,2]"));
            string line = model.StatusLine(3950, 3f)!;
            Assert.Contains("tick 3950", line);
            Assert.Contains("v×3", line);
            Assert.Contains("colonia", line);
            Assert.Contains(ColonyCardModel.LightGlyph(2), line);
            Assert.Contains("50%", line);                       // reserva 50/100

            // En pausa el reloj lo dice con palabras (no «v×0», que parece un error).
            Assert.Contains("EN PAUSA", model.StatusLine(3950, 0f));
        }

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
            // Y las líneas renderizadas llevan el tag de colonia cuando aplica,
            // con el glifo de nivel por delante (F5.1: glifos de FORMA — ◐ ● ■ · —
            // porque la fuente por defecto de uGUI no tiene emoji y salían cajas).
            var glyphs = new[] { HudToastsModel.Glyph(0), HudToastsModel.Glyph(1),
                HudToastsModel.Glyph(2), HudToastsModel.Glyph(3) };
            foreach (var line in toasts.RenderLines())
            {
                bool hasGlyph = line.StartsWith("·");
                foreach (var g in glyphs) if (line.StartsWith(g)) hasGlyph = true;
                Assert.True(hasGlyph,
                    "la línea debe empezar por su glifo de nivel: " + line);
            }
        }

        // ————— historial de comandos (contrato HUD §4) —————

        [Fact]
        public void HistorialComandos_RegistraDropFoodYSaveGame()
        {
            var model = new CommandHistoryModel();
            // kind 11 = CommandExecuted; AntId trae el kind del comando (0 DropFood,
            // 1 SaveGame — contrato del canal B), X/Y las coordenadas.
            model.Observe(TickFrom(events: "[11,0,0,42,17,0]"));   // DropFood @ (42,17)
            model.Observe(TickFrom(events: "[11,0,1,0,0,0]", tickIsIrrelevant: false));

            Assert.Equal(2, model.TotalCommands);
            Assert.True(model.CanReplayFromSave);
            string panel = model.Render();
            Assert.Contains("t=1 — DropFood @ (42, 17)", panel);
            Assert.Contains("SaveGame → slot", panel);
            Assert.Contains("✓ guardado", panel);
            Assert.Equal(2, model.Rows.Count);          // la más nueva arriba
            Assert.Equal(1, model.Rows[0].CommandKind); // SaveGame
        }

        [Fact]
        public void HistorialComandos_TopeDeFilas_NoPierdeElTotal()
        {
            var model = new CommandHistoryModel();
            for (ulong t = 1; t <= 250; t++)
                model.Observe(TickFrom(events: "[11,0,0,1,1,0]", tick: t));

            Assert.Equal(250, model.TotalCommands);       // el acumulado no se recorta
            Assert.Equal(CommandHistoryModel.MaxRows, model.Rows.Count);
            Assert.Equal(250UL, model.Rows[0].Tick);      // la más nueva arriba
        }

        [Fact]
        public void HistorialComandos_DelStreamReal()
        {
            // Partida real con comandos inyectados (--drop del contrato F4.0):
            // el panel debe registrarlos todos desde el stream.
            string stream = GameScenario.Run(42, ticks: 300, colonies: 1, grid: 96,
                frameEvery: 30, seedPoolPath: null,
                drops: new[] { (100, 384f, 384f), (150, 300f, 300f) });
            var model = new CommandHistoryModel();
            foreach (var v in Parse(stream)) model.Observe(v);

            Assert.Equal(2, model.TotalCommands);
            Assert.Contains("DropFood @ (384, 384)", model.Render());
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
            string colonies = "", string colmetrics = "", string events = "",
            string light = "", ulong tick = 1, bool tickIsIrrelevant = true)
        {
            var sb = new System.Text.StringBuilder("{\"tick\":").Append(tick);
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

        // ————— puentes de UI: repro de presets y verificación del guardado —————

        [Fact]
        public void Preset_SeedPoolPath_SeExtraeDelReproCanonico()
        {
            // F4.3: la UI siembra el juego con la ruta que el PROPIO preset declara
            // en su repro — sin hardcodear rutas de pools en la capa de presentación.
            var p = new PoolPickerModel.Preset
            {
                Id = "warm-v2",
                ReproCommand = "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm-v2.antgenome",
            };
            Assert.Equal("artifacts/pretrain-warm-v2.antgenome", p.ResolveSeedPoolPath());
            Assert.Equal("artifacts/pretrain-warm-v2.antgenome", p.ResolveSeedPoolPath()); // estable

            var natural = new PoolPickerModel.Preset { Id = "natural", ReproCommand = "antsim --mode game --seed <N>" };
            Assert.Null(natural.ResolveSeedPoolPath()); // sin siembra: partida natural
        }

        [Fact]
        public void HistorialComandos_ComandoVerify_DesdeElModelo()
        {
            // §4: el botón lanza el verify del CLI (el oráculo) — el modelo produce
            // el comando con checkpoint y log; el resultado ✓/✗ lo da su exit code.
            var model = new CommandHistoryModel();
            model.Observe(TickFrom(events: "[11,0,1,0,0,0]")); // SaveGame
            Assert.True(model.CanReplayFromSave);
            Assert.Equal("antsim --mode verify --load partida.antsave --antlog partida.antlog",
                model.BuildVerifyCommand("partida.antsave", "partida.antlog"));
            Assert.Equal("antsim --mode verify --load partida.antsave",
                model.BuildVerifyCommand("partida.antsave"));
        }

        [Fact]
        public void StreamSource_ResuelveElExeDeWindows()
        {
            // El campo CliPath es multiplataforma ("build/antsim"): en Windows con
            // UseShellExecute=false hay que resolver "antsim.exe" — el Play no
            // debe fallar en silencio por un sufijo.
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "antsim-test-" + System.Guid.NewGuid().ToString("N")[..8]);
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string exe = System.IO.Path.Combine(dir, "antsim.exe");
                System.IO.File.WriteAllText(exe, "");
                var src = new AntSim.Unity.Scripts.Streaming.StreamSource(
                    System.IO.Path.Combine(dir, "antsim"));
                Assert.Equal(exe, src.CliPath);
            }
            finally { System.IO.Directory.Delete(dir, recursive: true); }
        }
    }
}
