using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Salto de cámara por toast (F4.2 polish, contrato §1: «el ancla de cámara
    /// viene del canal D»). Click con Alt (o el botón derecho sobre la pila) en
    /// un toast de la pila → mueve la cámara al ancla (x, y) de ESA alerta. El
    /// mapeo pantalla→mundo vive aquí; qué ancla tiene cada toast ya lo decidió
    /// el Core al derivar la alerta.
    /// </summary>
    public sealed class ToastClickCameraJump : MonoBehaviour
    {
        [Tooltip("HUD cuya pila de toasts se consume (modelo puro con anclas).")]
        public HudLayoutBehaviour? Hud;

        [Tooltip("Cámara a mover (null = Camera.main).")]
        public Camera? TargetCamera;

        [Tooltip("Altura de la cámara sobre el mundo tras el salto.")]
        public float CameraHeight = 40f;

        [Tooltip("Tecla de salto al toast más reciente con ancla.")]
        public KeyCode JumpKey = KeyCode.J;

        /// <summary>Último salto realizado (para tests y feedback).</summary>
        public Vector3? LastJumpTarget { get; private set; }

        private void Update()
        {
            var hud = Hud;
            if (hud == null) return;

            if (Input.GetKeyDown(JumpKey))
            {
                // El más reciente con ancla válida (el tope de la pila suele serlo).
                foreach (var toast in hud.Toasts.Active)
                {
                    if (toast.X >= 0f && toast.Y >= 0f)
                    {
                        JumpTo(toast.X, toast.Y);
                        return;
                    }
                }
                return;
            }

            // F5.1 — click exacto: la posición del ratón (px desde arriba, en el
            // espacio del contenedor de toasts) se pasa al hit-test del modelo
            // puro. Alt ya no es necesario si el contenedor per-elemento existe
            // (cada rect captura su propio click), pero se mantiene como atajo
            // para la pila v1 de texto.
            if (Input.GetMouseButtonDown(0) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            {
                var toasts = hud.Toasts.Active;
                if (toasts.Count == 0) return;
                // El contenedor ancla su esquina superior al toast superior:
                // py = distancia del ratón al borde superior del contenedor.
                var container = hud.ToastContainer;
                float py;
                if (container != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        container, Input.mousePosition, null, out var local);
                    py = container.rect.height * 0.5f - local.y;
                }
                else
                {
                    py = Input.mousePosition.y; // fallback v1: aproximación por índice
                    int idx = Mathf.Clamp((int)(py / 22f), 0, toasts.Count - 1);
                    var t1 = toasts[idx];
                    if (t1.X >= 0f && t1.Y >= 0f) JumpTo(t1.X, t1.Y);
                    return;
                }

                var elements = AntSim.Unity.Scripts.Streaming.HudElementLayoutModel.ToastElements(toasts);
                var hit = AntSim.Unity.Scripts.Streaming.HudElementLayoutModel.ToastAt(elements, py);
                if (hit != null && hit.HasAnchor)
                    JumpTo(hit.AnchorX, hit.AnchorY);
            }
        }

        /// <summary>Mueve la cámara sobre el ancla de mundo (x, y).</summary>
        public void JumpTo(float worldX, float worldY)
        {
            var cam = TargetCamera != null ? TargetCamera : Camera.main;
            if (cam == null) return;
            var target = new Vector3(worldX, CameraHeight, worldY);
            cam.transform.position = target;
            LastJumpTarget = target;
        }
    }
}
