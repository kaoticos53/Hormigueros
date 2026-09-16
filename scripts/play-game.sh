#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# play-game.sh — Lanza Unity y abre el juego con la escena lista para Play.
#
# USO:
#   bash scripts/play-game.sh
#
# Esto abre Unity con la escena de juego construida. Cuando el editor cargue,
# dale a Play (▶) para ver la simulación en vivo.
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

echo "═══════════════════════════════════════════════════════════════════════════"
echo "  AntSim — Lanzar juego en Unity"
echo "═══════════════════════════════════════════════════════════════════════════"

# Verificar que el proyecto existe
if [[ ! -f "$PROJECT/Assets/Scripts/EditorTools/SceneBootstrapper.cs" ]]; then
    echo "✗ Proyecto Unity no encontrado en $PROJECT"
    exit 2
fi

# Buscar Unity
UNITY_BIN=""
if [[ -n "${UNITY_CLI:-}" && -x "$UNITY_CLI" ]]; then
    UNITY_BIN="$UNITY_CLI"
else
    for candidate in \
        "/c/Program Files/Unity/Hub/Editor"*/Editor/Unity.exe \
        "$LOCALAPPDATA/Unity/Hub/Editor"*/Editor/Unity.exe \
        "$LOCALAPPDATA/Unity/bin/unity.exe"; do
        # Expand glob manually (set -u might complain otherwise)
        for f in $candidate; do
            if [[ -f "$f" ]]; then
                UNITY_BIN="$f"
                break 2
            fi
        done
    done
fi

if [[ -z "$UNITY_BIN" ]]; then
    echo "✗ No se encontró Unity CLI"
    echo ""
    echo "  Opciones:"
    echo "  1. Abre Unity Hub → Add → selecciona: $PROJECT"
    echo "  2. Añade Unity al PATH: export UNITY_CLI=/ruta/a/Unity.exe"
    echo "  3. Abre el proyecto manualmente en el editor"
    echo ""
    echo "  Una vez abierto el proyecto:"
    echo "    Menú AntSim → Crear escena de juego (o AntSim → Jugar)"
    echo "    Pulsa Play (▶) para ver la simulación"
    echo ""
    exit 3
fi

echo "Unity: $UNITY_BIN"
echo "Proyecto: $PROJECT"
echo ""
echo "Abriendo Unity... (puede tardar 30-60s la primera vez)"
echo ""

# Abrir Unity con el proyecto (desde la línea de comandos, abre el editor)
exec env MSYS_NO_PATHCONV=1 "$UNITY_BIN" \
    -projectPath "$(cygpath -w "$PROJECT" 2>/dev/null || echo "$PROJECT")" \
    -logFile -
