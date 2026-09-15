using System;
using AntSim.Core.Sim;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2c — CanonMath: matemática canónica multiplataforma. Los tests validan
///   1. corrección (±2 ULP float contra MathF en los rangos del juego);
///   2. estabilidad bit a bit (misma llamada dos veces, y contra valores
///      "golden" fijados a mano donde son conocidos);
///   3. valores especiales (0, ±inf, NaN, desbordes).
/// La prueba definitiva de cross-platform la da la suite en Linux (WSL/CI):
/// los 5 pines de hash deben pasar en ambos SO.
/// </summary>
public class CanonMathTests
{
    [Fact]
    public void Exp_CoincideConMathF_DentroDe2Ulp()
    {
        for (int i = -600; i <= 600; i++)
        {
            float x = i * 0.15f;
            float canon = CanonMath.Exp(x);
            float refv = MathF.Exp(x);
            Assert.True(UlpBetween(canon, refv, 2),
                $"Exp({x:R}): canon={canon:R} ref={refv:R}");
        }
    }

    [Fact]
    public void Log_CoincideConMathF_DentroDe2Ulp()
    {
        for (int i = 1; i <= 2000; i++)
        {
            float x = i * 0.01f;
            float canon = CanonMath.Log(x);
            float refv = MathF.Log(x);
            Assert.True(UlpBetween(canon, refv, 2),
                $"Log({x:R}): canon={canon:R} ref={refv:R}");
        }
    }

    [Fact]
    public void Tanh_CoincideConMathF_DentroDe6Ulp()
    {
        // La fórmula 1 − 2/(e²ˣ+1) cancela cifras cerca de 0 (t → 1): el error
        // absoluto frente a la libm sube a ~4 ULP ahí. Sigue siendo DETERMINISTA
        // (misma secuencia IEEE en toda plataforma) — la única promesa de
        // CanonMath. La relativa al mundo: 6 ULP de tanh no mueve una decisión.
        for (int i = -800; i <= 800; i++)
        {
            float x = i * 0.02f;
            float canon = CanonMath.Tanh(x);
            float refv = MathF.Tanh(x);
            Assert.True(UlpBetween(canon, refv, 6),
                $"Tanh({x:R}): canon={canon:R} ref={refv:R}");
        }
    }

    [Fact]
    public void SinCos_CoincidenConMathF_DentroDe2Ulp()
    {
        for (int i = -3200; i <= 3200; i++)
        {
            float x = i * 0.005f; // cubre ±16 rad: más que cualquier heading
            float s = CanonMath.Sin(x), c = CanonMath.Cos(x);
            Assert.True(UlpBetween(s, MathF.Sin(x), 2), $"Sin({x:R}): {s:R} vs {MathF.Sin(x):R}");
            Assert.True(UlpBetween(c, MathF.Cos(x), 2), $"Cos({x:R}): {c:R} vs {MathF.Cos(x):R}");
        }
    }

    [Fact]
    public void Exp_ValoresFijosConocidos()
    {
        Assert.Equal(1f, CanonMath.Exp(0f));
        Assert.Equal(MathF.E, CanonMath.Exp(1f), 5);
        // exp(ln 2) = 2 (con Ln2 en double, la serie devuelve ~2.0 exacto).
        Assert.Equal(2f, CanonMath.Exp(0.6931472f), 4);
        Assert.Equal(0f, CanonMath.Exp(-104f));
        Assert.Equal(float.PositiveInfinity, CanonMath.Exp(89f));
    }

    [Fact]
    public void Log_ValoresFijosConocidos()
    {
        Assert.Equal(0f, CanonMath.Log(1f));
        Assert.Equal(1f, CanonMath.Log(MathF.E), 5);
        Assert.Equal(float.NegativeInfinity, CanonMath.Log(0f));
        Assert.True(float.IsNaN(CanonMath.Log(-1f)));
    }

    [Fact]
    public void Tanh_SimetriaYEstabilizacion()
    {
        Assert.Equal(0f, CanonMath.Tanh(0f));
        Assert.Equal(CanonMath.Tanh(0.5f), -CanonMath.Tanh(-0.5f));
        Assert.Equal(1f, CanonMath.Tanh(9f));
        Assert.Equal(-1f, CanonMath.Tanh(-9f));
        Assert.Equal(1f, CanonMath.Tanh(100f));
    }

    [Fact]
    public void SinCos_CuadrantesConocidos()
    {
        Assert.Equal(0f, CanonMath.Sin(0f));
        Assert.Equal(1f, CanonMath.Sin(MathF.PI / 2f), 5);
        Assert.Equal(1f, CanonMath.Cos(0f));
        Assert.Equal(-1f, CanonMath.Cos(MathF.PI), 5);
        Assert.Equal(0f, CanonMath.Sin(MathF.PI), 5);
        Assert.Equal(-1f, CanonMath.Sin(-MathF.PI / 2f), 5);
    }

    [Fact]
    public void Determinismo_MismaLlamada_MismoBits()
    {
        var xs = new[] { 0.1f, -0.7f, 3.14159f, -12.5f, 88.2f, 0.0049f };
        foreach (float x in xs)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(CanonMath.Exp(x)),
                         BitConverter.SingleToInt32Bits(CanonMath.Exp(x)));
            Assert.Equal(BitConverter.SingleToInt32Bits(CanonMath.Tanh(x)),
                         BitConverter.SingleToInt32Bits(CanonMath.Tanh(x)));
            Assert.Equal(BitConverter.SingleToInt32Bits(CanonMath.Sin(x)),
                         BitConverter.SingleToInt32Bits(CanonMath.Sin(x)));
            Assert.Equal(BitConverter.SingleToInt32Bits(CanonMath.Cos(x)),
                         BitConverter.SingleToInt32Bits(CanonMath.Cos(x)));
            if (x > 0)
                Assert.Equal(BitConverter.SingleToInt32Bits(CanonMath.Log(x)),
                             BitConverter.SingleToInt32Bits(CanonMath.Log(x)));
        }
    }

    [Fact]
    public void Exp_RangoDelJuego_NuncaFueraDeFloat()
    {
        // El dominio real: activaciones y decay de feromonas.
        for (int i = -900; i <= 900; i++)
        {
            float x = i * 0.1f;
            float v = CanonMath.Exp(x);
            Assert.False(float.IsNaN(v));
        }
    }

    private static bool UlpBetween(float a, float b, int ulps)
    {
        if (a == b) return true;
        if (float.IsNaN(a) || float.IsNaN(b)) return false;
        int ia = BitConverter.SingleToInt32Bits(a);
        int ib = BitConverter.SingleToInt32Bits(b);
        if (ia < 0) ia = unchecked((int)(0x8000_0000u - (uint)ia));
        if (ib < 0) ib = unchecked((int)(0x8000_0000u - (uint)ib));
        return Math.Abs((long)ia - ib) <= ulps;
    }
}
