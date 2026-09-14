using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            // F5.2a.5: cadena de la cortadora ACUMULADA (el canal C trae ventanas
            // de 1 s; la tarjeta las suma — la UI no inventa, solo acumula).
            public long LeafCuts;
            public long FungusFed;
            // F5.2b.5: cadena del saqueo ACUMULADA (el bloque raids trae lo
            // ocurrido DESDE la última emisión — la tarjeta suma de nuevo).
            public long Strikes;
            public long RaidInflows;
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

            // F5.2a.5: cutters (canal C por colonia, ventana de 1 s) → acumulados.
            foreach (var cv in v.Cutters)
            {
                if (!_cards.TryGetValue(cv.ColonyId, out var card))
                    _cards[cv.ColonyId] = card = new Card { ColonyId = cv.ColonyId };
                card.LeafCuts += cv.LeafCuts;
                card.FungusFed += cv.FungusFed;
            }

            // F5.2b.5: raids (canal C por colonia, acumulados desde la emisión
            // previa) → la tarjeta vuelve a sumar. Strikes infligidos por ESTA
            // colonia; el «robado» de la víctima viaja por canal B (StockRobbed).
            foreach (var rv in v.Raids)
            {
                if (!_cards.TryGetValue(rv.ColonyId, out var card))
                    _cards[rv.ColonyId] = card = new Card { ColonyId = rv.ColonyId };
                card.Strikes += rv.Strikes;
                card.RaidInflows += rv.RaidInflows;
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

        /// <summary>F5.1bis — versión COMPACTA para las vistas del multi-visor:
        /// una línea POR COLONIA (no una tarjeta de 5 líneas por colonia) con lo
        /// que se lee a media pantalla: semáforo, adultas, reserva y flujo 1 s.
        /// Misma regla de reserva baja que la tarjeta grande. Sin datos de canal
        /// A aún ⇒ texto de espera (nunca vacío: el jugador ve que la vista
        /// está viva aunque el stream tarde en arrancar).</summary>
        public string RenderCompact()
        {
            if (_cards.Count == 0)
                return "esperando stream…";

            var sb = new StringBuilder();
            foreach (var card in _cards.Values.OrderBy(c => c.ColonyId))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append('<').Append('b')  // rich text: título en negrita
                  .Append(">colonia ").Append(card.ColonyId)
                  .Append(' ').Append(LightGlyph(card.Light)).Append("</b>");
                if (card.Colony is not GameStreamParser.ColonyView c)
                {
                    sb.Append("  —");  // semáforo llegado antes del canal A
                    continue;
                }
                float frac = c.StockMax > 0 ? c.Stock / c.StockMax : 0f;
                sb.Append("  ").Append(c.Adults).Append("h")
                  .Append(' ').Append(F1(frac * 100f)).Append('%');
                if (frac < 0.20f) sb.Append(" ¡BAJA!");
                if (card.Window is GameStreamParser.ColonyMetricsView w && (w.Pickups > 0 || w.Unloads > 0))
                    sb.Append(' ').Append(w.Pickups).Append("rec/")
                      .Append(w.Unloads).Append("desc");
                // F5.2a.5: el hongo en SU línea (colonia con FungusMax>0 = Atta;
                // Lasius/Eciton no ensucian su tarjeta con fungus 0/0).
                if (c.FungusMax > 0f)
                {
                    float ffrac = c.Fungus / c.FungusMax;
                    sb.Append("  hongo [").Append(new string('#', FungusSegments(ffrac)))
                      .Append(new string('.', 10 - FungusSegments(ffrac))).Append(']');
                }
                // Línea de cortadora: solo con actividad acumulada (tolerante:
                // en mundos sin hojas nunca aparece).
                if (card.LeafCuts > 0 || card.FungusFed > 0)
                    sb.Append("\ncortes ").Append(card.LeafCuts)
                      .Append(" · ").Append(card.FungusFed).Append(" al hongo");
                // F5.2b.5: línea del saqueo — golpes infligidos y botín descargado
                // en el nido. Solo con actividad (en mundos pacíficos nunca sale).
                if (card.Strikes > 0 || card.RaidInflows > 0)
                    sb.Append("\nraids ").Append(card.Strikes)
                      .Append(" · ").Append(card.RaidInflows).Append(" al nido");
            }
            return sb.ToString();
        }

        private static string F1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Segmentos llenos de la barra del hongo (misma granularidad
        /// de 10 que la reserva; modelo puro para que la vista solo pinte).</summary>
        internal static int FungusSegments(float fraction)
        {
            if (fraction < 0f) fraction = 0f; else if (fraction > 1f) fraction = 1f;
            int filled = (int)(fraction * 10f + 0.5f);
            return filled;
        }
    }
}
