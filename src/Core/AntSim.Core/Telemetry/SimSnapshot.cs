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
    /// <summary>Pose interpolable de una hormiga (por índice de colonia/ant).
    /// F4.2: incluye los datos de inspección (vigor, energía, edad, huella del
    /// genoma) para la tarjeta de seguimiento del HUD.</summary>
    public readonly struct AntPose
    {
        public readonly uint Id;
        public readonly int ColonyId;
        public readonly float X;
        public readonly float Y;
        public readonly float Heading;
        public readonly bool HasLoad;
        public readonly bool Alive;
        // — Inspección (F4.2) —
        public readonly float Vigor;          // [0,~1.2] modulador físico del individuo
        public readonly float Energy;         // [0,1] reserva normalizada
        public readonly float Age;            // s de sim
        public readonly bool IsImmigrant;     // en prueba de cuarentena
        public readonly uint GenomeFingerprint; // huella determinista del genoma (primeros pesos + tamaño)
        /// <summary>F5.2c rodaja 6 — resumen topológico del cerebro NEAT
        /// ("h/c" con n implícito: 19 entradas y 6 salidas son fijas) o cadena
        /// vacía para el MLP clásico (el contrato v1 no cambia para los mundos
        /// existentes: el campo es opcional, posicional y solo aparece en NEAT).</summary>
        public readonly string BrainShape;

        public AntPose(uint id, int colonyId, float x, float y, float heading, bool hasLoad, bool alive,
            float vigor = 0f, float energy = 0f, float age = 0f, bool isImmigrant = false,
            uint genomeFingerprint = 0, string brainShape = "")
        {
            Id = id; ColonyId = colonyId; X = x; Y = y; Heading = heading; HasLoad = hasLoad; Alive = alive;
            Vigor = vigor; Energy = energy; Age = age; IsImmigrant = isImmigrant;
            GenomeFingerprint = genomeFingerprint;
            BrainShape = brainShape;
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
        // F5.2a.2: hongo (0/0 = especies sin hongo — default tolerante).
        public readonly float Fungus;
        public readonly float FungusMax;

        public ColonyView(int id, float nestX, float nestY, int adultsAlive,
            int eggs, int larvae, int pupae, float stock, float stockMax, int eliteCount,
            float fungus = 0f, float fungusMax = 0f)
        {
            Id = id; NestX = nestX; NestY = nestY; AdultsAlive = adultsAlive;
            Eggs = eggs; Larvae = larvae; Pupae = pupae;
            Stock = stock; StockMax = stockMax; EliteCount = eliteCount;
            Fungus = fungus; FungusMax = fungusMax;
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
                ants.Add(new AntPose(a.Id, colony.Id, a.X, a.Y, a.Heading, a.HasLoad, a.Alive,
                    a.Vigor, a.Energy, a.Age, a.IsImmigrantTrial,
                    Fingerprint(a.Genome), BrainShapeOf(a.Brain)));
            }
        }

        var colonies = new List<ColonyView>(sim.Colonies.Count);
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var colony = sim.Colonies[c];
            colonies.Add(new ColonyView(colony.Id, colony.NestX, colony.NestY,
                colony.AdultCountAlive, colony.Eggs.Count, colony.Larvae.Count,
                colony.Pupae.Count, colony.Stock, colony.StockMax, colony.Pool.EliteCount,
                colony.Fungus, colony.FungusMax));
        }

        return new Frame(sim.Tick, ants, sim.Items, colonies);
    }

    /// <summary>
    /// Huella determinista del genoma para la UI (identificar "el mismo cerebro"
    /// sin serializar pesos): tamaño + primeros 4 pesos mezclados por bits.
    /// Cero alocación; genoma null ⇒ 0 (sin cerebro aún).
    /// </summary>
    /// <summary>Huella de identidad de un genoma (la expone el canal A como
    /// "cerebro"): pública para que tests y herramientas comparen linajes.</summary>
    public static uint Fingerprint(Evolution.MlpGenome? genome)
    {
        if (genome is null) return 0;
        var sizes = genome.Sizes; // clone — barato: 3-4 ints
        uint h = 2166136261u;
        h = (h ^ (uint)sizes.Length) * 16777619u;
        for (int i = 0; i < sizes.Length; i++)
            h = (h ^ (uint)sizes[i]) * 16777619u;
        var w = genome.CopyWeights(); // clone — el contrato no expone el array interno
        int take = Math.Min(4, w.Length);
        for (int i = 0; i < take; i++)
        {
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(w[i]), 0);
            h = (h ^ bits) * 16777619u;
        }
        return h;
    }

    /// <summary>
    /// F5.2c rodaja 6 — resumen n/h/c del cerebro (linaje): solo para NEAT (el
    /// MLP clásico devuelve cadena vacía — el canal A de los mundos v1 no
    /// cambia). Sin alocación por tick: la descripción vive cacheada en el
    /// <see cref="NeatBrain"/>.
    /// </summary>
    public static string BrainShapeOf(Brain.IBrain brain)
    {
        return brain is Brain.NeatBrain nb
            ? string.Concat(
                nb.Graph.HiddenCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "h/",
                nb.Graph.ActiveConnCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "c")
            : "";
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
    private long _leafCuts, _fungusFed, _fungusDigested; // F5.2a.3
    private long _strikes, _robbedMilliEp, _raidInflows; // F5.2b.3: incursiones (robbed en centésimas de ep)
    // F5.2b.3: telemetría ACUMULADA de incursiones desde la última lectura del
    // bloque `raids` (los golpes son raros: la ventana de 1 s casi nunca los
    // pilla — patrón acumulativo del RelayTracker, no el de ventana).
    private readonly Dictionary<int, long[]> _raidSinceRead = new(); // [strikes, inflows]
    private readonly Dictionary<int, long[]> _perColony = new(); // F4.2: [pickups,unloads,births,deaths,eggs,eclosed] · F5.2a.3: [6]=leafCuts, [7]=fungusFed · F5.2b.3: [8]=strikes, [9]=robbedCentiEp, [10]=raidInflows
    private ulong _windowStartTick = ulong.MaxValue; // ulong.MaxValue = ventana sin abrir

    /// <summary>Frame agregado desde el último <c>TakeFrame</c> (ventana cerrada).</summary>
    public readonly struct MetricFrame
    {
        public readonly ulong TickStart;
        public readonly ulong TickEnd;
        public readonly long Pickups, Unloads, Births, Deaths, EggsLaid, Eclosed, ItemsConsumed, Commands;
        // F5.2a.3: contadores de la cadena de la cortadora.
        public readonly long LeafCuts, FungusFed, FungusDigested;

        public MetricFrame(ulong tickStart, ulong tickEnd,
            long pickups, long unloads, long births, long deaths,
            long eggsLaid, long eclosed, long itemsConsumed, long commands,
            long leafCuts = 0, long fungusFed = 0, long fungusDigested = 0)
        {
            TickStart = tickStart; TickEnd = tickEnd;
            Pickups = pickups; Unloads = unloads; Births = births; Deaths = deaths;
            EggsLaid = eggsLaid; Eclosed = eclosed; ItemsConsumed = itemsConsumed; Commands = commands;
            LeafCuts = leafCuts; FungusFed = fungusFed; FungusDigested = fungusDigested;
        }
    }

    /// <summary>Observa los eventos de un paso (llamar tras cada Step, como RelayTracker).</summary>
    public void Observe(IReadOnlyList<SimEvent> events)
    {
        if (_windowStartTick == ulong.MaxValue && events.Count > 0)
            _windowStartTick = events[0].Tick;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            switch (ev.Kind)
            {
                case SimEventKind.Pickup: _pickups++; Bump(ev.ColonyId, 0); break;
                case SimEventKind.Unload: _unloads++; Bump(ev.ColonyId, 1); break;
                case SimEventKind.AntBorn: _births++; Bump(ev.ColonyId, 2); break;
                case SimEventKind.AntDied: _deaths++; Bump(ev.ColonyId, 3); break;
                case SimEventKind.EggLaid: _eggsLaid++; Bump(ev.ColonyId, 4); break;
                case SimEventKind.Eclosed: _eclosed++; Bump(ev.ColonyId, 5); break;
                case SimEventKind.ItemConsumed: _itemsConsumed++; break;
                case SimEventKind.CommandExecuted: _commands++; break;
                case SimEventKind.LeafCut: _leafCuts++; Bump(ev.ColonyId, 6); break;
                case SimEventKind.FungusFed: _fungusFed++; Bump(ev.ColonyId, 7); break;
                case SimEventKind.FungusDigested: _fungusDigested++; break;
                // F5.2b.3: cadena del saqueo. StockRobbed lleva los ep en Cause
                // (centésimas, sin float); el robo se contabiliza en la VÍCTIMA.
                case SimEventKind.Strike: _strikes++; Bump(ev.ColonyId, 8); RaidBump(ev.ColonyId, 0); break;
                case SimEventKind.StockRobbed:
                    _robbedMilliEp += ev.Cause; // centésimas de ep, entero (sin float)
                    Bump(ev.ColonyId, 9);
                    break;
                case SimEventKind.RaidInflow: _raidInflows++; Bump(ev.ColonyId, 10); RaidBump(ev.ColonyId, 1); break;
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
            _pickups, _unloads, _births, _deaths, _eggsLaid, _eclosed, _itemsConsumed, _commands,
            _leafCuts, _fungusFed, _fungusDigested);

        _pickups = _unloads = _births = _deaths = 0;
        _eggsLaid = _eclosed = _itemsConsumed = _commands = 0;
        _leafCuts = _fungusFed = _fungusDigested = 0;
        _strikes = 0; _robbedMilliEp = 0; _raidInflows = 0;
        foreach (var kv in _perColony) Array.Clear(kv.Value, 0, kv.Value.Length);
        _windowStartTick = end;
        return frame;
    }

    /// <summary>Ventana por colonia (F4.2): contadores de la ventana abierta para
    /// la tarjeta del HUD. Orden canónico por id de colonia ascendente.</summary>
    public IReadOnlyList<(int ColonyId, long Pickups, long Unloads, long Births, long Deaths, long Eggs, long Eclosed)> ColonyWindows()
    {
        var list = new List<(int, long, long, long, long, long, long)>();
        foreach (var kv in _perColony)
            list.Add((kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5]));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    /// <summary>Ventana por colonia F5.2a.3: cortes y FungusFed de la ventana
    /// abierta (la tarjeta Atta muestra «3 cortes / 2 al hongo»). Orden
    /// canónico por id de colonia ascendente.</summary>
    public IReadOnlyList<(int ColonyId, long LeafCuts, long FungusFed)> CutterWindows()
    {
        var list = new List<(int, long, long)>();
        foreach (var kv in _perColony)
            list.Add((kv.Key, kv.Value[6], kv.Value[7]));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    /// <summary>
    /// Ventana por colonia F5.2b.3: la cadena del saqueo de la ventana abierta
    /// — golpes INFLIGIDOS y botín DESCARGADO en el nido (el «robado» va en la
    /// tarjeta de la víctima vía canal B, aquí la tarjeta del atacante). Orden
    /// canónico por id de colonia ascendente.
    /// </summary>
    public IReadOnlyList<(int ColonyId, long Strikes, long RaidInflows)> RaidWindows()
    {
        var list = new List<(int, long, long)>();
        foreach (var kv in _perColony)
            list.Add((kv.Key, kv.Value[8], kv.Value[10]));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    /// <summary>
    /// F5.2b.3 — incursiones ACUMULADAS desde la última <c>TakeRaids</c>, para
    /// el bloque `raids` del stream (telemetría cada 120 ticks): los golpes son
    /// eventos raros y la ventana de 1 s los pasaría sin verlos. Lectura
    /// destructiva como TakeFrame, orden canónico por colonia.
    /// </summary>
    public IReadOnlyList<(int ColonyId, long Strikes, long RaidInflows)> TakeRaids()
    {
        var list = new List<(int, long, long)>();
        foreach (var kv in _raidSinceRead)
            list.Add((kv.Key, kv.Value[0], kv.Value[1]));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        foreach (var kv in _raidSinceRead) Array.Clear(kv.Value, 0, kv.Value.Length);
        return list;
    }

    private void RaidBump(int colonyId, int idx)
    {
        if (colonyId < 0) return;
        if (!_raidSinceRead.TryGetValue(colonyId, out var arr))
        {
            arr = new long[2];
            _raidSinceRead[colonyId] = arr;
        }
        arr[idx]++;
    }

    private void Bump(int colonyId, int idx)
    {
        if (colonyId < 0) return; // eventos sin colonia (spawns regulares, comandos)
        if (!_perColony.TryGetValue(colonyId, out var arr))
        {
            arr = new long[11]; // F5.2a.3: +leafCuts/fungusFed · F5.2b.3: +strikes/robbed/inflows
            _perColony[colonyId] = arr;
        }
        arr[idx]++;
    }
}
