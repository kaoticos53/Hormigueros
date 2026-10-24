using System;
using System.Collections.Generic;
using AntSim.Core.Contracts;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F8 — LA RONDA NO SE APAGA POR HAMBRE. La partida canónica (grid 96,
    /// laberinto, pool pre-entrenado) moría a los ~1 180 s con 150 ítems en el
    /// tablero: las cargadas no volvían al nido (3 de 44 llegaban a 40 u), la
    /// que se quedaba sin reserva moría en el campo y el nido pagaba la ración
    /// teórica de todas aunque estuvieran llenas. Estas pruebas fijan cada pieza
    /// por separado —el campo de vuelta, la descarga al llegar, la vuelta a
    /// recargar y la contabilidad del stock— para que el fallo no vuelva por
    /// ninguno de los cuatro lados.
    /// </summary>
    public sealed class RoundSurvivalTests
    {
        private const int Grid = 96;

        private static WorldSim MazeWorld(ulong seed = 42UL) =>
            new(seed, Grid, colonyCount: 1, maze: true, recruitment: true);

        /// <summary>Campo de distancia al nido: 0 en el nido, finito lejos y el
        /// descenso monótono lleva al nido sin cruzar una pared.</summary>
        [Fact]
        public void CampoDeVuelta_BajaHastaCeroSiguiendoPasillos()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var maze = sim.Maze!;

            Assert.Equal(0, sim.NestGuideDistance(0, colony.NestX, colony.NestY));

            // Una celda abierta lejos del nido: cualquier punto del corredor vale,
            // lo importante es que el campo tenga valor y que el descenso llegue.
            var cell = SimConstants.CellSizeUnits;
            int startX = -1, startY = -1;
            for (int y = 1; y < maze.Height - 1 && startX < 0; y++)
                for (int x = 1; x < maze.Width - 1; x++)
                {
                    if (maze.IsWall(x, y) || maze.IsWall(x - 1, y) || maze.IsWall(x, y - 1)) continue;
                    float px = (x + 0.5f) * cell, py = (y + 0.5f) * cell;
                    if (sim.NestGuideDistance(0, px, py) >= 20) { startX = x; startY = y; break; }
                }
            Assert.True(startX >= 0, "no hay ninguna celda a 20+ pasos del nido");

            int cx = startX, cy = startY;
            for (int guard = 0; guard < maze.Width + maze.Height; guard++)
            {
                int here = sim.NestGuideDistance(0, (cx + 0.5f) * cell, (cy + 0.5f) * cell);
                if (here == 0) break;
                int best = here, bx = cx, by = cy;
                (int, int)[] next = { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) };
                foreach (var (nx, ny) in next)
                {
                    if (maze.IsWall(nx, ny)) continue;
                    int v = sim.NestGuideDistance(0, (nx + 0.5f) * cell, (ny + 0.5f) * cell);
                    if (v >= 0 && v < best) { best = v; bx = nx; by = ny; }
                }
                Assert.True(bx != cx || by != cy,
                    $"el campo se atasca en la celda ({cx},{cy}) con distancia {here}: no baja al nido");
                cx = bx; cy = by;
            }
            Assert.Equal(0, sim.NestGuideDistance(0, (cx + 0.5f) * cell, (cy + 0.5f) * cell));
        }

        /// <summary>Las paredes son OPACAS al nido: con laberinto la brújula innata
        /// del cerebro no es la recta al nido sino la dirección del pasillo (la
        /// celda vecina que baja el campo de distancia). Se recorre la rejilla
        /// entera: desde NINGUNA celda abierta la brújula apunta a una pared, y
        /// cuando el nido queda tras un muro deja de apuntar en línea recta —que
        /// era lo que hacía empujar la pared de vuelta al nido—.</summary>
        [Fact]
        public void BrujulaDelNido_ConLaberinto_NoCruzaParedes()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var maze = sim.Maze!;
            float cell = SimConstants.CellSizeUnits;
            var ant = new Ant { Heading = 0f }; // heading 0 ⇒ el marco local es el del mundo

            int open = 0, blockedView = 0;
            for (int y = 1; y < maze.Height - 1; y++)
                for (int x = 1; x < maze.Width - 1; x++)
                {
                    if (maze.IsWall(x, y)) continue;
                    float px = (x + 0.5f) * cell, py = (y + 0.5f) * cell;
                    int here = sim.NestGuideDistance(0, px, py);
                    if (here <= 0) continue; // nido o celda no conectada
                    open++;

                    ant.X = px;
                    ant.Y = py;
                    var sensors = AntSensors.Default();
                    sim.ApplyCorridorHomeSense(colony, ant, ref sensors);

                    float len = MathF.Sqrt(sensors.HomeDx * sensors.HomeDx + sensors.HomeDy * sensors.HomeDy);
                    Assert.InRange(len, 0.999f, 1.001f);

                    // La dirección tiene que llevar a una celda CONTIGUA abierta que
                    // además baje la distancia al nido: nunca a través del muro.
                    int tx = (int)MathF.Floor((px + sensors.HomeDx * cell) / cell);
                    int ty = (int)MathF.Floor((py + sensors.HomeDy * cell) / cell);
                    Assert.False(maze.IsWall(tx, ty),
                        $"la brújula desde ({x},{y}) apunta a la pared ({tx},{ty})");
                    Assert.True(sim.NestGuideDistance(0, (tx + 0.5f) * cell, (ty + 0.5f) * cell) < here,
                        $"la brújula desde ({x},{y}) no baja al nido: ({tx},{ty})");

                    if (maze.SegmentClear(px, py, colony.NestX, colony.NestY)) continue;
                    blockedView++;
                    float ddx = colony.NestX - px, ddy = colony.NestY - py;
                    float dl = MathF.Sqrt(ddx * ddx + ddy * ddy);
                    if (dl <= 1e-3f) continue;
                    float sx = ddx / dl, sy = ddy / dl;

                    // El caso que hacía empujar la pared: la recta al nido entra en
                    // una celda de pared. Ahí la brújula TIENE que separarse de la
                    // recta y llevar al pasillo.
                    int wx2 = (int)MathF.Floor((px + sx * cell) / cell);
                    int wy2 = (int)MathF.Floor((py + sy * cell) / cell);
                    if (!maze.IsWall(wx2, wy2)) continue;
                    float diff = MathF.Abs(sensors.HomeDx - sx) + MathF.Abs(sensors.HomeDy - sy);
                    Assert.True(diff > 0.05f,
                        $"la recta al nido desde ({x},{y}) entra en la pared ({wx2},{wy2}) y la brújula no se separa de ella");
                }

            Assert.True(open > 100, $"solo {open} celdas conectadas: el laberinto no se exploró");
            Assert.True(blockedView > 50, $"solo {blockedView} celdas con el nido tras la pared: prueba floja");

            // En la celda del nido no hay rumbo que dar.
            ant.X = colony.NestX;
            ant.Y = colony.NestY;
            var atNest = AntSensors.Default();
            sim.ApplyCorridorHomeSense(colony, ant, ref atNest);
            Assert.Equal(0f, atNest.HomeDx);
            Assert.Equal(0f, atNest.HomeDy);
        }

        /// <summary>Sin laberinto no hay campo: la vuelta es la brújula y no se
        /// inventan valores (la partida clásica no toca CampoDeVuelta).</summary>
        [Fact]
        public void SinLaberinto_NoHayCampoDeVuelta()
        {
            var sim = new WorldSim(42UL, Grid, colonyCount: 1, recruitment: true);
            Assert.Null(sim.Maze);
            Assert.Equal(-1, sim.NestGuideDistance(0, sim.Colonies[0].NestX, sim.Colonies[0].NestY));
        }

        /// <summary>La cargada que pisa el nido descarga sin que el cerebro pulse
        /// interacción: el bocado venía para casa y llegó.</summary>
        [Fact]
        public void Cargada_DescargaAlPisarElNido()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var ant = colony.Adults[0];
            colony.Stock = 10f;
            ant.X = colony.NestX;
            ant.Y = colony.NestY;
            ant.HasLoad = true;
            ant.LoadValue = 7f;
            ant.InteractCooldown = 0f;
            float before = colony.Stock;

            sim.Step();

            Assert.False(ant.HasLoad, "la cargada que llegó al nido no soltó la carga");
            Assert.True(colony.Stock > before, $"el stock no subió al descargar ({before:0.00} → {colony.Stock:0.00})");
        }

        /// <summary>La que se queda sin reserva vuelve al nido; con reserva no se
        /// desvía, y con el mundo clásico el reflejo no existe.</summary>
        [Fact]
        public void Cansada_VuelveAlNidoSoloCuandoLeQuedaPoco()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var ant = colony.Adults[0];
            ant.HasLoad = false;
            // Lejos del nido y apuntando en contra (rumbo +X, nido a la izquierda).
            ant.X = colony.NestX + 160f;
            ant.Y = colony.NestY;
            ant.Heading = 0f;
            ant.Energy = 0.20f;

            var tired = new AntDecision();
            sim.ApplyReturnWhenTired(colony, ant, ref tired);
            Assert.True(tired.Steer > 0f,
                $"Steer={tired.Steer:0.000}: con 20 % de reserva debía girar hacia el nido (izquierda)");

            ant.Energy = 0.90f;
            var fresh = new AntDecision();
            sim.ApplyReturnWhenTired(colony, ant, ref fresh);
            Assert.Equal(0f, fresh.Steer);

            var classic = new WorldSim(42UL, Grid, colonyCount: 1, maze: true);
            var c2 = classic.Colonies[0];
            var a2 = c2.Adults[0];
            a2.X = c2.NestX + 160f;
            a2.Y = c2.NestY;
            a2.Heading = 0f;
            a2.Energy = 0.2f;
            var off = new AntDecision();
            classic.ApplyReturnWhenTired(c2, a2, ref off);
            Assert.Equal(0f, off.Steer);
        }

        /// <summary>El nido solo paga el alimento que la obrera se come: con la
        /// tropa llena no se va ni un ep de reserva por la ración teórica.</summary>
        [Fact]
        public void Nido_SoloPagaElAlimentoQueSeCome()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            colony.Eggs.Clear();
            colony.Larvae.Clear();
            colony.Pupae.Clear();
            colony.Stock = 100f;
            foreach (var a in colony.Adults) a.Energy = 1f;

            float before = colony.Stock;
            ColonyController.Step(colony, SimConstants.FixedDtSeconds, sim.Tick, new List<SimEvent>(), ref NextId());
            float eaten = before - colony.Stock;

            float allowance = colony.AdultCountAlive * colony.Species.AdultUpkeep * SimConstants.FixedDtSeconds;
            Assert.True(eaten < allowance * 0.5f,
                $"el nido pagó {eaten:0.0000} ep con la tropa llena (ración teórica {allowance:0.0000})");

            // El mundo clásico conserva la ración fija: es su economía cerrada.
            var classic = new WorldSim(42UL, Grid, colonyCount: 1);
            var cc = classic.Colonies[0];
            cc.Eggs.Clear(); cc.Larvae.Clear(); cc.Pupae.Clear();
            cc.Stock = 100f;
            foreach (var a in cc.Adults) a.Energy = 1f;
            float before2 = cc.Stock;
            ColonyController.Step(cc, SimConstants.FixedDtSeconds, classic.Tick, new List<SimEvent>(), ref NextId());
            float eaten2 = before2 - cc.Stock;
            float allowance2 = cc.AdultCountAlive * cc.Species.AdultUpkeep * SimConstants.FixedDtSeconds;
            Assert.True(eaten2 >= allowance2 * 0.9f,
                $"el mundo clásico debía pagar la ración entera y pagó {eaten2:0.0000} de {allowance2:0.0000}");
        }

        /// <summary>La pared no se atraviesa: la hormiga que empuja de frente se
        /// apoya y gira. Antes de esto se quedaba empujando el muro para siempre
        /// (55 inmóviles &gt;3 s en la partida canónica, con el bocado en la boca).</summary>
        [Fact]
        public void AlChocar_SeApoyaEnLaParedYGira()
        {
            var sim = MazeWorld();
            var colony = sim.Colonies[0];
            var maze = sim.Maze!;
            float cell = SimConstants.CellSizeUnits;

            // Una celda de pared con el oeste abierto: la hormiga se coloca justo
            // contra ella, mirando al este (dentro de la pared).
            int wx = -1, wy = -1;
            for (int y = 1; y < maze.Height - 1 && wx < 0; y++)
                for (int x = 2; x < maze.Width - 1; x++)
                    if (maze.IsWall(x, y) && !maze.IsWall(x - 1, y)) { wx = x; wy = y; break; }
            Assert.True(wx > 0 && wy > 0, "el laberinto no tiene ninguna pared con hueco al oeste");

            var ant = colony.Adults[0];
            ant.Brain = new StillBrain();
            ant.HasLoad = false;
            ant.Energy = 1f;
            ant.Heading = 0f;
            ant.X = wx * cell - MazeLayout.AntRadius;
            ant.Y = (wy + 0.5f) * cell;
            float heading = ant.Heading;

            sim.Step();

            Assert.NotEqual(heading, ant.Heading);
            Assert.InRange(ant.Heading, sim.MazeBumpTurn - 0.01f, sim.MazeBumpTurn + 0.01f);
        }

        /// <summary>Cerebro plano: avanza siempre de frente, sin girar.</summary>
        private sealed class StillBrain : AntSim.Core.Brain.IBrain
        {
            public AntSim.Core.Brain.BrainKind Kind => AntSim.Core.Brain.BrainKind.Mlp;
            public int ContractVersion => AntSim.Core.Brain.BrainContract.CurrentVersion;
            public void Evaluate(in AntSensors sensors, ref AntDecision decision)
            {
                decision.Steer = 0f;
                decision.Speed = 1f;
            }
        }

        private static uint _nextId = 1_000_000;
        private static ref uint NextId() => ref _nextId;
    }
}
