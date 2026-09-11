using System;
using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Core.Telemetry;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

public static class TestPaths
{
    /// <summary>Ruta relativa a la raíz del repo (los tests corren desde bin/...).</summary>
    public static string RepoPath(string rel)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git"))
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AntSim.slnx")))
            dir = dir.Parent!;
        return System.IO.Path.Combine(dir!.FullName, rel);
    }
}

/// <summary>
/// Contrato del AlertDeriver (F4.2): cada gatillo de docs/fase4-hud-contrato.md §1
/// dispara la alerta correcta, con su nivel, texto y cadencia — y los rate limits
/// no se pasan. Puro: nunca toca el mundo.
/// </summary>
public sealed class AlertDeriverTests
{
    private static SimEvent Ev(SimEventKind kind, int colony = 0, uint ant = 1,
        float x = 100f, float y = 100f, byte cause = 0, ulong tick = 1)
        => new(kind, tick, colony, ant, x, y, cause);

    private static WorldSim Sim(int colonies = 1)
    {
        var sim = new WorldSim(42, 32, colonyCount: colonies);
        for (int i = 0; i < 5; i++) sim.Step(); // asentar fundadoras
        return sim;
    }

    [Fact]
    public void PrimeraDescarga_HitoVerde_Unico()
    {
        var sim = Sim();
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();

        // Descarga: pickup + unload de la misma hormiga.
        var evs = new List<SimEvent> { Ev(SimEventKind.Pickup), Ev(SimEventKind.Unload) };
        relay.Observe(evs, sim);
        deriver.Observe(evs, null, relay, sim, output);

        var green = output.FindAll(a => a.Key == "first-unload");
        Assert.Single(green);
        Assert.Equal(AlertDeriver.Level.Green, green[0].Lvl);
        Assert.Contains("Primera descarga", green[0].Text);
        Assert.Contains("la colonia completa ciclos", green[0].Text);

        // Segunda descarga: no repite el hito.
        output.Clear();
        relay.Observe(evs, sim);
        deriver.Observe(evs, null, relay, sim, output);
        Assert.DoesNotContain(output, a => a.Key == "first-unload");
    }

    [Fact]
    public void ColoniaExtinta_Roja_UnaVezPorColonia()
    {
        var sim = Sim(colonies: 2);
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();

        var evs = new List<SimEvent> { Ev(SimEventKind.ColonyExtinct, colony: 1, x: 500f, y: 500f) };
        relay.Observe(evs, sim);
        deriver.Observe(evs, null, relay, sim, output);

        var red = output.FindAll(a => a.Key == "extinct:1");
        Assert.Single(red);
        Assert.Equal(AlertDeriver.Level.Red, red[0].Lvl);
        Assert.Equal(1, red[0].ColonyId);
        Assert.InRange(red[0].X, 499f, 501f); // posición del nido para la cámara

        // Repetición del evento (no debería ocurrir del sim, pero el deriver
        // lo deduplica igualmente).
        output.Clear();
        deriver.Observe(evs, null, relay, sim, output);
        Assert.Empty(output);
    }

    [Fact]
    public void GenomeAlerts_CadenciaDe10Segundos()
    {
        var sim = Sim();
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();

        // 1ª entrada en élite: alerta.
        var evs = new List<SimEvent> { Ev(SimEventKind.GenomeEnteredElite) };
        deriver.Observe(evs, null, relay, sim, output);
        Assert.Contains(output, a => a.Key == "elite");

        // 2ª al tick siguiente: silenciada por la cadencia de 10 s (300 ticks).
        output.Clear();
        for (int i = 0; i < 299; i++) sim.Step();
        evs = new List<SimEvent> { Ev(SimEventKind.GenomeEnteredElite, tick: sim.Tick) };
        deriver.Observe(evs, null, relay, sim, output);
        Assert.DoesNotContain(output, a => a.Key == "elite");

        // Tras 10 s de sim: vuelve a pasar.
        output.Clear();
        sim.Step();
        evs = new List<SimEvent> { Ev(SimEventKind.GenomeEnteredElite, tick: sim.Tick) };
        deriver.Observe(evs, null, relay, sim, output);
        Assert.Contains(output, a => a.Key == "elite");
    }

