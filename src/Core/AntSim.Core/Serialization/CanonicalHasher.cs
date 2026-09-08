using System;
using System.Security.Cryptography;
using System.Text;

namespace AntSim.Core.Serialization;

/// <summary>
/// Acumulador SHA-256 canónico para hashes de estado reproducibles.
/// Convierte cada valor a sus bytes exactos (floats por bits, enteros LE)
/// en el orden en que el llamador los anexa — el llamador es responsable del
/// orden canónico. Sirve para hashes de hito, huellas y verificación bit-a-bit.
/// </summary>
public sealed class CanonicalHasher : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void AppendByte(byte v) => AppendBytes(stackalloc byte[] { v });

    public void AppendBool(bool v) => AppendByte(v ? (byte)1 : (byte)0);

    public void AppendUInt64(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        for (int i = 0; i < 8; i++) b[i] = (byte)(v >> (8 * i));
        AppendBytes(b);
    }

    public void AppendInt32(int v) => AppendUInt32(unchecked((uint)v));

    public void AppendUInt32(uint v) => AppendUInt64(v);

    /// <summary>Float por bits exactos (mismo valor ⇒ mismos bytes).</summary>
    public void AppendFloat(float v) => AppendUInt32(FloatBits.ToUInt32(v));

    public void AppendDouble(double v) => AppendUInt64(FloatBits.ToUInt64(v));

    public void AppendBytes(ReadOnlySpan<byte> bytes) => _hash.AppendData(bytes);

    public void AppendString(string s)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(s);
        AppendUInt32((uint)utf8.Length);
        AppendBytes(utf8);
    }

    public byte[] FinalizeHash()
    {
        byte[] result = _hash.GetHashAndReset();
        return result;
    }

    public string FinalizeHex() => ToHex(FinalizeHash());

    public static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public void Dispose() => _hash.Dispose();
}

/// <summary>
/// Conversión bit-exacta float/double ↔ uint/ulong con APIs disponibles en
/// netstandard2.1 (SingleToUInt32Bits/DoubleToUInt64Bits no existen ahí).
/// La convención de bytes es fija (little-endian) e independiente del host.
/// </summary>
public static class FloatBits
{
    public static uint ToUInt32(float v) => unchecked((uint)BitConverter.SingleToInt32Bits(v));

    public static float FromUInt32(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

    public static ulong ToUInt64(double v) => unchecked((ulong)BitConverter.DoubleToInt64Bits(v));
}
