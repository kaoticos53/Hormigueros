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

    // ═════════════════════════════════════════════════════════════════════
    // Formato v2 (F5.2c rodaja 4, diseño §4): genomas NEAT con estructura.
    //
    //   magic "ANTGENOM" (8) · formatVersion u32 (=2) · contractVersion u32 ·
    //   count u32 · nombre · speciesHint · originSeed u64 · generation u32 ·
    //   bestFitness f64 ·
    //   por genoma:
    //     nNodes u16 · nConns u16 ·
    //     por nodo:   id u16 · kind u8 · bias f32 · act u8          (8 B)
    //     por conn:   innovation u16 · from u16 · to u16 · weight f32 · enabled u8
    //     fitness f64
    //   SHA-256 (32) de todo lo anterior.
    //
    // Orden canónico: nodos por id, conexiones por innovation — un mismo
    // grafo serializa siempre igual (hash del archivo estable).
    //
    // RE-INNOVACIÓN CANÓNICA AL IMPORTAR (diseño §3.3): el lector v2 (y la
    // conversión v1→v2) renumera las conexiones 1..K por orden (from,to) —
    // dos importaciones del mismo archivo producen pools idénticos, y las
    // innovations dejan de depender de la historia del pool exportador.
    // ═════════════════════════════════════════════════════════════════════

    public const int FormatVersionV2 = 2;

    /// <summary>Escribe un .antgenome v2 con genomas NEAT (orden canónico).</summary>
    public static byte[] SerializeNeat(string name, string speciesHint, ulong originSeed, int generation,
        IReadOnlyList<NeatGenome> genomes, int contractVersion)
    {
        if (genomes is null || genomes.Count == 0)
            throw new ArgumentException("Debe haber al menos un genoma.", nameof(genomes));

        using var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        WriteU32(ms, FormatVersionV2);
        WriteU32(ms, (uint)contractVersion);
        WriteU32(ms, (uint)genomes.Count);
        WriteString(ms, name ?? "");
        WriteString(ms, speciesHint ?? "");
        WriteU64(ms, originSeed);
        WriteU32(ms, (uint)generation);

        double best = 0.0;
        foreach (var g in genomes) best = Math.Max(best, g.Fitness);
        WriteF64(ms, best);

        foreach (var genome in genomes)
        {
            if (genome.Nodes.Count > ushort.MaxValue || genome.Conns.Count > ushort.MaxValue)
                throw new ArgumentException("Genoma demasiado grande para el formato v2.");
            // CANONIZACIÓN AL ESCRIBIR: el archivo SIEMPRE lleva la forma
            // canónica (nodos por id, innovations 1..K por (from,to)) — un
            // mismo grafo serializa idéntico sin importar la historia del
            // pool exportador, y el renglón re-innovación del lector la deja
            // igual. El genoma en MEMORIA conserva sus innovations de trabajo.
            var g = RenumberCanonically(genome);
            WriteU16(ms, (ushort)g.Nodes.Count);
            WriteU16(ms, (ushort)g.Conns.Count);
            foreach (var n in SortNodesById(g.Nodes))
            {
                WriteU16(ms, (ushort)n.Id);
                ms.WriteByte((byte)n.Kind);
                WriteF32(ms, n.Bias);
                ms.WriteByte(n.Act);
            }
            foreach (var c in SortByInnovation(g.Conns))
            {
                WriteU16(ms, (ushort)c.Innovation);
                WriteU16(ms, (ushort)c.From);
                WriteU16(ms, (ushort)c.To);
                WriteF32(ms, c.Weight);
                ms.WriteByte(c.Enabled ? (byte)1 : (byte)0);
            }
            WriteF64(ms, g.Fitness);
        }

        return WithSha(ms.ToArray());
    }

    /// <summary>
    /// Lee un .antgenome de CUALQUIER versión soportada como genomas NEAT:
    /// v1 se convierte (grafo denso de su sizes[], paridad demostrada en
    /// rodaja 1) y v2 se parsea; ambos re-innovan canónicamente 1..K por
    /// (from,to) — ver <see cref="RenumberCanonically"/>.
    /// </summary>
    public static (AntGenomeMetadata Metadata, IReadOnlyList<NeatGenome> Genomes) DeserializeNeat(
        byte[] data, int expectedContractVersion)
    {
        int format = PeekFormatVersion(data);
        return format switch
        {
            FormatVersion => FromV1(data, expectedContractVersion),
            FormatVersionV2 => FromV2(data, expectedContractVersion),
            _ => throw new FormatException($"Versión de formato {format} no soportada (válidas: 1, {FormatVersionV2}).")
        };
    }

    public static void WriteNeatFile(string path, string name, string speciesHint, ulong originSeed,
        int generation, IReadOnlyList<NeatGenome> genomes, int contractVersion)
        => File.WriteAllBytes(path, SerializeNeat(name, speciesHint, originSeed, generation, genomes, contractVersion));

    public static (AntGenomeMetadata Metadata, IReadOnlyList<NeatGenome> Genomes) ReadNeatFile(string path, int expectedContractVersion)
        => DeserializeNeat(File.ReadAllBytes(path), expectedContractVersion);

    /// <summary>formatVersion del archivo SIN parsear (byte 8..12); -1 si es demasiado corto.</summary>
    public static int PeekFormatVersion(byte[] data)
    {
        if (data is null || data.Length < Magic.Length + 4) return -1;
        return (int)(data[Magic.Length] | (data[Magic.Length + 1] << 8)
                   | (data[Magic.Length + 2] << 16) | (data[Magic.Length + 3] << 24));
    }

    private static (AntGenomeMetadata, IReadOnlyList<NeatGenome>) FromV1(byte[] data, int expectedContractVersion)
    {
        var (meta, genomes) = Deserialize(data, expectedContractVersion);
        var neat = new List<NeatGenome>(genomes.Count);
        foreach (var g in genomes)
            neat.Add(RenumberCanonically(NeatGenome.FromMlp(g.Sizes, g.CopyWeights(), g.Fitness)));
        return (meta, neat);
    }

    private static (AntGenomeMetadata, IReadOnlyList<NeatGenome>) FromV2(byte[] data, int expectedContractVersion)
    {
        var body = VerifyAndGetBody(data);
        using var ms = new MemoryStream(body);
        var magic = new byte[Magic.Length];
        ReadExactly(ms, magic);
        for (int i = 0; i < Magic.Length; i++)
            if (magic[i] != Magic[i])
                throw new FormatException("Magic inválido: no es un archivo .antgenome.");

        int format = (int)ReadU32(ms);
        if (format != FormatVersionV2)
            throw new FormatException($"Versión de formato {format} no soportada (se esperaba {FormatVersionV2}).");
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

        var genomes = new List<NeatGenome>(count);
        for (int i = 0; i < count; i++)
        {
            int nNodes = ReadU16(ms);
            int nConns = ReadU16(ms);
            if (nNodes < AntSensorChannelInfo.Count + AntDecision.DecisionCount || nNodes > NeatGenome.MaxNodes)
                throw new FormatException($"Número de nodos inválido: {nNodes}.");
            if (nConns > NeatGenome.MaxConns)
                throw new FormatException($"Número de conexiones inválido: {nConns}.");

            var nodes = new List<NodeGene>(nNodes);
            for (int k = 0; k < nNodes; k++)
            {
                int id = ReadU16(ms);
                int kind = ms.ReadByte();
                float bias = ReadF32(ms);
                int act = ms.ReadByte();
                if (kind < 0 || kind > 2)
                    throw new FormatException($"Kind de nodo desconocido: {kind}.");
                if (!FloatUtil.IsFinite(bias))
                    throw new FormatException("Bias no finito: el archivo está corrupto.");
                nodes.Add(new NodeGene(id, (NodeKind)kind, bias, (byte)act));
            }

            var conns = new List<ConnGene>(nConns);
            for (int k = 0; k < nConns; k++)
            {
                int innovation = ReadU16(ms);
                int from = ReadU16(ms);
                int to = ReadU16(ms);
                float weight = ReadF32(ms);
                int enabled = ms.ReadByte();
                if (enabled != 0 && enabled != 1)
                    throw new FormatException($"Enabled inválido: {enabled}.");
                if (!FloatUtil.IsFinite(weight))
                    throw new FormatException("Peso no finito: el archivo está corrupto.");
                conns.Add(new ConnGene(innovation, from, to, weight, enabled == 1));
            }

            double fitness = ReadF64(ms);
            // Re-innovación canónica al importar (diseño §3.3): el constructor
            // valida los invariantes completos DESPUÉS de la renumeración.
            genomes.Add(RenumberCanonically(new NeatGenome(nodes, conns, fitness)));
        }

        return (new AntGenomeMetadata(name, speciesHint, originSeed, generation, bestFitness, count), genomes);
    }

    /// <summary>Forma canónica del genoma: nodos por id, conexiones 1..K por
    /// orden (from,to) — determinista e independiente de la historia del
    /// pool exportador (la misma definición §4 que el writer serializa).</summary>
    public static NeatGenome RenumberCanonically(NeatGenome genome)
    {
        var sorted = SortByFromTo(genome.Conns);
        var innovOf = new Dictionary<(int from, int to), int>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
            innovOf[(sorted[i].From, sorted[i].To)] = i + 1;
        var renumbered = new List<ConnGene>(sorted.Count);
        foreach (var c in genome.Conns)
            renumbered.Add(c with { Innovation = innovOf[(c.From, c.To)] });
        return new NeatGenome(SortNodesById(genome.Nodes), renumbered, genome.Fitness);
    }

    private static List<NodeGene> SortNodesById(IReadOnlyList<NodeGene> nodes)
    {
        var list = new List<NodeGene>(nodes);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    private static List<ConnGene> SortByInnovation(IReadOnlyList<ConnGene> conns)
    {
        var list = new List<ConnGene>(conns);
        list.Sort((a, b) => a.Innovation.CompareTo(b.Innovation));
        return list;
    }

    private static List<ConnGene> SortByFromTo(IReadOnlyList<ConnGene> conns)
    {
        var list = new List<ConnGene>(conns);
        list.Sort((a, b) => a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));
        return list;
    }

    private static byte[] WithSha(byte[] body)
    {
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(body);
        var result = new byte[body.Length + hash.Length];
        Array.Copy(body, result, body.Length);
        Array.Copy(hash, 0, result, body.Length, hash.Length);
        return result;
    }

    /// <summary>Verifica el SHA-256 final y devuelve el cuerpo.</summary>
    private static byte[] VerifyAndGetBody(byte[] data)
    {
        if (data.Length < Magic.Length + 4 + 4 + 4 + 32)
            throw new FormatException("Archivo .antgenome demasiado corto.");
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
        return body;
    }

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