#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-stream-fixture.sh — CI: el hash final del stream canónico no cambia.
#
# Regenera el stream de la partida canónica (la misma que
# artifacts/stream-fixture.jsonl: seed 42, grid 96, 2 colonias, 7200 ticks,
# frame-every 1) y comprueba que su hash final es el fijado abajo. Si cambia,
# ALGO ROMPIÓ EL DETERMINISMO DEL MUNDO (o se alteró la simulación de forma
# observable): el stream es el contrato de la UI de Unity, y cualquier deriva
# sin decisión consciente es una regresión. Si el cambio es INTENCIONAL
# (p. ej. una decisión de diseño del mundo documentada), actualiza el hash
# fijado con --update, revisa el diff en el PR y actualiza también el
# fixture de artifacts si procede.
#
# Uso:
#   scripts/check-stream-fixture.sh              # modo CI: compara con el hash fijado
#   scripts/check-stream-fixture.sh --update     # actualiza el hash fijado (cambio intencional)
#   scripts/check-stream-fixture.sh --hash XXX…  # compara contra otro hash (para tests del propio script)
#   scripts/check-stream-fixture.sh --quiet      # solo el veredicto (para logs de CI)
#
# Requisitos: dotnet SDK (compila el CLI). Determinista: la misma semilla y
# parámetros producen el mismo hash en cualquier máquina (misma versión del repo).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"

# ── Parámetros de la partida canónica (NO tocar sin decisión consciente) ─────
SEED=42
TICKS=7200
GRID=96
COLONIES=2
FRAME_EVERY=1

# ── Hash esperado: el del contrato actual del mundo ──────────────────────────
# Vive en scripts/stream-fixture.expected (trackeado): --update lo reescribe
# (otro archivo, no este script en ejecución — bash lo lee por offset y
# reescribirlo debajo de sí mismo es indeterminado).
EXPECTED_FILE="$ROOT/scripts/stream-fixture.expected"
if [[ ! -f "$EXPECTED_FILE" ]]; then
  echo "✗ ERROR: falta $EXPECTED_FILE (el hash fijado del fixture)" >&2
  exit 1
fi
EXPECTED_HASH="$(tr -d '[:space:]' < "$EXPECTED_FILE")"

QUIET=0
UPDATE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --quiet) QUIET=1; shift ;;
    --update) UPDATE=1; shift ;;
    --hash) EXPECTED_HASH="$2"; shift 2 ;;
    --help|-h) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

# ── 1. Regenerar el stream ───────────────────────────────────────────────────
log "▶ Regenerando el stream canónico (seed $SEED, grid $GRID, $TICKS ticks)…"
STREAM="$(dotnet run --project "$CLI_PROJECT" -c Debug -- --mode game \
  --seed "$SEED" --ticks "$TICKS" --grid "$GRID" --colonies "$COLONIES" \
  --frame-every "$FRAME_EVERY" 2>/dev/null)"

# ── 2. Extraer el hash final (línea {"end":true,…,"hash":"…"}) ───────────────
ACTUAL_HASH="$(printf '%s\n' "$STREAM" | tail -n 1 \
  | sed -n 's/.*"hash":"\([0-9a-f]*\)".*/\1/p')"
if [[ -z "$ACTUAL_HASH" ]]; then
  echo "✗ ERROR: el stream no terminó con hash (línea end ausente o malformada)" >&2
  exit 1
fi

# ── 3. Comparar ──────────────────────────────────────────────────────────────
if [[ "$ACTUAL_HASH" == "$EXPECTED_HASH" ]]; then
  log "✓ hash del stream canónico intacto: $ACTUAL_HASH"
  exit 0
fi

if [[ $UPDATE -eq 1 ]]; then
  # Actualiza el hash fijado (cambio intencional, revisable en el PR): escribe
  # en stream-fixture.expected, NO en este script en ejecución.
  printf '%s\n' "$ACTUAL_HASH" > "$EXPECTED_FILE"
  echo "✓ EXPECTED_HASH actualizado a $ACTUAL_HASH (revisa el diff y regenera artifacts/stream-fixture.jsonl)"
  exit 0
fi

echo "✗ FALLO: el hash del stream canónico CAMBIÓ — el determinismo del mundo se rompió" >&2
echo "    esperado: $EXPECTED_HASH" >&2
echo "    actual:   $ACTUAL_HASH" >&2
echo "    Si el cambio es intencional (decisión de diseño documentada), corre" >&2
echo "    scripts/check-stream-fixture.sh --update y revisa el diff en el PR." >&2
exit 1
