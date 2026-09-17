using System;
using System.Collections.Generic;
using AntSim.Core.Evolution;
using AntSim.Core.World;

namespace AntSim.Core.Telemetry;

/// <summary>Punto de la curva de aprendizaje: lo que dejó una generación.</summary>
public readonly record struct GenerationPoint(int Generation, int Ants, double Mean, double Best);

/// <summary>
/// Aprendizaje de una colonia (canal C, F5.3ter): la curva de fitness por
/// GENERACIÓN y la COBERTURA del mundo que esa colonia ha pisado.
/// </summary>
public sealed class ColonyLearning
{
    public int ColonyId { get; init; }
    /// <summary>Generación EN CURSO (0 = las fundadoras y su primera cohorte).</summary>
    public int Generation { get; init; }
    /// <summary>Nacimientos contados desde la fundación (el reloj de las generaciones).</summary>
    public int Births { get; init; }
    /// <summary>Hormigas nacidas en la generación en curso (vivas + caídas).</summary>
    public int GenerationAnts { get; init; }
    /// <summary>De esas, las que ya cayeron (el resto sigue sumando fitness).</summary>
    public int GenerationDead { get; init; }
    /// <summary>Fitness medio de la generación en curso (vivas + caídas, cada una con su valor actual).</summary>
    public double GenerationMean { get; init; }
    public double GenerationBest { get; init; }
    /// <summary>Mejor genoma del pool: el aprendizaje ACUMULADO (no de una cohorte).</summary>
    public double EliteBest { get; init; }
    public double EliteAverage { get; init; }
    /// <summary>Celdas del grid con huella CHC &gt; 0 (el mundo que esa colonia conoce).</summary>
    public int VisitedCells { get; init; }
    public int TotalCells { get; init; }
    public float CoverageFraction { get; init; }
    /// <summary>Distancia máxima al nido alcanzada por una hormiga viva (u).</summary>
    public float MaxDistanceFromNest { get; init; }
    /// <summary>La curva: una entrada por generación con hormigas (incluida la en curso).</summary>
    public IReadOnlyList<GenerationPoint> Curve { get; init; } = Array.Empty<GenerationPoint>();
}

/// <summary>
/// F5.3ter — aprendizaje observable (canal C): «¿está mejorando esta colonia, y
/// cuánto mundo conoce?».
///
/// POR QUÉ. El modo evolución es el corazón del juego, y hasta ahora el HUD podía
/// enseñar reserva, crías y alertas, pero no el APRENDIZAJE. Las tres piezas que
/// sí se pueden medir sin instrumentar el cerebro:
///
///   1. **Generación**: no hay un contador de generaciones en el mundo (la
///      evolución es continua), así que se define por COHORTES de nacimiento —
///      <see cref="BirthsPerGeneration"/> = capacidad de la élite (64). Ese es el
///      número de nacimientos tras el cual toda la élite ha tenido ocasión de ser
///      reemplazada: es la definición operativa de «una generación» en un pool de
///      tamaño K, y es exactamente la del módulo de evolución del pool.
///   2. **Fitness por generación**: la fitness de POR VIDA de las hormigas que
///      mueren, atribuida a la generación en la que nacieron. La media de la
///      cohorte subiendo = el aprendizaje está funcionando; con solo los vivos se
///      mediría supervivencia (sesgo de selección), así que se cierra con las
///      bajas. El fitness de cada hormiga se sigue tick a tick y se sella con el
///      último valor observado (±1 tick: la compactación F5.3 retira al muerto
///      del mundo en el mismo tick en que cae, así que su valor final ya no es
///      legible). Es el único coste real del tracker y es O(hormigas vivas).
///   3. **Cobertura del mundo**: fracción de celdas con huella CHC &gt; 0 — la
///      traza que la colonia deja al andar (F5.3). Sale gratis porque la huella
///      ya existe, y responde a la pregunta que el jugador hace al mirar el mapa:
///      «¿ya conoce su mundo o sigue dando vueltas por el mismo pasillo?». El
///      escaneo de la capa se hace SOLO al leer (1 Hz), nunca por tick.
///
/// Telemetría pura: lee el mundo, nunca lo escribe ni consume RNG (los pines de
/// hash no se mueven; hay test).
/// </summary>
public sealed class LearningTracker
{
    /// <summary>Nacimientos por generación = capacidad de la élite del pool:
    /// tras 64 nacimientos toda la élite ha podido ser reemplazada.</summary>
    public const int BirthsPerGeneration = GenomePool.EliteCapacity;

    /// <summary>Generaciones que la curva retiene (las últimas; la cola inicial
    /// de una partida larga no explica la decisión de ahora).</summary>
    public const int MaxCurvePoints = 48;

    private sealed class Ref
    {
        public int Colony;
        public int Generation;
        public double Fitness;
        public ulong SeenTick = ulong.MaxValue;
    }

    /// <summary
    /// Acumulador por generación. La fitness se sigue con DELTAS: el valor vivo
    /// de cada hormiga se suma a su generación a medida que crece y, al morir, se
    /// traspasa de «vivas» a «caídas» sin duplicar ni perder nada (conservación
    /// exacta). Así la media de una generación es la de TODAS sus hormigas —no
    /// solo las supervivientes— desde el primer tick, y la curva empieza a moverse
    /// mucho antes de la primera baja (a 9000 ticks las fundadoras de Lasius aún
    /// viven: una curva que esperara muertes estaría vacía toda la partida corta).
    /// </summary>
    private sealed class GenStats
    {
        public double SumAlive;
        public double SumDead;
        public int Alive;
        public int Dead;
        public double Best;
        public int Total => Alive + Dead;
        public double Mean => Total > 0 ? (SumAlive + SumDead) / Total : 0.0;
    }

