using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.3ter — panel de APRENDIZAJE del HUD (canal C): curva de fitness por
    /// generación + cobertura del mundo, por colonia.
    ///
    /// POR QUÉ ES UN MODELO Y NO CÓDIGO DE VISTA (regla dura del contrato §6.4): la
    /// forma de la curva y el texto de la tarjeta se verifican headless contra
    /// streams reales del Core; si se dibujaran con coordenadas de pantalla, la
    /// única prueba sería el ojo.
    ///
    /// QUÉ ENSEÑA, Y POR QUÉ ESTAS DOS COSAS:
    ///   · **Curva de fitness por generación** — es el aprendizaje: cada generación
    ///     es una cohorte de nacimientos y su fitness medio (de TODAS sus hormigas,
    ///     no solo las supervivientes) subiendo es la prueba de que el modo
    ///     evolución está funcionando.
    ///   · **Cobertura del mundo** — la traza de huella (CHC, F5.3) dice qué parte
    ///     del mapa ha pisado esa colonia: distingue «está aprendiendo» de «está
    ///     dando vueltas por el mismo pasillo».
    ///
    /// Ejes y escalas: la curva tiene por abscisa la GENERACIÓN (no el tiempo) y la
    /// ordenada se autoescala (el fitness no tiene techo), a diferencia de la
    /// gráfica de reserva — que por eso es otro modelo (0..1 y eje de segundos).
    /// </summary>
    public sealed class LearningPanelModel
    {
        /// <summary>Puntos retenidos por colonia (la cola lejana no explica la decisión de ahora).</summary>
        public const int Capacity = 48;

        /// <summary>Cobertura por debajo de la cual el mundo «aún no se conoce» (aviso ámbar).</summary>
        public const float LowCoverage = 0.05f;
        /// <summary>Cobertura a partir de la cual el sello es verde.</summary>
        public const float GoodCoverage = 0.25f;

        /// <summary>Un punto de la curva (una generación).</summary>
        public readonly struct Point
        {
            public readonly int Generation;
            public readonly int Ants;
            public readonly float Mean;
            public readonly float Best;

            public Point(int generation, int ants, float mean, float best)
            { Generation = generation; Ants = ants; Mean = mean; Best = best; }
        }

        /// <summary>Estado por colonia: la curva y el mundo que conoce.</summary>
        public sealed class ColonyPanel
        {
            private readonly List<Point> _points = new();

            public int ColonyId { get; }
            public int Generation { get; private set; }
            public int Births { get; private set; }
            public int Ants { get; private set; }
            public int Dead { get; private set; }
            public float MeanFitness { get; private set; }
            public float BestFitness { get; private set; }
            public float EliteBest { get; private set; }
            public float EliteAverage { get; private set; }
            public int VisitedCells { get; private set; }
            public int TotalCells { get; private set; }
            public int MaxDistance { get; private set; }
            public ulong LastTick { get; private set; }

            public ColonyPanel(int colonyId) { ColonyId = colonyId; }

            public IReadOnlyList<Point> Points => _points;
            public int PointCount => _points.Count;

            /// <summary>Fracción [0,1] del mundo pisada por esta colonia.</summary>
            public float Coverage => TotalCells > 0 ? (float)VisitedCells / TotalCells : 0f;

            /// <summary>0 = apenas conoce su mundo · 1 = conoce una parte sustancial.</summary>
            public byte CoverageLevel =>
                Coverage < LowCoverage ? (byte)0 : (Coverage < GoodCoverage ? (byte)1 : (byte)2);

            /// <summary>Media de la ÚLTIMA generación de la curva (0 sin datos).</summary>
            public float LatestMean => _points.Count == 0 ? 0f : _points[_points.Count - 1].Mean;

            /// <summary>Mejor media vista en la curva: la referencia de escala del dibujo.</summary>
            public float PeakMean
            {
                get
                {
                    float m = 0f;
                    for (int i = 0; i < _points.Count; i++)
                        if (_points[i].Mean > m) m = _points[i].Mean;
                    return m;
                }
            }

            /// <summary>¿La curva está subiendo? Compara la última generación con la primera retenida.</summary>
            public bool Improving => _points.Count >= 2 && _points[_points.Count - 1].Mean > _points[0].Mean;

            public void Apply(GameStreamParser.LearningView v, ulong tick)
            {
                Generation = v.Generation;
                Births = v.Births;
                Ants = v.Ants;
                Dead = v.Dead;
                MeanFitness = v.MeanFitness;
                BestFitness = v.BestFitness;
                EliteBest = v.EliteBest;
                EliteAverage = v.EliteAverage;
                VisitedCells = v.VisitedCells;
                TotalCells = v.TotalCells;
                MaxDistance = v.MaxDistance;
                LastTick = tick;
            }

            /// <summary>
            /// Reemplaza la curva por la que trae el stream (es una serie completa,
            /// no deltas: el emisor ya la retiene) y la recorta a la ventana.
            /// </summary>
            public void SetCurve(List<Point> points)
            {
                _points.Clear();
                int first = points.Count > Capacity ? points.Count - Capacity : 0;
                for (int i = first; i < points.Count; i++) _points.Add(points[i]);
            }

            /// <summary>Línea del panel: generación, cohorte, fitness y élite.</summary>
            public string FitnessLine()
            {
                string elite = EliteBest > 0f
                    ? $"élite {Num(EliteBest)} (medio {Num(EliteAverage)})"
                    : "élite —";
                return $"gen {Generation} · {Births} nac. · cohorte {Ants} ({Dead} bajas) · " +
                       $"fitness {Num(MeanFitness)} (máx {Num(BestFitness)}) · {elite}";
            }

            /// <summary>Línea del mundo: cobertura, celdas y radio máximo.</summary>
            public string WorldLine()
                => $"cobertura {Coverage * 100f:0.0} % · {VisitedCells}/{TotalCells} celdas · radio {MaxDistance} u";

            private static string Num(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private readonly Dictionary<int, ColonyPanel> _panels = new();

        public IReadOnlyDictionary<int, ColonyPanel> Panels => _panels;

        public ColonyPanel For(int colonyId)
        {
            if (!_panels.TryGetValue(colonyId, out var p))
                _panels[colonyId] = p = new ColonyPanel(colonyId);
            return p;
        }

        /// <summary>
        /// Consume el tick. Devuelve true solo cuando llegó un bloque de aprendizaje
        /// (canal C, 1 Hz): la vista redibuja entonces, no en cada tick.
        /// </summary>
        public bool Observe(GameStreamParser.TickView? view)
        {
            if (view == null || view.Learning.Count == 0) return false;

            for (int i = 0; i < view.Learning.Count; i++)
            {
                var v = view.Learning[i];
                For(v.ColonyId).Apply(v, view.Tick);
            }

            // La curva llega como serie completa: se agrupa por colonia y se aplica.
            if (view.FitnessCurve.Count > 0)
            {
                var byColony = new Dictionary<int, List<Point>>();
                for (int i = 0; i < view.FitnessCurve.Count; i++)
                {
                    var p = view.FitnessCurve[i];
                    if (!byColony.TryGetValue(p.ColonyId, out var list))
                        byColony[p.ColonyId] = list = new List<Point>();
                    list.Add(new Point(p.Generation, p.Ants, p.Mean, p.Best));
                }
                foreach (var kv in byColony) For(kv.Key).SetCurve(kv.Value);
            }
            return true;
        }

        /// <summary>
        /// Textura RGBA (fila 0 = ABAJO, como espera <c>Texture2D.SetPixels32</c>) de
        /// la curva de fitness de una colonia: área blanca por generación, autoescalada
        /// entre 0 y la media máxima de la ventana (el fitness no tiene techo).
        /// Sin datos o sin techo ⇒ transparente (no una línea a cero, que mentiría).
        /// </summary>
        public byte[] Render(int colonyId, int width, int height)
        {
            if (width < 1 || height < 1) return Array.Empty<byte>();
            var px = new byte[width * height * 4];
            var panel = For(colonyId);
            int n = panel.PointCount;
            if (n == 0) return px;
            float peak = panel.PeakMean;
            if (peak <= 0f) return px;

            for (int x = 0; x < width; x++)
            {
                // Con más puntos que columnas se toman los ÚLTIMOS (la generación
                // reciente es la que importa); con menos, se reparten por el ancho.
                int idx = n > width
                    ? n - width + x
                    : (width == 1 ? n - 1 : (int)MathF.Round((float)x * (n - 1) / (width - 1)));
                if (idx < 0) idx = 0; else if (idx >= n) idx = n - 1;
                float v = panel.Points[idx].Mean / peak;
                if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
                int top = (int)MathF.Round(v * (height - 1));
                if (top < 0) top = 0; else if (top > height - 1) top = height - 1;
                for (int y = 0; y <= top; y++)
                {
                    int p = (y * width + x) * 4;
                    byte a = (byte)(y == top ? 255 : 190);
                    px[p] = 255; px[p + 1] = 255; px[p + 2] = 255; px[p + 3] = a;
                }
            }
            return px;
        }

        public void Clear() => _panels.Clear();
    }
}
