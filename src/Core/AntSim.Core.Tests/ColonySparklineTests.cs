using System.Collections.Generic;
using System.IO;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — gráfica de reserva por colonia. Lo que se fija aquí es lo que en el
    /// editor solo se juzga con el ojo: la ventana de historia (no «todo desde el
    /// inicio»), el muestreo a 1 Hz (el stream va a 30 Hz y redibujar cada tick
    /// sería trabajo inútil), el relleno de columnas cuando hay pocas muestras y
    /// el umbral bajo del contrato (20%, el mismo de la barra).
    /// </summary>
    public class ColonySparklineTests
    {
        private const int W = 8, H = 4;

        private static byte AlphaAt(byte[] rgba, int width, int x, int y)
            => rgba[(y * width + x) * 4 + 3];

        private static GameStreamParser.TickView Tick(ulong tick, params (int Id, float Stock, float Max)[] colonies)
        {
            var v = new GameStreamParser.TickView { Tick = tick };
            foreach (var c in colonies)
                v.Colonies.Add(new GameStreamParser.ColonyView(
                    c.Id, 0f, 0f, c.Stock, c.Max, 0, 0, 0, 0, 0));
            return v;
        }

        /// <summary>Canal B: `Unload = 5` — un evento por descarga, con su colonia.
        /// Las colonias 0 y 1 van siempre en el canal A para que la muestra exista.</summary>
        private const int KindUnload = 5;

        private static GameStreamParser.TickView TickWithUnloads(ulong tick,
            params (int Colony, int Count)[] unloads)
        {
            var v = Tick(tick, (0, 5f, 10f), (1, 5f, 10f));
            foreach (var u in unloads)
                for (int i = 0; i < u.Count; i++)
                    v.Events.Add(new GameStreamParser.EventView(KindUnload, u.Colony, 1, 0f, 0f, 0));
            return v;
        }

        // ————— serie —————

        [Fact]
        public void SinMuestras_LaTexturaEstáTransparente()
        {
            var m = new ColonySparklineModel();
            var px = m.Render(0, W, H);
            Assert.Equal(W * H * 4, px.Length);
            for (int i = 3; i < px.Length; i += 4) Assert.Equal(0, px[i]);
        }

        [Fact]
        public void Muestreo_AUnSegundoNoEnCadaTick()
        {
            var m = new ColonySparklineModel();
            // 30 ticks de 30 Hz = 1 s de simulación; el resto de ticks no cambia la
            // serie (y la vista no redibuja).
            Assert.False(m.Observe(Tick(29, (0, 5f, 10f))));
            Assert.True(m.Observe(Tick(30, (0, 5f, 10f))));
            Assert.True(m.Observe(Tick(60, (0, 6f, 10f))));
            Assert.False(m.Observe(Tick(61, (0, 7f, 10f))));

            var s = m.For(0);
            Assert.Equal(2, s.Count);
            Assert.Equal(0.5f, s.At(0), 3);
            Assert.Equal(0.6f, s.At(1), 3);
            Assert.Equal(60ul, s.LastTick);
        }

        [Fact]
        public void Anillo_RetieneLasUltimasNoventaMuestras()
        {
            var m = new ColonySparklineModel();
            for (int i = 0; i < ColonySparklineModel.Capacity + 10; i++)
                m.For(0).Push(i / 100f, (ulong)(i * 30));

            var s = m.For(0);
            Assert.Equal(ColonySparklineModel.Capacity, s.Count);
            // Lo más viejo retenido es la muestra 10 (las 10 primeras cayeron).
            Assert.Equal(0.10f, s.At(0), 3);
            Assert.Equal((ColonySparklineModel.Capacity + 9) / 100f, s.Latest, 3);
        }

        [Fact]
        public void ReservaBaja_ConElMismoUmbralQueLaBarra()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.19f, 30);
            Assert.True(m.For(0).Low);
            m.For(0).Push(0.20f, 60);
            Assert.False(m.For(0).Low); // estrictamente por debajo del 20%
        }

        [Fact]
        public void Clear_DejaLasSeriesVacías()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 30);
            m.Clear();
            Assert.Empty(m.SeriesByColony);
            Assert.Equal(0, m.For(0).Count);
        }

        // ————— descargas por ventana (canal B) —————

        [Fact]
        public void Descargas_SeCuentanPorVentanaYPorColonia()
        {
            var m = new ColonySparklineModel();
            m.Observe(TickWithUnloads(0, (0, 1)));          // muestra en t=0, cuenta su evento
            m.Observe(TickWithUnloads(10, (0, 2), (1, 1))); // ventana de la colonia 0
            m.Observe(TickWithUnloads(20, (1, 3)));
            m.Observe(TickWithUnloads(30, (0, 1)));         // segunda muestra

            var c0 = m.For(0);
            var c1 = m.For(1);
            Assert.Equal(2, c0.Count);
            Assert.Equal(1, c0.DropsAt(0));   // la del tick 0, en su propia muestra
            Assert.Equal(3, c0.DropsLatest);  // 2 + 1: la ventana 0..30
            Assert.Equal(0, c1.DropsAt(0));   // la colonia 1 no descargó antes
            Assert.Equal(4, c1.DropsLatest);  // 1 + 3, la misma ventana
        }

        [Fact]
        public void Descargas_ElMismoTickVistoDosVecesNoCuentaDoble()
        {
            // La vista lee `CurrentTick` más de una vez por tick de simulación: sin
            // la guarda, cada descarga se apuntaría dos veces y la barra mentiría.
            var m = new ColonySparklineModel();
            var v = TickWithUnloads(30, (0, 2));
            Assert.True(m.Observe(v));
            Assert.False(m.Observe(v));

            Assert.Equal(1, m.For(0).Count);
            Assert.Equal(2, m.For(0).DropsLatest);
        }

        [Fact]
        public void Descargas_LaVentanaSiguienteEmpiezaACero()
        {
            var m = new ColonySparklineModel();
            m.Observe(TickWithUnloads(30, (0, 3)));
            m.Observe(TickWithUnloads(60));       // ventana sin descargas

            Assert.Equal(3, m.For(0).DropsAt(0));
            Assert.Equal(0, m.For(0).DropsLatest);
        }

        [Fact]
        public void Descargas_TambienSePuedenAportarPorMuestra()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 2, 30);
            m.For(0).Push(0.5f, 30);      // sobrecarga sin descargas: la ventana a 0

            Assert.Equal(2, m.For(0).DropsAt(0));
            Assert.Equal(0, m.For(0).DropsLatest);
            Assert.Equal(2, m.For(0).DropsMax);
        }

        [Fact]
        public void RenderDrops_SinDescargas_EsTransparente()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 30);
            var px = m.RenderDrops(0, W, H);
            for (int i = 3; i < px.Length; i += 4) Assert.Equal(0, px[i]);
        }

        [Fact]
        public void RenderDrops_LaVentanaSeEscalaALaMayorCuenta()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 1, 30);
            m.For(0).Push(0.5f, 4, 60);   // el máximo de la ventana: 4

            var px = m.RenderDrops(0, W, H);
            // 2 muestras en 8 columnas ⇒ columnas 0–3 y 4–7. Altura: 1/4·3 ⇒ 1 y
            // 4/4·3 ⇒ 3, que es la fila de arriba (H−1).
            Assert.NotEqual(0, AlphaAt(px, W, 0, 1));
            Assert.Equal(0, AlphaAt(px, W, 0, 2));
            Assert.NotEqual(0, AlphaAt(px, W, 7, 3));
        }

        [Fact]
        public void RenderDrops_LasBarrasCaenSobreSuMuestra()
        {
            // 3 muestras en 9 columnas: columnas 0–2, 3–5 y 6–8 (el MISMO reparto
            // del área). Si las barras mapearan por su cuenta podrían caer entre
            // dos muestras y la gráfica mentiría sobre qué ventana explica qué.
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 0, 30);
            m.For(0).Push(0.5f, 2, 60);
            m.For(0).Push(0.5f, 0, 90);

            var px = m.RenderDrops(0, 9, 4);
            for (int x = 0; x < 3; x++) Assert.Equal(0, AlphaAt(px, 9, x, 0));
            for (int x = 3; x < 6; x++) Assert.NotEqual(0, AlphaAt(px, 9, x, 3));
            for (int x = 6; x < 9; x++) Assert.Equal(0, AlphaAt(px, 9, x, 0));
        }

        [Fact]
        public void RenderDrops_UnaDescargaPequena_SeVeConUnPixel()
        {
            // Con una banda de 2 px, 1 de 4 descargas redondea a 0 filas y la
            // barra desaparecería: «una descarga» tiene que verse, porque es
            // justo la información que la reserva no da.
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 1, 30);
            m.For(0).Push(0.5f, 4, 60);

            var px = m.RenderDrops(0, 2, 2);
            Assert.NotEqual(0, AlphaAt(px, 2, 0, 0));  // la de 1, al menos la base
            Assert.Equal(0, AlphaAt(px, 2, 0, 1));
            Assert.NotEqual(0, AlphaAt(px, 2, 1, 1));  // la de 4 llega arriba
        }

        // ————— textura —————

        [Fact]
        public void Relleno_SubeHastaLaReservaYBordeMasOpaco()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 30);
            var px = m.Render(0, W, H);

            // 0,5 de una columna de 4 px ⇒ borde en la fila 2 (redondeo de 1,5).
            Assert.Equal(200, AlphaAt(px, W, 0, 0));
            Assert.Equal(200, AlphaAt(px, W, 0, 1));
            Assert.Equal(255, AlphaAt(px, W, 0, 2)); // el borde se marca
            Assert.Equal(0, AlphaAt(px, W, 0, 3));
        }

        [Fact]
        public void MenosMuestrasQueColumnas_NoDejaHuecoALaDerecha()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.5f, 30);
            var px = m.Render(0, W, H);
            for (int x = 0; x < W; x++)
                Assert.Equal(255, AlphaAt(px, W, x, 2)); // misma altura en todas
        }

        [Fact]
        public void MenosMuestrasQueColumnas_SeRepartenPorTodoElAncho()
        {
            // 3 muestras en 9 columnas: cada tercio con su valor. Si en vez de
            // repartirlas se repitiera la última hacia la derecha (o la primera
            // hacia la izquierda), el gráfico saldría como un escalón pegado a un
            // borde y el resto llano.
            var m = new ColonySparklineModel();
            m.For(0).Push(0.25f, 30);
            m.For(0).Push(0.50f, 60);
            m.For(0).Push(1.00f, 90);
            var px = m.Render(0, 9, 5);

            Assert.NotEqual(0, AlphaAt(px, 9, 0, 1));
            Assert.Equal(0, AlphaAt(px, 9, 0, 2));   // col 0 = 0,25 ⇒ fila 1
            Assert.Equal(0, AlphaAt(px, 9, 4, 3));   // col 4 = 0,50 ⇒ fila 2
            Assert.NotEqual(0, AlphaAt(px, 9, 8, 4)); // col 8 = 1,00 ⇒ fila 4
        }

        [Fact]
        public void MasMuestrasQueColumnas_UsaLasUltimas()
        {
            var m = new ColonySparklineModel();
            for (int i = 1; i <= 10; i++) m.For(0).Push(i / 10f, (ulong)(i * 30));
            var px = m.Render(0, 3, 10);

            // Solo caben 3 columnas y deben ser las ÚLTIMAS: 0,8 / 0,9 / 1,0. Con
            // altura 10 el borde cae en la fila round(v·9) ⇒ 7 / 8 / 9, una
            // escalera que solo se da si el orden es el correcto (si se tomaran las
            // primeras serían 0,1 / 0,2 / 0,3, casi planas abajo).
            Assert.NotEqual(0, AlphaAt(px, 3, 0, 7));
            Assert.Equal(0, AlphaAt(px, 3, 0, 8));     // 0,8 no llega a la fila 8
            Assert.NotEqual(0, AlphaAt(px, 3, 1, 8));
            Assert.Equal(0, AlphaAt(px, 3, 1, 9));     // 0,9 no llega a la fila 9
            Assert.NotEqual(0, AlphaAt(px, 3, 2, 9));  // 1,0 sí
        }

        [Fact]
        public void ColoniasSeparadas_CadaUnaConSuSerie()
        {
            var m = new ColonySparklineModel();
            m.For(0).Push(0.9f, 30);
            m.For(1).Push(0.1f, 30);

            var px0 = m.Render(0, W, H);
            var px1 = m.Render(1, W, H);
            Assert.Equal(255, AlphaAt(px0, W, 0, 3)); // 0,9 llega arriba
            Assert.Equal(0, AlphaAt(px1, W, 0, 3));   // 0,1 no
            Assert.True(m.For(1).Low);
            Assert.False(m.For(0).Low);
        }

        // ————— integración: contra el stream real —————

        [Fact]
        public void ConStreamReal_LaSerieSeLlenaYQuedaEnRango()
        {
            string stream = GameScenario.Run(42, ticks: 150, colonies: 2, grid: 32,
                frameEvery: 1);
            var parser = new GameStreamParser();
            var m = new ColonySparklineModel();

            int muestras = 0;
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line.TrimEnd('\r'));
                if (v != null && m.Observe(v)) muestras++;
            }

            Assert.True(muestras >= 4, $"se esperaban ~5 muestras de 1 s, hubo {muestras}");
            for (int id = 0; id < 2; id++)
            {
                var s = m.For(id);
                Assert.True(s.Count >= 4, $"la colonia {id} no acumuló muestras");
                for (int i = 0; i < s.Count; i++)
                    Assert.InRange(s.At(i), 0f, 1f);
            }
        }

        [Fact]
        public void ConStreamReal_LasDescargasSumanLoQueTraeElCanalB()
        {
            // La serie no puede inventar ni perder descargas: la suma de las
            // barras de la ventana es EXACTAMENTE el número de eventos `Unload`
            // que el Core emitió en esos ticks, por colonia. Es el contrato entre
            // el canal B y la gráfica, medido contra el mundo de verdad.
            // El warm-v2 del fixture descarga por primera vez en el tick 3858
            // (medido: 37 descargas en 24000 ticks, grid 256); con 3900 hay al
            // menos una y la ventana de 90 s cabe entera en la serie.
            const int Ticks = 3900;
            string fixture = TestPaths.WarmV2PoolPath();
            Assert.True(File.Exists(fixture), $"fixture ausente: {fixture}");
            string stream = GameScenario.Run(42, ticks: Ticks, colonies: 1, grid: 256,
                frameEvery: 1, seedPoolPath: fixture);
            var parser = new GameStreamParser();
            var m = new ColonySparklineModel();
            var emitidas = new Dictionary<int, int>();
            int desde = Ticks - ColonySparklineModel.Capacity * ColonySparklineModel.SampleEveryTicks;

            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line.TrimEnd('\r'));
                if (v == null) continue;
                foreach (var ev in v.Events)
                {
                    if (ev.Kind != KindUnload) continue;
                    if (v.Tick < (ulong)desde) continue; // fuera de la ventana que se dibuja
                    emitidas.TryGetValue(ev.ColonyId, out int n);
                    emitidas[ev.ColonyId] = n + 1;
                }
                m.Observe(v);
            }

            int total = 0;
            foreach (var kv in emitidas) total += kv.Value;
            Assert.True(total > 0, "la partida no emitió ni una descarga en la ventana: el test no mediría nada");

            var serie = m.For(0);
            int enSerie = 0;
            for (int i = 0; i < serie.Count; i++) enSerie += serie.DropsAt(i);
            emitidas.TryGetValue(0, out int enStream);
            Assert.Equal(enStream, enSerie);
        }
    }
}
