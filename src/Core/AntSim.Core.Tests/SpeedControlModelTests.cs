using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    public class SpeedControlModelTests
    {
        [Fact]
        public void Constants_RangoDe10a1000PorcientoSobreBase10()
        {
            Assert.Equal(1.00f, SpeedControlModel.MinSpeed);
            Assert.Equal(100.00f, SpeedControlModel.MaxSpeed);
            Assert.Equal(10.00f, SpeedControlModel.NormalSpeed);
            Assert.Equal(10.00f, SpeedControlModel.DefaultSpeed);
        }

        [Theory]
        [InlineData(0.5f, 1.0f)]     // menor que 10% base (1.0x) -> clamp a 1.0x
        [InlineData(1.0f, 1.0f)]
        [InlineData(5.0f, 5.0f)]
        [InlineData(10.0f, 10.0f)]
        [InlineData(50.0f, 50.0f)]
        [InlineData(100.0f, 100.0f)]
        [InlineData(150.0f, 100.0f)] // mayor que 1000% base (100x) -> clamp a 100x
        public void ClampSpeed_LimitaEstrictamente(float input, float expected)
        {
            float result = SpeedControlModel.ClampSpeed(input, allowPause: false);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ClampSpeed_PermitePausaSiSeEspecifica()
        {
            Assert.Equal(0f, SpeedControlModel.ClampSpeed(0f, allowPause: true));
            Assert.Equal(0f, SpeedControlModel.ClampSpeed(-1f, allowPause: true));
            Assert.Equal(1.0f, SpeedControlModel.ClampSpeed(0f, allowPause: false));
        }

        [Theory]
        [InlineData(1.0f, 10f)]
        [InlineData(5.0f, 50f)]
        [InlineData(10.0f, 100f)]
        [InlineData(25.0f, 250f)]
        [InlineData(50.0f, 500f)]
        [InlineData(100.0f, 1000f)]
        public void SpeedToPercent_ConvierteCorrectamente(float speed, float percent)
        {
            Assert.Equal(percent, SpeedControlModel.SpeedToPercent(speed));
        }

        [Theory]
        [InlineData(10f, 1.0f)]
        [InlineData(50f, 5.0f)]
        [InlineData(100f, 10.0f)]
        [InlineData(250f, 25.0f)]
        [InlineData(500f, 50.0f)]
        [InlineData(1000f, 100.0f)]
        [InlineData(5f, 1.0f)]    // clamp min
        [InlineData(2000f, 100.0f)] // clamp max
        public void PercentToSpeed_ConvierteYLimita(float percent, float speed)
        {
            Assert.Equal(speed, SpeedControlModel.PercentToSpeed(percent));
        }

        [Fact]
        public void StepUp_Y_StepDown_RecorrenPresets()
        {
            float speed = SpeedControlModel.MinSpeed; // 1.0x
            Assert.Equal(1.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(2.5f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(5.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(10.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(25.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(50.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(100.0f, speed);

            // En el tope se queda en 100.0
            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(100.0f, speed);

            // Bajar
            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(50.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(25.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(10.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(5.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(2.5f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(1.0f, speed);

            // En el piso se queda en 1.0
            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(1.0f, speed);
        }

        [Fact]
        public void FormatSpeed_FormatosLegibles()
        {
            Assert.Equal("EN PAUSA", SpeedControlModel.FormatSpeed(0f));
            Assert.Equal("10% (1×)", SpeedControlModel.FormatSpeed(1.0f));
            Assert.Equal("50% (5×)", SpeedControlModel.FormatSpeed(5.0f));
            Assert.Equal("100% (10× Base)", SpeedControlModel.FormatSpeed(10.0f));
            Assert.Equal("500% (50×)", SpeedControlModel.FormatSpeed(50.0f));
            Assert.Equal("1000% (100×)", SpeedControlModel.FormatSpeed(100.0f));
        }

        [Fact]
        public void FormatPercent_EtiquetasCortas()
        {
            Assert.Equal("0%", SpeedControlModel.FormatPercent(0f));
            Assert.Equal("10%", SpeedControlModel.FormatPercent(1.0f));
            Assert.Equal("100%", SpeedControlModel.FormatPercent(10.0f));
            Assert.Equal("1000%", SpeedControlModel.FormatPercent(100.0f));
        }
    }
}
