namespace AntSim.Core.World;

/// <summary>
/// Comandos de usuario que tocan el mundo (Fase 4, F4.0). Toda acción que
/// muta la simulación pasa por aquí: se encola con <c>WorldSim.EnqueueCommand</c>,
/// se aplica en el PUNTO CANÓNICO del próximo <c>Step()</c> (tras avanzar el tick,
/// antes de que actúe cualquier hormiga) y queda registrada en el Canal B con un
/// evento <see cref="SimEventKind.CommandExecuted"/> — así el .antlog y el modo
/// verify la ven, y "misma semilla + mismos comandos ⇒ mismo mundo" es verificable.
///
/// Pause/Speed NO son comandos del mundo: no mutan estado, los gestiona el
/// presenter (escalan cuántos ticks corren por segundo, nunca el RNG ni el orden).
/// </summary>
public enum SimCommandKind : byte
{
    /// <summary>Suelta un ítem de comida en (X, Y) — la única palanca económica del jugador.</summary>
    DropFood = 0,

    /// <summary>Guarda la partida (slot = <see cref="SimCommand.Slot"/>). Comando de
    /// OBSERVACIÓN: no muta el estado del mundo (los hashes con y sin él son
    /// idénticos); queda registrado en el Canal B porque el historial de la
    /// partida incluye CUÁNDO se guardó. Tras el Step que lo procesa, el sim
    /// expone la petición en <c>WorldSim.SaveRequests</c> y el presenter escribe
    /// el .antsave con <c>WorldSimSave.Save</c>.</summary>
    SaveGame = 1
}

/// <summary>Comando inmutable con destino. El tick de aplicación lo asigna el sim.</summary>
public readonly struct SimCommand
{
    /// <summary>Cantidad ep del ítem soltado (constante determinista, dentro del rango
    /// natural 4–6 ep del spawn del mundo — no consume RNG: la comida del jugador no
    /// es una tirada aleatoria y el flujo del mundo queda intacto).</summary>
    public const float DropFoodAmount = 5f;

    public readonly SimCommandKind Kind;
    public readonly float X;
    public readonly float Y;
    /// <summary>Slot de guardado para SaveGame (0–255); ignorable en el resto.</summary>
    public readonly byte Slot;

    public SimCommand(SimCommandKind kind, float x, float y, byte slot = 0)
    {
        Kind = kind;
        X = x;
        Y = y;
        Slot = slot;
    }
}
