using System;
using System.Collections.Generic;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.World;

namespace AntSim.Core.Training;

/// <summary>Resultado de una evaluación de arena.</summary>
public readonly struct ArenaResult
{
    public readonly double Fitness;
    public readonly int Pickups;
    public readonly int Unloads;

    public ArenaResult(double fitness, int pickups, int unloads)
    {
        Fitness = fitness;
        Pickups = pickups;
        Unloads = unloads;
    }

    /// <summary>Competencia mínima: al menos un ciclo completo (una descarga al nido).</summary>
    public bool Competent => Unloads >= 1;
}

/// <summary>
/// Arena de evaluación determinista de un genoma (Fase 3bis: transferencia arena↔mundo).
/// Réplica de las condiciones del mundo real para cerrar la brecha detectada al validar
/// la transferencia del pre-entrenamiento (la arena antigua — hormiga sola, ítem a la
/// vista, rastro plantado — no transfería: pickup 0 en el mundo abierto).
///
/// - COLONIA COMPLETA: las 10 fundadoras y las 4 crías iniciales de
///   <see cref="WorldSim"/>, con el ColonyController operando (alimentación, puesta,
///   cría, canibalismo). Las fundadoras llevan clones del genoma evaluado y el pool de
///   la colonia se siembra con él, de modo que la descendencia nace de variantes
///   mutadas — el mismo régimen que `--seed-pool` en el mundo real.
/// - COMIDA REAL: 24 ítems de 4 ep (la densidad del mundo: TargetItemsDefault)
///   repartidos con el muestreo del mundo (posición uniforme, ≥
///   NestMinSpawnDistance del nido), restringidos a la banda de distancia de la
///   etapa del currículo. Sin rastro plantado y sin respawn (TargetItems = 0):
///   descubrir la comida solo con visión (60 u) y búsqueda. La densidad del mundo
///   no es solo fidelidad: multiplica las oportunidades de pickup y densifica el
///   gradiente de descubrimiento.
/// - SEÑAL DE COLONIA: fitness = suma de la aptitud de TODAS las adultas (fundadoras
///   y descendencia; los muertos conservan su fitness) más dos términos DENSADOS que
///   forman el gradiente ida-y-vuelta:
///     · EXPLORACIÓN (sin carga): progreso hacia FUERA del nido, solo mientras d ≤
///       MaxDistance de la etapa (acampar en una esquina no premia) y clamped ≥ 0
///       (volver con las manos vacías no castiga). Sin él el PRIMER eslabón — llegar
///       a la banda de comida a ≥ 200 u dentro de la vida de una fundadora — nunca
///       se muestrea: la dispersión de un paseo aleatorio apenas alcanza ~190 u.
///     · HOMING (con carga): progreso neto hacia el nido (× HomeShapingPerUnit).
///       Un portador aleatorio jamás regresa a casa dentro de su vida; el término
///       enseña exactamente el comportamiento — volver por brújula con la carga —
///       que la descarga exige, y la recompensa del mundo (unload ≈ 12.5) domina en
///       cuanto aparece.
///     · CARRY-LEG (al completar): en cada descarga real se paga la distancia
///       recta pickup→descarga de ESA carga (× CarryLegPerUnit). Es el término
///       que falta para el relevo: el homing premia el progreso PARCIAL de
///       cualquier portadora, también el de una fundadora que muere a mitad de
///       camino; en bandas anchas (200–450 u) esa señal parcial + el forrajeo
///       del anillo amplio superan al cierre largo y la descendencia completa
///       tramos cada vez más cortos (hallazgo warm3: carry-leg 91.8→46.6 u en
///       el mundo abierto con drop-avg y descargas sanas). Pagar SOLO en la
///       descarga y proporcional al tramo completo hace farmeable exactamente
///       lo que queremos: exige pickup real + descarga real, y el pago crece
///       con la longitud del tramo completado (una cría que recoge una suelta
///       a 200 u y llega al nido cobra ~16 ep a 0.08 ep/u — más que el ciclo
///       básico, así que seleccionar "relevo profundo" compite y gana).
///   La recompensa real del mundo (pickup + unload) sigue siendo la señal objetivo;
///   los densados solo hacen muestreable el camino hacia ella.
/// - COMPETENCIA OBSERVADA: ≥ 1 descarga al nido (ciclo completo), contada por
///   eventos del mundo — no se infiere del fitness. Un ciclo vale 0.5 + 4·2 + 4 = 12.5.
/// - Determinista: mismo genoma + misma semilla ⇒ mismo resultado bit a bit.
/// </summary>
public sealed class ArenaEvaluator
{
    /// <summary>Mundo de arena por defecto: 96 celdas = 768 u (el del CLI pequeño).
    /// La 4ª etapa del currículo (FullWorldStages) usa arenas más grandes vía
    /// <see cref="CustomGridCells"/> — ítems a > 450 u no caben en 768 u.</summary>
    public const int GridCells = 96;

