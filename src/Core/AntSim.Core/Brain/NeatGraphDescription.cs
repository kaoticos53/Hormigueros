using System;

namespace AntSim.Core.Brain;

/// <summary>
/// F5.2c rodaja 6 — descripción de la TOPOLOGÍA de un grafo NEAT para la
/// telemetría (canal F) y el inspector (linaje n/h/c). Pura y serializable:
/// ids de nodo en el orden canónico del canal F ([entradas 0..18, ocultos por
/// id, salidas 19..24]) + profundidad topológica de cada oculto + nº de
/// conexiones activas. No lleva pesos ni activaciones: es la FORMA del cerebro,
/// la parte que el linaje de la UI quiere seguir entre generaciones.
/// </summary>
public sealed class NeatGraphDescription
{
    /// <summary>Ids de nodo en el orden canónico del canal F.</summary>
    public int[] NodeIds { get; }

    /// <summary>Profundidad topológica de cada OCULTO (paralelo a la sección
    /// de ocultos de <see cref="NodeIds"/>): 1 = directamente sobre entradas,
    /// 2 = sobre ocultos de profundidad 1, … La profundidad determina la
    /// columna del render.</summary>
    public int[] HiddenDepth { get; }

    /// <summary>Nº de conexiones ACTIVAS (el c del resumen n/h/c).</summary>
    public int ActiveConnCount { get; }

    /// <summary>Profundidad máxima de la red (la salida vive en MaxDepth+1).</summary>
    public int MaxDepth { get; }

    /// <summary>Nº de nodos ocultos (el h del resumen n/h/c).</summary>
    public int HiddenCount { get; }

    public NeatGraphDescription(int[] nodeIds, int[] hiddenDepth, int activeConnCount)
    {
        NodeIds = nodeIds ?? throw new ArgumentNullException(nameof(nodeIds));
        HiddenDepth = hiddenDepth ?? throw new ArgumentNullException(nameof(hiddenDepth));
        ActiveConnCount = activeConnCount;
        HiddenCount = hiddenDepth.Length;
        MaxDepth = 0;
        for (int i = 0; i < hiddenDepth.Length; i++)
            if (hiddenDepth[i] > MaxDepth) MaxDepth = hiddenDepth[i];
    }
}
