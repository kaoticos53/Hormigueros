#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# player-perf.sh — el framerate PRESENTADO: build del jugador + puerta de píxeles.
#
# POR QUÉ HACE FALTA. El medidor del editor (perf-scene.sh) mide el COSTE de
# producir frames, y su corrida en ventana tampoco da el refresco presentado: en
# el editor el vsync no llega a aplicarse (medido: vSyncCount=1 y 403,8 fps,
# dentro del 1 % de la corrida con vsync apagado). El criterio de salida de F5.3
# («N colonias estables a 60 fps») solo se puede leer en un BUILD, que sí presenta
# al monitor con siguiente frame vecino.
#
# QUÉ HACE, EN DOS PASOS:
#   1. construye el player (Assets/Scenes/MultiSim.unity, 4 vistas × 2 colonias,
#      grid 256 — el caso de carga del criterio) con `PlayerBuild.BuildMultiViewPlayer`;
#   2. lo ejecuta con la sonda del player, que muestrea frames/s durante la
#      ventana (con el calentamiento aparte) y pasa la PUERTA DE PÍXELES de cada
#      vista — la misma implementación que el Play pass del editor (FrameGate).
#
# LO QUE DEJA EN EL ÁRBOL. El build regenera la escena del multi-visor y sus
# materiales (artefactos del bootstrapper): quedan «modificados» con contenido
# canónicamente idéntico. Para dejarlo limpio:
#   git checkout -- src/App/AntSim.Unity/Assets/Materials \
#                   src/App/AntSim.Unity/Assets/Scenes/MultiSim.unity
# El player va a build/player/ (ignorado por git) y el informe a
# artifacts/perf-player.json.
#
# MODO COSTE (--cpu). El framerate presentado tiene un problema de fondo: con el
# vsync entregando al refresco, el techo de 60 fps lo pone la PANTALLA y el coste
# del frame queda tapado. `--cpu` construye el player con los tiempos de frame
# encendidos (`enableFrameTimingStats`) y lo corre con el vsync APAGADO, así que
# el bucle produce frames tan rápido como puede y el FrameTimingManager dice
# cuánto cuesta cada uno: CPU total, hilo principal, hilo de render y GPU (si la
# plataforma la expone). Informe aparte (artifacts/perf-player-cpu.json): son dos
# medidas distintas y con un solo nombre la segunda borraría la evidencia de la
# primera. En este modo el vsync apagado es el REQUISITO, no el defecto.
#
# Uso:
#   scripts/player-perf.sh                     # construye y mide 30 s (+12 s de calentamiento)
#   scripts/player-perf.sh --seconds 60
#   scripts/player-perf.sh --cpu               # ídem, sin vsync: el COSTE por frame
#   scripts/player-perf.sh --skip-build        # reusa el player ya construido
#   scripts/player-perf.sh --log FICHERO       # solo analiza el log de una corrida
#   scripts/player-perf.sh --selftest          # verifica el analizador (sin Unity)
#
# Salida: 0 medido y puerta verde · 1 medido con la puerta en rojo · 2 uso ·
# 3 sin editor/CLI · 4 el player no arrancó · 5 sin veredicto (no midió) ·
# 6 el build falló · 7 el vsync está apagado (la medida no sería la presentada) ·
# 8 en modo --cpu el vsync NO llegó a apagarse (mediría el refresco, no el coste) ·
# 9 en modo --cpu el informe no trae coste medido (sin tiempos de frame no hay coste).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"
METHOD="AntSim.Unity.Scripts.EditorTools.PlayerBuild.BuildMultiViewPlayer"
PLAYER="$ROOT/build/player/AntSim.exe"
CLI_EXE="$ROOT/build/antsim/antsim.exe"
REPORT="artifacts/perf-player.json"
REPORT_CPU="artifacts/perf-player-cpu.json"
CPU_MODE=0
SECONDS_WINDOW=30
WARMUP=12
BOOST=10
GRID=256
SKIP_BUILD=0
LOG=""
ANALYZE_ONLY=0
QUIET=0
SELFTEST=0
UNITY=""

