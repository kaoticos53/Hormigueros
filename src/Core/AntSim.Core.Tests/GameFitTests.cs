using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.Training;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F10 — SELECCIÓN POR DESCARGAS EN LA PARTIDA REAL. Lo que se fija aquí es el
/// contrato de la vara nueva: el evaluador corre la partida del lanzador
/// (montones, rastro y rival salvaje), cuenta descargas de la colonia sembrada y
/// es determinista; el trainer obedece al evaluador en vez de a la arena y no
/// admite las dos varas a la vez. La mejora en partida se mide aparte (tirada
/// larga contra el pool de referencia); aquí se verifica la mecánica.
/// </summary>
public class GameFitTests
{
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

    [Fact]
    public void Partida_MismoGenomaYSemilla_EsDeterminista()
    {
        // Misma semilla + mismo genoma ⇒ misma partida bit a bit, aunque el
        // evaluador corra varias pruebas: la semilla de cada prueba es función
        // pura de la del genoma (el paralelo no cambia el resultado).
        var genome = DrawGenome(11UL, 0);
        var a = new GameEvaluator(ticks: 600, trials: 2);
        var b = new GameEvaluator(ticks: 600, trials: 2);

        var r1 = a.Evaluate(4242UL, genome);
        var r2 = b.Evaluate(4242UL, genome);

        Assert.Equal(r1.Fitness, r2.Fitness);
        Assert.Equal(r1.Unloads, r2.Unloads);
        Assert.Equal(r1.Pickups, r2.Pickups);
    }

    [Fact]
    public void Partida_CuentaLasDescargasDeLaColoniaSembrada()
    {
        // Un pool competente descarga en la partida real (montones + rastro), y el
        // fitness es descargas × peso + pickups: la descarga domina cualquier
        // desempate.
        var (_, genomes) = AntGenomeFile.ReadFile(RepoFixture("warm-v2.antgenome"),
            BrainContract.CurrentVersion);
        MlpGenome elite = genomes[0];
        for (int i = 1; i < genomes.Count; i++)
            if (genomes[i].Fitness > elite.Fitness) elite = genomes[i];

        var result = new GameEvaluator(ticks: 6000, trials: 1).Evaluate(99UL, elite);

        Assert.True(result.Unloads > 0,
            $"el pool competente no descargó en la partida (pickups {result.Pickups})");
        Assert.InRange(result.Fitness,
            result.Unloads * GameEvaluator.DownloadWeight + result.Pickups - 1.0,
            result.Unloads * GameEvaluator.DownloadWeight + result.Pickups + 1.0);
    }

    [Fact]
    public void Trainer_ConEvaluadorDePartida_PuntuaConEl()
    {
        // El trainer no construye arena: pide la puntuación al evaluador, una vez
        // por genoma y generación, y la usa como fitness de la población.
        var stages = new List<CurriculumStage>
        {
            new() { Name = "partida", MinGenerations = 1, MaxGenerations = 2, TickBudget = 8 }
        };
        var invoked = new List<(ulong Seed, int Hidden)>();
        Func<ulong, MlpGenome, ArenaResult> evaluator = (seed, genome) =>
        {
            lock (invoked) invoked.Add((seed, genome.Sizes[1]));
            return new ArenaResult(5000.0, 7, 5);
        };

        var trainer = new CurriculumTrainer(3UL, 4, stages, onGeneration: null,
            gameEvaluator: evaluator, parallel: false);
        var population = trainer.Run();

        Assert.Equal(8, invoked.Count); // 4 genomas × 2 generaciones
        Assert.Equal(2, trainer.Report.Count);
        Assert.All(trainer.Report, r => Assert.Equal(5000.0, r.BestFitness));
        Assert.All(trainer.Report, r => Assert.Equal(5, r.BestUnloads));
        Assert.All(trainer.Report, r => Assert.Equal(7, r.BestPickups));
        Assert.All(population, g => Assert.Equal(5000.0, g.Fitness));
    }

