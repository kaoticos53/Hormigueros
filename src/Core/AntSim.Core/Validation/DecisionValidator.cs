using AntSim.Core.Contracts;

namespace AntSim.Core.Validation;

/// <summary>
/// Validación de cada decisión en el Core (etapa a y b del contrato IBrain).
///
/// a) Sanitización: cualquier salida NaN/Inf se sustituye por el valor neutro y la
///    decisión se marca inválida (el genoma acumula demérito al morir).
/// b) Clamp a los límites del actuador.
///
/// Las etapas c (gating físico/químico) y d (cooldowns) dependen del estado del
/// mundo y se resuelven donde hay contacto; aquí solo se garantiza que la salida
/// del cerebro sea finita y acotada antes de mapearla a actuadores.
/// </summary>
public static class DecisionValidator
{
    /// <summary>
    /// Valida una decisión cruda. Devuelve false si hubo que sanitizar
    /// (salida no finita) — la decisión queda con valores neutros válidos.
    /// </summary>
    public static bool SanitizeAndClamp(in AntDecision raw, out AntDecision clean)
    {
        bool allFinite = raw.AllFinite();
        if (allFinite)
        {
            clean = raw;
        }
        else
        {
            clean = AntDecision.Neutral();
        }

        clean.Steer = Clamp(clean.Steer, -1f, 1f);
        clean.Speed = Clamp(clean.Speed, 0f, 1f);
        clean.DepositFood = Clamp(clean.DepositFood, 0f, 1f);
        clean.DepositHome = Clamp(clean.DepositHome, 0f, 1f);
        clean.DepositAlarm = Clamp(clean.DepositAlarm, 0f, 1f);
        clean.Interact = Clamp(clean.Interact, 0f, 1f);
        return allFinite;
    }

    /// <summary>Intentar interacción (interact ≥ 0.5).</summary>
    public static bool WantsInteraction(in AntDecision d) => d.Interact >= 0.5f;

    private static float Clamp(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }
}
