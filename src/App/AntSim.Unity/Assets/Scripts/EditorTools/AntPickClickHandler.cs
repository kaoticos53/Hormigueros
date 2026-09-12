using UnityEngine;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// Wire-up del click de selección (F4.2, contrato HUD §5): un componente de
    /// ESCENA — creado por el SceneBootstrapper — convierte el click del ratón en
    /// un raycast al plano del mundo y llama a AntInspectorBehaviour.PickNearest
    /// con el estado interpolado del presenter. Es lo único que Unity puede hacer
    /// mejor que el Core: mapear pantalla→mundo. La tarjeta y el linaje siguen
    /// siendo el modelo puro. Componente de RUNTIME (la escena también funciona
    /// en build); vive junto al bootstrapper por cohesión de montaje.
    /// </summary>
    public sealed class AntPickClickHandler : MonoBehaviour
    {
        [Tooltip("Presenter cuyo estado interpolado consume PickNearest.")]
        public Presenter.SimPresenterBehaviour? Presenter;

        [Tooltip("Inspector que recibe la selección del click.")]
        public Presenter.AntInspectorBehaviour? Inspector;

        [Tooltip("Plano del mundo (y=0) para proyectar el ray del click.")]
        public float WorldPlaneY = 0f;

        private void Update()
        {
            if (Presenter == null || Inspector == null) return;
            if (!Input.GetMouseButtonDown(0)) return;
            PickAtScreen(Input.mousePosition);
        }

        // ── Entrada sin dispositivo (F5.2) ──────────────────────────────────
        // `Input` no es inyectable desde el CLI, pero estas acciones sí: el
        // pass en vivo puede seleccionar una hormiga con el MISMO camino que el
        // click (rayo real de la cámara → raycast al plano → PickNearest sobre
        // el estado interpolado del presenter). El contrato de selección queda
        // así verificado sin un humano delante del ratón.

        /// <summary>Selecciona la hormiga bajo un punto de PANTALLA (el camino
        /// del click). Devuelve el id seleccionado (0 = nada).</summary>
        public uint PickAtScreen(Vector2 screenPoint)
        {
            if (!ScreenRay(screenPoint, out float hx, out float hz)) return 0;
            return PickAtWorld(new Vector3(hx, WorldPlaneY, hz));
        }

        /// <summary>Selecciona la hormiga más cercana a un punto del mundo.
        /// Devuelve el id seleccionado (0 = ninguna dentro del radio).</summary>
        public uint PickAtWorld(Vector3 worldPoint)
        {
            if (Inspector == null) return 0;
            uint id = Inspector.PickNearest(worldPoint);
            if (id != 0)
                Debug.Log($"[AntPick] seleccionada hormiga #{id} @ ({worldPoint.x:0}, {worldPoint.z:0})");
            return id;
        }

        /// <summary>Rayo de la cámara por un punto de pantalla hasta el plano del mundo.</summary>
        private bool ScreenRay(Vector2 screenPoint, out float hitX, out float hitZ)
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
                origin = new Vector3(0f, 100f, 0f);
                direction = Vector3.down;
            }
            return Streaming.WorldPlaneRay.TryHit(
                origin.x, origin.y, origin.z, direction.x, direction.y, direction.z,
                WorldPlaneY, out hitX, out hitZ);
        }
    }
}
