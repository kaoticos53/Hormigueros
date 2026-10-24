#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# play-game.sh — Lanza Unity con el juego YA configurado y entrando en Play.
#
# La configuración no se teclea en el inspector: este script la escribe en un
# archivo y Unity la aplica al montar la escena (entrada del lanzador del
# bootstrapper), con geometría incluida — grid y colonias dimensionan también
# el suelo, la cámara y los marcadores de nido.
#
# USO:
#   bash scripts/play-game.sh                                warm-v2, grid 96
#   bash scripts/play-game.sh --pool pretrain-neat
#   bash scripts/play-game.sh --grid 256 --ticks 12000
#   bash scripts/play-game.sh --species lasius,eciton --speed 60
#   bash scripts/play-game.sh --clusters 2 --cluster-size 12:16   montones grandes
#   bash scripts/play-game.sh --trail-half-life 60 --trail-follow 0  rastro efímero y ciego
#   bash scripts/play-game.sh --dry-run                      no abre Unity
#
# PERILLAS DEL RECLUTAMIENTO (F8): --clusters, --cluster-size MIN:MAX,
# --cluster-radius, --trail-deposit, --trail-half-life y --trail-follow dejan
# comparar configuraciones sin tocar código. Los defaults son los del mundo.
#
# CONTROLES EN JUEGO:
#   Click     → inspeccionar hormiga (ver su cerebro NEAT/MLP)
#   D         → modo marcar drops (click en el suelo para marcar, Z deshace)
#   F         → rotar capa de feromonas (home → food → alarm)
#   G         → rotar colonia en la capa de feromonas
#   I         → abrir diálogo de importar pool (.antgenome)
#   J         → saltar la cámara a la alerta seleccionada
#   Espacio   → pausar/reanudar
#
# REQUISITO: Unity 6000.x instalado. El proyecto se abre y compila solo.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"

# F7: pool entrenado con obstáculos y línea de visión (se resuelve a
# artifacts/pretrain-maze-los3.antgenome).
# `--pool pretrain-warm-v2` sigue disponible para comparar con la referencia.
POOL="pretrain-maze-los3"
GRID=96
COLONIES=2
TICKS=216000
SPECIES="lasius"
SPEED=120
FRAME_EVERY=1
RECRUITMENT=1
DRY_RUN=0
# F8: perillas del reclutamiento (montones de comida y rastro de feromonas).
CLUSTERS=8
CLUSTER_SIZE="6:10"
CLUSTER_RADIUS=18
TRAIL_DEPOSIT=1
TRAIL_HALF_LIFE=2100
TRAIL_FOLLOW=8

usage() {
  cat <<'EOF'

  play-game.sh [--pool nombre] [--grid 96|256] [--colonies 1|2]
               [--ticks N] [--species lasius|atta|eciton] [--speed 10-160]
               [--frame-every N] [--no-recruitment] [--dry-run]
               [--clusters N] [--cluster-size MIN:MAX] [--cluster-radius U]
               [--trail-deposit F] [--trail-half-life S] [--trail-follow G]

  Reclutamiento (F8) — default entre paréntesis:
    --clusters N            montones de comida que siembra el mundo (8)
    --cluster-size MIN:MAX  ítems por montón (6:10)
    --cluster-radius U      radio del montón en unidades (18)
    --trail-deposit F       rastro de comida de la cargada, × QMaxFood/s (1)
    --trail-half-life S     vida media del rastro en segundos (2100 = 7x la clásica)
    --trail-follow G        giro que provoca el rastro, rad/s (8; 0 = no lo sigue)

  Ejemplos:
    bash scripts/play-game.sh
    bash scripts/play-game.sh --pool pretrain-neat --grid 256 --ticks 12000
    bash scripts/play-game.sh --clusters 2 --cluster-size 12:16 --cluster-radius 30
    bash scripts/play-game.sh --trail-half-life 60 --trail-follow 0

EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --pool)        POOL="${2:-}"; shift 2 ;;
    --grid)        GRID="${2:-}"; shift 2 ;;
    --colonies)    COLONIES="${2:-}"; shift 2 ;;
    --ticks)       TICKS="${2:-}"; shift 2 ;;
    --species)     SPECIES="${2:-}"; shift 2 ;;
    --speed)       SPEED="${2:-}"; shift 2 ;;
    --frame-every) FRAME_EVERY="${2:-}"; shift 2 ;;
    --clusters)       CLUSTERS="${2:-}"; shift 2 ;;
    --cluster-size)   CLUSTER_SIZE="${2:-}"; shift 2 ;;
    --cluster-radius) CLUSTER_RADIUS="${2:-}"; shift 2 ;;
    --trail-deposit)  TRAIL_DEPOSIT="${2:-}"; shift 2 ;;
    --trail-half-life) TRAIL_HALF_LIFE="${2:-}"; shift 2 ;;
    --trail-follow)   TRAIL_FOLLOW="${2:-}"; shift 2 ;;
    --no-recruitment) RECRUITMENT=0; shift ;;
    --dry-run)     DRY_RUN=1; shift ;;
    --help|-h)     usage; exit 0 ;;
    *) echo "✗ opción desconocida: $1" >&2; usage; exit 2 ;;
  esac
