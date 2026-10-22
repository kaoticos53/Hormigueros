using System;
using System.Collections.Generic;
using System.IO;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// EL PASO DEL CANÓNICO DEL GENERADOR (AssetCanonicalizer), sin repo y sin
    /// Python: lo que se prueba es la DECISIÓN —qué intérprete, qué argumentos y
    /// qué se hace con el rc y la salida del canonicalizador—, que es lo que
    /// puede equivocarse en silencio.
    ///
    /// POR QUÉ IMPORTA QUE ESTÉ AQUÍ. El veredicto decide si el generador ROMPE
    /// (un asset que derivaría del canónico, o una herramienta que no pudo
    /// analizarlo) o si sólo AVISA (no se pudo medir: sin Python, sin el repo).
    /// Un mapeo al revés —avisar de lo que rompe, o romper por no poder medir—
    /// deja pasar el churn que este paso existe para cortar, o rompe el generador
    /// en una máquina sin Python.
    ///
    /// El camino extremo a extremo (proceso lanzado, proyecto real, veredicto
    /// sobre los assets de verdad) NO se prueba aquí: depende de Python en el
    /// PATH, y esa clase de comprobación vive en los scripts del repo —el paso
    /// del Play pass y `check-unity-assets.sh`—, no en la suite.
    /// </summary>
    public class AssetCanonicalizerTests
    {
        // ── El interruptor de entorno ───────────────────────────────────────

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("1", true)]
        [InlineData("sí", true)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("NO", false)]
        [InlineData("  off  ", false)]
        public void Enabled_SoloLoApagaUnValorExplicito(string? valor, bool esperado)
            => Assert.Equal(esperado, AssetCanonicalizer.Enabled(valor));

        // ── Los argumentos con los que se llama al canonicalizador ──────────

        [Fact]
        public void Arguments_SinCheck_NoEscribeNada()
        {
            string[] args = AssetCanonicalizer.Arguments("/repo/scripts/unity-canonicalize-assets.py", "/repo/proj", check: false);
            Assert.Equal(new[] { "/repo/scripts/unity-canonicalize-assets.py", "--project", "/repo/proj" }, args);
        }

        [Fact]
        public void Arguments_ConCheck_LlevaElVeredicto()
        {
            string[] args = AssetCanonicalizer.Arguments("/repo/scripts/unity-canonicalize-assets.py", "/repo/proj", check: true);
            Assert.Equal(
                new[] { "/repo/scripts/unity-canonicalize-assets.py", "--project", "/repo/proj", "--check" },
                args);
        }

        // ── El intérprete: override manda, y si no, el orden del PATH ───────

        [Fact]
        public void ResolvePython_ElOverrideMandaSinMirarElPath()
        {
            var mirado = new List<string>();
            string? hit = AssetCanonicalizer.ResolvePython("/opt/py/python3", name =>
            {
                mirado.Add(name);
                return "/usr/bin/" + name;
            });
            Assert.Equal("/opt/py/python3", hit);
            Assert.Empty(mirado);
        }

        [Fact]
        public void ResolvePython_RespetaElOrdenDeLosCandidatos()
        {
            // Sólo está el tercero: se llega a él en vez de rendirse.
            string? hit = AssetCanonicalizer.ResolvePython(null, name => name == "py" ? "C:\\Windows\\py.exe" : null);
            Assert.Equal("C:\\Windows\\py.exe", hit);
        }

        [Fact]
        public void ResolvePython_SinNinguno_NoInventa()
        {
            Assert.Null(AssetCanonicalizer.ResolvePython(null, _ => null));
        }

        [Fact]
        public void ResolvePython_ElPrimeroDelPathGana()
        {
            string? hit = AssetCanonicalizer.ResolvePython(null, name => name == "python3" ? "A" : "B");
            Assert.Equal("A", hit);
        }

        // ── El veredicto: romper o avisar, y con qué motivo ────────────────

        [Fact]
        public void Classify_Rc0_EsCanonicoYMedido()
        {
            var r = AssetCanonicalizer.Classify(0, "✓ 103 assets revisados · 17 reescritos al canónico\n");
            Assert.Equal(CanonicalizeOutcome.Canonical, r.Outcome);
            Assert.True(r.Ok);
            Assert.True(r.Measured);
            Assert.Contains("17 reescritos", r.Detail);
        }

        [Fact]
        public void Classify_Rc1ConDerivaria_EsDerivadoYRompe()
        {
            var r = AssetCanonicalizer.Classify(1, "DERIVARÍA  Assets/Materials/AntMat.mat  (3 documentos)\n");
            Assert.Equal(CanonicalizeOutcome.Derived, r.Outcome);
            Assert.False(r.Ok);
            Assert.True(r.Measured);
            Assert.Contains("AntMat.mat", r.Detail);
        }

        [Fact]
        public void Classify_Rc1SinVeredictoDeAnalisis_NoSeConfundeConDeriva()
        {
            // El rc es el mismo (1) que el de «derivaría»: lo que los separa es el
            // marcador de la herramienta. Sin esto, un asset ilegible se reportaría
            // como «derivaría», que manda a mirar anclas en vez del fichero.
            var r = AssetCanonicalizer.Classify(1, "✗ NO SE PUDO CANONICALIZAR Assets/x.unity: documento sin ancla\n");
            Assert.Equal(CanonicalizeOutcome.NotAnalyzable, r.Outcome);
            Assert.True(r.Measured);
        }

        [Fact]
        public void Classify_RcDeUso_NoEsUnVerde()
        {
            var r = AssetCanonicalizer.Classify(2, "✗ no encuentro src/App/AntSim.Unity/Assets\n");
            Assert.Equal(CanonicalizeOutcome.NotAnalyzable, r.Outcome);
            Assert.False(r.Ok);
        }

        [Fact]
        public void NotMeasurable_NoEsRoto()
        {
            var r = AssetCanonicalizer.NotMeasurable("sin Python 3 en el PATH");
            Assert.Equal(CanonicalizeOutcome.NotMeasurable, r.Outcome);
            Assert.False(r.Measured);
            Assert.False(r.Ok);
        }

        // ── La salida, tal como se enseña en el log del editor ─────────────

        [Fact]
        public void Lines_QuitaCrYLineasVacias()
        {
            var r = AssetCanonicalizer.Classify(0, "REESCRITO  a.mat\r\n\r\n  REESCRITO  b.mat  \r\n");
            Assert.Equal(new[] { "REESCRITO  a.mat", "  REESCRITO  b.mat" }, r.Lines());
        }

        // ── Los casos en los que no hay ni proceso que juzgar ──────────────

        [Fact]
        public void Run_SinAnclaDelRepo_NoMide()
        {
            var r = AssetCanonicalizer.Run(null, "/proj");
            Assert.Equal(CanonicalizeOutcome.NotMeasurable, r.Outcome);
            Assert.Contains("raíz del repo", r.Detail);
        }

        [Fact]
        public void Run_SinLaHerramienta_NoMideYDiceCualFalta()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "antsim-canon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var r = AssetCanonicalizer.Run(tmp, "/proj");
                Assert.Equal(CanonicalizeOutcome.NotMeasurable, r.Outcome);
                Assert.Contains(AssetCanonicalizer.ScriptRelativePath, r.Detail);
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }

        [Fact]
        public void Run_SinPython_NoMideYNoLanzaProceso()
        {
            // Con la herramienta presente y la sonda del PATH inyectada (que no
            // encuentra nada): el paso tiene que decir que no midió, y no lanzar
            // «python3» a lo loco ni romper el generador por eso.
            string repo = Path.Combine(Path.GetTempPath(), "antsim-canon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repo, "scripts"));
            try
            {
                File.WriteAllText(Path.Combine(repo, AssetCanonicalizer.ScriptRelativePath), string.Empty);
                var r = AssetCanonicalizer.Run(repo, "/proj", check: true, pythonOverride: null, onPath: _ => null);
                Assert.Equal(CanonicalizeOutcome.NotMeasurable, r.Outcome);
                Assert.Contains("Python 3", r.Detail);
                Assert.Contains(AssetCanonicalizer.PythonEnvVar, r.Detail);
            }
            finally
            {
                Directory.Delete(repo, true);
            }
        }

        [Fact]
        public void Run_ElNombreDeLaHerramientaEsElDelRepo()
            => Assert.Equal("scripts/unity-canonicalize-assets.py", AssetCanonicalizer.ScriptRelativePath);
    }
}
