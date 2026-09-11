using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Tarjeta de inspección de hormiga (F4.2, contrato HUD §5). Modelo PURO —
    /// sin UnityEngine — que sigue a UNA hormiga seleccionada a través de los
    /// TickViews del stream: serie temporal de los 12 campos del canal A, muerte
    /// capturada del canal B (con causa) y render de la tarjeta en formato fijo.
    /// La UI solo selecciona (click) y muestra <see cref="RenderCard"/>; nunca
    /// consulta el Core ni inventa umbrales (regla dura del contrato).
    ///
    /// Consumo: <c>presenter.Feed2(line)</c> devuelve el TickView completo y este
    /// modelo lo observa — <c>inspector.Observe(view)</c>. Memoria acotada: solo
    /// rastrea hormigas seleccionadas en algún momento (no todo el mundo).
    /// </summary>
    public sealed class AntInspectorModel
    {
        /// <summary>Una muestra del canal A para la hormiga rastreada (12 campos).</summary>
        public readonly struct Sample
        {
            public readonly ulong Tick;
            public readonly float X, Y, Heading;
            public readonly bool HasLoad, Alive;
            public readonly float Vigor, Energy, Age;
            public readonly bool IsImmigrant;
            public readonly uint GenomeFingerprint;

            public Sample(ulong tick, float x, float y, float heading, bool hasLoad, bool alive,
                float vigor, float energy, float age, bool isImmigrant, uint genomeFingerprint)
            { Tick = tick; X = x; Y = y; Heading = heading; HasLoad = hasLoad; Alive = alive;
              Vigor = vigor; Energy = energy; Age = age; IsImmigrant = isImmigrant;
              GenomeFingerprint = genomeFingerprint; }
        }

        /// <summary>Historial de una hormiga rastreada.</summary>
        public sealed class AntRecord
        {
            public uint Id;
            public int ColonyId;
            public readonly List<Sample> Samples = new();

            /// <summary>Muerte (canal B, evento AntDied): tick y causa — null si vive.</summary>
            public ulong? DeathTick;
            /// <summary>Causa byte del evento: 0 = vejez, 1 = inanición (DeathCause del Core).</summary>
            public byte? DeathCause;

            public Sample Last => Samples[^1];
        }

        private readonly Dictionary<uint, AntRecord> _records = new();
        private uint? _selected;

        /// <summary>Id de la hormiga seleccionada (null = nada seleccionado).</summary>
        public uint? SelectedId => _selected;

        /// <summary>Registro de la hormiga seleccionada (null si aún no se la vio en el stream).</summary>
        public AntRecord? Tracked => _selected is uint id && _records.TryGetValue(id, out var r) ? r : null;

        /// <summary>Todos los registros acumulados (selecciones pasadas incluidas).</summary>
        public IReadOnlyCollection<AntRecord> Records => _records.Values;

        /// <summary>Selecciona una hormiga. Puede hacerse antes de verla: la tarjeta
        /// muestra "esperando datos" hasta la primera aparición en el canal A.</summary>
        public void Select(uint antId) => _selected = antId;

        /// <summary>Deselecciona (el historial acumulado se conserva).</summary>
        public void Clear() => _selected = null;

        /// <summary>
        /// Observa un TickView completo (el de <c>Feed2</c>). Añade la muestra del
        /// canal A si la hormiga seleccionada aparece, y registra la muerte si el
        /// canal B trae un AntDied. Las muertes de hormigas no rastreadas se
        /// guardan como expediente (sin datos de posición): una muerte nunca se
        /// pierde por llegar antes que la selección — el canal A no emite filas
        /// de hormigas muertas.
        /// </summary>
        public void Observe(GameStreamParser.TickView view)
        {
            // — Canal B: muerte de una hormiga rastreada (con causa) —
            foreach (var ev in view.Events)
            {
                if (ev.Kind != 1 /* SimEventKind.AntDied */) continue;
                if (_records.TryGetValue(ev.AntId, out var dead))
                {
                    if (dead.DeathTick != null) continue; // una sola muerte por hormiga
                }
                else
                {
                    // Expediente sin muestras: la hormiga murió antes de ser rastreada.
                    dead = new AntRecord { Id = ev.AntId, ColonyId = ev.ColonyId };
                    _records[ev.AntId] = dead;
                }
                dead.DeathTick = view.Tick;
                dead.DeathCause = ev.Cause;
            }

            // — Canal A: muestra de la hormiga seleccionada —
            if (_selected is uint sel)
            {
                foreach (var a in view.Ants)
                {
                    if (a.Id != sel) continue;
                    if (!_records.TryGetValue(sel, out var rec))
                    {
                        rec = new AntRecord { Id = a.Id, ColonyId = a.ColonyId };
                        _records[sel] = rec;
                    }
                    rec.Samples.Add(new Sample(view.Tick, a.X, a.Y, a.Heading,
                        a.HasLoad, a.Alive, a.Vigor, a.Energy, a.Age,
                        a.IsImmigrant, a.GenomeFingerprint));
                }
            }
        }

        /// <summary>Causa de muerte como texto fijo del contrato (byte 0/1 del Core).</summary>
        public static string DeathCauseText(byte cause) => cause switch
        {
            0 => "vejez",
            1 => "inanición",
            _ => "causa " + cause.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>
        /// Render de la tarjeta (formato fijo, textos definitivos de la v1,
        /// cultura invariante). Estados: sin selección / esperando datos /
        /// viva / muerta. El historial muestra el rango de ticks con datos.
        /// </summary>
        public string RenderCard()
        {
            var sb = new StringBuilder();

            if (_selected is not uint sel)
            {
                sb.Append("inspección: sin selección");
                return sb.ToString();
            }

            var rec = Tracked;
            if (rec == null || rec.Samples.Count == 0)
            {
                // Con expediente de muerte pero sin datos del canal A (seleccionada
                // tarde: los muertos no emiten filas) — la muerte manda.
                if (rec?.DeathTick is ulong dt0)
                {
                    sb.Append("Hormiga #").Append(rec.Id)
                      .Append(" · colonia ").Append(rec.ColonyId.ToString(CultureInfo.InvariantCulture))
                      .Append('\n')
                      .Append("estado: muerta (").Append(DeathCauseText(rec.DeathCause ?? 0))
                      .Append(" · tick ").Append(dt0.ToString(CultureInfo.InvariantCulture)).Append(")\n")
                      .Append("sin datos de posición");
                    return sb.ToString();
                }
                sb.Append("Hormiga #").Append(sel).Append(" — esperando datos");
                return sb.ToString();
            }

            var last = rec.Last;
            sb.Append("Hormiga #").Append(rec.Id)
              .Append(" · colonia ").Append(rec.ColonyId.ToString(CultureInfo.InvariantCulture))
              .Append('\n');

            if (rec.DeathTick is ulong dt)
            {
                sb.Append("estado: muerta (").Append(DeathCauseText(rec.DeathCause ?? 0))
                  .Append(" · tick ").Append(dt.ToString(CultureInfo.InvariantCulture)).Append(")\n");
            }
            else
            {
                sb.Append("estado: ").Append(last.Alive ? "viva" : "muerta (sin evento)").Append('\n');
            }

            sb.Append("posición: ")
              .Append(last.X.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ")
              .Append(last.Y.ToString("0.0", CultureInfo.InvariantCulture))
              .Append(" · rumbo ").Append(last.Heading.ToString("0.00", CultureInfo.InvariantCulture))
              .Append('\n');
            sb.Append("carga: ").Append(last.HasLoad ? "con carga" : "sin carga").Append('\n');
            sb.Append("vigor ").Append(last.Vigor.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" · energía ").Append(last.Energy.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" · edad ").Append(last.Age.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s\n");
            sb.Append("inmigrante: ").Append(last.IsImmigrant ? "sí" : "no")
              .Append(" · cerebro #").Append(last.GenomeFingerprint.ToString(CultureInfo.InvariantCulture))
              .Append('\n');
            sb.Append("historial: ").Append(rec.Samples.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" muestras · ticks ").Append(rec.Samples[0].Tick.ToString(CultureInfo.InvariantCulture))
              .Append('-').Append(last.Tick.ToString(CultureInfo.InvariantCulture));

            return sb.ToString();
        }
    }
}