done

echo "═══════════════════════════════════════════════════════════════════════════"
echo "  AntSim — Lanzar juego en Unity"
echo "═══════════════════════════════════════════════════════════════════════════"

# Verificar que el proyecto existe
if [[ ! -f "$PROJECT/Assets/Scripts/EditorTools/SceneBootstrapper.cs" ]]; then
    echo "✗ Proyecto Unity no encontrado en $PROJECT"
    exit 2
fi

# ── Validación (las mismas reglas que aplica Unity) ─────────────────────────
fail() { echo "✗ $1" >&2; exit 2; }
[[ "$GRID" =~ ^[0-9]+$ ]] && (( GRID >= 16 && GRID <= 1024 ))       || fail "--grid fuera de rango [16, 1024]: $GRID"
[[ "$COLONIES" =~ ^[0-9]+$ ]] && (( COLONIES >= 1 && COLONIES <= 2 )) || fail "--colonies fuera de rango [1, 2]: $COLONIES"
[[ "$TICKS" =~ ^[0-9]+$ ]] && (( TICKS >= 30 ))                      || fail "--ticks fuera de rango [30, 4000000]: $TICKS"
[[ "$FRAME_EVERY" =~ ^[0-9]+$ ]] && (( FRAME_EVERY >= 1 && FRAME_EVERY <= 60 )) || fail "--frame-every fuera de rango [1, 60]: $FRAME_EVERY"
awk -v s="$SPEED" 'BEGIN{exit !(s+0>=10 && s+0<=160)}'              || fail "--speed fuera de rango [10, 160]: $SPEED"
[[ "$SPECIES" =~ ^[A-Za-z]+(,[A-Za-z]+)*$ ]]                         || fail "--species inválida: '$SPECIES' (p. ej. lasius o lasius,eciton)"

# F8: montones y rastro. Mismos rangos que RecruitmentTuning (la tabla única
# que comparten el CLI, el lanzador de Windows y el mundo).
[[ "$CLUSTERS" =~ ^[0-9]+$ ]] && (( CLUSTERS >= 0 && CLUSTERS <= 64 )) || fail "--clusters fuera de rango [0, 64]: $CLUSTERS"
[[ "$CLUSTER_SIZE" =~ ^[0-9]+:[0-9]+$ ]]                             || fail "--cluster-size debe ser MIN:MAX (p. ej. 6:10): '$CLUSTER_SIZE'"
SIZE_MIN="${CLUSTER_SIZE%%:*}"; SIZE_MAX="${CLUSTER_SIZE##*:}"
(( SIZE_MIN >= 1 && SIZE_MIN <= 40 ))                               || fail "--cluster-size mínimo fuera de rango [1, 40]: $SIZE_MIN"
(( SIZE_MAX >= SIZE_MIN && SIZE_MAX <= 40 ))                        || fail "--cluster-size máximo fuera de rango [$SIZE_MIN, 40]: $SIZE_MAX"
awk -v r="$CLUSTER_RADIUS" 'BEGIN{exit !(r+0>=1 && r+0<=120)}'      || fail "--cluster-radius fuera de rango [1, 120] u: $CLUSTER_RADIUS"
awk -v d="$TRAIL_DEPOSIT" 'BEGIN{exit !(d+0>=0 && d+0<=5)}'         || fail "--trail-deposit fuera de rango [0, 5]: $TRAIL_DEPOSIT"
awk -v h="$TRAIL_HALF_LIFE" 'BEGIN{exit !(h+0>=1 && h+0<=3600)}'    || fail "--trail-half-life fuera de rango [1, 3600] s: $TRAIL_HALF_LIFE"
awk -v g="$TRAIL_FOLLOW" 'BEGIN{exit !(g+0>=0 && g+0<=20)}'         || fail "--trail-follow fuera de rango [0, 20] rad/s: $TRAIL_FOLLOW"

