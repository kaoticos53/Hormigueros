#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-invasion-command.sh — CI: quinto pin de regresión — la partida
# CANÓNICA de INVASIÓN (F5.2b) reproduce su hash final fijado.
#
# La partida: colonia 0 Lasius (PRESA, pacífica — ContactRadius 0) y colonia
# 1 Eciton (LEGIONARIA, SEMBRADA con el pool del fixture trackeado
# tests/fixtures/warm-v2.antgenome — transferencia Lasius→Eciton validada en
# docs/fase5-2b-eciton.md §8quater). Ejercita el camino COMPLETO del
# saqueo: sensor de presa (F5.2b.2), strike/robo/botín (F5.2b.1, constantes
# V6 de la calibración §8septies), eventos 17-19 en canal B y bloque raids
# en canal C (F5.2b.3). Es el primer pin cuyo mundo DEPENDE del combate.
#
# Si el hash cambia, algo rompió la cadena del saqueo (sensor, combate,
# economía V6, telemetría de raids, o la simulación). Si el cambio es
# INTENCIONAL (decisión de diseño documentada), actualiza el pin con
# --update y revisa el diff.
#
# Uso:
#   scripts/check-invasion-command.sh              # modo CI
#   scripts/check-invasion-command.sh --update     # actualiza el pin (cambio intencional)
#   scripts/check-invasion-command.sh --hash XXX…  # compara contra otro hash (tests del script)
#   scripts/check-invasion-command.sh --quiet      # solo el veredicto (para logs de CI)
#
# Requisitos: dotnet SDK.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"
POOL="$ROOT/tests/fixtures/warm-v2.antgenome"

# ── Parámetros de la partida de invasión canónica (NO tocar sin decisión consciente) ─
SEED=42
GRID=96
COLONIES=2
TICKS=7200
FRAME_EVERY=30          # cada tick de telemetría (el bloque raids es acumulado)
SPECIES="lasius,eciton" # 0 = presa pacífica · 1 = legionaria sembrada

EXPECTED_FILE="$ROOT/scripts/invasion-command.expected"
if [[ ! -f "$EXPECTED_FILE" ]]; then
  echo "✗ ERROR: falta $EXPECTED_FILE (el hash fijado de la partida de invasión)" >&2
  exit 1
fi
EXPECTED="$(tr -d '[:space:]' < "$EXPECTED_FILE")"
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
    --hash) EXPECTED="$2"; shift 2 ;;
    --help|-h) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

log "▶ Partida de invasión canónica (7200 ticks, lasius+eciton, warm-v2 en colonia 1)…"
ACTUAL="$(dotnet run --project "$CLI_PROJECT" -c Debug -- --mode game \
  --seed "$SEED" --ticks "$TICKS" --grid "$GRID" --colonies "$COLONIES" \
  --frame-every "$FRAME_EVERY" \
  --species "$SPECIES" \
  --seed-pool "$POOL" 2>/dev/null | tail -n 1 \
  | sed -n 's/.*"hash":"\([0-9a-f]*\)".*/\1/p')"

if [[ -z "$ACTUAL" ]]; then
  echo "✗ ERROR: el stream de invasión no terminó con hash" >&2
  exit 1
fi

if [[ "$ACTUAL" == "$EXPECTED" ]]; then
  log "✓ hash invasión (lasius+eciton + saqueo V6) intacto: $ACTUAL"
  exit 0
fi

if [[ $UPDATE -eq 1 ]]; then
  printf '%s\n' "$ACTUAL" > "$EXPECTED_FILE"
  log "✓ pin invasión actualizado a $ACTUAL (revisa el diff)"
  exit 0
fi

echo "✗ FALLO: el hash de la partida de invasión canónica CAMBIÓ" >&2
echo "    esperado: $EXPECTED" >&2
echo "    actual:   $ACTUAL" >&2
echo "    Si el cambio es intencional (decisión de diseño documentada), corre" >&2
echo "    scripts/check-invasion-command.sh --update y revisa el diff en el PR." >&2
exit 1
