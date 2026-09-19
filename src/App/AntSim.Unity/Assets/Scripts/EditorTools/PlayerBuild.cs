#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// F5.3 — BUILD del PLAYER que se mide. El criterio de salida de la fase pide
    /// «N colonias estables a 60 fps» y el framerate PRESENTADO (vsync del monitor)
    /// solo existe en un build: en el editor el vsync no llega a aplicarse (medido:
    /// vSyncCount=1 y 403,8 fps, dentro del 1 % de la corrida con vsync apagado).
    ///
    /// QUÉ MONTA. Regenera la escena del multi-visor con la rejilla pedida (la misma
    /// que usa el medidor del editor) y construye un player que ARRANCA en ella: las
    /// cuatro vistas con sus dos colonias cada una son el caso de carga del criterio.
    /// El build va a <c>build/player/</c> (ignorado por git, como el resto de build/).
    ///
    /// DOS MODOS DE MEDIDA, DOS BUILDES. El defecto mide el framerate PRESENTADO
    /// (vsync del monitor). Con <c>ANTSIM_PERF_FRAME_TIMING=1</c> el build además
    /// enciende los estadísticos de tiempos de frame (`enableFrameTimingStats`), que
    /// es lo que necesita la sonda para medir el COSTE por frame desde dentro sin
    /// inventar nada. Ese ajuste se RESTAURA antes de salir: es un ajuste del
    /// proyecto (vive en ProjectSettings) y la corrida de coste no debe dejarlo
    /// cambiado para la siguiente.
    ///
    /// Entrada: <c>-executeMethod AntSim.Unity.Scripts.EditorTools.PlayerBuild.BuildMultiViewPlayer</c>.
    /// Variables de entorno: <c>ANTSIM_BUILD_OUT</c> (def. build/player/AntSim.exe),
    /// <c>ANTSIM_BUILD_SCENE</c> (def. la escena del multi-visor),
    /// <c>ANTSIM_PERF_GRID</c> (def. 256),
    /// <c>ANTSIM_PERF_TICKS</c> (def. 12 000, el horizonte del criterio),
    /// <c>ANTSIM_PERF_FRAME_TIMING</c> (1 = enciende los tiempos de frame del build).
    ///
    /// Salida: 0 si el build terminó con éxito, 1 si falló (con el resumen del
    /// BuildReport en el log: los bytes y, si falla, los errores uno a uno).
    /// </summary>
    public static class PlayerBuild
    {
        private const string Tag = "[PlayerBuild]";
        private const string MultiSimScene = "Assets/Scenes/MultiSim.unity";

        public static void BuildMultiViewPlayer()
        {
            string? repo = RepoRoot();
            if (repo == null)
            {
                Debug.Log($"{Tag} ✗ no se encontró la raíz del repo (marcador src/Tools/AntSim.Cli)");
                EditorApplication.Exit(1);
                return;
            }

            int grid = (int)EnvFloat("ANTSIM_PERF_GRID", 256f);
            // El horizonte del medidor es un parámetro (def. el del criterio): la
            // medida del SISTEMA necesita un mundo que siga vivo durante la ventana.
            int ticks = (int)EnvFloat("ANTSIM_PERF_TICKS", MultiSimBootstrapper.PerfTicks);
            bool frameTiming = EnvFloat("ANTSIM_PERF_FRAME_TIMING", 0f) >= 0.5f;
            string scenePath = Environment.GetEnvironmentVariable("ANTSIM_BUILD_SCENE") ?? MultiSimScene;
            string outPath = Environment.GetEnvironmentVariable("ANTSIM_BUILD_OUT") ?? "";
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine("build", "player", "AntSim.exe");
            if (!Path.IsPathRooted(outPath)) outPath = Path.GetFullPath(Path.Combine(repo, outPath));

            // La escena que se va a medir: la misma que monta el medidor del editor.
            MultiSimBootstrapper.CreateMultiSimPerfScene(grid, ticks);

            var scenes = new List<string> { scenePath };
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            // Los tiempos de frame del build: el ajuste lo LEE el build, así que se
            // enciende antes de construirlo y se devuelve a su valor al terminar (si
            // no, quedaría escrito en ProjectSettings para siempre).
            bool prevFrameTiming = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = frameTiming;

            Debug.Log($"{Tag} construyendo {outPath} · escena {scenePath} · grid {grid}² · " +
                      $"horizonte {ticks} ticks · " +
                      $"backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)}" +
                      $" · tiempos de frame={PlayerSettings.enableFrameTimingStats}");
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.enableFrameTimingStats = prevFrameTiming;
            }
            BuildSummary summary = report.summary;

            Debug.Log($"{Tag} resultado={summary.result} · tamaño={summary.totalSize / (1024 * 1024)} MB · " +
                      $"errores={summary.totalErrors} · avisos={summary.totalWarnings} · " +
                      $"tiempo={(int)summary.totalTime.TotalSeconds}s");
            if (summary.result != BuildResult.Succeeded)
            {
                foreach (string step in FailedSteps(report))
                    Debug.Log($"{Tag}   ✗ {step}");
                var errors = new List<string>();
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            errors.Add(msg.content);
                for (int i = 0; i < Math.Min(10, errors.Count); i++)
                    Debug.Log($"{Tag}   error: {errors[i]}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"{Tag} ✓ build listo: {outPath}");
            EditorApplication.Exit(0);
        }

        private static IEnumerable<string> FailedSteps(BuildReport report)
        {
            foreach (var step in report.steps)
                if (step.messages != null)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            yield return $"{step.name}: {msg.content}";
        }

        /// <summary>Raíz del repo subiendo desde el proyecto Unity (el marcador es la
        /// carpeta del CLI, no un README: el proyecto también tiene el suyo).</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(Application.dataPath);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Tools", "AntSim.Cli")))
                dir = dir.Parent;
            return dir?.FullName;
        }

        private static float EnvFloat(string name, float fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return raw != null && float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f
                ? v : fallback;
        }
    }
}
#endif
