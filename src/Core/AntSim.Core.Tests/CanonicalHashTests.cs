using AntSim.Core.Serialization;
using Xunit;

namespace AntSim.Core.Tests;

public class CanonicalHashTests
{
    [Fact]
    public void FloatBits_RoundTrip_IsBitExact()
    {
        float[] samples = { 0f, 1f, -1f, 0.1f, 1.0f / 3.0f, float.MaxValue, float.Epsilon, -123.456f };
        foreach (float f in samples)
        {
            uint bits = FloatBits.ToUInt32(f);
            float back = FloatBits.FromUInt32(bits);
            Assert.Equal(FloatBits.ToUInt32(f), FloatBits.ToUInt32(back));
        }
    }

    [Fact]
    public void SameInputs_ProduceSameHash()
    {
        string HashOne() => HashABCFloat(1.5f, 2.25f, 42UL);
        string HashTwo() => HashABCFloat(1.5f, 2.25f, 42UL);

        Assert.Equal(HashOne(), HashTwo());
    }

    [Fact]
    public void DifferentInputs_ProduceDifferentHash()
    {
        string h1 = HashABCFloat(1.5f, 2.25f, 42UL);
        string h2 = HashABCFloat(1.5f, 2.26f, 42UL);
        Assert.NotEqual(h1, h2);
    }

    private static string HashABCFloat(float a, float b, ulong c)
    {
        using var h = new CanonicalHasher();
        h.AppendFloat(a);
        h.AppendFloat(b);
        h.AppendUInt64(c);
        return h.FinalizeHex();
    }
}
