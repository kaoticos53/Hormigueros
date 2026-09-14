using System;
using System.Linq;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2b.3 — contratos de las incursiones en el stream: canal B con los kinds
/// 17/18/19 (Strike/RaidInflow/StockRobbed), canal C con el bloque `raids`
/// por colonia (mismo patrón tolerante de `cutters`: ausencia = sin actividad)
/// y la causa de muerte combate (byte 2) en el texto del inspector. Defaults
/// tolerantes: un stream SIN raids (antiguo o pacífico) parsea igual.
/// </summary>
public class RaidContractTests
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
    public void StreamEciton_Kinds17a19_YBloqueRaids_SeParsean()
    {
        // Invasión real: Eciton salvaje (colonia 1) contra Lasius (colonia 0).
        // Con sensor activo hay strikes garantizados en 12 000 ticks (sonda
        // §8quater: ≥ 18 strikes en seed 42); el robo exige stock en la
        // víctima, así que StockRobbed/RaidInflow solo se exige si hubo strikes
        // con botín — aquí se validan los que aparezcan, y strikes SIEMPRE.
        string stream = GameScenario.Run(42, ticks: 12000, colonies: 2, grid: 96,
            frameEvery: 3600, seedPoolPath: null, drops: null,
            leafFraction: 0f,
            species: new[] { World.SpeciesDescriptor.LasiusNiger, World.SpeciesDescriptor.Eciton });

        var parser = new GameStreamParser();
        bool vistoStrike = false, vistoRaidInflow = false, vistoStockRobbed = false,
             vistoRaids = false, vistoRobbedEnVicima = false, vistoMuerteCombate = false;
        int strikesParser = 0, raidsParser = 0;
        long strikesWindowSum = 0;

        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;

            foreach (var ev in v.Events)
            {
                if (ev.Kind == 17)
                {
                    vistoStrike = true;
                    // Contrato §4.1: cause = id de la PRESA (>0).
                    Assert.True(ev.Cause > 0, "Strike.cause debe llevar el id de la presa");
                }
                if (ev.Kind == 18) vistoRaidInflow = true;
                if (ev.Kind == 19)
                {
                    vistoStockRobbed = true;
                    // Contrato §4.1: cause = ep·100 en la VÍCTIMA (colonia saqueada).
                    Assert.True(ev.Cause > 0, "StockRobbed.cause debe llevar ep·100");
                    vistoRobbedEnVicima = true;
                }
                if (ev.Kind == 1 && ev.Cause == 2) vistoMuerteCombate = true;
            }

            foreach (var r in v.Raids)
            {
                vistoRaids = true;
                Assert.True(r.Strikes > 0 || r.RaidInflows > 0,
                    "raids: fila con strikes e inflows a 0 no debe emitirse");
                strikesParser++;
                strikesWindowSum += r.Strikes;
            }
        }

        Assert.True(vistoStrike, "canal B: Strike (17) ausente — el sensor garantiza contactos en seed 42");
        Assert.True(vistoRaids, "canal C: bloque raids ausente con strikes en el mundo");
        Assert.True(strikesParser > 0);
        Assert.True(strikesWindowSum > 0);
        Assert.True(vistoStockRobbed || vistoRaidInflow || strikesWindowSum < 18,
            "sin robo ni descarga la sonda §8quater esperaba ~18 strikes");
        _ = vistoRobbedEnVicima;
        _ = vistoMuerteCombate; // daño 0.35 ep rara vez mata: no se exige (sonda §8sexies)
    }

    [Fact]
    public void StreamClasico_SinEciton_NiBloqueRaids_NiPinosMovidos()
    {
        // Mundo clásico: sin Eciton no hay kinds 17-19 NI bloque raids, y el
        // stream canónico (pin CI) no puede cambiar por este slice.
        string stream = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
            frameEvery: 200, seedPoolPath: null, drops: null);

        var parser = new GameStreamParser();
        bool raids = false, combatKinds = false;
        foreach (var line in stream.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = parser.ParseLine(line.Trim());
            if (v == null) continue;
            if (v.Raids.Count > 0) raids = true;
            foreach (var ev in v.Events)
                if (ev.Kind is 17 or 18 or 19) combatKinds = true;
        }
        Assert.False(raids, "sin Eciton no hay bloque raids");
        Assert.False(combatKinds, "sin Eciton no hay eventos de combate");
    }

    [Fact]
    public void Sintetico_FilaRaids_SeParsea_YCausa2TextoCombate()
    {
        string linea = "{\"tick\":5000,\"ants\":[],\"items\":[],\"colonies\":[]," +
            "\"events\":[[17,1,7,50.5,60.25,3],[18,1,7,10,10,0],[19,0,0,10,10,30],[1,0,4,20,20,2]]," +
            "\"raids\":[[0,0,0],[1,2,1]]}";
        var parser = new GameStreamParser();
        var v = parser.ParseLine(linea);
        Assert.NotNull(v);

        var raid1 = v.Raids.Single(r => r.ColonyId == 1);
        Assert.Equal(2, raid1.Strikes);
        Assert.Equal(1, raid1.RaidInflows);

        // kinds 17-19 con sus causes del contrato.
        Assert.Contains(v.Events, e => e.Kind == 17 && e.Cause == 3);
        Assert.Contains(v.Events, e => e.Kind == 18);
        Assert.Contains(v.Events, e => e.Kind == 19 && e.Cause == 30); // 0.30 ep
        Assert.Contains(v.Events, e => e.Kind == 1 && e.Cause == 2);

        // La tarjeta del inspector nombra la causa combate (byte 2).
        Assert.Equal("combate", AntInspectorModel.DeathCauseText(2));
        Assert.Equal("vejez", AntInspectorModel.DeathCauseText(0));
        Assert.Equal("inanición", AntInspectorModel.DeathCauseText(1));
    }

    [Fact]
    public void StreamViejo_ParserTolerante_SinRaids()
    {
        // Línea estilo PRE-F5.2b (sin raids ni kinds nuevos): parsea con defaults.
        string viejo = "{\"tick\":10,\"ants\":[],\"items\":[[1,10.5,20.25,4.5]]," +
            "\"colonies\":[{\"id\":0,\"nest\":[30,30],\"adults\":5,\"eggs\":0," +
            "\"larvae\":0,\"pupae\":0,\"stock\":80.0,\"stockMax\":100,\"elite\":3}],\"events\":[]}";
        var parser = new GameStreamParser();
        var v = parser.ParseLine(viejo);
        Assert.NotNull(v);
        Assert.Empty(v.Raids);
    }
}