    private sealed class ColonyStats
    {
        public int Births;
        public readonly List<GenStats> Gens = new();
        public float MaxDistSq;
    }

    private readonly Dictionary<uint, Ref> _ants = new();
    private readonly Dictionary<int, ColonyStats> _cols = new();
    private readonly List<uint> _settleScratch = new();

    /// <summary>Nacimientos totales contados (todas las colonias).</summary>
    public int TotalBirths { get; private set; }

    /// <summary>
    /// Un paso del mundo: registra nacimientos (asigna generación), actualiza el
    /// fitness visto de cada viva, la distancia máxima al nido y —al desaparecer
    /// una hormiga— la sella en la generación en la que nació.
    /// </summary>
    public void Observe(WorldSim sim)
    {
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var colony = sim.Colonies[c];
            var stats = StatsFor(colony.Id);
            var adults = colony.Adults;
            for (int i = 0; i < adults.Count; i++)
            {
                var ant = adults[i];
                if (!ant.Alive) continue;

                if (!_ants.TryGetValue(ant.Id, out var r))
                {
                    int gen = stats.Births / BirthsPerGeneration;
                    stats.Births++;
                    TotalBirths++;
                    var g = Grow(stats, gen);
                    g.Alive++;
                    _ants[ant.Id] = r = new Ref { Colony = colony.Id, Generation = gen };
                }
                // Delta del fitness vivo: lo que creció desde la última mirada.
                var genStats = stats.Gens[r.Generation];
                genStats.SumAlive += ant.Fitness - r.Fitness;
                if (ant.Fitness > genStats.Best) genStats.Best = ant.Fitness;
                r.Fitness = ant.Fitness;
                r.SeenTick = sim.Tick;

                float dx = ant.X - colony.NestX;
                float dy = ant.Y - colony.NestY;
                float d2 = dx * dx + dy * dy;
                if (d2 > stats.MaxDistSq) stats.MaxDistSq = d2;
            }
        }

        // Bajas: el que no se haya visto en este tick ya no está (muerto y
        // compactado). Su fitness final se atribuye a SU generación.
        _settleScratch.Clear();
        foreach (var kv in _ants)
            if (kv.Value.SeenTick != sim.Tick) _settleScratch.Add(kv.Key);
        for (int i = 0; i < _settleScratch.Count; i++)
        {
            var r = _ants[_settleScratch[i]];
            var g = Grow(StatsFor(r.Colony), r.Generation);
            // Traspaso vivo→caída: el valor final sale de «vivas» y entra en
            // «caídas» (misma suma, otra cuenta).
            g.SumAlive -= r.Fitness;
            g.Alive--;
            g.SumDead += r.Fitness;
            g.Dead++;
            if (r.Fitness > g.Best) g.Best = r.Fitness;
            _ants.Remove(_settleScratch[i]);
        }
    }

    /// <summary>
    /// Lectura para el canal C (1 Hz): curva + cobertura. Escanea la capa de
    /// huella de cada colonia para contar celdas pisadas — por eso NO se llama
    /// por tick.
    /// </summary>
    public IReadOnlyList<ColonyLearning> Snapshot(WorldSim sim)
    {
        var list = new List<ColonyLearning>(sim.Colonies.Count);
        for (int c = 0; c < sim.Colonies.Count; c++)
        {
            var colony = sim.Colonies[c];
            var stats = StatsFor(colony.Id);

            var layer = colony.FootprintLayer;
            int total = layer.Width * layer.Height;
            int visited = 0;
            for (int y = 0; y < layer.Height; y++)
                for (int x = 0; x < layer.Width; x++)
                    if (layer[x, y] > 0f) visited++;

            int gen = stats.Births / BirthsPerGeneration;
            var curve = new List<GenerationPoint>(Math.Min(stats.Gens.Count, MaxCurvePoints));
            int first = Math.Max(0, stats.Gens.Count - MaxCurvePoints);
            for (int g = first; g < stats.Gens.Count; g++)
            {
                var gs = stats.Gens[g];
                if (gs.Total <= 0) continue;
                curve.Add(new GenerationPoint(g, gs.Total, gs.Mean, gs.Best));
            }
            var current = stats.Gens.Count > gen ? stats.Gens[gen] : new GenStats();

            list.Add(new ColonyLearning
            {
                ColonyId = colony.Id,
                Generation = gen,
                Births = stats.Births,
                GenerationAnts = current.Total,
                GenerationDead = current.Dead,
                GenerationMean = current.Mean,
                GenerationBest = current.Best,
                EliteBest = colony.Pool.BestFitness,
                EliteAverage = colony.Pool.AvgFitness,
                VisitedCells = visited,
                TotalCells = total,
                CoverageFraction = total > 0 ? (float)visited / total : 0f,
                MaxDistanceFromNest = MathF.Sqrt(stats.MaxDistSq),
                Curve = curve,
            });
        }
        return list;
    }

    public void Reset()
    {
        _ants.Clear();
        _cols.Clear();
        TotalBirths = 0;
    }

    private ColonyStats StatsFor(int colonyId)
    {
        if (!_cols.TryGetValue(colonyId, out var s))
            _cols[colonyId] = s = new ColonyStats();
        return s;
    }

    private static GenStats Grow(ColonyStats s, int gen)
    {
        while (s.Gens.Count <= gen) s.Gens.Add(new GenStats());
        return s.Gens[gen];
    }
}
