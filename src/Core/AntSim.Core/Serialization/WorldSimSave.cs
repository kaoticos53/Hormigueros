using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Pheromone;
using AntSim.Core.Sim;
using AntSim.Core.World;

namespace AntSim.Core.Serialization;

/// <summary>
/// Checkpoint .antsave v3 (F5.2a.1 — ítems compuestos): como v2 con CutsLeft/
/// CutsInitial por ítem. Los v2 ya no se cargan (el patrón v1→v2 ya probado).
/// en un límite de tick. "Se guarda la CAUSA, no los efectos": RNGs por su
/// estado exacto, acumuladores ocultos (EWMA, puesta, canibalismo), nutrición
/// larvaria, genomas de la élite y de cada adulta, grids de feromona celda a
/// celda con sus versiones por tile, y todos los contadores que la
/// reprodución necesita. Cargar en T y re-ejecutar regenera eventos y hashes
/// idénticos bit a bit.
///
/// Layout canónico (little-endian, floats por bits exactos):
///   magic "ANTSAVE1" (8) · SHA-256 al final (32) del cuerpo:
///   seed u64 · tick u64 · gridCells i32 · targetItems i32 · recompensas f32 ×5 ·
///   rng del mundo (s0..s3 u64) · nextAntId u32 · nextItemId u32 ·
///   items: count u32 · {id u32, x f32, y f32, amount f32, cuts i32 ×2}·
///   por colonia:
///     id i32 · nombre de especie (str) · nestX f32 · nestY f32 ·
///     stock f32 · stockMax f32 · queenEnergy f32 ·
///     inflowEma f32 · inflowAccum f32 · consumeEma f32 ·
///     eggAccumulator f32 · cannibalAccumulator f32 · insertCounter i32 ·
///     fungus f32 · fungusMax f32 ·
///     rng (s0..s3 u64) ·
///     élite: count u32 · {nLayers u32 · sizes u16· · weights f32· · fitness f64}·
///     inmigrantes: count u32 · {mismo layout + queuedTick u64}·
///     trialsEntered i32 · trialsDiscarded i32 · trialsExpired i32 ·
///     rng del pool (s0..s3 u64) — los nacimientos (torneo/crossover/mutación)
///     food/home/alarm: {w i32 · h i32 · mutationCount u64 · tileVersions u32· · values f32·}·
///     adultas: count u32 · {id u32 · x/y/heading f32 · energy f32 ·
///       energyCapacity f32 · age f32 · lifespan f32 · vigor/speedScale/sensorScale f32 ·
///       hasLoad u8 · loadValue f32 · alive u8 · interactCooldown f32 · fitness f64 ·
///       isImmigrantTrial u8 · genoma (mismo layout de genoma, 0 = ninguna)}·
///     cría: eggs/larvae/pupae: count u32 · {kind u8 · insert i32 · age f32 ·
///       g0 f32 · nutrition f32 · weak u8}·
/// </summary>
public static class WorldSimSave
{
    /// <summary>v2 (F4.4): añade el flag CloneFromElite por colonia (1 byte al
    /// final del bloque de colonia). Los checkpoints v1 ya no se cargan.</summary>
    public const int FormatVersion = 3;
    private static readonly byte[] Magic = { (byte)'A', (byte)'N', (byte)'T', (byte)'S', (byte)'A', (byte)'V', (byte)'E', (byte)'1' };

    // Recompensas configurables de WorldSim: son parte del estado (la arena
    // las re-equilibra y dos mundos con recompensas distintas no son el mismo).
    private static readonly System.Func<WorldSim, float>[] RewardGetters =
    {
        s => s.RewardPickup,
        s => s.RewardUnloadPerEp,
        s => s.RewardSurvivalPerSecond,
        s => s.RewardDepositPerUnit,
        s => s.RewardUnloadBonus
    };

    public static void Save(WorldSim sim, string path)
    {
        if (sim is null) throw new ArgumentNullException(nameof(sim));
        File.WriteAllBytes(path, Serialize(sim));
    }

