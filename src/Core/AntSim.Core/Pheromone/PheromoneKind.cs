using System;

namespace AntSim.Core.Pheromone;

/// <summary>
/// Tipos de feromona por capa. Las capas son por colonia (una hormiga solo lee
/// las suyas). FoodTrail/Home/Alarm activas en Fase 1; Territory en fases posteriores.
/// </summary>
public enum PheromoneKind : byte
{
    FoodTrail = 0,   // reclutamiento hacia comida
    Home = 1,        // camino de vuelta al nido
    Alarm = 2,       // peligro (decae rápido)
    Territory = 3    // marcaje territorial (extensible, Fase 5+)
}

public static class PheromoneDefaults
{
    /// <summary>Vida media (τ½) por defecto en segundos, por tipo.</summary>
    public static float HalfLifeSeconds(PheromoneKind kind) => kind switch
    {
        PheromoneKind.FoodTrail => 10f,
        PheromoneKind.Home => 20f,
        PheromoneKind.Alarm => 1f,
        PheromoneKind.Territory => 60f,
        _ => 10f
    };

    /// <summary>Constante de decaimiento exponencial: λ = ln 2 / τ½ (s⁻¹).</summary>
    public static float LambdaPerSecond(PheromoneKind kind) => LambdaFromHalfLife(HalfLifeSeconds(kind));

    public static float LambdaFromHalfLife(float halfLifeSeconds) => MathF.Log(2f) / halfLifeSeconds;
}
