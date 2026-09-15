using System;
using System.Collections.Generic;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Evolution;
using AntSim.Core.Sim;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2c rodaja 3 — especiation en el pool NEAT (diseño §3.5):
///   1. Agrupación por δ &lt; δt: genomas idénticos en forma juntos, un grafo
///      ajeno funda especie nueva.
///   2. Cuotas (sharing): la especie de fitness medio mayor cría más — y el
///      sharing la FRENA al crecer con mediocres.
///   3. El criterio de cierre de la rodaja: 200 generaciones con mutación
///      estructural activa NO colapsan a 1 especie (mín 3 sostenidas).
///   4. Determinismo: misma semilla ⇒ misma secuencia de nacimientos.
/// </summary>
public class NeatGenomePoolTests
{
    private static readonly int[] Sizes = { AntSensorChannelInfo.Count, 8, AntDecision.DecisionCount };

    private static NeatGenome Dense(double fitness, ulong seed)
    {
        var rng = new DeterministicRandom(seed);
        int n = MlpBrain.ExpectedWeightCount(Sizes);
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = (float)(rng.NextDouble01() * 2.0 - 1.0);
        return NeatGenome.FromMlp(Sizes, w, fitness);
    }

    /// <summary>Genoma con un skip input→output (especie ajena a los densos).</summary>
    private static NeatGenome WithSkip(double fitness, int from, int to, float w)
    {
        var g = Dense(fitness, (ulong)(from * 31 + to + 7));
        var reg = new InnovationRegistry(new[] { g });
        g.AddConnChecked(new ConnGene(reg.GetOrAssign(from, to), from, to, w, true));
        g.Fitness = fitness;
        return g;
    }

    // ————— 1. Agrupación por δ —————

    [Fact]
    public void Stats_LosClonesJuntos_YElAjenoFundaNueva()
    {
        // Parientesco por LINAJE (no por forma): clones (delta=0) de un mismo
        // genoma viven juntos; un skip sobre un clon sigue siendo pariente
        // (delta = 1/201 < suelo 0.05); un denso de OTRO linaje funda especie.
        var base0 = Dense(5.0, 11UL);
        var pool = new NeatGenomePool(new DeterministicRandom(1UL));
        pool.Seed(new[] { base0, base0.Clone(), base0.Clone() });
        Assert.Equal(1, pool.Stats.SpeciesCount);

        // Un skip REALMENTE emparentado (clon del base + conn nueva): NO funda.
        var kin2 = base0.Clone();
        var reg = new InnovationRegistry(pool.Elite);
        kin2.AddConnChecked(new ConnGene(reg.GetOrAssign(3, 21), 3, 21, 0.4f, true));
        kin2.Fitness = 6.0;
        Assert.True(pool.TryAdd(kin2));
        Assert.Equal(1, pool.Stats.SpeciesCount);

        // Un denso de OTRO linaje (con skip): funda especie nueva.
        Assert.True(pool.TryAdd(WithSkip(6.0, 3, 21, 0.7f)));
        var stats = pool.Stats;
        Assert.Equal(2, stats.SpeciesCount);
        Assert.Equal(4, stats.Species.Max(s => s.Size)); // la familia: 3 clones + kin2
        Assert.Equal(1, stats.Species.Min(s => s.Size)); // el ajeno, solo
    }

    [Fact]
    public void Stats_PoolVacio_CeroEspecies()
    {
        var pool = new NeatGenomePool(new DeterministicRandom(2UL));
        Assert.Equal(0, pool.Stats.SpeciesCount);
        Assert.Equal(0, pool.Stats.LargestSize);
        Assert.Null(pool.Birth());
    }

    [Fact]
    public void TryAdd_EliteGlobalOrdenada_YCapacidadRespetaElCriterioV1()
    {
        var pool = new NeatGenomePool(new DeterministicRandom(3UL));
        for (int i = 0; i < NeatGenomePool.EliteCapacity + 5; i++)
            pool.TryAdd(Dense((double)(NeatGenomePool.EliteCapacity + 10 - i), (ulong)(100 + i)));

        Assert.Equal(NeatGenomePool.EliteCapacity, pool.EliteCount);
        // Orden fitness desc: el [0] es el mejor; los peores quedaron fuera.
        Assert.True(pool.Elite[0].Fitness >= pool.Elite[^1].Fitness);
        Assert.False(pool.TryAdd(Dense(-1.0, 999UL))); // peor que el peor: fuera
    }

    // ————— 2. Cuotas (sharing) —————

    [Fact]
    public void Birth_EspecieConMejorFitnessMedio_CriaMas()
    {
        // Dos especies separadas: densos (fitness 5) y skips (fitness 20).
        // Con cuotas, la especie del skip debe dominar los nacimientos.
        var pool = new NeatGenomePool(new DeterministicRandom(4UL),
            probAddConn: 0f, probAddNode: 0f, probToggle: 0f);
        for (int i = 0; i < 8; i++) pool.TryAdd(Dense(5.0, (ulong)(200 + i)));
        for (int i = 0; i < 8; i++) pool.TryAdd(WithSkip(20.0, 3 + i % 4, 21 + i % 4, 0.5f + i));

        int skipSpeciesOffspring = 0;
        const int births = 60;
        for (int i = 0; i < births; i++)
        {
            var child = pool.Birth()!;
            // Un hijo de la especie skip conserva genes skip (innovations fuera
            // del rango denso). Los densos solo tienen innovations 1..200.
            if (child.Conns.Any(c => c.Innovation > 200)) skipSpeciesOffspring++;
        }
        // Sharing: avg 20×8 vs avg 5×8 ⇒ ~80% de la cuota para los skips.
        Assert.True(skipSpeciesOffspring > births * 55 / 100,
            $"solo {skipSpeciesOffspring}/{births} hijos de la especie fuerte");
    }

