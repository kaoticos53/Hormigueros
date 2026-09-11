using System;
using AntSim.Core.Scenario;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// Regla del semáforo de relevo (F4.3): el drop escala con el tamaño del
    /// mundo (190 × grid/96), el tramo es invariante (≥ 60 u). Los valores
    /// centinela salen del smoke real de 5 semillas (docs/fase4-hud-contrato §8).
    /// </summary>
    public sealed class RelayVerdictTests
    {
        [Fact]
        public void Umbrales_Calibrados()
        {
            Assert.Equal(60f, RelayVerdict.CarryLegMin);
            Assert.Equal(190f, RelayVerdict.DropMaxFor(96));   // mundo de calibración
            Assert.Equal(506.67f, RelayVerdict.DropMaxFor(256), 2); // 190 × 256/96
        }

        [Fact]
        public void DatosAusentes_Gris()
        {
            Assert.Equal(RelayLight.Grey, RelayVerdict.Evaluate(null, null, 256));
            Assert.Equal(RelayLight.Grey, RelayVerdict.Evaluate(80f, null, 256));
            Assert.Equal(RelayLight.Grey, RelayVerdict.Evaluate(null, 180f, 256));
        }

        [Fact]
        public void Mundo96_UmbralesOriginales()
        {
            // En el mundo de calibración la regla es la del contrato original.
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(80f, 167.9f, 96)); // warm-v2 sano
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(80f, 190.1f, 96)); // drop fuera
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(59.9f, 167.9f, 96)); // leg corto
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(60f, 190f, 96));   // fronteras OK
        }

        [Fact]
        public void Mundo256_DropEscala_LegInvariante()
        {
            // Centinelas reales del smoke (grid 256) — ver §8 del contrato HUD.
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(64.0f, 182.3f, 256));  // seed 42
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(49.3f, 131.6f, 256));  // seed 7: leg corto
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(85.0f, 190.3f, 256));  // seed 99: antes ámbar por drop fijo
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(174.0f, 240.0f, 256)); // seed 777: antes ámbar por drop fijo
            Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(71.0f, 177.0f, 256));  // seed 1234

            // El drop SÍ castiga en 256 cuando supera 190 × 256/96.
            Assert.Equal(RelayLight.Amber, RelayVerdict.Evaluate(80f, 506.7f, 256));
        }

        [Fact]
        public void GridInvalido_Lanza()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RelayVerdict.DropMaxFor(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RelayVerdict.DropMaxFor(-96));
        }

        // ————— F4.4: ratio de tramo normalizado —————

        [Fact]
        public void RatioDeTramo_ProximidadCompleta_EsVerde()
        {
            // El caso seed 7 (§8.1): item a 30.6 u del nido ⇒ cadena ~6.6 u y
            // leg 6.7 u. El leg ABSOLUTO (49.3 < 60) la ambarizaba; el RATIO
            // (≈1.0, cadena completa) la deja en verde.
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(6.7f, 6.6f, 131.6f, 256));
            Assert.True(RelayVerdict.LegRatio(6.7f, 6.6f) > 1f); // completó más que la recta
        }

        [Fact]
        public void RatioDeTramo_Bajo_EsAmbar_AunqueElLegSeaLargo()
        {
            // Un leg de 80 u con cadena de 200 u (40% < 55%): el eslabón NO se
            // completa — el leg absoluto lo habría dado por sano.
            Assert.Equal(RelayLight.Amber, RelayVerdict.EvaluateNormalized(80f, 200f, 300f, 256));
            // Frontera exacta: 55% pasa.
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(110f, 200f, 300f, 256));
        }

        [Fact]
        public void RatioConCadenaNula_EsRelevoPerfecto()
        {
            // Cadena 0 (pickup en el radio de descarga): nada por completar.
            Assert.Equal(1f, RelayVerdict.LegRatio(0f, 0f));
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(0f, 0f, 100f, 96));
        }

        [Fact]
        public void RatioSinCadena_Gris()
        {
            // Sin chainAvg (stream antiguo o colonia vacía) no hay veredicto ratio.
            Assert.Equal(RelayLight.Grey, RelayVerdict.EvaluateNormalized(64f, null, 182f, 256));
            Assert.Equal(0f, RelayVerdict.LegRatio(64f, null));
        }

        [Fact]
        public void SemillasDelSmoke_BajoReglaNormalizada()
        {
            // Re-evaluación F4.4 de las 5 semillas — legMean/chainMean REALES
            // computados de los dumps (pickup→nido − 24 por carga emparejada):
            // las 5 en verde (ratios 1.00-1.04: cada carga completa ~toda la
            // cadena disponible). La seed 7 pasa de ámbar (leg 49.3 < 60) a
            // verde — era cadena corta completada, no degradación.
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(64.4f, 61.8f, 182.3f, 256));   // 42
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(49.6f, 48.7f, 131.6f, 256));   // 7
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(85.9f, 85.6f, 190.3f, 256));   // 99
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(71.8f, 71.3f, 177.0f, 256));   // 1234
            Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(174.9f, 173.4f, 240.0f, 256)); // 777
        }
    }
}
