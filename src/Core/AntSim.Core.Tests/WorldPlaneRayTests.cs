using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.2 — el mapeo «rayo → plano del mundo» que usan los DOS handlers de click
    /// (seleccionar hormiga y marcar drop). Vive aparte precisamente para poder
    /// fijarlo aquí: la geometría del click ya no depende de que un humano acierte
    /// a pulsar el ratón en el sitio correcto.
    ///
    /// Lo que se fija: el caso normal, y los dos degenerados que deben NO producir
    /// selección ni drop (rayo paralelo al plano y plano detrás del origen).
    /// </summary>
    public sealed class WorldPlaneRayTests
    {
        private const float Eps = 1e-4f;

        [Fact]
        public void RayoVertical_MirandoAlSuelo_ApuntaAlPieDeLaCamara()
        {
            // Cámara sobre (100, 40, 250) mirando recto hacia abajo: el impacto
            // cae exactamente bajo ella.
            bool hit = WorldPlaneRay.TryHit(
                100f, 40f, 250f,
                0f, -1f, 0f,
                0f, out float x, out float z);

            Assert.True(hit);
            Assert.Equal(100f, x, Eps);
            Assert.Equal(250f, z, Eps);
        }

        [Fact]
        public void RayoInclinado_ElPuntoCaeDondeLaRectaCruzaElPlano()
        {
            // Origen (0, 30, 0), dirección (1, -1, 0) normalizada por componente:
            // t = 30 ⇒ impacto en (30, 0). Es el caso del click en perspectiva.
            bool hit = WorldPlaneRay.TryHit(
                0f, 30f, 0f,
                1f, -1f, 0f,
                0f, out float x, out float z);

            Assert.True(hit);
            Assert.Equal(30f, x, Eps);
            Assert.Equal(0f, z, Eps);
        }

        [Fact]
        public void PlanoDistintoDeCero_UsaElPlanoQueSeLePasa()
        {
            // El bootstrapper puede mover el plano del mundo: la altura la manda
            // el llamante, no una constante escondida aquí.
            bool hit = WorldPlaneRay.TryHit(
                0f, 50f, 0f,
                0f, -2f, 0f,
                10f, out float x, out float z);

            Assert.True(hit);
            Assert.Equal(0f, x, Eps);
            Assert.Equal(0f, z, Eps);
        }

        [Fact]
        public void RayoParaleloAlPlano_NoHayImpacto()
        {
            // dirY == 0: el rayo nunca corta el suelo. Sin esta guarda, t sería
            // infinito y el click marcaría un drop a kilómetros.
            bool hit = WorldPlaneRay.TryHit(
                5f, 5f, 5f,
                1f, 0f, 0f,
                0f, out float x, out float z);

            Assert.False(hit);
            Assert.Equal(0f, x);
            Assert.Equal(0f, z);
        }

        [Fact]
        public void PlanoDetrasDelOrigen_NoHayImpacto()
        {
            // Origen por ENCIMA del suelo mirando hacia ARRIBA (dirY > 0): el rayo
            // se aleja del plano, t < 0, y un click así no debe seleccionar ni
            // marcar nada. (Al revés —origen bajo el suelo mirando arriba— el
            // plano SÍ está delante y el impacto es legítimo: ver el test
            // siguiente.)
            bool hit = WorldPlaneRay.TryHit(
                0f, 10f, 0f,
                0f, 1f, 0f,
                0f, out float x, out float z);

            Assert.False(hit);
            Assert.Equal(0f, x);
            Assert.Equal(0f, z);
        }

        [Fact]
        public void OrigenBajoElPlanoMirandoArriba_SiHayImpacto()
        {
            // El plano está DELANTE: t = 10 y el impacto es correcto. Se fija
            // porque es el caso que un «detrás del origen» mal razonado se
            // llevaría por delante.
            bool hit = WorldPlaneRay.TryHit(
                0f, -10f, 0f,
                0f, 1f, 0f,
                0f, out float x, out float z);

            Assert.True(hit);
            Assert.Equal(0f, x, Eps);
            Assert.Equal(0f, z, Eps);
        }

        [Fact]
        public void OrigenEnElPlano_ImpactoEnElOrigen()
        {
            // t = 0 es un impacto válido (borde del caso «detrás»): el click a
            // ras de suelo sigue contando.
            bool hit = WorldPlaneRay.TryHit(
                7f, 0f, 9f,
                0f, -1f, 0f,
                0f, out float x, out float z);

            Assert.True(hit);
            Assert.Equal(7f, x, Eps);
            Assert.Equal(9f, z, Eps);
        }
    }
}
