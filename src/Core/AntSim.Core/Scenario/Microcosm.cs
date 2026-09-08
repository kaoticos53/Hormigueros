using System;
using System.Text;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Pheromone;
using AntSim.Core.Serialization;
using AntSim.Core.Sim;
using AntSim.Core.Validation;

namespace AntSim.Core.Scenario;

/// <summary>
/// Microcosmos determinista de la Fase 0: un harness headless que combina los
/// cimientos (RNG por colonia, feromonas con evaporación/difusión, cerebros MLP
/// y validación de decisiones) y produce una salida textual reproducible.
///
/// Garantía: misma semilla + mismos parámetros ⇒ misma cadena exacta de salida.
/// Es la semilla de los tests de hash de estado y del modo verificación (Fase 3).
/// </summary>
public static class Microcosm
{
    /// <summary>Cada cuántos ticks se emite un hash de hito.</summary>
    public const int MilestoneEvery = 120;

    public static string Run(ulong seed, int ticks, int grid = 96, int colonies = 2)
    {
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (grid < 8) throw new ArgumentOutOfRangeException(nameof(grid));

        var sb = new StringBuilder();
        sb.Append("seed ").Append(seed)
          .Append(" ticks ").Append(ticks)
          .Append(" grid ").Append(grid)
          .Append(" colonies ").Append(colonies)
          .AppendLine();

        // — Flujos RNG: mundo → un flujo por colonia (Fork) —
        var world = new DeterministicRandom(seed);
        var colonyRng = new DeterministicRandom[colonies];
        for (int c = 0; c < colonies; c++)
            colonyRng[c] = world.Fork(0x9E3779B97F4A7C15UL + (ulong)c * 0xBF58476D1CE4E5B9UL);

        // — Capas de feromonas por colonia (Food + Home) —
        var food = new PheromoneLayer[colonies];
        var home = new PheromoneLayer[colonies];
        for (int c = 0; c < colonies; c++)
        {
            food[c] = new PheromoneLayer(grid, grid);
            home[c] = new PheromoneLayer(grid, grid);
        }

        // — Cerebro fijo con pesos deterministas (del flujo del mundo) —
        int[] sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };
        int weightCount = MlpBrain.ExpectedWeightCount(sizes);
        var weights = new float[weightCount];
        for (int i = 0; i < weightCount; i++)
            weights[i] = (float)(world.NextDouble01() * 2.0 - 1.0);
        var brain = new MlpBrain(sizes, weights);

        // — Estado por colonia: posición de paseo (ant), nido, acumuladores —
        var walkX = new int[colonies];
        var walkY = new int[colonies];
        var nestX = new int[colonies];
        var nestY = new int[colonies];
        var sumDecision = new float[colonies * AntDecision.DecisionCount];
        var invalidCount = new int[colonies];
        var milestoneCount = 0;

        for (int c = 0; c < colonies; c++)
        {
            walkX[c] = colonyRng[c].NextInt(0, grid);
            walkY[c] = colonyRng[c].NextInt(0, grid);
            // Nidos en zonas separadas del grid.
            nestX[c] = (int)((grid * 0.25f) * (c + 1));
            nestY[c] = (int)(grid * 0.5f);
        }

