using System;

namespace AntSim.Core.Scenario;

/// <summary>Semáforo de relevo de la tarjeta de colonia (contrato HUD §2).</summary>
public enum RelayLight : byte
{
    /// <summary>Sin descargas: el relevo no ha arrancado.</summary>
    Grey = 0,
    /// <summary>Descarga pero tramo corto o drops largos.</summary>
    Amber = 1,
    /// <summary>Relevo sano: tramo completo y sueltas cerca del nido.</summary>
    Green = 2,
}

/// <summary>
/// Regla del semáforo de relevo — LOS UMBRALES CALIBRADOS DEL PROYECTO, como
/// código (la UI nunca inventa uno; contrato HUD §2/§8).
///
/// Calibración: los umbrales nacen del benchmark Fase 3ter en grid 96
/// (warm-v2 sano: drop 167.9 u / tramo 80.0 u; límite de regresión de
/// pipeline.sh: drop +10% ⇒ 190, leg −20% ⇒ 60). El TODO F4.3 cierra con la
/// decisión: el drop escala con la distancia de forrajeo del mundo, el tramo
/// es una propiedad del portador. Así el semáforo calibrado en 96² vale en
/// 256² con UNA regla (drop máx = 190 × grid/96), verificada con el smoke de
/// 5 semillas (docs/fase4-hud-contrato.md §8).
/// </summary>
public static class RelayVerdict
{
    /// <summary>Tramo mínimo sano (u): invariante al tamaño del mundo.</summary>
    public const float CarryLegMin = 60f;

    /// <summary>Drop máximo sano en el mundo de calibración (grid 96): 190 u
    /// (límite de regresión drop +10% sobre warm-v2 sano = 167.9 u).</summary>
    public const float DropMaxGrid96 = 190f;

    /// <summary>Grid de calibración (el del benchmark Fase 3ter).</summary>
    public const int CalibrationGrid = 96;

    /// <summary>
    /// Drop máximo sano para un mundo de <paramref name="grid"/>: escala lineal
    /// con la distancia de forrajeo (grid/96). 96 ⇒ 190 · 256 ⇒ 506.67.
    /// </summary>
    public static float DropMaxFor(int grid)
    {
        if (grid <= 0) throw new ArgumentOutOfRangeException(nameof(grid));
        return DropMaxGrid96 * grid / (float)CalibrationGrid;
    }

    /// <summary>
    /// Semáforo para los datos de relevo de UNA colonia en un mundo de
    /// <paramref name="grid"/> (el del header del stream). Nulls ⇒ datos
    /// ausentes (colonia sin relevo observado) ⇒ gris.
    /// </summary>
    public static RelayLight Evaluate(float? carryLeg, float? dropAvg, int grid)
    {
        if (carryLeg is null || dropAvg is null) return RelayLight.Grey;
        if (carryLeg < CarryLegMin || dropAvg > DropMaxFor(grid))
            return RelayLight.Amber;
        return RelayLight.Green;
    }
}
