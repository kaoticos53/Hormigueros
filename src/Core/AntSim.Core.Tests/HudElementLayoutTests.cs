using System;
using System.Linq;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — layout per-elemento del HUD verificado headless:
    ///   1. Rects por toast: hit-test exacto (franja del toast i), click fuera
    ///      de la pila devuelve null, cada elemento mantiene key/nivel/ancla.
    ///   2. Barras de stock: fracción → segmentos, clamp, umbral de reserva baja.
    ///   3. Botones nativos: Confirmar deshabilitado sin tarjeta OK, Reiniciar
    ///      solo con drops — la semántica de siempre, ahora como specs.
    /// </summary>
    public class HudElementLayoutTests
    {
        private static HudToastsModel.Toast Toast(string key, byte level, float x, float y)
        {
            var a = new GameStreamParser.AlertView(key, level, 0, x, y, 123, "texto " + key);
            var t = new HudToastsModel.Toast(a);
            return t;
        }

        // ————— 1. rects por toast y hit-test —————

        [Fact]
        public void ToastElements_UnaFranjaPorToast_ConEstadoCompleto()
        {
            var active = new[] { Toast("a", 1, 10f, 20f), Toast("b", 2, -1f, -1f) };
            var elems = HudElementLayoutModel.ToastElements(active);

            Assert.Equal(2, elems.Count);
            Assert.Equal(0, elems[0].Index);
            Assert.Equal(0f, elems[0].Y);
            Assert.Equal(HudElementLayoutModel.ToastHeight + HudElementLayoutModel.ToastGap, elems[1].Y);
            Assert.True(elems[0].HasAnchor);
            Assert.Equal(10f, elems[0].AnchorX);
            Assert.False(elems[1].HasAnchor); // ancla -1 = sin salto de cámara
            Assert.Equal(2, elems[1].Level);
        }

        [Fact]
        public void ToastAt_ClickDentroDeLaFranja_DevuelveEseToast()
        {
            var active = new[] { Toast("a", 0, 5f, 5f), Toast("b", 1, 6f, 6f), Toast("c", 2, 7f, 7f) };
            var elems = HudElementLayoutModel.ToastElements(active);

            float h = HudElementLayoutModel.ToastHeight, gap = HudElementLayoutModel.ToastGap;
            Assert.Equal("a", HudElementLayoutModel.ToastAt(elems, 0f)!.Key);
            Assert.Equal("a", HudElementLayoutModel.ToastAt(elems, h - 1f)!.Key);
            Assert.Equal("b", HudElementLayoutModel.ToastAt(elems, h + gap)!.Key);
            Assert.Equal("c", HudElementLayoutModel.ToastAt(elems, 2 * (h + gap) + h / 2)!.Key);
        }

        [Fact]
        public void ToastAt_ClickEntreFranjasOOutside_Null()
        {
            var active = new[] { Toast("a", 0, 5f, 5f), Toast("b", 1, 6f, 6f) };
            var elems = HudElementLayoutModel.ToastElements(active);

            float h = HudElementLayoutModel.ToastHeight, gap = HudElementLayoutModel.ToastGap;
            Assert.Null(HudElementLayoutModel.ToastAt(elems, h));               // en el gap
            Assert.Null(HudElementLayoutModel.ToastAt(elems, -1f));             // encima
            Assert.Null(HudElementLayoutModel.ToastAt(elems, 3 * (h + gap)));   // debajo de todo
            Assert.Null(HudElementLayoutModel.ToastAt(Array.Empty<HudElementLayoutModel.ToastElement>(), 0f));
        }

        // ————— 2. barras de stock —————

        [Fact]
        public void StockBar_FraccionASegmentos()
        {
            Assert.Equal(10, new HudElementLayoutModel.StockBar(0, 1f).Filled);
            Assert.Equal(5, new HudElementLayoutModel.StockBar(0, 0.52f).Filled);
            Assert.Equal(0, new HudElementLayoutModel.StockBar(0, 0.04f).Filled);
            Assert.Equal(10, new HudElementLayoutModel.StockBar(0, 1.5f).Filled);  // clamp
            Assert.Equal(0, new HudElementLayoutModel.StockBar(0, -0.5f).Filled);  // clamp
        }

        [Fact]
        public void StockBar_ReservaBaja_Marcada()
        {
            Assert.True(new HudElementLayoutModel.StockBar(0, 0.10f).Low);
            Assert.False(new HudElementLayoutModel.StockBar(0, 0.25f).Low);
            // SMax=0: fracción 0 → barra vacía y en reserva baja (nada que mostrar).
            var zero = HudElementLayoutModel.StockBars(new[] { (0, 0f, 0f) }).Single();
            Assert.Equal(0, zero.Filled);
            Assert.True(zero.Low);
        }

        [Fact]
        public void StockBars_UnaPorColonia()
        {
            var bars = HudElementLayoutModel.StockBars(new[] { (0, 40f, 100f), (1, 90f, 100f) });
            Assert.Equal(2, bars.Count);
            Assert.Equal(0, bars[0].ColonyId);
            Assert.Equal(4, bars[0].Filled);
            Assert.Equal(9, bars[1].Filled);
        }

        // ————— 3. botones nativos —————

        [Fact]
        public void ImportButtons_ConfirmarSoloConTarjetaOk()
        {
            var ok = HudElementLayoutModel.ImportButtons(confirmEnabled: true);
            var no = HudElementLayoutModel.ImportButtons(confirmEnabled: false);

            // F5.1: Inspeccionar (abre la cuarentena) + Confirmar + Cancelar.
            Assert.Equal(3, ok.Count);
            var confirm = ok.Single(b => b.Action == HudElementLayoutModel.ButtonAction.ImportConfirm);
            Assert.True(confirm.Enabled);
            var cancel = ok.Single(b => b.Action == HudElementLayoutModel.ButtonAction.ImportCancel);
            Assert.True(cancel.Enabled);
            // Inspeccionar siempre disponible: es el paso que FALTA antes de poder
            // confirmar nada (sin él, el modal no tenía forma de alimentarse).
            var inspect = ok.Single(b => b.Action == HudElementLayoutModel.ButtonAction.ImportInspect);
            Assert.True(inspect.Enabled);
            Assert.True(no.Single(b => b.Action == HudElementLayoutModel.ButtonAction.ImportInspect).Enabled);

            Assert.False(no.Single(b => b.Action == HudElementLayoutModel.ButtonAction.ImportConfirm).Enabled);
        }

        [Fact]
        public void DropPlanButtons_ReiniciarSoloConDrops()
        {
            Assert.False(HudElementLayoutModel.DropPlanButtons(0).Single().Enabled);
            Assert.True(HudElementLayoutModel.DropPlanButtons(3).Single().Enabled);
            Assert.Equal(HudElementLayoutModel.ButtonAction.RestartWithPlan,
                HudElementLayoutModel.DropPlanButtons(1).Single().Action);
        }
    }
}
