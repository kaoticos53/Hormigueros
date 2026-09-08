using System;
using System.Globalization;
using AntSim.Core.Evolution;
using AntSim.Core.Scenario;
using AntSim.Core.Training;
using AntSim.Core.World;

namespace AntSim.Cli;

/// <summary>
/// antsim — herramienta headless (predecesora del modo análisis/verificación).
///
/// Uso:
///   antsim [--mode micro|world|evolve|pretrain] [--seed N] [--ticks N] [--grid N]
///          [--colonies N] [--import archivo.antgenome] [--export archivo.antgenome]
///          [--pop N] [--generations N]
///
/// Modos:
///   micro    — microcosmos de cimientos (RNG + feromonas + MLP + validación).
///   world    — mundo completo de Fase 1 (hormigas, comida, nido, ColonyController).
///   evolve   — mundo con neuroevolución (Fase 2): pool élite, fitness al morir,
///              inmigración con cuarentena; con --import encola genomas externos y
///              con --export escribe la élite final.
///   pretrain — pre-entrenamiento headless (Fase 3): currículo por etapas sobre la
///              arena de WorldSim hasta alcanzar competencia mínima (ida-vuelta con
///              comida); con --export escribe la población entrenada.
///
/// Todos emiten hashes de hito por tick (o reportes deterministas): misma semilla ⇒
/// salida idéntica.
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
        int pop = 16;
        int generations = 0; // 0 = usa el tope de cada etapa (30/60/80/100)
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
                    if (mode != "micro" && mode != "world" && mode != "evolve" && mode != "pretrain")
                        return Fail("--mode debe ser 'micro', 'world', 'evolve' o 'pretrain'.");
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
                case "--pop":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out pop) || pop < 2)
                        return Fail("--pop requiere un entero ≥ 2.");
                    break;
                case "--generations":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out generations) || generations < 1)
                        return Fail("--generations requiere un entero ≥ 1.");
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
                "pretrain" => RunPretrain(seed, pop, generations, exportPath),
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

    private static string RunPretrain(ulong seed, int pop, int generations, string? exportPath)
    {
        // Currículo calibrado (Fase 3); --generations limita el máximo por etapa.
        var stages = new System.Collections.Generic.List<CurriculumStage>();
        foreach (var s in CurriculumTrainer.DefaultStages())
        {
            stages.Add(new CurriculumStage
            {
                Name = s.Name,
                FoodDistance = s.FoodDistance,
                TickBudget = s.TickBudget,
                CompetenceFitness = s.CompetenceFitness,
                MinGenerations = s.MinGenerations,
                MaxGenerations = generations > 0 ? Math.Min(s.MaxGenerations, generations) : s.MaxGenerations
            });
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" pop ").Append(pop).AppendLine();

        var trainer = new CurriculumTrainer(seed, pop, stages, stats =>
        {
            sb.Append("gen stage=").Append(stats.Stage)
              .Append(" gen=").Append(stats.Generation)
              .Append(" best=").Append(stats.BestFitness.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" mean=").Append(stats.MeanFitness.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" competent=").Append(stats.CompetentCount)
              .AppendLine();
        });

        var trained = trainer.Run();
        sb.Append("trained ").Append(trained.Count).Append(" genomes, stages ").Append(trainer.Report[trainer.Report.Count - 1].Stage).AppendLine();

        if (exportPath != null)
        {
            AntGenomeFile.WriteFile(exportPath, "pretrain", SpeciesDescriptor.LasiusNiger.Name,
                seed, 0, trained, AntSim.Core.Brain.BrainContract.CurrentVersion);
            sb.Append("exported ").Append(trained.Count).Append(" genomes to ").Append(exportPath).AppendLine();
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
        Console.Out.WriteLine("Uso: antsim [--mode micro|world|evolve|pretrain] [--seed N] [--ticks N] [--grid N] [--colonies N] [--import f] [--export f] [--pop N] [--generations N]");
    }
}