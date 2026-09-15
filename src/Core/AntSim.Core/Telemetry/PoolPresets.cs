using System;
using System.Collections.Generic;
using System.Globalization;

namespace AntSim.Core.Telemetry;

/// <summary>
/// Presets del selector de pools para el HUD (Fase 4, F4.5 según el diseño de
/// UX). LOS NÚMEROS SON REALES: salen del benchmark de referencia de la Fase 3ter
/// (`artifacts/benchmark-fase3ter.txt`, 10 semillas × 24 000 ticks bajo las
/// constantes finales del mundo) y de las evaluaciones de modo juego (grid 256,
/// 2 colonias, 48 000 ticks, 5 semillas). Cada preset lleva su procedencia
/// (fuente documental) — la tarjeta del HUD muestra lo que el benchmark midió,
/// no marketing. El test de contrato vigila los valores centinela.
/// </summary>
public static class PoolPresets
{
    /// <summary>Métricas de una tarjeta de pool (mundo estándar 96², benchmark 10 semillas).</summary>
    public readonly struct BenchmarkStats
    {
        public readonly int Pickups;
        public readonly int Unloads;
        public readonly int SeedsWithUnload;
        public readonly int SeedsTotal;
        public readonly float? DropAvg;      // distancia media de suelta (u)
        public readonly float? CarryLegMean; // último eslabón del relevo (u)

        public BenchmarkStats(int pickups, int unloads, int seedsWithUnload, int seedsTotal,
            float? dropAvg, float? carryLegMean)
        {
            Pickups = pickups; Unloads = unloads;
            SeedsWithUnload = seedsWithUnload; SeedsTotal = seedsTotal;
            DropAvg = dropAvg; CarryLegMean = carryLegMean;
        }
    }

    /// <summary>Métricas del modo juego en mundo grande (grid 256, 5 semillas).</summary>
    public readonly struct GameModeStats
    {
        public readonly int SeedsWithUnload;
        public readonly int SeedsTotal;
        public readonly int Unloads;

        public GameModeStats(int seedsWithUnload, int seedsTotal, int unloads)
        {
            SeedsWithUnload = seedsWithUnload; SeedsTotal = seedsTotal; Unloads = unloads;
        }
    }

    /// <summary>Un preset del selector: identidad, archivo, métricas y procedencia.
    /// Los presets RECOMENDADOS (IsRecommended) son los 4 del diseño de UX; los
    /// demás son especialistas de la cadena completa y SIEMPRE llevan su
    /// TradeOff — el picker los muestra con la contrapartida visible.</summary>
    public sealed class PoolPreset
    {
        public readonly string Id;            // estable, para saves y UI
        public readonly string DisplayName;   // tarjeta del HUD
        public readonly string Tagline;       // una línea, con datos, no adjetivos
        public readonly string? GenomeFile;   // null = naturalista (sin sembrar)
        public readonly string Band;          // banda de entrenamiento
        public readonly BenchmarkStats Benchmark;
        public readonly GameModeStats? GameMode; // null = no evaluado en grid 256
        public readonly string SourceDoc;     // procedencia: doc + artefacto
        public readonly string ReproCommand;  // cómo reproducir la partida sembrada
        public readonly bool IsRecommended;   // los 4 del diseño de UX (tarjeta estándar)
        public readonly string TradeOff;      // vacío en recomendados; contrapartida en especialistas

        public PoolPreset(string id, string displayName, string tagline, string? genomeFile,
            string band, BenchmarkStats benchmark, GameModeStats? gameMode,
            string sourceDoc, string reproCommand,
            bool isRecommended = true, string tradeOff = "")
        {
            Id = id; DisplayName = displayName; Tagline = tagline; GenomeFile = genomeFile;
            Band = band; Benchmark = benchmark; GameMode = gameMode;
            SourceDoc = sourceDoc; ReproCommand = reproCommand;
            IsRecommended = isRecommended; TradeOff = tradeOff;
        }
    }

    private const string SourceBenchmark =
        "artifacts/benchmark-fase3ter.txt (benchmark Fase 3ter regenerado, mundo final; docs/especificaciones.md §4ter)";

    private const string SourceOutsideBenchmark =
        "docs/fase3ter-resumen.md §4 (pools evaluados fuera del benchmark de referencia)";

