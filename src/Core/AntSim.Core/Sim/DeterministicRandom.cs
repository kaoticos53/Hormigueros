using System;

namespace AntSim.Core.Sim;

/// <summary>
/// RNG determinista xoshiro256** para la simulación.
/// - Mismo estado inicial (semilla) ⇒ misma secuencia exacta de números.
/// - El estado completo son 4 ulongs: se serializa tal cual en los checkpoints
///   para que un guardado/carga continúe la secuencia bit a bit.
/// - Nunca usar la RNG del framework ni hilos: solo flujos de esta clase,
///   uno por colonia/subsistema, derivados con <see cref="Fork"/>.
/// </summary>
public struct DeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRandom(ulong seed)
    {
        // splitmix64 para sembrar los 4 registros desde una única semilla.
        ulong smix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        _s0 = smix(seed);
        _s1 = smix(_s0);
        _s2 = smix(_s1);
        _s3 = smix(_s2);
        if ((_s0 | _s1 | _s2 | _s3) == 0UL)
            _s0 = 1UL; // xoshiro exige estado no nulo (prácticamente inalcanzable)
    }

    private DeterministicRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
    }

    /// <summary>Reconstruye un flujo desde su estado completo (checkpoints).</summary>
    public static DeterministicRandom FromState(ulong s0, ulong s1, ulong s2, ulong s3)
        => new DeterministicRandom(s0, s1, s2, s3);

    /// <summary>Estado completo del flujo: lo que se serializa en los checkpoints.</summary>
    public readonly (ulong S0, ulong S1, ulong S2, ulong S3) State => (_s0, _s1, _s2, _s3);

    /// <summary>
    /// Deriva un flujo hijo independiente con una sal determinista.
    /// Es la forma correcta de crear un RNG por colonia a partir de la semilla del mundo.
    /// </summary>
    public DeterministicRandom Fork(ulong salt)
    {
        // Mezcla la sal con el estado actual y siembra un flujo nuevo: nunca
        // consume ni altera el flujo padre.
        ulong mix = (NextUInt64() ^ salt) + 0x9E3779B97F4A7C15UL;
        return new DeterministicRandom(mix);
    }

    public ulong NextUInt64()
    {
        ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);

        return result;
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    /// <summary>Uniforme en [0, 1) con 53 bits de entropía (máxima precisión double).</summary>
    public double NextDouble01() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public float NextFloat01() => (float)NextDouble01();

    /// <summary>Entero uniforme en [minInclusive, maxExclusive). maxExclusive &gt; minInclusive.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive debe ser mayor que minInclusive.");
        int range = maxExclusive - minInclusive;
        return minInclusive + (int)(NextUInt64() % (ulong)range);
    }

    public bool NextBool() => (NextUInt64() & 1UL) == 1UL;

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
}
