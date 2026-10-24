using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F8 — montones de comida y rastro de feromonas, tal como los ve la VISTA:
    /// lo que el lanzador escribe en el archivo de ajustes, lo que el presenter
    /// manda al CLI y lo que el HUD puede describir con números.
    ///
    /// ESPEJO DELIBERADO de <c>AntSim.Core.World.RecruitmentTuning</c>. Unity no
    /// referencia el Core (no hay asmdef ni plugin: la vista solo conoce sus
    /// modelos puros), así que estos siete valores existen en los dos lados. Lo
    /// que impide que deriven NO es la disciplina sino una prueba
    /// (<c>RecruitmentKnobsTests</c>): compara defaults y rangos de las dos
    /// tablas, y si alguien toca una sola, la suite falla.
    ///
    /// Modelo PURO (sin UnityEngine): se compila también en la suite headless.
    /// </summary>
    public sealed class RecruitmentKnobs
    {
        /// <summary>Montones de comida sembrados además de los ítems sueltos (0 = ninguno).</summary>
        public int Clusters = 8;

        /// <summary>Ítems por montón: el tamaño se sortea en [ClusterMin, ClusterMax].</summary>
        public int ClusterMin = 6;
        public int ClusterMax = 10;

        /// <summary>Radio del montón en unidades de mundo.</summary>
        public float ClusterRadius = 18f;

        /// <summary>Rastro que deja la hormiga cargada (fracción de QMaxFood por segundo).</summary>
        public float TrailDeposit = 1f;

        /// <summary>Vida media (s) del rastro de comida (7× la clásica: el camino
        /// sobrevive entre visita y visita).</summary>
        public float TrailHalfLife = 2100f;

        /// <summary>Ganancia del reflejo que sigue el rastro (rad/s a plena asimetría).</summary>
        public float TrailFollow = 8f;

        // Los mismos rangos que RecruitmentTuning (lo fija la prueba de espejo).
        public const int MaxClusters = 64;
        public const int MaxClusterItems = 40;
        public const float MaxClusterRadius = 120f;
        public const float MaxTrailDeposit = 5f;
        public const float MaxTrailHalfLife = 3600f;
        public const float MaxTrailFollow = 20f;

        public static RecruitmentKnobs Default => new RecruitmentKnobs();

        /// <summary>¿Son los valores de fábrica? Con true la línea del CLI no lleva
        /// ninguna bandera de reclutamiento y el stream queda el de siempre.</summary>
        public bool IsDefault =>
            Clusters == 8 && ClusterMin == 6 && ClusterMax == 10
            && ClusterRadius == 18f && TrailDeposit == 1f
            && TrailHalfLife == 2100f && TrailFollow == 8f;

        /// <summary>Reglas de rango, con el mismo texto que el Core y el CLI.</summary>
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
            return true;
        }

        /// <summary>Línea compacta con los números que se van a aplicar.</summary>
        public string Describe() =>
            $"montones={Clusters} ({ClusterMin}–{ClusterMax} ítems, radio {F(ClusterRadius)} u) · " +
            $"rastro={F(TrailDeposit)}× / {F(TrailHalfLife)} s / sigue {F(TrailFollow)}";

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
