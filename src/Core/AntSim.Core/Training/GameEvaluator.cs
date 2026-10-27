using System;
using AntSim.Core.Evolution;
using AntSim.Core.World;

namespace AntSim.Core.Training;

/// <summary>
/// F10 — EVALUACIÓN EN LA PARTIDA REAL: el genoma se puntúa por DESCARGAS, no
/// por el fitness densado del currículo.
///
/// <para><b>Por qué existe.</b> El currículo de obstáculos (F7) selecciona por
/// un fitness de arena con términos densados (exploración, homing, carry-leg)
/// que hacen muestreable el ciclo completo, y el currículo mueve la dificultad
/// con las descargas observadas. Medido: tras 3 000 generaciones el fitness del
/// currículo subió 5,3 % y las descargas en el laberinto sin reclutamiento
/// 123 → 135 (+9,8 %), pero en la partida del lanzador —montones, rastro y una
/// colonia rival— el marcador quedó indistinguible (527 → 522). La causa es que
/// la arena no es la partida: con reclutamiento encendido el marcador lo mueven
/// los cúmulos y los reflejos de rastro, no la calidad individual de la
/// política, y el fitness densado premia conductas que ese régimen no paga.
/// Esta clase puntúa exactamente lo que el juego cuenta.</para>
///
/// <para><b>Qué replica</b> (el protocolo del lanzador, `play-game.sh`):
/// grid 96 (768 u), dos colonias —la evaluada y una rival salvaje, como la
/// partida—, laberinto generado de la semilla, reclutamiento con sus defaults
/// (8 montones de 6–10 ítems, rastro 1×QMaxFood/s a 2 100 s, seguimiento
/// 8 rad/s), comida del mundo con reposición y cría de la colonia. Ni arena
/// recortada ni comida plantada: el mundo tal cual.</para>
///
/// <para><b>Qué se puntúa.</b> Descargas (eventos <see cref="SimEventKind.Unload"/>)
/// de la colonia 0 —la que lleva el genoma—. La colonia 1 conserva su pool
/// frío: es el rival salvaje de la partida, y su presión por la comida forma
/// parte de la vara. El fitness ordena primero por descargas y desempata con
/// los pickups: <c>descargas × 1000 + pickups</c>, que da a la descarga
/// dominancia absoluta sobre cualquier desempate y a los pickups el gradiente
/// fino que la descarga sola no tiene (dos genomas con las mismas descargas
/// siguen teniendo un orden estable y significativo).</para>
///
/// <para><b>Fundadoras = el genoma.</b> Las diez fundadoras llevan clones
/// exactos del genoma evaluado (el pool también se siembra con él, así que la
/// descendencia son sus variantes mutadas y el relevo intergeneracional sigue
/// existiendo). El lanzador, en cambio, reparte mutantes del pool entre las
/// fundadoras: aquí se prefiere medir la POLÍTICA, no el sorteo de mutaciones
/// del nacimiento.</para>
///
/// <para><b>Determinismo.</b> Cada prueba usa una semilla propia derivada de la
/// semilla del genoma, así que el resultado no depende de cuántos hilos lo
/// ejecuten ni del orden de evaluación. Mismo genoma + misma semilla ⇒ mismo
/// resultado bit a bit.</para>
/// </summary>
public sealed class GameEvaluator
{
    /// <summary>Celdas del mundo de la partida del lanzador (96 = 768 u).</summary>
    public const int GridCells = 96;

    /// <summary>Colonias de la partida: la evaluada + el rival salvaje.</summary>
    public const int ColonyCount = 2;

    /// <summary>Horizonte por prueba: 24 000 ticks = 400 s de partida. Es el
    /// tramo en el que la ronda ya tiene montones visitados, rastro legible,
    /// ~40 adultas por colonia y varias generaciones de relevo; el horizonte
    /// completo del lanzador (216 000) cuesta 9× y no añade señal nueva a la
    /// selección.</summary>
    public const int DefaultTicks = 24_000;

    /// <summary>Semillas por genoma. Una prueba sola es demasiado ruidosa para
    /// seleccionar (la partida tiene ~40 descargas por colonia y el rival
    /// cambia de humor con la semilla); tres promedian el ruido de la ronda sin
    /// triplicar el coste de la tirada.</summary>
    public const int DefaultTrials = 3;

    /// <summary>Peso de una descarga en el fitness. Con 1 000 ninguna suma de
    /// pickups de una partida (decenas) puede empatar con una descarga.</summary>
    public const int DownloadWeight = 1_000;

    private readonly int _ticks;
    private readonly int _trials;
    private readonly int _gridCells;
    private readonly int _colonyCount;
    private readonly bool _trailByBrain;
    private readonly float _trailGlandSeconds;
    private readonly float _trailGlandRefillSeconds;

