using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;

namespace AntSim.Core.Evolution;

public readonly struct AntGenomeMetadata
{
    public readonly string Name;
    public readonly string SpeciesHint;
    public readonly ulong OriginSeed;
    public readonly int Generation;
    public readonly double BestFitnessAtExport;
    public readonly int GenomeCount;

    public AntGenomeMetadata(string name, string speciesHint, ulong originSeed, int generation,
        double bestFitnessAtExport, int genomeCount)
    {
        Name = name;
        SpeciesHint = speciesHint;
        OriginSeed = originSeed;
        Generation = generation;
        BestFitnessAtExport = bestFitnessAtExport;
        GenomeCount = genomeCount;
    }
}

/// <summary>
/// Formato .antgenome v1 (cerebros portables entre partidas y especies).
///
/// Layout binario canónico (little-endian, floats por bits exactos):
///   magic "ANTGENOM" (8) · formatVersion u32 · contractVersion u32 ·
///   count u32 · nombre (u32 len + UTF-8) · speciesHint (u32 len + UTF-8) ·
///   originSeed u64 · generation u32 · bestFitness f64 ·
///   por genoma: nLayers u32 · sizes u16[] · weights f32[] · fitness f64 ·
///   SHA-256 (32) de todo lo anterior.
///
/// El fitness de exportación es informativo (no comparable entre partidas); el
/// orden de los genomas en el archivo (mérito descendente) sí se conserva.
/// </summary>
public static class AntGenomeFile
{
    public const int FormatVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ANTGENOM");

    public static byte[] Serialize(string name, string speciesHint, ulong originSeed, int generation,
        IReadOnlyList<MlpGenome> genomes, int contractVersion)
    {
        if (genomes is null || genomes.Count == 0)
            throw new ArgumentException("Debe haber al menos un genoma.", nameof(genomes));

        using var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        WriteU32(ms, FormatVersion);
        WriteU32(ms, (uint)contractVersion);
        WriteU32(ms, (uint)genomes.Count);
        WriteString(ms, name ?? "");
        WriteString(ms, speciesHint ?? "");

        ulong origin = originSeed;
        WriteU64(ms, origin);
        WriteU32(ms, (uint)generation);

        double best = 0.0;
        foreach (var g in genomes) best = Math.Max(best, g.Fitness);
        WriteF64(ms, best);

        foreach (var g in genomes)
        {
            int[] sizes = g.Sizes;
            WriteU32(ms, (uint)sizes.Length);
            foreach (int s in sizes) WriteU16(ms, (ushort)s);
            float[] w = g.CopyWeights();
            foreach (float f in w) WriteF32(ms, f);
            WriteF64(ms, g.Fitness);
        }

        byte[] body = ms.ToArray();
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(body);
        var result = new byte[body.Length + hash.Length];
        Array.Copy(body, result, body.Length);
        Array.Copy(hash, 0, result, body.Length, hash.Length);
        return result;
    }

