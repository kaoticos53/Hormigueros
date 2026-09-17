using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntSim.Core.Brain;
using AntSim.Core.Evolution;

namespace AntSim.Core.Training;

/// <summary>Una fila del benchmark de políticas: una política medida sobre las MISMAS semillas.</summary>
public readonly record struct PolicyRow(
    string Name,
    string What,
    double FitnessMean,
    double FitnessSd,
    double PickupsMean,
    double UnloadsMean,
    int SeedsWithUnload,
    int Seeds,
    double BestFitness);

/// <summary>Resultado del benchmark: filas + las condiciones exactas de la medición.</summary>
public sealed class PolicyBenchmarkResult
{
    public IReadOnlyList<PolicyRow> Rows { get; }
    public IReadOnlyList<ulong> Seeds { get; }
    public float BandMin { get; }
    public float BandMax { get; }
    public int Ticks { get; }
    public int Trials { get; }

    public PolicyBenchmarkResult(IReadOnlyList<PolicyRow> rows, IReadOnlyList<ulong> seeds,
        float bandMin, float bandMax, int ticks, int trials, int arenaCells)
    {
        Rows = rows;
        Seeds = seeds;
        BandMin = bandMin;
        BandMax = bandMax;
        Ticks = ticks;
        Trials = trials;
        ArenaCells = arenaCells;
    }

    /// <summary>Celdas del grid de la arena usada (768 u por defecto).</summary>
    public int ArenaCells { get; }

