using System.Globalization;

namespace AntSim.Core.World;

/// <summary>
/// F8 — LAS PERILLAS DEL RECLUTAMIENTO en un solo sitio: cuántos montones de
/// comida siembra el mundo, de qué tamaño, y qué rastro deja la hormiga cargada
/// (fuerza, vida media y cuánto lo siguen sus compañeras).
///
/// POR QUÉ EXISTE: comparar configuraciones de reclutamiento era tocar código
/// (los valores vivían como <c>get; set;</c> de <see cref="WorldSim"/> con el
/// default escrito en la propiedad), así que un experimento A/B obligaba a
/// recompilar tres capas —CLI, lanzador y mundo— y a deshacerlo después. Aquí
/// van juntos, con los MISMOS defaults que el mundo tenía cableados: sin
/// banderas, la partida es byte a byte la de siempre.
///
/// No conoce Unity ni la línea de comandos: la parsea el CLI
/// (<c>--clusters</c>, <c>--cluster-size</c>, <c>--trail-deposit</c>…) y el
/// lanzador (<c>clusters=</c>, <c>clusterSize=</c>… en el archivo de ajustes),
/// y ambos validan con <see cref="TryValidate"/> para no aceptar un montón de
/// 0 ítems ni un rastro que no se borra nunca.
/// </summary>
public sealed class RecruitmentTuning
{
    /// <summary>Montones que se siembran ADEMÁS de la comida aleatoria (0 = ninguno).</summary>
    public int Clusters { get; set; } = 8;

    /// <summary>Ítems por montón (se sortea un tamaño en [Min, Max]).</summary>
    public int ClusterMin { get; set; } = 6;
    public int ClusterMax { get; set; } = 10;

    /// <summary>Radio del montón en unidades de mundo (u).</summary>
    public float ClusterRadius { get; set; } = 18f;

    /// <summary>Rastro de comida que deja la hormiga cargada, como fracción de
    /// QMaxFood por segundo (0 = no deja rastro: control A/B).</summary>
    public float TrailDeposit { get; set; } = 1f;

    /// <summary>Vida media (s) del rastro de comida. 2 100 s = 7× la clásica
    /// (300 s): el camino sobrevive entre visita y visita en vez de borrarse.</summary>
    public float TrailHalfLife { get; set; } = 2100f;

    /// <summary>Ganancia del reflejo que sigue el rastro (rad/s a plena
    /// asimetría; 0 = lo huele pero no lo sigue).</summary>
    public float TrailFollow { get; set; } = 8f;

    /// <summary>F11 — ¿el depósito del reflejo lo abre el CEREBRO? false (default)
    /// es el rastro fijo de F8: la cargada deja su rastro y la que busca el suyo,
    /// siempre y con la misma fuerza. true lo escala por las salidas
    /// <c>DepositFood</c>/<c>DepositHome</c>: la hormiga decide si deja rastro y
    /// cuánto. Control A/B del experimento: mismo mundo, misma tirada, una
    /// constante en vez de una salida.</summary>
    public bool TrailByBrain { get; set; }

    /// <summary>F11b — PRECIO DEL RASTRO: segundos de depósito a plena tasa que
    /// caben en la glándula de una hormiga (0 = gratis, el default de F8). Con
    /// precio la reserva se gasta al depositar y solo se rellena en el nido, así
    /// que el cerebro tiene algo que decidir de verdad: dónde y cuánto gastarla.
    /// Con 0 el mundo no cambia ni un byte.</summary>
    public float TrailGlandSeconds { get; set; }

    /// <summary>F11b — segundos que tarda la glándula en llenarse dentro del nido.</summary>
    public float TrailGlandRefillSeconds { get; set; } = 3f;

    public const int MaxClusters = 64;
    public const int MaxClusterItems = 40;
    public const float MaxClusterRadius = 120f;
    public const float MaxTrailDeposit = 5f;
    public const float MaxTrailHalfLife = 3600f;
    public const float MaxTrailFollow = 20f;
    public const float MaxTrailGland = 120f;
    public const float MaxTrailGlandRefill = 60f;

    public static RecruitmentTuning Default => new RecruitmentTuning();

