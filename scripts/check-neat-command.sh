#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-neat-command.sh — CI: 6º pin de determinismo (F5.2c rodaja 7, cierre).
#
# La partida canónica del criterio de cierre del plan §3: invasión Eciton vs
# colonia presa SEMBRADA CON POOL NEAT PROPIO (.antgenome v2, grafos). Ejercita
# el camino completo del mundo NEAT:
#
#   pretrain --neat (v2 writer canonizado) → --seed-pool v2 (lector v2 +
#   SeedPoolFromNeatGenomes: fundadoras con cerebro de grafo) → mundo con
#   especies → hash canónico.
#
# Si el hash cambia, ALGO rompió el determinismo del mundo NEAT o se alteró la
# simulación de forma observable. Regenerar SOLO como cambio de mundo intencional:
#
#   bash scripts/check-neat-command.sh --update
#
# y verificar en Windows Y Linux antes de commitear (misma disciplina que los
# otros pines; ver el comentario en ci.yml). Dependencia de CanonMath: igual
# que los demás pines — el hash solo reproduce con la matemática canónica.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"
POOL="$ROOT/tests/fixtures/pretrain-neat.antgenome"

# ── Parámetros de la partida NEAT canónica (NO tocar sin decisión consciente) ─
SEED=42
GRID=96
COLONIES=2
TICKS=7200
FRAME_EVERY=30
SPECIES="lasius,eciton"  # 0 = presa con pool NEAT sembrado · 1 = legionaria

EXPECTED_FILE="$ROOT/scripts/neat-command.expected"
if [[ ! -f "$EXPECTED_FILE" ]]; then
  echo "✗ ERROR: falta $EXPECTED_FILE (el hash fijado de la partida NEAT)" >&2
  exit 1
fi
EXPECTED="$(tr -d '[:space:]' < "$EXPECTED_FILE")"
if [[ ! -f "$POOL" ]]; then
  echo "✗ ERROR: falta $POOL (el pool NEAT del fixture, trackeado en git)" >&2
  exit 1
fi

QUIET=0
UPDATE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --quiet) QUIET=1; shift ;;
    --update) UPDATE=1; shift ;;
    --hash) EXPECTED="$2"; shift 2 ;;
    --help|-h) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

log "▶ Partida NEAT canónica (7200 ticks, lasius+eciton, pool v2 en colonia 0)…"
ACTUAL="$(dotnet run --project "$CLI_PROJECT" -c Debug -- --mode game \
  --seed "$SEED" --ticks "$TICKS" --grid "$GRID" --colonies "$COLONIES" \
  --frame-every "$FRAME_EVERY" \
  --species "$SPECIES" \
  --seed-pool "$POOL" 2>/dev/null | tail -n 1 \
  | sed -n 's/.*"hash":"\([0-9a-f]*\)".*/\1/p')"

if [[ -z "$ACTUAL" ]]; then
  echo "✗ ERROR: el stream NEAT no terminó con hash" >&2
  exit 1
fi

if [[ $UPDATE -eq 1 ]]; then
  printf '%s\n' "$ACTUAL" > "$EXPECTED_FILE"
  log "✓ esperado actualizado: $ACTUAL"
  exit 0
fi

if [[ "$ACTUAL" == "$EXPECTED" ]]; then
  log "✓ hash NEAT (pool v2 sembrado + invasión) intacto: $ACTUAL"
else
  echo "✗ DIVERGENCIA en la partida NEAT canónica:" >&2
  echo "    esperado: $EXPECTED" >&2
  echo "    actual:   $ACTUAL" >&2
  echo "  Si el cambio de mundo es INTENCIONAL: regenerar con --update y" >&2
  echo "  verificar en Windows Y Linux (ver comentario de pines en ci.yml)." >&2
  exit 1
fi
