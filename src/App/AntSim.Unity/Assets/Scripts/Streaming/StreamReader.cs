using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private volatile bool _stopRequested;

        /// <summary>
        /// Detiene el CLI en curso (F6). Sin esto, parar Play con una partida de
        /// 2 h deja un proceso huérfano bloqueado escribiendo en una tubería que
        /// ya nadie lee.
        /// </summary>
        public void Stop()
        {
            _stopRequested = true;
            var proc = _live;
            if (proc == null) return;
            // `Kill()` sin árbol: la sobrecarga con `entireProcessTree` NO existe
            // en el perfil de .NET de Unity 6000 (error CS1739 al importar el
            // script). El CLI no lanza hijos, así que el árbol no hace falta.
            try { if (!proc.HasExited) proc.Kill(); }
            catch (Exception) { /* ya terminó o el handle se fue con él */ }
        }

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
            int pheroEvery = 0, int activEvery = 0, uint inspectId = 0, string? species = null,
            bool maze = false, string? exportPath = null, bool untilExtinct = false,
            bool recruitment = false, RecruitmentKnobs? recruitmentKnobs = null)
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
            if (maze)
                psi.ArgumentList.Add("--maze");
            if (!string.IsNullOrWhiteSpace(exportPath))
            { psi.ArgumentList.Add("--export"); psi.ArgumentList.Add(exportPath); }
            // F8: con esto el CLI cierra la ronda cuando no queda ninguna hormiga ni
            // cría y exporta la élite EN ESE MOMENTO — la semilla de la ronda nueva.
            if (untilExtinct)
                psi.ArgumentList.Add("--until-extinct");
            // F8: cúmulos de comida + reflejos de rastro. Con la bandera, el canal E
            // tiene algo que enseñar: el rastro de COMIDA (no solo el de casa).
            if (recruitment)
            {
                psi.ArgumentList.Add("--recruitment");
                // F8: montones y rastro. Se envían SOLO si difieren del default del
                // mundo: con los valores de fábrica la línea de comandos queda
                // idéntica a la de siempre (y los pines del stream no se tocan).
                if (recruitmentKnobs != null && !recruitmentKnobs.IsDefault)
                {
                    psi.ArgumentList.Add("--clusters");
                    psi.ArgumentList.Add(recruitmentKnobs.Clusters.ToString(CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("--cluster-size");
                    psi.ArgumentList.Add(recruitmentKnobs.ClusterMin.ToString(CultureInfo.InvariantCulture) + ":" +
                                        recruitmentKnobs.ClusterMax.ToString(CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("--cluster-radius");
                    psi.ArgumentList.Add(recruitmentKnobs.ClusterRadius.ToString("0.###", CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("--trail-deposit");
                    psi.ArgumentList.Add(recruitmentKnobs.TrailDeposit.ToString("0.###", CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("--trail-half-life");
                    psi.ArgumentList.Add(recruitmentKnobs.TrailHalfLife.ToString("0.###", CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("--trail-follow");
                    psi.ArgumentList.Add(recruitmentKnobs.TrailFollow.ToString("0.###", CultureInfo.InvariantCulture));
                    // F11n: el PAR de la puerta. La puerta sola no paga sobre el pool
                    // de fábrica y el precio solo borra la señal, así que viajan
                    // juntas y solo cuando el lanzador las pide: sin ellas la línea
                    // de comandos es la de siempre y el mundo no cambia ni un byte.
                    if (recruitmentKnobs.TrailByBrain)
                        psi.ArgumentList.Add("--trail-by-brain");
                    if (recruitmentKnobs.TrailGlandSeconds > 0f)
                    {
                        psi.ArgumentList.Add("--trail-gland");
                        psi.ArgumentList.Add(recruitmentKnobs.TrailGlandSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                        psi.ArgumentList.Add("--trail-gland-refill");
                        psi.ArgumentList.Add(recruitmentKnobs.TrailGlandRefillSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }
            }
            if (pheroEvery > 0)
            {
                psi.ArgumentList.Add("--phero-every"); psi.ArgumentList.Add(pheroEvery.ToString());
                // F8: se piden las CUATRO capas de cada colonia —comida, casa,
                // alarma y huella—, porque el tablero las pinta a la vez con un
                // color por tipo. Antes solo viajaban food y home y el render
                // enseñaba una cada vez (F ciclaba); con todas en el canal E el
                // espectador ve el rastro completo sin teclas y sin alternar.
                if (recruitment)
                {
                    psi.ArgumentList.Add("--phero-layers");
                    var spec = new System.Text.StringBuilder();
                    for (int c = 0; c < colonies; c++)
                    {
                        if (spec.Length > 0) spec.Append(',');
                        spec.Append(c).Append(":food,").Append(c).Append(":home,")
                            .Append(c).Append(":alarm,").Append(c).Append(":footprint");
                    }
                    psi.ArgumentList.Add(spec.ToString());
                }
            }
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
                while (!_stopRequested && (line = proc.StandardOutput.ReadLine()) != null)
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

        /// <summary>
        /// F7 — JSON canónico de `--mode genome-info` para un pool: trae la
        /// topología (`sizes`) y la tarjeta de cuarentena. Lo usa el inspector para
        /// decodificar el canal F de un cerebro más ancho que el clásico.
        /// </summary>
        public string FetchGenomeInfoJson(string genomePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--mode"); psi.ArgumentList.Add("genome-info");
            psi.ArgumentList.Add("--import"); psi.ArgumentList.Add(genomePath);
            psi.ArgumentList.Add("--json");
            using var proc = Process.Start(psi)!;
            string json = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return json;
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
