using System;
using System.Linq;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2a.1 — ítems compuestos (hojas): el pickup sobre una hoja NO la elimina,
/// le resta un corte y la hormiga se lleva un fragmento. Con LeafFraction = 0
/// el mundo es byte a byte el actual (hash invariante — los 3 pines de CI no
/// se mueven).
/// </summary>
public class CompoundItemTests : IDisposable
{
    private readonly WorldSim _sim;
    private readonly Colony _colony;

    public CompoundItemTests()
    {
        _sim = new WorldSim(42UL, 96, 1) { LeafFraction = 1f, TargetItems = 0 };
        _colony = _sim.Colonies[0];
    }

    public void Dispose()
    {
        // WorldSim no es IDisposable; el hook queda por simetría con los
        // tests que crean varios mundos y los dejan al GC.
    }

    // — Helpers ————————————————————————————————————————————————

    /// <summary>Hormiga pegada a un ítem, lista para interactuar.</summary>
    private Ant PlaceAt(FoodItem item)
    {
        var ant = new Ant
        {
            Id = 9000, ColonyId = _colony.Id,
            X = item.X, Y = item.Y, Heading = 0f
        };
        ant.InitFromVigor(_colony.Species.EnergyCapacity, _colony.Species.BaseLifespan, 1f);
        _colony.Adults.Add(ant);
        return ant;
    }

    private static FoodItem Leaf(float x, float y, float amount, int cuts)
        => new() { Id = 7000, X = x, Y = y, Amount = amount, CutsLeft = cuts, CutsInitial = cuts };

    /// <summary>Un paso con decisión de interacción plena (WantsInteraction).</summary>
    private static AntDecision InteractDecision() => new()
    {
        Steer = 0f, Speed = 0f, DepositFood = 0f, DepositHome = 0f, DepositAlarm = 0f,
        Interact = 1f
    };

    // — Corte ——————————————————————————————————————————————————

    [Fact]
    public void PickupSobreHoja_LaDejaVivaConUnCorteMenos()
    {
        var leaf = Leaf(200, 200, amount: 12f, cuts: 4);
        _sim.AddItem(leaf);
        var ant = PlaceAt(leaf);
        ant.Brain = new FixedDecisionBrain(InteractDecision());

        float esperado = leaf.Amount / leaf.CutsInitial; // 3 ep — ANTES del paso
        _sim.Step();

        Assert.True(ant.HasLoad);
        Assert.Equal(esperado, ant.LoadValue, 3);
        Assert.Contains(leaf, _sim.Items);
        Assert.Equal(3, leaf.CutsLeft);
    }

    [Fact]
    public void UltimoCorte_EliminaLaHojaYEmiteItemConsumed()
    {
        var leaf = Leaf(200, 200, amount: 9f, cuts: 3);
        _sim.AddItem(leaf);
        var ant = PlaceAt(leaf);
        ant.Brain = new FixedDecisionBrain(InteractDecision());

        // 3 cortes: la hoja aguanta los dos primeros y cae en el tercero.
        for (int i = 0; i < 3; i++)
        {
            _sim.Step();
            ant = _colony.Adults[0];
            ant.X = leaf.X; ant.Y = leaf.Y;         // vuelve a la hoja
            ant.HasLoad = false; ant.LoadValue = 0f; // simula descarga fuera de nido
            ant.InteractCooldown = 0f;
        }

        Assert.DoesNotContain(leaf, _sim.Items);
        Assert.Contains(_sim.LastEvents, e => e.Kind == SimEventKind.ItemConsumed);
    }

    [Fact]
    public void CorteReduceElValorDelItem_PeroElFragmentoEsInvariante()
    {
        var leaf = Leaf(200, 200, amount: 10f, cuts: 5);
        _sim.AddItem(leaf);
        var ant = PlaceAt(leaf);
        ant.Brain = new FixedDecisionBrain(InteractDecision());

        _sim.Step();

        Assert.Equal(2f, ant.LoadValue, 3);           // fragmento 10/5
        Assert.Equal(8f, leaf.Amount, 3);             // la hoja pierde el valor cortado
        Assert.Equal(4, leaf.CutsLeft);
    }

    [Fact]
    public void ItemSimple_ComportamientoActual()
    {
        // CutsLeft = 0: el pickup de toda la vida — elimina el ítem completo.
        var simple = new FoodItem { Id = 7001, X = 200, Y = 200, Amount = 5f };
        _sim.AddItem(simple);
        var ant = PlaceAt(simple);
        ant.Brain = new FixedDecisionBrain(InteractDecision());

        _sim.Step();

        Assert.True(ant.HasLoad);
        Assert.Equal(5f, ant.LoadValue, 3);
        Assert.DoesNotContain(simple, _sim.Items);
    }

    // — Spawn de hojas ————————————————————————————————————————

