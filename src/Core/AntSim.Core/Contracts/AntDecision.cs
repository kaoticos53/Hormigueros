using System;

namespace AntSim.Core.Contracts;

/// <summary>
/// Salida del cerebro (contrato IBrain v1): 6 canales normalizados.
/// El Core valida cada decisión (ver <see cref="AntSim.Core.Validation.DecisionValidator"/>):
/// sanitización de no-finitos, clamp a límites, gating físico/químico y cooldowns.
/// </summary>
public struct AntDecision
{
    public float Steer;          // [−1,1]  ω = steer · Ωmax(especie)
    public float Speed;          // [0,1]   v = speed · vMax(especie)
    public float DepositFood;    // [0,1]   tasa de depósito FoodTrail
    public float DepositHome;    // [0,1]   tasa de depósito Home
    public float DepositAlarm;   // [0,1]   tasa de depósito Alarm
    public float Interact;       // [0,1]   ≥ 0.5 intenta interacción

    /// <summary>Número de salidas del contrato v1.</summary>
    public const int DecisionCount = 6;

    public static AntDecision Neutral()
    {
        // Valores neutros usados cuando una salida no es finita.
        AntDecision d = default;
        d.Steer = 0f;
        d.Speed = 0f;
        return d;
    }

    public void CopyTo(Span<float> target)
    {
        target[0] = Steer;
        target[1] = Speed;
        target[2] = DepositFood;
        target[3] = DepositHome;
        target[4] = DepositAlarm;
        target[5] = Interact;
    }

    public readonly bool AllFinite()
    {
        return FloatUtil.IsFinite(Steer) && FloatUtil.IsFinite(Speed)
            && FloatUtil.IsFinite(DepositFood) && FloatUtil.IsFinite(DepositHome)
            && FloatUtil.IsFinite(DepositAlarm) && FloatUtil.IsFinite(Interact);
    }
}

public static class FloatUtil
{
    public static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
