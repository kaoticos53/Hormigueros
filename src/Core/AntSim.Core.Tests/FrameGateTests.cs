using System;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.3 — la puerta de PÍXELES, verificada headless con frames sintéticos.
    /// Es la misma implementación que usan la sonda del editor (F5.1) y la del
    /// player empaquetado: si el defecto del frame blanco vuelve, tiene que
    /// fallar AQUÍ (sin abrir Unity) antes de fallar en un Play pass.
    ///
    /// Los tres casos que importan:
    ///   · tierra + hormigas      → pasa;
    ///   · tierra SIN hormigas    → suspende (el mundo se pintó, pero sin bichos);
    ///   · blanco por todas partes → suspende (el defecto real del F5.1: el quad
    ///     de feromonas opaco tapaba suelo y hormigas con el estado en verde).
    /// </summary>
    public class FrameGateTests
    {
        // Paleta de la escena (la que pintan los materiales del bootstrapper).
        private static readonly Rgb Earth = new(107, 84, 59);
        private static readonly Rgb Ant = new(76, 46, 26);
        private static readonly Rgb Carrier = new(196, 128, 64);
        private static readonly Rgb Item = new(96, 150, 72);
        private static readonly Rgb White = new(255, 255, 255);

        private const int W = 64;
        private const int H = 48;

        [Fact]
        public void FrameDeTierraConHormigas_Pasa()
        {
            byte[] frame = Solid(Earth);
            PaintRect(frame, x0: 50, x1: 53, y0: 4, y1: 7, Ant);   // 4×4 = 16 px

            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.True(r.Brown);
            Assert.Equal(16, r.AntPx);
            Assert.Equal(0, r.CarrierPx);
            Assert.True(r.Ok);
            Assert.Equal("ok", r.Verdict);
        }

        [Fact]
        public void FrameDeTierraSinHormigas_Suspende()
        {
            byte[] frame = Solid(Earth);

            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.True(r.Brown);                     // el terreno está bien…
            Assert.Equal(0, r.AntPx);                 // …pero no hay ni una hormiga
            Assert.False(r.Ok);
            Assert.Equal("sin hormigas en pantalla", r.Verdict);
        }

        [Fact]
        public void FrameBlanco_Suspende_ElDefectoDelF51()
        {
            byte[] frame = Solid(White);

            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.False(r.Brown);                    // r≈g≈b: no es tierra
            Assert.Equal(0, r.AntPx);
            Assert.False(r.Ok);
            Assert.Equal("terreno no-tierra y sin hormigas", r.Verdict);
        }

        [Fact]
        public void CadaFamiliaDeColor_SeCuentaEnSuCasilla()
        {
            byte[] frame = Solid(Earth);
            PaintRect(frame, 2, 5, 30, 32, Ant);          // 4×3 = 12 hormigas
            PaintRect(frame, 10, 12, 30, 32, Carrier);    // 3×3 = 9 portadoras
            PaintRect(frame, 18, 20, 30, 32, Item);       // 3×3 = 9 ítems

            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.Equal(12, r.AntPx);
            Assert.Equal(9, r.CarrierPx);
            Assert.Equal(9, r.ItemPx);
        }

        [Fact]
        public void LaCantidadDeNegro_SeMideSobreElTotal()
        {
            byte[] frame = Solid(Earth);
            PaintRect(frame, 0, W - 1, 0, H / 2 - 1, new Rgb(10, 10, 10)); // media pantalla

            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.Equal(W * H, r.TotalPx);
            Assert.InRange(r.DarkFraction, 0.45f, 0.55f);
        }

        [Fact]
        public void ElFondoEsLaEsquina_NoElSuelo()
        {
            byte[] frame = Solid(Earth);
            // La esquina (2,2) es del fondo de la escena, no del tablero: el
            // muestreo del suelo es 7×7 DENTRO del mundo (0.15–0.85).
            Assert.True(FrameGate.Analyze(frame, W, H, Ant, Carrier, Item).Brown);

            var sky = new Rgb(20, 21, 26);
            PaintRect(frame, 0, 4, 0, 4, sky);
            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.Equal(sky, r.Background);
            Assert.True(r.Brown);                       // el suelo sigue siendo tierra
        }

        [Fact]
        public void UnColorQueNoEsNingunaFamilia_NoSeCuenta()
        {
            // Gris neutro: ni tierra, ni hormiga, ni portadora, ni ítem (dista >12
            // de todos). Es la defensa contra el umbral ancho, que contaba el
            // tablero entero como hormigas.
            byte[] frame = Solid(new Rgb(200, 200, 200));

            var r = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);

            Assert.Equal(0, r.AntPx);
            Assert.Equal(0, r.CarrierPx);
            Assert.Equal(0, r.ItemPx);
        }

        [Fact]
        public void LaVentanaDeMuestreo_CambiaLoQueSeMide()
        {
            // Tablero estrecho (x 30..34) sobre el fondo: con la ventana por
            // defecto la «mediana del suelo» es el FONDO; con una ventana centrada
            // en la banda, es el tablero. Es el defecto que obligó a calcular la
            // ventana del encuadre real en el player: una vista 16:9 deja el
            // tablero en su 54 % central y la ventana fija de 0.15–0.85 caía medio
            // fuera (mediana = fondo, falso suspenso).
            var sky = new Rgb(56, 45, 33);
            byte[] frame = Solid(sky);
            PaintRect(frame, 30, 34, 0, H - 1, Earth);

            var wide = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);
            var narrow = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item,
                FrameWindow.Centered(0.05f, 0.4f));

            Assert.Equal(sky, wide.Floor);
            Assert.Equal(Earth, narrow.Floor);
        }

        [Fact]
        public void ElFondoDeclarado_NoSeCuentaComoHormigas()
        {
            // El fondo del multi-visor (56,45,33) dista 12 del tono de hormiga: si
            // no está en la paleta, cada píxel de fondo entra en la familia de
            // hormigas. El caso que lo destapa es un frame en el que el TABLERO
            // cubre la esquina (la sonda deduce el fondo de ahí): el fondo de
            // verdad se queda fuera de la paleta y cae en el tono más cercano,
            // que es el de hormiga.
            var sky = new Rgb(56, 45, 33);
            byte[] frame = Solid(Earth);
            PaintRect(frame, 58, W - 1, 0, H - 1, sky);   // margen derecho
            PaintRect(frame, 0, W - 1, 44, H - 1, sky);   // margen superior

            var sinDeclarar = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item);
            var declarado = FrameGate.Analyze(frame, W, H, Ant, Carrier, Item,
                backgroundOverride: sky);

            Assert.True(sinDeclarar.AntPx > 0);   // el defecto, medido
            Assert.Equal(0, declarado.AntPx);     // declarado: fondo, no hormigas
            Assert.False(declarado.Ok);
            Assert.True(declarado.Brown);         // el tablero se sigue leyendo tierra
        }

        [Fact]
        public void BufferCorto_Lanza()
        {
            byte[] frame = new byte[W * H * 3 - 1];
            Assert.Throws<ArgumentException>(() => FrameGate.Analyze(frame, W, H, Ant, Carrier, Item));
        }

        [Fact]
        public void DimensionesInvalidas_Lanzan()
        {
            byte[] frame = new byte[W * H * 3];
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameGate.Analyze(frame, 0, H, Ant, Carrier, Item));
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameGate.Analyze(frame, W, -1, Ant, Carrier, Item));
        }

        // ── Utilidades ──────────────────────────────────────────────────────────

        private static byte[] Solid(Rgb c)
        {
            var frame = new byte[W * H * 3];
            for (int i = 0; i < W * H; i++) Set(frame, i, c);
            return frame;
        }

        /// <summary>Rectángulo INCLUSIVO en coordenadas (x,y) del buffer.</summary>
        private static void PaintRect(byte[] frame, int x0, int x1, int y0, int y1, Rgb c)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Set(frame, y * W + x, c);
        }

        private static void Set(byte[] frame, int index, Rgb c)
        {
            frame[index * 3] = c.R;
            frame[index * 3 + 1] = c.G;
            frame[index * 3 + 2] = c.B;
        }
    }
}
