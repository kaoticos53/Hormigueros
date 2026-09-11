using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntSim.Core.Telemetry;
using AntSim.Core.World;

namespace AntSim.Core.Scenario;

/// <summary>
/// Derivación de alertas del HUD (F4.2, contrato en docs/fase4-hud-contrato.md §1).
/// Observador puro de canal B/C (mismo patrón que RelayTracker/MetricRecorder):
/// nunca muta el mundo, nunca consume RNG — idénticos hashes con y sin él.
///
/// Los umbrales y cadencias son LOS DEL CONTRATO (benchmark Fase 3ter y límites
/// de regresión de pipeline.sh): la UI no inventa ninguno.
/// </summary>
public sealed class AlertDeriver
{
    public enum Level : byte { Info = 0, Amber = 1, Green = 2, Red = 3 }

    /// <summary>Alerta derivada: la UI la muestra con su color de nivel.</summary>
    public readonly struct Alert
    {
        public readonly string Key;       // estable (para no duplicar en la cola de la UI)
        public readonly Level Lvl;
        public readonly string Text;      // texto final v1 (español, del contrato)
        public readonly int ColonyId;     // -1 = global
        public readonly float X, Y;       // para el salto de cámara (-1 si no aplica)
        public readonly ulong Tick;

        public Alert(string key, Level lvl, string text, int colonyId, float x, float y, ulong tick)
        { Key = key; Lvl = lvl; Text = text; ColonyId = colonyId; X = x; Y = y; Tick = tick; }
    }

    // — umbrales del contrato —
    public const int MortalityWindowDeaths = 5;      // muertes en 1 ventana (1 s)
    public const int LayingHaltedWindows = 5;        // ventanas sin puesta seguidas
    public const float StockLowFraction = 0.20f;     // <20% de stockMax
    public const float RelayWeakShrink = 0.20f;      // caída >20% del tramo vs media de 5
    public const int MaxAlertsPerCall = 8;           // cola de la UI: la más vieja sale

    // — cadencias (ticks entre alertas del mismo tipo) —
    private const ulong GenomeCadenceTicks = 10 * 30;   // 10 s
    private const ulong MortalityCadenceTicks = 30 * 30; // 30 s
    private const ulong RelayWeakCadenceTicks = 60 * 30; // 60 s

    private readonly HashSet<string> _once = new();          // alertas de disparo único
    // Último tick de cada alerta cadenciada (0 = nunca: el primer disparo siempre pasa).
    private ulong _lastGenomeAlert, _lastMortalityAlert, _lastRelayWeakAlert;
    private int _layingHaltedStreak;                          // ventanas sin puesta
    private readonly float[] _recentCarryLegs = new float[5]; // media móvil del relevo
    private int _carryLegCount;
    private int _colonyCount = -1;

    /// <summary>Deriva las alertas de un paso: eventos del tick + métrica de la
    /// ventana (cuando cierra) + telemetría de relevo (cuando se emite). Llamar
    /// tras cada Step, con lo mismo que consume la UI del stream.</summary>
    public void Observe(IReadOnlyList<SimEvent> events, MetricRecorder.MetricFrame? metrics,
        RelayTracker relay, WorldSim sim, List<Alert> output)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (relay is null) throw new ArgumentNullException(nameof(relay));
        if (sim is null) throw new ArgumentNullException(nameof(sim));
        if (output is null) throw new ArgumentNullException(nameof(output));

        ulong tick = sim.Tick;
        if (_colonyCount < 0) _colonyCount = sim.Colonies.Count;

