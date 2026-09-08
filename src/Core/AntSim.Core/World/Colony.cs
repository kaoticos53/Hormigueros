using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Pheromone;
using AntSim.Core.Sim;

namespace AntSim.Core.World;

/// <summary>
/// Estado completo de una colonia: entradas, capas de feromonas propias,
/// adultos, cría y el estado interno del ColonyController (EWMAs y
/// acumuladores ocultos — se serializan en los checkpoints).
/// </summary>
public sealed class Colony
{
    public int Id;
    public SpeciesDescriptor Species = SpeciesDescriptor.LasiusNiger;
    public float NestX;
    public float NestY;

    // — Reservas —
    public float Stock;         // ep
    public float StockMax;

    // — Reina —
    public float QueenEnergy = 1f; // [0,1]

    // — RNG propio (Fork del flujo del mundo) —
    public DeterministicRandom Rng;

    // — Cerebro por defecto de la colonia (los genomas llegan en Fase 2) —
    // Se asigna siempre en la construcción (WorldSim o tests).
    public IBrain Brain = null!;

    // — Feromonas por colonia —
    public PheromoneLayer FoodLayer = null!;
    public PheromoneLayer HomeLayer = null!;
    public PheromoneLayer AlarmLayer = null!;

    // — Demografía —
    public List<Ant> Adults = new();
    public List<BroodMember> Eggs = new();
    public List<BroodMember> Larvae = new();
    public List<BroodMember> Pupae = new();

    // — Estado del ColonyController (acumuladores ocultos) —
    public float InflowEma;      // ep/s suavizado (descargas al nido)
    public float InflowAccum;    // ep acumulados en el paso (el controlador calcula la tasa)
    public float ConsumeEma;     // ep/s suavizado (alimentación real)
    public float EggAccumulator; // fracciones de huevo pendientes
    public float CannibalAccumulator;
    public int InsertCounter;    // orden de creación de la cría

    public int AdultCountAlive
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Adults.Count; i++)
                if (Adults[i].Alive) n++;
            return n;
        }
    }

    public void RecordInflow(float ep)
    {
        Stock += ep;
        if (Stock > StockMax) Stock = StockMax;
        // La EMA de entrada se actualiza en el paso del controlador.
    }
}