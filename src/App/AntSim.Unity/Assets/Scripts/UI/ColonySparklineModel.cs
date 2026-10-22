using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1 — gráfica por colonia: la RESERVA (área) y las DESCARGAS por ventana
    /// (barras) en la misma imagen. Modelo PURO: guarda una muestra por segundo y
    /// genera las texturas RGBA que el HUD pinta.
    ///
    /// POR QUÉ AQUÍ Y NO EN LA VISTA. La regla dura del contrato (§6.4) es que la
    /// UI no deriva: si la gráfica se dibujara con coordenadas de pantalla, el
    /// historial y su forma no tendrían más test que el ojo. Aquí las series y la
    /// imagen son datos, así que se verifican headless contra streams reales.
    ///
    /// Muestreo: un punto cada `SampleEveryTicks` (1 s de simulación: 30 ticks),
    /// de la RESERVA de cada colonia del canal A. Se retienen `Capacity` puntos:
    /// la ventana es «los últimos 90 s», no «todo desde el inicio» — una gráfica
    /// de 10 minutos en 200 px no dice nada, y el stock de hace 5 minutos no
    /// explica la decisión de ahora.
    ///
    /// LAS DESCARGAS, y por qué salen de los EVENTOS. La reserva sola dice QUE se
    /// cayó; las descargas dicen POR QUÉ se estaba llenando. Viajan por el canal B
    /// (un `Unload` por descarga, con su colonia) en CADA tick, mientras que
    /// `colmetrics` es la ventana ABIERTA que el Core publica cada 120 ticks: no
    /// sirve para una serie de 1 Hz. El modelo cuenta los `Unload` que ve entre
    /// dos muestras y los apunta con la muestra, así que la ventana de la barra
    /// es la misma ventana que la muestra del área.
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

        /// <summary>
        /// Canal B: el kind del evento de descarga (`SimEventKind.Unload = 5`). El
        /// proyecto de Unity NO referencia el Core, y esta frontera es la que
        /// mantiene el esqueleto compilable headless: el ordinal viaja como
        /// constante, con el mismo trato que los `9`/`10` de la tarjeta.
        /// </summary>
        private const int KindUnload = 5;

        /// <summary>Serie de una colonia: anillo de fracciones de reserva 0..1 y, en
        /// paralelo, las descargas contadas en esa misma ventana de 1 s.</summary>
        public sealed class Series
        {
            private readonly float[] _values = new float[Capacity];
            private readonly int[] _drops = new int[Capacity];
            private int _next;
            private int _count;

            public int ColonyId { get; }
            public int Count => _count;
            public ulong LastTick { get; private set; }

            public Series(int colonyId) { ColonyId = colonyId; }

            /// <summary>Fracción de reserva más reciente (0 si aún no hay muestras).</summary>
            public float Latest => _count == 0 ? 0f : At(_count - 1);

            public bool Low => _count > 0 && Latest < LowThreshold;

            /// <summary>Descargas de la ventana más reciente (0 si aún no hay muestras).</summary>
            public int DropsLatest => _count == 0 ? 0 : DropsAt(_count - 1);

            /// <summary>Mayor cuenta de descargas retenida: la escala de las barras.
            /// Se mide sobre la MISMA ventana que se dibuja (90 s) para que dos
            /// pasadas del mismo mundo den la misma imagen, y para que una partida
            /// tranquila no salga con barras de un píxel por un pico de hace dos
            /// minutos que ya no está en pantalla.</summary>
            public int DropsMax
            {
                get
                {
                    int max = 0;
                    for (int i = 0; i < _count; i++)
                    {
                        int v = DropsAt(i);
                        if (v > max) max = v;
                    }
                    return max;
                }
            }

            /// <summary>Muestra por antigüedad: 0 = la más vieja retenida.</summary>
            public float At(int index)
            {
                if (index < 0 || index >= _count) return 0f;
                int start = _count < Capacity ? 0 : _next;
                return _values[(start + index) % Capacity];
            }

            /// <summary>Descargas de la muestra `index`, por antigüedad (0 = la más
            /// vieja retenida).</summary>
            public int DropsAt(int index)
            {
                if (index < 0 || index >= _count) return 0;
                int start = _count < Capacity ? 0 : _next;
                return _drops[(start + index) % Capacity];
            }

            /// <summary>Apunta una fracción de reserva (se recorta a 0..1) sin
            /// descargas. Público a propósito: la serie es una estructura de datos
            /// —el muestreo 1 Hz lo pone <see cref="Observe"/>, pero quien tenga
            /// otra cadencia (o un test) puede alimentarla directamente—.</summary>
            public void Push(float value, ulong tick) => Push(value, 0, tick);

            /// <summary>Apunta reserva y descargas de la MISMA ventana.</summary>
            public void Push(float value, int drops, ulong tick)
            {
                _values[_next] = value < 0f ? 0f : (value > 1f ? 1f : value);
                _drops[_next] = drops < 0 ? 0 : drops;
                _next = (_next + 1) % Capacity;
                if (_count < Capacity) _count++;
                LastTick = tick;
            }
        }

        private readonly Dictionary<int, Series> _series = new();
        private readonly Dictionary<int, int> _pendingDrops = new();
        private ulong _lastSeenTick = ulong.MaxValue;

        public IReadOnlyDictionary<int, Series> SeriesByColony => _series;

        public Series For(int colonyId)
        {
            if (!_series.TryGetValue(colonyId, out var s))
                _series[colonyId] = s = new Series(colonyId);
            return s;
        }

        /// <summary>
        /// Consume el tick: cuenta las descargas del canal B y, si toca muestrear
        /// (1 Hz), apunta reserva y descargas de cada colonia. Devuelve true solo
        /// cuando muestreó: la vista redibuja a 1 Hz, no en cada tick (el stream va
        /// a 30 Hz y la imagen no cambia).
        ///
        /// El MISMO tick visto dos veces no cuenta doble: la vista lee
        /// `CurrentTick` varias veces por tick de simulación (60 fps o más) y sin
        /// esta guarda cada descarga se apuntaría en dos muestras.
        /// </summary>
        public bool Observe(GameStreamParser.TickView? view, ulong? tick = null)
        {
            if (view == null) return false;
            ulong t = tick ?? view.Tick;
            if (t == _lastSeenTick) return false;
            _lastSeenTick = t;

            // Las descargas del tick se acumulan SIEMPRE (también en los ticks que
            // no muestrean): la barra de la muestra debe contar la ventana entera.
            for (int i = 0; i < view.Events.Count; i++)
            {
                var ev = view.Events[i];
                if (ev.Kind != KindUnload) continue;
                _pendingDrops.TryGetValue(ev.ColonyId, out int n);
                _pendingDrops[ev.ColonyId] = n + 1;
            }

            if (t % (ulong)SampleEveryTicks != 0) return false;

            for (int i = 0; i < view.Colonies.Count; i++)
            {
                var c = view.Colonies[i];
                float frac = c.StockMax > 0f ? c.Stock / c.StockMax : 0f;
                _pendingDrops.TryGetValue(c.Id, out int drops);
                For(c.Id).Push(frac, drops, t);
            }
            _pendingDrops.Clear();
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

            for (int x = 0; x < width; x++)
            {
                float v = s.At(ColumnIndex(s, x, width));
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

        /// <summary>
        /// Textura RGBA de las DESCARGAS de una colonia, como máscara aparte (blanco
        /// opaco donde hay barra). Va separada de <see cref="Render"/> porque son dos
        /// series con dos colores: la vista tiñe la reserva con su color de estado y
        /// las barras con el suyo, sin que este modelo tenga que saber de colores.
        ///
        /// La barra de una muestra sube desde la base hasta la fracción de la MAYOR
        /// cuenta retenida (<see cref="Series.DropsMax"/>), así que la altura es
        /// relativa a la ventana que se está viendo y no a una escala inventada.
        /// Cada columna usa el MISMO índice que el área (`ColumnIndex`): las barras
        /// caen sobre la muestra de reserva a la que corresponden.
        /// </summary>
        public byte[] RenderDrops(int colonyId, int width, int height)
        {
            if (width < 1 || height < 1) return Array.Empty<byte>();
            var px = new byte[width * height * 4];
            var s = For(colonyId);
            int max = s.DropsMax;
            if (s.Count == 0 || max <= 0) return px; // sin descargas: nada que dibujar

            for (int x = 0; x < width; x++)
            {
                int v = s.DropsAt(ColumnIndex(s, x, width));
                if (v <= 0) continue;
                // Una cuenta pequeña frente al máximo de la ventana redondea a 0
                // filas; el bucle pinta igualmente la fila de la BASE (y=0), así
                // que «una descarga» se ve como un píxel y no como «ninguna».
                int top = (int)MathF.Round((float)v / max * (height - 1));
                if (top < 0) top = 0;
                if (top > height - 1) top = height - 1;
                for (int y = 0; y <= top; y++)
                {
                    int p = (y * width + x) * 4;
                    px[p] = 255; px[p + 1] = 255; px[p + 2] = 255; px[p + 3] = 255;
                }
            }
            return px;
        }

        /// <summary>
        /// Qué muestra cae en la columna `x`. Lo comparten las dos series: si cada
        /// una mapeara por su cuenta, una barra podría caer entre dos muestras del
        /// área y la gráfica mentiría sobre qué ventana explica qué reserva.
        /// </summary>
        private static int ColumnIndex(Series s, int x, int width)
        {
            int last = s.Count - 1;
            if (s.Count > width) return Math.Min(last, s.Count - width + x);
            if (width <= 1) return last;
            return (int)MathF.Round((float)x * last / (width - 1));
        }

        /// <summary>Vacía las series y lo pendiente de contar (al reiniciar la partida).</summary>
        public void Clear()
        {
            _series.Clear();
            _pendingDrops.Clear();
            _lastSeenTick = ulong.MaxValue;
        }
    }
}
