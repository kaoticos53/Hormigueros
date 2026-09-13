using System;
using System.Collections.Generic;
using AntSim.Core.Contracts;
using AntSim.Core.Sim;

namespace AntSim.Core.World;

/// <summary>
/// Construcción de los 19 canales de <see cref="AntSensors"/> desde el estado
/// del mundo. La sonda de olfato usa 3 puntos (centro y antenas ±ángulo) sobre
/// las capas de feromonas de la PROPIA colonia con interpolación bilineal.
/// </summary>
public static class AntSenses
{
    /// <summary>Versión clásica (F1–F5.2a): canal 12 ProxFront = pared.
    /// Los cuatro pines de hash de CI se fijan con ESTA vía.</summary>
    public static AntSensors Build(Colony c, Ant a, IReadOnlyList<FoodItem> items,
        float worldWidth, float worldHeight)
        => Build(c, a, items, worldWidth, worldHeight, rivals: null);

    /// <summary>
    /// F5.2b.2: con `rivals` no nulo (especie beligerante en mundo multi-colonia),
    /// el canal 12 ProxFront se reconvierte a «hormiga enemiga más cercana»:
    /// 1 − dist/VisionRadius. Canales 11/13 siguen midiendo la pared (los bordes
    /// siguen siendo el único obstáculo físico). El GATING es de la llamada:
    /// un mundo de una colonia o sin especie beligerante pasa null ⇒ bytes
    /// idénticos al build anterior (pines de CI intactos).
    /// </summary>
    public static AntSensors Build(Colony c, Ant a, IReadOnlyList<FoodItem> items,
        float worldWidth, float worldHeight, IReadOnlyList<Colony>? rivals)
    {
        SpeciesDescriptor sp = c.Species;
        float reach = sp.SensorReach * a.SensorScale;
        float sinH = MathF.Sin(a.Heading);
        float cosH = MathF.Cos(a.Heading);

        // Puntos de la sonda (marco local: +X = rumbo)
        float cx = a.X + cosH * reach;
        float cy = a.Y + sinH * reach;
        float angL = a.Heading + sp.SenseAngle;
        float angR = a.Heading - sp.SenseAngle;
        float lx = a.X + MathF.Cos(angL) * reach;
        float ly = a.Y + MathF.Sin(angL) * reach;
        float rx = a.X + MathF.Cos(angR) * reach;
        float ry = a.Y + MathF.Sin(angR) * reach;

        var s = AntSensors.Default();

        // — Olfato (capas de la propia colonia) —
        float foodC = Read(c.FoodLayer, cx, cy);
        float foodL = Read(c.FoodLayer, lx, ly);
        float foodR = Read(c.FoodLayer, rx, ry);
        float homeC = Read(c.HomeLayer, cx, cy);
        float homeL = Read(c.HomeLayer, lx, ly);
        float homeR = Read(c.HomeLayer, rx, ry);
        float alarmC = Read(c.AlarmLayer, cx, cy);
        float alarmL = Read(c.AlarmLayer, lx, ly);
        float alarmR = Read(c.AlarmLayer, rx, ry);

        s.FoodTrailCenter = foodC;
        s.FoodTrailDiff = Math.Clamp(foodL - foodR, -1f, 1f);
        s.HomeTrailCenter = homeC;
        s.HomeTrailDiff = Math.Clamp(homeL - homeR, -1f, 1f);
        s.AlarmCenter = alarmC;
        s.AlarmDiff = Math.Clamp(alarmL - alarmR, -1f, 1f);

        // — Comida visible: ítem más cercano dentro de rV —
        FoodItem? nearest = null;
        float bestDist2 = float.MaxValue;
        float vision = sp.VisionRadius * a.SensorScale;
        float vision2 = vision * vision;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            float dx = it.X - a.X;
            float dy = it.Y - a.Y;
            float d2 = dx * dx + dy * dy;
            if (d2 < vision2 && d2 < bestDist2)
            {
                bestDist2 = d2;
                nearest = it;
            }
        }
        if (nearest != null)
        {
            float dist = MathF.Sqrt(bestDist2);
            if (dist > 1e-4f)
            {
                // Vector al ítem en el marco LOCAL (X = rumbo, Y = izquierda), igual
                // que la brújula: el cerebro ve "adelante/detrás" y "izquierda/derecha".
                float dx = (nearest.X - a.X) / dist;
                float dy = (nearest.Y - a.Y) / dist;
                s.FoodDx = Math.Clamp(dx * cosH + dy * sinH, -1f, 1f);
                s.FoodDy = Math.Clamp(-dx * sinH + dy * cosH, -1f, 1f);
            }
            s.FoodSize = nearest.SizeNormalized;
        }

