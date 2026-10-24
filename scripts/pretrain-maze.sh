#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# pretrain-maze.sh — tirada CANÓNICA del pre-entrenamiento con obstáculos (F7).
#
# Entrena en headless (sin editor, sin gráficos) un pool MLP que forrajea en el
# laberinto del juego Y sigue forrajeando en campo abierto. La dificultad del
# terreno sube sola por competencia medida (descargas reales) y la capa oculta
# crece 8→16→24 cuando el aprendizaje se estanca en el terreno completo. Semilla
# fija ⇒ mismo `.antgenome` byte a byte (el SHA del archivo se imprime al final).
#
# La receta tiene DOS fases, porque midiendo se vio que el orden importa:
#
#   1. CUENCA (`--classic-every 5`): el currículo de obstáculos con una de cada
#      cinco generaciones en campo abierto. Deja la política en el régimen de
#      forrajeo clásico (57 descargas en el laberinto, 172 sin laberinto).
#   2. LABERINTO + COLA (`--classic-every 0 --classic-tail 60`): refina el
#      laberinto sin mantenimiento y cierra con 60 generaciones clásicas en el
#      régimen de la PARTIDA (comida en todo el mapa, horizonte largo). El pool
#      final es esa última evaluación.
#
# Medido (partida canónica, 4 semillas): 75 descargas con `--maze` (frente a 37
# del pool de referencia) y 335 sin laberinto (frente a 242). La fase 1 sola
# conserva el ciclo clásico pero no lo recupera; la cola sola no basta si la
# política ya salió de la cuenca clásica — de ahí las dos fases.
#
# Uso:
#   scripts/pretrain-maze.sh                 # la receta canónica (seed 7, pop 32, 600+600 gens)
#   POP=24 GENS=300 scripts/pretrain-maze.sh # tirada más corta (humo)
#   scripts/pretrain-maze.sh --quick         # 60 gens por fase, para comprobar que el camino vive
#
# Entradas:  artifacts/pretrain-warm-v2.antgenome (warm-start; 19→8→6)
# Salidas:   artifacts/pretrain-maze.antgenome       (el pool entrenado)
#            artifacts/pretrain-maze-base.antgenome  (el intermedio de la fase 1)
#            artifacts/pretrain-maze.log             (una línea por generación +
#                                                     dificultad, descargas y crecimiento)
#
# La partida normal usa el pool de la TIRADA CORTA de esta misma receta
# (GENS=300, el de por defecto): el mundo adopta su ancho de cerebro y la escena
# arranca ya entrenada.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_PROJECT="$ROOT/src/Tools/AntSim.Cli"

SEED="${SEED:-7}"
POP="${POP:-32}"
GENS="${GENS:-600}"
HIDDEN="${HIDDEN:-8}"
CLASSIC="${CLASSIC:-5}"   # fase 1: 1 de cada N generaciones en campo abierto (0 = ninguna)
TAIL="${TAIL:-60}"        # fase 2: generaciones clásicas de cola al final
WARM="${WARM:-$ROOT/artifacts/pretrain-warm-v2.antgenome}"
OUT="${OUT:-$ROOT/artifacts/pretrain-maze.antgenome}"
LOG="${LOG:-$ROOT/artifacts/pretrain-maze.log}"
BASE_OUT="${BASE_OUT:-$(dirname "$OUT")/pretrain-maze-base.antgenome}"

QUICK=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --quick) QUICK=1; shift ;;
    --help|-h) sed -n '2,40p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done
if [[ $QUICK -eq 1 ]]; then POP=8; GENS=60; fi

if [[ ! -f "$WARM" ]]; then
  echo "✗ falta el warm-start: $WARM" >&2
  echo "  (el pool de referencia vive versionado en tests/fixtures/warm-v2.antgenome;" >&2
  echo "   cópialo a artifacts/ o pasa WARM=<ruta>)" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT")" "$(dirname "$LOG")" "$(dirname "$BASE_OUT")"

echo "▶ Fase 1/2 — cuenca clásica: seed $SEED · pop $POP · $GENS gens · hidden $HIDDEN · clásica 1/$CLASSIC"
echo "  warm-start: $WARM"
echo "  base:       $BASE_OUT"
START=$(date +%s)
dotnet run --project "$CLI_PROJECT" -c Release -- --mode pretrain \
  --seed "$SEED" --pop "$POP" --generations "$GENS" \
  --obstacles --hidden "$HIDDEN" --grow --classic-every "$CLASSIC" --classic-tail 0 \
  --warm-start "$WARM" --export "$BASE_OUT" 2>&1 | tee "$LOG"

echo ""
echo "▶ Fase 2/2 — laberinto + cola clásica: $GENS gens · cola $TAIL"
dotnet run --project "$CLI_PROJECT" -c Release -- --mode pretrain \
  --seed "$SEED" --pop "$POP" --generations "$GENS" \
  --obstacles --hidden "$HIDDEN" --grow --classic-every 0 --classic-tail "$TAIL" \
  --warm-start "$BASE_OUT" --export "$OUT" 2>&1 | tee -a "$LOG"
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
