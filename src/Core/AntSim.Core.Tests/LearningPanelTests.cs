using System;
using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3ter — panel de aprendizaje del HUD (curva de fitness por generación +
/// cobertura del mundo) verificado HEADLESS contra streams REALES del Core.
///
/// La regla del contrato §6.4 es que la UI no deriva: la forma de la curva, el
/// texto de la tarjeta y el color por cobertura se calculan en un modelo puro. Si
/// el stream cambia (bloque `learning`/`fitcurve`), estos tests son los que avisan
/// antes de que la escena enseñe algo que no es.
/// </summary>
public class LearningPanelTests
{
    private static GameStreamParser.TickView UltimoBloque(string stream)
    {
        var parser = new GameStreamParser();
        GameStreamParser.TickView? last = null;
        foreach (var line in stream.Split('\n'))
        {
            var v = parser.ParseLine(line.TrimEnd('\r'));
            if (v != null && v.Learning.Count > 0) last = v;
        }
        Assert.NotNull(last);
        return last!;
    }

    private static string Stream(int ticks = 3600, int colonies = 2, int grid = 96,
        string? seedPool = null)
        => GameScenario.Run(42, ticks: ticks, colonies: colonies, grid: grid,
            frameEvery: ticks, seedPoolPath: seedPool);

    /// <summary>Ruta absoluta al fixture (los tests corren desde bin/).</summary>
    private static string Fixture(string name)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, ".git"))
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, "tests", "fixtures", name);
    }

    // ————— 1. el stream trae el bloque y el parser lo entiende —————

    [Fact]
    public void ElStreamTraeAprendizajeYCobertura_PorColonia()
    {
        var view = UltimoBloque(Stream());
        Assert.Equal(2, view.Learning.Count);

        foreach (var l in view.Learning)
        {
            Assert.InRange(l.ColonyId, 0, 1);
            Assert.Equal(0, l.Generation);                  // 3600 ticks: aún generación 0
            Assert.True(l.Ants > 0);
            Assert.True(l.MeanFitness > 0f, "una cohorte viva ya tiene fitness");
            Assert.True(l.BestFitness >= l.MeanFitness);
            Assert.Equal(96 * 96, l.TotalCells);
            Assert.True(l.VisitedCells > 0, "algo de mundo ha pisado");
            Assert.InRange(l.Coverage, 0.0001f, 1f);
            Assert.True(l.MaxDistance > 0);
        }

        // La curva llega aparte y trae un punto por colonia (la generación en curso).
        Assert.Equal(2, view.FitnessCurve.Count);
    }

    [Fact]
    public void LaCobertura_CreceConLaPartida_YElFitnessTambien()
    {
        var parser = new GameStreamParser();
        string stream = Stream(ticks: 6000, colonies: 1);
        var first = new List<GameStreamParser.LearningView>();
        var last = new List<GameStreamParser.LearningView>();
        foreach (var line in stream.Split('\n'))
        {
            var v = parser.ParseLine(line.TrimEnd('\r'));
            if (v == null || v.Learning.Count == 0) continue;
            if (first.Count == 0) first.Add(v.Learning[0]);
            last.Clear();
            last.Add(v.Learning[0]);
        }

        Assert.True(last[0].VisitedCells > first[0].VisitedCells,
            $"cobertura {first[0].VisitedCells} → {last[0].VisitedCells}");
        Assert.True(last[0].MeanFitness >= first[0].MeanFitness);
    }

    [Fact]
    public void LaElite_LlegaCuandoElPoolAprendio()
    {
        // Colonia 0 sembrada con el pool pre-entrenado: trae élite desde el tick 0.
        // Colonia 1 nace fría: sin nadie muerto, élite 0 (el bloque no miente).
        var view = UltimoBloque(Stream(ticks: 2400, seedPool: Fixture("warm-v2.antgenome")));
        var sembrada = view.Learning.Find(l => l.ColonyId == 0);
        var fria = view.Learning.Find(l => l.ColonyId == 1);

        Assert.True(sembrada.EliteBest > 0f, "el pool sembrado trae su élite");
        Assert.True(sembrada.EliteAverage > 0f);
        Assert.Equal(0f, fria.EliteBest);
    }

    // ————— 2. el modelo puro: estado, texto y color —————

    [Fact]
    public void ElModelo_ActualizaPanelYCurva()
    {
        var model = new LearningPanelModel();
        var view = UltimoBloque(Stream(ticks: 2400, colonies: 1));

        Assert.True(model.Observe(view));
        var panel = model.For(0);

        Assert.Equal(1, panel.PointCount);
        Assert.Equal(0, panel.Generation);
        Assert.True(panel.Coverage > 0f);
        Assert.Equal(panel.Coverage < LearningPanelModel.LowCoverage ? (byte)0
            : panel.Coverage < LearningPanelModel.GoodCoverage ? (byte)1 : (byte)2,
            panel.CoverageLevel);

        string fitness = panel.FitnessLine();
        Assert.Contains("gen 0", fitness);
        Assert.Contains("nac.", fitness);
        Assert.Contains("cohorte", fitness);
        string world = panel.WorldLine();
        Assert.Contains("cobertura", world);
        Assert.Contains("celdas", world);
    }

    [Fact]
    public void ElModelo_NoSeRompeSinBloqueDeAprendizaje()
    {
        // Stream viejo (sin `learning`/`fitcurve`) o mundo sin tracker: el panel
        // no inventa datos — Observe devuelve false y no se pinta nada.
        var model = new LearningPanelModel();
        var sinBloque = new GameStreamParser.TickView { Tick = 500 };
        Assert.False(model.Observe(sinBloque));
        Assert.False(model.Observe(null));

        var panel = model.For(0);
        Assert.Equal(0, panel.PointCount);
        Assert.Equal(0f, panel.Coverage);
        // Sin datos: textura del tamaño pedido pero completamente transparente
        // (ni una línea a cero, que mentiría sobre el fitness de la colonia).
        var px = model.Render(0, 8, 4);
        Assert.Equal(8 * 4 * 4, px.Length);
        Assert.All(px, b => Assert.Equal(0, b));
    }

    [Fact]
    public void LaCurva_RetieneLasUltimasGeneraciones_YSeAutoescala()
    {
        var model = new LearningPanelModel();
        // Serie sintética: 60 generaciones con fitness creciente.
        var points = new List<GameStreamParser.FitnessPointView>();
        for (int g = 0; g < 60; g++)
            points.Add(new GameStreamParser.FitnessPointView(0, g, 10 + g, 100f + g, 200f + g));

        var view = new GameStreamParser.TickView();
        view.Tick = 100;
        view.Learning.Add(new GameStreamParser.LearningView(
            0, 59, 600, 10, 0, 159f, 259f, 900, 9216, 300, 428f, 337f));
        view.FitnessCurve.AddRange(points);
        Assert.True(model.Observe(view));

        var panel = model.For(0);
        Assert.Equal(LearningPanelModel.Capacity, panel.PointCount);
        Assert.Equal(59, panel.Points[panel.PointCount - 1].Generation);  // la última
        Assert.True(panel.Improving, "la serie sintética sube");

        // Autoescala: la columna más alta toca el borde superior de la textura.
        var px = model.Render(0, 8, 5);
        Assert.Equal(8 * 5 * 4, px.Length);
        bool tocaTecho = false;
        for (int y = 4; y < 5; y++)
            for (int x = 0; x < 8; x++)
                if (px[(y * 8 + x) * 4 + 3] == 255) tocaTecho = true;
        Assert.True(tocaTecho, "el punto máximo (última generación) llega arriba");
    }
}
