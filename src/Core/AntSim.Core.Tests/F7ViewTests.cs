using System;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F7 — la VISTA cuando el cerebro es más ancho que el contrato clásico. El canal
/// F solo trae el número total de nodos, así que la topología tiene que llegar
/// desde el pool (`genome-info --json`). Sin esto, un pool 19→16→6 se decodifica
/// contra 19·8·6, el total no cuadra y el inspector se queda en blanco.
/// </summary>
public class F7ViewTests
{
    private static string Payload(int[] sizes)
    {
        int total = 0;
        foreach (int s in sizes) total += s;
        var bytes = new byte[2 + total];
        bytes[0] = (byte)(total & 0xFF);
        bytes[1] = (byte)(total >> 8);
        for (int i = 0; i < total; i++) bytes[2 + i] = (byte)(i % 100);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public void ActivationViewModel_RendersAWiderBrain_WithThePoolSizes()
    {
        int[] sizes = { 19, 16, 6 };
        string? text = ActivationViewModel.Render(Payload(sizes), sizes);

        Assert.NotNull(text);
        Assert.Contains("MLP 19·16·6 (41 nodos)", text);
        Assert.Contains("hid[15]", text);
        Assert.Contains("== capa 2 ==", text);
        Assert.Contains("DepositAlarm", text);
    }

    [Fact]
    public void ActivationViewModel_StillDecodesTheClassicBrain_ByDefault()
    {
        int[] classic = { 19, 8, 6 };
        string? text = ActivationViewModel.Render(Payload(classic));
        Assert.NotNull(text);
        Assert.Contains("MLP 19·8·6 (33 nodos)", text);
    }

    [Fact]
    public void ActivationViewModel_RejectsAPayloadThatDoesNotMatchTheSizes()
    {
        // Un paquete de 19·16·6 decodificado con 19·8·6 no cuadra: antes se
        // devolvía null y el inspector no mentía con una capa de menos.
        Assert.Null(ActivationViewModel.Render(Payload(new[] { 19, 16, 6 }), new[] { 19, 8, 6 }));
    }

    [Fact]
    public void ImportDialog_ParsesThePoolSizes_FromTheOracleJson()
    {
        const string json = "{\"ok\":true,\"fileName\":\"pretrain-maze.antgenome\"," +
            "\"sizes\":[19,16,6],\"genomeCount\":32}";
        Assert.True(ImportDialogModel.TryParseSizes(json, out int[] sizes));
        Assert.Equal(new[] { 19, 16, 6 }, sizes);
    }

    [Fact]
    public void ImportDialog_ToleratesMissingOrNeatSizes()
    {
        Assert.False(ImportDialogModel.TryParseSizes("{\"ok\":true,\"sizes\":null}", out _));
        Assert.False(ImportDialogModel.TryParseSizes("{\"ok\":true,\"nodeCount\":33}", out _));
        Assert.False(ImportDialogModel.TryParseSizes("{\"sizes\":[19]}", out _));
        Assert.False(ImportDialogModel.TryParseSizes("{\"sizes\":[19,0,6]}", out _));
    }
}
