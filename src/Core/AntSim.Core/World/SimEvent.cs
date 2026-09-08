namespace AntSim.Core.World;

/// <summary>Eventos discretos del Canal B (los que jamás se interpolan en la vista).</summary>
public enum SimEventKind : byte
{
    AntBorn = 0,
    AntDied = 1,
    ItemSpawned = 2,
    ItemConsumed = 3,
    Pickup = 4,
    Unload = 5,
    EggLaid = 6,
    Eclosed = 7,
    ColonyFounded = 8
}

/// <summary>Causa de muerte (para telemetría y reproducción).</summary>
public enum DeathCause : byte
{
    Age = 0,
    Starvation = 1
}

public readonly struct SimEvent
{
    public readonly SimEventKind Kind;
    public readonly ulong Tick;
    public readonly uint AntId;
    public readonly int ColonyId;
    public readonly float X;
    public readonly float Y;
    public readonly byte Cause;

    public SimEvent(SimEventKind kind, ulong tick, int colonyId, uint antId, float x, float y, byte cause = 0)
    {
        Kind = kind;
        Tick = tick;
        ColonyId = colonyId;
        AntId = antId;
        X = x;
        Y = y;
        Cause = cause;
    }
}