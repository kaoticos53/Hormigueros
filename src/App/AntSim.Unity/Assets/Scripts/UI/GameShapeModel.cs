using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.UI
{
    /// <summary>Vértice 2D en unidades de la EM (y hacia ARRIBA, como el glifo).</summary>
    public readonly struct V2
    {
        public readonly float X, Y;
        public V2(float x, float y) { X = x; Y = y; }
    }

    /// <summary>
    /// Un STAMP: polígono CONVEXO cerrado y en sentido antihorario. Es la única
    /// primitiva de la identidad visual del juego, y lo es por una razón de
    /// verificación: los dos consumidores pueden tratarla sin ambigüedad.
    ///
    ///   · LA FUENTE (uGUI) rellena cada stamp como un contorno propio con la
    ///     regla NON-ZERO de TrueType. Si TODOS los contornos van en el mismo
    ///     sentido, las zonas donde dos stamps se solapan suman 2 y siguen
    ///     llenas: la unión sale exacta, sin depender del orden ni de cortes.
    ///     (Por eso ninguna forma se construye con contornos de sentidos
    ///     opuestos: un anillo hueco —círculo dentro de círculo— restaría y
    ///     abriría un agujero donde otro trazo lo cruzase.)
    ///   · LA MALLA extruye cada stamp como un abanico de triángulos + sus
    ///     caras laterales. Al ser convexo, el abanico es una triangulación
    ///     VÁLIDA sin ear-clipping ni casos degenerados que depurar.
    ///
    /// La unión por solape es lo que hace que la hormiga sea tres elipses + seis
    /// patas sin calcular ninguna intersección: se dibujan unas sobre otras.
    /// </summary>
    public sealed class Stamp
    {
        public readonly V2[] P;

        public Stamp(V2[] points)
        {
            if (points == null || points.Length < 3)
                throw new ArgumentException("un stamp necesita al menos 3 puntos", nameof(points));
            P = EnsureCcw(points);
        }

        /// <summary>Área con signo (positiva = antihorario).</summary>
        public static float SignedArea(V2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++)
            {
                var q = p[(i + 1) % p.Length];
                a += p[i].X * q.Y - q.X * p[i].Y;
            }
            return a * 0.5f;
        }

        /// <summary>Invierte el orden si el polígono llega en sentido horario.</summary>
        public static V2[] EnsureCcw(V2[] p)
        {
            if (SignedArea(p) >= 0f) return p;
            var r = new V2[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = p[p.Length - 1 - i];
            return r;
        }

        public float MinX { get { float m = P[0].X; foreach (var q in P) if (q.X < m) m = q.X; return m; } }
        public float MaxX { get { float m = P[0].X; foreach (var q in P) if (q.X > m) m = q.X; return m; } }
        public float MinY { get { float m = P[0].Y; foreach (var q in P) if (q.Y < m) m = q.Y; return m; } }
        public float MaxY { get { float m = P[0].Y; foreach (var q in P) if (q.Y > m) m = q.Y; return m; } }
    }

    /// <summary>
    /// Fábrica de formas: TODO lo que se ve en el juego —el glifo de una letra,
    /// la silueta de una hormiga, la carga que lleva, un huevo— se compone de
    /// aquí. Sin curvas Bézier: las curvas se aproximan con polígonos de radio
    /// muestreado, así que el mismo dato sirve para la fuente (relleno) y para
    /// la malla (triangulación), y no hay que aplanar nada después.
    /// </summary>
    public static class Shapes
    {
        /// <summary>Círculo lleno (para puntos, cabezas, articulaciones).</summary>
        public static Stamp Disc(float cx, float cy, float r, int segments = 10)
        {
            var p = new V2[segments];
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                p[i] = new V2(cx + (float)Math.Cos(a) * r, cy + (float)Math.Sin(a) * r);
            }
            return new Stamp(p);
        }

        /// <summary>Elipse llena muestreada (cuerpos de la hormiga, huevo).</summary>
        public static Stamp Ellipse(float cx, float cy, float rx, float ry, int segments = 12)
        {
            var p = new V2[segments];
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                p[i] = new V2(cx + (float)Math.Cos(a) * rx, cy + (float)Math.Sin(a) * ry);
            }
            return new Stamp(p);
        }

        /// <summary>Rectángulo lleno (barras de pausa, cuadrados).</summary>
        public static Stamp Box(float cx, float cy, float halfW, float halfH)
            => new(new[]
            {
                new V2(cx - halfW, cy - halfH), new V2(cx + halfW, cy - halfH),
                new V2(cx + halfW, cy + halfH), new V2(cx - halfW, cy + halfH),
            });

        /// <summary>Triángulo lleno (puntas de flecha, ▶).</summary>
        public static Stamp Triangle(V2 a, V2 b, V2 c) => new(new[] { a, b, c });

        /// <summary>
        /// PÍLDORA: el trazo de la pluma monolineal. Un segmento de anchura
        /// constante con las puntas redondeadas — el «cap» redondo es lo que
        /// hace que dos segmentos que comparten vértice se lean como un trazo
        /// continuo sin tener que calcular la unión.
        /// </summary>
        public static Stamp Pill(float x0, float y0, float x1, float y1, float width, int capSegments = 3)
        {
            float hw = width * 0.5f;
            float dx = x1 - x0, dy = y1 - y0;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f)
            {
                // Segmento degenerado = un punto: la píldora es un disco.
                return Disc(x0, y0, hw, Math.Max(4, capSegments * 3));
            }
            double t = Math.Atan2(dy, dx);
            var pts = new List<V2>(capSegments * 2 + 2);
            // Cap del extremo 1: barre desde n a −n PASANDO por la dirección de
            // avance (θ), así el redondeo mira hacia delante.
            for (int i = 0; i <= capSegments; i++)
            {
                double a = t + Math.PI * 0.5 - Math.PI * i / capSegments;
                pts.Add(new V2(x1 + (float)Math.Cos(a) * hw, y1 + (float)Math.Sin(a) * hw));
            }
            // Cap del extremo 0: barre desde −n a n pasando por θ+π (hacia atrás).
            for (int i = 0; i <= capSegments; i++)
            {
                double a = t - Math.PI * 0.5 - Math.PI * i / capSegments;
                pts.Add(new V2(x0 + (float)Math.Cos(a) * hw, y0 + (float)Math.Sin(a) * hw));
            }
            return new Stamp(pts.ToArray());
        }

        /// <summary>
        /// Un TRAZO: polilínea de píldoras + un disco por vértice. El disco tapa
        /// la cuña que dos píldoras dejan en un vértice cerrado; sin él, una
        /// esquina de 90° saldría con un mordisco. Devuelve stamps sueltos, no
        /// un contorno unido: la unión la hace el relleno non-zero.
        /// </summary>
        public static void Stroke(List<Stamp> into, float width, IReadOnlyList<V2> points)
        {
            if (points.Count == 0) return;
            for (int i = 0; i + 1 < points.Count; i++)
                into.Add(Pill(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, width));
            for (int i = 0; i < points.Count; i++)
                into.Add(Disc(points[i].X, points[i].Y, width * 0.5f, 8));
        }

        /// <summary>
        /// BUCLE de trazo (polilínea cerrada): el anillo de una O, el contorno de
        /// un huevo a trazo. Se cierra con el segmento último→primero y se
        /// redondean los vértices; nada de contornos «huecos» con sentido
        /// invertido (ver Stamp).
        /// </summary>
        public static void Loop(List<Stamp> into, float width, IReadOnlyList<V2> points)
        {
            if (points.Count < 3) { Stroke(into, width, points); return; }
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                into.Add(Pill(a.X, a.Y, b.X, b.Y, width));
            }
            foreach (var p in points) into.Add(Disc(p.X, p.Y, width * 0.5f, 8));
        }

        /// <summary>Puntos muestreados de un bucle elíptico (para un anillo a trazo).</summary>
        public static V2[] EllipsePoints(float cx, float cy, float rx, float ry, int segments = 10)
        {
            var p = new V2[segments];
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                p[i] = new V2(cx + (float)Math.Cos(a) * rx, cy + (float)Math.Sin(a) * ry);
            }
            return p;
        }
    }

    /// <summary>
    /// Las siluetas del juego. Una sola definición por forma, dos consumidores:
    /// la MALLA del mundo (el presenter dibuja hormigas, cargas y hojas) y el
    /// GLIFO de la fuente propia (el HUD pinta los mismos iconos como texto).
    ///
    /// Todas viven en la MISMA caja normalizada que la primitiva que sustituyen:
    /// x ∈ [−0.5, 0.5] (ancho), y ∈ [−1, 1] (LARGO, que es el eje de avance del
    /// presenter: su rotación y su escala ya esperan 2 u de largo × 1 u de ancho,
    /// como la cápsula y la esfera de antes). Cambiar la caja obligaría a tocar
    /// la escala del presenter; respetarla es lo que deja el cambio en el dato.
    /// </summary>
    public static class GameSpriteModel
    {
        /// <summary>Semiancho de la caja normalizada (eje X del presenter).</summary>
        public const float HalfWidth = 0.5f;
        /// <summary>Semilargo de la caja normalizada (eje Y del presenter = avance).</summary>
        public const float HalfLength = 1.0f;
        /// <summary>Semigrosor de la caja normalizada (eje Z del presenter).</summary>
        public const float HalfThickness = 0.5f;

        /// <summary>
        /// Semiancho del LIMBO de la hoja (eje X normalizado): su borde lateral llega
        /// a 0,30. El presenter reparte los MORDISCOS sobre ESTA elipse —0,30 × 0,62 s
        /// en el plano del tablero tras <c>FlatRotation</c>—, no sobre un círculo: con
        /// un radio único la muesca no cae en el borde en ningún sitio (queda FUERA por
        /// los costados y DENTRO por las puntas), así que flota en el vacío junto a la
        /// hoja en vez de comer de ella.
        /// </summary>
        public const float LeafHalfWidth = 0.30f;

        /// <summary>Semilargo del LIMBO de la hoja (eje Y = eje de dibujo, que
        /// <c>FlatRotation</c> lleva al eje Z del tablero): su punta llega a 0,62. Ver
        /// <see cref="LeafHalfWidth"/>.</summary>
        public const float LeafHalfLength = 0.62f;

        public enum Sprite { Ant, Load, Leaf, Egg }

        public static IReadOnlyList<Stamp> Build(Sprite sprite) => sprite switch
        {
            Sprite.Ant => Ant(),
            Sprite.Load => Load(),
            Sprite.Leaf => Leaf(),
            Sprite.Egg => Egg(),
            _ => Array.Empty<Stamp>(),
        };

        /// <summary>
        /// HORMIGA de perfil cenital: tres lóbulos (gáster, tórax, cabeza), seis
        /// patas y dos antenas. La cabeza va hacia +Y porque el presenter rota
        /// con el rumbo del stream y la escala alarga el eje Y: el bicho tiene
        /// que «mirar» por donde avanza. Sustituye a la cápsula, que era un
        /// poste sin lectura de dirección (el defecto que destapó la sonda).
        ///
        /// El trazo de patas y antenas va FINO a propósito: en pantalla el bicho
        /// mide ~18 px y lo que tiene que leerse son los tres lóbulos. El icono
        /// del HUD no reutiliza estos polígonos, pero sí el sujeto y la
        /// gramática (tres lóbulos + patas): a 14 px de altura de texto una
        /// pluma de 0,14 u desaparecería, y engrosarla aquí haría un borrón en
        /// el mundo. Son dos tamaños ópticos del mismo dibujo, no dos dibujos.
        /// </summary>
        public static IReadOnlyList<Stamp> Ant()
        {
            var s = new List<Stamp>();
            const float w = 0.14f; // grosor de patas y antenas (caja normalizada)

            // Gáster (abdomen) — el lóbulo mayor, detrás.
            s.Add(Shapes.Ellipse(0f, -0.58f, 0.27f, 0.36f, 14));
            // Peciolo + tórax.
            s.Add(Shapes.Disc(0f, -0.22f, 0.075f, 8));
            s.Add(Shapes.Ellipse(0f, -0.10f, 0.16f, 0.21f, 12));
            // Cabeza.
            s.Add(Shapes.Ellipse(0f, 0.30f, 0.20f, 0.18f, 12));
            // Mandíbulas: dos trazos cortos que se abren por delante de la cabeza.
            s.Add(Shapes.Pill(-0.06f, 0.44f, -0.10f, 0.55f, w));
            s.Add(Shapes.Pill(0.06f, 0.44f, 0.10f, 0.55f, w));

            // Antenas acodadas: hacia fuera y luego hacia delante.
            s.Add(Shapes.Pill(-0.14f, 0.38f, -0.34f, 0.52f, w));
            s.Add(Shapes.Pill(-0.34f, 0.52f, -0.30f, 0.74f, w));
            s.Add(Shapes.Pill(0.14f, 0.38f, 0.34f, 0.52f, w));
            s.Add(Shapes.Pill(0.34f, 0.52f, 0.30f, 0.74f, w));

            // Tres pares de patas desde el tórax, acodadas hacia fuera y atrás.
            // (Nodo de la rodilla desplazado en X para que la pata se lea.) Las
            // puntas de las patas NO pasan de 0,50 − w/2: la caja es el contrato
            // con la escala del presenter y el test de malla la comprueba.
            for (int side = -1; side <= 1; side += 2)
            {
                s.Add(Shapes.Pill(side * 0.10f, 0.02f, side * 0.32f, 0.16f, w));
                s.Add(Shapes.Pill(side * 0.32f, 0.16f, side * 0.42f, 0.02f, w));

                s.Add(Shapes.Pill(side * 0.12f, -0.10f, side * 0.38f, -0.06f, w));
                s.Add(Shapes.Pill(side * 0.38f, -0.06f, side * 0.43f, -0.20f, w));

                s.Add(Shapes.Pill(side * 0.11f, -0.22f, side * 0.34f, -0.34f, w));
                s.Add(Shapes.Pill(side * 0.34f, -0.34f, side * 0.42f, -0.46f, w));
            }
            return s;
        }

        /// <summary>
        /// CARGA: lo que la hormiga llevará al nido. Un gránulo redondeado con
        /// una muesca —sustituye a la esfera, que no decía «comida»—. Cabe en la
        /// mitad superior de la caja porque el presenter la dibuja APOYADA en el
        /// suelo, no centrada en el largo de la hormiga.
        /// </summary>
        public static IReadOnlyList<Stamp> Load()
        {
            var s = new List<Stamp>
            {
                Shapes.Ellipse(0f, 0f, 0.34f, 0.40f, 14),
                // Dos lóbulos menores pegados: un grumo, no una bola perfecta.
                Shapes.Ellipse(-0.22f, 0.18f, 0.15f, 0.14f, 10),
                Shapes.Ellipse(0.20f, -0.20f, 0.13f, 0.12f, 10),
            };
            return s;
        }

        /// <summary>
        /// HOJA (F5.2a): limbo apuntado con nervio central. Sustituye a la esfera
        /// achatada con la que el presenter insinuaba la hoja. El limbo es un
        /// polígono convexo (dos arcos que se juntan en las puntas) y el nervio
        /// un trazo fino encima; los mordiscos los sigue dibujando el presenter.
        /// </summary>
        public static IReadOnlyList<Stamp> Leaf()
        {
            var s = new List<Stamp>
            {
                new(new[]
                {
                    new V2(0f, LeafHalfLength), new V2(0.22f, 0.30f), new V2(LeafHalfWidth, -0.05f),
                    new V2(0.16f, -0.45f), new V2(0f, -LeafHalfLength), new V2(-0.16f, -0.45f),
                    new V2(-LeafHalfWidth, -0.05f), new V2(-0.22f, 0.30f),
                }),
                Shapes.Pill(0f, -0.50f, 0f, 0.50f, 0.055f),
            };
            return s;
        }

        /// <summary>
        /// HUEVO: ovoide de la cría. Se pinta en el HUD (el stream solo trae el
        /// CONTEO por colonia, no posiciones), así que vive centrado en su caja.
        /// </summary>
        public static IReadOnlyList<Stamp> Egg()
        {
            var s = new List<Stamp>
            {
                // Ovoide: elipse estrechada en la punta superior.
                new(new[]
                {
                    new V2(0f, 0.52f), new V2(0.20f, 0.34f), new V2(0.33f, 0.02f),
                    new V2(0.30f, -0.30f), new V2(0.16f, -0.50f), new V2(-0.16f, -0.50f),
                    new V2(-0.30f, -0.30f), new V2(-0.33f, 0.02f), new V2(-0.20f, 0.34f),
                }),
            };
            return s;
        }
    }
}
