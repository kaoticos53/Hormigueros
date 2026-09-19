using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.3 — el coste de un frame DENTRO del build, en los términos del
    /// <c>FrameTimingManager</c> de Unity (F5.3, 5ª pieza del criterio de cierre).
    ///
    /// POR QUÉ HACÍA FALTA. El build ya está medido por su framerate presentado
    /// (§4.7: 59,99 fps al refresco), pero ese número lo pone la PANTALLA: con el
    /// vsync entregando al refresco del monitor, el coste del frame queda tapado
    /// (60 fps es el techo del monitor, no del juego). Para publicar el coste hay
    /// que medirlo en el único sitio donde el juego no espera a nadie: dentro del
    /// player, con el vsync apagado, y cronometrando la CPU desde dentro.
    ///
    /// Los nombres y el significado son los de la API (documentación de
    /// <c>FrameTiming</c>), no una invención de este proyecto:
    ///   <c>CpuMs</c>      cpuFrameTime — tiempo TOTAL de CPU del frame (incluye
    ///                     esperas y sobrecarga: es la duración real del frame en
    ///                     la CPU cuando no hay nada que la frene).
    ///   <c>MainMs</c>     cpuMainThreadFrameTime — trabajo del hilo principal.
    ///   <c>RenderMs</c>   cpuRenderThreadFrameTime — del primer envío al hilo de
    ///                     render hasta que se llama a Present.
    ///   <c>PresentWaitMs</c> cpuMainThreadPresentWaitTime — espera del hilo
    ///                     principal en Present (vsync o techo de fps).
    ///   <c>GpuMs</c>      gpuFrameTime — trabajo de GPU del frame (0 si la
    ///                     plataforma no expone tiempos de GPU).
    ///
    /// Sin UnityEngine: este fichero se compila también en la suite headless (ver
    /// el .csproj de tests), así que la clasificación y el veredicto que se
    /// publican en los documentos quedan probados con números hechos a mano.
    /// </summary>
    public readonly struct FrameTimingSample
    {
        public readonly double CpuMs;
        public readonly double MainMs;
        public readonly double RenderMs;
        public readonly double PresentWaitMs;
        public readonly double GpuMs;

        public FrameTimingSample(double cpuMs, double mainMs, double renderMs,
            double presentWaitMs, double gpuMs)
        {
            CpuMs = cpuMs; MainMs = mainMs; RenderMs = renderMs;
            PresentWaitMs = presentWaitMs; GpuMs = gpuMs;
        }
    }

    /// <summary>Una ventana de muestreo (1 s) resumida: los frames contados de
    /// verdad entre muestras y la MEDIANA de los tiempos de cada frame dentro de
    /// esa ventana (no la media: una sola muestra desviada —una recolección de
    /// basura, un pico del sistema— no debe mover el número publicado).</summary>
    public readonly struct FrameCostWindow
    {
        public readonly double Seconds;
        public readonly long Frames;
        public readonly FrameTimingSample Timing;

        public FrameCostWindow(double seconds, long frames, FrameTimingSample timing)
        {
            Seconds = seconds; Frames = frames; Timing = timing;
        }

        /// <summary>ms/frame alcanzados en la ventana: lo que tardó de verdad el
        /// bucle en producir un frame (techo de producción sin vsync).</summary>
        public double AchievedMs => Frames > 0 && Seconds > 0 ? Seconds * 1000.0 / Frames : 0.0;
    }

    /// <summary>Qué frena el frame, según la clasificación documentada por Unity
    /// (el ejemplo de <c>FrameTiming.DetermineBottleneck</c>).</summary>
    public enum FrameBottleneck : byte
    {
        /// <summary>No se puede determinar: la plataforma no da tiempos de GPU.</summary>
        Indeterminate = 0,
        /// <summary>Limitado por la presentación (vsync o techo de fps): con el
        /// vsync apagado esto sería una medida mal hecha.</summary>
        PresentLimited = 1,
        /// <summary>Limitado por CPU (hilo principal y/o de render).</summary>
        Cpu = 2,
        /// <summary>Limitado por GPU.</summary>
        Gpu = 3,
        /// <summary>Equilibrado: CPU y GPU se reparten el frame.</summary>
        Balanced = 4,
    }

    public sealed class FrameCostSummary
    {
        /// <summary>Ventanas usadas en el resumen.</summary>
        public int Windows;
        /// <summary>Frames contados en total (los reales, no los supuestos).</summary>
        public long Frames;
        /// <summary>frames/s sin vsync: el TECHO de producción del build.</summary>
        public double FpsMedian;
        public double AchievedMsMedian;
        /// <summary>Mediana de cpuFrameTime por frame (el coste de CPU).</summary>
        public double CpuMsMedian;
        public double MainMsMedian;
        public double RenderMsMedian;
        public double PresentWaitMsMedian;
        public double GpuMsMedian;
        /// <summary>false si la plataforma no expone tiempos de GPU (GpuMs = 0).</summary>
        public bool GpuAvailable;
        /// <summary>Presupuesto de un frame al objetivo (1000/objetivo).</summary>
        public double BudgetMs;
        /// <summary>Fracción del presupuesto que consume la CPU (0.15 = 15 %).</summary>
        public double CpuFractionOfBudget;
        /// <summary>Cuántas veces cabe el presupuesto en el coste de CPU (×1.8).</summary>
        public double Headroom;
        /// <summary>El coste de CPU cabe en el presupuesto del objetivo.</summary>
        public bool Fits;
        public FrameBottleneck Bottleneck;
        /// <summary>Veredicto legible (ASCII: lo consumen scripts y documentos).</summary>
        public string Verdict = "";

        public bool Measured => Windows > 0 && CpuMsMedian > 0.0;
    }

    /// <summary>
    /// Una ventana del SISTEMA COMPLETO (F5.3, 6ª pieza del criterio): lo que
    /// costó producir esos frames CONTANDO el CLI que simula el mundo en OTRO
    /// proceso.
    ///
    /// POR QUÉ NO BASTA CON EL COSTE DEL PLAYER. El player lee el stream y dibuja,
    /// pero el mundo lo simula un proceso aparte; publicar solo su frame deja fuera
    /// la mitad —o más— del trabajo que la máquina hace para que ese frame exista.
    /// Y no es un adorno: la simulación compite por los mismos núcleos, así que una
    /// pata del CLI grande es margen que el juego NO tiene.
    ///
    /// DE DÓNDE SALE CADA PATA, Y POR QUÉ NO SALEN DE LA MISMA VENTANA.
    ///
    /// El coste del PLAYER es la mediana de `cpuFrameTime` de las ventanas con el
    /// mundo YA en pantalla (el mismo dato de la 5ª pieza): un frame de tablero
    /// todavía vacío no es un frame del juego.
    ///
    /// El precio del CLI NO se puede sacar de ventanas, y esto se midió antes de
    /// decidirlo. Este CLI simula el horizonte ENTERO y escribe el stream cuando
    /// termina, así que su CPU y la ENTREGA de ticks están DESACOPLADAS: con 4
    /// vistas, 6 de los 9,8 s de CPU del hijo ya estaban gastados a los 2 s, cuando
    /// el player no había recibido ni un tick. Emparejar CPU y ticks por ventana daba
    /// 0,016 ms/tick frente a los 0,2 reales. Por eso el precio del tick es el
    /// AGREGADO de la vida entera del hijo (toda su CPU entre todos los ticks que
    /// simuló), y la medida exige que el CLI haya TERMINADO su horizonte dentro de la
    /// corrida: si no, su CPU es parcial y la cuenta mentiría.
    ///
    /// Lo que queda ventana a ventana es la información de FASES (cuántas hubo con el
    /// CLI trabajando, cuántas con el mundo en pantalla y cuánto se solapan: 0 es lo
    /// normal en esta arquitectura). Sumar dos patas de fases distintas es la única
    /// lectura honesta de este sistema, y decirlo forma parte del número.
    /// </summary>
    public readonly struct SystemWindow
    {
        public readonly double Seconds;
        public readonly long Frames;
        /// <summary>Coste del frame del player (mediana de la ventana), con el
        /// tablero que hubiera: qué ventanas valen lo decide el resumen.</summary>
        public readonly double PlayerMsPerFrame;
        /// <summary>CPU del/los CLI dentro de la ventana (ms, todos sus hilos).</summary>
        public readonly double CliCpuMs;
        /// <summary>Ticks simulados por el/los CLI dentro de la ventana.</summary>
        public readonly long Ticks;
        /// <summary>Había mundo en pantalla (hormigas dibujadas) en esa ventana.</summary>
        public readonly bool WorldOnScreen;

        public SystemWindow(double seconds, long frames, double playerMsPerFrame,
            double cliCpuMs, long ticks, bool worldOnScreen)
        {
            Seconds = seconds; Frames = frames; PlayerMsPerFrame = playerMsPerFrame;
            CliCpuMs = cliCpuMs; Ticks = ticks; WorldOnScreen = worldOnScreen;
        }

        public double CliMsPerFrame => Frames > 0 ? CliCpuMs / Frames : 0.0;
        public double CliMsPerTick => Ticks > 0 ? CliCpuMs / Ticks : 0.0;

        /// <summary>
        /// El CLI estaba TRABAJANDO. Si no (mundo terminado, o proceso muerto) su CPU
        /// no crece y su coste por frame saldría 0 — que se leería como «simular es
        /// gratis», justo lo contrario de lo que hay que publicar.
        /// </summary>
        public bool Simulated => Ticks > 0 && CliCpuMs > 0.0;

        /// <summary>El player dibujando el MUNDO: un frame de tablero aún vacío no
        /// es un frame del juego, y usarlo como pata del player daría un coste de
        /// vista que nadie va a experimentar.</summary>
        public bool Loaded => WorldOnScreen && PlayerMsPerFrame > 0.0;
    }

    /// <summary>Resumen del SISTEMA COMPLETO (F5.3, 6ª pieza del criterio).</summary>
    public sealed class SystemCostSummary
    {
        public int Windows;
        /// <summary>Ventanas en las que la CPU del CLI creció (simulaba).</summary>
        public int SimulatedWindows;
        /// <summary>Ventanas en las que el player dibujaba el mundo ya cargado.</summary>
        public int LoadedWindows;
        /// <summary>Ventanas en las que pasaban las dos cosas a la vez: con el stream
        /// en ráfaga esto sale 0, y ese 0 es parte del resultado.</summary>
        public int OverlapWindows;
        /// <summary>Frames producidos durante la fase de simulación.</summary>
        public long Frames;
        /// <summary>Ticks entregados durante la fase de simulación.</summary>
        public long Ticks;
        /// <summary>La pata del player: mediana de las ventanas con el mundo cargado.</summary>
        public double PlayerMsPerFrame;
        /// <summary>CPU del CLI de TODAS las vistas y de TODA su vida (ms).</summary>
        public double CliCpuMsTotal;
        /// <summary>Ticks que simularon los CLI (todas las vistas).</summary>
        public long CliTicks;
        /// <summary>EL PRECIO DEL TICK: CPU del CLI entre ticks simulados. Es el
        /// número que se lleva a cualquier régimen, porque no depende del framerate
        /// ni de cuánto tardó el stream en llegar.</summary>
        public double CliMsPerTick;
        /// <summary>Ticks que avanza un frame en el régimen que se publica (60 fps
        /// con el boost de la corrida), sumando TODAS las vistas: es un dato del
        /// montaje y se pasa de fuera.</summary>
        public double TicksPerPresentedFrame;
        /// <summary>La pata del mundo por frame: los ticks del frame a su precio.</summary>
        public double CliMsPerFrame;
        /// <summary>EL NÚMERO DE LA PIEZA: el frame del player con el mundo cargado
        /// MÁS el mundo que ese frame muestra, costeado al precio medido por tick.</summary>
        public double SystemMsPerFrame;
        /// <summary>Qué parte del sistema es el CLI (0,97 = el 97 % de la CPU del
        /// sistema se va en simular).</summary>
        public double CliShareOfSystem;
        /// <summary>CPU de la máquina por frame en la corrida entera (las dos fases
        /// mezcladas): informativo, y SÍ depende de cuánto durase cada fase.</summary>
        public double AggregateMsPerFrame;
        public double BudgetMs;
        /// <summary>La pata del player contra el presupuesto (el lado de los fps).</summary>
        public double PlayerFractionOfBudget;
        public double PlayerHeadroom;
        /// <summary>El frame del player cabe en el presupuesto: la vista va sobrada.</summary>
        public bool PlayerFits;
        public double SystemFractionOfBudget;
        /// <summary>NÚCLEOS EQUIVALENTES que pide el sistema al ritmo del juego:
        /// SystemMsPerFrame / presupuesto. Es la lectura correcta de un coste
        /// AGREGADO —la simulación corre en otros procesos y se reparte entre
        /// núcleos—, mientras que la pata del player sí tiene que caber en UN frame.
        public double SystemCores;
        /// <summary>Los CLI terminaron su horizonte dentro de la corrida: su CPU es
        /// la de todo el mundo simulado y el precio por tick vale.</summary>
        public bool CliCompleted;
        /// <summary>Las DOS patas están medidas: el número es del sistema y no de un
        /// player solo (o de un CLI solo).</summary>
        public bool Measured;
        public string Verdict = "";
    }

    public static class FrameCost
    {
        /// <summary>Objetivo del criterio de cierre de F5.3.</summary>
        public const double DefaultTargetFps = 60.0;

        /// <summary>Cerca del frame completo (el 20 % de margen del ejemplo oficial).</summary>
        public const double NearFullFrameThreshold = 0.2;

        /// <summary>Ventanas mínimas de cada clase (simulación y mundo en pantalla)
        /// para dar el coste del SISTEMA por medido: una mediana de una sola ventana
        /// no es una mediana, es una muestra.</summary>
        public const int MinimumWindowsForALeg = 3;
        /// <summary>Por debajo de esto la espera en Present es ruido, no un techo.</summary>
        public const double NonZeroPresentWaitMs = 0.5;

        /// <summary>
        /// Mediana ignorando los ceros: en las series de tiempos un 0 significa
        /// «este frame no dejó dato» (el FrameTimingManager entrega los tiempos con
        /// retardo), no «costó 0 ms» — contarlo hundiría el número.
        /// </summary>
        public static double Median(IReadOnlyList<double> values)
        {
            var v = new List<double>();
            for (int i = 0; i < values.Count; i++)
                if (values[i] > 0.0) v.Add(values[i]);
            if (v.Count == 0) return 0.0;
            v.Sort();
            return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        /// <summary>Clasificación de cuello de botella: la del ejemplo de la
        /// documentación de <c>FrameTiming</c>, con los mismos umbrales.</summary>
        public static FrameBottleneck Classify(FrameTimingSample s,
            double nearFullFrameThresholdPercent = NearFullFrameThreshold,
            double nonZeroPresentWaitMs = NonZeroPresentWaitMs)
        {
            // Sin tiempos de GPU no se puede decidir: es el caso de las plataformas
            // que no los exponen (y el de la sonda si el cálculo no mide GPU).
            if (s.GpuMs <= 0.0) return FrameBottleneck.Indeterminate;

            double margin = (1.0 - nearFullFrameThresholdPercent) * s.CpuMs;

            if (s.GpuMs > margin && s.MainMs < margin && s.RenderMs < margin)
                return FrameBottleneck.Gpu;

            if (s.GpuMs < margin && (s.MainMs > margin || s.RenderMs > margin))
                return FrameBottleneck.Cpu;

            if (s.PresentWaitMs > nonZeroPresentWaitMs
                && s.GpuMs < margin && s.MainMs < margin && s.RenderMs < margin)
                return FrameBottleneck.PresentLimited;

            return FrameBottleneck.Balanced;
        }

        /// <summary>
        /// Resumen de las ventanas medidas. La mediana del framerate sale de las
        /// ventanas; el coste de CPU es la mediana de las medianas por ventana (con
        /// 30 ventanas de 1 s eso es un número estable, que es lo que se publica).
        /// </summary>
        public static FrameCostSummary Summarize(IReadOnlyList<FrameCostWindow> windows,
            double targetFps = DefaultTargetFps)
        {
            var summary = new FrameCostSummary
            {
                Windows = windows.Count,
                BudgetMs = targetFps > 0.0 ? 1000.0 / targetFps : 0.0,
            };

            var fps = new List<double>();
            var achieved = new List<double>();
            var cpu = new List<double>();
            var main = new List<double>();
            var render = new List<double>();
            var wait = new List<double>();
            var gpu = new List<double>();
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if (w.Frames > 0 && w.Seconds > 0)
                {
                    fps.Add(w.Frames / w.Seconds);
                    achieved.Add(w.AchievedMs);
                }
                summary.Frames += w.Frames;
                cpu.Add(w.Timing.CpuMs);
                main.Add(w.Timing.MainMs);
                render.Add(w.Timing.RenderMs);
                wait.Add(w.Timing.PresentWaitMs);
                gpu.Add(w.Timing.GpuMs);
            }

            summary.FpsMedian = Median(fps);
            summary.AchievedMsMedian = Median(achieved);
            summary.CpuMsMedian = Median(cpu);
            summary.MainMsMedian = Median(main);
            summary.RenderMsMedian = Median(render);
            summary.PresentWaitMsMedian = Median(wait);
            summary.GpuMsMedian = Median(gpu);
            summary.GpuAvailable = summary.GpuMsMedian > 0.0;
            if (summary.BudgetMs > 0.0 && summary.CpuMsMedian > 0.0)
            {
                summary.CpuFractionOfBudget = summary.CpuMsMedian / summary.BudgetMs;
                summary.Headroom = summary.BudgetMs / summary.CpuMsMedian;
            }
            summary.Fits = summary.BudgetMs > 0.0 && summary.CpuMsMedian > 0.0
                && summary.CpuMsMedian <= summary.BudgetMs;
            summary.Bottleneck = summary.Measured
                ? Classify(new FrameTimingSample(summary.CpuMsMedian, summary.MainMsMedian,
                    summary.RenderMsMedian, summary.PresentWaitMsMedian, summary.GpuMsMedian))
                : FrameBottleneck.Indeterminate;
            summary.Verdict = Verdict(summary);
            return summary;
        }

        private static string Verdict(FrameCostSummary s)
        {
            if (!s.Measured) return "sin datos de CPU (no se pudo cronometrar el frame)";
            if (!s.Fits) return "no cabe en el presupuesto de la CPU";
            return "cabe: la CPU deja margen al objetivo";
        }

        /// <summary>
        /// Resumen del SISTEMA COMPLETO: el frame del player MÁS la simulación que
        /// ese frame arrastra. `ticksPerPresentedFrame` es cuántos ticks entran en un
        /// frame del régimen que se quiere publicar (a 60 fps con el boost de la
        /// corrida) — el montaje lo sabe y el modelo no tiene por qué adivinarlo.
        ///
        /// CADA PATA VIENE DE DONDE SE PUEDE MEDIR. La del player, de las ventanas con
        /// el mundo YA dibujado (un tablero todavía vacío no es un frame del juego).
        /// La del CLI, del AGREGADO de su vida entera (`cliCpuMsTotal` entre
        /// `cliTicksTotal`) y no de las ventanas: su CPU se gasta antes de que los
        /// ticks lleguen al player, así que emparejarlos por ventana miente. Las
        /// ventanas se usan para contar las FASES y comprobar que el CLI terminó
        /// (`cliCompleted`) — sin eso su CPU es parcial.
        /// </summary>
        public static SystemCostSummary SummarizeSystem(IReadOnlyList<SystemWindow> windows,
            double cliCpuMsTotal, long cliTicksTotal, double ticksPerPresentedFrame,
            bool cliCompleted, double targetFps = DefaultTargetFps)
        {
            var summary = new SystemCostSummary
            {
                Windows = windows.Count,
                BudgetMs = targetFps > 0.0 ? 1000.0 / targetFps : 0.0,
                TicksPerPresentedFrame = ticksPerPresentedFrame,
            };

            var player = new List<double>();
            double totalCpuMs = 0.0;
            long totalFrames = 0;
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                // La pata del PLAYER solo cuenta con el mundo en pantalla: el coste de
                // dibujar un tablero vacío no es el coste de un frame del juego.
                if (w.Loaded) { summary.LoadedWindows++; player.Add(w.PlayerMsPerFrame); }
                if (w.Frames > 0 && w.PlayerMsPerFrame > 0.0)
                {
                    totalCpuMs += w.PlayerMsPerFrame * w.Frames;
                    totalFrames += w.Frames;
                }
                if (!w.Simulated) continue;
                summary.SimulatedWindows++;
                summary.Frames += w.Frames;
                summary.Ticks += w.Ticks;
                totalCpuMs += w.CliCpuMs;
                if (w.Loaded) summary.OverlapWindows++;
            }

            summary.PlayerMsPerFrame = Median(player);
            summary.AggregateMsPerFrame = totalFrames > 0 ? totalCpuMs / totalFrames : 0.0;

            // La pata del MUNDO: toda la CPU del hijo entre todos los ticks que
            // simuló. Es un agregado a propósito — su CPU se gasta antes de que los
            // ticks lleguen al player, y emparejarlos por ventana daba 0,016 ms/tick
            // en vez de 0,2 (medido).
            summary.CliCpuMsTotal = cliCpuMsTotal;
            summary.CliTicks = cliTicksTotal;
            summary.CliCompleted = cliCompleted;
            summary.CliMsPerTick = cliTicksTotal > 0 ? cliCpuMsTotal / cliTicksTotal : 0.0;
            summary.CliMsPerFrame = summary.CliMsPerTick * ticksPerPresentedFrame;

            // EL NÚMERO: el frame del juego, dibujado con el mundo cargado, más el
            // mundo que ese frame muestra costeado al precio medido por tick.
            summary.SystemMsPerFrame = summary.PlayerMsPerFrame + summary.CliMsPerFrame;
            if (summary.BudgetMs > 0.0)
            {
                if (summary.PlayerMsPerFrame > 0.0)
                {
                    summary.PlayerFractionOfBudget = summary.PlayerMsPerFrame / summary.BudgetMs;
                    summary.PlayerHeadroom = summary.BudgetMs / summary.PlayerMsPerFrame;
                }
                if (summary.SystemMsPerFrame > 0.0)
                {
                    summary.SystemFractionOfBudget = summary.SystemMsPerFrame / summary.BudgetMs;
                    // Núcleos equivalentes: un coste AGREGADO repartible entre
                    // procesos, comparado con el tiempo de UN frame.
                    summary.SystemCores = summary.SystemMsPerFrame / summary.BudgetMs;
                }
            }
            summary.PlayerFits = summary.BudgetMs > 0.0 && summary.PlayerMsPerFrame > 0.0
                && summary.PlayerMsPerFrame <= summary.BudgetMs;
            summary.CliShareOfSystem = summary.SystemMsPerFrame > 0.0
                ? summary.CliMsPerFrame / summary.SystemMsPerFrame : 0.0;
            // Sin alguna de las dos patas esto NO es el sistema: es un player (o un
            // CLI) con otro nombre, y publicarlo así sería el error que esta pieza
            // existe para no cometer.
            summary.Measured = summary.LoadedWindows >= MinimumWindowsForALeg
                && summary.CliMsPerTick > 0.0 && summary.CliCompleted;
            summary.Verdict = SystemVerdict(summary);
            return summary;
        }

        private static string SystemVerdict(SystemCostSummary s)
        {
            if (s.Windows == 0) return "sin ventanas (la sonda no llego a muestrear)";
            if (s.LoadedWindows < MinimumWindowsForALeg)
                return "el mundo no llego a estar en pantalla (" + s.LoadedWindows +
                       " ventanas): no hay frame del juego que medir";
            if (!s.CliCompleted)
                return "el CLI no termino su horizonte dentro de la corrida: su CPU es parcial y el precio por tick no vale";
            if (s.CliMsPerTick <= 0.0) return "sin CPU del CLI (no se pudo cronometrar el proceso hijo)";
            if (!s.PlayerFits) return "la pata del player no cabe en el presupuesto del objetivo";
            return "cabe: el frame del player deja margen y el mundo cuesta " +
                   M(s.SystemCores) + " nucleos al reloj del juego";
        }

        /// <summary>Una línea con lo que se publica del SISTEMA (ASCII, invariable).</summary>
        public static string DescribeSystem(SystemCostSummary s)
        {
            var sb = new StringBuilder();
            sb.Append("coste/SISTEMA: player(cargado)=").Append(M(s.PlayerMsPerFrame));
            sb.Append(" ms/frame (").Append(P(s.PlayerFractionOfBudget * 100.0));
            sb.Append("% del presupuesto, x").Append(M(s.PlayerHeadroom)).Append(")");
            sb.Append(" + mundo=").Append(M(s.CliMsPerFrame));
            sb.Append(" ms/frame (").Append(M(s.CliMsPerTick)).Append(" ms/tick x ");
            sb.Append(M(s.TicksPerPresentedFrame)).Append(" ticks/frame)");
            sb.Append(" = ").Append(M(s.SystemMsPerFrame)).Append(" ms/frame agregados = ");
            sb.Append(M(s.SystemCores)).Append(" nucleos al reloj del juego");
            sb.Append(" · cli=").Append(P(s.CliShareOfSystem * 100.0)).Append("% del sistema");
            sb.Append(" (fases: ").Append(s.SimulatedWindows).Append(" ventanas de simulacion · ");
            sb.Append(s.LoadedWindows).Append(" con el mundo en pantalla · ");
            sb.Append(s.OverlapWindows).Append(" de solape · CLI ");
            sb.Append(s.CliCompleted ? "terminado" : "SIN terminar");
            sb.Append(" dentro de la corrida · agregado de la corrida ");
            sb.Append(M(s.AggregateMsPerFrame)).Append(" ms/frame)");
            sb.Append(" · ").Append(s.Verdict);
            return sb.ToString();
        }

        public static string Name(FrameBottleneck b)
        {
            switch (b)
            {
                case FrameBottleneck.Cpu: return "CPU";
                case FrameBottleneck.Gpu: return "GPU";
                case FrameBottleneck.Balanced: return "equilibrado";
                case FrameBottleneck.PresentLimited: return "presentacion";
                default: return "indeterminado";
            }
        }

        /// <summary>Una línea con lo que se publica (ASCII, invariable).</summary>
        public static string Describe(FrameCostSummary s)
        {
            var sb = new StringBuilder();
            sb.Append("coste/frame cpu=").Append(M(s.CpuMsMedian)).Append(" ms");
            sb.Append(" (hilo ppal ").Append(M(s.MainMsMedian));
            sb.Append(", hilo render ").Append(M(s.RenderMsMedian)).Append(')');
            sb.Append(" · gpu=").Append(s.GpuAvailable ? M(s.GpuMsMedian) + " ms" : "n/d");
            sb.Append(" · espera Present=").Append(M(s.PresentWaitMsMedian)).Append(" ms");
            sb.Append(" · techo sin vsync=").Append(M(s.FpsMedian)).Append(" fps");
            sb.Append(" · ").Append(P(s.CpuFractionOfBudget * 100.0)).Append("% del presupuesto de ");
            sb.Append(M(s.BudgetMs)).Append(" ms · cuello=").Append(Name(s.Bottleneck));
            sb.Append(" · ").Append(s.Verdict);
            return sb.ToString();
        }

        private static string M(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string P(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
