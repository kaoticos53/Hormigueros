using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Fuente del stream (F4.1): lanza el CLI (<c>--mode game</c>) y entrega sus
    /// líneas al presenter, o lee un stream previamente volcado a archivo.
    /// Aislar Process aquí permite que TODO lo demás sea puro y testeable headless.
    /// </summary>
    public sealed class StreamSource
    {
        private readonly string _cliPath;

        /// <param name="cliPath">Ruta del CLI: absoluta, relativa al cwd o
        /// relativa al repo root (p. ej. "build/antsim").</param>
        /// <param name="repoRoot">Ancla del repo root para rutas relativas
        /// (Play-pass: el cwd del editor es la carpeta del proyecto, no la del
        /// repo). null = comportamiento de siempre (solo cwd).</param>
        public StreamSource(string cliPath, string? repoRoot = null)
            => _cliPath = RepoPathResolver.ResolveExecutable(cliPath, baseDir: null, repoRoot);

        /// <summary>Ejecuta el CLI y bombea cada línea a <paramref name="onLine"/> (bloqueante).
        /// <paramref name="extraArgs"/> transporta el plan de intervención (F4.4):
        /// pares --drop tick:x:y ya validados por DropFoodPlanModel.
        /// Canales opt-in (F4.5/F5.0): pheroEvery (canal E), activEvery + inspectId
        /// (canal F) — telemetría pura, el hash del mundo no cambia.</summary>
        public void StreamGame(ulong seed, int ticks, int grid, int colonies, int frameEvery,
            string? seedPoolPath, Action<string> onLine, IReadOnlyList<string>? extraArgs = null,
            int pheroEvery = 0, int activEvery = 0, uint inspectId = 0)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--mode"); psi.ArgumentList.Add("game");
            psi.ArgumentList.Add("--seed"); psi.ArgumentList.Add(seed.ToString());
            psi.ArgumentList.Add("--ticks"); psi.ArgumentList.Add(ticks.ToString());
            psi.ArgumentList.Add("--grid"); psi.ArgumentList.Add(grid.ToString());
            psi.ArgumentList.Add("--colonies"); psi.ArgumentList.Add(colonies.ToString());
            psi.ArgumentList.Add("--frame-every"); psi.ArgumentList.Add(frameEvery.ToString());
            if (seedPoolPath != null)
            { psi.ArgumentList.Add("--seed-pool"); psi.ArgumentList.Add(seedPoolPath); }
            if (pheroEvery > 0)
            { psi.ArgumentList.Add("--phero-every"); psi.ArgumentList.Add(pheroEvery.ToString()); }
            if (activEvery > 0)
            {
                psi.ArgumentList.Add("--activ-every"); psi.ArgumentList.Add(activEvery.ToString());
                if (inspectId > 0)
                { psi.ArgumentList.Add("--inspect"); psi.ArgumentList.Add(inspectId.ToString()); }
            }
            if (extraArgs != null)
            {
                foreach (string a in extraArgs)
                    psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi)!;
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
                onLine(line);
            proc.WaitForExit();
        }

        /// <summary>Carga un stream volcado a archivo (debug/replay sin CLI).</summary>
        public void StreamFile(string path, Action<string> onLine)
        {
            foreach (string line in File.ReadLines(path))
                onLine(line);
        }

        /// <summary>Ruta del CLI ya resuelta (tests y diagnóstico).</summary>
        public string CliPath => _cliPath;

        /// <summary>Descarga las tarjetas del picker (JSON canónico del CLI).</summary>
        public string FetchPresetsJson()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--mode"); psi.ArgumentList.Add("presets");
            psi.ArgumentList.Add("--json");
            using var proc = Process.Start(psi)!;
            return proc.StandardOutput.ReadToEnd();
        }
    }
}
