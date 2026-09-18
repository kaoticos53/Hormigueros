#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# perf-scene.sh — el techo de la VISTA: frames/s y draw calls con 8 colonias.
#
# POR QUÉ EXISTE. El criterio de salida de F5.3 es «N colonias estables a 60 fps
# en la escena de juego». El Core ya está medido headless (`--mode scale`: 8
# colonias = 3 % del presupuesto de un frame), pero eso NO es el framerate: el
# framerate los hace el editor. Este script monta la escena del multi-visor con
# CUATRO vistas × 2 colonias = 8 colonias en el grid REAL del modo juego (256),
# entra en Play en batch y mide, por muestra y por vista, frames/s reales,
# `LastDrawCalls` (el contador del instancing), hormigas e ítems dibujados.
#
# QUÉ NO ES. No es un banco de pruebas de rendimiento de Unity: corre en
# batchmode y sin vsync, así que lo que se lee es el COSTE de producir frames,
# no los fps que verá el jugador con la pantalla. Se publica como cota y así se
# documenta en docs/fase5-3-escala.md.
#
# LA SONDA SE MONTA EN EL PROYECTO. `-executeMethod` necesita la clase compilada
# dentro del proyecto: la sonda vive en scripts/unity/ (fuente, con el resto de
# sondas del repo) y este script la COPIA a Assets/Scripts/EditorTools/ para la
# corrida, borrándola al salir (trap), para no dejar un script suelto que Unity
# compile siempre. El `.meta` que genera Unity se borra igual.
#
# LO QUE DEJA EN EL ÁRBOL (importante). La corrida monta la escena del multi-visor
# con el bootstrapper, así que REGENERA `Assets/Scenes/MultiSim.unity` y sus
# materiales: son artefactos generados y quedan «modificados» con contenido
# canónicamente idéntico (los ids locales y el orden de los documentos son de cada
# sesión de Unity — byte-idéntico no es alcanzable, ya está medido y documentado).
# Para dejar el árbol limpio tras medir:
#   git checkout -- src/App/AntSim.Unity/Assets/Materials \
#                   src/App/AntSim.Unity/Assets/Scenes/MultiSim.unity
#
# Uso:
#   scripts/perf-scene.sh                        # 45 s, boost ×10, grid 256
#   scripts/perf-scene.sh --seconds 30 --boost 6
#   scripts/perf-scene.sh --log FICHERO          # solo analiza un log ya hecho
#   scripts/perf-scene.sh --project RUTA         # otro proyecto (con guarda)
#   scripts/perf-scene.sh --selftest             # verifica los analizadores (sin Unity)
#
# Salida: 0 medido · 1 medición vacía o presupuesto de draw calls roto · 2 uso ·
# 3 sin editor · 4 el editor no arrancó · 5 la instancia batch no llegó a correr
# (otro editor con el proyecto abierto).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"
PROBE_SRC="$ROOT/scripts/unity/PerfSceneProbe.cs"
PROBE_DST="$PROJECT/Assets/Scripts/EditorTools/PerfSceneProbe.cs"
METHOD="AntSim.Unity.Scripts.EditorTools.PerfSceneProbe.RunPerf"
SECONDS_WINDOW=45
BOOST=10
GRID=256
LOG=""
ANALYZE_ONLY=0
QUIET=0
SELFTEST=0
UNITY=""

# shellcheck source=lib/unity-project.sh
source "$ROOT/scripts/lib/unity-project.sh"

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2 ;;
    --unity)   UNITY="$2"; shift 2 ;;
    --seconds) SECONDS_WINDOW="$2"; shift 2 ;;
    --boost)   BOOST="$2"; shift 2 ;;
    --grid)    GRID="$2"; shift 2 ;;
    --log)     LOG="$2"; ANALYZE_ONLY=1; shift 2 ;;
    --quiet)   QUIET=1; shift ;;
    --selftest) SELFTEST=1; shift ;;
    --help|-h) sed -n '2,40p' "$0"; exit 0 ;;
    *) echo "✗ parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

# ── Analizador del log (aislado: testeable sin Unity) ────────────────────────
# Imprime las muestras `[Perf]` y devuelve el veredicto que la sonda escribió en
# su última línea (`✓ medido` / `✗`). Es el único veredicto que importa: Unity
# sale con código ≠ 0 por mil motivos, y un log sin la línea de cierre es una
# corrida que no midió nada.
perf_lines() { grep -a "^\[Perf\]\|\[Perf\] " "$1" | sed 's/\r$//' || true; }

perf_verdict() {
  local file="$1" lines
  lines="$(perf_lines "$file")"
  if [[ -z "$lines" ]]; then
    echo "(el log no trae ninguna línea [Perf]: la sonda no llegó a correr)"
    return 5
  fi
  printf '%s\n' "$lines"
  if grep -qa '\[Perf\] ✓ medido' "$file"; then return 0; fi
  if grep -qa '\[Perf\] ✗' "$file"; then return 1; fi
  echo "(la sonda arrancó pero no cerró con veredicto: se quedó sin ventana)"
  return 5
}

