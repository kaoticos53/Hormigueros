using System;
using System.IO;
using System.Text.RegularExpressions;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// CONTRATO DEL POOL POR DEFECTO: el juego arranca sembrado con UN pool, y
    /// ese nombre tiene que ser el mismo en los cuatro sitios que lo declaran.
    ///
    /// POR QUÉ EXISTE. El default del pool vive en cuatro archivos que nadie
    /// compara entre sí: el lanzador (`scripts/play-game.sh` y `play-game.ps1`,
    /// con el `.bat` como ayuda que documenta la misma línea), el modelo que
    /// Unity aplica al montar (`LaunchSettings.Pool`), la escena versionada
    /// (`Game.unity`, campo `SeedPoolPath`) y el registro de esa escena
    /// (`GeneratedSceneCatalog`). `GeneratedSceneContractTests` ya cubre
    /// escena↔catálogo y catálogo↔Play pass, pero NO el lanzador ni el modelo:
    /// cambiar el default de `play-game.sh` y olvidar `LaunchSettings` dejaba al
    /// jugador con el pool que el Editor tenía escrito en la escena, y a la suite
    /// en verde. Un default partido no rompe nada — simplemente el juego
    /// arranca con un cerebro y el catálogo declara otro, que es peor.
    ///
    /// QUÉ SE COMPRUEBA. `LaunchSettings.Defaults().Pool` es la fuente de la
    /// verdad (es lo que el Editor aplica); de ahí sale la ruta esperada
    /// `artifacts/{pool}.antgenome`, y cada uno de los otros tres tiene que
    /// decir exactamente eso. Las rutas se comparan normalizadas (sin comillas,
    /// con `/`) porque Unity y los scripts escriben separadores distintos.
    /// </summary>
    public class DefaultPoolContractTests
    {
        private const string LaunchSh = "scripts/play-game.sh";
        private const string LaunchPs1 = "scripts/play-game.ps1";
        private const string LaunchBat = "scripts/play-game.bat";
        private const string GameScene = "src/App/AntSim.Unity/Assets/Scenes/Game.unity";

        [Fact]
        public void PoolPorDefecto_CoincideEnLanzadoresAjustesEscenaYCatalogo()
        {
            string root = RepoRoot();
            string pool = LaunchSettings.Defaults().Pool;

            Assert.False(string.IsNullOrWhiteSpace(pool),
                "LaunchSettings.Defaults().Pool está vacío: el juego arrancaría sin sembrar y no " +
                "habría default que contrastar.");
            Assert.False(pool.Contains('/') || pool.Contains('\\'),
                $"LaunchSettings.Defaults().Pool es una ruta («{pool}») y aquí se trata como NOMBRE " +
                $"de pool: la escena y el catálogo esperan «artifacts/{pool}.antgenome».");
            string ruta = "artifacts/" + pool + ".antgenome";

            // ── Lanzadores ──────────────────────────────────────────────────
            // bash: `POOL="nombre"` a principio de línea (las banderas `--pool`
            // asignan a la misma variable, pero no a principio de línea).
            string sh = Captura(root, LaunchSh, @"^POOL=""([^""]+)""", "la asignación `POOL=…`");
            MismoPool(LaunchSh, sh, pool, ruta);

            // PowerShell: el parámetro del script es el default real del .bat
            // (el .bat no parsea nada: delega en él).
            string ps1 = Captura(root, LaunchPs1, @"\[string\]\$Pool\s*=\s*""([^""]+)""",
                "el parámetro `$Pool`");
            MismoPool(LaunchPs1, ps1, pool, ruta);

            // El .bat no tiene default propio, pero su ayuda SÍ documenta el
            // que aplica: si se queda atrás, la ayuda miente al jugador.
            string bat = Captura(root, LaunchBat, @"^REM\s+play-game\.bat\s+(\S+),\s*grid",
                "la línea de uso que documenta el pool por defecto");
            MismoPool(LaunchBat, bat, pool, ruta);

            // ── Escena versionada ───────────────────────────────────────────
            string escena = Normaliza(Captura(root, GameScene, @"^[ \t]*SeedPoolPath:[ \t]*(\S+)[ \t]*$",
                "el campo `SeedPoolPath` del presentador"));
            Assert.True(escena == ruta,
                $"«{GameScene}» siembra «{escena}» y LaunchSettings.Defaults().Pool es «{pool}» " +
                $"(«{ruta}»). La escena es lo que ve el jugador al abrir el proyecto: " +
                "regenera Game.unity con el default nuevo (SceneBootstrapper) en vez de " +
                "dejar que difiera del lanzador.");

            // ── Catálogo de escenas ─────────────────────────────────────────
            var juego = GeneratedSceneCatalog.Game;
            Assert.True(juego.Views.Count == 1,
                $"el catálogo declara {juego.Views.Count} vistas para Game.unity: este contrato " +
                "asume una sola, como el montador. Si el juego pasó a multi-visor, amplía el contrato.");
            string catalogo = Normaliza(juego.Views[0].PoolPath);
            Assert.True(catalogo == ruta,
                $"GeneratedSceneCatalog declara «{catalogo}» para Game.unity y " +
                $"LaunchSettings.Defaults().Pool es «{pool}» («{ruta}»): el registro de la escena " +
                "dejó de describir con qué pool arranca el juego.");
        }

        // ── herramientas del test ──────────────────────────────────────────

        /// <summary>El valor del primer grupo de <paramref name="pattern"/> en el
        /// archivo. Un patrón que ya no casa es un fallo explícito, no un test
        /// verde: significa que el lanzador cambió de forma.</summary>
        private static string Captura(string root, string relative, string pattern, string what)
        {
            string path = Path.Combine(root, relative);
            Assert.True(File.Exists(path), "no existe " + relative);
            string text = File.ReadAllText(path).Replace("\r", string.Empty);
            var m = Regex.Match(text, pattern, RegexOptions.Multiline);
            Assert.True(m.Success,
                $"no encontré {what} en {relative} (patrón «{pattern}»): si el archivo cambió de " +
                "forma, ajusta este contrato a la vez que el default del pool.");
            return m.Groups[1].Value.Trim();
        }

        private static void MismoPool(string donde, string real, string esperado, string ruta)
        {
            Assert.True(real == esperado,
                $"{donde} arranca con el pool «{real}» y LaunchSettings.Defaults().Pool es " +
                $"«{esperado}» («{ruta}»). El pool por defecto es UNO: lanzador, LaunchSettings, " +
                "Game.unity y GeneratedSceneCatalog tienen que decir lo mismo. Cambia los cuatro " +
                "—o el que se quedó atrás— en el mismo commit.");
        }

        /// <summary>Rutas tal como las escriben Unity y los scripts: sin comillas,
        /// sin espacios de sobra y con `/` (el montador puede llegar con `\\`).</summary>
        private static string Normaliza(string value)
            => value.Trim().Trim('"').Replace('\\', '/');

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