# shellcheck source=lib/unity-project.sh
source "$ROOT/scripts/lib/unity-project.sh"
# shellcheck source=lib/unity-cli.sh
source "$ROOT/scripts/lib/unity-cli.sh"

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)     PROJECT="$2"; shift 2 ;;
    --unity)       UNITY="$2"; shift 2 ;;
    --seconds)     SECONDS_WINDOW="$2"; shift 2 ;;
    --warmup)      WARMUP="$2"; shift 2 ;;
    --boost)       BOOST="$2"; shift 2 ;;
    --grid)        GRID="$2"; shift 2 ;;
    --cpu)         CPU_MODE=1; REPORT="$REPORT_CPU"; shift ;;
    --skip-build)  SKIP_BUILD=1; shift ;;
    --log)         LOG="$2"; ANALYZE_ONLY=1; shift 2 ;;
    --quiet)       QUIET=1; shift ;;
    --selftest)    SELFTEST=1; shift ;;
    --help|-h)     sed -n '2,48p' "$0"; exit 0 ;;
    *) echo "✗ parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

# ── Analizador del log (aislado: testeable sin Unity) ────────────────────────
# El veredicto es la última línea de la sonda, no el código de salida del player:
# Unity sale ≠ 0 por mil motivos y un log sin cierre es una corrida que no midió.
pp_lines() { grep -a "\[PlayerPerf\]" "$1" | sed 's/\r$//' || true; }

pp_verdict() {
  local file="$1" lines
  lines="$(pp_lines "$file")"
  if [[ -z "$lines" ]]; then
    echo "(el log no trae ninguna línea [PlayerPerf]: la sonda no llegó a correr)"
    return 5
  fi
  printf '%s\n' "$lines"
  # Los dos cierres verdes: el del modo presentado y el del modo coste.
  if grep -qaE '\[PlayerPerf\] ✓ (medido|coste medido)' "$file"; then return 0; fi
  if grep -qa '\[PlayerPerf\] ✗' "$file"; then return 1; fi
  echo "(la sonda arrancó pero no cerró con veredicto: se quedó sin ventana)"
  return 5
}

# ¿El player llegó a correr de verdad? Sin la marca del motor, un log vacío o
# truncado se leería como «no hay nada malo».
ran_player() {
  grep -qaE 'Initialize engine version|Mono path' "$1" || return 1
  grep -qaE '\[PlayerPerf\] arrancada' "$1" || return 1
  return 0
}

jget() {
  # jget <fichero> <clave> → valor crudo del JSON (el informe escribe una clave
  # por línea: la coma final y las comillas de los textos se quitan aquí).
  sed -n "s/^ *\"$2\": *//p" "$1" | head -n 1 | tr -d '\r' \
    | sed 's/,$//' | sed 's/^"//; s/"$//'
}

