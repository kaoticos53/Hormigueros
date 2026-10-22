using System;
using System.Collections.Generic;
using System.Globalization;

namespace AntSim.Unity.Scripts.UI
{
    /// <summary>
    /// LA FUENTE DEL JUEGO — definición pura, sin UnityEngine.
    ///
    /// POR QUÉ EXISTE: el HUD usaba <c>LegacyRuntime.ttf</c>, la fuente de
    /// relleno del motor. Eso ataba el aspecto del juego a lo que Unity traiga
    /// instalado, no cubría los signos que el HUD necesita («·», «─», «○ ◐ ●»,
    /// «→», «≈», «⏸») y era, literalmente, el mismo tipo que el de cualquier
    /// proyecto nuevo. Aquí el juego se dibuja SU tipografía: un solo trazo
    /// geométrico, caja monorriel (todas las letras avanzan lo mismo), pensada
    /// para leerse en el HUD a 13–24 px y para medir igual que la anterior —el
    /// ancho de la tarjeta de colonia está calculado con 7 px por carácter a
    /// 14 px, y esa cuenta NO se toca.
    ///
    /// CÓMO SE DEFINEN LOS GLIFOS: una gramática de texto diminuta (ver
    /// <see cref="Parse"/>), no puntos codificados a mano:
    ///   <c>p:x,y x,y …</c>  polilínea a trazo (la pluma monolineal)
    ///   <c>e:cx,cy,rx,ry</c> anillo elíptico a trazo (la O, el redondel)
    ///   <c>d:cx,cy,r</c>      disco lleno (punto, lóbulo, articulación)
    ///   <c>o:cx,cy,rx,ry</c>  elipse llena (huevo, carga)
    ///   <c>b:cx,cy,w,h</c>    rectángulo lleno (barras de pausa)
    ///   <c>t:x,y,x,y,x,y</c>  triángulo lleno (▶)
    ///   <c>h:cx,cy,r</c>      medio disco lleno (◐)
    /// El prefijo <c>-</c> adelgaza el trazo (0,7×) y <c>+</c> lo engrosa (1,8×).
    /// Las coordenadas van en la REJILLA DE LA CELDA: x ∈ [0, 5] (0 = borde
    /// izquierdo del avance, 5 = derecho) e y en filas de 100 unidades con la
    /// línea base en 0, la altura de x en 5, la altura de mayúscula en 7 y el
    /// descendente en −2. Escribir así los glifos es lo que hace posible
    /// revisarlos y corregirlos como texto, tachán a tachán.
    ///
    /// COBERTURA: ASCII imprimible COMPLETO más los signos que el HUD usa de
    /// verdad (medidos sobre las cadenas del código, no supuestos) más los
    /// acentos del castellano compuestos por superposición. La lista viva de lo
    /// que hay que cubrir y su comprobación headless están en
    /// <c>GameFontTests</c>: si alguien usa un carácter nuevo en el HUD, el test
    /// falla antes de que salga un hueco en pantalla.
    /// </summary>
    public static class GameFontModel
    {
        // — Métrica (unidades de la em) ————————————————————————————————
        public const int UnitsPerEm = 1000;
        /// <summary>Avance monorriel: 0,5 em = 7 px a 14 px (la cuenta del ancho de tarjeta).</summary>
        public const int Advance = 500;
        public const int Ascender = 790;
        public const int Descender = -230;
        public const int LineGap = 0;
        /// <summary>Grosor de la pluma monolineal.</summary>
        public const int Pen = 100;
        /// <summary>Avance de un glifo-icono: más ancho que una letra, para que la silueta respire.</summary>
        public const int IconAdvance = 720;

        // — Rejilla de la celda ————————————————————————————————————————
        private const float CellX = 72f;    // unidades por columna (5 columnas = 360)
        private const float CellY = 100f;   // unidades por fila
        private const float OriginX = 70f;  // borde izquierdo de la caja de tinta

        /// <summary>Iconos del juego (glifos de uso privado): hormiga, carga y huevo.</summary>
        public const int AntGlyph = 0xE000;
        public const int LoadGlyph = 0xE001;
        public const int EggGlyph = 0xE002;

        /// <summary>Cadena de un solo icono, para el HUD (evita literales sueltos).</summary>
        public static string Ant => char.ConvertFromUtf32(AntGlyph);
        public static string Load => char.ConvertFromUtf32(LoadGlyph);
        public static string Egg => char.ConvertFromUtf32(EggGlyph);

