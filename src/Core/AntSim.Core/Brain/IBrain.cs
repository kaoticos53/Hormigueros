using AntSim.Core.Contracts;

namespace AntSim.Core.Brain;

/// <summary>Versión del contrato de canales (19 sensores / 6 decisiones).</summary>
public static class BrainContract
{
    public const int CurrentVersion = 1;
}

public enum BrainKind : byte
{
    Mlp = 0,
    Neat = 1
}

/// <summary>
/// Contrato de cerebro: evaluación pura y determinista del estado de una hormiga.
/// - Sin RNG, sin asignaciones, sin dependencia del orden de iteración.
/// - El Core desconoce los pesos: solo conoce esta interfaz (MLP y NEAT son plug-ins).
/// </summary>
public interface IBrain
{
    BrainKind Kind { get; }

    int ContractVersion { get; }

    /// <summary>
    /// Evalúa los sensores y escribe la decisión. Debe ser pura: los mismos
    /// sensores producen siempre la misma decisión.
    /// </summary>
    void Evaluate(in AntSensors sensors, ref AntDecision decision);
}
