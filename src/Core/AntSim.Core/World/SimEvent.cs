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
    ColonyExtinct = 12,

    /// <summary>F5.2a.1: la hormiga AntId cortó un fragmento de la hoja en X/Y
    /// (la hoja pierde un corte; el Pickup del fragmento se emite aparte).</summary>
    LeafCut = 13,

    /// <summary>F5.2a.1: la hoja en X/Y agotó su último corte y desaparece
    /// (acompaña al ItemConsumed del mundo).</summary>
    LeafDepleted = 14,

    /// <summary>F5.2a.2: la colonia ColonyId procesó carga hacia el hongo
    /// (solo especies con FungusMax > 0). AntId = 0; Cause = 0.</summary>
    FungusFed = 15,

    /// <summary>F5.2a.2: la digestión convirtió hongo → stock en la colonia
    /// ColonyId (cada tick con digestión; agregable en canal C).</summary>
    FungusDigested = 16,

    /// <summary>F5.2b.1: la hormiga AntId de la colonia ColonyId (ATACANTE)
    /// golpeó a la presa Cause=id de la víctima en X/Y. El mundo inyectó
    /// alarma en la capa de la presa y el golpe robó ep como carga.
    /// Agregable en canal C (bloque raids).</summary>
    Strike = 17,

    /// <summary>F5.2b.1: la colonia ColonyId (saqueadora) descargó botín de
    /// incursión en SU nido — equivale a Unload para el relevo (el evento
    /// Unload se emite aparte, con el botín como carga). X/Y = nido.</summary>
    RaidInflow = 18,

    /// <summary>F5.2b.1: la colonia ColonyId (SAQUEADA) perdió stock. AntId = 0;
    /// X/Y = su nido; Cause = ep robados × 100 (centésimas, sin float).
    /// Telemetría de tarjeta: NO toca el hash ni el estado del saqueador.</summary>
    StockRobbed = 19
}

/// <summary>Causa de muerte (para telemetría y reproducción).</summary>
public enum DeathCause : byte
{
    Age = 0,
    Starvation = 1,

    /// <summary>F5.2b.1: la energía llegó a 0 por golpes de una Eciton
    /// (el mundo la mató, no el metabolismo).</summary>
    Combat = 2
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