    /// <summary>Celdas del grid de feromonas de la arena (null = usar GridCells).
    /// Configurable SOLO en el constructor: cambiarlo en curso alteraría el
    /// determinismo de las evaluaciones en vuelo.</summary>
    public int? CustomGridCells { get; }

    private int EffectiveGridCells => CustomGridCells ?? GridCells;
    public const float FoodAmount = 4.0f;       // ep por ítem (la banda del mundo es 4–6)
    public const int TestItemCount = WorldSim.TargetItemsDefault; // 24: densidad real del mundo
    public const int MaxSpawnAttempts = 64;
    public const float HomeShapingPerUnit = 0.12f;   // densado de homing con carga (ep/u)
    public const float ExplorePerUnit = 0.015f;      // densado de exploración sin carga (ep/u)
    public const float CarryLegPerUnit = 0.15f;      // densado de tramo completado pickup→descarga (ep/u)
    // Calibración: sin densados el fitness de todos los genomas colapsa a la
    // supervivencia pura (10 × 0.002 × 106.2 s = 2.124 EXACTO: cero interacciones —
    // el paseo aleatorio no alcanza la banda de ≥ 200 u antes de morir y el ciclo
    // completo nunca se muestrea). Exploración récord (máx ~3.3 por hormiga en
    // etapa cercana) selecciona "salir de aquí"; homing récord a 0.06 (máx 12 por
    // ciclo) selecciona "volver a casa con la carga" y compite con la exploración;
    // la descarga real (12.5) sigue siendo el pago máximo del ciclo completo.

    private readonly ulong _seed;
    private readonly float _minDistance;
    private readonly float _maxDistance;
    private readonly int _tickBudget;
    private readonly int _trials;
    // No readonly: DeterministicRandom es un struct y mutar una copia defensiva
    // descartaría el avance del flujo (bug corregido en Fase 3).
    private DeterministicRandom _trialRng;

    public ArenaEvaluator(ulong seed, float minDistance, float maxDistance, int tickBudget, int trials = 1,
        int? customGridCells = null)
    {
        if (minDistance < WorldSim.NestMinSpawnDistance)
            throw new ArgumentOutOfRangeException(nameof(minDistance),
                $"La distancia mínima debe ser ≥ NestMinSpawnDistance ({WorldSim.NestMinSpawnDistance} u).");
        if (maxDistance < minDistance)
            throw new ArgumentOutOfRangeException(nameof(maxDistance), "La banda de distancia está vacía.");
        if (customGridCells is int g && g < 16)
            throw new ArgumentOutOfRangeException(nameof(customGridCells), "El grid de arena exige ≥ 16 celdas.");
        // El ítem más lejano debe caber en el mundo de la arena: el nido está en
        // el centro, así que basta maxDistance < mitad del lado (con margen para
        // el cuerpo y el radio de pickup).
        if (customGridCells is int cells && maxDistance > cells * SimConstants.CellSizeUnits * 0.5f)
            throw new ArgumentOutOfRangeException(nameof(customGridCells),
                $"La banda {minDistance}-{maxDistance} no cabe en una arena de {cells} celdas.");
        _seed = seed;
        _minDistance = minDistance;
        _maxDistance = maxDistance;
        _tickBudget = tickBudget;
        _trials = Math.Max(1, trials);
        CustomGridCells = customGridCells;
        _trialRng = new DeterministicRandom(_seed);
    }

