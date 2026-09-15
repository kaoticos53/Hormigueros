using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;

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

    // ═════════════════════════════════════════════════════════════════════
    // F5.2c rodaja 6 — grafo NEAT arbitrario. Mismo estilo de línea (tag,
    // nombre, valor, barra con signo) pero la "capa" es la PROFUNDIDAD
    // TOPOLOGICA del nodo: los ids fijos 0..18 anclan la capa de entrada y
    // los 19..24 la de salida; los ocultos se colocan por profundidad (un
    // oculto entre entrada y salida = capa 1, uno en cadena = 2, …). El
    // vector de activaciones del canal F NO cambia de formato — solo el
    // grafo que lo explica.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Renderiza el grafo NEAT a partir de su descripción + activaciones del
    /// canal F (orden canónico [entradas, ocultos por id, salidas]).
    /// <paramref name="graph"/> sale de <see cref="NeatBrain.DescribeGraph"/>.
    /// </summary>
    public static string RenderNeat(NeatGraphDescription graph, ReadOnlySpan<float> activations,
        string? title = null, string[]? outputNames = null)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (activations.Length != graph.NodeIds.Length)
            throw new ArgumentException(
                $"El vector tiene {activations.Length} activaciones; el grafo tiene {graph.NodeIds.Length} nodos.",
                nameof(activations));

        var sb = new StringBuilder();
        sb.Append("NEAT ").Append(graph.HiddenCount).Append("h/")
          .Append(graph.ActiveConnCount).Append("c (")
          .Append(graph.NodeIds.Length).Append(" nodos)");
        if (title != null) sb.Append(" — ").Append(title);
        sb.AppendLine();

        // Capas por profundidad: entra 0..18 = 0, ocultos 1..K, salidas al final.
        var byDepth = new List<(int slot, int id, string name)>[graph.MaxDepth + 2];
        for (int d = 0; d < byDepth.Length; d++) byDepth[d] = new();

        // F5.2c rodaja 6 — los ocultos NO son contiguos en el orden del canal F
        // en grafos crecidos (Kahn puede emitir salidas 19..24 antes que un
        // oculto de id mayor): el índice de HiddenDepth es un CONTADOR propio,
        // no la posición del nodo en el vector.
        int hiddenIdx = 0;
        for (int i = 0; i < graph.NodeIds.Length; i++)
        {
            int id = graph.NodeIds[i];
            int slot;
            string name;
            if (id < AntSensorChannelInfo.Count)
            {
                slot = 0;
                name = Enum.GetName(typeof(AntSensorChannel), id) ?? $"ch{id}";
            }
            else if (id < NodeGene.FirstHiddenId)
            {
                slot = graph.MaxDepth + 1; // salida: última capa
                int oi = id - NodeGene.FirstOutputId;
                name = outputNames != null && oi < outputNames.Length
                    ? outputNames[oi] : OutputNames[oi];
            }
            else
            {
                slot = 1 + graph.HiddenDepth[hiddenIdx++];
                name = $"h{id}";
            }
            byDepth[slot].Add((i, id, name));
        }

        for (int d = 0; d < byDepth.Length; d++)
        {
            if (byDepth[d].Count == 0) continue;
            sb.Append("== capa ").Append(d).Append(" ==").AppendLine();
            foreach (var entry in byDepth[d])
            {
                int slot = entry.slot;
                float v = activations[slot];
                sb.Append(' ').Append(NodeTag(slot, graph.NodeIds[slot])).Append('[')
                  .Append(slot.ToString("00", CultureInfo.InvariantCulture)).Append("] ")
                  .Append(entry.name.PadRight(15))
                  .Append(FormatValue(v)).Append(' ').Append(Bar(v))
                  .AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string NodeTag(int slot, int id)
    {
        if (id < AntSensorChannelInfo.Count) return "in";
        if (id < NodeGene.FirstHiddenId) return "out";
        return "hid";
    }

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