if [[ $SELFTEST -eq 1 ]]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  # Log bueno: arranque del motor + condiciones + cierre verde con gate ok.
  {
    echo "Initialize engine version: 6000.6.0f1"
    echo "[PlayerPerf] arrancada: ventana 30s · calentamiento 12s · boost ×10 · vsync del proyecto=1 · pantalla=1280×720 · refresco=60 Hz · informe=/x/artifacts/perf-player.json"
    echo "[PlayerPerf] condiciones: vsync=1 · targetFrameRate=-1 · pantalla=1280×720 · refresco=60.0 Hz · con foco=30/30 muestras"
    echo "[PlayerPerf] frames/s: mediana=59.9 · peor=55.1 · mejor=60.4 · frames=2712 · tick final=12000"
    echo "[PlayerPerf] vista 0 (Camera_V0): floor=0.42:0.329:0.231 brown=1 antPx=412 carrierPx=30 itemPx=180 darkFrac=0.11 → ok"
    echo "[PlayerPerf] ✓ medido: 292 hormigas, 19 draw calls, mediana 59.9 frames/s con vsync del monitor"
  } > "$tmp/ok.log"
  # Log con la puerta en rojo: midió, pero el tablero no se lee tierra.
  {
    echo "Initialize engine version: 6000.6.0f1"
    echo "[PlayerPerf] arrancada: ventana 30s · calentamiento 12s · boost ×10 · vsync del proyecto=1 · pantalla=1280×720 · refresco=60 Hz"
    echo "[PlayerPerf] vista 0 (Camera_V0): floor=1:1:1 brown=0 antPx=0 carrierPx=0 itemPx=0 darkFrac=0 → terreno no-tierra y sin hormigas"
    echo "[PlayerPerf] ✗ la puerta de píxeles suspende en alguna vista (¿tablero sin tierra o sin hormigas?)"
  } > "$tmp/gate.log"
  # Log truncado: el player no llegó a arrancar.
  printf 'Mono path[0] = C:/x\n' > "$tmp/truncado.log"

  fails=0
  rc_ok=0; rc_gate=0; rc_trunc=0
  pp_verdict "$tmp/ok.log" >/dev/null 2>&1 || rc_ok=$?
  pp_verdict "$tmp/gate.log" >/dev/null 2>&1 || rc_gate=$?
  pp_verdict "$tmp/truncado.log" >/dev/null 2>&1 || rc_trunc=$?
  if [[ $rc_ok -ne 0 ]]; then echo "✗ selftest: un log medido dio $rc_ok (esperado 0)" >&2; fails=1; fi
  if [[ $rc_gate -ne 1 ]]; then echo "✗ selftest: una puerta en rojo dio $rc_gate (esperado 1)" >&2; fails=1; fi
  if [[ $rc_trunc -ne 5 ]]; then echo "✗ selftest: un log sin sonda dio $rc_trunc (esperado 5)" >&2; fails=1; fi
  if ran_player "$tmp/truncado.log"; then echo "✗ selftest: un log sin sonda pasó por bueno" >&2; fails=1; fi
  if ! ran_player "$tmp/ok.log"; then echo "✗ selftest: un log con sonda se reportó como no arrancado" >&2; fails=1; fi
  # El extractor de claves del JSON: es lo que publica la tabla, y un cambio de
  # formato del informe no debe romperlo en silencio.
  printf '{\n  "fpsMedian": 59.9,\n  "gateOk": true,\n  "vSyncCount": 1,\n  "series": [\n    {"s": 1}\n  ]\n}\n' > "$tmp/perf.json"
  got="$(jget "$tmp/perf.json" fpsMedian)"
  if [[ "$got" != "59.9" ]]; then echo "✗ selftest: jget fpsMedian dio «$got» (esperado 59.9)" >&2; fails=1; fi
  got="$(jget "$tmp/perf.json" gateOk)"
  if [[ "$got" != "true" ]]; then echo "✗ selftest: jget gateOk dio «$got» (esperado true)" >&2; fails=1; fi
  # Las claves del modo coste: son las que publica la 5ª pieza del criterio.
  printf '{\n  "mode": "player-cpu",\n  "vSyncCount": 0,\n  "projectVSyncCount": 1,\n  "cpuMsMedian": 4.1,\n  "costMeasured": true,\n  "costFits": true,\n  "costVerdict": "cabe: la CPU deja margen al objetivo",\n  "bottleneck": "equilibrado"\n}\n' > "$tmp/cpu.json"
  for kv in "cpuMsMedian=4.1" "costMeasured=true" "projectVSyncCount=1"; do
    k="${kv%%=*}"; want="${kv#*=}"
    got="$(jget "$tmp/cpu.json" "$k")"
    if [[ "$got" != "$want" ]]; then echo "✗ selftest: jget $k dio «$got» (esperado $want)" >&2; fails=1; fi
  done
  # Log del modo coste: la sonda cierra con «coste medido» y el analizador debe
  # leerlo como verde igual que el del modo presentado.
  {
    echo "Initialize engine version: 6000.6.0f1"
    echo "[PlayerPerf] arrancada: ventana 30s · calentamiento 12s · boost ×10 · vsync del proyecto=1 · modo=COSTE (vsync apagado por la sonda) · pantalla=1280×720 · refresco=60 Hz"
    echo "[PlayerPerf] coste/frame: coste/frame cpu=4.1 ms (hilo ppal 3.4 ms, hilo render 0.7 ms) · gpu=2.2 ms · techo sin vsync=250 fps · 24.6% del presupuesto de 16.667 ms · cuello=equilibrado · cabe: la CPU deja margen al objetivo"
    echo "[PlayerPerf] ✓ coste medido: coste/frame cpu=4.1 ms"
  } > "$tmp/coste.log"
  rc_cost=0; pp_verdict "$tmp/coste.log" >/dev/null 2>&1 || rc_cost=$?
  if [[ $rc_cost -ne 0 ]]; then echo "✗ selftest: un log de coste medido dio $rc_cost (esperado 0)" >&2; fails=1; fi
  if [[ $fails -eq 0 ]]; then
    echo "✓ selftest del analizador del player ok (medido / puerta roja / sin sonda / claves del JSON y del modo coste)"
    exit 0
  fi
  exit 1
fi

