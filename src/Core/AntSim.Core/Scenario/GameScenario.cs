using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntSim.Core.Serialization;
using AntSim.Core.Evolution;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Pheromone;
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

    /// <summary>
    /// Una capa pedida del canal E: de qué COLONIA y de qué TIPO. Las capas son
    /// por colonia (una hormiga solo lee las suyas), así que «las feromonas» del
    /// mundo son N capas, no una: con dos colonias compitiendo, enseñar solo la
    /// de la 0 era contar media partida.
    /// </summary>
    public readonly struct PheroRequest
    {
        public readonly int Colony;
        public readonly PheromoneKind Kind;

        public PheroRequest(int colony, PheromoneKind kind)
        {
            Colony = colony;
            Kind = kind;
        }
    }

    public static string Run(ulong seed, int ticks, int colonies = 2, int grid = 256,
        int frameEvery = DefaultFrameEvery, string? seedPoolPath = null,
        IReadOnlyList<(int Tick, float X, float Y)>? drops = null,
        bool cloneFromElite = false, int pheroEvery = 0,
        uint inspectId = 0, int activEvery = 0,
        IReadOnlyList<PheroRequest>? pheroLayers = null)
    {
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (frameEvery < 1) throw new ArgumentOutOfRangeException(nameof(frameEvery));
        if (pheroEvery < 0) throw new ArgumentOutOfRangeException(nameof(pheroEvery));
        if (activEvery < 0) throw new ArgumentOutOfRangeException(nameof(activEvery));

        if (pheroLayers != null)
        {
            // El conjunto de capas es un modo EXPLÍCITO y sustituto del canal E
            // clásico: no se emiten los dos (sería el mismo dato dos veces por
            // tick) y exige `pheroEvery`, porque sin cadencia no hay nada que
            // emitir y el fallo sería silencioso en la UI.
            if (pheroEvery <= 0)
                throw new ArgumentException("pheroLayers requiere pheroEvery > 0.", nameof(pheroLayers));
            if (pheroLayers.Count == 0)
                throw new ArgumentException("pheroLayers vacío: no hay ninguna capa que emitir.", nameof(pheroLayers));
            for (int i = 0; i < pheroLayers.Count; i++)
            {
                var r = pheroLayers[i];
                if (r.Colony < 0 || r.Colony >= colonies)
                    throw new ArgumentOutOfRangeException(nameof(pheroLayers),
                        $"colonia {r.Colony} fuera de rango (0..{colonies - 1}).");
                if (r.Kind == PheromoneKind.Territory)
                    throw new ArgumentException(
                        "Territory todavía no se deposita: la capa estaría siempre vacía.",
                        nameof(pheroLayers));
            }
        }

        var sim = new WorldSim(seed, grid, colonies, cloneFromElite: cloneFromElite);
        var sb = new StringBuilder();
        var metrics = new MetricRecorder();
        var relay = new RelayTracker();
        var alerts = new AlertDeriver();
        var alertsOut = new List<AlertDeriver.Alert>();

        AppendHeader(sb, seed, ticks, colonies, grid, frameEvery, seedPoolPath, cloneFromElite);
        if (pheroEvery > 0 || activEvery > 0)
            sb.Length -= 3; // "}}\r\n" → cierra en el bucle: añadimos canales opt-in

        // Canal E (F4.5): feromonas opt-in. Se declara en la cabecera para que
        // el parser sepa que los ticks pueden traer "phero". La emisión nunca
        // toca el mundo (telemetría pura) y va en ticks múltiplo de pheroEvery.
        if (pheroEvery > 0)
            sb.Append(",\"pheroEvery\":").Append(pheroEvery);

        // Canal E múltiple (F5.1): con `pheroLayers` la cabecera declara que los
        // ticks traen "pheroSet" (array con colonia y tipo por paquete) en vez
        // del "phero" suelto. El canal clásico no cambia: los pines de CI y los
        // fixtures de stream quedan intactos.
        if (pheroLayers != null)
            sb.Append(",\"pheroSet\":true");

        // Canal F (F5.0): activaciones del cerebro de UNA hormiga inspeccionada,
        // opt-in como el E. La hormiga se elige por Id (el mismo que viaja en el
        // canal A); la emisión re-evalúa el cerebro con sensores re-construidos —
        // determinista y sin tocar el mundo.
        if (activEvery > 0)
        {
            if (inspectId == 0)
                throw new ArgumentException("activEvery > 0 requiere un inspectId ≠ 0.");
            sb.Append(",\"activEvery\":").Append(activEvery)
              .Append(",\"inspectId\":").Append(inspectId);
        }

        if (pheroEvery > 0 || activEvery > 0)
            // F5.0 fix: AppendHeader terminó en "}}\r\n"; Length-=3 deja UNA llave
            // (la del objeto exterior). Antes se re-añadían DOS → llave extra (JSON
            // inválido en la cabecera con canales opt-in).
            sb.Append('}').AppendLine();

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

            // Canal D (F4.2): las alertas derivadas del HUD viajan EN el stream —
            // la UI nunca inventa umbrales (regla dura del contrato §6.4) y no
            // necesita referenciar el Core. Consumo puro: no toca el mundo.
            alertsOut.Clear();
            var frame = metrics.TakeFrame(sim.Tick);
            alerts.Observe(sim.LastEvents, frame, relay, sim, alertsOut);

            AppendTick(sb, sim, metrics, relay, alerts, alertsOut, frame, grid, frameEvery);
            // Canales opt-in: se añaden DENTRO del objeto del tick (AppendTick deja
            // la llave abierta); la línea cierra aquí. F5.0 fix: antes el canal E
            // se añadía tras la llave de cierre y el JSONL quedaba pegado.
            if (pheroEvery > 0 && sim.Tick % (ulong)pheroEvery == 0)
            {
                if (pheroLayers == null) AppendPheromones(sb, sim);
                else AppendPheromoneSet(sb, sim, pheroLayers);
            }
            if (activEvery > 0 && sim.Tick % (ulong)activEvery == 0)
                AppendActivations(sb, sim, inspectId);
            sb.Append('}').AppendLine(); // cierra el objeto del tick
        }

        sb.Append("{\"end\":true,\"tick\":").Append(sim.Tick)
          .Append(",\"hash\":\"").Append(sim.HashLine()).Append("\"}").AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Canal E clásico (F4.5): capa Home de la colonia 0 como string base64 dentro
    /// del tick JSON. Se mantiene tal cual — los fixtures de stream y los pines de
    /// CI cuentan con este formato; el modo multi-capa es <see cref="AppendPheromoneSet"/>.
    /// </summary>
    private static void AppendPheromones(StringBuilder sb, WorldSim sim)
    {
        // HomeLayer en vez de FoodLayer: las fundadoras depositan home siempre
        // que caminan (el food trail exige llevar carga), así el canal muestra
        // actividad desde los primeros ticks.
        var layer = sim.Colonies[0].HomeLayer;
        sb.Append(",\"phero\":\"").Append(Convert.ToBase64String(EncodeLayer(layer))).Append('"');
    }

    /// <summary>
    /// Canal E múltiple (F5.1): varias capas —colonia×tipo— en el mismo tick como
    /// array de paquetes con su procedencia. El orden del array es el que pidió el
    /// llamante (determinista), y `k` es el ordinal de <see cref="PheromoneKind"/>
    /// (0 FoodTrail, 1 Home, 2 Alarm): parte del contrato, no un detalle interno.
    /// </summary>
    private static void AppendPheromoneSet(StringBuilder sb, WorldSim sim, IReadOnlyList<PheroRequest> layers)
    {
        sb.Append(",\"pheroSet\":[");
        for (int i = 0; i < layers.Count; i++)
        {
            var req = layers[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"c\":").Append(req.Colony)
              .Append(",\"k\":").Append((int)req.Kind)
              .Append(",\"d\":\"")
              .Append(Convert.ToBase64String(EncodeLayer(LayerOf(sim.Colonies[req.Colony], req.Kind))))
              .Append("\"}");
        }
        sb.Append(']');
    }

    private static PheromoneLayer LayerOf(Colony colony, PheromoneKind kind) => kind switch
    {
        PheromoneKind.FoodTrail => colony.FoodLayer,
        PheromoneKind.Home => colony.HomeLayer,
        PheromoneKind.Alarm => colony.AlarmLayer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "capa sin datos que emitir.")
    };

    /// <summary>
    /// Codificación canónica de una capa: cabecera w,h (u16 LE) + filas no vacías
    /// [y (u16 LE) + pares (valor, run ≤255)], cada fila suma exactamente w celdas.
    /// Cuantiza a byte (el render no distingue más), se salta las filas vacías y no
    /// lee fuera de la capa. Telemetría pura: lee, no escribe.
    /// </summary>
    private static byte[] EncodeLayer(PheromoneLayer layer)
    {
        int w = layer.Width, h = layer.Height;
        var payload = new List<byte>(w * h / 4 + 16);
        // Cabecera: w, h little-endian u16.
        payload.Add((byte)w); payload.Add((byte)(w >> 8));
        payload.Add((byte)h); payload.Add((byte)(h >> 8));

        var quant = new byte[w];
        int y = 0;
        while (y < h)
        {
            bool rowZero = true;
            for (int x = 0; x < w; x++)
            {
                float v = layer[x, y];
                byte q = v <= 0.004f ? (byte)0 : (byte)Math.Clamp((int)MathF.Round(v * 255f), 1, 255);
                quant[x] = q;
                if (q != 0) rowZero = false;
            }
            if (rowZero) { y++; continue; }

            payload.Add((byte)(y & 0xFF));
            payload.Add((byte)((y >> 8) & 0xFF));
            int cx = 0;
            while (cx < w)
            {
                byte v = quant[cx];
                int run = 1;
                while (cx + run < w && run < 255 && quant[cx + run] == v) run++;
                payload.Add(v); payload.Add((byte)run);
                cx += run;
            }
            y++;
        }
        return payload.ToArray();
    }

    /// <summary>
    /// Canal F (F5.0): activaciones del MLP de la hormiga inspeccionada, cuantizadas
    /// a bytes (s8: v·128, saturado) + base64, dentro del tick JSON. Formato:
    /// [total u16 LE][activación s8 × total]. La emisión re-construye los sensores
    /// y re-evalúa el cerebro: determinista y sin tocar el mundo (hash invariante —
    /// verificado en tests). Hormiga muerta/ausente o cerebro no-MLP ⇒ paquete
    /// vacío (la vista lo interpreta con "alive" del canal A).
    /// </summary>
    private static void AppendActivations(StringBuilder sb, WorldSim sim, uint inspectId)
    {
        Ant? target = null;
        for (int c = 0; c < sim.Colonies.Count && target == null; c++)
        {
            var colony = sim.Colonies[c];
            for (int i = 0; i < colony.Adults.Count; i++)
            {
                if (colony.Adults[i].Id == inspectId) { target = colony.Adults[i]; break; }
            }
        }

        sb.Append(",\"activ\":\"");
        if (target is { Alive: true } ant && ant.Brain is MlpBrain mlp)
        {
            var sensors = AntSenses.Build(sim.Colonies[ant.ColonyId], ant, sim.Items, sim.WorldWidth, sim.WorldHeight);
            var decision = AntDecision.Neutral();
            mlp.Evaluate(in sensors, ref decision);

            int total = mlp.ActivationTotal;
            var payload = new byte[2 + total];
            payload[0] = (byte)total;
            payload[1] = (byte)(total >> 8);
            Span<float> act = stackalloc float[total];
            mlp.SnapshotActivations(act);
            for (int i = 0; i < total; i++)
            {
                int q = (int)MathF.Round(act[i] * 128f);
                payload[2 + i] = (byte)Math.Clamp(q, -128, 127);
            }
            sb.Append(Convert.ToBase64String(payload));
        }
        sb.Append('"');
    }

    private static void AppendHeader(StringBuilder sb, ulong seed, int ticks, int colonies,
        int grid, int frameEvery, string? seedPoolPath, bool cloneFromElite)
    {
        sb.Append("{\"header\":{")
          .Append("\"mode\":\"game\",\"seed\":").Append(seed)
          .Append(",\"ticks\":").Append(ticks)
          .Append(",\"colonies\":").Append(colonies)
          .Append(",\"grid\":").Append(grid)
          .Append(",\"frameEvery\":").Append(frameEvery);
        if (seedPoolPath != null)
            sb.Append(",\"seedPool\":\"").Append(Escape(seedPoolPath)).Append('"');
        if (cloneFromElite)
            sb.Append(",\"cloneFromElite\":true");
        sb.Append("}}").AppendLine();
    }

    private static void AppendTick(StringBuilder sb, WorldSim sim, MetricRecorder metrics,
        RelayTracker relay, AlertDeriver alerts, List<AlertDeriver.Alert> alertsOut,
        MetricRecorder.MetricFrame? metricFrame, int grid, int frameEvery)
    {
        bool firstField = true;
        sb.Append('{');

        // — El tick SIEMPRE (descubierto en el smoke warm-v2): los ticks donde el
        // canal A no sale pero sí eventos, sin "tick", serían inatribuibles.
        sb.Append("\"tick\":").Append(sim.Tick);
        firstField = false;

        // — Canal A (snapshot): cada frameEvery ticks —
        if (sim.Tick % (ulong)frameEvery == 0)
        {
            var frame = SimSnapshot.Capture(sim);
            sb.Append(",\"ants\":[");
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
        //    (la ventana la consume UNA vez en el bucle principal para el deriver
        //    y llega aquí ya extraída: TakeFrame es destructivo)
        if (metricFrame is MetricRecorder.MetricFrame f)
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
                      .Append(",\"chainAvg\":").Append(v.ChainMean is double cm2 ? F((float)cm2) : "null")
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

        // — Canal D (F4.2): alertas derivadas de este tick + semáforo por colonia.
        //    El texto es la CARGA del toast; el color lo pone el nivel en la UI.
        if (alertsOut.Count > 0)
        {
            if (!firstField) sb.Append(',');
            sb.Append("\"alerts\":[");
            for (int i = 0; i < alertsOut.Count; i++)
            {
                var a = alertsOut[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"k\":\"").Append(Escape(a.Key))
                  .Append("\",\"lvl\":").Append((byte)a.Lvl)
                  .Append(",\"col\":").Append(a.ColonyId)
                  .Append(",\"x\":").Append(F(a.X)).Append(',').Append("\"y\":").Append(F(a.Y))
                  .Append(",\"t\":").Append(a.Tick)
                  .Append(",\"txt\":\"").Append(Escape(a.Text)).Append("\"}");
            }
            sb.Append(']');
            firstField = false;
        }
        if (sim.Tick % 120 == 0)
        {
            if (!firstField) sb.Append(',');
            sb.Append("\"light\":[");
            for (int c = 0; c < sim.Colonies.Count; c++)
            {
                var col = sim.Colonies[c];
                if (c > 0) sb.Append(',');
                var v = relay.ForColony(col.Id);
                RelayLight light = v is RelayTracker.ColonyView cv
                    ? RelayVerdict.EvaluateNormalized(
                        cv.CarryLegMean is double l ? (float)l : null,
                        cv.ChainMean is double ch ? (float)ch : null,
                        cv.DropMean is double d ? (float)d : null, grid)
                    : RelayLight.Grey;
                sb.Append('[').Append(col.Id).Append(',')
                  .Append((byte)light).Append(']');
            }
            sb.Append(']');
            firstField = false;
        }

        // El objeto del tick lo cierra Run() (línea 113), tras los canales opt-in.
    }

    /// <summary>Float en formato canónico (punto, sin cultura, redondeo corto).</summary>
    private static string F(float v)
        => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
