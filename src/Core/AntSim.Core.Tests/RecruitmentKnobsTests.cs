using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F8 — EL ESPEJO. Unity no referencia el Core (la vista solo conoce sus
    /// modelos puros), así que las perillas de montones/rastro existen dos veces:
    /// <see cref="RecruitmentTuning"/> (CLI y mundo) y <see cref="RecruitmentKnobs"/>
    /// (lanzador, presenter y línea de comandos del CLI).
    ///
    /// Estas pruebas son lo que impide que deriven: si alguien cambia un default
    /// o un rango en una sola de las dos tablas, la suite falla aquí y no en una
    /// partida donde el lanzador y el mundo ya discrepan.
    /// </summary>
    public class RecruitmentKnobsTests
    {
        [Fact]
        public void Espejo_MismosDefaults()
        {
            var core = RecruitmentTuning.Default;
            var vista = RecruitmentKnobs.Default;

            Assert.Equal(core.Clusters, vista.Clusters);
            Assert.Equal(core.ClusterMin, vista.ClusterMin);
            Assert.Equal(core.ClusterMax, vista.ClusterMax);
            Assert.Equal(core.ClusterRadius, vista.ClusterRadius);
            Assert.Equal(core.TrailDeposit, vista.TrailDeposit);
            Assert.Equal(core.TrailHalfLife, vista.TrailHalfLife);
            Assert.Equal(core.TrailFollow, vista.TrailFollow);
            // F11n: la puerta y su precio — el par que adopta el lanzador.
            Assert.Equal(core.TrailByBrain, vista.TrailByBrain);
            Assert.Equal(core.TrailGlandSeconds, vista.TrailGlandSeconds);
            Assert.Equal(core.TrailGlandRefillSeconds, vista.TrailGlandRefillSeconds);
            Assert.True(core.IsDefault);
            Assert.True(vista.IsDefault);
            Assert.Equal(core.Describe(), vista.Describe());
        }

        [Fact]
        public void Espejo_MismosRangos()
        {
            Assert.Equal(RecruitmentTuning.MaxClusters, RecruitmentKnobs.MaxClusters);
            Assert.Equal(RecruitmentTuning.MaxClusterItems, RecruitmentKnobs.MaxClusterItems);
            Assert.Equal(RecruitmentTuning.MaxClusterRadius, RecruitmentKnobs.MaxClusterRadius);
            Assert.Equal(RecruitmentTuning.MaxTrailDeposit, RecruitmentKnobs.MaxTrailDeposit);
            Assert.Equal(RecruitmentTuning.MaxTrailHalfLife, RecruitmentKnobs.MaxTrailHalfLife);
            Assert.Equal(RecruitmentTuning.MaxTrailFollow, RecruitmentKnobs.MaxTrailFollow);
            Assert.Equal(RecruitmentTuning.MaxTrailGland, RecruitmentKnobs.MaxTrailGland);
            Assert.Equal(RecruitmentTuning.MaxTrailGlandRefill, RecruitmentKnobs.MaxTrailGlandRefill);
        }

        [Fact]
        public void Espejo_RechazanLoMismoConElMismoMotivo()
        {
            var malos = new (RecruitmentTuning Core, RecruitmentKnobs Vista)[]
            {
                (new RecruitmentTuning { Clusters = 99 }, new RecruitmentKnobs { Clusters = 99 }),
                (new RecruitmentTuning { ClusterMin = 0 }, new RecruitmentKnobs { ClusterMin = 0 }),
                (new RecruitmentTuning { ClusterMin = 9, ClusterMax = 4 },
                 new RecruitmentKnobs { ClusterMin = 9, ClusterMax = 4 }),
                (new RecruitmentTuning { ClusterRadius = 0f }, new RecruitmentKnobs { ClusterRadius = 0f }),
                (new RecruitmentTuning { TrailDeposit = 9f }, new RecruitmentKnobs { TrailDeposit = 9f }),
                (new RecruitmentTuning { TrailHalfLife = 0f }, new RecruitmentKnobs { TrailHalfLife = 0f }),
                (new RecruitmentTuning { TrailFollow = -1f }, new RecruitmentKnobs { TrailFollow = -1f }),
                (new RecruitmentTuning { TrailGlandSeconds = 121f }, new RecruitmentKnobs { TrailGlandSeconds = 121f }),
                (new RecruitmentTuning { TrailGlandRefillSeconds = 0.01f },
                 new RecruitmentKnobs { TrailGlandRefillSeconds = 0.01f }),
            };

            foreach (var (core, vista) in malos)
            {
                Assert.False(core.TryValidate(out string coreError));
                Assert.False(vista.TryValidate(out string vistaError));
                Assert.Equal(coreError, vistaError);
            }
        }

        [Fact]
        public void AjustesDelLanzador_LleganConLosMismosNumerosAlCLI()
        {
            // El camino real: el archivo de ajustes → LaunchSettings → knobs con
            // los que se construye la línea de comandos. Y el mismo puñado de
            // valores, del otro lado, en la tabla que usa el mundo.
            var settings = LaunchSettings.Defaults();
            settings.ClusterCount = 2;
            settings.ClusterMin = 12;
            settings.ClusterMax = 16;
            settings.ClusterRadius = 30f;
            settings.TrailDeposit = 1.5f;
            settings.TrailHalfLife = 60f;
            settings.TrailFollow = 0f;

            var vista = settings.ToRecruitmentKnobs();
            var core = new RecruitmentTuning
            {
                Clusters = vista.Clusters,
                ClusterMin = vista.ClusterMin,
                ClusterMax = vista.ClusterMax,
                ClusterRadius = vista.ClusterRadius,
                TrailDeposit = vista.TrailDeposit,
                TrailHalfLife = vista.TrailHalfLife,
                TrailFollow = vista.TrailFollow,
            };

            Assert.False(vista.IsDefault);
            Assert.False(core.IsDefault);
            Assert.Equal(core.Describe(), vista.Describe());
            Assert.Equal("montones=2 (12–16 ítems, radio 30 u) · rastro=1.5× / 60 s / sigue 0", vista.Describe());
            Assert.Contains("montones=2", settings.Describe()); // el log del editor dice lo mismo
        }

        [Fact]
        public void ElParDeLaPuerta_DelLanzadorAlMundo_ConLosMismosNumeros()
        {
            // F11n: lo que el lanzador expone es el PAR —la red abre el depósito y
            // una glándula de 25 s con 6 s de recarga lo cobra— con el pool que lo
            // entrenó. Las dos tablas tienen que describirlo igual, cifra a cifra.
            var settings = LaunchSettings.Defaults();
            settings.Pool = "pretrain-partida-200-gl-red";
            settings.TrailByBrain = true;
            settings.TrailGlandSeconds = 25f;
            settings.TrailGlandRefillSeconds = 6f;

            var vista = settings.ToRecruitmentKnobs();
            var core = new RecruitmentTuning
            {
                Clusters = vista.Clusters,
                ClusterMin = vista.ClusterMin,
                ClusterMax = vista.ClusterMax,
                ClusterRadius = vista.ClusterRadius,
                TrailDeposit = vista.TrailDeposit,
                TrailHalfLife = vista.TrailHalfLife,
                TrailFollow = vista.TrailFollow,
                TrailByBrain = vista.TrailByBrain,
                TrailGlandSeconds = vista.TrailGlandSeconds,
                TrailGlandRefillSeconds = vista.TrailGlandRefillSeconds,
            };

            Assert.False(vista.IsDefault);   // el juego de fábrica queda fuera
            Assert.False(core.IsDefault);
            Assert.Equal(core.Describe(), vista.Describe());
            Assert.Equal(
                "montones=8 (6–10 ítems, radio 18 u) · rastro=1× / 2100 s / sigue 8" +
                " / puerta del cerebro / glándula 25 s (recarga 6 s en el nido)",
                vista.Describe());
            Assert.Contains("puerta del cerebro", settings.Describe());
            Assert.Contains("glándula 25 s", settings.Describe());
        }
    }
}
