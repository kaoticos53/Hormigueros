using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Core.Brain;
using AntSim.Core.Evolution;
using AntSim.Core.Pheromone;
using AntSim.Core.Sim;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// F5.3 rodaja 3 — escenario del medidor de ESCALA (`--mode scale`).
///
/// El criterio de salida de la sub-fase es «N colonias estables a 60 fps en la
/// escena de juego». Ninguna tabla de esa forma puede salir del editor (Unity no
/// corre headless aquí), así que el medidor la descompone en las tres cifras que
/// SÍ son del Core y verificables:
///
///   1. **coste del tick** (ms/tick, medido con el mundo real) y qué fracción del
///      presupuesto de un frame a 60 fps (16,6 ms) consume;
///   2. **trabajo de feromonas por tick** (celdas visitadas con el LOD frente al
///      grid completo) — la parte del tick que la rodaja 3 vino a recortar;
/// Las llamadas de dibujo NO se miden aquí: la vista no viaja al Core (consume el
/// stream). Ese número sale del modelo puro del esqueleto Unity
/// (<c>InstancedDrawPlan</c>, compilado en esta misma suite) y del contador en
/// vivo del presenter (<c>LastDrawCalls</c>).
///
/// Es determinista en su parte de mundo (mismas semillas ⇒ mismo estado medido);
/// los tiempos, obviamente, no: el reporte lo dice.
/// </summary>
public static class ScaleScenario
{
    /// <summary>Colonias medidas por defecto: 1, 2, 4 y 8.</summary>
    public static readonly int[] DefaultColonyCounts = { 1, 2, 4, 8 };

    /// <summary>Ticks de calentamiento (JIT + primer llenado de cachés) que no se cronometran.</summary>
    public const int WarmupTicks = 200;

    /// <summary>Presupuesto de un frame a 60 fps, en ms.</summary>
    public const double FrameBudget60Ms = 1000.0 / 60.0;

    public static string Run(int grid, int ticks, IReadOnlyList<int>? colonyCounts, ulong seed,
        string? seedPoolPath = null)
    {
        var counts = colonyCounts is { Count: > 0 } ? colonyCounts : DefaultColonyCounts;
        var pool = LoadPool(seedPoolPath);

        var sb = new StringBuilder();
        sb.Append("escala: semilla ").Append(seed)
          .Append(" · grid ").Append(grid).Append('²')
          .Append(" · ").Append(ticks).Append(" ticks cronometrados (+")
          .Append(WarmupTicks).Append(" de calentamiento)")
          .Append(" · pool '").Append(seedPoolPath ?? "(aleatorio)").Append('\'').AppendLine();
        sb.AppendLine();

        var filas = new List<Fila>();
        foreach (int colonies in counts)
        {
            if (colonies < 1) continue;
            filas.Add(Medir(seed, grid, colonies, ticks, pool));
        }

        sb.Append(RenderText(filas));
        sb.AppendLine();
        sb.AppendLine("## Tabla markdown");
        sb.AppendLine();
        sb.Append(RenderMarkdown(filas));
        sb.AppendLine();
        sb.AppendLine("Notas: los ms/tick son de ESTA máquina (una medida, no un récord); el estado");
        sb.AppendLine("medido (hormigas, celdas activas, llamadas) sí es determinista con la semilla.");
        return sb.ToString();
    }

    private readonly record struct Fila(
        int Colonies,
        int Ants,
        int Items,
        double MsPerTick,
        double FrameShare,
        long CellsTotal,
        long CellsVisited,
        long CellsSupport);

    // ── Barrido del bloque del LOD (F5.3 rodaja 3bis) ─────────────────────────

    /// <summary>Bloques de LOD que el barrido mide por defecto. El 16 era el
    /// valor histórico y el 64 el techo razonable del tile de render; el 4 es el
    /// extremo fino, donde la lista ya escanea 4 096 contadores por reconstrucción.</summary>
    public static readonly int[] DefaultLodBlockSizes = { 4, 8, 16, 32, 64 };

