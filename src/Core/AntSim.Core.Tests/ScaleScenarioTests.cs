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
}
