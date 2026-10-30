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
    private int[][]? _nestGuide;                               // F8: campo de distancia al nido por pasillos
    private readonly Dictionary<uint, (int dist, int ticks, int cx, int cy)> _guideStall = new(); // F8: la vuelta no avanza
    private readonly Dictionary<uint, (int ticks, int cx, int cy)> _homingOff = new(); // F8: tregua del reflejo tras atascarse
    private bool[]? _extinctReported; // F4.2: ColonyExtinct una vez por colonia (lazy: el load reconstruye colonias)
    private uint _nextAntId = 1;
    private uint _nextItemId = 1;
    private readonly int _gridCells;

    /// <summary>Topología por defecto del cerebro (contrato v1: 19·8·6). Es la
    /// que usan los pines de hash y todos los pools históricos.</summary>
    public static readonly int[] DefaultBrainSizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private readonly int[] _brainSizes;

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

    /// <summary>
    /// F8 — ¿no queda NINGUNA hormiga ni cría en ninguna colonia? Es la
    /// condición con la que se cierra una ronda: el CLI para antes del horizonte
    /// (con <c>--until-extinct</c>) y exporta la élite evolucionada, y el juego
    /// abre la siguiente. Un mundo SIN colonias no está extinto: no hay ronda
    /// que cerrar ni élite que heredar, y devolver true ahí encadenaría rondas
    /// vacías en bucle.
    /// </summary>
    public bool AllColoniesExtinct()
    {
        if (_colonies.Count == 0) return false;
        for (int i = 0; i < _colonies.Count; i++)
        {
            var c = _colonies[i];
            if (c.AdultCountAlive > 0 || c.Eggs.Count > 0 || c.Larvae.Count > 0 || c.Pupae.Count > 0)
                return false;
        }
        return true;
    }

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

    /// <summary>Lado del bloque del LOD de difusión con el que se construyeron las
    /// capas de feromonas de este mundo (F5.3 rodaja 3bis). Es un parámetro de
    /// construcción del mundo porque el bloque se fija en cada capa al crearla;
    /// cambiarlo NO cambia ningún valor (el LOD es exacto), solo el trabajo.</summary>
    public int LodBlockSize { get; }

    private readonly List<Colony> _colonies = new();
    private readonly List<FoodItem> _items = new();
    private readonly MazeLayout? _maze;

    public WorldSim(ulong seed, int gridCells = 256, int colonyCount = 2,
        IReadOnlyList<SpeciesDescriptor>? species = null,
        bool cloneFromElite = false,
        float leafFraction = 0f,
        int lodBlockSize = PheromoneLayer.DefaultLodBlockSize,
        bool maze = false,
        MazeLayout? mazeLayout = null,
        int[]? brainSizes = null,
        bool recruitment = false,
        RecruitmentTuning? recruitmentTuning = null)
    {
        if (gridCells < 16) throw new ArgumentOutOfRangeException(nameof(gridCells));
        if (colonyCount < 1) throw new ArgumentOutOfRangeException(nameof(colonyCount));
        if (lodBlockSize < 1) throw new ArgumentOutOfRangeException(nameof(lodBlockSize));
        // F7: el cerebro puede ser más ancho que el contrato clásico cuando la
        // partida se siembra con un pool entrenado (19→16→6, 19→24→6). La
        // entrada (19 sensores) y la salida (6 decisiones) son fijas.
        _brainSizes = brainSizes is null ? (int[])DefaultBrainSizes.Clone() : (int[])brainSizes.Clone();
        if (_brainSizes.Length < 2 || _brainSizes[0] != AntSensorChannelInfo.Count
            || _brainSizes[^1] != AntDecision.DecisionCount)
            throw new ArgumentException(
                $"Topología de cerebro inválida: {string.Join("→", _brainSizes)} (se exige {AntSensorChannelInfo.Count} entradas y {AntDecision.DecisionCount} salidas).",
                nameof(brainSizes));

        _seed = seed;
        _gridCells = gridCells;
        Recruitment = recruitment;
        LodBlockSize = lodBlockSize;
        // F8: las perillas de montones/rastro (lanzador y CLI) se aplican ANTES
        // de sembrar — el constructor crea los montones más abajo, así que
        // asignarlas después no cambiaría nada del mundo ya nacido.
        recruitmentTuning?.ApplyTo(this);
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

        // — F6: terreno con obstáculos —
        // Se construye DESPUÉS de las colonias (necesita sus nidos para despejar
        // las plazas) y ANTES del spawn inicial (ningún ítem dentro de una
        // pared). Sin laberinto, `_maze` queda null y el mundo es el de siempre.
        _maze = mazeLayout ?? (maze ? MazeLayout.Braided(seed, gridCells, colonyCount) : null);
        // Con terreno, el objetivo de comida sube: las paredes recortan el área
        // útil y alargan cada viaje (ver MazeLayout.ItemDensityFactor).
        if (_maze != null) TargetItems = Math.Max(1, (int)MathF.Round(DensityScaledTargetItems * MazeLayout.ItemDensityFactor));

        // La siembra clásica es la MISMA con y sin reclutamiento: comerse el
        // mundo a ítems sueltos no es lo que se pide al encender la bandera, y
        // conservarla deja el flujo del RNG intacto (los mundos clásicos y sus
        // pines de hash no cambian).
        int initialItems = _maze != null ? TargetItems : DensityScaledTargetItems;
        for (int i = 0; i < initialItems; i++)
            SpawnItem();

        if (Recruitment)
        {
            // F8: ADEMÁS de lo aleatorio, el mundo siembra CÚMULOS —montones que
            // una hormiga puede anunciar como «aquí hay comida» y que se ven en
            // el tablero como un punto caliente del rastro de comida. El guard es
            // por si el terreno rechazara centros: nunca un bucle sin salida.
            int pileTarget = FoodClusterCount * Math.Max(1, _colonies.Count);
            int piles = 0;
            for (int guard = 0; guard < 256 && piles < pileTarget; guard++)
                if (SpawnCluster(piles % Math.Max(1, _colonies.Count))) piles++;
            // Nivel al que se REPONEN los montones (ver RespawnItems): el total que
            // dejó la siembra inicial, no solo la cuota de ítems sueltos.
            _pileFloor = _items.Count;
        }
    }

    /// <summary>Laberinto activo del mundo (null = terreno clásico sin obstáculos).</summary>
    public MazeLayout? Maze => _maze;

    /// <summary>
    /// F8 — RECLUTAMIENTO POR RASTRO. Con esto encendido el mundo cambia en dos
    /// cosas, y solo en la partida (los mundos clásicos, los pines de hash y los
    /// entrenamientos siguen byte a byte iguales):
    ///
    ///   · la comida se siembra en CÚMULOS (ver <see cref="SpawnCluster"/>) RICOS
    ///     y AL ALCANCE — es lo que da algo que anunciar, y medido es lo que más
    ///     mueve la aguja; un ítem suelto o un montón al otro lado del mapa no
    ///     sostienen un rastro porque no lo visita nadie;
    ///   · las hormigas tienen dos REFLEJOS innatos (no salidas del cerebro): la
    ///     que vuelve cargada deposita rastro de comida (hacia el montón) y de
    ///     casa (hacia el nido), y la que busca sin carga gira hacia el lado con
    ///     más rastro de comida (<see cref="ApplyTrailRecruitment"/>). Con
    ///     <see cref="RecruitmentTrailByBrain"/> esos dos depósitos los abre o
    ///     cierra el cerebro en vez de ser constantes (F11).
    ///
    /// Es el mismo patrón que la tropotaxis de huella (F5.3): un reflejo
    /// periférico que el cerebro puede reforzar o corregir, no una decisión
    /// cableada. Con la bandera apagada no se toca ni un flotante.
    /// </summary>
    public bool Recruitment { get; }

    /// <summary>Rastro de comida que deja una hormiga CARGADA, como fracción de
    /// QMaxFood por segundo (F8).</summary>
    public float RecruitmentFoodDeposit { get; set; } = 1f;

    /// <summary>Rastro de casa que deja la que vuelve cargada (F8).</summary>
    public float RecruitmentHomeDeposit { get; set; } = 0.25f;

    /// <summary>
    /// F11 — ¿EL RASTRO LO ABRE EL CEREBRO? Con esto encendido —y solo junto a
    /// <see cref="Recruitment"/>— los dos depósitos del reflejo dejan de ser
    /// constantes y se escalan por la salida del cerebro que les toca: la
    /// hormiga CARGADA deja rastro de comida en proporción a
    /// <c>DepositFood</c>, y la que BUSCA sin carga deja rastro de casa en
    /// proporción a <c>DepositHome</c>.
    ///
    /// La salida a 1 reproduce el reflejo fijo de F8; a 0 la hormiga no deja
    /// nada por su cuenta (queda lo que el cerebro deposite por los canales
    /// genéricos, que también es suyo). Con esto apagado —el default— el bloque
    /// de F8 no gasta ni una multiplicación de más y el mundo es byte a byte el
    /// de siempre.
    /// </summary>
    public bool RecruitmentTrailByBrain { get; set; }

    /// <summary>
    /// F11b — EL PRECIO DEL RASTRO: segundos de depósito a plena tasa que caben
    /// en la glándula de una hormiga (0 = sin precio, el default; el mundo es
    /// byte a byte el de F8).
    ///
    /// Con precio, cada gota de feromona de la colonia sale de la reserva
    /// personal de quien la deja (<see cref="Ant.Gland"/>) y la reserva solo se
    /// rellena dentro del nido: el depósito deja de ser gratis y pasa a ser un
    /// PRESUPUESTO. Ahí está la decisión que F11 no tenía: la hormiga que vuelve
    /// cargada no puede dejar el rastro entero sin gastarlo todo en los primeros
    /// metros —lo que pone el camino pegado a la fuente y deja el corredor
    /// vacío—; si quiere que el rastro llegue lejos tiene que RACIONARLO. Sin
    /// precio, la salida del cerebro solo podía adelgazar el rastro y F11 midió
    /// que no paga.
    ///
    /// Se cobra la feromona de COMIDA y de CASA (los canales genéricos y los dos
    /// del reclutamiento); la de alarma y la huella CHC no salen de esta
    /// glándula. El gating es por capa: <c>amt / QMax</c> es el tiempo consumido,
    /// así que un depósito a media tasa cuesta la mitad.
    /// </summary>
    public float RecruitmentGlandSeconds { get; set; }

    /// <summary>F11b — segundos que tarda la glándula en pasar de vacía a llena
    /// dentro del nido. Es lo que hace que el precio duele pero no ahogue: la
    /// hormiga que descarga puede volver a marcar el camino.</summary>
    public float RecruitmentGlandRefillSeconds { get; set; } = 3f;

    /// <summary>F11k — CUÁNTO RASTRO SE DEPOSITÓ, acumulado en toda la partida y
    /// en todas las colonias (capas de comida y de casa, sumando el reflejo de
    /// reclutamiento y el canal genérico del cerebro). No entra en el hash ni en
    /// el guardado: es un contador de instrumentación, para responder «cuánto
    /// rastro deja cada brazo» sin leer la capa al final —que ya evaporó—.
    /// Con <see cref="Recruitment"/> apagado se queda en 0.</summary>
    public double TrailFoodDeposited { get; private set; }

    /// <inheritdoc cref="TrailFoodDeposited"/>
    public double TrailHomeDeposited { get; private set; }

    /// <summary>F11k — lo que la glándula NO pudo pagar: cantidad pedida por los
    /// depósitos que se quedaron sin presupuesto. Medido junto a
    /// <see cref="TrailGlandSpentSeconds"/> dice si el precio es el que recorta el
    /// rastro o si cada brazo deposita menos porque su puerta está más cerrada.</summary>
    public double TrailGlandDenied { get; private set; }

    /// <summary>F11k — segundos de glándula consumidos (a plena tasa), sumados en
    /// toda la partida. Es el presupuesto que el rastro gastó de verdad.</summary>
    public double TrailGlandSpentSeconds { get; private set; }

    /// <summary>Ganancia del reflejo que sigue el rastro de comida, en rad/s a
    /// plena asimetría (F8). Del orden de la huella (8): mueve la conducta.
    /// Calibrado contra el rastro real de la partida (12 % de las hormigas sin
    /// carga lo tienen al alcance de las antenas, frente al 2 % con el rastro
    /// estrecho de antes): con 0 el mundo rinde ~331 descargas en 8 semillas, con
    /// 8 rinde ~371 — el rastro aporta, pero quien más aporta es que los montones
    /// estén al alcance (ver <see cref="FoodClusterMinDistance"/>).</summary>
    public float FoodTrailAttract { get; set; } = 8f;

    /// <summary>Término que convierte el gradiente de comida en un RATIO (F8):
    /// satura donde el rastro ya es denso en vez de crecer sin tope.</summary>
    public float FoodTrailSaturation { get; set; } = 4f;

    /// <summary>Energía (fracción de la capacidad) por debajo de la cual la
    /// hormiga vuelve al nido a recargar (<see cref="ApplyReturnWhenTired"/>).
    /// 0,40 deja ~160 s de caminata para volver desde cualquier punto que
    /// alcance en una salida (el gasto medido es ≈0,0025 de la reserva por
    /// segundo) — margen de sobra para cruzar el mapa, y suficiente para que
    /// recargar sea rentable frente a morir en el campo. 0 = sin reflejo.</summary>
    public float TiredEnergy { get; set; } = 0.40f;

    /// <summary>Ticks sin bajar de celda en el campo antes de darse la vuelta
    /// (150 = 5 s). Ver <see cref="BreakGuideStall"/>.</summary>
    public int GuideStallTicks { get; set; } = 150;

    /// <summary>Ticks de tregua del reflejo de vuelta tras un atasco (90 = 3 s).</summary>
    public int HomingTruceTicks { get; set; } = 90;

    /// <summary>Giro (rad) que da la hormiga al chocar con una pared del
    /// laberinto (F6): la pared no se atraviesa, la hormiga se apoya y gira. Se
    /// aplica cuando el paso real es menor que <see cref="MazeBumpMinProgress"/>
    /// de lo que pretendía — la que empuja de frente avanza milésimas por el eje
    /// libre y sin esto no se distingue de la que camina. 0 = sin rebote.</summary>
    public float MazeBumpTurn { get; set; } = 0.5f;

    /// <summary>Fracción del paso pretendido por debajo de la cual se considera
    /// que la hormiga se apoyó en una pared y toca girar (ver
    /// <see cref="MazeBumpTurn"/>). 0,25 descarta los deslizamientos reales
    /// (a 45° se avanza ~0,7 del paso) y caza el empuje perpendicular.</summary>
    public float MazeBumpMinProgress { get; set; } = 0.25f;

    /// <summary>Ganancia del reflejo que DEVUELVE al nido a la hormiga CARGADA
    /// (F8), en unidades de <c>Steer</c> por unidad de desvío lateral (el seno
    /// del ángulo entre el rumbo y el nido). Se suma al giro del cerebro: si el
    /// nido está de frente el reflejo vale 0 (el cerebro manda para esquivar
    /// paredes) y si está de lado vale la ganancia entera.
    ///
    /// Sin él, la cargada se queda con el bocado lejos del nido —medido: 3 de 44
    /// cargadas llegaban a 40 u del nido y la colonia vivía de la reserva
    /// fundadora hasta extinguirse a los ~1180 s con 150 ítems en el tablero—.
    /// 4 satura el giro a partir de ~15° de desvío: la vuelta es una recta.
    /// 0 = el reflejo no existe (control A/B).</summary>
    public float LoadHoming { get; set; } = 4f;

    /// <summary>Vida media (s) del rastro de comida con reclutamiento encendido
    /// (F8). Con la clásica (10 s) el camino se borraba antes de que llegara la
    /// siguiente hormiga; 300 s lo mantiene mientras la colonia lo usa y 2 100 s
    /// —siete veces más— lo deja legible durante toda la ronda: el camino que
    /// pisa una obrera sigue ahí cuando la colonia vuelve al montón minutos
    /// después, en vez de desvanecerse entre visita y visita.</summary>
    public float RecruitmentFoodHalfLife { get; set; } = 2100f;

    /// <summary>Factor de vida (F8) que se aplica en la ronda a las capas que
    /// NO tienen perilla propia (casa, alarma y huella): 7× su vida media
    /// clásica. El rastro de comida ya lleva su propia vida media
    /// (<see cref="RecruitmentFoodHalfLife"/>, también 7× la clásica).</summary>
    public const float RecruitmentPheromoneLifeScale = 7f;

    /// <summary>Distancia al nido (u) a la que el rastro de comida alcanza su
    /// intensidad plena (F8). Por debajo, la hormiga cargada deposita MENOS: el
    /// rastro marca la FUENTE y no el tráfico. Es lo que hace legible la cresta:
    /// con depósito uniforme el campo es más denso en el nido (todas las cargas
    /// convergen allí) y el que sube el gradiente vuelve a casa, no al montón. El
    /// suelo de 0,4 deja oler la boca del nido, que es por donde se sale.</summary>
    public float RecruitmentTrailSourceDistance { get; set; } = 240f;

    /// <summary>Montones que se siembran ADEMÁS de la comida aleatoria (F8), POR
    /// COLONIA: el mundo reparte la misma oferta a cada nido en vez de partir una
    /// sola cuota entre todos. Con el reparto global, dos colonias se quedaban con
    /// la mitad de montones cada una y la ronda de pareja moría de hambre teniendo
    /// el mapa lleno (medido: c0 extinta en 46 200 ticks de 216 000 con los mismos
    /// montones que sostienen una colonia sola hasta el horizonte).</summary>
    public int FoodClusterCount { get; set; } = 8;

    /// <summary>Ítems por montón (F8).</summary>
    public int FoodClusterMin { get; set; } = 6;
    public int FoodClusterMax { get; set; } = 10;

    /// <summary>Valor (ep) de cada trozo de un montón (F8). Un parche rico da
    /// bocados grandes: 10 ep es el tope del contrato (<see cref="FoodItem.MaxValue"/>)
    /// y el doble de una migaja suelta (4–6). Es lo que hace que el viaje al
    /// montón pague el coste de mantener a la obrera.</summary>
    public float FoodClusterItemAmount { get; set; } = 10f;

    /// <summary>Total de comida que dejó la siembra inicial con montones (F8):
    /// por debajo de este nivel se repone UN montón por paso.</summary>
    private int _pileFloor;

    /// <summary>Índice de la colonia que recibe el próximo montón repuesto: los
    /// montones se reparten POR COLONIA y en rueda, así que ninguna se queda sin
    /// parche mientras la otra acumula.</summary>
    private int _pileSeed;

    /// <summary>Radio del montón en unidades (F8). A 18 u el grueso de un montón
    /// cabe en 2–3 celdas de feromona, así que el rastro nace como un punto
    /// caliente y no como una mancha informe.</summary>
    public float FoodClusterRadius { get; set; } = 18f;

    /// <summary>Banda de distancia al nido (u) donde caen los montones (F8):
    /// dentro del radio que la colonia patrulla de verdad. Antes era 100–220 u y
    /// las obreras patrullan ~100 u (solo un 11 % pasa de 200): los montones
    /// quedaban fuera del alcance real y la colonia se moría de hambre con más de
    /// 1 000 ep sin recoger en el tablero. Con 40–120 u el viaje cabe en una salida
    /// y la ronda de dos colonias llega al horizonte en las cuatro semillas
    /// medidas (docs/fase6-terreno.md §5).</summary>
    public float FoodClusterMinDistance { get; set; } = 40f;
    public float FoodClusterMaxDistance { get; set; } = 120f;

    /// <summary>Topología del cerebro de esta partida (copia defensiva).</summary>
    public int[] BrainSizes => (int[])_brainSizes.Clone();

    private Colony CreateColony(int id, SpeciesDescriptor sp, int colonyCount, bool cloneFromElite = false)
    {
        var colony = new Colony
        {
            Id = id,
            Species = sp,
            RecruitmentEconomy = Recruitment, // F8: contabilidad de la ronda
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
            Pool = new GenomePool(_worldRng.Fork(0xA5C3E7B9UL + (ulong)id * 0x9E3779B9UL), _brainSizes,
                cloneFromElite: cloneFromElite),
            FoodLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize),
            HomeLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize),
            AlarmLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize),
            FootprintLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize), // F5.3: huella CHC
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
                // F8: con reclutamiento el rastro de comida tiene que SOBREVIVIR AL
                // VIAJE (nido → montón → nido son ~1–2 min en 96²). Con la vida
                // media clásica (10 s) el camino se borraba entre visita y visita:
                // cada hormiga dejaba su tramo, pero nadie encontraba el tramo del
                // anterior y el rastro nunca se volvía un camino. Los mundos
                // clásicos conservan su λ exacta y su difusión (pines de hash
                // intactos).
                //
                // En la RONDA el rastro es una HUELLA, no humo: se apaga la
                // difusión (k = 0). La difusión era la que ensanchaba el depósito
                // de una celda hasta una mancha de decenas de unidades —más ancha
                // que la hormiga— y le daba esa cola de niebla alrededor del
                // camino. Sin ella la celda depositada se queda en su celda (8 u,
                // menos que el largo de una obrera) y el camino es exactamente lo
                // que se ha pisado. La vida media, en cambio, sube 7× en todas las
                // capas: el rastro se borra por tiempo, no por difusión.
                float life = Recruitment ? RecruitmentPheromoneLifeScale : 1f;
                float k = Recruitment ? 0f : 0.10f;
                float foodLambda = Recruitment
                    ? PheromoneDefaults.LambdaFromHalfLife(RecruitmentFoodHalfLife)
                    : PheromoneDefaults.LambdaPerSecond(PheromoneKind.FoodTrail);
                colony.FoodLayer.Evaporate(dt, foodLambda);
                colony.FoodLayer.Diffuse(k);
                colony.HomeLayer.Evaporate(dt, PheromoneDefaults.LambdaFromHalfLife(
                    PheromoneDefaults.HalfLifeSeconds(PheromoneKind.Home) * life));
                colony.HomeLayer.Diffuse(k);
                colony.AlarmLayer.Evaporate(dt, PheromoneDefaults.LambdaFromHalfLife(
                    PheromoneDefaults.HalfLifeSeconds(PheromoneKind.Alarm) * life));
                colony.AlarmLayer.Diffuse(k);
                // CHC (F5.3): la huella de tráfico ya es de vida larga (240 s); en
                // la ronda va a 1 680 s y tampoco se difunde.
                colony.FootprintLayer.Evaporate(dt, PheromoneDefaults.LambdaFromHalfLife(
                    PheromoneDefaults.HalfLifeSeconds(PheromoneKind.Footprint) * life));
                colony.FootprintLayer.Diffuse(Recruitment ? 0f : 0.06f);
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
    /// F8 — atracción por el rastro de comida (reclutamiento). La sonda es la
    /// MISMA de las antenas que ya lee el cerebro: si el lado derecho huele más
    /// comida, la hormiga gira a la derecha (signo negativo del contrato de
    /// giro); el denominador satura donde el rastro es denso, como la huella.
    /// Solo actúa la que busca (sin carga): la que vuelve ya lleva su camino.
    ///
    /// Es <c>public</c>, igual que la huella, para fijar su álgebra en los tests
    /// con una capa pintada a mano (simetría, signo y saturación) sin depender
    /// de que una partida entera produzca la geometría deseada.
    /// </summary>
    public void ApplyTrailRecruitment(Colony colony, Ant ant, SpeciesDescriptor sp, ref AntDecision decision)
    {
        if (!Recruitment || ant.HasLoad) return;

        float reach = sp.SensorReach * ant.SensorScale;
        AntSenses.SampleSides(colony.FoodLayer, ant, reach, sp.SenseAngle, out float left, out float right);

        float imbalance = right - left;                                  // >0 ⇒ más rastro a la derecha
        float denom = 1f + FoodTrailSaturation * MathF.Max(left, right);
        decision.Steer -= FoodTrailAttract * imbalance / denom;          // negativo = gira a la derecha
    }

    /// <summary>
    /// F8 — VUELTA DE LA CARGADA: la hormiga que lleva comida vuelve al nido con
    /// una corrección LATERAL (el seno del ángulo al objetivo), no con un rumbo
    /// impuesto. Apuntando al objetivo vale 0 y el cerebro queda libre para
    /// esquivar; de lado vale <see cref="LoadHoming"/> y el giro satura hacia
    /// casa. Con el objetivo DETRÁS el lado no está definido por el seno, así que
    /// se gira por el lado más corto (empate a +, determinista).
    ///
    /// Con LABERINTO el objetivo no es el nido en línea recta (la pared está en
    /// medio) sino la siguiente celda del CAMPO DE DISTANCIA por pasillos que se
    /// construye en <see cref="NestGuides"/>: el reflejo sigue el descenso del
    /// campo celda a celda, que es lo que hace que la vuelta rodee el muro en vez
    /// de empujarlo. Sin terreno, el objetivo es el nido.
    ///
    /// Por qué hace falta: la política entrenada no aprendió a volver al nido a
    /// la escala de la partida. Medido en la partida canónica con reclutamiento:
    /// de 44 hormigas que cogieron comida solo 3 se acercaron a 40 u del nido
    /// (distancia mediana de las cargadas: 226 u) y la colonia se apagaba a los
    /// ~1180 s con 150 ítems sin tocar. Vive bajo la bandera F8: la partida
    /// clásica no mueve un flotante.
    /// </summary>
    public void ApplyLoadHoming(Colony colony, Ant ant, ref AntDecision decision)
    {
        if (!Recruitment || !ant.HasLoad || LoadHoming <= 0f) return;
        if (_homingOff.Count > 0 && _homingOff.ContainsKey(ant.Id)) return; // tregua: la vuelta no progresaba
        SteerHome(colony, ant, LoadHoming, ref decision);
    }

    /// <summary>
    /// F8 — VUELTA A RECARGAR: la hormiga que se queda sin reserva vuelve al
    /// nido, como la obrera real que vacía el buche. Sin esto, la que NO
    /// encuentra comida se queda en el campo hasta morir de hambre: medido en la
    /// partida canónica, el gasto de caminar y depositar vacía la reserva en
    /// ~400 s, así que la vida útil de la que no vuelve es la mitad de su
    /// longevidad (720 s) y la colonia paga su mantenimiento para nada. Con el
    /// reflejo vuelve, recarga en el nido (<see cref="NestFeedRate"/>) y sale otra
    /// vez: es lo que cierra el ciclo de la energía, no solo el de la comida.
    ///
    /// Umbral en <see cref="TiredEnergy"/>; mismos guiado y ganancia que la
    /// cargada. Vive bajo la bandera F8: la partida clásica no mueve un flotante.
    /// </summary>
    public void ApplyReturnWhenTired(Colony colony, Ant ant, ref AntDecision decision)
    {
        if (!Recruitment || ant.HasLoad || LoadHoming <= 0f) return;
        if (ant.Energy >= TiredEnergy) return;
        if (_homingOff.Count > 0 && _homingOff.ContainsKey(ant.Id)) return; // tregua: la vuelta no progresaba
        SteerHome(colony, ant, LoadHoming, ref decision);
    }

    /// <summary>Corrección lateral hacia el nido (campo de pasillos si hay
    /// laberinto, línea recta si no) compartida por la cargada y la cansada.</summary>
    private void SteerHome(Colony colony, Ant ant, float gain, ref AntDecision decision)
    {
        float tx = colony.NestX, ty = colony.NestY;
        GuideStep(colony, ant, ref tx, ref ty);

        float dx = tx - ant.X;
        float dy = ty - ant.Y;
        float d2 = dx * dx + dy * dy;
        if (d2 <= 1e-6f) return; // encima del objetivo: no hay rumbo que corregir

        float inv = 1f / MathF.Sqrt(d2);
        float cosH = CanonMath.Cos(ant.Heading);
        float sinH = CanonMath.Sin(ant.Heading);
        float forward = (dx * cosH + dy * sinH) * inv;  // >0 ⇒ el objetivo está delante
        float left = (-dx * sinH + dy * cosH) * inv;    // >0 ⇒ el objetivo está a la izquierda

        // RUMBO, no «de qué lado». `left` vale ±1 cuando el objetivo está a un
        // lado, así que con gain 4 el reflejo SATURABA con solo 15° de desvío:
        // la hormiga giraba a tope, se pasaba del rumbo y el muro la devolvía
        // —medido: la cargada 13 recorre 13 u de pared arriba y abajo durante
        // 1 800 ticks con el objetivo a 8 u—. El ángulo relativo (atan2) da la
        // corrección proporcional al desvío real: se apaga al quedar encarada y
        // satura solo cuando el objetivo está de verdad a la espalda.
        float side = forward < 0f ? (left >= 0f ? 1f : -1f) : left;
        decision.Steer += gain * side;
    }

    /// <summary>
    /// F8 — EL NIDO NO SE VE A TRAVÉS DE LA PARED. Con laberinto, la brújula
    /// innata del cerebro (canales <c>HomeDx</c>/<c>HomeDy</c>) dejaba de ser el
    /// vector en línea recta al nido y pasa a ser la dirección del PASILLO: el
    /// centro de la celda vecina que baja el campo de distancia del nido. Es el
    /// mismo campo que usa la vuelta de la cargada, así que la hormiga que
    /// vuelve —cargada o cansada— deja de empujar el muro que tiene el nido
    /// detrás y rodea el obstáculo.
    ///
    /// Sin laberinto no toca nada (brújula clásica, pines de hash intactos) y una
    /// celda no conectada deja la brújula a CERO en vez de mentir con una recta
    /// que cruza paredes: mejor no saber dónde está la casa que creer que está
    /// al otro lado del muro.
    ///
    /// Es <c>public</c>, como los demás reflejos, para poder fijar en un test que
    /// la dirección que sale NO cruza una pared, celda por celda del laberinto.
    /// </summary>
    public void ApplyCorridorHomeSense(Colony colony, Ant ant, ref AntSensors sensors)
    {
        if (!Recruitment || _maze == null) return;
        var guides = NestGuides();
        if (colony.Id < 0 || colony.Id >= guides.Length) return;
        int[] dist = guides[colony.Id];
        if (dist.Length == 0) return;

        int n = _gridCells;
        int cx = Cell(ant.X), cy = Cell(ant.Y);
        int here = dist[cy * n + cx];
        if (here == int.MaxValue)
        {
            // Celda no conectada al nido (o dentro de una pared): no hay pasillo
            // que enseñar. La brújula se apaga; la pared sigue siendo opaca.
            sensors.HomeDx = 0f;
            sensors.HomeDy = 0f;
            return;
        }

        int best = here, bx = cx, by = cy;
        if (cx > 0 && dist[cy * n + cx - 1] < best) { best = dist[cy * n + cx - 1]; bx = cx - 1; by = cy; }
        if (cx + 1 < n && dist[cy * n + cx + 1] < best) { best = dist[cy * n + cx + 1]; bx = cx + 1; by = cy; }
        if (cy > 0 && dist[(cy - 1) * n + cx] < best) { best = dist[(cy - 1) * n + cx]; bx = cx; by = cy - 1; }
        if (cy + 1 < n && dist[(cy + 1) * n + cx] < best) { best = dist[(cy + 1) * n + cx]; bx = cx; by = cy + 1; }
        if (best == here)
        {
            // Ya en la celda del nido: casa está aquí, no hay rumbo que dar.
            sensors.HomeDx = 0f;
            sensors.HomeDy = 0f;
            return;
        }

        float cell = SimConstants.CellSizeUnits;
        float dx = (bx + 0.5f) * cell - ant.X;
        float dy = (by + 0.5f) * cell - ant.Y;
        float d2 = dx * dx + dy * dy;
        if (d2 <= 1e-6f) return;

        // Mismo marco local que AntSenses: X = rumbo, Y = izquierda.
        float inv = 1f / MathF.Sqrt(d2);
        float cosH = CanonMath.Cos(ant.Heading);
        float sinH = CanonMath.Sin(ant.Heading);
        sensors.HomeDx = Math.Clamp((dx * cosH + dy * sinH) * inv, -1f, 1f);
        sensors.HomeDy = Math.Clamp((-dx * sinH + dy * cosH) * inv, -1f, 1f);
    }

    /// <summary>
    /// F8 — SI LA VUELTA NO AVANZA, LA HORMIGA SALE DEL RINCÓN POR UNA CELDA LIBRE.
    /// El campo de pasillos baja celda a celda, pero en una esquina el objetivo cae
    /// en el borde del hueco: la hormiga se apoya en el muro y se queda orbitando
    /// por su cara con el nido al otro lado (medido: la cargada 13 recorre 13 u de
    /// pared arriba y abajo durante 1 800 ticks).
    ///
    /// El primer intento —media vuelta a ciegas— no bastaba: el giro de 180° la
    /// devolvía al mismo pasillo y la tregua de 3 s se agotaba ANTES de que la
    /// hormiga cambiara de celda, así que el reflejo volvía a apuntar al mismo
    /// rincón y el ciclo se repetía indefinidamente. Medido en la partida viva:
    /// tres cargadas (124, 147, 167) seguían deslizándose por la cara del muro con
    /// el nido detrás, con deriva < 1,4 u en ventanas de 500 s.
    ///
    /// Ahora el desatasco ESCOGE DIRECCIÓN: de las celdas vecinas libres (las
    /// cuatro ortogonales del campo, sin diagonales que corten esquinas) toma la
    /// que menos aleja del nido, y la tregua no se consume por tiempo sino al
    /// CAMBIAR DE CELDA — el reflejo no puede volver a engancharla en el mismo
    /// sitio. Determinista (recorre los vecinos en el orden fijo del BFS y no usa
    /// el RNG) y solo existe con reclutamiento y laberinto.
    /// </summary>
    private void BreakGuideStall(Colony colony, Ant ant)
    {
        if (!Recruitment || _maze == null) return;

        int cx = Cell(ant.X), cy = Cell(ant.Y);
        if (_homingOff.TryGetValue(ant.Id, out var off))
        {
            // La tregua dura hasta que la hormiga CAMBIA de celda: es la prueba
            // de que salió del rincón donde se atascó.
            if (cx != off.cx || cy != off.cy || off.ticks <= 1)
                _homingOff.Remove(ant.Id);
            else
                _homingOff[ant.Id] = (off.ticks - 1, off.cx, off.cy);
            return;
        }

        bool homing = ant.HasLoad || ant.Energy < TiredEnergy;
        if (!homing) { _guideStall.Remove(ant.Id); return; }

        int d = NestGuideDistance(colony.Id, ant.X, ant.Y);
        if (d <= 2) { _guideStall.Remove(ant.Id); return; }
        if (!_guideStall.TryGetValue(ant.Id, out var s)) { _guideStall[ant.Id] = (d, 0, cx, cy); return; }
        if (d < s.dist) { _guideStall[ant.Id] = (d, 0, cx, cy); return; }

        int ticks = s.ticks + 1;
        if (ticks < GuideStallTicks) { _guideStall[ant.Id] = (s.dist, ticks, s.cx, s.cy); return; }

        float? escape = EscapeHeading(colony, ant);
        ant.Heading = escape ?? WrapPi(ant.Heading + MathF.PI);
        _homingOff[ant.Id] = (HomingTruceTicks, cx, cy);
        _guideStall[ant.Id] = (d, 0, cx, cy);
    }

    /// <summary>
    /// F8 — rumbo de salida de un rincón: de las cuatro celdas ortogonales libres
    /// de la celda actual, la que MÁS BAJA el campo de distancia (empate: el orden
    /// fijo −X, +X, −Y, +Y del BFS). Devuelve null si ninguna es transitable o
    /// está conectada al nido; entonces el que llama recurre al 180°.
    ///
    /// Ortogonales, no diagonales, a propósito: una diagonal desde el borde de una
    /// celda vuelve a cortar la esquina del muro, que es justo el gesto que deja a
    /// la hormiga deslizándose por la cara.
    /// </summary>
    private float? EscapeHeading(Colony colony, Ant ant)
    {
        if (_maze == null || colony.Id < 0) return null;
        var guides = NestGuides();
        if (colony.Id >= guides.Length) return null;
        int[] dist = guides[colony.Id];
        if (dist.Length == 0) return null;

        int n = _gridCells;
        int cx = Cell(ant.X), cy = Cell(ant.Y);
        int here = dist[cy * n + cx];
        int best = here, bx = -1, by = -1;

        void Consider(int x, int y)
        {
            if (x < 0 || y < 0 || x >= n || y >= n) return;
            if (_maze!.IsWall(x, y)) return;
            int value = dist[y * n + x];
            if (value == int.MaxValue || value >= best) return;
            best = value; bx = x; by = y;
        }

        Consider(cx - 1, cy);
        Consider(cx + 1, cy);
        Consider(cx, cy - 1);
        Consider(cx, cy + 1);
        if (bx < 0) return null;

        const float cell = SimConstants.CellSizeUnits;
        float dx = (bx + 0.5f) * cell - ant.X;
        float dy = (by + 0.5f) * cell - ant.Y;
        if (dx * dx + dy * dy <= 1e-6f) return null;
        return WrapPi(MathF.Atan2(dy, dx));
    }

    /// <summary>
    /// F8 — CAMPO DE DISTANCIA AL NIDO, POR PASILLOS, una vez por colonia: BFS de
    /// 4 vecinos desde la celda del nido sobre las celdas abiertas del laberinto
    /// (entero, sin RNG: determinista). Es el mapa que usa la cargada para
    /// volver bordeando los muros en vez de empujarlos.
    ///
    /// Se construye PEREZOSAMENTE (la primera cargada lo paga: ~9 200 celdas) y se
    /// cachea; <see cref="ClearColoniesForLoad"/> lo invalida para que un
    /// checkpoint reconstruya el suyo. Sin laberinto no hay campo (la vuelta es la
    /// brújula al nido).
    /// </summary>
    private int[][] NestGuides()
    {
        if (_nestGuide != null) return _nestGuide;
        var guides = new int[_colonies.Count][];
        _nestGuide = guides;
        if (_maze == null) return guides;

        int n = _gridCells;
        for (int c = 0; c < _colonies.Count; c++)
        {
            var dist = new int[n * n];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
            guides[c] = dist;

            var (ox, oy) = _maze.NearestOpen(_colonies[c].NestX, _colonies[c].NestY);
            int sx = Cell(ox), sy = Cell(oy);
            if (_maze.IsWall(sx, sy)) continue; // sin celda de arranque: la cargada usa la brújula

            var queue = new Queue<int>();
            dist[sy * n + sx] = 0;
            queue.Enqueue(sy * n + sx);
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int cx = idx % n, cy = idx / n;
                int d = dist[idx] + 1;
                // Orden fijo de vecinos (−X, +X, −Y, +Y): el campo no depende del
                // orden de llegada porque cada celda se escribe una sola vez.
                if (cx > 0) Push(cx - 1, cy, d);
                if (cx + 1 < n) Push(cx + 1, cy, d);
                if (cy > 0) Push(cx, cy - 1, d);
                if (cy + 1 < n) Push(cx, cy + 1, d);

                void Push(int x, int y, int value)
                {
                    int i2 = y * n + x;
                    if (dist[i2] != int.MaxValue || _maze.IsWall(x, y)) return;
                    dist[i2] = value;
                    queue.Enqueue(i2);
                }
            }
        }
        return guides;
    }

    /// <summary>
    /// F8 — distancia al nido por pasillos, en celdas, para la colonia
    /// <paramref name="colonyId"/> en la celda que contiene (x, y). −1 = sin
    /// campo (sin laberinto) o celda no conectada al nido. Es la lectura que
    /// fijan los tests del campo (descenso monótono, nido en 0).
    /// </summary>
    public int NestGuideDistance(int colonyId, float x, float y)
    {
        if (_maze == null || colonyId < 0 || colonyId >= _colonies.Count) return -1;
        int[] dist = NestGuides()[colonyId];
        if (dist.Length == 0) return -1;
        int value = dist[Cell(y) * _gridCells + Cell(x)];
        return value == int.MaxValue ? -1 : value;
    }

    /// <summary>
    /// F8 — un paso del campo: deja en (<paramref name="tx"/>, <paramref name="ty"/>) el
    /// centro de la celda vecina que más baja la distancia al nido. Si no hay
    /// campo, la hormiga está en una celda aislada o ya está en el mínimo, deja el
    /// nido como objetivo (brújula).
    /// </summary>
    private void GuideStep(Colony colony, Ant ant, ref float tx, ref float ty)
    {
        if (_maze == null) return;
        var guides = NestGuides();
        if (colony.Id < 0 || colony.Id >= guides.Length) return;
        int[] dist = guides[colony.Id];
        if (dist.Length == 0) return;

        int n = _gridCells;
        int cx = Cell(ant.X), cy = Cell(ant.Y);
        int here = dist[cy * n + cx];
        if (here == int.MaxValue) return; // celda no conectada al nido

        int best = here, bx = cx, by = cy;
        if (cx > 0 && dist[cy * n + cx - 1] < best) { best = dist[cy * n + cx - 1]; bx = cx - 1; by = cy; }
        if (cx + 1 < n && dist[cy * n + cx + 1] < best) { best = dist[cy * n + cx + 1]; bx = cx + 1; by = cy; }
        if (cy > 0 && dist[(cy - 1) * n + cx] < best) { best = dist[(cy - 1) * n + cx]; bx = cx; by = cy - 1; }
        if (cy + 1 < n && dist[(cy + 1) * n + cx] < best) { best = dist[(cy + 1) * n + cx]; bx = cx; by = cy + 1; }
        if (bx == cx && by == cy) return; // ya en el mínimo (celda del nido)

        const float cell = SimConstants.CellSizeUnits;
        tx = (bx + 0.5f) * cell;
        ty = (by + 0.5f) * cell;

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
            rivals: sp.ContactRadius > 0f && _colonies.Count > 1 ? _colonies : null,
            maze: _maze);
        // F8: con laberinto el nido NO se ve a través de la pared — la brújula
        // innata apunta por el pasillo (ver ApplyCorridorHomeSense).
        ApplyCorridorHomeSense(colony, ant, ref sensors);
        var decision = AntDecision.Neutral();
        ant.Brain.Evaluate(in sensors, ref decision);
        // — F5.3: tropotaxis repelente por huella CHC (reflejo periférico) —
        ApplyFootprintRepulsion(colony, ant, sp, ref decision);
        // — F8: tropotaxis ATRACTIVA por rastro de comida (reclutamiento) —
        ApplyTrailRecruitment(colony, ant, sp, ref decision);
        // — F8: brújula de la CARGADA — vuelve al nido con lo que lleva —
        ApplyLoadHoming(colony, ant, ref decision);
        // — F8: la que se queda sin reserva vuelve a recargar al nido —
        ApplyReturnWhenTired(colony, ant, ref decision);
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
        float nx = ant.X + CanonMath.Cos(ant.Heading) * v * dt;
        float ny = ant.Y + CanonMath.Sin(ant.Heading) * v * dt;
        if (_maze == null)
        {
            ant.X = Math.Clamp(nx, 0f, WorldWidth);
            ant.Y = Math.Clamp(ny, 0f, WorldHeight);
        }
        else
        {
            // F6 — Pared con deslizamiento: el intento completo; si choca, solo
            // X y luego solo Y. Deslizar (en vez de bloquear el paso entero) es
            // lo que permite recorrer un corredor pegado a la pared sin que el
            // cerebro tenga que calcular el ángulo exacto.
            nx = Math.Clamp(nx, 0f, WorldWidth);
            ny = Math.Clamp(ny, 0f, WorldHeight);
            float stepX = ant.X, stepY = ant.Y; // para medir cuánto avanzó de verdad
            if (!_maze.Blocked(nx, ny, MazeLayout.AntRadius))
            {
                ant.X = nx;
                ant.Y = ny;
            }
            else if (!_maze.Blocked(nx, ant.Y, MazeLayout.AntRadius))
            {
                ant.X = nx;
            }
            else if (!_maze.Blocked(ant.X, ny, MazeLayout.AntRadius))
            {
                ant.Y = ny;
            }
            // Si apenas avanzó, la hormiga SE APOYA en la pared y gira (ver
            // <see cref="MazeBumpTurn"/>). El criterio es el AVANCE EFECTIVO, no
            // «fallaron los tres intentos»: la que empuja una pared de frente
            // con un deslizamiento de milésimas por el otro eje nunca ve fallar
            // el paso entero, y se quedaba ahí para siempre —medido en la
            // partida canónica: 55 hormigas inmóviles más de 3 s, con el bocado
            // en la boca—. La colonia se apagaba por falta de descargas, no por
            // falta de comida.
            float moved = MathF.Abs(ant.X - stepX) + MathF.Abs(ant.Y - stepY);
            if (MazeBumpTurn != 0f && moved < MazeBumpMinProgress * v * dt)
                ant.Heading = WrapPi(ant.Heading + MazeBumpTurn);
        }
        BreakGuideStall(colony, ant);
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
            float amt = SpendGland(ant, decision.DepositFood * sp.QMaxFood * dt, sp.QMaxFood);
            if (amt > 0f)
            {
                deposited += amt;
                colony.FoodLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
                TrailFoodDeposited += amt;
            }
        }
        if (decision.DepositHome > 0f)
        {
            float amt = SpendGland(ant, decision.DepositHome * sp.QMaxHome * dt, sp.QMaxHome);
            if (amt > 0f)
            {
                deposited += amt;
                colony.HomeLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
                TrailHomeDeposited += amt;
            }
        }
        if (decision.DepositAlarm > 0f)
        {
            float amt = decision.DepositAlarm * sp.QMaxAlarm * dt;
            deposited += amt;
            colony.AlarmLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
        }
        // — F8: reflejo de reclutamiento — el rastro lo deja la hormiga según lo
        //   que esté HACIENDO, sin que el cerebro tenga que decidirlo: la que
        //   BUSCA va dejando rastro de CASA (el camino de vuelta al nido) y la
        //   que vuelve CARGADA deja rastro de COMIDA (el camino al montón). Una
        //   sola celda, la que pisa: el rastro es un camino, no una nube.
        //   Sin coste de energía ni fitness: es el reflejo que hace que un
        //   hallazgo se convierta en camino para las demás, no una inversión que
        //   la política tenga que justificar.
        if (Recruitment && ant.HasLoad)
        {
            // Rastro de comida con forma de cresta hacia la FUENTE y de UNA CELDA:
            // es el camino que la hormiga pisa, no una nube. El kernel 3×3 de la
            // primera versión (centro 0,4 + cuatro vecinas 0,15) dejaba una banda
            // de tres celdas —24 u— por la que nadie había pasado, y como la
            // difusión no puede agrandar el soporte, ese era el ancho MÍNIMO: en
            // el tablero se veía como humo de decenas de unidades, y la cola
            // difusa (2–10/255) ensanchaba aún más el halo con la curva de
            // pantalla. Ahora TODO el depósito va a la celda que ocupa la hormiga
            // (misma cantidad, no menos señal: el reparto lateral solo repartía).
            float dxN = ant.X - colony.NestX;
            float dyN = ant.Y - colony.NestY;
            float away = MathF.Sqrt(dxN * dxN + dyN * dyN) / MathF.Max(RecruitmentTrailSourceDistance, 1f);
            // F11: la PUERTA. Si el rastro lo decide el cerebro, la cresta se
            // escala por su salida de depósito de comida y con la puerta cerrada
            // (salida 0) no se deposita NADA — ni cresta ni cola.
            float gate = RecruitmentTrailByBrain ? decision.DepositFood : 1f;
            float amt = RecruitmentFoodDeposit * gate * (0.4f + 0.6f * Math.Clamp(away, 0f, 1f)) * sp.QMaxFood * dt;
            amt = SpendGland(ant, amt, sp.QMaxFood); // F11b: la glándula paga la cresta
            int cx = Cell(ant.X), cy = Cell(ant.Y);
            if (amt > 0f)
            {
                colony.FoodLayer.Deposit(cx, cy, amt);
                TrailFoodDeposited += amt;
            }
        }
        else if (Recruitment)
        {
            // Buscando (sin carga): rastro de CASA. Es el camino de vuelta —el
            // que la trajo y el que la devuelve al nido— y se pinta en verde con
            // la capa `home`. No se sigue como guía (se probó y no mejoraba el
            // forrajeo: el cerebro ya deposita `home` por todas partes); lo que
            // guía a la comida es el rastro de la cargada.
            float amt = RecruitmentHomeDeposit * (RecruitmentTrailByBrain ? decision.DepositHome : 1f) * sp.QMaxHome * dt;
            amt = SpendGland(ant, amt, sp.QMaxHome); // F11b: también sale de la reserva
            if (amt > 0f)
            {
                colony.HomeLayer.Deposit(Cell(ant.X), Cell(ant.Y), amt);
                TrailHomeDeposited += amt;
            }
        }
        // F8 — LA FEROMONA DE LA PARTIDA NO SE PAGA CON LA RESERVA. En el mundo
        // de reclutamiento el rastro lo pone el REFLEJO (gratis, ver más abajo);
        // los canales del cerebro siguen escribiendo su capa, pero cobrarlos
        // costaba más que el mantenimiento entero de la obrera: medido, el
        // depósito a plena tasa son ~0,03 ep/s por hormiga (frente a 0,012 de
        // upkeep) y la colonia se quedaba sin reserva con el tablero lleno. La
        // partida clásica sigue cobrando cada gota (pines de hash intactos).
        if (!Recruitment)
            ant.Energy = Math.Max(0f, ant.Energy - deposited * sp.CostDeposit / ant.EnergyCapacity);
        if (deposited > 0f && RewardDepositPerUnit > 0f)
            ant.Fitness += deposited * RewardDepositPerUnit;

        // — Recarga de energía / trofalaxia comunal dentro del nido —
        float dxNest = ant.X - colony.NestX;
        float dyNest = ant.Y - colony.NestY;
        bool insideNest = dxNest * dxNest + dyNest * dyNest <= NestRadius * NestRadius;
        if (insideNest)
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
            // F11b: la glándula de rastro se rellena SOLO aquí, dentro del nido.
            // Es lo que convierte el depósito en un presupuesto: para volver a
            // marcar el camino hay que volver a casa.
            if (RecruitmentGlandSeconds > 0f && ant.Gland < 1f)
                ant.Gland = MathF.Min(1f, ant.Gland + dt / MathF.Max(RecruitmentGlandRefillSeconds, 0.05f));
        }

        // — Interacción (gating físico) —
        ant.InteractCooldown = Math.Max(0f, ant.InteractCooldown - dt);
        // F5.2b.1: el golpe de combate TAMBIÉN vive en el cooldown de
        // interacción (misma ventana de 0.5 s que pickup/unload).
        // F8: la CARGADA que pisa el nido DESCARGA sin que el cerebro pulse
        // interacción — el bocado ya venía para casa y llegó. Antes de esto, la
        // hormiga podía cruzar el nido cien veces con la carga encima y la
        // comida se quedaba en la boca del nido sin entrar a la reserva.
        bool atNestWithLoad = Recruitment && ant.HasLoad && insideNest;
        if ((DecisionValidator.WantsInteraction(in decision) || atNestWithLoad) && ant.InteractCooldown <= 0f)
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
                float dropX = Math.Clamp(command.X, 0f, WorldWidth);
                float dropY = Math.Clamp(command.Y, 0f, WorldHeight);
                // F6: una gota sobre una pared sería comida inalcanzable — se
                // reubica en la celda abierta más cercana (determinista).
                if (_maze != null) (dropX, dropY) = _maze.NearestOpen(dropX, dropY);
                var item = new FoodItem
                {
                    Id = _nextItemId++,
                    X = dropX,
                    Y = dropY,
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
        // Objetivo escalado por área (ver DensityScaledTargetItems): el setter
        // explícito de TargetItems (la arena lo pone a 0; el CLI podría fijarlo)
        // tiene prioridad — solo cuando sigue en el default se aplica la
        // densidad constante.
        int target = TargetItems != TargetItemsDefault ? TargetItems : DensityScaledTargetItems;

        int spawned = 0;
        while (_items.Count < target && spawned < RespawnBudgetPerStep)
        {
            if (SpawnItem()) spawned++;
            else break;
        }

        // F8: los montones se reponen ADEMÁS, y de uno en uno: un montón son 6–10
        // ítems y el presupuesto clásico de 2 por paso tardaría una eternidad en
        // volver a formar uno visible en el tablero.
        if (Recruitment && _items.Count < _pileFloor)
            SpawnCluster(_pileSeed++ % Math.Max(1, _colonies.Count));
    }

    private bool SpawnItem()
    {
        if (!TryFindSpawnPoint(out float x, out float y)) return false;
        AddFoodItemAt(x, y);
        return true;
    }

    /// <summary>
    /// F8 — siembra un MONTÓN: un centro dentro del radio de forrajeo de una
    /// colonia (ver <see cref="TryFindClusterCenter"/>) y 6–10 bocados ricos de
    /// <see cref="FoodClusterItemAmount"/> alrededor (radio
    /// <see cref="FoodClusterRadius"/>). El primero cae en el centro, así que el
    /// montón tiene corazón y no un hueco; los que caen en pared se descartan (no
    /// se reintentan: reintentar por ítem desordenaría el flujo del RNG sin ganar
    /// nada).
    /// </summary>
    private bool SpawnCluster(int? colonyIndex = null)
    {
        if (!TryFindClusterCenter(out float cx, out float cy, colonyIndex)) return false;

        int size = FoodClusterMin +
            (int)(_worldRng.NextDouble01() * (FoodClusterMax - FoodClusterMin + 1));
        AddFoodItemAt(cx, cy, FoodClusterItemAmount);
        for (int i = 1; i < size; i++)
        {
            float ox = cx + ((float)_worldRng.NextDouble01() * 2f - 1f) * FoodClusterRadius;
            float oy = cy + ((float)_worldRng.NextDouble01() * 2f - 1f) * FoodClusterRadius;
            ox = Math.Clamp(ox, 0f, WorldWidth);
            oy = Math.Clamp(oy, 0f, WorldHeight);
            if (_maze != null && !_maze.ItemFits(ox, oy)) continue;
            AddFoodItemAt(ox, oy, FoodClusterItemAmount);
        }
        return true;
    }

    /// <summary>
    /// F8 — centro de un MONTÓN. No es un punto cualquiera del mapa: se elige
    /// una colonia al azar y un rumbo, y se deja el montón a media distancia
    /// (entre <see cref="FoodClusterMinDistance"/> y
    /// <see cref="FoodClusterMaxDistance"/>). Una colonia recluta hacia parches
    /// que puede alcanzar: medido, sus obreras patrullan a ~100 u del nido y solo
    /// un 11 % pasa de 200, así que un montón al otro lado del mapa no lo visita
    /// nadie y el rastro no tiene a quién guiar.
    /// </summary>
    private bool TryFindClusterCenter(out float cx, out float cy, int? colonyIndex = null)
    {
        cx = cy = 0f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int pick = colonyIndex ?? (int)(_worldRng.NextDouble01() * _colonies.Count);
            var colony = _colonies[pick % _colonies.Count];
            float bearing = (float)(_worldRng.NextDouble01() * Math.PI * 2.0);
            float dist = FoodClusterMinDistance +
                (float)_worldRng.NextDouble01() * (FoodClusterMaxDistance - FoodClusterMinDistance);
            float x = colony.NestX + CanonMath.Cos(bearing) * dist;
            float y = colony.NestY + CanonMath.Sin(bearing) * dist;
            x = Math.Clamp(x, 0f, WorldWidth);
            y = Math.Clamp(y, 0f, WorldHeight);
            if (_maze != null)
            {
                if (!_maze.ItemFits(x, y)) (x, y) = _maze.NearestOpen(x, y);
                if (!_maze.ItemFits(x, y)) continue;
            }
            cx = x;
            cy = y;
            return true;
        }
        return false;
    }

    /// <summary>Un punto válido para comida: fuera del radio de los nidos, fuera de
    /// las paredes y sin pegarse a ellas. Consume el MISMO flujo del RNG que el
    /// spawn clásico (dos dobles por intento), así que la ruta sin reclutamiento
    /// queda byte a byte igual.</summary>
    private bool TryFindSpawnPoint(out float x, out float y)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            x = (float)(_worldRng.NextDouble01() * WorldWidth);
            y = (float)(_worldRng.NextDouble01() * WorldHeight);
            bool ok = true;
            float minDist = _maze != null ? MazeLayout.ItemSpawnMinDistance : NestMinSpawnDistance;
            for (int c = 0; c < _colonies.Count; c++)
            {
                float dx = x - _colonies[c].NestX;
                float dy = y - _colonies[c].NestY;
                if (dx * dx + dy * dy < minDist * minDist)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;
            // F6: ni dentro de una pared ni pegado a ella (el pickup tiene radio
            // 12 u, pero un ítem a 3 u del muro obliga a la hormiga a meterse en
            // él). Con laberinto, un punto así se descarta y se reintenta.
            if (_maze != null && !_maze.ItemFits(x, y)) continue;
            return true;
        }
        x = 0f;
        y = 0f;
        return false;
    }

    /// <summary>Crea el ítem (hoja o simple) en un punto YA validado y avisa al
    /// canal de eventos. Las tiradas de RNG son las del spawn clásico: la regla
    /// hoja/simple solo consume cuando <see cref="LeafFraction"/> &gt; 0.</summary>
    private void AddFoodItemAt(float x, float y, float? amountOverride = null)
    {
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

        // F8: un montón es un parche RICO — el trozo que se lleva la obrera es
        // más grande que el de una migaja suelta (buche lleno). Se aplica DESPUÉS
        // de la tirada clásica, así que el flujo del RNG no cambia.
        if (amountOverride.HasValue) amount = amountOverride.Value;

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

    /// <summary>
    /// F11b — COBRA EL DEPÓSITO DE LA GLÁNDULA. Devuelve la cantidad que la
    /// hormiga puede dejar de verdad y descuenta el tiempo de glándula que
    /// consume; con el precio apagado (o fuera del mundo de reclutamiento)
    /// devuelve <paramref name="amt"/> intacto y no toca ni un flotante.
    ///
    /// El presupuesto está en SEGUNDOS de depósito a plena tasa: como
    /// <c>amt = tasa · QMax · dt</c>, <c>amt / QMax</c> son los segundos que se
    /// gastan, así que media tasa cuesta la mitad y la capacidad vale igual
    /// para cualquier capa. La reserva nunca se queda negativa: cuando el
    /// presupuesto es menor que lo pedido se deposita solo lo que cabe (y a
    /// partir de ahí, nada hasta volver al nido).
    /// </summary>
    private float SpendGland(Ant ant, float amt, float qMax)
    {
        if (!Recruitment || RecruitmentGlandSeconds <= 0f || amt <= 0f) return amt;
        float requested = amt;                                // lo que la hormiga quería dejar
        float cost = amt / MathF.Max(qMax, 1e-6f);           // s de glándula que pide
        float budget = ant.Gland * RecruitmentGlandSeconds; // s de glándula que hay
        if (cost > budget)
        {
            if (budget <= 0f)
            {
                TrailGlandDenied += requested;
                return 0f;
            }
            cost = budget;
            amt = cost * qMax;
            TrailGlandDenied += requested - amt;
        }
        ant.Gland = MathF.Max(0f, ant.Gland - cost / RecruitmentGlandSeconds);
        TrailGlandSpentSeconds += cost;
        return amt;
    }

    /// <summary>Hash canónico del estado completo (hashes de hito / verificación).</summary>
    public string HashLine()
    {
        using var h = new CanonicalHasher();
        h.AppendUInt64(_seed);
        h.AppendUInt64(Tick);
        // F6: el terreno forma parte del mundo — dos partidas con la misma
        // semilla y laberintos distintos NO son el mismo mundo. Solo se añade
        // con laberinto: los hashes clásicos (y sus pines) no se mueven.
        if (_maze != null) h.AppendUInt64(_maze.LayoutHash);

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
        for (int i = 0; i < genomes.Count; i++)
        {
            RequireCompatibleSizes(genomes[i].Sizes);
            colony.Pool.QueueImmigrant(genomes[i], Tick);
        }
    }

    /// <summary>
    /// F7: un genoma de otro ancho no puede cruzarse con la élite de este mundo
    /// (el cruce uniforme exige vectores del mismo tamaño). Falla con un mensaje
    /// que dice QUÉ hacer en vez de romper más tarde dentro del pool.
    /// </summary>
    private void RequireCompatibleSizes(int[] sizes)
    {
        if (sizes.Length == _brainSizes.Length)
        {
            bool same = true;
            for (int i = 0; i < sizes.Length; i++) if (sizes[i] != _brainSizes[i]) { same = false; break; }
            if (same) return;
        }
        throw new ArgumentException(
            $"El genoma es {string.Join("→", sizes)} y este mundo corre {string.Join("→", _brainSizes)}: " +
            "siembra la partida con --seed-pool (el mundo adopta el ancho del pool) en vez de importarlo en caliente.");
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
        for (int i = 0; i < genomes.Count; i++) RequireCompatibleSizes(genomes[i].Sizes);
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
        int[] coldSizes = _brainSizes;
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
    internal void ClearColoniesForLoad()
    {
        _colonies.Clear();
        _nestGuide = null; // F8: el campo de vuelta se reconstruye con los nidos cargados
    }

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
            RecruitmentEconomy = Recruitment, // F8: contabilidad de la ronda
            NestX = nestX,
            NestY = nestY,
            StockMax = stockMax,
            FungusMax = sp.FungusMax, // F5.2a.2: capacidad siempre de la especie
            Rng = rng,
            Pool = new GenomePool(rng.Fork(0xA5C3E7B9UL), _brainSizes, seedCount: 0),
            FoodLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize),
            HomeLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize),
            AlarmLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize),
            FootprintLayer = new PheromoneLayer(_gridCells, _gridCells, lodBlockSize: LodBlockSize) // F5.3: huella CHC
        };
        _colonies.Add(colony);
        return colony;
    }
}