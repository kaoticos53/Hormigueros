namespace AntSim.Core.World;

/// <summary>
/// Hormiga adulta. Los parámetros físicos (capacidad de energía, esperanza de
/// vida, velocidad) están modulados por el vigor con el que eclosionó — el
/// puente entre la nutrición larvaria y el individuo.
/// </summary>
public sealed class Ant
{
    public uint Id;
    public int ColonyId;

    // Pose
    public float X;
    public float Y;
    public float Heading;

    // Estado interno
    public float Energy;            // [0,1] normalizada por EnergyCapacity
    public float EnergyCapacity;    // ep = base * (0.25 + 0.75·vigor)
    public float Age;               // s
    public float Lifespan;          // s = base * (0.7 + 0.6·vigor)

    // Moduladores por vigor
    public float Vigor;
    public float SpeedScale;        // 0.85 + 0.30·vigor
    public float SensorScale;       // 0.9 + 0.2·vigor

    // Carga
    public bool HasLoad;
    public float LoadValue;         // ep del ítem transportado

    public bool Alive = true;
    public float InteractCooldown;  // s

    public void InitFromVigor(float baseCapacity, float baseLifespan, float vigor)
    {
        Vigor = vigor;
        EnergyCapacity = baseCapacity * (0.25f + 0.75f * vigor);
        Lifespan = baseLifespan * (0.7f + 0.6f * vigor);
        SpeedScale = 0.85f + 0.30f * vigor;
        SensorScale = 0.9f + 0.2f * vigor;
        Energy = 1f; // nace con la reserva completa
        Age = 0f;
    }
}