using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Tarjeta de colonia (F4.2, contrato HUD §2) — modelo PURO. Consume los
    /// TickViews del stream (canal A para colonia/reserva, canal C por colonia
    /// para el flujo de 1 s, canal D para el semáforo y los contadores de
    /// cerebros) y produce el render de texto fijo de la tarjeta. Sin
    /// UnityEngine: verificado headless en la suite contra streams reales.
    ///
    /// Regla dura del contrato §6.4: el semáforo NO se calcula aquí — llega
    /// calculado por el Core (RelayVerdict) en el campo light del stream; la
    /// tarjeta solo lo pinta. La UI nunca inventa umbrales.
    /// </summary>
    public sealed class ColonyCardModel
    {
        public const int AdultsHardCap = 40;

        /// <summary>Estado acumulado de UNA colonia para su tarjeta.</summary>
        public sealed class Card
        {
            public int ColonyId;
            public GameStreamParser.ColonyView? Colony;      // último canal A
            public byte Light = 0;                            // 0 gris · 1 ámbar · 2 verde
            public GameStreamParser.ColonyMetricsView? Window; // última ventana de 1 s
            public long EliteEntered;                         // acumulados desde el arranque
            public long GenomeDiscarded;
        }

        private readonly Dictionary<int, Card> _cards = new();

        public IReadOnlyDictionary<int, Card> Cards => _cards;

        /// <summary>Consume un tick completo: actualiza todas las tarjetas.</summary>
        public void Observe(GameStreamParser.TickView v)
        {
            foreach (var c in v.Colonies)
            {
                if (!_cards.TryGetValue(c.Id, out var card))
                    _cards[c.Id] = card = new Card { ColonyId = c.Id };
                card.Colony = c;
            }

            // El semáforo llega del canal D (calculado por RelayVerdict en el Core).
            foreach (var l in v.Lights)
            {
                if (!_cards.TryGetValue(l.ColonyId, out var card))
                    _cards[l.ColonyId] = card = new Card { ColonyId = l.ColonyId };
                card.Light = l.Light;
            }

            foreach (var m in v.ColonyMetrics)
            {
                if (!_cards.TryGetValue(m.ColonyId, out var card))
                    _cards[m.ColonyId] = card = new Card { ColonyId = m.ColonyId };
                card.Window = m;
            }

            foreach (var ev in v.Events)
            {
                if (!_cards.TryGetValue(ev.ColonyId, out var card)) continue;
                if (ev.Kind == 9) card.EliteEntered++;        // GenomeEnteredElite
                else if (ev.Kind == 10) card.GenomeDiscarded++; // GenomeDiscarded
            }
        }

        /// <summary>Render de texto fijo de la tarjeta (v1: una línea por fila).
        /// null si la colonia aún no tiene snapshot del canal A.</summary>
        public string? Render(int colonyId)
        {
            if (!_cards.TryGetValue(colonyId, out var card) || card.Colony is not GameStreamParser.ColonyView c)
                return null;

            var sb = new StringBuilder();
            sb.Append("colonia ").Append(c.Id).Append(" · ").Append(LightGlyph(card.Light)).AppendLine();

            // Adultas con tope duro del diseño + cría en chips.
            sb.Append("adultas ").Append(c.Adults).Append('/').Append(AdultsHardCap)
              .Append("  · 🥚 ").Append(c.Eggs)
              .Append(" · 🐛 ").Append(c.Larvae)
              .Append(" · 🛑 ").Append(c.Pupae).AppendLine();

            // Reserva: barra de 10 celdas; <20% = rojo (marcador «! RESERVA BAJA»).
            float frac = c.StockMax > 0 ? c.Stock / c.StockMax : 0f;
            int filled = (int)(frac * 10f + 0.5f);
            if (filled < 0) filled = 0; else if (filled > 10) filled = 10;
            sb.Append("reserva [").Append(new string('#', filled))
              .Append(new string('.', 10 - filled)).Append(']')
              .Append(' ').Append(F1(frac * 100f)).Append('%');
            if (frac < 0.20f) sb.Append("  ¡RESERVA BAJA!");
            sb.AppendLine();

            // Flujo de la última ventana de 1 s (canal C por colonia).
            if (card.Window is GameStreamParser.ColonyMetricsView w)
            {
                sb.Append("1s: +").Append(w.Pickups).Append(" rec · +").Append(w.Unloads)
                  .Append(" desc · +").Append(w.Births).Append(" nac · −")
                  .Append(w.Deaths).Append(" mue · +").Append(w.Eggs).Append(" huevos")
                  .Append(" · 🐝").Append(w.Eclosed).AppendLine();
            }
            else
            {
                sb.Append("1s: —").AppendLine();
            }

            // Cerebros: línea permanente (no toast), contadores acumulados por la UI.
            sb.Append("cerebros: ").Append(card.EliteEntered).Append(" élite · ")
              .Append(card.GenomeDiscarded).Append(" descartados").AppendLine();
            return sb.ToString();
        }

        internal static string LightGlyph(byte light) => light switch
        {
            1 => "🟡", // ámbar
            2 => "🟢", // verde
            _ => "🔘", // gris: sin datos de relevo
        };

        private static string F1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
