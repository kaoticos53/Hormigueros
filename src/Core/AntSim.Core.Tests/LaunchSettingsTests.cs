using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.3 — configuración de lanzamiento (lo que play-game.ps1/.bat inyectan
    /// en la escena antes de Play). El contrato: el texto que escribe el
    /// lanzador se parsea, se valida contra los rangos del mundo/CLI y se
    /// describe para el log del editor.
    /// </summary>
    public class LaunchSettingsTests
    {
        [Fact]
        public void Defaults_CoincidenConElBootstrapper()
        {
            var s = LaunchSettings.Defaults();
            Assert.Equal(96, s.Grid);
            Assert.Equal(2, s.Colonies);
            Assert.Equal(36000, s.Ticks);
            Assert.Equal("lasius", s.Species);
            Assert.Equal(3f, s.Speed);
            Assert.Equal(1, s.FrameEvery);
            Assert.True(s.TryValidate(out var err), err);
        }

        [Fact]
        public void Parse_TextoDelLanzador_AplicaTodo()
        {
            string text =
                "# lanzado por play-game.ps1\n" +
                "pool=pretrain-neat\n" +
                "poolPath=C:\\repo\\artifacts\\pretrain-neat.antgenome\n" +
                "grid=256\n" +
                "colonies=1\n" +
                "ticks=12000\n" +
                "species=lasius,eciton\n" +
                "speed=2.5\n" +
                "frameEvery=2\n";

            Assert.True(LaunchSettings.TryParse(text, out var s, out var err), err);
            Assert.Equal("pretrain-neat", s.Pool);
            Assert.Equal(@"C:\repo\artifacts\pretrain-neat.antgenome", s.PoolPath);
            Assert.Equal(256, s.Grid);
            Assert.Equal(1, s.Colonies);
            Assert.Equal(12000, s.Ticks);
            Assert.Equal("lasius,eciton", s.Species);
            Assert.Equal(2.5f, s.Speed);
            Assert.Equal(2, s.FrameEvery);
        }

        [Fact]
        public void Roundtrip_SerializeParse_EsIdentidad()
        {
            var original = new LaunchSettings
            {
                Pool = "hybrid",
                PoolPath = "/tmp/hybrid.antgenome",
                Grid = 128,
                Colonies = 2,
                Ticks = 7200,
                Species = "atta",
                Speed = 4.5f,
                FrameEvery = 3
            };

            Assert.True(LaunchSettings.TryParse(original.Serialize(), out var back, out var err), err);
            Assert.Equal(original.Pool, back.Pool);
            Assert.Equal(original.PoolPath, back.PoolPath);
            Assert.Equal(original.Grid, back.Grid);
            Assert.Equal(original.Colonies, back.Colonies);
            Assert.Equal(original.Ticks, back.Ticks);
            Assert.Equal(original.Species, back.Species);
            Assert.Equal(original.Speed, back.Speed);
            Assert.Equal(original.FrameEvery, back.FrameEvery);
        }

        [Fact]
        public void Parse_ClaveDesconocidaYEspacios_SeIgnora()
        {
            string text = "  grid = 96 \r\nfuturo=loquesea\n\n";
            Assert.True(LaunchSettings.TryParse(text, out var s, out var err), err);
            Assert.Equal(96, s.Grid);
        }

        [Theory]
        [InlineData("grid=8", "grid fuera de rango")]
        [InlineData("grid=4096", "grid fuera de rango")]
        [InlineData("ticks=1", "ticks fuera de rango")]
        [InlineData("speed=0", "velocidad fuera de rango")]
        [InlineData("speed=500", "velocidad fuera de rango")]
        [InlineData("colonies=3", "colonies fuera de rango")]
        [InlineData("frameEvery=0", "frameEvery fuera de rango")]
        [InlineData("species=", "especie inválida")]
        [InlineData("species=lasius,", "especie inválida")]
        [InlineData("species=lasius;eciton", "especie inválida")]
        public void Parse_ValoresInvalidos_RechazadosConMotivo(string line, string motivo)
        {
            Assert.False(LaunchSettings.TryParse(line, out _, out var err));
            Assert.Contains(motivo, err);
        }

        [Theory]
        [InlineData("grid")]
        [InlineData("grid=abc")]
        [InlineData("speed=muyrapido")]
        public void Parse_ValoresNoNumericos_Rechazados(string line)
        {
            Assert.False(LaunchSettings.TryParse(line, out _, out var err));
            Assert.NotEqual("", err);
        }

        [Fact]
        public void Describe_NombraElPoolResueltoYLasUnidades()
        {
            var s = LaunchSettings.Defaults();
            s.PoolPath = "C:\\repo\\artifacts\\pretrain-warm-v2.antgenome";
            s.Grid = 96;

            string d = s.Describe();
            Assert.Contains("pretrain-warm-v2.antgenome", d);
            Assert.Contains("768 u", d);      // grid 96 × 8 u
            Assert.Contains("lasius", d);

            var sinPool = LaunchSettings.Defaults();
            sinPool.Pool = "";
            Assert.Contains("(sin sembrar)", sinPool.Describe());

            // El default del proyecto siembra warm-v2 (mismo camino que el menú).
            Assert.Equal("pretrain-warm-v2", LaunchSettings.Defaults().Pool);
        }
    }
}
