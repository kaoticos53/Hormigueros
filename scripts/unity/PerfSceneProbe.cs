#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using AntSim.Unity.Scripts.Presenter;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// Medidor de RENDIMIENTO de la escena de juego (F5.3 rodaja 3): entra en Play
    /// sobre la escena del multi-visor con CUATRO vistas × 2 colonias = **8
    /// colonias en pantalla**, en el grid del modo juego (256) y con los pools
    /// reales, y muestrea una vez por segundo:
    ///
    ///   · frames/s REALES (frames contados entre muestras / tiempo real);
    ///   · `SimPresenterBehaviour.LastDrawCalls` por vista (el contador del
    ///     instancing) y su suma;
    ///   · hormigas e ítems dibujados por vista (la carga que produce esos draws);
    ///   · tick alcanzado y ticks/s reales de la reproducción.
    ///
    /// POR QUÉ ASÍ. El criterio de salida de F5.3 es «N colonias estables a 60 fps
    /// en la escena de juego» y el Core ya está medido headless (`--mode scale`);
    /// lo que falta es el lado de la VISTA. La sonda corre en DOS modos, y la
    /// diferencia entre ellos es todo:
    ///
    ///   · **batch** (`-batchmode`): sin presentación, con `captureDeltaTime` fijo
    ///     (1/30 s) y empujando el bucle del jugador si el editor lo deja
    ///     congelado (defecto conocido de un Library frío). Mide el COSTE de
    ///     producir frames — es comparable con `--mode scale` del Core.
    ///   · **ventana** (Play pass: el editor normal, sin `-batchmode`): el bucle y
    ///     la presentación son REALES, así que lo que se lee es el framerate que ve
    ///     el jugador. Con `ANTSIM_PERF_VSYNC=1` se respeta el vsync del proyecto y
    ///     el número sale con el techo de la pantalla; apagado (por defecto) el
    ///     coste del frame queda al descubierto.
    ///
    /// En los dos modos, la sonda empuja el bucle SOLO si hace falta y lo dice en el
    /// reporte (`bucle=real` / `bucle=empujado`), y anota si la ventana tenía el
    /// foco en cada muestra: en modo ventana, un editor en segundo plano puede bajar
    /// su tasa por el sistema operativo, y eso no es coste del juego.
    ///
    /// NO es una foto: las muestras se acumulan hasta la ventana y el reporte
    /// publica la MEDIANA y el peor caso, porque un frame lento aislado no es un
    /// framerate y una media lo escondería.
    ///
    /// Entrada batch: <c>-executeMethod …PerfSceneProbe.RunPerf</c>.
    /// Variables de entorno: <c>ANTSIM_PERF_SECONDS</c> (def. 45),
    /// <c>ANTSIM_PERF_BOOST</c> (def. 10 = ×10 del control de velocidad),
    /// <c>ANTSIM_PERF_GRID</c> (def. 256), <c>ANTSIM_PERF_VSYNC</c> (def. 0 = apagado),
    /// <c>ANTSIM_PERF_OUT</c> (def. artifacts/perf-scene.json).
    ///
    /// Salida: 0 si MIDIÓ algo real (hay muestras, hay hormigas y hay dibujo), 1
    /// si el montaje está muerto (una corrida verde sobre una escena vacía sería
    /// el peor resultado posible), 2 si el editor no llegó a entrar en Play.
    /// </summary>
    public static class PerfSceneProbe
    {
        private const string Tag = "[Perf]";
        private const int ViewCount = 4;

        private static bool _reported;
        private static bool _enteringPlay;
        private static double _deadline;
        private static int _frames;
        private static double _lastSampleTime;
        private static int _lastSampleFrames;
        private static bool _playerLoopPushed;
        private static bool _vsync;
        private static bool _batch;
        private static float _windowSeconds = 45f;
        private static float _boost = 10f;
        private static int _grid = 256;
        private static string _outPath = "";
        private static readonly List<Sample> _samples = new();

        private readonly struct Sample
        {
            public readonly double Seconds;
            public readonly double Fps;
            public readonly int DrawCallsTotal;
            public readonly int AntsTotal;
            public readonly int ItemsTotal;
            public readonly ulong Tick;
            public readonly int DrawCallsMax;
            public readonly bool AnyBeyondBudget;
            public readonly int InstancedCalls;
            public readonly int FallbackCalls;
            /// <summary>¿La ventana del editor tenía el FOCO en esta muestra? Solo se
            /// mira en modo ventana: un editor en segundo plano puede bajar su tasa de
            /// frames por el sistema operativo, y eso no es coste del juego.</summary>
            public readonly bool Focused;

            public Sample(double seconds, double fps, int drawCallsTotal, int drawCallsMax,
                int antsTotal, int itemsTotal, ulong tick, bool anyBeyondBudget,
                int instancedCalls, int fallbackCalls, bool focused)
            {
                Seconds = seconds;
                Fps = fps;
                DrawCallsTotal = drawCallsTotal;
                DrawCallsMax = drawCallsMax;
                AntsTotal = antsTotal;
                ItemsTotal = itemsTotal;
                Tick = tick;
                AnyBeyondBudget = anyBeyondBudget;
                InstancedCalls = instancedCalls;
                FallbackCalls = fallbackCalls;
                Focused = focused;
            }
        }

        /// <summary>Entrada batch del medidor.</summary>
        public static void RunPerf()
        {
            _windowSeconds = EnvFloat("ANTSIM_PERF_SECONDS", 45f);
            _boost = EnvFloat("ANTSIM_PERF_BOOST", 10f);
            _grid = (int)EnvFloat("ANTSIM_PERF_GRID", MultiSimBootstrapper.PerfGridCells);
            // VSYNC: por defecto la sonda lo APAGA, para medir el COSTE de producir
            // frames (comparable con la corrida en batch). Con ANTSIM_PERF_VSYNC=1 se
            // RESPETA el del proyecto, y entonces lo que se lee es el framerate
            // PRESENTADO —el que ve el jugador—, con el techo de la pantalla.
            _vsync = EnvFloat("ANTSIM_PERF_VSYNC", 0f) > 0f;
            _batch = Application.isBatchMode;
            string? repo = RepoRoot();
            _outPath = Environment.GetEnvironmentVariable("ANTSIM_PERF_OUT") ?? "";
            if (string.IsNullOrEmpty(_outPath))
                _outPath = Path.Combine(repo ?? ".", "artifacts", "perf-scene.json");
            else if (!Path.IsPathRooted(_outPath) && repo != null)
                _outPath = Path.Combine(repo, _outPath);

            MultiSimBootstrapper.CreateMultiSimPerfScene(_grid);
            // SessionState, NO statics: entrar en Play RECARGA el dominio y los
            // statics vuelven a su inicializador — la configuración se perdía y el
            // informe se escribía en una ruta vacía (ArgumentException: Invalid
            // path). Es el mismo defecto que ya documenta la sonda del multi-visor.
            SessionState.SetFloat("Perf.Boost", _boost);
            SessionState.SetFloat("Perf.Seconds", _windowSeconds);
            SessionState.SetInt("Perf.Grid", _grid);
            SessionState.SetString("Perf.Out", _outPath);
            SessionState.SetBool("Perf.Vsync", _vsync);
            SessionState.SetBool("Perf.Batch", _batch);
            Debug.Log($"{Tag} escena: {ViewCount} vistas × 2 colonias = {ViewCount * 2} colonias · " +
                      $"grid {_grid}² · ventana {_windowSeconds:0}s · boost ×{_boost:0.#} · " +
                      $"modo={(_batch ? "batch" : "ventana")} · vsync={(_vsync ? "proyecto" : "apagado")}");
            _enteringPlay = true;
            EditorApplication.update += EnterPlayWhenReady;
        }

        private static void EnterPlayWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= EnterPlayWhenReady;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.update += Tick;

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || _reported) return;

            if (Application.isBatchMode)
            {
                // El bucle del JUGADOR puede quedarse en el frame 1 en batch (dt=0,
                // Time.frameCount clavado). Se empuja desde el bucle del editor; si
                // aun así no avanza, la sonda avanza la reproducción por sí misma y
                // lo declara, porque un frames/s medido sobre un bucle congelado no
                // significa nada.
                EditorApplication.QueuePlayerLoopUpdate();
                Time.captureDeltaTime = 1f / 30f;
            }

            if (Application.isBatchMode && Time.frameCount <= 1 && _frames > 5)
            {
                _playerLoopPushed = true;
                foreach (var p in Presenters()) p.PumpOneTick();
            }

            // El boost se re-aplica cada frame: el reload de Play borra los statics
            // (no SessionState) y el campo del componente tarda un frame.
            float wantBoost = SessionState.GetFloat("Perf.Boost", _boost);
            if (wantBoost > 0f)
                foreach (var p in Presenters()) p.SpeedBoost = wantBoost;

            if (_frames == 0)
            {
                // Config desde SessionState (sobrevive al reload de Play).
                _boost = SessionState.GetFloat("Perf.Boost", _boost);
                _windowSeconds = SessionState.GetFloat("Perf.Seconds", _windowSeconds);
                _grid = SessionState.GetInt("Perf.Grid", _grid);
                _outPath = SessionState.GetString("Perf.Out", _outPath);
                _vsync = SessionState.GetBool("Perf.Vsync", _vsync);
                _batch = SessionState.GetBool("Perf.Batch", _batch);
                if (!_vsync)
                {
                    // Apagar vsync es lo que hace COMPARABLE esta corrida con la de
                    // batch: sin eso el techo lo pone la pantalla (60 Hz) y el coste
                    // real del frame queda escondido por debajo.
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                }
                _deadline = EditorApplication.timeSinceStartup + _windowSeconds;
                _lastSampleTime = EditorApplication.timeSinceStartup;
                _lastSampleFrames = 0;
            }
            _frames++;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastSampleTime >= 1.0)
            {
                TakeSample(now);
                _lastSampleTime = now;
                _lastSampleFrames = _frames;
            }

            if (now < _deadline) return;
            _reported = true;

            int rc = Report();
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(rc);
        }

        /// <summary>Los presenters de la escena. `FindObjectsByType` SIN el
        /// argumento de orden: la sobrecarga con `FindObjectsSortMode` está
        /// obsoleta en Unity 6000.6 y avisa en cada compilación.</summary>
        private static SimPresenterBehaviour[] Presenters()
            => UnityEngine.Object.FindObjectsByType<SimPresenterBehaviour>();

        private static void TakeSample(double now)
        {
            int drawCalls = 0, drawCallsMax = 0, ants = 0, items = 0, instanced = 0, fallback = 0;
            ulong tick = 0;
            bool beyond = false;
            foreach (var p in Presenters())
            {
                drawCalls += p.LastDrawCalls;
                instanced += p.LastInstancedCalls;
                fallback += p.LastFallbackCalls;
                if (p.LastDrawCalls > drawCallsMax) drawCallsMax = p.LastDrawCalls;
                if (p.DrawingBeyondBudget) beyond = true;
                var state = p.CurrentState;
                if (state != null)
                {
                    ants += state.Ants.Count;
                    items += state.Items.Count;
                }
                ulong t = p.Presenter?.CurrentTick?.Tick ?? 0;
                if (t > tick) tick = t;
            }
            double dt = now - _lastSampleTime;
            double fps = dt > 0 ? (_frames - _lastSampleFrames) / dt : 0;
            _samples.Add(new Sample(now - (_deadline - _windowSeconds), fps, drawCalls, drawCallsMax,
                ants, items, tick, beyond, instanced, fallback, Application.isFocused));
        }

        private static int Report()
        {
            if (_samples.Count == 0)
            {
                Debug.Log($"{Tag} ✗ SIN MUESTRAS: el editor no llegó a correr en Play");
                return 2;
            }

            var fps = new List<double>();
            foreach (var s in _samples) fps.Add(s.Fps);
            fps.Sort();
            double median = fps[fps.Count / 2];
            double p10 = fps[Math.Max(0, fps.Count / 10)];
            double worst = fps[0];
            double best = fps[fps.Count - 1];

            // Estado final: es el frame más cargado de la corrida (el mundo crece)
            var last = _samples[_samples.Count - 1];

            Debug.Log($"{Tag} condiciones: modo={(_batch ? "batch (sin presentación)" : "ventana (Play pass, con presentación)")} · " +
                      $"vsync={(_vsync ? "proyecto" : "apagado por la sonda")} · targetFrameRate={Application.targetFrameRate} · " +
                      $"pantalla={Screen.width}×{Screen.height} · con foco={FocusedSamples()}/{_samples.Count} muestras");
            Debug.Log($"{Tag} bucle={(_playerLoopPushed ? "empujado por la sonda" : "real")} · " +
                      $"frames={_frames} · muestras={_samples.Count} · tick final={last.Tick}");
            Debug.Log($"{Tag} frames/s: mediana={median:0.#} · p10={p10:0.#} · peor={worst:0.#} · mejor={best:0.#}");
            Debug.Log($"{Tag} draw calls: total={last.DrawCallsTotal} · peor vista={last.DrawCallsMax} " +
                      $"(presupuesto {SimPresenterBehaviour.DrawCallBudget}/vista) · " +
                      $"mediana total={MedianDrawCalls()} · instanciadas={last.InstancedCalls} · " +
                      $"respaldo={last.FallbackCalls}");
            Debug.Log($"{Tag} carga final: {last.AntsTotal} hormigas y {last.ItemsTotal} ítems en pantalla");
            // El stream se agota ANTES de la ventana (el CLI produce ticks mucho más
            // rápido de lo que la vista los consume): las muestras posteriores miden
            // el coste de REPINTAR el estado final, que es la fase estable, y las
            // anteriores incluyen además el consumo del stream. Se publican las dos
            // — y las dos por separado, porque una media las mezclaría.
            double fpsStable = StableFps(out int stableSamples, out double exhaustedAt);
            Debug.Log($"{Tag} stream agotado en t≈{exhaustedAt:0}s (tick {last.Tick}) · " +
                      $"estable: {fpsStable:0.#} frames/s = {1000.0 / Math.Max(1.0, fpsStable):0.##} ms/frame " +
                      $"({stableSamples} muestras) · peor: {worst:0.#} frames/s = {1000.0 / Math.Max(1.0, worst):0.##} ms/frame");
            Debug.Log($"{Tag} muestras: " + SamplesLine());

            WriteJson(median, p10, worst, last, fpsStable, exhaustedAt);

            bool alive = last.AntsTotal > 0 && last.DrawCallsTotal > 0;
            bool beyondBudget = last.AnyBeyondBudget;
            if (!alive)
            {
                Debug.Log($"{Tag} ✗ MEDICIÓN VACÍA: {last.AntsTotal} hormigas y {last.DrawCallsTotal} draw calls " +
                          "al cierre — un verde aquí sería un falso verde");
                return 1;
            }
            if (last.InstancedCalls == 0)
            {
                // Hormigas dibujadas por el camino de una por objeto: la escena
                // funciona, pero el instancing NO está haciendo su trabajo (fue
                // el defecto real: `enableInstancing` apagado en los materiales).
                Debug.Log($"{Tag} ✗ {last.DrawCallsTotal} draw calls y NINGUNA instanciada: " +
                          "el dibujo cayó al camino por objeto");
                return 1;
            }
            if (beyondBudget)
            {
                Debug.Log($"{Tag} ✗ alguna vista pasó el presupuesto de {SimPresenterBehaviour.DrawCallBudget} " +
                          "llamadas por frame y vista");
                return 1;
            }
            Debug.Log($"{Tag} ✓ medido: {last.AntsTotal} hormigas, {last.DrawCallsTotal} draw calls, " +
                      $"mediana {median:0.#} frames/s");
            return 0;
        }

        /// <summary>Cuántas muestras se tomaron con la ventana del editor en
        /// primer plano. En modo ventana es la lectura que distingue «el juego
        /// cuesta esto» de «el sistema operativo dejó el editor en segundo
        /// plano»: sin ella, un framerate bajo no se puede atribuir.</summary>
        private static int FocusedSamples()
        {
            int n = 0;
            foreach (var s in _samples) if (s.Focused) n++;
            return n;
        }

        /// <summary>Frames/s de la fase ESTABLE: las muestras posteriores a la
        /// última en la que el tick avanzó (el stream ya se agotó y solo queda
        /// repintar). Si el stream no se agotó, es la mediana de todas.</summary>
        private static double StableFps(out int count, out double exhaustedAt)
        {
            int lastAdvance = -1;
            for (int i = 1; i < _samples.Count; i++)
                if (_samples[i].Tick > _samples[i - 1].Tick) lastAdvance = i;

            exhaustedAt = lastAdvance >= 0 ? _samples[lastAdvance].Seconds : 0;
            var stable = new List<double>();
            for (int i = lastAdvance + 1; i < _samples.Count; i++) stable.Add(_samples[i].Fps);
            if (stable.Count == 0) for (int i = 0; i < _samples.Count; i++) stable.Add(_samples[i].Fps);

            stable.Sort();
            count = stable.Count;
            return stable[stable.Count / 2];
        }

        private static double MedianDrawCalls()
        {
            var v = new List<int>();
            foreach (var s in _samples) v.Add(s.DrawCallsTotal);
            v.Sort();
            return v[v.Count / 2];
        }

        private static string SamplesLine()
        {
            var sb = new StringBuilder();
            foreach (var s in _samples)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append('t').Append(s.Seconds.ToString("0", CultureInfo.InvariantCulture))
                  .Append('=').Append(s.Fps.ToString("0.#", CultureInfo.InvariantCulture)).Append("fps/")
                  .Append(s.DrawCallsTotal).Append("dc/").Append(s.AntsTotal).Append("ants");
            }
            return sb.ToString();
        }

        private static void WriteJson(double median, double p10, double worst, Sample last,
            double fpsStable, double exhaustedAt)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"probe\": \"perf-scene\",\n");
                sb.Append("  \"views\": ").Append(ViewCount).Append(",\n");
                sb.Append("  \"colonies\": ").Append(ViewCount * 2).Append(",\n");
                sb.Append("  \"grid\": ").Append(_grid).Append(",\n");
                sb.Append("  \"boost\": ").Append(_boost.ToString("0.#", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"mode\": \"").Append(_batch ? "batch" : "window").Append("\",\n");
                sb.Append("  \"vSync\": ").Append(_vsync ? "true" : "false").Append(",\n");
                sb.Append("  \"targetFrameRate\": ").Append(Application.targetFrameRate).Append(",\n");
                sb.Append("  \"screenWidth\": ").Append(Screen.width).Append(",\n");
                sb.Append("  \"screenHeight\": ").Append(Screen.height).Append(",\n");
                sb.Append("  \"focusedSamples\": ").Append(FocusedSamples()).Append(",\n");
                sb.Append("  \"windowSeconds\": ").Append(_windowSeconds.ToString("0.#", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"playerLoopPushed\": ").Append(_playerLoopPushed ? "true" : "false").Append(",\n");
                sb.Append("  \"frames\": ").Append(_frames).Append(",\n");
                sb.Append("  \"samples\": ").Append(_samples.Count).Append(",\n");
                sb.Append("  \"fpsMedian\": ").Append(median.ToString("0.##", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"fpsP10\": ").Append(p10.ToString("0.##", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"fpsWorst\": ").Append(worst.ToString("0.##", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"drawCallsTotal\": ").Append(last.DrawCallsTotal).Append(",\n");
                sb.Append("  \"instancedCalls\": ").Append(last.InstancedCalls).Append(",\n");
                sb.Append("  \"fallbackCalls\": ").Append(last.FallbackCalls).Append(",\n");
                sb.Append("  \"drawCallsWorstView\": ").Append(last.DrawCallsMax).Append(",\n");
                sb.Append("  \"drawCallBudgetPerView\": ").Append(SimPresenterBehaviour.DrawCallBudget).Append(",\n");
                sb.Append("  \"ants\": ").Append(last.AntsTotal).Append(",\n");
                sb.Append("  \"items\": ").Append(last.ItemsTotal).Append(",\n");
                sb.Append("  \"finalTick\": ").Append(last.Tick).Append(",\n");
                sb.Append("  \"fpsStable\": ").Append(fpsStable.ToString("0.##", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"msPerFrameStable\": ")
                  .Append((1000.0 / Math.Max(1.0, fpsStable)).ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"msPerFrameWorst\": ")
                  .Append((1000.0 / Math.Max(1.0, worst)).ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"streamExhaustedAtSeconds\": ")
                  .Append(exhaustedAt.ToString("0.#", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"series\": [");
                for (int i = 0; i < _samples.Count; i++)
                {
                    var s = _samples[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("\n    {\"s\": ").Append(s.Seconds.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append(", \"fps\": ").Append(s.Fps.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append(", \"drawCalls\": ").Append(s.DrawCallsTotal)
                      .Append(", \"ants\": ").Append(s.AntsTotal)
                      .Append(", \"focused\": ").Append(s.Focused ? "true" : "false")
                      .Append(", \"tick\": ").Append(s.Tick).Append('}');
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

        private static float EnvFloat(string name, float fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(raw)) return fallback;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f
                ? v : fallback;
        }

        /// <summary>
        /// Raíz del REPO subiendo desde el proyecto Unity. El marcador es la
        /// carpeta del CLI (`src/Tools/AntSim.Cli`) y NO un README.md: el proyecto
        /// Unity también tiene el suyo, así que con ese marcador el informe
        /// acababa dentro de `src/App/AntSim.Unity/artifacts/` (un directorio de
        /// artefactos dentro del proyecto, justo lo que no se quiere).
        /// </summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(Application.dataPath);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Tools", "AntSim.Cli")))
                dir = dir.Parent;
            return dir?.FullName;
        }
    }
}
#endif
