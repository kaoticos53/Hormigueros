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
    public const int TargetItemsDefault = 24;
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

    // — Fitness (Fase 2): recompensas de por vida. Configurables para que la
    // arena de pre-entrenamiento (Fase 3) pueda re-equilibrar la señal sin
    // tocar el contrato de determinismo (mismos valores ⇒ mismo mundo).
    public float RewardPickup { get; set; } = 0.5f;
    public float RewardUnloadPerEp { get; set; } = 2.0f;
    public float RewardSurvivalPerSecond { get; set; } = 0.01f;
    public float RewardDepositPerUnit { get; set; } = 0.0f; // refuerzo del depósito de feromona (arena)
    public float RewardUnloadBonus { get; set; } = 0.0f;    // bonus plano por ciclo completo (arena)    /// <summary>Objetivo de ítems en el mundo (la arena lo pone a 0).
    /// Por defecto escala con el ÁREA del mundo: 24 ítems por mundo de 96²
    /// (la calibración de la arena), i.e. densidad constante ≈ 24/(96·8)² u².
    /// Fase 3ter: con objetivo fijo, el mundo grande (256²) tenía 24 ítems en
    /// 7.1× el área — densidad ×7 menor, y el forrajeo inicial en la banda
    /// 200–260 acertaba tan raramente que el relevo no arrancaba en la
    /// mitad de las semillas del modo juego. Escalar por área mantiene la
    /// MISMA probabilidad de encuentro por unidad de recorrido en cualquier
    /// tamaño de mundo (24 → 171 ítems en 256²).</summary>
    public int TargetItems { get; set; } = TargetItemsDefault;

    private int DensityScaledTargetItems => (int)MathF.Round(
        TargetItemsDefault * (WorldWidth * WorldHeight) / (96f * SimConstants.CellSizeUnits * 96f * SimConstants.CellSizeUnits));

    public ulong Tick { get; private set; }

    public float WorldWidth { get; }
    public float WorldHeight { get; }

    public IReadOnlyList<Colony> Colonies => _colonies;
    public IReadOnlyList<FoodItem> Items => _items;
    public IReadOnlyList<SimEvent> LastEvents => _events;

    /// <summary>Semilla original del mundo (los checkpoints la reproducen).</summary>
    public ulong Seed => _seed;

    /// <summary>Celdas por lado del grid de feromonas.</summary>
    public int GridCells => _gridCells;

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

        int initialItems = DensityScaledTargetItems;
        for (int i = 0; i < initialItems; i++)
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
            // Fundamento a pleno rendimiento: una colonia fundada debe sobrevivir
            // al arranque en frío con SU reserva completa (StockMax), como una
            // colonia real que funda con sus reservas propias. Al 50 % el stock se
            // agotaba (~1.1 ep/s de quema) antes de la primera ventana de forrajeo
            // (~80–110 s: la comida está a ≥ NestMinSpawnDistance, fuera de visión).
            Stock = sp.StockMax,
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
            // Vigor fundador ESCALONADO de 0.75 a 1.15: con vigor uniforme todas
            // las fundadoras comparten esperanza de vida y mueren el MISMO tick —
            // el relevo por suelta al morir (una portadora que cae deja el ítem
            // en el suelo) era imposible por construcción y la colonia fundada
            // moría en bloque sin descendencia. El escalonado desincroniza las
            // muertes y escalona las sueltas a lo largo de la generación.
            // Fase 3ter (dead zone del modo juego): con 0.6–1.0 la ÚLTIMA
            // fundadora moría ~117 s (tick 3510) y la primera eclosión madura
            // ~120–140 s: en el mundo grande (grid 256) el relevo apenas
            // arrancaba cuando ya no quedaban portadoras expertas — ventana
            // muerta hasta el final de la partida (evaluación a 1600 s: 92 %
            // del horizonte sin actividad). Rango 0.75–1.15: la última fundadora
            // vive ~90·(0.7+0.6·1.15) = 125 s (tick 3750), la más frágil
            // 90·(0.7+0.6·0.75) = 103.5 s, y la primera descendencia (de
            // huevos iniciales, vigor alto con cría bien nodrizada) eclosiona
            // en vida de las fundadoras con solapamiento completo. Realismo:
            // las reinas fundadoras de Lasius niger producen primeras obreras
            // más robustas que la media (inversión fundadora), no menos.
            float vigor = InitialAdults > 1
                ? 0.75f + 0.4f * i / (InitialAdults - 1)
                : 1f;
            ant.InitFromVigor(sp.EnergyCapacity, sp.BaseLifespan, vigor);
            // Los mejores candidatos se usan al nacer (Fase 2).
            ant.Genome = colony.Pool.Birth();
            ant.Brain = ant.Genome.ToBrain();
            colony.Adults.Add(ant);
        }

        // Cría inicial ADELANTADA (Fase 3ter, dead zone del modo juego): los
        // huevos de la fundación nacen con la mitad de su tiempo de huevo ya
        // consumido (Age = EggTime/2). Realismo: una reina fundadora pone su
        // PRIMERA puesta antes de que la colonia exista como tal — los huevos
        // que se encuentran al fundar no empiezan de cero. Efecto: la primera
        // cohorte de obreras eclosiona ~4 s antes (8→4 s de huevo + 25 de
        // larva + 12 de pupa ≈ 41 s → ~37 s), solapando con el pico de
        // actividad forrajera de las fundadoras (103–125 s) y dando tiempo a
        // que la descendencia esté VIVA y pueda recoger las sueltas del relevo
        // mientras aún hay portadoras expertas.
        for (int i = 0; i < InitialEggs; i++)
        {
            colony.Eggs.Add(new BroodMember
            {
                Kind = BroodKind.Egg,
                Insert = colony.InsertCounter++,
                G0 = 0.4f + 0.6f * colony.Rng.NextFloat01(),
                Age = sp.EggTime * 0.5f
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
        if (deposited > 0f && RewardDepositPerUnit > 0f)
            ant.Fitness += deposited * RewardDepositPerUnit;

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
                    ant.Fitness += ant.LoadValue * RewardUnloadPerEp + RewardUnloadBonus;
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
        // Objetivo escalado por área (ver DensityScaledTargetItems): el setter
        // explícito de TargetItems (la arena lo pone a 0; el CLI podría fijarlo)
        // tiene prioridad — solo cuando sigue en el default se aplica la
        // densidad constante.
        int target = TargetItems != TargetItemsDefault ? TargetItems : DensityScaledTargetItems;
        while (_items.Count < target && spawned < RespawnBudgetPerStep)
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

    /// <summary>Vacía la lista de ítems (uso de la arena de pre-entrenamiento).</summary>
    public void ClearItems() => _items.Clear();

    /// <summary>Añade un ítem directamente (uso de la arena de pre-entrenamiento).</summary>
    public void AddItem(FoodItem item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (item.Id == 0) item.Id = _nextItemId++;
        _items.Add(item);
    }

    /// <summary>Élite actual de una colonia (orden de mérito descendente).</summary>
    public IReadOnlyList<MlpGenome> ExportElite(int colonyId) => _colonies[colonyId].Pool.Elite;

    public void ImportGenomesFromFile(int colonyId, string path)
    {
        var (_, genomes) = AntGenomeFile.ReadFile(path, BrainContract.CurrentVersion);
        ImportGenomes(colonyId, genomes);
    }

    /// <summary>
    /// Siembra el pool élite de una colonia con genomas pre-entrenados
    /// (Fase 3): desde ese momento los nacimientos usan esta élite. Además
    /// re-asigna cerebros a las adultas VIVAS ya construidas (fundadoras): en el
    /// constructor WorldSim las fundadoras nacen ANTES de que exista la élite
    /// (Pool.Birth cae al aleatorio), de modo que sembrar solo el pool dejaba
    /// a la generación 0 con cerebros aleatorios — la causa de que --seed-pool
    /// no mejorara la supervisión inicial pese a que la arena demuestra que los
    /// genomas pre-entrenados SÍ forrajean en el régimen del mundo real.
    /// </summary>
    public void SeedPoolFromGenomes(int colonyId, IReadOnlyList<MlpGenome> genomes)
    {
        var colony = _colonies[colonyId];
        colony.Pool.ReplaceElite(genomes);
        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var ant = colony.Adults[i];
            if (!ant.Alive) continue;
            ant.Genome = colony.Pool.Birth();
            ant.Brain = ant.Genome.ToBrain();
        }
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

    // — Fase 4: carga de checkpoints (.antsave) —
    // Accesores internos: WorldSimSave reconstruye el estado COMPLETO (se
    // guarda la causa, no los efectos), incluyendo los RNGs por su estado
    // exacto y los contadores de identidad. No son parte de la API pública.

    /// <summary>Restablece el tick tras cargar (los contadores vienen después).</summary>
    internal void ResetForLoad(ulong tick)
    {
        Tick = tick;
    }

    /// <summary>Estado exacto del RNG del mundo (serialización de checkpoints).</summary>
    internal (ulong S0, ulong S1, ulong S2, ulong S3) WorldRngState => _worldRng.State;

    /// <summary>Restaura el RNG del mundo desde su estado exacto (carga de checkpoints).</summary>
    internal void RestoreWorldRng(in DeterministicRandom rng) => _worldRng = rng;

    /// <summary>Contadores de identidad actuales (serialización de checkpoints).</summary>
    internal (uint NextAntId, uint NextItemId) IdentityCounters => (_nextAntId, _nextItemId);

    /// <summary>Restaura los contadores de identidad desde un checkpoint.</summary>
    internal void RestoreIdentityCounters(uint nextAntId, uint nextItemId)
    {
        _nextAntId = nextAntId;
        _nextItemId = nextItemId;
    }

    /// <summary>Vacía las colonias provisionales antes de reconstruirlas desde el checkpoint.</summary>
    internal void ClearColoniesForLoad() => _colonies.Clear();

    /// <summary>Añade un ítem SIN reasignar Id (carga de checkpoint: el Id es parte del estado).</summary>
    internal void AddItemUnchecked(FoodItem item) => _items.Add(item);

    /// <summary>
    /// Añade durante la carga de un checkpoint una colonia reconstruida: Id y
    /// nido guardados, RNG por estado exacto y especie recuperada por nombre.
    /// Devuelve la colonia vacía (sin adultas ni cría) con capas y pool ya
    /// construidos; el pool se siembra con 0 genomas (la élite llega del
    /// archivo) y su RNG se restaura después por estado exacto.
    /// </summary>
    internal Colony AddColonyForLoad(int id, string speciesName, float nestX, float nestY,
        float stockMax, DeterministicRandom rng)
    {
        var sp = SpeciesDescriptor.ByName(speciesName);
        var colony = new Colony
        {
            Id = id,
            Species = sp,
            NestX = nestX,
            NestY = nestY,
            StockMax = stockMax,
            Rng = rng,
            Pool = new GenomePool(rng.Fork(0xA5C3E7B9UL), BrainSizes, seedCount: 0),
            FoodLayer = new PheromoneLayer(_gridCells, _gridCells),
            HomeLayer = new PheromoneLayer(_gridCells, _gridCells),
            AlarmLayer = new PheromoneLayer(_gridCells, _gridCells)
        };
        _colonies.Add(colony);
        return colony;
    }
}