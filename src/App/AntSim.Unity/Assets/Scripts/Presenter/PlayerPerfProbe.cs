using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// F5.3 — sonda del PLAYER EMPAQUETADO: mide el framerate PRESENTADO (con el
    /// vsync del proyecto, que en un build sí lo paga el swap de la ventana) y
    /// pasa la puerta de PÍXELES sobre un build real.
    ///
    /// POR QUÉ HACE FALTA UN PLAYER. La corrida en ventana del editor (Play pass)
    /// mide el coste de producir frames, pero NO el refresco presentado: en el
    /// editor el vsync no llegó a aplicarse (vSyncCount=1 y 403,8 fps, dentro del
    /// 1 % de la corrida con vsync apagado). Un player sí presenta al monitor, así
    /// que la única lectura honesta del criterio «N colonias estables a 60 fps» es
    /// ésta: ¿cuántos frames CON SIGUIENTE FRAME VECINO entrega el build?
    ///
    /// La sonda entra SOLO si la línea de comandos trae <c>-antsimPerfOut</c>: sin
    /// argumento es inerte (también en el editor, donde el hook se ejecuta igual).
    /// Argumentos:
    ///   -antsimPerfOut &lt;ruta&gt;      informe JSON (obligatorio; relativo = repo root)
    ///   -antsimPerfSeconds &lt;n&gt;     ventana de muestreo (def. 30)
    ///   -antsimPerfWarmup &lt;n&gt;      calentamiento ignorado (def. 12): el stream entra
    ///                             y el mundo crece; muestrear eso mezclaría arranque
    ///                             con régimen
    ///   -antsimPerfBoost &lt;x&gt;       velocidad de reproducción (def. 10)
    ///   -antsimPerfNoVsync &lt;1|0&gt;  apaga el vsync para medir el COSTE por frame
    ///                             (5ª pieza del criterio de cierre). Con vsync el
    ///                             techo de 60 fps lo pone la pantalla y el coste del
    ///                             frame queda tapado; sin vsync el bucle produce
    ///                             frames tan rápido como puede y el
    ///                             `FrameTimingManager` dice cuánto cuesta cada uno
    ///                             de CPU (y de GPU, si la plataforma lo expone).
    ///                             El vsync DEL PROYECTO se registra aparte
    ///                             (`projectVSyncCount`): la corrida lo apaga, y eso
    ///                             no debe leerse como que el proyecto lo tenía así.
    ///   -antsimPerfDump &lt;ruta&gt;    vuelca la CAPTURA CRUDA a un fichero (cabecera
    ///                             «ANTSIMRGB w h» + bytes R,G,B). Es la prueba
    ///                             irrefutable de lo que hay en pantalla: el mosaico
    ///                             dice DÓNDE falta la tinta, pero no de qué color es
    ///                             lo que hay en su lugar (¿nada pintado, o pintado
    ///                             tan apagado que no llega a «tinta»?). Sin
    ///                             argumento no vuelca.
    ///   -antsimPerfSystem &lt;1|0&gt;  añade la pata del CLI: el coste del SISTEMA
    ///                             COMPLETO (6ª pieza del criterio). Implica lo del
    ///                             modo coste (sin vsync) y suma, ventana a ventana,
    ///                             la CPU del proceso hijo y los ticks que simuló. El
    ///                             horizonte tiene que dar para que el CLI siga
    ///                             simulando TODA la ventana (`ANTSIM_PERF_TICKS`):
    ///                             con el mundo terminado lo medido sería un player
    ///                             solo con otro nombre.
    ///
    /// EL HUD SÍ SE CAPTURA, y sin PNG (F5.1). El render de la cámara a una
    /// RenderTexture —de donde sale la puerta del mundo— NO incluye un Canvas en
    /// Screen Space Overlay: el HUD lo compone el motor y solo existe en el
    /// backbuffer. Por eso el chip de runway se mide con `ScreenCapture` (el módulo
    /// está en el manifest desde 9ce551c, que lo repuso para la sonda visual del
    /// editor) sobre el frame PRESENTADO, con una consecuencia que importa: la
    /// expectativa de cada tarjeta se toma del frame ANTERIOR, porque el backbuffer
    /// que se lee es el que ya se presentó. Lo que se publica son los tres números
    /// que el modelo puro `HudInkModel` saca de esos píxeles —el color del tramo, el
    /// ancho real de la línea del título y la tinta que invade la barra de
    /// reserva—; el mundo se sigue leyendo en MOSAICAS de caracteres.
    ///
    /// Salida: 0 si MIDIÓ y la puerta de píxeles pasó en todas las vistas (y el HUD
    /// midió); 1 si midió pero la puerta suspende (o el montaje está muerto) o el
    /// chip no es el que el Core ordenó; 2 si no hubo ni una muestra; 3 en modo
    /// coste si el FrameTimingManager no entregó ni un tiempo de frame (sin eso no
    /// hay coste, solo framerate); 4 si el HUD no se pudo medir (sin captura de
    /// pantalla o sin tarjetas de colonia); 10 en modo sistema si el CLI no estuvo
    /// simulando dentro de la ventana (sin su pata no hay coste del sistema). Un
    /// verde sobre una escena sin hormigas sería el peor resultado posible, así que
    /// ese caso es un suspenso explícito — y un verde sin un solo chip medido
    /// también: medir nada no es medir bien.
    /// </summary>
    public sealed class PlayerPerfProbe : MonoBehaviour
    {
        private const string Tag = "[PlayerPerf]";

        private string _outPath = "";
        private string _dumpPath = "";
        private float _seconds = 30f;
        private float _warmup = 12f;
        private float _boost = 10f;
        private bool _noVsync;
        private bool _system;
        private int _projectVSync;

        // Tiempos del FrameTimingManager de la ventana en curso. Los ceros significan
        // «este frame no dejó dato» (el manager entrega con retardo) y la mediana los
        // ignora: contarlos hundiría el coste que se publica.
        private readonly List<double> _cpuMs = new();
        private readonly List<double> _mainMs = new();
        private readonly List<double> _renderMs = new();
        private readonly List<double> _waitMs = new();
        private readonly List<double> _gpuMs = new();
        private readonly FrameTiming[] _timingOne = new FrameTiming[1];
        private int _timingsTaken;
        private uint _lastSyncInterval;

        private float _start;
        private float _lastSampleAt;
        private int _lastSampleFrames;
        private int _frames;
        private bool _reported;
        private readonly List<Sample> _samples = new();

        // Tarjetas del HUD del montaje (se descubren una vez) y su ESTADO DEL FRAME
        // ANTERIOR: el que está en el backbuffer que captura el cierre.
        private List<HudCardTarget> _hudCards = new();
        private List<HudExpectation> _hudPrev = new();

        private readonly struct Sample
        {
            public readonly float Seconds;
            public readonly double Fps;
            public readonly int DrawCalls;
            public readonly int Instanced;
            public readonly int Fallback;
            public readonly int Ants;
            public readonly int Items;
            public readonly ulong Tick;
            public readonly bool Focused;
            /// <summary>Mediana de los tiempos de frame de esa ventana (0 = sin dato).</summary>
            public readonly FrameTimingSample Timing;
            public readonly long WindowFrames;
            public readonly uint SyncInterval;
            /// <summary>CPU acumulada de los CLI de todas las vistas (ms) en el
            /// instante de esta muestra (F5.3, coste del SISTEMA).</summary>
            public readonly double CliCpuMs;
            /// <summary>Ticks simulados acumulados de todas las vistas: la SUMA, no
            /// el máximo — cada vista simula su mundo y el CLI de cada una gasta lo
            /// suyo, así que el trabajo total del sistema es la suma.</summary>
            public readonly ulong TickSum;

            public Sample(float seconds, double fps, int drawCalls, int instanced, int fallback,
                int ants, int items, ulong tick, bool focused, FrameTimingSample timing,
                long windowFrames, uint syncInterval, double cliCpuMs, ulong tickSum)
            {
                Seconds = seconds; Fps = fps; DrawCalls = drawCalls; Instanced = instanced;
                Fallback = fallback; Ants = ants; Items = items; Tick = tick; Focused = focused;
                Timing = timing; WindowFrames = windowFrames; SyncInterval = syncInterval;
                CliCpuMs = cliCpuMs; TickSum = tickSum;
            }
        }

        private sealed class ViewGate
        {
            public int Index;
            public string Camera = "";
            public float Window = 0f;
            public FrameGateResult Result;
            public string Mosaic = "";
        }

        /// <summary>
        /// UNA tarjeta de colonia del montaje, vista como lo que la medición
        /// necesita: el texto que la pinta, las barras de reserva (que NO son texto) y
        /// el modelo que dice qué tramo de runway le toca a cada colonia.
        ///
        /// Las dos formas del HUD entran por aquí: la tarjeta del juego de una vista
        /// (<see cref="HudLayoutBehaviour"/>, con el chip en la línea del título) y la
        /// compacta del multi-visor (<see cref="ViewCardBehaviour"/>, con una línea por
        /// colonia). El HUD no sabe que le están midiendo: la sonda lee sus
        /// componentes, no al revés.
        /// </summary>
        private sealed class HudCardTarget
        {
            public string Name = "";
            public UnityEngine.UI.Text Body = null!;
            public UnityEngine.UI.Image?[]? Bars;
            /// <summary>La segunda familia de franjas (el hongo de la cortadora). Va
            /// aparte porque hay que EXCLUIRLA también: su ocre está a 40 del ámbar de
            /// la paleta y sus franjas cruzan las filas del texto.</summary>
            public UnityEngine.UI.Image?[]? ExtraBars;
            public ColonyCardModel Cards = null!;
            /// <summary>≥0 = sólo esa colonia (la tarjeta grande tiene un texto por
            /// colonia); −1 = todas las de la tarjeta (la compacta).</summary>
            public int ColonyId = -1;
        }

        /// <summary>Lo que UNA tarjeta debía tener pintado en el frame capturado.</summary>
        private sealed class HudExpectation
        {
            public HudCardTarget Target = null!;
            /// <summary>Tramos que el CORE ordenó (canal A → RunwayVerdict).</summary>
            public readonly List<byte> Ordered = new();
            /// <summary>Tramos que el TEXTO pintado declara (sus etiquetas de color).</summary>
            public readonly List<byte> Painted = new();
            public int AuthoredLines;
            /// <summary>La ventana medida de su texto, ya en píxeles de la captura.</summary>
            public bool HasBody;
            public PixelRect Body;
            /// <summary>El FONDO medido de la tarjeta (su panel sobre el mundo): sin él
            /// no se puede medir la cobertura de la tinta ni de los chips.</summary>
            public Rgb Background;
        }

        /// <summary>La medida del HUD de una corrida, con lo que se publica.</summary>
        private sealed class HudReport
        {
            public bool Measured;
            public string Why = "";
            public string Capture = "";
            /// <summary>Dónde quedó el volcado crudo (vacío = no se pidió).</summary>
            public string Dump = "";
            public string Canvas = "";
            public float Scale = 1f;
            public int Chips;
            public int WorstTitlePx;
            public int WorstTitleAvail;
            public int MinHeadroom;
            /// <summary>Peor y mejor cobertura con la que llegó un chip a la pantalla.</summary>
            public float MinChipCover = -1f;
            public float MaxChipCover = -1f;
            public int MaxBarInk;
            public int MaxStagingInk;
            public bool Ok = true;
            /// <summary>El árbol del HUD en texto (una línea por elemento).</summary>
            public string Tree = "";
            public readonly List<HudCardMeasure> Measures = new();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string? outPath = ArgValue(args, "-antsimPerfOut");
            if (string.IsNullOrEmpty(outPath)) return;   // inerte sin argumento

            var go = new GameObject("PlayerPerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerPerfProbe>().Configure(
                outPath,
                ArgFloat(args, "-antsimPerfSeconds", 30f),
                ArgFloat(args, "-antsimPerfWarmup", 12f),
                ArgFloat(args, "-antsimPerfBoost", 10f),
                ArgFloat(args, "-antsimPerfNoVsync", 0f) >= 0.5f,
                ArgFloat(args, "-antsimPerfSystem", 0f) >= 0.5f,
                ArgValue(args, "-antsimPerfDump"));
        }

        private void Configure(string outPath, float seconds, float warmup, float boost,
            bool noVsync, bool system, string? dumpPath)
        {
            // El cwd de un player es su carpeta de build: el informe (y el pool y el
            // CLI) se anclan al repo root, que en un build dentro del repo se
            // encuentra por el marcador (src/Tools/AntSim.Cli).
            string? repo = Streaming.RepoPathResolver.RepoRootFromDataPath(Application.dataPath);
            _outPath = ResolveAgainstRepo(outPath, repo);
            _dumpPath = string.IsNullOrEmpty(dumpPath) ? "" : ResolveAgainstRepo(dumpPath!, repo);
            _seconds = Mathf.Max(1f, seconds);
            _warmup = Mathf.Max(0f, warmup);
            _boost = Mathf.Max(0f, boost);

            // El vsync DEL PROYECTO se lee ANTES de tocarlo: si la corrida lo apaga
            // para medir coste, el informe tiene que decir cuál era el del proyecto
            // (si no, la corrida de coste parecería un proyecto mal configurado).
            _projectVSync = QualitySettings.vSyncCount;
            // El coste del SISTEMA se mide por fuerza sin vsync: con el vsync puesto
            // el frame lo cierra la pantalla y el `cpuFrameTime` del player se llena
            // de espera, así que sumarle la pata del CLI daría un número que no es
            // trabajo de nadie.
            _noVsync = noVsync || system;
            _system = system;
            if (_noVsync)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;   // sin techo de fps: el bucle manda
            }

            _start = Time.realtimeSinceStartup;
            _lastSampleAt = _start;
            Debug.Log($"{Tag} arrancada: ventana {_seconds:0}s · calentamiento {_warmup:0}s · " +
                      $"boost ×{_boost:0.#} · vsync del proyecto={_projectVSync} · " +
                      $"modo={(_noVsync ? "COSTE (vsync apagado por la sonda)" : "PRESENTADO")} · " +
                      $"pantalla={Screen.width}×{Screen.height} · " +
                      $"refresco={Screen.currentResolution.refreshRateRatio.value:0.#} Hz · informe={_outPath}");
        }

        private static string ResolveAgainstRepo(string path, string? repo)
        {
            if (Path.IsPathRooted(path)) return path;
            return repo != null ? Path.GetFullPath(Path.Combine(repo, path)) : Path.GetFullPath(path);
        }

        private void Update()
        {
            if (_reported) return;

            // LO PRIMERO DE CADA FRAME: qué HUD está en el backbuffer que se
            // presentó al final del frame anterior. Se toma aquí y no en el cierre
            // porque el cierre captura ese backbuffer ya presentado, no el de ahora.
            SnapshotHud();

            float now = Time.realtimeSinceStartup;
            float elapsed = now - _start;
            _frames++;

            // El boost se re-aplica cada frame: el valor del componente manda en
            // Update y una recarga de escena lo devuelve a 1.
            if (_boost > 1f)
                foreach (var p in Presenters()) p.SpeedBoost = _boost;

            // Tiempos de frame: hay que CAPTURAR cada frame (la API entrega los datos
            // con retardo) y se acumulan hasta la muestra. `syncInterval` deja la
            // prueba de que la corrida de coste se hizo sin vsync de verdad.
            CollectTimings();

            // Las muestras empiezan DESPUÉS del calentamiento: el arranque (el
            // stream entrando, el mundo naciendo) no es el régimen que se mide.
            if (elapsed >= _warmup && now - _lastSampleAt >= 1f)
                TakeSample(elapsed, now);

            if (elapsed < _warmup + _seconds) return;

            // Ventana cumplida.
            _reported = true;
            int rc = Report();
            Application.Quit(rc);
        }

        private static SimPresenterBehaviour[] Presenters()
            => FindObjectsByType<SimPresenterBehaviour>();

        // ── El HUD (F5.1): descubrimiento, estado del frame anterior y medida ────

        /// <summary>
        /// Las tarjetas de colonia del montaje, en las dos formas del HUD. Se
        /// descubren UNA vez (una recarga de escena las rehace y el sondeo se
        /// repite solo si la lista quedó vacía).
        /// </summary>
        private void DiscoverHudCards()
        {
            if (_hudCards.Count > 0) return;

            // Juego de una vista: un texto por colonia, con la barra horizontal
            // debajo del texto.
            foreach (var hud in FindObjectsByType<HudLayoutBehaviour>())
            {
                var texts = hud.ColonyCardTexts;
                if (texts == null) continue;
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null) continue;
                    _hudCards.Add(new HudCardTarget
                    {
                        Name = $"colonia {i}",
                        Body = texts[i]!,
                        Bars = hud.StockBars,
                        Cards = hud.Cards,
                        ColonyId = i,
                    });
                }
            }

            // Multi-visor: una tarjeta compacta por vista (una línea por colonia).
            foreach (var card in FindObjectsByType<ViewCardBehaviour>())
            {
                if (card.Body == null) continue;
                _hudCards.Add(new HudCardTarget
                {
                    Name = $"vista {card.name}",
                    Body = card.Body!,
                    Bars = card.StockBars,
                    ExtraBars = card.FungusBars,
                    Cards = card.Cards,
                });
            }
        }

        /// <summary>
        /// El estado del HUD DE ESTE FRAME (que es el que se presentará al final): se
        /// refresca cada frame y es la expectativa con la que el cierre mide el frame
        /// ya presentado. Se guarda el texto pintado tal cual, así que la expectativa
        /// sale del propio HUD y no de una copia paralela.
        /// </summary>
        private void SnapshotHud()
        {
            DiscoverHudCards();
            if (_hudCards.Count == 0) return;

            if (_hudPrev.Count != _hudCards.Count)
            {
                _hudPrev.Clear();
                foreach (var t in _hudCards) _hudPrev.Add(new HudExpectation { Target = t });
            }
            foreach (var e in _hudPrev)
            {
                e.Ordered.Clear();
                e.Painted.Clear();
                foreach (var level in RunwayLevels(e.Target)) e.Ordered.Add(level);
                string text = e.Target.Body.text ?? "";
                HudInkModel.ChipsInText(text, e.Painted);
                e.AuthoredLines = HudInkModel.AuthoredLines(text);
            }
        }

        /// <summary>Los tramos de runway que el modelo pinta en ESA tarjeta (uno por
        /// colonia; la grande tiene un texto por colonia, la compacta todas).</summary>
        private static List<byte> RunwayLevels(HudCardTarget t)
        {
            var levels = new List<byte>();
            foreach (var c in t.Cards.Cards.Values)
            {
                if (t.ColonyId >= 0 && c.ColonyId != t.ColonyId) continue;
                if (c.Colony is not GameStreamParser.ColonyView view) continue;
                levels.Add(view.RunwayLevel);
            }
            levels.Sort();
            return HudInkModel.ExpectedChips(levels);
        }

        /// <summary>
        /// El chip de runway EN PANTALLA: captura el backbuffer (lo que el jugador ve,
        /// con el HUD ya compuesto), mide cada tarjeta con el modelo puro y devuelve
        /// los números y el veredicto. La geometría sale de los RectTransform —que es
        /// la única fuente de «dónde está cada cosa»— y los colores de los propios
        /// componentes; nada se supone.
        /// </summary>
        private HudReport MeasureHud()
        {
            var report = new HudReport();
            if (_hudPrev.Count == 0)
            {
                report.Why = "el montaje no tiene tarjetas de colonia";
                return report;
            }

            Texture2D? shot = null;
            byte[] rgb;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (shot == null)
                {
                    report.Why = "ScreenCapture no devolvió ni un frame";
                    return report;
                }
                var px = shot.GetPixels32();
                rgb = new byte[px.Length * 3];
                for (int i = 0; i < px.Length; i++)
                {
                    rgb[i * 3] = px[i].r;
                    rgb[i * 3 + 1] = px[i].g;
                    rgb[i * 3 + 2] = px[i].b;
                }
            }
            catch (Exception ex)
            {
                report.Why = $"{ex.GetType().Name}: {ex.Message}";
                if (shot != null) Destroy(shot);
                return report;
            }

            int cw = shot.width, ch = shot.height;
            // La escala se MIDE (captura / pantalla): en el editor la captura puede
            // venir a otra resolución y suponerla desplazaría todas las ventanas.
            float scale = cw / (float)Mathf.Max(1, Screen.width);
            report.Capture = $"{cw}x{ch}";
            report.Scale = scale;
            if (_dumpPath.Length > 0) report.Dump = TryDump(rgb, cw, ch);
            // La info del Canvas: sin ella un rect medido no se puede interpretar (el
            // CanvasScaler cambia las unidades de lo que la UI cree que es un píxel).
            var canvas0 = _hudPrev[0].Target.Body.canvas;
            if (canvas0 != null)
                report.Canvas = $"{canvas0.renderMode} · scaleFactor {canvas0.scaleFactor:0.###} · " +
                                $"pixelRect {canvas0.pixelRect.width:0}x{canvas0.pixelRect.height:0} · " +
                                $"localScale {canvas0.rootCanvas.transform.localScale.x:0.###}";

            // El ÁRBOL del HUD, en texto: nombres, rects y lo que dice cada texto. Es
            // la ventana de la medida puesta en un sitio donde se puede leer — sin esto,
            // un rect o un texto que no se pintan no tienen forma de explicarse.
            if (canvas0 != null) report.Tree = DescribeHudTree(canvas0, scale, cw, ch);

            // La geometría y el FONDO de cada tarjeta ANTES de los mosaicos: el fondo
            // (el panel medido) es lo que hace medible la cobertura de la tinta y de los
            // chips, y el mosaico de diagnóstico lo usa para su rejilla. Un fondo
            // supuesto en vez de medido daría una cobertura comparable consigo misma y
            // no con la del resto de tarjetas.
            foreach (var e in _hudPrev)
            {
                e.HasBody = TryScreenRect(e.Target.Body.rectTransform, scale, cw, ch, out e.Body);
                if (e.HasBody) e.Background = HudInkModel.Background(rgb, cw, ch, e.Body);
            }
            var screenBg = _hudPrev[0].HasBody ? _hudPrev[0].Background : new Rgb(49, 42, 35);
            Debug.Log($"{Tag} pantalla entera ('#' texto · letra = tramo del chip · 'e' tierra · '.' claro · ' ' oscuro):\n" +
                      ScreenMosaic(rgb, cw, ch, screenBg));

            foreach (var e in _hudPrev)
            {
                var t = e.Target;
                if (!e.HasBody)
                {
                    report.Measured = true;
                    report.Ok = false;
                    continue;
                }
                var body = e.Body;

                var bars = new List<PixelRect>();
                var excl = new List<PixelRect>();
                CollectBars(t.Bars, scale, cw, ch, bars, excl);
                CollectBars(t.ExtraBars, scale, cw, ch, bars, excl);

                // La ventana de las líneas: el rect del texto, y MÁS ancho si el
                // texto puede desbordar (el HUD grande no envuelve: el desborde hay
                // que verlo, no recortarlo). Las barras quedan excluidas: su verde
                // comparte paleta con el chip de RESERVA —a 27 de distancia— y sus
                // franjas cruzan las filas del texto, así que sin excluirlas
                // fusionarían todas las líneas en una y votarían como chip.
                // El ancho extra solo si el texto puede DESBORDAR de su rect (el HUD
                // grande no envuelve: el desborde hay que verlo, no recortarlo). En
                // vertical, el rect EXACTO: con 4 px de más se cuela el rótulo (misma
                // tinta, 8 px más arriba) y el tablero, y salen bandas de texto que no
                // son texto (medido: 4 bandas para 2 líneas escritas).
                int mx = t.Body.horizontalOverflow == HorizontalWrapMode.Overflow ? 24 : 4;
                var linesArea = PixelRect.FromScreen(body.X - mx, body.Y, body.W + 2 * mx, body.H,
                    1f, cw, ch);
                // El color declarado del texto SOBRE el fondo medido de la tarjeta: la
                // clasificación mide COBERTURA, no igualdad de color (el rasterizador de
                // fuentes no garantiza el color puro en glifos finos: medido 0,70–0,96).
                var ink = new PaintTarget(e.Background, MaterialRgbOf(t.Body.color));

                var lines = HudInkModel.Lines(rgb, cw, ch, linesArea, ink,
                    HudInkModel.MinRowInkPx, excl);
                var chips = HudInkModel.ChipsInLines(rgb, cw, ch, lines, e.Background, excl);

                // La franja que separa el texto de la barra: por debajo (HUD grande)
                // o a su izquierda (compacta). Si las barras PISAN el rect del texto
                // no hay franja que medir y el solape se publica como aire 0.
                var staging = default(PixelRect);
                bool overlaps = false;
                if (bars.Count > 0)
                {
                    int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue;
                    foreach (var b in bars)
                    {
                        if (b.X < bx0) bx0 = b.X;
                        if (b.Y < by0) by0 = b.Y;
                        if (b.X1 > bx1) bx1 = b.X1;
                        if (b.Y1 > by1) by1 = b.Y1;
                    }
                    if (by1 <= body.Y)
                        staging = PixelRect.Between(body.X - mx, by1 - 2, body.X1 + mx, body.Y + 2, cw, ch);
                    else if (bx1 <= body.X)
                        staging = PixelRect.Between(bx1 - 2, body.Y - 4, body.X + 2, body.Y1 + 4, cw, ch);
                    else
                        overlaps = true;
                }

                int stagingInk = staging.IsEmpty
                    ? 0
                    : HudInkModel.InkPixels(rgb, cw, ch, staging, ink);
                int barInk = 0;
                foreach (var b in bars) barInk += HudInkModel.InkPixels(rgb, cw, ch, b, ink);

                var measure = HudInkModel.Judge(t.Name, e.Ordered, e.AuthoredLines, lines, chips,
                    body.W, stagingInk, barInk, bars);
                measure.Background = e.Background;
                measure.BarsOverlapText = overlaps;
                measure.Geometry = "texto " + RawRect(t.Body.rectTransform) + " → píxeles " + body +
                                   " · barras " + (bars.Count == 0 ? "ninguna" : Rects(bars)) +
                                   " · bandas " + Bands(lines) + " · chips " + ChipsDetail(chips);
                measure.Mosaic = HudMosaic(rgb, cw, ch, linesArea, ink, excl);

                // La expectativa del texto pintado contra la del Core: si el HUD
                // escribiera un chip de otro tramo, los píxeles podrían ser coherentes
                // consigo mismos y el error pasaría por debajo.
                if (measure.Ok && !HudInkModel.SameChips(e.Ordered, e.Painted))
                {
                    measure.Ok = false;
                    measure.Verdict = "el texto del HUD declara los tramos " + Chips(e.Painted) +
                                      " y el Core ordenó " + Chips(e.Ordered);
                }
                report.Measures.Add(measure);
                if (!measure.Ok) report.Ok = false;

                foreach (var c in measure.Chips)
                {
                    if (c.Px < HudInkModel.MinChipPx) continue;
                    report.Chips++;
                    if (report.MinChipCover < 0 || c.Cover < report.MinChipCover) report.MinChipCover = c.Cover;
                    if (c.Cover > report.MaxChipCover) report.MaxChipCover = c.Cover;
                }
                if (measure.TitleWidth > report.WorstTitlePx)
                {
                    report.WorstTitlePx = measure.TitleWidth;
                    report.WorstTitleAvail = measure.AvailableWidth;
                }
                if (report.MinHeadroom == 0 || measure.TitleHeadroom < report.MinHeadroom)
                    report.MinHeadroom = measure.TitleHeadroom;
                if (measure.BarInkPx > report.MaxBarInk) report.MaxBarInk = measure.BarInkPx;
                if (measure.StagingInkPx > report.MaxStagingInk) report.MaxStagingInk = measure.StagingInkPx;
            }

            Destroy(shot);
            report.Measured = true;
            // Sin UN chip medido el verde sería vacío: medir nada no es medir bien.
            if (report.Chips == 0) report.Ok = false;
            return report;
        }

        /// <summary>La pantalla entera en rejilla de caracteres: el HUD y el tablero en
        /// una sola imagen de texto, para poder ver de un vistazo qué se pintó dónde.
        /// 96×26 celdas.</summary>
        private static string ScreenMosaic(byte[] rgb, int w, int h, Rgb background)
        {
            const int cols = 96, rows = 26;
            var sb = new StringBuilder();
            for (int row = rows - 1; row >= 0; row--)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("      ");
                for (int col = 0; col < cols; col++)
                {
                    int x0 = col * w / cols, x1 = Mathf.Max(x0 + 1, (col + 1) * w / cols);
                    int y0 = row * h / rows, y1 = Mathf.Max(y0 + 1, (row + 1) * h / rows);
                    char cell = ' ';
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int i = (y * w + x) * 3;
                            var p = new Rgb(rgb[i], rgb[i + 1], rgb[i + 2]);
                            if (HudInkModel.LooksLikeInk(p, Tinta)) { cell = '#'; goto next; }
                            byte state = HudInkModel.NearestChip(p, background);
                            if (state != 0) { if (cell == ' ') cell = Tramo(state); }
                            else if (cell == ' ')
                            {
                                // Tierra del tablero: los tres canales cálidos que usa
                                // la puerta de píxeles (r > g > b, ninguno apagado).
                                cell = p.R > 60 && p.R > p.G && p.G > p.B ? 'e'
                                     : (p.R + p.G + p.B) / 3 > 90 ? '.' : ' ';
                            }
                        }
                    next:
                    sb.Append(cell);
                }
            }
            return sb.ToString();
        }

        /// <summary>La tinta del HUD (color de los textos de las tarjetas y rótulos).</summary>
        private static readonly Rgb Tinta = new Rgb(237, 232, 219);

        /// <summary>
        /// El volcado CRUDO de la captura: cabecera ASCII «ANTSIMRGB w h» + los
        /// píxeles R,G,B. Existe porque una medida que suspende necesita su prueba:
        /// el mosaico dice DÓNDE falta la tinta y el volcado dice QUÉ hay en su
        /// lugar (una tarjeta sin pintar y una tarjeta pintada con un color que no
        /// llega al umbral de tinta se parecen en el log y no se parecen en nada).
        /// Un fallo al escribir NO tumba la corrida: se publica el motivo.
        /// </summary>
        private string TryDump(byte[] rgb, int w, int h)
        {
            try
            {
                var head = System.Text.Encoding.ASCII.GetBytes($"ANTSIMRGB {w} {h}\n");
                var bytes = new byte[head.Length + rgb.Length];
                Buffer.BlockCopy(head, 0, bytes, 0, head.Length);
                Buffer.BlockCopy(rgb, 0, bytes, head.Length, rgb.Length);
                var dir = Path.GetDirectoryName(_dumpPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(_dumpPath, bytes);
                return _dumpPath;
            }
            catch (Exception ex)
            {
                return $"(no se pudo volcar: {ex.GetType().Name}: {ex.Message})";
            }
        }

        /// <summary>El árbol del Canvas en texto: por elemento, su rect en píxeles y el
        /// texto que lleva (recortado). Un volcado del HUD de una corrida entera pesa
        /// media pantalla de log y ahorra horas de conjeturas.</summary>
        private static string DescribeHudTree(Canvas canvas, float scale, int cw, int ch)
        {
            var sb = new StringBuilder();
            var all = canvas.GetComponentsInChildren<RectTransform>(false);
            foreach (var rt in all)
            {
                if (rt == canvas.transform) continue;
                string rect = TryScreenRect(rt, scale, cw, ch, out var r)
                    ? r.X + "," + r.Y + " " + r.W + "x" + r.H
                    : "(vacío)";
                sb.Append("\n    ").Append(ElementPath(rt)).Append(" ").Append(rect);
                var cr = rt.GetComponent<CanvasRenderer>();
                if (cr != null) sb.Append(" cull=").Append(cr.cull ? "sí" : "no");
                var graphic = rt.GetComponent<UnityEngine.UI.Graphic>();
                if (graphic != null)
                    sb.Append(" a=").Append(graphic.color.a.ToString("0.##", CultureInfo.InvariantCulture))
                      .Append(graphic.enabled ? "" : " DESHABILITADO");
                var text = rt.GetComponent<UnityEngine.UI.Text>();
                if (text != null)
                    sb.Append(" \"").Append(Prefix(text.text, 70)).Append('"');
            }
            return sb.ToString();
        }

        private static string ElementPath(RectTransform rt)
        {
            var sb = new StringBuilder(rt.name);
            var p = rt.parent;
            while (p != null && p is RectTransform)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private static string Prefix(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\n', '|').Replace("\r", "");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>
        /// La ventana medida como rejilla de caracteres: '#' = tinta del texto, la
        /// INICIAL del tramo = píxeles de la paleta del chip (C/R/O/A/D), ' ' = el fondo.
        /// Es el mismo recurso que el mosaico del mundo y por la misma razón: una medida
        /// en texto se puede auditar desde un log, un PNG no.
        /// </summary>
        private static string HudMosaic(byte[] rgb, int w, int h, PixelRect area, PaintTarget ink,
            System.Collections.Generic.IReadOnlyList<PixelRect> exclude)
        {
            const int cols = 72, rows = 10;
            var sb = new StringBuilder();
            for (int row = rows - 1; row >= 0; row--)   // fila de arriba primero
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("      ");
                for (int col = 0; col < cols; col++)
                {
                    int x0 = area.X + col * area.W / cols, x1 = area.X + (col + 1) * area.W / cols;
                    int y0 = area.Y + row * area.H / rows, y1 = area.Y + (row + 1) * area.H / rows;
                    char cell = ' ';
                    for (int y = y0; y < y1 && y < h && cell != '#'; y++)
                        for (int x = x0; x < x1 && x < w; x++)
                        {
                            if (x < 0 || y < 0 || PixelRect.InAny(exclude, x, y)) continue;
                            int i = (y * w + x) * 3;
                            var p = new Rgb(rgb[i], rgb[i + 1], rgb[i + 2]);
                            if (HudInkModel.IsTarget(p, ink, HudInkModel.InkResidual)) { cell = '#'; break; }
                            byte state = HudInkModel.NearestChip(p, ink.Background);
                            if (state != 0 && cell == ' ') cell = Tramo(state);
                        }
                    sb.Append(cell);
                }
            }
            return sb.ToString();
        }

        /// <summary>Inicial del tramo para el mosaico (C aníbal y C rítico comparten
        /// letra: lo que importa es ver DÓNDE hay color de chip, no cuál).</summary>
        private static char Tramo(byte state) => state switch
        {
            1 => '!',   // crítico
            2 => 'c',   // caníbal
            3 => 'o',   // oofagia
            4 => 'r',   // reserva
            5 => 'd',   // desahogo
            _ => '?',
        };

        private static void CollectBars(UnityEngine.UI.Image?[]? images, float scale, int cw, int ch,
            List<PixelRect> bars, List<PixelRect> excl)
        {
            if (images == null) return;
            foreach (var img in images)
            {
                if (img == null) continue;
                if (!TryScreenRect(img.rectTransform, scale, cw, ch, out var b)) continue;
                bars.Add(b);
                excl.Add(b.Inflated(1, cw, ch));
            }
        }

        /// <summary>El rect CRUDO de un RectTransform en coordenadas de pantalla, sin
        /// normalizar: si el layout lo dejó con ancho negativo, aquí se ve el signo.</summary>
        private static string RawRect(RectTransform rt)
        {
            if (rt == null) return "(sin rect)";
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.min));
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.max));
            return $"[{min.x:0.#},{min.y:0.#} → {max.x:0.#},{max.y:0.#}] (rect {rt.rect.width:0.#}x{rt.rect.height:0.#})";
        }

        private static string Rects(List<PixelRect> rects)
        {
            var sb = new StringBuilder();
            foreach (var r in rects)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(r);
            }
            return sb.ToString();
        }

        /// <summary>Rect de pantalla de un RectTransform en píxeles de la captura.
        /// `null` como cámara: un Canvas en Screen Space Overlay trabaja en
        /// coordenadas de pantalla y así es como se pide su rect.</summary>
        private static bool TryScreenRect(RectTransform rt, float scale, int cw, int ch, out PixelRect rect)
        {
            rect = default;
            if (rt == null) return false;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.min));
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.max));
            rect = PixelRect.FromScreen(
                Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), scale, cw, ch);
            return rect.W > 0 && rect.H > 0;
        }

        private static Rgb MaterialRgbOf(Color c)
            => new Rgb(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255));

        private static string Chips(System.Collections.Generic.IReadOnlyList<byte> states)
        {
            if (states.Count == 0) return "(ninguno)";
            var sb = new StringBuilder();
            foreach (var s in states)
            {
                if (sb.Length > 0) sb.Append('+');
                sb.Append(RunwayChipModel.Label(s));
            }
            return sb.ToString();
        }

        /// <summary>Una muestra por segundo: frames REALES contados entre muestras
        /// divididos por el tiempo real transcurrido (no por un dt supuesto).</summary>
        private void TakeSample(float seconds, float now)
        {
            int drawCalls = 0, instanced = 0, fallback = 0, ants = 0, items = 0;
            ulong tick = 0, tickSum = 0;
            double cliCpuMs = 0.0;
            foreach (var p in Presenters())
            {
                drawCalls += p.LastDrawCalls;
                instanced += p.LastInstancedCalls;
                fallback += p.LastFallbackCalls;
                var state = p.CurrentState;
                if (state != null) { ants += state.Ants.Count; items += state.Items.Count; }
                ulong t = p.Presenter?.CurrentTick?.Tick ?? 0;
                if (t > tick) tick = t;
                tickSum += t;
                // La pata del CLI se lee en el MISMO instante que el coste del frame:
                // emparejar ventanas de procesos distintos es lo único que hace
                // comparable el número del sistema.
                cliCpuMs += p.CliCpuMs;
            }
            double dt = now - _lastSampleAt;
            long windowFrames = _frames - _lastSampleFrames;
            double fps = dt > 0.001 ? windowFrames / dt : 0;
            var timing = new FrameTimingSample(
                FrameCost.Median(_cpuMs), FrameCost.Median(_mainMs),
                FrameCost.Median(_renderMs), FrameCost.Median(_waitMs),
                FrameCost.Median(_gpuMs));
            _samples.Add(new Sample(seconds, fps, drawCalls, instanced, fallback, ants, items,
                tick, Application.isFocused, timing, windowFrames, _lastSyncInterval,
                cliCpuMs, tickSum));
            _cpuMs.Clear(); _mainMs.Clear(); _renderMs.Clear(); _waitMs.Clear(); _gpuMs.Clear();
            _lastSampleAt = now;
            _lastSampleFrames = _frames;
        }

        /// <summary>Un frame de tiempos del FrameTimingManager por Update. Sin
        /// `enableFrameTimingStats` en el build (lo enciende `PlayerBuild`) la API
        /// devuelve 0 frames y el informe lo declara en vez de inventar un cero.
        /// </summary>
        private void CollectTimings()
        {
            FrameTimingManager.CaptureFrameTimings();
            uint n = FrameTimingManager.GetLatestTimings(1u, _timingOne);
            if (n == 0) return;
            var t = _timingOne[0];
            _lastSyncInterval = t.syncInterval;
            if (t.cpuFrameTime <= 0.0 && t.cpuMainThreadFrameTime <= 0.0 && t.gpuFrameTime <= 0.0) return;
            _cpuMs.Add(t.cpuFrameTime);
            _mainMs.Add(t.cpuMainThreadFrameTime);
            _renderMs.Add(t.cpuRenderThreadFrameTime);
            _waitMs.Add(t.cpuMainThreadPresentWaitTime);
            _gpuMs.Add(t.gpuFrameTime);
            _timingsTaken++;
        }

        private int Report()
        {
            var fps = new List<double>();
            foreach (var s in _samples) fps.Add(s.Fps);
            double median = FrameCost.Median(fps);
            var ordered = new List<double>(fps);
            ordered.Sort();
            double worst = ordered.Count > 0 ? ordered[0] : 0;
            double best = ordered.Count > 0 ? ordered[ordered.Count - 1] : 0;
            var last = _samples.Count > 0 ? _samples[_samples.Count - 1] : default;

            // El COSTE por frame de la corrida (5ª pieza del criterio): se resume con
            // el modelo puro, el mismo que prueban los tests headless.
            var windows = new List<FrameCostWindow>();
            foreach (var s in _samples)
            {
                double windowSeconds = s.WindowFrames > 0 && s.Fps > 0 ? s.WindowFrames / s.Fps : 0.0;
                windows.Add(new FrameCostWindow(windowSeconds, s.WindowFrames, s.Timing));
            }
            var cost = FrameCost.Summarize(windows);

            // ── El coste del SISTEMA COMPLETO (F5.3, 6ª pieza) ──────────────────
            //    El frame del player MÁS la simulación del CLI. Las dos patas se leen
            //    en el mismo instante de muestreo, pero OCURREN en fases distintas
            //    (el CLI simula y luego el player reproduce): el modelo se queda con
            //    cada una en las ventanas donde pasa, y declara el solape.
            //    El régimen al que se proyecta es el del JUEGO: los ticks que el
            //    reloj de simulación pide por frame a la frecuencia objetivo (el
            //    número sale del propio presenter, no de una constante inventada).
            var systemWindows = new List<SystemWindow>();
            for (int i = 1; i < _samples.Count; i++)
            {
                var prev = _samples[i - 1];
                var cur = _samples[i];
                double winSeconds = cur.WindowFrames > 0 && cur.Fps > 0 ? cur.WindowFrames / cur.Fps : 0.0;
                double cliDelta = cur.CliCpuMs - prev.CliCpuMs;
                long tickDelta = cur.TickSum >= prev.TickSum ? (long)(cur.TickSum - prev.TickSum) : 0L;
                systemWindows.Add(new SystemWindow(winSeconds, cur.WindowFrames,
                    cur.Timing.CpuMs, Math.Max(0.0, cliDelta), tickDelta, cur.Ants > 0));
            }

            // La pata del mundo, por AGREGADO: toda la CPU de los CLI y todos los
            // ticks que simularon. La CPU del hijo se gasta antes de que los ticks
            // lleguen (el stream sale en ráfaga al final), así que emparejarlos
            // ventana a ventana daba 0,016 ms/tick en vez de 0,2 (medido).
            double cliCpuTotal = 0.0, ticksPerPresentedFrame = 0.0;
            long cliTicksTotal = 0;
            bool cliCompleted = true;
            foreach (var p in Presenters())
            {
                cliCpuTotal += p.CliCpuMs;
                ulong tick = p.Presenter?.CurrentTick?.Tick ?? 0;
                cliTicksTotal += (long)tick;
                // Terminado = el hilo del stream acabó (el CLI salió) Y entregó su
                // horizonte entero. Con la mitad del mundo simulado, la CPU es
                // parcial y el precio por tick no vale.
                if (p.IsStreaming) cliCompleted = false;
                if (tick < (ulong)Math.Max(0, p.Ticks)) cliCompleted = false;
                // Ticks que avanza UN frame sumando todas las vistas: el reloj de
                // cada presenter al repartirlo entre los frames del objetivo.
                ticksPerPresentedFrame += (double)p.ClockTicksPerSecond / FrameCost.DefaultTargetFps;
            }
            var system = FrameCost.SummarizeSystem(systemWindows, cliCpuTotal, cliTicksTotal,
                ticksPerPresentedFrame, cliCompleted);

            var gates = GateViews();
            bool gateOk = gates.Count > 0;
            foreach (var g in gates) if (!g.Result.Ok) gateOk = false;

            // El HUD: el chip de runway EN PANTALLA (F5.1). Va aquí, y no por frame,
            // porque lo que se mide es el frame PRESENTADO al cierre.
            var hud = MeasureHud();

            float refresh = (float)Screen.currentResolution.refreshRateRatio.value;
            Debug.Log($"{Tag} condiciones: vsync={QualitySettings.vSyncCount} (proyecto={_projectVSync}) · " +
                      $"targetFrameRate={Application.targetFrameRate} · pantalla={Screen.width}×{Screen.height} · " +
                      $"refresco={refresh:0.#} Hz · con foco={Focused()}/{_samples.Count} muestras");
            Debug.Log($"{Tag} frames/s: mediana={median:0.#} · peor={worst:0.#} · mejor={best:0.#} · " +
                      $"frames={_frames} · tick final={last.Tick}");
            if (_noVsync)
                Debug.Log($"{Tag} coste/frame: {FrameCost.Describe(cost)}");
            if (_system)
                Debug.Log($"{Tag} coste/SISTEMA: {FrameCost.DescribeSystem(system)}");
            Debug.Log($"{Tag} draw calls={last.DrawCalls} (instanciadas={last.Instanced}, respaldo={last.Fallback}) · " +
                      $"carga final={last.Ants} hormigas y {last.Items} ítems");
            foreach (var g in gates)
            {
                Debug.Log($"{Tag} vista {g.Index} ({g.Camera}): {g.Result.Describe()} → {g.Result.Verdict}");
                if (g.Mosaic.Length > 0) Debug.Log($"{Tag} mosaico vista {g.Index}: {g.Mosaic}");
            }
            if (hud.Measured)
            {
                Debug.Log($"{Tag} HUD en pantalla: {hud.Measures.Count} tarjetas · captura {hud.Capture} · " +
                          $"escala {hud.Scale:0.###} · canvas {hud.Canvas} · {hud.Chips} chips medidos");
                if (hud.Dump.Length > 0) Debug.Log($"{Tag} captura cruda volcada en {hud.Dump}");
                foreach (var m in hud.Measures)
                    Debug.Log($"{Tag} HUD {m.Name}: {m.Verdict} · línea más ancha {m.TitleWidth}/{m.AvailableWidth} px " +
                              $"({m.TitleHeadroom} de aire) · líneas {m.Lines.Count}/{m.AuthoredLines} · " +
                              $"franja {(m.BarsOverlapText ? "solapada por las barras" : m.StagingInkPx + " px de tinta")} · " +
                              $"barra {m.BarInkPx} px de tinta · aire a la barra {m.BarGapPx} px → " +
                              (m.Ok ? "ok" : "SUSPENDE"));
                foreach (var m in hud.Measures)
                {
                    Debug.Log($"{Tag} geometría {m.Name}: {m.Geometry}");
                    if (m.Mosaic.Length > 0) Debug.Log($"{Tag} mosaico HUD {m.Name} (arriba abajo, '#' texto · letra = tramo del chip):\n{m.Mosaic}");
                }
                if (hud.Tree.Length > 0) Debug.Log($"{Tag} árbol del HUD:{hud.Tree}");
            }
            else
            {
                Debug.Log($"{Tag} HUD sin medir: {hud.Why}");
            }

            WriteJson(median, worst, best, last, gates, gateOk, refresh, cost, system, hud);

            if (_samples.Count == 0)
            {
                Debug.Log($"{Tag} ✗ SIN MUESTRAS: el player no llegó a correr la ventana");
                return 2;
            }
            if (last.Ants == 0 || last.DrawCalls == 0)
            {
                Debug.Log($"{Tag} ✗ MEDICIÓN VACÍA: {last.Ants} hormigas y {last.DrawCalls} draw calls " +
                          "al cierre — un verde aquí sería un falso verde");
                return 1;
            }
            if (!gateOk)
            {
                Debug.Log($"{Tag} ✗ la puerta de píxeles suspende en alguna vista (¿tablero sin tierra o sin hormigas?)");
                return 1;
            }
            // El HUD se mide o no se mide: «no medido» no es un verde (una corrida sin
            // captura no dice nada del chip, y darla por buena sería el falso verde que
            // esta pieza existe para evitar).
            if (!hud.Measured)
            {
                Debug.Log($"{Tag} ✗ SIN MEDIDA DEL HUD EN PANTALLA: {hud.Why}");
                return 4;
            }
            if (!hud.Ok)
            {
                Debug.Log($"{Tag} ✗ el chip de runway no pasa la medición en pantalla " +
                          $"({hud.Chips} chips medidos en {hud.Measures.Count} tarjetas)");
                return 1;
            }
            Debug.Log($"{Tag} ✓ HUD: {hud.Chips} chips medidos en pantalla sobre {hud.Measures.Count} tarjetas · " +
                      $"cobertura del chip {hud.MinChipCover:0.##}–{hud.MaxChipCover:0.##} · línea más ancha {hud.WorstTitlePx}/" +
                      $"{hud.WorstTitleAvail} px (aire {hud.MinHeadroom}) · tinta en la franja {hud.MaxStagingInk} px · " +
                      $"dentro de la barra {hud.MaxBarInk} px");
            // En modo coste, «no medido» no es un verde: sin tiempos de frame lo que
            // hay es un framerate, que es justo el número que esta corrida existe
            // para no publicar como coste.
            if (_noVsync && !cost.Measured)
            {
                Debug.Log($"{Tag} ✗ MODO COSTE SIN TIEMPOS DE FRAME: {_timingsTaken} frames con dato del " +
                          "FrameTimingManager — falta enableFrameTimingStats en el build");
                return 3;
            }
            // El coste del SISTEMA solo es del sistema si el CLI estaba simulando
            // dentro de la ventana: con el mundo terminado lo medido es un player
            // solo, y publicarlo como «sistema» sería el error que esta pieza evita.
            if (_system && !system.Measured)
            {
                Debug.Log($"{Tag} ✗ MODO SISTEMA INCOMPLETO: {system.LoadedWindows} ventanas con el mundo en " +
                          $"pantalla de {system.Windows} (mínimo {FrameCost.MinimumWindowsForALeg}), CLI terminado " +
                          $"dentro de la corrida={system.CliCompleted}, cliMsPerTick={system.CliMsPerTick:0.###}");
                return 10;
            }
            if (_noVsync)
            {
                if (!cost.Fits)
                {
                    Debug.Log($"{Tag} ✗ el coste de CPU NO cabe en el presupuesto de {cost.BudgetMs:0.#} ms: " +
                              FrameCost.Describe(cost));
                    return 1;
                }
                Debug.Log($"{Tag} ✓ coste medido: {FrameCost.Describe(cost)}");
                if (_system && !system.PlayerFits)
                {
                    Debug.Log($"{Tag} ✗ la pata del PLAYER del sistema NO cabe en " +
                              $"{system.BudgetMs:0.#} ms: {FrameCost.DescribeSystem(system)}");
                    return 1;
                }
                if (_system) Debug.Log($"{Tag} ✓ coste del sistema medido: {FrameCost.DescribeSystem(system)}");
                return 0;
            }
            Debug.Log($"{Tag} ✓ medido: {last.Ants} hormigas, {last.DrawCalls} draw calls, " +
                      $"mediana {median:0.#} frames/s con vsync del monitor");
            return 0;
        }

        /// <summary>
        /// Puerta de píxeles por VISTA. Cada cámara se renderiza a un RenderTexture
        /// propio con el rect a pantalla completa (restaurado en el acto: esto corre
        /// en Update, antes del render de la pantalla, así que no altera lo que el
        /// jugador ve) y se analiza con la MISMA puerta que el Play pass del editor.
        ///
        /// La ventana de muestreo NO es la del editor: se CALCULA del encuadre real.
        /// Una vista 2×2 en 16:9 es más ancha que el tablero (la cámara lo encuadra a
        /// lo alto), así que con la ventana fija de 0.15–0.85 la mitad de las muestras
        /// caerían fuera del tablero y la «mediana del suelo» sería el fondo.
        /// </summary>
        private List<ViewGate> GateViews()
        {
            var gates = new List<ViewGate>();
            var cams = Camera.allCameras;
            Array.Sort(cams, (a, b) => a.depth.CompareTo(b.depth));
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null || !cam.enabled || cam.cullingMask == 0) continue;
                var presenter = PresenterFor(cam);
                var gate = new ViewGate { Index = i, Camera = cam.name };
                try
                {
                    gate.Result = GateOne(cam, presenter, out float window, out string mosaic);
                    gate.Window = window;
                    gate.Mosaic = mosaic;
                }
                catch (Exception ex)
                {
                    Debug.Log($"{Tag} (aviso) no se pudo medir la vista {i}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                gates.Add(gate);
            }
            return gates;
        }

        private static SimPresenterBehaviour? PresenterFor(Camera cam)
        {
            foreach (var p in Presenters())
                if ((cam.cullingMask & (1 << p.RenderLayer)) != 0) return p;
            return null;
        }

        private FrameGateResult GateOne(Camera cam, SimPresenterBehaviour? presenter,
            out float window, out string mosaic)
        {
            mosaic = "";
            int rtW = 320;
            int rtH = Mathf.Max(1, Mathf.RoundToInt(rtW * (float)Screen.height / Mathf.Max(1, Screen.width)));

            // Encaje del tablero DENTRO de la vista: la cámara lo encuadra con un 4 %
            // de margen, así que la mitad del mundo cae en (mundo/2)/(ortho·aspecto)
            // del semiancho. Se deja un 15 % extra de margen para no rozar el borde.
            int grid = presenter != null && presenter.Grid > 0 ? presenter.Grid : 96;
            float halfWorld = Streaming.WorldUnits.WorldSize(grid) * 0.5f;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float halfX = halfWorld / Mathf.Max(0.001f, cam.orthographicSize * aspect) * 0.85f;
            float halfY = halfWorld / Mathf.Max(0.001f, cam.orthographicSize) * 0.85f;
            halfX = Mathf.Clamp(halfX, 0.05f, 0.48f);
            halfY = Mathf.Clamp(halfY, 0.05f, 0.48f);
            window = halfX;

            // Familias de color: el multi-visor pinta las hormigas con los
            // materiales POR COLONIA (`ColonyAntMaterials`), no con `AntMaterial`;
            // con un solo color la puerta suspendía con antPx=0 sobre un tablero con
            // 292 hormigas dibujadas (medido en el primer build, 04/09).
            var ants = Family(presenter?.ColonyAntMaterials, presenter?.AntMaterial);
            var carriers = Family(presenter?.ColonyCarrierMaterials, presenter?.CarrierMaterial);
            var items = Family(null, presenter?.ItemMaterial);
            var background = new Rgb(
                (byte)Mathf.RoundToInt(cam.backgroundColor.r * 255f),
                (byte)Mathf.RoundToInt(cam.backgroundColor.g * 255f),
                (byte)Mathf.RoundToInt(cam.backgroundColor.b * 255f));

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24);
            var prevRect = cam.rect;
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.rect = new Rect(0f, 0f, 1f, 1f);   // la vista entera, sin recorte de mosaico
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rtW, rtH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                tex.Apply();
                var px = tex.GetPixels32();
                var bytes = new byte[rtW * rtH * 3];
                for (int i = 0; i < px.Length; i++)
                {
                    bytes[i * 3] = px[i].r;
                    bytes[i * 3 + 1] = px[i].g;
                    bytes[i * 3 + 2] = px[i].b;
                }
                Destroy(tex);
                var result = FrameGate.Analyze(bytes, rtW, rtH, ants, carriers, items,
                    FrameWindow.Centered(halfX, halfY), background);
                mosaic = Mosaic(bytes, rtW, rtH, ants, carriers, items, result.Floor, background);
                return result;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.rect = prevRect;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// El frame de una vista como rejilla de caracteres (24×10). Un agente que
        /// lee stdout no puede mirar un PNG, y sin el módulo Screen Capture tampoco
        /// hay PNG: esto deja el mundo inspeccionable en texto, que es lo que se
        /// necesita para saber si el player PINTA lo que dice el estado.
        ///   'e' tierra · 'a' hormiga · 'c' portadora · 'i' ítem · '#' oscuro · ' ' resto.
        /// </summary>
        private static string Mosaic(byte[] rgb, int w, int h, IReadOnlyList<Rgb> ants,
            IReadOnlyList<Rgb> carriers, IReadOnlyList<Rgb> items, Rgb floor, Rgb background)
        {
            const int cols = 24, rows = 10;
            var sb = new StringBuilder();
            for (int cy = rows - 1; cy >= 0; cy--)   // fila 0 del buffer = abajo
            {
                for (int cx = 0; cx < cols; cx++)
                {
                    int x0 = cx * w / cols, x1 = Mathf.Max(x0 + 1, (cx + 1) * w / cols);
                    int y0 = cy * h / rows, y1 = Mathf.Max(y0 + 1, (cy + 1) * h / rows);
                    int antsPx = 0, carrierPx = 0, itemPx = 0, earth = 0, dark = 0, n = 0;
                    for (int y = y0; y < y1; y += 2)
                        for (int x = x0; x < x1; x += 2)
                        {
                            int i = (y * w + x) * 3;
                            var p = new Rgb(rgb[i], rgb[i + 1], rgb[i + 2]);
                            n++;
                            if (NearAny(p, ants)) antsPx++;
                            else if (NearAny(p, carriers)) carrierPx++;
                            else if (NearAny(p, items)) itemPx++;
                            else if (Near(p, floor, 12)) earth++;
                            else if (Near(p, background, 12)) { }
                            if (p.R < 40 && p.G < 40 && p.B < 40) dark++;
                        }
                    // Hormigas, portadoras e ítems se marcan con UN píxel: en una
                    // rejilla de 24×10 sobre un RT de 320×180 una hormiga ocupa ~2 px
                    // de 63 muestras por celda, así que exigir mayoría escondería
                    // justo lo que se quiere ver. La MEDIDA son los conteos de la
                    // puerta; esto es solo la vista.
                    if (n == 0) { sb.Append('?'); continue; }
                    if (antsPx > 0) sb.Append('a');
                    else if (carrierPx > 0) sb.Append('c');
                    else if (itemPx > 0) sb.Append('i');
                    else if (dark * 2 > n) sb.Append('#');
                    else if (earth * 2 > n) sb.Append('e');
                    else sb.Append(' ');
                }
                sb.Append('/');
            }
            return sb.ToString();
        }

        private static bool NearAny(Rgb p, IReadOnlyList<Rgb> colors)
        {
            for (int i = 0; i < colors.Count; i++) if (Near(p, colors[i], 12)) return true;
            return false;
        }

        /// <summary>Colores de una familia: los materiales POR COLONIA si los hay
        /// (el caso del multi-visor) y, si no, el material único del presenter.
        /// Nunca devuelve una lista vacía: la puerta necesita la familia declarada
        /// para no atribuir sus píxeles a otra.</summary>
        private static List<Rgb> Family(Material[]? perColony, Material? single)
        {
            var list = new List<Rgb>();
            if (perColony != null)
                foreach (var m in perColony) if (m != null) list.Add(MaterialRgb(m));
            if (list.Count == 0) list.Add(MaterialRgb(single));
            return list;
        }

        private static bool Near(Rgb a, Rgb b, int tol)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            if (dr < 0) dr = -dr;
            if (dg < 0) dg = -dg;
            if (db < 0) db = -db;
            return dr <= tol && dg <= tol && db <= tol;
        }

        private static Rgb MaterialRgb(Material? m)
        {
            if (m == null) return new Rgb(0, 0, 0);
            Color c = m.color;
            return new Rgb(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255));
        }

        private int Focused()
        {
            int n = 0;
            foreach (var s in _samples) if (s.Focused) n++;
            return n;
        }

        private void WriteJson(double median, double worst, double best, Sample last,
            List<ViewGate> gates, bool gateOk, float refresh, FrameCostSummary cost,
            SystemCostSummary system, HudReport hud)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"probe\": \"player-perf\",\n");
                sb.Append("  \"mode\": \"")
                  .Append(_system ? "player-system" : _noVsync ? "player-cpu" : "player").Append("\",\n");
                sb.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
                sb.Append("  \"vSyncCount\": ").Append(QualitySettings.vSyncCount).Append(",\n");
                sb.Append("  \"projectVSyncCount\": ").Append(_projectVSync).Append(",\n");
                sb.Append("  \"targetFrameRate\": ").Append(Application.targetFrameRate).Append(",\n");
                sb.Append("  \"refreshRateHz\": ").Append(F(refresh)).Append(",\n");
                sb.Append("  \"screenWidth\": ").Append(Screen.width).Append(",\n");
                sb.Append("  \"screenHeight\": ").Append(Screen.height).Append(",\n");
                sb.Append("  \"windowSeconds\": ").Append(F(_seconds)).Append(",\n");
                sb.Append("  \"warmupSeconds\": ").Append(F(_warmup)).Append(",\n");
                sb.Append("  \"boost\": ").Append(F(_boost)).Append(",\n");
                sb.Append("  \"views\": ").Append(gates.Count).Append(",\n");
                // Las colonias POR VISTA del montaje: es lo que el barrido del precio
                // del tick mueve (F5.3 §4.10), y sin declararlo el informe no se puede
                // leer (el coste del mundo depende de cuántas colonias simula).
                var colonies = Presenters();
                sb.Append("  \"coloniesPerView\": ")
                  .Append(colonies.Length > 0 ? colonies[0].Colonies : 0).Append(",\n");
                sb.Append("  \"focusedSamples\": ").Append(Focused()).Append(",\n");
                sb.Append("  \"samples\": ").Append(_samples.Count).Append(",\n");
                sb.Append("  \"frames\": ").Append(_frames).Append(",\n");
                sb.Append("  \"fpsMedian\": ").Append(F(median)).Append(",\n");
                sb.Append("  \"fpsWorst\": ").Append(F(worst)).Append(",\n");
                sb.Append("  \"fpsBest\": ").Append(F(best)).Append(",\n");
                // OJO con el nombre: esto es el INTERVALO presentado (1000/fps), no
                // el coste del frame. El coste lo mide la vista en el editor
                // (`perf-scene.sh`: 2,44 ms/frame con presentación); aquí el número
                // solo dice a qué ritmo entrega el build con vsync.
                sb.Append("  \"presentedIntervalMs\": ").Append(F(1000.0 / Math.Max(1.0, median))).Append(",\n");
                sb.Append("  \"presentedAtRefresh\": ")
                  .Append(refresh > 1f && Math.Abs(median - refresh) < refresh * 0.05 ? "true" : "false").Append(",\n");
                // ── El COSTE por frame medido DENTRO del build (modo coste) ────
                sb.Append("  \"frameTimingsTaken\": ").Append(_timingsTaken).Append(",\n");
                sb.Append("  \"syncInterval\": ").Append(last.SyncInterval).Append(",\n");
                sb.Append("  \"achievedMsMedian\": ").Append(F(cost.AchievedMsMedian)).Append(",\n");
                sb.Append("  \"cpuMsMedian\": ").Append(F(cost.CpuMsMedian)).Append(",\n");
                sb.Append("  \"cpuMainMsMedian\": ").Append(F(cost.MainMsMedian)).Append(",\n");
                sb.Append("  \"cpuRenderMsMedian\": ").Append(F(cost.RenderMsMedian)).Append(",\n");
                sb.Append("  \"presentWaitMsMedian\": ").Append(F(cost.PresentWaitMsMedian)).Append(",\n");
                sb.Append("  \"gpuMsMedian\": ").Append(F(cost.GpuMsMedian)).Append(",\n");
                sb.Append("  \"gpuAvailable\": ").Append(cost.GpuAvailable ? "true" : "false").Append(",\n");
                sb.Append("  \"budgetMs\": ").Append(F(cost.BudgetMs)).Append(",\n");
                sb.Append("  \"cpuFractionOfBudget\": ").Append(F(cost.CpuFractionOfBudget)).Append(",\n");
                sb.Append("  \"costHeadroom\": ").Append(F(cost.Headroom)).Append(",\n");
                sb.Append("  \"costMeasured\": ").Append(cost.Measured ? "true" : "false").Append(",\n");
                sb.Append("  \"costFits\": ").Append(cost.Fits ? "true" : "false").Append(",\n");
                sb.Append("  \"bottleneck\": \"").Append(FrameCost.Name(cost.Bottleneck)).Append("\",\n");
                sb.Append("  \"costVerdict\": \"").Append(cost.Verdict).Append("\",\n");
                // ── El SISTEMA COMPLETO (player + CLI) — 6ª pieza del criterio ──
                sb.Append("  \"cliCpuMsTotal\": ").Append(F(system.CliCpuMsTotal)).Append(",\n");
                sb.Append("  \"cliTicksTotal\": ").Append(system.CliTicks).Append(",\n");
                sb.Append("  \"cliCompleted\": ").Append(system.CliCompleted ? "true" : "false").Append(",\n");
                // La CPU de cada CLI por separado: si un proceso no se dejó
                // cronometrar, la suma taparía su cero y el precio por tick saldría
                // barato sin que nadie lo notara.
                sb.Append("  \"cliCpuMsViews\": [");
                var views = Presenters();
                for (int i = 0; i < views.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(F(views[i].CliCpuMs));
                }
                sb.Append("],\n");
                sb.Append("  \"ticksTotal\": ").Append(last.TickSum).Append(",\n");
                sb.Append("  \"systemPlayerFits\": ").Append(system.PlayerFits ? "true" : "false").Append(",\n");
                sb.Append("  \"systemCores\": ").Append(F(system.SystemCores)).Append(",\n");
                sb.Append("  \"systemWindows\": ").Append(system.Windows).Append(",\n");
                sb.Append("  \"systemSimWindows\": ").Append(system.SimulatedWindows).Append(",\n");
                sb.Append("  \"systemLoadedWindows\": ").Append(system.LoadedWindows).Append(",\n");
                sb.Append("  \"systemOverlapWindows\": ").Append(system.OverlapWindows).Append(",\n");
                sb.Append("  \"cliMsPerTick\": ").Append(F(system.CliMsPerTick)).Append(",\n");
                sb.Append("  \"cliMsPerFrame\": ").Append(F(system.CliMsPerFrame)).Append(",\n");
                sb.Append("  \"systemMsPerFrame\": ").Append(F(system.SystemMsPerFrame)).Append(",\n");
                sb.Append("  \"systemPlayerMsPerFrame\": ").Append(F(system.PlayerMsPerFrame)).Append(",\n");
                sb.Append("  \"systemCliShare\": ").Append(F(system.CliShareOfSystem)).Append(",\n");
                sb.Append("  \"systemAggregateMsPerFrame\": ").Append(F(system.AggregateMsPerFrame)).Append(",\n");
                sb.Append("  \"ticksPerPresentedFrame\": ").Append(F(system.TicksPerPresentedFrame)).Append(",\n");
                sb.Append("  \"systemFractionOfBudget\": ").Append(F(system.SystemFractionOfBudget)).Append(",\n");
                sb.Append("  \"systemPlayerFractionOfBudget\": ").Append(F(system.PlayerFractionOfBudget)).Append(",\n");
                sb.Append("  \"systemPlayerHeadroom\": ").Append(F(system.PlayerHeadroom)).Append(",\n");
                sb.Append("  \"systemMeasured\": ").Append(system.Measured ? "true" : "false").Append(",\n");
                sb.Append("  \"systemVerdict\": \"").Append(system.Verdict).Append("\",\n");
                sb.Append("  \"drawCalls\": ").Append(last.DrawCalls).Append(",\n");
                sb.Append("  \"instancedCalls\": ").Append(last.Instanced).Append(",\n");
                sb.Append("  \"fallbackCalls\": ").Append(last.Fallback).Append(",\n");
                sb.Append("  \"ants\": ").Append(last.Ants).Append(",\n");
                sb.Append("  \"items\": ").Append(last.Items).Append(",\n");
                sb.Append("  \"finalTick\": ").Append(last.Tick).Append(",\n");
                sb.Append("  \"gateOk\": ").Append(gateOk ? "true" : "false").Append(",\n");
                // ── El chip de runway EN PANTALLA (F5.1) ────────────────────────
                // El color del tramo, el ancho REAL de la línea que lo lleva y la
                // tinta que invade la barra de reserva: lo que ningún test de modelo
                // puede probar, medido en el frame que el jugador vio.
                sb.Append("  \"hudMeasured\": ").Append(hud.Measured ? "true" : "false").Append(",\n");
                sb.Append("  \"hudOk\": ").Append(hud.Ok ? "true" : "false").Append(",\n");
                sb.Append("  \"hudCapture\": \"").Append(hud.Capture).Append("\",\n");
                sb.Append("  \"hudCards\": ").Append(hud.Measures.Count).Append(",\n");
                sb.Append("  \"hudChips\": ").Append(hud.Chips).Append(",\n");
                sb.Append("  \"hudChipMinCover\": ").Append(hud.MinChipCover.ToString("0.####", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"hudChipMaxCover\": ").Append(hud.MaxChipCover.ToString("0.####", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"hudTitlePx\": ").Append(hud.WorstTitlePx).Append(",\n");
                sb.Append("  \"hudTitleAvail\": ").Append(hud.WorstTitleAvail).Append(",\n");
                sb.Append("  \"hudTitleHeadroom\": ").Append(hud.MinHeadroom).Append(",\n");
                sb.Append("  \"hudStagingInkPx\": ").Append(hud.MaxStagingInk).Append(",\n");
                sb.Append("  \"hudBarInkPx\": ").Append(hud.MaxBarInk).Append(",\n");
                sb.Append("  \"hudWhy\": \"").Append(hud.Why).Append("\",\n");
                sb.Append("  \"hudCanvas\": \"").Append(hud.Canvas).Append("\",\n");
                sb.Append("  \"hud\": [");
                for (int i = 0; i < hud.Measures.Count; i++)
                {
                    var m = hud.Measures[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("\n    {\"name\": \"").Append(m.Name).Append('"')
                      .Append(", \"chip\": \"").Append(ChipsDetail(m.Chips)).Append('"')
                      .Append(", \"titlePx\": ").Append(m.TitleWidth)
                      .Append(", \"availPx\": ").Append(m.AvailableWidth)
                      .Append(", \"headroomPx\": ").Append(m.TitleHeadroom)
                      .Append(", \"lines\": ").Append(m.Lines.Count)
                      .Append(", \"authoredLines\": ").Append(m.AuthoredLines)
                      .Append(", \"stagingInkPx\": ").Append(m.StagingInkPx)
                      .Append(", \"barInkPx\": ").Append(m.BarInkPx)
                      .Append(", \"barGapPx\": ").Append(m.BarGapPx)
                      .Append(", \"barsOverlapText\": ").Append(m.BarsOverlapText ? "true" : "false")
                      .Append(", \"geometry\": \"").Append(m.Geometry).Append('"')
                      .Append(", \"ok\": ").Append(m.Ok ? "true" : "false")
                      .Append(", \"verdict\": \"").Append(m.Verdict).Append("\"}");
                }
                sb.Append("\n  ],\n");
                sb.Append("  \"gate\": [");
                for (int i = 0; i < gates.Count; i++)
                {
                    var g = gates[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("\n    {\"view\": ").Append(g.Index)
                      .Append(", \"camera\": \"").Append(g.Camera).Append('"')
                      .Append(", \"window\": ").Append(F(g.Window))
                      .Append(", \"floor\": \"").Append(g.Result.Floor).Append('"')
                      .Append(", \"brown\": ").Append(g.Result.Brown ? "true" : "false")
                      .Append(", \"antPx\": ").Append(g.Result.AntPx)
                      .Append(", \"carrierPx\": ").Append(g.Result.CarrierPx)
                      .Append(", \"itemPx\": ").Append(g.Result.ItemPx)
                      .Append(", \"darkFrac\": ").Append(F(g.Result.DarkFraction))
                      .Append(", \"ok\": ").Append(g.Result.Ok ? "true" : "false")
                      .Append(", \"verdict\": \"").Append(g.Result.Verdict)
                      .Append("\", \"mosaic\": \"").Append(g.Mosaic).Append("\"}");
                }
                sb.Append("\n  ],\n");
                sb.Append("  \"series\": [");
                for (int i = 0; i < _samples.Count; i++)
                {
                    var s = _samples[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("\n    {\"s\": ").Append(F(s.Seconds))
                      .Append(", \"fps\": ").Append(F(s.Fps))
                      .Append(", \"drawCalls\": ").Append(s.DrawCalls)
                      .Append(", \"ants\": ").Append(s.Ants)
                      .Append(", \"tick\": ").Append(s.Tick)
                      .Append(", \"cpuMs\": ").Append(F(s.Timing.CpuMs))
                      .Append(", \"cliCpuMs\": ").Append(F(s.CliCpuMs))
                      .Append(", \"tickSum\": ").Append(s.TickSum)
                      .Append(", \"gpuMs\": ").Append(F(s.Timing.GpuMs))
                      .Append(", \"focused\": ").Append(s.Focused ? "true" : "false").Append('}');
                }
                sb.Append("\n  ]\n}\n");

                Directory.CreateDirectory(Path.GetDirectoryName(_outPath)!);
                File.WriteAllText(_outPath, sb.ToString());
                Debug.Log($"{Tag} informe escrito en {_outPath}");
            }
            catch (Exception ex)
            {
                Debug.Log($"{Tag} (aviso) no se pudo escribir el informe en '{_outPath}': {ex.GetType().Name}: {ex.Message}");
            }

        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Las bandas de línea medidas, en una línea: «k:x[a..b] y[c..d] i=P c=Q».
        /// Es la ventana de la medida puesta en texto, que es lo que permite auditar
        /// una longitud sin volver a correr el player.</summary>
        private static string Bands(System.Collections.Generic.IReadOnlyList<InkLine> lines)
        {
            if (lines.Count == 0) return "ninguna";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                var l = lines[i];
                sb.Append(i).Append(":x[").Append(l.MinX).Append("..").Append(l.MaxX)
                  .Append("] y[").Append(l.Y0).Append("..").Append(l.Y1).Append(')')
                  .Append(" i=").Append(l.InkPx).Append(" c=").Append(l.ChipPx);
            }
            return sb.ToString();
        }

        /// <summary>Detalle de los chips medidos en UNA tarjeta, para el informe:
        /// «RESERVA×2 #7ED07E 74px α 0.90». Solo los tramos con píxeles. La COBERTURA
        /// sustituye a la distancia al color: es la magnitud que el rasterizador de
        /// fuentes sí garantiza comparable entre tarjetas (medido: 0,70–0,90).</summary>
        private static string ChipsDetail(System.Collections.Generic.IReadOnlyList<ChipPixels> chips)
        {
            var sb = new StringBuilder();
            foreach (var c in chips)
            {
                if (c.Px <= 0) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(RunwayChipModel.Label(c.State)).Append(' ').Append(RunwayChipModel.ColorHex(c.State))
                  .Append(' ').Append(c.Px).Append("px α ")
                  .Append(c.Cover.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(" rgb(").Append(c.Mean).Append(')');
            }
            return sb.Length == 0 ? "ninguno" : sb.ToString();
        }

        private static string? ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static float ArgFloat(string[] args, string name, float fallback)
        {
            string? raw = ArgValue(args, name);
            return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                ? v : fallback;
        }
    }
}
