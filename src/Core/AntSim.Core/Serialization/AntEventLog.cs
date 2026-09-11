using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AntSim.Core.World;

namespace AntSim.Core.Serialization;

/// <summary>
/// Registro de eventos .antlog v1 (Fase 4 — persistencia). Observador de solo
/// lectura del Canal B: graba cada <see cref="SimEvent"/> con su tick y los
/// hashes de hito cada 1024 ticks (momento canónico de la especificación).
///
/// La reproducción = cargar un .antsave en T y re-ejecutar; el .antlog es la
/// CONTRAVIDEDAD: permite comparar una ejecución contra otra (los eventos y
/// hashes del intervalo deben coincidir byte a byte) sin tocar el mundo — los
/// hashes de WorldSim son idénticos con y sin el recorder adjunto.
///
/// Layout canónico (little-endian): magic "ANTLOG11" (8) · SHA-256 al final:
///   formato u32 · semilla u64 ·
///   registros: {tick u64 · kind u8 · colonyId i32 · antId u32 · x f32 ·
///               y f32 · cause u8}· ·
///   hitos: {tick u64 · hash (32 bytes)}·
///   (los hitos van después de todos los eventos, con su propio conteo).
/// </summary>
public sealed class AntEventLog : IDisposable
{
    public const int FormatVersion = 1;
    public const int MilestoneEvery = 1024;

    private static readonly byte[] Magic = { (byte)'A', (byte)'N', (byte)'T', (byte)'L', (byte)'O', (byte)'G', (byte)'1', (byte)'1' };

    private readonly MemoryStream _eventsBuffer = new();
    private readonly CanonicalWriter _writer;
    private readonly List<(ulong Tick, byte[] Hash)> _milestones = new();
    private readonly ulong _seed;
    private ulong _lastMilestoneTick;
    private bool _finished;

    public AntEventLog(WorldSim sim)
    {
        if (sim is null) throw new ArgumentNullException(nameof(sim));
        _seed = sim.Seed;
        _writer = new CanonicalWriter(_eventsBuffer);
    }

    /// <summary>
    /// Observa los eventos de un paso. Llamar DESPUÉS de <c>WorldSim.Step()</c>
    /// con <c>sim.LastEvents</c>. Detecta los hitos por tick (1024) — el hash
    /// se toma del mundo tal cual queda tras el paso que lo alcanza.
    /// </summary>
    public void Observe(IReadOnlyList<SimEvent> events)
    {
        if (_finished) throw new InvalidOperationException("El log ya finalizó (Finish fue llamado).");
        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            _writer.WriteU64(ev.Tick);
            _writer.WriteByte((byte)ev.Kind);
            _writer.WriteI32(ev.ColonyId);
            _writer.WriteU32(ev.AntId);
            _writer.WriteF32(ev.X);
            _writer.WriteF32(ev.Y);
            _writer.WriteByte(ev.Cause);
        }
    }

    /// <summary>
    /// Registra un hito (llamar en los ticks múltiplo de 1024 con el hash del mundo).
    /// </summary>
    public void RecordMilestone(ulong tick, string hashHex)
    {
        if (_finished) throw new InvalidOperationException("El log ya finalizó (Finish fue llamado).");
        _milestones.Add((tick, HexToBytes(hashHex)));
        _lastMilestoneTick = tick;
    }

    /// <summary>
    /// Serializa todo el log: eventos grabados + hitos + SHA-256 de integridad.
    /// Después de llamarlo el log queda sellado (no acepta más eventos).
    /// </summary>
    public byte[] Finish()
    {
        if (_finished) return _eventsBuffer.ToArray(); // idempotente: devuelve lo ya sellado

        // Reorganiza el buffer: eventos primero (ya están), luego bloque de hitos.
        byte[] events = _eventsBuffer.ToArray();

        using var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        var w = new CanonicalWriter(ms);
        w.WriteU32(FormatVersion);
        w.WriteU64(_seed);
        w.WriteU64((ulong)events.Length);
        ms.Write(events, 0, events.Length);
        w.WriteU32((uint)_milestones.Count);
        for (int i = 0; i < _milestones.Count; i++)
        {
            w.WriteU64(_milestones[i].Tick);
            ms.Write(_milestones[i].Hash, 0, 32);
        }

        byte[] body = ms.ToArray();
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(body);
        var result = new byte[body.Length + hash.Length];
        Array.Copy(body, result, body.Length);
        Array.Copy(hash, 0, result, body.Length, hash.Length);

        _finished = true;
        return result;
    }

    public void WriteTo(string path) => File.WriteAllBytes(path, Finish());

    public void Dispose()
    {
        _writer.Dispose();
        _eventsBuffer.Dispose();
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex is null || hex.Length != 64)
            throw new ArgumentException("El hash de hito debe ser un hex SHA-256 de 64 caracteres.", nameof(hex));
        var bytes = new byte[32];
        for (int i = 0; i < 32; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}

