using AntSim.Core.Brain;

namespace AntSim.Core.Evolution;

/// <summary>
/// Contrato evolutivo de un cerebro. En Fase 2 solo existe <see cref="MlpGenome"/>;
/// NEAT implementará la misma interfaz con genes estructurales (Fase 5), de modo
/// que el pool y la cuarentena no cambien.
/// </summary>
public interface IGenome
{
    BrainKind Kind { get; }

    /// <summary>Versión del contrato de canales (19 sensores / 6 decisiones).</summary>
    int ContractVersion { get; }

    double Fitness { get; set; }

    IGenome CloneGenome();
}