    /// <summary>Colonias del barrido del LOD por defecto: el peor caso del modo de
    /// juego y donde el trabajo de feromonas pesa más.</summary>
    public const int DefaultLodSweepColonies = 8;

    /// <summary>Reconstrucciones cronometradas en la micro-medida por bloque: el
    /// coste de la lista es un escaneo de 16 a 4 096 enteros, así que una sola
    /// reconstrucción queda por debajo de la resolución del reloj.</summary>
    public const int RebuildBenchmarkCalls = 20_000;

    /// <summary>Celdas de soporte del patrón de la micro-medida (determinista,
    /// disperso): lo que se mide es el escaneo de contadores, no cuántos quedan
    /// activos.</summary>
    public const int RebuildBenchmarkCells = 2_000;

    /// <summary>
    /// Barrido del bloque del LOD: corre el MISMO mundo (misma semilla, mismo
    /// pool, mismo calentamiento y mismos ticks) con cada lado de bloque y publica
    /// el equilibrio entre sus dos costes:
    ///
    ///   · **celdas visitadas por operación** — lo que el bloque fino AHORRA (un
    ///     bloque activo cuesta su área entera, aunque solo una celda tenga rastro);
    ///   · **reconstrucciones por tick y contadores escaneados** — lo que el bloque
    ///     fino PAGA (reconstruir la lista cuesta `(lado/bloque)²` comparaciones);
    ///   · **ns por reconstrucción** (micro-medida aislada, con
    ///     <c>ForceBlockListRebuild</c>) y µs/tick que eso supone.
    ///
    /// Cierra con la fila de referencia **sin LOD** (grid completo, que es la
    /// implementación con la que el test de equivalencia compara): 100 % de celdas
    /// y ningún coste de lista. El bloque elegido es el que minimiza celdas
    /// visitadas sin que la reconstrucción deje de ser ruido.
    /// </summary>
    public static string RunLodSweep(int grid, int ticks, IReadOnlyList<int>? blockSizes,
        int colonies, ulong seed, string? seedPoolPath = null)
    {
        var sizes = blockSizes is { Count: > 0 } ? blockSizes : DefaultLodBlockSizes;
        var pool = LoadPool(seedPoolPath);

        var sb = new StringBuilder();
        sb.Append("LOD de difusión — barrido del tamaño de bloque: semilla ").Append(seed)
          .Append(" · grid ").Append(grid).Append('²')
          .Append(" · ").Append(colonies).Append(" colonias")
          .Append(" · ").Append(ticks).Append(" ticks cronometrados (+")
          .Append(WarmupTicks).Append(" de calentamiento)")
          .Append(" · pool '").Append(seedPoolPath ?? "(aleatorio)").Append('\'').AppendLine();
        sb.AppendLine();

        var filas = new List<FilaLod>();
        foreach (int b in sizes)
        {
            if (b < 1) continue;
            filas.Add(MedirLod(seed, grid, colonies, ticks, pool, b));
        }
        filas.Add(MedirLod(seed, grid, colonies, ticks, pool, PheromoneLayer.DefaultLodBlockSize,
            lodEnabled: false));

        sb.Append(RenderLodText(filas));
        sb.AppendLine();
        sb.AppendLine("## Tabla markdown");
        sb.AppendLine();
        sb.Append(RenderLodMarkdown(filas));
        sb.AppendLine();
        sb.AppendLine("Notas: las celdas visitadas y los contadores son deterministas con la semilla;");
        sb.AppendLine("los ns y los ms/tick son de ESTA máquina. Los ns por reconstrucción son de una");
        sb.AppendLine("micro-medida aparte (patrón disperso, 20 000 llamadas), no del mundo corriendo.");
        return sb.ToString();
    }

    private readonly record struct FilaLod(
        int BlockSize,
        bool LodEnabled,
        long SlotsPerLayer,
        long CellsTotal,
        long CellsVisited,
        long CellsSupport,
        double RebuildsPerTick,
        double SlotsScannedPerTick,
        double NsPerRebuild,
        double RebuildShareOfTick,
        double MsPerTick,
        double FrameShare);

