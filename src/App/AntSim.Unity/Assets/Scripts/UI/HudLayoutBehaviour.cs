using System.Collections.Generic;
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

        [Header("Toasts (un Text vertical; el color lo fija ToastLevelColors)")]
        public UnityEngine.UI.Text? ToastsText;
        public Color[] ToastLevelColors = { Color.gray, new Color(1f, 0.6f, 0.1f), Color.green, Color.red };
        public float ToastLifetime = HudToastsModel.DefaultLifetime;
        public int ToastMaxStack = HudToastsModel.DefaultMaxStack;

        [Header("Inspector (opcional: reutiliza el AntInspectorBehaviour existente)")]
        public AntInspectorBehaviour? Inspector;

        [Header("Historial de comandos (opcional, contrato HUD §4)")]
        public UnityEngine.UI.Text? HistoryText;

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
            RenderHistory();
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

        /// <summary>Color uGUI del nivel de un toast (0 info · 1 ámbar · 2 verde · 3 rojo).</summary>
        public Color LevelColor(byte level)
            => level < ToastLevelColors.Length ? ToastLevelColors[level] : Color.white;

        private void RenderHistory()
        {
            if (HistoryText == null) return;
            string text = _history.Render();
            if (HistoryText.text != text) HistoryText.text = text;
        }
    }
}
