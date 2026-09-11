using System;
using System.Collections.Generic;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// Métricas de salud del RELEVO (Fase 3ter) acumuladas desde el flujo de
/// eventos, SIN tocar la simulación (los hashes del mundo no cambian — la
/// detección es de solo lectura). Permiten detectar regresiones del relevo
/// intergeneracional directamente en el reporte del CLI, sin sondas:
///
///   - <see cref="FirstUnloadTick"/>: primer tick con un evento Unload
///     (0 = ninguna descarga; el primer ciclo completo del relevo).
///   - <see cref="DropCount"/> / <see cref="DropDistanceMean"/>: distancia al
///     nido de cada suelta por muerte de portadora (la portadora muere con la
///     carga lejos del nido y el mundo re-spawnea el ítem allí — es el eslabón
///     que la descendencia debe completar). Cuanto más cerca del nido caen las
///     sueltas, más sano está el homing; un drop-avg en descenso indica que la
///     política está cerrando el ciclo.
///   - <see cref="UnloadCount"/> / <see cref="UnloadDistanceMean"/>: el ÚLTIMO
///     eslabón — distancia al nido en el momento de cada descarga (por
///     construcción ≤ NestRadius = 24 u). No mide el tramo recorrido: para eso
///     está <see cref="CarryLegMean"/> — distancia del PICKUP a la descarga de
///     cada carga completada, i.e. cuánto del ciclo cierra el portador que
///     termina. Un relevo sano (descendencia que recoge sueltas lejanas) da
///     carry-leg ~150-250 u; un pool que solo descarga lo que recoge en el
///     anillo cercano al nido da carry-leg ~0-50 u. Es la métrica que separa
///     "descarga del fundador" de "descarga por relevo".
///
/// Distinción de eventos: el spawn REGULAR de ítems emite ItemSpawned con el
/// centinela ColonyId = -1 (WorldSim.SpawnItem); la suelta por muerte emite
/// ItemSpawned con el ColonyId y el AntId reales de la portadora
/// (WorldSim.ApplyDeaths). Esa diferencia es la que separa ambos casos.
/// </summary>
public sealed class RelayTracker
{
    // (colonyId, antId) → posición del pickup sin descargar todavía.
    private readonly Dictionary<(int Colony, uint Ant), (float X, float Y)> _openCarries = new();

    // — F4.2: desglose por colonia (la tarjeta del HUD necesita su propio semáforo) —
    private sealed class ColonyRelay
    {
        public ulong FirstUnloadTick;
        public long UnloadDistanceSum;
        public int UnloadCount;
        public long CarryLegSum;
        public int CarryLegCount;
        public long DropDistanceSum;
        public int DropCount;
        // F4.4: cadena disponible por carga (pickup→nido − radio de descarga).
        public long ChainSum;
        public int ChainCount;

        public double? UnloadMean => UnloadCount > 0 ? UnloadDistanceSum / (double)UnloadCount : null;
        public double? CarryLegMean => CarryLegCount > 0 ? CarryLegSum / (double)CarryLegCount : null;
        public double? DropMean => DropCount > 0 ? DropDistanceSum / (double)DropCount : null;
        public double? ChainMean => ChainCount > 0 ? ChainSum / (double)ChainCount : null;
    }

    private readonly Dictionary<int, ColonyRelay> _perColony = new();

    private ColonyRelay Colony(int colonyId)
    {
        if (!_perColony.TryGetValue(colonyId, out var cr))
        {
            cr = new ColonyRelay();
            _perColony[colonyId] = cr;
        }
        return cr;
    }

    /// <summary>Vista de solo lectura del relevo de una colonia (F4.2). null = sin
    /// eventos registrados para esa colonia todavía.</summary>
    public readonly struct ColonyView
    {
        public readonly ulong FirstUnloadTick;
        public readonly double? UnloadMean;
        public readonly double? CarryLegMean;
        public readonly double? DropMean;
        public readonly int UnloadCount;
        public readonly double? ChainMean; // F4.4: cadena disponible media

        public ColonyView(ulong firstUnloadTick, double? unloadMean, double? carryLegMean,
            double? dropMean, int unloadCount, double? chainMean = null)
        {
            FirstUnloadTick = firstUnloadTick; UnloadMean = unloadMean;
            CarryLegMean = carryLegMean; DropMean = dropMean; UnloadCount = unloadCount;
            ChainMean = chainMean;
        }
    }

    public ColonyView? ForColony(int colonyId)
    {
        if (!_perColony.TryGetValue(colonyId, out var cr)) return null;
        return new ColonyView(cr.FirstUnloadTick, cr.UnloadMean, cr.CarryLegMean, cr.DropMean,
            cr.UnloadCount, cr.ChainMean);
    }

    public ulong FirstUnloadTick { get; private set; }
    public ulong LastUnloadTick { get; private set; }

