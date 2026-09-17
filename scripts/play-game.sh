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
#   bash scripts/play-game.sh --species lasius,eciton --speed 6
#   bash scripts/play-game.sh --dry-run                      no abre Unity
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

POOL="pretrain-warm-v2"
GRID=96
COLONIES=2
TICKS=36000
SPECIES="lasius"
SPEED=3
FRAME_EVERY=1
DRY_RUN=0

usage() {
  cat <<'EOF'

  play-game.sh [--pool nombre] [--grid 96|256] [--colonies 1|2]
               [--ticks N] [--species lasius|atta|eciton] [--speed 1-100]
               [--frame-every N] [--dry-run]

  Ejemplos:
    bash scripts/play-game.sh
    bash scripts/play-game.sh --pool pretrain-neat --grid 256 --ticks 12000
    bash scripts/play-game.sh --species lasius,eciton --speed 6

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
awk -v s="$SPEED" 'BEGIN{exit !(s+0>=1 && s+0<=100)}'               || fail "--speed fuera de rango [1, 100]: $SPEED"
[[ "$SPECIES" =~ ^[A-Za-z]+(,[A-Za-z]+)*$ ]]                         || fail "--species inválida: '$SPECIES' (p. ej. lasius o lasius,eciton)"

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
{
    echo "# Generado por scripts/play-game.sh; lo aplica SceneBootstrapper.PlayFromLaunchSettings"
    echo "pool=$POOL"
    echo "poolPath=$POOL_PATH"
    echo "grid=$GRID"
    echo "colonies=$COLONIES"
    echo "ticks=$TICKS"
    echo "species=$SPECIES"
    echo "speed=$SPEED"
    echo "frameEvery=$FRAME_EVERY"
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
