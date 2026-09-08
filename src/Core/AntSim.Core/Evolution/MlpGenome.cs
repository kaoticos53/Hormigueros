using System;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Sim;

namespace AntSim.Core.Evolution;

/// <summary>
/// Genoma MLP (Fase 2): topología fija + vector de pesos canónico.
/// - Crossover uniforme por peso (padre A/B elegidos con el RNG).
/// - Mutación gaussiana (Box-Muller) con σ por defecto 0.05.
/// - Distancia en espacio de pesos (media de |Δw|) — proxy v1 de la diversidad;
///   las sondas comportamentales llegan en Fase 5.
/// Determinista: mismas entradas + mismo RNG ⇒ mismo resultado bit a bit.
/// </summary>
public sealed class MlpGenome : IGenome
{
    public const float DefaultSigma = 0.05f;

    private readonly int[] _sizes;
    private readonly float[] _weights;

    public MlpGenome(int[] sizes, float[] weights, double fitness = 0.0)
    {
        if (sizes is null) throw new ArgumentNullException(nameof(sizes));
        if (weights is null) throw new ArgumentNullException(nameof(weights));
        if (sizes.Length < 2 || sizes[0] < 1 || sizes[^1] < 1)
            throw new ArgumentException("Topología inválida.", nameof(sizes));
        int expected = MlpBrain.ExpectedWeightCount(sizes);
        if (weights.Length != expected)
            throw new ArgumentException($"Se esperaban {expected} pesos; hay {weights.Length}.", nameof(weights));

        _sizes = (int[])sizes.Clone();
        _weights = (float[])weights.Clone();
        Fitness = fitness;
    }

    public BrainKind Kind => BrainKind.Mlp;
    public int ContractVersion => BrainContract.CurrentVersion;
    public double Fitness { get; set; }

    public int[] Sizes => (int[])_sizes.Clone();
    public int WeightCount => _weights.Length;

    public MlpGenome Clone() => new MlpGenome(_sizes, _weights, Fitness);

    public IGenome CloneGenome() => Clone();

    public MlpBrain ToBrain() => new MlpBrain(_sizes, _weights);

    public static MlpGenome Random(DeterministicRandom rng, int[] sizes)
    {
        int n = MlpBrain.ExpectedWeightCount(sizes);
        var w = new float[n];
        for (int i = 0; i < n; i++)
            w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        return new MlpGenome(sizes, w);
    }

    /// <summary>Crossover uniforme: cada peso viene del padre A o del B (50/50).</summary>
    public static MlpGenome Crossover(MlpGenome a, MlpGenome b, DeterministicRandom rng)
    {
        if (a.WeightCount != b.WeightCount)
            throw new ArgumentException("Los genomas deben tener la misma topología para cruzarse.");
        var w = new float[a.WeightCount];
        float[] wa = a._weights;
        float[] wb = b._weights;
        for (int i = 0; i < w.Length; i++)
            w[i] = rng.NextBool() ? wa[i] : wb[i];
        return new MlpGenome(a._sizes, w);
    }

    /// <summary>Mutación gaussiana aditiva con σ (Box-Muller sobre el RNG determinista).</summary>
    public void Mutate(DeterministicRandom rng, float sigma = DefaultSigma)
    {
        for (int i = 0; i < _weights.Length; i++)
        {
            double u1 = Math.Max(rng.NextDouble01(), 1e-12);
            double u2 = rng.NextDouble01();
            double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            _weights[i] = _weights[i] + (float)gauss * sigma;
        }
    }

    /// <summary>
    /// Distancia de pesos normalizada (media de |Δw|). Devuelve 1 si las
    /// topologías difieren (genomas incomparables en este espacio).
    /// </summary>
    public double DistanceTo(MlpGenome other)
    {
        if (other is null || other.WeightCount != WeightCount) return 1.0;
        double sum = 0.0;
        for (int i = 0; i < _weights.Length; i++)
            sum += Math.Abs(_weights[i] - other._weights[i]);
        return sum / _weights.Length;
    }

    /// <summary>Copia de los pesos (para serialización y huellas).</summary>
    public float[] CopyWeights() => (float[])_weights.Clone();
}