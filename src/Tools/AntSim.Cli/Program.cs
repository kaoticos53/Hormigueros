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
///          [--colonies N] [--import archivo.antgenome] [--seed-pool archivo.antgenome]
///          [--export archivo.antgenome] [--pop N] [--generations N] [--warm-start f]
///
/// Modos:
///   micro    — microcosmos de cimientos (RNG + feromonas + MLP + validación).
///   world    — mundo completo de Fase 1 (hormigas, comida, nido, ColonyController).
///   evolve   — mundo con neuroevolución (Fase 2): pool élite, fitness al morir,
///              inmigración con cuarentena; con --import encola genomas externos y
///              con --export escribe la élite final.
///   pretrain — pre-entrenamiento headless (Fase 3): currículo por etapas sobre la
///              arena de WorldSim hasta alcanzar competencia mínima (ida-vuelta con
///              comida); con --export escribe la población entrenada y con
///              --warm-start SIEMBRA la población inicial desde un .antgenome
///              existente (continuar un pre-entrenamiento, Fase 3ter) en lugar de
///              genomas aleatorios.
///
/// --import encola genomas como inmigrantes en cuarentena (se usan en eclosiones
/// futuras); --seed-pool SIEMBRA la élite con un .antgenome pre-entrenado (Fase 3):
/// los nacimientos —incluidas las fundadoras del tick 0— usan esa población.
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
        float bandMin = 200f;   // banda de distancia del pretrain (por defecto: la calibrada)
        float bandMax = 260f;
        string? importPath = null;
        string? seedPoolPath = null;
        string? warmStartPath = null;
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
                case "--seed-pool":
                    seedPoolPath = Next(args, ref i);
                    break;
                case "--warm-start":
                    warmStartPath = Next(args, ref i);
                    break;
                case "--band-min":
                    if (!float.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out bandMin) || bandMin < WorldSim.NestMinSpawnDistance)
                        return Fail($"--band-min requiere un float ≥ {WorldSim.NestMinSpawnDistance}.");
                    break;
                case "--band-max":
                    if (!float.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out bandMax) || bandMax <= bandMin)
                        return Fail("--band-max requiere un float > --band-min.");
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
                "evolve" => RunEvolve(seed, ticks, colonies, grid, importPath, seedPoolPath, exportPath),
                "pretrain" => RunPretrain(seed, pop, generations, exportPath, warmStartPath, bandMin, bandMax),
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
        string? importPath, string? seedPoolPath, string? exportPath)
    {
        var sim = new WorldSim(seed, grid, colonies);

        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" ticks ").Append(ticks)
          .Append(" colonies ").Append(colonies).Append(" grid ").Append(grid).AppendLine();

        if (seedPoolPath != null)
        {
            // Siembra la élite (Fase 3): los nacimientos —incluidas las fundadoras
            // del tick 0— salen de la población pre-entrenada.
            var (_, seeded) = AntGenomeFile.ReadFile(seedPoolPath, AntSim.Core.Brain.BrainContract.CurrentVersion);
            sim.SeedPoolFromGenomes(0, seeded);
            sb.Append("seeded ").Append(seeded.Count).Append(" genomes from ").Append(seedPoolPath).AppendLine();
        }

        int imported = 0;
        if (importPath != null)
        {
            sim.ImportGenomesFromFile(0, importPath);
            imported = sim.Colonies[0].Pool.PendingImmigrants;
            sb.Append("imported ").Append(imported).Append(" from ").Append(importPath).AppendLine();
        }

        long totalEvents = 0;
        var eventCounts = new int[12]; // SimEventKind
        var relay = new RelayTracker(); // salud del relevo: 1ª descarga + sueltas
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            totalEvents += sim.LastEvents.Count;
            relay.Observe(sim.LastEvents, sim);
            foreach (var ev in sim.LastEvents)
                eventCounts[(int)ev.Kind]++;

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
          .Append(" events ").Append(totalEvents)
          .Append(" pickup ").Append(eventCounts[(int)SimEventKind.Pickup])
          .Append(" unload ").Append(eventCounts[(int)SimEventKind.Unload])
          .Append(" eclosed ").Append(eventCounts[(int)SimEventKind.Eclosed])
          .Append(" died ").Append(eventCounts[(int)SimEventKind.AntDied])
          .Append(" eggs ").Append(eventCounts[(int)SimEventKind.EggLaid])
          .Append(" first-unload ").Append(relay.HasUnload ? relay.FirstUnloadTick.ToString() : "-")
          .Append(" drop-avg ").Append(relay.DropDistanceMean is double dm
              ? dm.ToString("0.0", CultureInfo.InvariantCulture) : "-")
          .AppendLine();

        if (exportPath != null)
        {
            var colony = sim.Colonies[0];
            sim.ExportEliteToFile(0, exportPath, "evolve-export", colony.Species.Name);
            sb.Append("exported ").Append(colony.Pool.EliteCount)
              .Append(" genomes to ").Append(exportPath).AppendLine();
        }

        return sb.ToString();
    }

    private static string RunPretrain(ulong seed, int pop, int generations, string? exportPath,
        string? warmStartPath, float bandMin, float bandMax)
    {
        // Currículo calibrado (Fase 3bis, arena realista); --generations limita el
        // máximo por etapa y --band-min/--band-max re-bandan TODAS las etapas
        // (warm-start de refinado: extender el anillo de forrajeo sobre un pool ya
        // competente).
        var stages = new System.Collections.Generic.List<CurriculumStage>();
        foreach (var s in CurriculumTrainer.DefaultStages())
        {
            stages.Add(new CurriculumStage
            {
                Name = s.Name,
                MinDistance = bandMin,
                MaxDistance = bandMax,
                TickBudget = s.TickBudget,
                CompetenceFitness = s.CompetenceFitness,
                MinGenerations = s.MinGenerations,
                MaxGenerations = generations > 0 ? Math.Min(s.MaxGenerations, generations) : s.MaxGenerations
            });
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" pop ").Append(pop)
          .Append(" band ").Append(bandMin.ToString("0", CultureInfo.InvariantCulture))
          .Append("-").Append(bandMax.ToString("0", CultureInfo.InvariantCulture))
          .AppendLine();

        System.Collections.Generic.List<MlpGenome>? seeded = null;
        if (warmStartPath != null)
        {
            var (_, genomes) = AntGenomeFile.ReadFile(warmStartPath, AntSim.Core.Brain.BrainContract.CurrentVersion);
            seeded = new System.Collections.Generic.List<MlpGenome>(genomes);
            sb.Append("warm-started ").Append(seeded.Count).Append(" genomes from ").Append(warmStartPath).AppendLine();
        }

        var trainer = new CurriculumTrainer(seed, pop, stages, stats =>
        {
            sb.Append("gen stage=").Append(stats.Stage)
              .Append(" gen=").Append(stats.Generation)
              .Append(" best=").Append(stats.BestFitness.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" mean=").Append(stats.MeanFitness.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" competent=").Append(stats.CompetentCount)
              .AppendLine();
        }, seedGenomes: seeded);

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
              .Append(" inflow=").Append(sim.Colonies[c].InflowEma.ToString("0.000", CultureInfo.InvariantCulture))
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
        Console.Out.WriteLine("Uso: antsim [--mode micro|world|evolve|pretrain] [--seed N] [--ticks N] [--grid N] [--colonies N] [--import f] [--seed-pool f] [--warm-start f] [--export f] [--pop N] [--generations N] [--band-min F] [--band-max F]");
    }
}