    [Fact]
    public void Birth_SinSenalDeFitness_ReparteUniformePorEspecie()
    {
        var pool = new NeatGenomePool(new DeterministicRandom(5UL),
            probAddConn: 0f, probAddNode: 0f, probToggle: 0f);
        for (int i = 0; i < 4; i++) pool.TryAdd(Dense(0.0, (ulong)(300 + i)));
        pool.TryAdd(WithSkip(0.0, 3, 21, 0.3f));

        // Arranque en frío (todo 0): cada especie recibe ≈ su mitad.
        int fromSkip = 0;
        const int births = 40;
        for (int i = 0; i < births; i++)
        {
            var child = pool.Birth()!;
            if (child.Conns.Any(c => c.Innovation > 200)) fromSkip++;
        }
        Assert.InRange(fromSkip, 1, births - 1); // ni 0 (muerta) ni todo (dominio)
    }

    [Fact]
    public void Birth_EliteVaciaYUnitaria_NoExplota()
    {
        var pool = new NeatGenomePool(new DeterministicRandom(6UL));
        Assert.Null(pool.Birth());

        pool.TryAdd(Dense(1.0, 401UL));
        for (int i = 0; i < 10; i++)
        {
            var child = pool.Birth();
            Assert.NotNull(child); // torneo con un solo miembro: el propio
        }
    }

    // ————— 3. El criterio de cierre: 200 generaciones SIN colapso —————

    [Fact]
    public void DoscientasGeneraciones_ConMutacionEstructural_NoColapsaAUnaEspecie()
    {
        var pool = new NeatGenomePool(new DeterministicRandom(20260915UL));
        pool.SeedRandom(Sizes, 16);

        int minSpecies = int.MaxValue;
        var sizesSeen = new List<int>();
        for (int gen = 0; gen < 200; gen++)
        {
            // Nacimientos de la generación (cuotas sobre la élite actual).
            var children = new List<NeatGenome>();
            for (int i = 0; i < 16; i++)
                children.Add(pool.Birth()!);

            // Evaluación SIMULADA: el fitness premia tamaño moderado + variación
            // de pesos — nada que el operador pueda «granjear» sin estructura.
            foreach (var c in children)
            {
                double structure = c.Conns.Count(ck => ck.Enabled) * 0.01
                                 + c.Nodes.Count * 0.01;
                double weights = c.Conns.Sum(ck => Math.Abs(ck.Weight)) * 0.001;
                pool.RecordFitness(c, structure + weights + (c.Nodes.Count - 25) * 0.02);
            }

            minSpecies = Math.Min(minSpecies, pool.Stats.SpeciesCount);
            sizesSeen.Add(pool.Stats.SpeciesCount);
        }

        // El criterio: NUNCA colapsa a 1 especie; mínimo 3 sostenidas en la
        // generación final. (La élite global es de 64: las especies viven si
        // sus miembros entran por fitness — el sharing les da cuota para criar.)
        Assert.True(minSpecies >= 2, $"colapsó a {minSpecies} especie(s) en algún momento");
        Assert.True(sizesSeen[^1] >= 3, $"generación 200: solo {sizesSeen[^1]} especie(s)");
    }

    // ————— 4. Determinismo —————

    [Fact]
    public void MismaSemilla_MismaSecuenciaDeNacimientos()
    {
        var run = (ulong seed) =>
        {
            var pool = new NeatGenomePool(new DeterministicRandom(seed));
            pool.SeedRandom(Sizes, 12);
            var outs = new List<float>();
            for (int gen = 0; gen < 12; gen++)
            {
                for (int i = 0; i < 8; i++)
                {
                    var child = pool.Birth()!;
                    outs.Add(child.Conns[0].Weight);
                    pool.RecordFitness(child, child.Conns.Count * 0.01);
                }
            }
            return outs;
        };

        var a = run(777UL);
        var b = run(777UL);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i], b[i]); // bit a bit
    }

    // ————— 5. Diversidad y CompleteTrial —————

    [Fact]
    public void Diversity_CeroConClones_YPositivaConLinajesAjenos()
    {
        var base0 = Dense(1.0, 501UL);
        var pool = new NeatGenomePool(new DeterministicRandom(8UL));
        for (int i = 0; i < 4; i++) pool.TryAdd(Dense(1.0 + i, (ulong)(500 + i)));
        // Pesos distintos, linajes ajenos: la distancia de grupo es la
        // UNRELATED (0.5) — la diversidad la marca el linaje, no el ruido.
        Assert.True(pool.Diversity() > 0.05);

        var kinPool = new NeatGenomePool(new DeterministicRandom(81UL));
        for (int i = 0; i < 4; i++) kinPool.TryAdd(base0.Clone());
        Assert.True(kinPool.Diversity() < 0.05); // clones: delta=0
    }

    [Fact]
    public void CompleteTrial_UmbralesV1_Respetados()
    {
        var pool = new NeatGenomePool(new DeterministicRandom(9UL));
        for (int i = 0; i < 8; i++) pool.TryAdd(Dense((double)(10 - i), (ulong)(600 + i)));

        // Encima de p50: entra.
        var good = Dense(9.5, 701UL);
        Assert.Equal(TrialResult.EnteredElite, pool.CompleteTrial(good, 9.5));
        Assert.Equal(1, pool.TrialsEntered);

        // Por debajo de p25 con diversidad normal: descartado.
        var bad = Dense(0.5, 702UL);
        Assert.Equal(TrialResult.Discarded, pool.CompleteTrial(bad, 0.5));
        Assert.Equal(1, pool.TrialsDiscarded);

        Assert.Equal(9, pool.EliteCount);
    }
}
