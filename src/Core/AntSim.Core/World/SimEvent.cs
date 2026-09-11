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
    ColonyFounded = 8,
    GenomeEnteredElite = 9,
    GenomeDiscarded = 10,

    /// <summary>Comando de usuario aplicado (Fase 4): la vista registra la causa,
    /// el .antlog la reproduce (misma semilla + mismos comandos ⇒ mismo mundo).
    /// X/Y = coordenadas del comando; AntId = kind del comando.</summary>
    CommandExecuted = 11,

    /// <summary>F4.2: la colonia ColonyId quedó sin adultas NI cría — extinción
    /// completa (el nido permanece; la tierra no se consume). Se emite UNA vez,
    /// por transición de estado (no hay evento por hormiga).</summary>
    ColonyExtinct = 12
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