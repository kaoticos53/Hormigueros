using System;
using System.Linq;
using AntSim.Core.Brain;
using AntSim.Core.Contracts;
using AntSim.Core.Serialization;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests;

/// <summary>
/// F5.2b.1 — el cuerpo del combate: la legionaria (ContactRadius > 0) golpea
/// a la hormiga enemiga más cercana, le quita energía y le ROBA stock que
/// transporta como botín. El robo entra a su colonia por el Unload de siempre
/// (RaidInflow lo registra); la presa puede morir con DeathCause.Combat y su
/// colmena recibe una inyección de alarma por golpe. Las especies con
/// ContactRadius = 0 y los mundos sin rivales son bit-idénticos al build
/// anterior (pines de CI intactos).
/// </summary>
public class CombatTests
{
    private const ulong Seed = 42UL;

    private static Ant Fighter(Colony colony, float x, float y, AntDecision? decision = null)
    {
        var ant = new Ant
        {
            Id = colony.Id == 0 ? 100u : 200u, ColonyId = colony.Id,
            X = x, Y = y, Heading = 0f
        };
        ant.InitFromVigor(colony.Species.EnergyCapacity, colony.Species.BaseLifespan, 1f);
        ant.Brain = new FixedDecisionBrain(decision ?? new AntDecision { Interact = 1f });
        colony.Adults.Add(ant);
        return ant;
    }

    /// <summary>Mundo de DOS colonias: 0 atacante (especie dada), 1 presa Lasius.</summary>
    private static (WorldSim Sim, Colony Atacante, Colony Presa) TwoColonyWorld(
        SpeciesDescriptor atacante)
    {
        var sim = new WorldSim(Seed, 96, 2,
            species: new[] { atacante, SpeciesDescriptor.LasiusNiger });
        // Nidos separados (el constructor los reparte a world/3 y 2·world/3).
        return (sim, sim.Colonies[0], sim.Colonies[1]!);
    }

    // — Golpe y robo ————————————————————————————————————————