        // — Tamaño del texto en la tarjeta de colonia (px de diseño del HUD) ——
        // El icono va en un RUN más grande que el cuerpo, NO en un cuerpo más
        // grande para todos. La cuenta que lo decide, medida: la tarjeta mide
        // 440×268 con paso 280 y la línea de 1 s sale de ella; al doble (880×536,
        // paso 560) la segunda tarjeta terminaría en 72+560+536 = 1168 unidades
        // y el canvas de referencia —1920×1080— solo tiene 1080: en 16:9 la
        // segunda tarjeta se saldría de la pantalla. El run, en cambio, escala el
        // glifo sin mover la caja del texto ni el avance de las letras.
        /// <summary>Tamaño del cuerpo de la tarjeta de colonia (el `fontSize` del
        /// Text que la pinta). Única definición: la escena la construye con esta
        /// constante y la suite comprueba la relación con el icono.</summary>
        public const int CardBodySize = 14;
        /// <summary>Tamaño de los tres iconos DENTRO del cuerpo: el DOBLE. A 14 px
        /// medían huevo 5×7, carga 7×8 y hormiga 9×14 px —tres manchas—; al doble
        /// miden 10×14, 14×16 y 18×28 y la silueta vuelve a leerse.</summary>
        public const int CardIconSize = CardBodySize * 2;

        /// <summary>Un icono como RUN de rich text de tamaño `pointSize`: el glifo
        /// es el de la fuente del juego, pintado a ese tamaño. Es lo que permite
        /// que el icono crezca sin tocar el avance del texto que lo rodea (el
        /// ancho de la tarjeta está contado con 0,5 em por letra y esa cuenta no
        /// se mueve).</summary>
        public static string IconRun(int codepoint, int pointSize)
            => "<size=" + pointSize.ToString(CultureInfo.InvariantCulture) + ">"
               + char.ConvertFromUtf32(codepoint) + "</size>";

        /// <summary>El icono al tamaño de icono de la tarjeta — el que usan las
        /// tarjetas. Los crudos de arriba son para texto que NO es de tarjeta.</summary>
        public static string AntLarge => IconRun(AntGlyph, CardIconSize);
        public static string LoadLarge => IconRun(LoadGlyph, CardIconSize);
        public static string EggLarge => IconRun(EggGlyph, CardIconSize);

        // — Glifos base reutilizados por la composición de acentos ————
        private const string LowerA = "p:4.6,5 4.6,0;p:4.6,4 3.2,5 1.4,5 0,3.8 0,1.2 1.4,0 3.2,0 4.6,1";
        private const string LowerE = "p:0,2.9 4.6,2.9 4.6,3.8 3.2,5 1.4,5 0,3.8 0,1.2 1.4,0 3.4,0 4.6,0.8";
        private const string UpperA = "p:0,0 2.5,7 5,0;p:0.9,2.3 4.1,2.3";
        private const string UpperE = "p:0,0 0,7;p:0,7 4.8,7;p:0,3.6 4.2,3.6;p:0,0 4.8,0";
        private const string UpperI = "p:1,7 4,7;p:2.5,7 2.5,0;p:1,0 4,0";
        private const string UpperO = "p:0,1.6 1.6,0 3.4,0 5,1.6 5,5.4 3.4,7 1.6,7 0,5.4 0,1.6";
        private const string UpperU = "p:0,7 0,1.6 1.6,0 3.4,0 5,1.6 5,7";
        private const string LowerN = "p:0,5 0,0;p:0,4 1.4,5 3.2,5 4.6,3.8 4.6,0";
        private const string LowerO = "p:0,1.3 1.3,0 3.3,0 4.6,1.3 4.6,3.7 3.3,5 1.3,5 0,3.7 0,1.3";
        private const string LowerU = "p:0,5 0,1.2 1.4,0 3.2,0 4.6,1;p:4.6,5 4.6,0";

        // — Acentos (sobre la altura de x = 5 y sobre la de mayúscula = 7) —
        private const string AcuteLow = "p:2.2,6.5 3.8,7.7";
        private const string AcuteCap = "p:1.5,8.2 3.1,9.4";
        private const string TildeLow = "p:1.2,6.4 2.3,7.0 3.5,6.3 4.3,6.7";
        private const string TildeCap = "p:1.2,8.2 2.3,8.8 3.5,8.1 4.3,8.5";
        private const string DotsLow = "d:1.9,7.0,0.4;d:3.6,7.0,0.4";
        private const string DotsCap = "d:1.9,8.9,0.4;d:3.6,8.9,0.4";

