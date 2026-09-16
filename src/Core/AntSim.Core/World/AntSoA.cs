using System;
using System.Collections.Generic;

namespace AntSim.Core.World;

/// <summary>
/// F5.3 rodaja 1 — Struct-of-Arrays para los datos de hormigas.
///
/// Cada campo del <see cref="Ant"/> se almacena en un array contiguo en vez
/// de en un objeto referenciado por puntero. Esto elimina la indirección de
/// referencia por hormiga en el bucle interno (ActAllAnts → Act) y mejora
/// significativamente la localidad de caché: en vez de cargar una línea de
/// caché por hormiga (64 B de cabecera + campos), el bucle recorre un solo
/// array por campo, aprovechando prefetch secuencial y, en el futuro,
/// vectorización SIMD.
///
/// <b>Estado actual (rodaja 1):</b> la infraestructura está lista pero el
/// inner loop aún opera sobre objetos Ant (AoS) porque AntSenses.Build y
/// Brain.Evaluate necesitan campos del objeto. El SoA se usa para la
/// compactación de muertas (CompactDeadAnts), que está deshabilitada hasta
/// que HashLine() deje de incluir hormigas muertas (rodaja 2).
///
/// <b>Contrato de determinismo:</b> el SoA es un ESPEJO transitivo — se
/// construye al inicio de cada Step desde la lista de Ant, el bucle lo
/// muta, y se sincroniza de vuelta al final del Step. Los valores exactos
/// que entren en HashLine() provienen de los objetos Ant (que reflejan el
/// estado post-SoA). Mismo código ⇒ mismos valores ⇒ mismo hash.
/// </summary>
public sealed class AntSoA
{
    // ─── Capacidad y longitud ───────────────────────────────────────────
    private int _len;

    /// <summary>Número de hormigas activas en los arrays.</summary>
    public int Length => _len;

    // ─── Arrays paralelos (cada uno tiene capacidad Capacity) ──────────
    // Identidad y parentesco
    public uint[] Id = Array.Empty<uint>();
    public int[] ColonyId = Array.Empty<int>();

    // Pose
    public float[] X = Array.Empty<float>();
    public float[] Y = Array.Empty<float>();
    public float[] Heading = Array.Empty<float>();

    // Estado interno
    public float[] Energy = Array.Empty<float>();
    public float[] EnergyCapacity = Array.Empty<float>();
    public float[] Age = Array.Empty<float>();
    public float[] Lifespan = Array.Empty<float>();

    // Moduladores por vigor
    public float[] Vigor = Array.Empty<float>();
    public float[] SpeedScale = Array.Empty<float>();
    public float[] SensorScale = Array.Empty<float>();

    // Carga
    public bool[] HasLoad = Array.Empty<bool>();
    public float[] LoadValue = Array.Empty<float>();
    public bool[] LoadIsLoot = Array.Empty<bool>();
    public float[] LootFromColony = Array.Empty<float>();

    // Fitness (se escribe en SoA, se sincroniza al final)
    public double[] Fitness = Array.Empty<double>();

    // Control
    public float[] InteractCooldown = Array.Empty<float>();
    public bool[] Alive = Array.Empty<bool>();

    // ─── EnsureCapacity ────────────────────────────────────────────────
    /// <summary>
    /// Asegura que todos los arrays tengan capacidad para <paramref name="n"/>
    /// elementos. Solo reasigna si la capacidad actual es menor.
    /// </summary>
    public void EnsureCapacity(int n)
    {
        if (n <= Id.Length) return;

        Id = Grow(Id, n);
        ColonyId = Grow(ColonyId, n);
        X = Grow(X, n);
        Y = Grow(Y, n);
        Heading = Grow(Heading, n);
        Energy = Grow(Energy, n);
        EnergyCapacity = Grow(EnergyCapacity, n);
        Age = Grow(Age, n);
        Lifespan = Grow(Lifespan, n);
        Vigor = Grow(Vigor, n);
        SpeedScale = Grow(SpeedScale, n);
        SensorScale = Grow(SensorScale, n);
        HasLoad = Grow(HasLoad, n);
        LoadValue = Grow(LoadValue, n);
        LoadIsLoot = Grow(LoadIsLoot, n);
        LootFromColony = Grow(LootFromColony, n);
        Fitness = Grow(Fitness, n);
        InteractCooldown = Grow(InteractCooldown, n);
        Alive = Grow(Alive, n);
    }

