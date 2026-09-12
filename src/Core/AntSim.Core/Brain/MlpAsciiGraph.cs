using System;
using System.Globalization;
using System.Text;
using AntSim.Core.Contracts;

namespace AntSim.Core.Brain;

/// <summary>
/// F5.0 — Renderizador ASCII del mini-grafo MLP de la hormiga inspeccionada
/// (canal F). Puro y sin dependencias: recibe las dimensiones del cerebro y el
/// vector de activaciones del canal F y produce un texto legible por capa, con
/// el nombre canónico de cada canal (contrato v1: <see cref="AntSensorChannel"/>,
/// salidas de <see cref="AntDecision"/>) y una barra con signo. Verificado
/// headless en la suite; la vista de Unity puede reutilizarlo tal cual o
/// sustituirlo por el grafo interactivo.
/// </summary>
public static class MlpAsciiGraph
{
    /// <summary>Nombres canónicos de las 6 salidas (contrato v1).</summary>
    public static readonly string[] OutputNames =
        { "Steer", "Speed", "DepositFood", "DepositHome", "DepositAlarm", "Interact" };

    /// <summary>
    /// Renderiza el vector de activaciones en el orden canónico del MLP
    /// (entradas · ocultas · salidas). Lanza si la longitud no coincide con
    /// <paramref name="sizes"/>.
    /// </summary>
    /// <param name="sizes">Tamaños por capa, p. ej. {19, 8, 6}.</param>
    /// <param name="activations">Activaciones del canal F (SnapshotActivations).</param>
    /// <param name="title">Primera línea opcional (tick, hormiga…).</param>
    public static string Render(int[] sizes, ReadOnlySpan<float> activations, string? title = null,
        string[]? outputNames = null)
    {
        if (sizes is null) throw new ArgumentNullException(nameof(sizes));
        if (sizes.Length < 2) throw new ArgumentException("Se requieren al menos 2 capas.", nameof(sizes));

        int total = 0;
        for (int l = 0; l < sizes.Length; l++) total += sizes[l];
        if (activations.Length != total)
            throw new ArgumentException(
                $"El vector tiene {activations.Length} activaciones; el cerebro espera {total}.", nameof(activations));

        var sb = new StringBuilder();
        sb.Append("MLP ");
        for (int l = 0; l < sizes.Length; l++)
            sb.Append(l > 0 ? "·" : "").Append(sizes[l]);
        sb.Append(" (").Append(total).Append(" nodos)");
        if (title != null) sb.Append(" — ").Append(title);
        sb.AppendLine();

        int off = 0;
        for (int l = 0; l < sizes.Length; l++)
        {
            int n = sizes[l];
            sb.Append("== capa ").Append(l).Append(n > 0 ? " ==" : " ==").AppendLine();
            for (int i = 0; i < n; i++)
            {
                float v = activations[off + i];
                sb.Append(' ').Append(LayerTag(l)).Append('[')
                  .Append(i.ToString("00", CultureInfo.InvariantCulture)).Append("] ")
                  .Append(NodeName(l, i, n, outputNames).PadRight(15))
                  .Append(FormatValue(v)).Append(' ').Append(Bar(v))
                  .AppendLine();
            }
            off += n;
        }
        return sb.ToString();
    }

    private static string LayerTag(int layer) => layer switch
    {
        0 => "in",
        1 => "hid",
        _ => "out"
    };

    private static string NodeName(int layer, int index, int layerSize, string[]? outputNames)
    {
        if (layer == 0 && index < AntSensorChannelInfo.Count)
            return Enum.GetName(typeof(AntSensorChannel), index) ?? $"ch{index}";
        if (layerSize == AntDecision.DecisionCount && outputNames != null && index < outputNames.Length)
            return outputNames[index];
        return $"h{index}";
    }

    private static string FormatValue(float v)
        => (v >= 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Barra ASCII con signo: v en [−1,1] → hasta 10 celdas.</summary>
    private static string Bar(float v)
    {
        float c = Math.Clamp(v, -1f, 1f);
        int cells = (int)MathF.Round(MathF.Abs(c) * 10f);
        var sb = new StringBuilder("[");
        if (c < 0) sb.Append(' ', 10 - cells).Append('#', cells).Append('|');
        else sb.Append('|').Append('#', cells).Append(' ', 10 - cells);
        sb.Append(']');
        return sb.ToString();
    }
}
