using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// EL PASO DEL CANÓNICO, del lado del GENERADOR — puro (sin UnityEngine, así
    /// que la suite headless prueba sus decisiones y el editor lo llama igual).
    ///
    /// POR QUÉ EXISTE. Unity no da API para fijar el `m_LocalIdentfierInFile` de
    /// los objetos que crea: lo asigna de un contador con base ALEATORIA por
    /// sesión —medido: dos pases del MISMO montaje dieron bases 54570640 y
    /// 8954272— y URP añade a cada `.mat` un bloque `AssetVersion` con un id de
    /// 64 bits que cambia de sitio. Por eso el bootstrapper, al escribir la
    /// escena y los materiales, dejaba **17 assets** (16 `.mat` y `Game.unity`)
    /// con bytes distintos en cada pasada aunque el montaje fuese el mismo, y el
    /// ruido lo quitaba DESPUÉS el Play pass: los assets dependían de que otro
    /// los arreglara.
    ///
    /// QUÉ HACE. El generador CIERRA su propia escritura con este paso: llama al
    /// canonicalizador del repo —`scripts/unity-canonicalize-assets.py`, la
    /// ÚNICA definición de la forma canónica; aquí no se reimplementa ni una
    /// línea de su receta— para que lo que quede en disco sean anclas derivadas
    /// del CONTENIDO, y lee su veredicto:
    ///
    ///   · <see cref="CanonicalizeOutcome.Canonical"/> — en forma canónica (o ya
    ///     lo estaba). Es el único veredicto que deja seguir sin más.
    ///   · <see cref="CanonicalizeOutcome.Derived"/> — algún asset derivaría:
    ///     el generador NO escribió lo que debía. Falla cerrado.
    ///   · <see cref="CanonicalizeOutcome.NotAnalyzable"/> — la herramienta
    ///     corrió y no dio veredicto (un asset que no entiende, uso equivocado,
    ///     o el repo donde no toca): también falla cerrado, porque «no sé» sobre
    ///     un asset recién escrito es exactamente el falso verde que buscamos.
    ///   · <see cref="CanonicalizeOutcome.NotMeasurable"/> — no se pudo ni
    ///     ejecutar (sin Python 3, sin la herramienta, sin ancla del repo, o se
    ///     pasó del tiempo). AVISA en vez de romper —«no medido» no es «roto»,
    ///     el mismo criterio que las puertas del pre-push—, pero se dice, porque
    ///     significa que la garantía NO se cumplió en esta pasada.
    ///
    /// DOS PERILLAS, y el generador las necesita por eso:
    ///   · <c>ANTSIM_CANONICALIZE=0</c> salta el paso (medir el antes/después de
    ///     este cambio, o trabajar a mano sobre un árbol que no es del repo).
    ///   · <c>ANTSIM_PYTHON</c> apunta al intérprete cuando no está en el PATH
    ///     del editor. Si no se encuentra ninguno se AVISA (NotMeasurable), no
    ///     se inventa un intérprete.
    /// </summary>
    public enum CanonicalizeOutcome
    {
        /// <summary>Los assets quedaron en forma canónica (o ya lo estaban).</summary>
        Canonical,

        /// <summary>Algún asset derivaría del canónico: el generador no escribió lo debido.</summary>
        Derived,

        /// <summary>La herramienta corrió pero no dio veredicto (asset ilegible, uso).</summary>
        NotAnalyzable,

        /// <summary>No se pudo medir: sin Python 3, sin la herramienta, sin repo o agotado el tiempo.</summary>
        NotMeasurable,
    }

    /// <summary>El veredicto del paso, con la salida cruda de la herramienta para
    /// poder enseñarla tal cual (el que decide es el canonicalizador, no esto).</summary>
    public sealed class CanonicalizeResult
    {
        public CanonicalizeOutcome Outcome { get; }
        public int ExitCode { get; }
        public string Output { get; }
        public string Detail { get; }

        public CanonicalizeResult(CanonicalizeOutcome outcome, int exitCode, string output, string detail)
        {
            Outcome = outcome;
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        /// <summary>¿Quedó canónico? El único «sí» posible.</summary>
        public bool Ok => Outcome == CanonicalizeOutcome.Canonical;

        /// <summary>¿Se llegó a medir? (false = no hubo veredicto de la herramienta).</summary>
        public bool Measured => Outcome != CanonicalizeOutcome.NotMeasurable;

        /// <summary>La salida partida en líneas limpias: sin CR (consola de Windows)
        /// y sin vacías — es lo que se enseña en el log del editor.</summary>
        public string[] Lines()
        {
            var salida = new List<string>();
            foreach (string cruda in Output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (cruda.Trim().Length > 0) salida.Add(cruda.TrimEnd());
            }
            return salida.ToArray();
        }

        public override string ToString() => Outcome + ": " + Detail;
    }

    public static class AssetCanonicalizer
    {
        /// <summary>La herramienta del repo. Su nombre va en el veredicto y en el
        /// mensaje: quien lea el error tiene que saber QUÉ corrió.</summary>
        public const string ScriptRelativePath = "scripts/unity-canonicalize-assets.py";

        /// <summary>Intérprete a mano, para un editor lanzado sin el PATH del usuario.</summary>
        public const string PythonEnvVar = "ANTSIM_PYTHON";

        /// <summary>`=0` apaga el paso (antes/después, o árbol que no es del repo).</summary>
        public const string SwitchEnvVar = "ANTSIM_CANONICALIZE";

        /// <summary>Candidatos, en orden: el mismo que resuelven los scripts del
        /// repo (`python3`, luego `python`; en Windows `py`, el lanzador).</summary>
        public static readonly string[] PythonCandidates = { "python3", "python", "py" };

        /// <summary>El canonicalizador entero tarda décimas de segundo con el
        /// proyecto del repo; el tope existe para no colgar al EDITOR si el
        /// intérprete que se encuentre es cualquier cosa.</summary>
        public const int DefaultTimeoutMs = 180_000;

        private const string MarcadorDeriva = "DERIVARÍA";
        private const string MarcadorNoAnaliza = "NO SE PUDO CANONICALIZAR";

        /// <summary>¿Está encendido el paso? Sin valor → sí; `0`/`false`/`no`/`off` → no.</summary>
        public static bool Enabled(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return true;
            switch (valor.Trim().ToLowerInvariant())
            {
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>Argumentos del intérprete: el script, el proyecto y, si toca,
        /// el modo de comprobación (`--check` no escribe: es un VEREDICTO).</summary>
        public static string[] Arguments(string scriptPath, string projectPath, bool check)
            => check
                ? new[] { scriptPath, "--project", projectPath, "--check" }
                : new[] { scriptPath, "--project", projectPath };

        /// <summary>
        /// El intérprete a usar: primero <see cref="PythonEnvVar"/> (una ruta
        /// explícita manda), después los candidatos del PATH. Devuelve null si no
        /// hay ninguno — quien llama AVISA y sigue (no medido), no inventa.
        /// <paramref name="onPath"/> se inyecta para poder probarlo sin PATH.
        /// </summary>
        public static string? ResolvePython(string? overridePath = null, Func<string, string?>? onPath = null)
        {
            if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
            onPath ??= OnPath;
            foreach (string candidato in PythonCandidates)
            {
                string? hit = onPath(candidato);
                if (!string.IsNullOrEmpty(hit)) return hit;
            }
            return null;
        }

        /// <summary>El primer ejecutable con ese nombre en el PATH (con PATHEXT en
        /// Windows), o null. Es lo que hace un shell antes de lanzar el proceso.</summary>
        public static string? OnPath(string nombre)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] extensiones = Environment.GetEnvironmentVariable("PATHEXT") is { Length: > 0 } exts
                ? exts.Split(';')
                : new[] { string.Empty, ".exe", ".cmd", ".bat" };
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (string ext in extensiones)
                {
                    try
                    {
                        string candidato = Path.Combine(dir.Trim(), nombre + ext);
                        if (File.Exists(candidato)) return candidato;
                    }
                    catch (ArgumentException)
                    {
                        // Una entrada del PATH con caracteres inválidos: se salta,
                        // que el resto del PATH siga valiendo.
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// El veredicto a partir del rc y de la salida. Los rc son los del
        /// canonicalizador (0 hecho · 1 deriva o no analizable · 2 uso): aquí no
        /// se reinterpretan, se leen — y el marcador de la propia herramienta
        /// distingue «deriva» de «no pude analizarlo», que su rc 1 confunde.
        /// </summary>
        public static CanonicalizeResult Classify(int exitCode, string output)
        {
            output ??= string.Empty;
            string detalle = UltimaLineaUtil(output);
            if (exitCode == 0)
                return new CanonicalizeResult(CanonicalizeOutcome.Canonical, exitCode, output, detalle);

            if (exitCode == 1 && output.Contains(MarcadorNoAnaliza, StringComparison.Ordinal))
                return new CanonicalizeResult(CanonicalizeOutcome.NotAnalyzable, exitCode, output,
                    "la herramienta no pudo analizar algún asset: " + detalle);

            if (exitCode == 1)
                return new CanonicalizeResult(CanonicalizeOutcome.Derived, exitCode, output,
                    "algún asset derivaría del canónico: " + detalle);

            return new CanonicalizeResult(CanonicalizeOutcome.NotAnalyzable, exitCode, output,
                $"la herramienta salió con rc={exitCode}: " + detalle);
        }

        /// <summary>Veredicto de «no se pudo medir»: sin proceso que juzgar.</summary>
        public static CanonicalizeResult NotMeasurable(string detalle)
            => new CanonicalizeResult(CanonicalizeOutcome.NotMeasurable, -1, string.Empty, detalle);

        /// <summary>
        /// Corre el canonicalizador sobre <paramref name="projectPath"/> y devuelve
        /// su veredicto. NO lanza: un fallo de entorno es <c>NotMeasurable</c> y un
        /// fallo de los assets es <c>Derived</c>/<c>NotAnalyzable</c>; quien decide
        /// qué hacer con cada uno es el generador (el editor rompe con los dos
        /// últimos y avisa con el primero).
        /// </summary>
        public static CanonicalizeResult Run(
            string? repoRoot, string projectPath, bool check = false, int timeoutMs = DefaultTimeoutMs,
            string? pythonOverride = null, Func<string, string?>? onPath = null)
        {
            if (string.IsNullOrWhiteSpace(repoRoot)) return NotMeasurable("sin ancla de la raíz del repo");
            if (string.IsNullOrWhiteSpace(projectPath)) return NotMeasurable("sin ruta del proyecto Unity");

            string script = Path.Combine(repoRoot!, ScriptRelativePath);
            if (!File.Exists(script)) return NotMeasurable("falta " + ScriptRelativePath + " en " + repoRoot);

            string? python = ResolvePython(pythonOverride, onPath);
            if (python == null)
                return NotMeasurable("sin Python 3 en el PATH (ni en " + PythonEnvVar + ")");

            var psi = new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = repoRoot!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string arg in Arguments(script, projectPath, check)) psi.ArgumentList.Add(arg);
            // El canonicalizador imprime ✓/✗/· y en la consola cp1252 de Windows se
            // caería al escribirlos: el paso del pase le pone esto por eso.
            psi.Environment["PYTHONUTF8"] = "1";

            var salida = new StringBuilder();
            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return NotMeasurable("no arrancó el intérprete: " + python);

                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(timeoutMs))
                {
                    // `Kill()` sin `entireProcessTree`: ese parámetro no existe en el
                    // perfil .NET de Unity (lo cazó la puerta de compilación, que es
                    // justo para lo que está). El canonicalizador no lanza hijos, así
                    // que matar el proceso que arrancamos basta.
                    try { proc.Kill(); } catch (Exception) { /* ya se fue */ }
                    return NotMeasurable(
                        $"el canonicalizador no terminó en {timeoutMs / 1000} s (¿intérprete colgado?)");
                }
                salida.Append(stdout.GetAwaiter().GetResult());
                salida.Append(stderr.GetAwaiter().GetResult());
                return Classify(proc.ExitCode, salida.ToString());
            }
            catch (Exception ex)
            {
                return NotMeasurable("no pude ejecutar el canonicalizador (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        private static string UltimaLineaUtil(string output)
        {
            string[] lineas = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = lineas.Length - 1; i >= 0; i--)
            {
                string l = lineas[i].Trim();
                if (l.Length > 0) return l;
            }
            return "(sin salida)";
        }
    }
}