        // — Los iconos del juego: el mismo sujeto, tamaño óptico de 14 px ——
        // MEDIDO, y esta es la lección: la versión anterior dibujaba la hormiga
        // en 508 × 290 unidades y con las patas finas (55 u = 0,77 px a 14 px).
        // Rasterizada a 14 px —el tamaño REAL de las tarjetas— y ampliada 16×, no
        // se veía una hormiga: los tres lóbulos caían a 2-3 px cada uno con
        // separaciones de menos de un píxel y las patas desaparecían, así que los
        // tres iconos salían como manchas indistinguibles entre sí.
        //
        // POR QUÉ NO BASTAN MÁS LÓBULOS. Tres lóbulos separados de verdad (muesca
        // de ~1 px) piden 746 u de ancho y la caja de los iconos es el avance
        // (720 u, con 70 de sangrado): no caben, y ensanchar el avance movería el
        // texto de las tarjetas. Así que lo que los distingue a 14 px es la
        // SILUETA, no el detalle interior: la hormiga es la única con PATAS
        // (trazo a grosor normal, 100 u = 1,4 px, que sí llega a pintarse), la
        // carga es un bulto redondo y grumoso (cuerpo + tres satélites) y el
        // huevo un óvalo liso y alto. Las tres viven en la misma caja de avance,
        // así que el ancho de la tarjeta no se toca.
        private const string AntIcon =
            "d:2.40,3.60,2.05;d:4.50,3.60,1.70;d:6.05,3.60,1.35;" +  // gáster, tórax, cabeza
            "p:6.70,4.20 8.10,5.90;p:6.70,3.00 8.10,1.30;" +         // antenas
            "p:4.70,4.30 5.60,7.00;p:4.70,2.90 5.60,0.20;" +         // patas delanteras
            "p:3.90,4.60 3.30,7.90;p:3.90,2.60 3.30,-0.70;" +        // patas medias
            "p:2.60,4.30 1.40,6.30;p:2.60,2.90 1.40,0.90";          // patas traseras

        private const string LoadIcon =
            "d:3.60,3.60,2.35;d:1.45,5.30,1.05;d:5.75,2.05,1.10;d:5.15,5.60,0.85";

        private const string EggIcon = "o:3.60,3.60,2.30,3.40";