        // — Brújula innata al nido (vector local) —
        float dxN = c.NestX - a.X;
        float dyN = c.NestY - a.Y;
        float distN = MathF.Sqrt(dxN * dxN + dyN * dyN);
        if (distN > 1e-4f)
        {
            float fwd = dxN / distN * cosH + dyN / distN * sinH;
            float left = -dxN / distN * sinH + dyN / distN * cosH;
            s.HomeDx = Math.Clamp(fwd, -1f, 1f);
            s.HomeDy = Math.Clamp(left, -1f, 1f);
        }

        // — Obstáculos (en Fase 1: solo los bordes del mundo) —
        float dWall = MathF.Min(MathF.Min(a.X, worldWidth - a.X),
                                MathF.Min(a.Y, worldHeight - a.Y));
        float prox = Math.Clamp(1f - dWall / reach, 0f, 1f);
        s.ProxLeft = prox;
        // F5.2b.2: con rivales, el frontal mide la presa, no la pared.
        s.ProxFront = prox;
        s.ProxRight = prox;
        if (rivals != null)
        {
            float visionRival = sp.VisionRadius * a.SensorScale;
            float bestD2 = visionRival * visionRival;
            bool found = false;
            for (int rc = 0; rc < rivals.Count; rc++)
            {
                var rival = rivals[rc];
                if (rival.Id == c.Id) continue;
                for (int i = 0; i < rival.Adults.Count; i++)
                {
                    var enemy = rival.Adults[i];
                    if (!enemy.Alive) continue;
                    float dx = enemy.X - a.X;
                    float dy = enemy.Y - a.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < bestD2) { bestD2 = d2; found = true; }
                }
            }
            if (found)
                s.ProxFront = Math.Clamp(1f - MathF.Sqrt(bestD2) / visionRival, 0f, 1f);
        }

        // — Carga y estado —
        s.HasLoad = a.HasLoad ? 1f : 0f;
        s.LoadFraction = a.HasLoad ? Math.Clamp(a.LoadValue / FoodItem.MaxValue, 0f, 1f) : 0f;
        s.Energy = Math.Clamp(a.Energy, 0f, 1f);
        s.AgeNormalized = Math.Clamp(a.Age / Math.Max(a.Lifespan, 1e-4f), 0f, 1f);
        s.ColonyFoodRatio = Math.Clamp(c.Stock / c.StockMax, 0f, 1f);

        return s;
    }

    /// <summary>Lectura bilineal de una capa en coordenadas de mundo (celda 8 u).</summary>
    private static float Read(Pheromone.PheromoneLayer layer, float worldX, float worldY)
    {
        float fx = worldX / SimConstants.CellSizeUnits - 0.5f;
        float fy = worldY / SimConstants.CellSizeUnits - 0.5f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;
        float v00 = layer[x0, y0];
        float v10 = layer[x0 + 1, y0];
        float v01 = layer[x0, y0 + 1];
        float v11 = layer[x0 + 1, y0 + 1];
        float top = v00 + (v10 - v00) * tx;
        float bottom = v01 + (v11 - v01) * tx;
        return top + (bottom - top) * ty;
    }
}