    public ArenaResult Evaluate(MlpGenome genome)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));

        // Varias pruebas con posiciones de comida distintas: un genoma "competente"
        // es el mejor de K por fitness. El RNG de pruebas avanza en orden fijo, así
        // que el resultado sigue siendo determinista.
        ArenaResult best = default;
        for (int t = 0; t < _trials; t++)
        {
            var result = EvaluateTrial(genome, null);
            if (t == 0 || result.Fitness > best.Fitness)
                best = result;
        }
        return best;
    }

    /// <summary>
    /// Evaluación NEAT (F5.2c rodaja 5): el mismo protocolo exacto — misma
    /// colonia, misma comida, mismos densados, mismo RNG de pruebas — con los
    /// fundadores portando el cerebro de grafo. DIFERENCIA de régimen: los
    /// fundadores NEAT no entran al pool de la colonia (Genome=null, sin
    /// RecordFitness al morir) — la evolución la dueña el trainer; las crías
    /// que eclosionen durante la prueba son hormigas MLP salvajes del pool
    /// frío de la colonia, el mismo régimen que un mundo recién fundado.
    /// </summary>
    public ArenaResult EvaluateNeat(NeatGenome genome)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));

        ArenaResult best = default;
        for (int t = 0; t < _trials; t++)
        {
            var result = EvaluateTrial(null, genome);
            if (t == 0 || result.Fitness > best.Fitness)
                best = result;
        }
        return best;
    }

    private ArenaResult EvaluateTrial(MlpGenome? mlp, NeatGenome? neat)
    {
        var sim = new WorldSim(_seed, EffectiveGridCells, 1);
        var colony = sim.Colonies[0];

        // — Re-equilibrio de la señal de fitness (Fase 3) —
        // Sin refuerzo de depósito (se podría cultivar sin forrajear); el ciclo
        // completo (pickup + descarga) es la señal dominante gracias al bonus.
        sim.RewardPickup = 0.5f;
        sim.RewardUnloadPerEp = 2.0f;
        sim.RewardSurvivalPerSecond = 0.002f;
        sim.RewardDepositPerUnit = 0.0f;
        sim.RewardUnloadBonus = 4.0f;

        // — Sin respawn: solo los ítems de prueba —
        sim.TargetItems = 0;

        // — Colonia completa del constructor (10 fundadoras con posiciones y rumbos
        //   del flujo RNG real + cría inicial). La política evaluada va en todas
        //   las fundadoras (clones: cada muerte alimenta el pool con SU fitness) y
        //   el pool se siembra con el genoma para que la descendencia sean variantes.
        //   MLP: Genome != null ⇒ feedback de pool al morir. NEAT: Genome = null
        //   (solo cerebro) ⇒ la descendencia nace del pool frío de la colonia.
        if (neat is not null)
        {
            var nb = neat.ToBrain();
            for (int i = 0; i < colony.Adults.Count; i++)
            {
                var ant = colony.Adults[i];
                ant.Genome = null;
                ant.Brain = nb; // el mismo cerebro compartido es de solo lectura por tick
            }
        }
        else
        {
            sim.SeedPoolFromGenomes(0, new[] { mlp! });
            for (int i = 0; i < colony.Adults.Count; i++)
            {
                var ant = colony.Adults[i];
                ant.Genome = mlp!.Clone();
                ant.Brain = ant.Genome.ToBrain();
            }
        }
        // Las FUNDADORAS son el genoma evaluado; la descendencia que eclosione
        // durante la prueba son variantes mutadas (Pool.Birth). Los índices <
        // founderCount son fundadoras (los adultos nunca se eliminan de la
        // lista, solo se marcan muertos) — el densado de exploración solo paga
        // para ellas. Sonda empírica (Fase 3ter): con pago universal, el
        // campeón del relevo EXPLOTABA el término creciendo la colonia (hasta
        // 14 adultas) — cada neonata marcaba su propio récord de exploración
        // (~3.6 ep) y ese multiplicador superaba al homing (12/ciclo):
        // evolución seleccionaba "crecer y pasear" y el portador jamás volvía a
        // casa (distancia mínima con carga: 251 u — ni un paso de regreso).
        int founderCount = colony.Adults.Count;

        // — Comida: spawn con el mecanismo del mundo (uniforme, ≥ 200 u del nido),
        //   dentro de la banda de distancia de la etapa. Sin rastro plantado. —
        sim.ClearItems();
        for (int k = 0; k < TestItemCount; k++)
        {
            (float x, float y) = DrawFoodPosition(colony);
            sim.AddItem(new FoodItem { X = x, Y = y, Amount = FoodAmount });
        }

        // — Ejecución con presupuesto completo, SIN corte por estancamiento. En el
        //   relevo intergeneracional el récord de distancia máxima NO mide progreso:
        //   un portador que vuelve a casa lo REDUCE, y una colonia que completa
        //   ciclos a radio constante no marca récords — el corte de la arena antigua
        //   amputaba precisamente las pruebas productivas. Además, presupuesto
        //   completo = mismo número de ticks para todos los genomas (comparabilidad
        //   y determinismo estrictos).
        int pickups = 0, unloads = 0;
        double homeShaping = 0.0, exploreShaping = 0.0, carryLegShaping = 0.0;
        // Tramo abierto por (colonia, hormiga): posición del pickup sin descargar.
        // Los eventos traen X/Y, así que no hay que consultar el estado del mundo.
        var openCarries = new Dictionary<(int Colony, uint Ant), (float X, float Y)>();
        // Récords POR HORMIGA: los densados pagan solo en récord nuevo (monótonos,
        // no explotables: oscilar fuera-dentro-fuera junto a la banda no repite pago).
        // exploreRecord: máxima distancia alcanzada; homeRecord: mínima distancia
        // desde el último pickup (MaxValue = sin carga activa).
        var exploreRecord = new List<float>();
        var homeRecord = new List<float>();
        for (int i = 0; i < _tickBudget; i++)
        {
            sim.Step();

            var events = sim.LastEvents;
            for (int e = 0; e < events.Count; e++)
            {
                var kind = events[e].Kind;
                if (kind == SimEventKind.Pickup)
                {
                    pickups++;
                    openCarries[(events[e].ColonyId, events[e].AntId)] = (events[e].X, events[e].Y);
                }
                else if (kind == SimEventKind.Unload)
                {
                    unloads++;
                    // Densado de tramo completado: la distancia recta del pickup
                    // de esta carga a esta descarga. Solo paga al CERRAR el ciclo
                    // y crece con el tramo — selecciona relevo profundo.
                    if (openCarries.Remove((events[e].ColonyId, events[e].AntId), out var pick))
                    {
                        float ldx = events[e].X - pick.X;
                        float ldy = events[e].Y - pick.Y;
                        carryLegShaping += MathF.Sqrt(ldx * ldx + ldy * ldy) * CarryLegPerUnit;
                    }
                }
            }

            // — Densados ida-y-vuelta (ep/u), solo para vivas: los muertos no puntúan.
            //   · Exploración SIN carga: pago por récord de distancia hacia fuera,
            //     solo bajo el tope de la etapa (acampar en una esquina no premia).
            //   · Homing CON carga: pago por récord de acercamiento al nido desde el
            //     último pickup; la descarga resetea el récord (nuevo ciclo, nuevo pago).
            for (int a = 0; a < colony.Adults.Count; a++)
            {
                var ant = colony.Adults[a];
                if (!ant.Alive) continue;
                while (exploreRecord.Count <= a) { exploreRecord.Add(-1f); homeRecord.Add(float.MaxValue); }

                float dx = ant.X - colony.NestX;
                float dy = ant.Y - colony.NestY;
                float d = MathF.Sqrt(dx * dx + dy * dy);

                if (ant.HasLoad)
                {
                    if (homeRecord[a] == float.MaxValue)
                    {
                        homeRecord[a] = d; // pickup reciente: referencia inicial
                    }
                    else if (d < homeRecord[a])
                    {
                        homeShaping += (homeRecord[a] - d) * HomeShapingPerUnit;
                        homeRecord[a] = d;
                    }
                }
                else
                {
                    homeRecord[a] = float.MaxValue; // tras descarga: ciclo nuevo
                    // Exploración: SOLO fundadoras (ver founderCount arriba).
                    if (a >= founderCount) continue;
                    if (exploreRecord[a] < 0f)
                    {
                        exploreRecord[a] = d; // referencia inicial (posición de nacimiento)
                    }
                    else if (d <= _maxDistance && d > exploreRecord[a])
                    {
                        exploreShaping += (d - exploreRecord[a]) * ExplorePerUnit;
                        exploreRecord[a] = d;
                    }
                }
            }
        }

        // — Fitness de colonia: suma sobre todas las adultas (los muertos conservan
        //   su aptitud de por vida en la lista) + los densados. —
        double total = 0.0;
        for (int a = 0; a < colony.Adults.Count; a++)
            total += colony.Adults[a].Fitness;
        total += homeShaping + exploreShaping + carryLegShaping;

        return new ArenaResult(total, pickups, unloads);
    }

    /// <summary>
    /// Posición de comida con el mecanismo del mundo (muestreo uniforme del mundo,
    /// rechazo si viola la banda de distancia de la etapa; la banda es siempre
    /// ≥ NestMinSpawnDistance). Reserva determinista al este del nido si 64 intentos
    /// no caen en la banda (bandas sanas: probabilidad de fallo &lt; 1e-4).
    /// </summary>
    private (float X, float Y) DrawFoodPosition(Colony colony)
    {
        float worldSize = EffectiveGridCells * SimConstants.CellSizeUnits;
        float min2 = _minDistance * _minDistance;
        float max2 = _maxDistance * _maxDistance;
        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            float x = (float)(_trialRng.NextDouble01() * worldSize);
            float y = (float)(_trialRng.NextDouble01() * worldSize);
            float dx = x - colony.NestX;
            float dy = y - colony.NestY;
            float d2 = dx * dx + dy * dy;
            if (d2 >= min2 && d2 <= max2)
                return (x, y);
        }
        float d = (_minDistance + _maxDistance) * 0.5f;
        return (Math.Clamp(colony.NestX + d, 0f, worldSize), colony.NestY);
    }
}