    [Fact]
    public void MortalidadEnPicada_Ambar_Cadencia30Segundos()
    {
        var sim = Sim();
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();

        var frame = new MetricRecorder.MetricFrame(0, 30, 0, 0, 0, 6, 0, 0, 0, 0); // 6 muertes
        deriver.Observe(Array.Empty<SimEvent>(), frame, relay, sim, output);
        var amber = output.FindAll(a => a.Key == "mortality");
        Assert.Single(amber);
        Assert.Equal(AlertDeriver.Level.Amber, amber[0].Lvl);
        Assert.Contains("6 muertes", amber[0].Text);

        // Ventana con 4 muertes: bajo el umbral, sin alerta aunque pase la cadencia.
        output.Clear();
        var quiet = new MetricRecorder.MetricFrame(30, 60, 0, 0, 0, 4, 0, 0, 0, 0);
        for (int i = 0; i < 901; i++) sim.Step();
        deriver.Observe(Array.Empty<SimEvent>(), quiet, relay, sim, output);
        Assert.DoesNotContain(output, a => a.Key == "mortality");

        // Otra pico tras la cadencia (30 s = 900 ticks desde la última): alerta.
        output.Clear();
        sim.Step();
        var peak = new MetricRecorder.MetricFrame(60, 90, 0, 0, 0, 7, 0, 0, 0, 0);
        deriver.Observe(Array.Empty<SimEvent>(), peak, relay, sim, output);
        Assert.Contains(output, a => a.Key == "mortality");
    }

    [Fact]
    public void PuestaParada_Ambar_5VentanasYReservaBaja()
    {
        var sim = Sim();
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();

        // Reserva baja: vaciar el stock de la colonia.
        var colony = sim.Colonies[0];
        colony.Stock = colony.StockMax * 0.1f; // <20%

        // 5 ventanas seguidas sin huevos ni nacimientos.
        var empty = new MetricRecorder.MetricFrame(0, 30, 0, 0, 0, 0, 0, 0, 0, 0);
        for (int w = 0; w < 5; w++)
        {
            output.Clear();
            deriver.Observe(Array.Empty<SimEvent>(), empty, relay, sim, output);
            if (w < 4) Assert.DoesNotContain(output, a => a.Key == "laying:0");
        }
        Assert.Contains(output, a => a.Key == "laying:0");
        Assert.Equal(AlertDeriver.Level.Amber, output.Find(a => a.Key == "laying:0").Lvl);

        // Con reserva sana: nunca dispara.
        var sim2 = Sim();
        var deriver2 = new AlertDeriver();
        var output2 = new List<AlertDeriver.Alert>();
        for (int w = 0; w < 6; w++)
            deriver2.Observe(Array.Empty<SimEvent>(), empty, relay, sim2, output2);
        Assert.DoesNotContain(output2, a => a.Key == "laying:0");
    }

    [Fact]
    public void RelevoDebil_Ambar_PorEncogimientoSostenido()
    {
        // El contrato compara la media de carryLeg contra la media de las últimas
        // 5 emisiones: el shallowing debe ser SOSTENIDO (una pata suelta se diluye
        // en la media — así es el contrato, y así lo midió pipeline.sh).
        // Mundo grid 96 (umbrales de calibración de RelayVerdict): nido en
        // (384, 384); coords de evento relativas a él para que el drop NO
        // dispare su propio umbral y el test aísle el ENCOGIMIENTO.
        var sim = new WorldSim(42, 96, colonyCount: 1);
        for (int i = 0; i < 5; i++) sim.Step();
        float nx = sim.Colonies[0].NestX, ny = sim.Colonies[0].NestY;
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();

        uint antId = 1;
        // 5 cargas sanas de 100 u (media de historia ≈ 100), sueltas junto al nido.
        for (int i = 0; i < 5; i++)
        {
            var evs = new List<SimEvent>
            {
                Ev(SimEventKind.Pickup, x: nx - 100f, y: ny, ant: antId),
                Ev(SimEventKind.Unload, x: nx, y: ny, ant: antId)
            };
            antId++;
            relay.Observe(evs, sim);
            deriver.Observe(evs, null, relay, sim, output);
        }
        Assert.DoesNotContain(output, a => a.Key == "relay-weak");

        // Shallowing sostenido: cargas de 20 u. La media cae bajo 80 (−20% de la
        // historia) al segundo ciclo → alerta ámbar (aún >60: por ENCOGIMIENTO).
        bool fired = false;
        for (int k = 0; k < 4 && !fired; k++)
        {
            var evs = new List<SimEvent>
            {
                Ev(SimEventKind.Pickup, x: nx - 20f, y: ny, ant: antId),
                Ev(SimEventKind.Unload, x: nx, y: ny, ant: antId)
            };
            antId++;
            relay.Observe(evs, sim);
            deriver.Observe(evs, null, relay, sim, output);
            fired = output.Exists(a => a.Key == "relay-weak");
        }
        Assert.True(fired, "el shallowing sostenido debería disparar relay-weak");
        var weak = output.FindAll(a => a.Key == "relay-weak");
        Assert.Equal(AlertDeriver.Level.Amber, weak[0].Lvl);
        Assert.Contains("tramos más cortos", weak[0].Text);

        // Cadencia de 60 s: las siguientes emisiones débiles no repiten.
        output.Clear();
        int weakBefore = 0;
        for (int k = 0; k < 3; k++)
        {
            for (int i = 0; i < 100; i++) sim.Step(); // 10 s por ciclo
            var evs = new List<SimEvent>
            {
                Ev(SimEventKind.Pickup, x: nx - 20f, y: ny, ant: antId, tick: sim.Tick),
                Ev(SimEventKind.Unload, x: nx, y: ny, ant: antId, tick: sim.Tick)
            };
            antId++;
            relay.Observe(evs, sim);
            deriver.Observe(evs, null, relay, sim, output);
            weakBefore += output.FindAll(a => a.Key == "relay-weak").Count;
            output.Clear();
        }
        // 3 ciclos × 10 s < cadencia de 60 s: como mucho la primera repite (si
        // pasaron ≥60 ticks desde la última) — pero nunca una por ciclo.
        Assert.InRange(weakBefore, 0, 1);
    }