        /// <summary>La tabla: código Unicode → glifo.</summary>
        private static readonly (int Cp, string Dsl)[] Table =
        {
            // ————— ASCII imprimible —————
            (0x20, ""),                                                   // espacio
            (0x21, "p:2.4,7 2.4,2.2;p:2.4,0.9 2.4,0.6"),                  // !
            (0x22, "-p:1.7,7 1.7,5.4;-p:3.5,7 3.5,5.4"),                  // "
            (0x23, "p:1.4,7 0.6,0;p:4,7 3.2,0;p:0.2,4.8 4.6,4.8;p:0.6,2.2 5,2.2"),
            (0x24, "p:4.8,5.8 3.4,7 1.4,7 0,5.8 0,4.8 1.4,3.8 3.4,3.4 4.8,2.4 4.8,1.2 3.4,0 1.4,0 0,1.2;-p:2.4,7.8 2.4,-0.8"),
            (0x25, "p:0,0 5,7;p:0.4,5.6 0.4,6.8 1.6,6.8 1.6,5.6 0.4,5.6;p:3.4,0.2 3.4,1.4 4.6,1.4 4.6,0.2 3.4,0.2"),
            (0x26, "p:4.6,0.4 2.2,2.4 0.6,4.2 0.6,5.4 1.7,6.6 2.9,6.6 4,5.6 4,4.6 2.4,3 1.2,1.8 1.2,0.9 2.3,0 3.8,0 5,1.1"),
            (0x27, "-p:2.4,7 2.4,5.4"),                                   // '
            (0x28, "-p:3.2,7.6 1.8,6 1.8,1 3.2,-0.6"),                    // (
            (0x29, "-p:1.4,7.6 2.8,6 2.8,1 1.4,-0.6"),                    // )
            (0x2A, "-p:2.4,6.6 2.4,3.8;-p:1.3,6.3 3.5,4.1;-p:3.5,6.3 1.3,4.1"),
            (0x2B, "p:2.4,5.6 2.4,1.4;p:0.3,3.5 4.5,3.5"),                // +
            (0x2C, "-p:2.7,0.5 2.1,-0.9"),                                // ,
            (0x2D, "p:0.5,3.4 4.3,3.4"),                                  // -
            (0x2E, "d:2.4,0.5,0.5"),                                      // .
            (0x2F, "p:0,0 5,7"),                                          // /
            (0x30, "p:0,1.6 1.6,0 3.4,0 5,1.6 5,5.4 3.4,7 1.6,7 0,5.4 0,1.6"),
            (0x31, "p:1.2,5.5 2.6,7 2.6,0;p:0.9,0 4.3,0"),
            (0x32, "p:0,5.8 1.5,7 3.5,7 5,5.8 5,4.9 0,0 5,0"),
            (0x33, "p:0.2,7 5,7 2.5,4.2 3.7,4.2 5,2.9 5,1.2 3.6,0 1.4,0 0,1"),
            (0x34, "p:3.6,7 0,2.2 5,2.2;p:3.6,4.2 3.6,0"),
            (0x35, "p:5,7 0,7 0,3.8 3.4,3.8 5,2.5 5,1.3 3.6,0 1.4,0 0,1"),
            (0x36, "p:4.8,6.2 3.4,7 1.6,7 0,5.4 0,1.6 1.6,0 3.4,0 5,1.6 5,2.4 3.4,4 1.6,4 0,2.4"),
            (0x37, "p:0,7 5,7 1.8,0"),
            (0x38, "p:1.5,3.9 0,2.8 0,1.3 1.5,0 3.5,0 5,1.3 5,2.8 3.5,3.9 1.5,3.9;p:1.5,3.9 0.7,5 0.7,6 2,7 3,7 4.3,6 4.3,5 3.5,3.9"),
            (0x39, "p:0.2,0.8 1.6,0 3.4,0 5,1.6 5,5.4 3.4,7 1.6,7 0,5.4 0,4.6 1.6,3 3.4,3 5,4.6"),
            (0x3A, "d:2.4,4.3,0.5;d:2.4,0.5,0.5"),                        // :
            (0x3B, "d:2.4,4.3,0.5;-p:2.7,0.5 2.1,-0.9"),                  // ;
            (0x3C, "p:4.4,6.4 0.6,3.5 4.4,0.6"),                          // <
            (0x3D, "p:0.4,4.7 4.6,4.7;p:0.4,2.3 4.6,2.3"),                // =
            (0x3E, "p:0.6,6.4 4.4,3.5 0.6,0.6"),                          // >
            (0x3F, "p:0,5.8 1.4,7 3.4,7 4.8,5.8 4.8,4.9 2.4,3.3 2.4,2.3;p:2.4,0.9 2.4,0.6"),
            (0x40, "p:1.4,5 0.4,3.8 0.4,2 1.6,0 3.6,0 5,0.9;p:4.4,4.6 4.4,1 5.2,0.6;p:4.4,3 3.2,2.1 2.1,2.3 1.8,3.2 2.3,4.2 3.4,4.2 4.4,3.3"),
            (0x41, UpperA), (0x42, "p:0,0 0,7;p:0,7 3.2,7 4.6,6 4.6,4.7 3.2,3.6 0,3.6;p:3.2,3.6 4.8,2.5 4.8,1.1 3.2,0 0,0"),
            (0x43, "p:5,1.5 3.5,0 1.5,0 0,1.5 0,5.5 1.5,7 3.5,7 5,5.5"),
            (0x44, "p:0,0 0,7;p:0,7 2.8,7 5,5.5 5,1.5 2.8,0 0,0"),
            (0x45, UpperE),
            (0x46, "p:0,0 0,7;p:0,7 4.8,7;p:0,3.6 4,3.6"),
            (0x47, "p:5,1.5 3.5,0 1.5,0 0,1.5 0,5.5 1.5,7 3.5,7 5,5.5 5,3.6 2.8,3.6"),
            (0x48, "p:0,0 0,7;p:5,0 5,7;p:0,3.6 5,3.6"),
            (0x49, UpperI),
            (0x4A, "p:1,7 4.6,7;p:4.6,7 4.6,1.5 3.2,0 1.6,0 0,1.5"),
            (0x4B, "p:0,0 0,7;p:0,2.8 4.6,7;p:1.8,3.9 4.8,0"),
            (0x4C, "p:0,7 0,0;p:0,0 4.8,0"),
            (0x4D, "p:0,0 0,7 2.5,3.2 5,7 5,0"),
            (0x4E, "p:0,0 0,7 5,0 5,7"),
            (0x4F, UpperO),
            (0x50, "p:0,0 0,7;p:0,7 3.4,7 4.8,5.8 4.8,4.4 3.4,3.3 0,3.3"),
            (0x51, UpperO + ";p:3.1,2.1 5,-0.5"),
            (0x52, "p:0,0 0,7;p:0,7 3.4,7 4.8,5.8 4.8,4.4 3.4,3.3 0,3.3;p:2.4,3.3 4.8,0"),
            (0x53, "p:5,6 3.6,7 1.4,7 0,5.8 0,4.6 1.4,3.6 3.6,3.4 5,2.4 5,1.2 3.6,0 1.4,0 0,1"),
            (0x54, "p:0,7 5,7;p:2.5,7 2.5,0"),
            (0x55, UpperU),
            (0x56, "p:0,7 2.5,0 5,7"),
            (0x57, "p:0,7 0.7,0 2.5,4.2 4.3,0 5,7"),
            (0x58, "p:0,0 5,7;p:0,7 5,0"),
            (0x59, "p:0,7 2.5,3.4 5,7;p:2.5,3.4 2.5,0"),
            (0x5A, "p:0,7 5,7 0,0 5,0"),
            (0x5B, "-p:3.4,7.6 1.8,7.6 1.8,-0.6 3.4,-0.6"),
            (0x5C, "p:0,7 5,0"),
            (0x5D, "-p:1.6,7.6 3.2,7.6 3.2,-0.6 1.6,-0.6"),
            (0x5E, "p:0.8,5.4 2.5,7 4.2,5.4"),
            (0x5F, "p:0,-1.3 5,-1.3"),
            (0x60, "-p:1.6,7 3,5.9"),
            (0x61, LowerA),
            (0x62, "p:0,7 0,0;p:0,4 1.4,5 3.2,5 4.6,3.8 4.6,1.2 3.2,0 1.4,0 0,1"),
            (0x63, "p:4.6,4.2 3.4,5 1.4,5 0,3.8 0,1.2 1.4,0 3.4,0 4.6,0.8"),
            (0x64, "p:4.6,7 4.6,0;p:4.6,4 3.2,5 1.4,5 0,3.8 0,1.2 1.4,0 3.2,0 4.6,1"),
            (0x65, LowerE),
            (0x66, "p:3.7,7 2.2,7 1.3,5.9 1.3,0;p:0.3,5 3.2,5"),
            (0x67, "p:4.6,5 4.6,-0.7 3.2,-1.9 1.1,-1.7;p:4.6,4 3.2,5 1.4,5 0,3.8 0,1.2 1.4,0 3.2,0 4.6,1"),
            (0x68, "p:0,7 0,0;p:0,4 1.4,5 3.2,5 4.6,3.8 4.6,0"),
            (0x69, "p:2.4,5 2.4,0;d:2.4,6.5,0.5"),
            (0x6A, "p:3.5,5 3.5,-0.7 2.3,-1.9 0.8,-1.7;d:3.5,6.5,0.5"),
            (0x6B, "p:0,7 0,0;p:0,1.9 4.2,5;p:1.6,2.7 4.4,0"),
            (0x6C, "p:2.4,7 2.4,0"),
            (0x6D, "p:0,5 0,0;p:0,4.2 1,5 1.9,5 2.8,4.2 2.8,0;p:2.8,4.2 3.8,5 4.7,5 5,4.2 5,0"),
            (0x6E, LowerN),
            (0x6F, LowerO),
            (0x70, "p:0,5 0,-2;p:0,4 1.4,5 3.2,5 4.6,3.8 4.6,1.2 3.2,0 1.4,0 0,1"),
            (0x71, "p:4.6,5 4.6,-2;p:4.6,4 3.2,5 1.4,5 0,3.8 0,1.2 1.4,0 3.2,0 4.6,1"),
            (0x72, "p:0,5 0,0;p:0,4.2 1.6,5 3.4,5 4.6,4"),
            (0x73, "p:4.6,4.2 3.4,5 1.4,5 0,4.2 0,3.4 1.4,2.7 3.2,2.5 4.6,1.7 4.6,0.9 3.2,0 1.2,0 0,0.8"),
            (0x74, "p:1.6,7 1.6,1 2.8,0 4.4,0 4.9,0.4;p:0.2,5 3.6,5"),
            (0x75, LowerU),
            (0x76, "p:0,5 2.3,0 4.6,5"),
            (0x77, "p:0,5 0.7,0 2.4,3.6 4.1,0 4.8,5"),
            (0x78, "p:0,0 4.6,5;p:0,5 4.6,0"),
            (0x79, "p:0,5 2.3,0.8 4.6,5;p:2.3,0.8 1.2,-1.2 0.2,-1.8"),
            (0x7A, "p:0,5 4.6,5 0,0 4.6,0"),
            (0x7B, "-p:3.6,7.6 2.4,7.6 1.7,6.7 1.7,4.2 0.6,3.5 1.7,2.7 1.7,0.2 2.4,-0.6 3.6,-0.6"),
            (0x7C, "-p:2.4,7.8 2.4,-1.1"),
            (0x7D, "-p:1.4,7.6 2.6,7.6 3.3,6.7 3.3,4.2 4.4,3.5 3.3,2.7 3.3,0.2 2.6,-0.6 1.4,-0.6"),
            (0x7E, "-p:0.4,3.4 1.4,4.5 3.4,2.5 4.4,3.6"),

            // ————— Signos y letras del HUD (medidos sobre el código) —————
            (0xA1, "d:2.4,5.4,0.5;p:2.4,3.7 2.4,0"),                       // ¡
            (0xA7, "p:4.6,4.9 3.3,5.7 1.5,5.7 0.5,4.7 0.5,4 1.7,3.3 3.4,2.9 4.6,2.1 4.6,1 3.5,0.2 1.6,0.2 0.4,1;-p:1.8,5.9 1.8,7.3;-p:3.2,2.9 3.2,1.4"), // §
            (0xAA, "p:3.2,5.7 3.2,3.4;p:3.2,5 2.3,5.7 1.4,5.7 0.6,4.9 0.6,4.1 1.4,3.4 2.3,3.4 3.2,4.1;-p:0,2.4 3.9,2.4"), // ª
            (0xAB, "p:2.6,5.1 0.6,3.4 2.6,1.7;p:4.6,5.1 2.6,3.4 4.6,1.7"),  // «
            (0xB2, "p:0.8,7.7 1.8,8.6 3.1,8.6 4.2,7.7 4.2,7.1 0.8,3.9 4.3,3.9"), // ²
            (0xB7, "d:2.5,3.1,0.55"),                                      // ·
            (0xBA, "p:0.9,4.1 0.9,5.5 1.8,5.9 2.8,5.5 2.8,4.1 1.8,3.7 0.9,4.1;p:4.6,4.1 4.6,5.5 3.7,5.9 2.8,5.5 2.8,4.1 3.7,3.7 4.6,4.1"), // º
            (0xBB, "p:0.4,5.1 2.4,3.4 0.4,1.7;p:2.4,5.1 4.4,3.4 2.4,1.7"),  // »
            (0xBF, "p:0,1.3 1.4,0 3.4,0 4.8,1.3 4.8,2.1 2.4,3.7 2.4,4.7;p:2.4,5.2 2.4,5.5"), // ¿
            (0xD7, "p:0.7,6.3 4.3,0.5;p:0.7,0.5 4.3,6.3"),                 // ×
            (0x2013, "p:0.6,3.4 4.4,3.4"),                                 // –
            (0x2014, "p:0.1,3.4 4.9,3.4"),                                 // —
            (0x2020, "p:2.4,7 2.4,-0.9;p:0.6,5.3 4.2,5.3"),                // †
            (0x2026, "d:0.6,0.5,0.5;d:2.5,0.5,0.5;d:4.4,0.5,0.5"),         // …
            (0x2192, "p:0,3.5 4.4,3.5;p:2.9,5.3 4.7,3.5 2.9,1.7"),         // →
            (0x21BB, "e:2.5,3.4,1.9,1.9;p:3.0,5.7 4.5,4.6 3.1,3.5"),       // ↻
            (0x2212, "p:0.4,3.4 4.6,3.4"),                                 // −
            (0x2248, "p:0.4,4.7 1.6,5.2 2.8,4.5 4.6,5.0;p:0.4,2.7 1.6,3.2 2.8,2.5 4.6,3.0"), // ≈
            (0x23F8, "+p:1.7,5.6 1.7,1.2;+p:3.3,5.6 3.3,1.2"),             // ⏸
            (0x2500, "p:-1.0,3.4 6.0,3.4"),                                // ─ (regla a sangre)
            (0x25A0, "b:2.5,3.4,1.5,1.5"),                                 // ■
            (0x25B6, "t:1.5,5.5 1.5,1.3 4.5,3.4"),                         // ▶
            (0x25CB, "e:2.5,3.4,1.7,1.7"),                                 // ○
            (0x25CF, "d:2.5,3.4,1.7"),                                     // ●
            (0x25D0, "e:2.5,3.4,1.7,1.7;h:2.5,3.4,1.55"),                  // ◐
            (0x2713, "p:0.5,3.6 2.0,1.2 4.6,6.2"),                         // ✓
            (0x2717, "p:0.8,5.8 4.2,1.2;p:0.8,1.2 4.2,5.8"),               // ✗
            (0x2B07, "p:2.5,7 2.5,0.7;p:0.7,2.5 2.5,0.6 4.3,2.5"),         // ⬇
            (0x3B1, "p:3.7,5 4.7,1.3;p:3.7,5 1.7,5 0.4,3.6 0.4,1.4 1.7,0 3.4,0 4.7,1.3"), // α

            // ————— Castellano: acentos compuestos sobre las bases —————
            (0xC1, UpperA + ";" + AcuteCap), (0xC9, UpperE + ";" + AcuteCap),
            (0xCD, UpperI + ";" + AcuteCap), (0xD1, "p:0,0 0,7 5,0 5,7;" + TildeCap),
            (0xD3, UpperO + ";" + AcuteCap), (0xDA, UpperU + ";" + AcuteCap),
            (0xDC, UpperU + ";" + DotsCap),
            (0xE1, LowerA + ";" + AcuteLow), (0xE9, LowerE + ";" + AcuteLow),
            (0xED, "p:2.4,5 2.4,0;" + AcuteLow),
            (0xF1, LowerN + ";" + TildeLow), (0xF3, LowerO + ";" + AcuteLow),
            (0xFA, LowerU + ";" + AcuteLow), (0xFC, LowerU + ";" + DotsLow),

            // ————— Iconos del juego (hormiga, carga, huevo) —————
            (AntGlyph, AntIcon), (LoadGlyph, LoadIcon), (EggGlyph, EggIcon),
        };

