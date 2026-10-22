using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// CONTRATO DE LAS ESCENAS GENERADAS: cada `.unity` que el repo versiona tiene
    /// que ser lo que dice <see cref="GeneratedSceneCatalog"/> — la misma
    /// configuración que declara la entrada que lo produjo, vista a vista.
    ///
    /// POR QUÉ EXISTE. Es la mitad que faltaba de los contratos hermanos
    /// (<c>GeneratedAssetContractTests</c> cubre el `.ttf` y las mallas,
    /// <c>GeneratedMaterialContractTests</c> los `.mat` y las texturas): la ESCENA
    /// es el otro artefacto del bootstrapper, y sus valores no coinciden con los
    /// del montador. El `Game.unity` versionado pide ticks 7200 y velocidad 2 —la
    /// configuración del Play pass—, no los 36000/3 de `LaunchSettings.Defaults()`;
    /// y el `MultiSim.unity` versionado es el del MEDIDOR (grid 256, 12000 ticks,
    /// frameEvery 5), no el del menú (grid 96, 36000 ticks, frameEvery 2). Eso vivía
    /// sólo en la cabeza de quien lo ejecutó: regenerar «con el bootstrapper»
    /// devolvía otra escena sin que nada avisara, y el jugador se encontraba otro
    /// mundo (u otra escala de tablero). Aquí se ve en milisegundos y sin editor.
    ///
    /// QUÉ SE COMPRUEBA, y por qué así:
    ///   · BIYECCIÓN —un `.unity` con presentador que no esté en el catálogo falla—
    ///     para que una entrada nueva del montador no se quede sin registrar.
    ///   · CAMPO A CAMPO contra el YAML serializado, emparejando por CAPA de render
    ///     (la capa es la identidad estable de cada vista dentro del fichero: el
    ///     orden de los documentos lo cambia Unity al re-serializar).
    ///   · El catálogo contra las CONSTANTES DE SUS PRODUCTORES: los valores del
    ///     Play pass (`TICKS`, `B4_SPEED`, `GRID`, `COLONIES`, `SEED`, `SEED_POOL`
    ///     de `scripts/playpass-live.sh`) y los del medidor (`PerfGridCells`,
    ///     `PerfTicks`, `PerfFrameEvery`, `PerfColoniesPerView` y el `baseSeed` de
    ///     `MultiSimBootstrapper`). Sin esto el registro podría quedarse atrás en
    ///     silencio —que es justo lo que «no depender de recordar los valores»
    ///     quiere decir—: si el pase cambia de ticks, el registro deja de describir
    ///     la escena y este test lo dice antes de que nadie la regenere.
    ///
    /// LA REPARACIÓN que pide un fallo de aquí es una de dos, y las dos son
    /// explícitas: regenerar la escena con la entrada declarada (si el catálogo ya
    /// describe lo que se quiere) o actualizar el catálogo (si el cambio de
    /// configuración es intencionado). El mensaje trae la entrada y el comando.
    /// </summary>
    public class GeneratedSceneContractTests
    {
        private const string ScenesDir = "src/App/AntSim.Unity/Assets/Scenes";
        private const string PresenterType = "AntSim.Unity.Scripts.Presenter.SimPresenterBehaviour";
        private const string PlaypassScript = "scripts/playpass-live.sh";
        private const string MultiSimBootstrapperFile =
            "src/App/AntSim.Unity/Assets/Scripts/EditorTools/MultiSimBootstrapper.cs";
        private const string PresenterScriptFile =
            "src/App/AntSim.Unity/Assets/Scripts/Presenter/SimPresenterBehaviour.cs";

        /// <summary>Los campos del presenter que el contrato lee del YAML. Son los que
        /// deciden la partida y el montaje; el resto (mallas, materiales, teclas) ya
        /// está cubierto por los contratos de assets.</summary>
        private static readonly string[] KnownFields =
        {
            "RenderLayer", "Seed", "Grid", "Colonies", "Ticks", "FrameEvery",
            "Species", "SeedPoolPath", "Speed", "Maze",
        };

        [Fact]
        public void EscenasGeneradas_SonLasDelCatalogo()
        {
            string root = RepoRoot();
            string dir = Path.Combine(root, ScenesDir);
            Assert.True(Directory.Exists(dir), "no existe el directorio de escenas: " + dir);

            string presenterGuid = GuidOf(Path.Combine(root, PresenterScriptFile));

            // — Biyección: ninguna escena con presentador sin registrar —
            foreach (string file in Directory.GetFiles(dir, "*.unity"))
            {
                var presenters = ReadPresenters(file, presenterGuid);
                if (presenters.Count == 0) continue;
                string relative = "Assets/Scenes/" + Path.GetFileName(file);
                Assert.True(GeneratedSceneCatalog.ByPath(relative) != null,
                    $"«{relative}» tiene {presenters.Count} {PresenterType} y no está en " +
                    "GeneratedSceneCatalog: añádelo con la entrada y la configuración que lo generó " +
                    "(y con qué otras entradas escriben esa misma ruta).");
            }

            // — Campo a campo: la escena versionada es la que declara el catálogo —
            foreach (var scene in GeneratedSceneCatalog.Scenes)
            {
                string path = Path.Combine(dir, Path.GetFileName(scene.Path));
                Assert.True(File.Exists(path), $"el catálogo declara {scene.Path} y no existe: {path}");

                var presenters = ReadPresenters(path, presenterGuid);
                var bad = Discrepancias(scene, presenters);
                Assert.True(bad.Count == 0,
                    $"«{scene.Path}» no es la escena que declara GeneratedSceneCatalog " +
                    $"({bad.Count} discrepancia(s)):\n  - " + string.Join("\n  - ", bad) +
                    $"\n  Entrada que la produjo: {scene.EntryPoint}" +
                    $"\n  Para regenerarla:       {scene.Regenerate}" +
                    "\n  Si el cambio es intencionado, actualiza GeneratedSceneCatalog " +
                    "(y regenera la escena con la entrada de arriba).");
            }
        }

        [Fact]
        public void Catalogo_CoincideConLasConstantesDeSusProductores()
        {
            string root = RepoRoot();
            var game = GeneratedSceneCatalog.Game;
            var multi = GeneratedSceneCatalog.MultiSim;
            var defaults = LaunchSettings.Defaults();

            // ── La escena simple: la config del Play pass (scripts/playpass-live.sh),
            //    aplicada por la sonda del pass sobre la escena del menú. ──
            var pass = Assignments(Path.Combine(root, PlaypassScript));
            IgualDelProductor("ticks de Game.unity", pass, "TICKS",
                game.Ticks.ToString(CultureInfo.InvariantCulture));
            IgualDelProductor("velocidad de Game.unity (bloque 4)", pass, "B4_SPEED",
                game.Speed.ToString("0.###", CultureInfo.InvariantCulture));
            IgualDelProductor("grid de Game.unity", pass, "GRID",
                game.Grid.ToString(CultureInfo.InvariantCulture));
            IgualDelProductor("colonias de Game.unity", pass, "COLONIES",
                game.Colonies.ToString(CultureInfo.InvariantCulture));
            IgualDelProductor("semilla de Game.unity", pass, "SEED",
                game.Views[0].Seed.ToString(CultureInfo.InvariantCulture));
            IgualDelProductor("pool de Game.unity", pass, "SEED_POOL", Normaliza(game.Views[0].PoolPath));

            // frameEvery y especie NO los pone el pase: el config del play pass no
            // los lleva, así que la escena conserva los defaults del montador. Es la
            // parte de la procedencia que no está en ningún script, y por eso se
            // ancla al modelo (no a la memoria).
            Assert.True(game.FrameEvery == defaults.FrameEvery,
                $"Game.unity declara frameEvery {game.FrameEvery} y el Play pass no lo toca: " +
                $"la escena conserva el default del montador ({defaults.FrameEvery}).");
            Assert.True(game.Species == defaults.Species,
                $"Game.unity declara especie «{game.Species}» y el Play pass no la toca: " +
                $"la escena conserva el default del montador («{defaults.Species}»).");

            // ── El multi-visor: las constantes del MEDIDOR. ──
            string bootstrapper = Path.Combine(root, MultiSimBootstrapperFile);
            Igual("grid del medidor", ConstanteInt(bootstrapper, "PerfGridCells"),
                multi.Grid.ToString(CultureInfo.InvariantCulture));
            Igual("horizonte del medidor", ConstanteInt(bootstrapper, "PerfTicks"),
                multi.Ticks.ToString(CultureInfo.InvariantCulture));
            Igual("frameEvery del medidor", ConstanteInt(bootstrapper, "PerfFrameEvery"),
                multi.FrameEvery.ToString(CultureInfo.InvariantCulture));
            Igual("colonias por vista del medidor", ConstanteInt(bootstrapper, "PerfColoniesPerView"),
                multi.Colonies.ToString(CultureInfo.InvariantCulture));

            // El `baseSeed` del medidor: `CreateMultiSimScene(views: N, baseSeed: S, pools: null, …)`
            // es la llamada del medidor (las del smoke pasan array de pools, no null).
            var perf = Regex.Match(File.ReadAllText(bootstrapper),
                @"CreateMultiSimScene\(views: (\d+), baseSeed: (\d+), pools: null,");
            Assert.True(perf.Success,
                "no encontré la llamada del medidor en MultiSimBootstrapper " +
                "(«CreateMultiSimScene(views: N, baseSeed: S, pools: null, …)»): " +
                "si se cambió de forma, ajusta este contrato y el catálogo a la vez.");
            Igual("vistas del medidor", perf.Groups[1].Value,
                multi.Views.Count.ToString(CultureInfo.InvariantCulture));
            Igual("semilla base del medidor", perf.Groups[2].Value,
                GeneratedSceneCatalog.MultiViewBaseSeed.ToString(CultureInfo.InvariantCulture));

            // Los pools por vista son la cadena de autodetección, EN ORDEN: la vista i
            // se siembra con el i-ésimo candidato que existe.
            var candidatos = Regex.Matches(File.ReadAllText(bootstrapper),
                "\"(artifacts/[^\"]+\\.antgenome)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.True(candidatos.Count >= multi.Views.Count,
                $"el bootstrapper declara {candidatos.Count} pools candidatos y el medidor usa " +
                $"{multi.Views.Count} vistas.");
            for (int i = 0; i < multi.Views.Count; i++)
                Assert.True(multi.Views[i].PoolPath == candidatos[i],
                    $"la vista {i} del catálogo usa «{multi.Views[i].PoolPath}» y la cadena de " +
                    $"autodetección la sembraría con «{candidatos[i]}».");

            // Y, donde los pools EXISTEN, cada vista tiene que recibir el suyo: si
            // faltara uno, la autodetección pasaría al siguiente y la escena dejaría
            // de ser la del catálogo. `artifacts/*.antgenome` NO se versiona (son los
            // pools de cada máquina), así que en un clon limpio —CI— no hay ninguno y
            // esto no aplica: sólo se exige cuando están TODOS los de la escena.
            int presentes = 0;
            while (presentes < candidatos.Count
                   && File.Exists(Path.Combine(root, candidatos[presentes]))) presentes++;
            if (presentes >= multi.Views.Count)
            {
                for (int i = 0; i < multi.Views.Count; i++)
                    Assert.True(File.Exists(Path.Combine(root, candidatos[i])),
                        $"la vista {i} espera el pool «{candidatos[i]}» y no está en disco.");
            }
            else if (presentes > 0)
            {
                Assert.Fail($"sólo están {presentes} de los {multi.Views.Count} pools de la cadena " +
                    $"({string.Join(", ", candidatos.Take(presentes))}): la autodetección sembraría " +
                    "otras vistas y MultiSim.unity dejaría de ser la escena del catálogo. " +
                    "Regenera el medidor con la cadena completa o añade los que falten a artifacts/.");
            }
            // La velocidad del medidor es explícita desde F6: heredarla del
            // presenter (que ahora arranca a 120×) cambiaría la carga que mide.
            Igual("velocidad del medidor",
                ConstanteFloat(bootstrapper, "PerfSpeed").ToString("0.###", CultureInfo.InvariantCulture),
                multi.Speed.ToString("0.###", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// El lector LEE: si devolviera el catálogo en vez del fichero, el test de
        /// arriba se estaría comparando consigo mismo y pasaría siempre. Aquí se
        /// copia el `Game.unity` real, se le mueven dos campos y se exige que el
        /// contraste lo VEA — la comprobación negativa del contrato, en la forma de
        /// una mutación de la escena.
        /// </summary>
        [Fact]
        public void ContrasteDeEscena_VeLaDerivaDeLaEscenaYNoElCatalogo()
        {
            string original = Path.Combine(RepoRoot(), ScenesDir, "Game.unity");
            string root = RepoRoot();
            string presenterGuid = GuidOf(Path.Combine(root, PresenterScriptFile));
            string tmp = Path.Combine(Path.GetTempPath(), "antsim-scene-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string text = File.ReadAllText(original);

                // (1) La copia intacta pasa: el contraste no es un «siempre rojo».
                string intacto = Path.Combine(tmp, "Game.unity");
                File.WriteAllText(intacto, text);
                Assert.Empty(Discrepancias(GeneratedSceneCatalog.Game,
                    ReadPresenters(intacto, presenterGuid)));

                // (2) Con ticks y velocidad de una partida VIEJA (el fallo que este
                //     contrato persigue: regenerar con otra configuración y creer
                //     que la escena es la misma) el contraste lo dice.
                Assert.Contains("  Ticks: 216000", text);
                Assert.Contains("  Speed: 120", text);
                string mutado = Path.Combine(tmp, "Game-vieja.unity");
                File.WriteAllText(mutado, text
                    .Replace("  Ticks: 216000", "  Ticks: 36000")
                    .Replace("  Speed: 120", "  Speed: 3"));

                var presenters = ReadPresenters(mutado, presenterGuid);
                Assert.Equal(216000, ReadPresenters(intacto, presenterGuid)[0].Int("Ticks")); // el lector ancla
                Assert.Equal(36000, presenters[0].Int("Ticks"));                            // y VE la mutación

                var bad = Discrepancias(GeneratedSceneCatalog.Game, presenters);
                Assert.True(bad.Count == 2,
                    $"esperaba dos discrepancias (ticks y velocidad) y salieron {bad.Count}: " +
                    string.Join(" · ", bad));
                Assert.Contains(bad, m => m.Contains("Ticks") && m.Contains("36000"));
                Assert.Contains(bad, m => m.Contains("Speed") && m.Contains("3"));
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }

        // ── herramientas del test ──────────────────────────────────────────

        /// <summary>Todas las diferencias entre lo que declara el catálogo y lo que
        /// dice el fichero, en texto legible (vacío = la escena es la del catálogo).</summary>
        private static List<string> Discrepancias(GeneratedSceneCatalog.SceneSpec scene,
            List<Presenter> presenters)
        {
            var bad = new List<string>();

            if (presenters.Count != scene.Views.Count)
                bad.Add($"hay {presenters.Count} presentador(es) y el catálogo declara " +
                        $"{scene.Views.Count} vista(s)");

            foreach (var view in scene.Views)
            {
                var conCapa = presenters.Where(p => p.Int("RenderLayer") == view.RenderLayer).ToList();
                if (conCapa.Count != 1)
                {
                    bad.Add($"no hay UNA vista con capa {view.RenderLayer} (encontradas: {conCapa.Count}); " +
                            "la capa es la identidad de cada vista dentro del fichero");
                    continue;
                }

                var p = conCapa[0];
                string donde = scene.Views.Count > 1 ? $", capa {view.RenderLayer}" : "";
                Campo(bad, "Grid", scene.Grid.ToString(CultureInfo.InvariantCulture), p.Get("Grid"), donde);
                Campo(bad, "Colonies", scene.Colonies.ToString(CultureInfo.InvariantCulture), p.Get("Colonies"), donde);
                Campo(bad, "Ticks", scene.Ticks.ToString(CultureInfo.InvariantCulture), p.Get("Ticks"), donde);
                Campo(bad, "FrameEvery", scene.FrameEvery.ToString(CultureInfo.InvariantCulture), p.Get("FrameEvery"), donde);
                Campo(bad, "Species", scene.Species, p.Get("Species"), donde);
                Campo(bad, "Seed", view.Seed.ToString(CultureInfo.InvariantCulture), p.Get("Seed"), donde);

                if (Math.Abs(p.Float("Speed") - scene.Speed) > 1e-4f)
                    bad.Add($"Speed{donde}: la escena dice «{p.Get("Speed")}» y el catálogo " +
                            $"«{scene.Speed.ToString("0.###", CultureInfo.InvariantCulture)}»");

                // F6: el terreno es parte de la partida. Una escena sin el campo
                // (serializada por un presenter anterior) equivale a 0-«sin
                // laberinto»; la simple tiene que declararlo a 1.
                string mazeScene = p.Get("Maze") == "" ? "0" : p.Get("Maze");
                Campo(bad, "Maze", scene.Maze ? "1" : "0", mazeScene, donde);

                string esperado = Normaliza(view.PoolPath);
                string real = Normaliza(p.Get("SeedPoolPath"));
                if (esperado != real)
                    bad.Add($"SeedPoolPath{donde}: la escena dice «{real}» y el catálogo «{esperado}»");
            }

            foreach (var p in presenters)
                if (!scene.Views.Any(v => v.RenderLayer == p.Int("RenderLayer")))
                    bad.Add($"sobra un presentador en la capa {p.Get("RenderLayer")}: no está en el catálogo");

            return bad;
        }

        private static void Campo(List<string> bad, string field, string esperado, string real, string donde)
        {
            if (esperado == real) return;
            bad.Add($"{field}{donde}: la escena dice «{real}» y el catálogo «{esperado}»");
        }

        /// <summary>Rutas de asset tal como las escribe Unity en YAML: separador
        /// normalizado y sin comillas (el montador puede llegar con `\\`).</summary>
        private static string Normaliza(string value)
            => value.Trim().Trim('"').Replace('\\', '/');

        /// <summary>Un presentador leído del YAML: los campos conocidos, por nombre.</summary>
        private sealed class Presenter
        {
            public readonly Dictionary<string, string> Fields = new(StringComparer.Ordinal);

            public string Get(string key) => Fields.TryGetValue(key, out string? v) ? v : "";

            public int Int(string key)
                => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                    ? v : int.MinValue;

            public float Float(string key)
                => float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                    ? v : float.NaN;
        }

        /// <summary>
        /// Los `SimPresenterBehaviour` del `.unity`, documento a documento. Un
        /// documento se reconoce por su `m_Script` (la guía del `.meta` del
        /// presenter) o por el `m_EditorClassIdentifier`, y sus campos son las
        /// líneas de primer nivel con el nombre de <see cref="KnownFields"/>. El
        /// lector NO es un parser de YAML: lee lo que el escritor de Unity produce,
        /// y si algún día cambia de forma devuelve cero presentadores (el contrato
        /// falla con «0 presentadores») en vez de leer basura en silencio.
        /// </summary>
        private static List<Presenter> ReadPresenters(string path, string presenterGuid)
        {
            string text = File.ReadAllText(path).Replace("\r", string.Empty);
            var result = new List<Presenter>();
            var doc = new List<string>();

            void Cerrar()
            {
                var p = ParseDoc(doc, presenterGuid);
                if (p != null) result.Add(p);
                doc.Clear();
            }

            foreach (string raw in text.Split('\n'))
            {
                if (raw.StartsWith("--- ", StringComparison.Ordinal)) Cerrar();
                doc.Add(raw.Trim());
            }
            Cerrar();
            return result;
        }

        private static Presenter? ParseDoc(List<string> doc, string presenterGuid)
        {
            bool esPresenter = doc.Any(line =>
                (line.StartsWith("m_Script:", StringComparison.Ordinal) && line.Contains(presenterGuid)) ||
                (line.StartsWith("m_EditorClassIdentifier:", StringComparison.Ordinal)
                 && line.Contains(PresenterType)));
            if (!esPresenter) return null;

            var p = new Presenter();
            foreach (string line in doc)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon);
                if (Array.IndexOf(KnownFields, key) < 0) continue;
                p.Fields[key] = line.Substring(colon + 1).Trim();
            }
            return p;
        }

        /// <summary>La guía del `.meta` del script del presenter: el ancla que dice
        /// «este MonoBehaviour ES el presenter» sin depender del nombre del tipo.</summary>
        private static string GuidOf(string scriptPath)
        {
            string meta = scriptPath + ".meta";
            Assert.True(File.Exists(meta), $"falta {meta}: sin ella no puedo anclar el presenter");
            var m = Regex.Match(File.ReadAllText(meta), @"guid: ([0-9a-f]{32})");
            Assert.True(m.Success, $"{meta} no declara un guid");
            return m.Groups[1].Value;
        }

        /// <summary>Las asignaciones `CLAVE=valor` de un script de bash (las que están
        /// a principio de línea), con comillas quitadas: los defaults del Play pass.</summary>
        private static Dictionary<string, string> Assignments(string path)
        {
            Assert.True(File.Exists(path), "no existe " + path);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path))
            {
                var m = Regex.Match(line, @"^([A-Z][A-Z0-9_]*)=(.*)$");
                if (!m.Success) continue;
                map[m.Groups[1].Value] = Normaliza(m.Groups[2].Value);
            }
            return map;
        }

        /// <summary>Una constante del productor contra el valor del catálogo.</summary>
        private static void Igual(string what, string real, string esperado)
        {
            Assert.True(real == esperado,
                $"{what}: el productor declara «{real}» y el catálogo «{esperado}». Si el cambio es " +
                "intencionado, actualiza GeneratedSceneCatalog (y regenera la escena); si no, " +
                "el registro dejó de describir lo que el productor escribe.");
        }

        private static void IgualDelProductor(string what, Dictionary<string, string> values,
            string key, string esperado)
        {
            Assert.True(values.TryGetValue(key, out string? real),
                $"no encontré la asignación «{key}=…» en el script: si se renombró, ajusta este " +
                "contrato y el catálogo a la vez.");
            Igual(what, real!, esperado);
        }

        private static string ConstanteInt(string path, string name)
        {
            Assert.True(File.Exists(path), "no existe " + path);
            var m = Regex.Match(File.ReadAllText(path),
                @"public const int " + Regex.Escape(name) + @" = (\d+);");
            Assert.True(m.Success,
                $"no encontré la constante int «{name}» en {Path.GetFileName(path)}: si se renombró, " +
                "ajusta este contrato y el catálogo a la vez.");
            return m.Groups[1].Value;
        }

        private static float ConstanteFloat(string scriptPath, string name)
        {
            Assert.True(File.Exists(scriptPath), "no existe " + scriptPath);
            var m = Regex.Match(File.ReadAllText(scriptPath),
                @"public (?:const )?float " + Regex.Escape(name) + @" = ([0-9.]+)f;");
            Assert.True(m.Success,
                $"no encontré el campo float «{name}» en {Path.GetFileName(scriptPath)}: si se " +
                "renombró, ajusta este contrato y el catálogo a la vez.");
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

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
