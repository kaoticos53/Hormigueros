using System;
using System.Collections.Generic;
using AntSim.Core.Contracts;
using AntSim.Core.Serialization;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F6 — el terreno con laberinto. Lo que se fija aquí es el CONTRATO del
    /// generador (determinismo, conectividad, plazas de los nidos y bucles) y su
    /// convivencia con el mundo: nadie nace ni come dentro de una pared, las
    /// hormigas no la atraviesan, el bitmap viaja en el checkpoint y el hash del
    /// mundo la incluye. La otra mitad del contrato —que SIN laberinto el mundo y
    /// el stream son los de siempre— la sostienen los pines de hash de CI, que
    /// este cambio no toca.
    /// </summary>
    public class MazeTests
    {
        private const int Grid = 96;

        // ── 1. Generación ───────────────────────────────────────────────────

        [Fact]
        public void Braided_EsDeterminista_ParaLaMismaSemilla()
        {
            var a = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);
            var b = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);
            var otra = MazeLayout.Braided(seed: 43UL, cells: Grid, colonyCount: 2);

            Assert.Equal(a.LayoutHash, b.LayoutHash);
            Assert.Equal(a.WallCells, b.WallCells);
            Assert.Equal(a.Rects.Count, b.Rects.Count);
            for (int i = 0; i < a.Rects.Count; i++)
            {
                Assert.Equal(a.Rects[i].X, b.Rects[i].X);
                Assert.Equal(a.Rects[i].Y, b.Rects[i].Y);
                Assert.Equal(a.Rects[i].W, b.Rects[i].W);
                Assert.Equal(a.Rects[i].H, b.Rects[i].H);
            }

            // Otra semilla ⇒ otro terreno (si no, el laberinto no vendría de ella).
            Assert.NotEqual(a.LayoutHash, otra.LayoutHash);
        }

        [Fact]
        public void Braided_EsUnSoloRecintoCerrado()
        {
            var maze = MazeLayout.Braided(seed: 7UL, cells: Grid, colonyCount: 2);

            // Fuera del mapa cuenta como pared: la colisión del mundo recorta en
            // el borde y nadie puede salir por una celda que no existe.
            Assert.True(maze.IsWall(-1, 0));
            Assert.True(maze.IsWall(0, -1));
            Assert.True(maze.IsWall(Grid, 0));
            Assert.True(maze.IsWall(0, Grid));

            // Y todo el interior abierto es alcanzable desde el nido de la
            // colonia 0: puertas y braid dejan un único recinto, no islas.
            int startX = (int)(maze.WorldWidth * 1 / 3 / SimConstants.CellSizeUnits);
            int startY = Grid / 2;
            Assert.False(maze.IsWall(startX, startY), "el nido debe caer en zona abierta");

            var seen = new bool[Grid * Grid];
            var stack = new Stack<(int X, int Y)>();
            stack.Push((startX, startY));
            seen[startY * Grid + startX] = true;
            int reached = 0;
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                reached++;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Grid || ny >= Grid) continue;
                    int idx = ny * Grid + nx;
                    if (seen[idx] || maze.IsWall(nx, ny)) continue;
                    seen[idx] = true;
                    stack.Push((nx, ny));
                }
            }
            Assert.Equal(Grid * Grid - maze.WallCells, reached);
        }

        [Fact]
        public void Braided_DespejaLaPlazaDeCadaNido()
        {
            var maze = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);
            float world = maze.WorldWidth;
            for (int c = 0; c < 2; c++)
            {
                float nx = world * (c + 1) / 3;
                float ny = world * 0.5f;
                // Una hormiga nace en el nido: si hubiera pared, nacería dentro.
                Assert.False(maze.Blocked(nx, ny, MazeLayout.AntRadius));
                // Y la plaza da espacio para salir en cualquier dirección.
                for (int k = 0; k < 8; k++)
                {
                    double ang = k * Math.PI / 4;
                    float px = nx + (float)Math.Cos(ang) * MazeLayout.PlazaRadius * 0.5f;
                    float py = ny + (float)Math.Sin(ang) * MazeLayout.PlazaRadius * 0.5f;
                    Assert.False(maze.Blocked(px, py, MazeLayout.AntRadius));
                }
            }
        }

        [Fact]
        public void Braided_TieneBucles_YNoEsSoloUnArbol()
        {
            var maze = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);

            // Un árbol de expansión sobre R×R salas usa exactamente R²−1 aristas;
            // cada arista abre UNA puerta. El braid tiene que añadir lados NUEVOS,
            // que se leen como cruces en el bitmap: celdas abiertas con 3 o más
            // vecinos abiertos (un pasillo recto tiene 2).
            int cruces = 0;
            for (int y = 1; y < Grid - 1; y++)
                for (int x = 1; x < Grid - 1; x++)
                {
                    if (maze.IsWall(x, y)) continue;
                    int abiertos = 0;
                    if (!maze.IsWall(x - 1, y)) abiertos++;
                    if (!maze.IsWall(x + 1, y)) abiertos++;
                    if (!maze.IsWall(x, y - 1)) abiertos++;
                    if (!maze.IsWall(x, y + 1)) abiertos++;
                    if (abiertos >= 3) cruces++;
                }

            // Sin bucles no habría cruces (ni una sola celda con 3 salidas).
            Assert.True(cruces > 40, $"el laberinto tiene {cruces} cruces: no parece tener bucles");
            // Y sin embargo sigue siendo un laberinto: las paredes ocupan una
            // fracción del mapa (sin el braid serían ~23 %, las puertas extra
            // recortan; por debajo del 10 % sería un campo abierto).
            Assert.True(maze.WallCells > Grid * Grid / 10,
                $"{maze.WallCells} celdas de pared en {Grid * Grid}: el terreno no se lee como laberinto");
        }

        [Fact]
        public void Blocked_MideElCirculo_YNoSoloElCentro()
        {
            var maze = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);
            // Una celda de pared abierta a su alrededor no existe en la plaza, así
            // que se busca una pared lejos del nido y se comprueba el borde.
            for (int y = 2; y < Grid - 2; y++)
                for (int x = 2; x < Grid - 2; x++)
                {
                    // Una pared con la celda de la derecha ABIERTA: el borde del
                    // muro es lo que se mide, no el interior de un bloque.
                    if (!maze.IsWall(x, y) || maze.IsWall(x + 1, y)) continue;
                    float cell = SimConstants.CellSizeUnits;
                    float cx = (x + 0.5f) * cell;
                    float cy = (y + 0.5f) * cell;
                    Assert.True(maze.Blocked(cx, cy, MazeLayout.AntRadius));
                    // En el centro de la celda abierta contigua, un círculo de
                    // radio pequeño no toca el muro; uno grande sí. Es la
                    // diferencia entre «el centro está libre» y «el cuerpo cabe».
                    float fuera = (x + 1.5f) * cell;
                    Assert.False(maze.Blocked(fuera, cy, 0.5f));
                    Assert.True(maze.Blocked(fuera, cy, cell));
                    return;
                }
            Assert.Fail("no encontré ninguna celda de pared en el laberinto");
        }

        // ── 2. El mundo con terreno ─────────────────────────────────────────

        [Fact]
        public void Mundo_ConLaberinto_NoPoneComidaDentroDeUnaPared()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, maze: true);
            Assert.NotNull(sim.Maze);
            Assert.True(sim.Items.Count > 0);

            foreach (var item in sim.Items)
            {
                Assert.False(sim.Maze!.Blocked(item.X, item.Y, MazeLayout.AntRadius),
                    $"el ítem {item.Id} nace en ({item.X:0.0}, {item.Y:0.0}), dentro de una pared");
                Assert.True(sim.Maze.ItemFits(item.X, item.Y),
                    $"el ítem {item.Id} nace pegado a una pared");
            }
        }

        [Fact]
        public void Mundo_ConLaberinto_NingunaHormigaAtraviesaUnaPared()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, maze: true);
            for (int i = 0; i < 600; i++) sim.Step();

            int vivas = 0;
            foreach (var colony in sim.Colonies)
                foreach (var ant in colony.Adults)
                {
                    if (!ant.Alive) continue;
                    vivas++;
                    int cx = (int)(ant.X / SimConstants.CellSizeUnits);
                    int cy = (int)(ant.Y / SimConstants.CellSizeUnits);
                    Assert.False(sim.Maze!.IsWall(cx, cy),
                        $"la hormiga {ant.Id} está en la celda de pared ({cx},{cy})");
                }
            Assert.True(vivas > 0, "el mundo se quedó sin hormigas en 600 ticks");
        }

        [Fact]
        public void Mundo_ConLaberinto_ProduceComidaYNoSeExtingue()
        {
            // El laberinto no puede hacer la partida imposible: con la densidad y
            // la distancia mínima del terreno (MazeLayout), en 20.000 ticks tiene
            // que haber pickups y colonias vivas.
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, maze: true);
            for (int i = 0; i < 20000; i++) sim.Step();

            bool vivas = false;
            foreach (var colony in sim.Colonies)
                if (colony.AdultCountAlive > 0) vivas = true;
            Assert.True(vivas, "las dos colonias se extinguieron con el laberinto");

            // El hash incluye el terreno: dos mundos idénticos menos el laberinto
            // son mundos distintos por construcción.
            var clasico = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2);
            Assert.Null(clasico.Maze);
            Assert.NotEqual(sim.HashLine(), clasico.HashLine());
        }

        [Fact]
        public void Sensores_ConLaberinto_VenLaParedEnLaAntenaQueLaToca()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 1, maze: true);
            var colony = sim.Colonies[0];
            var ant = new Ant
            {
                Id = 9999,
                ColonyId = 0,
                X = colony.NestX,
                Y = colony.NestY,
                Heading = 0f,
            };
            ant.InitFromVigor(colony.Species.EnergyCapacity, colony.Species.BaseLifespan, 1f);

            // En la plaza del nido no hay pared: la proximidad es la del borde del
            // mundo (lejos) en las tres antenas.
            var libre = AntSenses.Build(colony, ant, sim.Items, sim.WorldWidth,
                sim.WorldHeight, rivals: null, maze: sim.Maze);
            Assert.True(libre.ProxFront < 0.5f);

            // Pegada a una pared mirando hacia ella, la antena frontal la ve.
            var maze = sim.Maze!;
            (float X, float Y, float Heading)? sitio = null;
            for (int y = 2; y < Grid - 2 && sitio == null; y++)
                for (int x = 3; x < Grid - 2 && sitio == null; x++)
                {
                    // Pared en (x,y) con la celda de la IZQUIERDA abierta: la
                    // hormiga se pone en esa celda, mirando a +X hacia el muro.
                    if (!maze.IsWall(x, y) || maze.IsWall(x - 1, y)) continue;
                    float cell = SimConstants.CellSizeUnits;
                    float px = x * cell - MazeLayout.AntRadius - 0.5f;
                    sitio = (px, (y + 0.5f) * cell, 0f);
                    if (maze.Blocked(sitio.Value.X, sitio.Value.Y, MazeLayout.AntRadius)) sitio = null;
                }
            Assert.NotNull(sitio);

            ant.X = sitio!.Value.X;
            ant.Y = sitio.Value.Y;
            ant.Heading = sitio.Value.Heading;
            var pegado = AntSenses.Build(colony, ant, sim.Items, sim.WorldWidth,
                sim.WorldHeight, rivals: null, maze: maze);

            Assert.True(pegado.ProxFront > 0.5f,
                $"la antena frontal no ve la pared: ProxFront={pegado.ProxFront:0.000}");

            // Sin laberinto, el mismo cuerpo y posición no ven obstáculo: es la
            // prueba de que el canal lo añade el terreno, no otra cosa.
            var sinMaze = AntSenses.Build(colony, ant, sim.Items, sim.WorldWidth,
                sim.WorldHeight, rivals: null, maze: null);
            Assert.True(sinMaze.ProxFront < 0.5f);
        }

        // ── 3. Checkpoint (v6) ──────────────────────────────────────────────

        [Fact]
        public void Checkpoint_V6_ConservaElTerreno_YEsDeterminista()
        {
            var original = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, maze: true);
            for (int i = 0; i < 300; i++) original.Step();

            byte[] bytes = WorldSimSave.Serialize(original);
            var cargado = WorldSimSave.Deserialize(bytes);

            Assert.NotNull(cargado.Maze);
            Assert.Equal(original.Maze!.LayoutHash, cargado.Maze!.LayoutHash);
            Assert.Equal(original.HashLine(), cargado.HashLine());

            // Y sigue igual desde ahí: el bitmap cargado no es un adorno.
            for (int i = 0; i < 200; i++) { original.Step(); cargado.Step(); }
            Assert.Equal(original.HashLine(), cargado.HashLine());
        }

        [Fact]
        public void Checkpoint_V6_SinLaberinto_SeCargaSinTerreno()
        {
            var original = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2);
            for (int i = 0; i < 120; i++) original.Step();

            var cargado = WorldSimSave.Deserialize(WorldSimSave.Serialize(original));
            Assert.Null(cargado.Maze);
            // El hash clásico tampoco se mueve al pasar por el checkpoint.
            Assert.Equal(original.HashLine(), cargado.HashLine());
        }
    }
}
