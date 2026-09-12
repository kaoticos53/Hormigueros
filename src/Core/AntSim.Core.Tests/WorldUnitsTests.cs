using AntSim.Core.Sim;
using AntSim.Core.World;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// Play-pass F5.1 — la escala del mundo de la VISTA debe coincidir con la del
    /// Core. El defecto que motivó estos tests: la escena Unity se dimensionaba con
    /// <c>grid</c> unidades cuando el Core simula en <c>grid × CellSizeUnits</c>
    /// (8 u/celda), así que el mundo (768 u para grid 96) quedaba 8× fuera de
    /// cámara y los drops se rechazaban por «fuera del mundo». Ningún test lo veía
    /// porque los MonoBehaviours no se compilan headless.
    /// </summary>
    public class WorldUnitsTests
    {
        [Fact]
        public void PerCell_EspejaSimConstants()
            => Assert.Equal(SimConstants.CellSizeUnits, WorldUnits.PerCell);

        [Fact]
        public void WorldSize_CoincideConWorldSim()
        {
            foreach (int grid in new[] { 96, 256 })
            {
                var sim = new WorldSim(42, grid, colonyCount: 2);
                Assert.Equal(sim.WorldWidth, WorldUnits.WorldSize(grid), 3);
                Assert.Equal(sim.WorldHeight, WorldUnits.WorldSize(grid), 3);
            }
        }

        [Fact]
        public void Nidos_FormulaDeLaEscena_CoincideConElCore()
        {
            // El bootstrapper coloca Nest_c en world·(c+1)/(colonies+1) sobre
            // y = world/2. Debe dar EXACTAMENTE el nido que el Core emite.
            const int grid = 96;
            var sim = new WorldSim(42, grid, colonyCount: 2);
            float world = WorldUnits.WorldSize(grid);
            for (int c = 0; c < 2; c++)
            {
                Assert.Equal(sim.Colonies[c].NestX, world * (c + 1) / 3f, 3);
                Assert.Equal(sim.Colonies[c].NestY, world * 0.5f, 3);
            }
        }
    }
}
