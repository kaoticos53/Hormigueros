using AntSim.Core.Scenario;
using Xunit;

namespace AntSim.Core.Tests;

public class MicrocosmTests
{
    [Fact]
    public void SameSeed_Twice_ProducesBitIdenticalOutput()
    {
        string run1 = Microcosm.Run(20240908UL, ticks: 600, grid: 64);
        string run2 = Microcosm.Run(20240908UL, ticks: 600, grid: 64);

        Assert.Equal(run1, run2);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentHashes()
    {
        string runA = Microcosm.Run(1UL, ticks: 240, grid: 64);
        string runB = Microcosm.Run(2UL, ticks: 240, grid: 64);

        Assert.NotEqual(runA, runB);
    }

    [Fact]
    public void Run_EmitsMilestoneAndFinalHashes()
    {
        string output = Microcosm.Run(7UL, ticks: 240, grid: 64);
        Assert.Contains("milestones 1", output);
        Assert.Contains("final-hash", output);
        Assert.Contains("tick 120", output);
    }
}
