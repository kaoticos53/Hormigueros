using System;
using System.Collections.Generic;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Pheromone;
using AntSim.Core.Serialization;
using AntSim.Core.Sim;
using AntSim.Core.Validation;

namespace AntSim.Core.World;

/// <summary>
/// Orquestador determinista del mundo (Fase 1).
///
/// Orden de etapas por paso (inmutable — parte del contrato de determinismo):
///   1. Cada hormiga viva (por colonia y antId ascendente): sensores → cerebro →
///      validación → movimiento, depósitos, interacción (pickup/unload).
///   2. Muertes (edad o energía ≤ 0); si llevaban carga, el ítem cae al suelo.
///   3. ColonyController por colonia (alimentación, canibalismo, cría, puesta).
///   4. Feromonas: evaporación + difusión cada 10 ticks.
///   5. Respawn de comida hasta el objetivo (RNG del mundo).
/// </summary>
public sealed class WorldSim
{
    public const float PickupRadius = 12f;
    public const float NestRadius = 24f;
    public const int InitialAdults = 10;
    public const int InitialEggs = 4;
    public const int TargetItems = 24;
    public const int RespawnBudgetPerStep = 2;
    public const int PheromoneUpdateEvery = 10;
    public const float NestMinSpawnDistance = 200f;

    private readonly ulong _seed;
    private DeterministicRandom _worldRng; // mutable: SpawnItem/Forks avanzan el flujo
    private readonly List<SimEvent> _events = new();
    private uint _nextAntId = 1;
    private uint _nextItemId = 1;
    private readonly int _gridCells;

    private static readonly int[] BrainSizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    // — Fitness (Fase 2): recompensas de por vida —
    private const float RewardPickup = 0.5f;
    private const float RewardUnloadPerEp = 2.0f;
    private const float RewardSurvivalPerSecond = 0.01f;

    public ulong Tick { get; private set; }

    public float WorldWidth { get; }
    public float WorldHeight { get; }

    public IReadOnlyList<Colony> Colonies => _colonies;
    public IReadOnlyList<FoodItem> Items => _items;
    public IReadOnlyList<SimEvent> LastEvents => _events;

    private readonly List<Colony> _colonies = new();
    private readonly List<FoodItem> _items = new();

    public WorldSim(ulong seed, int gridCells = 256, int colonyCount = 2,
        IReadOnlyList<SpeciesDescriptor>? species = null)
    {
        if (gridCells < 16) throw new ArgumentOutOfRangeException(nameof(gridCells));
        if (colonyCount < 1) throw new ArgumentOutOfRangeException(nameof(colonyCount));

        _seed = seed;
        _gridCells = gridCells;
        WorldWidth = gridCells * SimConstants.CellSizeUnits;
        WorldHeight = gridCells * SimConstants.CellSizeUnits;
        _worldRng = new DeterministicRandom(seed);

        for (int c = 0; c < colonyCount; c++)
        {
            var sp = species != null && c < species.Count ? species[c] : SpeciesDescriptor.LasiusNiger;
            var colony = CreateColony(c, sp, colonyCount);
            _colonies.Add(colony);
        }

        for (int i = 0; i < TargetItems; i++)
            SpawnItem();
    }

    private Colony CreateColony(int id, SpeciesDescriptor sp, int colonyCount)
    {
        var colony = new Colony
        {
            Id = id,
            Species = sp,
            NestX = WorldWidth * (id + 1) / (colonyCount + 1),
            NestY = WorldHeight * 0.5f,
            Stock = sp.StockMax * 0.5f,
            StockMax = sp.StockMax,
            QueenEnergy = 1f,
            Rng = _worldRng.Fork(0x9E3779B97F4A7C15UL + (ulong)id * 0xBF58476D1CE4E5B9UL),
            Pool = new GenomePool(_worldRng.Fork(0xA5C3E7B9UL + (ulong)id * 0x9E3779B9UL), BrainSizes),
            FoodLayer = new PheromoneLayer(_gridCells, _gridCells),
            HomeLayer = new PheromoneLayer(_gridCells, _gridCells),
            AlarmLayer = new PheromoneLayer(_gridCells, _gridCells),
            ConsumeEma = 1.0f
        };

        for (int i = 0; i < InitialAdults; i++)
        {
            var ant = new Ant
            {
                Id = _nextAntId++,
                ColonyId = id,
                X = colony.NestX + (float)(colony.Rng.NextDouble01() * 2.0 - 1.0) * 40f,
                Y = colony.NestY + (float)(colony.Rng.NextDouble01() * 2.0 - 1.0) * 40f,
                Heading = (float)(colony.Rng.NextDouble01() * Math.PI * 2.0 - Math.PI)
            };
            ant.InitFromVigor(sp.EnergyCapacity, sp.BaseLifespan, 0.8f);
            // Los mejores candidatos se usan al nacer (Fase 2).
            ant.Genome = colony.Pool.Birth();
            ant.Brain = ant.Genome.ToBrain();
            colony.Adults.Add(ant);
        }

        for (int i = 0; i < InitialEggs; i++)
        {
            colony.Eggs.Add(new BroodMember
            {
                Kind = BroodKind.Egg,
                Insert = colony.InsertCounter++,
                G0 = 0.4f + 0.6f * colony.Rng.NextFloat01()
            });
        }

        return colony;
    }

