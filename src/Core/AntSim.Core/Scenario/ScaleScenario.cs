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
