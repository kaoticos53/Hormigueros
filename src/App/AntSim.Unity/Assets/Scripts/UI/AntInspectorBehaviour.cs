using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Tarjeta de inspección de hormiga (F4.2, contrato HUD §5). Componente FINO:
    /// expone la hormiga a inspeccionar (por click del raycast de la F4.2 real, o
    /// por id directo), bombea los TickViews que ya consume <see cref="SimPresenterBehaviour"/>
    /// y pinta <see cref="AntInspectorModel.RenderCard"/> en el UI Text asignado.
    /// Toda la lógica vive en el modelo puro (verificado headless en la suite).
    /// </summary>
    public sealed class AntInspectorBehaviour : MonoBehaviour
    {
        [Header("UI")]
        public UnityEngine.UI.Text? CardText;    // uGUI Text de la tarjeta
        public bool Visible = true;

        [Header("Selección (v1: por id; el click llega en F4.2)")]
        [Tooltip("Id de la hormiga a seguir (0 = ninguna). El modelo acepta ids aún no vistos.")]
        public uint SelectAntId;

        [Tooltip("Referencia opcional al presenter para auto-seleccionar la hormiga más cercana al click.")]
        public SimPresenterBehaviour? Presenter;

        private readonly Streaming.AntInspectorModel _model = new();

        /// <summary>Modelo puro subyacente (los tests lo usan directamente).</summary>
        public Streaming.AntInspectorModel Model => _model;

        /// <summary>Texto renderizado actual (para tests y para HUDs no-uGUI).</summary>
        public string Card => _model.RenderCard();

        /// <summary>Inyecta un TickView observado (p. ej. desde el bombeo del presenter).</summary>
        public void Observe(Streaming.GameStreamParser.TickView view) => _model.Observe(view);

        private void Update()
        {
            // — Selección por id directa (la vía del click llega con el raycast F4.2) —
            if (SelectAntId != 0 && _model.SelectedId != SelectAntId)
                _model.Select(SelectAntId);

            if (CardText != null)
            {
                CardText.enabled = Visible;
                string card = _model.RenderCard();
                if (CardText.text != card) CardText.text = card;
            }
        }

        /// <summary>
        /// Selección por click de mundo (la F4.2 la llama desde su raycast): elige
        /// la hormiga viva más cercana al punto dentro de un radio de tolerancia.
        /// Devuelve el id seleccionado (0 si no hubo candidata).
        /// </summary>
        public uint PickNearest(Vector3 worldPoint, float radius = 2.5f)
        {
            var state = Presenter != null ? Presenter.CurrentState : null;
            if (state == null) return 0;

            uint best = 0;
            float bestDist = radius * radius;
            foreach (var a in state.Ants)
            {
                if (!a.Alive) continue;
                float dx = a.X - worldPoint.x, dy = a.Y - worldPoint.z;
                float d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = a.Id; }
            }
            if (best != 0) _model.Select(best);
            return best;
        }
    }
}