        for (int tick = 0; tick < ticks; tick++)
        {
            for (int c = 0; c < colonies; c++)
            {
                var rng = colonyRng[c];

                // Paseo aleatorio determinista (la "hormiga" del microcosmos).
                walkX[c] = Math.Clamp(walkX[c] + rng.NextInt(-1, 2), 0, grid - 1);
                walkY[c] = Math.Clamp(walkY[c] + rng.NextInt(-1, 2), 0, grid - 1);

                // Deposita rastro de comida al andar y olor de casa en el nido.
                food[c].Deposit(walkX[c], walkY[c], 0.6f * SimConstants.FixedDtSeconds);
                home[c].Deposit(nestX[c], nestY[c], 0.5f * SimConstants.FixedDtSeconds);

                // Actualización periódica (cada 10 ticks): evaporación + difusión.
                if (tick % 10 == 0)
                {
                    food[c].Evaporate(SimConstants.FixedDtSeconds * 10f, PheromoneDefaults.LambdaPerSecond(PheromoneKind.FoodTrail));
                    food[c].Diffuse(0.10f);
                    home[c].Evaporate(SimConstants.FixedDtSeconds * 10f, PheromoneDefaults.LambdaPerSecond(PheromoneKind.Home));
                    home[c].Diffuse(0.10f);
                }

                // — Sensores (sonda simplificada de 1 punto + diferencia lateral) —
                int x = walkX[c];
                int y = walkY[c];
                var sensors = AntSensors.Default();
                sensors.FoodTrailCenter = food[c][x, y];
                sensors.FoodTrailDiff = food[c][x - 1, y] - food[c][x + 1, y];
                sensors.HomeTrailCenter = home[c][x, y];
                sensors.HomeTrailDiff = home[c][x - 1, y] - home[c][x + 1, y];
                sensors.HomeDx = (nestX[c] - x) / (float)grid;
                sensors.HomeDy = (nestY[c] - y) / (float)grid;
                sensors.Energy = 0.8f;
                sensors.AgeNormalized = 0.2f;
                sensors.ColonyFoodRatio = 0.5f;

                // — Cerebro + validación —
                var decision = AntDecision.Neutral();
                brain.Evaluate(in sensors, ref decision);
                bool valid = DecisionValidator.SanitizeAndClamp(in decision, out decision);
                if (!valid) invalidCount[c]++;

                int baseIdx = c * AntDecision.DecisionCount;
                sumDecision[baseIdx + 0] += decision.Steer;
                sumDecision[baseIdx + 1] += decision.Speed;
                sumDecision[baseIdx + 2] += decision.DepositFood;
                sumDecision[baseIdx + 3] += decision.DepositHome;
                sumDecision[baseIdx + 4] += decision.DepositAlarm;
                sumDecision[baseIdx + 5] += decision.Interact;
            }

            // — Hash de hito cada MilestoneEvery ticks —
            if (tick > 0 && tick % MilestoneEvery == 0)
            {
                sb.AppendLine(BuildHashLine(seed, tick, colonyRng, food, home, walkX, walkY,
                    sumDecision, invalidCount, colonies));
                milestoneCount++;
            }
        }

        sb.AppendLine(BuildHashLine(seed, ticks, colonyRng, food, home, walkX, walkY,
            sumDecision, invalidCount, colonies));
        sb.Append("milestones ").Append(milestoneCount).Append(" final-hash ");
        sb.Append(BuildHashLine(seed, ticks, colonyRng, food, home, walkX, walkY,
            sumDecision, invalidCount, colonies).Split(' ')[^1]);
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildHashLine(
        ulong seed, int tick,
        DeterministicRandom[] colonyRng,
        PheromoneLayer[] food, PheromoneLayer[] home,
        int[] walkX, int[] walkY,
        float[] sumDecision, int[] invalidCount, int colonies)
    {
        using var h = new CanonicalHasher();
        h.AppendUInt64(seed);
        h.AppendInt32(tick);
        for (int c = 0; c < colonies; c++)
        {
            (ulong s0, ulong s1, ulong s2, ulong s3) = colonyRng[c].State;
            h.AppendUInt64(s0); h.AppendUInt64(s1); h.AppendUInt64(s2); h.AppendUInt64(s3);
            h.AppendInt32(walkX[c]); h.AppendInt32(walkY[c]);
            h.AppendUInt64(food[c].MutationCount);
            h.AppendUInt64(home[c].MutationCount);
            h.AppendFloat(food[c].SumOfValues());
            h.AppendFloat(home[c].SumOfValues());
            h.AppendInt32(invalidCount[c]);
            for (int i = 0; i < AntDecision.DecisionCount; i++)
                h.AppendFloat(sumDecision[c * AntDecision.DecisionCount + i]);
        }
        return $"tick {tick}  {h.FinalizeHex()}";
    }
}