# ── ¿La instancia batch llegó a correr? ─────────────────────────────────────
# Mismo motivo que en check-unity-compile.sh: un proyecto ABIERTO en otro editor
# hace que la instancia batch salga de inmediato con un log de ~40 líneas, y sin
# esta comprobación un log truncado se leería como «no hay nada malo».
ran_in_log() {
  grep -qaE 'Initialize engine version' "$1" || return 1
  # La escena montada por el bootstrapper es la señal de que se llegó al editor
  # de verdad (no solo al motor): sin ella el veredicto no vale.
  grep -qaE '\[Perf\] escena:' "$1" || return 1
  return 0
}

if [[ $SELFTEST -eq 1 ]]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  # Log bueno: montaje + muestras + cierre verde.
  {
    echo "Initialize engine version: 6000.6.0f1"
    echo "[Perf] escena: 4 vistas × 2 colonias = 8 colonias · grid 256² · ventana 45s · boost ×10"
    echo "[Perf] frames/s: mediana=52.3 · p10=41.0 · peor=38.9 · mejor=61.2"
    echo "[Perf] ✓ medido: 206 hormigas, 13 draw calls, mediana 52.3 frames/s"
  } > "$tmp/ok.log"
  # Log vacío: la sonda corrió y midió una escena muerta.
  {
    echo "Initialize engine version: 6000.6.0f1"
    echo "[Perf] escena: 4 vistas × 2 colonias = 8 colonias · grid 256² · ventana 45s · boost ×10"
    echo "[Perf] ✗ MEDICIÓN VACÍA: 0 hormigas y 0 draw calls al cierre"
  } > "$tmp/vacio.log"
  # Log truncado: no llegó a montar la escena (editor abierto en otra instancia).
  printf 'Successfully changed project path to: /x\n[ExitDontLaunchBugReporter] Exiting without the bug reporter. Application will exit with return code 1\n' > "$tmp/truncado.log"

  fails=0
  rc_ok=0; rc_vacio=0; rc_trunc=0
  perf_verdict "$tmp/ok.log" >/dev/null 2>&1 || rc_ok=$?
  perf_verdict "$tmp/vacio.log" >/dev/null 2>&1 || rc_vacio=$?
  perf_verdict "$tmp/truncado.log" >/dev/null 2>&1 || rc_trunc=$?
  if [[ $rc_ok -ne 0 ]]; then echo "✗ selftest: un log medido dio $rc_ok (esperado 0)" >&2; fails=1; fi
  if [[ $rc_vacio -ne 1 ]]; then echo "✗ selftest: una medición vacía dio $rc_vacio (esperado 1)" >&2; fails=1; fi
  if [[ $rc_trunc -ne 5 ]]; then echo "✗ selftest: un log sin escena dio $rc_trunc (esperado 5)" >&2; fails=1; fi
  if ran_in_log "$tmp/truncado.log"; then echo "✗ selftest: un log sin montaje pasó por bueno" >&2; fails=1; fi
  if ! ran_in_log "$tmp/ok.log"; then echo "✗ selftest: un log con montaje se reportó como no montado" >&2; fails=1; fi
  if [[ $fails -eq 0 ]]; then
    echo "✓ selftest del analizador del medidor ok (medido / vacío / sin montaje)"
    exit 0
  fi
  exit 1
fi

# ── Veredicto sobre un log ya hecho ─────────────────────────────────────────
if [[ $ANALYZE_ONLY -eq 1 ]]; then
  [[ -f "$LOG" ]] || { echo "✗ no existe el log: $LOG" >&2; exit 2; }
  set +e
  perf_verdict "$LOG"
  rc=$?
  set -e
  if [[ $rc -eq 0 ]] && ! ran_in_log "$LOG"; then
    echo "✗ el log no muestra que se montara la escena: veredicto no válido" >&2
    exit 5
  fi
  exit $rc
fi

# ── Resolución del editor (misma preferencia que check-unity-compile.sh) ─────
prefer_exe() {
  if [[ -f "$1.exe" ]]; then printf '%s\n' "$1.exe"; return 0; fi
  if [[ -f "$1" ]]; then printf '%s\n' "$1"; return 0; fi
  return 1
}

pinned_version() {
  local f="$PROJECT/ProjectSettings/ProjectVersion.txt"
  [[ -f "$f" ]] || return 0
  sed -n 's/^m_EditorVersion: *//p' "$f" | tr -d '\r' | head -n 1
}

