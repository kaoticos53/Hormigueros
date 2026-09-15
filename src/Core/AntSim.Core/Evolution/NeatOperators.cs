using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Sim;

namespace AntSim.Core.Evolution;

/// <summary>
/// Registro de innovaciones del pool EN VIVO (F5.2c rodaja 2, diseño §3.3):
/// mapa (from,to) → innovation compartido por la población. Dos padres del
/// MISMO pool que mutan la misma conexión nueva reciben la MISMA innovation —
/// la clave del crossover align-by-innovation. Se reconstruye al inicio de
/// cada generación desde los genes existentes (cero estado entre generaciones:
/// misma semilla ⇒ misma secuencia completa, determinismo del pool intacto).
/// </summary>
public sealed class InnovationRegistry
{
    private readonly Dictionary<(int from, int to), int> _byPair = new();
    private int _next;

    public InnovationRegistry(IEnumerable<NeatGenome> population)
    {
        if (population is null) throw new ArgumentNullException(nameof(population));
        foreach (var g in population)
            foreach (var c in g.Conns)
                Track(c.From, c.To, c.Innovation);
    }

    private void Track(int from, int to, int innovation)
    {
        var key = (from, to);
        if (_byPair.TryGetValue(key, out int existing))
        {
            // Incoherencia solo posible con un pool mezclado a mano: gana la
            // menor (canónica dentro del pool).
            if (innovation < existing) _byPair[key] = innovation;
        }
        else
        {
            _byPair[key] = innovation;
        }
        if (innovation >= _next) _next = innovation + 1;
    }

    /// <summary>
    /// Innovation de la conexión (from,to): la existente si el par ya vive en
    /// el pool; una NUEVA (compartida por toda la población de esta generación)
    /// si no. Determinista: la asignación no consume RNG.
    /// </summary>
    public int GetOrAssign(int from, int to)
    {
        var key = (from, to);
        if (_byPair.TryGetValue(key, out int innov)) return innov;
        innov = _next++;
        _byPair[key] = innov;
        return innov;
    }

    /// <summary>¿La conexión (from,to) existe ya en el pool?</summary>
    public bool Contains(int from, int to) => _byPair.ContainsKey((from, to));

    /// <summary>Nº de pares registrados (diagnóstico).</summary>
    public int Count => _byPair.Count;
}

/// <summary>
/// Operadores genéticos NEAT (F5.2c rodaja 2, diseño §3.4) — todos
/// `ref DeterministicRandom` (la disciplina Fase 3: el struct por valor
/// mutaría una copia y la secuencia jamás avanzaría), sin RNG nuevo, puros
/// sobre los genes (nunca tocan el mundo).
///
/// Pesos y biases usan CanonMath-free Box-Muller idéntico al de
/// <see cref="MlpGenome.Mutate"/> (misma σ por defecto) para que los dos
/// tipos de genoma compartan la misma disciplina estocástica.
/// </summary>
public static class NeatOperators
{
    /// <summary>Probabilidades estructurales por genoma/generación (diseño §3.4).</summary>
    public const float DefaultProbAddConn = 0.03f;
    public const float DefaultProbAddNode = 0.02f;
    public const float DefaultProbToggle = 0.01f;
    public const float DefaultSigma = MlpGenome.DefaultSigma;

    private const int AddConnMaxAttempts = 20; // diseño: no-op tras 20 intentos

    // ————— Mutación de pesos (gaussiana sobre conexiones activas) —————