# ── Resolver pool ───────────────────────────────────────────────────────────
POOL_PATH=""
if [[ "$POOL" == */* || "$POOL" == *"\\"* || "$POOL" == *.antgenome ]]; then
    if [[ "$POOL" = /* ]]; then POOL_PATH="$POOL"; else POOL_PATH="$ROOT/$POOL"; fi
else
    for c in "$ROOT/artifacts/$POOL.antgenome" "$ROOT/artifacts/$POOL" "$ROOT/tests/fixtures/$POOL.antgenome"; do
        if [[ -f "$c" ]]; then POOL_PATH="$c"; break; fi
    done
fi
if [[ -z "$POOL_PATH" || ! -f "$POOL_PATH" ]]; then
    echo "✗ Pool no encontrado: $POOL" >&2
    echo "  Pools disponibles:" >&2
    ls -1 "$ROOT/artifacts"/*.antgenome 2>/dev/null | sed 's/.*\///; s/\.antgenome$//; s/^/    /' >&2 || true
    exit 2
fi

# ── Archivo de configuración que Unity leerá al arrancar ────────────────────
SETTINGS_DIR="${TEMP:-${TMPDIR:-/tmp}}"
SETTINGS_FILE="$SETTINGS_DIR/antsim-launch.txt"
# La ruta del pool viaja al Editor, no a bash: en MSYS `$ROOT` es `/e/...` y
# `File.Exists` de Unity no resuelve esa forma (el lanzador avisaba «el pool
# indicado no existe» y la escena arrancaba SIN semilla). Se convierte a la ruta
# de Windows; en Linux/macOS `cygpath` no existe y queda tal cual.
POOL_PATH_WIN="$(cygpath -w "$POOL_PATH" 2>/dev/null || printf '%s' "$POOL_PATH")"
{
    echo "# Generado por scripts/play-game.sh; lo aplica SceneBootstrapper.PlayFromLaunchSettings"
    echo "pool=$POOL"
    echo "poolPath=$POOL_PATH_WIN"
    echo "grid=$GRID"
    echo "colonies=$COLONIES"
    echo "ticks=$TICKS"
    echo "species=$SPECIES"
    echo "speed=$SPEED"
    echo "frameEvery=$FRAME_EVERY"
    echo "maze=1"
    echo "recruitment=$RECRUITMENT"
    echo "clusters=$CLUSTERS"
    echo "clusterSize=$CLUSTER_SIZE"
    echo "clusterRadius=$CLUSTER_RADIUS"
    echo "trailDeposit=$TRAIL_DEPOSIT"
    echo "trailHalfLife=$TRAIL_HALF_LIFE"
    echo "trailFollow=$TRAIL_FOLLOW"
} > "$SETTINGS_FILE"

# ── Buscar Unity ────────────────────────────────────────────────────────────
UNITY_BIN=""
if [[ -n "${UNITY_CLI:-}" && -x "${UNITY_CLI:-}" ]]; then
    UNITY_BIN="$UNITY_CLI"
else
    # `find` con los directorios ENTRE COMLLAS: el glob `"prefijo"*` no casó en
    # MSYS (devolvía vacío con el espacio de «Program Files») y la búsqueda caía
    # al launcher del Hub ($LOCALAPPDATA/Unity/bin/unity.exe), que NO acepta
    # -executeMethod ni -projectPath como el Editor. Aquí solo se mira el
    # directorio de Editores del Hub, así que el launcher queda fuera.
    hub_roots=()
    [[ -d "/c/Program Files/Unity/Hub/Editor" ]] && hub_roots+=("/c/Program Files/Unity/Hub/Editor")
    if [[ -n "${LOCALAPPDATA:-}" && -d "$LOCALAPPDATA/Unity/Hub/Editor" ]]; then
        hub_roots+=("$LOCALAPPDATA/Unity/Hub/Editor")
    fi
    for r in "${hub_roots[@]:-}"; do
        [[ -d "$r" ]] || continue
        # Versión más reciente primero (los nombres de carpeta ordenan bien).
        while IFS= read -r f; do
            if [[ -f "$f" ]]; then UNITY_BIN="$f"; break; fi
        done < <(find "$r" -maxdepth 3 -name Unity.exe -type f 2>/dev/null | sort -r)
        [[ -n "$UNITY_BIN" ]] && break
    done
    if [[ -z "$UNITY_BIN" && -f "/c/Program Files/Unity/Editor/Unity.exe" ]]; then
        UNITY_BIN="/c/Program Files/Unity/Editor/Unity.exe"
    fi
fi

if [[ -z "$UNITY_BIN" ]]; then
    echo "✗ No se encontró el Editor de Unity (no se usa el launcher del Hub)"
    echo ""
    echo "  Opciones:"
    echo "  1. Abre Unity Hub → Add → selecciona: $PROJECT"
    echo "  2. Añade el editor al entorno: export UNITY_CLI=/ruta/a/Unity.exe"
    echo "  3. Abre el proyecto manualmente en el editor"
    echo ""
    exit 3
fi

# ── Compilar el CLI si falta ────────────────────────────────────────────────
CLI_EXE="$ROOT/build/antsim/antsim.exe"
if [[ ! -f "$CLI_EXE" ]]; then
    echo "Compilando el CLI…"
    (cd "$ROOT" && dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim >/dev/null)
    echo "CLI compilado: $CLI_EXE"
fi

echo "Unity:        $UNITY_BIN"
echo "Proyecto:     $PROJECT"
echo "Pool:         $(basename "$POOL_PATH")"
echo "Grid:         $GRID ($((GRID * 8)) u)"
echo "Colonias:     $COLONIES"
echo "Ticks:        $TICKS"
echo "Especie:      $SPECIES"
echo "Velocidad:    ${SPEED}x"
echo "Frame every:  $FRAME_EVERY"
echo "Reclutamiento: $([[ "$RECRUITMENT" == "1" ]] && echo sí || echo no) (cúmulos + rastro de comida)"
if [[ "$RECRUITMENT" == "1" ]]; then
    echo "Montones:     $CLUSTERS ($SIZE_MIN-$SIZE_MAX ítems, radio $CLUSTER_RADIUS u)"
    echo "Rastro:       ${TRAIL_DEPOSIT}x QMaxFood/s · vida media ${TRAIL_HALF_LIFE} s · sigue ${TRAIL_FOLLOW} rad/s"
fi
echo "Ajustes:      $SETTINGS_FILE"
echo ""

PROJECT_WIN="$(cygpath -w "$PROJECT" 2>/dev/null || echo "$PROJECT")"
SETTINGS_WIN="$(cygpath -w "$SETTINGS_FILE" 2>/dev/null || echo "$SETTINGS_FILE")"

if [[ "$DRY_RUN" == "1" ]]; then
    echo "--dry-run: NO se lanza Unity. Comando que se ejecutaría:"
    echo ""
    echo "  \"$UNITY_BIN\" -projectPath \"$PROJECT_WIN\" \\"
    echo "      -executeMethod AntSim.Unity.Scripts.EditorTools.SceneBootstrapper.PlayFromLaunchSettings \\"
    echo "      -antsimSettings \"$SETTINGS_WIN\""
    echo ""
    exit 0
fi

echo "Abriendo Unity y aplicando la configuración… (la primera vez tarda 30-60 s)"
echo ""

exec env MSYS_NO_PATHCONV=1 "$UNITY_BIN" \
    -projectPath "$PROJECT_WIN" \
    -executeMethod AntSim.Unity.Scripts.EditorTools.SceneBootstrapper.PlayFromLaunchSettings \
    -antsimSettings "$SETTINGS_WIN" \
    -logFile -
