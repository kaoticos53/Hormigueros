namespace AntSim.Core.World;

/// <summary>
/// Petición de guardado (F4.0): resultado de aplicar un comando
/// <see cref="SimCommandKind.SaveGame"/> en el punto canónico. El presenter la
/// consume tras el Step y escribe el checkpoint con <c>WorldSimSave.Save</c>.
/// No es estado del mundo: es un mensaje del sim hacia la vista.
/// </summary>
public readonly struct SaveRequest
{
    /// <summary>Slot solicitado (0–255); el presenter lo mapea a su naming de archivo.</summary>
    public readonly byte Slot;

    /// <summary>Tick en el que se aplicó el comando (el checkpoint contiene este tick).</summary>
    public readonly ulong Tick;

    public SaveRequest(byte slot, ulong tick)
    {
        Slot = slot;
        Tick = tick;
    }
}