    public static byte[] Serialize(WorldSim sim)
    {
        using var ms = new MemoryStream();
        var w = new CanonicalWriter(ms);
        ms.Write(Magic, 0, Magic.Length);
        w.WriteI32(FormatVersion); // v2: presente desde F4.4 (los v1 no lo llevaban)

        w.WriteU64(sim.Seed);
        w.WriteU64(sim.Tick);
        w.WriteI32(sim.GridCells);
        w.WriteI32(sim.TargetItems);
        for (int i = 0; i < RewardGetters.Length; i++) w.WriteF32(RewardGetters[i](sim));

        // — RNG del mundo y contadores de identidad (respawn e Ids dependen de ellos) —
        WriteRandom(w, DeterministicRandom.FromState(
            sim.WorldRngState.S0, sim.WorldRngState.S1, sim.WorldRngState.S2, sim.WorldRngState.S3));
        w.WriteU32(sim.IdentityCounters.NextAntId);
        w.WriteU32(sim.IdentityCounters.NextItemId);

        // — Ítems —
        w.WriteU32((uint)sim.Items.Count);
        for (int i = 0; i < sim.Items.Count; i++)
        {
            var it = sim.Items[i];
            w.WriteU32(it.Id);
            w.WriteF32(it.X);
            w.WriteF32(it.Y);
            w.WriteF32(it.Amount);
            w.WriteI32(it.CutsLeft); // F5.2a.1 (v3)
            w.WriteI32(it.CutsInitial);
        }

        // — Colonias —
        w.WriteU32((uint)sim.Colonies.Count);
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var col = sim.Colonies[c];
            w.WriteI32(col.Id);
            w.WriteString(col.Species.Name);
            w.WriteF32(col.NestX);
            w.WriteF32(col.NestY);
            w.WriteF32(col.Stock);
            w.WriteF32(col.StockMax);
            w.WriteF32(col.Fungus); // F5.2a.2 (v3)
            w.WriteF32(col.QueenEnergy);
            w.WriteF32(col.InflowEma);
            w.WriteF32(col.InflowAccum);
            w.WriteF32(col.ConsumeEma);
            w.WriteF32(col.EggAccumulator);
            w.WriteF32(col.CannibalAccumulator);
            w.WriteI32(col.InsertCounter);
            WriteRandom(w, col.Rng);

            WriteGenomeList(w, col.Pool.Elite);
            WriteImmigrants(w, col.Pool);
            w.WriteI32(col.Pool.TrialsEntered);
            w.WriteI32(col.Pool.TrialsDiscarded);
            w.WriteI32(col.Pool.TrialsExpired);
            WriteRandom(w, DeterministicRandom.FromState(
                col.Pool.RngState.S0, col.Pool.RngState.S1, col.Pool.RngState.S2, col.Pool.RngState.S3));
            w.WriteBool(col.Pool.CloneFromElite); // v2 (F4.4): modo reuso de cerebros

            WriteLayer(w, col.FoodLayer);
            WriteLayer(w, col.HomeLayer);
            WriteLayer(w, col.AlarmLayer);

            w.WriteU32((uint)col.Adults.Count);
            for (int i = 0; i < col.Adults.Count; i++)
            {
                var a = col.Adults[i];
                w.WriteU32(a.Id);
                w.WriteF32(a.X);
                w.WriteF32(a.Y);
                w.WriteF32(a.Heading);
                w.WriteF32(a.Energy);
                w.WriteF32(a.EnergyCapacity);
                w.WriteF32(a.Age);
                w.WriteF32(a.Lifespan);
                w.WriteF32(a.Vigor);
                w.WriteF32(a.SpeedScale);
                w.WriteF32(a.SensorScale);
                w.WriteBool(a.HasLoad);
                w.WriteF32(a.LoadValue);
                w.WriteBool(a.Alive);
                w.WriteF32(a.InteractCooldown);
                w.WriteF64(a.Fitness);
                w.WriteBool(a.IsImmigrantTrial);
                WriteGenomeOrNull(w, a.Genome);
            }

            WriteBrood(w, col.Eggs);
            WriteBrood(w, col.Larvae);
            WriteBrood(w, col.Pupae);
        }

