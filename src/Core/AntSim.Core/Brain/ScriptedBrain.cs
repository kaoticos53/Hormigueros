using System;
using AntSim.Core.Contracts;

namespace AntSim.Core.Brain;

/// <summary>
/// Política ESCRITA A MANO (F5.3bis) — el «jugador experto» contra el que se mide
/// la evolución, y la vara de medir del benchmark de políticas.
///
/// POR QUÉ EXISTE. Hasta ahora no había forma de afirmar «la evolución gana a X»:
/// entrenábamos y comparábamos pools entre sí, no políticas entre sí. Esta clase
/// cierra ese hueco con la política scripted del benchmark: reglas explícitas,
/// deterministas y sin estado, que un humano puede leer, discutir y mejorar.
///
/// REGLAS (todas sobre los 19 canales, sin RNG ni memoria):
///   1. CON CARGA → vuelve al nido por la BRÚJULA innata (HomeDx/HomeDy), marcando
///      el camino con rastro de COMIDA (reclutamiento: es el rastro que las
///      compañeras siguen hacia la fuente) y con un rastro de casa tenue. La
///      descarga la dispara la intención de interacción: el mundo solo la concede
///      dentro del radio del nido, así que «intentar siempre» es exactamente
///      «descargar al llegar».
///   2. SIN CARGA y con comida a la vista → persigue el ítem más cercano
///      (FoodDx/FoodDy) e intenta recogerlo. Si no ve nada, sigue el rastro de
///      comida si lo hay (reclutamiento).
///   3. SIN comida ni rastro → mantiene el rumbo (giro 0): la dispersión en línea
///      recta desde el nido es lo que hace que las fundadoras alcancen la banda de
///      comida a ≥200 u antes de agotar su vida — el problema que la arena mide.
///   4. PARED: con la proximidad alta gira hacia el NIDO. Las tres sondas de
///      obstáculo valen lo mismo (el único obstáculo de F1 son los bordes), así que
///      la hormiga no puede saber de qué lado está la pared; volver hacia el
///      centro es la respuesta determinista que no la deja atrapada pegada al muro
///      (girar en el sitio la dejaría orbitando el borde).
///
/// El giro hacia un objetivo es un control proporcional saturado sobre el ángulo
/// local (`steer = clamp(ganancia · atan2(izquierda, adelante), −1, 1)`), sin
/// depender de constantes de especie: el mismo cerebro sirve para las tres.
/// </summary>
public sealed class ScriptedBrain : IBrain
{
    public BrainKind Kind => BrainKind.Mlp;
    public int ContractVersion => BrainContract.CurrentVersion;

    /// <summary>Ganancia del control de rumbo hacia un objetivo local.</summary>
    public const float HeadingGain = 2.5f;
    /// <summary>Cuánto pesa el rastro (de comida o de casa) frente a la brújula.</summary>
    public const float TrailGain = 0.5f;
    /// <summary>Rastro de COMIDA mientras vuelve con la carga (recluta hacia la fuente).</summary>
    public const float FoodDeposit = 0.5f;
    /// <summary>Rastro de casa tenue mientras vuelve (marca el camino al nido).</summary>
    public const float HomeDeposit = 0.3f;
    /// <summary>Rastro de comida al verla de lejos (refuerzo antes del pickup).</summary>
    public const float SightDeposit = 0.4f;
    /// <summary>Proximidad de pared a partir de la cual vuelve hacia el nido.</summary>
    public const float WallProx = 0.6f;

    public void Evaluate(in AntSensors s, ref AntDecision d)
    {
        d = AntDecision.Neutral();
        d.Speed = 1f;

        if (s.HasLoad >= 0.5f)
        {
            // — 1. Carga: a casa por brújula + rastro de casa —
            d.Steer = SteerTowards(s.HomeDx, s.HomeDy) + TrailGain * s.HomeTrailDiff;
            d.DepositFood = FoodDeposit;
            d.DepositHome = HomeDeposit;
            d.Interact = 1f; // descarga al entrar en el radio del nido
        }
        else
        {
            if (s.FoodSize > 0f)
            {
                // — 2. Comida a la vista: a por ella (y la marca al verla) —
                d.Steer = SteerTowards(s.FoodDx, s.FoodDy);
                d.DepositFood = SightDeposit;
            }
            else
            {
                // — 3. Nada a la vista: rastro de comida o rumbo recto (dispersión) —
                d.Steer = TrailGain * s.FoodTrailDiff;
            }
            d.Interact = 1f; // recoge si está al alcance
        }

        // — 4. Pared: de vuelta al centro —
        float prox = MathF.Max(s.ProxLeft, MathF.Max(s.ProxFront, s.ProxRight));
        if (prox > WallProx)
            d.Steer += SteerTowards(s.HomeDx, s.HomeDy);

        d.Steer = Math.Clamp(d.Steer, -1f, 1f);
    }

    /// <summary>
    /// Giro hacia un objetivo expresado en el marco local (+adelante, +izquierda),
    /// como proporción del giro máximo disponible. Señal: positivo = izquierda (el
    /// mismo convenio que el contrato de decisiones).
    /// </summary>
    public static float SteerTowards(float forward, float left)
    {
        if (MathF.Abs(forward) < 1e-6f && MathF.Abs(left) < 1e-6f) return 0f;
        return Math.Clamp(HeadingGain * MathF.Atan2(left, forward), -1f, 1f);
    }
}
