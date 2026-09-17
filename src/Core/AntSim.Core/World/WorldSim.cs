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
    private double[] _deadFitnessBank = Array.Empty<double>(); // F5.3 rodaja 2: fitness de por vida de las muertas compactadas
    private readonly List<SimEvent> _events = new();
    private readonly List<SimCommand> _pendingCommands = new(); // F4.0: cola de comandos (aplicados en el punto canónico)
    private readonly List<SaveRequest> _saveRequests = new();   // F4.0: peticiones de guardado del último Step
    private bool[]? _extinctReported; // F4.2: ColonyExtinct una vez por colonia (lazy: el load reconstruye colonias)
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

    /// <summary>F5.2a.1: fracción [0,1] de ítems nuevos que nacen como HOJAS
    /// (ítems compuestos con CutsLeft 3–5). 0 = mundo clásico, byte a byte
    /// el de siempre (ni siquiera consume RNG: el hash de los pines de CI
    /// no se mueve). La hoja vale 8–14 ep con fragmento ≈ 2.7–3.5 ep.</summary>
    public float LeafFraction { get; set; } = 0f;

    /// <summary>
    /// F5.3bis — política FIJADA para toda la colonia (ver <see cref="ForcePolicy"/>):
    /// si no es null, cada hormiga que ECLOSIONE recibe este cerebro en vez de un
    /// genoma del pool (y sin Genome, así que tampoco realimenta la evolución).
    ///
    /// Con null — por defecto — el mundo funciona exactamente como antes: el hook
    /// no toca el RNG ni el orden de iteración (los pines de hash de CI siguen
    /// valiendo, hay test).
    /// </summary>
    public Brain.IBrain? ForcedPolicy { get; private set; }

    /// <summary>
    /// Fija la política de TODA la colonia: re-cerebra a las adultas VIVAS
    /// (fundadoras incluidas — en el constructor ya nacieron con el pool) y deja
    /// el hook puesto para cada nacida posterior. Con <paramref name="policy"/>
    /// null desactiva el hook y no toca los cerebros actuales (no se puede
    /// reconstruir un genoma que ya no existe).
    ///
    /// Es el interruptor que convierte una partida en una PRUEBA DE POLÍTICA: el
    /// benchmark de políticas lo usa para congelar scripted/aleatoria/evolucionada
    /// y medir el cerebro en vez de la evolución.
    /// </summary>
    public void ForcePolicy(Brain.IBrain? policy)
    {
        ForcedPolicy = policy;
        if (policy is null) return;
        for (int c = 0; c < _colonies.Count; c++)
        {
            var adults = _colonies[c].Adults;
            for (int i = 0; i < adults.Count; i++)
            {
                var ant = adults[i];
                if (!ant.Alive) continue;
                ant.Genome = null;
                ant.Brain = policy;
            }
        }
    }

    private int DensityScaledTargetItems => (int)MathF.Round(
        TargetItemsDefault * (WorldWidth * WorldHeight) / (96f * SimConstants.CellSizeUnits * 96f * SimConstants.CellSizeUnits));

    public ulong Tick { get; private set; }

    public float WorldWidth { get; }
    public float WorldHeight { get; }

    public IReadOnlyList<Colony> Colonies => _colonies;
    public IReadOnlyList<FoodItem> Items => _items;
    public IReadOnlyList<SimEvent> LastEvents => _events;

    /// <summary>Comandos pendientes de aplicar en el próximo Step (F4.0). La vista
    /// encola; el sim aplica en el punto canónico y los registra en el Canal B.</summary>
    public int PendingCommandCount => _pendingCommands.Count;

    /// <summary>Peticiones de guardado generadas por comandos SaveGame en el último
    /// Step (F4.0). El presenter las consume y escribe con <c>WorldSimSave.Save</c>.
    /// Se vacía al inicio de cada Step — refleja solo el paso actual.</summary>
    public IReadOnlyList<SaveRequest> SaveRequests => _saveRequests;

    /// <summary>Semilla original del mundo (los checkpoints la reproducen).</summary>
    public ulong Seed => _seed;

    /// <summary>Celdas por lado del grid de feromonas.</summary>
    public int GridCells => _gridCells;

    private readonly List<Colony> _colonies = new();
    private readonly List<FoodItem> _items = new();

    public WorldSim(ulong seed, int gridCells = 256, int colonyCount = 2,
        IReadOnlyList<SpeciesDescriptor>? species = null,
        bool cloneFromElite = false,
        float leafFraction = 0f)
    {
        if (gridCells < 16) throw new ArgumentOutOfRangeException(nameof(gridCells));
        if (colonyCount < 1) throw new ArgumentOutOfRangeException(nameof(colonyCount));

        _seed = seed;
        _gridCells = gridCells;
        WorldWidth = gridCells * SimConstants.CellSizeUnits;
        WorldHeight = gridCells * SimConstants.CellSizeUnits;
        _worldRng = new DeterministicRandom(seed);
        // F5.2a.1: ANTES del spawn inicial — el constructor ya crea ítems y
        // necesitan la regla hoja/simple decidida en construcción.
        LeafFraction = leafFraction;

        for (int c = 0; c < colonyCount; c++)
        {
            var sp = species != null && c < species.Count ? species[c] : SpeciesDescriptor.LasiusNiger;
            var colony = CreateColony(c, sp, colonyCount, cloneFromElite);
            _colonies.Add(colony);
        }

        int initialItems = DensityScaledTargetItems;
        for (int i = 0; i < initialItems; i++)
            SpawnItem();
    }

    private Colony CreateColony(int id, SpeciesDescriptor sp, int colonyCount, bool cloneFromElite = false)
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
            // F5.2a.2: el hongo NACE VACÍO (la reina fundadora lo construye —
            // es la economía de la especie, no una reserva inicial).
            Fungus = 0f,
            FungusMax = sp.FungusMax,
            QueenEnergy = 1f,
            Rng = _worldRng.Fork(0x9E3779B97F4A7C15UL + (ulong)id * 0xBF58476D1CE4E5B9UL),
            Pool = new GenomePool(_worldRng.Fork(0xA5C3E7B9UL + (ulong)id * 0x9E3779B9UL), BrainSizes,
                cloneFromElite: cloneFromElite),
            FoodLayer = new PheromoneLayer(_gridCells, _gridCells),
            HomeLayer = new PheromoneLayer(_gridCells, _gridCells),
            AlarmLayer = new PheromoneLayer(_gridCells, _gridCells),
            FootprintLayer = new PheromoneLayer(_gridCells, _gridCells), // F5.3: huella CHC
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
        _saveRequests.Clear();
        Tick++;

        // — Punto canónico de los comandos (F4.0): tras avanzar el tick, antes de
        // que actúe cualquier hormiga. Toda mutación del jugador pasa por aquí;
        // queda registrada en el Canal B (CommandExecuted) para el .antlog, y así
        // "misma semilla + mismos comandos ⇒ mismo mundo" es verificable bit a bit.
        for (int i = 0; i < _pendingCommands.Count; i++)
            ApplyCommand(_pendingCommands[i]);
        _pendingCommands.Clear();

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

                if (ForcedPolicy != null)
                {
                    // Política FIJADA (F5.3bis, benchmark de políticas): la
                    // descendencia hereda el mismo cerebro en vez de un genoma
                    // del pool. Sin Genome ⇒ sin realimentación al pool: la
                    // prueba mide el CEREBRO, no la evolución.
                    ant.Genome = null;
                    ant.Brain = ForcedPolicy;
                    continue;
                }

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

        // — F5.3 rodaja 2: los muertos NO describen el mundo —
        // 1) Fitness de las muertas al BANCO: ApplyDeaths ya pagó al pool en el
        //    tick de la muerte (RecordFitness/CompleteTrial); el banco conserva
        //    la suma TOTAL de aptitud de por vida para que hasher y arena sigan
        //    viendo lo mismo sin cadáveres en la lista.
        // 2) Compactación O(n) al FINAL del tick: todos los consumidores internos
        //    ya actuaron (ActAllAnts, ApplyDeaths, ColonyController, eclosiones)
        //    y los consumidores EXTERNOS (hash, telemetría, save, arena) leen
        //    estado equivalente al de siempre — solo sin muertas.
        // 3) HashLine añade SOLO hormigas vivas: lista compactada y hash
        //    describen el mismo conjunto.
        if (_deadFitnessBank.Length != _colonies.Count)
            _deadFitnessBank = new double[_colonies.Count];
        for (int c = 0; c < _colonies.Count; c++)
            BankAndCompactDeadAnts(_colonies[c], c);

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
                // CHC (F5.3): difunde y evapora como las demás, pero con la vida
                // media larga de la huella de tráfico (240 s) — el rastro de
                // zonas ya peinadas sobrevive a la visita.
                colony.FootprintLayer.Evaporate(dt, PheromoneDefaults.LambdaPerSecond(PheromoneKind.Footprint));
                colony.FootprintLayer.Diffuse(0.06f);
            }
        }

        RespawnItems();

        // — F4.2: extinción por transición de estado (canal B, UNA vez por colonia) —
        // Sin adultas vivas y sin cría la colonia no puede recuperarse: el nido
        // permanece (los eventos de spawn lo usan), pero el relevo ha muerto.
        // (Array perezoso: el camino de carga de checkpoints reconstruye colonias
        // fuera del constructor y comparte el array por tamaño, no por estado.)
        if (_extinctReported == null || _extinctReported.Length != _colonies.Count)
            _extinctReported = new bool[_colonies.Count];
        for (int c = 0; c < _colonies.Count; c++)
        {
            if (_extinctReported[c]) continue;
            var colony = _colonies[c];
            if (colony.AdultCountAlive == 0 && colony.Eggs.Count == 0
                && colony.Larvae.Count == 0 && colony.Pupae.Count == 0)
            {
                _extinctReported[c] = true;
                _events.Add(new SimEvent(SimEventKind.ColonyExtinct, Tick,
                    colony.Id, 0, colony.NestX, colony.NestY));
            }
        }
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

    /// <summary>
    /// F5.3 — Tropotaxis por huella CHC: reflejo periférico, NO una decisión del
    /// cerebro (no hay canal de sensor nuevo, así que ningún genoma del pool
    /// cambia de forma). La diferencia de huella entre las dos antenas empuja el
    /// giro hacia el lado MENOS pisado.
    ///
    /// Es un RATIO, como en la tropotaxis clásica: el desequilibrio lateral va al
    /// numerador y el propio nivel de huella al denominador. De ahí salen las tres
    /// propiedades que buscamos: satura donde el sustrato ya está muy pisado (un
    /// pasillo saturado no empuja sin tope), no empuja nada donde está limpio, y
    /// con huella simétrica da exactamente 0 (una hormiga sobre el filo de un
    /// rastro no gira).
    ///
    /// Es <c>public</c> para poder fijar su álgebra en los tests (simetría, signo
    /// y saturación) con una capa pintada a mano, sin depender de que una partida
    /// entera produzca la geometría deseada.
    /// </summary>
    public static void ApplyFootprintRepulsion(Colony colony, Ant ant, SpeciesDescriptor sp, ref AntDecision decision)
    {
        if (sp.FootprintRepel <= 0f || sp.QMaxFootprint <= 0f) return;

        float reach = sp.SensorReach * ant.SensorScale;
        AntSenses.SampleFootprintSides(colony.FootprintLayer, ant, reach, sp.SenseAngle,
            out float left, out float right);

        float imbalance = right - left;                                  // >0 ⇒ más pisado a la derecha
        float denom = 1f + sp.FootprintSaturation * MathF.Max(left, right);
        decision.Steer += sp.FootprintRepel * imbalance / denom;         // positivo = gira a la izquierda
    }

    // ─── F5.3 rodaja 2: bancar fitness de muertas + compactación O(n) ──────
    // La compactación in-place sobre la lista es O(n) con un solo pass —
    // writeIdx avanza solo para vivas y un RemoveRange trunca el excedente;
    // mucho más rápido que iterar cadáveres tick tras tick cuando mueren
    // muchas hormigas (típico en equilibrio).
    private void BankAndCompactDeadAnts(Colony colony, int colonyIdx)
    {
        var adults = colony.Adults;
        double banked = _deadFitnessBank[colonyIdx];
        int writeIdx = 0;
        int len = adults.Count;
        for (int i = 0; i < len; i++)
        {
            var ant = adults[i];
            if (ant.Alive)
                adults[writeIdx++] = ant;
            else
                banked += ant.Fitness; // aptitud de por vida al banco (el pool ya cobró)
        }
        _deadFitnessBank[colonyIdx] = banked;
        if (writeIdx < len)
            adults.RemoveRange(writeIdx, len - writeIdx);
    }

    /// <summary>
    /// F5.3 rodaja 2: fitness de por vida bancado de las adultas YA compactadas
    /// de la colonia en la posición <paramref name="colonyIdx"/> de la lista.
    /// Con esto, «sumar fitness sobre Adults» sigue dando el total de siempre
    /// (vivas + caídas) aunque los cadáveres ya no estén en la lista.
    /// </summary>
    public double DeadFitnessBank(int colonyIdx) =>
        colonyIdx >= 0 && colonyIdx < _deadFitnessBank.Length ? _deadFitnessBank[colonyIdx] : 0.0;

    private void Act(Colony colony, Ant ant)
    {
        SpeciesDescriptor sp = colony.Species;
        float dt = SimConstants.FixedDtSeconds;

        var sensors = AntSenses.Build(colony, ant, _items, WorldWidth, WorldHeight,
            rivals: sp.ContactRadius > 0f && _colonies.Count > 1 ? _colonies : null);
        var decision = AntDecision.Neutral();
        ant.Brain.Evaluate(in sensors, ref decision);
        // — F5.3: tropotaxis repelente por huella CHC (reflejo periférico) —
        ApplyFootprintRepulsion(colony, ant, sp, ref decision);
        DecisionValidator.SanitizeAndClamp(in decision, out decision);

        // Supervivencia: pequeña recompensa por estar viva cada paso.
        ant.Fitness += RewardSurvivalPerSecond * dt;

        // — Ritmo circadiano (Día / Noche) —
        float sunPhase = sp.DayNightPeriod > 0 ? (float)(Tick % (ulong)sp.DayNightPeriod) / sp.DayNightPeriod * 2f * MathF.PI : 0f;
        float dayLight = 0.5f + 0.5f * CanonMath.Sin(sunPhase); // 1 = mediodía, 0 = medianoche
        float speedCircadian = 1.0f + (sp.DaySpeedMultiplier - 1.0f) * dayLight;
        float metabolismCircadian = sp.NightMetabolismMultiplier + (1.0f - sp.NightMetabolismMultiplier) * dayLight;

        // — Movimiento —
        ant.Heading = WrapPi(ant.Heading + decision.Steer * sp.OmegaMax * dt);
        float loadFactor = ant.HasLoad ? (0.6f + 0.4f * (1f - Math.Min(1f, ant.LoadValue / FoodItem.MaxValue))) : 1f;
        float v = decision.Speed * sp.VMax * ant.SpeedScale * loadFactor * speedCircadian;
        // CanonMath (F5.2c): Cos/Sin cross-platform bit-exact — el movimiento
        // alimenta TODO el estado posterior del mundo.
        ant.X = Math.Clamp(ant.X + CanonMath.Cos(ant.Heading) * v * dt, 0f, WorldWidth);
        ant.Y = Math.Clamp(ant.Y + CanonMath.Sin(ant.Heading) * v * dt, 0f, WorldHeight);
        ant.LifetimeDistanceExplored += v * dt;
        ant.Energy = Math.Max(0f, ant.Energy - (sp.CostMove * v * dt * metabolismCircadian) / ant.EnergyCapacity);

        // — F5.3: huella CHC pasiva — se deposita POR UNIDAD RECORRIDA, no por
        //    segundo y sin gasto de energía: es cutícula que se roza, no una
        //    glándula que se aprieta. Nadie «decide» dejar huella: el tráfico la
        //    deja, y por eso es la señal honesta de qué zonas ya están peinadas.
        if (sp.QMaxFootprint > 0f && v > 0f)
            colony.FootprintLayer.Deposit(Cell(ant.X), Cell(ant.Y), sp.QMaxFootprint * v * dt);

        // — F5.2b.1: combate de incursión (después del movimiento, con la pose
        //    final del tick — la condición es GEOMÉTRICA y determinista: sin
        //    RNG nuevo, el orden de iteración por antId es el que es) —
        TryStrike(colony, ant);

        // — Trofalaxia directa entre obreras de la misma colonia —
        TryDirectTrophallaxis(colony, ant, dt);

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

        // — Recarga de energía / trofalaxia comunal dentro del nido —
        float dxNest = ant.X - colony.NestX;
        float dyNest = ant.Y - colony.NestY;
        if (dxNest * dxNest + dyNest * dyNest <= NestRadius * NestRadius)
        {
            if (ant.Energy < 0.85f && colony.Stock > 0f)
            {
                float deficit = (0.95f - ant.Energy) * ant.EnergyCapacity;
                float feedRate = sp.NestFeedRate * dt;
                float feedAmount = Math.Min(colony.Stock, Math.Min(deficit, feedRate));
                if (feedAmount > 0f)
                {
                    colony.Stock -= feedAmount;
                    ant.Energy = Math.Min(1.0f, ant.Energy + feedAmount / ant.EnergyCapacity);
                }
            }
        }

        // — Interacción (gating físico) —
        ant.InteractCooldown = Math.Max(0f, ant.InteractCooldown - dt);
        // F5.2b.1: el golpe de combate TAMBIÉN vive en el cooldown de
        // interacción (misma ventana de 0.5 s que pickup/unload).
        if (DecisionValidator.WantsInteraction(in decision) && ant.InteractCooldown <= 0f)
        {
            if (!ant.HasLoad)
            {
                var item = NearestItemWithin(ant.X, ant.Y, PickupRadius);
                if (item != null)
                {
                    // F5.2a.1: hoja = ítem compuesto. El pickup corta UN fragmento
                    // (Amount/CutsLeft) y la hoja sobrevive con un corte menos; el
                    // ítem simple se lleva entero como siempre.
                    if (item.IsLeaf)
                    {
                        float fragment = item.Amount / item.CutsLeft;
                        ant.HasLoad = true;
                        ant.LoadValue = fragment;
                        ant.Fitness += RewardPickup;
                        item.CutsLeft--;
                        item.Amount -= fragment;
                        ant.InteractCooldown = 0.5f;
                        _events.Add(new SimEvent(SimEventKind.Pickup, Tick, colony.Id, ant.Id, ant.X, ant.Y));
                        _events.Add(new SimEvent(SimEventKind.LeafCut, Tick, colony.Id, ant.Id, item.X, item.Y));
                        if (item.CutsLeft == 0)
                        {
                            // Último corte: la hoja desaparece (lo que queda es
                            // restos no aprovechables) y el mundo registra consumo.
                            _items.Remove(item);
                            _events.Add(new SimEvent(SimEventKind.ItemConsumed, Tick, colony.Id, ant.Id, item.X, item.Y));
                            _events.Add(new SimEvent(SimEventKind.LeafDepleted, Tick, colony.Id, ant.Id, item.X, item.Y));
                        }
                    }
                    else
                    {
                        ant.HasLoad = true;
                        ant.LoadValue = item.Amount;
                        ant.Fitness += RewardPickup;
                        _items.Remove(item);
                        ant.InteractCooldown = 0.5f;
                        _events.Add(new SimEvent(SimEventKind.Pickup, Tick, colony.Id, ant.Id, ant.X, ant.Y));
                    }
                }
            }
            else
            {
                float dx = ant.X - colony.NestX;
                float dy = ant.Y - colony.NestY;
                if (dx * dx + dy * dy <= NestRadius * NestRadius)
                {
                    ant.Fitness += ant.LoadValue * RewardUnloadPerEp + RewardUnloadBonus;
                    ant.LifetimeFoodGathered += ant.LoadValue;
                    // F5.2a.2: en especies con hongo, la descarga va al hongo
                    // (con merma de procesado) y la digestión alimenta el stock
                    // por otra vía — el relevo y su fitness no cambian.
                    if (colony.FungusMax > 0f)
                    {
                        float procesado = ant.LoadValue * colony.Species.LeafEfficiency;
                        float hueco = colony.FungusMax - colony.Fungus;
                        float aceptado = Math.Min(procesado, hueco); // el excedente se pierde
                        colony.Fungus += aceptado;
                        _events.Add(new SimEvent(SimEventKind.FungusFed, Tick, colony.Id, ant.Id, ant.X, ant.Y));
                    }
                    else
                    {
                        colony.RecordInflow(ant.LoadValue);
                    }
                    colony.InflowAccum += ant.LoadValue;
                    // F5.2b.1: botín de incursión — el ROBO ya ocurrió en el
                    // Strike; aquí solo se registra su llegada (telemetría).
                    if (ant.LoadIsLoot)
                    {
                        _events.Add(new SimEvent(SimEventKind.RaidInflow, Tick, colony.Id, ant.Id, colony.NestX, colony.NestY));
                        ant.LoadIsLoot = false;
                        ant.LootFromColony = 0;
                    }
                    ant.HasLoad = false;
                    ant.LoadValue = 0f;
                    ant.InteractCooldown = 0.5f;
                    _events.Add(new SimEvent(SimEventKind.Unload, Tick, colony.Id, ant.Id, ant.X, ant.Y));
                }
            }
        }
    }

    /// <summary>
    /// F5.2b.1 — golpe de incursión: la hormiga `attacker` (si su especie es
    /// beligerante y no va en cooldown) golpea a la hormiga enemiga más
    /// cercana dentro de ContactRadius. Determinista: sin RNG, la presa es la
    /// de antId MENOR en empate a distancia (orden de iteración como fuente
    /// de azar, mismo criterio que el resto del mundo).
    /// </summary>
    private void TryStrike(Colony attackerColony, Ant attacker)
    {
        SpeciesDescriptor sp = attackerColony.Species;
        if (sp.ContactRadius <= 0f || attacker.InteractCooldown > 0f || attacker.HasLoad)
            return; // especie pacífica · en cooldown · cargando (el botín no combina)

        Ant? prey = null;
        float bestD2 = sp.ContactRadius * sp.ContactRadius;
        for (int c = 0; c < _colonies.Count; c++)
        {
            var victimColony = _colonies[c];
            if (victimColony.Id == attackerColony.Id) continue;
            for (int i = 0; i < victimColony.Adults.Count; i++)
            {
                var victim = victimColony.Adults[i];
                if (!victim.Alive) continue;
                float dx = victim.X - attacker.X;
                float dy = victim.Y - attacker.Y;
                float d2 = dx * dx + dy * dy;
                // antId menor gana los empates (d2 estrictamente menor reemplaza):
                if (d2 < bestD2 || (d2 <= bestD2 + 1e-9f && d2 <= sp.ContactRadius * sp.ContactRadius && prey != null && victim.Id < prey.Id))
                {
                    if (d2 <= bestD2 + 1e-9f && prey != null && victim.Id >= prey.Id) continue;
                    bestD2 = d2;
                    prey = victim;
                }
            }
        }
        if (prey == null) return;

        var preyColony = _colonies[prey.ColonyId];

        // Daño: StrikeDamage en ep de CAPACIDAD de la presa (la energía está
        // normalizada [0,1] por su capacidad — el golpe la baja proporcional).
        float damage = sp.StrikeDamage / Math.Max(prey.EnergyCapacity, 1e-4f);
        prey.Energy = Math.Max(0f, prey.Energy - damage);
        if (prey.Energy <= 0f)
            prey.DiedInCombat = true; // la causa se lee en ApplyDeaths del paso de la presa

        // Robo: ep que el atacante TRANSPORTA como botín (clamp al stock real
        // de la víctima-colonia: no se roba lo que no hay).
        float robido = Math.Min(sp.StealPerStrike, preyColony.Stock);
        preyColony.Stock -= robido;
        if (robido > 0f)
        {
            attacker.HasLoad = true;
            attacker.LoadValue = robido;
            attacker.LoadIsLoot = true;
            attacker.LootFromColony = preyColony.Id;
            _events.Add(new SimEvent(SimEventKind.StockRobbed, Tick, preyColony.Id, 0,
                preyColony.NestX, preyColony.NestY, (byte)Math.Min(255, (int)MathF.Round(robido * 100f))));
        }

        // Alarma ofensiva en la PRESA: su colmena siente el golpe (τ½ 1 s —
        // se disipa en ~3 s). Inyección del MUNDO, no decisión de la hormiga.
        preyColony.AlarmLayer.Deposit(Cell(prey.X), Cell(prey.Y), 0.8f);

        attacker.InteractCooldown = 0.5f;
        attacker.Fitness += RewardPickup * 0.5f; // golpear orienta la evolución (mitad de un pickup)
        _events.Add(new SimEvent(SimEventKind.Strike, Tick, attackerColony.Id, attacker.Id, attacker.X, attacker.Y,
            (byte)Math.Min(255, prey.Id)));
    }

    /// <summary>
    /// Trofalaxia directa: intercambio de alimento líquido boca a boca entre dos
    /// obreras de la misma colonia cuando están en contacto estrecho. La hormiga
    /// saciada (E > 0.70) cede una porción de energía a la hambrienta (E < 0.35).
    /// </summary>
    private void TryDirectTrophallaxis(Colony colony, Ant ant, float dt)
    {
        ant.TrophallaxisCooldown = Math.Max(0f, ant.TrophallaxisCooldown - dt);
        if (ant.TrophallaxisCooldown > 0f || ant.Energy < 0.70f) return;

        SpeciesDescriptor sp = colony.Species;
        float r2 = sp.TrophallaxisRadius * sp.TrophallaxisRadius;
        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var peer = colony.Adults[i];
            if (peer.Id == ant.Id || !peer.Alive || peer.Energy >= 0.35f) continue;

            float dx = peer.X - ant.X;
            float dy = peer.Y - ant.Y;
            if (dx * dx + dy * dy <= r2)
            {
                float transferEp = Math.Min(sp.TrophallaxisRate * dt, (ant.Energy - 0.50f) * ant.EnergyCapacity);
                if (transferEp > 0f)
                {
                    ant.Energy = Math.Max(0.50f, ant.Energy - transferEp / ant.EnergyCapacity);
                    peer.Energy = Math.Min(1.0f, peer.Energy + transferEp / peer.EnergyCapacity);
                    ant.TrophallaxisCooldown = 0.5f;
                    peer.TrophallaxisCooldown = 0.5f;
                    ant.Fitness += 0.05f; // pequeña recompensa evolutiva por altruismo / cooperación comunal
                    break;
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
                // F5.2b.1: la energía la pudo agotar el COMBATE (golpes de una
                // incursión), no el metabolismo — causa telemétrica distinta.
                byte cause = ant.Age >= ant.Lifespan ? (byte)DeathCause.Age
                           : ant.DiedInCombat ? (byte)DeathCause.Combat
                           : (byte)DeathCause.Starvation;

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

    // — Fase 4 (F4.0): comandos de usuario con tick —

    /// <summary>Encola un comando del jugador: se aplica en el punto canónico del
    /// próximo <c>Step()</c> (tras avanzar el tick, antes de que actúe cualquier
    /// hormiga) y se registra en el Canal B con un evento CommandExecuted.</summary>
    public void EnqueueCommand(in SimCommand command) => _pendingCommands.Add(command);

    /// <summary>Punto canónico de aplicación (inmutable — parte del contrato de
    /// determinismo): mismo orden de encolado ⇒ mismo mundo bit a bit.</summary>
    private void ApplyCommand(in SimCommand command)
    {
        switch (command.Kind)
        {
            case SimCommandKind.DropFood:
            {
                var item = new FoodItem
                {
                    Id = _nextItemId++,
                    X = Math.Clamp(command.X, 0f, WorldWidth),
                    Y = Math.Clamp(command.Y, 0f, WorldHeight),
                    Amount = SimCommand.DropFoodAmount
                };
                _items.Add(item);
                _events.Add(new SimEvent(SimEventKind.CommandExecuted, Tick, -1,
                    (uint)command.Kind, item.X, item.Y));
                break;
            }

            case SimCommandKind.SaveGame:
                // Comando de observación: no muta el mundo. Queda en el Canal B
                // (el historial incluye cuándo se guardó) y expone la petición
                // para que el presenter escriba el .antsave tras el Step.
                _events.Add(new SimEvent(SimEventKind.CommandExecuted, Tick, -1,
                    (uint)command.Kind, command.X, command.Y, command.Slot));
                _saveRequests.Add(new SaveRequest(command.Slot, Tick));
                break;
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

            // F5.2a.1: ¿hoja o ítem simple? La tirada SOLO se consume con
            // LeafFraction > 0 — con 0 el flujo de RNG es el de siempre.
            bool isLeaf = LeafFraction > 0f && _worldRng.NextDouble01() < LeafFraction;
            float amount;
            int cuts = 0;
            if (isLeaf)
            {
                cuts = 3 + (int)(_worldRng.NextDouble01() * 3f); // 3..5
                amount = 8f + (float)_worldRng.NextDouble01() * 6f; // 8..14 ep
            }
            else
            {
                amount = 4f + (float)_worldRng.NextDouble01() * 2f;
            }

            var item = new FoodItem
            {
                Id = _nextItemId++,
                X = x,
                Y = y,
                Amount = amount,
                CutsLeft = cuts,
                CutsInitial = cuts
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
            // F5.2a.1: SOLO las hojas aportan cortes al hash — un mundo sin
            // hojas (CutsLeft = 0 en todos los ítems) produce EXACTAMENTE los
            // mismos bytes que el build anterior y los pines de CI no se mueven.
            if (it.IsLeaf)
            {
                h.AppendInt32(it.CutsLeft);
                h.AppendInt32(it.CutsInitial);
            }
        }

        for (int c = 0; c < _colonies.Count; c++)
        {
            var col = _colonies[c];
            h.AppendInt32(col.Id);
            h.AppendFloat(col.Stock);
            // F5.2a.2: el hongo entra en el hash SOLO si la especie lo tiene —
            // colonias sin hongo (FungusMax = 0 ⇒ Fungus = 0) no alteran los bytes.
            if (col.FungusMax > 0f)
            {
                h.AppendFloat(col.Fungus);
                h.AppendFloat(col.FungusMax);
            }
            h.AppendFloat(col.QueenEnergy);
            h.AppendFloat(col.InflowEma); h.AppendFloat(col.ConsumeEma);
            h.AppendFloat(col.EggAccumulator); h.AppendFloat(col.CannibalAccumulator);
            h.AppendInt32(col.Eggs.Count); h.AppendInt32(col.Larvae.Count);
            h.AppendInt32(col.Pupae.Count); h.AppendInt32(col.AdultCountAlive);
            h.AppendUInt64(col.FoodLayer.MutationCount);
            h.AppendUInt64(col.HomeLayer.MutationCount);
            h.AppendUInt64(col.AlarmLayer.MutationCount);
            h.AppendUInt64(col.FootprintLayer.MutationCount); // F5.3: CHC
            h.AppendFloat(col.FoodLayer.SumOfValues());
            h.AppendFloat(col.HomeLayer.SumOfValues());
            h.AppendFloat(col.AlarmLayer.SumOfValues());
            h.AppendFloat(col.FootprintLayer.SumOfValues());

            // Pool genético: estado relevante para la reproducción.
            h.AppendInt32(col.Pool.EliteCount);
            h.AppendInt32(col.Pool.PendingImmigrants);
            for (int i = 0; i < col.Pool.EliteCount; i++)
                h.AppendDouble(col.Pool.Elite[i].Fitness);

            // F5.3 rodaja 2: SOLO hormigas VIVAS describen el mundo — las
            // muertas se compactan al final del Step y su fitness de por vida
            // vive en el banco (el pool lo cobró en el tick de la muerte).
            for (int i = 0; i < col.Adults.Count; i++)
            {
                var a = col.Adults[i];
                if (!a.Alive) continue;
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

    /// <summary>
    /// F5.2c rodaja 7 — siembra de pool NEAT: la MISMA disciplina que la siembra
    /// MLP (élite + fundadoras re-cerebradas) pero las fundadoras portan el
    /// cerebro de GRAFO (clonado por fundadora: cada una su instancia, Genome
    /// queda null — la evolución in-arena es del pool MLP de la colonia, como
    /// en EvaluateNeat).
    /// </summary>
    public void SeedPoolFromNeatGenomes(int colonyId, IReadOnlyList<NeatGenome> genomes)
    {
        var colony = _colonies[colonyId];
        var brains = new NeatBrain[genomes.Count];
        for (int i = 0; i < genomes.Count; i++) brains[i] = genomes[i].ToBrain();

        // La élite MLP de la colonia queda VACÍA (la evolución in-arena no la
        // alimenta — la misma disciplina de EvaluateNeat), pero los nacimientos
        // SÍ necesitan un pool: densos en frío 19-8-6 (el régimen de un mundo
        // recién fundado; Birth() sobre élite vacía lanzaría). Los descendientes
        // nacen salvajes; solo las FUNDADORAS portan el cerebro NEAT.
        int[] coldSizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };
        var cold = new List<MlpGenome>(4);
        for (int i = 0; i < 4; i++)
        {
            int n = MlpBrain.ExpectedWeightCount(coldSizes);
            var w = new float[n];
            for (int k = 0; k < n; k++) w[k] = (float)((_worldRng.NextDouble01() * 2.0 - 1.0) * 0.1);
            cold.Add(new MlpGenome(coldSizes, w));
        }
        colony.Pool.ReplaceElite(cold);

        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var ant = colony.Adults[i];
            if (!ant.Alive) continue;
            ant.Genome = null;
            ant.Brain = brains[i % brains.Length];
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
            FungusMax = sp.FungusMax, // F5.2a.2: capacidad siempre de la especie
            Rng = rng,
            Pool = new GenomePool(rng.Fork(0xA5C3E7B9UL), BrainSizes, seedCount: 0),
            FoodLayer = new PheromoneLayer(_gridCells, _gridCells),
            HomeLayer = new PheromoneLayer(_gridCells, _gridCells),
            AlarmLayer = new PheromoneLayer(_gridCells, _gridCells),
            FootprintLayer = new PheromoneLayer(_gridCells, _gridCells) // F5.3: huella CHC
        };
        _colonies.Add(colony);
        return colony;
    }
}