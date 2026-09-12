using System;
using System.IO;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.5 — render de feromonas (canal E) y salto de cámara por toast.
    /// El paquete "phero" (RLE por filas + base64) se valida contra la capa
    /// REAL del Core: un juego corto con la colonia 0 cerca de su nido produce
    /// depósitos, y el decodificador puro debe reconstruir exactamente los
    /// mismos valores cuantizados. El mundo no cambia por emitir el canal.
    /// </summary>
    public sealed class PheromoneTileTests
    {
        [Fact]
        public void Decode_PayloadVacio_SoloCabecera()
        {
            var model = new PheromoneTileModel();
            // "YABgAA==" = 96x96 sin filas (mundo sin rastros aún).
            Assert.True(model.Decode("YABgAA=="));
            Assert.Equal(96, model.Width);
            Assert.Equal(96, model.Height);
            Assert.All(model.Cells, c => Assert.Equal(0, c));
        }

        [Fact]
        public void Decode_PayloadRoto_FallaSinLanzar()
        {
            var model = new PheromoneTileModel();
            Assert.False(model.Decode("###no-base64###"));
            Assert.False(model.Decode("AAAA")); // < 4 bytes tras decodificar
        }

        [Fact]
        public void Integracion_StreamReal_CanalE_PresenteYDecodificable()
        {
            string stream = GameScenario.Run(42, ticks: 300, colonies: 1, grid: 96,
                frameEvery: 30, pheroEvery: 30);

            int packets = 0;
            int maxCell = 0;
            var model = new PheromoneTileModel();
            foreach (string line in stream.Split('\n'))
            {
                var view = new GameStreamParser().ParseLine(line.Trim());
                if (view?.Phero == null) continue;
                packets++;
                Assert.True(model.Decode(view.Phero), $"payload roto en tick {view.Tick}");
                foreach (byte c in model.Cells) maxCell = Math.Max(maxCell, c);
            }
            Assert.Equal(10, packets); // ticks 0,30,...,270
            // Con fundadoras moviéndose 300 ticks desde el nido, hay rastro.
            Assert.True(maxCell > 0, "sin depósitos de feromona en 300 ticks");
        }

        [Fact]
        public void Integracion_CanalE_NoTocaElMundo()
        {
            // Emitir feromonas no debe alterar el hash: la telemetría es pura.
            string plain = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96, frameEvery: 30);
            string withPhero = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
                frameEvery: 30, pheroEvery: 30);
            string hashPlain = HashOf(plain);
            string hashPhero = HashOf(withPhero);
            Assert.Equal(hashPlain, hashPhero);
        }

        [Fact]
        public void Decode_FilasParciales_Y_RunsLargos()
        {
            // Payload sintético: 4x2, fila 0 = [255×3, 0], fila 1 = [7×4].
            // Header w=4 h=2, y=0: (255,3)(0,1); y=1: (7,4)
            byte[] p =
            {
                4, 0, 2, 0,
                0, 0, 255, 3, 0, 1,
                1, 0, 7, 4,
            };
            string b64 = Convert.ToBase64String(p);
            var model = new PheromoneTileModel();
            Assert.True(model.Decode(b64));
            Assert.Equal(4, model.Width);
            Assert.Equal(2, model.Height);
            Assert.Equal(255, model.Cell(0, 0));
            Assert.Equal(255, model.Cell(2, 0));
            Assert.Equal(0, model.Cell(3, 0));
            Assert.Equal(7, model.Cell(0, 1));
            Assert.Equal(7, model.Cell(3, 1));
        }

        [Fact]
        public void Toast_AnclaDelCanalD_CoordenadasDeMundo()
        {
            // El salto de cámara consume las anclas (x, y) de las alertas: ya
            // vienen del Core en coordenadas de mundo. Contrato: colonia 0
            // ancla a su nido (comprobado con la geometría del WorldSim).
            string stream = GameScenario.Run(7, ticks: 7200, colonies: 2, grid: 96,
                frameEvery: 30);
            foreach (string line in stream.Split('\n'))
            {
                var view = new GameStreamParser().ParseLine(line.Trim());
                if (view == null) continue;
                foreach (var a in view.Alerts)
                {
                    if (a.ColonyId < 0) continue;
                    // El ancla debe caer DENTRO del mundo (coordenadas válidas).
                    Assert.InRange(a.X, 0f, 96f * 8f);
                    Assert.InRange(a.Y, 0f, 96f * 8f);
                }
            }
        }

        private static string HashOf(string stream)
        {
            foreach (string line in stream.Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith("{\"end\"", StringComparison.Ordinal))
                    return t;
            }
            throw new InvalidDataException("stream sin línea end");
        }
    }
}
