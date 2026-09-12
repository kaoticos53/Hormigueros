#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-replay-command.sh — CI: la partida CON PLAN DE DROPS del smoke e2e
# reproduce su hash final fijado.
#
# Extiende el pin del fixture canónico (check-stream-fixture.sh) al camino de
# INYECCIÓN DE COMANDOS (F4.0/F4.4): la misma línea de comandos con
# --seed-pool y tres --drop debe producir SIEMPRE el mismo mundo. Si el hash
# cambia, algo rompió el determinismo del camino de intervención del jugador
# (encolado de comandos, aplicación en el punto canónico del Step, o la
# simulación en sí). Si el cambio es INTENCIONAL (decisión de diseño del
# mundo documentada), actualiza el hash fijado con --update y revisa el diff.
#
# Uso:
#   scripts/check-replay-command.sh              # modo CI: compara con el hash fijado
#   scripts/check-replay-command.sh --update     # actualiza el hash fijado (cambio intencional)
#   scripts/check-replay-command.sh --hash XXX…  # compara contra otro hash (tests del script)
#   scripts/check-replay-command.sh --quiet      # solo el veredicto (para logs de CI)
#
# Requisitos: dotnet SDK. El pool del fixture vive en
# tests/fixtures/warm-v2.antgenome (TRACKED — los .antgenome de artifacts/
# son ejecución y no viajan con el repo; la procedencia del fixture está en
# tests/fixtures/README.md).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"
POOL="$ROOT/tests/fixtures/warm-v2.antgenome"

# ── Parámetros de la partida del smoke e2e (NO tocar sin decisión consciente) ─
SEED=42
TICKS=3000
GRID=96
COLONIES=2
FRAME_EVERY=30
DROPS=("--drop" "1500:400:300" "--drop" "1600:500:250" "--drop" "1700:200:450")

# ── Hash esperado: el del smoke e2e (docs/fase4-smoke-e2e.md) ─────────────────
EXPECTED_FILE="$ROOT/scripts/replay-command.expected"
if [[ ! -f "$EXPECTED_FILE" ]]; then
  echo "✗ ERROR: falta $EXPECTED_FILE (el hash fijado del replay con drops)" >&2
  exit 1
fi
EXPECTED_HASH="$(tr -d '[:space:]' < "$EXPECTED_FILE")"
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
    --hash) EXPECTED_HASH="$2"; shift 2 ;;
    --help|-h) sed -n '2,32p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

# ── 1. Ejecutar la partida con el plan de drops ───────────────────────────────
log "▶ Ejecutando la partida del smoke e2e (seed $SEED, grid $GRID, $TICKS ticks, 3 drops)…"
STREAM="$(dotnet run --project "$CLI_PROJECT" -c Debug -- --mode game \
  --seed "$SEED" --ticks "$TICKS" --grid "$GRID" --colonies "$COLONIES" \
  --frame-every "$FRAME_EVERY" \
  --seed-pool "$POOL" \
  "${DROPS[@]}" 2>/dev/null)"

# ── 2. Extraer el hash final ─────────────────────────────────────────────────
ACTUAL_HASH="$(printf '%s\n' "$STREAM" | tail -n 1 \
  | sed -n 's/.*"hash":"\([0-9a-f]*\)".*/\1/p')"
if [[ -z "$ACTUAL_HASH" ]]; then
  echo "✗ ERROR: el stream no terminó con hash (línea end ausente o malformada)" >&2
  exit 1
fi

# ── 3. Comparar ───────────────────────────────────────────────────────────────
if [[ "$ACTUAL_HASH" == "$EXPECTED_HASH" ]]; then
  log "✓ hash del replay con drops intacto: $ACTUAL_HASH"
  exit 0
fi

if [[ $UPDATE -eq 1 ]]; then
  printf '%s\n' "$ACTUAL_HASH" > "$EXPECTED_FILE"
  echo "✓ EXPECTED_HASH actualizado a $ACTUAL_HASH (revisa el diff: cambia el pool, los drops o el mundo)"
  exit 0
fi

echo "✗ FALLO: el hash del replay con drops CAMBIÓ — el determinismo de la intervención se rompió" >&2
echo "    esperado: $EXPECTED_HASH" >&2
echo "    actual:   $ACTUAL_HASH" >&2
echo "    Si el cambio es intencional (decisión de diseño documentada), corre" >&2
echo "    scripts/check-replay-command.sh --update y revisa el diff en el PR." >&2
exit 1
