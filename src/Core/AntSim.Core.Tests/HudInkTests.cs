using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — MEDICIÓN del chip de runway EN PANTALLA (sonda del player). El modelo
    /// del chip ya dice qué texto y qué color tocan; esto mide que eso LLEGUE a los
    /// píxeles: la banda de cada línea y su ancho real, el color del tramo por
    /// píxel, y que el texto no invada la barra de reserva.
    ///
    /// Todos los casos corren sobre capturas SINTÉTICAS —un buffer RGB con las
    /// formas pintadas a mano—, así que la suite prueba exactamente lo que la sonda
    /// del player va a medir en el build, sin un editor ni una GPU de por medio.
    /// </summary>
    public sealed class HudInkTests
    {
        // ————— capturas sintéticas —————

        /// <summary>Un «frame» mínimo: buffer RGB con las formas que pinta el test.
        /// Mismo orden de filas que `ReadPixels` (fila 0 = abajo).</summary>
        private sealed class Marco
        {
            public readonly int W;
            public readonly int H;
            public readonly byte[] Buf;

            public Marco(int w, int h, Rgb fondo)
            {
                W = w; H = h;
                Buf = new byte[w * h * 3];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Pintar(x, y, fondo);
            }

            public void Pintar(int x, int y, Rgb c)
            {
                int i = (y * W + x) * 3;
                Buf[i] = c.R; Buf[i + 1] = c.G; Buf[i + 2] = c.B;
            }

            public void Rect(int x, int y, int ancho, int alto, Rgb c)
            {
                for (int j = y; j < y + alto; j++)
                    for (int i = x; i < x + ancho; i++)
                        Pintar(i, j, c);
            }

            public Rgb Pixel(int x, int y)
            {
                int i = (y * W + x) * 3;
                return new Rgb(Buf[i], Buf[i + 1], Buf[i + 2]);
            }

            public PixelRect Area => new PixelRect(0, 0, W, H);
        }

        private static readonly Rgb Fondo = new Rgb(19, 19, 23);      // panel oscuro
        private static readonly Rgb Tinta = new Rgb(237, 232, 219);   // Ink del HUD
        private static readonly Rgb Reserva = new Rgb(115, 204, 102); // verde de la barra

        /// <summary>La tinta TAL COMO SE MIDE: sobre el fondo medido del panel. El
        /// modelo no compara con el color declarado, mide cuánto de él hay en el
        /// píxel (los glifos finos no llegan al color puro).</summary>
        private static readonly PaintTarget TintaSobrePanel = new PaintTarget(Fondo, Tinta);

        /// <summary>Un píxel a la cobertura dada del color objetivo sobre el panel:
        /// es el modelo exacto de lo que pinta el rasterizador de fuentes.</summary>
        private static Rgb Mezcla(Rgb color, float cobertura)
            => new Rgb(
                (byte)System.Math.Round(Fondo.R + cobertura * (color.R - Fondo.R)),
                (byte)System.Math.Round(Fondo.G + cobertura * (color.G - Fondo.G)),
                (byte)System.Math.Round(Fondo.B + cobertura * (color.B - Fondo.B)));

        // ————— la paleta no se repite: sale del modelo del chip —————

        [Fact]
        public void Paleta_EsLaDelModeloDelChip()
        {
            Assert.Equal(new Rgb(224, 58, 58), HudInkModel.Palette(1));   // #E03A3A crítico
            Assert.Equal(new Rgb(224, 122, 42), HudInkModel.Palette(2));  // #E07A2A caníbal
            Assert.Equal(new Rgb(216, 176, 42), HudInkModel.Palette(3));  // #D8B02A oofagia
            Assert.Equal(new Rgb(126, 208, 126), HudInkModel.Palette(4)); // #7ED07E reserva
            Assert.Equal(new Rgb(76, 175, 80), HudInkModel.Palette(5));   // #4CAF50 desahogo
            // Y sigue a RunwayChipModel: si el chip cambia de color, la medición lo
            // sigue sin que nadie toque una segunda tabla.
            foreach (var s in HudInkModel.PaletteStates)
                Assert.Equal(RunwayChipModel.ColorHex(s), "#" + HudInkModel.Palette(s).R.ToString("X2")
                    + HudInkModel.Palette(s).G.ToString("X2") + HudInkModel.Palette(s).B.ToString("X2"));
        }

        [Fact]
        public void Hex_MalFormado_DevuelveNegro()
        {
            // La paleta es total (el chip nunca emite color sin valor): un hex roto
            // no puede hacer estallar la medición.
            Assert.Equal(new Rgb(0, 0, 0), HudInkModel.ParseHex("no-es-un-hex"));
            Assert.Equal(new Rgb(0, 0, 0), HudInkModel.ParseHex("#GGGGGG"));
        }

        // ————— líneas: una banda por línea y su ancho REAL —————

        [Fact]
        public void Lineas_UnaBandaPorLinea_DeArribaAbajo_YConSuAnchoReal()
        {
            // Dos líneas de anchos distintos: la de arriba es más larga. El ancho
            // que se publica es el pintado (no el del rect), que es la pregunta.
            var m = new Marco(200, 60, Fondo);
            m.Rect(10, 40, 150, 8, Tinta);   // línea de arriba (y=40): 150 px
            m.Rect(10, 20, 100, 8, Tinta);   // línea de abajo (y=20): 100 px

            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx);

            Assert.Equal(2, lineas.Count);
            Assert.Equal(150, lineas[0].Width);
            Assert.Equal(100, lineas[1].Width);
            Assert.True(lineas[0].Y0 > lineas[1].Y0, "la primera banda es la de ARRIBA");
        }

        [Fact]
        public void Lineas_UnPixelSuelto_NoAbreBanda()
        {
            // Un píxel de antialiasing no es una línea: sin el umbral por fila, una
            // mota de ruido contaría como línea extra y el veredicto diría «envuelve».
            var m = new Marco(80, 40, Fondo);
            m.Rect(10, 20, 40, 6, Tinta);
            m.Pintar(60, 10, Tinta);

            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx);

            Assert.Single(lineas);
        }

        [Fact]
        public void Tinta_GlifoFino_NoLlegaAlColorPuro_YAunAsiCuentaComoLinea()
        {
            // DEFECTO MEDIDO EN EL BUILD (2026-09-20, sonda del player sobre el player
            // empaquetado): en la MISMA captura y con el MISMO texto, los glifos de una
            // columna llegaron con el núcleo al 96 % de su color declarado y los de la
            // otra al 84 % (el atlas de la fuente dinámica los rasteriza distinto). La
            // medida exigía 26 de tolerancia al color declarado y declaraba «líneas sin
            // pintar» la tarjeta del 84 %: un texto perfectamente legible dado por
            // roto. Lo que se mide es la COBERTURA.
            var m = new Marco(300, 40, Fondo);
            m.Rect(10, 28, 120, 8, Mezcla(Tinta, 0.84f));   // la línea «apagada»
            m.Rect(10, 12, 120, 8, Mezcla(Tinta, 0.96f));   // la línea «viva»

            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx);

            Assert.Equal(2, lineas.Count);
            Assert.Equal(120, lineas[0].Width);
            Assert.Equal(120, lineas[1].Width);
            // Y las dos cuentan como tinta: el ancho no sale del brillo, sale del
            // segmento fondo→color.
            Assert.Equal(120 * 8, lineas[0].InkPx);
            Assert.Equal(120 * 8, lineas[1].InkPx);
        }

        [Fact]
        public void Tinta_LoQueBrillaPeroNoEsSuColor_NoEsLinea()
        {
            // La otra cara de la cobertura: la barra de hongo (209,158,76) está BRILLANTE
            // sobre el panel (α 0,57 hacia la tinta) pero no está sobre el segmento
            // fondo→tinta (residuo 83, medido). Por brillo colaría como texto; por
            // cobertura no.
            var m = new Marco(200, 30, Fondo);
            m.Rect(10, 8, 80, 10, new Rgb(209, 158, 76));

            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx);

            Assert.Empty(lineas);
            var cobertura = HudInkModel.Cover(new Rgb(209, 158, 76), Fondo, Tinta);
            Assert.True(cobertura.Residual > HudInkModel.InkResidual, $"{cobertura}");
        }

        [Fact]
        public void Fondo_EsElColorMasFrecuenteDeLaVentana()
        {
            // El fondo de la tarjeta se MIDE (el panel semiopaco deja ver el mundo
            // debajo): la cobertura publicada tiene que ser comparable entre tarjetas,
            // y para eso el fondo no puede suponerse.
            var m = new Marco(60, 20, Fondo);
            m.Rect(40, 2, 18, 16, new Rgb(200, 40, 40));   // un bloque rojo minoritario

            Assert.Equal(Fondo, HudInkModel.Background(m.Buf, m.W, m.H, m.Area));
            Assert.Equal(new Rgb(200, 40, 40),
                HudInkModel.Background(m.Buf, m.W, m.H, new PixelRect(40, 2, 18, 16)));
        }

        // ————— chips: clasificación por COBERTURA sobre el fondo —————

        [Fact]
        public void Chips_ColorExacto_CoberturaUno()
        {
            var m = new Marco(100, 20, Fondo);
            m.Rect(10, 5, 20, 8, HudInkModel.Palette(5));   // 160 px de verde desahogo

            var chips = HudInkModel.Chips(m.Buf, m.W, m.H, m.Area, Fondo);

            var verde = Buscar(chips, 5);
            Assert.Equal(160, verde.Px);
            Assert.Equal(1f, verde.Cover, 3);
            Assert.Equal(HudInkModel.Palette(5), verde.Mean);
            foreach (var s in HudInkModel.PaletteStates)
                if (s != 5) Assert.Equal(0, Buscar(chips, s).Px);
        }

        [Fact]
        public void Chips_MediaCobertura_Cuenta_YPublicaSuCobertura()
        {
            // Lo que el rasterizador de fuentes entrega NO es el color puro: el núcleo
            // de un glifo fino llega a la pantalla al 50-90 %. Medido en el build: el
            // chip del mismo texto dio 0,74 en una columna y 0,90 en la otra. Con el
            // color exacto como exigencia, el chip del 74 % se daba por ausente.
            var m = new Marco(60, 16, Fondo);
            m.Rect(10, 4, 10, 6, Mezcla(HudInkModel.Palette(5), 0.5f));   // al 50 % del color

            var verde = Buscar(HudInkModel.Chips(m.Buf, m.W, m.H, m.Area, Fondo), 5);

            Assert.Equal(60, verde.Px);
            Assert.Equal(0.5f, verde.Cover, 2);
            // Y la cobertura es la publicada: es el número que dice si el color del
            // tramo está de verdad en pantalla.
            var medida = HudInkModel.Judge("vista 0 · colonia 0", new List<byte> { 5 }, 1,
                UnaLinea(120), new List<ChipPixels> { verde }, availableWidth: 400,
                stagingInkPx: 0, barInkPx: 0, bars: SinBarras);
            Assert.True(medida.Ok, medida.Verdict);
            Assert.Contains("0.5", medida.Verdict);
        }

        [Fact]
        public void Chips_PorDebajoDeLaCoberturaMinima_NoEsChip()
        {
            // Por debajo del mínimo no hay glifo: hay fondo con ruido. Medido: el ruido
            // que pasaba el residuo se quedaba en 0,31–0,40.
            var m = new Marco(40, 12, Fondo);
            m.Rect(5, 3, 10, 5, Mezcla(HudInkModel.Palette(4), 0.20f));

            var reserva = Buscar(HudInkModel.Chips(m.Buf, m.W, m.H, m.Area, Fondo), 4);
            Assert.Equal(0, reserva.Px);
        }

        [Fact]
        public void Chips_ElVerdeDeLaBarra_NoEsDeLaPaleta_PorCobertura_YSigueExcluido()
        {
            // TRAMPA MEDIDA: el verde de la barra de reserva (0.45·0.8·0.4 = 115,204,102)
            // está a 27 del verde «reserva» de la paleta (126,208,126): por COLOR son
            // casi el mismo. Por COBERTURA no lo son —la barra obliga a tres alfas
            // incompatibles—, así que el modelo la descarta por residuo; y la vista
            // además la excluye por geometría. Dos cinturones, y el residuo medido
            // (19) frente al límite (8) deja claro que el segundo hace falta.
            var m = new Marco(120, 40, Fondo);
            m.Rect(10, 4, 100, 8, Reserva);              // la barra, abajo
            m.Rect(10, 24, 60, 8, Tinta);                // «colonia 0», arriba
            m.Rect(80, 24, 20, 8, HudInkModel.Palette(3)); // su chip de OOFAGIA

            int d = HudInkModel.Distance(Reserva, HudInkModel.Palette(4));
            Assert.Equal(27, d);
            var cobertura = HudInkModel.Cover(Reserva, Fondo, HudInkModel.Palette(4));
            Assert.True(cobertura.Residual > HudInkModel.ChipResidual,
                $"el verde de la barra tiene que quedar fuera por residuo: {cobertura}");

            var aLoAncho = HudInkModel.Chips(m.Buf, m.W, m.H, m.Area, Fondo);
            Assert.Equal(0, Buscar(aLoAncho, 4).Px);   // la barra no vota aunque se cuente a lo ancho

            // La vista sabe dónde está su barra, así que la declara EXCLUIDA: ni abre
            // banda ni vota. Entonces la línea es la del texto y el único chip es el real.
            var barras = new List<PixelRect> { new PixelRect(10, 4, 100, 8) };
            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx, barras);
            var dentro = HudInkModel.ChipsInLines(m.Buf, m.W, m.H, lineas,
                Fondo, barras);

            Assert.Single(lineas);
            Assert.Equal(90, lineas[0].Width);            // 10..99: texto + chip, sin barra
            Assert.Equal(0, Buscar(dentro, 4).Px);        // la barra queda fuera
            Assert.Equal(20 * 8, Buscar(dentro, 3).Px);   // y el chip de verdad dentro
        }

        [Fact]
        public void Lineas_ElChipCuentaParaElAnchoDeLaLinea()
        {
            // El chip va en OTRO color que el texto: si la banda sólo mirara el color
            // del texto, el ancho real de la línea del título saldría corto justo por
            // lo que ocupa el chip — el número que la medición publica.
            var m = new Marco(200, 40, Fondo);
            m.Rect(10, 20, 60, 8, Tinta);                    // texto
            m.Rect(80, 20, 20, 8, HudInkModel.Palette(5));   // chip, separado del texto

            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx);

            Assert.Single(lineas);
            Assert.Equal(90, lineas[0].Width);          // 10..99: texto + hueco + chip
            Assert.Equal(60 * 8, lineas[0].InkPx);
            Assert.Equal(20 * 8, lineas[0].ChipPx);
        }

        // ————— el veredicto, caso a caso —————

        private static List<InkLine> UnaLinea(int ancho, int x = 10, int y = 60)
            => new List<InkLine> { new InkLine(x, y, x + ancho - 1, y + 8, ancho * 8) };

        private static List<ChipPixels> Chips(params (byte estado, int px)[] pintados)
        {
            var list = new List<ChipPixels>();
            for (byte s = 1; s <= 5; s++)
            {
                int px = 0;
                foreach (var p in pintados) if (p.estado == s) px = p.px;
                list.Add(new ChipPixels(s, px, px > 0 ? 1f : 0f, HudInkModel.Palette(s)));
            }
            return list;
        }

        private static readonly List<PixelRect> SinBarras = new();

        [Fact]
        public void Veredicto_Verde_ReflejaLasTresMedidas()
        {
            var m = HudInkModel.Judge("vista 0 · colonia 1",
                new List<byte> { 4 }, authoredLines: 1,
                lines: UnaLinea(240), chips: Chips((4, 41)),
                availableWidth: 586, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);

            Assert.True(m.Ok, m.Verdict);
            Assert.Equal(240, m.TitleWidth);
            Assert.Equal(346, m.TitleHeadroom);
            Assert.Contains("RESERVA", m.Verdict);
            Assert.Contains("#7ED07E", m.Verdict);
            Assert.Contains("41px", m.Verdict);
        }

        [Fact]
        public void Veredicto_ChipOrdenado_QueNoEstaEnPantalla()
        {
            var m = HudInkModel.Judge("colonia 0", new List<byte> { 1 }, 1,
                UnaLinea(200), Chips(), availableWidth: 400, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);

            Assert.False(m.Ok);
            Assert.Contains("CRÍTICO", m.Verdict);
            Assert.Contains("no está en pantalla", m.Verdict);
        }

        [Fact]
        public void Veredicto_ColorDeOtroTramo()
        {
            // El Core ordena RESERVA y en pantalla hay DESAHOGO: es el caso del color
            // que se quedó de un frame anterior, y cuenta como chip ausente Y como
            // color inesperado. El primero que salta es el ausente.
            var m = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, 1,
                UnaLinea(200), Chips((5, 50)), availableWidth: 400, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);

            Assert.False(m.Ok);
            Assert.Contains("RESERVA", m.Verdict);
        }

        [Fact]
        public void Veredicto_ColorQueElCoreNoOrdeno()
        {
            var m = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, 1,
                UnaLinea(200), Chips((4, 50), (5, 50)), availableWidth: 400,
                stagingInkPx: 0, barInkPx: 0, bars: SinBarras);

            Assert.False(m.Ok);
            Assert.Contains("no lo ordenó", m.Verdict);
        }

        [Fact]
        public void Veredicto_DosColoniasDelMismoTramo_NecesitanLosDosChips()
        {
            // La tarjeta compacta lleva una línea por colonia: dos colonias en
            // RESERVA necesitan DOS chips pintados, no uno.
            var chips = Chips((4, 8));   // 8 px: la mitad de un chip, no dos
            var m = HudInkModel.Judge("vista 0", new List<byte> { 4, 4 }, 2,
                new List<InkLine> { new InkLine(10, 60, 209, 68, 1600), new InkLine(10, 40, 209, 48, 1600) },
                chips, availableWidth: 400, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);

            Assert.False(m.Ok);
            Assert.Contains("2 colonias en RESERVA", m.Verdict);

            var ok = HudInkModel.Judge("vista 0", new List<byte> { 4, 4 }, 2,
                new List<InkLine> { new InkLine(10, 60, 209, 68, 1600), new InkLine(10, 40, 209, 48, 1600) },
                Chips((4, 80)), availableWidth: 400, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);
            Assert.True(ok.Ok, ok.Verdict);
        }

        [Fact]
        public void Veredicto_LineaQueDesbordaElAnchoReservado()
        {
            var m = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, 1,
                UnaLinea(420), Chips((4, 50)), availableWidth: 405,
                stagingInkPx: 0, barInkPx: 0, bars: SinBarras);

            Assert.False(m.Ok);
            Assert.Contains("desborda", m.Verdict);
            Assert.Contains("420", m.Verdict);
        }

        [Fact]
        public void Veredicto_EnvuelveYLineasSinPintar()
        {
            var envuelve = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, authoredLines: 1,
                new List<InkLine> { new InkLine(10, 60, 209, 68, 800), new InkLine(10, 40, 109, 48, 400) },
                Chips((4, 50)), availableWidth: 400, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);
            Assert.False(envuelve.Ok);
            Assert.Contains("envuelve", envuelve.Verdict);

            var perdida = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, authoredLines: 5,
                UnaLinea(200), Chips((4, 50)), availableWidth: 400, stagingInkPx: 0, barInkPx: 0, bars: SinBarras);
            Assert.False(perdida.Ok);
            Assert.Contains("líneas sin pintar", perdida.Verdict);
        }

        [Fact]
        public void Veredicto_TextoEnLaFranjaDeLaBarra_YDentroDeLaBarra()
        {
            var franja = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, 1,
                UnaLinea(200), Chips((4, 50)), availableWidth: 400,
                stagingInkPx: 24, barInkPx: 0, bars: SinBarras);
            Assert.False(franja.Ok);
            Assert.Contains("invade la barra", franja.Verdict);

            var dentro = HudInkModel.Judge("colonia 0", new List<byte> { 4 }, 1,
                UnaLinea(200), Chips((4, 50)), availableWidth: 400,
                stagingInkPx: 0, barInkPx: 9, bars: SinBarras);
            Assert.False(dentro.Ok);
            Assert.Contains("dentro de la barra", dentro.Verdict);
        }

        // ————— la separación hasta la barra, en las dos formas del HUD —————

        [Fact]
        public void Separacion_BarraDebajo_SeMideEnVertical()
        {
            // El HUD grande pone la barra bajo el texto: hay que decir cuánto aire
            // queda entre la línea más baja y la barra.
            var lineas = new List<InkLine> { new InkLine(30, 100, 229, 108, 1600) };
            var barras = new List<PixelRect> { new PixelRect(20, 14, 406, 12) };
            // La barra ocupa de la fila 14 a la 25; la línea más baja empieza en la 100.
            Assert.Equal(74, HudInkModel.BarGap(lineas, barras));
        }

        [Fact]
        public void Separacion_BarraALaDerechaOLaIzquierda_SeMideEnHorizontal()
        {
            // La tarjeta compacta del multi-visor pone las barras a la IZQUIERDA del
            // texto (franjas verticales): la misma regla tiene que medir el hueco en
            // horizontal, no en vertical.
            var lineas = new List<InkLine> { new InkLine(22, 40, 321, 52, 3600) };
            var barras = new List<PixelRect> { new PixelRect(8, 6, 3, 54) };
            Assert.Equal(11, HudInkModel.BarGap(lineas, barras));
        }

        [Fact]
        public void Separacion_SiSePisan_EsCero()
        {
            var lineas = new List<InkLine> { new InkLine(10, 14, 300, 22, 1600) };
            var barras = new List<PixelRect> { new PixelRect(20, 14, 406, 12) };
            Assert.Equal(0, HudInkModel.BarGap(lineas, barras));
        }

        // ————— geometría: de la pantalla a píxeles de la captura —————

        [Fact]
        public void Rect_DePantallaACaptura_RespetaLaEscalaYRecorta()
        {
            // El editor puede capturar a otra resolución que la ventana: suponer la
            // escala desplazaría TODAS las ventanas de medida.
            var r = PixelRect.FromScreen(10f, 20f, 100f, 50f, 2f, 1280, 720);
            Assert.Equal(new PixelRect(20, 40, 200, 100).ToString(), r.ToString());

            // Y nada fuera de la captura: una ventana recortada se leería como «sin
            // tinta» y daría un verde falso.
            var fuera = PixelRect.FromScreen(1200f, 700f, 200f, 100f, 1f, 1280, 720);
            Assert.Equal(1280, fuera.X1);
            Assert.Equal(720, fuera.Y1);
        }

        [Fact]
        public void Rect_InfladoYEntreBordes()
        {
            var r = new PixelRect(100, 100, 50, 20).Inflated(4, 1000, 1000);
            Assert.Equal(new PixelRect(96, 96, 58, 28).ToString(), r.ToString());
            var entre = PixelRect.Between(50, 200, 150, 120, 1000, 1000);
            Assert.Equal(new PixelRect(50, 120, 100, 80).ToString(), entre.ToString());
        }

        // ————— el caso completo, de una captura sintética al veredicto —————

        [Fact]
        public void TarjetaCompleta_DeLaCapturaAlVeredicto()
        {
            // Un frame con la forma real de la tarjeta compacta: la línea del título
            // con su chip en RESERVA, las barras de reserva a la izquierda y la
            // franja que el texto NO puede tocar entre las dos.
            var m = new Marco(400, 120, Fondo);
            m.Rect(8, 10, 3, 100, Reserva);                    // barra de reserva (franja)
            m.Rect(30, 80, 120, 9, Tinta);                     // «colonia 0 ●»
            m.Rect(160, 80, 30, 9, HudInkModel.Palette(4));    // el chip [+] 12.4s

            var barras = new List<PixelRect> { new PixelRect(8, 10, 3, 100) };
            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx, barras);
            var chips = HudInkModel.ChipsInLines(m.Buf, m.W, m.H, lineas, Fondo, barras);
            var franja = PixelRect.Between(12, 10, 30, 110, m.W, m.H);

            var medida = HudInkModel.Judge("vista 0 · colonia 0", new List<byte> { 4 }, 1,
                lineas, chips, availableWidth: 370,
                stagingInkPx: HudInkModel.InkPixels(m.Buf, m.W, m.H, franja, TintaSobrePanel),
                barInkPx: HudInkModel.InkPixels(m.Buf, m.W, m.H, barras[0], TintaSobrePanel),
                bars: barras);

            Assert.True(medida.Ok, medida.Verdict);
            // El ancho de la línea incluye el chip: 10..149 de texto + 160..189 de chip.
            Assert.Equal(160, medida.TitleWidth);
            Assert.Equal(0, medida.StagingInkPx);
            Assert.Equal(0, medida.BarInkPx);
            Assert.Equal(19, medida.BarGapPx);         // 30 − 11: el aire hasta la franja
        }

        [Fact]
        public void TarjetaCompleta_TextoQueEntraEnLaFranjaDeLaBarra_Suspende()
        {
            // La misma tarjeta con la línea del título pintada hasta la franja de la
            // barra (un desborde de ancho real): ahora el staging SÍ tiene tinta.
            var m = new Marco(400, 120, Fondo);
            m.Rect(8, 10, 3, 100, Reserva);
            m.Rect(20, 80, 120, 9, Tinta);

            var barras = new List<PixelRect> { new PixelRect(8, 10, 3, 100) };
            var lineas = HudInkModel.Lines(m.Buf, m.W, m.H, m.Area, TintaSobrePanel,
                HudInkModel.MinRowInkPx, barras);
            var chips = HudInkModel.ChipsInLines(m.Buf, m.W, m.H, lineas, Fondo, barras);
            var franja = PixelRect.Between(12, 10, 30, 110, m.W, m.H);

            var medida = HudInkModel.Judge("vista 0 · colonia 0", new List<byte>(), 1,
                lineas, chips, availableWidth: 370,
                stagingInkPx: HudInkModel.InkPixels(m.Buf, m.W, m.H, franja, TintaSobrePanel),
                barInkPx: HudInkModel.InkPixels(m.Buf, m.W, m.H, barras[0], TintaSobrePanel),
                bars: barras);

            Assert.False(medida.Ok);
            Assert.Contains("invade la barra", medida.Verdict);
            Assert.Equal(90, medida.StagingInkPx);   // 10 px por fila × 9 filas dentro de la franja
            Assert.Equal(9, medida.BarGapPx);        // la franja ya está pisada, la barra aún no
        }

        // ————— la expectativa vista desde el HUD: sus etiquetas de color —————

        [Fact]
        public void TramoDeclaradoPorElTexto_SaleDeSuEtiquetaDeColor()
        {
            // El chip escribe su color en rich text, así que la expectativa se lee del
            // PROPIO texto pintado: si el HUD escribiera el color de otro tramo, esto
            // lo delata sin mirar un píxel.
            var into = new List<byte>();
            HudInkModel.ChipsInText("<b>colonia 0  ●</b>  <color=#7ED07E>[+] 22.5s</color>", into);
            Assert.Equal(new List<byte> { 4 }, into);

            // Una línea por colonia (la tarjeta compacta): en el orden del texto.
            HudInkModel.ChipsInText(
                "<b>colonia 0 ●</b> <color=#4CAF50>[++] 200s</color>\n<b>colonia 1 ●</b> <color=#E03A3A>[!!!] 5s</color>", into);
            Assert.Equal(new List<byte> { 5, 1 }, into);

            // Sin chip (estado 0) no hay etiqueta que leer…
            HudInkModel.ChipsInText("<b>colonia 0 ●  24h 62%</b> 3rec/1desc", into);
            Assert.Empty(into);

            // …y una etiqueta de color ajena a la paleta tampoco es un chip.
            HudInkModel.ChipsInText("<color=#FF8800>alerta</color>", into);
            Assert.Empty(into);
            Assert.Equal(0, HudInkModel.StateOfHex("#FF8800"));
        }

        [Fact]
        public void MismosTramos_EsMulticonjunto()
        {
            Assert.True(HudInkModel.SameChips(new byte[] { 4, 4 }, new byte[] { 4, 4 }));
            Assert.True(HudInkModel.SameChips(new byte[] { 1, 4 }, new byte[] { 4, 1 }));
            Assert.False(HudInkModel.SameChips(new byte[] { 4, 4 }, new byte[] { 4 }));
            Assert.False(HudInkModel.SameChips(new byte[] { 4 }, new byte[] { 5 }));
            Assert.False(HudInkModel.SameChips(new byte[] { 4, 1 }, new byte[] { 1, 1 }));
        }

        // ————— contra el mundo real: el stream decide los chips esperados —————

        [Fact]
        public void ChipsEsperados_YLineasEscritas_ContraUnStreamReal()
        {
            // La sonda del player no inventa qué chips tienen que estar en pantalla:
            // los lee del canal A. Aquí se comprueba contra una partida REAL que
            // «chip ordenado» es exactamente «el modelo pinta texto de chip», y que
            // la cuenta de líneas que el veredicto exige es la que el modelo escribe.
            string stream = GameScenario.Run(42, ticks: 4000, colonies: 2, grid: 96, frameEvery: 60);
            var parser = new GameStreamParser();
            var tarjeta = new ColonyCardModel();

            int frames = 0, conChip = 0;
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                frames++;
                tarjeta.Observe(v);

                var niveles = new List<byte>();
                foreach (var c in v.Colonies) niveles.Add(c.RunwayLevel);
                var esperados = HudInkModel.ExpectedChips(niveles);
                if (esperados.Count > 0) conChip++;

                // Cada colonia con chip pinta texto de chip; y al revés: ninguna sin
                // chip lo pinta (el 0 es «sin medida» por contrato).
                int pintan = 0;
                foreach (var c in v.Colonies)
                    if (RunwayChipModel.Text(c.RunwayLevel, c.Runway).Length > 0) pintan++;
                Assert.Equal(pintan, esperados.Count);
            }

            Assert.True(frames > 0, "el stream no trajo ni un frame");
            Assert.True(conChip > 0, "ninguna colonia con chip en 4000 ticks: la prueba sería vacua");

            // Y la forma de la tarjeta: 5 líneas la grande, una por colonia la
            // compacta del multi-visor. Es lo que el veredicto exige por escritura.
            Assert.Equal(5, HudInkModel.AuthoredLines(tarjeta.Render(0)!));
            Assert.Equal(2, HudInkModel.AuthoredLines(tarjeta.RenderCompact()));
        }

        private static ChipPixels Buscar(IReadOnlyList<ChipPixels> chips, byte estado)
        {
            foreach (var c in chips) if (c.State == estado) return c;
            return new ChipPixels(estado, 0, 0f, new Rgb(0, 0, 0));
        }
    }
}
