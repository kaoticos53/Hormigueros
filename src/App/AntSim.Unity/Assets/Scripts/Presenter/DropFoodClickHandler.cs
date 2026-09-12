using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Click-to-place de DropFood (F4.4, «Intervenir»): con el modo activo, el
    /// click izquierdo marca un drop en el punto del mundo y el plan se acumula
    /// en el <see cref="DropFoodPlanModel"/> puro; el botón «Reiniciar con plan»
    /// relanza la misma partida sembrada con <c>--drop tick:x:y</c> vía el CLI.
    /// Mismo reparto que AntPickClickHandler: aquí solo vive pantalla→mundo,
    /// la validación y el plan son del modelo puro. Componente de RUNTIME.
    /// </summary>
    public sealed class DropFoodClickHandler : MonoBehaviour
    {
        [Tooltip("Presenter cuyo tick actual alimenta el plan (y que relanza).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Tecla que activa/desactiva el modo marcar drops (D por defecto).")]
        public KeyCode ToggleKey = KeyCode.D;

        /// <summary>Modo marcar-drops activo (lo alterna la tecla).</summary>
        public bool PlaceMode { get; private set; }

        /// <summary>Plan puro compartido (el HUD muestra el resumen y la cuota).</summary>
        public readonly Streaming.DropFoodPlanModel Plan = new();

        /// <summary>Último rechazo para feedback en el HUD (null = sin error).</summary>
        public string? LastError { get; private set; }

        /// <summary>Horizonte de marcado: drops a t+600 (20 s de sim) por defecto.</summary>
        public ulong DropHorizon = 600;

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                PlaceMode = !PlaceMode;
                LastError = null;
            }
            if (Presenter == null) return;
            Plan.ObserveTick(Presenter.Presenter.CurrentTick?.Tick ?? 0);

            if (!PlaceMode || !Input.GetMouseButtonDown(0)) return;

            Ray ray = Camera.main != null
                ? Camera.main.ScreenPointToRay(Input.mousePosition)
                : new Ray(new Vector3(0, 100, 0), Vector3.down);
            if (ray.direction.y == 0f) return;

            float t = (0f - ray.origin.y) / ray.direction.y;
            if (t < 0f) return;
            Vector3 hit = ray.origin + ray.direction * t;

            ulong tick = Plan.CurrentTick + DropHorizon;
            LastError = Plan.TryPlace(tick, hit.x, hit.z, Grid());
            if (LastError == null)
                Debug.Log($"[DropFood] marcado t{tick} @ ({hit.x:0}, {hit.z:0})");
        }

        private float Grid()
        {
            // El grid vive en la cabecera del stream (parser); el presenter lo
            // expone vía Header. Fallback defensivo al campo del componente.
            return Presenter.Presenter.Header?.Grid ?? Presenter.Grid;
        }

        /// <summary>Relanza la MISMA partida sembrada con el plan horneado.
        /// El reinicio de escena relanza el CLI vía Start() con los nuevos args.</summary>
        public void RestartWithPlan()
        {
            if (Presenter == null) return;
            var args = Plan.BuildCliArgs();
            var copy = new string[args.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = args[i];
            Presenter.PendingDropArgs = copy;
            PlaceMode = false;
            LastError = null;
            Plan.Clear();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}