    public void Step()
    {
        _events.Clear();
        Tick++;

        foreach (var colony in _colonies)
            ActAllAnts(colony);

        foreach (var colony in _colonies)
            ApplyDeaths(colony);

        // Contamos las adultas antes del controlador para detectar las eclosiones.
        var adultCounts = new int[_colonies.Count];
        for (int c = 0; c < _colonies.Count; c++)
            adultCounts[c] = _colonies[c].Adults.Count;

        foreach (var colony in _colonies)
            ColonyController.Step(colony, SimConstants.FixedDtSeconds, Tick, _events, ref _nextAntId);

        // Los recién eclosionados reciben genoma y cerebro: inmigrante en cola
        // (cuarentena) o nacimiento del pool élite (los mejores al nacer).
        for (int c = 0; c < _colonies.Count; c++)
        {
            var colony = _colonies[c];
            for (int i = adultCounts[c]; i < colony.Adults.Count; i++)
            {
                var ant = colony.Adults[i];
                if (ant.Genome != null) continue;

                if (colony.Pool.TryNextImmigrant(Tick, out var immigrant))
                {
                    ant.Genome = immigrant;
                    ant.IsImmigrantTrial = true;
                }
                else
                {
                    ant.Genome = colony.Pool.Birth();
                }
                ant.Brain = ant.Genome.ToBrain();
            }
        }

        if (Tick % PheromoneUpdateEvery == 0)
        {
            foreach (var colony in _colonies)
            {
                float dt = SimConstants.FixedDtSeconds * PheromoneUpdateEvery;
                colony.FoodLayer.Evaporate(dt, PheromoneDefaults.LambdaPerSecond(PheromoneKind.FoodTrail));
                colony.FoodLayer.Diffuse(0.10f);
                colony.HomeLayer.Evaporate(dt, PheromoneDefaults.LambdaPerSecond(PheromoneKind.Home));
                colony.HomeLayer.Diffuse(0.10f);
                colony.AlarmLayer.Evaporate(dt, PheromoneDefaults.LambdaPerSecond(PheromoneKind.Alarm));
                colony.AlarmLayer.Diffuse(0.10f);
            }
        }

        RespawnItems();
    }

