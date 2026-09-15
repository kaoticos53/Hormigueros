using System;
using System.Globalization;
using AntSim.Core.Telemetry;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// Contrato de los presets del selector de pools (F4.5): los valores centinela
/// del benchmark de referencia Fase 3ter deben coincidir con los documentos —
/// si alguien regenera el benchmark con otro mundo, estos tests lo obligan a
/// actualizar las tarjetas (la UI nunca muestra números que el repo no respalda).
/// </summary>
public sealed class PoolPresetsTests
{
    [Fact]
    public void SeisPresets_OrdenEstable_RecomendadosPrimero()
    {
        Assert.Equal(7, PoolPresets.All.Count);
        Assert.Equal(new[] { "naturalista", "warm-v2", "warm-4", "warm3", "warm3-v2", "warm-5", "neat-cierre" },
            new[] { PoolPresets.All[0].Id, PoolPresets.All[1].Id, PoolPresets.All[2].Id,
                    PoolPresets.All[3].Id, PoolPresets.All[4].Id, PoolPresets.All[5].Id, PoolPresets.All[6].Id });

        // Los 4 recomendados del diseño de UX van primero y sin TradeOff.
        var rec = new System.Collections.Generic.List<string>();
        foreach (var p in PoolPresets.Recommended) rec.Add(p.Id);
        Assert.Equal(new[] { "naturalista", "warm-v2", "warm-4", "warm3" }, rec);
        foreach (var p in PoolPresets.All)
        {
            if (p.IsRecommended) Assert.Equal("", p.TradeOff);
            else Assert.True(p.TradeOff.Length > 20); // contrapartida explicada, no un adjetivo
        }
    }

    [Fact]
    public void Naturalista_SinArchivo_YMuertoEnBenchmark()
    {
        var p = PoolPresets.ById("naturalista")!;
        Assert.Null(p.GenomeFile);
        Assert.Equal(0, p.Benchmark.Pickups);
        Assert.Equal(0, p.Benchmark.Unloads);
        Assert.Equal(0, p.Benchmark.SeedsWithUnload);
    }

    [Fact]
    public void WarmV2_LosNumerosDelBenchmark()
    {
        var p = PoolPresets.ById("warm-v2")!;
        Assert.Equal("artifacts/pretrain-warm-v2.antgenome", p.GenomeFile);
        Assert.Equal(99, p.Benchmark.Pickups);
        Assert.Equal(17, p.Benchmark.Unloads);
        Assert.Equal(10, p.Benchmark.SeedsWithUnload);
        Assert.Equal(10, p.Benchmark.SeedsTotal);
        Assert.Equal(167.9f, p.Benchmark.DropAvg!.Value, 1);
        Assert.Equal(80.0f, p.Benchmark.CarryLegMean!.Value, 1);
        Assert.NotNull(p.GameMode);
        Assert.Equal(5, p.GameMode!.Value.SeedsWithUnload);
        Assert.Equal(5, p.GameMode.Value.SeedsTotal);
    }

    [Fact]
    public void Warm4_CoberturaMundoCompleto()
    {
        var p = PoolPresets.ById("warm-4")!;
        Assert.Equal("artifacts/pretrain-warm-4.antgenome", p.GenomeFile);
        Assert.Equal(106, p.Benchmark.Pickups);
        Assert.Equal(14, p.Benchmark.Unloads);
        Assert.Equal(7, p.Benchmark.SeedsWithUnload);
        Assert.Equal(5, p.GameMode!.Value.SeedsWithUnload); // 5/5 en mundo grande
        Assert.Contains("700", p.Band);
    }

    [Fact]
    public void Warm3V2_EspecialistaBandaAnchaSana()
    {
        var p = PoolPresets.ById("warm3-v2")!;
        Assert.False(p.IsRecommended);
        Assert.Equal(88, p.Benchmark.Pickups);
        Assert.Equal(18, p.Benchmark.Unloads);
        Assert.Equal(10, p.Benchmark.SeedsWithUnload);
        Assert.Equal(164.7f, p.Benchmark.DropAvg!.Value, 1);   // el más sano de todos
        Assert.Equal(61.6f, p.Benchmark.CarryLegMean!.Value, 1);
        Assert.Equal(5, p.GameMode!.Value.SeedsWithUnload);
        // La tarjeta del especialista lleva su contrapartida visible.
        Assert.Contains("⚠", PoolPresets.FormatCard(p));
        Assert.Contains("warm3", p.TradeOff);
    }

    [Fact]
    public void Warm5_EspecialistaMundoPequeno()
    {
        var p = PoolPresets.ById("warm-5")!;
        Assert.False(p.IsRecommended);
        Assert.Equal(118, p.Benchmark.Pickups);               // récord absoluto
        Assert.Equal(17, p.Benchmark.Unloads);
        Assert.Equal(9, p.Benchmark.SeedsWithUnload);
        Assert.Equal(181.6f, p.Benchmark.DropAvg!.Value, 1);
        Assert.Equal(60.5f, p.Benchmark.CarryLegMean!.Value, 1);
        Assert.Equal(2, p.GameMode!.Value.SeedsWithUnload);   // decae en mundo grande
        Assert.Equal(5, p.GameMode.Value.SeedsTotal);
        Assert.Contains("2/5", PoolPresets.FormatCard(p));
        Assert.Contains("warm-4", p.TradeOff);
    }

    [Fact]
    public void Warm3_CoronaDeVolumen()
    {
        var p = PoolPresets.ById("warm3")!;
        Assert.Equal("artifacts/pretrain-warm3.antgenome", p.GenomeFile);
        Assert.Equal(107, p.Benchmark.Pickups);
        Assert.Equal(22, p.Benchmark.Unloads); // el máximo de la referencia
        Assert.Equal(10, p.Benchmark.SeedsWithUnload);
        Assert.Equal(61.1f, p.Benchmark.CarryLegMean!.Value, 1); // y el tramo más corto
        Assert.True(p.Benchmark.Unloads > PoolPresets.ById("warm-v2")!.Benchmark.Unloads);
    }

    [Fact]
    public void TodosConProcedencia_YComandoReproducible()
    {
        foreach (var p in PoolPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.SourceDoc));
            Assert.False(string.IsNullOrWhiteSpace(p.ReproCommand));
            if (p.GenomeFile != null)
                Assert.Contains("--seed-pool", p.ReproCommand);
        }
    }

    [Fact]
    public void FormatCard_MetricasVisibles()
    {
        string card = PoolPresets.FormatCard(PoolPresets.ById("warm-v2")!);
        Assert.DoesNotContain("⚠", card); // los recomendados no llevan advertencia
        Assert.Contains("99", card);      // pickups
        Assert.Contains("17", card);      // descargas
        Assert.Contains("10/10", card);   // fiabilidad
        Assert.Contains("167.9", card);   // drop-avg
        Assert.Contains("80.0", card);    // carry-leg
        // La tarjeta es culture-invariant (comparability entre procesos y saves).
        Assert.DoesNotContain(",", card);
    }

    [Fact]
    public void ById_Desconocido_DevuelveNull()
    {
        Assert.Null(PoolPresets.ById("no-existe"));
    }
}
