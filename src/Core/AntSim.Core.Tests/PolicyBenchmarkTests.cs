using System;
using System.IO;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Training;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3bis — benchmark de políticas: la política SCRIPTED (reglas a mano), el
/// hook que la fija para toda la colonia y la tabla aleatoria/scripted/evolucionada.
///
/// Lo que se fija aquí es lo que hace creíble la tabla publicada: las reglas del
/// scripted (para que se pueda discutir el baseline, no solo mirarlo), que el hook
/// es INERTE sin política fijada (el mundo de siempre no se mueve: los pines de CI
/// siguen valiendo), que la fila aleatoria es determinista y que las dos políticas
/// buenas son COMPETENTES (≥ 1 descarga) donde la aleatoria no llega ni a recoger.
/// </summary>
public class PolicyBenchmarkTests
{
    private static string RepoFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
               && !File.Exists(Path.Combine(dir.FullName, ".git"))
               && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "fixtures", name);
    }

    private static MlpGenome Elite()
    {
        string path = RepoFixture("warm-v2.antgenome");
        Assert.True(File.Exists(path), "falta tests/fixtures/warm-v2.antgenome");
        var (_, genomes) = AntGenomeFile.ReadFile(path, BrainContract.CurrentVersion);
        MlpGenome best = genomes[0];
        for (int i = 1; i < genomes.Count; i++)
            if (genomes[i].Fitness > best.Fitness) best = genomes[i];
        return best;
    }

    // ————— 1. las reglas del scripted, en aislamiento —————

    [Fact]
    public void Scripted_ConCarga_VuelveAlNidoYDescarga()
    {
        var brain = new ScriptedBrain();
        var s = AntSensors.Default();
        s.HasLoad = 1f;
        s.HomeDx = 0.2f;   // el nido queda a la IZQUIERDA (+Y local = izquierda)
        s.HomeDy = 1f;
        var d = AntDecision.Neutral();

        brain.Evaluate(in s, ref d);

        Assert.True(d.Steer > 0f, "gira a la izquierda hacia el nido");
        Assert.Equal(1f, d.Speed);
        Assert.Equal(1f, d.Interact, 3);            // intenta soltar: el mundo concede al llegar
        Assert.Equal(ScriptedBrain.FoodDeposit, d.DepositFood, 3);
        Assert.Equal(ScriptedBrain.HomeDeposit, d.DepositHome, 3);
    }

    [Fact]
    public void Scripted_SinCargaYConComida_LaPersigue()
    {
        var brain = new ScriptedBrain();
        var s = AntSensors.Default();
        s.FoodSize = 0.8f;
        s.FoodDx = 1f;     // de frente
        s.FoodDy = 0f;
        var d = AntDecision.Neutral();

        brain.Evaluate(in s, ref d);

        Assert.Equal(0f, d.Steer, 3);               // recto hacia ella
        Assert.Equal(ScriptedBrain.SightDeposit, d.DepositFood, 3);
        Assert.Equal(1f, d.Interact, 3);
    }

    [Fact]
    public void Scripted_SinNadaALaVista_MantieneElRumbo_YElRastroManda()
    {
        var brain = new ScriptedBrain();
        var s = AntSensors.Default();
        var recto = AntDecision.Neutral();
        brain.Evaluate(in s, ref recto);
        Assert.Equal(0f, recto.Steer, 3);           // dispersión en línea recta

        s.FoodTrailDiff = 0.6f;                     // rastro a la izquierda
        var conRastro = AntDecision.Neutral();
        brain.Evaluate(in s, ref conRastro);
        Assert.True(conRastro.Steer > 0f, "sigue el rastro de comida");
    }

    [Fact]
    public void Scripted_ConLaParedCerca_VuelveHaciaElCentro()
    {
        var brain = new ScriptedBrain();
        var s = AntSensors.Default();
        s.ProxLeft = 1f; s.ProxFront = 1f; s.ProxRight = 1f;
        s.HomeDx = -1f;  // el nido queda DETRÁS
        s.HomeDy = 0f;
        var d = AntDecision.Neutral();

        brain.Evaluate(in s, ref d);

        Assert.NotEqual(0f, d.Steer);               // no se queda pegado al borde
    }

    [Fact]
    public void SteerTowards_SaturaYNoDependeDeLaLongitud()
    {
        // Control proporcional: misma dirección, distinta magnitud normalizada.
        Assert.Equal(ScriptedBrain.SteerTowards(1f, 0f), ScriptedBrain.SteerTowards(0.5f, 0f), 3);
        // ±90° satura a ±1 (giro máximo) sin desbordar el contrato.
        Assert.Equal(1f, ScriptedBrain.SteerTowards(0f, 1f), 3);
        Assert.Equal(-1f, ScriptedBrain.SteerTowards(0f, -1f), 3);
        Assert.Equal(0f, ScriptedBrain.SteerTowards(0f, 0f), 3);
    }

    // ————— 2. el hook: inerte sin política, efectivo con ella —————

    [Fact]
    public void ForcedPolicy_Nula_NoMueveElMundo()
    {
        var a = new WorldSim(42UL, 96, 1);
        var b = new WorldSim(42UL, 96, 1);
        b.ForcePolicy(null);   // desactivar el hook NO puede cambiar el mundo
        for (int i = 0; i < 400; i++) { a.Step(); b.Step(); }
        Assert.Equal(a.HashLine(), b.HashLine());
    }

    [Fact]
    public void ForcedPolicy_LlegaALasNacidas()
    {
        var policy = new ScriptedBrain();
        var sim = new WorldSim(3UL, 48, 1);
        sim.ForcePolicy(policy);   // fundadoras incluidas, no solo las nacidas
        for (int i = 0; i < 2400; i++) sim.Step();

        Assert.True(sim.Colonies[0].Adults.Count > 0);
        foreach (var ant in sim.Colonies[0].Adults)
        {
            Assert.Same(policy, ant.Brain);
            Assert.Null(ant.Genome);   // sin genoma: no realimenta el pool
        }
    }

    // ————— 3. la tabla —————

    [Fact]
    public void Benchmark_EsDeterminista_YOrdenaAleatoriaScriptedEvolucionada()
    {
        var seeds = new ulong[] { 42, 7 };
        var a = PolicyBenchmark.Run(seeds, 200f, 260f, ticks: 5400, trials: 1, Elite(), randomSeed: 1UL);
        var b = PolicyBenchmark.Run(seeds, 200f, 260f, ticks: 5400, trials: 1, Elite(), randomSeed: 1UL);

        Assert.Equal(a.RenderText(), b.RenderText());
        Assert.Equal(4, a.Rows.Count);

        // Fila 0: aleatoria. El paseo aleatorio no alcanza la banda de comida
        // antes de morir — es el hallazgo que hace falta para poder afirmar que
        // las otras dos políticas hacen algo.
        var random = a.Rows[0];
        Assert.Equal(PolicyBenchmark.RandomName, random.Name);
        Assert.Equal(0.0, random.PickupsMean);
        Assert.Equal(0.0, random.UnloadsMean);

        // Filas 1 y 2: scripted y evolucionada, ambas muy por encima del azar.
        var scripted = a.Rows[1];
        var evolved = a.Rows[2];
        Assert.Equal(PolicyBenchmark.ScriptedName, scripted.Name);
        Assert.Equal(PolicyBenchmark.EvolvedName, evolved.Name);
        Assert.True(scripted.FitnessMean > 10 * random.FitnessMean,
            $"scripted {scripted.FitnessMean} vs aleatoria {random.FitnessMean}");
        Assert.True(evolved.FitnessMean > 10 * random.FitnessMean,
            $"evolucionada {evolved.FitnessMean} vs aleatoria {random.FitnessMean}");
        Assert.Equal(2, scripted.SeedsWithUnload);
        Assert.Equal(2, evolved.SeedsWithUnload);

        // La tabla markdown tiene una fila por política más la de referencia.
        string md = a.RenderMarkdown();
        Assert.Contains(PolicyBenchmark.EvolvedPoolName, md);
        Assert.Equal(6, md.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
