using System;
using System.IO;
using System.Linq;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2b.5 — la tarjeta de vista del multi-visor muestra la cadena del
/// saqueo: modelo PURO (<see cref="ColonyCardModel"/>) alimentado con un
/// stream REAL de la partida de invasión canónica (el mismo comando del
/// 5º pin CI, check-invasion-command.sh). El render compacto debe contener
/// la línea `raids` SOLO para la colonia beligerante; la presa pacífica
/// nunca la muestra (en mundos sin Eciton el bloque raids no existe).
/// Es el test headless del criterio que el probe del editor verifica vivo.
/// </summary>
public class RaidViewCardTests
{
    private static string RepoFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, ".git"))
               && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent!;
        return Path.Combine(dir!.FullName, "tests", "fixtures", name);
    }

    [Fact]
    public void TarjetaCompacta_MuestraRaidsSoloDeLaBeligerante()
    {
        // La partida canónica de invasión: Lasius (colonia 0, presa) vs Eciton
        // (colonia 1, SEMBRADA con warm-v2 — transferencia validada §8quater).
        // Con las constantes V6 hay strikes garantizados en seed 42.
        string fixture = RepoFixture("warm-v2.antgenome");
        Assert.True(File.Exists(fixture), $"fixture ausente: {fixture}");

        string stream = GameScenario.Run(42, ticks: 7200, colonies: 2, grid: 96,
            frameEvery: 30, seedPoolPath: fixture, drops: null,
            leafFraction: 0f,
            species: new[] { World.SpeciesDescriptor.LasiusNiger, World.SpeciesDescriptor.Eciton });

        var parser = new GameStreamParser();
        var cards = new ColonyCardModel();

        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;
            cards.Observe(v);
        }

        var presa = cards.Cards.Values.FirstOrDefault(c => c.ColonyId == 0);
        var eciton = cards.Cards.Values.FirstOrDefault(c => c.ColonyId == 1);

        Assert.NotNull(presa);
        Assert.NotNull(eciton);

        // Canal C: golpes acumulados SOLO en la beligerante (la presa pacífica
        // con ContactRadius 0 no puede infligir ninguno).
        Assert.True(eciton!.Strikes > 0, $"colonia 1: Strikes acumulados > 0 (saldos: presa={presa!.Strikes}, eciton={eciton.Strikes})");
        Assert.Equal(0, presa!.Strikes);
        Assert.Equal(0, presa.RaidInflows);

        // Render compacto: línea raids SOLO en la Eciton.
        string compact = cards.RenderCompact();
        Assert.Contains("raids ", compact);
        Assert.Contains(" al nido", compact);

        // Mundo clásico (sin Eciton): NINGUNA línea de saqueo.
        string classic = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
            frameEvery: 400, seedPoolPath: null, drops: null, leafFraction: 0f, species: null);
        var p2 = new GameStreamParser();
        var c2 = new ColonyCardModel();
        foreach (var line in classic.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = p2.ParseLine(line.Trim());
            if (v == null) continue;
            c2.Observe(v);
        }
        Assert.DoesNotContain("raids ", c2.RenderCompact());
    }
}