    /// <summary>
    /// F11 — LA PUERTA DEL RASTRO. Con <c>TrailByBrain</c> el depósito del
    /// reflejo de reclutamiento se escala por una salida del cerebro; sin ella
    /// es la constante de F8. Para aislar el depósito de todo lo demás el mundo
    /// se construye con cerebros CONSTANTES: sin movimiento, sin comida cogida
    /// y sin nacimientos, la única fuente de las capas de feromona de la colonia
    /// es el depósito del tick.
    /// </summary>
    private sealed class ConstantBrain : IBrain
    {
        private readonly AntDecision _decision;

        public ConstantBrain(float deposit)
        {
            var d = AntDecision.Neutral();
            d.DepositFood = deposit;
            d.DepositHome = deposit;
            _decision = d;
        }

        public BrainKind Kind => BrainKind.Mlp;
        public int ContractVersion => BrainContract.CurrentVersion;
        public void Evaluate(in AntSensors sensors, ref AntDecision decision) => decision = _decision;
    }

    /// <summary>Feromona total de la colonia 0 (la suma de las dos capas que toca
    /// el reclutamiento) con todos sus cerebros fijados. <paramref name="ticks"/> es
    /// corto por defecto en la prueba de proporcionalidad: la capa satura por celda
    /// y un depósito de varios ticks se cae por el techo, no por la puerta.</summary>
    private static double TrailMass(ulong seed, bool trailByBrain, float deposit, int ticks = 40)
    {
        var tuning = RecruitmentTuning.Default;
        tuning.TrailByBrain = trailByBrain;
        var sim = new WorldSim(seed, gridCells: GameEvaluator.GridCells, colonyCount: 1,
            maze: true, recruitment: true, recruitmentTuning: tuning);
        var colony = sim.Colonies[0];
        var brain = new ConstantBrain(deposit);
        foreach (var ant in colony.Adults) ant.Brain = brain;
        for (int i = 0; i < ticks; i++) sim.Step();
        return colony.FoodLayer.SumOfValues() + colony.HomeLayer.SumOfValues();
    }

    [Fact]
    public void Rastro_PuertaDelCerebro_CierraElDepositoDelReflejo()
    {
        // Con la salida de depósito a 0 la hormiga no deja nada: la puerta manda
        // sobre el reflejo. Con la puerta apagada el MISMO cerebro (y el mismo
        // mundo) sigue dejando el rastro fijo de F8.
        double fixedTrail = TrailMass(4242UL, trailByBrain: false, deposit: 0f);
        double gatedTrail = TrailMass(4242UL, trailByBrain: true, deposit: 0f);

        Assert.True(fixedTrail > 0.0, "el reflejo fijo no depositó nada: la prueba no mide la puerta");
        Assert.Equal(0.0, gatedTrail);
    }

    [Fact]
    public void Rastro_PuertaDelCerebro_EscalaElDepositoConLaSalida()
    {
        // La cresta del rastro y el depósito genérico del canal escalan LOS DOS
        // por la salida, así que la mitad de salida es la mitad de feromona: es
        // una puerta continua, no un interruptor. Tres ticks, para que la capa no
        // llegue a su techo por celda y lo que se mida sea la puerta.
        double full = TrailMass(4242UL, trailByBrain: true, deposit: 1f, ticks: 3);
        double half = TrailMass(4242UL, trailByBrain: true, deposit: 0.5f, ticks: 3);

        Assert.True(full > 0.0);
        Assert.Equal(0.5, half / full, 3);
    }

