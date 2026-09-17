using System;
using System.Collections.Generic;
using AntSim.Core.Contracts;
using AntSim.Core.Pheromone;
using AntSim.Core.Serialization;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3 — huella CHC: cuarta capa de feromona y la tropotaxis en RATIO que la lee.
///
/// QUÉ ES Y POR QUÉ. Las tres capas anteriores son señales DECIDIDAS (el cerebro
/// elige depositarlas) y todas son positivas o de peligro: ninguna desincentiva
/// volver a pisar la misma zona, así que el reparto espacial del forrajeo dependía
/// por completo del ruido del cerebro. La huella CHC es lo contrario: es CUTÍCULA
/// que se roza al andar, se deposita por unidad RECORRIDA (no por segundo), sin
/// gasto de energía y sin decisión, tiene vida larga (τ½ 240 s: un pasillo muy
/// peinado sigue siendo poco atractivo en la siguiente visita) y su única lectura
/// es un REFLEJO PERIFÉRICO — no un canal de sensor, así que ningún genoma del
/// pool cambia de forma por añadirla.
///
/// Lo que se fija aquí: la capa se llena al andar y entra en el hash, el reflejo
/// tiene el álgebra exacta que promete el diseño (simetría → 0, signo hacia el
/// lado limpio, saturación de ratio) y cambia la TRAYECTORIA (no solo el hash),
/// el checkpoint la conserva y la lectura es determinista entre cargas.
/// </summary>
public class ChcFootprintTests
{
    // ————— helpers —————

    private static WorldSim NewWorld(ulong seed = 42UL, int grid = 96, int colonies = 1)
        => new(seed, grid, colonies);

    private static int CellOf(float worldCoord) => (int)(worldCoord / SimConstants.CellSizeUnits);

    private static void Paint(PheromoneLayer layer, float worldX, float worldY, float amount)
        => layer.Deposit(CellOf(worldX), CellOf(worldY), amount);

    /// <summary>
    /// Mide el giro que añade el reflejo con una huella pintada a mano: la sonda
    /// lateral derecha (heading − ángulo) y la izquierda (heading + ángulo), con
    /// la hormiga mirando al este para que el signo sea legible.
    /// </summary>
    private static float DeltaSteer(WorldSim sim, float right, float left, SpeciesDescriptor? species = null)
        => DeltaSteer(sim.Colonies[0], right, left, species);

    private static float DeltaSteer(Colony colony, float right, float left, SpeciesDescriptor? species = null)
    {
        var ant = colony.Adults[0];
        ant.Heading = 0f;                                  // al este: +y es la IZQUIERDA
        var sp = species ?? colony.Species;
        float reach = sp.SensorReach * ant.SensorScale;
        float cos = CanonMath.Cos(sp.SenseAngle);
        float sin = CanonMath.Sin(sp.SenseAngle);

        if (right > 0f) Paint(colony.FootprintLayer, ant.X + cos * reach, ant.Y - sin * reach, right);
        if (left > 0f) Paint(colony.FootprintLayer, ant.X + cos * reach, ant.Y + sin * reach, left);

        var decision = AntDecision.Neutral();
        WorldSim.ApplyFootprintRepulsion(colony, ant, sp, ref decision);
        return decision.Steer;
    }

    private static float SumX(WorldSim sim)
    {
        float sum = 0f;
        foreach (var col in sim.Colonies)
            foreach (var ant in col.Adults)
                sum += ant.X;
        return sum;
    }

    // ————— 1. la capa se llena al andar, sola —————

    [Fact]
    public void LaHuella_SeDepositaAlAndar_SinDecisionNiEnergia()
    {
        var sim = NewWorld();
        var layer = sim.Colonies[0].FootprintLayer;

        for (int i = 0; i < 100; i++) sim.Step();
        float early = layer.SumOfValues();
        ulong earlyMutations = layer.MutationCount;

        for (int i = 0; i < 300; i++) sim.Step();

        Assert.True(early > 0f, "a los 100 ticks ya debería haber tráfico depositado");
        Assert.True(layer.SumOfValues() > early, "la huella crece mientras la colonia anda");
        Assert.True(layer.MutationCount > earlyMutations, "las escrituras versionan los tiles (render incremental)");
    }

