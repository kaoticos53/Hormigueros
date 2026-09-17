using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Core.Brain;
using AntSim.Core.Evolution;
using AntSim.Core.Training;

namespace AntSim.Core.Scenario;

/// <summary>
/// F5.3bis — escenario del BENCHMARK DE POLÍTICAS para el CLI (`--mode bench`).
///
/// Es la capa fina que une el experimento (<see cref="PolicyBenchmark"/>) con la
/// línea de comandos: elige las semillas, carga la élite evolucionada del
/// <c>.antgenome</c> y devuelve el reporte con la tabla pegable en un doc.
/// Determinista: mismas semillas + mismo pool + misma banda ⇒ misma tabla.
/// </summary>
public static class PolicyBenchmarkScenario
{
    /// <summary>Semillas por defecto: las MISMAS del benchmark de referencia de Fase 3ter
    /// (comparabilidad con la tabla publicada de pools).</summary>
    public static readonly ulong[] DefaultSeeds =
        { 42, 7, 99, 1234, 777, 314, 555, 808, 1000, 2026 };

    /// <summary>Pool por defecto: la élite pre-entrenada de referencia del repo.</summary>
    public const string DefaultPoolPath = "tests/fixtures/warm-v2.antgenome";

    /// <summary>Semilla de la política aleatoria (fija: la fila es reproducible).</summary>
    public const ulong RandomPolicySeed = 20_260_917UL;

    public static string Run(
        IReadOnlyList<ulong>? seeds,
        float bandMin,
        float bandMax,
        int ticks,
        int trials,
        string? seedPoolPath,
        int? arenaCells = null)
    {
        var seedList = seeds is { Count: > 0 } ? seeds : DefaultSeeds;
        string poolPath = seedPoolPath ?? DefaultPoolPath;
        if (!File.Exists(poolPath))
            throw new FileNotFoundException(
                $"El benchmark necesita un .antgenome para la política evolucionada: no existe '{poolPath}'. " +
                "Pasa --seed-pool <archivo> (por ejemplo artifacts/pretrain-warm-v2.antgenome).", poolPath);

        var (metadata, genomes) = AntGenomeFile.ReadFile(poolPath, BrainContract.CurrentVersion);
        if (genomes.Count == 0)
            throw new InvalidOperationException($"'{poolPath}' no trae genomas.");

        // La élite = el mejor por fitness del archivo (no se asume orden).
        MlpGenome elite = genomes[0];
        for (int i = 1; i < genomes.Count; i++)
            if (genomes[i].Fitness > elite.Fitness) elite = genomes[i];

        var result = PolicyBenchmark.Run(seedList, bandMin, bandMax, ticks, trials, elite, RandomPolicySeed, arenaCells);

        var sb = new StringBuilder();
        sb.Append("políticas: ").Append(poolPath)
          .Append(" · ").Append(genomes.Count).Append(" genomas · élite fitness ")
          .Append(elite.Fitness.ToString("0.0", CultureInfo.InvariantCulture))
          .Append(" · pool '").Append(string.IsNullOrEmpty(metadata.Name) ? "?" : metadata.Name)
          .Append("' · gen ").Append(metadata.Generation).AppendLine();
        sb.AppendLine();
        sb.Append(result.RenderText());
        sb.AppendLine();
        sb.AppendLine("## Tabla markdown");
        sb.AppendLine();
        sb.Append(result.RenderMarkdown());
        return sb.ToString();
    }
}
