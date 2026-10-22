using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Rect en PÍXELES DE LA CAPTURA, con el origen abajo-izquierda (el de
    /// `ReadPixels` y el de la pantalla de Unity: los dos miden y desde abajo).
    /// Vive aquí, y no en el MonoBehaviour, porque el modelo de medición se
    /// verifica headless y no puede hablar en `Rect`.
    /// </summary>
    public readonly struct PixelRect
    {
        public readonly int X;
        public readonly int Y;
        public readonly int W;
        public readonly int H;

        public PixelRect(int x, int y, int w, int h)
        {
            X = x; Y = y;
            W = w < 0 ? 0 : w;
            H = h < 0 ? 0 : h;
        }

        /// <summary>Borde derecho (exclusivo).</summary>
        public int X1 => X + W;
        /// <summary>Borde superior (exclusivo).</summary>
        public int Y1 => Y + H;

        public bool IsEmpty => W <= 0 || H <= 0;

        /// <summary>¿El píxel cae dentro? (rangos semiabiertos por la derecha y por
        /// arriba, como el buffer).</summary>
        public bool Contains(int x, int y) => x >= X && x < X1 && y >= Y && y < Y1;

        /// <summary>¿Alguna de las ventanas excluidas contiene este píxel?</summary>
        public static bool InAny(IReadOnlyList<PixelRect>? rects, int x, int y)
        {
            if (rects == null) return false;
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(x, y)) return true;
            return false;
        }

        /// <summary>
        /// Del rect de PANTALLA (puntos, el que devuelve la UI) a píxeles de la
        /// captura. La escala se MIDE (ancho de la captura / ancho de pantalla) en
        /// vez de suponerse: en el editor la captura puede venir a otra resolución
        /// que la ventana, y suponerla desplazaría todas las ventanas de medida —
        /// el error que convierte una medida en un invento.
        /// </summary>
        public static PixelRect FromScreen(float x, float y, float w, float h, float scale, int maxW, int maxH)
        {
            int px = (int)(x * scale + 0.5f);
            int py = (int)(y * scale + 0.5f);
            int pw = (int)(w * scale + 0.5f);
            int ph = (int)(h * scale + 0.5f);
            // Recorte a la captura: una ventana de medida fuera del buffer se leería
            // como «sin tinta» y daría un verde falso.
            if (px < 0) { pw += px; px = 0; }
            if (py < 0) { ph += py; py = 0; }
            if (px + pw > maxW) pw = maxW - px;
            if (py + ph > maxH) ph = maxH - py;
            return new PixelRect(px, py, pw, ph);
        }

        /// <summary>Este rect, crecido en `m` píxeles por lado (recortado a la captura).</summary>
        public PixelRect Inflated(int m, int maxW, int maxH)
            => FromScreen(X - m, Y - m, W + 2 * m, H + 2 * m, 1f, maxW, maxH);

        /// <summary>Rect entre dos bordes (se ordenan: cualquiera de los dos puede
        /// ser el de arriba). Devuelve vacío si no hay hueco.</summary>
        public static PixelRect Between(int x0, int y0, int x1, int y1, int maxW, int maxH)
        {
            int loX = x0 < x1 ? x0 : x1, hiX = x0 < x1 ? x1 : x0;
            int loY = y0 < y1 ? y0 : y1, hiY = y0 < y1 ? y1 : y0;
            return FromScreen(loX, loY, hiX - loX, hiY - loY, 1f, maxW, maxH);
        }

        public override string ToString() => X + "," + Y + " " + W + "x" + H;
    }

    /// <summary>
    /// UN color OBJETIVO visto SOBRE el fondo real de la tarjeta. Es el par
    /// (fondo, color) con el que este modelo clasifica un píxel.
    ///
    /// POR QUÉ NO BASTA EL COLOR. Medido en el build (sonda del player, captura
    /// cruda, 2026-09-20): el MISMO glifo llega a la pantalla con coberturas muy
    /// distintas según cómo lo rasterice el atlas de la fuente dinámica. En la
    /// misma captura, y con el mismo texto y el mismo `color`, las tarjetas de una
    /// columna pintaron el glifo con el núcleo al 96 % de su color declarado
    /// (227,222,210 sobre el panel 49,42,35) y las de la otra al 84 %
    /// (198,193,182) — y el chip de color, al 74 % (frente al 90 %). Exigir el
    /// color declarado con una tolerancia por canal rechazaba como «sin pintar» un
    /// texto perfectamente legible: un falso suspenso. Lo que hay que medir es la
    /// COBERTURA sobre el fondo, que es justo lo que el ojo ve.
    /// </summary>
    public readonly struct PaintTarget
    {
        /// <summary>Fondo sobre el que se pinta (el panel medido de la tarjeta).</summary>
        public readonly Rgb Background;
        /// <summary>Color que el HUD declara para el glifo (tinta o tramo del chip).</summary>
        public readonly Rgb Target;

        public PaintTarget(Rgb background, Rgb target)
        {
            Background = background;
            Target = target;
        }

        public override string ToString() => Target + " sobre " + Background;
    }

    /// <summary>
    /// Cuánto de un color objetivo hay en un píxel: `Alpha` es la fracción del
    /// segmento fondo→color que lo explica (1 = píxel del color puro, 0 = fondo) y
    /// `Residual` la distancia que sobra (si el píxel no está sobre ese segmento,
    /// no es ese color por mucho brillo que tenga). Es el número que la sonda
    /// publica: «el chip llega a pantalla al 74 % de su color».
    /// </summary>
    public readonly struct Coverage
    {
        public readonly float Alpha;
        public readonly int Residual;

        public Coverage(float alpha, int residual)
        {
            Alpha = alpha;
            Residual = residual;
        }

        public override string ToString() => "α " + Alpha.ToString("0.##") + " r" + Residual;
    }

    /// <summary>
    /// UNA línea de texto medida: la banda de filas con tinta y los extremos de
    /// esa tinta. El ANCHO real de la línea es <see cref="Width"/> — lo que el
    /// jugador ve pintado, no lo que el layout reservó.
    /// </summary>
    public sealed class InkLine
    {
        /// <summary>Extremo izquierdo con tinta (inclusive).</summary>
        public readonly int MinX;
        /// <summary>Fila inferior con tinta (inclusive).</summary>
        public readonly int Y0;
        /// <summary>Extremo derecho con tinta (inclusive).</summary>
        public readonly int MaxX;
        /// <summary>Fila superior con tinta + 1 (exclusivo, como los rangos de C#).</summary>
        public readonly int Y1;
        /// <summary>Píxeles del color del texto (glifos de tinta).</summary>
        public readonly int InkPx;
        /// <summary>Píxeles de la PALETA del chip dentro de la banda: el chip va en
        /// otro color que el texto, así que sin contarlos por separado el ancho de la
        /// línea saldría corto — justo el número que se publica.</summary>
        public readonly int ChipPx;

        public InkLine(int minX, int y0, int maxX, int y1, int inkPx, int chipPx = 0)
        {
            MinX = minX; Y0 = y0; MaxX = maxX; Y1 = y1; InkPx = inkPx; ChipPx = chipPx;
        }

        /// <summary>Glifos de la banda: texto más chip.</summary>
        public int GlyphPx => InkPx + ChipPx;

        public int Width => MaxX < MinX ? 0 : MaxX - MinX + 1;
        public int Height => Y1 - Y0;

        public override string ToString() => $"x[{MinX}..{MaxX}] y[{Y0}..{Y1}) {Width}px";
    }

    /// <summary>
    /// Píxeles de UN tramo de la paleta del chip dentro de la ventana medida.
    /// `State` es el estado cuya paleta ganó (0 = ninguno). `Cover` es la cobertura
    /// MÁXIMA medida en ese tramo y `Mean` el color medio: son la prueba de que el
    /// color pintado es el que el modelo ordenó — la cobertura baja con el grosor
    /// del glifo (medido: 0,70–0,90), así que lo que se publica es la cobertura, no
    /// una igualdad de color que el rasterizador no garantiza.
    /// </summary>
    public sealed class ChipPixels
    {
        public readonly byte State;
        public readonly int Px;
        public readonly float Cover;
        public readonly Rgb Mean;

        public ChipPixels(byte state, int px, float cover, Rgb mean)
        {
            State = state; Px = px; Cover = cover; Mean = mean;
        }
    }

    /// <summary>Resultado de medir UNA tarjeta del HUD en un frame capturado.</summary>
    public sealed class HudCardMeasure
    {
        public string Name = "";
        /// <summary>Estados que el Core ordenó pintar (uno por colonia con chip).</summary>
        public readonly List<byte> Expected = new();
        /// <summary>Líneas que el texto declara (las que el modelo escribió).</summary>
        public int AuthoredLines;
        /// <summary>Bandas de tinta medidas, de arriba abajo.</summary>
        public readonly List<InkLine> Lines = new();
        /// <summary>Píxeles por tramo de la paleta (una entrada por estado 1..5).</summary>
        public readonly List<ChipPixels> Chips = new();
        /// <summary>Ancho que la tarjeta reservó para el texto.</summary>
        public int AvailableWidth;
        /// <summary>Ancho real de la PRIMERA línea medida (la que lleva el chip).</summary>
        public int TitleWidth;
        /// <summary>Aire de la línea MÁS ANCHA antes del borde reservado (lo que
        /// tiene que caber cuando el chip aparece en la línea del título).</summary>
        public int TitleHeadroom;
        /// <summary>Tinta dentro de la franja que separa el texto de las barras.</summary>
        public int StagingInkPx;
        /// <summary>Tinta dentro de las barras de reserva.</summary>
        public int BarInkPx;
        /// <summary>Las barras PISAN el rect del texto, así que no hay franja vacía
        /// que medir entre las dos: la invasión se lee del aire (0), no de una
        /// franja inexistente. Lo sabe la vista, que es quien tiene la geometría.</summary>
        public bool BarsOverlapText;
        /// <summary>Separación entre la línea más baja y la barra más cercana (0 = pega o pisa).</summary>
        public int BarGapPx;
        /// <summary>El fondo medido de la tarjeta (el panel sobre el que se pinta).</summary>
        public Rgb Background;
        public bool Ok;
        public string Verdict = "";
        /// <summary>La geometría que se midió (rect del texto y de las barras, en
        /// píxeles de la captura). Va en el informe porque una medida sin su ventana no
        /// se puede auditar — y porque un rect degenerado (ancho negativo del layout)
        /// se ve aquí antes que en ninguna otra parte.</summary>
        public string Geometry = "";
        /// <summary>La ventana medida como rejilla de caracteres (diagnóstico de la
        /// sonda: el mismo recurso que el mosaico del mundo).</summary>
        public string Mosaic = "";

        public override string ToString() => Name + ": " + Verdict;
    }

    /// <summary>
    /// F5.1 — MEDICIÓN del chip de runway EN PANTALLA, como modelo PURO: recibe el
    /// buffer de una captura y las ventanas (en píxeles) que la sonda del player
    /// calcula de los RectTransform, y devuelve los números y el veredicto.
    ///
    /// CÓMO SE CLASIFICA UN PÍXEL (y por qué así). No se compara con el color
    /// declarado: se mide la COBERTURA del color declarado SOBRE EL FONDO MEDIDO de
    /// la tarjeta (<see cref="Cover"/>). Tres cosas que se midieron y que este
    /// modelo resuelve de una vez:
    ///   · El glifo NO llega con su color puro: el núcleo mide 0,70–0,96 de su color
    ///     según el grosor y cómo lo rasterice el atlas de la fuente dinámica. Dos
    ///     tarjetas del MISMO texto pueden diferir en 12 puntos de cobertura en el
    ///     mismo frame (medido: 0,96 y 0,84). Exigir el color declarado a 26 de
    ///     tolerancia rechazaba como «línea sin pintar» un texto legible.
    ///   · El chip es MÁS fino que el texto: su cobertura máxima medida es 0,74–0,90,
    ///     y con ella una distancia absoluta a la paleta lo deja fuera de tolerancia.
    ///   · El verde de la barra de reserva (115,204,102) está a 27 del verde «reserva»
    ///     de la paleta: por color son casi el mismo. Por COBERTURA no lo son —para
    ///     explicar la barra sobre el panel harían falta tres alfas incompatibles
    ///     (residuo 23)—, así que el modelo la descarta solo. La exclusión geométrica
    ///     de las barras se mantiene igual: son dos cinturones, no uno.
    ///
    /// POR QUÉ ASÍ. El modelo del chip (`RunwayChipModel`) ya está verificado
    /// headless: sabe qué texto y qué color tocan. Lo que no puede probar ningún
    /// test de modelo es que eso LLEGUE a los píxeles: que el rich text se pinte, que
    /// la línea quepa en la tarjeta y que el texto no invada la barra de reserva.
    /// Eso solo se mide en una captura — y medirlo aquí, en un modelo sin
    /// UnityEngine, es lo que hace que la sonda del player y (si algún día se
    /// construye esa escena) la del juego compartan UNA implementación de la
    /// medida, con sus casos límite probados en la suite.
    ///
    /// LA PALETA NO SE REPITE: se lee de <see cref="RunwayChipModel.ColorHex"/>,
    /// que es la única definición de «de qué color va cada tramo». Una segunda
    /// tabla aquí podría discrepar del chip que se pinta y la medición daría verde
    /// sobre un color equivocado.
    /// </summary>
    public static class HudInkModel
    {
        /// <summary>Cobertura mínima para contar un píxel como glifo del color
        /// objetivo: por debajo de esto no hay glifo, hay fondo con ruido. Medido:
        /// los núcleos reales van de 0,70 a 0,96 y el ruido que pasa el residuo se
        /// queda en 0,31–0,40.</summary>
        public const float MinCoverage = 0.35f;

        /// <summary>Residuo máximo (distancia al segmento fondo→color) para aceptar una
        /// cobertura: un píxel que NO está sobre ese segmento no es ese color por mucho
        /// que brille. MEDIDO en la captura del build: los núcleos de tinta dan 0–4, y
        /// lo que se cuela por brillo (la barra de hongo, 209,158,76) da 83. El 10 deja
        /// dos veces de margen por los dos lados.</summary>
        public const int InkResidual = 10;

        /// <summary>Ídem para los tramos del chip. MEDIDO: los píxeles del chip dan 0–1,
        /// el verde de la barra de reserva 19, la tinta del texto (que por brillo se
        /// parece al verde «reserva») ≥ 22. El 8 separa los tres grupos con holgura.</summary>
        public const int ChipResidual = 8;

        /// <summary>Bajada máxima de brillo (por canal) que se le admite a un glifo
        /// de tinta ANTES de conocer el fondo: la usan los MOSAICOS, que son
        /// diagnóstico de la pantalla entera y no tienen un fondo por píxel. La
        /// puerta de verdad usa la cobertura, que sí lo conoce.</summary>
        public const int InkDrop = 70;

        /// <summary>Dispersión máxima entre canales para que un píxel cuente como
        /// tinta en un mosaico (la tinta es casi neutra: 237,232,219).</summary>
        public const int InkSpread = 46;

        /// <summary>Píxeles mínimos para declarar que un chip está pintado: un
        /// glifo de 14-15 px de fuente deja decenas; por debajo de esto es ruido,
        /// no un chip.</summary>
        public const int MinChipPx = 6;

        /// <summary>Píxeles por fila para considerar que una fila tiene glifos (un
        /// píxel suelto de antialiasing no abre banda).</summary>
        public const int MinRowInkPx = 2;

        /// <summary>Estados de la paleta, del más grave al más holgado.</summary>
        public static readonly byte[] PaletteStates = { 1, 2, 3, 4, 5 };

        /// <summary>
        /// Los chips que el Core ordena pintar: un estado por colonia con medida
        /// (el 0 — sin EMA o stream viejo — no pinta chip, por contrato).
        /// </summary>
        public static List<byte> ExpectedChips(IEnumerable<byte> runwayLevels)
        {
            var list = new List<byte>();
            foreach (var level in runwayLevels)
                if (level >= 1 && level <= 5) list.Add(level);
            return list;
        }

        /// <summary>La etiqueta con la que el chip escribe su color (rich text).</summary>
        public const string ColorTag = "<color=#";

        /// <summary>
        /// Los tramos que el TEXTO PINTADO declara, leídos de sus etiquetas
        /// <c>&lt;color=#RRGGBB&gt;</c>. Es la expectativa vista desde el HUD (no desde
        /// el stream): si el HUD escribiera el chip de otro tramo, esta lista lo
        /// delata sin necesidad de mirar un píxel.
        /// </summary>
        public static void ChipsInText(string richText, List<byte> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(richText)) return;
            int i = 0;
            while ((i = richText.IndexOf(ColorTag, i, StringComparison.Ordinal)) >= 0)
            {
                int start = i + ColorTag.Length;
                if (start + 6 > richText.Length) break;
                byte state = StateOfHex("#" + richText.Substring(start, 6));
                if (state != 0) into.Add(state);
                i = start + 6;
            }
        }

        /// <summary>El tramo cuya paleta es ESE hex (0 = ninguno). Se compara el hex,
        /// que es lo que el chip escribe: si un tramo cambia de color, la lectura del
        /// texto lo sigue sin tocar nada.</summary>
        public static byte StateOfHex(string hex)
        {
            for (int i = 0; i < PaletteStates.Length; i++)
                if (string.Equals(RunwayChipModel.ColorHex(PaletteStates[i]), hex,
                        StringComparison.OrdinalIgnoreCase))
                    return PaletteStates[i];
            return 0;
        }

        /// <summary>¿El texto pintado declara los MISMOS tramos que el Core ordenó?
        /// Multiconjunto, no conjunto: dos colonias en el mismo tramo necesitan dos
        /// chips pintados, y uno solo no las cubre.</summary>
        public static bool SameChips(IReadOnlyList<byte> ordered, IReadOnlyList<byte> painted)
        {
            if (ordered.Count != painted.Count) return false;
            var rest = new List<byte>(painted);
            foreach (var o in ordered)
            {
                int at = rest.IndexOf(o);
                if (at < 0) return false;
                rest.RemoveAt(at);
            }
            return true;
        }

        /// <summary>
        /// Líneas que el texto DECLARA: las cadenas no vacías del texto del
        /// modelo. Se cuentan aquí (y no con un split a pelo) porque el render del
        /// Core termina cada línea con un salto: contar el hueco final haría que
        /// una tarjeta bien pintada pareciera tener una línea sin pintar.
        /// </summary>
        public static int AuthoredLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            foreach (var line in text.Split('\n'))
                if (line.Trim().Length > 0) n++;
            return n;
        }

        /// <summary>Color autorado de un tramo (hex → RGB 0-255).</summary>
        public static Rgb Palette(byte state) => ParseHex(RunwayChipModel.ColorHex(state));

        /// <summary>«#RRGGBB» → RGB. Un hex mal formado devuelve negro: la paleta
        /// es total y el chip nunca emite una etiqueta sin color.</summary>
        public static Rgb ParseHex(string hex)
        {
            if (hex == null || hex.Length < 7 || hex[0] != '#') return new Rgb(0, 0, 0);
            return new Rgb((byte)Hex2(hex, 1), (byte)Hex2(hex, 3), (byte)Hex2(hex, 5));
        }

        private static int Hex2(string s, int i)
        {
            int hi = HexDigit(s[i]), lo = HexDigit(s[i + 1]);
            return hi < 0 || lo < 0 ? 0 : hi * 16 + lo;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>Distancia euclídea entre dos colores (la métrica de los
        /// diagnósticos: el veredicto mide cobertura, no distancia).</summary>
        public static int Distance(Rgb a, Rgb b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return (int)(System.Math.Sqrt(dr * dr + dg * dg + db * db) + 0.5);
        }

        /// <summary>
        /// CUÁNTO del color objetivo hay en el píxel: se proyecta el píxel sobre el
        /// segmento fondo→color y se devuelve la fracción (alfa) y lo que sobra
        /// (residuo). Es la operación central del modelo: con ella, un núcleo al
        /// 84 % y otro al 96 % son el MISMO glifo, y un verde de barra que se parece
        /// al verde de la paleta no lo es (tres alfas incompatibles ⇒ residuo alto).
        /// </summary>
        public static Coverage Cover(Rgb p, Rgb background, Rgb target)
        {
            int dr = target.R - background.R;
            int dg = target.G - background.G;
            int db = target.B - background.B;
            int len2 = dr * dr + dg * dg + db * db;
            if (len2 == 0) return new Coverage(0f, Distance(p, background));

            double num = (p.R - background.R) * dr + (p.G - background.G) * dg + (p.B - background.B) * db;
            double alpha = num / len2;
            if (alpha < 0) alpha = 0;
            if (alpha > 1.6) alpha = 1.6;   // por encima del color puro no hay glifo: acota el ruido

            double er = p.R - (background.R + alpha * dr);
            double eg = p.G - (background.G + alpha * dg);
            double eb = p.B - (background.B + alpha * db);
            int residual = (int)(System.Math.Sqrt(er * er + eg * eg + eb * eb) + 0.5);
            return new Coverage((float)alpha, residual);
        }

        /// <summary>¿El píxel es un glifo del color objetivo, con el fondo dado?</summary>
        public static bool IsTarget(Rgb p, PaintTarget target, int residual)
        {
            var c = Cover(p, target.Background, target.Target);
            return c.Alpha >= MinCoverage && c.Residual <= residual;
        }

        /// <summary>
        /// La tinta vista SIN fondo conocido (mosaicos de diagnóstico): un píxel es
        /// tinta si es CASI NEUTRO y claramente más brillante que el panel. No vale
        /// para el veredicto (no mide cobertura) y sí para ver DÓNDE hay texto en una
        /// rejilla de caracteres de la pantalla entera.
        /// </summary>
        public static bool LooksLikeInk(Rgb p, Rgb ink)
        {
            int min = Min(p), max = Max(p);
            return min >= Min(ink) - InkDrop && max - min <= InkSpread;
        }

        private static int Min(Rgb c) => c.R < c.G ? (c.R < c.B ? c.R : c.B) : (c.G < c.B ? c.G : c.B);
        private static int Max(Rgb c) => c.R > c.G ? (c.R > c.B ? c.R : c.B) : (c.G > c.B ? c.G : c.B);

        /// <summary>
        /// El FONDO de una tarjeta: el color más frecuente de su ventana. Es el panel
        /// semiopaco sobre el mundo —el mismo sitio donde va el texto—, y se MIDE en
        /// vez de suponerse porque su valor depende de lo que haya debajo. Un fondo
        /// equivocado no rompe la medida (la tinta casi blanca deja el segmento
        /// fondo→color casi paralelo para cualquier fondo oscuro), pero medirlo es
        /// lo que hace que la cobertura publicada sea comparable entre tarjetas.
        /// </summary>
        public static Rgb Background(byte[] rgb, int w, int h, PixelRect area)
        {
            var counts = new Dictionary<int, int>();
            int bestKey = 0, bestCount = 0;
            for (int y = area.Y; y < area.Y1 && y < h; y++)
            {
                if (y < 0) continue;
                for (int x = area.X; x < area.X1 && x < w; x++)
                {
                    if (x < 0) continue;
                    var p = Pixel(rgb, w, x, y);
                    int key = (p.R << 16) | (p.G << 8) | p.B;
                    counts.TryGetValue(key, out int n);
                    counts[key] = ++n;
                    if (n > bestCount) { bestCount = n; bestKey = key; }
                }
            }
            return bestCount == 0
                ? new Rgb(0, 0, 0)
                : new Rgb((byte)(bestKey >> 16), (byte)((bestKey >> 8) & 0xFF), (byte)(bestKey & 0xFF));
        }

        private static Rgb Pixel(byte[] rgb, int w, int x, int y)
        {
            int i = (y * w + x) * 3;
            return new Rgb(rgb[i], rgb[i + 1], rgb[i + 2]);
        }

        /// <summary>Píxeles del color del texto dentro de un área.</summary>
        public static int InkPixels(byte[] rgb, int w, int h, PixelRect area, PaintTarget ink)
        {
            int n = 0;
            for (int y = area.Y; y < area.Y1 && y < h; y++)
                for (int x = area.X; x < area.X1 && x < w; x++)
                    if (IsTarget(Pixel(rgb, w, x, y), ink, InkResidual)) n++;
            return n;
        }

        /// <summary>Estado de la paleta cuyo segmento explica mejor este píxel (0 =
        /// ninguno con cobertura y residuo suficientes). Es la MISMA clasificación que
        /// usa <see cref="Chips"/>: una sola definición de «este píxel es del tramo X».</summary>
        public static byte NearestChip(Rgb p, Rgb background)
        {
            byte best = 0;
            int bestResidual = int.MaxValue;
            for (int i = 0; i < PaletteStates.Length; i++)
            {
                var c = Cover(p, background, Palette(PaletteStates[i]));
                if (c.Alpha < MinCoverage || c.Residual > ChipResidual) continue;
                if (c.Residual < bestResidual) { bestResidual = c.Residual; best = PaletteStates[i]; }
            }
            return best;
        }

        /// <summary>¿El píxel es un glifo de texto? Sí si es tinta o de algún tramo de
        /// la paleta del chip: la línea del título lleva los dos, y medir solo la tinta
        /// dejaría el chip fuera del ancho real de la línea.</summary>
        private static bool GlyphPixel(Rgb p, PaintTarget ink, out bool chip)
        {
            if (IsTarget(p, ink, InkResidual)) { chip = false; return true; }
            chip = NearestChip(p, ink.Background) != 0;
            return chip;
        }

        /// <summary>
        /// Bandas de glifo: una por LÍNEA de texto, de arriba abajo. Se recorre fila a
        /// fila y se agrupan las contiguas; el ancho de cada banda es el ancho real
        /// pintado de esa línea (texto + chip), no el del rect.
        /// </summary>
        /// <param name="exclude">Ventanas que NO son texto y hay que ignorar: la barra
        /// de reserva. El modelo ya las distingue por cobertura, pero la geometría las
        /// conoce la vista y excluirlas cuesta cero: dos cinturones, no uno.</param>
        public static List<InkLine> Lines(byte[] rgb, int w, int h, PixelRect area,
            PaintTarget ink, int minRowInkPx, IReadOnlyList<PixelRect>? exclude = null)
        {
            var lines = new List<InkLine>();
            bool open = false;
            int minX = 0, maxX = 0, yTop = 0, yBottom = 0, inkPx = 0, chipPx = 0;
            for (int y = area.Y1 - 1; y >= area.Y; y--)
            {
                if (y >= h || y < 0) continue;
                int rowMinX = int.MaxValue, rowMaxX = int.MinValue, rowGlyphs = 0, rowChip = 0;
                for (int x = area.X; x < area.X1 && x < w; x++)
                {
                    if (x < 0 || PixelRect.InAny(exclude, x, y)) continue;
                    if (!GlyphPixel(Pixel(rgb, w, x, y), ink, out bool chip)) continue;
                    rowGlyphs++;
                    if (chip) rowChip++;
                    if (x < rowMinX) rowMinX = x;
                    if (x > rowMaxX) rowMaxX = x;
                }
                if (rowGlyphs >= minRowInkPx)
                {
                    if (!open) { open = true; minX = rowMinX; maxX = rowMaxX; yTop = y; yBottom = y; inkPx = 0; chipPx = 0; }
                    if (rowMinX < minX) minX = rowMinX;
                    if (rowMaxX > maxX) maxX = rowMaxX;
                    yBottom = y;
                    inkPx += rowGlyphs - rowChip;
                    chipPx += rowChip;
                }
                else if (open)
                {
                    lines.Add(new InkLine(minX, yBottom, maxX, yTop + 1, inkPx, chipPx));
                    open = false;
                }
            }
            if (open) lines.Add(new InkLine(minX, yBottom, maxX, yTop + 1, inkPx, chipPx));
            return lines;
        }

        /// <summary>
        /// Píxeles por tramo de la paleta en un área: cada píxel vota por el tramo
        /// cuyo segmento fondo→color lo explica mejor. Devuelve una entrada por estado
        /// de la paleta, en orden, con cero píxeles incluido.
        /// </summary>
        public static List<ChipPixels> Chips(byte[] rgb, int w, int h, PixelRect area, Rgb background,
            IReadOnlyList<PixelRect>? exclude = null)
        {
            var acc = new ChipAccumulator(background);
            acc.Add(rgb, w, h, area, exclude);
            return acc.Result();
        }

        /// <summary>
        /// Píxeles por tramo contando SOLO dentro de las bandas de línea medidas. Es
        /// la cuenta que usa la sonda, y no la del área entera, porque la tarjeta
        /// entera incluye las barras de reserva: aunque el modelo las distinga por
        /// cobertura, contar dentro de las líneas es lo que ata el chip a la LÍNEA
        /// que lo lleva (un chip pintado fuera del texto no es el del texto).
        /// </summary>
        public static List<ChipPixels> ChipsInLines(byte[] rgb, int w, int h,
            IReadOnlyList<InkLine> lines, Rgb background, IReadOnlyList<PixelRect>? exclude = null)
        {
            var acc = new ChipAccumulator(background);
            foreach (var l in lines)
                acc.Add(rgb, w, h, new PixelRect(l.MinX, l.Y0, l.Width, l.Height), exclude);
            return acc.Result();
        }

        /// <summary>Acumulador de votos por tramo (una entrada por estado).</summary>
        private sealed class ChipAccumulator
        {
            private readonly Rgb _bg;
            private readonly int[] _px = new int[PaletteStates.Length];
            private readonly float[] _cover = new float[PaletteStates.Length];
            private readonly long[] _sumR = new long[PaletteStates.Length];
            private readonly long[] _sumG = new long[PaletteStates.Length];
            private readonly long[] _sumB = new long[PaletteStates.Length];

            public ChipAccumulator(Rgb background) { _bg = background; }

            public void Add(byte[] rgb, int w, int h, PixelRect area, IReadOnlyList<PixelRect>? exclude = null)
            {
                for (int y = area.Y; y < area.Y1 && y < h; y++)
                {
                    if (y < 0) continue;
                    for (int x = area.X; x < area.X1 && x < w; x++)
                    {
                        if (x < 0 || PixelRect.InAny(exclude, x, y)) continue;
                        var p = Pixel(rgb, w, x, y);
                        byte state = NearestChip(p, _bg);
                        if (state == 0) continue;
                        int i = state - 1;
                        var cov = Cover(p, _bg, Palette(state));
                        _px[i]++;
                        if (cov.Alpha > _cover[i]) _cover[i] = cov.Alpha;
                        _sumR[i] += p.R; _sumG[i] += p.G; _sumB[i] += p.B;
                    }
                }
            }

            public List<ChipPixels> Result()
            {
                var list = new List<ChipPixels>(PaletteStates.Length);
                for (int i = 0; i < PaletteStates.Length; i++)
                {
                    var mean = _px[i] == 0
                        ? new Rgb(0, 0, 0)
                        : new Rgb((byte)(_sumR[i] / _px[i]), (byte)(_sumG[i] / _px[i]), (byte)(_sumB[i] / _px[i]));
                    list.Add(new ChipPixels(PaletteStates[i], _px[i], _cover[i], mean));
                }
                return list;
            }
        }

        /// <summary>
        /// Separación entre la línea más baja medida y la barra más cercana. 0 =
        /// no hay hueco (se pegan o se pisan). La dirección la decide la geometría:
        /// si la barra está por debajo se mide en vertical; si está a la izquierda,
        /// en horizontal — el HUD grande pone la barra debajo del texto y la tarjeta
        /// compacta del multi-visor la pone a su izquierda, y las dos cosas tienen
        /// que medirse con la misma regla.
        /// </summary>
        public static int BarGap(IReadOnlyList<InkLine> lines, IReadOnlyList<PixelRect> bars)
        {
            int best = int.MaxValue;
            foreach (var bar in bars)
            {
                if (bar.IsEmpty) continue;
                foreach (var line in lines)
                {
                    int gap;
                    bool insideX = line.MinX < bar.X1 && bar.X < line.MaxX + 1;
                    bool insideY = line.Y0 < bar.Y1 && bar.Y < line.Y1;
                    if (insideX && insideY) gap = 0;                       // se pisan
                    else if (bar.Y1 <= line.Y0) gap = line.Y0 - bar.Y1;     // la barra está debajo
                    else if (line.Y1 <= bar.Y) gap = bar.Y - line.Y1;       // la barra está encima
                    else if (bar.X1 <= line.MinX) gap = line.MinX - bar.X1; // la barra está a la izquierda
                    else gap = bar.X - line.MaxX - 1;                       // …o a la derecha
                    if (gap < best) best = gap;
                }
            }
            return best == int.MaxValue ? 0 : (best < 0 ? 0 : best);
        }

        /// <summary>
        /// El veredicto de UNA tarjeta. Es una función pura: la sonda del player solo
        /// le pasa lo que midió y la suite headless prueba los mismos casos límite
        /// (chip ausente, color equivocado del tramo, línea que desborda, tinta en la
        /// franja de la barra). Reglas, en orden — la primera que salta es el motivo:
        ///
        ///   1. El número de líneas pintadas tiene que ser el que el texto declara:
        ///      ni más (envuelve o se cuela otra cosa) ni menos (se perdió una).
        ///   2. La línea más larga no puede desbordar el ancho reservado: la tarjeta
        ///      con el chip es más ancha que la tarjeta sin él, y eso es justo lo que
        ///      hay que medir.
        ///   3. Cada chip ordenado tiene que estar pintado, con sus píxeles y de su
        ///      color; y NO puede haber un chip de un tramo que el Core no ordenó
        ///      (un color viejo que se quedó en pantalla).
        ///   4. Ni una gota de tinta en la franja que separa el texto de la barra ni
        ///      dentro de la barra de reserva.
        /// </summary>
        public static HudCardMeasure Judge(string name, IReadOnlyList<byte> expectedChips, int authoredLines,
            IReadOnlyList<InkLine> lines, IReadOnlyList<ChipPixels> chips, int availableWidth,
            int stagingInkPx, int barInkPx, IReadOnlyList<PixelRect> bars)
        {
            var m = new HudCardMeasure
            {
                Name = name,
                AuthoredLines = authoredLines,
                AvailableWidth = availableWidth,
                StagingInkPx = stagingInkPx,
                BarInkPx = barInkPx,
            };
            m.Expected.AddRange(expectedChips);
            m.Lines.AddRange(lines);
            m.Chips.AddRange(chips);

            int widest = 0;
            foreach (var l in lines) if (l.Width > widest) widest = l.Width;
            m.TitleWidth = lines.Count > 0 ? lines[0].Width : 0;
            m.TitleHeadroom = availableWidth - widest;
            m.BarGapPx = BarGap(lines, bars);

            if (authoredLines > 0 && lines.Count > authoredLines)
            {
                m.Ok = false;
                m.Verdict = $"{lines.Count} bandas de tinta para {authoredLines} líneas escritas: el texto envuelve";
                return m;
            }
            if (authoredLines > 0 && lines.Count < authoredLines)
            {
                m.Ok = false;
                m.Verdict = $"{lines.Count} bandas de tinta para {authoredLines} líneas escritas: hay líneas sin pintar";
                return m;
            }
            if (widest > availableWidth)
            {
                m.Ok = false;
                m.Verdict = $"la línea más ancha mide {widest} px y el ancho reservado es {availableWidth} px: desborda";
                return m;
            }

            // Los chips: cuántos por tramo espera el Core contra los que hay pintados.
            for (int s = 1; s <= 5; s++)
            {
                int want = 0;
                foreach (var e in expectedChips) if (e == s) want++;
                var got = Find(chips, (byte)s);
                if (want > 0 && got.Px < want * MinChipPx)
                {
                    m.Ok = false;
                    m.Verdict = want == 1
                        ? $"el chip del tramo {RunwayChipModel.Label((byte)s)} no está en pantalla " +
                          $"({got.Px} px de su color, mínimo {MinChipPx})"
                        : $"{want} colonias en {RunwayChipModel.Label((byte)s)} y solo {got.Px} px de su color pintados " +
                          $"(mínimo {want * MinChipPx})";
                    return m;
                }
                if (want == 0 && got.Px >= MinChipPx)
                {
                    m.Ok = false;
                    m.Verdict = $"hay {got.Px} px del tramo {RunwayChipModel.Label((byte)s)} en pantalla " +
                                "y el Core no lo ordenó: color viejo o de otro tramo";
                    return m;
                }
            }
            if (stagingInkPx > 0)
            {
                m.Ok = false;
                m.Verdict = $"{stagingInkPx} px de texto en la franja de la barra de reserva: el texto invade la barra";
                return m;
            }
            if (barInkPx > 0)
            {
                m.Ok = false;
                m.Verdict = $"{barInkPx} px de texto dentro de la barra de reserva";
                return m;
            }

            m.Ok = true;
            m.Verdict = ChipReport(chips, expectedChips, m);
            return m;
        }

        private static ChipPixels Find(IReadOnlyList<ChipPixels> chips, byte state)
        {
            foreach (var c in chips) if (c.State == state) return c;
            return new ChipPixels(state, 0, 0f, new Rgb(0, 0, 0));
        }

        /// <summary>Resumen de los chips medidos para el veredicto verde: qué tramos
        /// están pintados y con qué COBERTURA llegaron (el número que dice si el color
        /// del chip está de verdad en pantalla, y no una igualdad que el rasterizador
        /// de fuentes no garantiza).</summary>
        private static string ChipReport(IReadOnlyList<ChipPixels> chips, IReadOnlyList<byte> expected, HudCardMeasure m)
        {
            if (expected.Count == 0) return "sin chip ordenado (colonia sin medida de runway)";
            var parts = new List<string>();
            for (int s = 1; s <= 5; s++)
            {
                int want = 0;
                foreach (var e in expected) if (e == s) want++;
                if (want == 0) continue;
                var got = Find(chips, (byte)s);
                parts.Add($"{RunwayChipModel.Label((byte)s)}×{want} {RunwayChipModel.ColorHex((byte)s)} " +
                          $"{got.Px}px α {got.Cover.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
            }
            return string.Join(" · ", parts);
        }
    }
}
