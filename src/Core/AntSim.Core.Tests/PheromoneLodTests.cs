using System;
using System.IO;
using AntSim.Core.Pheromone;
using AntSim.Core.Serialization;
using AntSim.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3 rodaja 3 — LOD de difusión por bloques con soporte.
///
/// La afirmación que estos tests defienden NO es "el LOD aproxima bien": es que
/// el LOD es EXACTO. La difusión del proyecto nunca llena una celda nula, así que
/// las celdas nulas no pueden cambiar y visitarlas era trabajo puro. El test
/// central hace lo que ninguna cifra de rendimiento puede: correr la MISMA
/// secuencia de operaciones por los dos caminos (LOD encendido y la
/// implementación de referencia a grid completo) y comparar celda a celda.
/// </summary>
public class PheromoneLodTests
{
    private readonly ITestOutputHelper _out;

    public PheromoneLodTests(ITestOutputHelper output) => _out = output;

    /// <summary>RNG propio del test: la secuencia no debe depender de nada del mundo.</summary>
    private sealed class Lcg
    {
        private ulong _s;
        public Lcg(ulong seed) => _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        public int Next(int max)
        {
            _s = _s * 6364136223846793005UL + 1442695040888963407UL;
            return (int)((_s >> 33) % (ulong)max);
        }
        public float NextFloat() => (Next(1_000_000) / 1_000_000f);
    }

    private static int SoporteTotal(WorldSim sim)
    {
        int n = 0;
        foreach (var c in sim.Colonies)
            n += c.FoodLayer.NonZeroCells + c.HomeLayer.NonZeroCells
                 + c.AlarmLayer.NonZeroCells + c.FootprintLayer.NonZeroCells;
        return n;
    }