    /// <summary>Los cuatro presets del diseño de UX (docs/fase4-diseno-ux.md §2.1).</summary>
    public static readonly IReadOnlyList<PoolPreset> All = new[]
    {
        new PoolPreset(
            id: "naturalista",
            displayName: "Naturalista (sin sembrar)",
            tagline: "Arranque en frío: la colonia fundadora no completa ciclos — así se ve el problema que los pools resuelven.",
            genomeFile: null,
            band: "—",
            benchmark: new BenchmarkStats(pickups: 0, unloads: 0, seedsWithUnload: 0, seedsTotal: 10,
                dropAvg: null, carryLegMean: null),
            gameMode: null,
            sourceDoc: SourceBenchmark + "; dead zone documentada en docs/fase3ter-resumen.md",
            reproCommand: "antsim --mode game --seed <N>"),

        new PoolPreset(
            id: "warm-v2",
            displayName: "warm-v2 · el fiable",
            tagline: "10/10 semillas con relevo y el tramo portado más sano del benchmark. El pool por defecto del modo juego.",
            genomeFile: "artifacts/pretrain-warm-v2.antgenome",
            band: "200–260 u",
            benchmark: new BenchmarkStats(pickups: 99, unloads: 17, seedsWithUnload: 10, seedsTotal: 10,
                dropAvg: 167.9f, carryLegMean: 80.0f),
            gameMode: new GameModeStats(seedsWithUnload: 5, seedsTotal: 5, unloads: 8),
            sourceDoc: SourceBenchmark + "; modo juego grid 256 en docs/especificaciones.md (densidad por área)",
            reproCommand: "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm-v2.antgenome"),

        new PoolPreset(
            id: "warm-4",
            displayName: "warm-4 · mapa completo",
            tagline: "Entrenado hasta 700 u con relevo multi-salto obligatorio: 5/5 semillas en mundo grande, sin coste en mundos pequeños.",
            genomeFile: "artifacts/pretrain-warm-4.antgenome",
            band: "200–700 u (4ª etapa mundo-completo, arena 176)",
            benchmark: new BenchmarkStats(pickups: 106, unloads: 14, seedsWithUnload: 7, seedsTotal: 10,
                dropAvg: 176.8f, carryLegMean: 62.5f),
            gameMode: new GameModeStats(seedsWithUnload: 5, seedsTotal: 5, unloads: 8),
            sourceDoc: "docs/especificaciones.md §4ter (4ª etapa) + docs/fase3ter-resumen.md; benchmark 10 semillas propio",
            reproCommand: "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm-4.antgenome"),

        new PoolPreset(
            id: "warm3",
            displayName: "warm3 · máximo volumen",
            tagline: "22 descargas en 10/10 semillas: la corona de volumen del benchmark, a cambio del tramo portado más corto.",
            genomeFile: "artifacts/pretrain-warm3.antgenome",
            band: "200–450 u",
            benchmark: new BenchmarkStats(pickups: 107, unloads: 22, seedsWithUnload: 10, seedsTotal: 10,
                dropAvg: 171.1f, carryLegMean: 61.1f),
            gameMode: new GameModeStats(seedsWithUnload: 5, seedsTotal: 5, unloads: 8),
            sourceDoc: SourceBenchmark + "; modo juego grid 256 en docs/especificaciones.md",
            reproCommand: "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm3.antgenome"),

        // — Especialistas de la cadena completa (no recomendados por defecto; TradeOff visible) —
        new PoolPreset(
            id: "warm3-v2",
            displayName: "warm3-v2 · banda ancha sana",
            tagline: "Re-entrenado bajo el mundo final: 10/10 con el drop-avg más sano de todos (164.7). No recupera la corona de descargas de warm3.",
            genomeFile: "artifacts/pretrain-warm3-v2.antgenome",
            band: "200–450 u",
            benchmark: new BenchmarkStats(pickups: 88, unloads: 18, seedsWithUnload: 10, seedsTotal: 10,
                dropAvg: 164.7f, carryLegMean: 61.6f),
            gameMode: new GameModeStats(seedsWithUnload: 5, seedsTotal: 5, unloads: 7),
            sourceDoc: SourceOutsideBenchmark + "; docs/especificaciones.md §4ter (warm3-v2)",
            reproCommand: "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm3-v2.antgenome",
            isRecommended: false,
            tradeOff: "volumen menor que warm3 (18 vs 22 descargas) — elige warm3 para volumen o warm-v2 para el tramo más sano"),

        new PoolPreset(
            id: "warm-5",
            displayName: "warm-5 · récord de forrajeo",
            tagline: "118 pickups, el mejor de todos los pools. Especialista de mundo pequeño: en mundo grande solo 2/5 semillas.",
            genomeFile: "artifacts/pretrain-warm-5.antgenome",
            band: "200–325/200–450 alternado + 200–700 (mundo-completo)",
            benchmark: new BenchmarkStats(pickups: 118, unloads: 17, seedsWithUnload: 9, seedsTotal: 10,
                dropAvg: 181.6f, carryLegMean: 60.5f),
            gameMode: new GameModeStats(seedsWithUnload: 2, seedsTotal: 5, unloads: 3),
            sourceDoc: SourceOutsideBenchmark + "; docs/especificaciones.md §4ter (quinto eslabón)",
            reproCommand: "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-warm-5.antgenome",
            isRecommended: false,
            tradeOff: "decae en mundo grande (2/5 semillas) — el híbrido + mundo-completo combinados no son gratis; usa warm-4 para mapa completo"),

        // — Pool NEAT (F5.2c cierre): grafo v2, pool entrenado por arena con
        //    mutación estructural. El grafo del cerebro es visible por canal F.
        new PoolPreset(
            id: "neat-cierre",
            displayName: "NEAT · topologías que evolucionan",
            tagline: "Pool v2 (grafos) con 64 genomas entrenados por arena + mutación estructural. Grafo visible por canal F.",
            genomeFile: "artifacts/pretrain-neat.antgenome",
            band: "200–260 u (arena 2700 ticks, 3 trials, 4 gens × 3 etapas)",
            benchmark: new BenchmarkStats(pickups: 0, unloads: 0, seedsWithUnload: 0, seedsTotal: 0,
                dropAvg: null, carryLegMean: null),
            gameMode: new GameModeStats(seedsWithUnload: 0, seedsTotal: 0, unloads: 0),
            sourceDoc: "docs/fase5-2c-neat.md rodaja 7 (cierre F5.2c); 6º pin CI check-neat-command.sh",
            reproCommand: "antsim --mode game --seed <N> --seed-pool artifacts/pretrain-neat.antgenome --species lasius,eciton",
            isRecommended: false,
            tradeOff: "pool NEAT: fundadoras con cerebro de grafo, nacimientos en frío — el grafo evoluciona en la arena del modo juego"),
    };

