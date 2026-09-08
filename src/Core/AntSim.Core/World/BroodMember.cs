namespace AntSim.Core.World;

public enum BroodKind : byte
{
    Egg = 0,
    Larva = 1,
    Pupa = 2
}

/// <summary>
/// Miembro de la cría (huevo/larva/pupa). Lleva el potencial genético g0 y la
/// nutrición acumulada (nutr_l): es la historia real que decide el vigor del
/// recién nacido. `Insert` ordena de forma determinista (FIFO / empates).
/// </summary>
public sealed class BroodMember
{
    public BroodKind Kind;
    public int Insert;          // orden de creación (empates deterministas)
    public float Age;           // s en el estadio actual
    public float G0;            // potencial genético del huevo [0.4, 1.0]
    public float Nutrition;     // ep ingeridos durante la vida larvaria
    public bool Weak;           // pupa débil (minim) si se eclosiona con n̄ < 1
}