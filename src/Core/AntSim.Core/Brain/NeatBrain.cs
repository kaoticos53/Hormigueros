using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;

namespace AntSim.Core.Brain;

/// <summary>
/// Cerebro NEAT (F5.2c, rodaja 1): grafo acíclico con orden topológico
/// calculado UNA VEZ en construcción (Kahn con cola por id ascendente —
/// determinista) y activación en un pase lineal.
///
/// PARIDAD BIT A BIT con <see cref="MlpBrain"/> (contrato de la conversión
/// v1→v2): la suma de cada nodo acumula el bias PRIMERO y luego las fuentes
/// en orden ASCENDENTE de id. En un MLP denso convertido, el id de cada
/// fuente de capa l−1 crece con su índice de capa (ocultos asignados por
/// capa y ascendentes, entradas 0..18), así que "id ascendente" REPRODUCE
/// el orden de acumulación del MlpBrain (bias primero, fuentes por índice
/// de capa) y el float acumulado es idéntico.
///
/// Determinismo: pura, sin RNG, buffers reutilizados (cero asignaciones por
/// llamada tras la construcción).
/// </summary>
public sealed class NeatBrain : IBrain
{
    private readonly int[] _order;            // ids en orden topológico (ids fijos primero)
    private readonly float[] _bias;           // por índice de array interno
    private readonly byte[] _act;             // ActivationId por nodo
    private readonly Dictionary<int, int> _index; // id de gen → índice interno
    private readonly int[] _inStart;          // offset de entradas por nodo (orden de evaluación)
    private readonly int[] _inSrc;            // índice interno de la fuente
    private readonly float[] _inW;            // peso de la fuente
    private readonly float[] _value;
    private readonly float[] _activations;    // F5.0/F5.2c: registro por Evaluate (ids de gen)
    private readonly int _inputCount;

    public NeatBrain(IReadOnlyList<NodeGene> nodes, IReadOnlyList<ConnGene> conns)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (conns is null) throw new ArgumentNullException(nameof(conns));

        var byId = new Dictionary<int, NodeGene>(nodes.Count);
        foreach (var n in nodes) byId[n.Id] = n;

        _inputCount = AntSensorChannelInfo.Count;

        // Orden topológico determinista: Kahn con cola de MENOR id primero
        // (los ids fijos 0..24 anclan entradas y salidas; los ocultos crecen
        // monotónicamente). Empate ⇒ id menor primero, siempre igual.
        var adj = new Dictionary<int, List<int>>(nodes.Count);
        var indeg = new Dictionary<int, int>(nodes.Count);
        foreach (var n in nodes) { adj[n.Id] = new List<int>(); indeg[n.Id] = 0; }
        var active = conns.Where(c => c.Enabled).ToList();
        foreach (var c in active)
        {
            adj[c.From].Add(c.To);
            indeg[c.To]++;
        }
        var ready = new SortedSet<int>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<int>(nodes.Count);
        while (ready.Count > 0)
        {
            int id = ready.Min;
            ready.Remove(id);
            order.Add(id);
            foreach (int to in adj[id]) if (--indeg[to] == 0) ready.Add(to);
        }
        if (order.Count != nodes.Count)
            throw new ArgumentException("El grafo tiene un ciclo (v2.0 es feed-forward).");

        // Índices internos en el orden de evaluación.
        _order = order.ToArray();
        _bias = new float[_order.Length];
        _act = new byte[_order.Length];
        _index = new Dictionary<int, int>(_order.Length);
        for (int i = 0; i < _order.Length; i++)
        {
            NodeGene n = byId[_order[i]];
            _bias[i] = n.Bias;
            _act[i] = n.Act;
            _index[n.Id] = i;
        }

        // Entradas por nodo destino (ÍNDICE INTERNO), ordenadas por id de
        // fuente ascendente — el mismo orden de suma que el MLP denso.
        var buckets = new List<(int srcIdx, float w)>[_order.Length];
        for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<(int, float)>();
        foreach (var c in active)
            buckets[_index[c.To]].Add((_index[c.From], c.Weight));

