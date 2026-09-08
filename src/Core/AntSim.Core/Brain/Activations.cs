using System;

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
    public static float Apply(ActivationId id, float x) => id switch
    {
        ActivationId.Tanh => MathF.Tanh(x),
        ActivationId.Sigmoid => 1.0f / (1.0f + MathF.Exp(-x)),
        ActivationId.Linear => x,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Id de activación desconocido.")
    };
}
