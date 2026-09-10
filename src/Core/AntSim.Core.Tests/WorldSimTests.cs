using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

public class WorldSimTests
{
    /// <summary>Cerebro de test con sesgos fijos (actuadores deterministas).</summary>
    private static MlpBrain ForceBrain(float steerBias, float speedBias, float interactBias, float depositBias = 0f)
    {
        int[] sizes = { 19, 1, 6 };
        var w = new float[MlpBrain.ExpectedWeightCount(sizes)];
        int off2 = 20; // bloque de salida: 6·(1+1) floats
        w[off2 + 0 * 2 + 1] = steerBias;    // steer  (tanh)
        w[off2 + 1 * 2 + 1] = speedBias;    // speed  (sigmoid)
        w[off2 + 2 * 2 + 1] = depositBias;  // depositFood
        w[off2 + 5 * 2 + 1] = interactBias; // interact (sigmoid)
        return new MlpBrain(sizes, w);
    }

    private static WorldSim NewSim(int colonies = 1, int grid = 128)
        => new WorldSim(99UL, grid, colonies);

    [Fact]
    public void SameSeed_TwoRuns_ProduceIdenticalHashStream()
    {
        var a = NewSim();
        var b = NewSim();

        var hashesA = new List<string>();
        var hashesB = new List<string>();
        for (int i = 0; i < 600; i++)
        {
            a.Step();
            b.Step();
            if (a.Tick % 100 == 0)
            {
                hashesA.Add(a.HashLine());
                hashesB.Add(b.HashLine());
            }
        }
        Assert.Equal(hashesA, hashesB);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentWorlds()
    {
        var a = new WorldSim(1UL, 128, 1);
        var b = new WorldSim(2UL, 128, 1);
        for (int i = 0; i < 300; i++) { a.Step(); b.Step(); }
        Assert.NotEqual(a.HashLine(), b.HashLine());
    }

    [Fact]
    public void AdultsNeverExceedForty_EvenWithUnlimitedStock()
    {
        var sim = NewSim();
        int maxAdults = 0;
        for (int i = 0; i < 2400; i++)
        {
            if (i % 100 == 0)
                sim.Colonies[0].Stock = 5000f; // riqueza artificial del test
            sim.Step();
            maxAdults = Math.Max(maxAdults, sim.Colonies[0].AdultCountAlive);
        }
        Assert.True(maxAdults <= ColonyController.MaxAdultsPerColony,
            $"Máximo de adultas observado: {maxAdults}");
    }

    [Fact]
    public void Pickup_RequiresLoadFreeAndContact_AndRemovesItem()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 2f, 10f, 0f); // interact ≈ 1
        var item = sim.Items[0];
        item.X = ant.X;
        item.Y = ant.Y;

        sim.Step();

