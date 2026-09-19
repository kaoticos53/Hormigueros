using System.Collections.Generic;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.3 — la 5ª pieza del criterio de cierre: el COSTE por frame dentro del
    /// build. Es el número que el framerate presentado (§4.7) tapa por
    /// construcción: con el vsync entregando al refresco del monitor, 60 fps es el
    /// techo de la pantalla, no del juego.
    ///
    /// Lo que se prueba aquí es el MODELO PURO (mediana, clasificación de cuello de
    /// botella y veredicto), que es lo que se publica en los documentos. Tres cosas
    /// que importan:
    ///   · la mediana ignora los ceros (un frame sin dato NO costó 0 ms);
    ///   · la clasificación es la del ejemplo oficial de <c>FrameTiming</c>, con sus
    ///     mismos umbrales (GPU / CPU / presentación / equilibrado);
    ///   · el veredicto se decide contra el presupuesto del objetivo, no a ojo.
    /// </summary>
    public class FrameCostTests
    {
        private const double Budget60 = 1000.0 / 60.0;   // 16,667 ms

        private static FrameCostWindow Window(double seconds, long frames, double cpu,
            double main, double render, double wait, double gpu)
            => new(seconds, frames, new FrameTimingSample(cpu, main, render, wait, gpu));

        [Fact]
        public void Mediana_IgnoraLosCeros()
        {
            // 0 = «este frame no dejó dato» (el manager entrega con retardo), y
            // contarlo hundiría el coste publicado.
            Assert.Equal(10.0, FrameCost.Median(new List<double> { 0, 10, 0, 12, 9 }));
        }

        [Fact]
        public void Mediana_Par_EsLaMediaDeLosDosCentrales()
        {
            Assert.Equal(11.0, FrameCost.Median(new List<double> { 9, 10, 12, 13 }));
        }

        [Fact]
        public void Mediana_SinDatos_EsCero()
        {
            Assert.Equal(0.0, FrameCost.Median(new List<double> { 0, 0 }));
        }

        [Theory]
        // La GPU manda: está pegada al frame y ninguna CPU lo está.
        [InlineData(12.0, 8.0, 6.0, 0.1, 10.0, FrameBottleneck.Gpu)]
        // La CPU manda: el hilo principal se pega al frame y la GPU no.
        [InlineData(16.0, 15.5, 2.0, 0.1, 5.0, FrameBottleneck.Cpu)]
        // Nadie se pega al frame y el hilo principal espera en Present.
        [InlineData(16.0, 3.0, 4.0, 2.0, 5.0, FrameBottleneck.PresentLimited)]
        // CPU y GPU comparten el frame.
        [InlineData(10.0, 8.0, 7.0, 0.0, 8.0, FrameBottleneck.Balanced)]
        // Sin tiempos de GPU no se puede decidir.
        [InlineData(10.0, 8.0, 7.0, 0.0, 0.0, FrameBottleneck.Indeterminate)]
        public void Clasificacion_DeCuelloDeBotella(double cpu, double main, double render,
            double wait, double gpu, FrameBottleneck esperado)
        {
            var s = new FrameTimingSample(cpu, main, render, wait, gpu);
            Assert.Equal(esperado, FrameCost.Classify(s));
        }

        [Fact]
        public void Resumen_30Ventanas_MideElTechoYElPresupuesto()
        {
            // 30 ventanas de 1 s a 400 fps: el régimen de la corrida real. La CPU
            // cuesta 10 ms por frame, así que cabe en los 16,667 del objetivo.
            var windows = new List<FrameCostWindow>();
            for (int i = 0; i < 30; i++)
                windows.Add(Window(1.0, 400, 10.0, 6.0, 5.0, 0.05, 7.0));

            var s = FrameCost.Summarize(windows);

            Assert.True(s.Measured);
            Assert.Equal(30, s.Windows);
            Assert.Equal(12000, s.Frames);
            Assert.Equal(400.0, s.FpsMedian, 3);
            Assert.Equal(2.5, s.AchievedMsMedian, 3);
            Assert.Equal(10.0, s.CpuMsMedian, 3);
            Assert.True(s.GpuAvailable);
            Assert.Equal(Budget60, s.BudgetMs, 3);
            Assert.Equal(0.6, s.CpuFractionOfBudget, 3);
            Assert.Equal(1.667, s.Headroom, 2);
            Assert.True(s.Fits);
            Assert.Contains("cabe", s.Verdict);
            // GPU 7 < margen 8 y ninguna CPU se pega al frame → equilibrado.
            Assert.Equal(FrameBottleneck.Balanced, s.Bottleneck);
        }

        [Fact]
        public void Resumen_SinGPU_LoDeclara_NoLoInventa()
        {
            var windows = new List<FrameCostWindow>
            {
                Window(1.0, 250, 4.0, 3.5, 0.5, 0.0, 0.0),
                Window(1.0, 250, 4.4, 3.9, 0.5, 0.0, 0.0),
                Window(1.0, 250, 4.2, 3.7, 0.5, 0.0, 0.0),
            };

            var s = FrameCost.Summarize(windows);

            Assert.False(s.GpuAvailable);
            Assert.Equal(FrameBottleneck.Indeterminate, s.Bottleneck);
            Assert.True(s.Fits);
            Assert.Contains("gpu=n/d", FrameCost.Describe(s));
        }

        [Fact]
        public void Resumen_CostePorEncimaDelPresupuestoSuspende()
        {
            // 24 ms de CPU por frame = 1,44× el presupuesto de 60 fps.
            var windows = new List<FrameCostWindow>
            {
                Window(1.0, 42, 24.0, 20.0, 4.0, 0.0, 8.0),
                Window(1.0, 42, 24.5, 20.4, 4.1, 0.0, 8.2),
                Window(1.0, 42, 23.5, 19.6, 3.9, 0.0, 7.8),
            };

            var s = FrameCost.Summarize(windows);

            Assert.False(s.Fits);
            // Headroom = presupuesto/coste = 16,667/24 = 0,69: NO cabe.
            Assert.Equal(0.6944, s.Headroom, 3);
            Assert.Contains("no cabe", s.Verdict);
            Assert.Equal(FrameBottleneck.Cpu, s.Bottleneck);
        }

        [Fact]
        public void Resumen_SinMuestras_NoSeDeclaraMedido()
        {
            var s = FrameCost.Summarize(new List<FrameCostWindow>());

            Assert.False(s.Measured);
            Assert.False(s.Fits);
            Assert.Equal(0, s.Windows);
            Assert.Contains("sin datos", s.Verdict);
        }
    }
}
