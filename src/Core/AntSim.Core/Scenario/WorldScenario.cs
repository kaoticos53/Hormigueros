using System;
using System.Text;
using AntSim.Core.Serialization;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// Escenario del mundo completo (Fase 1): ejecuta el WorldSim headless y emite
/// hashes de hito del estado cada <see cref="MilestoneEvery"/> ticks. Misma
/// semilla ⇒ misma salida exacta: es la base del modo verificación (Fase 3).
/// </summary>
public static class WorldScenario
{
    public const int MilestoneEvery = 120;

    /// <summary>
    /// Ejecuta el mundo. Con <paramref name="antlogPath"/> graba además un
    /// registro de eventos .antlog (hitos de hash cada 1024 ticks); con
    /// <paramref name="savePath"/> escribe un checkpoint .antsave en
    /// <paramref name="saveTick"/> (0 = al final). Persistencia de Fase 4:
    /// grabar/partir y luego verificar con --mode verify.
    /// </summary>
    public static string Run(ulong seed, int ticks, int colonies = 2, int grid = 256,
        string? antlogPath = null, string? savePath = null, int saveTick = 0)
    {
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));

        var sb = new StringBuilder();
        sb.Append("seed ").Append(seed)
          .Append(" ticks ").Append(ticks)
          .Append(" colonies ").Append(colonies)
          .Append(" grid ").Append(grid)
          .AppendLine();

        var sim = new WorldSim(seed, grid, colonies);
        long totalEvents = 0;
        var relay = new RelayTracker(); // salud del relevo: 1ª descarga + sueltas
        using AntEventLog? antlog = antlogPath != null ? new AntEventLog(sim) : null;

        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            totalEvents += sim.LastEvents.Count;
            relay.Observe(sim.LastEvents, sim);
            antlog?.Observe(sim.LastEvents);

            if (sim.Tick > 0 && sim.Tick % MilestoneEvery == 0)
                sb.Append("tick ").Append(sim.Tick).Append("  ").Append(sim.HashLine()).AppendLine();

            if (sim.Tick > 0 && sim.Tick % AntEventLog.MilestoneEvery == 0)
                antlog?.RecordMilestone(sim.Tick, sim.HashLine());

            if (savePath != null && saveTick > 0 && sim.Tick == (ulong)saveTick)
            {
                WorldSimSave.Save(sim, savePath);
                sb.Append("saved ").Append(savePath).Append(" at tick ").Append(sim.Tick).AppendLine();
            }
        }

        if (savePath != null && saveTick <= 0)
        {
            WorldSimSave.Save(sim, savePath);
            sb.Append("saved ").Append(savePath).Append(" at tick ").Append(sim.Tick).AppendLine();
        }

        if (antlog != null)
        {
            antlog.WriteTo(antlogPath!);
            sb.Append("antlog ").Append(antlogPath).AppendLine();
        }

        sb.Append("final-hash ").Append(sim.HashLine()).AppendLine();
        sb.Append("totals adults ").Append(TotalAdults(sim))
          .Append(" items ").Append(sim.Items.Count)
          .Append(" eggs ").Append(TotalBrood(sim, BroodKind.Egg))
          .Append(" larvae ").Append(TotalBrood(sim, BroodKind.Larva))
          .Append(" pupae ").Append(TotalBrood(sim, BroodKind.Pupa))
          .Append(" events ").Append(totalEvents)
          .Append(" first-unload ").Append(relay.HasUnload ? relay.FirstUnloadTick.ToString() : "-")
          .Append(" drop-avg ").Append(relay.DropDistanceMean is double dm
              ? dm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-")
          .AppendLine();
        return sb.ToString();
    }

    private static int TotalAdults(WorldSim sim)
    {
        int n = 0;
        for (int c = 0; c < sim.Colonies.Count; c++)
            n += sim.Colonies[c].AdultCountAlive;
        return n;
    }

    private static int TotalBrood(WorldSim sim, BroodKind kind)
    {
        int n = 0;
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var col = sim.Colonies[c];
            n += kind switch
            {
                BroodKind.Egg => col.Eggs.Count,
                BroodKind.Larva => col.Larvae.Count,
                _ => col.Pupae.Count
            };
        }
        return n;
    }
}