# ── Veredicto sobre un log ya hecho ─────────────────────────────────────────
if [[ $ANALYZE_ONLY -eq 1 ]]; then
  [[ -f "$LOG" ]] || { echo "✗ no existe el log: $LOG" >&2; exit 2; }
  set +e
  pp_verdict "$LOG"
  rc=$?
  set -e
  if [[ $rc -eq 0 ]] && ! ran_player "$LOG"; then
    echo "✗ el log no muestra que el player arrancara: veredicto no válido" >&2
    exit 5
  fi
  exit $rc
fi

PROJECT="$(require_unity_project "$PROJECT")" || exit 2
[[ -n "$UNITY" ]] && export UNITY_EDITOR="$UNITY"
if ! UNITY="$(find_unity_editor "$PROJECT")"; then
  echo "✗ editor de Unity no encontrado (usa --unity, \$UNITY_EDITOR o instala el Hub)" >&2
  exit 3
fi

# El player lanza el CLI como proceso hijo: sin el binario publicado no habría
# mundo que dibujar y la puerta suspendería por una razón que no es la medida.
if [[ ! -f "$CLI_EXE" ]]; then
  log "   publicado el CLI (falta $CLI_EXE)…"
  (cd "$ROOT" && dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim >/dev/null) || {
    echo "✗ no se pudo publicar el CLI en build/antsim" >&2; exit 3; }
fi

BUILD_LOG="$(mktemp)"
RUN_LOG="$(mktemp)"
cleanup() { rm -f "$BUILD_LOG" "$RUN_LOG"; return 0; }
trap cleanup EXIT

if [[ $SKIP_BUILD -eq 0 ]]; then
  log "▶ Build del player: $PLAYER"
  log "   escena:   Assets/Scenes/MultiSim.unity · grid $GRID · 4 vistas × 2 colonias"
  log "   modo:     $([[ $CPU_MODE -eq 1 ]] && echo 'COSTE por frame (vsync apagado, tiempos de frame encendidos)' || echo 'framerate PRESENTADO (vsync del monitor)')"
  log "   editor:   $UNITY"
  set +e
  # El techo de tiempo importa: un proyecto abierto en OTRO editor hace que la
  # instancia batch salga de inmediato, pero un build sin nadie mirando puede
  # quedarse colgado sin escribir el cierre. `timeout` lo mata y el veredicto
  # (que exige la línea de cierre) lo detecta.
  MSYS_NO_PATHCONV=1 ANTSIM_PERF_GRID="$GRID" \
    ANTSIM_PERF_FRAME_TIMING="$CPU_MODE" \
    timeout 900 "$UNITY" -batchmode -quit -nographics \
      -projectPath "$(winpath "$PROJECT")" \
      -executeMethod "$METHOD" \
      -logFile "$(winpath "$BUILD_LOG")" >/dev/null 2>&1
  build_rc=$?
  set -e
  if [[ $build_rc -eq 124 ]]; then
    echo "✗ el build no terminó en 900s (timeout)" >&2
    exit 6
  fi
  if ! grep -qa '\[PlayerBuild\] ✓ build listo' "$BUILD_LOG"; then
    echo "✗ el build del player falló (rc=$build_rc)" >&2
    grep -a '\[PlayerBuild\]' "$BUILD_LOG" | sed 's/\r$//' | tail -12 >&2 || true
    mkdir -p "$ROOT/artifacts"
    cp "$BUILD_LOG" "$ROOT/artifacts/perf-player-build.log"
    echo "   log del build: artifacts/perf-player-build.log" >&2
    exit 6
  fi
  grep -a '\[PlayerBuild\]' "$BUILD_LOG" | sed 's/\r$//' | tail -3 | sed 's/^/   /'
fi

[[ -f "$PLAYER" ]] || { echo "✗ no existe el player: $PLAYER (¿se saltó el build?)" >&2; exit 6; }

log "▶ Sonda del player: ventana ${SECONDS_WINDOW}s · calentamiento ${WARMUP}s · boost ×$BOOST · no-vsync $CPU_MODE"
set +e
# Se ejecuta desde la RAÍZ del repo: el ancla del player ya resuelve el repo root
# por el marcador (build dentro del repo), pero el cwd deja el caso trivial a mano.
(cd "$ROOT" && "$PLAYER" \
    -logFile "$(winpath "$RUN_LOG")" \
    -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
    -antsimPerfOut "$REPORT" \
    -antsimPerfNoVsync "$CPU_MODE" \
    -antsimPerfSeconds "$SECONDS_WINDOW" \
    -antsimPerfWarmup "$WARMUP" \
    -antsimPerfBoost "$BOOST" >/dev/null 2>&1)
player_rc=$?
set -e