    [Fact]
    public void RelevoDebil_Ambar_DropLejos_EscalaConElMundo()
    {
        // F4.3: el drop máximo del relevo débil es el de RelayVerdict PARA ESTE
        // MUNDO (190 × grid/96). El mismo patrón de eventos es débil en 96 y
        // sano en 256 — la alerta enseña el umbral del mundo en su texto.
        var relay = new RelayTracker();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();
        uint antId = 1;

        // — Grid 96: anillo de comida a 200 u del nido > 190 ⇒ débil —
        //    (dropAvg del contrato = distancia de SPAWN de los ítems: el anillo
        //    de forrajeo; el unload va aparte en unloadAvg). Para que el relevo
        //    esté "activo" hace falta un Unload (en el nido, sin carry abierto).
        var sim96 = new WorldSim(42, 96, colonyCount: 1);
        for (int i = 0; i < 5; i++) sim96.Step();
        float nx96 = sim96.Colonies[0].NestX, ny96 = sim96.Colonies[0].NestY;
        var evs96 = new List<SimEvent>
        {
            Ev(SimEventKind.ItemSpawned, x: nx96 + 200f, y: ny96, ant: 0, tick: sim96.Tick),
            Ev(SimEventKind.Unload, x: nx96, y: ny96, ant: antId, tick: sim96.Tick),
        };
        relay.Observe(evs96, sim96);
        deriver.Observe(evs96, null, relay, sim96, output);
        var weak96 = output.FindAll(a => a.Key == "relay-weak");
        Assert.Single(weak96);
        Assert.Equal(AlertDeriver.Level.Amber, weak96[0].Lvl);
        Assert.Contains("demasiado lejos", weak96[0].Text);
        Assert.Contains("190", weak96[0].Text); // el umbral del mundo 96 en el texto

        // — El mismo patrón en grid 256 (umbral 506.7): SIN alerta —
        var relay2 = new RelayTracker();
        var deriver2 = new AlertDeriver();
        var output2 = new List<AlertDeriver.Alert>();
        var sim256 = new WorldSim(42, 256, colonyCount: 1);
        for (int i = 0; i < 5; i++) sim256.Step();
        float nx256 = sim256.Colonies[0].NestX, ny256 = sim256.Colonies[0].NestY;
        var evs256 = new List<SimEvent>
        {
            Ev(SimEventKind.ItemSpawned, x: nx256 + 200f, y: ny256, ant: 0, tick: sim256.Tick),
            Ev(SimEventKind.Unload, x: nx256, y: ny256, ant: 42, tick: sim256.Tick),
        };
        relay2.Observe(evs256, sim256);
        deriver2.Observe(evs256, null, relay2, sim256, output2);
        Assert.DoesNotContain(output2, a => a.Key == "relay-weak");
    }

