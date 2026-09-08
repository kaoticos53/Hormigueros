using System;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using AntSim.Core.World;

namespace AntSim.Core.Training;

/// <summary>Resultado de una evaluación de arena.</summary>
public readonly struct ArenaResult
{
    public readonly double Fitness;
    /// <summary>Competencia mínima: al menos un ciclo completo ida-vuelta (pickup + unload).</summary>
    public readonly bool Competent;

    public ArenaResult(double fitness, bool competent)
    {
        Fitness = fitness;
        Competent = competent;
    }
}

/// <summary>
/// Arena de evaluación determinista de un genoma (Fase 3, pre-entrenamiento headless):
///
/// - Una sola hormiga, una sola colonia, un solo ítem de comida en un punto fijo
///   relativo al nido; sin respawn de comida (TargetItems = 0) y sin cría que
///   distraiga al ColonyController.
/// - La arena construye un WorldSim NUEVO por evaluación con la misma semilla,
///   de modo que cada prueba parte del mismo estado inicial exacto.
/// - Recompensas re-equilibradas (Fase 3): el refuerzo de depósito y descarga
///   domina sobre la supervivencia, para que la señal de fitness sea aprendible.
/// - Determinista: mismo genoma + misma semilla ⇒ misma secuencia de pasos y el
///   mismo resultado bit a bit (verificable en CI con doble ejecución).
/// </summary>
public sealed class ArenaEvaluator
{
    public const int GridCells = 160;          // 1280 × 1280 u
    public const float FoodAmount = 4.0f;      // ep del ítem de prueba
    public const float TrailStrength = 0.9f;   // feromona sembrada en el rastro
    public const int NoProgressWindow = 1200; // ticks sin nuevo récord de distancia

    private readonly ulong _seed;
    private readonly float _foodDistance;
    private readonly int _tickBudget;
    private readonly bool _seedTrail;
    private readonly int _trials;
    private DeterministicRandom _headingRng;

    public ArenaEvaluator(ulong seed, float foodDistance, int tickBudget, bool seedTrail = true, int trials = 1)
    {
        _seed = seed;
        _foodDistance = foodDistance;
        _tickBudget = tickBudget;
        _seedTrail = seedTrail;
        _trials = Math.Max(1, trials);
        _headingRng = new DeterministicRandom(_seed);
    }

    public ArenaResult Evaluate(MlpGenome genome)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));

        // Varias pruebas con rumbo inicial distinto: un genoma "competente" lo
        // es si completa el ciclo en CUALQUIER prueba. Suaviza la lotería de
        // puntos fijos reactivos (orbita vs. forrajea) que depende del rumbo
        // inicial, sin romper el determinismo (el RNG de rumbos avanza en orden
        // fijo de pruebas).
        double bestFitness = double.MinValue;
        bool bestCompetent = false;
        for (int t = 0; t < _trials; t++)
        {
            var result = EvaluateTrial(genome);
            if (result.Fitness > bestFitness)
            {
                bestFitness = result.Fitness;
                bestCompetent = result.Competent;
            }
        }
        return new ArenaResult(bestFitness, bestCompetent);
    }

    private ArenaResult EvaluateTrial(MlpGenome genome)
    {
        var sim = new WorldSim(_seed, GridCells, 1);
        var colony = sim.Colonies[0];

        // — Re-equilibrio de la señal de fitness (Fase 3) —
        // Sin refuerzo de depósito (se podría cultivar sin forrajear); el ciclo
        // completo (pickup + descarga) es la señal dominante gracias al bonus.
        sim.RewardPickup = 0.5f;
        sim.RewardUnloadPerEp = 2.0f;
        sim.RewardSurvivalPerSecond = 0.002f;
        sim.RewardDepositPerUnit = 0.0f;
        sim.RewardUnloadBonus = 4.0f;

        // — Arena: una sola hormiga, un solo ítem, sin respawn —
        sim.TargetItems = 0;
        colony.Adults.Clear();
        colony.Eggs.Clear();
        colony.Larvae.Clear();
        colony.Pupae.Clear();
        colony.Stock = colony.StockMax;

        // Rumbo inicial aleatorio pero determinista (semilla de la arena): evita
        // el punto fijo degenerado de "empezar mirando a la comida" (muchos
        // cerebros aleatorios se enclavan orbitando en el nido porque sus
        // sensores iniciales no cambian) y es más realista: las hormigas salen
        // del nido en todas direcciones.
        var ant = new Ant
        {
            Id = 1,
            ColonyId = 0,
            X = colony.NestX,
            Y = colony.NestY,
            Heading = (float)(_headingRng.NextDouble01() * Math.PI * 2.0 - Math.PI)
        };
        ant.InitFromVigor(colony.Species.EnergyCapacity, colony.Species.BaseLifespan, 0.8f);
        ant.Genome = genome;
        ant.Brain = genome.ToBrain();
        colony.Adults.Add(ant);

        // — Comida en un punto fijo relativo al nido (+X) —
        var food = new FoodItem
        {
            Id = 1,
            X = colony.NestX + _foodDistance,
            Y = colony.NestY,
            Amount = FoodAmount
        };
        sim.ClearItems();
        sim.AddItem(food);

        // — Rastro de comida sembrado del nido a la comida (el gradiente enseña a salir) —
        if (_seedTrail)
            SeedTrail(colony, _foodDistance, 0f);

        // — Ejecución con detección de estancamiento por progreso (genoma muerto:
        // no gastar ticks). Un genoma inútil orbita el nido o se queda contra un
        // borde: su distancia máxima al nido deja de crecer y se corta la
        // evaluación. Un genoma competente completa el ciclo y después se queda
        // quieto (no hay más comida): también se corta, con el fitness ya medido.
        double maxDist = 0.0;
        int noProgress = 0;
        for (int i = 0; i < _tickBudget; i++)
        {
            sim.Step();
            if (!ant.Alive) break;

            double dx = ant.X - colony.NestX;
            double dy = ant.Y - colony.NestY;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > maxDist)
            {
                maxDist = dist;
                noProgress = 0;
            }
            else if (++noProgress >= NoProgressWindow)
            {
                break; // sin avance real durante la ventana: genoma estancado
            }
        }

        // — Competencia: al menos un ciclo completo ida-vuelta. La señal fiable
        // es el fitness acumulado de la hormiga (los eventos solo cubren el
        // último paso): un ciclo completo vale pickup + descarga + bonus.
        double cycleThreshold = sim.RewardPickup + FoodAmount * sim.RewardUnloadPerEp + sim.RewardUnloadBonus;
        bool competent = ant.Fitness >= cycleThreshold;

        return new ArenaResult(ant.Fitness, competent);
    }

    private static void SeedTrail(Colony colony, float distance, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        int steps = Math.Max(8, (int)(distance / 12f)); // una gota cada ~12 u
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float x = colony.NestX + cos * distance * t;
            float y = colony.NestY + sin * distance * t;
            colony.FoodLayer.Deposit(
                (int)Math.Clamp(x / SimConstants.CellSizeUnits, 0, colony.FoodLayer.Width - 1),
                (int)Math.Clamp(y / SimConstants.CellSizeUnits, 0, colony.FoodLayer.Height - 1),
                TrailStrength);
        }
    }
}