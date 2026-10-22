using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1 — Modelo PURO del layout per-elemento del HUD (contrato §1/§4/§2).
    /// Convierte el estado puro (pila de toasts, tarjetas de colonia) en specs
    /// de elementos uGUI: un rect y texto POR toast (click exacto — se acabó el
    /// Alt+click por índice), una barra de stock con fracción/color por colonia
    /// y specs de botones nativos con sus acciones. Sin UnityEngine: compilado
    /// en la suite headless como el resto de modelos puros. Las coordenadas son
    /// anchors/píxeles del contrato HUD (los mismos que usa el bootstrapper);
    /// el presentador uGUI solo traduce spec → RectTransform/Image/Button.
    /// </summary>
    public static class HudElementLayoutModel
    {
        public const float ToastHeight = 26f;   // px por toast (antes 22 aprox.)
        public const float ToastGap = 4f;       // separación entre rects
        public const float ToastWidth = 560f;   // ancho del rect clicable
        public const int StockBarSegments = 10; // granularidad visual de la barra
        public const float LowStockThreshold = 0.20f; // «¡RESERVA BAJA!» del contrato

        // ── Cajas de TEXTO FIJO del HUD: su ancho sale de la FUENTE, no a ojo ──
        // A fs 14 la fuente del juego (GameFontModel: em 1000, avance 500) reserva
        // 7 px por carácter —redondeo del hinting incluido: lo ancla
        // `HudLayoutTests.AvanceReservado_EsElDeLaFuenteConElRedondeoDelHinting`—,
        // así que una caja de W px admite W/7 caracteres y el desborde de un texto
        // se puede CALCULAR y probar sin editor. Las pistas y las dos líneas de la
        // banda de aprendizaje viven aquí por eso: el Play pass las midió
        // desbordando su caja (pistas 1325 > 1100; líneas 648 y 513 > 406) y solo
        // lo veía una captura. Con estas constantes el ajuste es un test headless.

        /// <summary>Ancho de la barra de pistas: 194 caracteres a 7 px. Las pistas
        /// actuales miden 1302 px, así que quedan 58 px de aire — y el texto de la
        /// escena se mide, no se supone (antes la caja era de 1100: 157 chars).</summary>
        public const float HintsWidth = 1360f;
        public const float HintsHeight = 26f;

        /// <summary>Pistas de teclado (el único texto FIJO del HUD). Texto y caja
        /// juntos: así el ajuste se comprueba contra la misma constante que la
        /// escena usa, y no contra una copia que puede derivar.</summary>
        public const string HintsText =
            "click: inspeccionar   ·   + / -: velocidad (1×–100×)   ·   Espacio: pausa   ·   " +
            "D: drops (Z deshace)   ·   F: feromonas   ·   G: colonia   ·   I: importar pool   ·   " +
            "\u2b07: soltar .antgenome";

        /// <summary>Caja de las DOS líneas del panel de aprendizaje: la banda que la
        /// tarjeta reserva entre la curva de fitness (62–88) y la base. Mismo ancho
        /// que el cuerpo de la tarjeta, y el modelo tuvo que recortar su texto para
        /// caber (medido: 648 px de una línea de 89 caracteres).</summary>
        public const int LearningInfoWidth = ColonyCardModel.CardBodyWidth;   // 406 px
        public const int LearningInfoHeight = 34;                             // 2 líneas

        /// <summary>Spec de UN toast como elemento: rect clicable exacto.</summary>
        public sealed class ToastElement
        {
            public readonly string Key;       // dedupe/identidad (contrato §1)
            public readonly int Index;        // posición en la pila (0 = arriba)
            public readonly float Y;          // px desde el borde superior del ancla
            public readonly float Height;     // ToastHeight
            public readonly byte Level;       // 0 info · 1 ámbar · 2 verde · 3 rojo
            public readonly bool HasAnchor;   // ancla de cámara válida (X,Y mundo)
            public readonly float AnchorX;
            public readonly float AnchorY;
            public readonly string Text;

            public ToastElement(string key, int index, float y, byte level,
                bool hasAnchor, float anchorX, float anchorY, string text)
            {
                Key = key; Index = index; Y = y; Level = level;
                HasAnchor = hasAnchor; AnchorX = anchorX; AnchorY = anchorY; Text = text;
            }
        }

        /// <summary>
        /// Rects por toast: el i-ésimo toast activo ocupa la franja vertical
        /// [i·(H+gap), i·(H+gap)+H] desde el borde superior. Click exacto:
        /// la vista pregunta por (px, py) y el modelo responde con el toast.
        /// </summary>
        public static List<ToastElement> ToastElements(IReadOnlyList<HudToastsModel.Toast> active)
        {
            var list = new List<ToastElement>(active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                var t = active[i];
                list.Add(new ToastElement(
                    t.Key, i, i * (ToastHeight + ToastGap), t.Level,
                    t.X >= 0f && t.Y >= 0f, t.X, t.Y, t.Text));
            }
            return list;
        }

        /// <summary>
        /// Hit-test exacto de un click de pantalla (px desde arriba) sobre la
        /// pila: devuelve el toast de la franja o null si el click cae fuera.
        /// Es el reemplazo directo del «índice = mouse.y / 22» de F4.2.
        /// </summary>
        public static ToastElement? ToastAt(IReadOnlyList<ToastElement> elements, float px)
        {
            foreach (var e in elements)
            {
                if (px >= e.Y && px < e.Y + ToastHeight) return e;
            }
            return null;
        }

        /// <summary>Spec de UNA barra de stock (contrato §2: reserva S/Smax).</summary>
        public sealed class StockBar
        {
            public readonly int ColonyId;
            public readonly float Fraction;     // [0,1] S/Smax
            public readonly int Filled;         // segmentos llenos (0..StockBarSegments)
            public readonly bool Low;           // frac < 0.20 → rojo

            public StockBar(int colonyId, float fraction)
            {
                ColonyId = colonyId;
                Fraction = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
                Filled = (int)(Fraction * StockBarSegments + 0.5f);
                if (Filled < 0) Filled = 0; else if (Filled > StockBarSegments) Filled = StockBarSegments;
                Low = Fraction < LowStockThreshold;
            }
        }

        /// <summary>Barra por colonia a partir de las tarjetas ya observadas.</summary>
        public static List<StockBar> StockBars(IReadOnlyList<(int ColonyId, float Stock, float StockMax)> colonies)
        {
            var list = new List<StockBar>(colonies.Count);
            foreach (var c in colonies)
                list.Add(new StockBar(c.ColonyId, c.StockMax > 0f ? c.Stock / c.StockMax : 0f));
            return list;
        }

        public enum ButtonAction
        {
            ImportInspect,    // ImportDialogBehaviour.Inspect (F5.1: abre la cuarentena)
            ImportConfirm,    // ImportDialogBehaviour.Confirm
            ImportCancel,     // ImportDialogBehaviour.Cancel
            RestartWithPlan,  // DropFoodClickHandler.RestartWithPlan
        }

        /// <summary>Spec de un botón nativo uGUI (texto + acción a invocar).</summary>
        public sealed class ButtonSpec
        {
            public readonly string Name;
            public readonly string Label;
            public readonly ButtonAction Action;
            public readonly bool Enabled;

            public ButtonSpec(string name, string label, ButtonAction action, bool enabled)
            {
                Name = name; Label = label; Action = action; Enabled = enabled;
            }
        }

        /// <summary>
        /// Botones del diálogo de importación (contrato §2.1): Inspeccionar abre
        /// la cuarentena del .antgenome tecleado; Confirmar solo si hay tarjeta OK
        /// revisada; Cancelar siempre disponible. La UI nunca decide la semántica
        /// — las acciones son los métodos públicos que ya existían y que el flujo
        /// headless verificado usa.
        /// </summary>
        public static List<ButtonSpec> ImportButtons(bool confirmEnabled) => new()
        {
            new ButtonSpec("ImportInspectBtn", "Inspeccionar", ButtonAction.ImportInspect, true),
            new ButtonSpec("ImportConfirmBtn", "Confirmar", ButtonAction.ImportConfirm, confirmEnabled),
            new ButtonSpec("ImportCancelBtn", "Cancelar", ButtonAction.ImportCancel, true),
        };

        /// <summary>Botón «Reiniciar con plan» (contrato §4): activo solo con drops.</summary>
        public static List<ButtonSpec> DropPlanButtons(int dropCount) => new()
        {
            new ButtonSpec("RestartWithPlanBtn", "Reiniciar con plan",
                ButtonAction.RestartWithPlan, dropCount > 0),
        };
    }
}
