namespace AntSim.Core.Contracts;

/// <summary>
/// Orden canónico de los 19 canales de <see cref="AntSensors"/> (contrato IBrain v1).
/// El orden es estable y versionado: ampliar el contrato añade canales al FINAL
/// y sube <see cref="IBrain.CurrentContractVersion"/>, nunca reordena.
/// </summary>
public enum AntSensorChannel : byte
{
    // — Olfato: intensidad central y diferencia lateral (izquierda − derecha) por químico —
    FoodTrailCenter = 0,   // cFood  [0,1]
    HomeTrailCenter = 1,   // cHome  [0,1]
    AlarmCenter = 2,       // cAlarm [0,1]
    FoodTrailDiff = 3,     // dFood  [−1,1]
    HomeTrailDiff = 4,     // dHome  [−1,1]
    AlarmDiff = 5,         // dAlarm [−1,1]
    // — Comida visible: vector al ítem más cercano en el marco local (X=rumbo) —
    FoodDx = 6,            // [−1,1]
    FoodDy = 7,            // [−1,1]
    FoodSize = 8,          // [0,1] fracción restante
    // — Brújula innata hacia la entrada del nido propio (integración de camino) —
    HomeDx = 9,            // [−1,1]
    HomeDy = 10,           // [−1,1]
    // — Antenas táctiles: proximidad de obstáculo —
    ProxLeft = 11,         // [0,1]
    ProxFront = 12,        // [0,1]
    ProxRight = 13,        // [0,1]
    // — Carga y estado interno —
    HasLoad = 14,          // {0,1}
    LoadFraction = 15,     // [0,1]
    Energy = 16,           // [0,1]
    AgeNormalized = 17,    // [0,1]
    // — Estado de la colonia —
    ColonyFoodRatio = 18   // [0,1] reserva S/Smax
}

public static class AntSensorChannelInfo
{
    /// <summary>Número total de canales del contrato v1.</summary>
    public const int Count = 19;
}
