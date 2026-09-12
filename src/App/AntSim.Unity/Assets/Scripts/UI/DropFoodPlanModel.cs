using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Plan de intervención del jugador (F4.4, contrato HUD §4 «DropFood con
    /// cuota»): el click-to-place marca (tick, x, y) sobre el mundo y este
    /// modelo PURO los acumula, valida y convierte en argumentos CLI
    /// (<c>--drop tick:x:y</c>) para relanzar la MISMA partida sembrada con el
    /// plan horneado. La UI nunca implementa la inyección: eso es el oráculo
    /// del Core (mismos comandos con tick ⇒ mundo reproducible, F4.0).
    /// </summary>
    public sealed class DropFoodPlanModel
    {
        /// <summary>Máximo de drops por partida (cuota dura del contrato §4).</summary>
        public const int MaxDrops = 5;

        /// <summary>Marca de un drop: tick de inyección + posición en mundo.</summary>
        public readonly struct DropMark
        {
            public DropMark(ulong tick, float x, float y)
            {
                Tick = tick; X = x; Y = y;
            }
            public ulong Tick { get; }
            public float X { get; }
            public float Y { get; }
        }

        private readonly List<DropMark> _marks = new();

        /// <summary>Marcas acumuladas, en orden de inserción.</summary>
        public IReadOnlyList<DropMark> Marks => _marks;

        /// <summary>Tick del stream en el momento de marcar (el handler lo pasa).</summary>
        public ulong CurrentTick { get; private set; }

        /// <summary>Sincroniza el tick actual con el stream (una vez por tick).</summary>
        public void ObserveTick(ulong tick) => CurrentTick = tick;

        /// <summary>
        /// Registra una marca de drop en el tick indicado. Valida contra el
        /// mundo — <paramref name="worldSize"/> va en UNIDADES (grid ×
        /// <see cref="Streaming.WorldUnits.PerCell"/>), no en celdas, porque es el
        /// espacio en el que viajan x/y y el que espera el CLI (<c>--drop tick:x:y</c>)
        /// — y exige tick futuro (la causalidad de F4.0 es tick-cmd ⇒ efecto).
        /// Devuelve el motivo del rechazo o null si se aceptó.
        /// </summary>
        public string? TryPlace(ulong tick, float x, float y, float worldSize)
        {
            if (_marks.Count >= MaxDrops)
                return $"cuota agotada ({MaxDrops} drops por partida)";
            if (x < 0f || y < 0f || x >= worldSize || y >= worldSize)
                return "fuera del mundo";
            if (tick <= CurrentTick)
                return "el drop debe ser futuro";
            _marks.Add(new DropMark(tick, x, y));
            return null;
        }

        /// <summary>Desmarca la última marca (click de arrepentimiento).</summary>
        public bool Undo()
        {
            if (_marks.Count == 0) return false;
            _marks.RemoveAt(_marks.Count - 1);
            return true;
        }

        /// <summary>Vacía el plan (nueva partida).</summary>
        public void Clear() => _marks.Clear();

        /// <summary>Argumentos CLI con el plan completo: p. ej. --drop 300:350.5:400.25.
        /// Vacío si no hay marcas — el relanzamiento entonces es idéntico.</summary>
        public IReadOnlyList<string> BuildCliArgs()
        {
            var args = new List<string>(_marks.Count * 2);
            foreach (var m in _marks)
            {
                args.Add("--drop");
                // C# 9 (Unity): string.Create(IFormatProvider, …) es .NET 6+;
                // formateo invariante explícito equivalente.
                args.Add(m.Tick.ToString(CultureInfo.InvariantCulture) + ":"
                    + Round(m.X).ToString("0.##", CultureInfo.InvariantCulture) + ":"
                    + Round(m.Y).ToString("0.##", CultureInfo.InvariantCulture));
            }
            return args;
        }

        /// <summary>Resumen para el HUD: «3/5 · t300 (351, 400) · t450 (…)».</summary>
        public string RenderSummary()
        {
            if (_marks.Count == 0) return "drops: 0/" + MaxDrops;
            var sb = new StringBuilder();
            sb.Append("drops: ").Append(_marks.Count).Append('/').Append(MaxDrops);
            foreach (var m in _marks)
            {
                sb.Append(" · t").Append(m.Tick.ToString(CultureInfo.InvariantCulture))
                  .Append(" (").Append(Round(m.X).ToString("0", CultureInfo.InvariantCulture))
                  .Append(',').Append(Round(m.Y).ToString("0", CultureInfo.InvariantCulture)).Append(')');
            }
            return sb.ToString();
        }

        private static float Round(float v) => MathF.Round(v * 100f) / 100f;
    }
}
