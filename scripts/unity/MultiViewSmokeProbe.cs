#if UNITY_EDITOR
using System.Text;
using UnityEngine;
using UnityEditor;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.EditorTools;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>Smoke en vivo del multi-visor (F5.1bis): entra en Play sobre la
    /// escena multi-visor, deja correr la reproducción N segundos y extrae por
    /// vista: capa de render, máscara de cámara, tick alcanzado (y ticks/s
    /// REALES entre dos muestras — el criterio ≥2×), poses, y texto de la
    /// tarjeta compacta (con el glifo del semáforo). Escribe líneas
    /// [MultiSmoke] en el log y cierra el editor.
    ///
    /// Dos puntos de entrada batch:
    ///   RunSmoke  — 2 vistas a 1× (sanidad del multi-visor)
    ///   RunSmoke4 — 4 vistas a 2× (el CRITERIO DE CIERRE de fase5-plan §2bis)
    /// </summary>
    public static class MultiViewSmokeProbe
    {
        private static int _frames;
        private static double _deadline;
        private static bool _reported;
        private static bool _midSampled;
        // ticks/s reales por vista: tick y timestamp de la primera muestra.
        private static readonly System.Collections.Generic.Dictionary<string, (ulong tick, double time)> _firstSeen = new();

        /// <summary>Smoke de sanidad (2 vistas, 1×): reproduce streams volcados
        /// a archivo y verifica mundos distintos por capa. Batch:
        /// <c>-executeMethod …MultiViewSmokeProbe.RunSmoke</c>.</summary>
        public static void RunSmoke()
        {
            MultiSimBootstrapper.CreateMultiSimSmokeScene();
            // SessionState, no statics: entrar en Play recarga el dominio y los
            // statics vuelven a su inicializador (ventana 25 s — fue el defecto
            // de la primera corrida del criterio). SessionState sobrevive.
            SessionState.SetFloat("MultiSmoke.Window", 25f);
            SessionState.SetFloat("MultiSmoke.Boost", 0f);
            _enteringPlayAndRun();
        }

        /// <summary>Criterio de cierre de §2bis (F5.1bis): las CUATRO vistas con
        /// la cadena completa de pools a 2×, semáforo por vista.
        /// Batch: <c>-executeMethod …MultiViewSmokeProbe.RunSmoke4</c>.</summary>
        public static void RunSmoke4()
        {
            MultiSimBootstrapper.CreateMultiSimSmokeScene4();
            SessionState.SetFloat("MultiSmoke.Window", 115f);
            SessionState.SetFloat("MultiSmoke.Boost", 2f);
            _enteringPlayAndRun();
        }

        private static void _enteringPlayAndRun()
        {
            _enteringPlay = true;
            EditorApplication.update += EnterPlayWhenReady;
        }

        // El cambio a Play no es síncrono: se pide desde update y se confirma.
        private static void EnterPlayWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= EnterPlayWhenReady;
            EditorApplication.isPlaying = true;   // Play en batch: la escena corre
        }

        private static bool _enteringPlay;
        private static double _windowSeconds = 25.0;

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || _reported) return;

            // BOMBA DEL BUCLE DE JUEGO (defecto del clon limpio): en batchmode el
            // bucle del EDITOR corre (esta sonda vive en él) pero el del JUGADOR
            // puede quedarse en el frame 1 (dt=0, Time.frameCount clavado) — fue
            // invisible con un Library ya caliente y letal en un clon fresco.
            // QueuePlayerLoopUpdate es la API documentada para empujar iteraciones
            // del bucle de juego desde el bucle del editor.
            if (Application.isBatchMode)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                Time.captureDeltaTime = 1f / 30f;
            }

            // MODO BATCH DEGRADADO (clon limpio): si el bucle de jugador está
            // congelado (frame clavado — p. ej. la excepción del Search de Unity
            // 6 en un Library recién importado), la sonda AVANZA EL RELOJ POR SÍ
            // MISMA: mismo presenter, misma reproducción determinista del
            // archivo; solo cambia quién empuja. En interactivo no se toca.
            if (Application.isBatchMode && Time.frameCount <= 1 && _frames > 5)
            {
                foreach (var p in Object.FindObjectsByType<SimPresenterBehaviour>(FindObjectsSortMode.None))
                    p.PumpOneTick();
                if (_frames % 300 == 0)
                    Debug.Log("[MultiSmoke] batch con bucle de jugador congelado: avanzando por sonda (tick virtual)");
            }

            // Boost del criterio: se re-aplica cada frame mientras haga falta —
            // el reload de Play borra los statics pero NO SessionState, y el
            // campo serializado del componente puede tardar un frame.
            float wantBoost = SessionState.GetFloat("MultiSmoke.Boost", 0f);
            if (wantBoost > 0f)
                foreach (var p in Object.FindObjectsByType<SimPresenterBehaviour>(FindObjectsSortMode.None))
                    p.SpeedBoost = wantBoost;

            if (_frames == 0)
            {
                _windowSeconds = SessionState.GetFloat("MultiSmoke.Window", 25f);
                _deadline = EditorApplication.timeSinceStartup + _windowSeconds;
            }
            _frames++;

            // Muestra intermedia a los 8 s: base para medir ticks/s reales.
            if (!_midSampled && EditorApplication.timeSinceStartup >= _deadline - 17.0)
            {
                _midSampled = true;
                SampleAll("mid");
            }

            if (EditorApplication.timeSinceStartup < _deadline) return;
            _reported = true;
            SampleAll("final");

            EditorApplication.isPlaying = false;
            // En batch el `-quit` del CLI no dispara con Play activo: salir a mano.
            EditorApplication.Exit(0);
        }

        private static void SampleAll(string phase)
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!cam.name.StartsWith("Camera_V")) continue;
                string vid = cam.name.Substring("Camera_V".Length);
                var presenterGo = GameObject.Find("SimPresenter_V" + vid);
                if (presenterGo == null) { Log(phase, vid, "SIN PRESENTER"); continue; }
                var p = presenterGo.GetComponent<SimPresenterBehaviour>();
                var state = p.CurrentState;
                int ants = state?.Ants?.Count ?? 0;
                int items = state?.Items?.Count ?? 0;
                ulong tick = p.Presenter?.CurrentTick?.Tick ?? 0;

                // ticks/s REALES (30 ticks/s = 1×): entre la primera muestra y esta.
                double tps = 0;
                if (_firstSeen.TryGetValue(vid, out var first) && phase == "final")
                {
                    double dt = EditorApplication.timeSinceStartup - first.time;
                    if (dt > 0.5) tps = (tick - first.tick) / dt;
                }
                else _firstSeen[vid] = (tick, EditorApplication.timeSinceStartup);

                // Muestra 3 poses para comparar entre vistas.
                string poses = "";
                if (state != null)
                    for (int i = 0; i < state.Ants.Count && i < 3; i++)
                        poses += $"({state.Ants[i].X:0.#},{state.Ants[i].Y:0.#}) ";

                // Tarjeta compacta de la vista (texto con el glifo del semáforo).
                var cardGo = GameObject.Find("ViewCard_" + vid);
                string cardText = "";
                if (cardGo != null)
                {
                    var body = cardGo.transform.Find("Body");
                    if (body != null) cardText = body.GetComponent<UnityEngine.UI.Text>().text;
                }
                Log(phase, vid, $"layer={p.RenderLayer} camMask={cam.cullingMask} boost={p.SpeedBoost:0.#} tick={tick} tps={tps:0.#} ants={ants} items={items} buf={p.Presenter?.BufferedTicks ?? -1} final={p.Presenter?.FinalTick ?? 0} frame={Time.frameCount} dt={Time.deltaTime:0.###} rbg={Application.runInBackground} poses=[{poses}] card=[{cardText.Replace('\n', ';')}]");
            }
        }

        private static void Log(string phase, string vid, string msg)
            => Debug.Log($"[MultiSmoke] {phase} V{vid} {msg}");
    }
}
#endif
