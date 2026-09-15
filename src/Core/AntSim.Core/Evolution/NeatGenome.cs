using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;

namespace AntSim.Core.Evolution;

/// <summary>Tipo de nodo del genoma NEAT (F5.2c, rodaja 1).</summary>
public enum NodeKind : byte
{
    Input = 0,
    Hidden = 1,
    Output = 2,
}

/// <summary>
/// Gen de nodo NEAT: id estable, sesgo y activación (ids de
/// <see cref="Brain.ActivationId"/>, serializables). Los ids 0..18 son las
/// entradas (orden canónico de <c>AntSensorChannel</c>) y 19..24 las salidas
/// (contrato IBrain v1 congelado); los ocultos parten de 25.
/// </summary>
public sealed record NodeGene(int Id, NodeKind Kind, float Bias, byte Act)
{
    public const int FirstInputId = 0;
    public const int FirstOutputId = AntSensorChannelInfo.Count; // 19
    public const int FirstHiddenId = FirstOutputId + AntDecision.DecisionCount; // 25
}

/// <summary>
/// Gen de conexión NEAT: innovación (estable dentro del pool, canónica al
/// importar), extremos por id de nodo, peso y bandera Enabled. Los genes
/// desactivados NUNCA se borran (histórico NEAT para el alineamiento).
/// </summary>
public sealed record ConnGene(int Innovation, int From, int To, float Weight, bool Enabled);

/// <summary>
/// Genoma NEAT (F5.2c): grafo acíclico de <see cref="NodeGene"/>/
/// <see cref="ConnGene"/> sobre el contrato IBrain v1 congelado (19 sensores,
/// 6 salidas — NEAT solo añade nodos ocultos, nunca canales).
///
/// Invariantes (validadas en construcción y tras cada mutación; la
/// deserialización valida lo mismo):
///   1. ids de nodo únicos; exactamente 19 Input (ids 0..18) y 6 Output
///      (ids 19..24);
///   2. toda conexión referencia nodos existentes;
///   3. sin ciclos (v2.0 feed-forward — Kahn);
///   4. sin conexiones duplicadas activas (mismo From/To);
///   5. innovations únicas dentro del genoma;
///   6. topes 500 nodos / 2000 conexiones.
///
/// Determinista: misma construcción ⇒ misma evaluación bit a bit.
/// </summary>
public sealed class NeatGenome : IGenome
{
    public const int MaxNodes = 500;
    public const int MaxConns = 2000;

    private readonly List<NodeGene> _nodes;
    private readonly List<ConnGene> _conns;

    public NeatGenome(IEnumerable<NodeGene> nodes, IEnumerable<ConnGene> conns, double fitness = 0.0)
    {
        _nodes = new List<NodeGene>(nodes ?? throw new ArgumentNullException(nameof(nodes)));
        _conns = new List<ConnGene>(conns ?? throw new ArgumentNullException(nameof(conns)));
        Validate();
        Fitness = fitness;
    }

    public BrainKind Kind => BrainKind.Neat;
    public int ContractVersion => BrainContract.CurrentVersion;
    public double Fitness { get; set; }

    public IReadOnlyList<NodeGene> Nodes => _nodes;
    public IReadOnlyList<ConnGene> Conns => _conns;

    public NeatGenome Clone() => new NeatGenome(_nodes, _conns, Fitness);
    public IGenome CloneGenome() => Clone();

    /// <summary>Cerebro NEAT con orden topológico cacheado (una vez por genoma).</summary>
    public NeatBrain ToBrain() => new NeatBrain(_nodes, _conns);

    // ————— Mutación in situ (F5.2c rodaja 2): revalidan tras cada cambio —————

    /// <summary>Orden topológico de los ids (Kahn, menor id en empate) — el
    /// mismo cálculo del cerebro, expuesto para AddConn (guarda anti-ciclo).</summary>
    public int[] TopologicalOrder()
    {
        var adj = new Dictionary<int, List<int>>();
        var indeg = new Dictionary<int, int>();
        foreach (var n in _nodes) { adj[n.Id] = new List<int>(); indeg[n.Id] = 0; }
        foreach (var c in _conns)
        {
            if (!c.Enabled) continue;
            adj[c.From].Add(c.To);
            indeg[c.To]++;
        }
        var ready = new SortedSet<int>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<int>(_nodes.Count);
        while (ready.Count > 0)
        {
            int id = ready.Min;
            ready.Remove(id);
            order.Add(id);
            foreach (int to in adj[id]) if (--indeg[to] == 0) ready.Add(to);
        }
        return order.ToArray();
    }

