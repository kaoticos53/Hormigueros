using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>Color RGB 0-255 sin dependencias del motor: la puerta de píxeles
    /// se verifica headless, así que no puede hablar en Color32.</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }

        public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object? obj) => obj is Rgb o && Equals(o);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
        public override string ToString() => R + "," + G + "," + B;

        public float Rf => R / 255f;
        public float Gf => G / 255f;
        public float Bf => B / 255f;
    }

    /// <summary>
    /// Ventana NORMALIZADA de muestreo del terreno (0..1 del frame). El valor por
    /// defecto es el del Play pass del editor (0.15–0.85, dentro del mundo porque
    /// el orthoSize lleva un 4 % de margen). Una VISTA del multi-visor puede ser
    /// mucho más ancha que el tablero (una cámara que encuadra el mundo a lo alto
    /// en un rect 16:9 deja el tablero en su 54 % central), así que la sonda del
    /// player CALCULA la ventana del encuadre real en vez de suponerla: con la
    /// ventana por defecto, la mitad de las muestras caerían fuera del tablero y
    /// la «mediana» sería el fondo — un falso suspenso.
    /// </summary>
    public readonly struct FrameWindow
    {
        public readonly float X0;
        public readonly float Y0;
        public readonly float X1;
        public readonly float Y1;

        public FrameWindow(float x0, float y0, float x1, float y1)
        { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }

        /// <summary>La del Play pass del editor (F5.1): 0.15–0.85, las dos
        /// direcciones.</summary>
        public static readonly FrameWindow Default = new(
            FrameGate.SampleInset, FrameGate.SampleInset,
            FrameGate.SampleInset + FrameGate.SampleSpan,
            FrameGate.SampleInset + FrameGate.SampleSpan);

        /// <summary>Ventana centrada, por semiancho/semialto normalizados.</summary>
        public static FrameWindow Centered(float halfX, float halfY)
            => new(0.5f - halfX, 0.5f - halfY, 0.5f + halfX, 0.5f + halfY);
    }

    /// <summary>Resultado de la puerta de píxeles sobre UN frame.</summary>
    public readonly struct FrameGateResult
    {
        /// <summary>Color MEDIANO del terreno muestreado dentro del mundo.</summary>
        public readonly Rgb Floor;
        /// <summary>Muestra MÁS OSCURA del terreno: el surco de la rejilla.</summary>
        public readonly Rgb Grid;
        /// <summary>Color de la esquina del frame (el fondo, no el tablero).</summary>
        public readonly Rgb Background;
        /// <summary>¿El terreno se lee como TIERRA (rojo &gt; verde &gt; azul)?</summary>
        public readonly bool Brown;
        public readonly int AntPx;
        public readonly int CarrierPx;
        public readonly int ItemPx;
        public readonly int TotalPx;
        /// <summary>Fracción de píxeles casi negros (paneles/huecos oscuros).</summary>
        public readonly float DarkFraction;

        public FrameGateResult(Rgb floor, Rgb grid, Rgb background, bool brown,
            int antPx, int carrierPx, int itemPx, int totalPx, float darkFraction)
        {
            Floor = floor; Grid = grid; Background = background; Brown = brown;
            AntPx = antPx; CarrierPx = carrierPx; ItemPx = itemPx;
            TotalPx = totalPx; DarkFraction = darkFraction;
        }

        /// <summary>¿Pasa la puerta? El tablero se ve tierra y HAY hormigas
        /// dibujadas: son las dos cosas que el defecto real del F5.1 rompió
        /// (quad de feromonas opaco tapando suelo y hormigas) y las dos que la
        /// suite de ESTADO no veía.</summary>
        public bool Ok => Brown && AntPx > 0;

        /// <summary>Motivo del suspenso, o "ok".</summary>
        public string Verdict =>
            !Brown ? (AntPx > 0 ? "terreno no-tierra" : "terreno no-tierra y sin hormigas")
                   : (AntPx > 0 ? "ok" : "sin hormigas en pantalla");

        public string Describe() =>
            $"floor={Floor.Rf:0.###}:{Floor.Gf:0.###}:{Floor.Bf:0.###} brown={(Brown ? 1 : 0)} " +
            $"antPx={AntPx} carrierPx={CarrierPx} itemPx={ItemPx} darkFrac={DarkFraction:0.###}";
    }

    /// <summary>
    /// F5.3 — la PUERTA DE PÍXELES, en una sola implementación (pura, sin
    /// UnityEngine, verificada en la suite headless). Mide lo que el jugador VE,
    /// no lo que el estado dice: el defecto real del F5.1 fue un frame blanco sin
    /// una sola hormiga con todos los invariantes de estado en verde.
    ///
    /// La consumen las dos sondas que miran píxeles:
    ///   · <c>ProbeVisualSample</c> (Play pass en el editor, F5.1);
    ///   · la sonda del PLAYER empaquetado (F5.3), que mide el build real.
    /// Tenerla dos veces fue lo que hizo posible que las dos dieran números
    /// distintos sin que nadie lo notara.
    ///
    /// Entrada: buffer RGB (3 bytes por píxel, fila 0 ABAJO, el mismo orden que
    /// <c>Texture2D.ReadPixels</c>) y los colores con los que el presenter pinta
    /// hormiga, portadora e ítem. El suelo, el surco de la rejilla y el fondo NO
    /// se suponen: se MIDEN del propio frame.
    ///
    /// La clasificación es por COLOR MÁS CERCANO con umbral estrecho, no por
    /// tolerancia suelta: con el suelo tierra (107,84,59) y la hormiga (76,46,26)
    /// distan ~40, y un umbral ancho contaba el tablero ENTERO como hormigas
    /// (antPx=47 265 de 76 800 en la primera medida del F5.1).
    /// </summary>
    public static class FrameGate
    {
        /// <summary>Distancia máxima (por canal, euclídea/√3) para aceptar que un
        /// píxel es del color de referencia. Estrecho a propósito: el suelo y el
        /// surco están a 20-35 del tono de hormiga.</summary>
        public const int NearestThreshold = 12;

        /// <summary>Lado de la retícula de muestreo del terreno (7×7 = 49 puntos).</summary>
        public const int FloorSampleGrid = 7;

        /// <summary>Ventana de muestreo dentro del frame: 0.15 + 0.7·g/8 para
        /// g = 1..7. Es DENTRO del mundo (el orthoSize lleva margen): muestrear la
        /// esquina mide el fondo, y la primera versión de la sonda daba brown=0
        /// por eso, no porque el suelo estuviera mal.</summary>
        public const float SampleInset = 0.15f;
        public const float SampleSpan = 0.7f;
        public const float SampleDivisor = 8f;

        /// <summary>Umbral de "casi negro" (paneles, huecos) por canal.</summary>
        public const byte DarkCut = 40;

        /// <summary>Analiza un frame con UN color por familia (el caso del Play
        /// pass de una vista única).</summary>
        public static FrameGateResult Analyze(byte[] rgb, int width, int height,
            Rgb ant, Rgb carrier, Rgb item, FrameWindow? window = null,
            Rgb? backgroundOverride = null)
            => Analyze(rgb, width, height, new[] { ant }, new[] { carrier }, new[] { item },
                window, backgroundOverride);

        /// <summary>Analiza un frame completo. <paramref name="rgb"/> debe tener
        /// al menos <c>width·height·3</c> bytes. <paramref name="window"/> acota
        /// dónde se muestrea el TERRENO (null = la del Play pass del editor).
        ///
        /// Las familias son LISTAS porque el multi-visor pinta las hormigas con
        /// `ColonyAntMaterials` (una por colonia) y no con `AntMaterial`: pasarle
        /// un solo color dejaba la familia de hormigas sin representar y la puerta
        /// suspendía con `antPx=0` sobre un tablero con 292 hormigas dibujadas —
        /// medido el 04/09 en el primer build de jugador.
        /// <paramref name="backgroundOverride"/> fija el color del FONDO de la
        /// escena cuando se conoce (la cámara lo declara): con la esquina a secas,
        /// un tablero que cubre la esquina haría que el fondo de verdad no
        /// estuviera en la paleta y sus píxeles cayeran en la familia más cercana
        /// — el fondo del multi-visor (56,45,33) dista 12 del tono de hormiga, así
        /// que se contarían como HORMIGAS.</summary>
        public static FrameGateResult Analyze(byte[] rgb, int width, int height,
            IReadOnlyList<Rgb> ant, IReadOnlyList<Rgb> carrier, IReadOnlyList<Rgb> item,
            FrameWindow? window = null, Rgb? backgroundOverride = null)
        {
            if (rgb == null) throw new ArgumentNullException(nameof(rgb));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgb.Length < width * height * 3)
                throw new ArgumentException($"buffer corto: {rgb.Length} < {width * height * 3}", nameof(rgb));

            var win = window ?? FrameWindow.Default;

            // Fondo: la esquina del frame. La cámara cubre más que el tablero, así
            // que lo de fuera del mundo es el fondo de la escena.
            int bgIndex = 2 * width + 2;
            var background = backgroundOverride ?? Pixel(rgb, bgIndex);

            // Suelo: MEDIANA de 49 muestras dentro del mundo. La mediana aguanta
            // que una muestra caiga sobre una hormiga, un ítem o un nido.
            var samples = new List<Rgb>(FloorSampleGrid * FloorSampleGrid);
            for (int gy = 1; gy <= FloorSampleGrid; gy++)
                for (int gx = 1; gx <= FloorSampleGrid; gx++)
                {
                    int sx = (int)(width * (win.X0 + (win.X1 - win.X0) * gx / SampleDivisor));
                    int sy = (int)(height * (win.Y0 + (win.Y1 - win.Y0) * gy / SampleDivisor));
                    samples.Add(Pixel(rgb, sy * width + sx));
                }
            // Orden TOTAL (suma y luego canales): List.Sort no es estable y con
            // empates de suma el "mediano" podría cambiar entre corridas.
            samples.Sort(static (a, b) =>
            {
                int c = (a.R + a.G + a.B).CompareTo(b.R + b.G + b.B);
                if (c != 0) return c;
                c = a.R.CompareTo(b.R);
                if (c != 0) return c;
                c = a.G.CompareTo(b.G);
                return c != 0 ? c : a.B.CompareTo(b.B);
            });
            var floor = samples[samples.Count / 2];
            // El surco de la rejilla es la muestra más oscura del terreno, sin
            // salir del 10 % inferior (que ya podría ser una hormiga o el fondo).
            var grid = samples[samples.Count / 10];

            // Terreno «tierra»: el rojo manda y el azul es bajo. Si el quad de
            // feromonas vuelve a tapar el suelo, esto se pone gris/blanco.
            bool brown = floor.Rf > floor.Gf + 0.02f && floor.Gf > floor.Bf + 0.02f
                         && floor.Bf < 0.45f;

            // Paleta = todas las familias de color que el presenter pinta, MÁS los
            // tres neutros medidos del frame (suelo, surco, fondo). El índice gana
            // al más cercano y cada entrada lleva su FAMILIA: 0 hormiga, 1 portadora,
            // 2 ítem, 3+ neutros (no se cuentan, solo evitan que un píxel de suelo
            // caiga en la familia más cercana que sí cuenta).
            var palette = new List<Rgb>();
            var family = new List<byte>();
            AddFamily(palette, family, ant, 0);
            AddFamily(palette, family, carrier, 1);
            AddFamily(palette, family, item, 2);
            palette.Add(floor); family.Add(3);
            palette.Add(grid); family.Add(4);
            palette.Add(background); family.Add(5);

            int antPx = 0, carrierPx = 0, itemPx = 0, dark = 0;
            int total = width * height;
            for (int i = 0; i < total; i++)
            {
                var p = Pixel(rgb, i);
                int best = 0, bestDist = int.MaxValue;
                for (int k = 0; k < palette.Count; k++)
                {
                    int dr = p.R - palette[k].R, dg = p.G - palette[k].G, db = p.B - palette[k].B;
                    int d = dr * dr + dg * dg + db * db;
                    if (d < bestDist) { bestDist = d; best = k; }
                }
                if ((int)Math.Sqrt(bestDist / 3.0) <= NearestThreshold)
                {
                    byte fam = family[best];
                    if (fam == 0) antPx++;
                    else if (fam == 1) carrierPx++;
                    else if (fam == 2) itemPx++;
                }
                if (p.R < DarkCut && p.G < DarkCut && p.B < DarkCut) dark++;
            }

            return new FrameGateResult(floor, grid, background, brown, antPx, carrierPx,
                itemPx, total, total > 0 ? (float)dark / total : 0f);
        }

        private static void AddFamily(List<Rgb> palette, List<byte> family, IReadOnlyList<Rgb> colors, byte id)
        {
            if (colors == null) return;
            foreach (var c in colors) { palette.Add(c); family.Add(id); }
        }

        private static Rgb Pixel(byte[] rgb, int index)
        {
            int i = index * 3;
            return new Rgb(rgb[i], rgb[i + 1], rgb[i + 2]);
        }
    }
}