    // ─── Copiar desde Ant[] → SoA ──────────────────────────────────────
    /// <summary>
    /// Copia los campos de una lista de <see cref="Ant"/> a los arrays SoA.
    /// Solo copia hormigas con <c>Alive == true</c> (el bucle interno
    /// las itera en orden ascendente de antId, igual que la versión AoS).
    /// </summary>
    public int PopulateFrom(System.Collections.Generic.IReadOnlyList<Ant> adults)
    {
        // Primero: contar vivas para pre-asignar.
        int count = 0;
        for (int i = 0; i < adults.Count; i++)
            if (adults[i].Alive) count++;

        EnsureCapacity(count);

        int idx = 0;
        for (int i = 0; i < adults.Count; i++)
        {
            var a = adults[i];
            if (!a.Alive) continue;

            Id[idx] = a.Id;
            ColonyId[idx] = a.ColonyId;
            X[idx] = a.X;
            Y[idx] = a.Y;
            Heading[idx] = a.Heading;
            Energy[idx] = a.Energy;
            EnergyCapacity[idx] = a.EnergyCapacity;
            Age[idx] = a.Age;
            Lifespan[idx] = a.Lifespan;
            Vigor[idx] = a.Vigor;
            SpeedScale[idx] = a.SpeedScale;
            SensorScale[idx] = a.SensorScale;
            HasLoad[idx] = a.HasLoad;
            LoadValue[idx] = a.LoadValue;
            LoadIsLoot[idx] = a.LoadIsLoot;
            LootFromColony[idx] = a.LootFromColony;
            Fitness[idx] = a.Fitness;
            InteractCooldown[idx] = a.InteractCooldown;
            Alive[idx] = true;

            idx++;
        }

        _len = idx;
        return idx;
    }

    // ─── Copiar desde SoA → Ant[] ──────────────────────────────────────
    /// <summary>
    /// Escribe los valores modificados del SoA de vuelta en los objetos
    /// <see cref="Ant"/> correspondientes. Solo actualiza campos que el
    /// bucle interno mutó (pose, energía, carga, fitness, cooldown).
    /// Los campos que NO se modifican en Act (Brain, Genome, Age, Alive,
    /// etc.) se actualizan por separado en ApplyDeaths.
    /// </summary>
    public void SyncBackTo(IReadOnlyList<Ant> adults)
    {
        // Reconstruir el mapa antId → índice en Adults (para sync point-to-point).
        // El SoA fue populado desde adults con Alive == true, manteniendo el
        // orden original. Recorremos adults en el mismo orden que PopulateFrom.
        int soaIdx = 0;
        for (int i = 0; i < adults.Count; i++)
        {
            var a = adults[i];
            if (!a.Alive) continue;
            if (soaIdx >= _len) break;

            // Solo sincronizamos campos que Act MUTA. Los campos que ApplyDeaths
            // maneja (Age, Alive) se actualizan allí, no aquí.
            a.X = X[soaIdx];
            a.Y = Y[soaIdx];
            a.Heading = Heading[soaIdx];
            a.Energy = Energy[soaIdx];
            a.Fitness = Fitness[soaIdx];
            a.HasLoad = HasLoad[soaIdx];
            a.LoadValue = LoadValue[soaIdx];
            a.LoadIsLoot = LoadIsLoot[soaIdx];
            a.LootFromColony = LootFromColony[soaIdx];
            a.InteractCooldown = InteractCooldown[soaIdx];

            soaIdx++;
        }
    }

    // ─── Helpers internos ──────────────────────────────────────────────
    private static uint[] Grow(uint[] arr, int n)
    {
        var r = new uint[Math.Max(n, arr.Length * 2)];
        Array.Copy(arr, r, arr.Length);
        return r;
    }
    private static int[] Grow(int[] arr, int n)
    {
        var r = new int[Math.Max(n, arr.Length * 2)];
        Array.Copy(arr, r, arr.Length);
        return r;
    }
    private static float[] Grow(float[] arr, int n)
    {
        var r = new float[Math.Max(n, arr.Length * 2)];
        Array.Copy(arr, r, arr.Length);
        return r;
    }
    private static double[] Grow(double[] arr, int n)
    {
        var r = new double[Math.Max(n, arr.Length * 2)];
        Array.Copy(arr, r, arr.Length);
        return r;
    }
    private static bool[] Grow(bool[] arr, int n)
    {
        var r = new bool[Math.Max(n, arr.Length * 2)];
        Array.Copy(arr, r, arr.Length);
        return r;
    }
}
