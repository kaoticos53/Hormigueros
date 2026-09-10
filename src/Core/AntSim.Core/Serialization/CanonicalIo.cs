using System;
using System.IO;
using System.Text;

namespace AntSim.Core.Serialization;

/// <summary>
/// Escritura canónica little-endian sobre un Stream (mismas convenciones de
/// bytes que <see cref="AntGenomeFile"/>: floats/doubles por bits exactos,
/// enteros LE, UTF-8 con longitud prefijada u32). Compartida por los formatos
/// binarios del Core (.antsave, .antlog).
/// </summary>
public sealed class CanonicalWriter : IDisposable
{
    private readonly Stream _s;
    private bool _disposed;

    public CanonicalWriter(Stream s) => _s = s ?? throw new ArgumentNullException(nameof(s));

    public void WriteByte(byte v) => _s.WriteByte(v);

    public void WriteBool(bool v) => _s.WriteByte(v ? (byte)1 : (byte)0);

    public void WriteU16(ushort v) { _s.WriteByte((byte)v); _s.WriteByte((byte)(v >> 8)); }

    public void WriteU32(uint v)
    {
        _s.WriteByte((byte)v);
        _s.WriteByte((byte)(v >> 8));
        _s.WriteByte((byte)(v >> 16));
        _s.WriteByte((byte)(v >> 24));
    }

    public void WriteI32(int v) => WriteU32(unchecked((uint)v));

    public void WriteU64(ulong v)
    {
        for (int i = 0; i < 8; i++) _s.WriteByte((byte)(v >> (8 * i)));
    }

    public void WriteF32(float v) => WriteU32(unchecked((uint)BitConverter.SingleToInt32Bits(v)));

    public void WriteF64(double v) => WriteU64(unchecked((ulong)BitConverter.DoubleToInt64Bits(v)));

    public void WriteString(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
        WriteU32((uint)bytes.Length);
        _s.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _s.Flush();
            _disposed = true;
        }
    }
}

/// <summary>Lectura canónica espejo de <see cref="CanonicalWriter"/>.</summary>
public sealed class CanonicalReader
{
    private readonly Stream _s;

    public CanonicalReader(Stream s) => _s = s ?? throw new ArgumentNullException(nameof(s));

    public byte ReadByte() => (byte)_s.ReadByte();

    public bool ReadBool() => _s.ReadByte() != 0;

    public ushort ReadU16() => (ushort)(_s.ReadByte() | (_s.ReadByte() << 8));

    public uint ReadU32()
    {
        uint v = 0;
        for (int i = 0; i < 4; i++) v |= (uint)_s.ReadByte() << (8 * i);
        return v;
    }

    public int ReadI32() => unchecked((int)ReadU32());

    public ulong ReadU64()
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v |= (ulong)(byte)_s.ReadByte() << (8 * i);
        return v;
    }

    public float ReadF32() => BitConverter.Int32BitsToSingle(unchecked((int)ReadU32()));

    public double ReadF64() => BitConverter.Int64BitsToDouble(unchecked((long)ReadU64()));

    public string ReadString()
    {
        uint len = ReadU32();
        if (len > 10_000_000) throw new FormatException("Cadena demasiado larga (archivo corrupto).");
        var bytes = new byte[len];
        ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public void ReadExactly(byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = _s.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) throw new FormatException("Archivo truncado.");
            offset += read;
        }
    }

    public byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        ReadExactly(buffer);
        return buffer;
    }
}
