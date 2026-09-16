using System;
using System.Collections.Generic;
using AntSim.Core.Sim;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    public class EcosystemRealismTests
    {
        [Fact]
        public void NestFeeding_ReplenishesLowEnergyAnt_FromColonyStock()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: 96, colonyCount: 1);
            var colony = sim.Colonies[0];
            colony.Stock = 100f;

            // Situar una hormiga en el nido con energía baja
            var ant = colony.Adults[0];
            ant.X = colony.NestX;
            ant.Y = colony.NestY;
            ant.Energy = 0.20f;
            float initialStock = colony.Stock;

            // Ejecutar varios pasos de simulación
            for (int i = 0; i < 30; i++)
                sim.Step();

            // La hormiga debe haber ganado energía consumiendo stock del nido
            Assert.True(ant.Energy > 0.20f, $"Energy expected > 0.20 but was {ant.Energy}");
            Assert.True(colony.Stock < initialStock, $"Stock expected < {initialStock} but was {colony.Stock}");
        }

        [Fact]
        public void NestFeeding_CannotFeedWhenStockIsDepleted()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: 96, colonyCount: 1);
            var colony = sim.Colonies[0];
            colony.Stock = 0f;

            var ant = colony.Adults[0];
            ant.X = colony.NestX;
            ant.Y = colony.NestY;
            ant.Energy = 0.20f;
            ant.TrophallaxisCooldown = 10f; // Prevenir recibir trofalaxis de compañeras

            // Asegurar que ninguna compañera transfiera energía
            for (int i = 1; i < colony.Adults.Count; i++)
            {
                colony.Adults[i].Energy = 0.20f;
                colony.Adults[i].TrophallaxisCooldown = 10f;
            }

            sim.Step();

            // Con stock en 0, no puede recargar del nido
            Assert.True(ant.Energy <= 0.20f + 1e-4f, $"Energy expected <= 0.20 but was {ant.Energy}");
        }

        [Fact]
        public void DirectTrophallaxis_TransfersEnergyFromSatiatedToHungryPeer()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: 96, colonyCount: 1);
            var colony = sim.Colonies[0];

            var donor = colony.Adults[0];
            var receiver = colony.Adults[1];

            donor.X = 300f; donor.Y = 300f; donor.Energy = 0.95f; donor.TrophallaxisCooldown = 0f;
            receiver.X = 302f; receiver.Y = 302f; receiver.Energy = 0.20f; receiver.TrophallaxisCooldown = 0f;

            float initialDonorEnergy = donor.Energy;
            float initialReceiverEnergy = receiver.Energy;

            // Ejecutar un paso
            sim.Step();

            Assert.True(receiver.Energy > initialReceiverEnergy, $"Receiver energy expected > {initialReceiverEnergy} but was {receiver.Energy}");
            Assert.True(donor.Energy < initialDonorEnergy, $"Donor energy expected < {initialDonorEnergy} but was {donor.Energy}");
        }

        [Fact]
        public void AntCaste_CategorizesCorrectlyByAgeAndExperience()
        {
            var ant = new Ant();
            ant.InitFromVigor(15f, 600f, 1.0f);

            // Joven -> Nodriza
            ant.Age = 50f; // 50 / 780 = 0.06 < 0.25
            Assert.Equal(AntCaste.Nurse, ant.Caste);

            // Madura forrajera
            ant.Age = 250f; // > 0.25 y < 0.70
            Assert.Equal(AntCaste.Forager, ant.Caste);

            // Veterana exploradora
            ant.Age = 600f; // > 0.70 sin comida previa
            Assert.Equal(AntCaste.Scout, ant.Caste);

            // Veterana con experiencia de forrajeo -> Forrajera
            ant.LifetimeFoodGathered = 10f;
            Assert.Equal(AntCaste.Forager, ant.Caste);
        }

        [Fact]
        public void QueenOviposition_SlowsDownUnderStockScarcity()
        {
            var colonyScarcity = new Colony
            {
                Id = 0,
                Species = SpeciesDescriptor.LasiusNiger,
                Stock = 5f, // Muy bajo (< 20% de 150)
                StockMax = 150f,
                QueenEnergy = 1f,
                Rng = new DeterministicRandom(100),
                Pool = new Evolution.GenomePool(new DeterministicRandom(101), new[] { 19, 8, 6 }),
                InflowEma = 0.2f
            };
            colonyScarcity.Adults.Add(new Ant { Alive = true });

            var colonyAbundance = new Colony
            {
                Id = 1,
                Species = SpeciesDescriptor.LasiusNiger,
                Stock = 140f, // Abundante (> 60% de 150)
                StockMax = 150f,
                QueenEnergy = 1f,
                Rng = new DeterministicRandom(100),
                Pool = new Evolution.GenomePool(new DeterministicRandom(101), new[] { 19, 8, 6 }),
                InflowEma = 0.2f
            };
            colonyAbundance.Adults.Add(new Ant { Alive = true });

            var events = new List<SimEvent>();
            uint nextId = 10;

            for (int i = 0; i < 300; i++)
            {
                ColonyController.Step(colonyScarcity, SimConstants.FixedDtSeconds, (ulong)i, events, ref nextId);
                ColonyController.Step(colonyAbundance, SimConstants.FixedDtSeconds, (ulong)i, events, ref nextId);
            }

            Assert.True(colonyAbundance.Eggs.Count >= colonyScarcity.Eggs.Count,
                $"Abundance eggs ({colonyAbundance.Eggs.Count}) should be >= scarcity eggs ({colonyScarcity.Eggs.Count})");
        }

        [Fact]
        public void DynamicRespawn_ReplenishesDepletedFoodItems()
        {
            var sim = new WorldSim(seed: 42UL, gridCells: 96, colonyCount: 1);
            int initialItemCount = sim.Items.Count;
            Assert.True(initialItemCount > 0, "Debe haber ítems iniciales.");

            // Simular recolección masiva eliminando ítems
            var itemsList = (List<FoodItem>)sim.Items;
            itemsList.Clear();
            Assert.Empty(sim.Items);

            // Dar pasos para que RespawnItems actúe
            for (int i = 0; i < 20; i++)
                sim.Step();

            Assert.True(sim.Items.Count > 0, "Los ítems de comida deben haberse repuesto dinámicamente.");
        }
    }
}
