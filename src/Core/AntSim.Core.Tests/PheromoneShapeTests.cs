using System.Collections.Generic;
using AntSim.Core.Pheromone;
using AntSim.Core.Sim;
using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F8 — LA FORMA DEL RASTRO EN LA RONDA. Dos propiedades que se ven en el
    /// tablero y que antes no se cumplían:
    ///
    ///   · ESTRECHO Y SIN HUMO: en la ronda la difusión está apagada (k = 0), así
    ///     que el depósito de una celda no se riega a las vecinas. La prueba
    ///     deposita en una celda de PARED —donde ninguna hormiga puede dejar
    ///     rastro— y comprueba que las vecinas no ganan nada: si la difusión
    ///     siguiera encendida, el valor cruzaría el muro y se vería la mancha.
    ///   · LARGO: la vida media sube 7× en todas las capas. Con la clásica, 60 s
    ///     dejan la comida al 1,5 %; con la de la ronda, al 98 %.
    ///
    /// La tercera prueba fija la COMPOSICIÓN de capas (un color por tipo, mezcla
    /// donde se cruzan) que usa el quad para enseñarlas todas a la vez.
    /// </summary>
    public sealed class PheromoneShapeTests
    {
        private const int Grid = 96;

        private static WorldSim MazeWorld() =>
            new(seed: 42UL, gridCells: Grid, colonyCount: 1, maze: true, recruitment: true);

        /// <summary>Una celda de pared del laberinto (ninguna hormiga puede pisarla).</summary>
        private static (int X, int Y) WallCell(MazeLayout maze)
        {
            for (int y = 2; y < maze.Height - 2; y++)
                for (int x = 2; x < maze.Width - 2; x++)
                    if (maze.IsWall(x, y)) return (x, y);
            return (2, 2);
        }

        [Fact]
        public void RastroDeLaRonda_NoSeDifundeALasVecinas()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var maze = sim.Maze!;
            var (wx, wy) = WallCell(maze);

            // El valor puesto en la pared no lo puede producir una hormiga: si algo
            // aparece en las vecinas, es difusión.
            Assert.True(colony.FoodLayer.Deposit(wx, wy, SimConstants.PheromoneCellMax));
            float[] before =
            {
                colony.FoodLayer[wx - 1, wy], colony.FoodLayer[wx + 1, wy],
                colony.FoodLayer[wx, wy - 1], colony.FoodLayer[wx, wy + 1],
            };

            for (int i = 0; i < 10; i++) sim.Step();

            Assert.Equal(before[0], colony.FoodLayer[wx - 1, wy]);
            Assert.Equal(before[1], colony.FoodLayer[wx + 1, wy]);
            Assert.Equal(before[2], colony.FoodLayer[wx, wy - 1]);
            Assert.Equal(before[3], colony.FoodLayer[wx, wy + 1]);
            // Y la celda depositada sigue con rastro (no se borró en un tick).
            Assert.True(colony.FoodLayer[wx, wy] > 0.99f,
                $"el rastro se apagó en 1 s: {colony.FoodLayer[wx, wy]:0.000}");
        }

        [Fact]
        public void RastroDeLaRonda_DuraSieteVecesMas()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var (wx, wy) = WallCell(sim.Maze!);
            colony.FoodLayer.Deposit(wx, wy, SimConstants.PheromoneCellMax);
            colony.HomeLayer.Deposit(wx, wy - 1, SimConstants.PheromoneCellMax);

            // 600 ticks = 60 s (el paso de feromonas es 1 s cada 10 ticks).
            for (int i = 0; i < 600; i++) sim.Step();
            Assert.Equal(600UL, sim.Tick);

            // Comida: τ½ 2 100 s ⇒ exp(−ln2·60/2100) = 0,980. La clásica (10 s)
            // dejaría 2^−6 = 0,016.
            Assert.True(colony.FoodLayer[wx, wy] > 0.95f,
                $"el rastro de comida se evaporó: {colony.FoodLayer[wx, wy]:0.000} de 1 tras 60 s");
            // Casa: τ½ 20 s · 7 = 140 s ⇒ 0,743. La clásica dejaría 0,125.
            Assert.True(colony.HomeLayer[wx, wy - 1] > 0.70f,
                $"el rastro de casa se evaporó: {colony.HomeLayer[wx, wy - 1]:0.000} de 1 tras 60 s");

            // Alarma: τ½ 1 s · 7 = 7 s ⇒ a los 2 s queda 0,820; la clásica dejaría 50.
            var sim2 = MazeWorld();
            var (ax, ay) = WallCell(sim2.Maze!);
            sim2.Colonies[0].AlarmLayer.Deposit(ax, ay, SimConstants.PheromoneCellMax);
            for (int i = 0; i < 20; i++) sim2.Step();
            Assert.True(sim2.Colonies[0].AlarmLayer[ax, ay] > 0.78f,
                $"la alarma se evaporó: {sim2.Colonies[0].AlarmLayer[ax, ay]:0.000} de 1 tras 2 s");
        }

        [Fact]
        public void FueraDeLaRonda_LaVidaYLaDifusionSonLasClasicas()
        {
            // El mundo clásico conserva τ½ = 10 s y difusión: sus pines de CI no
            // se tocan. Comida tras 60 s: 2^−6 = 0,0156 (y algo menos por difusión).
            var classic = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 1, maze: true);
            Assert.False(classic.Recruitment);
            var (wx, wy) = WallCell(classic.Maze!);
            classic.Colonies[0].FoodLayer.Deposit(wx, wy, SimConstants.PheromoneCellMax);
            for (int i = 0; i < 600; i++) classic.Step();
            Assert.InRange(classic.Colonies[0].FoodLayer[wx, wy], 0f, 0.05f);

            var sim = MazeWorld();
            Assert.Equal(2100f, sim.RecruitmentFoodHalfLife);
        }

        // ————— composición de capas (render) —————

        [Fact]
        public void Compuesto_CadaTipoConSuColorYSinBleed()
        {
            int w = 3;
            var layers = new List<PheromoneCompositeModel.Layer>
            {
                new(PheromoneSelectorModel.Layer.Food, new byte[] { 200, 0, 200 }),
                new(PheromoneSelectorModel.Layer.Home, new byte[] { 0, 200, 200 }),
            };
            var rgb = new byte[w * 3];
            var alpha = new byte[w];

            PheromoneCompositeModel.Compose(layers, w, 1, rgb, alpha);

            // Celda 0: solo comida (ámbar del selector); 1: solo casa (verde);
            // 2: las dos, mezcla con alfa saturado.
            var food = PheromoneSelectorModel.PaletteOf(PheromoneSelectorModel.Layer.Food);
            var home = PheromoneSelectorModel.PaletteOf(PheromoneSelectorModel.Layer.Home);
            Assert.InRange(rgb[0], (byte)(food.R * 255 - 2), (byte)(food.R * 255 + 2));
            Assert.InRange(rgb[1], (byte)(food.G * 255 - 2), (byte)(food.G * 255 + 2));
            Assert.InRange(rgb[3], (byte)(home.R * 255 - 2), (byte)(home.R * 255 + 2));
            Assert.InRange(rgb[4], (byte)(home.G * 255 - 2), (byte)(home.G * 255 + 2));
            Assert.Equal(255, alpha[2]);
            Assert.True(alpha[0] > 0 && alpha[1] > 0);

            // Una celda vacía en TODAS las capas no se pinta.
            var vacio = new byte[w * 3];
            var alfaVacio = new byte[w];
            PheromoneCompositeModel.Compose(
                new List<PheromoneCompositeModel.Layer>
                {
                    new(PheromoneSelectorModel.Layer.Food, new byte[] { 0, 0, 0 }),
                }, w, 1, vacio, alfaVacio);
            Assert.All(vacio, b => Assert.Equal(0, b));
            Assert.All(alfaVacio, a => Assert.Equal(0, a));
        }

        [Fact]
        public void Compuesto_ElCaminoMandaSobreElSustrato()
        {
            // Mismo byte y mismo máximo en cada capa ⇒ la única diferencia es el
            // peso de pantalla. El camino de comida (estrecho) tiene que verse más
            // que la huella (que cubre un tercio del tablero) y más que la casa;
            // si no, el ámbar del camino se pierde bajo el velo del suelo pisado.
            byte AlphaOf(PheromoneSelectorModel.Layer kind)
            {
                var rgb = new byte[3];
                var alfa = new byte[1];
                PheromoneCompositeModel.Compose(
                    new List<PheromoneCompositeModel.Layer>
                    {
                        new(kind, new byte[] { 200 }),
                    }, 1, 1, rgb, alfa);
                return alfa[0];
            }

            byte food = AlphaOf(PheromoneSelectorModel.Layer.Food);
            byte home = AlphaOf(PheromoneSelectorModel.Layer.Home);
            byte footprint = AlphaOf(PheromoneSelectorModel.Layer.Footprint);
            Assert.Equal(255, food);
            Assert.True(home < food, $"casa {home} no es más tenue que comida {food}");
            Assert.True(footprint * 2 < home, $"huella {footprint} no es claramente más tenue que casa {home}");

            // Y donde el camino cruza la huella el color es el del CAMINO, no una
            // mezcla turbia: la celda sigue leyéndose ámbar.
            var rgbMix = new byte[3];
            var alfaMix = new byte[1];
            PheromoneCompositeModel.Compose(
                new List<PheromoneCompositeModel.Layer>
                {
                    new(PheromoneSelectorModel.Layer.Footprint, new byte[] { 200 }),
                    new(PheromoneSelectorModel.Layer.Food, new byte[] { 200 }),
                }, 1, 1, rgbMix, alfaMix);
            var foodColor = PheromoneSelectorModel.PaletteOf(PheromoneSelectorModel.Layer.Food);
            Assert.True(rgbMix[0] > rgbMix[2], $"la celda no sale cálida: {rgbMix[0]},{rgbMix[1]},{rgbMix[2]}");
            Assert.InRange(rgbMix[0], (byte)(foodColor.R * 255 - 40), (byte)(foodColor.R * 255 + 1));
        }

        [Fact]
        public void Compuesto_IgnoraCapasDeOtroTamaño()
        {
            var rgb = new byte[3 * 3];
            var alpha = new byte[3];
            PheromoneCompositeModel.Compose(
                new List<PheromoneCompositeModel.Layer>
                {
                    new(PheromoneSelectorModel.Layer.Food, new byte[] { 200 }),
                }, 3, 1, rgb, alpha);
            Assert.All(rgb, b => Assert.Equal(0, b));
            Assert.All(alpha, a => Assert.Equal(0, a));
        }
    }
}