    private static FilaLod MedirLod(ulong seed, int grid, int colonies, int ticks, MlpGenome? pool,
        int blockSize, bool lodEnabled = true)
    {
        var sim = new WorldSim(seed, grid, colonies, lodBlockSize: blockSize);
        if (pool != null)
            for (int c = 0; c < colonies; c++) sim.SeedPoolFromGenomes(c, new[] { pool });

        if (!lodEnabled)
            foreach (var colony in sim.Colonies)
                foreach (var layer in Layers(colony)) layer.LodEnabled = false;

        for (int i = 0; i < WarmupTicks; i++) sim.Step();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) sim.Step();
        sw.Stop();

        double ms = sw.Elapsed.TotalMilliseconds / ticks;

        long cellsTotal = 0, cellsVisited = 0, support = 0, rebuilds = 0, slots = 0, slotsPerLayer = 0;
        foreach (var colony in sim.Colonies)
        {
            foreach (var layer in Layers(colony))
            {
                cellsTotal += layer.CellCount;
                cellsVisited += layer.ActiveRegionCells;
                support += layer.NonZeroCells;
                rebuilds += layer.BlockListRebuilds;
                slots += layer.BlockSlotsScanned;
                slotsPerLayer += layer.BlockSlotsPerRebuild;
            }
        }

        double nsPerRebuild = RebuildNsPerCall(grid, blockSize);
        double rebuildsPerTick = rebuilds / (double)ticks;
        double rebuildUsPerTick = rebuildsPerTick * nsPerRebuild / 1000.0;
        double tickUs = ms * 1000.0;