    public static (AntGenomeMetadata Metadata, IReadOnlyList<MlpGenome> Genomes) Deserialize(
        byte[] data, int expectedContractVersion)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < Magic.Length + 4 + 4 + 4 + 32)
            throw new FormatException("Archivo .antgenome demasiado corto.");

        // Verificación de integridad (SHA-256 al final).
        int bodyLen = data.Length - 32;
        var body = new byte[bodyLen];
        Array.Copy(data, body, bodyLen);
        var trailing = new byte[32];
        Array.Copy(data, bodyLen, trailing, 0, 32);
        byte[] actual;
        using (var sha = SHA256.Create())
            actual = sha.ComputeHash(body);
        if (!FixedTimeEquals(actual, trailing))
            throw new FormatException("Integridad fallida: el hash SHA-256 no coincide.");

        using var ms = new MemoryStream(body);
        var magic = new byte[Magic.Length];
        ReadExactly(ms, magic);
        for (int i = 0; i < Magic.Length; i++)
            if (magic[i] != Magic[i])
                throw new FormatException("Magic inválido: no es un archivo .antgenome.");

        int format = (int)ReadU32(ms);
        if (format != FormatVersion)
            throw new FormatException($"Versión de formato {format} no soportada (se esperaba {FormatVersion}).");

        int contract = (int)ReadU32(ms);
        if (contract != expectedContractVersion)
            throw new FormatException($"Contrato incompatible: el archivo usa v{contract} y la partida v{expectedContractVersion}.");

        int count = (int)ReadU32(ms);
        if (count < 1 || count > 1_000_000)
            throw new FormatException($"Número de genomas inválido: {count}.");

        string name = ReadString(ms);
        string speciesHint = ReadString(ms);
        ulong originSeed = ReadU64(ms);
        int generation = (int)ReadU32(ms);
        double bestFitness = ReadF64(ms);

        var genomes = new List<MlpGenome>(count);
        for (int i = 0; i < count; i++)
        {
            int nLayers = (int)ReadU32(ms);
            if (nLayers < 2 || nLayers > 32)
                throw new FormatException($"Número de capas inválido: {nLayers}.");
            var sizes = new int[nLayers];
            for (int l = 0; l < nLayers; l++)
                sizes[l] = ReadU16(ms);

            // Validación de contrato: canales de entrada/salida fijos.
            if (sizes[0] != AntSensorChannelInfo.Count || sizes[^1] != AntDecision.DecisionCount)
                throw new FormatException("La topología no coincide con el contrato de canales.");

            int n = MlpBrain.ExpectedWeightCount(sizes);
            var w = new float[n];
            for (int k = 0; k < n; k++)
            {
                w[k] = ReadF32(ms);
                if (!FloatUtil.IsFinite(w[k]))
                    throw new FormatException("Peso no finito: el archivo está corrupto.");
            }
            double fitness = ReadF64(ms);
            genomes.Add(new MlpGenome(sizes, w, fitness));
        }

        var meta = new AntGenomeMetadata(name, speciesHint, originSeed, generation, bestFitness, count);
        return (meta, genomes);
    }

    public static void WriteFile(string path, string name, string speciesHint, ulong originSeed,
        int generation, IReadOnlyList<MlpGenome> genomes, int contractVersion)
        => File.WriteAllBytes(path, Serialize(name, speciesHint, originSeed, generation, genomes, contractVersion));

    public static (AntGenomeMetadata Metadata, IReadOnlyList<MlpGenome> Genomes) ReadFile(string path, int expectedContractVersion)
        => Deserialize(File.ReadAllBytes(path), expectedContractVersion);

    // — Escritura/lectura canónica little-endian —
    private static void WriteU16(Stream s, ushort v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
    private static void WriteU32(Stream s, uint v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }
    private static void WriteU64(Stream s, ulong v)
    {
        for (int i = 0; i < 8; i++) s.WriteByte((byte)(v >> (8 * i)));
    }
    private static void WriteF32(Stream s, float v) => WriteU32(s, unchecked((uint)BitConverter.SingleToInt32Bits(v)));
    private static void WriteF64(Stream s, double v) => WriteU64(s, unchecked((ulong)BitConverter.DoubleToInt64Bits(v)));
    private static void WriteString(Stream s, string str)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(str);
        WriteU32(s, (uint)bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static ushort ReadU16(Stream s) => (ushort)(s.ReadByte() | (s.ReadByte() << 8));
    private static uint ReadU32(Stream s) => (uint)(s.ReadByte() | (s.ReadByte() << 8) | (s.ReadByte() << 16) | (s.ReadByte() << 24));
    private static ulong ReadU64(Stream s)
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v |= (ulong)(byte)s.ReadByte() << (8 * i);
        return v;
    }
    private static float ReadF32(Stream s) => BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(s)));
    private static double ReadF64(Stream s) => BitConverter.Int64BitsToDouble(unchecked((long)ReadU64(s)));
    private static string ReadString(Stream s)
    {
        uint len = ReadU32(s);
        if (len > 1_000_000) throw new FormatException("Cadena demasiado larga (archivo corrupto).");
        var bytes = new byte[len];
        ReadExactly(s, bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void ReadExactly(Stream s, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = s.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) throw new FormatException("Archivo truncado.");
            offset += read;
        }
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        int diff = a.Length ^ b.Length;
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}