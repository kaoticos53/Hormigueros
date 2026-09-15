using System;
using AntSim.Core.Sim;

namespace AntSim.Core.Brain;

/// <summary>
/// Registro versionado de funciones de activación (ids estables: se serializan
/// en los archivos de genoma y el importador rechaza ids desconocidos).
/// </summary>
public enum ActivationId : byte
{
    Tanh = 0,     // [−1,1]   — steer y capas ocultas
    Sigmoid = 1,  // [0,1]    — speed, depósitos, interact
    Linear = 2    // sin saturar (reservado)
}

public static class Activations
{
    // CanonMath (F5.2c): MathF.Tanh/Exp difieren 1 ULP entre Windows y Linux
    // (libm del sistema) — con sensores que alimentan el cerebro, ese ULP
    // cambia la decisión de una hormiga y el hash canónico diverge cross-
    // platform. CanonMath es bit-exacta en cualquier SO.
    public static float Apply(ActivationId id, float x) => id switch
    {
        ActivationId.Tanh => CanonMath.Tanh(x),
        ActivationId.Sigmoid => 1.0f / (1.0f + CanonMath.Exp(-x)),
        ActivationId.Linear => x,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Id de activación desconocido.")
    };
}
