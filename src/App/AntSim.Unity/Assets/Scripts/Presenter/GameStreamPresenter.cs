using System;
using System.Collections.Generic;
using System.Threading;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Presenter puro (F4.1, sin UnityEngine): mantiene el último par de TickViews
    /// del stream y calcula el estado interpolado en t ∈ [0,1) — el render consume
    /// esto a su framerate, la simulación sigue a ticks fijos. El retraso de 1 tick
    /// de la arquitectura se materializa aquí: solo interpolamos VISTO.
    ///
    /// <para><b>Reproducción con buffer (F4.1b, Play pass).</b> El CLI construye el
    /// JSONL ENTERO en memoria y lo escribe de golpe: sin buffer el presenter salta
    /// al ÚLTIMO tick al primer frame y la vista nunca muestra la partida (ni las
    /// alertas cadenciadas del canal D, que solo viven en su tick). Con
    /// <see cref="Buffered"/> activo, <see cref="Feed2"/> ENCOLA los ticks y
    /// <see cref="AdvanceTo"/> avanza el cursor al ritmo de simulación; los ticks
    /// presentados se entregan ordenados y sin saltos por
    /// <see cref="TryDequeuePresented"/>, de modo que el HUD reparte CADA tick.</para>
    /// </summary>
    public sealed class GameStreamPresenter
    {
        private readonly GameStreamParser _parser = new();
        private GameStreamParser.TickView? _prev;
        private GameStreamParser.TickView? _curr;

        // Colas compartidas con el hilo que bombea el stream: candado propio.
        private readonly object _gate = new();
        // Ticks recibidos y aún no presentados (solo en modo buffered).
        private readonly Queue<GameStreamParser.TickView> _pending = new();
        // Ticks ya presentados que la UI aún no ha repartido (siempre se llenan).
        private readonly Queue<GameStreamParser.TickView> _presented = new();

        /// <summary>Reproducción con buffer: el stream llega en ráfaga y se
        /// presenta al ritmo de <see cref="AdvanceTo"/>. Por defecto OFF — un
        /// consumidor que solo quiere el último tick conserva el comportamiento
        /// de siempre (útil para diagnósticos y para los tests de contrato).</summary>
        public bool Buffered { get; set; }

        /// <summary>Ticks recibidos y aún no presentados (0 si no hay buffer).</summary>
        public int BufferedTicks { get { lock (_gate) return _pending.Count; } }

        /// <summary>
        /// Tope de ticks en cola (F6). 0 = sin tope (tests y consumidores
        /// cortos). Con una partida de 2 h a 120× el CLI produce más rápido de
        /// lo que el reproductor consume: sin tope la cola crece durante una hora
        /// y se come la RAM. Al llegar al tope, el hilo del stream ESPERA —la
        /// tubería hace de contrapresión y el CLI baja a nuestro ritmo—.
        /// </summary>
        public int MaxPending { get; set; }

        private bool _closed;

        /// <summary>Despierta al hilo que espera por contrapresión (parada de Play).</summary>
        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Ticks presentados pendientes de reparto a la UI.</summary>
        public int PresentedCount { get { lock (_gate) return _presented.Count; } }

        public GameStreamParser.HeaderView? Header => _parser.Header;
        public string? FinalHash => _parser.FinalHash;
        public ulong FinalTick => _parser.FinalTick;
        /// <summary>F8: la línea `end` dijo `extinct:true` (la ronda se cerró por
        /// extinción, no por agotar el horizonte).</summary>
        public bool Extinct => _parser.Extinct;

        /// <summary>F11n: el informe de la glándula de la ronda que acaba de cerrar
        /// (null si la partida no pidió `--trail-report`). Lo publica el HUD al
        /// terminar; durante la partida no hay cifra que enseñar.</summary>
        public GameStreamParser.TrailReportView? Trail => _parser.Trail;

        /// <summary>Último TickView completo recibido (F4.2): el repartidor del
        /// HUD (tarjetas, toasts, inspector) lo consume una vez por tick.</summary>
        public GameStreamParser.TickView? CurrentTick => _curr;

        /// <summary>Consume una línea del stream (en orden).</summary>
        public void Feed(string line) => Feed2(line);

        /// <summary>Variante con retorno: devuelve el TickView si la línea trae un tick
        /// completo (null en cabecera/end) — para consumidores con hooks por tick.</summary>
        public GameStreamParser.TickView? Feed2(string line)
        {
            var view = _parser.ParseLine(line);
            if (view == null) return null;
            if (Buffered)
            {
                // La presentación la decide AdvanceTo; aquí solo se encola.
                lock (_gate)
                {
                    while (!_closed && MaxPending > 0 && _pending.Count >= MaxPending)
                        Monitor.Wait(_gate);
                    if (_closed) return view;
                    _pending.Enqueue(view);
                    Monitor.PulseAll(_gate);
                }
            }
            else
            {
                _prev = _curr;
                _curr = view;
                lock (_gate) _presented.Enqueue(view);
            }
            return view;
        }

        /// <summary>
        /// Avanza la reproducción hasta <paramref name="targetTick"/> (inclusive),
        /// presentando cada tick intermedio en orden. Devuelve true si el cursor
        /// cambió. No hace nada si <see cref="Buffered"/> está apagado.
        /// </summary>
        public bool AdvanceTo(ulong targetTick)
        {
            if (!Buffered) return false;
            bool changed = false;
            lock (_gate)
            {
                while (_pending.Count > 0 && _pending.Peek().Tick <= targetTick)
                {
                    var v = _pending.Dequeue();
                    _prev = _curr;
                    _curr = v;
                    _presented.Enqueue(v);
                    changed = true;
                }
                if (changed) Monitor.PulseAll(_gate); // hueco para el hilo del stream
            }
            return changed;
        }

        /// <summary>
        /// Saca el siguiente tick PRESENTADO para que la UI lo reparta. Drena la cola
        /// entera por frame: a velocidad alta un frame presenta varios ticks y perder
        /// uno perdería su alerta. Devuelve false cuando no queda ninguno.
        /// </summary>
        public bool TryDequeuePresented(out GameStreamParser.TickView? view)
        {
            lock (_gate)
            {
                if (_presented.Count == 0) { view = null; return false; }
                view = _presented.Dequeue();
                return true;
            }
        }

        /// <summary>
        /// Estado interpolado para render: poses entre prev y curr según
        /// <paramref name="t"/> ∈ [0,1); items y colonias del tick actual
        /// (los ítems no se interpolan: aparecen/desaparecen por eventos).
        /// </summary>
        public RenderState Sample(float t)
        {
            var state = new RenderState();
            var curr = _curr;
            if (curr == null) return state;

            state.Tick = curr.Tick;
            state.Colonies.AddRange(curr.Colonies);
            state.Events.AddRange(curr.Events);
            state.Metrics = curr.Metrics;
            state.Relay = curr.Relay;
            state.Activ = curr.Activ;
            state.Graph = curr.Graph;
            state.Items.AddRange(curr.Items);

            var prev = _prev;
            if (prev != null && prev.Tick + 1 == curr.Tick)
            {
                // Mapa id→pose del tick anterior para interpolar los que siguen vivos.
                var prevById = new Dictionary<uint, GameStreamParser.AntPose>();
                foreach (var a in prev.Ants) prevById[a.Id] = a;

                foreach (var a in curr.Ants)
                {
                    float x = a.X, y = a.Y, h = a.Heading;
                    if (prevById.TryGetValue(a.Id, out var p) && a.Alive && p.Alive)
                    {
                        x = Lerp(p.X, a.X, t);
                        y = Lerp(p.Y, a.Y, t);
                        h = LerpAngle(p.Heading, a.Heading, t);
                    }
                    state.Ants.Add(new GameStreamParser.AntPose(a.Id, a.ColonyId, x, y, h, a.HasLoad, a.Alive));
                }
            }
            else
            {
                state.Ants.AddRange(curr.Ants);
            }

            return state;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Interpolación angular por el camino corto (heading ∈ [-π, π]).</summary>
        private static float LerpAngle(float a, float b, float t)
        {
            float d = b - a;
            while (d > MathF.PI) d -= 2f * MathF.PI;
            while (d < -MathF.PI) d += 2f * MathF.PI;
            return a + d * t;
        }
    }

    /// <summary>Estado renderizable de un instante (el MonoBehaviour lo dibuja).</summary>
    public sealed class RenderState
    {
        public ulong Tick;
        public readonly List<GameStreamParser.AntPose> Ants = new();
        public readonly List<GameStreamParser.ItemView> Items = new();
        public readonly List<GameStreamParser.ColonyView> Colonies = new();
        public readonly List<GameStreamParser.EventView> Events = new();
        public GameStreamParser.MetricsView? Metrics;
        public GameStreamParser.RelayView? Relay;
        /// <summary>F5.0/F5.2c: activaciones base64 del canal F y su topología.</summary>
        public string? Activ;
        public GameStreamParser.GraphView? Graph;
    }
}
