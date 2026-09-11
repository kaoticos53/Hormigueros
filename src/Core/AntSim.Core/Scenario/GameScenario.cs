using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntSim.Core.Serialization;
using AntSim.Core.Evolution;
using AntSim.Core.Telemetry;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// Escenario de juego headless (Fase 4, F4.1): ejecuta el <see cref="WorldSim"/>
/// y emite un stream JSONL — un objeto por tick con el canal A (snapshot: poses
/// e items), el canal B (eventos discretos del tick) y el canal C (MetricFrame
/// cuando la ventana de 1 s cierra). El presenter (Unity u otro) consume este
/// stream sin conocer el Core: mismo contrato que tendrá por referencias directas.
///
/// Determinismo: el stream es función pura de (seed, ticks, comandos, semilla de
/// pool) — misma entrada ⇒ salida byte a byte idéntica. La captura no toca el
/// mundo: el hash final coincide con <see cref="WorldScenario.Run"/> a igualdad
/// de semilla y ticks.
/// </summary>
public static class GameScenario
{
    /// <summary>Cada cuántos ticks se emite el canal A (poses). Los eventos del
    /// canal B se emiten SIEMPRE (son discretos, no interpolables). 1 = cada tick.</summary>
    public const int DefaultFrameEvery = 1;

    public static string Run(ulong seed, int ticks, int colonies = 2, int grid = 256,
        int frameEvery = DefaultFrameEvery, string? seedPoolPath = null,
        IReadOnlyList<(int Tick, float X, float Y)>? drops = null)
    {
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (frameEvery < 1) throw new ArgumentOutOfRangeException(nameof(frameEvery));

        var sim = new WorldSim(seed, grid, colonies);
        var sb = new StringBuilder();
        var metrics = new MetricRecorder();
        var relay = new RelayTracker();

        AppendHeader(sb, seed, ticks, colonies, grid, frameEvery, seedPoolPath);

        if (seedPoolPath != null)
        {
            var (_, seeded) = AntGenomeFile.ReadFile(seedPoolPath, Brain.BrainContract.CurrentVersion);
            sim.SeedPoolFromGenomes(0, seeded);
        }

        int nextDrop = 0;
        for (int i = 0; i < ticks; i++)
        {
            if (drops != null)
            {
                while (nextDrop < drops.Count && drops[nextDrop].Tick == i)
                {
                    var d = drops[nextDrop];
                    sim.EnqueueCommand(new SimCommand(SimCommandKind.DropFood, d.X, d.Y));
                    nextDrop++;
                }
            }

            sim.Step();
            relay.Observe(sim.LastEvents, sim);
            metrics.Observe(sim.LastEvents);

            AppendTick(sb, sim, metrics, relay, frameEvery);
        }

        sb.Append("{\"end\":true,\"tick\":").Append(sim.Tick)
          .Append(",\"hash\":\"").Append(sim.HashLine()).Append("\"}").AppendLine();
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, ulong seed, int ticks, int colonies,
        int grid, int frameEvery, string? seedPoolPath)
    {
        sb.Append("{\"header\":{")
          .Append("\"mode\":\"game\",\"seed\":").Append(seed)
          .Append(",\"ticks\":").Append(ticks)
          .Append(",\"colonies\":").Append(colonies)
          .Append(",\"grid\":").Append(grid)
          .Append(",\"frameEvery\":").Append(frameEvery);
        if (seedPoolPath != null)
            sb.Append(",\"seedPool\":\"").Append(Escape(seedPoolPath)).Append('"');
        sb.Append("}}").AppendLine();
    }

