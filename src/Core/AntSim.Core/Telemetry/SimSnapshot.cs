using System;
using System.Collections.Generic;
using AntSim.Core.World;

namespace AntSim.Core.Telemetry;

/// <summary>
/// Canal A — <c>SimSnapshot</c> (Fase 4, F4.0). Construcción PULL por tick desde
/// el <see cref="WorldSim"/>: poses interpolables + estado de colonia para el
/// presenter. Nunca muta el mundo, nunca consume RNG — los hashes y las semillas
/// no se tocan (verificado con tests). La vista no puede tocar el estado: lee.
/// </summary>
public static class SimSnapshot
{
    /// <summary>Pose interpolable de una hormiga (por índice de colonia/ant).</summary>
    public readonly struct AntPose
    {
        public readonly uint Id;
        public readonly int ColonyId;
        public readonly float X;
        public readonly float Y;
        public readonly float Heading;
        public readonly bool HasLoad;
        public readonly bool Alive;

        public AntPose(uint id, int colonyId, float x, float y, float heading, bool hasLoad, bool alive)
        {
            Id = id; ColonyId = colonyId; X = x; Y = y; Heading = heading; HasLoad = hasLoad; Alive = alive;
        }
    }

    /// <summary>Estado visible de una colonia (tarjeta del HUD).</summary>
    public readonly struct ColonyView
    {
        public readonly int Id;
        public readonly float NestX;
        public readonly float NestY;
        public readonly int AdultsAlive;
        public readonly int Eggs;
        public readonly int Larvae;
        public readonly int Pupae;
        public readonly float Stock;
        public readonly float StockMax;
        public readonly int EliteCount;

        public ColonyView(int id, float nestX, float nestY, int adultsAlive,
            int eggs, int larvae, int pupae, float stock, float stockMax, int eliteCount)
        {
            Id = id; NestX = nestX; NestY = nestY; AdultsAlive = adultsAlive;
            Eggs = eggs; Larvae = larvae; Pupae = pupae;
            Stock = stock; StockMax = stockMax; EliteCount = eliteCount;
        }
    }

    /// <summary>Snapshot completo de un tick: poses + items + vista de colonias.</summary>
    public readonly struct Frame
    {
        public readonly ulong Tick;
        public readonly IReadOnlyList<AntPose> Ants;
        public readonly IReadOnlyList<FoodItem> Items;
        public readonly IReadOnlyList<ColonyView> Colonies;

        public Frame(ulong tick, IReadOnlyList<AntPose> ants,
            IReadOnlyList<FoodItem> items, IReadOnlyList<ColonyView> colonies)
        {
            Tick = tick; Ants = ants; Items = items; Colonies = colonies;
        }
    }

    /// <summary>
    /// Construye el frame del tick actual. Solo lectura: recorre las listas del
    /// mundo y copia valores; no alocación del Core más allá de la lista devuelta.
    /// </summary>
    public static Frame Capture(WorldSim sim)
    {
        if (sim is null) throw new ArgumentNullException(nameof(sim));

        var ants = new List<AntPose>();
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var colony = sim.Colonies[c];
            for (int i = 0; i < colony.Adults.Count; i++)
            {
                var a = colony.Adults[i];
                ants.Add(new AntPose(a.Id, colony.Id, a.X, a.Y, a.Heading, a.HasLoad, a.Alive));
            }
        }

        var colonies = new List<ColonyView>(sim.Colonies.Count);
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var colony = sim.Colonies[c];
            colonies.Add(new ColonyView(colony.Id, colony.NestX, colony.NestY,
                colony.AdultCountAlive, colony.Eggs.Count, colony.Larvae.Count,
                colony.Pupae.Count, colony.Stock, colony.StockMax, colony.Pool.EliteCount));
        }

        return new Frame(sim.Tick, ants, sim.Items, colonies);
    }
}

/// <summary>
/// Canal C — <c>MetricFrame</c> (Fase 4, F4.0). Agregación de contadores a ritmo
/// fijo (1 s de sim = 30 ticks): el recorder observa el flujo de eventos del Canal B
/// (mismo patrón de <c>RelayTracker</c>) y expone frames acumulados de ventana.
/// Puro observador: idénticos hashes con y sin él adjunto.
/// </summary>
public sealed class MetricRecorder
{
    public const int TicksPerFrame = 30; // SimConstants.FixedDtSeconds = 1/30

    private long _pickups, _unloads, _births, _deaths, _eggsLaid, _eclosed, _itemsConsumed, _commands;
    private ulong _windowStartTick = ulong.MaxValue; // ulong.MaxValue = ventana sin abrir

    /// <summary>Frame agregado desde el último <c>TakeFrame</c> (ventana cerrada).</summary>
    public readonly struct MetricFrame
    {
        public readonly ulong TickStart;
        public readonly ulong TickEnd;
        public readonly long Pickups, Unloads, Births, Deaths, EggsLaid, Eclosed, ItemsConsumed, Commands;

        public MetricFrame(ulong tickStart, ulong tickEnd,
            long pickups, long unloads, long births, long deaths,
            long eggsLaid, long eclosed, long itemsConsumed, long commands)
        {
            TickStart = tickStart; TickEnd = tickEnd;
            Pickups = pickups; Unloads = unloads; Births = births; Deaths = deaths;
            EggsLaid = eggsLaid; Eclosed = eclosed; ItemsConsumed = itemsConsumed; Commands = commands;
        }
    }

    /// <summary>Observa los eventos de un paso (llamar tras cada Step, como RelayTracker).</summary>
    public void Observe(IReadOnlyList<SimEvent> events)
    {
        if (_windowStartTick == ulong.MaxValue && events.Count > 0)
            _windowStartTick = events[0].Tick;

        for (int i = 0; i < events.Count; i++)
        {
            switch (events[i].Kind)
            {
                case SimEventKind.Pickup: _pickups++; break;
                case SimEventKind.Unload: _unloads++; break;
                case SimEventKind.AntBorn: _births++; break;
                case SimEventKind.AntDied: _deaths++; break;
                case SimEventKind.EggLaid: _eggsLaid++; break;
                case SimEventKind.Eclosed: _eclosed++; break;
                case SimEventKind.ItemConsumed: _itemsConsumed++; break;
                case SimEventKind.CommandExecuted: _commands++; break;
            }
        }
    }

    /// <summary>
    /// Cierra la ventana si alcanzó <see cref="TicksPerFrame"/> ticks y devuelve el
    /// frame (o null si la ventana sigue abierta). Los contadores de la ventana
    /// se reinician; el tick final del frame es el del último evento visto.
    /// </summary>
    public MetricFrame? TakeFrame(ulong currentTick)
    {
        if (_windowStartTick == ulong.MaxValue) return null;
        ulong end = currentTick;
        if (end < _windowStartTick + TicksPerFrame) return null;

        var frame = new MetricFrame(_windowStartTick, end,
            _pickups, _unloads, _births, _deaths, _eggsLaid, _eclosed, _itemsConsumed, _commands);

        _pickups = _unloads = _births = _deaths = 0;
        _eggsLaid = _eclosed = _itemsConsumed = _commands = 0;
        _windowStartTick = end;
        return frame;
    }
}