        private static readonly Dictionary<int, Stamp[]> _glyphs = Build();
        private static readonly int[] _codepoints = CodepointsIn(_glyphs);
        private static readonly HashSet<int> _covered = new(_glyphs.Keys);

        /// <summary>Códigos cubiertos, en orden ascendente (el orden de la cmap).</summary>
        public static IReadOnlyList<int> Codepoints => _codepoints;

        /// <summary>¿La fuente tiene glifo para este carácter? Un carácter sin glifo
        /// lo pinta uGUI como un hueco: por eso la cobertura se comprueba.</summary>
        public static bool Covers(int codepoint) => _covered.Contains(codepoint);

        /// <summary>Avance del glifo en unidades de la em (el icono respira más).</summary>
        public static int AdvanceOf(int codepoint) => IsIcon(codepoint) ? IconAdvance : Advance;

        public static bool IsIcon(int codepoint)
            => codepoint == AntGlyph || codepoint == LoadGlyph || codepoint == EggGlyph;

        /// <summary>Los polígonos convexos del glifo (la celda de la letra).</summary>
        public static IReadOnlyList<Stamp> Stamps(int codepoint)
            => _glyphs.TryGetValue(codepoint, out var s) ? s : Array.Empty<Stamp>();

        private static Dictionary<int, Stamp[]> Build()
        {
            var map = new Dictionary<int, Stamp[]>(Table.Length);
            foreach (var (cp, dsl) in Table)
            {
                if (map.ContainsKey(cp))
                    throw new InvalidOperationException($"glifo duplicado U+{cp:X4}");
                map[cp] = Parse(dsl);
            }
            return map;
        }

