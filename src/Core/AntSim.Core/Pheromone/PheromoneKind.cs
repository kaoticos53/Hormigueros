using System;

namespace AntSim.Core.Pheromone;

/// <summary>
/// Tipos de feromona por capa. Las capas son por colonia (una hormiga solo lee
/// las suyas). FoodTrail/Home/Alarm activas en Fase 1; Footprint (F5.3, CHC:
/// hidrocarburos cuticulares) es la señal NEGATIVA que faltaba — se deposita
/// pasivamente al caminar y REPELE, de modo que el sustrato ya peinado pierde
/// atractivo y el forrajeo se reparte por el espacio en vez de reforzar un solo
/// camino. Territory queda para fases posteriores.
/// </summary>
public enum PheromoneKind : byte
{
    FoodTrail = 0,   // reclutamiento hacia comida
    Home = 1,        // camino de vuelta al nido
    Alarm = 2,       // peligro (decae rápido)
    Territory = 3    // marcaje territorial (extensible, Fase 5+)
    , Footprint = 4  // CHC (F5.3): huella de tráfico, repelente y de vida larga
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
        // CHC (F5.3): la huella de tráfico es un registro LARGO — una zona muy
        // peinada debe seguir siendo poco atractiva cuando la colonia vuelva a
        // pasar, no evaporarse entre visitas (los hidrocarburos cuticulares son
        // estables durante minutos u horas).
        PheromoneKind.Footprint => 240f,
        _ => 10f
    };

    /// <summary>Constante de decaimiento exponencial: λ = ln 2 / τ½ (s⁻¹).</summary>
    public static float LambdaPerSecond(PheromoneKind kind) => LambdaFromHalfLife(HalfLifeSeconds(kind));

    public static float LambdaFromHalfLife(float halfLifeSeconds) => MathF.Log(2f) / halfLifeSeconds;
}