    private void ActAllAnts(Colony colony)
    {
        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var ant = colony.Adults[i];
            if (!ant.Alive) continue;
            Act(colony, ant);
        }
    }

    private void Act(Colony colony, Ant ant)
    {
        SpeciesDescriptor sp = colony.Species;
        float dt = SimConstants.FixedDtSeconds;

        var sensors = AntSenses.Build(colony, ant, _items, WorldWidth, WorldHeight);
        var decision = AntDecision.Neutral();
        ant.Brain.Evaluate(in sensors, ref decision);
        DecisionValidator.SanitizeAndClamp(in decision, out decision);

        // Supervivencia: pequeña recompensa por estar viva cada paso.
        ant.Fitness += RewardSurvivalPerSecond * dt;

        // — Movimiento —
        ant.Heading = WrapPi(ant.Heading + decision.Steer * sp.OmegaMax * dt);
        float loadFactor = ant.HasLoad ? (0.6f + 0.4f * (1f - Math.Min(1f, ant.LoadValue / FoodItem.MaxValue))) : 1f;
        float v = decision.Speed * sp.VMax * ant.SpeedScale * loadFactor;
        ant.X = Math.Clamp(ant.X + MathF.Cos(ant.Heading) * v * dt, 0f, WorldWidth);
        ant.Y = Math.Clamp(ant.Y + MathF.Sin(ant.Heading) * v * dt, 0f, WorldHeight);
        ant.Energy = Math.Max(0f, ant.Energy - sp.CostMove * v * dt / ant.EnergyCapacity);

        // — Depósitos de feromona (con gating químico por capa) —
        float deposited = 0f;
        if (ant.HasLoad && decision.DepositFood > 0f)
        {
            float amt = decision.DepositFood * sp.QMaxFood * dt;
            deposited += amt;
            colony.FoodLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
        }
        if (decision.DepositHome > 0f)
        {
            float amt = decision.DepositHome * sp.QMaxHome * dt;
            deposited += amt;
            colony.HomeLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
        }
        if (decision.DepositAlarm > 0f)
        {
            float amt = decision.DepositAlarm * sp.QMaxAlarm * dt;
            deposited += amt;
            colony.AlarmLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
        }
        ant.Energy = Math.Max(0f, ant.Energy - deposited * sp.CostDeposit / ant.EnergyCapacity);

        // — Interacción (gating físico) —
        ant.InteractCooldown = Math.Max(0f, ant.InteractCooldown - dt);
        if (DecisionValidator.WantsInteraction(in decision) && ant.InteractCooldown <= 0f)
        {
            if (!ant.HasLoad)
            {
                var item = NearestItemWithin(ant.X, ant.Y, PickupRadius);
                if (item != null)
                {
                    ant.HasLoad = true;
                    ant.LoadValue = item.Amount;
                    ant.Fitness += RewardPickup;
                    _items.Remove(item);
                    ant.InteractCooldown = 0.5f;
                    _events.Add(new SimEvent(SimEventKind.Pickup, Tick, colony.Id, ant.Id, ant.X, ant.Y));
                }
            }
            else
            {
                float dx = ant.X - colony.NestX;
                float dy = ant.Y - colony.NestY;
                if (dx * dx + dy * dy <= NestRadius * NestRadius)
                {
                    ant.Fitness += ant.LoadValue * RewardUnloadPerEp;
                    colony.RecordInflow(ant.LoadValue);
                    colony.InflowAccum += ant.LoadValue;
                    ant.HasLoad = false;
                    ant.LoadValue = 0f;
                    ant.InteractCooldown = 0.5f;
                    _events.Add(new SimEvent(SimEventKind.Unload, Tick, colony.Id, ant.Id, ant.X, ant.Y));
                }
            }
        }
    }

    private void ApplyDeaths(Colony colony)
    {
        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var ant = colony.Adults[i];
            if (!ant.Alive) continue;

            ant.Age += SimConstants.FixedDtSeconds;
            if (ant.Age >= ant.Lifespan || ant.Energy <= 0f)
            {
                ant.Alive = false;
                byte cause = ant.Age >= ant.Lifespan ? (byte)DeathCause.Age : (byte)DeathCause.Starvation;

                // Fase 2: el fitness de por vida alimenta el acervo (o evalúa
                // al inmigrante en cuarentena).
                if (ant.Genome != null)
                {
                    if (ant.IsImmigrantTrial)
                    {
                        var result = colony.Pool.CompleteTrial(ant.Genome, ant.Fitness, Tick);
                        _events.Add(new SimEvent(
                            result == TrialResult.EnteredElite ? SimEventKind.GenomeEnteredElite : SimEventKind.GenomeDiscarded,
                            Tick, colony.Id, ant.Id, ant.X, ant.Y, (byte)(result == TrialResult.EnteredElite ? 0 : 1)));
                    }
                    else
                    {
                        colony.Pool.RecordFitness(ant.Genome, ant.Fitness);
                    }
                }
                if (ant.HasLoad)
                {
                    var item = new FoodItem { Id = _nextItemId++, X = ant.X, Y = ant.Y, Amount = ant.LoadValue };
                    _items.Add(item);
                    _events.Add(new SimEvent(SimEventKind.ItemSpawned, Tick, colony.Id, ant.Id, item.X, item.Y));
                }
                _events.Add(new SimEvent(SimEventKind.AntDied, Tick, colony.Id, ant.Id, ant.X, ant.Y, cause));
            }
        }
    }

    private void RespawnItems()
    {
        int spawned = 0;
        while (_items.Count < TargetItems && spawned < RespawnBudgetPerStep)
        {
            if (SpawnItem()) spawned++;
            else break;
        }
    }

    private bool SpawnItem()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float x = (float)(_worldRng.NextDouble01() * WorldWidth);
            float y = (float)(_worldRng.NextDouble01() * WorldHeight);
            bool ok = true;
            for (int c = 0; c < _colonies.Count; c++)
            {
                float dx = x - _colonies[c].NestX;
                float dy = y - _colonies[c].NestY;
                if (dx * dx + dy * dy < NestMinSpawnDistance * NestMinSpawnDistance)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            var item = new FoodItem
            {
                Id = _nextItemId++,
                X = x,
                Y = y,
                Amount = 4f + (float)_worldRng.NextDouble01() * 2f
            };
            _items.Add(item);
            _events.Add(new SimEvent(SimEventKind.ItemSpawned, Tick, -1, 0, x, y));
            return true;
        }
        return false;
    }

    private FoodItem? NearestItemWithin(float x, float y, float radius)
    {
        float r2 = radius * radius;
        FoodItem? best = null;
        float bestD2 = r2;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            float dx = it.X - x;
            float dy = it.Y - y;
            float d2 = dx * dx + dy * dy;
            if (d2 <= bestD2)
            {
                bestD2 = d2;
                best = it;
            }
        }
        return best;
    }

    private int Cell(float worldCoord)
        => (int)Math.Clamp(worldCoord / SimConstants.CellSizeUnits, 0, _gridCells - 1);

    private static float WrapPi(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.PI * 2f;
        while (angle < -MathF.PI) angle += MathF.PI * 2f;
        return angle;
    }

    /// <summary>Hash canónico del estado completo (hashes de hito / verificación).</summary>
    public string HashLine()
    {
        using var h = new CanonicalHasher();
        h.AppendUInt64(_seed);
        h.AppendUInt64(Tick);

        (ulong w0, ulong w1, ulong w2, ulong w3) = _worldRng.State;
        h.AppendUInt64(w0); h.AppendUInt64(w1); h.AppendUInt64(w2); h.AppendUInt64(w3);

        h.AppendInt32(_items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            h.AppendUInt32(it.Id);
            h.AppendFloat(it.X); h.AppendFloat(it.Y); h.AppendFloat(it.Amount);
        }

        for (int c = 0; c < _colonies.Count; c++)
        {
            var col = _colonies[c];
            h.AppendInt32(col.Id);
            h.AppendFloat(col.Stock); h.AppendFloat(col.QueenEnergy);
            h.AppendFloat(col.InflowEma); h.AppendFloat(col.ConsumeEma);
            h.AppendFloat(col.EggAccumulator); h.AppendFloat(col.CannibalAccumulator);
            h.AppendInt32(col.Eggs.Count); h.AppendInt32(col.Larvae.Count);
            h.AppendInt32(col.Pupae.Count); h.AppendInt32(col.AdultCountAlive);
            h.AppendUInt64(col.FoodLayer.MutationCount);
            h.AppendUInt64(col.HomeLayer.MutationCount);
            h.AppendUInt64(col.AlarmLayer.MutationCount);
            h.AppendFloat(col.FoodLayer.SumOfValues());
            h.AppendFloat(col.HomeLayer.SumOfValues());
            h.AppendFloat(col.AlarmLayer.SumOfValues());

            // Pool genético: estado relevante para la reproducción.
            h.AppendInt32(col.Pool.EliteCount);
            h.AppendInt32(col.Pool.PendingImmigrants);
            for (int i = 0; i < col.Pool.EliteCount; i++)
                h.AppendDouble(col.Pool.Elite[i].Fitness);

            for (int i = 0; i < col.Adults.Count; i++)
            {
                var a = col.Adults[i];
                h.AppendUInt32(a.Id);
                h.AppendFloat(a.X); h.AppendFloat(a.Y); h.AppendFloat(a.Heading);
                h.AppendFloat(a.Energy); h.AppendFloat(a.Age); h.AppendFloat(a.LoadValue);
                h.AppendBool(a.Alive); h.AppendFloat(a.InteractCooldown);
                h.AppendDouble(a.Fitness);
                h.AppendBool(a.IsImmigrantTrial);
            }
            for (int i = 0; i < col.Eggs.Count; i++) AppendBrood(h, col.Eggs[i]);
            for (int i = 0; i < col.Larvae.Count; i++) AppendBrood(h, col.Larvae[i]);
            for (int i = 0; i < col.Pupae.Count; i++) AppendBrood(h, col.Pupae[i]);
        }

        return h.FinalizeHex();
    }

    // — Fase 2: intercambio de cerebros (.antgenome) —

    /// <summary>Encola genomas importados como inmigrantes en cuarentena de una colonia.</summary>
    public void ImportGenomes(int colonyId, IReadOnlyList<MlpGenome> genomes)
    {
        var colony = _colonies[colonyId];
        foreach (var g in genomes)
            colony.Pool.QueueImmigrant(g, Tick);
    }

    /// <summary>Élite actual de una colonia (orden de mérito descendente).</summary>
    public IReadOnlyList<MlpGenome> ExportElite(int colonyId) => _colonies[colonyId].Pool.Elite;

    public void ImportGenomesFromFile(int colonyId, string path)
    {
        var (_, genomes) = AntGenomeFile.ReadFile(path, BrainContract.CurrentVersion);
        ImportGenomes(colonyId, genomes);
    }

    public void ExportEliteToFile(int colonyId, string path, string name, string speciesHint)
    {
        AntGenomeFile.WriteFile(path, name, speciesHint, _seed, 0, ExportElite(colonyId),
            BrainContract.CurrentVersion);
    }

    private static void AppendBrood(CanonicalHasher h, BroodMember b)
    {
        h.AppendByte((byte)b.Kind);
        h.AppendInt32(b.Insert);
        h.AppendFloat(b.Age);
        h.AppendFloat(b.G0);
        h.AppendFloat(b.Nutrition);
        h.AppendBool(b.Weak);
    }
}