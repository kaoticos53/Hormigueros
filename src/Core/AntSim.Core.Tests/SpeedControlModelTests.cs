using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    public class SpeedControlModelTests
    {
        [Fact]
        public void Constants_RangoDe30a1000Porciento()
        {
            Assert.Equal(0.30f, SpeedControlModel.MinSpeed);
            Assert.Equal(10.00f, SpeedControlModel.MaxSpeed);
            Assert.Equal(1.00f, SpeedControlModel.NormalSpeed);
        }

        [Theory]
        [InlineData(0.1f, 0.3f)]     // menor que 30% -> clamp a 30% (0.3x)
        [InlineData(0.3f, 0.3f)]
        [InlineData(1.0f, 1.0f)]
        [InlineData(5.0f, 5.0f)]
        [InlineData(10.0f, 10.0f)]
        [InlineData(15.0f, 10.0f)]   // mayor que 1000% -> clamp a 1000% (10x)
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
            Assert.Equal(0.3f, SpeedControlModel.ClampSpeed(0f, allowPause: false));
        }

        [Theory]
        [InlineData(0.3f, 30f)]
        [InlineData(0.5f, 50f)]
        [InlineData(1.0f, 100f)]
        [InlineData(3.0f, 300f)]
        [InlineData(10.0f, 1000f)]
        public void SpeedToPercent_ConvierteCorrectamente(float speed, float percent)
        {
            Assert.Equal(percent, SpeedControlModel.SpeedToPercent(speed));
        }

        [Theory]
        [InlineData(30f, 0.3f)]
        [InlineData(50f, 0.5f)]
        [InlineData(100f, 1.0f)]
        [InlineData(300f, 3.0f)]
        [InlineData(1000f, 10.0f)]
        [InlineData(10f, 0.3f)]   // clamp min
        [InlineData(2000f, 10.0f)] // clamp max
        public void PercentToSpeed_ConvierteYLimita(float percent, float speed)
        {
            Assert.Equal(speed, SpeedControlModel.PercentToSpeed(percent));
        }

        [Fact]
        public void StepUp_Y_StepDown_RecorrenPresets()
        {
            float speed = SpeedControlModel.MinSpeed; // 0.3x
            Assert.Equal(0.3f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(0.5f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(1.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(2.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(3.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(5.0f, speed);

            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(10.0f, speed);

            // En el tope se queda en 10.0
            speed = SpeedControlModel.StepUp(speed);
            Assert.Equal(10.0f, speed);

            // Bajar
            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(5.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(3.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(2.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(1.0f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(0.5f, speed);

            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(0.3f, speed);

            // En el piso se queda en 0.3
            speed = SpeedControlModel.StepDown(speed);
            Assert.Equal(0.3f, speed);
        }

        [Fact]
        public void FormatSpeed_FormatosLegibles()
        {
            Assert.Equal("EN PAUSA", SpeedControlModel.FormatSpeed(0f));
            Assert.Equal("30% (0.3×)", SpeedControlModel.FormatSpeed(0.3f));
            Assert.Equal("100% (1×)", SpeedControlModel.FormatSpeed(1.0f));
            Assert.Equal("300% (3×)", SpeedControlModel.FormatSpeed(3.0f));
            Assert.Equal("1000% (10×)", SpeedControlModel.FormatSpeed(10.0f));
        }

        [Fact]
        public void FormatPercent_EtiquetasCortas()
        {
            Assert.Equal("0%", SpeedControlModel.FormatPercent(0f));
            Assert.Equal("30%", SpeedControlModel.FormatPercent(0.3f));
            Assert.Equal("100%", SpeedControlModel.FormatPercent(1.0f));
            Assert.Equal("1000%", SpeedControlModel.FormatPercent(10.0f));
        }
    }
}
