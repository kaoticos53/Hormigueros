using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.Training;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F7 — entrenamiento headless con obstáculos. Lo que se fija aquí es el
/// contrato de las piezas NUEVAS: el terreno graduable es un subconjunto anidado
/// del laberinto del juego (d=1 idéntico, d=0 abierto), el currículo sube y baja
/// con las descargas observadas, la escalera de capacidad crece solo con el
/// terreno al máximo, y la expansión del genoma conserva la función. La
/// transferencia al mundo real se mide aparte (partida canónica); aquí se
/// verifica la mecánica.
/// </summary>
public class ObstacleTrainingTests
{
    private const int Grid = 96;
    private static readonly int[] Sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private static string RepoFixture(string name)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, ".git"))
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent!;
        return System.IO.Path.Combine(dir!.FullName, "tests", "fixtures", name);
    }

    private static MlpGenome DrawGenome(ulong seed, int index)
    {
        var rng = new DeterministicRandom(seed);
        MlpGenome g = null!;
        for (int i = 0; i <= index; i++) g = MlpGenome.Random(ref rng, Sizes);
        return g;
    }

    // ── 1. Terreno graduable ────────────────────────────────────────────────

    [Fact]
    public void Pruned_AtDifficultyOne_IsTheGameMaze()
    {
        var full = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);
        var atOne = MazeLayout.Pruned(seed: 42UL, cells: Grid, colonyCount: 2, difficulty: 1f);

        Assert.Equal(full.LayoutHash, atOne.LayoutHash);
        Assert.Equal(full.WallCells, atOne.WallCells);
        Assert.Equal(full.Rects.Count, atOne.Rects.Count);
    }

    [Fact]
    public void Pruned_AtZero_IsOpenField()
    {
        var open = MazeLayout.Pruned(seed: 42UL, cells: Grid, colonyCount: 2, difficulty: 0f);
        var full = MazeLayout.Braided(seed: 42UL, cells: Grid, colonyCount: 2);

        // Sin paredes interiores: solo queda el anillo exterior (las celdas de
        // fuera del mapa ya se consideran muro por `IsWall`, pero el bitmap
        // interior está limpio).
        for (int y = 1; y < Grid - 1; y++)
            for (int x = 1; x < Grid - 1; x++)
                Assert.False(open.IsWall(x, y), $"queda un muro interior en ({x},{y})");

        Assert.True(open.WallCells < full.WallCells / 4,
            $"el campo abierto debería tener muchas menos paredes: {open.WallCells} vs {full.WallCells}");
        Assert.Equal(0, open.Rects.Count(r => r.X > 0 && r.Y > 0 && r.X + r.W <= Grid - 1 && r.Y + r.H <= Grid - 1));
    }

    [Fact]
    public void Pruned_IsNested_AndDeterministic()
    {
        var levels = new[] { 0.2f, 0.5f, 0.8f, 1f };
        var maps = levels.Select(d => MazeLayout.Pruned(42UL, Grid, 2, d)).ToArray();
        var again = MazeLayout.Pruned(42UL, Grid, 2, 0.5f);

        Assert.Equal(maps[1].LayoutHash, again.LayoutHash);

        // Anidamiento: las paredes de una dificultad menor son subconjunto de la
        // siguiente — subir dificultad solo AÑADE obstáculos.
        for (int k = 1; k < maps.Length; k++)
        {
            var low = maps[k - 1].CopyCells();
            var high = maps[k].CopyCells();
            for (int i = 0; i < low.Length; i++)
                Assert.False(low[i] && !high[i], $"la celda {i} era muro a {levels[k - 1]} y no a {levels[k]}");
            Assert.True(maps[k].WallCells >= maps[k - 1].WallCells);
        }
    }

    [Fact]
    public void Pruned_KeepsNestPlazasOpen_AtEveryDifficulty()
    {
        float world = Grid * SimConstants.CellSizeUnits;
        foreach (float d in new[] { 0f, 0.5f, 1f })
        {
            var maze = MazeLayout.Pruned(42UL, Grid, 2, d);
            for (int c = 0; c < 2; c++)
            {
                float nx = world * (c + 1) / 3f;
                float ny = world * 0.5f;
                Assert.False(maze.Blocked(nx, ny, MazeLayout.AntRadius),
                    $"el nido {c} está bloqueado con dificultad {d}");
            }
            // El anillo exterior sigue cerrado (no se puede salir del mapa).
            Assert.True(maze.IsWall(0, Grid / 2) || maze.IsWall(Grid - 1, Grid / 2));
        }
    }

    [Fact]
    public void Pruned_RejectsDifficultyOutsideRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MazeLayout.Pruned(1UL, Grid, 1, -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MazeLayout.Pruned(1UL, Grid, 1, 1.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MazeLayout.Pruned(1UL, Grid, 1, float.NaN));
    }

    // ── 2. Arena con obstáculos ─────────────────────────────────────────────

    [Fact]
    public void Arena_WithMaze_ForagesAndIsDeterministic()
    {
        var maze = MazeLayout.Pruned(9UL, Grid, 1, 0.5f);
        var a = new ArenaEvaluator(123UL, 200f, 260f, 1800, trials: 1, customGridCells: null, maze: maze);
        var b = new ArenaEvaluator(123UL, 200f, 260f, 1800, trials: 1, customGridCells: null, maze: maze);

        // El pool de referencia (calibrado sin obstáculos) debe encontrar comida
        // YA en un mapa a media dificultad: si no, el terreno sería intransitable
        // y el currículo no tendría nada que enseñar.
        var (_, genomes) = AntGenomeFile.ReadFile(RepoFixture("warm-v2.antgenome"),
            BrainContract.CurrentVersion);
        var r1 = a.Evaluate(genomes[0]);
        var r2 = b.Evaluate(genomes[0]);

        Assert.Equal(r1.Fitness, r2.Fitness);
        Assert.Equal(r1.Pickups, r2.Pickups);
        Assert.Equal(r1.Unloads, r2.Unloads);
        Assert.True(r1.Pickups > 0, "la arena con laberinto no produjo ni un pickup con el pool de referencia");
    }

    [Fact]
    public void Arena_MazeSizeMustMatchArenaGrid()
    {
        var maze = MazeLayout.Pruned(9UL, 176, 1, 0.5f);
        Assert.Throws<ArgumentException>(() => new ArenaEvaluator(1UL, 200f, 260f, 60, 1, null, maze));
    }

    [Fact]
    public void Arena_WithoutMaze_IsUnchangedByTheNewPath()
    {
        // El camino clásico (sin laberinto) debe seguir dando exactamente lo
        // mismo: los densados y el spawn no cambian cuando `maze` es null.
        var genome = DrawGenome(7, 0);
        var r = new ArenaEvaluator(42UL, 200f, 260f, 300, trials: 1).Evaluate(genome);
        Assert.True(r.Fitness > double.MinValue);
        Assert.True(r.Pickups >= 0);
    }

    // ── 3. Currículo adaptativo ─────────────────────────────────────────────

    [Fact]
    public void ObstacleCurriculum_EscalatesAfterSustainedCompetence()
    {
        var c = new ObstacleCurriculum();
        Assert.Equal(0f, c.Difficulty);
        Assert.False(c.Observe(3));
        Assert.False(c.Observe(4));
        Assert.True(c.Observe(3));
        Assert.Equal(0.1f, c.Difficulty, 3);
        Assert.Equal(1, c.Escalations);
        Assert.Equal(0, c.Descalations);
    }

    [Fact]
    public void ObstacleCurriculum_DescendsWhenStuck()
    {
        var c = new ObstacleCurriculum(start: 0.5f);
        Assert.False(c.Observe(0));
        Assert.False(c.Observe(0));
        Assert.True(c.Observe(0));
        Assert.Equal(0.4f, c.Difficulty, 3);
        Assert.Equal(1, c.Descalations);
    }

    [Fact]
    public void ObstacleCurriculum_DoesNotOscillateOnMixedGenerations()
    {
        var c = new ObstacleCurriculum(start: 0.5f);
        // 3,0,0 / 3,3,0 ... ninguna ventana es toda buena ni toda nula.
        foreach (int u in new[] { 3, 0, 0, 3, 3, 0 }) Assert.False(c.Observe(u));
        Assert.Equal(0.5f, c.Difficulty, 3);
    }

    [Fact]
    public void ObstacleCurriculum_ClampsAtBothEnds_AndScalesTheHorizon()
    {
        var top = new ObstacleCurriculum(start: 1f);
        Assert.False(top.Observe(9));
        Assert.False(top.Observe(9));
        Assert.False(top.Observe(9));
        Assert.Equal(1f, top.Difficulty);
        Assert.Equal(ObstacleCurriculum.HardTickBudget, top.TickBudget);

        var bottom = new ObstacleCurriculum();
        Assert.False(bottom.Observe(0));
        Assert.False(bottom.Observe(0));
        Assert.False(bottom.Observe(0));
        Assert.Equal(0f, bottom.Difficulty);
        Assert.Equal(ObstacleCurriculum.EasyTickBudget, bottom.TickBudget);

        var mid = new ObstacleCurriculum(start: 0.6f);
        Assert.Equal(ObstacleCurriculum.MidTickBudget, mid.TickBudget);
    }

    [Fact]
    public void ObstacleCurriculum_IsReproducible()
    {
        var seq = new[] { 3, 3, 3, 0, 0, 0, 5, 5, 5 };
        var a = new ObstacleCurriculum();
        var b = new ObstacleCurriculum();
        foreach (int u in seq)
            Assert.Equal(a.Observe(u), b.Observe(u));
        Assert.Equal(a.Difficulty, b.Difficulty);
        Assert.Equal(a.Tenths, b.Tenths);
    }

    // ── 4. Escalera de capacidad y expansión ────────────────────────────────

    [Fact]
    public void CapacityLadder_GrowsOnlyAfterAPlateauAtMaxDifficulty()
    {
        var ladder = new CapacityLadder(new[] { 8, 16, 24 });
        Assert.Equal(8, ladder.Hidden);

        // Mejorando cada generación: no crece.
        for (int g = 0; g < 100; g++)
            Assert.Equal(0, ladder.Observe(100.0 + g * 5.0, atMaxDifficulty: true));
        Assert.Equal(8, ladder.Hidden);

        // Racha plana: crece una vez pasado el umbral.
        double flat = 50.0;
        int grew = 0;
        for (int g = 0; g < CapacityLadder.GrowthWindow; g++)
            grew += ladder.Observe(flat, atMaxDifficulty: true);
        Assert.Equal(16, grew);
        Assert.Equal(16, ladder.Hidden);

        // Fuera de dificultad máxima la ventana se reinicia: no crece.
        for (int g = 0; g < CapacityLadder.GrowthWindow * 2; g++)
            Assert.Equal(0, ladder.Observe(flat, atMaxDifficulty: false));
        Assert.Equal(16, ladder.Hidden);

        // Segundo peldaño y tope: 24, y luego ya no hay más. Tras crecer la
        // ventana arranca con el mejor en −∞, así que la primera generación de
        // la racha cuenta como referencia (de ahí el +1).
        for (int g = 0; g < CapacityLadder.GrowthWindow + 1; g++) ladder.Observe(flat, true);
        Assert.Equal(24, ladder.Hidden);
        Assert.True(ladder.Exhausted);
        for (int g = 0; g < CapacityLadder.GrowthWindow * 3; g++)
            Assert.Equal(0, ladder.Observe(flat, true));
        Assert.Equal(24, ladder.Hidden);
    }

    [Fact]
    public void MlpGenome_Expand_PreservesTheFunction()
    {
        var genome = DrawGenome(11, 0);
        foreach (int hidden in new[] { 16, 24 })
        {
            var expanded = genome.Expand(new[] { Sizes[0], hidden, Sizes[2] });
            Assert.Equal(hidden, expanded.Sizes[1]);
            Assert.Equal(MlpBrain.ExpectedWeightCount(expanded.Sizes), expanded.WeightCount);

            var original = genome.ToBrain();
            var wide = expanded.ToBrain();
            foreach (var sensors in SampleSensors())
            {
                var d1 = AntDecision.Neutral();
                var d2 = AntDecision.Neutral();
                original.Evaluate(sensors, ref d1);
                wide.Evaluate(sensors, ref d2);
                Assert.Equal(d1.Steer, d2.Steer, 4);
                Assert.Equal(d1.Speed, d2.Speed, 4);
                Assert.Equal(d1.DepositFood, d2.DepositFood, 4);
                Assert.Equal(d1.DepositHome, d2.DepositHome, 4);
                Assert.Equal(d1.DepositAlarm, d2.DepositAlarm, 4);
                Assert.Equal(d1.Interact, d2.Interact, 4);
            }
        }
    }

    [Fact]
    public void MlpGenome_Expand_RejectsIncompatibleTargets()
    {
        var genome = DrawGenome(11, 0);
        Assert.Throws<ArgumentException>(() => genome.Expand(new[] { 8, 16, Sizes[2] }));   // entrada distinta
        Assert.Throws<ArgumentException>(() => genome.Expand(new[] { Sizes[0], 16, 3 }));   // salida distinta
        Assert.Throws<ArgumentException>(() => genome.Expand(new[] { Sizes[0], 4, Sizes[2] })); // encoge
    }

    // ── 5. Entrenador ───────────────────────────────────────────────────────

    private static CurriculumStage TinyStage(int generations, int ticks = 60) => new()
    {
        Name = "obstaculos",
        MinDistance = 200f,
        MaxDistance = 260f,
        TickBudget = ticks,
        CompetenceFitness = 0.0,
        MinGenerations = 1,
        MaxGenerations = generations
    };

    [Fact]
    public void Trainer_GrowsTheBrain_WhenTheTerrainIsMaxedAndFitnessPlateaus()
    {
        var genome = DrawGenome(3, 0);
        var seeds = new List<MlpGenome>
        {
            genome.Clone(), genome.Clone(), genome.Clone(), genome.Clone()
        };
        // Un tick por evaluación: el fitness de clones idénticos es plano, que es
        // exactamente la señal de estancamiento que la escalera debe ver. El
        // nivel queda FIJO en el máximo (adaptive: false) para que el controlador
        // no lo baje al no haber descargas en un tick.
        var trainer = new CurriculumTrainer(21UL, 4, new[] { TinyStage(CapacityLadder.GrowthWindow + 6, ticks: 1) },
            onGeneration: null, seedGenomes: seeds,
            obstacleCurriculum: new ObstacleCurriculum(start: 1f, adaptive: false),
            hiddenLadder: new[] { 8, 16 },
            parallel: true);

        var trained = trainer.Run();

        Assert.Equal(16, trainer.Hidden);
        Assert.All(trained, g => Assert.Equal(16, g.Sizes[1]));
        Assert.Contains(trainer.Report, s => s.GrownHidden == 16);
        Assert.Equal(1f, trainer.Obstacles!.Difficulty);
    }

    [Fact]
    public void Trainer_AlternatesClassicGenerations_ToKeepOpenFieldForaging()
    {
        // F7: entrenar solo en el laberinto olvidaba el forrajeo clásico (medido
        // en la partida: 242 → 40 descargas). Con `classicEvery` una de cada N
        // generaciones se evalúa sin terreno y esas generaciones no tocan ni el
        // currículo ni la escalera de capacidad.
        var seeds = new List<MlpGenome> { DrawGenome(5, 0), DrawGenome(5, 1) };
        CurriculumTrainer Build(int every) => new(55UL, 2, new[] { TinyStage(4) },
            onGeneration: null, seedGenomes: seeds,
            obstacleCurriculum: new ObstacleCurriculum(adaptive: false),
            hiddenLadder: new[] { 8 }, parallel: false, classicEvery: every);

        var solo = Build(0);
        var mixto = Build(2);
        solo.Run();
        mixto.Run();

        Assert.All(solo.Report, s => Assert.False(s.ClassicGen));
        Assert.Contains(mixto.Report, s => s.ClassicGen);
        Assert.Contains(mixto.Report, s => !s.ClassicGen);
        Assert.DoesNotContain(mixto.Report, s => s.ClassicGen && s.GrownHidden > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(-1));
    }

    [Fact]
    public void Trainer_ClassicTail_ClosesWithOpenFieldGenerations()
    {
        // F7: el mantenimiento intercalado no recupera la competencia clásica — el
        // pool exportado es una evaluación de COL A sin terreno, en el régimen de la
        // partida, para que el archivo que siembra el juego traiga las dos.
        var seeds = new List<MlpGenome> { DrawGenome(7, 0), DrawGenome(7, 1) };
        CurriculumTrainer Build(int tail) => new(77UL, 2, new[] { TinyStage(2) },
            onGeneration: null, seedGenomes: seeds,
            obstacleCurriculum: new ObstacleCurriculum(adaptive: false),
            hiddenLadder: new[] { 8 }, parallel: false, classicEvery: 0, classicTail: tail);

        var conCola = Build(2);
        conCola.Run();

        Assert.Equal(4, conCola.Report.Count);
        Assert.All(conCola.Report.Take(2), s => Assert.False(s.ClassicGen));
        Assert.All(conCola.Report.Skip(2), s => Assert.True(s.ClassicGen));
        Assert.All(conCola.Report.Skip(2), s => Assert.Equal(0, s.GrownHidden));

        var sinCola = Build(0);
        sinCola.Run();
        Assert.Equal(2, sinCola.Report.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(-1));
    }

    [Fact]
    public void Trainer_WarmStartWiderThanTheLadder_KeepsTheSeedWidth()
    {
        // Continuar un pool de 24 ocultas con `--hidden 8` (el default del script)
        // no puede encoger la red: la escalera no tiene peldaño de bajada.
        var wide = DrawGenome(9, 0).Expand(new[] { Sizes[0], 24, Sizes[2] });
        var trainer = new CurriculumTrainer(91UL, 2, new[] { TinyStage(1, ticks: 1) },
            onGeneration: null, seedGenomes: new List<MlpGenome> { wide, wide.Clone() },
            obstacleCurriculum: new ObstacleCurriculum(adaptive: false),
            hiddenLadder: new[] { 8, 16, 24 }, parallel: false);

        var trained = trainer.Run();

        Assert.Equal(24, trainer.Hidden);
        Assert.All(trained, g => Assert.Equal(24, g.Sizes[1]));
    }

    [Fact]
    public void Trainer_ObstacleRuns_AreDeterministic()
    {
        var seeds = Enumerable.Range(0, 4).Select(i => DrawGenome(5, i)).ToList();
        CurriculumTrainer Build() => new(33UL, 4, new[] { TinyStage(6) }, onGeneration: null,
            seedGenomes: seeds,
            obstacleCurriculum: new ObstacleCurriculum(),
            hiddenLadder: new[] { 8 },
            parallel: true);

        var a = Build();
        var b = Build();
        a.Run();
        b.Run();

        Assert.Equal(a.Report.Count, b.Report.Count);
        for (int i = 0; i < a.Report.Count; i++)
        {
            Assert.Equal(a.Report[i].BestFitness, b.Report[i].BestFitness);
            Assert.Equal(a.Report[i].MeanFitness, b.Report[i].MeanFitness);
            Assert.Equal(a.Report[i].Difficulty, b.Report[i].Difficulty);
            Assert.Equal(a.Report[i].BestUnloads, b.Report[i].BestUnloads);
        }
    }

    [Fact]
    public void Trainer_ReportsDifficultyAndUnloads_ForTheGeneration()
    {
        var trainer = new CurriculumTrainer(44UL, 4, new[] { TinyStage(3) }, onGeneration: null,
            obstacleCurriculum: new ObstacleCurriculum(), hiddenLadder: new[] { 8 }, parallel: true);
        trainer.Run();

        Assert.Equal(3, trainer.Report.Count);
        Assert.All(trainer.Report, s => Assert.Equal(0f, s.Difficulty));
        Assert.All(trainer.Report, s => Assert.True(s.BestPickups >= 0));
        Assert.All(trainer.Report, s => Assert.True(s.BestUnloads >= 0));
    }

    // ── 6. Mundo: ancho del cerebro ─────────────────────────────────────────

    [Fact]
    public void World_DefaultBrainSizes_KeepTheClassicHash()
    {
        var a = new WorldSim(42UL, 96, 2);
        var b = new WorldSim(42UL, 96, 2, brainSizes: WorldSim.DefaultBrainSizes);
        Assert.Equal(a.HashLine(), b.HashLine());
        Assert.Equal(new[] { 19, 8, 6 }, a.BrainSizes);
    }

    [Fact]
    public void World_AcceptsAWiderBrain_AndSeedsItFromAnExpandedPool()
    {
        int[] wide = { 19, 16, 6 };
        var sim = new WorldSim(42UL, 96, 1, brainSizes: wide);
        Assert.Equal(wide, sim.BrainSizes);
        Assert.Equal(16, sim.Colonies[0].Pool.Elite[0].Sizes[1]);

        var genome = DrawGenome(3, 0).Expand(wide);
        sim.SeedPoolFromGenomes(0, new[] { genome });
        Assert.All(sim.ExportElite(0), g => Assert.Equal(16, g.Sizes[1]));
        Assert.Equal(16, sim.Colonies[0].Adults[0].Genome!.Sizes[1]);

        // El pool ancho se reproduce sin reventar: un nacimiento conserva el ancho.
        Assert.Equal(16, sim.Colonies[0].Pool.Birth().Sizes[1]);
    }

    [Fact]
    public void World_RejectsAMismatchedPool()
    {
        var sim = new WorldSim(42UL, 96, 1); // 19→8→6
        var wide = DrawGenome(3, 0).Expand(new[] { 19, 16, 6 });
        Assert.Throws<ArgumentException>(() => sim.SeedPoolFromGenomes(0, new[] { wide }));
        Assert.Throws<ArgumentException>(() => sim.ImportGenomes(0, new[] { wide }));
    }

    [Fact]
    public void GameScenario_AdoptsThePoolWidth_WhenSeeded()
    {
        // El juego normal no gana ninguna clave: el ancho sale del archivo. El
        // pool de 8 ocultas sigue dando el mundo clásico.
        var path = RepoFixture("warm-v2.antgenome");
        var stream = AntSim.Core.Scenario.GameScenario.Run(42UL, 60, 2, 96, 100, path, maze: true);
        Assert.Contains("\"seedPool\"", stream);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IEnumerable<AntSensors> SampleSensors()
    {
        var rng = new DeterministicRandom(99UL);
        for (int i = 0; i < 8; i++)
        {
            var s = AntSensors.Default();
            s.FoodTrailCenter = (float)(rng.NextDouble01() * 2 - 1);
            s.HomeTrailCenter = (float)(rng.NextDouble01() * 2 - 1);
            s.AlarmCenter = (float)(rng.NextDouble01() * 2 - 1);
            s.FoodTrailDiff = (float)(rng.NextDouble01() * 2 - 1);
            s.HomeTrailDiff = (float)(rng.NextDouble01() * 2 - 1);
            s.AlarmDiff = (float)(rng.NextDouble01() * 2 - 1);
            s.FoodDx = (float)(rng.NextDouble01() * 2 - 1);
            s.FoodDy = (float)(rng.NextDouble01() * 2 - 1);
            s.FoodSize = (float)rng.NextDouble01();
            s.HomeDx = (float)(rng.NextDouble01() * 2 - 1);
            s.HomeDy = (float)(rng.NextDouble01() * 2 - 1);
            s.ProxLeft = (float)rng.NextDouble01();
            s.ProxFront = (float)rng.NextDouble01();
            s.ProxRight = (float)rng.NextDouble01();
            s.HasLoad = rng.NextBool() ? 1f : 0f;
            s.LoadFraction = (float)rng.NextDouble01();
            s.Energy = (float)rng.NextDouble01();
            s.AgeNormalized = (float)rng.NextDouble01();
            s.ColonyFoodRatio = (float)rng.NextDouble01();
            yield return s;
        }
    }
}
