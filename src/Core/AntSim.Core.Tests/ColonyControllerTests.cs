using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Pheromone;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

public class ColonyControllerTests
{
    private static void AddAdults(Colony c, int n, float vigor = 0.8f)
    {
        for (int i = 0; i < n; i++)
        {
            var a = new Ant { Id = (uint)(100 + i), ColonyId = c.Id, X = c.NestX, Y = c.NestY };
            a.InitFromVigor(c.Species.EnergyCapacity, c.Species.BaseLifespan, vigor);
            c.Adults.Add(a);
        }
    }

    private static Colony NewColony(float stock)
    {
        var sp = SpeciesDescriptor.LasiusNiger;
        return new Colony
        {
            Id = 0,
            Species = sp,
            NestX = 512f,
            NestY = 512f,
            Stock = stock,
            StockMax = sp.StockMax,
            QueenEnergy = 1f,
            Rng = new DeterministicRandom(7UL),
            Brain = new MlpBrain(new[] { 19, 4, 6 }, new float[MlpBrain.ExpectedWeightCount(new[] { 19, 4, 6 })]),
            FoodLayer = new PheromoneLayer(64, 64),
            HomeLayer = new PheromoneLayer(64, 64),
            AlarmLayer = new PheromoneLayer(64, 64),
            ConsumeEma = 1.0f
        };
    }

    private static void Step(Colony c, List<SimEvent> events)
    {
        uint nextId = 1000;
        ColonyController.Step(c, SimConstants.FixedDtSeconds, 1, events, ref nextId);
    }

    [Fact]
    public void Egg_ProgressesToLarva()
    {
        var c = NewColony(stock: 1000f);
        c.Eggs.Add(new BroodMember { Kind = BroodKind.Egg, Insert = c.InsertCounter++, Age = c.Species.EggTime - 0.01f, G0 = 0.8f });

        Step(c, new List<SimEvent>());
        Assert.Empty(c.Eggs);
        var larva = Assert.Single(c.Larvae);
        Assert.Equal(0.8f, larva.G0);
    }

    [Fact]
    public void WellFedLarva_BecomesStrongPupa()
    {
        var c = NewColony(stock: 1000f);
        AddAdults(c, 10); // nodrizas
        c.Larvae.Add(new BroodMember
        {
            Kind = BroodKind.Larva,
            Insert = c.InsertCounter++,
            G0 = 0.8f,
            Nutrition = c.Species.KFull - 0.001f // un bocado la lleva a KFull
        });

        Step(c, new List<SimEvent>());
        Assert.Empty(c.Larvae);
        var pupa = Assert.Single(c.Pupae);
        Assert.False(pupa.Weak);
    }

    [Fact]
    public void StrongPupa_Ecloses_WithExpectedVigorAndCapacity()
    {
        var c = NewColony(stock: 1000f);
        c.Pupae.Add(new BroodMember
        {
            Kind = BroodKind.Pupa,
            Insert = c.InsertCounter++,
            G0 = 0.8f,
            Nutrition = c.Species.KFull, // n̄ = 1 ⇒ v = 0.8·(0.55 + 0.45) = 0.8
            Age = c.Species.PupaTime - 0.01f
        });

        Step(c, new List<SimEvent>());
        Assert.Empty(c.Pupae);
        var adult = Assert.Single(c.Adults);
        Assert.True(adult.Alive);
        AssertClose(0.8f, adult.Vigor);
        // Capacidad = base·(0.25 + 0.75·vigor) = 10·0.85
        AssertClose(10f * (0.25f + 0.75f * 0.8f), adult.EnergyCapacity);
    }

    [Fact]
    public void UnderfedLarva_BecomesWeakMinim_WithLowerVigor()
    {
        // Stock mínimo pero runway sano (sin canibalismo): no hay alimentación larval.
        var c = NewColony(stock: 0.005f);
        c.ConsumeEma = 0.0001f;
        c.Larvae.Add(new BroodMember
        {
            Kind = BroodKind.Larva,
            Insert = c.InsertCounter++,
            G0 = 0.8f,
            Nutrition = 0.9f,          // KMin ≤ nutr < KFull ⇒ minim
            Age = c.Species.LarvaTimeMax - 0.01f // a punto de vencer el plazo
        });

        Step(c, new List<SimEvent>());
        Assert.Empty(c.Larvae);
        var pupa = Assert.Single(c.Pupae);
        Assert.True(pupa.Weak);

        // Eclosión del minim en un solo paso (sin esperar: el runway es seguro).
        pupa.Age = c.Species.PupaTime - 0.01f;
        c.Stock = 0.005f;
        Step(c, new List<SimEvent>());

        var adult = Assert.Single(c.Adults);
        // n̄ = 0.9/2.0 = 0.45 → v = 0.8·(0.55 + 0.45·0.45) ≈ 0.602
        AssertClose(0.8f * (0.55f + 0.45f * (0.9f / 2.0f)), adult.Vigor);
        // El minim es más débil que la pupa fuerte (vigor 0.8 del test anterior).
        Assert.True(adult.Vigor < 0.8f);
    }