        private static int[] CodepointsIn(Dictionary<int, Stamp[]> map)
        {
            var list = new List<int>(map.Keys);
            list.Sort();
            return list.ToArray();
        }

        /// <summary>
        /// El parser de la gramática de glifos. Puro y total: cualquier glifo
        /// mal escrito lanza con el token delante en vez de dibujarse a medias.
        /// </summary>
        internal static Stamp[] Parse(string dsl)
        {
            var stamps = new List<Stamp>();
            foreach (string raw in dsl.Split(';'))
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;

                float width = Pen;
                if (token[0] == '-') { width *= 0.55f; token = token.Substring(1); }
                else if (token[0] == '+') { width *= 1.8f; token = token.Substring(1); }

                int colon = token.IndexOf(':');
                if (colon <= 0)
                    throw new FormatException($"glifo sin tipo: «{raw.Trim()}»");
                string kind = token.Substring(0, colon);
                string body = token.Substring(colon + 1);

                switch (kind)
                {
                    case "p":
                        Shapes.Stroke(stamps, width, Points(body));
                        break;
                    case "e":
                    {
                        var f = Floats(body, 4);
                        // 12 puntos por anillo: con 10 el octógono se notaba a 24 px.
                        Shapes.Loop(stamps, width,
                            Shapes.EllipsePoints(Fx(f[0]), Fy(f[1]), R(f[2]), R(f[3]), 12));
                        break;
                    }
                    case "d":
                    {
                        var f = Floats(body, 3);
                        stamps.Add(Shapes.Disc(Fx(f[0]), Fy(f[1]), R(f[2]), 10));
                        break;
                    }
                    case "o":
                    {
                        var f = Floats(body, 4);
                        stamps.Add(Shapes.Ellipse(Fx(f[0]), Fy(f[1]), R(f[2]), R(f[3]), 14));
                        break;
                    }
                    case "b":
                    {
                        var f = Floats(body, 4);
                        stamps.Add(Shapes.Box(Fx(f[0]), Fy(f[1]), R(f[2]), f[3] * CellY));
                        break;
                    }
                    case "t":
                    {
                        var p = Points(body);
                        if (p.Count != 3)
                            throw new FormatException($"un triángulo necesita 3 puntos: «{body}»");
                        stamps.Add(Shapes.Triangle(p[0], p[1], p[2]));
                        break;
                    }
                    case "h":
                    {
                        var f = Floats(body, 3);
                        stamps.Add(HalfDisc(Fx(f[0]), Fy(f[1]), R(f[2])));
                        break;
                    }
                    default:
                        throw new FormatException($"glifo desconocido: «{kind}»");
                }
            }
            return stamps.ToArray();
        }

