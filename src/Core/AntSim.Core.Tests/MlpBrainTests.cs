using System;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using Xunit;

namespace AntSim.Core.Tests;

public class MlpBrainTests
{
    [Fact]
    public void WeightCount_MatchesTopology()
    {
        int[] sizes = { 19, 8, 6 };
        // 8·20 (entrada→oculta) + 6·9 (oculta→salida)
        Assert.Equal(8 * 20 + 6 * 9, MlpBrain.ExpectedWeightCount(sizes));
    }

    [Fact]
    public void Constructor_RejectsWrongWeightCount()
    {
        Assert.Throws<ArgumentException>(() => new MlpBrain(new[] { 19, 8, 6 }, new float[1]));
    }

    [Fact]
    public void Evaluate_IsDeterministic_AcrossInstances()
    {
        int[] sizes = { 19, 8, 6 };
        float[] weights = BuildWeights(sizes, 12345);
        var brainA = new MlpBrain(sizes, weights);
        var brainB = new MlpBrain(sizes, weights);

        var sensors = AntSensors.Default();
        sensors.FoodTrailCenter = 0.9f;
        sensors.HomeTrailCenter = 0.2f;
        sensors.Energy = 0.5f;

        var a = AntDecision.Neutral();
        var b = AntDecision.Neutral();
        brainA.Evaluate(in sensors, ref a);
        brainB.Evaluate(in sensors, ref b);

        Assert.Equal(BitConverter.SingleToInt32Bits(a.Steer), BitConverter.SingleToInt32Bits(b.Steer));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.Speed), BitConverter.SingleToInt32Bits(b.Speed));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.DepositFood), BitConverter.SingleToInt32Bits(b.DepositFood));
        Assert.Equal(BitConverter.SingleToInt32Bits(a.Interact), BitConverter.SingleToInt32Bits(b.Interact));
    }

    [Fact]
    public void Evaluate_IsRepeatable_OnSameInstance()
    {
        int[] sizes = { 19, 8, 6 };
        var brain = new MlpBrain(sizes, BuildWeights(sizes, 99));
        var sensors = AntSensors.Default();
        sensors.FoodTrailCenter = 0.5f;

        var first = AntDecision.Neutral();
        var second = AntDecision.Neutral();
        brain.Evaluate(in sensors, ref first);
        brain.Evaluate(in sensors, ref second);

        Assert.Equal(BitConverter.SingleToInt32Bits(first.Steer), BitConverter.SingleToInt32Bits(second.Steer));
    }

    [Fact]
    public void ZeroWeights_ProduceExpectedBaseline()
    {
        int[] sizes = { 19, 8, 6 };
        var brain = new MlpBrain(sizes, new float[MlpBrain.ExpectedWeightCount(sizes)]);
        var sensors = AntSensors.Default();
        var d = AntDecision.Neutral();
        brain.Evaluate(in sensors, ref d);

        // tanh(0) = 0 (steer); sigmoid(0) = 0.5 para el resto.
        Assert.Equal(0f, d.Steer);
        AssertClose(0.5f, d.Speed);
        AssertClose(0.5f, d.DepositFood);
        AssertClose(0.5f, d.DepositHome);
        AssertClose(0.5f, d.DepositAlarm);
        AssertClose(0.5f, d.Interact);
    }

    [Fact]
    public void Outputs_AreAlwaysFinite_AndInRange()
    {
        int[] sizes = { 19, 8, 6 };
        var brain = new MlpBrain(sizes, BuildWeights(sizes, 7));
        var rng = new AntSim.Core.Sim.DeterministicRandom(3UL);

        for (int trial = 0; trial < 200; trial++)
        {
            var sensors = AntSensors.Default();
            sensors.FoodTrailCenter = rng.NextFloat01();
            sensors.HomeTrailCenter = rng.NextFloat01();
            sensors.Energy = rng.NextFloat01();
            sensors.HasLoad = rng.NextBool() ? 1f : 0f;

            var d = AntDecision.Neutral();
            brain.Evaluate(in sensors, ref d);

            Assert.True(d.AllFinite());
            Assert.InRange(d.Steer, -1f, 1f);
            Assert.InRange(d.Speed, 0f, 1f);
            Assert.InRange(d.DepositFood, 0f, 1f);
            Assert.InRange(d.Interact, 0f, 1f);
        }
    }

    private static float[] BuildWeights(int[] sizes, int seed)
    {
        float[] w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        var rng = new AntSim.Core.Sim.DeterministicRandom((ulong)seed);
        for (int i = 0; i < w.Length; i++)
            w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        return w;
    }

    private static void AssertClose(float expected, float actual)
    {
        Assert.True(Math.Abs(expected - actual) < 1e-5f,
            $"Esperado {expected}, obtenido {actual}");
    }
}
