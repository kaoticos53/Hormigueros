namespace AntSim.Core.World;

/// <summary>Ítem de comida en el mundo. Amount es el valor energético en ep.
/// F5.2a.1: un ítem COMPUESTO (hoja) lleva CutsLeft > 0 — el pickup le resta
/// un corte y la hormiga se lleva un fragmento; el ítem simple (CutsLeft = 0)
/// se elimina entero como siempre.</summary>
public sealed class FoodItem
{
    public const float MaxValue = 10f; // tope de normalización del sensor foodSize

    public uint Id;
    public float X;
    public float Y;
    public float Amount; // ep (en una hoja: el valor REMANENTE tras los cortes)

    // — F5.2a.1: ítem compuesto —
    public int CutsLeft;    // 0 = ítem simple (comportamiento clásico)
    public int CutsInitial; // cortes de la hoja recién nacida (fragmento = Amount / CutsLeft)

    public bool IsLeaf => CutsLeft > 0;

    public float SizeNormalized => Amount / MaxValue;
}