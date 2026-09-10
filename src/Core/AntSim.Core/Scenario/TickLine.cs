using System;
using System.Collections.Generic;
using System.Text;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// Utilidad de emisión de líneas <c>tick</c> periódicas (canal C). Los hashes
/// NO cambian la simulación (es puro StringBuilder, fuera del mundo), pero el
/// orden de append de cada campo es parte del contrato de salida del CLI:
/// mantenerlo centralizado aquí evita divergencias entre el modo evolve del
/// CLI y el escenario WorldScenario.
/// </summary>
public static class TickLine
{
    /// <summary>
    /// Añade la línea <c>tick N + hash</c> seguida de las métricas de salud del
    /// relevo hasta este instante (first-unload / drop-avg / unload-avg /
    /// carry-leg — el último eslabón pickup→descarga), y opcionalmente las
    /// stats de pool del CLI (canal C).
    /// </summary>
    public static void Append(
        StringBuilder sb,
        WorldSim sim,
        RelayTracker relay,
        bool includePoolStats = false,
        string? poolStatsLine = null)
    {
        if (sb is null) throw new ArgumentNullException(nameof(sb));
        if (sim is null) throw new ArgumentNullException(nameof(sim));
        if (relay is null) throw new ArgumentNullException(nameof(relay));

        sb.Append("tick ").Append(sim.Tick).Append("  ").Append(sim.HashLine()).AppendLine();
        sb.Append("relay tick=").Append(sim.Tick)
          .Append(" first-unload ").Append(relay.HasUnload ? relay.FirstUnloadTick.ToString() : "-")
          .Append(" drop-avg ").Append(relay.DropDistanceMean is double dm
              ? dm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-")
          .Append(" unload-avg ").Append(relay.UnloadDistanceMean is double um
              ? um.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-")
          .Append(" carry-leg ").Append(relay.CarryLegMean is double cl
              ? cl.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-")
          .AppendLine();

        if (includePoolStats && poolStatsLine != null)
            sb.Append(poolStatsLine).AppendLine();
    }
}
