using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntSim.Core.Evolution;
using AntSim.Core.Scenario;
using AntSim.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2c rodaja 7 (cierre) — el ciclo completo del mundo NEAT, verificado en la
/// suite: pool entrenado con arena → exportación v2 (writer canonizado) →
/// siembra de un mundo de invasión con --seed-pool v2 → hash determinista →
/// canal F emitiendo el grafo del cerebro NEAT en vivo. Es la versión en-suite
/// del 6º pin de CI (check-neat-command.sh, hash 021ed04f…).
/// </summary>
public class NeatClosureProbe
{
    private readonly ITestOutputHelper _out;

    public NeatClosureProbe(ITestOutputHelper output) => _out = output;

    private static string RepoFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, ".git"))
               && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent!;
        return Path.Combine(dir!.FullName, "tests", "fixtures", name);
    }

    [Fact]
    public void CicloCompleto_Entrena_Exporta_Siembra()
    {
        // — 1. El fixture del cierre: pool NEAT exportado por el CLI (--neat,
        //      warm-v2 semilla, currículo 3 etapas × 4 gens) — el MISMO archivo
        //      que fija el 6º pin de CI.
        string path = RepoFixture("pretrain-neat.antgenome");
        Assert.True(File.Exists(path), "falta tests/fixtures/pretrain-neat.antgenome");
        Assert.Equal(2, AntGenomeFile.PeekFormatVersion(File.ReadAllBytes(path)));
        var (_, poolGenomes) = AntGenomeFile.ReadNeatFile(path, AntSim.Core.Brain.BrainContract.CurrentVersion);
        _out.WriteLine($"pool v2 del fixture: {poolGenomes.Count} genomas");
        Assert.Equal(64, poolGenomes.Count);

        // — 2. Mundo de invasión sembrado con pool NEAT —
        string stream = GameScenario.Run(42UL, 600, colonies: 2, grid: 96, frameEvery: 30,
            seedPoolPath: path,
            species: new[] { AntSim.Core.World.SpeciesDescriptor.LasiusNiger, AntSim.Core.World.SpeciesDescriptor.Eciton });

        Assert.Contains("\"seedPool\"", stream);
        string? hash = null;
        foreach (var line in stream.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            int i = t.IndexOf("\"hash\":\"", StringComparison.Ordinal);
            if (i >= 0) hash = t.Substring(i + 8, 64);
        }
        Assert.NotNull(hash);
        _out.WriteLine($"mundo de invasión con pool NEAT: hash {hash[..12]}…");

        // — 3. Determinismo: misma partida, mismo hash —
        string stream2 = GameScenario.Run(42UL, 600, colonies: 2, grid: 96, frameEvery: 30,
            seedPoolPath: path,
            species: new[] { AntSim.Core.World.SpeciesDescriptor.LasiusNiger, AntSim.Core.World.SpeciesDescriptor.Eciton });
        Assert.Equal(stream, stream2);
        _out.WriteLine("determinismo: dos partidas byte a byte idénticas ✓");

        // — 4. El canal F del inspeccionado emite grafo NEAT (visible en vivo) —
        string withF = GameScenario.Run(42UL, 600, colonies: 2, grid: 96, frameEvery: 30,
            seedPoolPath: path,
            species: new[] { AntSim.Core.World.SpeciesDescriptor.LasiusNiger, AntSim.Core.World.SpeciesDescriptor.Eciton },
            inspectId: 1, activEvery: 60);
        Assert.Contains("\"graph\":{", withF);
        _out.WriteLine("canal F emite grafo NEAT en el mundo sembrado ✓");
    }
}
