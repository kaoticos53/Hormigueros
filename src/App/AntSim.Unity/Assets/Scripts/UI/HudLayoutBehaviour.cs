using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Montaje del HUD uGUI (F4.2): reparte cada TickView del stream entre las
    /// tarjetas de colonia, la pila de toasts y el inspector. Componente FINO —
    /// toda la lógica vive en los modelos puros (ColonyCardModel,
    /// HudToastsModel, AntInspectorModel), verificados headless en la suite.
    /// En el editor de Unity: crea un Canvas y cuelga aquí un elemento con los
    /// Texts asignados; el layout v1 es una columna de tarjetas + toasts arriba.
    /// </summary>
    public sealed class HudLayoutBehaviour : MonoBehaviour
    {
        [Header("Fuente (el presenter bombea el stream)")]
        public SimPresenterBehaviour? Presenter;

        [Header("Tarjetas de colonia (una Text por colonia, orden = id)")]
        public UnityEngine.UI.Text?[]? ColonyCardTexts;

        [Header("Toasts (v1: un Text; v2 F5.1: elementos clicables — ToastContainer)")]
        public UnityEngine.UI.Text? ToastsText;
        public Color[] ToastLevelColors = { Color.gray, new Color(1f, 0.6f, 0.1f), Color.green, Color.red };
        public float ToastLifetime = Streaming.HudToastsModel.DefaultLifetime;
        public int ToastMaxStack = Streaming.HudToastsModel.DefaultMaxStack;

        [Header("F5.1 per-elemento (opcionales — v1 texto si quedan null)")]
        [Tooltip("Contenedor uGUI donde se crea un elemento clicable POR toast.")]
        public RectTransform? ToastContainer;
        [Tooltip("Plantilla: RectTransform con Text+Image (se instancia por toast).")]
        public RectTransform? ToastTemplate;
        [Tooltip("Barras de stock por colonia (Image fill). Orden = id de colonia.")]
        public UnityEngine.UI.Image?[]? StockBars;
        [Tooltip("Botones nativos, emparejados por acción en tiempo de arranque (BindNativeButtons).")]
        public UnityEngine.UI.Button?[]? NativeButtons;
        [Tooltip("Campo de ruta del modal de importación (escribe GenomePath del diálogo).")]
        public UnityEngine.UI.InputField? ImportPathField;

        [Header("Inspector (opcional: reutiliza el AntInspectorBehaviour existente)")]
        public AntInspectorBehaviour? Inspector;

        [Header("Historial de comandos (opcional, contrato HUD §4)")]
        public UnityEngine.UI.Text? HistoryText;

        [Header("Plan de intervención (F4.4): handler de drops + Text de resumen")]
        public Presenter.DropFoodClickHandler? DropPlan;
        public UnityEngine.UI.Text? DropPlanText;

        [Header("Diálogo de importación (F4.3): handler + Text del diálogo")]
        public Presenter.ImportDialogBehaviour? ImportDialog;
        public UnityEngine.UI.Text? ImportDialogText;

        [Header("F5.1: estructura del HUD (opcionales)")]
        [Tooltip("Barra de estado superior (tick, velocidad, resumen por colonia).")]
        public UnityEngine.UI.Text? StatusText;
        [Tooltip("Pistas de teclado (texto fijo que rellena el bootstrapper).")]
        public UnityEngine.UI.Text? HintsText;
        [Tooltip("Overlay de drag & drop (se muestra cuando se arrastra un .antgenome).")]
        public UnityEngine.UI.Text? DragOverlayText;
        [Tooltip("Velo oscuro del modal de importación (se enciende con el modal).")]
        public GameObject? DialogBackdrop;
        [Tooltip("Panel del modal de importación (contiene el texto y los botones).")]
        public GameObject? ImportModal;

        private readonly Streaming.ColonyCardModel _cards = new();
        private readonly Streaming.CommandHistoryModel _history = new();
        private Streaming.HudToastsModel? _toasts;
        private ulong _lastTick;
        private float _lastSpeed = 1f;

        /// <summary>Modelo puro de tarjetas (tests y HUDs alternativos).</summary>
        public Streaming.ColonyCardModel Cards => _cards;

        /// <summary>Modelo puro de toasts: se crea perezoso con los valores ya
        /// deserializados por Unity (el constructor corre antes del deserialize).</summary>
        public Streaming.HudToastsModel Toasts =>
            _toasts ??= new Streaming.HudToastsModel(ToastLifetime, ToastMaxStack);

        /// <summary>Modelo puro del historial de comandos (auditoría §4).</summary>
        public Streaming.CommandHistoryModel History => _history;

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null) return;
            var p = presenter.Presenter;   // expuesto abajo en SimPresenterBehaviour
            if (p == null) return;

            // Reparto: drena TODOS los ticks PRESENTADOS desde el frame anterior (uno
            // por tick de simulación, en orden). A velocidad alta un frame presenta
            // varios ticks, así que drenarlos todos —y no leer solo el último— es lo
            // que garantiza no perder una alerta cadenciada del canal D (cada alerta
            // existe solo en su tick). Los toasts envejecen con sim, no con frame.
            var toasts = Toasts;
            while (p.TryDequeuePresented(out var view) && view != null)
            {
                _cards.Observe(view);
                toasts.Observe(view);
                _history.Observe(view);
                if (Inspector != null) Inspector.Observe(view);
                _lastTick = view.Tick;
            }
            toasts.Tick(Time.deltaTime * Mathf.Max(presenter.Speed, 0f));
            _lastSpeed = presenter.Speed;

            RenderCards();
            RenderStatus();
            RenderToasts();
            RenderStockBars();
            RenderHistory();
            RenderDropPlan();
            RenderImportDialog();
            RenderModalVisibility();
            RenderDragOverlay();
        }

        /// <summary>F5.1: barra de estado superior — derivada del modelo puro de
        /// tarjetas (tick, velocidad de reproducción y resumen por colonia).</summary>
        private void RenderStatus()
        {
            if (StatusText == null) return;
            string? line = _cards.StatusLine(_lastTick, _lastSpeed);
            if (line == null) return;
            if (StatusText.text != line) StatusText.text = line;
        }

        /// <summary>F5.1: el modal de importación se enseña/oculta por la REGLA del
        /// modelo (<see cref="Streaming.ImportDialogModel.ModalVisible"/>), no por
        /// un SetActive suelto en un botón. Sin esto, la regla y la vista podían
        /// discrepar (el caso real: botones de un modal invisible, pintados).</summary>
        private void RenderModalVisibility()
        {
            if (ImportDialog == null) return;
            bool visible = ImportDialog.Model.ModalVisible;
            if (DialogBackdrop != null && DialogBackdrop.activeSelf != visible)
                DialogBackdrop.SetActive(visible);
            if (ImportModal != null && ImportModal.activeSelf != visible)
                ImportModal.SetActive(visible);
        }

        /// <summary>F5.1: fracción de reserva por colonia en Image fillAmount
        /// (orden del array = id de colonia). El modelo puro calcula fracción
        /// clampada y reserva baja; aquí solo se pinta.</summary>
        private void RenderStockBars()
        {
            if (StockBars == null) return;
            var bars = Streaming.HudElementLayoutModel.StockBars(
                _cards.Cards.Values
                    .Where(c => c.Colony != null)
                    // Colony es un struct nullable: el ! no lo desenvuelve — .Value sí.
                    .Select(c => (c.ColonyId, c.Colony!.Value.Stock, c.Colony.Value.StockMax))
                    .ToList());
            foreach (var b in bars)
            {
                if (b.ColonyId >= StockBars.Length) break;
                var img = StockBars[b.ColonyId];
                if (img == null) continue;
                img.fillAmount = b.Fraction;
                img.color = b.Low ? new Color(0.9f, 0.2f, 0.15f) : new Color(0.35f, 0.75f, 0.3f);
            }
        }

        /// <summary>
        /// F5.1/F5.1: conecta los botones nativos a las acciones verificadas
        /// (import: Inspeccionar/Confirmar/Cancelar; drops: Reiniciar con plan).
        /// Llamar UNA vez desde el bootstrapper tras crear los botones.
        ///
        /// El emparejamiento es por ACCIÓN (no por índice): añadir un botón al
        /// modelo —como el Inspeccionar de F5.1— reordenaba los índices y habría
        /// dejado «Confirmar» llamando a otra cosa, sin que nada fallara.
        /// </summary>
        public void BindNativeButtons(
            UnityEngine.UI.Button? importInspect,
            UnityEngine.UI.Button? importConfirm,
            UnityEngine.UI.Button? importCancel,
            UnityEngine.UI.Button? restartWithPlan)
        {
            _buttons.Clear();
            AddButton(Streaming.HudElementLayoutModel.ButtonAction.ImportInspect, importInspect,
                () => { if (ImportDialog != null) ImportDialog.Inspect(); RefreshButtons(); });
            AddButton(Streaming.HudElementLayoutModel.ButtonAction.ImportConfirm, importConfirm,
                () => { if (ImportDialog != null) ImportDialog.Confirm(); RefreshButtons(); });
            AddButton(Streaming.HudElementLayoutModel.ButtonAction.ImportCancel, importCancel,
                () => { if (ImportDialog != null) ImportDialog.Cancel(); RefreshButtons(); });
            AddButton(Streaming.HudElementLayoutModel.ButtonAction.RestartWithPlan, restartWithPlan,
                () => { if (DropPlan != null) DropPlan.RestartWithPlan(); });
            NativeButtons = new[] { importInspect, importConfirm, importCancel, restartWithPlan };
            RefreshButtons();
        }

        /// <summary>
        /// F5.1: el campo de ruta del modal escribe el estado del diálogo. Sin
        /// esto el jugador no tenía forma de dar la ruta del .antgenome (el campo
        /// existía en el componente pero ninguna UI lo rellenaba) y el pilar de
        /// importación era inalcanzable: los botones del modal estaban ahí, pero
        /// el diálogo no se podía abrir ni alimentar. Enter (onEndEdit) inspecciona.
        /// </summary>
        public void BindImportPathField(UnityEngine.UI.InputField? field)
        {
            ImportPathField = field;
            if (field == null || ImportDialog == null) return;
            field.text = ImportDialog.GenomePath;
            field.onValueChanged.AddListener(v =>
            { if (ImportDialog != null) ImportDialog.GenomePath = v; });
            field.onEndEdit.AddListener(v =>
            {
                if (ImportDialog == null) return;
                ImportDialog.GenomePath = v;
                ImportDialog.Inspect();
            });
        }

        private readonly Dictionary<Streaming.HudElementLayoutModel.ButtonAction, UnityEngine.UI.Button> _buttons = new();

        private void AddButton(Streaming.HudElementLayoutModel.ButtonAction action,
            UnityEngine.UI.Button? button, UnityEngine.Events.UnityAction onClick)
        {
            if (button == null) return;
            _buttons[action] = button;
            button.onClick.AddListener(onClick);
        }

        private void RefreshButtons()
        {
            if (_buttons.Count == 0) return;
            // «Confirmar» solo con tarjeta de cuarentena revisada; el resto siempre.
            bool confirmEnabled = ImportDialog != null && ImportDialog.Model.Card != null;
            int dropCount = DropPlan?.Plan.Marks.Count ?? 0;
            var specs = Streaming.HudElementLayoutModel.ImportButtons(confirmEnabled);
            specs.AddRange(Streaming.HudElementLayoutModel.DropPlanButtons(dropCount));
            foreach (var spec in specs)
                if (_buttons.TryGetValue(spec.Action, out var btn) && btn != null)
                    btn.interactable = spec.Enabled;
        }

        private void RenderCards()
        {
            if (ColonyCardTexts == null) return;
            int i = 0;
            foreach (var kv in _cards.Cards)
            {
                if (i >= ColonyCardTexts.Length) break;
                var text = ColonyCardTexts[i++];
                if (text == null) continue;
                string card = _cards.Render(kv.Key) ?? "";
                if (text.text != card) text.text = card;
            }
        }

        private void RenderToasts()
        {
            // F5.1: per-elemento si hay contenedor+plantilla; fallback v1 si no.
            if (ToastContainer != null && ToastTemplate != null)
            {
                RenderToastsPerElement();
                if (ToastsText != null) ToastsText.text = "";
                return;
            }
            if (ToastsText == null) return;
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var line in _toasts.RenderLines())
            {
                if (n++ > 0) sb.Append('\n');
                sb.Append(line);
            }
            string text = sb.ToString();
            if (ToastsText.text != text) ToastsText.text = text;
        }

        // — F5.1: un rect clicable POR toast (pool por key: sin parpadeos) —
        // Se guarda la RAÍZ del elemento, no el Text: el fondo (Image) vive en la
        // raíz y el texto en un hijo, así que apagar solo el Text dejaba la barra
        // de fondo —y su raycast, porque la Image absorbe clicks— en pantalla
        // después de expirar el toast. Lo destapó el check del Play pass
        // (scripts/playpass-toast-dedupe.sh): el modelo estaba vacío y el
        // contenedor seguía con elementos pintados.
        private readonly Dictionary<string, GameObject> _toastRoots = new();

        private void RenderToastsPerElement()
        {
            var container = ToastContainer!;
            var template = ToastTemplate!;
            var elements = Streaming.HudElementLayoutModel.ToastElements(_toasts.Active);

            // Desactiva los que ya no están (pool, no Destroy): la raíz entera.
            var present = new HashSet<string>();
            foreach (var e in elements) present.Add(e.Key);
            foreach (var kv in _toastRoots)
                kv.Value.SetActive(present.Contains(kv.Key));

            foreach (var e in elements)
            {
                if (!_toastRoots.TryGetValue(e.Key, out var root) || root == null)
                {
                    var item = UnityEngine.Object.Instantiate(template, container);
                    item.name = "Toast_" + e.Key;
                    root = item.gameObject;
                    _toastRoots[e.Key] = root;
                }
                var rt = (RectTransform)root.transform;
                rt.anchoredPosition = new Vector2(0f, -e.Y);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, e.Height);
                var text = root.GetComponentInChildren<UnityEngine.UI.Text>();
                if (text != null)
                {
                    text.text = e.Text;
                    text.color = LevelColor(e.Level);
                }
                root.SetActive(true);
            }
        }

        /// <summary>Color uGUI del nivel de un toast (0 info · 1 ámbar · 2 verde · 3 rojo).</summary>
        public Color LevelColor(byte level)
            => level < ToastLevelColors.Length ? ToastLevelColors[level] : Color.white;

        private void RenderHistory()
        {
            if (HistoryText == null) return;
            string text = _history.Render();
            if (HistoryText.text != text) HistoryText.text = text;
        }

        private void RenderDropPlan()
        {
            if (DropPlanText == null || DropPlan == null) return;
            string summary = DropPlan.Plan.RenderSummary();
            string? err = DropPlan.LastError;
            string text = DropPlan.PlaceMode
                ? $"[DROP] {summary} — click para marcar (D para salir)"
                : summary;
            if (err != null) text += $"\n✗ {err}";
            if (DropPlanText.text != text) DropPlanText.text = text;
        }

        private void RenderImportDialog()
        {
            if (ImportDialogText == null || ImportDialog == null) return;
            string? text = ImportDialog.DialogText;
            // F5.1: con el modal abierto también se enseña la guía del modelo
            // (antes de inspeccionar, DialogText es null y el panel quedaba vacío).
            if (text == null && ImportDialog.Model.ModalVisible) text = ImportDialog.Model.RenderDialog();
            if (text == null) return;
            if (ImportDialogText.text != text) ImportDialogText.text = text;
        }

        /// <summary>F5.1 deuda: overlay de drag &amp; drop. Cuando el jugador arrastra
        /// un .antgenome sobre la ventana, se muestra un borde luminoso con
        /// el texto "Soltar para importar" en el centro de la pantalla.</summary>
        private void RenderDragOverlay()
        {
            if (DragOverlayText == null) return;
            bool hovering = ImportDialog != null && ImportDialog.IsDragHovering;
            if (DragOverlayText.gameObject.activeSelf != hovering)
                DragOverlayText.gameObject.SetActive(hovering);
        }
    }
}
