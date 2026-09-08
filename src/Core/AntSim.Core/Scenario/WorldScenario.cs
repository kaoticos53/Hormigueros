using System;
using System.Text;
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

    public static string Run(ulong seed, int ticks, int colonies = 2, int grid = 256)
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

        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            totalEvents += sim.LastEvents.Count;

            if (sim.Tick > 0 && sim.Tick % MilestoneEvery == 0)
                sb.Append("tick ").Append(sim.Tick).Append("  ").Append(sim.HashLine()).AppendLine();
        }

        sb.Append("final-hash ").Append(sim.HashLine()).AppendLine();
        sb.Append("totals adults ").Append(TotalAdults(sim))
          .Append(" items ").Append(sim.Items.Count)
          .Append(" eggs ").Append(TotalBrood(sim, BroodKind.Egg))
          .Append(" larvae ").Append(TotalBrood(sim, BroodKind.Larva))
          .Append(" pupae ").Append(TotalBrood(sim, BroodKind.Pupa))
          .Append(" events ").Append(totalEvents)
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