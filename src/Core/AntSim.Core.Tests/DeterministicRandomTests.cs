using AntSim.Core.Sim;
using Xunit;

namespace AntSim.Core.Tests;

public class DeterministicRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new DeterministicRandom(42UL);
        var b = new DeterministicRandom(42UL);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
        Assert.Equal(a.State, b.State);
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentSequence()
    {
        var a = new DeterministicRandom(1UL);
        var b = new DeterministicRandom(2UL);
        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Fork_SameSalt_SameChild_IndependentOfOrder()
    {
        var parent1 = new DeterministicRandom(7UL);
        var parent2 = new DeterministicRandom(7UL);

        var child1 = parent1.Fork(0xABCDUL);
        var child2 = parent2.Fork(0xABCDUL);

        for (int i = 0; i < 500; i++)
            Assert.Equal(child1.NextUInt64(), child2.NextUInt64());
    }

    [Fact]
    public void Fork_DifferentSalts_DifferentChildren()
    {
        var parent = new DeterministicRandom(7UL);
        var childA = parent.Fork(1UL);
        var childB = parent.Fork(2UL);
        Assert.NotEqual(childA.NextUInt64(), childB.NextUInt64());
    }

    [Fact]
    public void StateRoundTrip_ContinuesSequenceBitExact()
    {
        var original = new DeterministicRandom(99UL);
        for (int i = 0; i < 5; i++) original.NextUInt64();

        var restored = DeterministicRandom.FromState(
            original.State.S0, original.State.S1, original.State.S2, original.State.S3);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(original.NextUInt64(), restored.NextUInt64());
    }

    [Fact]
    public void NextInt_StaysWithinRange_AndIsDeterministic()
    {
        var a = new DeterministicRandom(5UL);
        var b = new DeterministicRandom(5UL);
        for (int i = 0; i < 10_000; i++)
        {
            int va = a.NextInt(-3, 5);
            int vb = b.NextInt(-3, 5);
            Assert.Equal(va, vb);
            Assert.InRange(va, -3, 4);
        }
    }

    [Fact]
    public void NextDouble01_IsInRange()
    {
        var rng = new DeterministicRandom(123UL);
        for (int i = 0; i < 10_000; i++)
        {
            double d = rng.NextDouble01();
            Assert.InRange(d, 0.0, 1.0);
        }
    }
}
