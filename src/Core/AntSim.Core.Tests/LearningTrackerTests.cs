using System;
using AntSim.Core.Telemetry;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3ter — aprendizaje observable (canal C): la curva de fitness por generación y
/// la cobertura del mundo que la colonia ha pisado.
///
/// Lo que se fija aquí: que la telemetría es PURA (observar no cambia el mundo ni
/// su hash), que la contabilidad de fitness por generación es EXACTA (la media es
/// la de todas las hormigas de la cohorte, no solo las supervivientes), que la
/// curva existe desde el primer tick —una curva que esperase bajas estaría vacía
/// toda la partida corta— y que la cobertura crece con el tráfico.
/// </summary>
public class LearningTrackerTests
{
    private static WorldSim NewWorld(ulong seed = 42UL, int grid = 96, int colonies = 2)
        => new(seed, grid, colonies);

    [Fact]
    public void Observar_NoMueveElMundo()
    {
        // La regla dura del canal C: la telemetría lee, no escribe. Si el tracker
        // consumiera RNG o mutara el mundo, todos los pines de hash de CI caerían.
        var a = NewWorld();
        var b = NewWorld();
        var learning = new LearningTracker();
        for (int i = 0; i < 1200; i++)
        {
            a.Step();
            b.Step();
            learning.Observe(b);
            if (i % 120 == 0) learning.Snapshot(b);   // también leer es puro
        }
        Assert.Equal(a.HashLine(), b.HashLine());
    }

    [Fact]
    public void MediaDeLaGeneracion_EsLaDeTodasSusHormigas()
    {
        // Sin bajas y con menos de 64 nacimientos, TODAS las hormigas son de la
        // generación 0: la media del tracker tiene que ser exactamente la media de
        // la fitness actual de las vivas (la contabilidad por deltas no pierde ni
        // duplica nada).
        var sim = NewWorld(seed: 7UL, grid: 48, colonies: 1);
        var learning = new LearningTracker();
        for (int i = 0; i < 900; i++)
        {
            sim.Step();
            learning.Observe(sim);
        }

        var view = learning.Snapshot(sim)[0];
        Assert.True(view.Births > 0 && view.Births < LearningTracker.BirthsPerGeneration,
            $"nacimientos {view.Births} deberían estar en la generación 0");
        Assert.Equal(0, view.Generation);
        Assert.Equal(0, view.GenerationDead);

        double suma = 0;
        foreach (var ant in sim.Colonies[0].Adults) suma += ant.Fitness;
        int n = sim.Colonies[0].Adults.Count;
        Assert.True(n > 0);
        Assert.Equal(suma / n, view.GenerationMean, 4);
        Assert.Equal(n, view.GenerationAnts);
    }

    [Fact]
    public void LaCurva_TienePuntoDesdeElPrimerTick_YParaCadaColonia()
    {
        var sim = NewWorld(seed: 3UL, grid: 48, colonies: 2);
        var learning = new LearningTracker();
        for (int i = 0; i < 600; i++)
        {
            sim.Step();
            learning.Observe(sim);
        }

        var views = learning.Snapshot(sim);
        Assert.Equal(2, views.Count);
        foreach (var v in views)
        {
            Assert.Single(v.Curve);                       // la generación 0, en curso
            var p = v.Curve[0];
            Assert.Equal(v.Generation, p.Generation);
            Assert.Equal(v.GenerationAnts, p.Ants);
            Assert.Equal(v.GenerationMean, p.Mean, 4);    // la curva y el bloque cuadran
            Assert.True(p.Mean > 0, "una cohorte viva ya tiene fitness");
        }
    }

    [Fact]
    public void Generacion_EsNacimientosEntreLaCapacidadDeLaElite()
    {
        // La definición operativa de «generación» en un pool de tamaño K: cada K
        // nacimientos toda la élite ha podido ser reemplazada.
        Assert.Equal(64, LearningTracker.BirthsPerGeneration);

        var sim = NewWorld(seed: 11UL, grid: 48, colonies: 1);
        var learning = new LearningTracker();
        for (int i = 0; i < 3000; i++)
        {
            sim.Step();
            learning.Observe(sim);
        }

        var v = learning.Snapshot(sim)[0];
        Assert.Equal(v.Births / LearningTracker.BirthsPerGeneration, v.Generation);
        // Y la curva no crece sin tope: retiene las últimas generaciones.
        Assert.True(v.Curve.Count <= LearningTracker.MaxCurvePoints);
    }

    [Fact]
    public void Cobertura_CreceConElTrafico_YSeAcota()
    {
        var sim = NewWorld(seed: 5UL, grid: 96, colonies: 1);
        var learning = new LearningTracker();

        for (int i = 0; i < 300; i++) { sim.Step(); learning.Observe(sim); }
        var temprana = learning.Snapshot(sim)[0];

        for (int i = 0; i < 4200; i++) { sim.Step(); learning.Observe(sim); }
        var tardia = learning.Snapshot(sim)[0];

        Assert.True(temprana.VisitedCells > 0, "algo de mundo conoce desde el principio");
        Assert.True(tardia.VisitedCells > temprana.VisitedCells, "y conoce más con el tiempo");
        Assert.Equal(sim.GridCells * sim.GridCells, tardia.TotalCells);
        Assert.InRange(tardia.CoverageFraction, 0.0001f, 1f);
        Assert.True(tardia.MaxDistanceFromNest > 0f);
        // La huella es la traza de ESA colonia: con dos colonias, cada una conoce
        // lo suyo (no la unión de las dos).
        Assert.True(tardia.CoverageFraction < 1f, "nadie pisa el mundo entero en 4500 ticks");
    }

    [Fact]
    public void Reset_VaciaLaCuenta()
    {
        var sim = NewWorld(grid: 48, colonies: 1);
        var learning = new LearningTracker();
        for (int i = 0; i < 300; i++) { sim.Step(); learning.Observe(sim); }
        Assert.True(learning.TotalBirths > 0);

        learning.Reset();

        Assert.Equal(0, learning.TotalBirths);
        var v = learning.Snapshot(sim)[0];
        Assert.Equal(0, v.Births);
        Assert.Empty(v.Curve);
    }
}
