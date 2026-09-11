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
    DropFood = 0
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

    public SimCommand(SimCommandKind kind, float x, float y)
    {
        Kind = kind;
        X = x;
        Y = y;
    }
}
