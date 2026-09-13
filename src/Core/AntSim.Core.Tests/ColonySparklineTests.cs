using System.Collections.Generic;
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
    }
}
