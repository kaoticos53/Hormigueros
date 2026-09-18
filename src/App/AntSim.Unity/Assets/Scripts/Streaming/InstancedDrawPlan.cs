namespace AntSim.Unity.Scripts.Streaming
{
/// <summary>
/// F5.3 rodaja 3 — plan de dibujo INSTANCIADO (GPU instancing).
///
/// El presenter dibujaba una llamada por hormiga (<c>Graphics.DrawMesh</c>): con
/// 2 colonias y varioscientos de hormigas son cientos de draw calls por frame y
/// por vista, y el multi-visor multiplica por 4. La GPU puede dibujar la misma
/// malla con el mismo material muchas veces en UNA llamada
/// (<c>Graphics.DrawMeshInstanced</c>), pero con dos límites que sí son contrato:
/// un máximo de instancias por llamada y un lote por MATERIAL (no se pueden
/// mezclar materiales en un lote).
///
/// Esta clase es la parte pura de ese plan —cuántos lotes hacen falta y cuántas
/// llamadas se ahorran— para que el número que se publica en los documentos sea
/// verificable sin arrancar el editor. El presenter la usa para trocear.
///
/// POR QUÉ VIVE AQUÍ Y NO EN EL CORE: el Core es netstandard y no viaja al
/// proyecto Unity (la vista consume el STREAM, no la biblioteca del mundo). El
/// esqueleto Unity mantiene sus modelos puros en este directorio y la suite
/// headless los compila enlazados (ver el .csproj de tests), así que el contrato
/// del troceo se verifica sin editor sin romper esa frontera.
/// </summary>
public static class InstancedDrawPlan
{
    /// <summary>
    /// Máximo de instancias por llamada de <c>Graphics.DrawMeshInstanced</c>.
    /// Es un límite del motor (1023), no nuestro: por eso es una constante
    /// pública y el presenter trocea contra ella en vez de contra un número
    /// escrito en su código.
    /// </summary>
    public const int BatchLimit = 1023;

    /// <summary>Lotes necesarios para <paramref name="instanceCount"/> instancias
    /// del mismo material (0 instancias ⇒ 0 lotes: no se dibuja nada).</summary>
    public static int Batches(int instanceCount)
    {
        if (instanceCount <= 0) return 0;
        return (instanceCount + BatchLimit - 1) / BatchLimit;
    }

    /// <summary>Llamadas de dibujo del plan instanciado: suma de lotes por material.</summary>
    public static int DrawCalls(System.Collections.Generic.IReadOnlyList<int> perMaterialCounts)
    {
        int total = 0;
        for (int i = 0; i < perMaterialCounts.Count; i++) total += Batches(perMaterialCounts[i]);
        return total;
    }

    /// <summary>Línea base: una llamada por instancia (lo que se hacía antes).</summary>
    public static int PerInstanceDrawCalls(int instanceCount) => instanceCount <= 0 ? 0 : instanceCount;

    /// <summary>Índice inicial de cada lote dentro de las instancias de un material
    /// (el presenter copia su búfer por tramos de este tamaño).</summary>
    public static int BatchStart(int batchIndex) => batchIndex * BatchLimit;

    /// <summary>Tamaño del lote <paramref name="batchIndex"/> de un material con
    /// <paramref name="instanceCount"/> instancias (el último suele ir incompleto).</summary>
    public static int BatchSize(int instanceCount, int batchIndex)
    {
        if (instanceCount <= 0 || batchIndex < 0) return 0;
        int start = BatchStart(batchIndex);
        if (start >= instanceCount) return 0;
        int rest = instanceCount - start;
        return rest > BatchLimit ? BatchLimit : rest;
    }
}
}
