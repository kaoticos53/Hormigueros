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

        [Tooltip("Tecla de deshacer el último drop marcado (contrato §4: «Z deshace»).")]
        public KeyCode UndoKey = KeyCode.Z;

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) TogglePlaceMode();
            // Deshace el último drop del plan (contrato §4). Fuera del modo
            // marcar también funciona: el plan sobrevive a PlaceMode.
            if (Input.GetKeyDown(UndoKey)) UndoLastDrop();

            if (Presenter == null) return;
            Plan.ObserveTick(Presenter.Presenter.CurrentTick?.Tick ?? 0);

            if (!Input.GetMouseButtonDown(0)) return;
            // El «¿está el modo activo?» lo contesta PlaceAtScreen: la acción del
            // click vive en un solo sitio y se puede verificar por sí sola.
            PlaceAtScreen(Input.mousePosition);
        }

        // ── Entradas sin dispositivo (F5.2) ─────────────────────────────────
        // Las tres acciones que dispara el jugador tienen aquí su punto de
        // entrada y Update() SOLO las cablea a `Input`. Así el pass en vivo
        // puede disparar el mismo camino —el rayo sale de la cámara real y el
        // plan es el real— sin un dispositivo de entrada, que es justo lo que
        // obligaba a un humano para cerrar el bloque 4. `Input` no es
        // inyectable desde el CLI, pero estas acciones sí.

        /// <summary>Alterna el modo marcar-drops (tecla <see cref="ToggleKey"/>).
        /// Devuelve el nuevo estado.</summary>
        public bool TogglePlaceMode()
        {
            PlaceMode = !PlaceMode;
            LastError = null;
            return PlaceMode;
        }

        /// <summary>Deshace el último drop del plan (tecla <see cref="UndoKey"/>).
        /// Devuelve <c>false</c> si no había ninguno.</summary>
        public bool UndoLastDrop()
        {
            bool undone = Plan.Undo();
            if (undone) Debug.Log("[DropFood] último drop deshecho");
            LastError = null;
            return undone;
        }

        /// <summary>
        /// Marca un drop en el punto del mundo bajo un punto de PANTALLA —el camino
        /// exacto del click izquierdo— y devuelve el error de validación
        /// (<c>null</c> = marcado). Sin el modo marcar activo NO hace nada: el
        /// contrato «D activa el modo, el click marca» se cumple aquí, que es el
        /// único sitio por el que pasa el click.
        /// </summary>
        public string? PlaceAtScreen(Vector2 screenPoint)
        {
            // El motivo del rechazo se devuelve pero NO se guarda en LastError:
            // LastError se pinta en el panel del HUD, y un click con el modo
            // apagado es lo normal mientras se juega — no un error que anunciar.
            if (!PlaceMode) return "modo apagado";
            if (Presenter == null) return LastError = "sin presenter";

            if (!ScreenRay(screenPoint, out float hx, out float hz)) return LastError;

            ulong tick = Plan.CurrentTick + DropHorizon;
            // El límite va en UNIDADES de mundo (grid × WorldUnits.PerCell): el
            // raycast entrega unidades y el CLI las espera así (--drop tick:x:y).
            LastError = Plan.TryPlace(tick, hx, hz,
                Streaming.WorldUnits.WorldSize(GridCells()));
            if (LastError == null)
                Debug.Log($"[DropFood] marcado t{tick} @ ({hx:0}, {hz:0})");
            return LastError;
        }

        /// <summary>Rayo de la cámara por un punto de pantalla hasta el plano y=0.</summary>
        private static bool ScreenRay(Vector2 screenPoint, out float hitX, out float hitZ)
        {
            var cam = Camera.main;
            Vector3 origin, direction;
            if (cam != null)
            {
                var ray = cam.ScreenPointToRay(screenPoint);
                origin = ray.origin;
                direction = ray.direction;
            }
            else
            {
                // Escena sin Camera.main: rayo vertical sobre el origen.
                origin = new Vector3(0f, 100f, 0f);
                direction = Vector3.down;
            }
            return Streaming.WorldPlaneRay.TryHit(
                origin.x, origin.y, origin.z, direction.x, direction.y, direction.z,
                0f, out hitX, out hitZ);
        }

        /// <summary>Celdas del grid: de la cabecera del stream (parser) o, en su
        /// defecto, del campo del componente.</summary>
        private int GridCells()
        {
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
            // La escena se recarga: la instancia del presenter se destruye, así que
            // el plan tiene que viajar a la PRÓXIMA instancia o el CLI arrancaría
            // sin los drops (el botón parecería funcionar y no marcar nada).
            SimPresenterBehaviour.ArmPendingArgsForNextScene(copy);
            PlaceMode = false;
            LastError = null;
            Plan.Clear();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}
