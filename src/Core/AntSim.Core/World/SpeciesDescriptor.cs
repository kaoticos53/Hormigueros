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
    public float CostMove = 0.0012f;       // ep por u recorrida
    public float CostDeposit = 0.02f;      // ep por unidad de depósito
    public float EnergyCapacity = 15f;     // ep base (modulada por vigor)
    public float AdultUpkeep = 0.012f;     // b_adult: ep/s alimentado del stock

    // — Cría —
    public float KFull = 2.0f;             // ep acumulados para pupa fuerte
    public float KMin = 0.8f;              // ep mínimos para pupa débil (minim)
    public float EggTime = 8f;             // s huevo → larva
    public float LarvaTimeMax = 25f;       // s tope de larva
    public float PupaTime = 12f;           // s pupa → adulta
    public float DeathRate = 1f / 240f;    // λ_death s⁻¹ (término de reposición)
    public float EggCost = 0.35f;          // ε_egg: ep por huevo (se descuenta del stock)
    // Vida útil realista: 720 s permite 6-12 viajes completos de forrajeo
    // (viaje medio 200-350 u a VMax 2.7 ≈ 75-130 s de pickup + vuelta).
    public float BaseLifespan = 720f;      // s (modulada por vigor)

    /// <summary>
    /// Tasa ideal de alimentación larval (b_ideal): la suficiente para que una
    /// larva bien alimentada alcance KFull dentro de LarvaTimeMax (con 10% de
    /// margen). Deriva de las constantes → nunca es internamente inconsistente.
    /// </summary>
    public float LarvaIdeal => (KFull / LarvaTimeMax) * 1.1f;

    // — Reservas y umbrales de escasez (runway en segundos) —
    public float StockMax = 150f;
    public float TSafe = 40f;
    public float TOoph = 25f;
    public float TCann = 15f;
    public float TCrit = 8f;

    // — F5.2a.2: hongo (solo Atta; 0 = la especie no procesa hongo) —
    public float FungusMax;       // ep de capacidad del hongo
    public float DigestionRate;   // ep/s a hongo LLENO (proporcional al llenado)
    public float LeafEfficiency;  // ep de hongo por ep de hoja descargada

    // — F5.2b.1: combate de incursión (solo Eciton; 0 = la especie no pelea) —
    public float ContactRadius;   // u: distancia de golpe (0 = especie pacífica)
    public float StrikeDamage;    // ep de CAPACIDAD de la presa por golpe
    public float StealPerStrike;  // ep que el atacante roba y transporta

    // — Ecosistema biológico realista: trofalaxia, ritmos circadianos y exploración —
    public float NestFeedRate = 0.5f;             // ep/s de recarga desde el stock del nido cuando E < 0.85
    public float TrophallaxisRate = 0.2f;         // ep/s transferidos en trofalaxia directa boca a boca
    public float TrophallaxisRadius = 8.0f;       // u: radio de interacción para trofalaxia directa
    public int DayNightPeriod = 1200;             // ticks por ciclo día/noche (1200 ticks = 40 s a 30 Hz)
    public float DaySpeedMultiplier = 1.20f;      // modulación de velocidad diurna
    public float NightMetabolismMultiplier = 0.70f; // reducción de metabolismo basal nocturno
    public float NoiseMagnitude = 0.02f;          // desviación estándar de ruido sensorial/motor

    // — Constantes predefinidas (tabla de la especificación) —
    public static readonly SpeciesDescriptor LasiusNiger = new() { Name = "Lasius niger" };

    /// <summary>
    /// Recupera una especie predefinida por su nombre (checkpoints .antsave).
    /// Un nombre desconocido es corrupción del archivo: se rechaza.
    /// </summary>
    public static SpeciesDescriptor ByName(string name)
    {
        if (name == LasiusNiger.Name) return LasiusNiger;
        if (name == Atta.Name) return Atta;
        if (name == Eciton.Name) return Eciton;
        throw new System.FormatException($"Especie desconocida en el checkpoint: '{name}'.");
    }

    public static readonly SpeciesDescriptor Atta = new()
    {
        Name = "Atta (cortadora)",
        VMax = 2.4f, OmegaMax = 1.8f,
        SensorReach = 30f, SenseAngle = 0.349f, VisionRadius = 80f,
        QMaxFood = 0.6f, QMaxHome = 0.6f, QMaxAlarm = 1.2f,
        CostMove = 0.0015f, CostDeposit = 0.015f,
        EnergyCapacity = 18f,
        AdultUpkeep = 0.015f,
        KFull = 2.8f, KMin = 1.0f,
        EggTime = 10f, LarvaTimeMax = 35f, PupaTime = 16f,
        DeathRate = 1f / 300f, EggCost = 0.5f, BaseLifespan = 900f,
        StockMax = 200f, TSafe = 50f, TOoph = 30f, TCann = 18f, TCrit = 10f,
        // — F5.2a.2: la cortadora procesa hoja en hongo —
        FungusMax = 80f,
        DigestionRate = 0.4f,
        LeafEfficiency = 0.75f
    };

    public static readonly SpeciesDescriptor Eciton = new()
    {
        Name = "Eciton (legionaria)",
        VMax = 3.6f, OmegaMax = 3.0f,
        SensorReach = 18f, SenseAngle = 0.262f, VisionRadius = 90f,
        QMaxFood = 0.4f, QMaxHome = 0.4f, QMaxAlarm = 2.4f,
        CostMove = 0.0018f, CostDeposit = 0.03f,
        EnergyCapacity = 15f,
        AdultUpkeep = 0.015f,
        KFull = 1.6f, KMin = 0.6f,
        EggTime = 5f, LarvaTimeMax = 18f, PupaTime = 8f,
        DeathRate = 1f / 200f, EggCost = 0.3f, BaseLifespan = 600f,
        StockMax = 150f, TSafe = 30f, TOoph = 20f, TCann = 12f, TCrit = 6f,
        ContactRadius = 40f, StrikeDamage = 2.5f, StealPerStrike = 5.0f
    };
}