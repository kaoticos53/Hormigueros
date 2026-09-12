using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AntSim.Core.Evolution;

namespace AntSim.Core.Scenario;

/// <summary>Info de cuarentena de un .antgenome (F4.3, contrato UX §2.1).</summary>
public sealed class GenomeImportInfo
{
    public GenomeImportInfo(bool ok, string? error, string fileName, string sha256,
        long bytes, string poolName, string speciesHint, ulong originSeed,
        int generation, double bestFitness, int genomeCount, string card,
        string quarantineNote)
    {
        Ok = ok; Error = error; FileName = fileName; Sha256 = sha256; Bytes = bytes;
        PoolName = poolName; SpeciesHint = speciesHint; OriginSeed = originSeed;
        Generation = generation; BestFitness = bestFitness; GenomeCount = genomeCount;
        Card = card; QuarantineNote = quarantineNote;
    }

    public bool Ok { get; }
    public string? Error { get; }
    public string FileName { get; }
    public string Sha256 { get; }
    public long Bytes { get; }
    public string PoolName { get; }
    public string SpeciesHint { get; }
    public ulong OriginSeed { get; }
    public int Generation { get; }
    public double BestFitness { get; }
    public int GenomeCount { get; }
    public string Card { get; }
    public string QuarantineNote { get; }

    /// <summary>Regla de cuarentena canónica (el oráculo la declara; la UI la pinta).</summary>
    public const string QuarantineRules =
        "Entrarán en cuarentena: se prueban uno a uno (ventana 120 s) y solo " +
        "entran a la élite si rinden ≥ mediana del pool. Nunca degradan la élite.";

    /// <summary>
    /// Inspecciona un .antgenome SIN tocar el mundo (pre-vista de riesgo 0 para
    /// el diálogo de cuarentena). Lee metadatos + SHA-256 y compone la tarjeta
    /// canónica; nunca lanza: los fallos (inexistente, corrupto, versión
    /// futura) vuelven como <c>Ok=false</c> con el motivo.
    /// </summary>
    public static GenomeImportInfo Inspect(string path, int expectedContractVersion)
    {
        string fileName;
        try { fileName = Path.GetFileName(path); }
        catch { fileName = path; }

        if (!File.Exists(path))
            return Fail($"archivo no encontrado: {path}", fileName);        long bytes;
        string sha;
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
            bytes = data.LongLength;
            using (var h = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                h.AppendData(data);
                sha = ToHex(h.GetHashAndReset());
            }
        }
        catch (Exception ex)
        {
            return Fail($"no se pudo leer: {ex.Message}", fileName);
        }

        AntGenomeMetadata meta;
        try
        {
            (meta, _) = AntGenomeFile.Deserialize(data, expectedContractVersion);
        }
        catch (Exception ex)
        {
            return Fail($"formato inválido: {ex.Message}", fileName);
        }

        var shaShort = sha.Substring(0, 8);
        var card = string.Format(CultureInfo.InvariantCulture,
            "{0} · {1} genomas · gen {2} · fitness {3:0.###} · {4:0.#} KiB · sha {5}",
            fileName, meta.GenomeCount, meta.Generation, meta.BestFitnessAtExport,
            bytes / 1024d, shaShort);

        return new GenomeImportInfo(
            ok: true, error: null, fileName: fileName, sha256: sha, bytes: bytes,
            poolName: meta.Name, speciesHint: meta.SpeciesHint, originSeed: meta.OriginSeed,
            generation: meta.Generation, bestFitness: meta.BestFitnessAtExport,
            genomeCount: meta.GenomeCount, card: card, quarantineNote: QuarantineRules);
    }

    private static GenomeImportInfo Fail(string error, string fileName) =>
        new(ok: false, error: error, fileName: fileName, sha256: "", bytes: 0,
            poolName: "", speciesHint: "", originSeed: 0, generation: 0,
            bestFitness: 0, genomeCount: 0,
            card: $"✗ {fileName}: {error}", quarantineNote: QuarantineRules);

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        const string hex = "0123456789abcdef";
        foreach (byte b in bytes)
        {
            sb.Append(hex[b >> 4]);
            sb.Append(hex[b & 0xF]);
        }
        return sb.ToString();
    }

    /// <summary>Objeto JSON canónico para el CLI (campos estructurados).</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"ok\":").Append(Ok ? "true" : "false");
        sb.Append(",\"error\":").Append(Error != null ? "\"" + JsonEscape(Error) + "\"" : "null");
        sb.Append(",\"fileName\":\"").Append(JsonEscape(FileName)).Append('"');
        sb.Append(",\"sha256\":\"").Append(Sha256).Append('"');
        sb.Append(",\"bytes\":").Append(Bytes);
        sb.Append(",\"poolName\":\"").Append(JsonEscape(PoolName)).Append('"');
        sb.Append(",\"speciesHint\":\"").Append(JsonEscape(SpeciesHint)).Append('"');
        sb.Append(",\"originSeed\":").Append(OriginSeed);
        sb.Append(",\"generation\":").Append(Generation);
        sb.Append(",\"bestFitness\":").Append(BestFitness.ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append(",\"genomeCount\":").Append(GenomeCount);
        sb.Append(",\"card\":\"").Append(JsonEscape(Card)).Append('"');
        sb.Append(",\"quarantine\":\"").Append(JsonEscape(QuarantineNote)).Append('"');
        sb.Append('}');
        return sb.ToString();
    }

    internal static string JsonEscape(string s) => s
        .Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
