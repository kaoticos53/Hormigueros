using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1/Play-pass — resolución de rutas relativas al REPO ROOT (puro, sin
    /// UnityEngine). El editor de Unity lanza los procesos con su propio cwd
    /// (la carpeta del proyecto), NO con el del repo: rutas como
    /// <c>build/antsim</c> o <c>artifacts/pretrain-warm-v2.antgenome</c> no
    /// resuelven desde ahí y el Play pass falla con «stream falló». El ancla
    /// es la carpeta del proyecto Unity (<c>src/App/AntSim.Unity</c>, dada por
    /// Application.dataPath en el componente): subir TRES niveles desde esa
    /// carpeta da la raíz del repo (src/App/AntSim.Unity → src/App → src →
    /// raíz), donde viven build/ y artifacts/. Las rutas absolutas pasan
    /// intactas; las relativas se intentan primero contra el cwd (comportami-
    /// ento de siempre) y después contra el repo root.
    /// </summary>
    public static class RepoPathResolver
    {
        /// <summary>Niveles a subir desde la carpeta del proyecto Unity hasta
        /// la raíz del repo: src/App/AntSim.Unity → src/App → src → root.</summary>
        public const int ProjectDepthFromRepoRoot = 3;

        /// <summary>Marcador del repo root: la carpeta del CLI. Es el mismo que
        /// usa la sonda de rendimiento, y NO un README.md (el proyecto Unity
        /// tiene el suyo: con ese marcador el ancla caía dentro del proyecto).</summary>
        public static readonly string[] RepoMarker = { "src", "Tools", "AntSim.Cli" };

        /// <summary>
        /// Ancla del repo root derivada de la carpeta del proyecto Unity
        /// (p. ej. Application.dataPath = …/src/App/AntSim.Unity/Assets).
        /// Devuelve null si el ancla no reconoce el layout esperado.
        /// </summary>
        public static string? RepoRootFromProjectPath(string? projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return null;
            // dataPath termina en /Assets: el proyecto es su padre.
            var proj = projectPath;
            if (proj.EndsWith("/Assets", StringComparison.OrdinalIgnoreCase)
                || proj.EndsWith("\\Assets", StringComparison.OrdinalIgnoreCase))
                proj = Directory.GetParent(proj)!.FullName;
            if (proj.EndsWith("/src/App/AntSim.Unity", StringComparison.OrdinalIgnoreCase)
                || proj.EndsWith("\\src\\App\\AntSim.Unity", StringComparison.OrdinalIgnoreCase))
                return Directory.GetParent(
                        Directory.GetParent(
                            Directory.GetParent(proj)!.FullName)!.FullName)!.FullName;
            return null; // proyecto movido/renombrado: sin ancla, solo cwd
        }

        /// <summary>
        /// Raíz del repo buscando la CARPETA DEL CLI subiendo desde
        /// <paramref name="startDir"/> (máx. <paramref name="maxUp"/> niveles).
        /// Sirve para el caso que el ancla del proyecto no cubre: un PLAYER
        /// empaquetado, donde <c>Application.dataPath</c> es
        /// <c>&lt;build&gt;/&lt;Nombre&gt;_Data</c> y no contiene el layout
        /// <c>src/App/…</c>. Devuelve null si no encuentra el marcador (build
        /// fuera del repo): el llamador se queda con el cwd.
        /// </summary>
        public static string? RepoRootByMarker(string? startDir, int maxUp = 6)
        {
            if (string.IsNullOrEmpty(startDir)) return null;
            var dir = new DirectoryInfo(startDir);
            for (int i = 0; dir != null && i <= maxUp; i++, dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, RepoMarker[0], RepoMarker[1], RepoMarker[2])))
                    return dir.FullName;
            return null;
        }

        /// <summary>
        /// Ancla del repo root para cualquiera de los dos mundos en los que corre
        /// la vista:
        ///   · **editor** — <c>Application.dataPath</c> = <c>&lt;proj&gt;/Assets</c>:
        ///     el layout del proyecto (subir tres niveles).
        ///   · **player empaquetado** — <c>dataPath</c> = <c>&lt;build&gt;/&lt;Nombre&gt;_Data</c>:
        ///     subir desde la carpeta del build hasta el marcador del repo.
        /// Un player lanzado desde fuera del repo (doble clic en un build
        /// exportado) no tiene ancla: se queda con el cwd, que es el
        /// comportamiento anterior.
        /// </summary>
        public static string? RepoRootFromDataPath(string? dataPath)
        {
            string? fromProject = RepoRootFromProjectPath(dataPath);
            if (fromProject != null) return fromProject;
            if (string.IsNullOrEmpty(dataPath)) return null;

            string start = dataPath;
            if (start.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(start);
                if (parent != null) start = parent.FullName;
            }
            return RepoRootByMarker(start);
        }

        /// <summary>
        /// Resuelve <paramref name="path"/>: absoluto → tal cual; relativo →
        /// existe contra <paramref name="baseDir"/> (cwd del proceso) o contra
        /// <paramref name="repoRoot"/>. Devuelve la PRIMERA variante existente
        /// (cwd gana: permite overrides locales); si no existe en ningún sitio,
        /// devuelve la variante repo-root (el error de Process.Start será claro
        /// y menciona la ruta canónica del repo).
        /// </summary>
        public static string Resolve(string path, string? baseDir, string? repoRoot)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (Path.IsPathRooted(path)) return path;

            string inBase = baseDir != null ? Path.Combine(baseDir, path) : path;
            if (File.Exists(inBase)) return inBase;

            if (repoRoot != null)
            {
                // Normalizar separadores: Path.Combine no los corrige y un
                // "artifacts/x" unido a base Windows produce mezcla /\.
                string inRepo = Path.GetFullPath(Path.Combine(repoRoot, path));
                if (File.Exists(inRepo)) return inRepo;
                return inRepo; // no existe en ningún sitio: la ruta canónica
            }
            return Path.GetFullPath(inBase);
        }

        /// <summary>
        /// Resuelve el ejecutable igual que <see cref="Resolve"/> probando DOS
        /// formas por variante (cwd primero, repo después), con sufijo ".exe"
        /// SOLO en Windows:
        ///   1. la ruta ES el ejecutable  → <c>build/antsim</c> + ".exe";
        ///   2. la ruta ES la carpeta de publicación → el ejecutable vive DENTRO
        ///      con el nombre del último segmento: <c>build/antsim/antsim[.exe]</c>
        ///      (layout real de <c>dotnet publish -o build/antsim</c>).
        /// Si no existe nada, devuelve la variante canónica: en Windows la
        /// plana con sufijo (<c>build/antsim.exe</c>); en Linux la de carpeta
        /// (<c>build/antsim/antsim</c>) — el error de Process.Start será claro
        /// en ambos.
        /// </summary>
        public static string ResolveExecutable(string path, string? baseDir, string? repoRoot)
        {
            if (string.IsNullOrEmpty(path)) return path;
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string ext = isWindows && !Path.HasExtension(path) ? ".exe" : "";

            string leaf = Path.GetFileName(path.TrimEnd('/', '\\'));

            // Variante cwd (baseDir): ejecutable plano y dentro de la carpeta.
            string c1 = Path.GetFullPath(baseDir != null ? Path.Combine(baseDir, path) : path) + ext;
            if (File.Exists(c1)) return c1;
            string c1d = Path.GetFullPath(Path.Combine(
                baseDir != null ? Path.Combine(baseDir, path) : path, leaf)) + ext;
            if (File.Exists(c1d)) return c1d;

            // Variante repo root: mismas dos formas.
            if (repoRoot != null)
            {
                string c2 = Path.GetFullPath(Path.Combine(repoRoot, path)) + ext;
                if (File.Exists(c2)) return c2;
                string c2d = Path.GetFullPath(Path.Combine(repoRoot, path, leaf)) + ext;
                if (File.Exists(c2d)) return c2d;
            }

            // Nada existe: variante canónica (error claro). Windows: plana con
            // sufijo. Linux: dentro de la carpeta de publicación.
            return repoRoot != null
                ? (isWindows
                    ? Path.GetFullPath(Path.Combine(repoRoot, path)) + ext
                    : Path.GetFullPath(Path.Combine(repoRoot, path, leaf)))
                : (isWindows ? c1 : c1d);
        }
    }
}
