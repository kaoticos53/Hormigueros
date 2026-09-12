using System;
using System.Collections.Generic;
using System.Globalization;
using AntSim.Core.Evolution;
using AntSim.Core.Serialization;
using AntSim.Core.Scenario;
using AntSim.Core.Training;
using AntSim.Core.World;

namespace AntSim.Cli;

/// <summary>
/// antsim — herramienta headless (predecesora del modo análisis/verificación).
///
/// Uso:
///   antsim [--mode micro|world|evolve|pretrain|verify] [--seed N] [--ticks N] [--grid N]
///          [--colonies N] [--import archivo.antgenome] [--seed-pool archivo.antgenome]
///          [--export archivo.antgenome] [--pop N] [--generations N] [--warm-start f]
///          [--save f] [--save-tick N] [--antlog f] [--load f]
///
/// Modos:
///   verify   — Fase 4 (persistencia): carga un checkpoint .antsave (--load) y
///              re-ejecuta --ticks pasos; con --antlog contrasta cada hash de
///              hito (cada 1024 ticks) y cada evento contra el log de
///              referencia — cualquier divergencia aborta con exit 3. Es la
///              verificación "se guarda la causa, no los efectos": reproducir
///              desde un guardado regenera el mundo bit a bit.
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
        bool hybrid = false;    // currículo híbrido alternado (200-mid / 200-max por generación)
        bool fullWorld = false; // añade la 4ª etapa mundo-completo (arena 160, 200–700 u)
        string? importPath = null;
        string? seedPoolPath = null;
        string? warmStartPath = null;
        string? exportPath = null;
        string? savePath = null;      // Fase 4: checkpoint .antsave
        int saveTick = 0;             // 0 = guardar al final de la ejecución
        string? antlogPath = null;    // Fase 4: registro de eventos .antlog
        string? loadPath = null;      // Fase 4: cargar checkpoint (modo verify)
        int frameEvery = 1;           // F4.1: canal A cada N ticks en modo game
        var drops = new List<(int Tick, float X, float Y)>(); // F4.1: comandos DropFood inyectados
        int pheroEvery = 0; // F4.5: canal E opt-in (feromonas en el stream)
        uint inspectId = 0; // F5.0: hormiga inspeccionada (canal F de activaciones)
        int activEvery = 0; // F5.0: canal F opt-in (activaciones del MLP en el stream)
        bool presetsJson = false;   // --mode presets: salida JSON estructurada

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
                if (mode != "micro" && mode != "world" && mode != "evolve" && mode != "pretrain" && mode != "verify" && mode != "game" && mode != "presets" && mode != "genome-info")
                    return Fail("--mode debe ser 'micro', 'world', 'evolve', 'pretrain', 'verify', 'game', 'presets' o 'genome-info'.");
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
                case "--hybrid":
                    hybrid = true;
                    break;
                case "--full-world":
                    fullWorld = true;
                    break;
                case "--export":
                    exportPath = Next(args, ref i);
                    break;
                case "--save":
                    savePath = Next(args, ref i);
                    break;
                case "--save-tick":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out saveTick) || saveTick < 0)
                        return Fail("--save-tick requiere un entero ≥ 0 (0 = guardar al final).");
                    break;
                case "--antlog":
                    antlogPath = Next(args, ref i);
                    break;
                case "--load":
                    loadPath = Next(args, ref i);
                    break;
                case "--frame-every":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out frameEvery) || frameEvery < 1)
                        return Fail("--frame-every requiere un entero ≥ 1.");
                    break;
                case "--json":
                    presetsJson = true;
                    break;
                case "--phero-every":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out pheroEvery) || pheroEvery < 0)
                        return Fail("--phero-every requiere un entero ≥ 0 (0 = desactivado).");
                    break;
                case "--inspect":
                    if (!uint.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out inspectId) || inspectId == 0)
                        return Fail("--inspect requiere un id de hormiga > 0.");
                    break;
                case "--activ-every":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out activEvery) || activEvery < 0)
                        return Fail("--activ-every requiere un entero ≥ 0 (0 = desactivado).");
                    break;
                case "--drop":
                {
                    // Formato tick:x:y — inyecta un comando DropFood del jugador (F4.0).
                    string spec = Next(args, ref i);
                    var parts = spec.Split(':');
                    if (parts.Length != 3
                        || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int dropTick)
                        || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx)
                        || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy))
                        return Fail("--drop requiere tick:x:y (p. ej. --drop 300:350.5:400.25).");
                    drops.Add((dropTick, dx, dy));
                    break;
                }
                default:
                    return Fail($"Argumento desconocido: {args[i]}");
            }
        }

        try
        {
            // El modo verify escribe su propio reporte y devuelve su propio exit
            // code (0 = reproducción idéntica, 3 = divergencia).
            if (mode == "verify")
                return RunVerify(ticks, loadPath, antlogPath);

            string output = mode switch
            {
                "world" => WorldScenario.Run(seed, ticks, colonies, grid, antlogPath, savePath, saveTick),
                "evolve" => RunEvolve(seed, ticks, colonies, grid, importPath, seedPoolPath, exportPath),
                "game" => GameScenario.Run(seed, ticks, colonies, grid, frameEvery, seedPoolPath, drops,
                    pheroEvery: pheroEvery, inspectId: inspectId, activEvery: activEvery),
                "presets" => PresetScenario.RenderCards(json: presetsJson),
                "genome-info" => importPath == null
                    ? throw new ArgumentException("--mode genome-info requiere --import archivo.antgenome")
                    : GenomeImportInfo.Inspect(importPath,
                        AntSim.Core.Brain.BrainContract.CurrentVersion).ToJson() + "\n",
                "pretrain" => RunPretrain(seed, pop, generations, exportPath, warmStartPath, bandMin, bandMax, hybrid, fullWorld),
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
                TickLine.Append(sb, sim, relay, includePoolStats: true, poolStatsLine: PoolStatsLine(sim));
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
          .Append(" unload-avg ").Append(relay.UnloadDistanceMean is double um
              ? um.ToString("0.0", CultureInfo.InvariantCulture) : "-")
          .Append(" carry-leg ").Append(relay.CarryLegMean is double cl
              ? cl.ToString("0.0", CultureInfo.InvariantCulture) : "-")
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
        string? warmStartPath, float bandMin, float bandMax, bool hybrid = false, bool fullWorld = false)
    {
        // Currículo calibrado (Fase 3bis, arena realista); --generations limita el
        // máximo por etapa y --band-min/--band-max re-bandan TODAS las etapas
        // (warm-start de refinado: extender el anillo de forrajeo sobre un pool ya
        // competente). --hybrid alterna generaciones entre la banda media
        // (bandMin–punto medio) y la ancha (bandMin–bandMax) dentro de cada etapa.
        // --full-world AÑADE una 4ª etapa (mundo-completo): arena grande de 160
        // celdas, banda 200–700 u, horizonte 14 400 ticks — cubre el mundo real
        // del juego (256²) con relevo multi-salto obligatorio.
        System.Collections.Generic.IReadOnlyList<CurriculumStage> baseStages = hybrid
            ? CurriculumTrainer.HybridStages(bandMin, (bandMin + bandMax) * 0.5f, bandMax)
            : CurriculumTrainer.DefaultStages();

        var stages = new System.Collections.Generic.List<CurriculumStage>();
        foreach (var s in baseStages)
        {
            stages.Add(new CurriculumStage
            {
                Name = s.Name,
                MinDistance = s.MinDistance,
                MaxDistance = s.MaxDistance,
                MidDistance = s.MidDistance,
                ArenaCells = s.ArenaCells,
                TickBudget = s.TickBudget,
                CompetenceFitness = s.CompetenceFitness,
                MinGenerations = s.MinGenerations,
                MaxGenerations = generations > 0 ? Math.Min(s.MaxGenerations, generations) : s.MaxGenerations
            });
        }

        if (fullWorld)
        {
            float fwMax = MathF.Max(bandMax, 700f);
            foreach (var s in CurriculumTrainer.FullWorldStage(bandMin, fwMax))
            {
                stages.Add(new CurriculumStage
                {
                    Name = s.Name,
                    MinDistance = s.MinDistance,
                    MaxDistance = s.MaxDistance,
                    ArenaCells = s.ArenaCells,
                    TickBudget = s.TickBudget,
                    CompetenceFitness = s.CompetenceFitness,
                    MinGenerations = s.MinGenerations,
                    MaxGenerations = generations > 0 ? Math.Min(s.MaxGenerations, generations) : s.MaxGenerations
                });
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" pop ").Append(pop)
          .Append(" band ").Append(bandMin.ToString("0", CultureInfo.InvariantCulture))
          .Append("-").Append(bandMax.ToString("0", CultureInfo.InvariantCulture))
          .Append(hybrid ? " hybrid" : "")
          .Append(fullWorld ? " full-world" : "")
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

    /// <summary>
    /// Modo verify (Fase 4): reproduce desde un checkpoint y contrasta con el
    /// log de referencia. SIN --antlog: solo re-ejecuta y emite los hashes
    /// (misma semilla ⇒ idénticos, la salida es comparable a ojo o por script).
    /// CON --antlog: compara cada hash de hito y cada evento del intervalo;
    /// cualquier divergencia aborta con código 3.
    /// </summary>
    private static int RunVerify(int ticks, string? loadPath, string? antlogPath)
    {
        if (loadPath is null)
            return Fail("--mode verify requiere --load checkpoint.antsave.");

        var sim = WorldSimSave.Load(loadPath);
        var sb = new System.Text.StringBuilder();
        sb.Append("verify ").Append(loadPath)
          .Append(" tick ").Append(sim.Tick)
          .Append(" colonies ").Append(sim.Colonies.Count)
          .Append(" items ").Append(sim.Items.Count).AppendLine();

        var reference = antlogPath != null ? AntEventLogFile.Read(antlogPath) : (AntEventLogFile.LogData?)null;
        if (reference != null)
        {
            sb.Append("antlog ").Append(antlogPath)
              .Append(" seed ").Append(reference.Value.Seed)
              .Append(" events ").Append(reference.Value.Events.Count)
              .Append(" milestones ").Append(reference.Value.Milestones.Count).AppendLine();
            if (reference.Value.Seed != sim.Seed)
            {
                Console.Error.WriteLine("Error: la semilla del checkpoint no coincide con la del log.");
                return 3;
            }
        }

        int refIdx = 0;                 // cursor sobre los eventos de referencia
        int nextMilestone = 0;          // cursor sobre los hitos de referencia
        int comparedEvents = 0;
        int comparedMilestones = 0;

        if (reference != null)
        {
            // El log de referencia arranca en el tick 1 de la partida original;
            // la reproducción arranca en el tick del checkpoint. Los eventos
            // previos NO deben reproducirse (ya ocurrieron antes del guardado):
            // se descartan, igual que los hitos anteriores al punto de carga.
            ulong fromTick = sim.Tick;
            while (refIdx < reference.Value.Events.Count &&
                   reference.Value.Events[refIdx].Tick <= fromTick)
                refIdx++;
            while (nextMilestone < reference.Value.Milestones.Count &&
                   reference.Value.Milestones[nextMilestone].Tick <= fromTick)
                nextMilestone++;
            int skipped = refIdx;
            if (skipped > 0)
                sb.Append("(descartados ").Append(skipped)
                  .Append(" eventos previos al checkpoint y sus hitos)").AppendLine();
        }

        for (int i = 0; i < ticks; i++)
        {
            sim.Step();

            if (reference != null)
            {
                // Eventos del intervalo: deben coincidir uno a uno (tick, kind,
                // colonia, hormiga, posición y causa).
                var evs = sim.LastEvents;
                for (int e = 0; e < evs.Count; e++)
                {
                    var ev = evs[e];
                    if (refIdx >= reference.Value.Events.Count)
                    {
                        Console.Error.WriteLine($"Error: evento inesperado en tick {ev.Tick} ({ev.Kind}) — el log de referencia terminó antes.");
                        return 3;
                    }
                    var exp = reference.Value.Events[refIdx++];
                    if (exp.Tick != ev.Tick || exp.Kind != ev.Kind || exp.ColonyId != ev.ColonyId ||
                        exp.AntId != ev.AntId || exp.X != ev.X || exp.Y != ev.Y || exp.Cause != ev.Cause)
                    {
                        Console.Error.WriteLine($"Error: divergencia en el evento {refIdx - 1} del tick {ev.Tick}: " +
                            $"esperado ({exp.Tick}, {exp.Kind}, col {exp.ColonyId}, ant {exp.AntId}, {exp.X:R}, {exp.Y:R}, causa {exp.Cause}) — " +
                            $"obtenido ({ev.Tick}, {ev.Kind}, col {ev.ColonyId}, ant {ev.AntId}, {ev.X:R}, {ev.Y:R}, causa {ev.Cause}).");
                        return 3;
                    }
                    comparedEvents++;
                }

                // Hito de hash: si este tick era uno de los del log, el hash debe coincidir.
                if (nextMilestone < reference.Value.Milestones.Count &&
                    sim.Tick == reference.Value.Milestones[nextMilestone].Tick)
                {
                    string expected = reference.Value.Milestones[nextMilestone].HashHex;
                    string actual = sim.HashLine();
                    if (expected != actual)
                    {
                        Console.Error.WriteLine($"Error: hash de hito divergente en el tick {sim.Tick}.");
                        Console.Error.WriteLine($"  esperado: {expected}");
                        Console.Error.WriteLine($"  obtenido: {actual}");
                        return 3;
                    }
                    comparedMilestones++;
                    nextMilestone++;
                }
            }
            else if (sim.Tick > 0 && sim.Tick % AntEventLog.MilestoneEvery == 0)
            {
                sb.Append("tick ").Append(sim.Tick).Append("  ").Append(sim.HashLine()).AppendLine();
            }
        }

        sb.Append("final-hash ").Append(sim.HashLine()).AppendLine();
        if (reference != null)
        {
            if (refIdx != reference.Value.Events.Count)
            {
                Console.Error.WriteLine($"Error: el log de referencia tiene {reference.Value.Events.Count - refIdx} eventos que la reproducción no emitió.");
                return 3;
            }
            if (nextMilestone != reference.Value.Milestones.Count)
            {
                Console.Error.WriteLine($"Error: la reproducción no alcanzó {reference.Value.Milestones.Count - nextMilestone} hitos del log.");
                return 3;
            }
            sb.Append("compared events ").Append(comparedEvents)
              .Append(" milestones ").Append(comparedMilestones)
              .AppendLine(" — reproducción bit a bit idéntica ✓");
        }
        else
        {
            sb.AppendLine("reproducción completada (sin log de contraste: compara los hashes manualmente o con --antlog).");
        }
        Console.Out.Write(sb.ToString());
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Out.WriteLine("Uso: antsim [--mode micro|world|evolve|pretrain|verify|game|presets|genome-info] [--seed N] [--ticks N] [--grid N] [--colonies N] [--import f] [--seed-pool f] [--warm-start f] [--export f] [--pop N] [--generations N] [--band-min F] [--band-max F] [--save f] [--save-tick N] [--antlog f] [--load f] [--frame-every N] [--drop tick:x:y] [--phero-every N] [--inspect N] [--activ-every N] [--json]");
    }
}