    /// <summary>Gaussiana aditiva sobre el peso de cada conexión ACTIVA.</summary>
    public static void MutateWeights(NeatGenome genome, ref DeterministicRandom rng, float sigma = DefaultSigma)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));
        var conns = genome.Conns;
        for (int i = 0; i < conns.Count; i++)
        {
            if (!conns[i].Enabled) continue;
            double u1 = Math.Max(rng.NextDouble01(), 1e-12);
            double u2 = rng.NextDouble01();
            double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            genome.SetWeight(conns[i].Innovation, conns[i].Weight + (float)gauss * sigma);
        }
    }

    /// <summary>Gaussiana aditiva sobre el bias de cada nodo OCULTO.</summary>
    public static void MutateBiases(NeatGenome genome, ref DeterministicRandom rng, float sigma = DefaultSigma)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));
        var nodes = genome.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Kind != NodeKind.Hidden) continue;
            double u1 = Math.Max(rng.NextDouble01(), 1e-12);
            double u2 = rng.NextDouble01();
            double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            genome.SetBias(nodes[i].Id, nodes[i].Bias + (float)gauss * sigma);
        }
    }

    // ————— Mutaciones estructurales —————

    /// <summary>
    /// AddNode (diseño §3.4): partir UNA conexión activa elegida al azar. La
    /// vieja se desactiva (su innovation se conserva — histórico para el
    /// alineamiento), la nueva entra con peso 1 y la de salida con el peso
    /// viejo. El nodo oculto nuevo toma el id libre más bajo ≥ 25.
    /// No-op si el genoma está en el tope de nodos o sin conexiones activas.
    /// </summary>
    public static bool MutateAddNode(NeatGenome genome, InnovationRegistry registry, ref DeterministicRandom rng)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var active = genome.Conns.Where(c => c.Enabled).ToList();
        if (active.Count == 0 || genome.Nodes.Count >= NeatGenome.MaxNodes) return false;

        var victim = active[rng.NextInt(0, active.Count)];
        int newNodeId = genome.NextHiddenId();
        genome.AddHiddenNode(); // el nodo ANTES de las conexiones que lo referencian

        // La vieja sale del circuito (innovation conservado).
        genome.SetEnabled(victim.Innovation, enabled: false);

        // Entrada: old.from → new, peso 1 (NEAT clásico: amortigua la perturbación).
        int inInnov = registry.GetOrAssign(victim.From, newNodeId);
        genome.AddConnChecked(new ConnGene(inInnov, victim.From, newNodeId, 1f, true));

        // Salida: new → old.to, peso viejo (la función se conserva EXACTAMENTE
        // en el instante de la mutación — el circuito existente no se rompe).
        int outInnov = registry.GetOrAssign(newNodeId, victim.To);
        genome.AddConnChecked(new ConnGene(outInnov, newNodeId, victim.To, victim.Weight, true));
        return true;
    }

    /// <summary>
    /// AddConn (diseño §3.4): par (from→to) al azar SIN ciclo (feed-forward:
    /// to debe venir DESPUÉS que from en el orden topológico) y sin duplicado
    /// activo. Tras 20 intentos sin candidato: no-op.
    /// </summary>
    public static bool MutateAddConn(NeatGenome genome, InnovationRegistry registry, ref DeterministicRandom rng)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        var order = genome.TopologicalOrder(); // índice = profundidad feed-forward
        var depthOf = new Dictionary<int, int>(order.Length);
        for (int i = 0; i < order.Length; i++) depthOf[order[i]] = i;

        var nodeIds = genome.Nodes.Select(n => n.Id).ToList();
        var activePairs = new HashSet<(int, int)>(genome.Conns.Where(c => c.Enabled).Select(c => (c.From, c.To)));

        for (int attempt = 0; attempt < AddConnMaxAttempts; attempt++)
        {
            int from = nodeIds[rng.NextInt(0, nodeIds.Count)];
            int to = nodeIds[rng.NextInt(0, nodeIds.Count)];
            if (depthOf[from] >= depthOf[to]) continue; // ciclo o self-loop
            if (!activePairs.Add((from, to))) continue; // duplicado activo

            int innov = registry.GetOrAssign(from, to);
            // Peso inicial pequeño (NEAT clásico: no perturba de golpe).
            genome.AddConnChecked(new ConnGene(innov, from, to, (float)(rng.NextDouble01() * 2.0 - 1.0) * 0.5f, true));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggle (diseño §3.4): invertir Enabled de una conexión al azar. Un gen
    /// desactivado NUNCA se borra (histórico NEAT) — el toggle puede
    /// reactivarlo. Devuelve la innovation tocada, o null si no hay conexiones.
    /// </summary>
    public static int? MutateToggle(NeatGenome genome, ref DeterministicRandom rng)
    {
        if (genome is null) throw new ArgumentNullException(nameof(genome));
        var conns = genome.Conns;
        if (conns.Count == 0) return null;
        var victim = conns[rng.NextInt(0, conns.Count)];
        genome.SetEnabled(victim.Innovation, !victim.Enabled);
        return victim.Innovation;
    }

    // ————— Crossover align-by-innovation (diseño §3.4/§3.3) —————

    /// <summary>
    /// Hijo de dos padres: los genes MATCHING (misma innovation) vienen de A
    /// o B al azar (1 bool/gen — diseño); los DISJOINT/EXCESS del MEJOR padre
    /// (empate de fitness → padre A). Nodos: unión de los ids referenciados
    /// por las conexiones heredadas (bias/act del padre que aportó el gen;
    /// para nodos compartidos, del mejor). La estructura resultante puede ser
    /// feed-forward inválida SOLO si los padres lo eran — ambos garantizan
    /// aciclicidad y el hijo hereda subconjuntos de dos DAGs con la MISMA
    /// ordenación por ids fijos (invariantes 1–5 del genoma re-validan).
    /// </summary>
    public static NeatGenome Crossover(NeatGenome a, NeatGenome b, ref DeterministicRandom rng)
    {
        if (a is null || b is null) throw new ArgumentNullException(a is null ? nameof(a) : nameof(b));

        bool aFitter = a.Fitness > b.Fitness;
        var best = aFitter ? a : (a.Fitness < b.Fitness ? b : a); // empate → A
        var bestByInnov = best.Conns.ToDictionary(c => c.Innovation);

        var conns = new List<ConnGene>();
        var nodes = new Dictionary<int, NodeGene>();

        // Matching: A y B al azar; el bias/act del nodo destino del elegido.
        var bByInnov = b.Conns.ToDictionary(c => c.Innovation);
        foreach (var ca in a.Conns)
        {
            if (!bByInnov.TryGetValue(ca.Innovation, out var cb)) continue;
            var chosen = rng.NextBool() ? ca : cb;
            conns.Add(chosen);
            nodes[chosen.To] = NodeFrom(chosen.To, chosen == cb ? b : a, chosen == cb ? b : a);
        }

        // Disjoint/excess SOLO del mejor padre.
        foreach (var c in best.Conns)
        {
            if (conns.Any(x => x.Innovation == c.Innovation)) continue;
            if (!bestByInnov.ContainsKey(c.Innovation)) continue;
            conns.Add(c);
            nodes[c.To] = NodeFrom(c.To, best, best);
        }

        // Nodos fijos del contrato (0..24): SIEMPRE presentes, del mejor padre
        // (bias de ocultos y act salidas; entradas son idénticas por contrato).
        foreach (var n in best.Nodes)
            if (n.Id < NodeGene.FirstHiddenId && !nodes.ContainsKey(n.Id))
                nodes[n.Id] = n;

        // Nodos ocultos del mejor padre sin conexión heredada (huérfanos por
        // toggles): se conservan — el histórico permite reactivar sus conns.
        // Y los del OTRO padre también: un matching gene puede elegir la
        // conexión de B hacia un nodo que solo B tiene (validación exige que
        // todo extremo referenciado exista).
        foreach (var n in a.Nodes)
            if (!nodes.ContainsKey(n.Id))
                nodes[n.Id] = n;
        foreach (var n in b.Nodes)
            if (!nodes.ContainsKey(n.Id))
                nodes[n.Id] = n; // no estaba en A: la copia de B es LA copia

        conns.Sort((x, y) => x.Innovation.CompareTo(y.Innovation));
        return new NeatGenome(nodes.Values.OrderBy(n => n.Id), conns, FitnessOf(a, b));
    }

    private static NodeGene NodeFrom(int id, NeatGenome primary, NeatGenome secondary)
    {
        var n = primary.Nodes.FirstOrDefault(x => x.Id == id);
        if (n != null) return n;
        n = secondary.Nodes.FirstOrDefault(x => x.Id == id);
        if (n != null) return n;
        throw new InvalidOperationException($"Nodo {id} referenciado pero ausente en ambos padres.");
    }

    private static double FitnessOf(NeatGenome a, NeatGenome b) => Math.Max(a.Fitness, b.Fitness);
}
