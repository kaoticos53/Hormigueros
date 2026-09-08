using System;
using AntSim.Core.Pheromone;
using Xunit;

namespace AntSim.Core.Tests;

public class PheromoneLayerTests
{
    [Fact]
    public void Deposit_CapsAtCellMax()
    {
        var layer = new PheromoneLayer(64, 64, cellMax: 1f);
        for (int i = 0; i < 10; i++)
            layer.Deposit(10, 10, 0.5f);

        Assert.Equal(1f, layer[10, 10]);
    }

    [Fact]
    public void Deposit_OutsideBounds_IsIgnored()
    {
        var layer = new PheromoneLayer(64, 64);
        Assert.False(layer.Deposit(-1, 0, 1f));
        Assert.False(layer.Deposit(0, 64, 1f));
        Assert.Equal(0f, layer[-1, 0]);
        Assert.Equal(0f, layer[0, 64]);
    }

    [Fact]
    public void Evaporation_IsExponential()
    {
        var layer = new PheromoneLayer(64, 64);
        layer.Deposit(32, 32, 1f);

        // λ = ln2/10 (vida media 10 s) durante dt = 1 s → factor = 2^(−0.1)
        layer.Evaporate(1f, PheromoneDefaults.LambdaFromHalfLife(10f));

        float expected = MathF.Pow(2f, -0.1f);
        AssertClose(expected, layer[32, 32]);
    }

    [Fact]
    public void Evaporate_OneSecondOfSteps_EqualsSingleBigStep_WithinTolerance()
    {
        // La evaporación exponencial es estable e independiente de la partición del dt.
        var discrete = new PheromoneLayer(64, 64);
        discrete.Deposit(32, 32, 1f);
        float lambda = 0.1f;
        for (int i = 0; i < 30; i++) discrete.Evaporate(1f / 30f, lambda);

        var single = new PheromoneLayer(64, 64);
        single.Deposit(32, 32, 1f);
        single.Evaporate(1f, lambda);

        AssertClose(discrete[32, 32], single[32, 32], tolerance: 1e-3f);
    }

    [Fact]
    public void Diffuse_IsDeterministic_AndStable()
    {
        var a = new PheromoneLayer(96, 96);
        var b = new PheromoneLayer(96, 96);
        DepositPattern(a);
        DepositPattern(b);

        for (int i = 0; i < 50; i++)
        {
            a.Diffuse(0.25f);
            b.Diffuse(0.25f);
        }

        for (int i = 0; i < 96 * 96; i++)
        {
            // Determinismo bit a bit: leer vía indexador comparando bits.
            int x = i % 96, y = i / 96;
            Assert.Equal(BitConverter.SingleToInt32Bits(a[x, y]),
                         BitConverter.SingleToInt32Bits(b[x, y]));
            Assert.True(a[x, y] >= 0f);
        }
    }

    [Fact]
    public void Diffuse_NeverIncreasesMaximum()
    {
        var layer = new PheromoneLayer(96, 96);
        DepositPattern(layer);

        float maxBefore = Max(layer);
        for (int i = 0; i < 30; i++) layer.Diffuse(0.25f);
        float maxAfter = Max(layer);

        Assert.True(maxAfter <= maxBefore + 1e-6f);
    }

    [Fact]
    public void Deposit_BumpsOnlyItsTileVersion()
    {
        // Tile de 64 celdas → grid 128 tiene 2×2 tiles.
        var layer = new PheromoneLayer(128, 128);
        Assert.Equal(2, layer.TilesX);
        Assert.Equal(2, layer.TilesY);
        Assert.Equal(0u, layer.TileVersion(0, 0));

        layer.Deposit(10, 10, 0.3f);      // tile (0,0)
        Assert.Equal(1u, layer.TileVersion(0, 0));
        Assert.Equal(0u, layer.TileVersion(1, 0));
        Assert.Equal(0u, layer.TileVersion(0, 1));

        layer.Deposit(70, 10, 0.3f);      // tile (1,0)
        Assert.Equal(1u, layer.TileVersion(1, 0));

        layer.Deposit(10, 70, 0.3f);      // tile (0,1)
        Assert.Equal(1u, layer.TileVersion(0, 1));
    }

    [Fact]
    public void MutationCounter_IsMonotonic()
    {
        var layer = new PheromoneLayer(64, 64);
        ulong before = layer.MutationCount;
        layer.Deposit(5, 5, 0.1f);
        Assert.True(layer.MutationCount > before);
        layer.Evaporate(1f, 0.1f);
        layer.Diffuse(0.1f);
        Assert.True(layer.MutationCount >= before);
    }

    private static void DepositPattern(PheromoneLayer layer)
    {
        layer.Deposit(48, 48, 1f);
        layer.Deposit(30, 40, 0.6f);
        layer.Deposit(70, 30, 0.8f);
    }

    private static float Max(PheromoneLayer layer)
    {
        float m = 0f;
        int w = layer.Width, h = layer.Height;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                m = Math.Max(m, layer[x, y]);
        return m;
    }

    private static void AssertClose(float expected, float actual, float tolerance = 1e-4f)
        => Assert.True(Math.Abs(expected - actual) < tolerance,
            $"Esperado {expected}, obtenido {actual}");
}
