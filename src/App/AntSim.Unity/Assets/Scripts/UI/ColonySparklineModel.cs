using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1 — gráfica de reserva por colonia (la «gráfica por tarjeta» que el
    /// contrato dejó pendiente). Modelo PURO: guarda una muestra por segundo y
    /// genera la textura RGBA que el HUD pinta.
    ///
    /// POR QUÉ AQUÍ Y NO EN LA VISTA. La regla dura del contrato (§6.4) es que la
    /// UI no deriva: si la gráfica se dibujara con coordenadas de pantalla, el
    /// historial y su forma no tendrían más test que el ojo. Aquí la serie y la
    /// imagen son datos, así que se verifican headless contra streams reales.
    ///
    /// Muestreo: un punto cada `SampleEveryTicks` (1 s de simulación: 30 ticks),
    /// de la RESERVA de cada colonia del canal A. Se retienen `Capacity` puntos:
    /// la ventana es «los últimos 90 s», no «todo desde el inicio» — una gráfica
    /// de 10 minutos en 200 px no dice nada, y el stock de hace 5 minutos no
    /// explica la decisión de ahora.
    /// </summary>
    public sealed class ColonySparklineModel
    {
        /// <summary>Muestras retenidas por colonia (1 Hz ⇒ 90 s de historia).</summary>
        public const int Capacity = 90;

        /// <summary>Un punto por segundo de simulación (30 ticks a 30 Hz), el mismo
        /// ritmo que la ventana de métricas del canal C.</summary>
        public const int SampleEveryTicks = 30;

        /// <summary>«Reserva baja» del contrato: el mismo 20% que la barra.</summary>
        public const float LowThreshold = 0.20f;

        /// <summary>Serie de una colonia: anillo de fracciones de reserva 0..1.</summary>
        public sealed class Series
        {
            private readonly float[] _values = new float[Capacity];
            private int _next;
            private int _count;

            public int ColonyId { get; }
            public int Count => _count;
            public ulong LastTick { get; private set; }

            public Series(int colonyId) { ColonyId = colonyId; }

            /// <summary>Fracción de reserva más reciente (0 si aún no hay muestras).</summary>
            public float Latest => _count == 0 ? 0f : At(_count - 1);

            public bool Low => _count > 0 && Latest < LowThreshold;

            /// <summary>Muestra por antigüedad: 0 = la más vieja retenida.</summary>
            public float At(int index)
            {
                if (index < 0 || index >= _count) return 0f;
                int start = _count < Capacity ? 0 : _next;
                return _values[(start + index) % Capacity];
            }

            /// <summary>Apunta una fracción de reserva (se recorta a 0..1). Público a
            /// propósito: la serie es una estructura de datos —el muestreo 1 Hz lo pone
            /// <see cref="Observe"/>, pero quien tenga otra cadencia (o un test) puede
            /// alimentarla directamente—.</summary>
            public void Push(float value, ulong tick)
            {
                _values[_next] = value < 0f ? 0f : (value > 1f ? 1f : value);
                _next = (_next + 1) % Capacity;
                if (_count < Capacity) _count++;
                LastTick = tick;
            }
        }

        private readonly Dictionary<int, Series> _series = new();

        public IReadOnlyDictionary<int, Series> SeriesByColony => _series;

        public Series For(int colonyId)
        {
            if (!_series.TryGetValue(colonyId, out var s))
                _series[colonyId] = s = new Series(colonyId);
            return s;
        }

        /// <summary>
        /// Consume el tick y, si toca muestrear (1 Hz), apunta la reserva de cada
        /// colonia. Devuelve true solo cuando muestreó: la vista redibuja a 1 Hz,
        /// no en cada tick (el stream va a 30 Hz y la imagen no cambia).
        /// </summary>
        public bool Observe(GameStreamParser.TickView? view, ulong? tick = null)
        {
            if (view == null) return false;
            ulong t = tick ?? view.Tick;
            if (t % (ulong)SampleEveryTicks != 0) return false;

            for (int i = 0; i < view.Colonies.Count; i++)
            {
                var c = view.Colonies[i];
                float frac = c.StockMax > 0f ? c.Stock / c.StockMax : 0f;
                For(c.Id).Push(frac, t);
            }
            return true;
        }

        /// <summary>
        /// Textura RGBA (fila 0 = ABAJO, como espera <c>Texture2D.SetPixels32</c>) de
        /// la serie de una colonia: gráfica de área — columna por muestra, relleno
        /// con alpha 200 y el borde superior a 255, en blanco para que la vista
        /// solo tenga que teñirla.
        ///
        /// Columnas: si hay más muestras que columnas se toman las ÚLTIMAS (la
        /// historia reciente es la que importa); si hay menos, se reparten por TODO
        /// el ancho (muestra más cercana), no un escalón a la izquierda con un
        /// llano al lado — que es lo que se veía con la ventana llena y una barra
        /// ancha como esta.
        /// </summary>
        public byte[] Render(int colonyId, int width, int height)
        {
            if (width < 1 || height < 1) return Array.Empty<byte>();
            var px = new byte[width * height * 4];
            var s = For(colonyId);
            if (s.Count == 0) return px; // sin datos: transparente, no una línea a cero

            int first = s.Count > width ? s.Count - width : 0;
            int last = s.Count - 1;
            bool estirar = s.Count < width && width > 1;
            for (int x = 0; x < width; x++)
            {
                int idx;
                if (estirar)
                    idx = (int)MathF.Round((float)x * last / (width - 1));
                else
                    idx = first + x;
                if (idx > last) idx = last;
                float v = s.At(idx);
                int top = (int)MathF.Round(v * (height - 1));
                if (top < 0) top = 0; else if (top > height - 1) top = height - 1;
                for (int y = 0; y <= top; y++)
                {
                    int p = (y * width + x) * 4;
                    byte a = (byte)(y == top ? 255 : 200);
                    px[p] = 255; px[p + 1] = 255; px[p + 2] = 255; px[p + 3] = a;
                }
            }
            return px;
        }

        /// <summary>Vacía las series (al reiniciar la partida).</summary>
        public void Clear() => _series.Clear();
    }
}