    // ————— 2. el álgebra del reflejo —————

    [Fact]
    public void Reflejo_SinHuella_NoGira()
    {
        Assert.Equal(0f, DeltaSteer(NewWorld(), 0f, 0f));
    }

    [Fact]
    public void Reflejo_ConHuellaSimetrica_NoGira()
    {
        var sim = NewWorld();
        var layer = sim.Colonies[0].FootprintLayer;
        for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
                layer.Deposit(x, y, 0.3f);

        // Capa constante ⇒ las dos antenas leen EXACTAMENTE lo mismo ⇒ el
        // desequilibrio es 0 y una hormiga sobre el filo de un rastro no gira.
        Assert.Equal(0f, DeltaSteer(sim, 0f, 0f));
    }

    [Fact]
    public void Reflejo_Asimetrico_GiraHaciaElLadoMenosPisado()
    {
        float derecha = DeltaSteer(NewWorld(), right: 0.4f, left: 0f);
        float izquierda = DeltaSteer(NewWorld(), right: 0f, left: 0.4f);
        var sp = SpeciesDescriptor.LasiusNiger;

        Assert.True(derecha > 0f, "más huella a la derecha ⇒ gira a la izquierda");
        Assert.True(izquierda < 0f, "más huella a la izquierda ⇒ gira a la derecha");
        // Las magnitudes no son idénticas al bit (cada mundo es un mundo), pero
        // sí del mismo orden: el reflejo no tiene lado favorito.
        Assert.InRange(MathF.Abs(derecha + izquierda), 0f, 0.2f * sp.FootprintRepel);
    }

    [Fact]
    public void Reflejo_Satura_EsUnRatioNoUnaGananciaLineal()
    {
        var sim = NewWorld();
        var sp = sim.Colonies[0].Species;

        float suave = DeltaSteer(sim, right: 0.05f, left: 0f);
        float fuerte = DeltaSteer(sim, right: 0.5f, left: 0f);

        Assert.True(suave > 0f && fuerte > suave);
        // 10× más huella NO da 10× más giro: el nivel propio va en el denominador.
        Assert.True(fuerte < 6f * suave, $"ratio lineal ({fuerte} vs {suave})");
        // Y el giro está acotado por la ganancia: nunca desborda el timón.
        Assert.True(fuerte < sp.FootprintRepel);
    }

    // ————— 3. la huella entra en el hash (y el reflejo mueve el mundo) —————

    [Fact]
    public void Huella_EntraEnElHashDelMundo()
    {
        var sim = NewWorld();
        for (int i = 0; i < 120; i++) sim.Step();
        string antes = sim.HashLine();

        // Un solo grano más de cutícula en una celda y el hash lo ve: la capa no
        // es decorativa, es estado del mundo.
        sim.Colonies[0].FootprintLayer.Deposit(3, 3, 0.25f);

        Assert.NotEqual(antes, sim.HashLine());
    }

    [Fact]
    public void Reflejo_CambiaLaTrayectoria_NoSoloElHash()
    {
        // Dos mundos idénticos salvo la ganancia del reflejo. El depósito es el
        // mismo (no depende del reflejo), así que cualquier diferencia de posición
        // solo puede venir del giro: la huella no decora el mundo, lo conduce.
        var conReflejo = NewWorld(seed: 7UL);
        var sinReflejo = NewWorld(seed: 7UL);
        foreach (var sim in new[] { conReflejo, sinReflejo })
        {
            var sp = new SpeciesDescriptor();          // copia propia: no tocar la estática compartida
            sim.Colonies[0].Species = sp;
        }
        sinReflejo.Colonies[0].Species.FootprintRepel = 0f;

        for (int i = 0; i < 600; i++)
        {
            conReflejo.Step();
            sinReflejo.Step();
        }

        Assert.NotEqual(SumX(conReflejo), SumX(sinReflejo));
    }