    /// <summary>Id libre más bajo ≥ 25 para un nodo oculto nuevo.</summary>
    public int NextHiddenId()
    {
        var used = new HashSet<int>(_nodes.Select(n => n.Id));
        int id = NodeGene.FirstHiddenId;
        while (used.Contains(id)) id++;
        return id;
    }

    /// <summary>Peso de la conexión con esa innovation (revalida el genoma).</summary>
    public void SetWeight(int innovation, float weight)
    {
        for (int i = 0; i < _conns.Count; i++)
        {
            if (_conns[i].Innovation != innovation) continue;
            _conns[i] = _conns[i] with { Weight = weight };
            Validate();
            return;
        }
        throw new ArgumentException($"No hay conexión con innovation {innovation}.");
    }

    /// <summary>Bias del nodo con ese id (revalida el genoma).</summary>
    public void SetBias(int nodeId, float bias)
    {
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].Id != nodeId) continue;
            _nodes[i] = _nodes[i] with { Bias = bias };
            Validate();
            return;
        }
        throw new ArgumentException($"No hay nodo con id {nodeId}.");
    }

    /// <summary>Enabled de la conexión con esa innovation (revalida: togglear
    /// puede romper la unicidad (from,to) si el pool introdujo otra activa —
    /// el registro de innovaciones lo impide, la validación lo garantiza).</summary>
    public void SetEnabled(int innovation, bool enabled)
    {
        for (int i = 0; i < _conns.Count; i++)
        {
            if (_conns[i].Innovation != innovation) continue;
            _conns[i] = _conns[i] with { Enabled = enabled };
            Validate();
            return;
        }
        throw new ArgumentException($"No hay conexión con innovation {innovation}.");
    }

    /// <summary>Añade una conexión validando los invariantes (unicidad, ciclo,
    /// topes) — la puerta ÚNICA para las mutaciones estructurales.</summary>
    public void AddConnChecked(ConnGene conn)
    {
        _conns.Add(conn);
        try { Validate(); }
        catch
        {
            _conns.RemoveAt(_conns.Count - 1);
            throw;
        }
    }

    /// <summary>Añade un nodo oculto (id libre más bajo) — usado por AddNode vía
    /// las conexiones que lo referencian; expuesto para tests.</summary>
    public void AddHiddenNode(float bias = 0f, byte act = (byte)ActivationId.Tanh)
    {
        int id = NextHiddenId();
        _nodes.Add(new NodeGene(id, NodeKind.Hidden, bias, act));
        try { Validate(); }
        catch
        {
            _nodes.RemoveAt(_nodes.Count - 1);
            throw;
        }
    }

    /// <summary>
    /// Distancia genómica NEAT clásica: δ = c1·E/N + c2·D/N + c3·W̄, con
    /// N = nº de conexiones del genoma más grande. Devuelve 1.0 si el otro no
    /// es NEAT (genomas incomparables — el mismo convenio de
    /// <see cref="MlpGenome.DistanceTo"/>).
    /// </summary>
    public double DistanceTo(NeatGenome? other, float c1 = 1f, float c2 = 1f, float c3 = 0.4f)
    {
        if (other is null) return 1.0;
        // Innovación → (peso, activo) por genoma.
        var mine = ByInnovation(_conns);
        var theirs = ByInnovation(other._conns);
        int maxMine = _conns.Count > 0 ? 0 : 0, maxTheirs = 0;
        foreach (var c in _conns) maxMine = Math.Max(maxMine, c.Innovation);
        foreach (var c in other._conns) maxTheirs = Math.Max(maxTheirs, c.Innovation);
        int disjoint = 0, excess = 0, matched = 0;
        double wDiff = 0.0;
        foreach (var (innov, conn) in mine)
        {
            if (theirs.TryGetValue(innov, out var otherConn))
            {
                if (conn.Enabled || otherConn.Enabled) // genes apagados en ambos no cuentan
                {
                    matched++;
                    wDiff += Math.Abs(conn.Weight - otherConn.Weight);
                }
            }
            else if (conn.Enabled)
            {
                if (innov > maxTheirs) excess++; else disjoint++;
            }
        }
        foreach (var (innov, conn) in theirs)
        {
            if (!mine.ContainsKey(innov) && conn.Enabled)
            {
                if (innov > maxMine) excess++; else disjoint++;
            }
        }

        int n = Math.Max(mine.Count, theirs.Count);
        if (n == 0) return 0.0; // dos genomas sin conexiones: idénticos en forma
        return (c1 * excess + c2 * disjoint) / (double)n
             + c3 * (matched > 0 ? wDiff / matched : 0.0);
    }

    private static Dictionary<int, ConnGene> ByInnovation(List<ConnGene> conns)
    {
        var d = new Dictionary<int, ConnGene>(conns.Count);
        foreach (var c in conns) d[c.Innovation] = c;
        return d;
    }

    /// <summary>
    /// Conversión EXACTA v1→v2: el MLP denso de <paramref name="sizes"/> se
    /// vuelve un grafo NEAT cuya evaluación reproduce bit a bit las
    /// activaciones del <see cref="MlpBrain"/> original (misma ordenación de
    /// la suma: bias primero, fuentes por id ascendente — ver NeatBrain).
    /// Innovations canónicas 1..K por orden (from, to).
    /// </summary>
    public static NeatGenome FromMlp(int[] sizes, float[] weights, double fitness = 0.0)
    {
        if (sizes is null) throw new ArgumentNullException(nameof(sizes));
        if (weights is null) throw new ArgumentNullException(nameof(weights));
        if (sizes.Length < 2 || sizes[0] != AntSensorChannelInfo.Count)
            throw new ArgumentException("La capa de entrada debe tener los 19 canales del contrato v1.", nameof(sizes));
        if (weights.Length != MlpBrain.ExpectedWeightCount(sizes))
            throw new ArgumentException("Los pesos no corresponden a la topología.", nameof(weights));
        if (sizes[^1] != AntDecision.DecisionCount)
            throw new ArgumentException("La capa de salida debe tener las 6 decisiones del contrato v1.", nameof(sizes));

        var nodes = new List<NodeGene>();
        for (int i = 0; i < AntSensorChannelInfo.Count; i++)
            nodes.Add(new NodeGene(i, NodeKind.Input, 0f, (byte)ActivationId.Tanh));
        int hiddenId = NodeGene.FirstHiddenId;
        var hiddenOf = new Dictionary<(int layer, int idx), int>();
        for (int l = 1; l < sizes.Length - 1; l++)
        {
            for (int i = 0; i < sizes[l]; i++)
            {
                hiddenOf[(l, i)] = hiddenId;
                nodes.Add(new NodeGene(hiddenId, NodeKind.Hidden, 0f, (byte)ActivationId.Tanh));
                hiddenId++;
            }
        }
        // Convención del contrato v1 (MlpBrain por defecto): canal 0 (steer)
        // en [−1,1] ⇒ Tanh; el resto en [0,1] ⇒ Sigmoid. Las ocultas: Tanh.
        for (int i = 0; i < AntDecision.DecisionCount; i++)
        {
            byte act = i == 0 ? (byte)ActivationId.Tanh : (byte)ActivationId.Sigmoid;
            nodes.Add(new NodeGene(NodeGene.FirstOutputId + i, NodeKind.Output, 0f, act));
        }

        // Bloques destination-major (formato .antgenome): por capa destino,
        // para cada destino d: pesos de cada fuente seguidos del bias.
        // El bias del MLP va al gen de nodo destino; el resto a conexiones.
        var conns = new List<ConnGene>();
        int off = 0;
        for (int l = 1; l < sizes.Length; l++)
        {
            int prev = sizes[l - 1], cur = sizes[l];
            for (int d = 0; d < cur; d++)
            {
                int block = off + d * (prev + 1);
                int target = l == sizes.Length - 1
                    ? NodeGene.FirstOutputId + d
                    : hiddenOf[(l, d)];
                for (int s = 0; s < prev; s++)
                {
                    int source = l == 1 ? s : hiddenOf[(l - 1, s)];
                    conns.Add(new ConnGene(0, source, target, weights[block + s], Enabled: true));
                }
            }
            off += cur * (prev + 1);
        }

        // Bias por nodo destino (en el mismo recorrido destination-major).
        off = 0;
        for (int l = 1; l < sizes.Length; l++)
        {
            int prev = sizes[l - 1], cur = sizes[l];
            for (int d = 0; d < cur; d++)
            {
                int block = off + d * (prev + 1);
                float bias = weights[block + prev];
                int target = l == sizes.Length - 1
                    ? NodeGene.FirstOutputId + d
                    : hiddenOf[(l, d)];
                var idx = nodes.FindIndex(n => n.Id == target);
                nodes[idx] = nodes[idx] with { Bias = bias };
            }
            off += cur * (prev + 1);
        }

        // Innovations canónicas: conexiones ordenadas por (from, to) → 1..K.
        var ordered = new List<ConnGene>(conns);
        ordered.Sort((a, b) => a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));
        var innovOf = new Dictionary<(int from, int to), int>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
            innovOf[(ordered[i].From, ordered[i].To)] = i + 1;

        for (int i = 0; i < conns.Count; i++)
            conns[i] = conns[i] with { Innovation = innovOf[(conns[i].From, conns[i].To)] };

        return new NeatGenome(nodes, conns, fitness);
    }

    private void Validate()
    {
        var seen = new HashSet<int>();
        int inputs = 0, outputs = 0;
        foreach (var n in _nodes)
        {
            if (!seen.Add(n.Id)) throw new ArgumentException($"Id de nodo duplicado: {n.Id}.");
            if ((ActivationId)n.Act is not (ActivationId.Tanh or ActivationId.Sigmoid or ActivationId.Linear))
                throw new ArgumentException($"Activación desconocida en el nodo {n.Id}: {n.Act}.");
            switch (n.Kind)
            {
                case NodeKind.Input:
                    if (n.Id < 0 || n.Id >= AntSensorChannelInfo.Count)
                        throw new ArgumentException($"Nodo Input fuera del rango 0..18: {n.Id}.");
                    inputs++;
                    break;
                case NodeKind.Output:
                    if (n.Id < NodeGene.FirstOutputId || n.Id >= NodeGene.FirstOutputId + AntDecision.DecisionCount)
                        throw new ArgumentException($"Nodo Output fuera del rango 19..24: {n.Id}.");
                    outputs++;
                    break;
                case NodeKind.Hidden:
                    if (n.Id < NodeGene.FirstHiddenId)
                        throw new ArgumentException($"Nodo Hidden con id reservado: {n.Id}.");
                    break;
            }
        }
        if (inputs != AntSensorChannelInfo.Count)
            throw new ArgumentException($"Se esperaban {AntSensorChannelInfo.Count} entradas; hay {inputs}.");
        if (outputs != AntDecision.DecisionCount)
            throw new ArgumentException($"Se esperaban {AntDecision.DecisionCount} salidas; hay {outputs}.");
        if (_nodes.Count > MaxNodes) throw new ArgumentException($"Demasiados nodos: {_nodes.Count} > {MaxNodes}.");
        if (_conns.Count > MaxConns) throw new ArgumentException($"Demasiadas conexiones: {_conns.Count} > {MaxConns}.");

        var innovations = new HashSet<int>();
        var activePairs = new HashSet<(int from, int to)>();
        foreach (var c in _conns)
        {
            if (!innovations.Add(c.Innovation))
                throw new ArgumentException($"Innovación duplicada: {c.Innovation}.");
            if (!seen.Contains(c.From) || !seen.Contains(c.To))
                throw new ArgumentException($"Conexión {c.Innovation} referencia nodos inexistentes ({c.From},{c.To}).");
            if (c.Enabled && !activePairs.Add((c.From, c.To)))
                throw new ArgumentException($"Conexión activa duplicada ({c.From},{c.To}).");
        }

        // Aciclicidad (Kahn sobre las conexiones activas).
        var adj = new Dictionary<int, List<int>>();
        var indeg = new Dictionary<int, int>();
        foreach (var n in _nodes) { adj[n.Id] = new List<int>(); indeg[n.Id] = 0; }
        foreach (var c in _conns)
        {
            if (!c.Enabled) continue;
            adj[c.From].Add(c.To);
            indeg[c.To]++;
        }
        var queue = new Queue<int>();
        foreach (var (id, deg) in indeg) if (deg == 0) queue.Enqueue(id);
        int visited = 0;
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            visited++;
            foreach (int to in adj[id]) if (--indeg[to] == 0) queue.Enqueue(to);
        }
        if (visited != _nodes.Count)
            throw new ArgumentException("El grafo tiene un ciclo (v2.0 es feed-forward).");
    }
}
