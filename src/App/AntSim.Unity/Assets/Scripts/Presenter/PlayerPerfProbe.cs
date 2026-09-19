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
    ///
    /// NO HAY CAPTURA PNG. `ScreenCapture` vive en el módulo `Screen Capture`, que
    /// este proyecto NO tiene en el manifest a propósito, y pedirlo rompería la
    /// compilación del juego entero por una sonda. En su lugar, cada vista deja su
    /// frame en una MOSAICA de caracteres (el mundo leído en texto, que es lo que
    /// un agente puede inspeccionar de verdad).
    ///
    /// Salida: 0 si MIDIÓ y la puerta de píxeles pasó en todas las vistas; 1 si
    /// midió pero la puerta suspende (o el montaje está muerto); 2 si no hubo ni
    /// una muestra; 3 en modo coste si el FrameTimingManager no entregó ni un
    /// tiempo de frame (sin eso no hay coste, solo framerate). Un verde sobre una
    /// escena sin hormigas sería el peor resultado posible, así que ese caso es un
    /// suspenso explícito.
    /// </summary>
    public sealed class PlayerPerfProbe : MonoBehaviour
    {
        private const string Tag = "[PlayerPerf]";

        private string _outPath = "";
        private float _seconds = 30f;
        private float _warmup = 12f;
        private float _boost = 10f;
        private bool _noVsync;
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

            public Sample(float seconds, double fps, int drawCalls, int instanced, int fallback,
                int ants, int items, ulong tick, bool focused, FrameTimingSample timing,
                long windowFrames, uint syncInterval)
            {
                Seconds = seconds; Fps = fps; DrawCalls = drawCalls; Instanced = instanced;
                Fallback = fallback; Ants = ants; Items = items; Tick = tick; Focused = focused;
                Timing = timing; WindowFrames = windowFrames; SyncInterval = syncInterval;
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
                ArgFloat(args, "-antsimPerfNoVsync", 0f) >= 0.5f);
        }

        private void Configure(string outPath, float seconds, float warmup, float boost, bool noVsync)
        {
            // El cwd de un player es su carpeta de build: el informe (y el pool y el
            // CLI) se anclan al repo root, que en un build dentro del repo se
            // encuentra por el marcador (src/Tools/AntSim.Cli).
            string? repo = Streaming.RepoPathResolver.RepoRootFromDataPath(Application.dataPath);
            _outPath = ResolveAgainstRepo(outPath, repo);
            _seconds = Mathf.Max(1f, seconds);
            _warmup = Mathf.Max(0f, warmup);
            _boost = Mathf.Max(0f, boost);

            // El vsync DEL PROYECTO se lee ANTES de tocarlo: si la corrida lo apaga
            // para medir coste, el informe tiene que decir cuál era el del proyecto
            // (si no, la corrida de coste parecería un proyecto mal configurado).
            _projectVSync = QualitySettings.vSyncCount;
            _noVsync = noVsync;
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

        /// <summary>Una muestra por segundo: frames REALES contados entre muestras
        /// divididos por el tiempo real transcurrido (no por un dt supuesto).</summary>
        private void TakeSample(float seconds, float now)
        {
            int drawCalls = 0, instanced = 0, fallback = 0, ants = 0, items = 0;
            ulong tick = 0;
            foreach (var p in Presenters())
            {
                drawCalls += p.LastDrawCalls;
                instanced += p.LastInstancedCalls;
                fallback += p.LastFallbackCalls;
                var state = p.CurrentState;
                if (state != null) { ants += state.Ants.Count; items += state.Items.Count; }
                ulong t = p.Presenter?.CurrentTick?.Tick ?? 0;
                if (t > tick) tick = t;
            }
            double dt = now - _lastSampleAt;
            long windowFrames = _frames - _lastSampleFrames;
            double fps = dt > 0.001 ? windowFrames / dt : 0;
            var timing = new FrameTimingSample(
                FrameCost.Median(_cpuMs), FrameCost.Median(_mainMs),
                FrameCost.Median(_renderMs), FrameCost.Median(_waitMs),
                FrameCost.Median(_gpuMs));
            _samples.Add(new Sample(seconds, fps, drawCalls, instanced, fallback, ants, items,
                tick, Application.isFocused, timing, windowFrames, _lastSyncInterval));
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

            var gates = GateViews();
            bool gateOk = gates.Count > 0;
            foreach (var g in gates) if (!g.Result.Ok) gateOk = false;

            float refresh = (float)Screen.currentResolution.refreshRateRatio.value;
            Debug.Log($"{Tag} condiciones: vsync={QualitySettings.vSyncCount} (proyecto={_projectVSync}) · " +
                      $"targetFrameRate={Application.targetFrameRate} · pantalla={Screen.width}×{Screen.height} · " +
                      $"refresco={refresh:0.#} Hz · con foco={Focused()}/{_samples.Count} muestras");
            Debug.Log($"{Tag} frames/s: mediana={median:0.#} · peor={worst:0.#} · mejor={best:0.#} · " +
                      $"frames={_frames} · tick final={last.Tick}");
            if (_noVsync)
                Debug.Log($"{Tag} coste/frame: {FrameCost.Describe(cost)}");
            Debug.Log($"{Tag} draw calls={last.DrawCalls} (instanciadas={last.Instanced}, respaldo={last.Fallback}) · " +
                      $"carga final={last.Ants} hormigas y {last.Items} ítems");
            foreach (var g in gates)
            {
                Debug.Log($"{Tag} vista {g.Index} ({g.Camera}): {g.Result.Describe()} → {g.Result.Verdict}");
                if (g.Mosaic.Length > 0) Debug.Log($"{Tag} mosaico vista {g.Index}: {g.Mosaic}");
            }

            WriteJson(median, worst, best, last, gates, gateOk, refresh, cost);

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
            // En modo coste, «no medido» no es un verde: sin tiempos de frame lo que
            // hay es un framerate, que es justo el número que esta corrida existe
            // para no publicar como coste.
            if (_noVsync && !cost.Measured)
            {
                Debug.Log($"{Tag} ✗ MODO COSTE SIN TIEMPOS DE FRAME: {_timingsTaken} frames con dato del " +
                          "FrameTimingManager — falta enableFrameTimingStats en el build");
                return 3;
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
            List<ViewGate> gates, bool gateOk, float refresh, FrameCostSummary cost)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"probe\": \"player-perf\",\n");
                sb.Append("  \"mode\": \"").Append(_noVsync ? "player-cpu" : "player").Append("\",\n");
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
                sb.Append("  \"drawCalls\": ").Append(last.DrawCalls).Append(",\n");
                sb.Append("  \"instancedCalls\": ").Append(last.Instanced).Append(",\n");
                sb.Append("  \"fallbackCalls\": ").Append(last.Fallback).Append(",\n");
                sb.Append("  \"ants\": ").Append(last.Ants).Append(",\n");
                sb.Append("  \"items\": ").Append(last.Items).Append(",\n");
                sb.Append("  \"finalTick\": ").Append(last.Tick).Append(",\n");
                sb.Append("  \"gateOk\": ").Append(gateOk ? "true" : "false").Append(",\n");
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