    // ————— 4. checkpoint: la huella viaja —————

    [Fact]
    public void Checkpoint_V5_ConservaLaHuella_YEsDeterminista()
    {
        var original = NewWorld(seed: 11UL, colonies: 2);
        for (int i = 0; i < 300; i++) original.Step();
        float huella = original.Colonies[0].FootprintLayer.SumOfValues();
        Assert.True(huella > 0f);

        byte[] bytes = WorldSimSave.Serialize(original);
        // La versión va en los 4 bytes siguientes a la magia de 8: que suba es
        // una decisión consciente de formato, no un efecto colateral.
        Assert.Equal(5, BitConverter.ToInt32(bytes, 8));
        Assert.Equal(5, WorldSimSave.FormatVersion);

        var cargado = WorldSimSave.Deserialize(bytes);

        Assert.Equal(original.HashLine(), cargado.HashLine());
        Assert.Equal(huella, cargado.Colonies[0].FootprintLayer.SumOfValues(), 3);

        // Y sigue la partida igual desde ahí: la huella no es peso muerto.
        for (int i = 0; i < 200; i++) { original.Step(); cargado.Step(); }
        Assert.Equal(original.HashLine(), cargado.HashLine());
    }

    [Fact]
    public void Checkpoint_V4_DeUnBuildAnterior_SeCargaConLaHuellaACero()
    {
        // El archivo lo generó el build ANTERIOR a la huella CHC (commit 4509edb,
        // procedencia en tests/fixtures/README.md): la compatibilidad hacia atrás
        // del formato es un archivo que CI carga, no una promesa del doc.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, ".git"))
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent;
        string path = System.IO.Path.Combine(dir!.FullName, "tests", "fixtures", "checkpoint-v4.antsave");
        Assert.True(System.IO.File.Exists(path), "falta tests/fixtures/checkpoint-v4.antsave");

        var sim = WorldSimSave.Deserialize(System.IO.File.ReadAllBytes(path));

        Assert.Equal(300UL, sim.Tick);
        Assert.Equal(42UL, sim.Seed);
        Assert.Equal(32, sim.GridCells);
        // v4 no conocía la capa: llega a cero. El tráfico anterior no se puede
        // reconstruir, pero el mundo sigue siendo determinista A PARTIR de aquí.
        Assert.Equal(0f, sim.Colonies[0].FootprintLayer.SumOfValues());

        for (int i = 0; i < 120; i++) sim.Step();
        string hash = sim.HashLine();
        float huellaDespues = sim.Colonies[0].FootprintLayer.SumOfValues();

        var again = WorldSimSave.Deserialize(System.IO.File.ReadAllBytes(path));
        for (int i = 0; i < 120; i++) again.Step();

        Assert.Equal(hash, again.HashLine());
        Assert.Equal(huellaDespues, again.Colonies[0].FootprintLayer.SumOfValues(), 3);
        // Y la huella ya no está a cero: el mundo viejo vuelve a generar tráfico.
        Assert.True(huellaDespues > 0f);
    }

    // ————— 5. contrato del canal E (ordinal de la capa) —————

    [Fact]
    public void OrdinalDelCanalE_EsCuatro()
    {
        // El selector del HUD y el parser de Unity declaran estos ordinales a
        // mano (la app no referencia el Core): insertar un tipo en medio del enum
        // rompería los dos lados, así que el valor queda fijado aquí.
        Assert.Equal(4, (byte)PheromoneKind.Footprint);
        Assert.Equal(240f, PheromoneDefaults.HalfLifeSeconds(PheromoneKind.Footprint));
    }

    [Fact]
    public void CanalE_EmiteLaHuella_CuandoSeLePide()
    {
        string stream = AntSim.Core.Scenario.GameScenario.Run(
            42, ticks: 300, colonies: 1, grid: 32, frameEvery: 300, pheroEvery: 300,
            pheroLayers: new List<AntSim.Core.Scenario.GameScenario.PheroRequest>
            {
                new(0, PheromoneKind.Footprint)
            });

        Assert.Contains("\"k\":4", stream);
    }
}
