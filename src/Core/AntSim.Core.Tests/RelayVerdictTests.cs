using System;
using AntSim.Core.Scenario;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// Regla del semáforo de relevo (F4.3): el drop escala con el tamaño del
    /// mundo (190 × grid/96), el tramo es invariante (≥ 60 u). Los valores
    /// centinela salen del smoke real de 5 semillas (docs/fase4-hud-contrato §8).
    /// </summary>
    public sealed class RelayVerdictTests
    {
        [Fact]
        public void Umbrales_Calibrados()
        {
            Assert.Equal(60f, RelayVerdict.CarryLegMin);
            Assert.Equal(190f, RelayVerdict.DropMaxFor(96));   // mundo de calibración
            Assert.Equal(506.67f, RelayVerdict.DropMaxFor(256), 2); // 190 × 256/96
        }

        [Fact]
        public void DatosAusentes_Gris()
        {
            Assert.Equal(RelayLight.Grey, RelayVerdict.Evaluate(null, null, 256));
            Assert.Equal(RelayLight.Grey, RelayVerdict.Evaluate(80f, null, 256));
            Assert.Equal(RelayLight.Grey, RelayVerdict.Evaluate(null, 180f, 256));
        }

        [Fact]
        public void Mundo96_UmbralesOriginales()
        {
            // En el mundo de calibración la regla es la del contrato original.
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(80f, 167.9f, 96)); // warm-v2 sano
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(80f, 190.1f, 96)); // drop fuera
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(59.9f, 167.9f, 96)); // leg corto
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(60f, 190f, 96));   // fronteras OK
        }

        [Fact]
        public void Mundo256_DropEscala_LegInvariante()
        {
            // Centinelas reales del smoke (grid 256) — ver §8 del contrato HUD.
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(64.0f, 182.3f, 256));  // seed 42
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(49.3f, 131.6f, 256));  // seed 7: leg corto
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(85.0f, 190.3f, 256));  // seed 99: antes ámbar por drop fijo
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(174.0f, 240.0f, 256)); // seed 777: antes ámbar por drop fijo
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(71.0f, 177.0f, 256));  // seed 1234

            // El drop SÍ castiga en 256 cuando supera 190 × 256/96.
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(80f, 506.7f, 256));
        }

        [Fact]
        public void GridInvalido_Lanza()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RelayVerdict.DropMaxFor(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RelayVerdict.DropMaxFor(-96));
        }
    }
}
