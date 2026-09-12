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
                JumpToNewestAnchored();
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
                var container = hud.ToastContainer;
                if (container != null)
                {
                    // El contenedor ancla su esquina superior al toast superior:
                    // py = distancia del ratón al borde superior del contenedor.
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        container, Input.mousePosition, null, out var local);
                    JumpToToastAtContainerY(container.rect.height * 0.5f - local.y);
                }
                else
                {
                    // Fallback v1 (pila de texto sin contenedor): por índice.
                    int idx = Mathf.Clamp((int)(Input.mousePosition.y / 22f), 0, toasts.Count - 1);
                    JumpToIndex(idx);
                }
            }
        }

        // ── Entrada sin dispositivo (F5.2) ──────────────────────────────────
        // El salto de cámara se dispara con J o con Alt+click sobre un toast; las
        // dos acciones tienen punto de entrada propio para poder verificarlas sin
        // teclado ni ratón, midiendo el destino REAL del salto (LastJumpTarget)
        // contra el ancla que el Core puso en la alerta.

        /// <summary>Salta al toast más reciente con ancla válida (tecla
        /// <see cref="JumpKey"/>). Devuelve <c>false</c> si ninguno tiene ancla
        /// (p. ej. alerta sin coordenadas): en ese caso la cámara NO se mueve.</summary>
        public bool JumpToNewestAnchored()
        {
            var hud = Hud;
            if (hud == null) return false;
            foreach (var toast in hud.Toasts.Active)
            {
                if (toast.X >= 0f && toast.Y >= 0f)
                {
                    JumpTo(toast.X, toast.Y);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Salta al toast que hay bajo una distancia vertical
        /// <paramref name="py"/> medida desde el borde superior del contenedor
        /// (el Alt+click de F5.1, ya resuelto a píxeles del contenedor).</summary>
        public bool JumpToToastAtContainerY(float py)
        {
            var hud = Hud;
            if (hud == null) return false;
            var elements = AntSim.Unity.Scripts.Streaming.HudElementLayoutModel.ToastElements(hud.Toasts.Active);
            var hit = AntSim.Unity.Scripts.Streaming.HudElementLayoutModel.ToastAt(elements, py);
            if (hit == null || !hit.HasAnchor)
            {
                JumpToMiss();
                return false;
            }
            JumpTo(hit.AnchorX, hit.AnchorY);
            return true;
        }

        /// <summary>Salto por índice de la pila (fallback v1, sin contenedor).</summary>
        public bool JumpToIndex(int index)
        {
            var hud = Hud;
            if (hud == null) return false;
            var toasts = hud.Toasts.Active;
            if (index < 0 || index >= toasts.Count) return false;
            var toast = toasts[index];
            if (toast.X < 0f || toast.Y < 0f) return false;
            JumpTo(toast.X, toast.Y);
            return true;
        }

        /// <summary>Registra un click sobre un toast SIN ancla (no mueve la cámara:
        /// el contrato §1 solo promete salto donde la alerta trae (x, y)).</summary>
        private void JumpToMiss() => LastJumpMiss = true;

        /// <summary>Hubo un click sobre un toast sin ancla (para tests/feedback).</summary>
        public bool LastJumpMiss { get; private set; }

        /// <summary>Mueve la cámara sobre el ancla de mundo (x, y).</summary>
        public void JumpTo(float worldX, float worldY)
        {
            var cam = TargetCamera != null ? TargetCamera : Camera.main;
            if (cam == null) return;
            var target = new Vector3(worldX, CameraHeight, worldY);
            cam.transform.position = target;
            LastJumpTarget = target;
            LastJumpMiss = false;
        }
    }
}