    /// <summary>Busca un preset por Id estable (para saves/UI). null si no existe.</summary>
    public static PoolPreset? ById(string id)
    {
        for (int i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Id, id, StringComparison.Ordinal))
                return All[i];
        return null;
    }

    /// <summary>Línea de tarjeta lista para el HUD: métricas formateadas con su semilla de referencia.
    /// Los especialistas llevan su contrapartida (TradeOff) en la tarjeta.</summary>
    public static string FormatCard(PoolPreset p)
    {
        var b = p.Benchmark;
        string seeds = $"{b.SeedsWithUnload}/{b.SeedsTotal}";
        string drop = b.DropAvg is float d ? d.ToString("0.0", CultureInfo.InvariantCulture) : "—";
        string leg = b.CarryLegMean is float l ? l.ToString("0.0", CultureInfo.InvariantCulture) : "—";
        string game = p.GameMode is GameModeStats g
            ? $" · mundo 256: {g.SeedsWithUnload}/{g.SeedsTotal} semillas"
            : "";
        string trade = p.IsRecommended ? "" : $" · ⚠ {p.TradeOff}";
        return $"{p.DisplayName} [{p.Band}] · pickups {b.Pickups} · descargas {b.Unloads} · relevo {seeds} semillas · drop {drop} u · tramo {leg} u{game}{trade}";
    }

    /// <summary>Los presets recomendados (los 4 del diseño de UX) en orden de tarjeta.</summary>
    public static IEnumerable<PoolPreset> Recommended
    {
        get
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].IsRecommended) yield return All[i];
        }
    }
}
