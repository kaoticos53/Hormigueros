using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.1 — contrato del esqueleto Unity verificado headless. Los scripts puros
    /// del proyecto Unity (GameStreamParser, GameStreamPresenter, PoolPickerModel,
    /// en src/App/AntSim.Unity/Assets/Scripts, sin UnityEngine) se compilan aquí
    /// y se verifican contra la salida REAL del Core: si el stream o el JSON de
    /// presets cambia de contrato, estos tests rompen ANTES de que lo haga la UI.
    /// </summary>
    public sealed class UnityStreamContractTests
    {
        private static string ProjectRoot => FindRoot();
        private static string UnityScripts => Path.Combine(ProjectRoot,
            "src", "App", "AntSim.Unity", "Assets", "Scripts");

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx"))
                   && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
                dir = dir.Parent!;
            return dir!.FullName;
        }

        [Fact]
        public void ScriptsPurosExisten_YNoReferencianUnity()
        {
            foreach (var rel in new[] { "Streaming/GameStreamParser.cs", "Presenter/GameStreamPresenter.cs", "UI/PoolPickerModel.cs", "UI/AntInspectorModel.cs", "UI/ColonyCardModel.cs", "UI/HudToastsModel.cs" })
            {
                string path = Path.Combine(UnityScripts, rel);
                Assert.True(File.Exists(path), "Falta el script puro: " + rel);
                string src = File.ReadAllText(path);
                Assert.DoesNotContain("using UnityEngine", src); // regla del proyecto: puro (sin dependencia de Unity)
            }
        }

        [Fact]
        public void Parser_ReconstruyeElStreamReal()
        {
            string stream = GameScenario.Run(42, ticks: 800, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var views = new List<AntSim.Unity.Scripts.Streaming.GameStreamParser.TickView>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) views.Add(v);
            }

            Assert.NotNull(parser.Header);
            Assert.Equal(42UL, parser.Header!.Seed);
            Assert.Equal(96, parser.Header.Grid);
            Assert.NotNull(parser.FinalHash);
            Assert.Equal(800UL, parser.FinalTick);

            // Todos los ticks del stream, con poses, items y colonia.
            Assert.Equal(800, views.Count);
            var last = views[^1];
            Assert.Equal(800UL, last.Tick);
            Assert.Equal(10, last.Ants.Count);     // fundadoras vivas
            Assert.All(last.Ants, a => Assert.True(a.Alive));
            Assert.Single(last.Colonies);
            Assert.True(last.Colonies[0].Adults >= 10);

            // Los eventos del canal B llegan (nacimiento/initial spawn o puesta).
            Assert.Contains(views, v => v.Events.Count > 0);

            // Las métricas de 1 s cierran periódicamente (30 ticks).
            Assert.Contains(views, v => v.Metrics != null);
        }

        [Fact]
        public void Parser_TrazaUnHormigaConMovimientoCoherente()
        {
            string stream = GameScenario.Run(42, ticks: 60, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var views = new List<AntSim.Unity.Scripts.Streaming.GameStreamParser.TickView>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) views.Add(v);
            }

            // La hormiga 1 existe en todos los ticks y no salta más de VMax*dt*2 por tick.
            uint id1 = 1;
            for (int i = 1; i < views.Count; i++)
            {
                var a0 = views[i - 1].Ants.First(a => a.Id == id1);
                var a1 = views[i].Ants.First(a => a.Id == id1);
                float dx = a1.X - a0.X, dy = a1.Y - a0.Y;
                Assert.True(dx * dx + dy * dy < 4f, $"salto imposible en tick {views[i].Tick}");
            }
        }

        [Fact]
        public void Presenter_InterpolaEntreTicksConsiguientes()
        {
            string stream = GameScenario.Run(42, ticks: 30, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var presenter = new AntSim.Unity.Scripts.Streaming.GameStreamPresenter();
            foreach (var line in stream.Split('\n')) presenter.Feed(line);

            var mid = presenter.Sample(0.5f);
            var end = presenter.Sample(1f); // clamp natural: usa el tick actual

            Assert.True(mid.Ants.Count > 0);
            Assert.Equal(30UL, mid.Tick);

            // A mitad de tick, la pose está entre el tick 29 y el 30 (si hubo tick 29).
            // Verificación débil pero real: ninguna pose NaN ni fuera del mundo.
            foreach (var a in mid.Ants)
            {
                Assert.False(float.IsNaN(a.X) || float.IsNaN(a.Y));
                Assert.InRange(a.X, 0f, 96f * 8f);
                Assert.InRange(a.Y, 0f, 96f * 8f);
            }
        }

        [Fact]
        public void ReplayPorArchivo_ElPresenterRenderizaCadaTick()
        {
            // 1. Genera el stream determinista (in-proc, mismo contrato que el CLI)
            //    y lo vuelca a un archivo — el fixture es la función, no un binario.
            string stream = GameScenario.Run(42, ticks: 1200, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);
            string path = Path.Combine(Path.GetTempPath(),
                "antsim-stream-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                File.WriteAllText(path, stream);

                // 2. Replay por StreamFile (el camino de la UI sin CLI). El presenter
                //    es una vista VIVA: se alimenta línea a línea y se renderiza cada
                //    tick según llega (dos muestras por tick, t=0 y t=0.5).
                var presenter = new AntSim.Unity.Scripts.Streaming.GameStreamPresenter();
                var source = new AntSim.Unity.Scripts.Streaming.StreamSource("no-cli-needed");

                const int MaxAdults = 40;
                var lastPos = new Dictionary<uint, (float X, float Y)>();
                ulong rendered = 0;
                ulong lastRenderedTick = 0;
                bool sawHeader = false, half = false;

                source.StreamFile(path, line =>
                {
                    var view = presenter.Feed2(line); // null en header/end, TickView en tick
                    if (view == null)
                    {
                        if (presenter.Header != null) sawHeader = true;
                        return;
                    }

                    foreach (float t in new[] { 0f, 0.5f })
                    {
                        var state = presenter.Sample(t);
                        rendered++;

                        Assert.True(state.Ants.Count <= 2 * MaxAdults,
                            $"demasiadas adultas en tick {state.Tick}: {state.Ants.Count}");

                        foreach (var a in state.Ants)
                        {
                            Assert.False(float.IsNaN(a.X) || float.IsNaN(a.Y),
                                $"pose NaN en tick {state.Tick}");
                            Assert.InRange(a.X, 0f, 96f * 8f);
                            Assert.InRange(a.Y, 0f, 96f * 8f);

                            // Coherencia física entre ticks consecutivos (muestra t=0).
                            if (t == 0f && lastPos.TryGetValue(a.Id, out var prev))
                            {
                                float dx = a.X - prev.X, dy = a.Y - prev.Y;
                                Assert.True(dx * dx + dy * dy < 9f,
                                    $"salto imposible de la hormiga {a.Id} en tick {state.Tick}");
                            }
                            if (t == 0f) lastPos[a.Id] = (a.X, a.Y);
                        }

                        if (t == 0.5f)
                        {
                            Assert.True(state.Tick > lastRenderedTick,
                                $"el replay retrocedió: {lastRenderedTick} → {state.Tick}");
                            lastRenderedTick = state.Tick;
                        }
                    }
                });

                Assert.True(sawHeader);
                Assert.NotNull(presenter.FinalHash);
                Assert.Equal(1200UL, presenter.FinalTick);

                // Ventana completa renderizada: 2 muestras × 1200 ticks.
                Assert.Equal(2400UL, rendered);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Presenter_Buffered_ReproduceEnOrdenSinSaltos()
        {
            // F4.1b (Play pass): el CLI escribe el JSONL ENTERO de golpe. Sin
            // buffer, el presenter saltaba al último tick al primer frame y la
            // vista nunca mostraba la partida. Con buffer, el stream se encola y
            // el cursor avanza a petición, tick a tick y en orden.
            string stream = GameScenario.Run(42, ticks: 600, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var presenter = new AntSim.Unity.Scripts.Streaming.GameStreamPresenter { Buffered = true };
            foreach (var line in stream.Split('\n')) presenter.Feed(line);

            // Nada presentado aún: el stream llegó entero y el cursor sigue en 0.
            Assert.Null(presenter.CurrentTick);
            Assert.Equal(600, presenter.BufferedTicks);

            var ticks = new List<ulong>();
            for (ulong target = 1; target <= 600; target++)
            {
                presenter.AdvanceTo(target);
                while (presenter.TryDequeuePresented(out var v) && v != null)
                    ticks.Add(v.Tick);
            }

            Assert.Equal(600, ticks.Count);
            for (int i = 0; i < ticks.Count; i++)
                Assert.Equal((ulong)(i + 1), ticks[i]);   // 1..600, sin saltos ni repeticiones
            Assert.Equal(0, presenter.BufferedTicks);
            Assert.Equal(600UL, presenter.CurrentTick!.Tick);
        }

        [Fact]
        public void Presenter_Buffered_NoPierdeNingunaAlertaDelCanalD()
        {
            // El canal D solo emite la alerta en SU tick. Si un frame presenta
            // varios ticks (velocidad alta) y el HUD leyera solo el último, la
            // alerta se perdería. El drenado de presentados debe entregarlas TODAS,
            // exactamente una vez.
            string stream = GameScenario.Run(42, ticks: 7200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: new[] { (30, 256f, 384f) });

            var expected = new List<string>();
            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line.Trim());
                if (v == null) continue;
                foreach (var a in v.Alerts) expected.Add(a.Key + "@" + a.Tick);
            }
            Assert.NotEmpty(expected);   // la partida canónica emite alertas

            var presenter = new AntSim.Unity.Scripts.Streaming.GameStreamPresenter { Buffered = true };
            foreach (var line in stream.Split('\n')) presenter.Feed(line);

            var got = new List<string>();
            // Render a 60 fps con sim a 30 Hz × velocidad 2 ⇒ 3 ticks por frame.
            for (ulong target = 1; target <= 7200; target += 3)
            {
                presenter.AdvanceTo(target);
                while (presenter.TryDequeuePresented(out var v) && v != null)
                    foreach (var a in v.Alerts) got.Add(a.Key + "@" + a.Tick);
            }
            while (presenter.TryDequeuePresented(out var v) && v != null)
                foreach (var a in v.Alerts) got.Add(a.Key + "@" + a.Tick);

            Assert.Equal(expected, got);
        }

        /// <summary>Hash final fijado del stream canónico (seed 42, grid 96,
        /// 2 colonias, 7200 ticks): la partida de artifacts/stream-fixture.jsonl.
        /// Lo comparte scripts/check-stream-fixture.sh (scripts/stream-fixture.
        /// expected). Si falla, el determinismo del mundo se rompió; un cambio
        /// INTENCIONAL se actualiza en ambos sitios con --update y en este test.</summary>
        public const string CanonicalStreamHash =
            "ab30f6ce31621418b36740edf744bcc3ef2664d4d7ab6754124f6a6bf6ce4e00";

        [Fact]
        public void FixtureHash_ElStreamCanonicoEsByteAByteEstable()
        {
            // Huella CHC (F5.3): la cuarta capa entra en el hash y su reflejo de
            // tropotaxis desvía las trayectorias (reparte el tráfico), así que el
            // mundo cambia de forma intencional y documentada: pines al día.
            string stream = GameScenario.Run(42, ticks: 7200, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            string endLine = stream.Split('\n').Last(l => !string.IsNullOrWhiteSpace(l)).TrimEnd('\r');
            Assert.StartsWith("{\"end\":true", endLine);

            int i = endLine.IndexOf("\"hash\":\"", StringComparison.Ordinal);
            Assert.True(i >= 0, "la línea end debe traer hash");
            i += "\"hash\":\"".Length;
            string hash = endLine.Substring(i, endLine.IndexOf('"', i) - i);

            Assert.Equal(CanonicalStreamHash, hash);
        }

        /// <summary>
        /// F11n — CONTRATO del bloque `trail`, que el HUD lee para publicar el
        /// gasto de la glándula al cerrar la ronda.
        ///
        /// QUÉ FIJA. El bloque vive DENTRO de la línea `end` (no en un objeto
        /// aparte), con las claves `food`, `home`, `denied`, `gland` en ese orden
        /// y en el formato canónico del stream (punto decimal, tres decimales como
        /// mucho, cultura invariante). Y sólo viaja cuando la partida lo pide:
        /// pedirlo no puede mover el mundo (mismo hash, mismo tick) y no pedirlo
        /// deja la línea `end` del juego de fábrica sin una sola letra nueva.
        ///
        /// LAS DOS CARAS DEL PRESUPUESTO. La ronda se mide con recarga holgada
        /// (6 s: la glándula se rellena más rápido de lo que se gasta y `denied`
        /// queda en cero) y con recarga lenta (60 s: el presupuesto se agota y el
        /// rastro deja depósito sin pagar). Sin el segundo brazo el pin pasaba
        /// siempre por el cero, y el cero es justo la cifra que el HUD publica
        /// como «sin pagar»: un `denied` que no se moviera nunca habría pasado
        /// inadvertido con el mismo bloque bien formado.
        ///
        /// Y EL DEPÓSITO DE COMIDA. A 600 ticks no hay ni una carga: las hormigas
        /// depositan rastro de casa desde que caminan, y el de comida solo cuando
        /// vuelven con algo. El tercer brazo corre 12 000 ticks con el rastro FIJO
        /// (sin puerta) porque la puerta le concede la decisión al cerebro, que
        /// con este pool no deposita comida nunca —`food` se quedaba en 0 con el
        /// bloque perfectamente formado, que es el otro cero que este pin tenía
        /// que dejar de creerse—.
        ///
        /// POR QUÉ. Renombrar una clave, reordenarlas o emitirlas siempre no rompe
        /// nada visible: el parser devuelve cero y el HUD publica «0 s gastados»,
        /// que es una mentira silenciosa. Este pin la convierte en un fallo de
        /// suite, y el `hash` de arriba impide que el informe se cuele en el mundo.
        /// </summary>
        [Fact]
        public void TrailReport_ElBloqueDeLaLineaEndEsContrato_YSoloViajaSiSePide()
        {
            static string EndOf(string stream)
                => stream.Split('\n').Last(l => !string.IsNullOrWhiteSpace(l)).TrimEnd('\r');
            static string HashOf(string end)
            {
                int i = end.IndexOf("\"hash\":\"", StringComparison.Ordinal) + "\"hash\":\"".Length;
                return end.Substring(i, end.IndexOf('"', i) - i);
            }
            static string Token(string block, string key)
            {
                int i = block.IndexOf("\"" + key + "\":", StringComparison.Ordinal) + key.Length + 3;
                int j = i;
                while (j < block.Length && block[j] != ',' && block[j] != '}') j++;
                return block.Substring(i, j - i);
            }
            static float Num(string block, string key)
                => float.Parse(Token(block, key), CultureInfo.InvariantCulture);

            AntSim.Unity.Scripts.Streaming.GameStreamParser.TrailReportView Run(
                float glandSeconds, float refillSeconds, int ticks, bool byBrain)
            {
                string arm = $"glándula {glandSeconds} s / recarga {refillSeconds} s / " +
                              $"{ticks} ticks / {(byBrain ? "puerta" : "rastro fijo")}";
                var tuning = new RecruitmentTuning
                {
                    TrailByBrain = byBrain,
                    TrailGlandSeconds = glandSeconds,
                    TrailGlandRefillSeconds = refillSeconds,
                };
                string with = GameScenario.Run(42, ticks: ticks, colonies: 1, grid: 96,
                    frameEvery: 30, recruitment: true, recruitmentTuning: tuning,
                    trailReport: true);
                string without = GameScenario.Run(42, ticks: ticks, colonies: 1, grid: 96,
                    frameEvery: 30, recruitment: true, recruitmentTuning: tuning,
                    trailReport: false);

                // — Sin pedirlo, la línea `end` es la de siempre —
                string plain = EndOf(without);
                Assert.DoesNotContain("\"trail\"", plain);

                // — Pedirlo no mueve el mundo: mismo tick y mismo hash —
                string reported = EndOf(with);
                Assert.Equal(HashOf(plain), HashOf(reported));
                Assert.StartsWith("{\"end\":true", reported);

                // — El bloque va dentro de `end` y sus cuatro claves, en orden —
                int at = reported.IndexOf("\"trail\":{", StringComparison.Ordinal);
                Assert.True(at > 0, $"[{arm}] la línea end no trae el bloque trail: {reported}");
                string block = reported.Substring(at);
                Assert.EndsWith("}}", block);              // cierra `trail` y con él `end`
                Assert.Equal("\"trail\":{\"food\":", block.Substring(0, "\"trail\":{\"food\":".Length));
                Assert.Equal(6, block.Split(':').Length);   // cuatro claves: ni una más
                int kFood = block.IndexOf("\"food\":", StringComparison.Ordinal);
                int kHome = block.IndexOf("\"home\":", StringComparison.Ordinal);
                int kDenied = block.IndexOf("\"denied\":", StringComparison.Ordinal);
                int kGland = block.IndexOf("\"gland\":", StringComparison.Ordinal);
                Assert.True(kFood < kHome && kHome < kDenied && kDenied < kGland,
                    $"[{arm}] el orden de las claves del informe es contrato: {block}");

                // — Formato canónico: cada número se reescribe igual con "0.###" —
                foreach (string key in new[] { "food", "home", "denied", "gland" })
                {
                    string token = Token(block, key);
                    Assert.Equal(token, float.Parse(token, CultureInfo.InvariantCulture)
                        .ToString("0.###", CultureInfo.InvariantCulture));
                }

                // — El parser devuelve exactamente esos números —
                var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
                foreach (var line in with.Split('\n')) parser.ParseLine(line);
                var trail = parser.Trail;
                Assert.NotNull(trail);
                Assert.Equal(Num(block, "food"), trail!.Value.Food);
                Assert.Equal(Num(block, "home"), trail.Value.Home);
                Assert.Equal(Num(block, "denied"), trail.Value.Denied);
                Assert.Equal(Num(block, "gland"), trail.Value.Gland);

                // — Y la cifra no es un cero disfrazado de informe —
                Assert.True(trail.Value.Gland > 0f,
                    $"[{arm}] la ronda con precio tiene que gastar glándula");
                Assert.True(trail.Value.Food + trail.Value.Home > 0f,
                    $"[{arm}] y tiene que depositar rastro");
                Assert.InRange(trail.Value.DeniedPercent, 0f, 100f);
                // El porcentaje es el cociente de los guiones de medida, no otro.
                Assert.Equal(100f * trail.Value.Denied /
                             (trail.Value.Denied + trail.Value.Food + trail.Value.Home),
                             trail.Value.DeniedPercent);

                return trail.Value;
            }

            // La glándula se rellena más rápido de lo que se gasta: no se niega nada.
            var holgada = Run(glandSeconds: 25f, refillSeconds: 6f, ticks: 600, byBrain: true);
            Assert.Equal(0f, holgada.Denied);

            // El presupuesto se agota y la recarga tarda 60 s: el rastro queda a medias.
            var racionada = Run(glandSeconds: 5f, refillSeconds: 60f, ticks: 600, byBrain: true);
            Assert.True(racionada.Denied > 0f,
                "con la recarga lenta la ronda tiene que dejar depósito sin pagar: " +
                $"food={racionada.Food} home={racionada.Home} denied={racionada.Denied}");
            Assert.True(racionada.DeniedPercent > 0f,
                $"y el porcentaje tiene que verlo: {racionada.DeniedPercent} %");

            // Con rastro fijo las hormigas vuelven con carga y el depósito de comida
            // entra en el informe; el presupuesto, al horizonte largo, también se agota.
            var conComida = Run(glandSeconds: 25f, refillSeconds: 6f, ticks: 12_000, byBrain: false);
            Assert.True(conComida.Food > 0f,
                "el rastro de comida tiene que entrar en el informe con datos reales: " +
                $"food={conComida.Food} home={conComida.Home} denied={conComida.Denied}");
            Assert.True(conComida.Denied > 0f,
                $"y a 12 000 ticks el presupuesto también se agota: denied={conComida.Denied}");
        }

        [Fact]
        public void Inspector_SigueUnaHormigaRealDelStream()
        {
            string stream = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var inspector = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();

            // Selecciona la hormiga 1 ANTES de alimentar: cubre "esperando datos".
            inspector.Select(1);
            Assert.Null(inspector.Tracked); // aún sin datos, la tarjeta espera

            foreach (var line in stream.Split('\n'))
            {
                var view = parser.ParseLine(line);
                if (view != null) inspector.Observe(view);
            }

            var rec = inspector.Tracked;
            Assert.NotNull(rec);
            Assert.Equal(400, rec!.Samples.Count); // una muestra por tick de canal A

            // — Los 12 campos del canal A llegan coherentemente —
            var s0 = rec.Samples[0];
            Assert.Equal(1u, rec.Id);
            Assert.Equal(0, rec.ColonyId);
            Assert.InRange(s0.Vigor, 0.5f, 1.3f);      // rango del fundador (contrato F4.2)
            Assert.InRange(s0.Energy, 0f, 1f);
            Assert.False(s0.IsImmigrant);              // las fundadoras no son inmigrantes
            Assert.NotEqual(0u, s0.GenomeFingerprint); // huella estable y no trivial

            // — Serie temporal: edad monótona, energía no creciente (sin comida garantizada)—
            for (int i = 1; i < rec.Samples.Count; i++)
            {
                Assert.True(rec.Samples[i].Age >= rec.Samples[i - 1].Age, "edad monótona");
                Assert.True(rec.Samples[i].Energy <= rec.Samples[i - 1].Energy + 1e-4f,
                    "energía no crece sin comer (semilla 42, sin pickup garantizado)");
            }

            // — La huella del cerebro es constante para la misma hormiga —
            Assert.All(rec.Samples, s => Assert.Equal(s0.GenomeFingerprint, s.GenomeFingerprint));

            // — Tarjeta: estados renderizados —
            string card = inspector.RenderCard();
            Assert.StartsWith("Hormiga #1", card);
            Assert.Contains("colonia 0", card);
            Assert.Contains("cerebro #", card);
            Assert.Contains("400", card); // rango de historial
        }

        [Fact]
        public void Inspector_CapturaLaMuerteDelCanalB()
        {
            // Mundo sin comida: las fundadoras mueren al alcanzar su esperanza de vida
            // (BaseLifespan 720 s ⇒ ~24840 ticks): 26000 basta.
            string stream = GameScenario.Run(42, ticks: 26000, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var inspector = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();

            // Sigue la hormiga 1 desde el arranque (el camino real del inspector:
            // el jugador selecciona a una viva y la observa hasta el final).
            inspector.Select(1);
            bool anyDeath = false;
            foreach (var line in stream.Split('\n'))
            {
                var view = parser.ParseLine(line);
                if (view == null) continue;
                foreach (var ev in view.Events)
                    if (ev.Kind == 1) anyDeath = true;
                inspector.Observe(view);
            }

            Assert.True(anyDeath, "el mundo sin comida debe producir muertes en 26000 ticks");
            var rec = inspector.Tracked;
            Assert.NotNull(rec);
            Assert.NotNull(rec!.DeathTick); // la muerte quedó capturada
            Assert.True(rec.DeathCause is 0 or 1, "causa válida del Core (vejez=0, inanición=1)");

            string card = inspector.RenderCard();
            Assert.Contains("muerta (", card);
            Assert.Contains(rec.DeathCause == 0 ? "vejez" : "inanición", card);

            // Contrato del canal A (F5.3 rodaja 2): la fila de la hormiga
            // DESAPARECE en el tick de su muerte — el mundo compacta los
            // cadáveres al final del Step, así que NO hay muestras posteriores
            // (ni una alive=false en el tick de la muerte). La muerte en sí la
            // captura el canal B (arriba: DeathTick + causa).
            Assert.All(rec.Samples, s =>
                Assert.True(s.Tick < rec.DeathTick!.Value,
                    "muestra del canal A en el tick de la muerte o posterior (la fila debe estar compactada): tick " + s.Tick));

            // — Caso selección tardía: la muerte de una hormiga NUNCA rastreada
            //    se conserva como expediente (el canal A no emite muertos) —
            var inspector2 = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();
            uint deadUntracked = 0; ulong deathTick = 0;
            foreach (var line in stream.Split('\n'))
            {
                var view = parser.ParseLine(line);
                if (view == null) continue;
                if (deadUntracked == 0)
                    foreach (var ev in view.Events)
                        if (ev.Kind == 1 && ev.AntId != 1)
                        { deadUntracked = ev.AntId; deathTick = view.Tick; }
                inspector2.Observe(view);
            }
            Assert.NotEqual(0u, deadUntracked);
            var stub = inspector2.Records.FirstOrDefault(r => r.Id == deadUntracked);
            Assert.NotNull(stub);
            Assert.Equal(deathTick, stub!.DeathTick);
            Assert.Empty(stub.Samples);
        }

        [Fact]
        public void Inspector_EstadosDeTarjeta_YSeleccionesMultiples()
        {
            var inspector = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();

            // Sin selección.
            Assert.Equal("inspección: sin selección", inspector.RenderCard());

            // Seleccionada pero sin datos todavía.
            inspector.Select(7);
            Assert.Equal("Hormiga #7 — esperando datos", inspector.RenderCard());

            // Selección con datos, luego clear: el historial se conserva.
            string stream = GameScenario.Run(42, ticks: 10, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);
            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            foreach (var line in stream.Split('\n'))
            {
                var view = parser.ParseLine(line);
                if (view != null) inspector.Observe(view);
            }
            Assert.NotNull(inspector.Tracked);
            Assert.Equal(10, inspector.Tracked!.Samples.Count);

            inspector.Clear();
            Assert.Null(inspector.SelectedId);
            Assert.Equal("inspección: sin selección", inspector.RenderCard());
            Assert.Single(inspector.Records); // el historial de la #7 no se pierde
        }

        [Fact]
        public void ChainAvg_ElStreamTraeLaCadenaDisponible()
        {
            // F4.4: relays[c] lleva chainAvg (pickup→nido − 24 u, media de las
            // cargas completadas) — el insumo del semáforo normalizado. Hace falta
            // una descarga real para que relays[c] deje de ser empty:true, así que
            // la partida va sembrada con warm-v2 hasta la primera descarga
            // (tick 5154 en seed 42/grid 256; con margen, 5600).
            string stream = GameScenario.Run(42, ticks: 5600, colonies: 2, grid: 256,
                frameEvery: 30, seedPoolPath: TestPaths.WarmV2PoolPath(),
                drops: null);
            Assert.Contains("\"chainAvg\":", stream); // presente (null o valor) en relays

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            float? chain = null;
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                foreach (var r in v.ColonyRelays)
                    if (r.ChainAvg is float ca) chain = ca;
            }
            Assert.NotNull(chain); // con la descarga ya ocurrida, chainAvg es valor
        }

        [Fact]
        public void Linaje_AgrupaCuerposPorHuellaYSaltaAlMuere()
        {
            // Sintético con el CONTRATO REAL del parser (filas de 12 campos): dos
            // cuerpos (#10, #11) portan la misma huella 777 — el mismo cerebro en
            // cuerpos sucesivos. Los genomas son únicos por cuerpo en los mundos
            // actuales (Birth siempre cruza+y muta), así que el caso multi-cuerpo
            // se construye; el contrato es para modos con reuso de cerebros.
            string Row(uint id, int tick, float x, bool load, bool alive, uint fp)
                => $"{{\"tick\":{tick},\"ants\":[[{id},0,{x.ToString(System.Globalization.CultureInfo.InvariantCulture)},100,0.5,{(load ? 1 : 0)},{(alive ? 1 : 0)},0.8,1,{tick / 30}.0,0,{fp}]],\"items\":[],\"colonies\":[],\"events\":[]}}";

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var insp = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();
            insp.Select(10);

            // Cuerpo #10 vivo t=30..90.
            foreach (int t in new[] { 30, 60, 90 })
            {
                var v = parser.ParseLine(Row(10, t, x: 10f + t, load: true, alive: true, fp: 777));
                insp.Observe(v!);
            }
            Assert.Equal(3, insp.Tracked!.Samples.Count);

            // Muerte del #10 (canal B): el flujo real de la UI es "seguir cerebro"
            // en/tras la muerte, ANTES de que aparezca el siguiente cuerpo.
            insp.Observe(parser.ParseLine(
                "{\"tick\":91,\"ants\":[],\"items\":[],\"colonies\":[],\"events\":[[1,0,10,20,100,1]]}")!);
            Assert.True(insp.FollowBrainOfTracked());
            Assert.Equal(777u, insp.FollowedBrain);

            // Aparece el #11 con la MISMA huella: el modo linaje lo rastrea solo.
            insp.Observe(parser.ParseLine(Row(11, tick: 120, x: 40f, load: false, alive: true, fp: 777))!);

            var lineage = insp.LineageOf(777);
            Assert.Equal(2, lineage.Count);
            Assert.Equal(10u, lineage[0].Id); // orden de aparición
            Assert.Equal(11u, lineage[1].Id);

            string card = insp.RenderCard();
            Assert.StartsWith("cerebro #777", card);
            Assert.Contains("2 cuerpos", card);
            Assert.Contains("1 vivo", card);
            Assert.Contains("#10 t30†91", card);     // cuerpo muerto con su tick
            Assert.Contains("#11 t120 (vivo)", card); // cuerpo actual

            // — Salto automático: el cuerpo actual del cerebro es el vivo —
            var cur = insp.CurrentBody;
            Assert.NotNull(cur);
            Assert.Equal(11u, cur!.Id);

            // — El modo hormiga sobre un cuerpo del linaje conserva el expediente —
            insp.FollowAnt(11);
            Assert.Null(insp.FollowedBrain);
            Assert.NotNull(insp.Tracked);
            Assert.Equal(1, insp.Tracked.Samples.Count);
        }

        [Fact]
        public void Linaje_EnStreamReal_CerebrosUnicosPorCuerpo()
        {
            // En los mundos actuales cada nacimiento es un genoma nuevo (Birth
            // siempre cruza y muta): el linaje de un cerebro es un solo cuerpo.
            // El test ancla ESTA REALIDAD para que un cambio en el mundo que
            // introduzca reuso de cerebros sea una decisión consciente.
            string stream = GameScenario.Run(42, ticks: 300, colonies: 2, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);
            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var insp = new AntSim.Unity.Scripts.Streaming.AntInspectorModel();

            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) insp.Observe(v);
            }

            // Sin selección ni follow, Observe no acumula nada.
            Assert.Empty(insp.Records);

            // Todos los cuerpos vistos por el canal A, agrupados por huella.
            var seen = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.HashSet<uint>>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                foreach (var a in v.Ants)
                {
                    if (!seen.TryGetValue(a.GenomeFingerprint, out var set))
                        seen[a.GenomeFingerprint] = set = new();
                    set.Add(a.Id);
                }
            }
            Assert.True(seen.Count > 0);
            Assert.All(seen.Values, set => Assert.Single(set)); // 1 cerebro = 1 cuerpo
        }

        [Fact]
        public void Picker_ParseaElJsonCanonical_YSeparaTiers()
        {
            string json = PresetScenario.RenderCards(json: true);
            var model = AntSim.Unity.Scripts.Streaming.PoolPickerModel.ParseJson(json);

            Assert.Equal(7, model.Presets.Count);

            var rec = model.Recommended().ToList();
            var spec = model.Specialists().ToList();
            Assert.Equal(4, rec.Count);
            Assert.Equal(3, spec.Count);

            // Nivel 1 (recomendados): los 4 del diseño de UX, en orden canónico.
            Assert.Equal(new[] { "naturalista", "warm-v2", "warm-4", "warm3" },
                rec.Select(p => p.Id).ToArray());

            // Nivel 2 (especialistas): la cadena completa con su contrapartida + NEAT.
            Assert.Equal(new[] { "warm3-v2", "warm-5", "neat-cierre" }, spec.Select(p => p.Id).ToArray());

            var warmv2 = rec.First(p => p.Id == "warm-v2");
            Assert.Equal(99, warmv2.Pickups);
            Assert.Equal(167.9f, warmv2.DropAvg!.Value, 1);
            Assert.Equal("artifacts/pretrain-warm-v2.antgenome", warmv2.GenomeFile);

            var warm5 = spec.First(p => p.Id == "warm-5");
            Assert.Equal(118, warm5.Pickups);
            Assert.Equal(2, warm5.GameSeedsWithUnload); // el ⚠ tiene datos detrás
        }
    }
}