        // Orden de fuente por ID DE GEN ascendente (no por índice interno): en
        // el genoma convertido, los ids de fuente de una capa l−1 crecen con
        // el índice de capa del MLP, reproduciendo el orden de acumulación
        // destino-major del MlpBrain (bias primero, fuentes por índice).
        int totalEdges = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i].Sort((a, b) =>
            {
                int bySrc = _order[a.srcIdx].CompareTo(_order[b.srcIdx]);
                return bySrc != 0 ? bySrc : a.w.CompareTo(b.w);
            });
            totalEdges += buckets[i].Count;
        }

        // Entradas por nodo destino, ordenadas por id de FUENTE ascendente
        // (la clave de la paridad: mismo orden de acumulación que el MLP).
        // IMPORTANTE: los bloques se agrupan por ÍNDICE INTERNO (orden de
        // evaluación), no por id de gen — el orden de Kahn no es el orden de
        // ids (las salidas 19..24 se evalúan DESPUÉS que los ocultos 25+).

        _inStart = new int[_order.Length + 1];
        _inSrc = new int[totalEdges];
        _inW = new float[totalEdges];
        int w = 0;
        for (int i = 0; i < _order.Length; i++)
        {
            _inStart[i] = w;
            foreach (var (srcIdx, weight) in buckets[i])
            {
                _inSrc[w] = srcIdx;
                _inW[w] = weight;
                w++;
            }
        }
        _inStart[_order.Length] = w;

        _value = new float[_order.Length];
        _activations = new float[_order.Length];
    }

    public BrainKind Kind => BrainKind.Neat;
    public int ContractVersion => BrainContract.CurrentVersion;

    /// <summary>Nº total de nodos del grafo — longitud del snapshot del canal F.</summary>
    public int ActivationTotal => _order.Length;

    public void Evaluate(in AntSensors sensors, ref AntDecision decision)
    {
        float[] value = _value;

        // Sensores primero: CopyTo emite por id 0..18, así que el bloque cae
        // directo en las posiciones de los inputs (ids < 19). Debe preceder al
        // pase de activación — los ocultos leen value de sus fuentes.
        Span<float> sensorBlock = stackalloc float[_inputCount];
        sensors.CopyTo(sensorBlock);
        for (int i = 0; i < _order.Length; i++)
            if (_order[i] < _inputCount) value[i] = sensorBlock[_order[i]];

        for (int i = 0; i < _order.Length; i++)
        {
            int id = _order[i];
            if (id < _inputCount) continue; // ya sembrado
            float acc = _bias[i]; // bias PRIMERO (paridad con MlpBrain)
            int start = _inStart[i], end = _inStart[i + 1];
            for (int k = start; k < end; k++)
                acc += _inW[k] * value[_inSrc[k]];
            value[i] = Activations.Apply((ActivationId)_act[i], acc);
        }

        // Registro del canal F: layout canónico [entradas, ocultas…, salidas]
        // = ids 0..18, luego ocultos por id, luego salidas 19..24 — idéntico
        // al convenio del MLP. (El orden de EVALUACIÓN no es este: las salidas
        // dependen de los ocultos y Kahn las pospone.)
        int w = 0;
        for (int i = 0; i < _order.Length; i++) if (_order[i] < _inputCount) _activations[w++] = value[i];
        for (int i = 0; i < _order.Length; i++) if (_order[i] >= NodeGene.FirstHiddenId) _activations[w++] = value[i];
        for (int i = 0; i < _order.Length; i++)
        {
            int id = _order[i];
            if (id >= NodeGene.FirstOutputId && id < NodeGene.FirstHiddenId) _activations[w++] = value[i];
        }

        // Salidas: ids 19..24 → índice interno por id.
        decision.Steer = value[_index[NodeGene.FirstOutputId + 0]];
        decision.Speed = value[_index[NodeGene.FirstOutputId + 1]];
        decision.DepositFood = value[_index[NodeGene.FirstOutputId + 2]];
        decision.DepositHome = value[_index[NodeGene.FirstOutputId + 3]];
        decision.DepositAlarm = value[_index[NodeGene.FirstOutputId + 4]];
        decision.Interact = value[_index[NodeGene.FirstOutputId + 5]];
    }

    /// <summary>
    /// Copia las activaciones del ÚLTIMO <see cref="Evaluate"/> al buffer, en
    /// el orden de evaluación (ids fijos primero). Formato idéntico al canal F
    /// del MLP (ver <see cref="MlpBrain.SnapshotActivations"/>).
    /// </summary>
    public void SnapshotActivations(Span<float> target)
    {
        if (target.Length < ActivationTotal)
            throw new ArgumentException($"El buffer tiene {target.Length}; se esperaban {ActivationTotal}.");
        for (int i = 0; i < ActivationTotal; i++) target[i] = _activations[i];
    }
}
