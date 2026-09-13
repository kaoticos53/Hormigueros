using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// F5.1bis — tarjeta compacta de UNA vista del multi-visor: semáforo de
    /// relevo, reserva y cría de las colonias de ESA vista. Wire-up FINO sobre
    /// los modelos puros (<see cref="Streaming.ColonyCardModel"/>): el
    /// bootstrapper crea un panel POR vista dentro de SU rect del canvas, lo
    /// alimenta con SU presenter y este componente solo pinta lo que los
    /// modelos dictan. Sin lógica propia que pueda discrepar del contrato.
    /// </summary>
    public sealed class ViewCardBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter de ESTA vista (su stream alimenta la tarjeta).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Texto del cuerpo de la tarjeta (una línea por colonia).")]
        public Text? Body;

        [Tooltip("Barras de reserva por colonia (Image fill; orden = id).")]
        public Image?[]? StockBars;

        private readonly Streaming.ColonyCardModel _cards = new();

        /// <summary>Modelo puro de tarjetas (tests y diagnósticos).</summary>
        public Streaming.ColonyCardModel Cards => _cards;

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null || Body == null) return;
            var p = presenter.Presenter;
            if (p == null) return;

            // Drena TODOS los ticks presentados (a velocidad alta un frame
            // presenta varios: drenarlos todos es lo que no pierde eventos).
            while (p.TryDequeuePresented(out var view) && view != null)
                _cards.Observe(view);

            // Compacto: una línea por colonia con semáforo, adultas y reserva.
            // Sale del MISMO modelo que la tarjeta grande, en versión de una
            // línea por colonia — el viewport es la mitad o menos del ancho, y
            // la tarjeta de 440 px no cabe.
            string text = _cards.RenderCompact();
            if (Body.text != text) Body.text = text;

            // Barras de reserva: la MISMA regla del HUD grande (spec del modelo
            // puro, indexado por ColonyId, color rojo bajo el umbral del
            // contrato). Solo cambia el color de verde: sobre un tablero de
            // tierra pequeño, el verde apagado del HUD grande se pierde.
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
                img.color = b.Low ? new Color(0.9f, 0.2f, 0.15f) : new Color(0.45f, 0.8f, 0.4f);
            }
        }
    }
}
