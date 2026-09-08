namespace AntSim.Core.World;

/// <summary>
/// Parámetros naturales por especie. La especie cambia constantes FUERA del
/// cerebro (geometría de sensores, actuadores, costes, demografía): los 19
/// canales de sensores y las 6 salidas son comunes a todas las especies, lo
/// que hace portables los genomas (.antgenome) entre ellas.
/// </summary>
public sealed class SpeciesDescriptor
{
    public string Name = "Lasius niger";

    // — Actuadores —
    public float VMax = 2.7f;              // u/s
    public float OmegaMax = 2.2f;          // rad/s

    // — Sensores (geometría de la sonda) —
    public float SensorReach = 24f;        // u (rA, 3 celdas)
    public float SenseAngle = 0.314f;      // rad (±18°)
    public float VisionRadius = 60f;       // u (rV)

    // — Feromonas: tasas máximas de depósito (Qmax, u/s) —
    public float QMaxFood = 0.8f;
    public float QMaxHome = 0.8f;
    public float QMaxAlarm = 1.6f;

    // — Energía (ep = energy points) —
    public float CostMove = 0.002f;        // ep por u recorrida
    public float CostDeposit = 0.02f;      // ep por unidad de depósito
    public float EnergyCapacity = 10f;     // ep base (modulada por vigor)
    public float AdultUpkeep = 0.03f;      // b_adult: ep/s alimentado del stock

    // — Cría —
    public float KFull = 2.0f;             // ep acumulados para pupa fuerte
    public float KMin = 0.8f;              // ep mínimos para pupa débil (minim)
    public float EggTime = 8f;             // s huevo → larva
    public float LarvaTimeMax = 25f;       // s tope de larva
    public float PupaTime = 12f;           // s pupa → adulta
    public float DeathRate = 1f / 90f;     // λ_death s⁻¹ (término de reposición)
    public float EggCost = 0.5f;           // ε_egg: ep por huevo (se descuenta del stock)
    public float BaseLifespan = 90f;       // s (modulada por vigor)

    /// <summary>
    /// Tasa ideal de alimentación larval (b_ideal): la suficiente para que una
    /// larva bien alimentada alcance KFull dentro de LarvaTimeMax (con 10% de
    /// margen). Deriva de las constantes → nunca es internamente inconsistente.
    /// </summary>
    public float LarvaIdeal => (KFull / LarvaTimeMax) * 1.1f;

    // — Reservas y umbrales de escasez (runway en segundos) —
    public float StockMax = 100f;
    public float TSafe = 40f;
    public float TOoph = 25f;
    public float TCann = 15f;
    public float TCrit = 8f;

    // — Constantes predefinidas (tabla de la especificación) —
    public static readonly SpeciesDescriptor LasiusNiger = new() { Name = "Lasius niger" };

    public static readonly SpeciesDescriptor Atta = new()
    {
        Name = "Atta (cortadora)",
        VMax = 2.4f, OmegaMax = 1.8f,
        SensorReach = 30f, SenseAngle = 0.349f, VisionRadius = 80f,
        QMaxFood = 0.6f, QMaxHome = 0.6f, QMaxAlarm = 1.2f,
        CostMove = 0.0024f, CostDeposit = 0.015f,
        AdultUpkeep = 0.035f,
        KFull = 2.8f, KMin = 1.0f,
        EggTime = 10f, LarvaTimeMax = 35f, PupaTime = 16f,
        DeathRate = 1f / 140f, EggCost = 0.7f, BaseLifespan = 140f,
        StockMax = 120f, TSafe = 50f, TOoph = 30f, TCann = 18f, TCrit = 10f
    };

    public static readonly SpeciesDescriptor Eciton = new()
    {
        Name = "Eciton (legionaria)",
        VMax = 3.6f, OmegaMax = 3.0f,
        SensorReach = 18f, SenseAngle = 0.262f, VisionRadius = 90f,
        QMaxFood = 0.4f, QMaxHome = 0.4f, QMaxAlarm = 2.4f,
        CostMove = 0.0028f, CostDeposit = 0.03f,
        AdultUpkeep = 0.045f,
        KFull = 1.6f, KMin = 0.6f,
        EggTime = 5f, LarvaTimeMax = 18f, PupaTime = 8f,
        DeathRate = 1f / 60f, EggCost = 0.4f, BaseLifespan = 60f,
        StockMax = 80f, TSafe = 30f, TOoph = 20f, TCann = 12f, TCrit = 6f
    };
}