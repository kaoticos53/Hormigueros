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

        /// <summary>
        /// Línea de estado (F5.1) — el renglón de la barra superior: tick, reloj de
        /// reproducción y un resumen por colonia (semáforo + adultas + reserva).
        /// Vive aquí, y no en la vista, porque es derivable del modelo puro: la UI
        /// pinta y los tests headless comprueban. null mientras no haya canal A.
        /// </summary>
        public string? StatusLine(ulong tick, float speed)
        {
            if (_cards.Count == 0) return null;
            var sb = new StringBuilder();
            sb.Append("tick ").Append(tick)
              .Append("   ·   ").Append(Paused(speed))
              .Append("   ·   ").Append(_cards.Count).Append(_cards.Count == 1 ? " colonia" : " colonias");
            for (int id = 0; id < _cards.Count; id++)
            {
                if (!_cards.TryGetValue(id, out var card)) continue;
                if (card.Colony is not GameStreamParser.ColonyView c) continue;
                float frac = c.StockMax > 0 ? c.Stock / c.StockMax : 0f;
                sb.Append("      colonia ").Append(id).Append(' ').Append(LightGlyph(card.Light))
                  .Append("  ").Append(c.Adults).Append("ad")
                  .Append("  ").Append((frac * 100f).ToString("0", CultureInfo.InvariantCulture)).Append("%");
            }
            return sb.ToString();
        }

        private static string Paused(float speed)
            => speed <= 0f ? "EN PAUSA" : "v×" + speed.ToString("0.##", CultureInfo.InvariantCulture);

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
            // F5.1: el título va en negrita (rich text del HUD) para que la tarjeta
            // tenga jerarquía — antes las cinco líneas pesaban igual y el bloque se
            // leía como un muro de texto.
            sb.Append("<b>colonia ").Append(c.Id).Append("  ").Append(LightGlyph(card.Light))
              .Append("</b>").AppendLine();

            // Adultas con tope duro del diseño + cría en chips.
            // F5.1: sin emoji — la fuente por defecto de uGUI (LegacyRuntime) no
            // tiene glifos de emoji y los pintaba como cajas vacías.
            sb.Append("adultas ").Append(c.Adults).Append('/').Append(AdultsHardCap)
              .Append("   huevos ").Append(c.Eggs)
              .Append("  ·  larvas ").Append(c.Larvae)
              .Append("  ·  pupas ").Append(c.Pupae).AppendLine();

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
                // F5.1: se quitó el prefijo «1s:» y los «+» pegados a cada número
                // (se lee igual y la línea cabe sin partirse en una tarjeta de
                // 440 px; el ancho de la tarjeta sale de ESTA línea).
                sb.Append("1s  ").Append(w.Pickups).Append(" rec  ").Append(w.Unloads)
                  .Append(" desc  ").Append(w.Births).Append(" nac  −")
                  .Append(w.Deaths).Append(" mue  ").Append(w.Eggs).Append(" huevos")
                  .Append("  ").Append(w.Eclosed).Append(" eclos").AppendLine();
            }
            else
            {
                sb.Append("1s  —").AppendLine();
            }

            // Cerebros: línea permanente (no toast), contadores acumulados por la UI.
            sb.Append("cerebros: ").Append(card.EliteEntered).Append(" élite · ")
              .Append(card.GenomeDiscarded).Append(" descartados").AppendLine();
            return sb.ToString();
        }

        /// <summary>Semáforo de relevo como GLIFO de forma (no emoji):
        /// círculo hueco = sin datos · medio = ámbar · lleno = verde. La fuente
        /// por defecto de uGUI dibuja estos; los emoji salían como cajas.</summary>
        internal static string LightGlyph(byte light) => light switch
        {
            1 => "◐", // ámbar
            2 => "●", // verde
            _ => "○", // gris: sin datos de relevo
        };

        private static string F1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
