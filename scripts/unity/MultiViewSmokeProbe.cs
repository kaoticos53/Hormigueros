#if UNITY_EDITOR
using System.Linq;
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

        /// <summary>Criterio de cierre de F5.2a (5.2a.5): la vista Atta — su
        /// tarjeta debe mostrar la barra del HONGO de la colonia 0 y la línea
        /// de CORTES acumulados, y la colonia 1 (Lasius) ninguna de las dos.
        /// Batch: <c>-executeMethod …MultiViewSmokeProbe.RunSmokeAtta</c>.
        /// Sale 0 solo si TODAS las aserciones pasan; si no, 1 (CI-ready).</summary>
        public static void RunSmokeAtta()
        {
            MultiSimBootstrapper.CreateMultiSimSmokeSceneAtta();
            SessionState.SetFloat("MultiSmoke.Window", 40f);
            SessionState.SetFloat("MultiSmoke.Boost", 0f);
            SessionState.SetInt("MultiSmoke.Mode", 2); // 2 = aserciones Atta
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

            int rc = 0;
            if (SessionState.GetInt("MultiSmoke.Mode", 0) == 2)
                rc = AssertAtta();

            EditorApplication.isPlaying = false;
            // En batch el `-quit` del CLI no dispara con Play activo: salir a mano.
            EditorApplication.Exit(rc);
        }

        /// <summary>Aserciones del criterio 5.2a.5 sobre el estado FINAL de la
        /// vista única: hongo de la colonia 0 visible (barra > 0 y glifo/texto),
        /// línea de cortes presente, colonia 1 Lasius sin hongo. Devuelve el
        /// código de salida del editor (0 = criterio cumplido).</summary>
        private static int AssertAtta()
        {
            var cardGo = GameObject.Find("ViewCard_0");
            if (cardGo == null) { Debug.Log("[AttaSmoke] ✗ SIN ViewCard_0"); return 1; }

            var body = cardGo.transform.Find("Body");
            string text = body != null ? body.GetComponent<UnityEngine.UI.Text>().text : "";
            Debug.Log($"[AttaSmoke] tarjeta=[{text.Replace('\n', ';')}]");

            var behaviour = cardGo.GetComponent<ViewCardBehaviour>();
            int failures = 0;
            void Need(bool ok, string what)
            {
                if (ok) Debug.Log($"[AttaSmoke] ✓ {what}");
                else { failures++; Debug.Log($"[AttaSmoke] ✗ {what}"); }
            }

            // 1) La tarjeta del modelo puro llega con hongo y cortes (canal A/C).
            var card0 = behaviour.Cards.Cards.Values.FirstOrDefault(c => c.ColonyId == 0);
            var card1 = behaviour.Cards.Cards.Values.FirstOrDefault(c => c.ColonyId == 1);
            Need(card0?.Colony is { FungusMax: > 0f }, "colonia 0: FungusMax > 0 (canal A)");
            Need((card0?.Colony?.Fungus ?? 0f) > 0f, "colonia 0: fungus > 0 acumulado (canal A)");
            Need(card0 is { LeafCuts: > 0 }, "colonia 0: cortes acumulados > 0 (canal C)");
            Need(card0 is { FungusFed: > 0 }, "colonia 0: fungusFed acumulado > 0 (canal C)");
            Need(text.Contains("hongo ["), "tarjeta: línea del hongo renderizada");
            Need(text.Contains("cortes "), "tarjeta: línea de cortes renderizada");

            // 2) La barra del hongo existe y está LLENA > 0 (franja ocre).
            var fungusBar = cardGo.transform.Find("FungusBar_0");
            var fungusImg = fungusBar != null ? fungusBar.GetComponent<UnityEngine.UI.Image>() : null;
            Need(fungusImg != null && fungusImg.fillAmount > 0f,
                $"barra del hongo FungusBar_0 con fill > 0 (fill={fungusImg?.fillAmount:0.##})");

            // 3) La colonia 1 (Lasius): sin hongo ni cortadora.
            Need(card1?.Colony is { FungusMax: 0f }, "colonia 1: FungusMax == 0 (Lasius)");
            Need((card1?.LeafCuts ?? 0) == 0 && (card1?.FungusFed ?? 0) == 0,
                "colonia 1: sin actividad de cortadora");

            Debug.Log($"[AttaSmoke] veredicto: {(failures == 0 ? "CRITERIO CUMPLIDO" : "FALLO")} ({failures} fallos)");
            return failures == 0 ? 0 : 1;
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