/// <summary>Lectura y verificación de un .antlog (espejo de <see cref="AntEventLog"/>).</summary>
public static class AntEventLogFile
{
    public readonly struct Entry
    {
        public readonly ulong Tick;
        public readonly SimEventKind Kind;
        public readonly int ColonyId;
        public readonly uint AntId;
        public readonly float X;
        public readonly float Y;
        public readonly byte Cause;

        public Entry(ulong tick, SimEventKind kind, int colonyId, uint antId, float x, float y, byte cause)
        {
            Tick = tick;
            Kind = kind;
            ColonyId = colonyId;
            AntId = antId;
            X = x;
            Y = y;
            Cause = cause;
        }
    }

    public readonly struct Milestone
    {
        public readonly ulong Tick;
        public readonly string HashHex;

        public Milestone(ulong tick, string hashHex)
        {
            Tick = tick;
            HashHex = hashHex;
        }
    }

    public readonly struct LogData
    {
        public readonly int FormatVersion;
        public readonly ulong Seed;
        public readonly IReadOnlyList<Entry> Events;
        public readonly IReadOnlyList<Milestone> Milestones;

        public LogData(int formatVersion, ulong seed, IReadOnlyList<Entry> events, IReadOnlyList<Milestone> milestones)
        {
            FormatVersion = formatVersion;
            Seed = seed;
            Events = events;
            Milestones = milestones;
        }
    }

    public static LogData Read(string path) => Deserialize(File.ReadAllBytes(path));

    public static LogData Deserialize(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < 8 + 4 + 8 + 8 + 4 + 32)
            throw new FormatException("Archivo .antlog demasiado corto.");

        int bodyLen = data.Length - 32;
        var body = new byte[bodyLen];
        Array.Copy(data, body, bodyLen);
        var trailing = new byte[32];
        Array.Copy(data, bodyLen, trailing, 0, 32);
        byte[] actual;
        using (var sha = SHA256.Create())
            actual = sha.ComputeHash(body);
        if (!FixedTimeEquals(actual, trailing))
            throw new FormatException("Integridad fallida: el hash SHA-256 del log no coincide.");

        var ms = new MemoryStream(body, writable: false);
        var r = new CanonicalReader(ms);
        var magic = r.ReadBytes(8);
        byte[] expected = { (byte)'A', (byte)'N', (byte)'T', (byte)'L', (byte)'O', (byte)'G', (byte)'1', (byte)'1' };
        for (int i = 0; i < 8; i++)
            if (magic[i] != expected[i])
                throw new FormatException("Magic inválido: no es un archivo .antlog.");

        int format = r.ReadI32();
        if (format != AntEventLog.FormatVersion)
            throw new FormatException($"Versión de formato {format} no soportada.");
        ulong seed = r.ReadU64();
        ulong eventsLen = r.ReadU64();

        var events = new List<Entry>();
        long eventsEnd = ms.Position + (long)eventsLen;
        while (ms.Position < eventsEnd)
        {
            ulong tick = r.ReadU64();
            byte kind = r.ReadByte();
            if (kind > (byte)SimEventKind.CommandExecuted)
                throw new FormatException($"Tipo de evento inválido: {kind}.");
            events.Add(new Entry(
                tick,
                (SimEventKind)kind,
                r.ReadI32(),
                r.ReadU32(),
                r.ReadF32(),
                r.ReadF32(),
                r.ReadByte()));
        }

        int milestoneCount = r.ReadI32();
        var milestones = new List<Milestone>(milestoneCount);
        for (int i = 0; i < milestoneCount; i++)
        {
            ulong tick = r.ReadU64();
            milestones.Add(new Milestone(tick, CanonicalHasher.ToHex(r.ReadBytes(32))));
        }

        if (ms.Position != ms.Length)
            throw new FormatException("Bytes sobrantes al final del log (versión distinta).");
        return new LogData(format, seed, events, milestones);
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        int diff = a.Length ^ b.Length;
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
