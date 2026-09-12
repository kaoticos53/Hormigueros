#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-replay-command.sh — CI: el bucle de juego completo (sembrar ·
# intervenir · relevo) reproduce sus hashes finales fijados.
#
# Dos fases, ambas con el pool del fixture trackeado
# tests/fixtures/warm-v2.antgenome (los .antgenome de artifacts/ son de
# ejecución y no viajan con el repo; procedencia en tests/fixtures/README.md):
#
#   Fase 3000 (intervención): partida con plan de 3 --drop del smoke e2e —
#     pin 816e280c… (scripts/replay-command.expected).
#   Fase 6000 (relevo): misma partida extendida a 6000 ticks — la colonia 0
#     sembrada completa su primera descarga (t3950, evento Unload) y el
#     semáforo del canal D pasa a ámbar por RelayVerdict; pin 87fbc8ed…
#     (scripts/replay-command-6k.expected).
#
# Si un hash cambia, algo rompió el determinismo del camino de intervención
# o del relevo (encolado de comandos, RelayTracker/RelayVerdict, o la
# simulación en sí). Si el cambio es INTENCIONAL (decisión de diseño del
# mundo documentada), actualiza los pines con --update y revisa el diff.
#
# Uso:
#   scripts/check-replay-command.sh              # modo CI: ambas fases
#   scripts/check-replay-command.sh --update     # actualiza los pines (cambio intencional)
#   scripts/check-replay-command.sh --hash XXX…  # compara contra otro hash (tests del script)
#   scripts/check-replay-command.sh --quiet      # solo el veredicto (para logs de CI)
#
# Requisitos: dotnet SDK.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"
POOL="$ROOT/tests/fixtures/warm-v2.antgenome"

# ── Parámetros del smoke e2e (NO tocar sin decisión consciente) ───────────────
SEED=42
GRID=96
COLONIES=2
FRAME_EVERY=30
DROPS=("--drop" "1500:400:300" "--drop" "1600:500:250" "--drop" "1700:200:450")
TICKS_SHORT=3000
TICKS_LONG=6000

EXPECTED_SHORT_FILE="$ROOT/scripts/replay-command.expected"
EXPECTED_LONG_FILE="$ROOT/scripts/replay-command-6k.expected"
for f in "$EXPECTED_SHORT_FILE" "$EXPECTED_LONG_FILE"; do
  if [[ ! -f "$f" ]]; then
    echo "✗ ERROR: falta $f (el hash fijado del replay)" >&2
    exit 1
  fi
done
EXPECTED_SHORT="$(tr -d '[:space:]' < "$EXPECTED_SHORT_FILE")"
EXPECTED_LONG="$(tr -d '[:space:]' < "$EXPECTED_LONG_FILE")"
if [[ ! -f "$POOL" ]]; then
  echo "✗ ERROR: falta $POOL (el pool del fixture, trackeado en git)" >&2
  exit 1
fi

QUIET=0
UPDATE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --quiet) QUIET=1; shift ;;
    --update) UPDATE=1; shift ;;
    --hash) EXPECTED_SHORT="$2"; EXPECTED_LONG="$2"; shift 2 ;;
    --help|-h) sed -n '2,34p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

run_game() {
  local ticks="$1"
  dotnet run --project "$CLI_PROJECT" -c Debug -- --mode game \
    --seed "$SEED" --ticks "$ticks" --grid "$GRID" --colonies "$COLONIES" \
    --frame-every "$FRAME_EVERY" \
    --seed-pool "$POOL" \
    "${DROPS[@]}" 2>/dev/null | tail -n 1 \
    | sed -n 's/.*"hash":"\([0-9a-f]*\)".*/\1/p'
}

# ── Fase 1: 3000 ticks (intervención) ─────────────────────────────────────────
log "▶ Fase 3000 (intervención): partida con 3 drops…"
ACTUAL_SHORT="$(run_game "$TICKS_SHORT")"
if [[ -z "$ACTUAL_SHORT" ]]; then
  echo "✗ ERROR: el stream de 3000 ticks no terminó con hash" >&2
  exit 1
fi

# ── Fase 2: 6000 ticks (relevo: first unload + semáforo) ─────────────────────
log "▶ Fase 6000 (relevo): misma partida extendida — first unload t3950 y semáforo…"
ACTUAL_LONG="$(run_game "$TICKS_LONG")"
if [[ -z "$ACTUAL_LONG" ]]; then
  echo "✗ ERROR: el stream de 6000 ticks no terminó con hash" >&2
  exit 1
fi

# ── Comparar ──────────────────────────────────────────────────────────────────
FAIL=0
if [[ "$ACTUAL_SHORT" == "$EXPECTED_SHORT" ]]; then
  log "✓ hash 3000 (intervención) intacto: $ACTUAL_SHORT"
else
  FAIL=1
  if [[ $UPDATE -eq 1 ]]; then
    printf '%s\n' "$ACTUAL_SHORT" > "$EXPECTED_SHORT_FILE"
    log "✓ pin 3000 actualizado a $ACTUAL_SHORT (revisa el diff)"
  else
    echo "✗ FALLO: el hash del replay con drops (3000) CAMBIÓ" >&2
    echo "    esperado: $EXPECTED_SHORT" >&2
    echo "    actual:   $ACTUAL_SHORT" >&2
  fi
fi

if [[ "$ACTUAL_LONG" == "$EXPECTED_LONG" ]]; then
  log "✓ hash 6000 (relevo) intacto: $ACTUAL_LONG"
else
  FAIL=1
  if [[ $UPDATE -eq 1 ]]; then
    printf '%s\n' "$ACTUAL_LONG" > "$EXPECTED_LONG_FILE"
    log "✓ pin 6000 actualizado a $ACTUAL_LONG (revisa el diff)"
  else
    echo "✗ FALLO: el hash del replay extendido (6000, relevo) CAMBIÓ" >&2
    echo "    esperado: $EXPECTED_LONG" >&2
    echo "    actual:   $ACTUAL_LONG" >&2
  fi
fi

if [[ $FAIL -eq 0 ]]; then
  exit 0
fi

echo "    Si el cambio es intencional (decisión de diseño documentada), corre" >&2
echo "    scripts/check-replay-command.sh --update y revisa el diff en el PR." >&2
exit 1