    [Fact]
    public void EsPuroObservable_LosHashesNoCambian()
    {
        // El deriver solo lee: dos mundos idénticos, uno con deriver y otro sin,
        // producen el mismo hash final.
        string Hash(bool use)
        {
            var sim = new WorldSim(42, 32, colonyCount: 2);
            var relay = new RelayTracker();
            var metrics = new MetricRecorder();
            var deriver = use ? new AlertDeriver() : null;
            var output = new List<AlertDeriver.Alert>();
            for (int i = 0; i < 600; i++)
            {
                sim.Step();
                relay.Observe(sim.LastEvents, sim);
                metrics.Observe(sim.LastEvents);
                var frame = metrics.TakeFrame(sim.Tick);
                deriver?.Observe(sim.LastEvents, frame, relay, sim, output);
            }
            return sim.HashLine();
        }

        Assert.Equal(Hash(false), Hash(true));
    }

    [Fact]
    public void IntegracionCompleta_WarmV2_DerivaElHitoVerde()
    {
        // La cadena completa de la UI: partidas sembrada con warm-v2 → deriver
        // → hito verde. Sin pool el mundo en frío no descarga (baseline).
        var (_, seeded) = AntSim.Core.Evolution.AntGenomeFile.ReadFile(
            TestPaths.RepoPath("artifacts/pretrain-warm-v2.antgenome"), AntSim.Core.Brain.BrainContract.CurrentVersion);
        var sim = new WorldSim(42, 96, colonyCount: 1);
        sim.SeedPoolFromGenomes(0, seeded);
        var relay = new RelayTracker();
        var metrics = new MetricRecorder();
        var deriver = new AlertDeriver();
        var output = new List<AlertDeriver.Alert>();
        bool sawGreen = false;

        for (int i = 0; i < 24000 && !sawGreen; i++)
        {
            sim.Step();
            relay.Observe(sim.LastEvents, sim);
            metrics.Observe(sim.LastEvents);
            deriver.Observe(sim.LastEvents, metrics.TakeFrame(sim.Tick), relay, sim, output);
            foreach (var a in output)
                if (a.Key == "first-unload") sawGreen = true;
            output.Clear();
        }

        // warm-v2 en mundo estándar descarga siempre antes de 24k ticks (10/10 en
        // el benchmark): el hito verde debe haberse derivado del stream real.
        Assert.True(sawGreen, "warm-v2 debería disparar el hito de primera descarga");
    }

    [Fact]
    public void IntegracionCompleta_WarmV2_Grid256_SemaforoVerde()
    {
        // TODO F4.3 cerrado: en el mundo grande (grid 256, partida de juego real)
        // el semáforo de la colonia sembrada es VERDE con la regla escalada
        // (leg ≥ 60; drop ≤ 190 × grid/96). Centinela de
        // docs/fase4-hud-contrato.md §8 (seed 42: leg 64, drop 182.3).
        var (_, seeded) = AntSim.Core.Evolution.AntGenomeFile.ReadFile(
            TestPaths.RepoPath("artifacts/pretrain-warm-v2.antgenome"), AntSim.Core.Brain.BrainContract.CurrentVersion);
        var sim = new WorldSim(42, 256, colonyCount: 1);
        sim.SeedPoolFromGenomes(0, seeded);
        var relay = new RelayTracker();
        bool sawUnload = false;

        for (int i = 0; i < 24000 && !sawUnload; i++)
        {
            sim.Step();
            relay.Observe(sim.LastEvents, sim);
            if (relay.HasUnload) sawUnload = true;
        }

        Assert.True(sawUnload, "warm-v2 debería descargar en el mundo grande");
        var col = relay.ForColony(0);
        Assert.True(col.HasValue, "la colonia sembrada debe tener relevo observado");
        // Ambas reglas en la partida real: la absoluta (F4.3) y la normalizada
        // (F4.4, recomendada — el ratio no castiga la cadena corta completada).
        Assert.Equal(RelayLight.Green, RelayVerdict.Evaluate(
            (float?)col.Value.CarryLegMean, (float?)col.Value.DropMean, 256));
        Assert.Equal(RelayLight.Green, RelayVerdict.EvaluateNormalized(
            (float?)col.Value.CarryLegMean, (float?)col.Value.ChainMean,
            (float?)col.Value.DropMean, 256));
    }
}
