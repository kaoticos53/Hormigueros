using System.Collections.Generic;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Panel de historial de comandos (F4.2, contrato HUD §4) — modelo PURO.
    /// Consume los eventos <c>CommandExecuted</c> (kind 11) del canal B y mantiene
    /// la auditoría de la partida: cada fila «t={tick} — {comando}», acumulados y
    /// el estado del botón «reproducir desde guardado». Sin UnityEngine: verificado
    /// headless en la suite. La UI nunca interpreta comandos: pinta lo que llega.
    /// </summary>
    public sealed class CommandHistoryModel
    {
        public const byte KindCommandExecuted = 11;

        /// <summary>Una fila del historial. AntId trae el kind del comando
        /// (contrato del canal B: X/Y = coords, AntId = SimCommandKind).</summary>
        public sealed class Row
        {
            public readonly ulong Tick;
            public readonly int ColonyId;
            public readonly byte CommandKind; // 0 DropFood · 1 SaveGame
            public readonly float X, Y;

            public Row(ulong tick, int colonyId, byte commandKind, float x, float y)
            { Tick = tick; ColonyId = colonyId; CommandKind = commandKind; X = x; Y = y; }

            public string Label => CommandKind switch
            {
                1 => "SaveGame → slot",
                _ => $"DropFood @ ({X:0}, {Y:0})",
            };
        }

        private readonly List<Row> _rows = new();

        /// <summary>Tope de filas retenidas (la auditoría v1 es acotada; el
        /// canal de verdad — .antlog — vive en el CLI).</summary>
        public const int MaxRows = 200;

        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>Total de comandos ejecutados desde el arranque del stream
        /// (aunque las filas antiguas salgan por el tope).</summary>
        public long TotalCommands { get; private set; }

        /// <summary>Ticks del último SaveGame observado (0 = ninguno) — habilita
        /// el botón «reproducir desde guardado» del contrato §4.</summary>
        public ulong LastSaveTick { get; private set; }

        /// <summary>Consume un tick: registra los CommandExecuted del canal B.</summary>
        public void Observe(GameStreamParser.TickView v)
        {
            foreach (var ev in v.Events)
            {
                if (ev.Kind != KindCommandExecuted) continue;
                TotalCommands++;
                var row = new Row(v.Tick, ev.ColonyId, (byte)ev.AntId, ev.X, ev.Y);
                _rows.Insert(0, row); // la más nueva arriba
                if (_rows.Count > MaxRows)
                    _rows.RemoveAt(_rows.Count - 1);
                if (row.CommandKind == 1) LastSaveTick = v.Tick;
            }
        }

        /// <summary>Render v1 del panel: filas «t={tick} — {comando}».</summary>
        public string Render()
        {
            var sb = new StringBuilder();
            sb.Append("comandos: ").Append(TotalCommands);
            if (LastSaveTick > 0) sb.Append(" · ✓ guardado en t=").Append(LastSaveTick);
            sb.AppendLine();
            foreach (var r in _rows)
                sb.Append("t=").Append(r.Tick).Append(" — ").Append(r.Label).AppendLine();
            return sb.ToString();
        }

        /// <summary>¿Hay partida guardada para el botón «reproducir desde guardado»?</summary>
        public bool CanReplayFromSave => LastSaveTick > 0;
    }
}
