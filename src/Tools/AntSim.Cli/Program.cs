using System;
using System.Globalization;
using AntSim.Core.Scenario;
using AntSim.Core.World;

namespace AntSim.Cli;

/// <summary>
/// antsim — herramienta headless (predecesora del modo análisis/verificación).
///
/// Uso:
///   antsim [--mode micro|world|evolve] [--seed N] [--ticks N] [--grid N]
///          [--colonies N] [--import archivo.antgenome] [--export archivo.antgenome]
///
/// Modos:
///   micro  — microcosmos de cimientos (RNG + feromonas + MLP + validación).
///   world  — mundo completo de Fase 1 (hormigas, comida, nido, ColonyController).
///   evolve — mundo con neuroevolución (Fase 2): pool élite, fitness al morir,
///            inmigración con cuarentena; con --import encola genomas externos y
///            con --export escribe la élite final.
///
/// Todos emiten hashes de hito por tick: misma semilla ⇒ salida idéntica.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = "micro";
        ulong seed = 12345UL;
        int ticks = 1200;
        int grid = 96;
        int colonies = 2;
        string? importPath = null;
        string? exportPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                case "--mode":
                    mode = Next(args, ref i);
                    if (mode != "micro" && mode != "world" && mode != "evolve")
                        return Fail("--mode debe ser 'micro', 'world' o 'evolve'.");
                    break;
                case "--seed":
                    if (!ulong.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                        return Fail("--seed requiere un entero sin signo.");
                    break;
                case "--ticks":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || ticks < 1)
                        return Fail("--ticks requiere un entero positivo.");
                    break;
                case "--grid":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out grid) || grid < 8)
                        return Fail("--grid requiere un entero ≥ 8.");
                    break;
                case "--colonies":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out colonies) || colonies < 1)
                        return Fail("--colonies requiere un entero ≥ 1.");
                    break;
                case "--import":
                    importPath = Next(args, ref i);
                    break;
                case "--export":
                    exportPath = Next(args, ref i);
                    break;
                default:
                    return Fail($"Argumento desconocido: {args[i]}");
            }
        }

        try
        {
            string output = mode switch
            {
                "world" => WorldScenario.Run(seed, ticks, colonies, grid),
                "evolve" => RunEvolve(seed, ticks, colonies, grid, importPath, exportPath),
                _ => Microcosm.Run(seed, ticks, grid)
            };
            Console.Out.Write(output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static string RunEvolve(ulong seed, int ticks, int colonies, int grid,
        string? importPath, string? exportPath)
    {
        var sim = new WorldSim(seed, grid, colonies);

        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" ticks ").Append(ticks)
          .Append(" colonies ").Append(colonies).Append(" grid ").Append(grid).AppendLine();

        int imported = 0;
        if (importPath != null)
        {
            sim.ImportGenomesFromFile(0, importPath);
            imported = sim.Colonies[0].Pool.PendingImmigrants;
            sb.Append("imported ").Append(imported).Append(" from ").Append(importPath).AppendLine();
        }

        long totalEvents = 0;
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            totalEvents += sim.LastEvents.Count;

            if (sim.Tick > 0 && sim.Tick % 120 == 0)
            {
                sb.Append("tick ").Append(sim.Tick).Append("  ").Append(sim.HashLine()).AppendLine();
                sb.Append(PoolStatsLine(sim));
            }
        }

        sb.Append("final-hash ").Append(sim.HashLine()).AppendLine();
        sb.Append(PoolStatsLine(sim));
        sb.Append("totals adults ").Append(TotalAdults(sim))
          .Append(" items ").Append(sim.Items.Count)
          .Append(" events ").Append(totalEvents).AppendLine();

        if (exportPath != null)
        {
            var colony = sim.Colonies[0];
            sim.ExportEliteToFile(0, exportPath, "evolve-export", colony.Species.Name);
            sb.Append("exported ").Append(colony.Pool.EliteCount)
              .Append(" genomes to ").Append(exportPath).AppendLine();
        }

        return sb.ToString();
    }

    private static string PoolStatsLine(WorldSim sim)
    {
        var sb = new System.Text.StringBuilder();
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var pool = sim.Colonies[c].Pool;
            sb.Append("pool colony=").Append(c)
              .Append(" elite=").Append(pool.EliteCount)
              .Append(" best=").Append(pool.BestFitness.ToString("0.0000", CultureInfo.InvariantCulture))
              .Append(" avg=").Append(pool.AvgFitness.ToString("0.0000", CultureInfo.InvariantCulture))
              .Append(" div=").Append(pool.Diversity().ToString("0.0000", CultureInfo.InvariantCulture))
              .Append(" imm=").Append(pool.PendingImmigrants)
              .Append(" entered=").Append(pool.TrialsEntered)
              .Append(" discarded=").Append(pool.TrialsDiscarded)
              .AppendLine();
        }
        return sb.ToString();
    }

    private static int TotalAdults(WorldSim sim)
    {
        int n = 0;
        for (int c = 0; c < sim.Colonies.Count; c++)
            n += sim.Colonies[c].AdultCountAlive;
        return n;
    }

    private static string Next(string[] args, ref int i)
    {
        i++;
        if (i >= args.Length) throw new ArgumentException("Falta el valor del argumento.");
        return args[i];
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Out.WriteLine("Uso: antsim [--mode micro|world|evolve] [--seed N] [--ticks N] [--grid N] [--colonies N] [--import f] [--export f]");
    }
}