        // — canal B: gatillos por evento —
        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            switch (ev.Kind)
            {
                case SimEventKind.ColonyExtinct:
                    if (_once.Add("extinct:" + ev.ColonyId))
                        output.Add(new Alert("extinct:" + ev.ColonyId, Level.Red,
                            $"La colonia {ev.ColonyId} murió en t={ev.Tick}",
                            ev.ColonyId, ev.X, ev.Y, ev.Tick));
                    break;

                case SimEventKind.GenomeEnteredElite:
                    if (_lastGenomeAlert == 0 || tick - _lastGenomeAlert >= GenomeCadenceTicks)
                    {
                        _lastGenomeAlert = tick;
                        output.Add(new Alert("elite", Level.Info,
                            "Un genoma entró en la élite", ev.ColonyId, ev.X, ev.Y, ev.Tick));
                    }
                    break;

                case SimEventKind.GenomeDiscarded:
                    if (_lastGenomeAlert == 0 || tick - _lastGenomeAlert >= GenomeCadenceTicks)
                    {
                        _lastGenomeAlert = tick;
                        output.Add(new Alert("discarded", Level.Info,
                            "Un inmigrante no rindió ≥ la mediana — fuera", ev.ColonyId, ev.X, ev.Y, ev.Tick));
                    }
                    break;
            }
        }

        // — canal C: mortalidad en picada (por ventana de 1 s) —
        if (metrics is MetricRecorder.MetricFrame m && m.Deaths >= MortalityWindowDeaths
            && (_lastMortalityAlert == 0 || tick - _lastMortalityAlert >= MortalityCadenceTicks))
        {
            _lastMortalityAlert = tick;
            output.Add(new Alert("mortality", Level.Amber,
                $"{m.Deaths} muertes en el último segundo", -1, -1f, -1f, tick));
        }

        // — puesta parada: sin huevos ni nacimientos 5 ventanas seguidas + reserva baja —
        if (metrics is MetricRecorder.MetricFrame mm)
        {
            bool quiet = mm.EggsLaid == 0 && mm.Births == 0;
            if (quiet) _layingHaltedStreak++;
            else _layingHaltedStreak = 0;

            if (_layingHaltedStreak >= LayingHaltedWindows)
            {
                for (int c = 0; c < sim.Colonies.Count; c++)
                {
                    var colony = sim.Colonies[c];
                    if (colony.StockMax <= 0 || colony.Stock / colony.StockMax >= StockLowFraction) continue;
                    string key = "laying:" + colony.Id;
                    if (_once.Add(key))
                        output.Add(new Alert(key, Level.Amber,
                            $"La colonia {colony.Id} no pone huevos: reserva baja",
                            colony.Id, colony.NestX, colony.NestY, tick));
                }
            }
            else
            {
                // La reserva se recupera: la alerta puede volver a dispararse.
                for (int c = 0; c < sim.Colonies.Count; c++)
                    _once.Remove("laying:" + sim.Colonies[c].Id);
            }
        }

        // — relevo débil: UMBRALES DE RelayVerdict PARA ESTE MUNDO (F4.3: el drop
        //    escala con el grid, el tramo es invariante) + encogimiento sostenido —
        if (relay.HasUnload)
        {
            // — absoluto: los mismos umbrales que el semáforo de la tarjeta.
            //    Prioridad: drop demasiado lejos (señal de mundo) > tramo corto.
            string? absolute = null;
            if (relay.DropDistanceMean is double dropD
                && dropD > RelayVerdict.DropMaxFor(sim.GridCells))
            {
                absolute = FormattableString.Invariant(
                    $"Las sueltas caen demasiado lejos del nido para este mundo ({dropD:0.0} u > {RelayVerdict.DropMaxFor(sim.GridCells):0} u)");
            }
            else if (relay.CarryLegMean is double legD && (float)legD < RelayVerdict.CarryLegMin)
            {
                absolute = FormattableString.Invariant(
                    $"Tramos demasiado cortos para completar el relevo ({legD:0.0} u < {RelayVerdict.CarryLegMin:0} u)");
            }

            // — encogimiento: caída >20% del tramo acumulado vs la media de las
            //    5 emisiones PREVIAS (se compara antes de almacenar la actual) —
            string? shrink = null;
            if (_carryLegCount >= 5 && relay.CarryLegMean is double leg2)
            {
                float f2 = (float)leg2;
                float mean = 0f;
                for (int i = 0; i < _recentCarryLegs.Length; i++) mean += _recentCarryLegs[i];
                mean /= _recentCarryLegs.Length;
                if (mean > 0f && f2 < mean * (1f - RelayWeakShrink))
                    shrink = FormattableString.Invariant(
                        $"Las cargas completan tramos más cortos ({f2:0.0} u vs media {mean:0.0} u)");
            }

            if (relay.CarryLegMean is double legNow)
            {
                _recentCarryLegs[_carryLegCount % _recentCarryLegs.Length] = (float)legNow;
                _carryLegCount++;
            }

            var reason = absolute ?? shrink;
            if (reason != null
                && (_lastRelayWeakAlert == 0 || tick - _lastRelayWeakAlert >= RelayWeakCadenceTicks))
            {
                _lastRelayWeakAlert = tick;
                output.Add(new Alert("relay-weak", Level.Amber, reason, -1, -1f, -1f, tick));
            }
        }

        // — hito verde: primera descarga de la partida (disparo único) —
        if (relay.HasUnload && _once.Add("first-unload"))
        {
            output.Add(new Alert("first-unload", Level.Green,
                FormattableString.Invariant(
                    $"Primera descarga en t={relay.FirstUnloadTick} ({relay.FirstUnloadTick / 30}s de sim) — la colonia completa ciclos"),
                -1, -1f, -1f, tick));
        }
    }

    /// <summary>Variante de conveniencia que devuelve una lista nueva.</summary>
    public List<Alert> Observe(IReadOnlyList<SimEvent> events, MetricRecorder.MetricFrame? metrics,
        RelayTracker relay, WorldSim sim)
    {
        var list = new List<Alert>();
        Observe(events, metrics, relay, sim, list);
        return list;
    }
}
