#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-atta-command.sh — CI: cuarto pin de regresión — la partida CANÓNICA
# Atta (F5.2a) reproduce su hash final fijado.
#
# La partida: colonia 0 Atta SEMBRADA con el pool del fixture trackeado
# (tests/fixtures/warm-v2.antgenome — transferencia validada en
# docs/fase5-2a-atta.md §4.1), colonia 1 Lasius sin sembrar, mundo 100 % hojas
# (--leaf-fraction 1.0). Ejercita el camino COMPLETO de la cortadora:
# hojas compuestas (F5.2a.1), hongo con digestión por el inflow (F5.2a.2),
# canales A/C con cuts/fungus/cutters (F5.2a.3) y --species/--leaf-fraction
# del CLI. Es el primer pin cuyo mundo DEPENDE de los campos nuevos — los
# otros tres pines (sin hojas, sin Atta) son la garantía de compatibilidad.
#
# Si el hash cambia, algo rompió la cadena de la cortadora (ítems
# compuestos, economía del hongo, telemetría de cutters, o la simulación).
# Si el cambio es INTENCIONAL (decisión de diseño documentada), actualiza
# el pin con --update y revisa el diff.
#
# Uso:
#   scripts/check-atta-command.sh              # modo CI
#   scripts/check-atta-command.sh --update     # actualiza el pin (cambio intencional)
#   scripts/check-atta-command.sh --hash XXX…  # compara contra otro hash (tests del script)
#   scripts/check-atta-command.sh --quiet      # solo el veredicto (para logs de CI)
#
# Requisitos: dotnet SDK.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"
POOL="$ROOT/tests/fixtures/warm-v2.antgenome"

# ── Parámetros de la partida Atta canónica (NO tocar sin decisión consciente) ─
SEED=42
GRID=96
COLONIES=2
TICKS=7200
FRAME_EVERY=3600        # 2 frames: t3600 (hongo aún 0) y t7200 (hongo acumulado)
LEAF_FRACTION=1.0
SPECIES="atta,lasius"

EXPECTED_FILE="$ROOT/scripts/atta-command.expected"
if [[ ! -f "$EXPECTED_FILE" ]]; then
  echo "✗ ERROR: falta $EXPECTED_FILE (el hash fijado de la partida Atta)" >&2
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

log "▶ Partida Atta canónica (7200 ticks, hojas 100 %, atta+lasius, warm-v2 en colonia 0)…"
ACTUAL="$(dotnet run --project "$CLI_PROJECT" -c Debug -- --mode game \
  --seed "$SEED" --ticks "$TICKS" --grid "$GRID" --colonies "$COLONIES" \
  --frame-every "$FRAME_EVERY" \
  --leaf-fraction "$LEAF_FRACTION" \
  --species "$SPECIES" \
  --seed-pool "$POOL" 2>/dev/null | tail -n 1 \
  | sed -n 's/.*"hash":"\([0-9a-f]*\)".*/\1/p')"

if [[ -z "$ACTUAL" ]]; then
  echo "✗ ERROR: el stream Atta no terminó con hash" >&2
  exit 1
fi

if [[ "$ACTUAL" == "$EXPECTED" ]]; then
  log "✓ hash Atta (especies + hojas + hongo) intacto: $ACTUAL"
  exit 0
fi

if [[ $UPDATE -eq 1 ]]; then
  printf '%s\n' "$ACTUAL" > "$EXPECTED_FILE"
  log "✓ pin Atta actualizado a $ACTUAL (revisa el diff)"
  exit 0
fi

echo "✗ FALLO: el hash de la partida Atta canónica CAMBIÓ" >&2
echo "    esperado: $EXPECTED" >&2
echo "    actual:   $ACTUAL" >&2
echo "    Si el cambio es intencional (decisión de diseño documentada), corre" >&2
echo "    scripts/check-atta-command.sh --update y revisa el diff en el PR." >&2
exit 1
