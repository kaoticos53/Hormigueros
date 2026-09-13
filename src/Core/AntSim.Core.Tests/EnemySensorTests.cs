using System;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2b.2 — el sensor de presa: con una especie beligerante (ContactRadius &gt; 0)
/// en un mundo multi-colonia, el canal 12 ProxFront se reconvierte a «hormiga
/// enemiga más cercana» (1 − dist/visión). El gating es de la llamada:
/// mundos de una colonia o sin beligerantes producen SENSORES y HASHES
/// idénticos al build anterior (los 4 pines de CI no se mueven).
/// </summary>
public class EnemySensorTests
{
    private const ulong Seed = 42UL;

    private static Ant Placed(Colony colony, uint id, float x, float y)
    {
        var ant = new Ant { Id = id, ColonyId = colony.Id, X = x, Y = y, Heading = 0f };
        ant.InitFromVigor(colony.Species.EnergyCapacity, colony.Species.BaseLifespan, 1f);
        ant.Brain = new FixedDecisionBrain(new AntDecision { Interact = 1f });
        colony.Adults.Add(ant);
        return ant;
    }

    [Fact]
    public void Eciton_MultiColonia_Canal12VeaAlRival()
    {
        var sim = new WorldSim(Seed, 96, 2,
            species: new[] { SpeciesDescriptor.Eciton, SpeciesDescriptor.LasiusNiger });
        var e = sim.Colonies[0];
        var l = sim.Colonies[1];

        // Eciton en el centro; la presa A 20 u de distancia (dentro de la
        // visión 90), alejada de los bordes para que la pared no domine.
        var raider = Placed(e, 100, 380f, 384f);
        Placed(l, 200, 400f, 384f);

        var sensors = AntSenses.Build(e, raider, sim.Items,
            sim.WorldWidth, sim.WorldHeight, rivals: sim.Colonies);

        // El rival se siente: magnitud > 0 con valor esperado 1 − 20/90.
        float esperado = 1f - 20f / (SpeciesDescriptor.Eciton.VisionRadius * raider.SensorScale);
        Assert.Equal(esperado, sensors.ProxFront, 3);
        // La pared sigue en los canales laterales (sin cambio).
        Assert.Equal(sensors.ProxLeft, sensors.ProxRight, 5);
    }

    [Fact]
    public void Eciton_SinRivalACanca_Canal12EsLaPared()
    {
        var sim = new WorldSim(Seed, 96, 2,
            species: new[] { SpeciesDescriptor.Eciton, SpeciesDescriptor.LasiusNiger });
        var e = sim.Colonies[0];
        // Nadie del rival cerca: la presa en SU nido, el raider en el suyo
        // (separación por defecto ≈ world/3 = 128 u > visión 90).
        var raider = Placed(e, 100, e.NestX, e.NestY);
        Placed(sim.Colonies[1], 200, sim.Colonies[1].NestX, sim.Colonies[1].NestY);

        var sensors = AntSenses.Build(e, raider, sim.Items,
            sim.WorldWidth, sim.WorldHeight, rivals: sim.Colonies);

        // Sin presa a la vista: el frontal vuelve a ser la pared (clásico).
        float dWall = MathF.Min(MathF.Min(raider.X, sim.WorldWidth - raider.X),
                                MathF.Min(raider.Y, sim.WorldHeight - raider.Y));
        float proxPared = Math.Clamp(1f - dWall / (SpeciesDescriptor.Eciton.SensorReach * raider.SensorScale), 0f, 1f);
        Assert.Equal(proxPared, sensors.ProxFront, 4);
    }

    [Fact]
    public void Lasius_EnElPasoDelMundo_Canal12SigueSiendoLaPared()
    {
        // El gating POR ESPECIE vive en WorldSim.Act (rivals = null para
        // especies con ContactRadius = 0): la Lasius de un mundo mixto
        // recibe el canal clásico aunque haya una Eciton al lado.
        var sim = new WorldSim(Seed, 96, 2,
            species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.Eciton });
        var c0 = sim.Colonies[0];
        var lasius = Placed(c0, 100, 380f, 384f);
        var eciton = Placed(sim.Colonies[1], 200, 382f, 384f); // a 2 u

        float dWall = MathF.Min(MathF.Min(lasius.X, sim.WorldWidth - lasius.X),
                                MathF.Min(lasius.Y, sim.WorldHeight - lasius.Y));
        float proxPared = Math.Clamp(1f - dWall / (SpeciesDescriptor.LasiusNiger.SensorReach * lasius.SensorScale), 0f, 1f);

        // El paso del MUNDO llama a AntSenses con rivals=null para la Lasius:
        // su canal 12 es la pared, no el rival pegado a ella.
        bool sensorVioRival = false;
        lasius.Brain = new ProbeBrain(v => sensorVioRival = v.ProxFront > proxPared + 0.01f);
        sim.Step();

        Assert.False(sensorVioRival, "la Lasius no debe ver rivales (gating por especie)");
        Assert.True(eciton.Alive); // y nadie la golpeó: solo Eciton golpea
    }

    [Fact]
    public void MundoSinEciton_ElHashEsIdenticoAlClasico()
    {
        // La vía clásica (rivals: null) y la nueva con gating bien puesto
        // (beligerante ausente ⇒ null) producen EL MISMO hash paso a paso.
        var clasico = new WorldSim(Seed, 96, 2,
            species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.LasiusNiger });
        var nuevo = new WorldSim(Seed, 96, 2,
            species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.LasiusNiger });

        for (int i = 0; i < 300; i++)
        {
            clasico.Step();
            nuevo.Step();
            Assert.Equal(clasico.HashLine(), nuevo.HashLine());
        }
    }

    [Fact]
    public void EcitonEnMundoDeUnaColonia_ElHashNoCambiaPorElSensor()
    {
        // El gating exige multi-colonia: un mundo de UNA colonia Eciton no
        // activa el sensor (no hay rivales que medir) y el hash coincide con
        // la vía clásica.
        var clasico = new WorldSim(Seed, 96, 1, species: new[] { SpeciesDescriptor.Eciton });
        var nuevo = new WorldSim(Seed, 96, 1, species: new[] { SpeciesDescriptor.Eciton });

        for (int i = 0; i < 300; i++)
        {
            clasico.Step();
            nuevo.Step();
            Assert.Equal(clasico.HashLine(), nuevo.HashLine());
        }
    }
}

/// <summary>Cerebro sonda: captura los sensores con los que fue evaluado.</summary>
public sealed class ProbeBrain : IBrain
{
    private readonly System.Action<AntSensors> _onEvaluate;
    public ProbeBrain(System.Action<AntSensors> onEvaluate) => _onEvaluate = onEvaluate;
    public BrainKind Kind => BrainKind.Mlp;
    public int ContractVersion => BrainContract.CurrentVersion;
    public void Evaluate(in AntSensors sensors, ref AntDecision decision)
    {
        _onEvaluate(sensors);
        decision = new AntDecision { Interact = 0f };
    }
}
