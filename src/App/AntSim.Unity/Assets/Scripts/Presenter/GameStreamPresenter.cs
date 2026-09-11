using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Presenter puro (F4.1, sin UnityEngine): mantiene el último par de TickViews
    /// del stream y calcula el estado interpolado en t ∈ [0,1) — el render consume
    /// esto a su framerate, la simulación sigue a ticks fijos. El retraso de 1 tick
    /// de la arquitectura se materializa aquí: solo interpolamos VISTO.
    /// </summary>
    public sealed class GameStreamPresenter
    {
        private readonly GameStreamParser _parser = new();
        private GameStreamParser.TickView? _prev;
        private GameStreamParser.TickView? _curr;

        public GameStreamParser.HeaderView? Header => _parser.Header;
        public string? FinalHash => _parser.FinalHash;
        public ulong FinalTick => _parser.FinalTick;

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
            _prev = _curr;
            _curr = view;
            return view;
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
    }
}
