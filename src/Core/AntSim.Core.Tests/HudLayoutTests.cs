using System.Collections.Generic;
using System.Globalization;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;
// Alias local, para no escribir el espacio de nombres completo en cada cuenta
// del ancho del texto (el nombre completo se repite demasiado en las medidas).
using GameFontModel = AntSim.Unity.Scripts.UI.GameFontModel;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.2 — layout del HUD (uGUI) verificado headless con los modelos PUROS:
    /// tarjeta de colonia (semáforo del canal D, reserva, cerebros), pila de
    /// toasts (dedupe por clave, expiración, tope) y el canal D completo sobre
    /// un stream REAL del Core. Si el contrato del stream cambia, estos tests
    /// rompen ANTES de que lo haga la UI de Unity.
    /// </summary>
    public sealed class HudLayoutTests
    {
        private static List<GameStreamParser.TickView> Parse(string stream)
        {
            var parser = new GameStreamParser();
            var ticks = new List<GameStreamParser.TickView>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) ticks.Add(v);
            }
            return ticks;
        }

        // ————— canal D: el Core emite alertas y semáforo en el stream —————

        [Fact]
        public void CanalD_ElStreamTraeAlertasYSemaforoPorColonia()
        {
            // Partida real con muerte de colonia (semilla 42, horizonte extendido trae
            // mortalidad/puesta parada en grid 96): el canal D debe aparecer con
            // el esquema {k, lvl, col, x, y, t, txt} y el semáforo [col, byte].
            string stream = GameScenario.Run(42, ticks: 12000, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);

            bool sawAlert = false, sawLight = false;
            foreach (var v in Parse(stream))
            {
                foreach (var a in v.Alerts)
                {
                    sawAlert = true;
                    Assert.False(string.IsNullOrEmpty(a.Key));
                    Assert.False(string.IsNullOrEmpty(a.Text));
                    Assert.InRange(a.Level, 0, 3);
                    Assert.True(a.ColonyId >= -1);
                }
                foreach (var l in v.Lights)
                {
                    sawLight = true;
                    Assert.InRange(l.Light, 0, 2); // gris/ámbar/verde de RelayVerdict
                }
            }
            Assert.True(sawLight, "el semáforo por colonia debe emitirse cada 120 ticks");
            Assert.True(sawAlert, "la partida canónica produce alertas (puesta parada/mortalidad)");
        }

        [Fact]
        public void CanalD_AlertaPorColonia_LlevaAnclaDeCamara()
        {
            // Las alertas con colonia (p. ej. laying:0) anclan al NIDO de ESA
            // colonia — el salto de cámara del toast cae sobre la colonia culpable.
            string stream = GameScenario.Run(42, ticks: 12000, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var parser = new GameStreamParser();

            GameStreamParser.ColonyView? col0 = null;
            GameStreamParser.AlertView? laying0 = null;
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v == null) continue;
                foreach (var c in v.Colonies)
                    if (c.Id == 0) col0 = c;
                foreach (var a in v.Alerts)
                    if (a.Key == "laying:0" && laying0 is null) laying0 = a;
                if (laying0 is not null && col0 is not null) break;
            }

            var a0 = laying0;
            var c0 = col0;
            Assert.True(a0.HasValue, "la partida emite laying:0");
            Assert.True(c0.HasValue, "la partida emite canal A");
            Assert.Equal(c0.Value.NestX, a0.Value.X);
            Assert.Equal(c0.Value.NestY, a0.Value.Y);
            Assert.Equal(0, a0.Value.ColonyId);
        }

        // ————— tarjeta de colonia —————

        [Fact]
        public void TarjetaColonia_MuestraSemaforoReservaYCerebros()
        {
            string stream = GameScenario.Run(42, ticks: 5200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var model = new ColonyCardModel();
            foreach (var v in Parse(stream)) model.Observe(v);

            string? card = model.Render(0);
            Assert.NotNull(card);
            Assert.Contains("colonia 0", card);
            Assert.Contains("/40", card);                    // tope duro de adultas
            // F5.1: la línea de la ventana cabe en la tarjeta (440 px a fs 14).
            // Se mide el ancho VISIBLE: el chip del runway lleva su color en
            // etiquetas de rich text, que no ocupan un solo píxel.
            foreach (var line in card.Split('\n'))
                Assert.True(SinTags(line).Length <= 60,
                    "línea demasiado larga para la tarjeta: " + line);
            Assert.Contains("reserva [", card);              // barra
            Assert.Contains("cerebros:", card);              // línea permanente
            // El semáforo llega del canal D (aquí gris: sin relevo en 5200 ticks).
            Assert.Contains(ColonyCardModel.LightGlyph(0), card);
        }

        [Fact]
        public void TarjetaColonia_ReservaBaja_MarcaYFlujoDeVentana()
        {
            // Sintético con el contrato REAL del parser: reserva 8/100 (<20%) y
            // una ventana de 1 s con muertes — la tarjeta marca y muestra el flujo.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(
                colonies: "{\"id\":0,\"nest\":[128,128],\"adults\":12,\"eggs\":4,\"larvae\":2,\"pupae\":1,\"stock\":8,\"stockMax\":100,\"elite\":3}",
                colmetrics: "[0,2,1,0,3,1,0]"));
            model.Observe(TickFrom(events: "[9,0,0,0,0,0],[10,0,0,0,0,0],[9,0,0,0,0,0]"));

            string card = model.Render(0)!;
            Assert.Contains("¡RESERVA BAJA!", card);
            Assert.Contains("−3 mue", card);                 // muertes de la ventana
            Assert.Contains("cerebros: 2 élite · 1 descartados", card);
            // F5.1: la cría va con el ICONO del huevo (glifo propio), no con la
            // palabra — y el rótulo «adultas» es el icono de la hormiga.
            // F5.1ter: los dos, al DOBLE del cuerpo (`EggLarge`/`AntLarge`).
            Assert.Contains(GameFontModel.EggLarge + " 4", card);
            Assert.Contains(GameFontModel.AntLarge + " 12/40", card);
            Assert.Contains("larvas 2", card);
            Assert.Contains("pupas 1", card);
            Assert.Contains("<b>colonia 0", card);          // título con jerarquía (rich text)
        }

        [Fact]
        public void TarjetaColonia_SemáforoSigueAlCanalD_NoSeCalculaEnLaUI()
        {
            // La tarjeta PINTA el byte que llega: [0,2] ⇒ verde, [0,1] ⇒ ámbar,
            // sin datos ⇒ gris. Ninguna aritmética de umbrales en la UI.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0, light: "[0,2]"));
            Assert.Contains(ColonyCardModel.LightGlyph(2), model.Render(0));
            model.Observe(TickFrom(light: "[0,1]"));
            Assert.Contains(ColonyCardModel.LightGlyph(1), model.Render(0));
            var fresh = new ColonyCardModel();
            fresh.Observe(TickFrom(colonies: Colony0));
            Assert.Contains(ColonyCardModel.LightGlyph(0), fresh.Render(0));
        }

        // ————— F5.1: chip de estado por runway (canal A, mismo patrón) —————

        private const string Colony0ConRunway =
            "{\"id\":0,\"nest\":[128,128],\"adults\":10,\"eggs\":0,\"larvae\":0,\"pupae\":0,\"stock\":50,\"stockMax\":100,\"elite\":0,\"runway\":22.5,\"runwayLevel\":3}";

        [Fact]
        public void TarjetaColonia_ChipDeRunway_EnLaLineaDelTitulo()
        {
            // El tramo (3 = oofagia) y los segundos llegan IMPRESOS en el canal A:
            // la tarjeta los pinta tal cual, en la misma línea del título para no
            // crecer la tarjeta. La igualdad exacta fija además que no hay espacios
            // colgando al final.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0ConRunway));
            string card = model.Render(0)!;
            string titulo = card.Split('\n')[0].TrimEnd('\r');
            Assert.Equal("<b>colonia 0  " + ColonyCardModel.LightGlyph(0) + "</b>  " +
                         RunwayChipModel.RichText(3, 22.5f), titulo);
        }

        [Fact]
        public void TarjetaColonia_SinMedida_ElTituloQuedaComoSiempre()
        {
            // Stream sin los campos de F5.1 (el parser los deja en −1/0): no hay
            // chip y el título es EXACTAMENTE el de antes — un chip en gris sería
            // peor que ninguno.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0));
            string titulo = model.Render(0)!.Split('\n')[0].TrimEnd('\r');
            Assert.Equal("<b>colonia 0  " + ColonyCardModel.LightGlyph(0) + "</b>", titulo);
        }

        [Fact]
        public void TarjetaColonia_EstadoCeroConRunway_NoPintaChip()
        {
            // Caso degenerado: el campo llega pero sin medida. Se pinta sin chip;
            // no se convierte un «sin dato» en un «crítico» inventado.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies:
                "{\"id\":0,\"nest\":[128,128],\"adults\":10,\"eggs\":0,\"larvae\":0,\"pupae\":0,\"stock\":50,\"stockMax\":100,\"elite\":0,\"runway\":-1,\"runwayLevel\":0}"));
            Assert.Equal("<b>colonia 0  " + ColonyCardModel.LightGlyph(0) + "</b>",
                model.Render(0)!.Split('\n')[0].TrimEnd('\r'));
        }

        // ————— F5.1ter: los iconos al DOBLE del cuerpo —————
        // MEDIDO en el HUD en vivo (ventana 1536×1303, escala 0,983): con el cuerpo
        // a 14 px el huevo medía 5×7 px, la carga 7×8 y la hormiga 9×14, y la
        // comparación contra la referencia del `.ttf` no llegaba a distinguirlos
        // (los tres puntuaban 0,80-0,86: sin margen). Ahora el icono va en un run
        // al doble, y estos dos tests fijan las dos mitades del cambio: que el
        // doble viaja en el TEXTO y que la caja de la tarjeta no se mueve.

        [Fact]
        public void TarjetaColonia_LosIconosVanAlDobleYEnSuPropioRun()
        {
            Assert.Equal(GameFontModel.CardBodySize * 2, GameFontModel.CardIconSize);

            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0ConRunway, light: "[0,2]",
                colmetrics: "[0,3,1,0,0,0,0]"));
            string card = model.Render(0)!;

            // El icono viaja en un RUN de rich text: así crece sin mover el avance
            // del texto que lo rodea (el ancho de la tarjeta está contado con 0,5 em
            // por letra y esa cuenta no se toca).
            Assert.Contains(GameFontModel.AntLarge, card);
            Assert.Contains(GameFontModel.LoadLarge, card);
            Assert.Contains(GameFontModel.EggLarge, card);

            // Y ningún glifo de icono queda FUERA de un run grande: si alguien vuelve
            // a escribir el glifo crudo, el icono se encoge a la mitad sin que nada
            // más lo delate (las dos cuentas tienen que coincidir).
            Assert.Equal(CountOf(card, GameFontModel.AntLarge), CountOf(card, GameFontModel.Ant));
            Assert.Equal(CountOf(card, GameFontModel.LoadLarge), CountOf(card, GameFontModel.Load));
            Assert.Equal(CountOf(card, GameFontModel.EggLarge), CountOf(card, GameFontModel.Egg));
        }

        [Fact]
        public void TarjetaColonia_ConLosIconosAlDoble_SigueCabiendoEnSuCaja()
        {
            // La tarjeta NO crece por los iconos: 440×268 con 14 de margen interior y
            // el cuerpo a 126 px del borde inferior ⇒ 406×135 de caja útil (las
            // mismas cifras que monta el bootstrapper). Antes esto se vigilaba a ojo
            // («unos 55 caracteres a 7 px»); ahora se mide como lo hace el
            // `TextGenerator`: cada carácter con su avance al tamaño del run en que
            // vive, y cada línea con el alto del run más alto.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0ConRunway, light: "[0,2]",
                colmetrics: "[0,12,11,2,3,14,1]"));   // ventana de 1 s con dos dígitos
            string card = model.Render(0)!;

            float widest = 0f, height = 0f;
            int lines = 0;
            foreach (var line in card.Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                lines++;
                float w = RenderedWidth(line);
                if (w > widest) widest = w;
                height += LineHeight(line);
            }

            Assert.True(widest <= ColonyCardModel.CardBodyWidth,
                $"la línea más ancha mide {Px(widest)} y la caja reserva {ColonyCardModel.CardBodyWidth} px");
            Assert.True(height <= ColonyCardModel.CardBodyHeight,
                $"las {lines} líneas piden {Px(height)} de alto y la caja reserva {ColonyCardModel.CardBodyHeight} px");
            // Con AIRE para una línea más: si el alto queda al filo, el icono al
            // doble se sale en cuanto una cifra gane un dígito. Una línea de cuerpo
            // son ~14,3 px, así que el aire no puede bajar de ahí.
            Assert.True(height <= ColonyCardModel.CardBodyHeight - 14,
                $"el cuerpo queda sin aire: {Px(height)} en una caja de {ColonyCardModel.CardBodyHeight} px");
        }

        [Fact]
        public void TextosFijosDelHud_CabenEnLaCajaQueLaEscenaLesDa()
        {
            // EL DESBORDE QUE SOLO VEÍA UNA CAPTURA. El Play pass midió tres textos
            // fuera de su caja —las pistas de teclado (1325 > 1100) y las dos líneas
            // del panel de aprendizaje (648 y 513 > 406)— y ninguna suite podía
            // verlo: la caja y el texto de las pistas vivían en el bootstrapper
            // (código de editor, no compilado aquí) y el texto del panel se compone
            // con números que trae el stream. Ahora el texto FIJO y su caja (y la
            // caja del panel) viven en modelos puros, así que el ajuste se comprueba
            // aquí con la MISMA cuenta del `TextGenerator` —y con el PEOR caso de
            // números, no con el del día— en vez de con una foto.
            float hints = RenderedWidth(HudElementLayoutModel.HintsText);
            Assert.True(hints <= HudElementLayoutModel.HintsWidth,
                $"las pistas miden {Px(hints)} y su caja reserva {Px(HudElementLayoutModel.HintsWidth)} px: «{HudElementLayoutModel.HintsText}»");

            // Peor caso explícito: generación de 2 dígitos, cohorte y bajas al tope
            // (40 = AdultsHardCap), fitness de 2 y 3 dígitos, el mundo ENTERO pisado
            // (9216 celdas) y una élite de 2 dígitos.
            string l1 = LearningPanelModel.FitnessLine(99, 40, 40, 99.9f, 999.9f);
            string l2 = LearningPanelModel.WorldLine(99.9f, 1.0f, 9216, 9216);
            foreach (var (nombre, texto) in new[] { ("cohorte/fitness", l1), ("élite/mundo", l2) })
            {
                float w = RenderedWidth(texto);
                Assert.True(w <= HudElementLayoutModel.LearningInfoWidth,
                    $"la línea {nombre} mide {Px(w)} y la banda del panel reserva {Px(HudElementLayoutModel.LearningInfoWidth)} px: «{texto}»");
            }
            // Y el ALTO: son DOS líneas en una banda de 34 px (la tarjeta no tiene
            // más hueco entre la curva de fitness y la base).
            float alto = LineHeight(l1) + LineHeight(l2);
            Assert.True(alto <= HudElementLayoutModel.LearningInfoHeight,
                $"las dos líneas piden {Px(alto)} de alto y la banda reserva {HudElementLayoutModel.LearningInfoHeight} px");
        }

        [Fact]
        public void LineaDeReclutamiento_DiceLasCifrasQueSeAplican_YCabeEnSuCaja()
        {
            // F8: la línea del HUD existe para comparar dos lanzamientos seguidos
            // sin abrir el log del editor, así que tiene que publicar los MISMOS
            // números que viajan al CLI — y caber con el peor caso posible, no con
            // el del día.
            string linea = HudElementLayoutModel.RecruitmentLine(RecruitmentKnobs.Default);
            Assert.Contains("montones=8", linea);
            Assert.Contains("6–10 ítems", linea);
            Assert.Contains("radio 18 u", linea);
            Assert.Contains("rastro=1× / 2100 s / sigue 8 rad/s", linea);

            // Los rangos MÁXIMOS que acepta la tabla: 64 montones, 40 ítems, radio
            // 120 u, rastro 5×/3600 s/20 rad/s. Si esto cabe, cualquier otra
            // configuración válida cabe.
            var tope = new RecruitmentKnobs
            {
                Clusters = RecruitmentKnobs.MaxClusters,
                ClusterMin = RecruitmentKnobs.MaxClusterItems,
                ClusterMax = RecruitmentKnobs.MaxClusterItems,
                ClusterRadius = RecruitmentKnobs.MaxClusterRadius,
                TrailDeposit = RecruitmentKnobs.MaxTrailDeposit,
                TrailHalfLife = RecruitmentKnobs.MaxTrailHalfLife,
                TrailFollow = RecruitmentKnobs.MaxTrailFollow,
            };
            Assert.True(tope.TryValidate(out string errTope), errTope);

            foreach (var (nombre, texto) in new[]
                     {
                         ("default", linea),
                         ("tope de rangos", HudElementLayoutModel.RecruitmentLine(tope)),
                         ("apagado", HudElementLayoutModel.RecruitmentLine(tope, enabled: false)),
                     })
            {
                float w = RenderedWidth(texto);
                Assert.True(w <= HudElementLayoutModel.RecruitmentWidth,
                    $"la línea de reclutamiento ({nombre}) mide {Px(w)} y su caja reserva " +
                    $"{Px(HudElementLayoutModel.RecruitmentWidth)} px: «{texto}»");
                float altoLinea = LineHeight(texto);
                Assert.True(altoLinea <= HudElementLayoutModel.RecruitmentHeight,
                    $"la línea de reclutamiento ({nombre}) pide {Px(altoLinea)} de alto y la caja " +
                    $"reserva {HudElementLayoutModel.RecruitmentHeight} px");
            }

            // Apagado se DICE: un hueco vacío se leería como «el HUD no pinta».
            string apagado = HudElementLayoutModel.RecruitmentLine(tope, enabled: false);
            Assert.Equal(HudElementLayoutModel.RecruitmentOffText, apagado);
            Assert.Contains("apagado", apagado);
        }

        [Fact]
        public void CifrasDelTablero_CuentanMontonesVivosYItems()
        {
            // F8: la línea de configuración dice lo que se PIDIÓ al mundo; esta
            // dice lo que HAY. Dos montones de tres ítems a 8 u (radio 18) y una
            // migaja sola a 200 u: la migaja no es un montón sembrado.
            var view = new GameStreamParser.TickView();
            for (int i = 0; i < 3; i++)
                view.Items.Add(new GameStreamParser.ItemView((uint)i, 100f + i * 8f, 100f, 1f));
            for (int i = 0; i < 3; i++)
                view.Items.Add(new GameStreamParser.ItemView((uint)(10 + i), 300f + i * 8f, 300f, 1f));
            view.Items.Add(new GameStreamParser.ItemView(99, 500f, 500f, 1f));

            var live = new RecruitmentLiveModel();
            Assert.False(live.HasWorld);
            Assert.Equal("", HudElementLayoutModel.RecruitmentLiveLine(live));

            live.Observe(view, 18f);
            Assert.True(live.HasWorld);
            Assert.Equal(7, live.Items);
            Assert.Equal(2, live.Piles);

            string line = HudElementLayoutModel.RecruitmentLiveLine(live);
            Assert.Contains("montones 2", line);
            Assert.Contains("ítems 7", line);
        }

        [Fact]
        public void CifrasDelTablero_CuentanSoloElRastroQueSeVe()
        {
            // El rastro se cuenta con la MISMA curva del render: de las cuatro
            // celdas con dato, la de 20/255 cae por debajo del corte (18 % de 200
            // = 36) y NO se pinta, así que el HUD no la cuenta.
            var cells = new byte[5 * 3];
            cells[1 * 5 + 1] = 200; cells[1 * 5 + 2] = 200; cells[1 * 5 + 3] = 200;
            cells[0 * 5 + 4] = 20;

            var live = new RecruitmentLiveModel();
            live.Observe(new GameStreamParser.TickView(), 18f);
            live.ObserveTrail("food", 5, 3, cells);

            Assert.Equal(3, live.TrailCells);       // no 4: la débil no se ve
            Assert.Equal((byte)200, live.TrailMax);
            Assert.Equal(24, live.BandWidthUnits);  // 3 celdas × 8 u
            Assert.Equal(8, live.BandHeightUnits);  // 1 celda

            string line = HudElementLayoutModel.RecruitmentLiveLine(live);
            Assert.Contains("rastro food 3 celdas", line);
            Assert.Contains("máx 200", line);
            Assert.Contains("banda 24×8 u", line);

            // Sin paquete no se deja la cifra anterior y no se inventa una banda.
            live.ObserveTrail("food", 0, 0, null);
            Assert.Equal(0, live.TrailCells);
            string vacia = HudElementLayoutModel.RecruitmentLiveLine(live);
            Assert.Contains("0 celdas", vacia);
            Assert.DoesNotContain("banda", vacia, System.StringComparison.Ordinal);
        }

        [Fact]
        public void CifrasDelTablero_CabenEnSuCaja_ConElPeorCaso()
        {
            // El texto más largo que el modelo puede publicar en un mundo de 256²:
            // el rastro entero pintado (65 536 celdas, banda 2 048 u) y 33 montones.
            var view = new GameStreamParser.TickView();
            for (int p = 0; p < 33; p++)
                for (int i = 0; i < 3; i++)
                    view.Items.Add(new GameStreamParser.ItemView(
                        (uint)(p * 3 + i), p * 30f + i * 2f, 0f, 1f));

            var live = new RecruitmentLiveModel();
            live.Observe(view, 5f);   // 3 ítems a 2 u = montón; montones a 30 u
            Assert.Equal(33, live.Piles);
            Assert.Equal(99, live.Items);

            const int w = 256, h = 256;
            var cells = new byte[w * h];
            for (int i = 0; i < cells.Length; i++) cells[i] = (byte)(255 - i % 7);
            live.ObserveTrail("footprint", w, h, cells);   // la capa de nombre más largo
            Assert.Equal(65_536, live.TrailCells);

            string text = HudElementLayoutModel.RecruitmentLiveLine(live);
            float ancho = RenderedWidth(text);
            Assert.True(ancho <= HudElementLayoutModel.RecruitmentLiveWidth,
                $"la línea del tablero mide {Px(ancho)} y su caja reserva " +
                $"{Px(HudElementLayoutModel.RecruitmentLiveWidth)} px: «{text}»");
            Assert.True(LineHeight(text) <= HudElementLayoutModel.RecruitmentLiveHeight,
                $"la línea del tablero pide {Px(LineHeight(text))} de alto y la caja " +
                $"reserva {HudElementLayoutModel.RecruitmentLiveHeight} px");
        }

        // ————— F11n: el informe de la glándula —————

        [Fact]
        public void InformeDeLaGlandula_SeLeeDeLaLineaEnd_YNoSeInventaSinElla()
        {
            var parser = new GameStreamParser();
            Assert.Null(parser.Trail);   // todavía no cerró nada

            // Sin --trail-report la línea `end` es la de siempre: no hay bloque.
            parser.ParseLine("{\"end\":true,\"tick\":216000,\"hash\":\"abc\"}");
            Assert.Null(parser.Trail);

            parser.ParseLine("{\"end\":true,\"tick\":216000,\"hash\":\"abc\"," +
                             "\"trail\":{\"food\":34223.4,\"home\":89816.2," +
                             "\"denied\":24266.9,\"gland\":19213.4}}");
            var trail = parser.Trail;
            Assert.NotNull(trail);
            Assert.Equal(34223.4f, trail!.Value.Food, 1);
            Assert.Equal(89816.2f, trail.Value.Home, 1);
            Assert.Equal(24266.9f, trail.Value.Denied, 1);
            Assert.Equal(19213.4f, trail.Value.Gland, 1);
            // El cociente es el de los guiones de medida (negado / lo pedido).
            Assert.Equal(16.4f, trail.Value.DeniedPercent, 1);
        }

        [Fact]
        public void InformeDeLaGlandula_DiceLoGastadoYLoNoPagado_SoloConPrecio()
        {
            var knobs = new RecruitmentKnobs
            {
                TrailByBrain = true,
                TrailGlandSeconds = 25f,
                TrailGlandRefillSeconds = 6f,
            };
            var trail = new GameStreamParser.TrailReportView(
                food: 34223.4f, home: 89816.2f, denied: 24266.9f, gland: 19213.4f);

            string line = HudElementLayoutModel.GlandLine(trail, knobs);
            Assert.Equal("glándula 25 s · 19213.4 s gastados en el rastro · " +
                         "16.4 % de lo pedido quedó sin pagar", line);

            // Sin informe no hay línea; y sin precio no hay presupuesto que contar
            // (un «0 s» se leería como «no se gastó nada»).
            Assert.Equal("", HudElementLayoutModel.GlandLine(null, knobs));
            Assert.Equal("", HudElementLayoutModel.GlandLine(trail, RecruitmentKnobs.Default));
        }

        [Fact]
        public void InformeDeLaGlandula_CabeEnLaCajaDelTablero_ConElPeorCaso()
        {
            // Los topes que acepta el mundo: 120 s de glándula y una ronda con
            // cifras de siete dígitos en los tres depósitos.
            var knobs = new RecruitmentKnobs
            {
                TrailGlandSeconds = RecruitmentKnobs.MaxTrailGland,
                TrailGlandRefillSeconds = RecruitmentKnobs.MaxTrailGlandRefill,
            };
            var trail = new GameStreamParser.TrailReportView(
                food: 9999999.9f, home: 9999999.9f, denied: 9999999.9f, gland: 9999999.9f);
            string text = HudElementLayoutModel.GlandLine(trail, knobs);

            float ancho = RenderedWidth(text);
            Assert.True(ancho <= HudElementLayoutModel.RecruitmentLiveWidth,
                $"el informe mide {Px(ancho)} y la caja reserva " +
                $"{Px(HudElementLayoutModel.RecruitmentLiveWidth)} px: «{text}»");
            Assert.True(LineHeight(text) <= HudElementLayoutModel.RecruitmentLiveHeight,
                $"el informe pide {Px(LineHeight(text))} de alto y la caja reserva " +
                $"{Px(HudElementLayoutModel.RecruitmentLiveHeight)} px");
        }

        [Fact]
        public void InformeDeLaGlandula_DelStreamReal_AlCerrarLaRonda()
        {
            // Extremo a extremo: el mundo gasta glándula al depositar y lo escribe
            // en la línea `end`; el parser lo lee y la línea del HUD lo publica.
            var tuning = new RecruitmentTuning
            {
                TrailByBrain = true,
                TrailGlandSeconds = 25f,
                TrailGlandRefillSeconds = 6f,
            };
            string stream = GameScenario.Run(42, ticks: 600, colonies: 1, grid: 96,
                frameEvery: 30, recruitment: true, recruitmentTuning: tuning,
                trailReport: true);

            var parser = new GameStreamParser();
            foreach (var line in stream.Split('\n')) parser.ParseLine(line);

            var trail = parser.Trail;
            Assert.NotNull(trail);
            Assert.True(trail!.Value.Gland > 0f,
                "la partida con puerta y precio tiene que gastar glándula");
            Assert.InRange(trail.Value.DeniedPercent, 0f, 100f);

            var knobs = new RecruitmentKnobs
            {
                TrailByBrain = true,
                TrailGlandSeconds = 25f,
                TrailGlandRefillSeconds = 6f,
            };
            string text = HudElementLayoutModel.GlandLine(trail, knobs);
            Assert.Contains("glándula 25 s", text);
            Assert.Contains("s gastados en el rastro", text);
            Assert.Contains("% de lo pedido quedó sin pagar", text);
        }

        [Fact]
        public void AvanceReservado_EsElDeLaFuenteConElRedondeoDelHinting()
        {
            // ANCLA A LA MEDIDA. El modelo de ancho de estas pruebas vale lo que
            // vale su cuenta del avance, y esa cuenta se creía «0,5 em las letras
            // y 0,72 em los iconos»: el `.ttf` lo declara exactamente así, y con
            // `LOAD_NO_HINTING` el motor lo devuelve exactamente así (719,76-719,87
            // unidades el icono a 14, 15, 28, 29 y 31 px; 500,00 la letra). Pero
            // uGUI carga los glifos CON hinting y el avance reservado cae al píxel
            // entero: 10 px a 14, 11 a 15, 20 a 28, 21 a 29 y 22 a 31 para el
            // icono, y 8 px a 15 px para una letra (500 → 533 unidades, un 6,7 %
            // más de lo que dice la em). Estos siete números son los medidos; si el
            // redondeo cambia, el ancho de las tarjetas deja de estar contado
            // sobre lo que el HUD pinta y esta prueba lo dice antes que la vista.
            Assert.Equal(7f, HintedAdvance('A', 14));
            Assert.Equal(8f, HintedAdvance('A', 15));
            Assert.Equal(14f, HintedAdvance('A', 28));
            Assert.Equal(15f, HintedAdvance('A', 29));
            Assert.Equal(10f, HintedAdvance(GameFontModel.AntGlyph, 14));
            Assert.Equal(11f, HintedAdvance(GameFontModel.AntGlyph, 15));
            Assert.Equal(20f, HintedAdvance(GameFontModel.AntGlyph, 28));
            Assert.Equal(21f, HintedAdvance(GameFontModel.AntGlyph, 29));
            Assert.Equal(22f, HintedAdvance(GameFontModel.AntGlyph, 31));
            // Y el icono no reserva un hueco de letra: 0,714 em redondeado son
            // 10 px a 14 px, no 7 como una letra. La caja de TINTA (0,643 em,
            // 18 px a 28 px) cabe dentro del avance: 20 - 18 = 2 px de aire.
            Assert.True(HintedAdvance(GameFontModel.AntGlyph, 14) > HintedAdvance('A', 14));
        }

        [Fact]
        public void TarjetaCompacta_LlevaElChipEnCorto()
        {
            // En el multi-visor la línea es corta: glifo + segundos, sin rótulo.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(colonies: Colony0ConRunway, light: "[0,2]"));
            string linea = model.RenderCompact().Split('\n')[0];
            Assert.Contains("[!]22.5s", linea);
        }

        [Fact]
        public void Chip_DelStreamReal_TarjetaYCompacta()
        {
            // Extremo a extremo: el chip de una partida REAL sale del canal A del
            // Core y acaba pintado en las DOS vistas. Hasta el tick 6000 la colonia
            // va sobrada (medido: todas las muestras en desahogo), así que el chip
            // tiene que estar en TODOS los frames con canal A — no «a veces».
            string stream = GameScenario.Run(42, ticks: 6000, colonies: 2, grid: 96,
                frameEvery: 30);
            var model = new ColonyCardModel();
            int frames = 0, conChip = 0;
            foreach (var v in Parse(stream))
            {
                if (v.Colonies.Count == 0) continue;   // tick sin canal A
                model.Observe(v);
                frames++;
                if (model.RenderCompact().Contains("[++]", System.StringComparison.Ordinal)) conChip++;
            }
            Assert.Equal(200, frames);      // 6000 ticks / frameEvery 30
            Assert.Equal(frames, conChip);  // chip en cada frame, sin huecos
            Assert.Contains("[++] DESAHOGO", model.Render(0)!);
        }

        // ————— F5.1bis: tarjeta compacta del multi-visor —————

        [Fact]
        public void TarjetaCompacta_EsperaStream_SinDatos()
        {
            var model = new ColonyCardModel();
            string text = model.RenderCompact();
            Assert.Contains("esperando", text); // nunca vacío: la vista se ve viva
        }

        [Fact]
        public void TarjetaCompacta_UnaLineaPorColonia_SemaforoYReserva()
        {
            // Sintético con el contrato REAL del parser: dos colonias, semáforo
            // verde en la 0, reserva baja en la 1 — compacto = 1 línea por colonia.
            var model = new ColonyCardModel();
            model.Observe(TickFrom(
                colonies: "{\"id\":0,\"nest\":[128,128],\"adults\":12,\"eggs\":1,\"larvae\":0,\"pupae\":0,\"stock\":70,\"stockMax\":100,\"elite\":0}," +
                          "{\"id\":1,\"nest\":[256,128],\"adults\":8,\"eggs\":0,\"larvae\":1,\"pupae\":0,\"stock\":15,\"stockMax\":100,\"elite\":0}",
                light: "[0,2],[1,1]",
                colmetrics: "[0,3,1,0,0,0,0],[1,1,0,0,2,0,0]"));

            string text = model.RenderCompact();
            var lines = text.Split('\n');
            Assert.Equal(2, lines.Length); // UNA línea por colonia (no tarjetas de 5)
            // Colonia 0: semáforo verde, adultas, reserva OK, flujo de la ventana.
            Assert.Contains("colonia 0", lines[0]);
            Assert.Contains(ColonyCardModel.LightGlyph(2), lines[0]);
            Assert.Contains(AntSim.Unity.Scripts.UI.GameFontModel.Ant + "12", lines[0]);
            Assert.Contains("70%", lines[0]);
            Assert.Contains("3rec/1desc", lines[0]);
            Assert.DoesNotContain("¡BAJA!", lines[0]);
            // Colonia 1: ámbar y reserva baja marcada.
            Assert.Contains("colonia 1", lines[1]);
            Assert.Contains(ColonyCardModel.LightGlyph(1), lines[1]);
            Assert.Contains("¡BAJA!", lines[1]);
            // Líneas cortas: caben en media pantalla (el viewport del multi-visor).
            // Ancho VISIBLE: el chip trae su color en rich text y las etiquetas no
            // ocupan píxeles — lo que tiene que caber es el texto que se ve.
            Assert.All(lines, l => Assert.True(SinTags(l).Length <= 55,
                "línea larga para el viewport: " + l));
        }

        [Fact]
        public void TarjetaCompacta_SemaforoSinCanalA_AunSeVe()
        {
            // El canal D (light) puede llegar antes que un frame del canal A:
            // la línea de esa colonia existe y muestra el glifo, con «—».
            var model = new ColonyCardModel();
            model.Observe(TickFrom(light: "[0,2]"));
            string text = model.RenderCompact();
            Assert.Contains("colonia 0", text);
            Assert.Contains(ColonyCardModel.LightGlyph(2), text);
            Assert.Contains("—", text);
        }

        [Fact]
        public void TarjetaCompacta_DelStreamReal_2Colonias()
        {
            string stream = GameScenario.Run(42, ticks: 3600, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var model = new ColonyCardModel();
            foreach (var v in Parse(stream)) model.Observe(v);

            string text = model.RenderCompact();
            Assert.Contains("colonia 0", text);
            Assert.Contains("colonia 1", text);
            Assert.All(text.Split('\n'), l => Assert.True(SinTags(l).Length <= 55, l));
        }

        private const string Colony0 =
            "{\"id\":0,\"nest\":[128,128],\"adults\":10,\"eggs\":0,\"larvae\":0,\"pupae\":0,\"stock\":50,\"stockMax\":100,\"elite\":0}";

        [Fact]
        public void BarraDeEstado_ResumeTickVelocidadYColonias()
        {
            // F5.1: el renglón superior sale del modelo puro (tick + reloj de
            // reproducción + semáforo/adultas/reserva por colonia). Antes la barra
            // no existía y la UI no tenía dónde decir en qué tick iba la partida.
            var model = new ColonyCardModel();
            Assert.Null(model.StatusLine(1, 1f));               // aún sin canal A

            model.Observe(TickFrom(colonies: Colony0, light: "[0,2]"));
            string line = model.StatusLine(3950, 3f)!;
            Assert.Contains("tick 3950", line);
            Assert.Contains("v×3", line);
            Assert.Contains("colonia", line);
            Assert.Contains(ColonyCardModel.LightGlyph(2), line);
            Assert.Contains("50%", line);                       // reserva 50/100

            // En pausa el reloj lo dice con palabras (no «v×0», que parece un error).
            Assert.Contains("EN PAUSA", model.StatusLine(3950, 0f));
        }

        // ————— toasts —————

        [Fact]
        public void Toasts_DedupePorClave_TopeYExpiracion()
        {
            var toasts = new HudToastsModel(lifetime: 2f, maxStack: 3);
            toasts.Push(Toast("k1", "primera", tick: 100));
            toasts.Push(Toast("k2", "segunda", tick: 110));
            toasts.Push(Toast("k1", "primera-refrescada", tick: 120)); // reemplaza, no duplica
            Assert.Equal(2, toasts.Active.Count);
            Assert.Equal("primera-refrescada", toasts.Active[0].Text); // la nueva arriba

            toasts.Push(Toast("k3", "tercera", tick: 130));
            toasts.Push(Toast("k4", "cuarta", tick: 140));             // tope 3: sale k2
            Assert.Equal(3, toasts.Active.Count);
            Assert.DoesNotContain(toasts.Active, t => t.Key == "k2");

            toasts.Tick(2f); // expiran todas (lifetime 2)
            Assert.Empty(toasts.Active);
            // Y la clave liberada puede volver a usarse.
            toasts.Push(Toast("k2", "regresada", tick: 200));
            Assert.Single(toasts.Active);
        }

        [Fact]
        public void Toasts_DelStream_Real_ConNivelYColonia()
        {
            string stream = GameScenario.Run(42, ticks: 5200, colonies: 2, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: new[] { (30, 256f, 384f) });
            var toasts = new HudToastsModel();
            foreach (var v in Parse(stream)) toasts.Observe(v);

            Assert.NotEmpty(toasts.Active); // la partida canónica emite alertas
            foreach (var t in toasts.Active)
            {
                Assert.InRange(t.Level, 0, 3);
                Assert.False(string.IsNullOrEmpty(t.Text));
            }
            // Y las líneas renderizadas llevan el tag de colonia cuando aplica,
            // con el glifo de nivel por delante (F5.1: glifos de FORMA — ◐ ● ■ · —
            // porque la fuente por defecto de uGUI no tiene emoji y salían cajas).
            var glyphs = new[] { HudToastsModel.Glyph(0), HudToastsModel.Glyph(1),
                HudToastsModel.Glyph(2), HudToastsModel.Glyph(3) };
            foreach (var line in toasts.RenderLines())
            {
                bool hasGlyph = line.StartsWith("·");
                foreach (var g in glyphs) if (line.StartsWith(g)) hasGlyph = true;
                Assert.True(hasGlyph,
                    "la línea debe empezar por su glifo de nivel: " + line);
            }
        }

        // ————— historial de comandos (contrato HUD §4) —————

        [Fact]
        public void HistorialComandos_RegistraDropFoodYSaveGame()
        {
            var model = new CommandHistoryModel();
            // kind 11 = CommandExecuted; AntId trae el kind del comando (0 DropFood,
            // 1 SaveGame — contrato del canal B), X/Y las coordenadas.
            model.Observe(TickFrom(events: "[11,0,0,42,17,0]"));   // DropFood @ (42,17)
            model.Observe(TickFrom(events: "[11,0,1,0,0,0]", tickIsIrrelevant: false));

            Assert.Equal(2, model.TotalCommands);
            Assert.True(model.CanReplayFromSave);
            string panel = model.Render();
            Assert.Contains("t=1 — DropFood @ (42, 17)", panel);
            Assert.Contains("SaveGame → slot", panel);
            Assert.Contains("✓ guardado", panel);
            Assert.Equal(2, model.Rows.Count);          // la más nueva arriba
            Assert.Equal(1, model.Rows[0].CommandKind); // SaveGame
        }

        [Fact]
        public void HistorialComandos_TopeDeFilas_NoPierdeElTotal()
        {
            var model = new CommandHistoryModel();
            for (ulong t = 1; t <= 250; t++)
                model.Observe(TickFrom(events: "[11,0,0,1,1,0]", tick: t));

            Assert.Equal(250, model.TotalCommands);       // el acumulado no se recorta
            Assert.Equal(CommandHistoryModel.MaxRows, model.Rows.Count);
            Assert.Equal(250UL, model.Rows[0].Tick);      // la más nueva arriba
        }

        [Fact]
        public void HistorialComandos_DelStreamReal()
        {
            // Partida real con comandos inyectados (--drop del contrato F4.0):
            // el panel debe registrarlos todos desde el stream.
            string stream = GameScenario.Run(42, ticks: 300, colonies: 1, grid: 96,
                frameEvery: 30, seedPoolPath: null,
                drops: new[] { (100, 384f, 384f), (150, 300f, 300f) });
            var model = new CommandHistoryModel();
            foreach (var v in Parse(stream)) model.Observe(v);

            Assert.Equal(2, model.TotalCommands);
            Assert.Contains("DropFood @ (384, 384)", model.Render());
        }

        [Fact]
        public void Toasts_LaPausaNoLosConsume_SegundosDeSim()
        {
            var toasts = new HudToastsModel(lifetime: 1f, maxStack: 4);
            toasts.Push(Toast("k", "algo", tick: 10));
            toasts.Tick(0.9f);
            toasts.Tick(0.9f); // 1.8 s de sim, PERO en dos pasos: expira al cruzar 1
            Assert.Empty(toasts.Active);

            toasts.Push(Toast("k2", "pausada", tick: 20));
            toasts.Tick(0f);  // pausa: dt=0 ⇒ nada envejece
            Assert.Single(toasts.Active);
        }

        // ————— helpers: TickViews sintéticos con el contrato real del parser —————

        private static GameStreamParser.TickView TickFrom(
            string colonies = "", string colmetrics = "", string events = "",
            string light = "", ulong tick = 1, bool tickIsIrrelevant = true)
        {
            var sb = new System.Text.StringBuilder("{\"tick\":").Append(tick);
            if (colonies.Length > 0) sb.Append(",\"colonies\":[").Append(colonies).Append(']');
            if (colmetrics.Length > 0) sb.Append(",\"colmetrics\":[").Append(colmetrics).Append(']');
            if (events.Length > 0) sb.Append(",\"events\":[").Append(events).Append(']');
            if (light.Length > 0) sb.Append(",\"light\":[").Append(light).Append(']');
            sb.Append('}');
            var v = new GameStreamParser().ParseLine(sb.ToString());
            Assert.NotNull(v);
            return v!;
        }

        /// <summary>Cuántas veces aparece `needle` (las pruebas de «todo icono va en
        /// su run grande» se leen mejor contando que con un `Contains`).</summary>
        private static int CountOf(string haystack, string needle)
        {
            int n = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
            {
                n++;
                at += needle.Length;
            }
            return n;
        }

        /// <summary>Píxeles que uGUI RESERVA a un carácter: el avance de la fuente
        /// al tamaño del run, REDONDEADO al píxel entero.
        ///
        /// POR QUÉ HAY REDONDEO, y no una simple multiplicación. Unity carga el
        /// glifo con el HINTING de FreeType (`LOAD_DEFAULT`), que ajusta el avance
        /// al píxel entero, y el `TextGenerator` acumula esos enteros (medido:
        /// `cursorPos` es 0, 14, 34…). MEDIDO con la fuente del juego cargada por
        /// el motor, sin hinting (`LOAD_NO_HINTING`) el icono da 719,76-719,87
        /// unidades a 14, 15, 28, 29 y 31 px (0,72 em, lo que declara el `.ttf`) y
        /// una letra 500,00 clavadas; CON hinting —lo que el HUD pinta— el icono
        /// reserva 10 px a 14, 11 a 15, 20 a 28, 21 a 29 y 22 a 31, y una letra
        /// pasa a 8 px a 15 (500 → 533 unidades). Sumar el avance de la em sin
        /// redondear mide algo que no existe: a 15 px se queda corta un 7 % por
        /// letra, y el ancho de la tarjeta está contado con 7 px por letra a
        /// 14 px —donde la suerte hace que el redondeo no cambie nada—.
        ///
        /// Ojo con el número que se le atribuye al icono: su CAJA DE TINTA mide
        /// 0,643 em (9 px a 14 px, 18 px a 28 px, medido sobre la referencia
        /// rasterizada). Eso es tinta, no avance; el avance es 720 unidades y la
        /// tinta cabe dentro.</summary>
        private static float HintedAdvance(int codepoint, int run)
        {
            float px = GameFontModel.AdvanceOf(codepoint) * run / (float)GameFontModel.UnitsPerEm;
            return (float)System.Math.Floor(px + 0.5);
        }

        /// <summary>Ancho que el texto ocupa, en unidades del canvas, con la misma
        /// cuenta que el `TextGenerator`: cada carácter con el avance RESERVADO
        /// (ver <see cref="HintedAdvance"/>) al tamaño del RUN en que vive — un
        /// icono dentro de `&lt;size=28&gt;` avanza al doble que uno del cuerpo. Las
        /// etiquetas no ocupan un píxel.</summary>
        private static float RenderedWidth(string line)
        {
            float px = 0f;
            int run = GameFontModel.CardBodySize;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '<')
                {
                    int close = line.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        run = RunSize(line.Substring(i + 1, close - i - 1), run);
                        i = close;
                        continue;
                    }
                }
                px += HintedAdvance(line[i], run);
            }
            return px;
        }

        /// <summary>Alto de una línea, en unidades del canvas: el de su run más alto
        /// (ascendente menos descendente) — un icono al doble hace su línea el doble
        /// de alta, y eso es lo que hay que contar para saber si cabe.</summary>
        private static float LineHeight(string line)
        {
            int run = GameFontModel.CardBodySize;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] != '<') continue;
                int close = line.IndexOf('>', i + 1);
                if (close <= i) continue;
                run = System.Math.Max(run, RunSize(line.Substring(i + 1, close - i - 1), run));
                i = close;
            }
            return run * (GameFontModel.Ascender - GameFontModel.Descender) / (float)GameFontModel.UnitsPerEm;
        }

        /// <summary>El tamaño que abre o cierra una etiqueta del texto del HUD: solo
        /// interesa `&lt;size=N&gt;` y su cierre.</summary>
        private static int RunSize(string tag, int current)
        {
            if (tag.StartsWith("size=", System.StringComparison.Ordinal))
                return int.Parse(tag.Substring(5), CultureInfo.InvariantCulture);
            if (tag.StartsWith("/size", System.StringComparison.Ordinal))
                return GameFontModel.CardBodySize;
            return current;
        }

        private static string Px(float v)
            => v.ToString("0.#", CultureInfo.InvariantCulture) + " px";

        /// <summary>Ancho VISIBLE de una línea: fuera las etiquetas de rich text
        /// (el chip del runway lleva su color en <c>&lt;color=…&gt;</c>).</summary>
        private static string SinTags(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            bool dentro = false;
            foreach (char ch in s)
            {
                if (ch == '<') dentro = true;
                else if (ch == '>') dentro = false;
                else if (!dentro) sb.Append(ch);
            }
            return sb.ToString();
        }

        private static HudToastsModel.Toast Toast(string key, string text, ulong tick)
            => new(new GameStreamParser.AlertView(key, level: 1, colonyId: 0,
                x: -1f, y: -1f, tick: tick, text: text));

        // ————— puentes de UI: repro de presets y verificación del guardado —————

        [Fact]
        public void Preset_SeedPoolPath_SeExtraeDelReproCanonico()
        {
            // F4.3: la UI siembra el juego con la ruta que el PROPIO preset declara
            // en su repro — sin hardcodear rutas de pools en la capa de presentación.
            var p = new PoolPickerModel.Preset
            {
                Id = "warm-v2",
                ReproCommand = "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm-v2.antgenome",
            };
            Assert.Equal("artifacts/pretrain-warm-v2.antgenome", p.ResolveSeedPoolPath());
            Assert.Equal("artifacts/pretrain-warm-v2.antgenome", p.ResolveSeedPoolPath()); // estable

            var natural = new PoolPickerModel.Preset { Id = "natural", ReproCommand = "antsim --mode game --seed <N>" };
            Assert.Null(natural.ResolveSeedPoolPath()); // sin siembra: partida natural
        }

        [Fact]
        public void HistorialComandos_ComandoVerify_DesdeElModelo()
        {
            // §4: el botón lanza el verify del CLI (el oráculo) — el modelo produce
            // el comando con checkpoint y log; el resultado ✓/✗ lo da su exit code.
            var model = new CommandHistoryModel();
            model.Observe(TickFrom(events: "[11,0,1,0,0,0]")); // SaveGame
            Assert.True(model.CanReplayFromSave);
            Assert.Equal("antsim --mode verify --load partida.antsave --antlog partida.antlog",
                model.BuildVerifyCommand("partida.antsave", "partida.antlog"));
            Assert.Equal("antsim --mode verify --load partida.antsave",
                model.BuildVerifyCommand("partida.antsave"));
        }

        [Fact]
        public void StreamSource_ResuelveElEjecutablePublicado()
        {
            // El campo CliPath es multiplataforma ("build/antsim"): el ejecutable
            // vive DENTRO de la carpeta de publicación con el nombre del último
            // segmento. En Windows además lleva .exe (UseShellExecute=false no
            // lo añade solo) — el Play no debe fallar en silencio por un sufijo.
            bool win = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "antsim-test-" + System.Guid.NewGuid().ToString("N")[..8]);
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string exe = System.IO.Path.Combine(dir, "antsim" + (win ? ".exe" : ""));
                System.IO.File.WriteAllText(exe, "");
                var src = new AntSim.Unity.Scripts.Streaming.StreamSource(
                    System.IO.Path.Combine(dir, "antsim"));
                Assert.Equal(exe, src.CliPath);
            }
            finally { System.IO.Directory.Delete(dir, recursive: true); }
        }
    }
}
