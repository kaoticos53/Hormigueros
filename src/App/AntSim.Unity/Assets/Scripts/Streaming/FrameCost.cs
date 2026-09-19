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

    public static class FrameCost
    {
        /// <summary>Objetivo del criterio de cierre de F5.3.</summary>
        public const double DefaultTargetFps = 60.0;

        /// <summary>Cerca del frame completo (el 20 % de margen del ejemplo oficial).</summary>
        public const double NearFullFrameThreshold = 0.2;
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
