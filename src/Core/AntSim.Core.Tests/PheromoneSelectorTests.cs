using System;
using System.Collections.Generic;
using AntSim.Core.Pheromone;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — selector de la capa de feromonas (colonia × tipo) y parser del canal
    /// E múltiple. El selector es el que decide qué se pinta cuando hay varias
    /// colonias compitiendo; la paleta es parte del contrato del HUD (cada capa
    /// tiene su color), así que se fija aquí y no «a ojo» en la escena.
    /// </summary>
    public class PheromoneSelectorTests
    {
        // ————— helpers —————

        // La app Unity NO referencia el Core: su enum Layer declara los mismos
        // ordinales que el contrato del canal E. Este test es el que impide que
        // los dos lados se separen (si alguien inserta un tipo en medio del enum
        // del Core, aquí salta).
        [Fact]
        public void Ordinales_CoincidenConElContratoDelCore()
        {
            Assert.Equal((byte)PheromoneKind.FoodTrail, (byte)PheromoneSelectorModel.Layer.Food);
            Assert.Equal((byte)PheromoneKind.Home, (byte)PheromoneSelectorModel.Layer.Home);
            Assert.Equal((byte)PheromoneKind.Alarm, (byte)PheromoneSelectorModel.Layer.Alarm);
        }

        private static GameStreamParser.TickView UltimoTick(string stream)
        {
            var parser = new GameStreamParser();
            GameStreamParser.TickView? last = null;
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line.TrimEnd('\r'));
                if (v != null) last = v;
            }
            return last!;
        }

        private static string StreamConCapas(bool clasico = false)
            => clasico
                ? GameScenario.Run(42, ticks: 200, colonies: 2, grid: 32,
                    frameEvery: 30, pheroEvery: 200)
                : GameScenario.Run(42, ticks: 200, colonies: 2, grid: 32,
                    frameEvery: 30, pheroEvery: 200,
                    pheroLayers: new List<GameScenario.PheroRequest>
                    {
                        new(0, PheromoneKind.Home),
                        new(1, PheromoneKind.Home),
                        new(1, PheromoneKind.Alarm),
                    });

        // ————— 1. estado y ciclo —————

        [Fact]
        public void PorDefecto_EsLaCapaQueYaSeVeia()
        {
            var s = new PheromoneSelectorModel();
            Assert.Equal(0, s.Colony);
            Assert.Equal(PheromoneSelectorModel.Layer.Home, s.Kind); // no cambia el comportamiento
            Assert.Equal(new PheromoneSelectorModel.Rgb(0.35f, 0.85f, 0.45f), s.Palette);
            Assert.Equal("feromonas · colonia 0 · home", s.Label);
        }

        [Fact]
        public void CicloDeCapas_HomeFoodAlarmYVuelve()
        {
            var s = new PheromoneSelectorModel();
            Assert.Equal(PheromoneSelectorModel.Layer.Food, s.CycleLayer());
            Assert.Equal(PheromoneSelectorModel.Layer.Alarm, s.CycleLayer());
            Assert.Equal(PheromoneSelectorModel.Layer.Home, s.CycleLayer());
        }

        [Fact]
        public void CadaCapa_TieneSuPropioColor()
        {
            var colores = new List<(float, float, float)>();
            foreach (var k in new[]
                     {
                         PheromoneSelectorModel.Layer.Home, PheromoneSelectorModel.Layer.Food,
                         PheromoneSelectorModel.Layer.Alarm
                     })
            {
                var c = PheromoneSelectorModel.PaletteOf(k);
                colores.Add((c.R, c.G, c.B));
            }
            Assert.Equal(3, new HashSet<(float, float, float)>(colores).Count);
            Assert.Equal(new PheromoneSelectorModel.Rgb(0.90f, 0.35f, 0.32f),
                PheromoneSelectorModel.PaletteOf(PheromoneSelectorModel.Layer.Alarm));
        }

        [Fact]
        public void CicloDeColonia_EnvuelveYLlevaSuCuenta()
        {
            var s = new PheromoneSelectorModel(colonyCount: 2);
            Assert.Equal(1, s.CycleColony());
            Assert.Equal(0, s.CycleColony());
            s.SetColonyCount(3);
            Assert.Equal(1, s.CycleColony());
        }

        // ————— 2. qué se pinta, con el stream real —————

        [Fact]
        public void CanalEMultiple_ElSelectorEligeLaCapaPedida()
        {
            var view = UltimoTick(StreamConCapas());
            Assert.Equal(3, view.PheroSet.Count); // el parser ve las tres

            var s = new PheromoneSelectorModel(colonyCount: 2);
            string? home0 = s.Payload(view);
            Assert.NotNull(home0);

            s.CycleColony();                       // colonia 1, home
            string? home1 = s.Payload(view);
            Assert.NotNull(home1);
            Assert.NotEqual(home0, home1);         // cada colonia trae SUS datos

            s.CycleLayer();                        // colonia 1, food → no viene
            Assert.Null(s.Payload(view));

            s.CycleLayer();                        // colonia 1, alarm → sí viene
            Assert.NotNull(s.Payload(view));
        }

        [Fact]
        public void CanalEClasico_SoloValeParaSuCapa()
        {
            // El canal clásico emite home de la colonia 0 y nada más: pedir otra
            // capa NO puede devolver ese paquete (sería pintar algo con la
            // etiqueta equivocada, que es peor que no pintar).
            var view = UltimoTick(StreamConCapas(clasico: true));
            Assert.NotNull(view.Phero);
            Assert.Empty(view.PheroSet);

            var s = new PheromoneSelectorModel(colonyCount: 2);
            Assert.Equal(view.Phero, s.Payload(view));   // (0, home) sí

            s.CycleColony();
            Assert.Null(s.Payload(view));                // (1, home) no
            s.SelectColony(0);
            s.Select(PheromoneSelectorModel.Layer.Alarm);
            Assert.Null(s.Payload(view));                // (0, alarm) no
        }

        [Fact]
        public void SinCanalE_NoHayNadaQuePintar()
        {
            var view = UltimoTick(GameScenario.Run(42, ticks: 60, colonies: 2, grid: 32,
                frameEvery: 30));
            var s = new PheromoneSelectorModel(colonyCount: 2);
            Assert.Null(s.Payload(view));
            Assert.Null(s.Payload(null));
            Assert.Equal(0, PheromoneSelectorModel.LayerCountIn(view));
        }

        [Fact]
        public void NombreDeCapa_EsElQueUsaElHud()
        {
            Assert.Equal("home", PheromoneSelectorModel.NameOf(PheromoneSelectorModel.Layer.Home));
            Assert.Equal("food", PheromoneSelectorModel.NameOf(PheromoneSelectorModel.Layer.Food));
            Assert.Equal("alarm", PheromoneSelectorModel.NameOf(PheromoneSelectorModel.Layer.Alarm));
            Assert.Equal("?", PheromoneSelectorModel.NameOf((PheromoneSelectorModel.Layer)7));
        }
    }
}
