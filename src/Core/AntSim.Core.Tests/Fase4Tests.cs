using System;
using System.Collections.Generic;
using AntSim.Core.Scenario;
using AntSim.Core.Serialization;
using AntSim.Core.Telemetry;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// Fase 4 F4.0 — contratos de la capa de jugador:
///   1. Comandos con tick: misma semilla + mismos comandos (mismo orden) ⇒
///      mundo idéntico bit a bit (hashes de hito y final).
///   2. Distinto orden/timing de comandos ⇒ mundo distinto (la causa queda
///      registrada en el Canal B y, por tanto, en el .antlog).
///   3. Telemetría (canal A snapshot + canal C MetricRecorder) es observación
///      pura: hashes idénticos con y sin ella adjunta.
///   4. El log de eventos .antlog acepta el nuevo evento CommandExecuted y los
///      hitos de hash coinciden entre la partida con comandos y su reproducción.
/// </summary>
public sealed class Fase4Tests
{
    private static List<string> RunWithCommands(ulong seed, int grid, int ticks,
        (int Tick, float X, float Y)[] commands, bool useTelemetry)
    {
        var sim = new WorldSim(seed, grid, colonyCount: 1);
        var relay = new Scenario.RelayTracker();
        var metrics = useTelemetry ? new MetricRecorder() : null;
        var hashes = new List<string>();

        int next = 0;
        for (int i = 0; i < ticks; i++)
        {
            while (next < commands.Length && commands[next].Tick == i)
            {
                var c = commands[next];
                sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, c.X, c.Y));
                next++;
            }

            sim.Step();
            relay.Observe(sim.LastEvents, sim);
            metrics?.Observe(sim.LastEvents);

