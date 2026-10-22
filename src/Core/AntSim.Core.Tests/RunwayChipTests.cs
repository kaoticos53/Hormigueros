using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — chip de estado por runway: el MODELO PURO que Unity pinta (glifo,
    /// rótulo, color, texto y forma corta). La regla dura del contrato es que el
    /// chip no decide nada — el tramo llega calculado del Core en el canal A
    /// (RunwayVerdictTests cubre esa mitad) y aquí solo se comprueba la
    /// presentación, incluida la degradación con streams viejos.
    /// </summary>
    public sealed class RunwayChipTests
    {
        [Theory]
        [InlineData((byte)0, "", "", (byte)0)]
        [InlineData((byte)1, "[!!!]", "CRÍTICO", (byte)1)]
        [InlineData((byte)2, "[!!]", "CANÍBAL", (byte)2)]
        [InlineData((byte)3, "[!]", "OOFAGIA", (byte)3)]
        [InlineData((byte)4, "[+]", "RESERVA", (byte)4)]
        [InlineData((byte)5, "[++]", "DESAHOGO", (byte)5)]
        public void Tabla_GlifoRotuloYColor(byte estado, string glifo, string rotulo, byte color)
        {
            Assert.Equal(glifo, RunwayChipModel.Glyph(estado));
            Assert.Equal(rotulo, RunwayChipModel.Label(estado));
            Assert.Equal(color, RunwayChipModel.ColorIndex(estado));
        }

        [Fact]
        public void SinMedida_NoHayChip()
        {
            // Estado 0 = sin medida: cadena vacía, NO un chip en gris. El contrato
            // pide chip ausente para no dejar un hueco en la línea del título.
            Assert.Equal("", RunwayChipModel.Text(0, 22.5f));
            Assert.Equal("", RunwayChipModel.Short(0, 22.5f));
            Assert.Equal("", RunwayChipModel.Glyph(0));
        }

        [Theory]
        [InlineData((byte)1, 7.5f, "[!!!] CRÍTICO 7.5s")]
        [InlineData((byte)2, 12f, "[!!] CANÍBAL 12s")]
        [InlineData((byte)3, 22.5f, "[!] OOFAGIA 22.5s")]
        [InlineData((byte)4, 30f, "[+] RESERVA 30s")]
        [InlineData((byte)5, 150f, "[++] DESAHOGO 150s")]
        public void Texto_ConSegundos(byte estado, float runway, string esperado)
        {
            Assert.Equal(esperado, RunwayChipModel.Text(estado, runway));
        }

        [Fact]
        public void Texto_SinSegundos_CuandoNoHayMedida()
        {
            // Tramo con dato pero runway ausente (stream a medias): el rótulo se
            // pinta igual — es información útil — y no se inventa un «-1s».
            Assert.Equal("[!] OOFAGIA", RunwayChipModel.Text(3, -1f));
            Assert.Equal("[!] OOFAGIA", RunwayChipModel.Text(3, float.NaN));
            Assert.Equal("[!]", RunwayChipModel.Short(3, -1f));
        }

        [Theory]
        [InlineData((byte)1, 7.5f, "[!!!]7.5s")]
        [InlineData((byte)3, 22.5f, "[!]22.5s")]
        [InlineData((byte)5, 150f, "[++]150s")]
        public void Corto_ParaElMultiVisor(byte estado, float runway, string esperado)
        {
            // En una vista de 1/4 de pantalla no cabe el rótulo: glifo + segundos,
            // que es lo que dice A QUIÉN le queda menos aire.
            Assert.Equal(esperado, RunwayChipModel.Short(estado, runway));
        }

        [Theory]
        [InlineData(0f, "0s")]
        [InlineData(22f, "22s")]
        [InlineData(22.5f, "22.5s")]
        [InlineData(22.44f, "22.4s")]
        [InlineData(150f, "150s")]
        public void Segundos_UnaDecimalSoloSiAporta(float runway, string esperado)
        {
            Assert.Equal(esperado, RunwayChipModel.Seconds(runway));
        }

        [Fact]
        public void Segundos_Vacio_SinMedida()
        {
            Assert.Equal("", RunwayChipModel.Seconds(-1f));
            Assert.Equal("", RunwayChipModel.Seconds(float.NaN));
        }

        [Fact]
        public void RichText_LlevaElColorDelTramo()
        {
            // La tarjeta pinta el chip con rich text de uGUI (ya usa <b> en el
            // título): el color va dentro del modelo para verificarlo headless.
            Assert.Equal("<color=#E03A3A>[!!!] CRÍTICO 7.5s</color>",
                RunwayChipModel.RichText(1, 7.5f));
            Assert.Equal("<color=#4CAF50>[++] DESAHOGO 150s</color>",
                RunwayChipModel.RichText(5, 150f));
            Assert.Equal("<color=#D8B02A>[!]22.5s</color>", RunwayChipModel.RichShort(3, 22.5f));

            // Sin chip no se emite etiqueta suelta (dejaría una línea con basura).
            Assert.Equal("", RunwayChipModel.RichText(0, 22.5f));
            Assert.Equal("", RunwayChipModel.RichShort(0, 22.5f));
            Assert.Equal("", RunwayChipModel.RichText(200, 22.5f));

            // La paleta es total y distinta por tramo (severidad legible de un
            // vistazo, del rojo del crítico al verde del desahogo).
            var vistos = new System.Collections.Generic.HashSet<string>();
            for (byte st = 1; st <= 5; st++) vistos.Add(RunwayChipModel.ColorHex(st));
            Assert.Equal(5, vistos.Count);
        }

        [Fact]
        public void Defensivo_EstadoFueraDeRango()
        {
            // Un stream de otra versión con el byte a otra cosa no puede pintar un
            // glifo inventado: se degrada a «sin chip».
            Assert.Equal("", RunwayChipModel.Glyph(200));
            Assert.Equal("", RunwayChipModel.Label(200));
            Assert.Equal("", RunwayChipModel.Text(200, 22.5f));
            Assert.Equal((byte)0, RunwayChipModel.ColorIndex(200));
        }
    }
}