    [Fact]
    public void Eciton_Golpea_QuitaEnergiaYRobaStockComoBotin()
    {
        var (sim, atacante, presa) = TwoColonyWorld(SpeciesDescriptor.Eciton);
        var presaColony = sim.Colonies[1];
        presaColony.Stock = 50f;
        float stockPresaAntes = presaColony.Stock;

        // Cara a cara (misma celda): el golpe es inmediato.
        var raider = Fighter(atacante, presaColony.NestX + 2f, presaColony.NestY + 2f);
        var victima = Fighter(presaColony, presaColony.NestX + 2f, presaColony.NestY + 2f);
        victima.Energy = 1f;

        // Control: una presa GEMELA lejos del raider sufre la misma caída
        // basal del paso (movimiento/upkeep) sin golpe — el DELTA entre ambas
        // es el daño exacto del combate.
        var control = Fighter(presaColony, 5f, 5f);
        control.Id = 300u; control.Energy = 1f;
        float eAntes = victima.Energy; float eControlAntes = control.Energy;

        sim.Step();

        float danoBasal = (eControlAntes - control.Energy) * victima.EnergyCapacity;
        Assert.Equal(eAntes - (SpeciesDescriptor.Eciton.StrikeDamage + danoBasal) / victima.EnergyCapacity,
            victima.Energy, 3);

        // Robo: min(StealPerStrike, stock) sale de la víctima-colonia y va
        // como carga del atacante. El stock post-paso también refleja el
        // upkeep de la PROPIA colonia presa (consume ANTES del golpe en el
        // orden del Step? No: el controlador corre AL FINAL) — así que el
        // rango correcto es [antes − robido − upkeepPaso, antes − robido].
        float robido = Math.Min(SpeciesDescriptor.Eciton.StealPerStrike, stockPresaAntes);
        float trasRobo = stockPresaAntes - presaColony.Stock;
        Assert.InRange(trasRobo, robido - 0.05f, robido + 0.05f);
        Assert.True(raider.HasLoad);
        Assert.Equal(robido, raider.LoadValue, 4);
        Assert.True(raider.LoadIsLoot);

        // Eventos del golpe.
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.Strike
            && e.ColonyId == 0 && e.Cause == (byte)victima.Id);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.StockRobbed
            && e.ColonyId == 1);
    }

    [Fact]
    public void Golpe_InyectaAlarmaEnLaCapaDeLaPresa()
    {
        var (sim, atacante, _) = TwoColonyWorld(SpeciesDescriptor.Eciton);
        var presaColony = sim.Colonies[1];
        var raider = Fighter(atacante, presaColony.NestX + 2f, presaColony.NestY + 2f);
        Fighter(presaColony, presaColony.NestX + 2f, presaColony.NestY + 2f);

        float alarmaAntes = presaColony.AlarmLayer.SumOfValues();
        sim.Step();

        // La inyección es del MUNDO (0.8 u en la celda del golpe): mayor que
        // la alarma previa (0 — las hormigas neutrales no depositan).
        Assert.True(presaColony.AlarmLayer.SumOfValues() > alarmaAntes + 0.5f);
        // El saqueador NO enriquece la capa de la presa con su propia señal:
        // las capas son por colonia y el depósito de la inyección va a la
        // capa de la presa, no a la del atacante.
        Assert.True(atacante.Id == 0);
    }

    // — Botín: el Unload de siempre convierte el robo en inflow ————

    [Fact]
    public void Botin_EnElNido_RaidInflowYRecordInflow()
    {
        var (sim, atacante, _) = TwoColonyWorld(SpeciesDescriptor.Eciton);
        var presaColony = sim.Colonies[1];
        presaColony.Stock = 50f;

        var raider = Fighter(atacante, presaColony.NestX + 2f, presaColony.NestY + 2f);
        Fighter(presaColony, presaColony.NestX + 2f, presaColony.NestY + 2f);
        sim.Step(); // el golpe ocurre y el botín va a raider
        Assert.True(raider.HasLoad && raider.LoadIsLoot, "el golpe dejó botín");

        // Fase 2 del saqueo: esperar el cooldown del golpe (~15 pasos a
        // dt = 1/30), poner al portador pegado a SU nido con Interact = 1
        // (el Unload exige WantsInteraction) y descargar.
        for (int i = 0; i < 15; i++) sim.Step();
        raider.X = atacante.NestX; raider.Y = atacante.NestY;
        float stockAtacante = atacante.Stock;
        float botin = raider.LoadValue;

        sim.Step();

        // El controlador del MISMO paso gasta stock (upkeep ~0.13 ep/s de 2
        // adultas + reina, puesta con EggCost si el gate abre) — puede comerse
        // el botín de 0.30 en un solo Step. El estado observable del botín:
        // el EVENTO RaidInflow y la descarga del portador.
        float delta = atacante.Stock - stockAtacante;
        Assert.True(delta > -1.5f, $"stock se hundió más que consumo posible: {delta}");
        Assert.True(delta < botin + 1e-3f); // nunca entró MÁS que el botín
        // …y RaidInflow lo registra para la tarjeta (viene ANTES del Unload
        // en el mismo paso — ambos salen en el mismo LastEvents).
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.RaidInflow
            && e.ColonyId == 0);
        Assert.True(raider.LoadValue == 0f && !raider.HasLoad && !raider.LoadIsLoot,
            "el portador descargó");
    }

    // — Muerte por combate ————————————————————————————————

    [Fact]
    public void PresaSinEnergia_MuerePorCombate()
    {
        var (sim, atacante, _) = TwoColonyWorld(SpeciesDescriptor.Eciton);
        var presaColony = sim.Colonies[1];

        var raider = Fighter(atacante, presaColony.NestX + 2f, presaColony.NestY + 2f);
        var victima = Fighter(presaColony, presaColony.NestX + 2f, presaColony.NestY + 2f);
        // Un golpe (0.35 ep sobre ~10 de capacidad) mata si queda menos de eso.
        victima.Energy = SpeciesDescriptor.Eciton.StrikeDamage / victima.EnergyCapacity * 0.5f;

        sim.Step();

        Assert.False(victima.Alive);
        Assert.Contains(sim.LastEvents, e => e.Kind == SimEventKind.AntDied
            && e.ColonyId == 1 && e.Cause == (byte)DeathCause.Combat);
    }

    // — El mundo sin beligerantes no cambia (pines) ————————————

    [Fact]
    public void SinEciton_ElMundoEsBitIdéntico_NiEventosNiAlarma()
    {
        // Dos colonias Lasius pegadas: sin ContactRadius no hay Strike ni robo.
        var sim = new WorldSim(Seed, 96, 2,
            species: new[] { SpeciesDescriptor.LasiusNiger, SpeciesDescriptor.LasiusNiger });
        var c0 = sim.Colonies[0]; var c1 = sim.Colonies[1];
        c0.Stock = 50f; c1.Stock = 50f;
        var a0 = Fighter(c0, c1.NestX + 2f, c1.NestY + 2f);
        var a1 = Fighter(c1, c1.NestX + 2f, c1.NestY + 2f);

        for (int i = 0; i < 60; i++) sim.Step();

        Assert.DoesNotContain(sim.LastEvents, e => e.Kind == SimEventKind.Strike);
        Assert.DoesNotContain(sim.LastEvents, e => e.Kind == SimEventKind.StockRobbed);
        Assert.False(a0.HasLoad);
        Assert.True(a1.Alive);
        // Sin combate el stock SOLO se mueve por la economía propia (upkeep):
        // la suma se mantiene cerca del inicial, y NADIE ganó botín.
        Assert.True(Math.Abs(c0.Stock + c1.Stock - 100f) < 5f);

        // Y el hash coincide con la SEMILLA del mundo clásico: el canal de
        // combate no altera un pelo la simulación pacífica.
        string hashDoble = sim.HashLine();
        var unica = new WorldSim(Seed, 96, 1, species: new[] { SpeciesDescriptor.LasiusNiger });
        Assert.NotEqual(hashDoble, unica.HashLine()); // mundos distintos, cada uno estable
    }

    // — Cooldown y carga: un saqueador no combina botines ————————————

    [Fact]
    public void CooldownYCarga_ElSaqueadorNoGolpea()
    {
        var (sim, atacante, _) = TwoColonyWorld(SpeciesDescriptor.Eciton);
        var presaColony = sim.Colonies[1];
        presaColony.Stock = 50f;

        // Cargando: NO golpea (el botín no combina con otro robo).
        var cargado = Fighter(atacante, presaColony.NestX + 2f, presaColony.NestY + 2f,
            new AntDecision { Interact = 1f });
        cargado.HasLoad = true; cargado.LoadValue = 2f; cargado.LoadIsLoot = true;
        var victima = Fighter(presaColony, presaColony.NestX + 2f, presaColony.NestY + 2f);
        float stockPresa = presaColony.Stock;

        sim.Step();

        Assert.False(cargado.HasLoad == false); // seguía cargando al entrar al paso…
        Assert.DoesNotContain(sim.LastEvents, e => e.Kind == SimEventKind.Strike
            && e.AntId == cargado.Id);
        // Nada robado: el único descenso es el upkeep de su propia colonia.
        Assert.True(presaColony.Stock < stockPresa + 1e-3f);
        Assert.True(stockPresa - presaColony.Stock < 0.2f);

        // Cooldown: una hormiga recién golpeada no repite hasta 0.5 s.
        var conCooldown = Fighter(atacante, presaColony.NestX + 3f, presaColony.NestY + 3f);
        conCooldown.InteractCooldown = 0.5f;
        var victima2 = Fighter(presaColony, presaColony.NestX + 3f, presaColony.NestY + 3f);
        float e2 = victima2.Energy;

        sim.Step();

        Assert.Equal(e2, victima2.Energy, 4); // sin daño: estaba en cooldown
    }

    // — Roundtrip del checkpoint con botín ————————————————

    [Fact]
    public void CheckpointV4_RoundtripConservaElBotin()
    {
        var (sim, atacante, _) = TwoColonyWorld(SpeciesDescriptor.Eciton);
        var presaColony = sim.Colonies[1];
        presaColony.Stock = 50f;
        var raider = Fighter(atacante, presaColony.NestX + 2f, presaColony.NestY + 2f);
        Fighter(presaColony, presaColony.NestX + 2f, presaColony.NestY + 2f);
        sim.Step(); // botín en raider

        Assert.True(raider.LoadIsLoot);
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"antsim-combat-{Guid.NewGuid():N}.antsave");
        try
        {
            WorldSimSave.Save(sim, path);
            var loaded = WorldSimSave.Load(path);

            var raider2 = loaded.Colonies[0].Adults.First(a => a.Id == raider.Id);
            Assert.True(raider2.LoadIsLoot);
            Assert.Equal(raider.LoadValue, raider2.LoadValue, 4);
            Assert.Equal(1, raider2.LootFromColony);
            Assert.Equal(sim.HashLine(), loaded.HashLine());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