find_editor() {
  [[ -n "$UNITY" ]] && { printf '%s\n' "$UNITY"; return 0; }
  [[ -n "${UNITY_PATH:-}" ]] && { printf '%s\n' "$UNITY_PATH"; return 0; }
  [[ -n "${UNITY_EDITOR:-}" ]] && { printf '%s\n' "$UNITY_EDITOR"; return 0; }

  local bases=() b dir hit version
  for b in "${LOCALAPPDATA:-}" "${PROGRAMFILES:-}"; do
    [[ -n "$b" ]] || continue
    command -v cygpath >/dev/null 2>&1 && b="$(cygpath -u "$b" 2>/dev/null || printf '%s' "$b")"
    bases+=("$b/Unity/Hub/Editor")
  done
  bases+=("$HOME/Unity/Hub/Editor" "/c/Program Files/Unity/Hub/Editor"
          "/Applications/Unity/Hub/Editor" "/opt/unity/editors")

  version="$(pinned_version)"
  if [[ -n "$version" ]]; then
    for dir in "${bases[@]}"; do
      [[ -d "$dir/$version/Editor" ]] || continue
      if hit="$(prefer_exe "$dir/$version/Editor/Unity")"; then printf '%s\n' "$hit"; return 0; fi
    done
  fi
  for dir in "${bases[@]}"; do
    [[ -d "$dir" ]] || continue
    for hit in "$dir"/*/Editor/Unity.exe "$dir"/*/Editor/Unity; do
      [[ -f "$hit" ]] && { printf '%s\n' "$hit"; return 0; }
    done
  done
  return 1
}

to_native() {
  case "$UNITY" in
    *.exe) if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi ;;
    *) printf '%s\n' "$1" ;;
  esac
}

# ── Montaje de la sonda en el proyecto y corrida ────────────────────────────
PROJECT="$(require_unity_project "$PROJECT")" || exit 2
[[ -f "$PROBE_SRC" ]] || { echo "✗ no encuentro la sonda: $PROBE_SRC" >&2; exit 2; }

if ! UNITY="$(find_editor)"; then
  echo "✗ editor de Unity no encontrado (usa --unity, \$UNITY_PATH o instala el Hub)" >&2
  exit 3
fi

TMP_LOG="$(mktemp)"
cleanup() {
  [[ -f "$PROBE_DST" ]] && rm -f "$PROBE_DST"
  [[ -f "$PROBE_DST.meta" ]] && rm -f "$PROBE_DST.meta"
  rm -f "$TMP_LOG"
  return 0
}
trap cleanup EXIT

log "▶ Medidor de rendimiento: 4 vistas × 2 colonias · grid $GRID · ventana ${SECONDS_WINDOW}s · boost ×$BOOST"
log "   editor:   $UNITY"
log "   proyecto: $PROJECT"
cp "$PROBE_SRC" "$PROBE_DST"

# El techo de tiempo: ventana + compilación + arranque. Sin esto, una sonda que
# no cierra deja el script colgado para siempre (y en CI, hasta el timeout).
#
# SIN `-nographics` a PROPÓSITO: lo que se mide es el camino de dibujo
# (DrawMeshInstanced, el upload de feromonas), y sin dispositivo gráfico ese
# trabajo no ocurre — el medidor daría frames/s de una escena que no pinta nada.
LIMIT=$(( SECONDS_WINDOW + 300 ))
MSYS_NO_PATHCONV=1 ANTSIM_PERF_SECONDS="$SECONDS_WINDOW" ANTSIM_PERF_BOOST="$BOOST" \
  ANTSIM_PERF_GRID="$GRID" \
  "$UNITY" -batchmode \
    -projectPath "$(to_native "$PROJECT")" \
    -executeMethod "$METHOD" \
    -logFile "$(to_native "$TMP_LOG")" >/dev/null 2>&1 &
EDITOR_PID=$!

elapsed=0
while kill -0 "$EDITOR_PID" 2>/dev/null; do
  if [[ $elapsed -ge $LIMIT ]]; then
    echo "✗ el editor no terminó en ${LIMIT}s (se mata): la sonda no cerró" >&2
    kill "$EDITOR_PID" 2>/dev/null || true
    wait "$EDITOR_PID" 2>/dev/null || true
    exit 5
  fi
  sleep 2
  elapsed=$((elapsed + 2))
done
wait "$EDITOR_PID" 2>/dev/null || true

if grep -qaE 'No valid Unity Editor license|Failed to activate|No valid license' "$TMP_LOG"; then
  echo "✗ el editor no arrancó por LICENCIA — activa una antes de medir" >&2
  exit 4
fi
if [[ ! -s "$TMP_LOG" ]]; then
  echo "✗ el editor no escribió log (¿arrancó?)" >&2
  exit 4
fi

set +e
perf_verdict "$TMP_LOG"
rc=$?
set -e

if [[ $rc -ne 0 && $rc -ne 1 ]]; then
  if ! ran_in_log "$TMP_LOG"; then
    echo "  causa habitual: OTRO editor de Unity tiene este proyecto abierto y la" >&2
    echo "  segunda instancia sale de inmediato — ciérralo." >&2
    exit 5
  fi
fi

if [[ $rc -ne 0 ]]; then
  # El log se conserva solo cuando hay algo que mirar: el trap lo borraría y el
  # diagnóstico de una medición fallida es justo lo que hace falta después.
  mkdir -p "$ROOT/artifacts"
  cp "$TMP_LOG" "$ROOT/artifacts/perf-scene.log"
  echo "   log de la corrida fallida: artifacts/perf-scene.log" >&2
fi

log ""
log "   informe JSON: artifacts/perf-scene.json"
exit $rc