if [[ ! -s "$RUN_LOG" ]]; then
  echo "✗ el player no escribió log (¿arrancó? rc=$player_rc)" >&2
  exit 4
fi

set +e
pp_verdict "$RUN_LOG" | sed 's/^/   /'
rc=$?
set -e

REPORT_ABS="$ROOT/$REPORT"
if [[ -f "$REPORT_ABS" ]]; then
  log ""
  log "   informe: $REPORT"
  log "   frames/s (mediana): $(jget "$REPORT_ABS" fpsMedian) · peor $(jget "$REPORT_ABS" fpsWorst) · mejor $(jget "$REPORT_ABS" fpsBest)"
  log "   vsync: $(jget "$REPORT_ABS" vSyncCount) (proyecto $(jget "$REPORT_ABS" projectVSyncCount)) · draw calls $(jget "$REPORT_ABS" drawCalls) (instanciadas $(jget "$REPORT_ABS" instancedCalls), respaldo $(jget "$REPORT_ABS" fallbackCalls))"
  log "   carga final: $(jget "$REPORT_ABS" ants) hormigas · $(jget "$REPORT_ABS" items) ítems · tick $(jget "$REPORT_ABS" finalTick)"
  log "   puerta de píxeles: $(jget "$REPORT_ABS" gateOk)"

  if [[ $CPU_MODE -eq 1 ]]; then
    # ── Modo coste: el número que se publica es el de la CPU del frame ────────
    log "   COSTE/FRAME: cpu $(jget "$REPORT_ABS" cpuMsMedian) ms (hilo ppal $(jget "$REPORT_ABS" cpuMainMsMedian) · hilo render $(jget "$REPORT_ABS" cpuRenderMsMedian))"
    log "   gpu: $(jget "$REPORT_ABS" gpuMsMedian) ms (disponible $(jget "$REPORT_ABS" gpuAvailable)) · espera Present $(jget "$REPORT_ABS" presentWaitMsMedian) ms"
    log "   techo sin vsync: $(jget "$REPORT_ABS" achievedMsMedian) ms/frame · $(jget "$REPORT_ABS" fpsMedian) fps"
    log "   presupuesto: $(jget "$REPORT_ABS" cpuFractionOfBudget) del de $(jget "$REPORT_ABS" budgetMs) ms · cuello de botella: $(jget "$REPORT_ABS" bottleneck)"
    log "   veredicto: $(jget "$REPORT_ABS" costVerdict)"
    log "   tiempos de frame: $(jget "$REPORT_ABS" frameTimingsTaken) frames con dato · syncInterval $(jget "$REPORT_ABS" syncInterval)"

    # En modo coste el requisito es lo contrario: el vsync TIENE que estar
    # apagado. Si no lo estuvo, lo medido es el refresco del monitor con otro
    # nombre — el error simétrico al que protege la salida 7.
    if [[ "$(jget "$REPORT_ABS" vSyncCount)" != "0" ]]; then
      echo "✗ modo coste con el vsync ACTIVO: lo medido no es el coste del frame" >&2
      exit 8
    fi
    if [[ "$(jget "$REPORT_ABS" costMeasured)" != "true" ]]; then
      echo "✗ modo coste sin tiempos de frame (¿build sin enableFrameTimingStats?)" >&2
      exit 9
    fi
    if [[ "$(jget "$REPORT_ABS" costFits)" != "true" ]]; then
      echo "✗ el coste de CPU NO cabe en el presupuesto del objetivo" >&2
      exit 1
    fi
  else
    log "   intervalo presentado: $(jget "$REPORT_ABS" presentedIntervalMs) ms/frame · al refresco: $(jget "$REPORT_ABS" presentedAtRefresh) · refresco $(jget "$REPORT_ABS" refreshRateHz) Hz"

    # Sin vsync no hay «framerate presentado»: la corrida mide otra cosa y dar el
    # número por bueno sería exactamente el error que este script existe para no
    # cometer.
    if [[ "$(jget "$REPORT_ABS" vSyncCount)" == "0" ]]; then
      echo "✗ el vsync del proyecto está APAGADO: esta corrida mide coste, no refresco presentado" >&2
      exit 7
    fi
  fi
fi

if [[ $rc -ne 0 ]]; then
  mkdir -p "$ROOT/artifacts"
  cp "$RUN_LOG" "$ROOT/artifacts/perf-player.log"
  echo "   log de la corrida: artifacts/perf-player.log" >&2
fi
exit $rc
