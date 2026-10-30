using System;
using AntSim.Core.Contracts;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F8 — RECLUTAMIENTO POR RASTRO en la partida. El mundo encendido con
    /// <c>recruitment: true</c> cambia en dos cosas y estas pruebas fijan las dos:
    /// la comida se siembra en CÚMULOS (algo que anunciar) y las hormigas tienen
    /// los reflejos que convierten un hallazgo en camino — la que BUSCA deja
    /// rastro de CASA (el camino de vuelta al nido), la CARGADA deja rastro de
    /// COMIDA (el camino al montón) y la que busca gira hacia el lado que huele
    /// más.
    ///
    /// El álgebra del reflejo se fija aquí, con la capa pintada a mano, por el
    /// mismo motivo que la tropotaxis de huella: depender de que una partida
    /// entera produzca la geometría deseada hace la prueba lenta y frágil. La
    /// integración (¿se ve el rastro en el tablero?) la cubre el stream real.
    /// </summary>
    public sealed class RecruitmentTests
    {
        // 96² es el mundo de la partida: con menos, NestMinSpawnDistance (200 u)
        // no deja ningún punto válido y el mundo nace sin comida.
        private const int Grid = 96;

        private static int Cell(float u) => (int)(u / SimConstants.CellSizeUnits);

        [Fact]
        public void MundoConReclutamiento_SiembraCumulos()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, recruitment: true);

            Assert.True(sim.Recruitment);
            Assert.True(sim.Items.Count > 0, "el mundo se quedó sin comida");

            // El ítem con más compañeros dentro del radio del montón. Los offsets
            // se sortean en [-R, +R] por eje, así que dos ítems del mismo montón
            // distan como mucho R·√2; eso es el radio de la cuenta.
            float radius2 = sim.FoodClusterRadius * sim.FoodClusterRadius * 2f;
            int densest = 0;
            foreach (var a in sim.Items)
            {
                int near = 0;
                foreach (var b in sim.Items)
                {
                    float dx = a.X - b.X, dy = a.Y - b.Y;
                    if (dx * dx + dy * dy <= radius2) near++;
                }
                densest = Math.Max(densest, near);
            }

            Assert.True(densest >= sim.FoodClusterMin,
                $"el montón más denso tiene {densest} ítems y FoodClusterMin es {sim.FoodClusterMin}: " +
                "la comida no se sembró en cúmulos");
        }

        /// <summary>
        /// F8 — dónde está el montón importa tanto como que exista. Medido: una
        /// colonia patrulla ~100 u alrededor del nido (solo un 11 % de las
        /// hormigas pasa de 200 u), así que un montón sembrado al azar en todo el
        /// mapa casi nunca lo visita nadie y el rastro no tiene a quién guiar.
        /// Por eso se siembra en la banda de forrajeo de UNA colonia y con bocados
        /// ricos (10 ep, el tope del contrato, frente a 4–6 de una migaja suelta).
        /// </summary>
        [Fact]
        public void Cumulos_SonRicosYCainEnElRadioQueLaColoniaPatrulla()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, recruitment: true);

            // El bocado rico es la firma del montón: la siembra clásica nunca pasa
            // de 6 ep, así que cualquier ítem de 10 ep salió de un cúmulo.
            var ricos = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Where(sim.Items,
                    i => MathF.Abs(i.Amount - sim.FoodClusterItemAmount) < 1e-3f));
            Assert.True(ricos.Count >= sim.FoodClusterCount * sim.FoodClusterMin,
                $"{ricos.Count} bocados ricos para {sim.FoodClusterCount} montones de {sim.FoodClusterMin}–{sim.FoodClusterMax}: " +
                "los cúmulos no se sembraron");

            foreach (var item in ricos)
            {
                float best = float.MaxValue;
                foreach (var c in sim.Colonies)
                {
                    float dx = item.X - c.NestX, dy = item.Y - c.NestY;
                    best = MathF.Min(best, MathF.Sqrt(dx * dx + dy * dy));
                }
                // El centro cae en la banda y cada ítem se separa hasta R por eje
                // (R·√2 en diagonal): ese es el margen que se admite.
                Assert.InRange(best,
                    sim.FoodClusterMinDistance - 2f * sim.FoodClusterRadius,
                    sim.FoodClusterMaxDistance + 2f * sim.FoodClusterRadius);
            }

            var clasico = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2);
            Assert.DoesNotContain(clasico.Items, i => i.Amount > 6f + 1e-3f);
        }

        /// <summary>
        /// SOSTENIBILIDAD DE LA RONDA — la oferta de montones se reparte POR
        /// COLONIA y en rueda. Con una sola cuota global, dos colonias se
        /// quedaban con la mitad de montones cada una y la partida de pareja moría
        /// de hambre a los ~46 000 ticks con más de 1 000 ep en el tablero; los
        /// mismos montones sostienen a UNA colonia hasta el horizonte.
        /// </summary>
        [Fact]
        public void Cumulos_SeRepartenPorColonia_NoComoCuotaUnica()
        {
            // Tamaño fijo de montón para que la cuenta sea exacta (el sorteo de
            // 6–10 hace que «bocados / máximo» no sea un conteo).
            var tuning = new RecruitmentTuning { Clusters = 5, ClusterMin = 4, ClusterMax = 4 };
            var una = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 1,
                recruitment: true, recruitmentTuning: tuning);
            var dos = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2,
                recruitment: true, recruitmentTuning: tuning);

            // Una cuota POR COLONIA: el doble de nidos, el doble de bocados ricos.
            Assert.Equal(5 * 4, Ricos(una));
            Assert.Equal(2 * 5 * 4, Ricos(dos));

            // Y la rueda es equilibrada: cada nido tiene SU cuota de montones al
            // alcance, no una colonia con todos y la otra sin ninguno.
            var centros = CentrosDeCumulo(dos);
            Assert.Equal(2 * 5, centros.Count);
            foreach (var colony in dos.Colonies)
            {
                int suyos = 0;
                foreach (var c in centros)
                {
                    float dx = c.x - colony.NestX, dy = c.y - colony.NestY;
                    if (MathF.Sqrt(dx * dx + dy * dy) <= dos.FoodClusterMaxDistance + 2f * dos.FoodClusterRadius)
                        suyos++;
                }
                Assert.True(suyos >= 5,
                    $"la colonia {colony.Id} tiene {suyos} montones al alcance y le tocan 5");
            }
        }

        /// <summary>
        /// SOSTENIBILIDAD DE LA RONDA — la banda de los montones cabe en el radio
        /// que la colonia patrulla de verdad (mediana 93 u; solo un 11 % de las
        /// obreras pasa de 200 u). Con la banda vieja (100–220 u) los montones
        /// caían fuera del alcance real: 33 720 ticks de ronda de 216 000. Este pin
        /// fija los números para que nadie los devuelva al borde sin medirlo.
        /// </summary>
        [Fact]
        public void Cumulos_CaenDentroDelRadioQueLaColoniaPatrulla()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 2, recruitment: true);

            Assert.True(sim.FoodClusterMaxDistance <= 140f,
                $"la banda de montones llega a {sim.FoodClusterMaxDistance} u: fuera del radio de patrulla");
            Assert.True(sim.FoodClusterMinDistance > 2f * sim.FoodClusterRadius,
                $"la banda arranca a {sim.FoodClusterMinDistance} u: los montones caerían dentro del cúmulo de nido");
        }

        /// <summary>Bocados ricos (10 ep = <see cref="FoodItem.MaxValue"/>): la firma
        /// del montón, porque la siembra clásica nunca pasa de 6 ep.</summary>
        private static int Ricos(WorldSim sim)
        {
            int ricos = 0;
            foreach (var item in sim.Items)
                if (MathF.Abs(item.Amount - sim.FoodClusterItemAmount) < 1e-3f) ricos++;
            return ricos;
        }

        /// <summary>Centros de montón aproximados: los bocados ricos agrupados por
        /// cercanía (el primero que aparece de cada grupo hace de centro).</summary>
        private static System.Collections.Generic.List<(float x, float y)> CentrosDeCumulo(WorldSim sim)
        {
            var centros = new System.Collections.Generic.List<(float x, float y)>();
            float r2 = sim.FoodClusterRadius * sim.FoodClusterRadius * 2f;
            foreach (var item in sim.Items)
            {
                if (MathF.Abs(item.Amount - sim.FoodClusterItemAmount) >= 1e-3f) continue;
                bool visto = false;
                foreach (var c in centros)
                {
                    float dx = item.X - c.x, dy = item.Y - c.y;
                    if (dx * dx + dy * dy <= r2) { visto = true; break; }
                }
                if (!visto) centros.Add((item.X, item.Y));
            }
            return centros;
        }

        [Fact]
        public void AjustesDelLanzador_LleganAlMundoAntesDeSembrar()
        {
            // F8: lo que el lanzador expone (`--clusters`, `--cluster-size`,
            // `--trail-…`) tiene que llegar al mundo ANTES de que siembre los
            // montones — el constructor ya los crea, así que aplicarlo después
            // daría un mundo con la comida de la configuración anterior.
            var tuning = new RecruitmentTuning
            {
                Clusters = 3,
                ClusterMin = 4,
                ClusterMax = 4,
                ClusterRadius = 24f,
                TrailDeposit = 2f,
                TrailHalfLife = 90f,
                TrailFollow = 0f,
            };

            var sim = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 1,
                recruitment: true, recruitmentTuning: tuning);

            Assert.Equal(3, sim.FoodClusterCount);
            Assert.Equal(4, sim.FoodClusterMin);
            Assert.Equal(24f, sim.FoodClusterRadius);
            Assert.Equal(2f, sim.RecruitmentFoodDeposit);
            Assert.Equal(90f, sim.RecruitmentFoodHalfLife);
            Assert.Equal(0f, sim.FoodTrailAttract);

            // 3 montones de 4 bocados ricos: los ítems de la siembra aleatoria
            // valen 4–6 ep, así que los de 10 ep son exactamente los del montón.
            int ricos = 0;
            foreach (var item in sim.Items)
                if (MathF.Abs(item.Amount - sim.FoodClusterItemAmount) < 1e-3f) ricos++;
            Assert.Equal(12, ricos);

            // Control A/B: 0 montones es una configuración VÁLIDA (mismo mundo sin
            // el punto caliente) y no debe dejar ni un bocado rico.
            var sinMontones = new WorldSim(seed: 42UL, gridCells: Grid, colonyCount: 1,
                recruitment: true, recruitmentTuning: new RecruitmentTuning { Clusters = 0 });
            Assert.DoesNotContain(sinMontones.Items,
                i => MathF.Abs(i.Amount - sinMontones.FoodClusterItemAmount) < 1e-3f);
        }

        [Fact]
        public void AjustesDelReclutamiento_RangosRechazados()
        {
            Assert.True(RecruitmentTuning.Default.TryValidate(out _));

            var malos = new[]
            {
                new RecruitmentTuning { Clusters = -1 },
                new RecruitmentTuning { Clusters = RecruitmentTuning.MaxClusters + 1 },
                new RecruitmentTuning { ClusterMin = 0 },
                new RecruitmentTuning { ClusterMin = 8, ClusterMax = 4 },
                new RecruitmentTuning { ClusterRadius = 0f },
                new RecruitmentTuning { TrailDeposit = -0.1f },
                new RecruitmentTuning { TrailHalfLife = 0f },
                new RecruitmentTuning { TrailFollow = -1f },
            };
            foreach (var tuning in malos)
            {
                Assert.False(tuning.TryValidate(out string error), $"debía rechazar {tuning.Describe()}");
                Assert.Contains("fuera de rango", error);
            }
        }

        [Fact]
        public void Reflejo_GiraHaciaElLadoConMasRastroDeComida()
        {
            var sim = new WorldSim(seed: 7UL, gridCells: Grid, colonyCount: 1, recruitment: true);
            var colony = sim.Colonies[0];
            var sp = colony.Species;
            var ant = colony.Adults[0];
            ant.HasLoad = false;
            ant.Heading = 0f; // +X; la antena derecha cae hacia −Y (heading − SenseAngle)

            float reach = sp.SensorReach * ant.SensorScale;
            float rx = ant.X + MathF.Cos(-sp.SenseAngle) * reach;
            float ry = ant.Y + MathF.Sin(-sp.SenseAngle) * reach;
            colony.FoodLayer.Deposit(Cell(rx), Cell(ry), 0.9f);

            var decision = new AntDecision();
            sim.ApplyTrailRecruitment(colony, ant, sp, ref decision);

            // Signo del contrato de giro: negativo = a la derecha. El rastro está
            // a la derecha, así que el reflejo debe empujar hacia allí.
            Assert.True(decision.Steer < 0f,
                $"Steer={decision.Steer:0.000}: debía girar a la derecha, donde está el rastro");
            Assert.True(decision.Steer > -sp.OmegaMax * 2f,
                $"Steer={decision.Steer:0.000}: el reflejo desborda el giro físico");
        }

        [Fact]
        public void Reflejo_ConRastroSimetricoONada_NoGira()
        {
            var sim = new WorldSim(seed: 7UL, gridCells: Grid, colonyCount: 1, recruitment: true);
            var colony = sim.Colonies[0];
            var sp = colony.Species;
            var ant = colony.Adults[0];
            ant.HasLoad = false;
            ant.Heading = 0.7f;

            var limpio = new AntDecision();
            sim.ApplyTrailRecruitment(colony, ant, sp, ref limpio);
            Assert.Equal(0f, limpio.Steer); // sin rastro no hay empuje (ni NaN)

            // Capa constante: las dos antenas leen EXACTAMENTE lo mismo, así que el
            // desequilibrio es 0 y una hormiga en el filo de un rastro no gira.
            var layer = colony.FoodLayer;
            for (int y = 0; y < layer.Height; y++)
                for (int x = 0; x < layer.Width; x++)
                    layer.Deposit(x, y, 0.3f);

            var simetrico = new AntDecision();
            sim.ApplyTrailRecruitment(colony, ant, sp, ref simetrico);
            Assert.Equal(0f, simetrico.Steer);
        }

        [Fact]
        public void Reflejo_SoloActuaSiBuscaYSiElMundoLoPide()
        {
            // (a) Con carga no se toca: la que vuelve ya lleva su camino.
            var conCarga = new WorldSim(seed: 7UL, gridCells: Grid, colonyCount: 1, recruitment: true);
            var c1 = conCarga.Colonies[0];
            var a1 = c1.Adults[0];
            a1.HasLoad = true;
            c1.FoodLayer.Deposit(Cell(a1.X) + 2, Cell(a1.Y), 0.9f);
            var d1 = new AntDecision();
            conCarga.ApplyTrailRecruitment(c1, a1, c1.Species, ref d1);
            Assert.Equal(0f, d1.Steer);

            // (b) Con el mundo clásico (sin la bandera) el reflejo no existe.
            var clasico = new WorldSim(seed: 7UL, gridCells: Grid, colonyCount: 1);
            var c2 = clasico.Colonies[0];
            var a2 = c2.Adults[0];
            a2.HasLoad = false;
            c2.FoodLayer.Deposit(Cell(a2.X) + 2, Cell(a2.Y), 0.9f);
            var d2 = new AntDecision();
            clasico.ApplyTrailRecruitment(c2, a2, c2.Species, ref d2);
            Assert.Equal(0f, d2.Steer);
        }

        [Fact]
        public void Cargada_DejaRastroDeComidaEnLaCeldaQuePisa()
        {
            var sim = new WorldSim(seed: 11UL, gridCells: Grid, colonyCount: 1, recruitment: true);
            var colony = sim.Colonies[0];
            var ant = colony.Adults[0];

            // Lejos del nido: dentro descargaría y perdería la carga en el mismo
            // paso, antes de poder dejar rastro de vuelta.
            ant.X = colony.NestX + 60f;
            ant.Y = colony.NestY;
            ant.HasLoad = true;
            ant.LoadValue = 5f;

            sim.Step();

            int cx = Cell(ant.X), cy = Cell(ant.Y);
            Assert.True(colony.FoodLayer[cx, cy] > 0f,
                "la cargada no dejó rastro de COMIDA en la celda que pisó (el camino al montón)");
        }

        [Fact]
        public void Buscadora_DejaRastroDeCasaYNoDeComida()
        {
            // El contrato de ida y vuelta: la que SALE a buscar marca el camino de
            // CASA (el que la devuelve al nido); la comida la marca la que vuelve
            // cargada. Antes el reflejo era al revés: la cargada dejaba las dos y
            // la que buscaba no dejaba ninguna.
            var sim = new WorldSim(seed: 11UL, gridCells: Grid, colonyCount: 1, recruitment: true);
            var colony = sim.Colonies[0];
            var ant = colony.Adults[0];

            ant.X = colony.NestX + 120f;
            ant.Y = colony.NestY;
            ant.HasLoad = false;

            sim.Step();

            int cx = Cell(ant.X), cy = Cell(ant.Y);
            Assert.True(colony.HomeLayer[cx, cy] > 0f,
                "la que busca no dejó rastro de CASA en la celda que pisó (el camino de vuelta)");
            Assert.Equal(0f, colony.FoodLayer[cx, cy]);
        }
    }
}