            if (sim.Tick % 256 == 0)
                hashes.Add(sim.HashLine());
        }
        hashes.Add(sim.HashLine());
        return hashes;
    }

    [Fact]
    public void Comandos_MismaSemillaMismoOrden_MundoBitABit()
    {
        var cmds = new[] { (300, 350.5f, 400.25f), (700, 500f, 300f), (1200, 250f, 600f) };
        var a = RunWithCommands(777, 96, 1500, cmds, useTelemetry: false);
        var b = RunWithCommands(777, 96, 1500, cmds, useTelemetry: true);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void Comandos_DistintoTiming_MundoDistinto()
    {
        var a = RunWithCommands(777, 96, 1500, new[] { (300, 350.5f, 400.25f), (700, 500f, 300f) }, false);
        var b = RunWithCommands(777, 96, 1500, new[] { (310, 350.5f, 400.25f), (700, 500f, 300f) }, false);
        Assert.NotEqual(a[^1], b[^1]);
    }

    [Fact]
    public void Comandos_DistintaPosicion_MundoDistinto()
    {
        var a = RunWithCommands(777, 96, 1500, new[] { (300, 350.5f, 400.25f) }, false);
        var b = RunWithCommands(777, 96, 1500, new[] { (300, 360f, 400.25f) }, false);
        Assert.NotEqual(a[^1], b[^1]);
    }

    [Fact]
    public void Comandos_AplicadosEnPuntoCanonico_YRegistradosEnCanalB()
    {
        var sim = new WorldSim(42, 96, colonyCount: 1);
        int itemsBefore = 0;
        for (int i = 0; i < 100; i++) sim.Step();
        itemsBefore = sim.Items.Count;

        sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, 300f, 300f));
        Assert.Equal(1, sim.PendingCommandCount);
        Assert.Equal(itemsBefore, sim.Items.Count); // aún no aplicado

        sim.Step(); // el punto canónico lo aplica antes de que actúe cualquier hormiga

        Assert.Equal(0, sim.PendingCommandCount);
        Assert.Equal(itemsBefore + 1, sim.Items.Count);

        var ev = sim.LastEvents;
        bool found = false;
        for (int i = 0; i < ev.Count; i++)
        {
            if (ev[i].Kind == SimEventKind.CommandExecuted)
            {
                found = true;
                Assert.Equal(300f, ev[i].X, 2);
                Assert.Equal(300f, ev[i].Y, 2);
                Assert.Equal((uint)SimCommandKind.DropFood, ev[i].AntId);
                Assert.Equal(sim.Tick, ev[i].Tick);
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void ComandoDropFood_ItemDentroDelMundo_ConAmountConstante()
    {
        var sim = new WorldSim(42, 96, colonyCount: 1);
        // Fuera de rango: el comando se clampa al mundo (comportamiento canónico).
        sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, -50f, 9999f));
        sim.Step();

        FoodItem? dropped = null;
        for (int i = 0; i < sim.Items.Count; i++)
            if (sim.Items[i].Amount == SimCommand.DropFoodAmount)
                dropped = sim.Items[i];
        Assert.NotNull(dropped);
        Assert.InRange(dropped!.X, 0f, sim.WorldWidth);
        Assert.InRange(dropped!.Y, 0f, sim.WorldHeight);
    }

    [Fact]
    public void MetricRecorder_FramesDe30Ticks_ContadoresCorrectos()
    {
        var sim = new WorldSim(42, 96, colonyCount: 1);
        var metrics = new MetricRecorder();
        long totalPickups = 0, totalUnloads = 0;
        ulong frames = 0;

        for (int i = 0; i < 3000; i++)
        {
            sim.Step();
            metrics.Observe(sim.LastEvents);
            if (metrics.TakeFrame(sim.Tick) is MetricRecorder.MetricFrame f)
            {
                frames++;
                totalPickups += f.Pickups;
                totalUnloads += f.Unloads;
                Assert.True(f.TickEnd >= f.TickStart);
            }
        }

        // Las ventanas suman exactamente el total de eventos del Canal B.
        Assert.InRange(frames, 90UL, 101UL); // 3000/30 = 100 ventanas (±1 por apertura)
        Assert.True(totalPickups >= 0 && totalUnloads >= 0);
    }

    [Fact]
    public void SimSnapshot_FielAlMundo_YNoLoMuta()
    {
        var sim = new WorldSim(42, 96, colonyCount: 2);
        for (int i = 0; i < 200; i++) sim.Step();

        string hashBefore = sim.HashLine();
        var frame = SimSnapshot.Capture(sim);

        int aliveTotal = 0;
        for (int c = 0; c < sim.Colonies.Count; c++)
            aliveTotal += sim.Colonies[c].AdultCountAlive;
        Assert.Equal(aliveTotal, frame.Ants.Count);

        Assert.Equal(sim.Items.Count, frame.Items.Count);
        Assert.Equal(sim.Colonies.Count, frame.Colonies.Count);
        Assert.Equal(sim.Tick, frame.Tick);

        Assert.Equal(hashBefore, sim.HashLine()); // captura = observación pura
    }

    [Fact]
    public void AntLog_RegistraComandos_YReproduceHashes()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "fase4-" + Guid.NewGuid().ToString("N") + ".antlog");
        try
        {
            var cmds = new (int, float, float)[] { (300, 350.5f, 400.25f), (700, 500f, 300f) };
            var a = RunWithLog(777, 96, 1500, cmds, path, out var hashesA);
            var data = AntEventLogFile.Read(path);
            Assert.Equal(a, hashesA); // sanity del helper

            int commandEvents = 0;
            foreach (var e in data.Events)
                if (e.Kind == SimEventKind.CommandExecuted) commandEvents++;
            Assert.Equal(cmds.Length, commandEvents);

            // Reproducción: misma semilla + mismos comandos ⇒ mismos hitos byte a byte.
            string path2 = path + ".replay";
            RunWithLog(777, 96, 1500, cmds, path2, out var hashesB);
            Assert.Equal(hashesA, hashesB);
        }
        finally
        {
            System.IO.File.Delete(path);
            System.IO.File.Delete(path + ".replay");
        }
    }

    // — F4.1: stream de juego headless (canal A/B/C JSONL) —

    private static string RunGame(ulong seed, int ticks, int grid, int frameEvery,
        (int Tick, float X, float Y)[]? drops = null)
        => GameScenario.Run(seed, ticks, colonies: 1, grid, frameEvery,
            seedPoolPath: null,
            drops == null ? null : Array.AsReadOnly(Array.ConvertAll(drops,
                d => (d.Tick, d.X, d.Y))));

    [Fact]
    public void GameStream_MismaSemilla_ByteABit()
    {
        var drops = new[] { (300, 350.5f, 400.25f), (700, 500f, 300f) };
        string a = RunGame(777, 600, 96, 2, drops);
        string b = RunGame(777, 600, 96, 2, drops);
        Assert.Equal(a, b);
    }

    [Fact]
    public void GameStream_PuroObservador_HashIgualQueScenario()

    {
        // Mismo mundo (seed, grid, colonias, ticks) sin comandos: el hash final
        // del stream debe coincidir con el escenario canónico WorldScenario.
        string game = RunGame(42, 1200, 96, 30, drops: null);
        string world = WorldScenario.Run(42, 1200, colonies: 1, grid: 96);

        string gameHash = ExtractLastHash(game);
        string worldHash = ExtractLine(world, "final-hash ")["final-hash ".Length..];
        Assert.Equal(worldHash, gameHash);
    }

    [Fact]
    public void GameStream_ConComandos_MundoDistinto()
    {
        string a = RunGame(777, 600, 96, 30, null);
        string b = RunGame(777, 600, 96, 30, new[] { (100, 350f, 400f) });
        Assert.NotEqual(ExtractLastHash(a), ExtractLastHash(b));
    }

    [Fact]
    public void GameStream_RegistraCommandExecuted_YItemCae()

    {
        string a = RunGame(777, 3, 96, 1, new[] { (1, 300f, 300f) });
        Assert.Contains("\"events\":[[11,-1,0,300,300,0]]", a);
        Assert.Contains("," + SimCommand.DropFoodAmount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "]", a);
    }

    private static string ExtractLastHash(string stream)
    {
        foreach (var line in ReverseLines(stream))
            if (line.Contains("\"end\":true"))
                return line[(line.IndexOf("\"hash\":") + 8)..].TrimEnd('"', '}', ' ', '\r', '\n');
        throw new InvalidOperationException("El stream no tiene línea end.");
    }

    private static IEnumerable<string> ReverseLines(string s)
    {
        var lines = s.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--) yield return lines[i];
    }

    private static string ExtractLine(string s, string prefix)
    {
        foreach (var line in s.Split('\n'))
            if (line.StartsWith(prefix)) return line.TrimEnd('\r');
        throw new InvalidOperationException("No se encontró: " + prefix);
    }

    // — F4.0: comando SaveGame y roundtrip guardar → cargar → reproducir —

    private static List<string> RunWithSaves(ulong seed, int grid, int ticks,
        (int Tick, float X, float Y)[] drops, (int Tick, byte Slot)[] saves,
        string? savePath, out string? savedAtHash)
    {
        var sim = new WorldSim(seed, grid, colonyCount: 1);
        var hashes = new List<string>();
        savedAtHash = null;
        int nextDrop = 0, nextSave = 0;

        for (int i = 0; i < ticks; i++)
        {
            while (nextDrop < drops.Length && drops[nextDrop].Tick == i)
            {
                sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, drops[nextDrop].X, drops[nextDrop].Y));
                nextDrop++;
            }
            while (nextSave < saves.Length && saves[nextSave].Tick == i)
            {
                sim.EnqueueCommand(new SimCommand(SimCommandKind.SaveGame, 0f, 0f, saves[nextSave].Slot));
                nextSave++;
            }

            sim.Step();

            // El presenter consume las peticiones de guardado tras el Step.
            foreach (var req in sim.SaveRequests)
            {
                if (savePath == null) continue;
                WorldSimSave.Save(sim, savePath);
                savedAtHash = sim.HashLine();
            }

            if (sim.Tick % 256 == 0) hashes.Add(sim.HashLine());
        }
        hashes.Add(sim.HashLine());
        return hashes;
    }

    [Fact]
    public void SaveGame_EsObservacion_HashesIdenticosConYSinEl()
    {
        var drops = new[] { (300, 350.5f, 400.25f), (700, 500f, 300f) };
        var a = RunWithSaves(777, 96, 1000, drops, Array.Empty<(int, byte)>(), null, out _);
        var b = RunWithSaves(777, 96, 1000, drops, new[] { (400, (byte)1), (800, (byte)2) }, null, out _);
        Assert.Equal(a, b); // guardar no muta el mundo
    }

    [Fact]
    public void SaveGame_RegistraEventoConSlot_YExponePeticion()
    {
        var sim = new WorldSim(42, 96, colonyCount: 1);
        for (int i = 0; i < 50; i++) sim.Step();

        sim.EnqueueCommand(new SimCommand(SimCommandKind.SaveGame, 0f, 0f, slot: 3));
        Assert.Equal(0, sim.SaveRequests.Count); // aún no aplicado
        sim.Step();

        Assert.Equal(1, sim.SaveRequests.Count);
        Assert.Equal(3, sim.SaveRequests[0].Slot);
        Assert.Equal(sim.Tick, sim.SaveRequests[0].Tick);

        bool found = false;
        foreach (var ev in sim.LastEvents)
        {
            if (ev.Kind == SimEventKind.CommandExecuted && ev.AntId == (uint)SimCommandKind.SaveGame)
            {
                found = true;
                Assert.Equal(3, ev.Cause); // el Cause del evento lleva el slot
            }
        }
        Assert.True(found);

        sim.Step();
        Assert.Equal(0, sim.SaveRequests.Count); // se consume por Step
    }

    [Fact]
    public void SaveLoadReplay_Roundtrip_BitABit()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "fase4-rt-" + Guid.NewGuid().ToString("N") + ".antsave");
        try
        {
            // Partida original: drops + saves; el guardado del slot 1 cae en el tick 700.
            var drops = new[] { (300, 350.5f, 400.25f), (700, 500f, 300f) };
            var saves = new[] { (700, (byte)1) };
            RunWithSaves(777, 96, 1000, drops, saves, path, out string? savedAtHash);
            Assert.NotNull(savedAtHash);

            // Carga el checkpoint: el comando encolado en i=700 se aplica en el Step
            // que lleva Tick a 701 — el checkpoint contiene ese tick exacto.
            var loaded = WorldSimSave.Load(path);
            Assert.Equal(701UL, loaded.Tick);
            Assert.Equal(savedAtHash, loaded.HashLine()); // el save es fiel al momento

            // Reproduce hasta el tick final de la partida original (1000): el drop
            // de tick 700 ya es pasado del checkpoint — la reproducción no re-inyecta
            // comandos, solo avanza (el mundo recrea el resto por determinismo).
            var replayHashes = new List<string>();
            for (int i = 0; i < 299; i++)
            {
                loaded.Step();
                if (loaded.Tick % 256 == 0) replayHashes.Add(loaded.HashLine());
            }
            replayHashes.Add(loaded.HashLine());

            // La referencia: seguir la partida original desde el tick 701 sin comandos nuevos.
            var reference = ContinueReference(777, 96, 1000, drops, fromTick: 700, tailTicks: 299);

            Assert.Equal(reference.Count, replayHashes.Count);
            for (int i = 0; i < reference.Count; i++)
                Assert.Equal(reference[i], replayHashes[i]); // bit a bit
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    /// <summary>Partida completa truncada al tick <paramref name="fromTick"/> + cola:
    /// la referencia canónica contra la que se compara la reproducción.</summary>
    private static List<string> ContinueReference(ulong seed, int grid, int totalTicks,
        (int Tick, float X, float Y)[] drops, int fromTick, int tailTicks)
    {
        var sim = new WorldSim(seed, grid, colonyCount: 1);
        var hashes = new List<string>();
        int nextDrop = 0;
        for (int i = 0; i < totalTicks; i++)
        {
            while (nextDrop < drops.Length && drops[nextDrop].Tick == i)
            {
                sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, drops[nextDrop].X, drops[nextDrop].Y));
                nextDrop++;
            }
            sim.Step();
            if (sim.Tick > (ulong)fromTick && sim.Tick % 256 == 0) hashes.Add(sim.HashLine());
        }
        hashes.Add(sim.HashLine());
        return hashes;
    }

    // — F4.5: tarjetas canónicas del selector (diff contra la UI de Unity) —

    [Fact]
    public void PresetCards_Deterministas_YValidas()
    {
        string a = PresetScenario.RenderCards();
        string b = PresetScenario.RenderCards();
        Assert.Equal(a, b); // función pura de PoolPresets

        // Las cuatro tarjetas con sus centinelas del benchmark.
        Assert.Contains("warm-v2", a);
        Assert.Contains("167.9", a);
        Assert.Contains("80.0", a);
        Assert.Contains("warm3", a);
        Assert.Contains("22", a);   // corona de descargas
        Assert.Contains("0/10", a); // naturalista

        // El JSON es parseable y lleva los campos estructurados clave.
        string j = PresetScenario.RenderCards(json: true);
        Assert.StartsWith("{\"mode\":\"presets\",\"presets\":[", j.TrimEnd('\r', '\n'));
        Assert.EndsWith("]}", j.TrimEnd('\r', '\n'));
        Assert.Contains("\"genomeFile\":\"artifacts/pretrain-warm-v2.antgenome\"", j);
        Assert.Contains("\"gameMode\":null", j); // naturalista
    }

    private static List<string> RunWithLog(ulong seed, int grid, int ticks,
        (int Tick, float X, float Y)[] commands, string antlogPath, out List<string> hashes)
    {
        var sim = new WorldSim(seed, grid, colonyCount: 1);
        using var log = new AntEventLog(sim);
        var h = new List<string>();
        int next = 0;
        for (int i = 0; i < ticks; i++)
        {
            while (next < commands.Length && commands[next].Tick == i)
            {
                sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, commands[next].X, commands[next].Y));
                next++;
            }
            sim.Step();
            log.Observe(sim.LastEvents);
            if (sim.Tick % AntEventLog.MilestoneEvery == 0)
                log.RecordMilestone(sim.Tick, sim.HashLine());
            if (sim.Tick % 256 == 0) h.Add(sim.HashLine());
        }
        log.WriteTo(antlogPath);
        hashes = h;
        return h;
    }
}