    public long DropDistanceSum { get; private set; }
    public int DropCount { get; private set; }

    public long UnloadDistanceSum { get; private set; }
    public int UnloadCount { get; private set; }

    public long CarryLegSum { get; private set; }
    public int CarryLegCount { get; private set; }

    /// <summary>Radio de descarga: las sueltas caen a ~24 u del nido (constante
    /// del mundo) — el tramo restante hasta el nido no es trabajo del portador.</summary>
    public const long UnloadRadius = 24;

    public bool HasUnload => FirstUnloadTick > 0;

    public double? DropDistanceMean => DropCount > 0 ? DropDistanceSum / (double)DropCount : null;

    public double? UnloadDistanceMean => UnloadCount > 0 ? UnloadDistanceSum / (double)UnloadCount : null;

    /// <summary>Tramo medio pickup→descarga de las cargas completadas (el eslabón final del relevo).</summary>
    public double? CarryLegMean => CarryLegCount > 0 ? CarryLegSum / (double)CarryLegCount : null;

    /// <summary>F4.4: cadena disponible media (pickup→nido − radio de descarga)
    /// de las cargas completadas — el techo físico del tramo.</summary>
    public double? ChainMean => ChainCount > 0 ? ChainSum / (double)ChainCount : null;

    public long ChainSum { get; private set; }
    public int ChainCount { get; private set; }

    /// <summary>Acumula las métricas de una tanda de eventos (un paso del mundo).</summary>
    public void Observe(IReadOnlyList<SimEvent> events, WorldSim sim)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (sim is null) throw new ArgumentNullException(nameof(sim));

        for (int e = 0; e < events.Count; e++)
        {
            var ev = events[e];
            if (ev.Kind == SimEventKind.Pickup)
            {
                // Nota: un pickup de un genoma que ya tenía una carga abierta con
                // la misma clave no ocurre (HasLoad excluye re-pickup); si la
                // hormiga muere cargada, la muerte la elimina sin Unload — la
                // entrada queda huérfana y se sobrescribe con el próximo pickup
                // del mismo antId (los ids no se reciclan, así que no ocurre).
                _openCarries[(ev.ColonyId, ev.AntId)] = (ev.X, ev.Y);
            }
            else if (ev.Kind == SimEventKind.Unload)
            {
                if (FirstUnloadTick == 0)
                    FirstUnloadTick = ev.Tick;
                LastUnloadTick = ev.Tick;

                var colony = sim.Colonies[ev.ColonyId];
                float udx = ev.X - colony.NestX;
                float udy = ev.Y - colony.NestY;
                long dist = (long)MathF.Sqrt(udx * udx + udy * udy);
                UnloadDistanceSum += dist;
                UnloadCount++;

                var cr = Colony(ev.ColonyId); // F4.2: desglose por colonia
                if (cr.FirstUnloadTick == 0) cr.FirstUnloadTick = ev.Tick;
                cr.UnloadDistanceSum += dist;
                cr.UnloadCount++;

                // Eslabón final: del pickup de esta carga a esta descarga.
                // F4.4: cadena disponible = (pickup→nido) − radio de descarga —
                // el techo físico del tramo. Con comida de proximidad el tramo
                // absoluto es corto por OFERTA, no por torpeza: el ratio
                // leg/cadena mide el % de la cadena que la cría completa.
                if (_openCarries.Remove((ev.ColonyId, ev.AntId), out var pick))
                {
                    float cdx = ev.X - pick.X;
                    float cdy = ev.Y - pick.Y;
                    long leg = (long)MathF.Sqrt(cdx * cdx + cdy * cdy);
                    float pdx = pick.X - colony.NestX;
                    float pdy = pick.Y - colony.NestY;
                    long chain = (long)MathF.Sqrt(pdx * pdx + pdy * pdy) - UnloadRadius;
                    if (chain < 0) chain = 0;
                    CarryLegSum += leg;
                    CarryLegCount++;
                    cr.CarryLegSum += leg;
                    cr.CarryLegCount++;
                    ChainSum += chain;
                    ChainCount++;
                    cr.ChainSum += chain;
                    cr.ChainCount++;
                }
            }
            else if (ev.Kind == SimEventKind.ItemSpawned && ev.ColonyId >= 0
                     && ev.ColonyId < sim.Colonies.Count)
            {
                var col = sim.Colonies[ev.ColonyId];
                float dx = ev.X - col.NestX;
                float dy = ev.Y - col.NestY;
                long ddist = (long)MathF.Sqrt(dx * dx + dy * dy);
                DropDistanceSum += ddist;
                DropCount++;

                var cr = Colony(ev.ColonyId); // F4.2
                cr.DropDistanceSum += ddist;
                cr.DropCount++;
            }
        }
    }
}