    /// <summary>Los MISMOS valores que trae el mundo de fábrica (para comparar
    /// contra el default sin repetir números por el repo).</summary>
    public bool IsDefault =>
        Clusters == 8 && ClusterMin == 6 && ClusterMax == 10
        && ClusterRadius == 18f && TrailDeposit == 1f
        && TrailHalfLife == 2100f && TrailFollow == 8f && !TrailByBrain
        && TrailGlandSeconds == 0f && TrailGlandRefillSeconds == 3f;

    /// <summary>Copia los valores al mundo. El mundo los necesita ANTES de
    /// sembrar (el constructor crea los montones), así que se llama desde su
    /// constructor y no después: asignarlos al terminar no cambiaría nada.</summary>
    public void ApplyTo(WorldSim sim)
    {
        sim.FoodClusterCount = Clusters;
        sim.FoodClusterMin = ClusterMin;
        sim.FoodClusterMax = ClusterMax;
        sim.FoodClusterRadius = ClusterRadius;
        sim.RecruitmentFoodDeposit = TrailDeposit;
        sim.RecruitmentFoodHalfLife = TrailHalfLife;
        sim.FoodTrailAttract = TrailFollow;
        sim.RecruitmentTrailByBrain = TrailByBrain;
        sim.RecruitmentGlandSeconds = TrailGlandSeconds;
        sim.RecruitmentGlandRefillSeconds = TrailGlandRefillSeconds;
    }

    /// <summary>Reglas de rango. Devuelve false con el motivo en
    /// <paramref name="error"/> — el CLI y el lanzador lo muestran tal cual.</summary>
    public bool TryValidate(out string error)
    {
        error = "";
        if (Clusters < 0 || Clusters > MaxClusters)
        { error = $"clusters fuera de rango [0, {MaxClusters}]: {Clusters}"; return false; }
        if (ClusterMin < 1 || ClusterMin > MaxClusterItems)
        { error = $"clusterSize mínimo fuera de rango [1, {MaxClusterItems}]: {ClusterMin}"; return false; }
        if (ClusterMax < ClusterMin || ClusterMax > MaxClusterItems)
        {
            error = $"clusterSize máximo fuera de rango [{ClusterMin}, {MaxClusterItems}]: {ClusterMax}";
            return false;
        }
        if (ClusterRadius < 1f || ClusterRadius > MaxClusterRadius)
        { error = $"clusterRadius fuera de rango [1, {F(MaxClusterRadius)}] u: {F(ClusterRadius)}"; return false; }
        if (TrailDeposit < 0f || TrailDeposit > MaxTrailDeposit)
        { error = $"trailDeposit fuera de rango [0, {F(MaxTrailDeposit)}] × QMaxFood: {F(TrailDeposit)}"; return false; }
        if (TrailHalfLife < 1f || TrailHalfLife > MaxTrailHalfLife)
        { error = $"trailHalfLife fuera de rango [1, {F(MaxTrailHalfLife)}] s: {F(TrailHalfLife)}"; return false; }
        if (TrailFollow < 0f || TrailFollow > MaxTrailFollow)
        { error = $"trailFollow fuera de rango [0, {F(MaxTrailFollow)}] rad/s: {F(TrailFollow)}"; return false; }
        if (TrailGlandSeconds < 0f || TrailGlandSeconds > MaxTrailGland)
        { error = $"trailGland fuera de rango [0, {F(MaxTrailGland)}] s (0 = sin precio): {F(TrailGlandSeconds)}"; return false; }
        if (TrailGlandRefillSeconds < 0.05f || TrailGlandRefillSeconds > MaxTrailGlandRefill)
        { error = $"trailGlandRefill fuera de rango [0.05, {F(MaxTrailGlandRefill)}] s: {F(TrailGlandRefillSeconds)}"; return false; }
        return true;
    }

    /// <summary>Línea compacta para el HUD/log del editor y para el lanzador.
    /// Se dice TAL CUAL lo que se aplica, sin adjetivos: quien compara dos
    /// configuraciones necesita los números, no una frase.</summary>
    public string Describe() =>
        $"montones={Clusters} ({ClusterMin}–{ClusterMax} ítems, radio {F(ClusterRadius)} u) · " +
        $"rastro={F(TrailDeposit)}× / {F(TrailHalfLife)} s / sigue {F(TrailFollow)}" +
        (TrailByBrain ? " / puerta del cerebro" : "") +
        (TrailGlandSeconds > 0f
            ? $" / glándula {F(TrailGlandSeconds)} s (recarga {F(TrailGlandRefillSeconds)} s en el nido)"
            : "");

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