        // El pickup ocurre: carga activada, valor transferido y el ítem desaparece.
        Assert.True(ant.HasLoad);
        AssertClose(item.Amount, ant.LoadValue);
        Assert.DoesNotContain(item, sim.Items);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.Pickup);
    }

    [Fact]
    public void Unload_OnlyWorksAtNest_AndAddsToStock()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 2f, 10f, 0f);
        ant.HasLoad = true;
        ant.LoadValue = 5f;
        float stockBefore = colony.Stock;

        // Lejos del nido: la descarga es un no-op (paso perdido).
        ant.X = colony.NestX + 500f;
        ant.Y = colony.NestY;
        sim.Step();
        Assert.True(ant.HasLoad);
        Assert.True(colony.Stock <= stockBefore + 0.01f); // nada entró (solo gasta la reina)

        // En el nido: descarga real. (Fase 3ter: la colonia funda con stock
        // completo = StockMax, así que primero se libera margen — la descarga
        // respeta el tope de reserva y sin hueco el crédito se trunca.)
        colony.Stock = 20f;
        ant.X = colony.NestX;
        ant.Y = colony.NestY;
        float stockBeforeNest = colony.Stock;
        sim.Step();
        Assert.False(ant.HasLoad);
        Assert.True(colony.Stock >= stockBeforeNest + 4.9f);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.Unload);
    }

    [Fact]
    public void DepositFood_IsGatedByLoad()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 2f, -10f, depositBias: 10f); // depositFood ≈ 1, sin interact
        int cx = (int)(ant.X / 8f);
        int cy = (int)(ant.Y / 8f);

        // Sin carga: el depósito de comida queda bloqueado.
        ant.HasLoad = false;
        float before = colony.FoodLayer[cx, cy];
        sim.Step();
        Assert.Equal(before, colony.FoodLayer[cx, cy]);

        // Con carga: deposita rastro.
        ant.HasLoad = true;
        ant.LoadValue = 3f;
        float before2 = colony.FoodLayer[cx, cy];
        sim.Step();
        Assert.True(colony.FoodLayer[cx, cy] > before2);
    }

    [Fact]
    public void Starvation_WithNoStock_KillsAnt_AndDropsCarriedItem()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 10f, -10f, 0f); // velocidad alta, sin interact
        colony.Stock = 0f; // sin alimentación
        ant.Energy = 0.0005f;
        ant.HasLoad = true;
        ant.LoadValue = 3f;
        int itemsBefore = sim.Items.Count;

        for (int i = 0; i < 40 && ant.Alive; i++)
            sim.Step();

        Assert.False(ant.Alive);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.AntDied && (DeathCause)e.Cause == DeathCause.Starvation);
        // El ítem cae al suelo al morir (sin respawn porque ya hay 25 ≥ objetivo).
        Assert.Equal(itemsBefore + 1, sim.Items.Count);
    }

    [Fact]
    public void AgeDeath_OccursAtEndOfLifespan()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 0f, 0f, 0f);
        // El interact neutro (sigmoid(0)=0.5) puede recoger ítems: lo dejamos,
        // la muerte por edad es independiente de la carga.
        ant.Age = ant.Lifespan - 0.01f;
        ant.Energy = 1f;

        for (int i = 0; i < 3 && ant.Alive; i++)
            sim.Step();

        Assert.False(ant.Alive);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.AntDied && (DeathCause)e.Cause == DeathCause.Age);
    }

    [Fact]
    public void WorldScenario_IsDeterministic()
    {
        string run1 = Scenario.WorldScenario.Run(123UL, ticks: 600, colonies: 2, grid: 128);
        string run2 = Scenario.WorldScenario.Run(123UL, ticks: 600, colonies: 2, grid: 128);
        Assert.Equal(run1, run2);
    }

    [Fact]
    public void RelayTracker_RecordsFirstUnloadTick()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 2f, 10f, 0f); // interact ≈ 1
        ant.HasLoad = true;
        ant.LoadValue = 5f;
        colony.Stock = 20f; // margen bajo el tope de reserva (Fase 3ter)
        ant.X = colony.NestX;
        ant.Y = colony.NestY;

        var relay = new RelayTracker();
        sim.Step();
        relay.Observe(sim.LastEvents, sim);

        // La descarga ocurre en el primer paso: el tracker debe reportar ESE tick.
        Assert.True(relay.HasUnload);
        Assert.Equal(sim.Tick, relay.FirstUnloadTick);
    }

    [Fact]
    public void RelayTracker_CountsDeathDrops_WithNestDistance()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 0f, -10f, 0f); // sin interacciones: muere con la carga
        colony.Stock = 0f;
        ant.Energy = 0.0005f;
        ant.HasLoad = true;
        ant.LoadValue = 3f;
        ant.X = colony.NestX + 250f; // suelta lejos del nido: debe caer AQUÍ
        ant.Y = colony.NestY;

        var relay = new RelayTracker();
        int steps = 0;
        while (ant.Alive && steps < 40)
        {
            sim.Step();
            relay.Observe(sim.LastEvents, sim);
            steps++;
        }

        Assert.False(ant.Alive);
        // La suelta por muerte es ItemSpawned con ColonyId real (≠ -1 del spawn
        // regular) y el tracker mide su distancia al nido.
        Assert.True(relay.DropCount >= 1);
        Assert.NotNull(relay.DropDistanceMean);
        Assert.InRange(relay.DropDistanceMean!.Value, 200.0, 300.0);
        Assert.False(relay.HasUnload); // murió sin llegar al nido
    }

    [Fact]
    public void RelayTracker_IgnoresRegularItemSpawns()
    {
        var sim = NewSim();
        var colony = sim.Colonies[0];
        IsolateSingleAnt(colony);

        var ant = colony.Adults[0];
        ant.Brain = ForceBrain(0f, 0f, -10f, 0f); // sin interacciones ni muertes
        sim.TargetItems = sim.TargetItems + 2; // fuerza respawn: ItemSpawned(-1)

        var relay = new RelayTracker();
        for (int i = 0; i < 30; i++)
        {
            sim.Step();
            relay.Observe(sim.LastEvents, sim);
        }

        // El mundo re-spawneó ítems con el centinela ColonyId = -1: no son sueltas.
        Assert.Equal(0, relay.DropCount);
        Assert.Null(relay.DropDistanceMean);
        Assert.False(relay.HasUnload);
    }

    private static void IsolateSingleAnt(Colony colony)
    {
        for (int i = colony.Adults.Count - 1; i > 0; i--)
            colony.Adults.RemoveAt(i);
    }

    private static void AssertClose(float expected, float actual, float tolerance = 1e-3f)
        => Assert.True(Math.Abs(expected - actual) < tolerance,
            $"Esperado {expected}, obtenido {actual}");
}