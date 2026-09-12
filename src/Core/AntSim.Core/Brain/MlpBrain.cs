using System;
using System.Collections.Generic;
using AntSim.Core.Contracts;

namespace AntSim.Core.Brain;

/// <summary>
/// Cerebro MLP de topología fija (contrato v1: 19 entradas, salidas según tamaños).
///
/// Disposición canónica de pesos (la misma que serializa el formato .antgenome):
/// por cada capa destino l (1..L−1) se almacena un bloque de
/// `n_l * (n_{l-1} + 1)` floats en orden *destination-major*: para cada destino d,
/// los n_{l-1} pesos de entrada seguidos de su bias.
///
/// Determinismo: evaluación pura con aritmética float en orden fijo; buffers
/// internos reutilizados (cero asignaciones por llamada).
/// </summary>
public sealed class MlpBrain : IBrain
{
    private readonly int[] _sizes;
    private readonly float[] _weights;
    private readonly int[] _layerOffset;
    private readonly ActivationId _hiddenActivation;
    private readonly ActivationId[] _outputActivations;
    private readonly float[] _a;
    private readonly float[] _b;
    private readonly float[] _activations; // F5.0: registro por Evaluate (orden canónico)
    private readonly int[] _layerOffsetActivation; // offset de cada capa en _activations
    private readonly int _inputCount;
    private readonly int _outputCount;

    public MlpBrain(
        int[] sizes,
        float[] weights,
        ActivationId hiddenActivation = ActivationId.Tanh,
        ActivationId[]? outputActivations = null)
    {
        if (sizes is null) throw new ArgumentNullException(nameof(sizes));
        if (weights is null) throw new ArgumentNullException(nameof(weights));
        if (sizes.Length < 2) throw new ArgumentException("Se requieren al menos capa de entrada y salida.", nameof(sizes));

        _sizes = (int[])sizes.Clone();
        _inputCount = _sizes[0];
        _outputCount = _sizes[_sizes.Length - 1];
        if (_inputCount < 1 || _outputCount < 1)
            throw new ArgumentException("Capas de entrada/salida deben tener tamaño ≥ 1.", nameof(sizes));

        _hiddenActivation = hiddenActivation;

        int expected = ExpectedWeightCount(_sizes);
        if (weights.Length != expected)
            throw new ArgumentException($"El array de pesos tiene {weights.Length} valores; se esperaban {expected}.", nameof(weights));
        _weights = (float[])weights.Clone();

        _layerOffset = new int[_sizes.Length];
        int offset = 0;
        for (int l = 1; l < _sizes.Length; l++)
        {
            _layerOffset[l] = offset;
            offset += _sizes[l] * (_sizes[l - 1] + 1);
        }

        // F5.0: offsets del registro de activaciones (capa 0 = 0).
        _layerOffsetActivation = new int[_sizes.Length];
        int actOffset = 0;
        for (int l = 1; l < _sizes.Length; l++)
        {
            _layerOffsetActivation[l] = actOffset;
            actOffset += _sizes[l];
        }

        if (outputActivations is null)
        {
            // Convención del contrato v1: canal 0 (steer) en [−1,1]; el resto en [0,1].
            outputActivations = new ActivationId[_outputCount];
            for (int i = 0; i < _outputCount; i++)
                outputActivations[i] = i == 0 ? ActivationId.Tanh : ActivationId.Sigmoid;
        }
        if (outputActivations.Length != _outputCount)
            throw new ArgumentException("outputActivations debe tener una entrada por salida.", nameof(outputActivations));
        _outputActivations = (ActivationId[])outputActivations.Clone();

        int maxLayer = 0;
        foreach (int n in _sizes) maxLayer = Math.Max(maxLayer, n);
        _a = new float[maxLayer];
        _b = new float[maxLayer];
        _activations = new float[ActivationTotal];
    }

    public BrainKind Kind => BrainKind.Mlp;

    public int ContractVersion => BrainContract.CurrentVersion;

    public IReadOnlyList<int> Sizes => _sizes;

    public int WeightCount => _weights.Length;

    public static int ExpectedWeightCount(int[] sizes)
    {
        int total = 0;
        for (int l = 1; l < sizes.Length; l++)
            total += sizes[l] * (sizes[l - 1] + 1);
        return total;
    }

    public void Evaluate(in AntSensors sensors, ref AntDecision decision)
    {
        float[] cur = _a;
        float[] next = _b;

        sensors.CopyTo(cur);
        Buffer.BlockCopy(cur, 0, _activations, 0, _inputCount * sizeof(float));

        for (int l = 1; l < _sizes.Length; l++)
        {
            int prevCount = _sizes[l - 1];
            int curCount = _sizes[l];
            int off = _layerOffset[l];
            bool last = l == _sizes.Length - 1;

            for (int d = 0; d < curCount; d++)
            {
                int block = off + d * (prevCount + 1);
                float acc = _weights[block + prevCount]; // bias al final del bloque
                for (int s = 0; s < prevCount; s++)
                    acc += _weights[block + s] * cur[s];

                next[d] = last
                    ? Activations.Apply(_outputActivations[d], acc)
                    : Activations.Apply(_hiddenActivation, acc);
            }

            // F5.0: registro de la capa recién computada (canal F de inspección).
            Buffer.BlockCopy(next, 0, _activations, _layerOffsetActivation[l], curCount * sizeof(float));

            (cur, next) = (next, cur);
        }

        decision.Steer = cur[0];
        decision.Speed = cur[Math.Min(1, _outputCount - 1)];
        decision.DepositFood = _outputCount > 2 ? cur[2] : 0f;
        decision.DepositHome = _outputCount > 3 ? cur[3] : 0f;
        decision.DepositAlarm = _outputCount > 4 ? cur[4] : 0f;
        decision.Interact = _outputCount > 5 ? cur[5] : 0f;
    }    /// <summary>Copia los pesos (útil para huellas y serialización .antgenome).</summary>
    public float[] CopyWeights() => (float[])_weights.Clone();

    /// <summary>Total de nodos del MLP (entradas + ocultas + salidas) — longitud
    /// del snapshot de activaciones del canal F.</summary>
    public int ActivationTotal
    {
        get { int t = 0; for (int l = 0; l < _sizes.Length; l++) t += _sizes[l]; return t; }
    }

    /// <summary>
    /// F5.0 (canal F): copia las activaciones del ÚLTIMO <see cref="Evaluate"/>
    /// al buffer <paramref name="target"/>, en el orden canónico del .antgenome
    /// (todas las entradas, luego cada capa oculta, luego las salidas). No
    /// evalúa nada: lee el registro que Evaluate dejó en <c>_activations</c>.
    /// Lanza si el buffer no alcanza para <see cref="ActivationTotal"/> floats.
    /// </summary>
    public void SnapshotActivations(Span<float> target)
    {
        int total = ActivationTotal;
        if (target.Length < total)
            throw new ArgumentException($"El buffer tiene {target.Length}; se esperaban {total}.", nameof(target));
        for (int i = 0; i < total; i++) target[i] = _activations[i];
    }
}
