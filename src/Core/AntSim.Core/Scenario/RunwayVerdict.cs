using System;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// Estado de AUTONOMÍA de una colonia (chip de la tarjeta, contrato HUD §2).
/// Seis estados, no cinco: «sin datos» es un valor propio, para que un stream
/// viejo (sin el campo) o una colonia que aún no tiene EMA de consumo NO se
/// pinten como desahogo —un chip que miente es peor que un chip ausente—.
/// </summary>
public enum RunwayState : byte
{
    /// <summary>Sin medida todavía (EMA de consumo a cero) o stream sin el campo.</summary>
    Unknown = 0,
    /// <summary><c>T &lt; T_crit</c>: raciona adultas y, en último recurso, pupas.</summary>
    Critical = 1,
    /// <summary><c>T &lt; T_cann</c>: come las larvas más débiles (y corta la puesta).</summary>
    Cannibalism = 2,
    /// <summary><c>T &lt; T_ooph</c>: reabsorbe huevos (oofagia).</summary>
    Oophagy = 3,
    /// <summary><c>T &lt; T_safe</c>: pone con reserva (<c>ρ_res &lt; 1</c>).</summary>
    Reserve = 4,
    /// <summary><c>T ≥ T_safe</c>: puesta a tope.</summary>
    Surplus = 5,
}

/// <summary>
/// Veredicto del runway (<c>T_runway = S / consumoEMA</c>) — LOS UMBRALES DE LA
/// ESPECIE, como código: la UI nunca inventa uno (contrato HUD §2/§8), y por eso
/// el nivel viaja calculado en el canal A igual que el semáforo del relevo viaja
/// en el canal D.
///
/// La escalera no es inventada: son los umbrales de escasez de
/// `ColonyController` (especificaciones §3), los mismos que deciden cuándo la
/// colonia reabsorbe huevos, se come larvas, raciona adultas y a qué ritmo pone.
/// El chip, por tanto, dice en qué tramo de ESA escalera está la colonia:
///
///   T_crit ─┬─ T_cann ─┬─ T_ooph ─┬─ T_safe ─┬─→
///           │          │          │          │
///        Crítico   Caníbal     Oofagia    Reserva   Desahogo
///
/// Especies (segundos): Lasius 40/25/15/8 · Atta 50/30/18/10 · Eciton 30/20/12/6.
/// </summary>
public static class RunwayVerdict
{
    /// <summary>Umbral de la especie por encima del cual la puesta va a tope.</summary>
    public static RunwayState StateOf(float runwaySeconds, in SpeciesDescriptor sp)
        => StateOf(runwaySeconds, sp.TCrit, sp.TCann, sp.TOoph, sp.TSafe);

    /// <summary>
    /// Tramo de la escalera para un runway en segundos. <c>NaN</c> o negativo
    /// (sin medida: el canal A emite −1) ⇒ <see cref="RunwayState.Unknown"/>.
    /// Los umbrales se leen de la ESPECIE, no de constantes globales: Atta y
    /// Eciton tienen economías más caras y su escalera se mueve con ellas.
    /// </summary>
    public static RunwayState StateOf(float runwaySeconds, float tCrit, float tCann, float tOoph, float tSafe)
    {
        if (float.IsNaN(runwaySeconds) || runwaySeconds < 0f) return RunwayState.Unknown;
        if (runwaySeconds < tCrit) return RunwayState.Critical;
        if (runwaySeconds < tCann) return RunwayState.Cannibalism;
        if (runwaySeconds < tOoph) return RunwayState.Oophagy;
        if (runwaySeconds < tSafe) return RunwayState.Reserve;
        return RunwayState.Surplus;
    }

    /// <summary>
    /// ¿Hay medida? El consumo es una EMA y <c>ColonyController.Runway</c> divide
    /// por <c>max(EMA, 1e-4)</c>: con una EMA degenerada (0 tras apagarse la
    /// ingesta, o NaN) esa división daría 10⁶ s y el chip diría «desahogo» con la
    /// colonia seca. Sin EMA no hay autonomía que medir. Un juego normal nunca
    /// cae aquí — la colonia nace con <c>ConsumeEma = 1</c>.
    /// </summary>
    public static bool HasMeasure(float consumeEma) => !(consumeEma <= 0f) && !float.IsNaN(consumeEma);
}