        byte[] body = ms.ToArray();
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(body);
        var result = new byte[body.Length + hash.Length];
        Array.Copy(body, result, body.Length);
        Array.Copy(hash, 0, result, body.Length, hash.Length);
        return result;
    }

    public static WorldSim Load(string path) => Deserialize(File.ReadAllBytes(path));

    public static WorldSim Deserialize(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < Magic.Length + 32)
            throw new FormatException("Archivo .antsave demasiado corto.");

        int bodyLen = data.Length - 32;
        var body = new byte[bodyLen];
        Array.Copy(data, body, bodyLen);
        var trailing = new byte[32];
        Array.Copy(data, bodyLen, trailing, 0, 32);
        byte[] actual;
        using (var sha = SHA256.Create())
            actual = sha.ComputeHash(body);
        if (!FixedTimeEquals(actual, trailing))
            throw new FormatException("Integridad fallida: el hash SHA-256 no coincide.");

        var ms = new MemoryStream(body, writable: false);
        var r = new CanonicalReader(ms);
        var magic = r.ReadBytes(Magic.Length);
        for (int i = 0; i < Magic.Length; i++)
            if (magic[i] != Magic[i])
                throw new FormatException("Magic inválido: no es un archivo .antsave.");

        // v2 (F4.4): los checkpoints guardan su versión — los v1 (sin el byte de
        // CloneFromElite por colonia) ya no se cargan: fallar AQUÍ y no después.
        int version = r.ReadI32();
        if (version < 2)
            throw new FormatException($"Checkpoint v{version} obsoleto (se requieren v{FormatVersion}+): regenerar el .antsave.");
        if (version > FormatVersion)
            throw new FormatException($"Checkpoint v{version} más nuevo que este build (v{FormatVersion}).");

        ulong seed = r.ReadU64();
        ulong tick = r.ReadU64();
        int gridCells = r.ReadI32();
        int targetItems = r.ReadI32();
        if (gridCells < 16) throw new FormatException($"Grid inválido: {gridCells}.");

        float[] rewards = new float[RewardGetters.Length];
        for (int i = 0; i < rewards.Length; i++)
        {
            rewards[i] = r.ReadF32();
            if (!FloatUtil.IsFinite(rewards[i])) throw new FormatException("Recompensa no finita.");
        }

        var worldRng = ReadRandom(r);
        uint nextAntId = r.ReadU32();
        uint nextItemId = r.ReadU32();

        var sim = new WorldSim(seed, gridCells, colonyCount: 1)
        {
            TargetItems = targetItems,
            RewardPickup = rewards[0],
            RewardUnloadPerEp = rewards[1],
            RewardSurvivalPerSecond = rewards[2],
            RewardDepositPerUnit = rewards[3],
            RewardUnloadBonus = rewards[4]
        };
        sim.ResetForLoad(tick);

        // — Ítems —
        uint itemCount = r.ReadU32();
        if (itemCount > 10_000_000) throw new FormatException($"Número de ítems inválido: {itemCount}.");
        sim.ClearItems();
        for (uint i = 0; i < itemCount; i++)
        {
            var item = new FoodItem
            {
                Id = r.ReadU32(),
                X = r.ReadF32(),
                Y = r.ReadF32(),
            Amount = r.ReadF32(),
            CutsLeft = r.ReadI32(),
            CutsInitial = r.ReadI32()
            };
            if (!FloatUtil.IsFinite(item.X) || !FloatUtil.IsFinite(item.Y) || !FloatUtil.IsFinite(item.Amount))
                throw new FormatException("Ítem no finito (archivo corrupto).");
            sim.AddItemUnchecked(item);
        }

        // — Colonias (reemplazan las provisionales del constructor) —
        uint colonyCount = r.ReadU32();
        if (colonyCount < 1 || colonyCount > 256) throw new FormatException($"Número de colonias inválido: {colonyCount}.");
        sim.ClearColoniesForLoad();
        for (uint c = 0; c < colonyCount; c++)
        {
            int id = r.ReadI32();
            string speciesName = r.ReadString();
            float nestX = r.ReadF32();
            float nestY = r.ReadF32();
            float stock = r.ReadF32();
            float stockMax = r.ReadF32();
            float fungus = r.ReadF32(); // F5.2a.2 (v3)
            float queenEnergy = r.ReadF32();
            float inflowEma = r.ReadF32();
            float inflowAccum = r.ReadF32();
            float consumeEma = r.ReadF32();
            float eggAccumulator = r.ReadF32();
            float cannibalAccumulator = r.ReadF32();
            int insertCounter = r.ReadI32();
            var colonyRng = ReadRandom(r);

            var colony = sim.AddColonyForLoad(id, speciesName, nestX, nestY, stockMax, colonyRng);
            colony.Stock = stock;
            colony.StockMax = stockMax;
            colony.Fungus = fungus;
            colony.QueenEnergy = queenEnergy;
            colony.InflowEma = inflowEma;
            colony.InflowAccum = inflowAccum;
            colony.ConsumeEma = consumeEma;
            colony.EggAccumulator = eggAccumulator;
            colony.CannibalAccumulator = cannibalAccumulator;
            colony.InsertCounter = insertCounter;

            // Élite
            int eliteCount = checked((int)r.ReadU32());
            var elite = new List<MlpGenome>(eliteCount);
            for (int i = 0; i < eliteCount; i++)
                elite.Add(ReadGenome(r));
            colony.Pool.ReplaceElite(elite);

            // Inmigrantes en cuarentena (con su tick de encolado)
            int immCount = checked((int)r.ReadU32());
            for (int i = 0; i < immCount; i++)
            {
                var g = ReadGenome(r);
                ulong queuedTick = r.ReadU64();
                colony.Pool.QueueImmigrant(g, queuedTick);
            }
            colony.Pool.TrialsEntered = r.ReadI32();
            colony.Pool.TrialsDiscarded = r.ReadI32();
            colony.Pool.TrialsExpired = r.ReadI32();
            colony.Pool.RestoreRng(ReadRandom(r));
            colony.Pool.RestoreCloneFromElite(r.ReadBool()); // v2 (F4.4)

            ReadLayerInto(r, colony.FoodLayer);
            ReadLayerInto(r, colony.HomeLayer);
            ReadLayerInto(r, colony.AlarmLayer);

            // Adultas
            int adultCount = checked((int)r.ReadU32());
            for (int i = 0; i < adultCount; i++)
            {
                var ant = new Ant
                {
                    Id = r.ReadU32(),
                    ColonyId = id,
                    X = r.ReadF32(),
                    Y = r.ReadF32(),
                    Heading = r.ReadF32(),
                    Energy = r.ReadF32(),
                    EnergyCapacity = r.ReadF32(),
                    Age = r.ReadF32(),
                    Lifespan = r.ReadF32(),
                    Vigor = r.ReadF32(),
                    SpeedScale = r.ReadF32(),
                    SensorScale = r.ReadF32(),
                    HasLoad = r.ReadBool(),
                    LoadValue = r.ReadF32(),
                    Alive = r.ReadBool(),
                    InteractCooldown = r.ReadF32(),
                    Fitness = r.ReadF64(),
                    IsImmigrantTrial = r.ReadBool()
                };
                var genome = ReadGenomeOrNull(r);
                if (genome != null)
                {
                    ant.Genome = genome;
                    ant.Brain = genome.ToBrain();
                }
                colony.Adults.Add(ant);
                ValidateFiniteAnt(ant);
            }

            ReadBroodInto(r, colony.Eggs);
            ReadBroodInto(r, colony.Larvae);
            ReadBroodInto(r, colony.Pupae);
        }

        if (ms.Position != ms.Length)
            throw new FormatException("Bytes sobrantes al final del checkpoint (versión distinta).");
        sim.RestoreWorldRng(worldRng);
        sim.RestoreIdentityCounters(nextAntId, nextItemId);
        return sim;
    }

    // — Genomas —

    private static void WriteGenomeList(CanonicalWriter w, IReadOnlyList<MlpGenome> genomes)
    {
        w.WriteU32((uint)genomes.Count);
        for (int i = 0; i < genomes.Count; i++)
            WriteGenomeBody(w, genomes[i]);
    }

    private static void WriteImmigrants(CanonicalWriter w, GenomePool pool)
    {
        // El pool no expone los inmigrantes pendientes en cola; se reconstruyen
        // vía PendingImmigrants/QueueImmigrant desde un accessor interno.
        var pending = pool.CopyPendingImmigrants();
        w.WriteU32((uint)pending.Count);
        for (int i = 0; i < pending.Count; i++)
        {
            WriteGenomeBody(w, pending[i].Genome);
            w.WriteU64(pending[i].QueuedTick);
        }
    }

    private static void WriteGenomeOrNull(CanonicalWriter w, MlpGenome? g)
    {
        if (g is null)
        {
            w.WriteU32(0);
            return;
        }
        WriteGenomeBody(w, g);
    }

    private static void WriteGenomeBody(CanonicalWriter w, MlpGenome g)
    {
        int[] sizes = g.Sizes;
        w.WriteU32((uint)sizes.Length);
        for (int l = 0; l < sizes.Length; l++) w.WriteU16((ushort)sizes[l]);
        float[] weights = g.CopyWeights();
        for (int i = 0; i < weights.Length; i++) w.WriteF32(weights[i]);
        w.WriteF64(g.Fitness);
    }

    private static MlpGenome ReadGenome(CanonicalReader r)
    {
        var g = ReadGenomeOrNull(r);
        if (g is null) throw new FormatException("Genoma nulo donde se esperaba uno.");
        return g;
    }

    private static MlpGenome? ReadGenomeOrNull(CanonicalReader r)
    {
        int nLayers = checked((int)r.ReadU32());
        if (nLayers == 0) return null;
        if (nLayers < 2 || nLayers > 32) throw new FormatException($"Número de capas inválido: {nLayers}.");
        var sizes = new int[nLayers];
        for (int l = 0; l < nLayers; l++)
        {
            sizes[l] = r.ReadU16();
            if (sizes[l] < 1) throw new FormatException("Capa de tamaño 0.");
        }
        if (sizes[0] != AntSensorChannelInfo.Count || sizes[^1] != AntDecision.DecisionCount)
            throw new FormatException("La topología no coincide con el contrato de canales.");
        int n = MlpBrain.ExpectedWeightCount(sizes);
        var weights = new float[n];
        for (int k = 0; k < n; k++)
        {
            weights[k] = r.ReadF32();
            if (!FloatUtil.IsFinite(weights[k])) throw new FormatException("Peso no finito.");
        }
        double fitness = r.ReadF64();
        return new MlpGenome(sizes, weights, fitness);
    }

    // — Feromonas —

    private static void WriteLayer(CanonicalWriter w, PheromoneLayer layer)
    {
        w.WriteI32(layer.Width);
        w.WriteI32(layer.Height);
        w.WriteU64(layer.MutationCount);
        int tiles = layer.TilesX * layer.TilesY;
        for (int t = 0; t < tiles; t++)
            w.WriteU32(layer.RawTileVersion(t));
        for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
                w.WriteF32(layer[x, y]);
    }

    private static void ReadLayerInto(CanonicalReader r, PheromoneLayer layer)
    {
        int width = r.ReadI32();
        int height = r.ReadI32();
        if (width != layer.Width || height != layer.Height)
            throw new FormatException("Dimensiones de la capa de feromona no coinciden con el mundo.");
        ulong mutationCount = r.ReadU64();
        int tiles = layer.TilesX * layer.TilesY;
        var tileVersions = new uint[tiles];
        for (int t = 0; t < tiles; t++) tileVersions[t] = r.ReadU32();
        var values = new float[width * height];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = r.ReadF32();
            if (!FloatUtil.IsFinite(values[i]) || values[i] < 0f)
                throw new FormatException("Valor de feromona inválido.");
        }
        layer.LoadState(values, tileVersions, mutationCount);
    }

    // — Cría —

    private static void WriteBrood(CanonicalWriter w, List<BroodMember> brood)
    {
        w.WriteU32((uint)brood.Count);
        for (int i = 0; i < brood.Count; i++)
        {
            var b = brood[i];
            w.WriteByte((byte)b.Kind);
            w.WriteI32(b.Insert);
            w.WriteF32(b.Age);
            w.WriteF32(b.G0);
            w.WriteF32(b.Nutrition);
            w.WriteBool(b.Weak);
        }
    }

    private static void ReadBroodInto(CanonicalReader r, List<BroodMember> brood)
    {
        int count = checked((int)r.ReadU32());
        for (int i = 0; i < count; i++)
        {
            byte kind = r.ReadByte();
            if (kind > (byte)BroodKind.Pupa) throw new FormatException($"Tipo de cría inválido: {kind}.");
            brood.Add(new BroodMember
            {
                Kind = (BroodKind)kind,
                Insert = r.ReadI32(),
                Age = r.ReadF32(),
                G0 = r.ReadF32(),
                Nutrition = r.ReadF32(),
                Weak = r.ReadBool()
            });
        }
    }

    // — RNG —

    private static void WriteRandom(CanonicalWriter w, in DeterministicRandom rng)
    {
        (ulong s0, ulong s1, ulong s2, ulong s3) = rng.State;
        w.WriteU64(s0);
        w.WriteU64(s1);
        w.WriteU64(s2);
        w.WriteU64(s3);
    }

    private static DeterministicRandom ReadRandom(CanonicalReader r)
        => DeterministicRandom.FromState(r.ReadU64(), r.ReadU64(), r.ReadU64(), r.ReadU64());

    private static void ValidateFiniteAnt(Ant a)
    {
        if (!FloatUtil.IsFinite(a.X) || !FloatUtil.IsFinite(a.Y) || !FloatUtil.IsFinite(a.Heading) ||
            !FloatUtil.IsFinite(a.Energy) || !FloatUtil.IsFinite(a.EnergyCapacity) ||
            !FloatUtil.IsFinite(a.Age) || !FloatUtil.IsFinite(a.Lifespan) ||
            !FloatUtil.IsFinite(a.InteractCooldown))
            throw new FormatException("Estado de hormiga no finito (archivo corrupto).");
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        int diff = a.Length ^ b.Length;
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
