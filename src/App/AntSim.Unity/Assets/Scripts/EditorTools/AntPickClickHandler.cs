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

            Ray ray = Camera.main != null
                ? Camera.main.ScreenPointToRay(Input.mousePosition)
                : new Ray(new Vector3(0, 100, 0), Vector3.down);
            if (ray.direction.y == 0f) return;

            float t = (WorldPlaneY - ray.origin.y) / ray.direction.y;
            if (t < 0f) return;
            Vector3 hit = ray.origin + ray.direction * t;

            uint id = Inspector.PickNearest(hit);
            if (id != 0)
                Debug.Log($"[AntPick] seleccionada hormiga #{id} @ ({hit.x:0}, {hit.z:0})");
        }
    }
}
