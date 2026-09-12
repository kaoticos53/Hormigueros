using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Parser del stream JSONL de <c>GameScenario</c> (Fase 4, F4.1). C# puro —
    /// SIN UnityEngine — para poder verificarlo contra la salida real del Core
    /// en la suite de tests headless antes de que Unity lo consuma. Reconstruye
    /// el estado visible (poses, items, colonias, eventos, métricas, relevo) de
    /// cada tick; el presenter no consume el Core: consume esto.
    ///
    /// El contrato del stream es fijo (GameScenario emite campos siempre en el
    /// mismo orden), así que un lector mínimos sobre ese layout es correcto y
    /// barato — sin dependencias de JSON externas (Unity-friendly).
    /// </summary>
    public sealed class GameStreamParser
    {
        public readonly struct AntPose
        {
            public readonly uint Id;
            public readonly int ColonyId;
            public readonly float X, Y, Heading;
            public readonly bool HasLoad, Alive;
            // — Inspección (F4.2) —
            public readonly float Vigor, Energy, Age;
            public readonly bool IsImmigrant;
            public readonly uint GenomeFingerprint;

            public AntPose(uint id, int colonyId, float x, float y, float heading, bool hasLoad, bool alive,
                float vigor = 0f, float energy = 0f, float age = 0f, bool isImmigrant = false, uint genomeFingerprint = 0)
            { Id = id; ColonyId = colonyId; X = x; Y = y; Heading = heading; HasLoad = hasLoad; Alive = alive;
              Vigor = vigor; Energy = energy; Age = age; IsImmigrant = isImmigrant;
              GenomeFingerprint = genomeFingerprint; }
        }

        public readonly struct ItemView
        {
            public readonly uint Id;
            public readonly float X, Y, Amount;

            public ItemView(uint id, float x, float y, float amount)
            { Id = id; X = x; Y = y; Amount = amount; }
        }

        public readonly struct ColonyView
        {
            public readonly int Id;
            public readonly float NestX, NestY, Stock, StockMax;
            public readonly int Adults, Eggs, Larvae, Pupae, Elite;

            public ColonyView(int id, float nestX, float nestY, float stock, float stockMax,
                int adults, int eggs, int larvae, int pupae, int elite)
            { Id = id; NestX = nestX; NestY = nestY; Stock = stock; StockMax = stockMax;
              Adults = adults; Eggs = eggs; Larvae = larvae; Pupae = pupae; Elite = elite; }
        }

        public readonly struct EventView
        {
            public readonly int Kind;
            public readonly int ColonyId;
            public readonly uint AntId;
            public readonly float X, Y;
            public readonly byte Cause;

            public EventView(int kind, int colonyId, uint antId, float x, float y, byte cause)
            { Kind = kind; ColonyId = colonyId; AntId = antId; X = x; Y = y; Cause = cause; }
        }

        public readonly struct MetricsView
        {
            public readonly ulong T0, T1;
            public readonly long Pickups, Unloads, Births, Deaths, Eggs, Eclosed, Consumed, Commands;

            public MetricsView(ulong t0, ulong t1, long pickups, long unloads, long births,
                long deaths, long eggs, long eclosed, long consumed, long commands)
            { T0 = t0; T1 = t1; Pickups = pickups; Unloads = unloads; Births = births;
              Deaths = deaths; Eggs = eggs; Eclosed = eclosed; Consumed = consumed; Commands = commands; }
        }

        public readonly struct RelayView
        {
            public readonly ulong? FirstUnload;
            public readonly float? DropAvg, UnloadAvg, CarryLeg;

            public RelayView(ulong? firstUnload, float? dropAvg, float? unloadAvg, float? carryLeg)
            { FirstUnload = firstUnload; DropAvg = dropAvg; UnloadAvg = unloadAvg; CarryLeg = carryLeg; }
        }

        /// <summary>Relevo de UNA colonia (F4.2): el semáforo de su tarjeta.</summary>
        public readonly struct ColonyRelayView
        {
            public readonly int ColonyId;
            public readonly ulong? FirstUnload;
            public readonly float? UnloadAvg, CarryLeg, DropAvg, ChainAvg;
            public readonly int Unloads;

            public ColonyRelayView(int colonyId, ulong? firstUnload, float? unloadAvg,
                float? carryLeg, float? dropAvg, int unloads, float? chainAvg = null)
            { ColonyId = colonyId; FirstUnload = firstUnload; UnloadAvg = unloadAvg;
              CarryLeg = carryLeg; DropAvg = dropAvg; Unloads = unloads; ChainAvg = chainAvg; }
        }

        /// <summary>Canal D (F4.2): alerta derivada POR EL CORE (AlertDeriver) y
        /// emitida en el stream — la UI nunca inventa umbrales (contrato §6.4).
        /// Key es estable para dedupe; X/Y anclan el salto de cámara (-1 = no aplica).</summary>
        public readonly struct AlertView
        {
            public readonly string Key;
            public readonly byte Level;    // 0 info · 1 ámbar · 2 verde · 3 rojo
            public readonly int ColonyId;  // -1 = global
            public readonly float X, Y;
            public readonly ulong Tick;
            public readonly string Text;

            public AlertView(string key, byte level, int colonyId, float x, float y, ulong tick, string text)
            { Key = key; Level = level; ColonyId = colonyId; X = x; Y = y; Tick = tick; Text = text; }
        }

        /// <summary>Semáforo de relevo de UNA colonia, calculado por el Core
        /// (RelayVerdict.EvaluateNormalized): 0 gris · 1 ámbar · 2 verde.</summary>
        public readonly struct ColonyLightView
        {
            public readonly int ColonyId;
            public readonly byte Light;

            public ColonyLightView(int colonyId, byte light)
            { ColonyId = colonyId; Light = light; }
        }

        /// <summary>Ventana de métricas de UNA colonia (F4.2).</summary>
        public readonly struct ColonyMetricsView
        {
            public readonly int ColonyId;
            public readonly long Pickups, Unloads, Births, Deaths, Eggs, Eclosed;

            public ColonyMetricsView(int colonyId, long pickups, long unloads,
                long births, long deaths, long eggs, long eclosed)
            { ColonyId = colonyId; Pickups = pickups; Unloads = unloads;
              Births = births; Deaths = deaths; Eggs = eggs; Eclosed = eclosed; }
        }

        /// <summary>Estado visible de un tick: lo que el presenter renderiza.</summary>
        public sealed class TickView
        {
            public ulong Tick;
            public readonly List<AntPose> Ants = new();
            public readonly List<ItemView> Items = new();
            public readonly List<ColonyView> Colonies = new();
            public readonly List<EventView> Events = new();
            public MetricsView? Metrics;
            public RelayView? Relay;
            public readonly List<ColonyRelayView> ColonyRelays = new();
            public readonly List<ColonyMetricsView> ColonyMetrics = new();
            public readonly List<AlertView> Alerts = new();      // canal D (F4.2)
            public readonly List<ColonyLightView> Lights = new(); // semáforo por colonia
            public string? Phero;                                 // canal E (F4.5), base64 RLE
        }

        /// <summary>Cabecera del stream (parámetros de la partida).</summary>
        public sealed class HeaderView
        {
            public ulong Seed;
            public int Ticks, Colonies, Grid, FrameEvery;
        }

        public HeaderView? Header { get; private set; }
        public string? FinalHash { get; private set; }
        public ulong FinalTick { get; private set; }

        /// <summary>
        /// Parsea una línea del stream. Devuelve el TickView de los ticks completos;
        /// la cabecera y la línea end actualizan estado interno y devuelven null.
        /// </summary>
        public TickView? ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            string raw = line.TrimEnd('\r');

            if (raw.StartsWith("{\"header\"", StringComparison.Ordinal))
            {
                var p = new Reader(raw);
                Header = new HeaderView
                {
                    Seed = (ulong)p.Num("seed"),
                    Ticks = (int)p.Num("ticks"),
                    Colonies = (int)p.Num("colonies"),
                    Grid = (int)p.Num("grid"),
                    FrameEvery = (int)p.Num("frameEvery"),
                };
                return null;
            }

            if (raw.StartsWith("{\"end\"", StringComparison.Ordinal))
            {
                var p = new Reader(raw);
                FinalTick = (ulong)p.Num("tick");
                FinalHash = p.Str("hash");
                return null;
            }

            // — Tick completo: "tick" siempre presente; el resto según el tick —
            var v = new TickView { Tick = (ulong)Reader.NumOf(ExtractValue(raw, "tick")) };

            int ai = raw.IndexOf("\"ants\":[", StringComparison.Ordinal);
            if (ai >= 0)
            {
                string antsBody = ArrayBody(raw, ai + "\"ants\":[".Length - 1);
                foreach (string row in SplitTop(antsBody))
                {
                    // [id, colony, x, y, heading, load, alive, vigor, energy, age,
                    //  immigrant, genomeFingerprint] — F4.2 (12 campos)
                    string[] f = RowFields(row);
                    if (f.Length < 7) continue;
                    v.Ants.Add(new AntPose(
                        (uint)Reader.NumOf(f[0]), (int)Reader.NumOf(f[1]),
                        (float)Reader.NumOf(f[2]), (float)Reader.NumOf(f[3]), (float)Reader.NumOf(f[4]),
                        f[5] == "1", f[6] == "1",
                        f.Length > 9 ? (float)Reader.NumOf(f[7]) : 0f,
                        f.Length > 9 ? (float)Reader.NumOf(f[8]) : 0f,
                        f.Length > 9 ? (float)Reader.NumOf(f[9]) : 0f,
                        f.Length > 10 && f[10] == "1",
                        f.Length > 11 ? (uint)Reader.NumOf(f[11]) : 0u));
                }
            }

            int ii = raw.IndexOf("\"items\":[", StringComparison.Ordinal);
            if (ii >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, ii + "\"items\":[".Length - 1)))
                {
                    string[] f = RowFields(row); // [id, x, y, amount]
                    if (f.Length < 4) continue;
                    v.Items.Add(new ItemView((uint)Reader.NumOf(f[0]),
                        (float)Reader.NumOf(f[1]), (float)Reader.NumOf(f[2]), (float)Reader.NumOf(f[3])));
                }
            }

            int ci = raw.IndexOf("\"colonies\":[", StringComparison.Ordinal);
            if (ci >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, ci + "\"colonies\":[".Length - 1)))
                {
                    var p = new Reader(row); // objeto {id, nest:[x,y], adults, ...}
                    v.Colonies.Add(new ColonyView(
                        (int)p.Num("id"),
                        (float)p.Num("nestX"), (float)p.Num("nestY"),
                        (float)p.Num("stock"), (float)p.Num("stockMax"),
                        (int)p.Num("adults"), (int)p.Num("eggs"),
                        (int)p.Num("larvae"), (int)p.Num("pupae"), (int)p.Num("elite")));
                }
            }

            int ei = raw.IndexOf("\"events\":[", StringComparison.Ordinal);
            if (ei >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, ei + "\"events\":[".Length - 1)))
                {
                    string[] f = RowFields(row); // [kind, colony, ant, x, y, cause]
                    if (f.Length < 6) continue;
                    v.Events.Add(new EventView((int)Reader.NumOf(f[0]), (int)Reader.NumOf(f[1]),
                        (uint)Reader.NumOf(f[2]), (float)Reader.NumOf(f[3]), (float)Reader.NumOf(f[4]),
                        (byte)Reader.NumOf(f[5])));
                }
            }

            int pi = raw.IndexOf("\"phero\":\"", StringComparison.Ordinal);
            if (pi >= 0)
            {
                int start = pi + 9;
                int end = raw.IndexOf('"', start);
                if (end > start) v.Phero = raw.Substring(start, end - start);
            }

            int mi = raw.IndexOf("\"metrics\":{", StringComparison.Ordinal);
            if (mi >= 0)
            {
                var p = new Reader(ObjectBody(raw, mi + "\"metrics\":".Length));
                v.Metrics = new MetricsView(
                    (ulong)p.Num("t0"), (ulong)p.Num("t1"),
                    (long)p.Num("pickups"), (long)p.Num("unloads"), (long)p.Num("births"),
                    (long)p.Num("deaths"), (long)p.Num("eggs"), (long)p.Num("eclosed"),
                    (long)p.Num("consumed"), (long)p.Num("commands"));
            }

            int ri = raw.IndexOf("\"relay\":{", StringComparison.Ordinal);
            if (ri >= 0)
            {
                var p = new Reader(ObjectBody(raw, ri + "\"relay\":".Length));
                v.Relay = new RelayView(
                    p.NullNum("firstUnload") is double fu ? (ulong)fu : null,
                    p.NullNum("dropAvg") is double da ? (float)da : null,
                    p.NullNum("unloadAvg") is double ua ? (float)ua : null,
                    p.NullNum("carryLeg") is double cl ? (float)cl : null);
            }

            // — F4.2: relays por colonia y métricas de ventana por colonia —
            int ri2 = raw.IndexOf("\"relays\":[", StringComparison.Ordinal);
            if (ri2 >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, ri2 + "\"relays\":[".Length - 1)))
                {
                    var p = new Reader(row);
                    int col = (int)p.Num("col");
                    if (row.Contains("\"empty\":true")) continue;
                    v.ColonyRelays.Add(new ColonyRelayView(
                        col,
                        p.NullNum("firstUnload") is double fu2 ? (ulong)fu2 : null,
                        p.NullNum("unloadAvg") is double ua2 ? (float)ua2 : null,
                        p.NullNum("carryLeg") is double cl2 ? (float)cl2 : null,
                        p.NullNum("dropAvg") is double da2 ? (float)da2 : null,
                        (int)p.Num("unloads"),
                        p.NullNum("chainAvg") is double ca2 ? (float)ca2 : null));
                }
            }

            int ci2 = raw.IndexOf("\"colmetrics\":[", StringComparison.Ordinal);
            if (ci2 >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, ci2 + "\"colmetrics\":[".Length - 1)))
                {
                    string[] f = RowFields(row); // [col, pickups, unloads, births, deaths, eggs, eclosed]
                    if (f.Length < 7) continue;
                    v.ColonyMetrics.Add(new ColonyMetricsView(
                        (int)Reader.NumOf(f[0]), (long)Reader.NumOf(f[1]), (long)Reader.NumOf(f[2]),
                        (long)Reader.NumOf(f[3]), (long)Reader.NumOf(f[4]),
                        (long)Reader.NumOf(f[5]), (long)Reader.NumOf(f[6])));
                }
            }

            // — Canal D (F4.2): alertas del Core y semáforo por colonia —
            int di = raw.IndexOf("\"alerts\":[", StringComparison.Ordinal);
            if (di >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, di + "\"alerts\":[".Length - 1)))
                {
                    var p = new Reader(row); // {k, lvl, col, x, y, t, txt}
                    v.Alerts.Add(new AlertView(
                        p.Str("k"), (byte)p.Num("lvl"), (int)p.Num("col"),
                        (float)p.Num("x"), (float)p.Num("y"), (ulong)p.Num("t"),
                        p.Str("txt")));
                }
            }

            int li = raw.IndexOf("\"light\":[", StringComparison.Ordinal);
            if (li >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, li + "\"light\":[".Length - 1)))
                {
                    string[] f = RowFields(row); // [col, light]
                    if (f.Length < 2) continue;
                    v.Lights.Add(new ColonyLightView(
                        (int)Reader.NumOf(f[0]), (byte)Reader.NumOf(f[1])));
                }
            }

            return v;
        }

        // ————— extracción mínima sobre el layout fijo del stream —————

        /// <summary>Cuerpo de un array JSON que empieza en <paramref name="openBracket"/>.</summary>
        private static string ArrayBody(string s, int openBracket)
        {
            int depth = 0, i = openBracket, start = openBracket + 1;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '[') depth++;
                else if (c == ']') { depth--; if (depth == 0) return s.Substring(start, i - start); }
            }
            return "";
        }

        /// <summary>Cuerpo de un objeto JSON que empieza en <paramref name="openBrace"/>.</summary>
        private static string ObjectBody(string s, int openBrace)
        {
            int depth = 0, i = openBrace, start = openBrace + 1;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return s.Substring(start, i - start); }
            }
            return "";
        }

        /// <summary>Campos de una fila "[a,b,c]": quita los corchetes externos y divide.</summary>
        private static string[] RowFields(string row)
        {
            string inner = row.Trim();
            if (inner.Length >= 2 && inner[0] == '[') inner = inner.Substring(1);
            if (inner.Length >= 1 && inner.EndsWith("]", StringComparison.Ordinal))
                inner = inner.Substring(0, inner.Length - 1);
            return SplitTop(inner).ToArray();
        }

        /// <summary>Divide un cuerpo de array en filas de nivel superior (por comas externas).</summary>
        private static IEnumerable<string> SplitTop(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) yield break;
            int depth = 0, start = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return body.Substring(start, i - start);
                    start = i + 1;
                }
            }
            if (start < body.Length) yield return body.Substring(start);
        }

        private static string ExtractValue(string s, string key)
        {
            int i = s.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (i < 0) return "0";
            i += key.Length + 3;
            int j = i;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '-' || s[j] == '.')) j++;
            return s.Substring(i, j - i);
        }

        /// <summary>Lector de claves sobre un objeto JSON plano (números y strings cortos).</summary>
        private sealed class Reader
        {
            private readonly string _s;
            public Reader(string s) { _s = s; }

            public static double NumOf(string raw) => double.Parse(raw.Trim(), CultureInfo.InvariantCulture);

            /// <summary>Número de una clave; "nestX"/"nestY" leen dentro de "nest":[x,y].</summary>
            public double Num(string key)
            {
                if (key == "nestX" || key == "nestY")
                {
                    int i = _s.IndexOf("\"nest\":[", StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        i += 7;
                        int comma = _s.IndexOf(',', i);
                        int close = _s.IndexOf(']', i);
                        return NumOf(key == "nestX"
                            ? _s.Substring(i + 1, comma - i - 1)
                            : _s.Substring(comma + 1, close - comma - 1));
                    }
                    return 0;
                }

                string v = ExtractValue(_s, key);
                return v.Length > 0 ? NumOf(v) : 0;
            }

            /// <summary>Número o null si la clave vale null.</summary>
            public double? NullNum(string key)
            {
                int i = _s.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
                if (i < 0) return null;
                i += key.Length + 3;
                if (i + 4 <= _s.Length && _s.Substring(i, 4) == "null") return null;
                int j = i;
                while (j < _s.Length && (char.IsDigit(_s[j]) || _s[j] == '-' || _s[j] == '.')) j++;
                return NumOf(_s.Substring(i, j - i));
            }

            public string Str(string key)
            {
                int i = _s.IndexOf("\"" + key + "\":\"", StringComparison.Ordinal);
                if (i < 0) return "";
                i += key.Length + 4;
                int j = _s.IndexOf('"', i);
                return j > i ? _s.Substring(i, j - i) : "";
            }
        }
    }
}
