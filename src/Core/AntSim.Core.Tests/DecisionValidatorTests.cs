using AntSim.Core.Contracts;
using AntSim.Core.Validation;
using Xunit;

namespace AntSim.Core.Tests;

public class DecisionValidatorTests
{
    [Fact]
    public void CleanDecision_PassesThrough_Unchanged()
    {
        var raw = new AntDecision
        {
            Steer = 0.3f,
            Speed = 0.7f,
            DepositFood = 0.2f,
            DepositHome = 0f,
            DepositAlarm = 0f,
            Interact = 0.1f
        };

        bool valid = DecisionValidator.SanitizeAndClamp(in raw, out AntDecision clean);

        Assert.True(valid);
        Assert.Equal(raw.Steer, clean.Steer);
        Assert.Equal(raw.Speed, clean.Speed);
        Assert.Equal(raw.DepositFood, clean.DepositFood);
        Assert.Equal(raw.Interact, clean.Interact);
    }

    [Fact]
    public void NaN_IsSanitized_ToNeutral_AndMarkedInvalid()
    {
        var raw = new AntDecision { Steer = float.NaN, Speed = 1f };
        bool valid = DecisionValidator.SanitizeAndClamp(in raw, out AntDecision clean);

        Assert.False(valid);
        Assert.Equal(0f, clean.Steer);
        Assert.Equal(0f, clean.Speed);
        Assert.Equal(0f, clean.DepositFood);
        Assert.Equal(0f, clean.Interact);
    }

    [Fact]
    public void Infinity_IsSanitized()
    {
        var raw = new AntDecision { DepositAlarm = float.PositiveInfinity };
        bool valid = DecisionValidator.SanitizeAndClamp(in raw, out AntDecision clean);

        Assert.False(valid);
        Assert.Equal(0f, clean.DepositAlarm);
    }

    [Theory]
    [InlineData(5f, 1f)]      // steer se recorta a [−1,1]
    [InlineData(-5f, -1f)]
    [InlineData(-0.3f, -0.3f)] // dentro de rango: sin cambios
    public void Steer_IsClamped(float input, float expected)
    {
        var raw = new AntDecision { Steer = input };
        DecisionValidator.SanitizeAndClamp(in raw, out AntDecision clean);
        Assert.Equal(expected, clean.Steer);
    }

    [Fact]
    public void SpeedAndDeposits_ClampToZeroOne()
    {
        var raw = new AntDecision
        {
            Speed = -0.3f,
            DepositFood = 2f,
            DepositHome = -1f,
            DepositAlarm = 1.5f,
            Interact = -2f
        };
        DecisionValidator.SanitizeAndClamp(in raw, out AntDecision clean);

        Assert.Equal(0f, clean.Speed);
        Assert.Equal(1f, clean.DepositFood);
        Assert.Equal(0f, clean.DepositHome);
        Assert.Equal(1f, clean.DepositAlarm);
        Assert.Equal(0f, clean.Interact);
    }

    [Theory]
    [InlineData(0.5f, true)]
    [InlineData(0.4999f, false)]
    public void InteractionThreshold_IsHalf(float interact, bool expected)
    {
        var d = new AntDecision { Interact = interact };
        Assert.Equal(expected, DecisionValidator.WantsInteraction(in d));
    }
}
