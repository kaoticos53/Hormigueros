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
        [Tooltip("Botones nativos: [0]=ImportConfirm [1]=ImportCancel [2]=RestartWithPlan.")]
        public UnityEngine.UI.Button?[]? NativeButtons;

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

        private readonly Streaming.ColonyCardModel _cards = new();
        private readonly Streaming.CommandHistoryModel _history = new();
        private Streaming.HudToastsModel? _toasts;

        /// <summary>Modelo puro de tarjetas (tests y HUDs alternativos).</summary>
        public Streaming.ColonyCardModel Cards => _cards;

        /// <summary>Modelo puro de toasts: se crea perezoso con los valores ya
        /// deserializados por Unity (el constructor corre antes del deserialize).</summary>
        public Streaming.HudToastsModel Toasts =>
            _toasts ??= new Streaming.HudToastsModel(ToastLifetime, ToastMaxStack);

        /// <summary>Modelo puro del historial de comandos (auditoría §4).</summary>
        public Streaming.CommandHistoryModel History => _history;

        private ulong _lastTick;

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null) return;
            var p = presenter.Presenter;   // expuesto abajo en SimPresenterBehaviour
            if (p == null) return;

            // Reparto UNA vez por tick nuevo (los toasts envejecen con sim, no con frame).
            var view = p.CurrentTick;
            var toasts = Toasts;
            if (view != null && view.Tick != _lastTick)
            {
                _lastTick = view.Tick;
                _cards.Observe(view);
                toasts.Observe(view);
                _history.Observe(view);
                if (Inspector != null) Inspector.Observe(view);
            }
            toasts.Tick(Time.deltaTime * Mathf.Max(presenter.Speed, 0f));

            RenderCards();
            RenderToasts();
            RenderStockBars();
            RenderHistory();
            RenderDropPlan();
            RenderImportDialog();
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

        /// <summary>F5.1: conecta los botones nativos a las acciones verificadas
        /// (import: Confirmar/Cancelar; drops: Reiniciar con plan). Llamar UNA vez
        /// desde el bootstrapper tras crear los botones.</summary>
        public void BindNativeButtons(
            UnityEngine.UI.Button? importConfirm,
            UnityEngine.UI.Button? importCancel,
            UnityEngine.UI.Button? restartWithPlan)
        {
            NativeButtons = new[] { importConfirm, importCancel, restartWithPlan };
            if (importConfirm != null)
                importConfirm.onClick.AddListener(() =>
                { if (ImportDialog != null) ImportDialog.Confirm(); RefreshButtons(); });
            if (importCancel != null)
                importCancel.onClick.AddListener(() =>
                { if (ImportDialog != null) ImportDialog.Cancel(); RefreshButtons(); });
            if (restartWithPlan != null)
                restartWithPlan.onClick.AddListener(() =>
                { if (DropPlan != null) DropPlan.RestartWithPlan(); });
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            if (NativeButtons == null) return;
            bool confirmEnabled = ImportDialog != null && ImportDialog.Model.CurrentPhase == Streaming.ImportDialogModel.Phase.Reviewing;
            int dropCount = DropPlan?.Plan.Marks.Count ?? 0;
            var specs = Streaming.HudElementLayoutModel.ImportButtons(confirmEnabled);
            specs.AddRange(Streaming.HudElementLayoutModel.DropPlanButtons(dropCount));
            // specs[0]=ImportConfirm specs[1]=ImportCancel specs[2]=RestartWithPlan
            for (int i = 0; i < NativeButtons.Length && i < specs.Count; i++)
                if (NativeButtons[i] != null) NativeButtons[i].interactable = specs[i].Enabled;
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
        private readonly Dictionary<string, UnityEngine.UI.Text> _toastItems = new();

        private void RenderToastsPerElement()
        {
            var container = ToastContainer!;
            var template = ToastTemplate!;
            var active = _toasts.Active;
            var elements = Streaming.HudElementLayoutModel.ToastElements(active);

            // Desactiva los que ya no están (pool, no Destroy).
            var present = new HashSet<string>();
            foreach (var e in elements) present.Add(e.Key);
            foreach (var kv in _toastItems)
                kv.Value.gameObject.SetActive(present.Contains(kv.Key));

            foreach (var e in elements)
            {
                if (!_toastItems.TryGetValue(e.Key, out var text) || text == null)
                {
                    var item = UnityEngine.Object.Instantiate(template, container);
                    item.name = "Toast_" + e.Key;
                    text = item.GetComponentInChildren<UnityEngine.UI.Text>();
                    _toastItems[e.Key] = text;
                }
                var rt = (RectTransform)text.transform.parent;
                rt.anchoredPosition = new Vector2(0f, -e.Y);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, e.Height);
                text.text = e.Text;
                text.color = LevelColor(e.Level);
                text.gameObject.SetActive(true);
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
            if (text == null) return;
            if (ImportDialogText.text != text) ImportDialogText.text = text;
        }
    }
}