    private static void AppendTick(StringBuilder sb, WorldSim sim, MetricRecorder metrics,
        RelayTracker relay, int frameEvery)
    {
        bool firstField = true;
        sb.Append('{');

        // — Canal A (snapshot): cada frameEvery ticks —
        if (sim.Tick % (ulong)frameEvery == 0)
        {
            var frame = SimSnapshot.Capture(sim);
            sb.Append("\"tick\":").Append(sim.Tick)
              .Append(",\"ants\":[");
            for (int i = 0; i < frame.Ants.Count; i++)
            {
                var a = frame.Ants[i];
                if (i > 0) sb.Append(',');
                // [id, colony, x, y, heading, load, alive, vigor, energy, age,
                //  immigrant, genomeFingerprint] — F4.2: +5 campos de inspección
                sb.Append('[').Append(a.Id).Append(',').Append(a.ColonyId)
                  .Append(',').Append(F(a.X)).Append(',').Append(F(a.Y))
                  .Append(',').Append(F(a.Heading)).Append(',')
                  .Append(a.HasLoad ? "1" : "0").Append(a.Alive ? ",1," : ",0,")
                  .Append(F(a.Vigor)).Append(',').Append(F(a.Energy)).Append(',')
                  .Append(F(a.Age)).Append(',')
                  .Append(a.IsImmigrant ? "1," : "0,")
                  .Append(a.GenomeFingerprint).Append(']');
            }
            sb.Append("],\"items\":[");
            for (int i = 0; i < frame.Items.Count; i++)
            {
                var it = frame.Items[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(it.Id).Append(',').Append(F(it.X))
                  .Append(',').Append(F(it.Y)).Append(',').Append(F(it.Amount)).Append(']');
            }
            sb.Append("],\"colonies\":[");
            for (int i = 0; i < frame.Colonies.Count; i++)
            {
                var c = frame.Colonies[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(c.Id)
                  .Append(",\"nest\":[").Append(F(c.NestX)).Append(',').Append(F(c.NestY)).Append(']')
                  .Append(",\"adults\":").Append(c.AdultsAlive)
                  .Append(",\"eggs\":").Append(c.Eggs)
                  .Append(",\"larvae\":").Append(c.Larvae)
                  .Append(",\"pupae\":").Append(c.Pupae)
                  .Append(",\"stock\":").Append(F(c.Stock))
                  .Append(",\"stockMax\":").Append(F(c.StockMax))
                  .Append(",\"elite\":").Append(c.EliteCount)
                  .Append('}');
            }
            sb.Append(']');
            firstField = false;
        }

        // — Canal B (eventos): SIEMPRE —
        if (sim.LastEvents.Count > 0)
        {
            if (!firstField) sb.Append(',');
            sb.Append("\"events\":[");
            for (int i = 0; i < sim.LastEvents.Count; i++)
            {
                var ev = sim.LastEvents[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append((byte)ev.Kind).Append(',').Append(ev.ColonyId)
                  .Append(',').Append(ev.AntId).Append(',')
                  .Append(F(ev.X)).Append(',').Append(F(ev.Y)).Append(',')
                  .Append(ev.Cause).Append(']');
            }
            sb.Append(']');
            firstField = false;
        }

        // — Canal C (métricas): cuando la ventana de 1 s cierra —
        if (metrics.TakeFrame(sim.Tick) is MetricRecorder.MetricFrame f)
        {
            if (!firstField) sb.Append(',');
            sb.Append("\"metrics\":{")
              .Append("\"t0\":").Append(f.TickStart).Append(",\"t1\":").Append(f.TickEnd)
              .Append(",\"pickups\":").Append(f.Pickups)
              .Append(",\"unloads\":").Append(f.Unloads)
              .Append(",\"births\":").Append(f.Births)
              .Append(",\"deaths\":").Append(f.Deaths)
              .Append(",\"eggs\":").Append(f.EggsLaid)
              .Append(",\"eclosed\":").Append(f.Eclosed)
              .Append(",\"consumed\":").Append(f.ItemsConsumed)
              .Append(",\"commands\":").Append(f.Commands)
              .Append('}');
            firstField = false;
        }

        // — Telemetría de relevo (misma que CLI/scenario, para el HUD) —
        if (sim.Tick % 120 == 0)
        {
            if (!firstField) sb.Append(',');
            sb.Append("\"relay\":{")
              .Append("\"firstUnload\":").Append(relay.HasUnload ? relay.FirstUnloadTick.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(",\"dropAvg\":").Append(relay.DropDistanceMean is double dm ? F((float)dm) : "null")
              .Append(",\"unloadAvg\":").Append(relay.UnloadDistanceMean is double um ? F((float)um) : "null")
              .Append(",\"carryLeg\":").Append(relay.CarryLegMean is double cl ? F((float)cl) : "null")
              .Append('}');

            // F4.2: desglose por colonia — el semáforo de cada tarjeta.
            sb.Append(",\"relays\":[");
            bool firstCol = true;
            for (int c = 0; c < sim.Colonies.Count; c++)
            {
                int cid = sim.Colonies[c].Id;
                var cv = relay.ForColony(cid);
                if (!firstCol) sb.Append(',');
                firstCol = false;
                if (cv is RelayTracker.ColonyView v)
                {
                    sb.Append('{').Append("\"col\":").Append(cid)
                      .Append(",\"firstUnload\":").Append(v.FirstUnloadTick > 0
                          ? v.FirstUnloadTick.ToString(CultureInfo.InvariantCulture) : "null")
                      .Append(",\"unloadAvg\":").Append(v.UnloadMean is double um2 ? F((float)um2) : "null")
                      .Append(",\"carryLeg\":").Append(v.CarryLegMean is double cl2 ? F((float)cl2) : "null")
                      .Append(",\"dropAvg\":").Append(v.DropMean is double dm2 ? F((float)dm2) : "null")
                      .Append(",\"unloads\":").Append(v.UnloadCount)
                      .Append('}');
                }
                else
                {
                    sb.Append('{').Append("\"col\":").Append(cid).Append(",\"empty\":true}");
                }
            }
            sb.Append(']');

            // F4.2: métricas de la ventana abierta por colonia (parciales).
            sb.Append(",\"colmetrics\":[");
            var cols = metrics.ColonyWindows();
            for (int i = 0; i < cols.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var (cid, pk, un, bi, de, eg, ec) = cols[i];
                sb.Append("[").Append(cid).Append(',').Append(pk).Append(',').Append(un)
                  .Append(',').Append(bi).Append(',').Append(de).Append(',')
                  .Append(eg).Append(',').Append(ec).Append(']');
            }
            sb.Append(']');
            firstField = false;
        }

        sb.Append('}').AppendLine();
    }

    /// <summary>Float en formato canónico (punto, sin cultura, redondeo corto).</summary>
    private static string F(float v)
        => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
