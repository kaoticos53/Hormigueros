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

            public AntPose(uint id, int colonyId, float x, float y, float heading, bool hasLoad, bool alive)
            { Id = id; ColonyId = colonyId; X = x; Y = y; Heading = heading; HasLoad = hasLoad; Alive = alive; }
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
                    // [id, colony, x, y, heading, load, alive]
                    string[] f = RowFields(row);
                    if (f.Length < 7) continue;
                    v.Ants.Add(new AntPose(
                        (uint)Reader.NumOf(f[0]), (int)Reader.NumOf(f[1]),
                        (float)Reader.NumOf(f[2]), (float)Reader.NumOf(f[3]), (float)Reader.NumOf(f[4]),
                        f[5] == "1", f[6] == "1"));
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
