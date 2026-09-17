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
            public readonly uint GenomeFingerprint;        /// <summary>F5.2c rodaja 6 — forma del cerebro NEAT ("h/c") o cadena
        /// vacía (MLP clásico). Campo 13 del canal A, opcional.</summary>
        public readonly string BrainShape;

        public AntPose(uint id, int colonyId, float x, float y, float heading, bool hasLoad, bool alive,
            float vigor = 0f, float energy = 0f, float age = 0f, bool isImmigrant = false,
            uint genomeFingerprint = 0, string brainShape = "")
        {
            Id = id; ColonyId = colonyId; X = x; Y = y; Heading = heading; HasLoad = hasLoad; Alive = alive;
            Vigor = vigor; Energy = energy; Age = age; IsImmigrant = isImmigrant;
            GenomeFingerprint = genomeFingerprint;
            BrainShape = brainShape;
        }
        }

        public readonly struct ItemView
        {
            public readonly uint Id;
            public readonly float X, Y, Amount;
            // F5.2a.3: hojas (ausencia/0 = ítem simple — default tolerante).
            public readonly int CutsLeft, CutsInitial;
            public bool IsLeaf => CutsLeft > 0;

            public ItemView(uint id, float x, float y, float amount,
                int cutsLeft = 0, int cutsInitial = 0)
            { Id = id; X = x; Y = y; Amount = amount;
              CutsLeft = cutsLeft; CutsInitial = cutsInitial; }
        }

        public readonly struct ColonyView
        {
            public readonly int Id;
            public readonly float NestX, NestY, Stock, StockMax;
            public readonly int Adults, Eggs, Larvae, Pupae, Elite;
            // F5.2a.3: hongo (0/0 = especie sin hongo — default tolerante).
            public readonly float Fungus, FungusMax;

            public ColonyView(int id, float nestX, float nestY, float stock, float stockMax,
                int adults, int eggs, int larvae, int pupae, int elite,
                float fungus = 0f, float fungusMax = 0f)
            { Id = id; NestX = nestX; NestY = nestY; Stock = stock; StockMax = stockMax;
              Adults = adults; Eggs = eggs; Larvae = larvae; Pupae = pupae; Elite = elite;
              Fungus = fungus; FungusMax = fungusMax; }
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
            // F5.2a.3: cadena de la cortadora (ausencia = 0 — default tolerante).
            public readonly long LeafCuts, FungusFed;

            public MetricsView(ulong t0, ulong t1, long pickups, long unloads, long births,
                long deaths, long eggs, long eclosed, long consumed, long commands,
                long leafCuts = 0, long fungusFed = 0)
            { T0 = t0; T1 = t1; Pickups = pickups; Unloads = unloads; Births = births;
              Deaths = deaths; Eggs = eggs; Eclosed = eclosed; Consumed = consumed; Commands = commands;
              LeafCuts = leafCuts; FungusFed = fungusFed; }
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
        /// <summary>F5.2a.3: cadena de la cortadora de UNA colonia en la ventana
        /// (cuts/fungusFed). Solo aparece si hubo actividad este tick.</summary>
        /// <summary>F5.2c rodaja 6 — topología del grafo NEAT inspeccionado
        /// (canal F): nº de nodos, profundidad topológica de cada oculto (el
        /// orden es el del canal F: ocultos por id ascendente) y nº de
        /// conexiones activas. Es TODO lo que la vista necesita para dibujar
        /// el grafo arbitrario — los pesos no viajan.</summary>
        public sealed class GraphView
        {
            public readonly int N;
            public readonly int[] H;
            public readonly int C;

            public GraphView(int n, int[] h, int c)
            {
                N = n; H = h; C = c;
            }
        }

        public readonly struct CutterView
        {
            public readonly int ColonyId;
            public readonly long LeafCuts, FungusFed;

            public CutterView(int colonyId, long leafCuts, long fungusFed)
            { ColonyId = colonyId; LeafCuts = leafCuts; FungusFed = fungusFed; }
        }

        /// <summary>F5.2b.3: cadena del saqueo de UNA colonia en la ventana
        /// (strikes/raidInflows). Solo aparece si hubo incursiones este tick.</summary>
        public readonly struct RaidView
        {
            public readonly int ColonyId;
            public readonly long Strikes, RaidInflows;

            public RaidView(int colonyId, long strikes, long raidInflows)
            { ColonyId = colonyId; Strikes = strikes; RaidInflows = raidInflows; }
        }

        public readonly struct ColonyMetricsView
        {
            public readonly int ColonyId;
            public readonly long Pickups, Unloads, Births, Deaths, Eggs, Eclosed;

            public ColonyMetricsView(int colonyId, long pickups, long unloads,
                long births, long deaths, long eggs, long eclosed)
            { ColonyId = colonyId; Pickups = pickups; Unloads = unloads;
              Births = births; Deaths = deaths; Eggs = eggs; Eclosed = eclosed; }
        }

        /// <summary>
        /// F5.3ter — aprendizaje de UNA colonia (canal C): la curva de fitness por
        /// generación se resume aquí (generación en curso, media y mejor de la
        /// cohorte, y el aprendizaje acumulado del pool: élite) más el mundo que
        /// esa colonia conoce (celdas pisadas, cobertura y radio máximo).
        /// </summary>
        public readonly struct LearningView
        {
            public readonly int ColonyId;
            public readonly int Generation;
            public readonly int Births;
            public readonly int Ants;        // hormigas nacidas en la generación en curso
            public readonly int Dead;        // de esas, cuántas cayeron
            public readonly float MeanFitness;   // media de la generación (vivas + caídas)
            public readonly float BestFitness;
            public readonly int VisitedCells;
            public readonly int TotalCells;
            public readonly int MaxDistance;     // u, redondeado
            public readonly float EliteBest;
            public readonly float EliteAverage;

            public LearningView(int colonyId, int generation, int births, int ants, int dead,
                float meanFitness, float bestFitness, int visitedCells, int totalCells,
                int maxDistance, float eliteBest, float eliteAverage)
            {
                ColonyId = colonyId; Generation = generation; Births = births;
                Ants = ants; Dead = dead; MeanFitness = meanFitness; BestFitness = bestFitness;
                VisitedCells = visitedCells; TotalCells = totalCells; MaxDistance = maxDistance;
                EliteBest = eliteBest; EliteAverage = eliteAverage;
            }

            /// <summary>Fracción [0,1] del mundo pisada por esta colonia.</summary>
            public float Coverage => TotalCells > 0 ? (float)VisitedCells / TotalCells : 0f;
        }

        /// <summary>F5.3ter — un punto de la curva de aprendizaje: [col, gen, hormigas, media, mejor].</summary>
        public readonly struct FitnessPointView
        {
            public readonly int ColonyId;
            public readonly int Generation;
            public readonly int Ants;
            public readonly float Mean;
            public readonly float Best;

            public FitnessPointView(int colonyId, int generation, int ants, float mean, float best)
            { ColonyId = colonyId; Generation = generation; Ants = ants; Mean = mean; Best = best; }
        }

        /// <summary>
        /// Un paquete del canal E múltiple (F5.1): de qué COLONIA y de qué TIPO,
        /// con su rejilla RLE en base64. Las capas de feromona son por colonia, así
        /// que «las feromonas» del mundo son N capas, no una.
        /// </summary>
        public readonly struct PheroLayerView
        {
            public readonly int Colony;
            public readonly byte Kind;   // ordinal de PheromoneKind (0 food, 1 home, 2 alarm)
            public readonly string Data; // base64 RLE, mismo formato que el canal E clásico

            public PheroLayerView(int colony, byte kind, string data)
            {
                Colony = colony; Kind = kind; Data = data;
            }
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
            public readonly List<CutterView> Cutters = new(); // F5.2a.3
            public readonly List<RaidView> Raids = new();     // F5.2b.3
            public readonly List<LearningView> Learning = new();      // canal C (F5.3ter)
            public readonly List<FitnessPointView> FitnessCurve = new(); // curva por generación
            public readonly List<AlertView> Alerts = new();      // canal D (F4.2)
            public readonly List<ColonyLightView> Lights = new(); // semáforo por colonia
            public string? Phero;                                 // canal E (F4.5), base64 RLE
            public readonly List<PheroLayerView> PheroSet = new(); // canal E múltiple (F5.1)
            public string? Activ;                                 // canal F (F5.0), base64 s8
            /// <summary>F5.2c rodaja 6 — topología del grafo inspeccionado
            /// (canal F NEAT). Null en mundos MLP (el campo no viaja).</summary>
            public GraphView? Graph;                              // canal F (F5.2c)
        }

        /// <summary>Cabecera del stream (parámetros de la partida).</summary>
        public sealed class HeaderView
        {
            public ulong Seed;
            public int Ticks, Colonies, Grid, FrameEvery;
            public int PheroEvery;   // canal E (F4.5); 0 = desactivado
            public bool PheroSetMode; // canal E múltiple (F5.1): los ticks traen pheroSet
            public int ActivEvery;   // canal F (F5.0); 0 = desactivado
            public ulong InspectId;  // hormiga del canal F; 0 = sin inspección
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
                    PheroEvery = raw.Contains("\"pheroEvery\"") ? (int)p.Num("pheroEvery") : 0,
                    PheroSetMode = raw.Contains("\"pheroSet\":true"),
                    ActivEvery = raw.Contains("\"activEvery\"") ? (int)p.Num("activEvery") : 0,
                    InspectId = raw.Contains("\"inspectId\"") ? (ulong)p.Num("inspectId") : 0ul,
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
                        f.Length > 11 ? (uint)Reader.NumOf(f[11]) : 0u,
                        // F5.2c rodaja 6: 13º campo opcional ("h/c") — tolerante
                        // con streams v1 (12 campos) y con cuerdas absent.
                        f.Length > 12 ? RowString(f[12]) : ""));
                }
            }

            int ii = raw.IndexOf("\"items\":[", StringComparison.Ordinal);
            if (ii >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, ii + "\"items\":[".Length - 1)))
                {
                    string[] f = RowFields(row); // [id, x, y, amount(, cutsLeft, cutsInitial)]
                    if (f.Length < 4) continue;
                    // F5.2a.3: hojas traen 6 elementos; los simples quedan en 4.
                    v.Items.Add(new ItemView((uint)Reader.NumOf(f[0]),
                        (float)Reader.NumOf(f[1]), (float)Reader.NumOf(f[2]), (float)Reader.NumOf(f[3]),
                        f.Length >= 6 ? (int)Reader.NumOf(f[4]) : 0,
                        f.Length >= 6 ? (int)Reader.NumOf(f[5]) : 0));
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
                        (int)p.Num("larvae"), (int)p.Num("pupae"), (int)p.Num("elite"),
                        (float)p.Num("fungus"), (float)p.Num("fungusMax")));
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

            // — Canal E múltiple (F5.1): [{"c":colonia,"k":capa,"d":base64}, …] —
            // El orden del array es el que pidió el emisor: se conserva tal cual
            // para que la UI pueda enseñarlo sin reordenar (y los tests de
            // determinismo comparen listas, no conjuntos).
            int psi = raw.IndexOf("\"pheroSet\":[", StringComparison.Ordinal);
            if (psi >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, psi + "\"pheroSet\":[".Length - 1)))
                {
                    var p = new Reader(row);
                    string data = p.Str("d");
                    if (data.Length == 0) continue;
                    v.PheroSet.Add(new PheroLayerView((int)p.Num("c"), (byte)p.Num("k"), data));
                }
            }

            // — Canal F (F5.0): activaciones base64 del cerebro inspeccionado —
            int aci = raw.IndexOf("\"activ\":\"", StringComparison.Ordinal);
            if (aci >= 0)
            {
                int start = aci + 9;
                int end = raw.IndexOf('"', start);
                if (end > start) v.Activ = raw.Substring(start, end - start);
            }

            // — Canal F NEAT (F5.2c rodaja 6): topología del grafo inspeccionado —
            int gi = raw.IndexOf("\"graph\":", StringComparison.Ordinal);
            if (gi >= 0)
            {
                var p = new Reader(ObjectBody(raw, gi + "\"graph\":".Length));
                v.Graph = new GraphView(
                    (int)p.Num("n"),
                    IntArrayOf(raw, "h"),
                    (int)p.Num("c"));
            }

            int mi = raw.IndexOf("\"metrics\":{", StringComparison.Ordinal);
            if (mi >= 0)
            {
                var p = new Reader(ObjectBody(raw, mi + "\"metrics\":".Length));
                v.Metrics = new MetricsView(
                    (ulong)p.Num("t0"), (ulong)p.Num("t1"),
                    (long)p.Num("pickups"), (long)p.Num("unloads"), (long)p.Num("births"),
                    (long)p.Num("deaths"), (long)p.Num("eggs"), (long)p.Num("eclosed"),
                    (long)p.Num("consumed"), (long)p.Num("commands"),
                    (long)p.Num("leafCuts"), (long)p.Num("fungusFed"));
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

            // F5.2a.3: cadena de la cortadora por colonia [col, leafCuts, fungusFed]
            // (bloque AUSENTE cuando no hubo actividad — tolerante).
            int cui = raw.IndexOf("\"cutters\":[", StringComparison.Ordinal);
            if (cui >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, cui + "\"cutters\":[".Length - 1)))
                {
                    string[] f = RowFields(row);
                    if (f.Length < 3) continue;
                    v.Cutters.Add(new CutterView((int)Reader.NumOf(f[0]),
                        (long)Reader.NumOf(f[1]), (long)Reader.NumOf(f[2])));
                }
            }

            // F5.2b.3: cadena del saqueo por colonia [col, strikes, raidInflows]
            // (bloque AUSENTE cuando no hubo actividad — tolerante).
            int rai = raw.IndexOf("\"raids\":[", StringComparison.Ordinal);
            if (rai >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, rai + "\"raids\":[".Length - 1)))
                {
                    string[] f = RowFields(row);
                    if (f.Length < 3) continue;
                    v.Raids.Add(new RaidView((int)Reader.NumOf(f[0]),
                        (long)Reader.NumOf(f[1]), (long)Reader.NumOf(f[2])));
                }
            }

            // F5.3ter: aprendizaje por colonia — [col, gen, nacimientos, hormigas,
            // caídas, media, mejor, celdas, celdas_totales, dist_máx, élite, élite_media]
            // (bloque AUSENTE en streams antiguos o mundos sin tracker: tolerante).
            int lni = raw.IndexOf("\"learning\":[", StringComparison.Ordinal);
            if (lni >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, lni + "\"learning\":[".Length - 1)))
                {
                    string[] f = RowFields(row);
                    if (f.Length < 12) continue;
                    v.Learning.Add(new LearningView(
                        (int)Reader.NumOf(f[0]), (int)Reader.NumOf(f[1]), (int)Reader.NumOf(f[2]),
                        (int)Reader.NumOf(f[3]), (int)Reader.NumOf(f[4]),
                        (float)Reader.NumOf(f[5]), (float)Reader.NumOf(f[6]),
                        (int)Reader.NumOf(f[7]), (int)Reader.NumOf(f[8]), (int)Reader.NumOf(f[9]),
                        (float)Reader.NumOf(f[10]), (float)Reader.NumOf(f[11])));
                }
            }

            // La curva de fitness por generación: [col, gen, hormigas, media, mejor].
            int fci = raw.IndexOf("\"fitcurve\":[", StringComparison.Ordinal);
            if (fci >= 0)
            {
                foreach (string row in SplitTop(ArrayBody(raw, fci + "\"fitcurve\":[".Length - 1)))
                {
                    string[] f = RowFields(row);
                    if (f.Length < 5) continue;
                    v.FitnessCurve.Add(new FitnessPointView(
                        (int)Reader.NumOf(f[0]), (int)Reader.NumOf(f[1]), (int)Reader.NumOf(f[2]),
                        (float)Reader.NumOf(f[3]), (float)Reader.NumOf(f[4])));
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
            // F5.2a.3: también cuenta LLAVES — los objetos de colonia contienen
            // arrays anidados ("nest":[x,y]) y un cierre por el PRIMER ] partía
            // la fila en el nido, perdiendo los campos posteriores (fungus).
            int depth = 0, brace = 0, i = openBracket, start = openBracket + 1;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{') brace++;
                else if (c == '}') brace--;
                else if (brace == 0)
                {
                    if (c == '[') depth++;
                    else if (c == ']') { depth--; if (depth == 0) return s.Substring(start, i - start); }
                }
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

        /// <summary>Campo de fila como string sin comillas (F5.2c rodaja 6:
        /// brainShape "h/c" del canal A).</summary>
        private static string RowString(string field)
        {
            string s = field.Trim();
            if (s.Length >= 2 && s[0] == '"' && s.EndsWith("\"", StringComparison.Ordinal))
                return s.Substring(1, s.Length - 2);
            return s;
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

        /// <summary>Array de enteros de una clave en el texto del tick
        /// (F5.2c rodaja 6: profundidades "h" del canal F NEAT). Vacío si
        /// ausente o malformado.</summary>
        private static int[] IntArrayOf(string raw, string key)
        {
            int i = raw.IndexOf("\"" + key + "\":[", StringComparison.Ordinal);
            if (i < 0) return Array.Empty<int>();
            i += key.Length + 3;
            int close = raw.IndexOf(']', i);
            if (close <= i) return Array.Empty<int>();
            string body = raw.Substring(i, close - i);
            var result = new List<int>();
            foreach (var s in SplitTop(body))
            {
                if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    result.Add(v);
            }
            return result.ToArray();
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
