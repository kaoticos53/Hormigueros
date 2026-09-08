namespace AntSim.Core.World;

/// <summary>Ítem de comida en el mundo. Amount es el valor energético en ep.</summary>
public sealed class FoodItem
{
    public const float MaxValue = 10f; // tope de normalización del sensor foodSize

    public uint Id;
    public float X;
    public float Y;
    public float Amount; // ep

    public float SizeNormalized => Amount / MaxValue;
}