    /// <summary>
    /// F11b — LA GLÁNDULA. Con precio, cada gota de feromona sale de una reserva
    /// personal que solo se rellena en el nido. Las hormigas se colocan en la
    /// esquina MÁS LEJOS del nido —si se quedan dentro se recargan y la prueba no
    /// mediría el presupuesto— y el cerebro constante no las mueve de ahí.
    /// </summary>
    private static double GlandMass(ulong seed, float glandSeconds, float deposit, int ticks,
        bool atNest = false)
    {
        var tuning = RecruitmentTuning.Default;
        tuning.TrailGlandSeconds = glandSeconds;
        var sim = new WorldSim(seed, gridCells: GameEvaluator.GridCells, colonyCount: 1,
            maze: false, recruitment: true, recruitmentTuning: tuning);
        var colony = sim.Colonies[0];
        var brain = new ConstantBrain(deposit);
        // Esquina opuesta al nido y cada hormiga en SU celda: una sola celda con
        // diez hormigas se llenaría hasta el tope Cmax (1,0) y lo que se mediría
        // sería el techo de la capa, no la reserva.
        float far = SimConstants.CellSizeUnits * 4f; // 32 u del borde
        float farX = colony.NestX > sim.WorldWidth * 0.5f ? far : sim.WorldWidth - far;
        float farY = colony.NestY > sim.WorldHeight * 0.5f ? far : sim.WorldHeight - far;
        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var ant = colony.Adults[i];
            ant.Brain = brain;
            if (!atNest)
            {
                ant.X = farX;
                ant.Y = farY + i * 3f * SimConstants.CellSizeUnits;
            }
        }
        for (int i = 0; i < ticks; i++) sim.Step();
        return colony.FoodLayer.SumOfValues() + colony.HomeLayer.SumOfValues();
    }

    [Fact]
    public void Rastro_Glandula_CobraElDepositoDeLaReserva()
    {
        // El MISMO cerebro pidiendo lo mismo deja menos rastro con la glándula
        // corta: el depósito ya no es gratis. Con reserva de sobra se reproduce
        // el mundo sin precio, que es la condición de control.
        // Seis ticks (0,2 s): el depósito se queda muy por debajo del tope por
        // celda, así que la diferencia mide la reserva y no el techo de la capa.
        double free = GlandMass(4242UL, glandSeconds: 0f, deposit: 1f, ticks: 6);
        double small = GlandMass(4242UL, glandSeconds: 0.05f, deposit: 1f, ticks: 6);
        double big = GlandMass(4242UL, glandSeconds: 0.5f, deposit: 1f, ticks: 6);

        Assert.True(free > 0.0, "sin precio el reflejo no dejó nada: la prueba no mide el precio");
        Assert.True(small < free * 0.4, $"la glándula corta no cobró: {small} contra {free}");
        Assert.True(big > small * 2.0, $"más glándula tendría que dejar más rastro: {big} contra {small}");
    }

    [Fact]
    public void Rastro_Glandula_SoloSeRellenaEnElNido()
    {
        // Fuera del nido la reserva no vuelve: agotada, la hormiga puede seguir
        // pidiendo depósito y no deja nada más. Dentro del nido se recarga y el
        // rastro sigue creciendo — es lo que convierte el depósito en presupuesto.
        double away = GlandMass(4242UL, glandSeconds: 0.1f, deposit: 1f, ticks: 200);
        double home = GlandMass(4242UL, glandSeconds: 0.1f, deposit: 1f, ticks: 200, atNest: true);

        // Solo recargan las que caen DENTRO del radio del nido (las fundadoras
        // nacen alrededor), así que la diferencia medida es un SUELO de lo que
        // aporta la recarga: las que quedaron fuera solo gastan su estallido.
        Assert.True(home > away * 1.5,
            $"la recarga del nido no aportó rastro: {home} en el nido contra {away} fuera");
    }

    [Fact]
    public void Trainer_NoAdmiteArenaYPartidaALaVez()
    {
        var stages = new List<CurriculumStage>
        {
            new() { Name = "partida", MinGenerations = 1, MaxGenerations = 1, TickBudget = 8 }
        };
        Assert.Throws<ArgumentException>(() => new CurriculumTrainer(1UL, 4, stages,
            obstacleCurriculum: new ObstacleCurriculum(),
            gameEvaluator: (seed, genome) => default));
    }
}