        return new FilaLod(blockSize, lodEnabled, slotsPerLayer, cellsTotal, cellsVisited, support,
            rebuildsPerTick, slots / (double)ticks, nsPerRebuild,
            tickUs <= 0 ? 0 : rebuildUsPerTick / tickUs, ms, ms / FrameBudget60Ms);
    }

    private static IEnumerable<PheromoneLayer> Layers(Colony colony)
    {
        yield return colony.FoodLayer;
        yield return colony.HomeLayer;
        yield return colony.AlarmLayer;
        yield return colony.FootprintLayer; // F5.3: huella CHC
    }

    /// <summary>ns de UNA reconstrucción de la lista, aislada: el escaneo de los
    /// contadores de bloque. Se mide sobre una capa propia con un patrón disperso
    /// determinista, porque medirlo dentro del mundo mezclaría el depósito que
    /// ensucia el soporte.</summary>
    private static double RebuildNsPerCall(int grid, int blockSize)
    {
        var layer = new PheromoneLayer(grid, grid, lodBlockSize: blockSize);
        for (int i = 0; i < RebuildBenchmarkCells; i++)
            layer.Deposit(i * 37 % grid, i * 101 % grid, 1f);
        layer.ForceBlockListRebuild(); // calentamiento (JIT y primer escaneo)

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < RebuildBenchmarkCalls; i++) layer.ForceBlockListRebuild();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds * 1_000_000.0 / RebuildBenchmarkCalls;
    }

    /// <summary>Cadencia base de la simulación en la vista (el 10× del control de
    /// velocidad del presenter): la velocidad ×N exige N × 30 ticks/s del CLI.</summary>
    public const double BaseTicksPerSecond = 30.0;

    private static Fila Medir(ulong seed, int grid, int colonies, int ticks, MlpGenome? pool)
    {
        var sim = new WorldSim(seed, grid, colonies);
        if (pool != null)
            for (int c = 0; c < colonies; c++) sim.SeedPoolFromGenomes(c, new[] { pool });

        for (int i = 0; i < WarmupTicks; i++) sim.Step();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) sim.Step();
        sw.Stop();

        double ms = sw.Elapsed.TotalMilliseconds / ticks;

        long cellsTotal = 0, cellsVisited = 0, cellsSupport = 0;
        int ants = 0;
        foreach (var colony in sim.Colonies)
        {
            for (int i = 0; i < colony.Adults.Count; i++)
                if (colony.Adults[i].Alive) ants++;

            foreach (var layer in new[] { colony.FoodLayer, colony.HomeLayer, colony.AlarmLayer, colony.FootprintLayer })
            {
                cellsTotal += layer.CellCount;
                cellsVisited += layer.ActiveRegionCells;
                cellsSupport += layer.NonZeroCells;
            }
        }

        return new Fila(colonies, ants, sim.Items.Count, ms, ms / FrameBudget60Ms,
            cellsTotal, cellsVisited, cellsSupport);
    }

    private static string RenderText(IReadOnlyList<Fila> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("col.  hormigas  ítems   ms/tick  %frame(60)  vel.máx.  celdas LOD            soporte");
        foreach (var f in filas)
        {
            sb.Append(f.Colonies.ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append("  ")
              .Append(f.Ants.ToString(CultureInfo.InvariantCulture).PadLeft(8)).Append("  ")
              .Append(f.Items.ToString(CultureInfo.InvariantCulture).PadLeft(5)).Append("  ")
              .Append(f.MsPerTick.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(7)).Append("  ")
              .Append((f.FrameShare * 100).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(9)).Append("%  ")
              .Append(MaxSpeed(f).PadLeft(7)).Append("  ")
              .Append(Visitadas(f).PadLeft(20)).Append("  ")
              .Append(f.CellsSupport.ToString(CultureInfo.InvariantCulture).PadLeft(7))
              .AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Velocidad máxima sostenible de la vista (×, base 30 ticks/s) si el
    /// CLI solo tuviera que producir ticks. Es la cifra que responde a «¿aguanta
    /// el mundo a velocidad alta con esta cantidad de colonias?».</summary>
    private static string MaxSpeed(Fila f) => f.MsPerTick <= 0
        ? "—"
        : "×" + (1000.0 / f.MsPerTick / BaseTicksPerSecond).ToString("0", CultureInfo.InvariantCulture);

    private static string RenderMarkdown(IReadOnlyList<Fila> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| colonias | hormigas | ítems | ms/tick | % frame (60 fps) | velocidad máx. | celdas LOD visitadas | celdas con soporte |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var f in filas)
        {
            sb.Append("| ").Append(f.Colonies)
              .Append(" | ").Append(f.Ants)
              .Append(" | ").Append(f.Items)
              .Append(" | ").Append(f.MsPerTick.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" | ").Append((f.FrameShare * 100).ToString("0.0", CultureInfo.InvariantCulture)).Append('%')
              .Append(" | ").Append(MaxSpeed(f))
              .Append(" | ").Append(Visitadas(f))
              .Append(" | ").Append(f.CellsSupport)
              .AppendLine(" |");
        }
        return sb.ToString();
    }

    private static string Visitadas(Fila f) =>
        f.CellsTotal == 0
            ? "—"
            : $"{f.CellsVisited}/{f.CellsTotal} ({(f.CellsVisited * 100.0 / f.CellsTotal).ToString("0.0", CultureInfo.InvariantCulture)}%)";

    private static string RenderLodText(IReadOnlyList<FilaLod> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bloque  contadores/capa  celdas visitadas por op.          soporte  reconstr./tick  contadores/tick  ns/reconstr.  reconstr. µs/tick  %del tick  ms/tick");
        foreach (var f in filas)
        {
            sb.Append(LodLabel(f).PadLeft(6)).Append("  ")
              .Append(f.SlotsPerLayer.ToString(CultureInfo.InvariantCulture).PadLeft(15)).Append("  ")
              .Append(LodVisited(f).PadLeft(30)).Append("  ")
              .Append(f.CellsSupport.ToString(CultureInfo.InvariantCulture).PadLeft(7)).Append("  ")
              .Append(f.RebuildsPerTick.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(14)).Append("  ")
              .Append(f.SlotsScannedPerTick.ToString("0", CultureInfo.InvariantCulture).PadLeft(15)).Append("  ")
              .Append(Num(f.LodEnabled, f.NsPerRebuild, "0").PadLeft(12)).Append("  ")
              .Append(Num(f.LodEnabled, f.RebuildsPerTick * f.NsPerRebuild / 1000.0, "0.0").PadLeft(17)).Append("  ")
              .Append((f.RebuildShareOfTick * 100).ToString("0.00", CultureInfo.InvariantCulture).PadLeft(9)).Append("%  ")
              .Append(f.MsPerTick.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(7))
              .AppendLine();
        }
        return sb.ToString();
    }

    private static string RenderLodMarkdown(IReadOnlyList<FilaLod> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| bloque | contadores por capa | celdas visitadas por operación | celdas con soporte | reconstrucciones/tick | contadores escaneados/tick | ns/reconstrucción | reconstrucción (µs/tick) | % del tick | ms/tick | % frame (60 fps) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var f in filas)
        {
            sb.Append("| ").Append(LodLabel(f))
              .Append(" | ").Append(f.SlotsPerLayer)
              .Append(" | ").Append(LodVisited(f))
              .Append(" | ").Append(f.CellsSupport)
              .Append(" | ").Append(f.RebuildsPerTick.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" | ").Append(f.SlotsScannedPerTick.ToString("0", CultureInfo.InvariantCulture))
              .Append(" | ").Append(Num(f.LodEnabled, f.NsPerRebuild, "0"))
              .Append(" | ").Append(Num(f.LodEnabled, f.RebuildsPerTick * f.NsPerRebuild / 1000.0, "0.0"))
              .Append(" | ").Append((f.RebuildShareOfTick * 100).ToString("0.00", CultureInfo.InvariantCulture)).Append('%')
              .Append(" | ").Append(f.MsPerTick.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" | ").Append((f.FrameShare * 100).ToString("0.0", CultureInfo.InvariantCulture)).Append('%')
              .AppendLine(" |");
        }
        return sb.ToString();
    }

    /// <summary>Etiqueta de la fila: el lado del bloque, o «sin LOD» para la fila
    /// de referencia (grid completo, la implementación del test de equivalencia).</summary>
    private static string LodLabel(FilaLod f) =>
        f.LodEnabled ? f.BlockSize.ToString(CultureInfo.InvariantCulture) : "sin LOD";

    private static string LodVisited(FilaLod f) =>
        f.CellsTotal == 0
            ? "—"
            : $"{f.CellsVisited}/{f.CellsTotal} ({(f.CellsVisited * 100.0 / f.CellsTotal).ToString("0.0", CultureInfo.InvariantCulture)}%)";

    /// <summary>Número con formato de la cultura invariante, o «—» cuando la fila
    /// no tiene LOD y por tanto no reconstruye ninguna lista.</summary>
    private static string Num(bool hasLod, double value, string format) =>
        hasLod ? value.ToString(format, CultureInfo.InvariantCulture) : "—";

    private static MlpGenome? LoadPool(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var (_, genomes) = AntGenomeFile.ReadFile(path, BrainContract.CurrentVersion);
        if (genomes.Count == 0) return null;
        var elite = genomes[0];
        for (int i = 1; i < genomes.Count; i++)
            if (genomes[i].Fitness > elite.Fitness) elite = genomes[i];
        return elite;
    }

    /// <summary>Semilla por defecto del medidor (la misma de los pines canónicos).</summary>
    public const ulong DefaultSeed = 42;

    /// <summary>Ticks por defecto: suficiente para que el mundo tenga rastro y cría.</summary>
    public const int DefaultTicks = 1500;

    /// <summary>Grid por defecto: el mundo real del modo juego.</summary>
    public const int DefaultGrid = 256;

    /// <summary>Fracción del grid que el LOD visita en un mundo real (0..1).</summary>
    public static double LodFraction(int grid = DefaultGrid, int colonies = 2, int ticks = DefaultTicks, ulong seed = DefaultSeed)
    {
        var sim = new WorldSim(seed, grid, colonies);
        for (int i = 0; i < ticks; i++) sim.Step();
        long total = 0, visited = 0;
        foreach (var colony in sim.Colonies)
        {
            foreach (var layer in new[] { colony.FoodLayer, colony.HomeLayer, colony.AlarmLayer, colony.FootprintLayer })
            {
                total += layer.CellCount;
                visited += layer.ActiveRegionCells;
            }
        }
        return total == 0 ? 0 : visited / (double)total;
    }
}