    private static void AssertSameEstado(PheromoneLayer a, PheromoneLayer b, string momento)
    {
        Assert.Equal(a.Width, b.Width);
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                // Bit a bit, no con tolerancia: `==` sobre float ya distingue
                // −0/NaN y aquí lo que se afirma es identidad exacta.
                if (a[x, y] != b[x, y])
                    Assert.Fail($"celda ({x},{y}) diverge {momento}: LOD={a[x, y]:R} referencia={b[x, y]:R}");
            }
        }
        Assert.Equal(b.NonZeroCells, a.NonZeroCells);
        Assert.Equal(b.CountNonZeroCells(), a.NonZeroCells);
        Assert.Equal(b.MutationCount, a.MutationCount);
        for (int t = 0; t < a.TilesX * a.TilesY; t++)
            Assert.Equal(b.TileVersion(t % a.TilesX, t / a.TilesX), a.TileVersion(t % a.TilesX, t / a.TilesX));
    }

    [Fact]
    public void Lod_EsBitExactoFrenteAlGridCompleto()
    {
        const int w = 96, h = 96;
        var lod = new PheromoneLayer(w, h);
        var referencia = new PheromoneLayer(w, h) { LodEnabled = false };
        var rng = new Lcg(20260918UL);

        // Tres focos de depósito (nido + dos "senderos") y un goteo disperso:
        // es la forma del rastro real, no ruido uniforme.
        int[] focosX = { 20, 70, 48 };
        int[] focosY = { 48, 24, 80 };

        for (int paso = 0; paso < 400; paso++)
        {
            int op = rng.Next(10);
            // Los operandos se sortean UNA vez y se aplican a las dos capas: si
            // cada una consumiera su propio número aleatorio, compararíamos dos
            // secuencias distintas y el test no probaría nada (el primer intento
            // cayó justo en eso).
            if (op < 4)
            {
                int f = rng.Next(3);
                int x = Math.Clamp(focosX[f] + rng.Next(9) - 4, 0, w - 1);
                int y = Math.Clamp(focosY[f] + rng.Next(9) - 4, 0, h - 1);
                float q = 0.05f + rng.NextFloat() * 0.3f;
                lod.Deposit(x, y, q);
                referencia.Deposit(x, y, q);
            }
            else if (op < 6)
            {
                int x = rng.Next(w), y = rng.Next(h);
                float q = 0.02f + rng.NextFloat() * 0.5f;
                lod.Deposit(x, y, q);
                referencia.Deposit(x, y, q);
            }
            else if (op < 8)
            {
                float dt = 0.5f + rng.NextFloat();
                lod.Evaporate(dt, PheromoneDefaults.LambdaPerSecond(PheromoneKind.FoodTrail));
                referencia.Evaporate(dt, PheromoneDefaults.LambdaPerSecond(PheromoneKind.FoodTrail));
            }
            else
            {
                float k = 0.06f + rng.NextFloat() * 0.19f;
                lod.Diffuse(k);
                referencia.Diffuse(k);
            }

            if (paso % 7 == 0) AssertSameEstado(lod, referencia, $"en el paso {paso}");
        }

        AssertSameEstado(lod, referencia, "al cierre");
        Assert.True(lod.ActiveBlocks > 0, "el escenario debía dejar soporte activo");
    }

    [Fact]
    public void Lod_SinSoporte_NoVisitaNingunaCelda()
    {
        var layer = new PheromoneLayer(64, 64);
        Assert.Equal(0, layer.NonZeroCells);
        Assert.Equal(0, layer.ActiveBlocks);

        layer.Diffuse(0.10f);
        layer.Evaporate(1f, PheromoneDefaults.LambdaPerSecond(PheromoneKind.FoodTrail));

        Assert.Equal(0, layer.ActiveRegionCells);
        Assert.Equal(0ul, layer.MutationCount); // ni un cambio: el mundo no cambia

        layer.Deposit(10, 10, 1f);
        layer.Diffuse(0.10f);
        Assert.True(layer.ActiveRegionCells > 0);
    }

    [Fact]
    public void Lod_ElBloqueSeDesactivaCuandoElRastroSeAgota()
    {
        var layer = new PheromoneLayer(64, 64);
        layer.Deposit(40, 40, 1e-40f); // subnormal: se agota al evaporar
        Assert.Equal(1, layer.NonZeroCells);

        Assert.Equal(1, layer.ActiveBlocks);

        // λ = 700 s⁻¹ ⇒ factor e^−700: la primera evaporación ya lo lleva a 0
        // (1e-40 no sobrevive ni al primer factor), así que el bloque se apaga
        // en esa misma pasada.
        layer.Evaporate(1f, lambdaPerSecond: 700f);

        for (int i = 0; i < 4 && layer.NonZeroCells > 0; i++)
            layer.Evaporate(1f, lambdaPerSecond: 700f);

        Assert.Equal(0, layer.NonZeroCells);
        Assert.Equal(0, layer.ActiveBlocks);
        Assert.Equal(0, layer.CountNonZeroCells());

        // Una operación más, ya sin soporte: no visita ni una celda.
        layer.Diffuse(0.10f);
        Assert.Equal(0, layer.ActiveRegionCells);
    }

    [Fact]
    public void Lod_ReduceCeldasVisitadas_EnUnMundoReal()
    {
        const int grid = 256;
        var sim = new WorldSim(42, grid, colonyCount: 2);
        for (int i = 0; i < 1500; i++) sim.Step();

        int total = 0;
        int visitadas = 0;
        int soporte = 0;
        foreach (var colony in sim.Colonies)
        {
            foreach (var layer in new[] { colony.FoodLayer, colony.HomeLayer, colony.AlarmLayer, colony.FootprintLayer })
            {
                total += layer.CellCount;
                visitadas += layer.ActiveRegionCells;
                soporte += layer.NonZeroCells;
                Assert.Equal(layer.CountNonZeroCells(), layer.NonZeroCells);
            }
        }

        double fraccion = visitadas / (double)total;
        _out.WriteLine($"grid {grid}² · celdas por pasada {total} · visitadas {visitadas} " +
                       $"({fraccion:P1}) · con soporte {soporte}");
        Assert.True(fraccion < 0.5,
            $"el LOD debía visitar menos de la mitad del grid en un mundo real; visitó {fraccion:P1}");
        Assert.True(soporte < total,
            "el rastro debe ser más fino que el grid completo, o el LOD no tiene de dónde ahorrar");
    }

    [Fact]
    public void Lod_ElCheckpointReconstruyeElSoporteYNoMueveElMundo()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lod-{Guid.NewGuid():N}.antsave");
        try
        {
            var original = new WorldSim(1234, 96, colonyCount: 2);
            // Hasta que el mundo TENGA rastro: con el grid inicialmente limpio el
            // test de checkpoint sería vacuo (y el primer intento lo asumió, mal).
            int guard = 0;
            while (SoporteTotal(original) == 0 && guard++ < 6000) original.Step();
            Assert.True(SoporteTotal(original) > 0, "el mundo debía generar rastro en 6000 ticks");
            WorldSimSave.Save(original, path);

            var cargado = WorldSimSave.Load(path);
            for (int c = 0; c < cargado.Colonies.Count; c++)
            {
                var a = original.Colonies[c];
                var b = cargado.Colonies[c];
                foreach (var (la, lb) in new[]
                         {
                             (a.FoodLayer, b.FoodLayer), (a.HomeLayer, b.HomeLayer),
                             (a.AlarmLayer, b.AlarmLayer), (a.FootprintLayer, b.FootprintLayer),
                         })
                {
                    // El soporte no viaja en el checkpoint: se deriva de los valores.
                    Assert.Equal(la.CountNonZeroCells(), lb.NonZeroCells);
                    Assert.Equal(la.NonZeroCells, lb.NonZeroCells);
                    Assert.Equal(la.ActiveBlocks, lb.ActiveBlocks);
                    // `ActiveRegionCells` es la foto de la ÚLTIMA operación, así que
                    // no es comparable entre un mundo vivo y uno recién cargado;
                    // lo que sí debe cumplir el cargado es que su región cubra su
                    // soporte y no pase del grid.
                    Assert.InRange(lb.ActiveRegionCells, lb.NonZeroCells, lb.CellCount);
                }
            }

            // Y la prueba que importa: 100 ticks más por los dos caminos, mismo mundo.
            for (int i = 0; i < 100; i++)
            {
                original.Step();
                cargado.Step();
            }
            Assert.Equal(original.HashLine(), cargado.HashLine());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