    /// <summary>Tabla markdown lista para pegar en un doc (misma forma que el resto del proyecto).</summary>
    public string RenderMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("| política | qué es | fitness (media ± σ) | pickups | descargas | semillas c/ desc. | mejor |")
          .AppendLine();
        sb.Append("|---|---|---|---|---|---|---|").AppendLine();
        foreach (var r in Rows)
        {
            sb.Append("| ").Append(r.Name)
              .Append(" | ").Append(r.What)
              .Append(" | ").Append(F(r.FitnessMean)).Append(" ± ").Append(F(r.FitnessSd))
              .Append(" | ").Append(F(r.PickupsMean))
              .Append(" | ").Append(F(r.UnloadsMean))
              .Append(" | ").Append(r.SeedsWithUnload).Append('/').Append(r.Seeds)
              .Append(" | ").Append(F(r.BestFitness))
              .AppendLine(" |");
        }
        return sb.ToString();
    }

    /// <summary>Resumen legible en una terminal (el CLI lo imprime tal cual).</summary>
    public string RenderText()
    {
        var sb = new StringBuilder();
        sb.Append("benchmark de políticas — banda ").Append(BandMin.ToString("0", CultureInfo.InvariantCulture))
          .Append('-').Append(BandMax.ToString("0", CultureInfo.InvariantCulture))
          .Append(" u · arena ").Append(ArenaCells).Append(" celdas · ").Append(Ticks)
          .Append(" ticks · ").Append(Trials).Append(" prueba(s) por semilla")
          .AppendLine();
        sb.Append("semillas: ").AppendLine(string.Join(' ', Seeds));
        sb.AppendLine();
        foreach (var r in Rows)
        {
            sb.Append(r.Name.PadRight(30))
              .Append(" fitness ").Append(F(r.FitnessMean).PadLeft(9))
              .Append(" ± ").Append(F(r.FitnessSd).PadLeft(8))
              .Append("  pickups ").Append(F(r.PickupsMean).PadLeft(7))
              .Append("  descargas ").Append(F(r.UnloadsMean).PadLeft(7))
              .Append("  c/desc ").Append(r.SeedsWithUnload).Append('/').Append(r.Seeds)
              .Append("  mejor ").Append(F(r.BestFitness).PadLeft(9))
              .AppendLine();
        }
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>
/// F5.3bis — BENCHMARK DE POLÍTICAS (idea nº2 de <c>docs/ideas-externas.md</c>,
/// inspirada en <c>ant-colony-rl</c>): aleatoria vs scripted vs evolucionada,
/// en la MISMA arena y las MISMAS semillas, con tabla publicada.
///
/// POR QUÉ. Teníamos entrenamiento, currículos y comparaciones de pools, pero
/// ninguna medida que respondiera a la pregunta que un lector externo hace
/// primero: ¿esto es mejor que no hacer nada, o que un puñado de reglas? Sin esa
/// vara, «el pool pre-entrenado mejora la supervisión inicial» es una afirmación
/// interna sin referencia.
///
/// PROTOCOLO (idéntico para las tres políticas):
///   · La misma <see cref="ArenaEvaluator"/>: colonia completa del mundo (10
///     fundadoras + cría), comida real a 4 ep en la banda pedida, sin rastro
///     plantado, sin respawn, presupuesto completo de ticks.
///   · POLÍTICA CONGELADA (<see cref="ArenaEvaluator.EvaluatePolicy"/>): todas las
///     hormigas, incluidas las que nacen durante la prueba, llevan el mismo
///     cerebro y ninguna realimenta el pool. Es lo que separa «calidad del cerebro»
///     de «evolución ocurrida durante la prueba».
///   · La MISMA semilla por fila ⇒ mismo mundo, misma comida, mismos rumbos.
///   · La política aleatoria usa la MISMA topología que la evolucionada (pesos
///     sorteados con semilla fija): la comparación aísla los pesos, no el tamaño
///     de la red.
///   · Fila de REFERENCIA «evolucionada + pool»: la misma élite pero con el
///     protocolo nativo de la arena (el pool se siembra y la descendencia nace de
///     variantes mutadas). Mide lo que la evolución añade ENCIMA del cerebro
///     congelado, y no es comparable fila a fila con las anteriores (otro régimen).
/// </summary>
public static class PolicyBenchmark
{
    public const string RandomName = "aleatoria";
    public const string ScriptedName = "scripted";
    public const string EvolvedName = "evolucionada (congelada)";
    public const string EvolvedPoolName = "evolucionada + pool (referencia)";

    public static PolicyBenchmarkResult Run(
        IReadOnlyList<ulong> seeds,
        float bandMin,
        float bandMax,
        int ticks,
        int trials,
        MlpGenome evolvedElite,
        ulong randomSeed,
        int? arenaCells = null)
    {
        if (seeds is null || seeds.Count == 0) throw new ArgumentException("Hacen falta semillas.", nameof(seeds));
        if (evolvedElite is null) throw new ArgumentNullException(nameof(evolvedElite));

        // Política aleatoria determinista con la MISMA topología que la evolucionada.
        var rng = new Sim.DeterministicRandom(randomSeed);
        var randomGenome = MlpGenome.Random(ref rng, evolvedElite.Sizes);

        var policies = new (string Name, string What, IBrain Brain)[]
        {
            (RandomName, "pesos al azar, misma topología", randomGenome.ToBrain()),
            (ScriptedName, "reglas a mano (brújula + visión + rastro)", new ScriptedBrain()),
            (EvolvedName, "élite del pool pre-entrenado, pesos congelados", evolvedElite.ToBrain()),
        };

        var rows = new List<PolicyRow>(policies.Length + 1);
        foreach (var (name, what, brain) in policies)
            rows.Add(Measure(name, what, seeds, bandMin, bandMax, ticks, trials, arenaCells,
                (ev) => ev.EvaluatePolicy(brain)));

        rows.Add(Measure(EvolvedPoolName, "la élite con su pool evolucionando en la prueba",
            seeds, bandMin, bandMax, ticks, trials, arenaCells, (ev) => ev.Evaluate(evolvedElite)));

        return new PolicyBenchmarkResult(rows, seeds, bandMin, bandMax, ticks, trials,
            arenaCells ?? ArenaEvaluator.GridCells);
    }

    private static PolicyRow Measure(
        string name, string what, IReadOnlyList<ulong> seeds,
        float bandMin, float bandMax, int ticks, int trials, int? arenaCells,
        Func<ArenaEvaluator, ArenaResult> evaluate)
    {
        double sumF = 0, sumF2 = 0, sumPickups = 0, sumUnloads = 0;
        double best = double.MinValue;
        int withUnload = 0;
        for (int i = 0; i < seeds.Count; i++)
        {
            // Una arena por semilla: el RNG de pruebas avanza en orden fijo, así
            // que la fila es determinista para (semilla, banda, ticks, trials).
            var arena = new ArenaEvaluator(seeds[i], bandMin, bandMax, ticks, trials, arenaCells);
            ArenaResult r = evaluate(arena);
            sumF += r.Fitness;
            sumF2 += r.Fitness * r.Fitness;
            sumPickups += r.Pickups;
            sumUnloads += r.Unloads;
            if (r.Unloads >= 1) withUnload++;
            if (r.Fitness > best) best = r.Fitness;
        }
        int n = seeds.Count;
        double mean = sumF / n;
        double variance = Math.Max(0.0, sumF2 / n - mean * mean); // poblacional (σ de la muestra medida)
        return new PolicyRow(name, what, mean, Math.Sqrt(variance),
            sumPickups / n, sumUnloads / n, withUnload, n, best);
    }
}
