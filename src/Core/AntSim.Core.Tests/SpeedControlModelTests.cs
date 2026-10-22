using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    public class SpeedControlModelTests
    {
        [Fact]
        public void Constants_RangoDe10a160ConBase120()
        {
            Assert.Equal(10.00f, SpeedControlModel.MinSpeed);
            Assert.Equal(160.00f, SpeedControlModel.MaxSpeed);
            Assert.Equal(120.00f, SpeedControlModel.NormalSpeed);
            Assert.Equal(120.00f, SpeedControlModel.DefaultSpeed);
        }

        [Theory]
        [InlineData(0.5f, 10.0f)]    // menor que 10× -> clamp al mínimo
        [InlineData(1.0f, 10.0f)]
        [InlineData(5.0f, 10.0f)]
        [InlineData(10.0f, 10.0f)]
        [InlineData(30.0f, 30.0f)]
        [InlineData(120.0f, 120.0f)]
        [InlineData(160.0f, 160.0f)]
        [InlineData(150.0f, 150.0f)]
        [InlineData(500.0f, 160.0f)] // mayor que 160× -> clamp al máximo
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
            Assert.Equal(10.0f, SpeedControlModel.ClampSpeed(0f, allowPause: false));
        }

        [Theory]
        [InlineData(10.0f, 8.3f)]
        [InlineData(30.0f, 25f)]
        [InlineData(60.0f, 50f)]
        [InlineData(90.0f, 75f)]
        [InlineData(120.0f, 100f)]
        [InlineData(160.0f, 133.3f)]
        public void SpeedToPercent_ConvierteCorrectamente(float speed, float percent)
        {
            Assert.Equal(percent, SpeedControlModel.SpeedToPercent(speed));
        }

        [Theory]
        [InlineData(8.3f, 10.0f)]
        [InlineData(25f, 30.0f)]
        [InlineData(50f, 60.0f)]
        [InlineData(75f, 90.0f)]
        [InlineData(100f, 120.0f)]
        [InlineData(133.4f, 160.0f)] // 160,08× → clamp al máximo
        [InlineData(5f, 10.0f)]    // clamp min
        [InlineData(2000f, 160.0f)] // clamp max
        public void PercentToSpeed_ConvierteYLimita(float percent, float speed)
        {
            Assert.Equal(speed, SpeedControlModel.PercentToSpeed(percent));
        }

        [Fact]
        public void StepUp_Y_StepDown_RecorrenPresets()
        {
            float speed = SpeedControlModel.MinSpeed; // 10×
            Assert.Equal(10.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(30.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(60.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(90.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(120.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(160.0f, speed);

            // En el tope se queda en 160×
            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(160.0f, speed);

            // Bajar
            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(120.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(90.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(60.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(30.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(10.0f, speed);

            // En el piso se queda en 10×
            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(10.0f, speed);
        }

        [Fact]
        public void FormatSpeed_FormatosLegibles()
        {
            Assert.Equal("EN PAUSA", SpeedControlModel.FormatSpeed(0f));
            Assert.Equal("8% (10×)", SpeedControlModel.FormatSpeed(10.0f));
            Assert.Equal("25% (30×)", SpeedControlModel.FormatSpeed(30.0f));
            Assert.Equal("50% (60×)", SpeedControlModel.FormatSpeed(60.0f));
            Assert.Equal("100% (120× Base)", SpeedControlModel.FormatSpeed(120.0f));
            Assert.Equal("133% (160×)", SpeedControlModel.FormatSpeed(160.0f));
        }

        [Fact]
        public void FormatPercent_EtiquetasCortas()
        {
            Assert.Equal("0%", SpeedControlModel.FormatPercent(0f));
            Assert.Equal("8%", SpeedControlModel.FormatPercent(10.0f));
            Assert.Equal("100%", SpeedControlModel.FormatPercent(120.0f));
            Assert.Equal("133%", SpeedControlModel.FormatPercent(160.0f));
        }
    }
}
