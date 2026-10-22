#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# pretrain-maze.sh — tirada CANÓNICA del pre-entrenamiento con obstáculos (F7).
#
# Entrena en headless (sin editor, sin gráficos) un pool MLP que aprende a
# forrajear en el laberinto del juego: la dificultad del terreno sube sola por
# competencia medida (descargas reales) y la capa oculta puede crecer 8→16→24
# cuando el aprendizaje se estanca en el terreno completo. Semilla fija ⇒ mismo
# `.antgenome` byte a byte (el SHA del archivo se imprime al final).
#
# Uso:
#   scripts/pretrain-maze.sh                 # la receta canónica (seed 7, pop 32, 600 gens)
#   POP=24 GENS=300 scripts/pretrain-maze.sh # tirada más corta (humo)
#   scripts/pretrain-maze.sh --quick         # 60 gens, para comprobar que el camino vive
#
# Entradas:  artifacts/pretrain-warm-v2.antgenome (warm-start; 19→8→6)
# Salidas:   artifacts/pretrain-maze.antgenome  (el pool entrenado)
#            artifacts/pretrain-maze.log        (una línea por generación +
#                                               dificultad, descargas y crecimiento)
#
# La partida normal usa este pool por defecto (scripts/play-game.sh): el mundo
# adopta su ancho de cerebro y la escena arranca ya entrenada.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"

SEED="${SEED:-7}"
POP="${POP:-32}"
GENS="${GENS:-600}"
HIDDEN="${HIDDEN:-8}"
CLASSIC="${CLASSIC:-5}"  # 1 de cada N generaciones en campo abierto (0 = solo laberinto)
WARM="${WARM:-$ROOT/artifacts/pretrain-warm-v2.antgenome}"
OUT="${OUT:-$ROOT/artifacts/pretrain-maze.antgenome}"
LOG="${LOG:-$ROOT/artifacts/pretrain-maze.log}"

QUICK=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --quick) QUICK=1; shift ;;
    --help|-h) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done
if [[ $QUICK -eq 1 ]]; then POP=8; GENS=60; fi

if [[ ! -f "$WARM" ]]; then
  echo "✗ falta el warm-start: $WARM" >&2
  echo "  (el pool de referencia vive versionado en artifacts/pretrain-warm-v2.antgenome)" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT")" "$(dirname "$LOG")"

echo "▶ Pre-entrenamiento con obstáculos: seed $SEED · pop $POP · $GENS gens · hidden $HIDDEN · clásica 1/$CLASSIC"
echo "  warm-start: $WARM"
echo "  pool:       $OUT"
echo "  log:        $LOG"

START=$(date +%s)
dotnet run --project "$CLI_PROJECT" -c Release -- --mode pretrain \
  --seed "$SEED" --pop "$POP" --generations "$GENS" \
  --obstacles --hidden "$HIDDEN" --grow --classic-every "$CLASSIC" \
  --warm-start "$WARM" --export "$OUT" 2>&1 | tee "$LOG"
END=$(date +%s)

# El archivo existe y es un .antgenome con hash de integridad: si el CLI falló a
# medias, el `set -e`+pipefail ya habría cortado aquí.
if [[ ! -s "$OUT" ]]; then
  echo "✗ el pool no se escribió: $OUT" >&2
  exit 1
fi

SHA="$(sha256sum "$OUT" | cut -d' ' -f1)"
GENS_RUN="$(grep -c '^gen ' "$LOG" || true)"
GROWS="$(grep -c '^grow ' "$LOG" || true)"
echo ""
echo "✓ tirada completa en $((END - START)) s · $GENS_RUN generaciones · $GROWS crecimiento(s)"
echo "  sha256 $SHA  $OUT"