    /// <param name="trailByBrain">F11: la partida evaluación abre el rastro con el
    /// cerebro (<see cref="WorldSim.RecruitmentTrailByBrain"/>) en vez de con el
    /// reflejo fijo. Es la variable del experimento «¿decide la red cuándo dejar
    /// feromona?»: mismo mundo, mismas semillas, una constante menos.</param>
    /// <param name="trailGlandSeconds">F11b: precio del rastro —segundos de
    /// depósito a plena tasa por glándula (0 = gratis)—. La evaluación tiene que
    /// correr con el MISMO precio que el entrenamiento, o no mide lo mismo.</param>
    /// <param name="trailGlandRefillSeconds">F11b: segundos de recarga de la
    /// glándula dentro del nido.</param>
    public GameEvaluator(int ticks = DefaultTicks, int trials = DefaultTrials,
        int gridCells = GridCells, int colonyCount = ColonyCount, bool trailByBrain = false,
        float trailGlandSeconds = 0f, float trailGlandRefillSeconds = 3f)
    {
        _trailByBrain = trailByBrain;
        if (trailGlandSeconds < 0f || trailGlandSeconds > RecruitmentTuning.MaxTrailGland)
            throw new ArgumentOutOfRangeException(nameof(trailGlandSeconds),
                $"La glándula exige [0, {RecruitmentTuning.MaxTrailGland}] s (0 = sin precio).");
        if (trailGlandRefillSeconds < 0.05f || trailGlandRefillSeconds > RecruitmentTuning.MaxTrailGlandRefill)
            throw new ArgumentOutOfRangeException(nameof(trailGlandRefillSeconds),
                $"La recarga exige [0.05, {RecruitmentTuning.MaxTrailGlandRefill}] s.");
        _trailGlandSeconds = trailGlandSeconds;
        _trailGlandRefillSeconds = trailGlandRefillSeconds;
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks), "El horizonte exige ≥ 1 tick.");
        if (trials < 1) throw new ArgumentOutOfRangeException(nameof(trials), "Hacen falta ≥ 1 prueba por genoma.");
        if (gridCells < 16) throw new ArgumentOutOfRangeException(nameof(gridCells), "El grid exige ≥ 16 celdas.");
        if (colonyCount < 1) throw new ArgumentOutOfRangeException(nameof(colonyCount), "Hacen falta ≥ 1 colonia.");
        _ticks = ticks;
        _trials = trials;
        _gridCells = gridCells;
        _colonyCount = colonyCount;
    }

    /// <summary>Ticks por prueba (diagnóstico y reporte).</summary>
    public int Ticks => _ticks;

    /// <summary>Semillas por genoma (diagnóstico y reporte).</summary>
    public int Trials => _trials;

    /// <summary>F11: ¿la partida de evaluación deja el rastro a la red?</summary>
    public bool TrailByBrain => _trailByBrain;

    /// <summary>F11b: precio del rastro en segundos de glándula (0 = gratis).</summary>
    public float TrailGlandSeconds => _trailGlandSeconds;

    /// <summary>F11b: segundos de recarga de la glándula en el nido.</summary>
    public float TrailGlandRefillSeconds => _trailGlandRefillSeconds;

    /// <summary>
    /// Evalúa un genoma en <see cref="Trials"/> partidas. Devuelve el fitness
    /// MEDIO (descargas × 1000 + pickups) y, redondeados, los pickups y las
    /// descargas medios — un promedio es más honesto que el mejor sorteo para
    /// seleccionar por un marcador de partida.
    /// </summary>
    public ArenaResult Evaluate(ulong seed, MlpGenome genome)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));

        double fitness = 0.0;
        int pickups = 0, unloads = 0;
        for (int t = 0; t < _trials; t++)
        {
            ulong trialSeed = unchecked(seed + (ulong)t * 0x9E3779B97F4A7C15UL);
            var (u, p) = RunTrial(trialSeed, genome);
            unloads += u;
            pickups += p;
            fitness += u * (double)DownloadWeight + p;
        }
        return new ArenaResult(fitness / _trials,
            (int)Math.Round(pickups / (double)_trials),
            (int)Math.Round(unloads / (double)_trials));
    }

    /// <summary>Las perillas del reclutamiento de la evaluación: los defaults del
    /// lanzador y, si toca, el rastro abierto por el cerebro (F11).</summary>
    private RecruitmentTuning TrailTuning()
    {
        var tuning = RecruitmentTuning.Default;
        tuning.TrailByBrain = _trailByBrain;
        tuning.TrailGlandSeconds = _trailGlandSeconds;
        tuning.TrailGlandRefillSeconds = _trailGlandRefillSeconds;
        return tuning;
    }

    /// <summary>Una partida. Devuelve (descargas, pickups) de la colonia 0.</summary>
    private (int Unloads, int Pickups) RunTrial(ulong seed, MlpGenome genome)
    {
        var sim = new WorldSim(seed, _gridCells, _colonyCount,
            maze: true,                                  // el terreno de la partida
            brainSizes: genome.Sizes,                    // el ancho del cerebro evaluado
            recruitment: true,                           // cúmulos + reflejos de rastro
            recruitmentTuning: TrailTuning());
        var colony = sim.Colonies[0];

        // El pool se siembra con el genoma (la descendencia son sus variantes) y
        // las fundadoras lo portan ENTERO: la prueba mide la política, no el
        // sorteo de mutaciones del nacimiento.
        sim.SeedPoolFromGenomes(0, new[] { genome });
        for (int i = 0; i < colony.Adults.Count; i++)
        {
            var ant = colony.Adults[i];
            if (!ant.Alive) continue;
            ant.Genome = genome.Clone();
            ant.Brain = ant.Genome.ToBrain();
        }

        int unloads = 0, pickups = 0;
        for (int i = 0; i < _ticks; i++)
        {
            sim.Step();
            var events = sim.LastEvents;
            for (int e = 0; e < events.Count; e++)
            {
                if (events[e].ColonyId != 0) continue; // el rival no puntúa
                if (events[e].Kind == SimEventKind.Unload) unloads++;
                else if (events[e].Kind == SimEventKind.Pickup) pickups++;
            }
        }
        return (unloads, pickups);
    }
}
