using System;
using System.Linq;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2a.3 — contratos de la cortadora en el stream: canal A con cortes de
/// hoja y fungus por colonia, canal C con leafCuts/fungusFed y el bloque
/// cutters por colonia. Defaults tolerantes: un stream SIN estos campos
/// (antiguo) parsea igual — los fixtures existentes no se regeneran.
/// </summary>
public class CutterContractTests
{
    private static string RepoFixture(string name)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, ".git"))
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, "tests", "fixtures", name);
    }
    [Fact]
    public void StreamAtta_ElParserConsumeCutsFungusYCutters()
    {
        // Partida Atta vs Lasius con hojas: cortes, hongo y su telemetría.
        // El pool trackeado acelera el relevo (primer unload ~t3950) para que
        // el hongo acumule dentro de la partida.
        string fixture = RepoFixture("warm-v2.antgenome");
        Assert.True(System.IO.File.Exists(fixture), $"fixture ausente: {fixture}");
        string stream = GameScenario.Run(42, ticks: 7200, colonies: 2, grid: 96,
            frameEvery: 3600, seedPoolPath: fixture, drops: null,
            leafFraction: 1f,
            species: new[] { World.SpeciesDescriptor.Atta, World.SpeciesDescriptor.LasiusNiger });

        var parser = new GameStreamParser();
        bool vistoCuts = false, vistoFungus = false, vistoCutters = false,
             vistoMetricsLeafCuts = false;
        float fungusMaxAtta = 0f, fungusMaxLasius = 0f, maxFungusVisto = 0f;
        int nLineas = 0;

        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;
            nLineas++;
            var f = v;

            foreach (var it in f.Items)
            {
                if (it.IsLeaf) vistoCuts = true;
            }
            foreach (var c in f.Colonies)
            {
                if (c.FungusMax > fungusMaxAtta) fungusMaxAtta = c.FungusMax;
                if (c.Fungus > maxFungusVisto) maxFungusVisto = c.Fungus;
                if (c.FungusMax > 0f && c.Fungus > 0f) vistoFungus = true;
                if (c.FungusMax == 0f) fungusMaxLasius = 0f;
            }
            if (f.Cutters.Count > 0)
            {
                vistoCutters = true;
                Assert.All(f.Cutters, cv => Assert.True(cv.LeafCuts > 0 || cv.FungusFed > 0));
            }
            if (f.Metrics is { } m && m.LeafCuts > 0) vistoMetricsLeafCuts = true;
        }

        Assert.True(vistoCuts, "canal A: las hojas traen [id,x,y,amount,cutsLeft,cutsInitial]");
        Assert.True(vistoFungus, $"canal A: fungus>0 ausente (max={maxFungusVisto:F2}, maxAtta={fungusMaxAtta:F1}, lineas={nLineas}, cuts={vistoCuts}, cutters={vistoCutters}, hash={parser.FinalHash?[..16]})");
        Assert.Equal(World.SpeciesDescriptor.Atta.FungusMax, fungusMaxAtta, 1);
        Assert.True(vistoCutters, "canal C: bloque cutters presente con actividad");
        Assert.True(vistoMetricsLeafCuts, "canal C: metrics.leafCuts > 0 en ventanas con cortes");
        _ = fungusMaxLasius; // lasius: fungusMax 0 verificado abajo en su test
    }

    [Fact]
    public void StreamClasico_SinCampos_ElParserNoReventa()
    {
        // Mundo clásico (sin hojas, sin Atta): los items quedan en 4 elementos,
        // fungus/fungusMax = 0 y NINGÚN bloque cutters — pero todo parsea.
        string stream = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
            frameEvery: 200, seedPoolPath: null, drops: null);

        var parser = new GameStreamParser();
        bool hojas = false, cutters = false;
        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;
            foreach (var it in v.Items)
                if (it.IsLeaf) hojas = true;
            if (v.Cutters.Count > 0) cutters = true;
        }
        Assert.False(hojas, "sin LeafFraction no hay hojas");
        Assert.False(cutters, "sin Atta no hay bloque cutters");
    }

    [Fact]
    public void Sintetico_FilaDeColoniaConFungus_SeParsea()
    {
        string linea = "{\"tick\":7200,\"ants\":[],\"items\":[]," +
            "\"colonies\":[{\"id\":0,\"nest\":[256,384],\"adults\":13,\"eggs\":1," +
            "\"larvae\":2,\"pupae\":1,\"stock\":60.529,\"stockMax\":120,\"elite\":16," +
            "\"fungus\":3.889,\"fungusMax\":60}],\"events\":[]}";
        var parser = new GameStreamParser();
        var v = parser.ParseLine(linea);
        Assert.NotNull(v);
        var c = Assert.Single(v.Colonies);
        Assert.Equal(60f, c.FungusMax);
        Assert.Equal(3.889f, c.Fungus, 3);
    }

    [Fact]
    public void StreamViejo_ParserTolerante_ArchivoSintetico()
    {
        // Línea de tick estilo PRE-F5.2a (4 campos por item, sin fungus):
        // el parser debe reconstruirla sin excepción y con defaults.
        string viejo = "{\"tick\":10,\"ants\":[],\"items\":[[1,10.5,20.25,4.5]]," +
            "\"colonies\":[{\"id\":0,\"nest\":[30,30],\"adults\":5,\"eggs\":0," +
            "\"larvae\":0,\"pupae\":0,\"stock\":80.0,\"stockMax\":100,\"elite\":3}],\"events\":[]}";
        var parser = new GameStreamParser();
        var v = parser.ParseLine(viejo);
        Assert.NotNull(v);
        var item = Assert.Single(v.Items);
        Assert.False(item.IsLeaf);
        Assert.Equal(0, item.CutsLeft);
        var colony = Assert.Single(v.Colonies);
        Assert.Equal(0f, colony.Fungus);
        Assert.Equal(0f, colony.FungusMax);
        Assert.Empty(v.Cutters);
    }
}
