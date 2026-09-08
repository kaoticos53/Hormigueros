namespace AntSim.Core.Sim;

/// <summary>
/// Constantes globales de la simulación. El tick fijo y el tamaño de celda son
/// parte del contrato de determinismo: nunca deben cambiarse sin subir la versión.
/// </summary>
public static class SimConstants
{
    /// <summary>Paso fijo de simulación en segundos (tick de 30 Hz).</summary>
    public const float FixedDtSeconds = 1.0f / 30.0f;

    /// <summary>Tamaño de celda del grid de feromonas en unidades de mundo.</summary>
    public const float CellSizeUnits = 8.0f;

    /// <summary>Ancho/alto por defecto del grid de feromonas (en celdas).</summary>
    public const int DefaultGridCells = 512;

    /// <summary>Capacidad máxima por celda de feromona (normalizada a [0,1]).</summary>
    public const float PheromoneCellMax = 1.0f;

    /// <summary>Máximo de hormigas adultas por colonia.</summary>
    public const int MaxAdultsPerColony = 40;
}
