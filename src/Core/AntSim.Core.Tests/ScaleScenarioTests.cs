using AntSim.Core.Scenario;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3 rodaja 3 — el medidor de escala. Aquí no se comprueba ningún tiempo (eso
/// es una medida de la máquina, no un contrato): se comprueba que el reporte sale
/// con una fila por número de colonias y con las dos columnas que son el
/// veredicto de la rodaja — trabajo de feromonas y llamadas de dibujo —, para que
/// un cambio que las vacíe rompa un test y no el documento.
/// </summary>
public class ScaleScenarioTests
{
    [Fact]
    public void Run_EmiteUnaFilaPorNumeroDeColonias()
    {
        string salida = ScaleScenario.Run(grid: 64, ticks: 120, colonyCounts: new[] { 1, 3 }, seed: 42);

        Assert.Contains("ms/tick", salida);
        Assert.Contains("celdas LOD", salida);
        Assert.Contains("velocidad máx.", salida);
        Assert.DoesNotContain("draw calls", salida); // la vista no viaja al Core
        Assert.Contains("| colonias |", salida);
        Assert.Contains("| 1 |", salida);
        Assert.Contains("| 3 |", salida);
        Assert.DoesNotContain("| 2 |", salida);
    }

    [Fact]
    public void Run_SinLista_UsaElEjePorDefecto()
    {
        string salida = ScaleScenario.Run(grid: 64, ticks: 60, colonyCounts: null, seed: 42);
        foreach (int colonias in ScaleScenario.DefaultColonyCounts)
            Assert.Contains($"| {colonias} |", salida);
    }

    /// <summary>
    /// F5.3 rodaja 3bis — el barrido del bloque del LOD. Lo que se exige del
    /// reporte es que traiga las TRES piezas del equilibrio (celdas visitadas,
    /// reconstrucciones y contadores escaneados) y la fila de referencia sin LOD;
    /// un cambio que borre cualquiera de ellas rompe aquí antes de que la tabla
    /// publicada deje de decir la verdad.
    /// </summary>
    [Fact]
    public void RunLodSweep_EmiteElEquilibrioYLaReferenciaSinLod()
    {
        string salida = ScaleScenario.RunLodSweep(grid: 64, ticks: 120,
            blockSizes: new[] { 4, 32 }, colonies: 1, seed: 42);

        Assert.Contains("celdas visitadas por operación", salida);
        Assert.Contains("contadores escaneados/tick", salida);
        Assert.Contains("ns/reconstrucción", salida);
        Assert.Contains("| 4 |", salida);
        Assert.Contains("| 32 |", salida);
        Assert.Contains("| sin LOD |", salida);
        // La referencia es la implementación a grid completo: 100 % de las celdas.
        Assert.Contains("100.0%", salida);
    }

    /// <summary>Sin lista de bloques, el barrido usa la suya (y sigue emitiendo la
    /// fila de referencia, que no depende de ningún lado de bloque).</summary>
    [Fact]
    public void RunLodSweep_SinLista_UsaElBarridoPorDefecto()
    {
        string salida = ScaleScenario.RunLodSweep(grid: 64, ticks: 60,
            blockSizes: null, colonies: 1, seed: 42);

        foreach (int bloque in ScaleScenario.DefaultLodBlockSizes)
            Assert.Contains($"| {bloque} |", salida);
        Assert.Contains("| sin LOD |", salida);
    }
}