        /// <summary>Media luna IZQUIERDA llena (◐): semicírculo + su diámetro.
        /// Es convexa, así que vale como stamp para los dos consumidores.</summary>
        private static Stamp HalfDisc(float cx, float cy, float r)
        {
            const int n = 6;
            var pts = new List<V2>(n + 3);
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * 0.5 + Math.PI * i / n; // de 90° a 270°, por la izquierda
                pts.Add(new V2(cx + (float)Math.Cos(a) * r, cy + (float)Math.Sin(a) * r));
            }
            return new Stamp(pts.ToArray());
        }

        // — Rejilla de la celda → unidades de la em ————————————————
        private static float Fx(float col) => OriginX + col * CellX;
        private static float Fy(float row) => row * CellY;
        /// <summary>Radio: SIEMPRE con la escala de x, para que un disco sea un círculo.</summary>
        private static float R(float r) => r * CellX;

        private static List<V2> Points(string body)
        {
            var pts = new List<V2>();
            foreach (string pair in body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = pair.Split(',');
                if (xy.Length != 2) throw new FormatException($"punto inválido: «{pair}»");
                pts.Add(new V2(Fx(Num(xy[0])), Fy(Num(xy[1]))));
            }
            if (pts.Count == 0) throw new FormatException("trazo sin puntos");
            return pts;
        }

        private static float[] Floats(string body, int expected)
        {
            var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expected)
                throw new FormatException($"se esperaban {expected} números en «{body}»");
            var f = new float[expected];
            for (int i = 0; i < expected; i++) f[i] = Num(parts[i]);
            return f;
        }

        private static float Num(string s)
            => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