    [Fact]
    public void LeafFraction_Uno_TodasLasHojasNuevas()
    {
        // Los ítems del CONSTRUCTOR nacen antes de poder fijar LeafFraction
        // (inicializador de objeto): lo verificable es que TODO ítem que
        // reaparece por RespawnItems sea hoja con cortes 3..5 intactos.
        var sim = new WorldSim(7UL, 96, 1) { LeafFraction = 1f };
        // Vaciar fuerza a RespawnItems a repoblar TODO con la regla nueva.
        sim.ClearItems();
        sim.Step();
        int hojas = 0;
        foreach (var it in sim.Items)
        {
            if (it.CutsLeft == 0) continue; // ítem inicial o suelta de portadora
            hojas++;
            Assert.InRange(it.CutsInitial, 3, 5);
            Assert.Equal(it.CutsInitial, it.CutsLeft); // recién nacida, sin cortes
            Assert.InRange(it.Amount, 8f, 14f);        // valor de hoja, no de simple
        }
        Assert.True(hojas > 0, "con LeafFraction=1 algún respawn debe ser hoja");
    }

    [Fact]
    public void LeafFraction_Cero_NingunCambioDeComportamiento()
    {
        // Dos mundos idénticos: el nuevo código no debe tocar nada cuando
        // LeafFraction = 0 — misma tirada de RNG, mismos ítems, mismo hash.
        var a = new WorldSim(99UL, 96, 2);
        var b = new WorldSim(99UL, 96, 2) { LeafFraction = 0f };
        for (int i = 0; i < 300; i++) { a.Step(); b.Step(); }
        Assert.Equal(a.HashLine(), b.HashLine());
    }

    [Fact]
    public void LeafFraction_Determinista_MismaSemillaMismosCortes()
    {
        var a = new WorldSim(55UL, 96, 1) { LeafFraction = 0.6f };
        var b = new WorldSim(55UL, 96, 1) { LeafFraction = 0.6f };
        for (int i = 0; i < 300; i++) { a.Step(); b.Step(); }
        Assert.Equal(a.HashLine(), b.HashLine());
        var porId = b.Items.ToDictionary(it => it.Id);
        Assert.All(a.Items, it => Assert.Equal(
            (it.CutsLeft, it.CutsInitial),
            (porId[it.Id].CutsLeft, porId[it.Id].CutsInitial)));
    }

    // — Hash incluye CutsLeft ——————————————————————————————————

    [Fact]
    public void Hash_DistingueHojasConDistintosCortes()
    {
        var a = new WorldSim(42UL, 96, 1) { TargetItems = 0 };
        var b = new WorldSim(42UL, 96, 1) { TargetItems = 0 };
        var la = new FoodItem { Id = 1, X = 200, Y = 200, Amount = 8f, CutsLeft = 4, CutsInitial = 4 };
        var lb = new FoodItem { Id = 1, X = 200, Y = 200, Amount = 8f, CutsLeft = 3, CutsInitial = 4 };
        a.AddItem(la); b.AddItem(lb);
        // Mismas poses, mismo Amount — solo CutsLeft difiere.
        Assert.NotEqual(a.HashLine(), b.HashLine());
    }

    [Fact]
    public void Hash_ConCutsCero_IdenticoAlMundoSinHojas()
    {
        // El caso del pín de CI: ítems simples (CutsLeft = 0) no alteran el hash.
        var a = new WorldSim(42UL, 96, 1) { TargetItems = 0 };
        var b = new WorldSim(42UL, 96, 1) { TargetItems = 0 };
        a.AddItem(new FoodItem { Id = 1, X = 200, Y = 200, Amount = 5f });
        b.AddItem(new FoodItem { Id = 1, X = 200, Y = 200, Amount = 5f, CutsLeft = 0, CutsInitial = 0 });
        Assert.Equal(a.HashLine(), b.HashLine());
    }

    // — Al morir una portadora de fragmento ————————————————————

    [Fact]
    public void PortadoraDeFragmentoQueMuere_SueltaUnItemSimple()
    {
        // El fragmento cortado es comida normal: al caer la hormiga, se vuelve
        // a engañar al mundo como ítem sin cortes (el suelo no sabe de hojas).
        var leaf = Leaf(200, 200, amount: 12f, cuts: 4);
        _sim.AddItem(leaf);
        var ant = PlaceAt(leaf);
        ant.Brain = new FixedDecisionBrain(InteractDecision());
        _sim.Step();
        Assert.True(ant.HasLoad);

        ant.Energy = 0f; // muerte por hambre en el próximo paso
        _sim.Step();

        var dropped = _sim.Items;
        Assert.Contains(dropped, it => it.Id != leaf.Id && it.Amount == ant.LoadValue && it.CutsLeft == 0);
    }
}

/// <summary>Cerebro de prueba con decisión fija (sin RNG ni evaluación real).</summary>
public sealed class FixedDecisionBrain : IBrain
{
    private readonly AntDecision _decision;
    public FixedDecisionBrain(AntDecision decision) => _decision = decision;
    public BrainKind Kind => BrainKind.Mlp;
    public int ContractVersion => BrainContract.CurrentVersion;
    public void Evaluate(in AntSensors sensors, ref AntDecision decision) => decision = _decision;
}
