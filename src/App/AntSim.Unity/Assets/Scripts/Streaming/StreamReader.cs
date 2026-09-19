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

        /// <summary>Proceso del CLI EN CURSO (F5.3, coste del SISTEMA). El player
        /// solo puede cronometrar al hijo mientras tiene su handle, y el motor del
        /// stream vive en otro hilo: aquí se publica de forma atómica y se retira al
        /// terminar.</summary>
        private volatile Process? _live;
        /// <summary>CPU del hijo al cerrar: el proceso ya no está, pero su coste
        /// total sí (dejarlo a 0 diría que la simulación fue gratis).</summary>
        private double _cliCpuMsClosed;

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
            int pheroEvery = 0, int activEvery = 0, uint inspectId = 0, string? species = null)
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
            if (!string.IsNullOrWhiteSpace(species))
            { psi.ArgumentList.Add("--species"); psi.ArgumentList.Add(species); }
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
            _live = proc;
            try
            {
                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    onLine(line);
            }
            finally
            {
                // El coste del CLI se congela ANTES de soltar el handle y de
                // disponer el objeto: después ya no hay de dónde leerlo.
                _cliCpuMsClosed = ReadCpuMs(proc, _cliCpuMsClosed);
                _live = null;
            }
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

        /// <summary>
        /// CPU consumida por el CLI (ms, todos sus hilos) hasta este instante. Es la
        /// pata que falta para medir el coste del SISTEMA COMPLETO: el mundo se
        /// simula en OTRO proceso y su trabajo no aparece en ningún tiempo de frame
        /// del player (F5.3, 6ª pieza del criterio).
        ///
        /// Si el CLI ya terminó devuelve su CPU final, no 0: un 0 significa «no
        /// trabajó» y se leería como que simular es gratis. Quien mide tiene que
        /// mirar además si los TICKS avanzaron — es el contraste que distingue «ya
        /// terminó» de «no está trabajando».
        /// </summary>
        public double CliCpuMs
        {
            get
            {
                Process? proc = _live;
                if (proc == null) return _cliCpuMsClosed;
                return ReadCpuMs(proc, _cliCpuMsClosed);
            }
        }

        /// <summary>CPU del proceso en ms, o el último valor conocido si el sistema
        /// ya no la deja leer (el hijo murió y el handle se fue con él).</summary>
        private static double ReadCpuMs(Process proc, double fallback)
        {
            try { return proc.TotalProcessorTime.TotalMilliseconds; }
            catch (Exception) { return fallback; }
        }

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
