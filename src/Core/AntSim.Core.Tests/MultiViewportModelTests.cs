using System;
using System.Collections.Generic;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1bis — modelo puro del multi-visor: geometría de los rects, capas de
    /// render y etiquetas. Es el contrato que el bootstrapper de Unity aplica
    /// (Camera.rect / cullingMask / rótulos), verificado sin editor.
    /// </summary>
    public class MultiViewportModelTests
    {
        [Fact]
        public void UnaVista_PantallaCompleta()
        {
            var views = MultiViewportModel.Layout(1);
            Assert.Single(views);
            Assert.Equal(0f, views[0].X, 5);
            Assert.Equal(0f, views[0].Y, 5);
            Assert.Equal(1f, views[0].W, 5);
            Assert.Equal(1f, views[0].H, 5);
        }

        [Fact]
        public void DosVistas_MediaPantallaCadaUna_SinSolape()
        {
            var views = MultiViewportModel.Layout(2);
            Assert.Equal(2, views.Count);
            // Orden de lectura: vista 0 a la IZQUIERDA, vista 1 a la derecha.
            Assert.True(views[0].X < views[1].X);
            // Mismo alto, sin solape y cubriendo la pantalla.
            Assert.Equal(1f, views[0].W + views[1].W + MultiViewportModel.Gap, 5);
            Assert.Equal(0f, views[0].Y, 5);
            Assert.Equal(1f, views[0].H, 5);
        }

        [Fact]
        public void CuatroVistas_Cuadricula2x2_YLaPrimeraArribaIzquierda()
        {
            var views = MultiViewportModel.Layout(4);
            Assert.Equal(4, views.Count);
            // Unity: Y=0 abajo ⇒ la vista 0 (primera) debe estar ARRIBA.
            Assert.True(views[0].Y > views[2].Y, "la vista 0 debe estar en la mitad superior");
            Assert.True(views[0].X < views[1].X, "la vista 0 debe estar a la izquierda");
            // Los 4 rects cubren la pantalla y no solapan: el gap se resta por
            // cada lado INTERIOR. En 2×2 hay 2 líneas de gap horizontales y 2
            // verticales (un único gap de ancho entre columnas/filas).
            float areaSum = 0f;
            foreach (var v in views) areaSum += v.W * v.H;
            float w = (1f - MultiViewportModel.Gap) / 2f;
            float h = (1f - MultiViewportModel.Gap) / 2f;
            Assert.Equal(4f * w * h, areaSum, 4);
        }

        [Fact]
        public void TresVistas_Cuadricula2x2_ConCuartaVacia()
        {
            var views = MultiViewportModel.Layout(3);
            Assert.Equal(3, views.Count);
            // Las tres caben en la misma geometría del 2×2: la cuarta celda
            // simplemente no se crea (ningún rect vacío ambiguo).
            Assert.All(views, v => Assert.True(v.W > 0.4f && v.H > 0.4f));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(-1)]
        public void VistasFueraDeRango_SeRechazan(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MultiViewportModel.Layout(n));
        }

        [Fact]
        public void CapasDeRender_DisjuntasPorVista_YSonElContratoDeTagManager()
        {
            // El ordinal ES el contrato con TagManager.asset (Sim0..3 en slots 8..11):
            // si esto cambia sin editar el asset, las cámaras ven mundos equivocados.
            var masks = new HashSet<int>();
            for (int i = 0; i < MultiViewportModel.MaxViews; i++)
            {
                int layer = MultiViewportModel.RenderLayer(i);
                Assert.InRange(layer, 8, 11);
                int mask = MultiViewportModel.CameraMask(i);
                // La máscara ve SU capa + Default (1<<0) + UI (1<<5).
                Assert.Equal((1 << 0) | (1 << 5) | (1 << layer), mask);
                Assert.True(masks.Add(1 << layer), "cada vista necesita su capa propia");
            }
        }

        [Fact]
        public void Etiquetas_PorDefecto_YPersonalizadas()
        {
            var views = MultiViewportModel.Layout(2);
            Assert.Equal("Vista 1", views[0].Label);
            Assert.Equal("Vista 2", views[1].Label);

            var named = MultiViewportModel.Layout(2, new[] { "warm-v2", "" });
            Assert.Equal("warm-v2", named[0].Label);
            Assert.Equal("Vista 2", named[1].Label); // vacío cae al default
        }

        [Fact]
        public void Caption_IncluyeSemillaYPool()
        {
            string c1 = MultiViewportModel.ViewCaption("Vista 1", 42, null);
            Assert.Equal("Vista 1 · seed 42", c1);

            string c2 = MultiViewportModel.ViewCaption("Vista 2", 43, "pretrain-warm-v2");
            Assert.Equal("Vista 2 · seed 43 · pretrain-warm-v2", c2);
        }
    }
}
