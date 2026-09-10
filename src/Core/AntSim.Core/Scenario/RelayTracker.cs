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
///
/// Distinción de eventos: el spawn REGULAR de ítems emite ItemSpawned con el
/// centinela ColonyId = -1 (WorldSim.SpawnItem); la suelta por muerte emite
/// ItemSpawned con el ColonyId y el AntId reales de la portadora
/// (WorldSim.ApplyDeaths). Esa diferencia es la que separa ambos casos.
/// </summary>
public sealed class RelayTracker
{
    public ulong FirstUnloadTick { get; private set; }

    public long DropDistanceSum { get; private set; }
    public int DropCount { get; private set; }

    public bool HasUnload => FirstUnloadTick > 0;

    public double? DropDistanceMean => DropCount > 0 ? DropDistanceSum / (double)DropCount : null;

    /// <summary>Acumula las métricas de una tanda de eventos (un paso del mundo).</summary>
    public void Observe(IReadOnlyList<SimEvent> events, WorldSim sim)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (sim is null) throw new ArgumentNullException(nameof(sim));

        for (int e = 0; e < events.Count; e++)
        {
            var ev = events[e];
            if (ev.Kind == SimEventKind.Unload)
            {
                if (FirstUnloadTick == 0)
                    FirstUnloadTick = ev.Tick;
            }
            else if (ev.Kind == SimEventKind.ItemSpawned && ev.ColonyId >= 0
                     && ev.ColonyId < sim.Colonies.Count)
            {
                var col = sim.Colonies[ev.ColonyId];
                float dx = ev.X - col.NestX;
                float dy = ev.Y - col.NestY;
                DropDistanceSum += (long)MathF.Sqrt(dx * dx + dy * dy);
                DropCount++;
            }
        }
    }
}