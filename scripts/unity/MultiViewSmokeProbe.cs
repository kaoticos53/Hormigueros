#if UNITY_EDITOR
using System.Text;
using UnityEngine;
using UnityEditor;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.EditorTools;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>Smoke en vivo del multi-visor (F5.1bis): entra en Play sobre la
    /// escena multi-visor de 2 vistas, deja correr la reproducción N frames y
    /// extrae por vista: poses muestreadas, capa de render, y texto de tarjeta.
    /// Escribe las líneas [MultiSmoke] en el log y cierra el editor.</summary>
    public static class MultiViewSmokeProbe
    {
        private static int _frames;
        private static double _deadline;
        private static bool _reported;
        private static bool _enteringPlay;

        /// <summary>Punto de entrada BATCH: monta la escena smoke (2 vistas con
        /// ReplayFile) y entra en Play para que `Tick` muestree el mundo en vivo.
        /// Play mode SÍ corre en batchmode — es lo que reproduce el stream.
        /// Uso: -executeMethod …MultiViewSmokeProbe.RunSmoke</summary>
        public static void RunSmoke()
        {
            MultiSimBootstrapper.CreateMultiSimSmokeScene();
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

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            // Solo una vez, tras entrar en Play (la escena ya está cargada).
            if (!EditorApplication.isPlaying || _reported) return;
            if (_frames == 0) _deadline = EditorApplication.timeSinceStartup + 25.0;
            _frames++;

            if (EditorApplication.timeSinceStartup < _deadline) return;
            _reported = true;

            var sb = new StringBuilder();
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!cam.name.StartsWith("Camera_V")) continue;
                string vid = cam.name.Substring("Camera_V".Length);
                var presenterGo = GameObject.Find("SimPresenter_V" + vid);
                if (presenterGo == null) { Log(vid, "SIN PRESENTER"); continue; }
                var p = presenterGo.GetComponent<SimPresenterBehaviour>();
                var state = p.CurrentState;
                int ants = state?.Ants?.Count ?? 0;
                int items = state?.Items?.Count ?? 0;
                var cur = p.Presenter?.CurrentTick;
                ulong tick = cur?.Tick ?? 0;
                // Muestra 3 poses para comparar entre vistas.
                string poses = "";
                if (state != null)
                    for (int i = 0; i < state.Ants.Count && i < 3; i++)
                        poses += $"({state.Ants[i].X:0.#},{state.Ants[i].Y:0.#}) ";
                // Tarjeta compacta de la vista.
                var cardGo = GameObject.Find("ViewCard_" + vid);
                string cardText = "";
                if (cardGo != null)
                {
                    var body = cardGo.transform.Find("Body");
                    if (body != null) cardText = body.GetComponent<UnityEngine.UI.Text>().text;
                }
                Log(vid, $"layer={p.RenderLayer} camMask={cam.cullingMask} tick={tick} ants={ants} items={items} poses=[{poses}] card=[{cardText.Replace('\n',';')}]");
            }
            EditorApplication.isPlaying = false;
            // En batch el `-quit` del CLI no dispara con Play activo: salir a mano.
            EditorApplication.Exit(0);
        }

        private static void Log(string vid, string msg)
            => Debug.Log($"[MultiSmoke] V{vid} {msg}");
    }
}
#endif
