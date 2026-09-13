using System;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2a.2 — el hongo: segunda reserva de las especies cortadoras. La
/// descarga de un fragmento alimenta `Colony.Fungus` (× LeafEfficiency) y la
/// digestión convierte hongo → stock por RecordInflow — la demografía
/// calibrada (InflowEma, gate de puesta, runway) lee la MISMA señal.
/// Lasius/Eciton (FungusMax = 0) mantienen el camino actual byte a byte.
/// </summary>
public class FungusTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public FungusTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    private const ulong Seed = 42UL;

    private static Ant NestCarrier(WorldSim sim, Colony colony, float load)
    {
        // Portadora DENTRO del radio de nido, lista para descargar.
        var ant = new Ant
        {
            Id = 9500, ColonyId = colony.Id,
            X = colony.NestX, Y = colony.NestY, Heading = 0f,
            HasLoad = true, LoadValue = load
        };
        ant.InitFromVigor(colony.Species.EnergyCapacity, colony.Species.BaseLifespan, 1f);
        ant.Brain = new FixedDecisionBrain(new AntDecision { Interact = 1f });
        colony.Adults.Add(ant);
        return ant;
    }

    private static WorldSim NewWorld(SpeciesDescriptor sp, float leafFraction = 0f)
        => new(Seed, 96, 1, species: new[] { sp }, leafFraction: leafFraction);

    // — Descarga al hongo ——————————————————————————————————————

    [Fact]
    public void Atta_DescargaAlimentaHongo_NoElStock()
    {
        var sim = NewWorld(SpeciesDescriptor.Atta);
        var colony = sim.Colonies[0];
        float stockAntes = colony.Stock;
        var ant = NestCarrier(sim, colony, load: 4f);

        sim.Step();
        // En el MISMO paso la digestión ya roe un tick del hongo recién puesto
        // (las hormigas actúan ANTES que el controlador): 3 ep − digestión(3/60).
        float digestidoTick = colony.Species.DigestionRate * (4f * colony.Species.LeafEfficiency / colony.FungusMax) / 30f;
        Assert.Equal(4f * colony.Species.LeafEfficiency - digestidoTick, colony.Fungus, 3);
        Assert.True(ant.Fitness > 0);                          // el relevo paga igual
        Assert.True(ant.LoadValue == 0f && !ant.HasLoad);      // descargó
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.FungusFed);
        // La digestión APORTA stock (≈ digestidoTick) y el consumo del paso
        // quita: el neto queda por debajo de stockAntes + digestidoTick.
        Assert.True(colony.Stock <= stockAntes + digestidoTick + 1e-4f);
    }

    [Fact]
    public void Atta_HongoLleno_ElExcedenteSePierde()
    {
        var sim = NewWorld(SpeciesDescriptor.Atta);
        var colony = sim.Colonies[0];
        colony.Fungus = colony.FungusMax; // 60 ep
        float hongoAntes = colony.Fungus;
        var ant = NestCarrier(sim, colony, load: 4f);

        sim.Step();

        // Lleno al descargar: solo muerde la digestión del paso (0.4 × 1 × dt).
        Assert.Equal(hongoAntes - colony.Species.DigestionRate / 30f, colony.Fungus, 3);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.FungusFed);
        // Importante: el excedente NO va al stock (el hongo es la única vía).
    }

    [Fact]
    public void Lasius_DescargaVaDirectoAlStock_SinHongo()
    {
        var sim = NewWorld(SpeciesDescriptor.LasiusNiger);
        var colony = sim.Colonies[0];
        Assert.Equal(0f, colony.FungusMax);           // la especie no tiene hongo
        colony.Stock = 50f; // media reserva: +4 debe verse (a tope clamparía)
        float stockAntes = colony.Stock;
        var ant = NestCarrier(sim, colony, load: 4f);

        sim.Step();

        Assert.Equal(0f, colony.Fungus);
        _out.WriteLine($"lasius stock: antes={stockAntes} despues={colony.Stock}");
        Assert.True(colony.Stock > stockAntes - 1e-4f); // neto: +4 − consumo del paso
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.Unload);
        Assert.DoesNotContain(sim.LastEvents, e => e.Kind == SimEventKind.FungusFed);
    }

    // — Digestión ——————————————————————————————————————————————

    [Fact]
    public void Digestion_HongoVacio_NoDigiere()
    {
        var sim = NewWorld(SpeciesDescriptor.Atta);
        var colony = sim.Colonies[0];
        Assert.Equal(0f, colony.Fungus);
        float stockAntes = colony.Stock;

        sim.Step();

        Assert.Equal(0f, colony.Fungus);
        Assert.DoesNotContain(sim.LastEvents, e => e.Kind == SimEventKind.FungusDigested);
    }

    [Fact]
    public void Digestion_ConvierteHongoEnStock_ATravesDelInflow()
    {
        var sim = NewWorld(SpeciesDescriptor.Atta);
        var colony = sim.Colonies[0];
        colony.Fungus = 30f; // llenado 0.5
        float stockAntes = colony.Stock;

        sim.Step(); // dt = 1/30

        float esperado = 0.4f * 0.5f / 30f; // DigestionRate × llenado × dt
        Assert.Equal(30f - esperado, colony.Fungus, 4);
        // InflowAccum lo consume la EWMA al cierre del MISMO paso: lo observable
        // es el stock — digestión entró, consumo (reina+adultas) salió.
        _out.WriteLine($"stock={colony.Stock} antes={stockAntes} esperado+={esperado}");
        Assert.True(colony.Stock >= stockAntes + esperado - 0.02f); // −consumo del paso
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.FungusDigested);
    }

    [Fact]
    public void Digestion_ProporcionalAlLlenado()
    {
        // Llenado doble ⇒ digestión doble (mismo dt).
        var a = NewWorld(SpeciesDescriptor.Atta);
        var b = NewWorld(SpeciesDescriptor.Atta);
        a.Colonies[0].Fungus = 15f;  // llenado 0.25
        b.Colonies[0].Fungus = 30f;  // llenado 0.5
        a.Step(); b.Step();

        float da = 15f - a.Colonies[0].Fungus;
        float db = 30f - b.Colonies[0].Fungus;
        Assert.Equal(2f, db / da, 2);
    }

    // — Ciclo completo: hoja → corte → hongo → digestión → cría ————

    [Fact]
    public void CicloCompleto_HojaAlimentaLaPuesta_Atta()
    {
        // Con hongo + hojas, la colonia sostiene puesta SIN descargas directas
        // al stock: la única entrada es la digestión del hongo.
        var sim = new WorldSim(7UL, 96, 1,
            species: new[] { SpeciesDescriptor.Atta }, leafFraction: 1f);
        var colony = sim.Colonies[0];
        sim.SeedPoolFromGenomes(0, PoolDePrueba());
        colony.Fungus = 30f;     // hongo precargado (el forrajeo tarda ~t4000)
        colony.Stock = 30f;      // runway 30 s: SIN digestión el gate de puesta
                                 // (InflowEma = 0) limita a reposición; CON
                                 // digestión el gate ve ~0.2 ep/s y la puesta corre

        int fungusDigested = 0, eggsAntes = colony.Eggs.Count;
        float stockMin = float.MaxValue;
        for (int i = 0; i < 900; i++) // 30 s de sim
        {
            sim.Step();
            fungusDigested += sim.LastEvents.Count(e => e.Kind == SimEventKind.FungusDigested);
            stockMin = Math.Min(stockMin, colony.Stock);
        }

        Assert.True(fungusDigested > 500, "la digestión debe funcionar casi todos los ticks");
        Assert.True(colony.Eggs.Count + colony.Larvae.Count > eggsAntes,
            "el caudal del hongo debe sostener puesta con stock bajo");
    }

    private static System.Collections.Generic.IReadOnlyList<Evolution.MlpGenome> PoolDePrueba()
    {
        // Pool mínimo: genomas aleatorios bastan (lo que importa aquí es la
        // economía del hongo, no el forrajeo).
        var rng = new Sim.DeterministicRandom(99UL);
        var pool = new System.Collections.Generic.List<Evolution.MlpGenome>();
        var sizes = new[] { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };
        for (int i = 0; i < 4; i++)
            pool.Add(Evolution.MlpGenome.Random(ref rng, sizes));
        return pool;
    }
}