    [Fact]
    public void StarvedLarva_DiesBeforePupating()
    {
        var c = NewColony(stock: 0.005f);
        c.ConsumeEma = 0.0001f;
        c.Larvae.Add(new BroodMember
        {
            Kind = BroodKind.Larva,
            Insert = c.InsertCounter++,
            G0 = 0.8f,
            Nutrition = 0.2f,          // < KMin
            Age = c.Species.LarvaTimeMax - 0.01f
        });

        Step(c, new List<SimEvent>());
        Assert.Empty(c.Larvae);
        Assert.Empty(c.Pupae);
    }

    [Fact]
    public void LowRunway_TriggersOophagy_EggsReabsorbed_WithEnergyRecovery()
    {
        var c = NewColony(stock: 10f);
        c.ConsumeEma = 5f; // runway ≈ 2 s < TOoph (25)
        for (int i = 0; i < 5; i++)
            c.Eggs.Add(new BroodMember { Kind = BroodKind.Egg, Insert = c.InsertCounter++, G0 = 0.8f });

        float stockBefore = c.Stock;
        for (int i = 0; i < 20; i++)
            Step(c, new List<SimEvent>());

        Assert.Empty(c.Eggs);
        // Cada huevo recupera η_egg·ε_egg = 0.3·0.5 = 0.15 ep.
        Assert.True(c.Stock >= stockBefore + 5 * 0.15f - 0.1f);
    }

    [Fact]
    public void LarvalCannibalism_EatsWeakestFirst()
    {
        // runway 10 s: por debajo de TCann (15) pero por encima de TCrit (8).
        var c = NewColony(stock: 10f);
        c.ConsumeEma = 1f;
        c.Larvae.Add(new BroodMember { Kind = BroodKind.Larva, Insert = 0, G0 = 0.8f, Nutrition = 1.0f });
        c.Larvae.Add(new BroodMember { Kind = BroodKind.Larva, Insert = 1, G0 = 0.8f, Nutrition = 0.2f });

        // El déficit acumulado solo alcanza para comerse a la más débil (0.2).
        for (int i = 0; i < 5; i++)
            Step(c, new List<SimEvent>());

        var survivor = Assert.Single(c.Larvae);
        AssertClose(1.0f, survivor.Nutrition);
    }

    [Fact]
    public void Eclosion_NeverExceedsMaxAdults_AndEggsRespectCap()
    {
        var c = NewColony(stock: 1000f);
        // Colonia llena: 40 adultas.
        for (int i = 0; i < ColonyController.MaxAdultsPerColony; i++)
        {
            var a = new Ant { Id = (uint)(i + 1), ColonyId = c.Id, X = c.NestX, Y = c.NestY };
            a.InitFromVigor(10f, 90f, 0.8f);
            c.Adults.Add(a);
        }
        // Huevo casi eclosionando para forzar la espera.
        c.Eggs.Add(new BroodMember { Kind = BroodKind.Egg, Insert = c.InsertCounter++, G0 = 0.8f });

        for (int i = 0; i < 600; i++)
        {
            c.Stock = 1000f;
            Step(c, new List<SimEvent>());
            Assert.True(c.AdultCountAlive <= ColonyController.MaxAdultsPerColony);
            Assert.True(c.Eggs.Count <= ColonyController.EggCap);
        }
    }

    [Fact]
    public void RichStock_ProducesMoreLaying_ThanScarce()
    {
        var rich = NewColony(stock: 90f);
        var scarce = NewColony(stock: 9f);
        scarce.ConsumeEma = 5f;

        var evR = new List<SimEvent>();
        var evS = new List<SimEvent>();
        for (int i = 0; i < 1200; i++)
        {
            Step(rich, evR);
            Step(scarce, evS);
        }

        // Con abundancia nacen más huevos que con escasez (FR-03).
        Assert.True(rich.Eggs.Count + rich.Larvae.Count + rich.Pupae.Count
                    > scarce.Eggs.Count + scarce.Larvae.Count + scarce.Pupae.Count);
    }

    private static void AssertClose(float expected, float actual, float tolerance = 1e-3f)
        => Assert.True(Math.Abs(expected - actual) < tolerance,
            $"Esperado {expected}, obtenido {actual}");
}