using System;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.3 rodaja 3 — el plan de dibujo instanciado. Lo que se defiende aquí es que
/// el troceo NUNCA pasa del límite del motor y que el ahorro publicado (llamadas
/// de dibujo frente a una por hormiga) sale de estas funciones, no de una
/// estimación a mano.
/// </summary>
public class InstancedDrawPlanTests
{
    [Fact]
    public void Batches_SinInstancias_NoProduceLotes()
    {
        Assert.Equal(0, InstancedDrawPlan.Batches(0));
        Assert.Equal(0, InstancedDrawPlan.Batches(-3));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1023, 1)]
    [InlineData(1024, 2)]
    [InlineData(2046, 2)]
    [InlineData(2047, 3)]
    [InlineData(3000, 3)]
    public void Batches_TroceaContraElLimiteDelMotor(int instancias, int lotes) =>
        Assert.Equal(lotes, InstancedDrawPlan.Batches(instancias));

    [Fact]
    public void LosLotes_CubrenTodasLasInstanciasYNoPasanDelLimite()
    {
        const int n = 4711;
        int lotes = InstancedDrawPlan.Batches(n);
        int cubiertas = 0;
        for (int b = 0; b < lotes; b++)
        {
            int tam = InstancedDrawPlan.BatchSize(n, b);
            Assert.InRange(tam, 1, InstancedDrawPlan.BatchLimit);
            Assert.Equal(InstancedDrawPlan.BatchStart(b), cubiertas);
            cubiertas += tam;
        }
        Assert.Equal(n, cubiertas);
        Assert.Equal(0, InstancedDrawPlan.BatchSize(n, lotes)); // no hay un lote de más
    }

    [Fact]
    public void DrawCalls_SumaPorMaterial_NoMezclaMateriales()
    {
        // 1 600 hormigas repartidas en 4 materiales: 2+1+1+1 lotes.
        var porMaterial = new[] { 1600, 800, 12, 1023 };
        Assert.Equal(2 + 1 + 1 + 1, InstancedDrawPlan.DrawCalls(porMaterial));
        Assert.Equal(0, InstancedDrawPlan.DrawCalls(Array.Empty<int>()));
    }

    [Fact]
    public void ElAhorro_EsDeOrdenesDeMagnitud()
    {
        // Escenario del multi-visor: 4 vistas × 2 colonias, 800 hormigas por
        // vista con 4 materiales (hormiga/portadora × 2 colonias).
        const int hormigasPorVista = 800;
        var porVista = new[] { 400, 200, 150, 50 };
        int instanciado = InstancedDrawPlan.DrawCalls(porVista);
        int lineaBase = InstancedDrawPlan.PerInstanceDrawCalls(hormigasPorVista);

        Assert.Equal(4, instanciado);
        Assert.Equal(800, lineaBase);
        Assert.True(instanciado * 100 < lineaBase,
            $"el plan debía ahorrar dos órdenes de magnitud: {instanciado} vs {lineaBase}");
    }
}
