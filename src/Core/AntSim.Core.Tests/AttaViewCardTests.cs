using System;
using System.IO;
using System.Linq;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2a.5 — la tarjeta de vista del multi-visor muestra la cadena de la
/// cortadora: modelo PURO (<see cref="ColonyCardModel"/>) alimentado con un
/// stream REAL de la partida Atta canónica (el mismo comando del pin CI
/// check-atta-command.sh). El render compacto debe contener la línea del
/// hongo para la colonia Atta y la de cortes acumulados; la Lasius ni hongo
/// ni cortes. Es el test headless del criterio que el probe RunSmokeAtta
/// verifica en vivo en el editor.
/// </summary>
public class AttaViewCardTests
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
    public void TarjetaCompacta_MuestraHongoYCortesSoloDeLaAtta()
    {
        string fixture = RepoFixture("warm-v2.antgenome");
        Assert.True(File.Exists(fixture), $"fixture ausente: {fixture}");

        string stream = GameScenario.Run(42, ticks: 7200, colonies: 2, grid: 96,
            frameEvery: 7200, seedPoolPath: fixture, drops: null,
            leafFraction: 1f,
            species: new[] { World.SpeciesDescriptor.Atta, World.SpeciesDescriptor.LasiusNiger });

        var parser = new GameStreamParser();
        var cards = new ColonyCardModel();

        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;
            cards.Observe(v);
        }

        var atta = cards.Cards.Values.FirstOrDefault(c => c.ColonyId == 0);
        var lasius = cards.Cards.Values.FirstOrDefault(c => c.ColonyId == 1);

        Assert.NotNull(atta);
        Assert.NotNull(lasius);

        // Canal A: la Atta tiene el segundo reserve y acumuló hongo.
        Assert.True(atta!.Colony is { FungusMax: > 0f }, "colonia 0: FungusMax > 0");
        Assert.True(atta.Colony!.Value.Fungus > 0f, "colonia 0: fungus acumulado > 0");

        // Canal C: cortes y fungusFed acumulados SOLO en la Atta.
        Assert.True(atta.LeafCuts > 0, "colonia 0: LeafCuts acumulados > 0");
        Assert.True(atta.FungusFed > 0, "colonia 0: FungusFed acumulado > 0");
        Assert.Equal(0, lasius!.LeafCuts);
        Assert.Equal(0, lasius.FungusFed);
        Assert.True(lasius.Colony is { FungusMax: 0f }, "colonia 1: Lasius sin hongo");

        // Render compacto: línea del hongo SOLO en la Atta, cortes solo en ella.
        string compact = cards.RenderCompact();
        Assert.Contains("hongo [", compact);
        Assert.Contains("cortes ", compact);
        Assert.Contains(" · ", compact);

        // Segmentos del modelo puro: la misma granularidad que la reserva.
        Assert.Equal(0, ColonyCardModel.FungusSegments(0f));
        Assert.Equal(10, ColonyCardModel.FungusSegments(1f));
        Assert.Equal(5, ColonyCardModel.FungusSegments(0.5f));
        Assert.Equal(0, ColonyCardModel.FungusSegments(-0.2f)); // clamp

        // Mundo clásico (sin hojas): NINGUNA línea de cortadora aparece.
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
        Assert.DoesNotContain("cortes ", c2.RenderCompact());
        Assert.DoesNotContain("hongo [", c2.RenderCompact());
    }

    [Fact]
    public void ItemView_LeafFields_PopulatedFromCanalA()
    {
        // F5.2a render de hojas: ItemView expone IsLeaf, CutsLeft, CutsInitial
        // para que el presenter pinte mordiscos. Validamos con un stream Atta
        // real que los ítems hoja tengan CutsLeft > 0 y los simples tengan 0.
        string fixture = RepoFixture("warm-v2.antgenome");
        string stream = GameScenario.Run(42, ticks: 7200, colonies: 2, grid: 96,
            frameEvery: 7200, seedPoolPath: fixture, drops: null,
            leafFraction: 1f,
            species: new[] { World.SpeciesDescriptor.Atta, World.SpeciesDescriptor.LasiusNiger });

        var parser = new GameStreamParser();
        var cards = new ColonyCardModel();
        GameStreamParser.TickView? lastTick = null;

        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;
            cards.Observe(v);
            lastTick = v;
        }

        Assert.NotNull(lastTick);
        // Con leafFraction=1, todos los ítems son hojas (CutsLeft > 0).
        Assert.True(lastTick!.Items.Count > 0, "debe haber ítems en el último tick");
        foreach (var item in lastTick.Items)
        {
            Assert.True(item.IsLeaf, $"item {item.Id}: debe ser hoja con leafFraction=1");
            Assert.True(item.CutsLeft > 0, $"item {item.Id}: CutsLeft debe ser > 0");
            Assert.True(item.CutsInitial > 0, $"item {item.Id}: CutsInitial debe ser > 0");
            Assert.True(item.CutsLeft <= item.CutsInitial,
                $"item {item.Id}: CutsLeft ({item.CutsLeft}) no puede exceder CutsInitial ({item.CutsInitial})");
        }
    }

    [Fact]
    public void ItemView_SimpleItem_NoLeafFields()
    {
        // Mundo sin hojas: los ítems simples tienen CutsLeft=0, IsLeaf=false.
        string stream = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
            frameEvery: 400, seedPoolPath: null, drops: null, leafFraction: 0f, species: null);

        var parser = new GameStreamParser();
        GameStreamParser.TickView? lastTick = null;
        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v != null) lastTick = v;
        }

        Assert.NotNull(lastTick);
        Assert.True(lastTick!.Items.Count > 0);
        foreach (var item in lastTick.Items)
        {
            Assert.False(item.IsLeaf, $"item {item.Id}: no debe ser hoja sin leafFraction");
            Assert.Equal(0, item.CutsLeft);
            Assert.Equal(0, item.CutsInitial);
        }
    }
}
