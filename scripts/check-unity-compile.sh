#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-unity-compile.sh — CI: el proyecto Unity COMPILA (errores CS = fallo).
#
# POR QUÉ EXISTE: la suite headless (`dotnet test`) solo compila los scripts
# PUROS que el csproj enlaza con Link="UnityPure/…". Los MonoBehaviours
# (SimPresenterBehaviour, HudLayoutBehaviour, SceneBootstrapper, …) NO se
# compilan ahí, así que un `error CS` en la capa de vista pasa `dotnet test`
# y solo revienta al abrir Unity. Ya pasó dos veces (el `ColonyView?` con `!`,
# el `SolidCamera`): este job los compila en batch y falla con el primer error.
#
# Compila abriendo el proyecto con el editor y `-quit`: la importación de
# scripts es la que reporta los errores. La señal fiable es el LOG, no el exit
# code (Unity no siempre lo pone a 1 con errores de compilación), así que el
# analizador busca `error CS<num>:` y los marcadores agregados
# («Scripts have compiler errors» / «Compilation failed»).
#
# Uso:
#   scripts/check-unity-compile.sh                 # compila el proyecto del repo
#   scripts/check-unity-compile.sh --log FICHERO   # solo analiza un log ya hecho
#   scripts/check-unity-compile.sh --selftest      # verifica el analizador (sin Unity)
#   scripts/check-unity-compile.sh --unity RUTA --project RUTA --quiet
#
# El PROYECTO (--project) pasa antes por la guarda scripts/lib/unity-project.sh:
# Unity crea el esqueleto de un proyecto en cualquier directorio que le des, así
# que una ruta equivocada (la RAÍZ del repo) dejaría un proyecto fantasma y, en
# batchmode, un log limpio que parecería un ÉXITO.
#
# Editor: se resuelve por (1) --unity, (2) $UNITY_PATH, (3) $UNITY_EDITOR,
# (4) el Unity Hub la versión FIJADA en ProjectSettings/ProjectVersion.txt y si
# no, cualquier otra instalada. Códigos de salida: 0 ok · 1 errores de
# compilación · 2 uso o proyecto inválido · 3 editor no encontrado · 4 el editor
# no arrancó (licencia) · 5 la instancia batch no llegó a compilar.
#
# Requisitos: un editor Unity con licencia. En CI se instala y activa antes de
# llamar a este script (ver .github/workflows/ci.yml, job `unity-compile`).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"
UNITY=""

# La guarda de -projectPath se comparte con playpass-live.sh (y es la que impide
# que Unity convierta en proyecto el directorio que le demos por error).
# shellcheck source=lib/unity-project.sh
source "$ROOT/scripts/lib/unity-project.sh"
LOG=""
ANALYZE_ONLY=0
QUIET=0
SELFTEST=0

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2 ;;
    --unity)   UNITY="$2"; shift 2 ;;
    --log)     LOG="$2"; ANALYZE_ONLY=1; shift 2 ;;
    --quiet)   QUIET=1; shift ;;
    --selftest) SELFTEST=1; shift ;;
    --help|-h) sed -n '2,40p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

# ── Analizador del log (aislado: testeable con --selftest, sin Unity) ─────────
# Imprime las líneas `error CS<num>:` y devuelve 1 si hay alguna o si aparece un
# marcador agregado de compilación fallida. El `:` tras el número evita falsos
# positivos de prosa («0 error CS…») o de nombres de archivo.
analyze_log() {
  local file="$1" errs marker=0
  errs="$(grep -aE 'error CS[0-9]+:' "$file" | sed 's/\r$//' | sort -u || true)"
  if grep -qaE 'Scripts have compiler errors|Compilation failed' "$file"; then
    marker=1
  fi
  if [[ -z "$errs" && $marker -eq 0 ]]; then
    return 0
  fi
  [[ -z "$errs" ]] || printf '%s\n' "$errs"
  if [[ -z "$errs" && $marker -eq 1 ]]; then
    echo "(el log declara la compilación fallida pero no incluye las líneas 'error CS:' — ¿log truncado?)"
  fi
  return 1
}

# ── ¿La compilación ocurrió de verdad? ───────────────────────────────────────
# Un proyecto ABIERTO en otro editor hace que la instancia batch salga sin
# cargarlo (log de ~40 líneas: ruta cambiada y cierre inmediato). Se exige la
# marca de arranque del motor, que solo aparece cuando el proyecto se abrió y
# los scripts llegaron a compilarse. Sin esta comprobación el veredicto es un
# FALSO VERDE: un log truncado no contiene `error CS` y se lee como éxito.
compiled_in_log() {
  grep -qaE 'Initialize engine version' "$1" || return 1
  # Tras arrancar el motor, un cierre con código ≠ 0 tampoco es veredicto válido.
  if grep -qaE 'Exiting without the bug reporter.*return code [^0]' "$1"; then return 1; fi
  return 0
}

if [[ $SELFTEST -eq 1 ]]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  clean="$tmp/clean.log"; broken="$tmp/broken.log"; marker="$tmp/marker.log"
  {
    echo "Refreshing native plugins compatible for Editor"
    echo "Assets/Scripts/UI/Foo.cs(12,9): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context."
    echo "Compilation succeeded"
  } > "$clean"
  {
    echo "Assets/Scripts/UI/HudLayoutBehaviour.cs(107,20): error CS1061: 'ColonyView?' does not contain a definition for 'Stock'"
    echo "Assets/Scripts/UI/Other.cs(3,1): error CS1002: ; expected"
    echo "Scripts have compiler errors."
  } > "$broken"
  echo "Some unrelated text mentioning 0 error CS in prose" > "$marker"
  # Se captura la salida y el código por separado: `analyze_log | grep` bajo
  # `set -o pipefail` devolvería 1 por el propio analyze_log y enmascararía el
  # veredicto de grep (bug que este mismo selftest destapó).
  out_clean=""; out_broken=""; out_marker=""
  rc_clean=0; rc_broken=0; rc_marker=0
  out_clean="$(analyze_log "$clean")" || rc_clean=$?
  out_broken="$(analyze_log "$broken")" || rc_broken=$?
  out_marker="$(analyze_log "$marker")" || rc_marker=$?
  # Guardia de «¿se compiló?»: un log truncado (proyecto abierto en OTRO editor,
  # caso real al depurar en local) NO debe pasar por bueno — el analizador ve un
  # log sin `error CS` y lo leería como éxito.
  printf 'Successfully changed project path to: /x\n[ExitDontLaunchBugReporter] Exiting without the bug reporter. Application will exit with return code 1\n' > "$tmp/truncated.log"
  printf 'Initialize engine version: 6000.6.0f1\nDisplayProgressbar: Compiling Scripts\n[ExitDontLaunchBugReporter] Exiting without the bug reporter. Application will exit with return code 0\n' > "$tmp/compiled.log"
  printf 'Initialize engine version: 6000.6.0f1\n[ExitDontLaunchBugReporter] Exiting without the bug reporter. Application will exit with return code 1\n' > "$tmp/crashed.log"
  fails=0
  if [[ $rc_clean -ne 0 ]]; then echo "✗ selftest: un log limpio se reportó como fallo" >&2; fails=1; fi
  if [[ $rc_broken -eq 0 ]]; then echo "✗ selftest: no se detectaron los 'error CS'" >&2; fails=1; fi
  if ! grep -q 'CS1061' <<<"$out_broken"; then echo "✗ selftest: no se extrae la línea del error" >&2; fails=1; fi
  if ! grep -q 'CS1002' <<<"$out_broken"; then echo "✗ selftest: no se extraen TODOS los errores" >&2; fails=1; fi
  if [[ $rc_marker -ne 0 ]]; then echo "✗ selftest: 'error CS' en prosa dio un falso positivo" >&2; fails=1; fi
  if compiled_in_log "$tmp/truncated.log"; then echo "✗ selftest: un log SIN compilación pasó por bueno (falso verde)" >&2; fails=1; fi
  if ! compiled_in_log "$tmp/compiled.log"; then echo "✗ selftest: un log compilado se reportó como no compilado" >&2; fails=1; fi
  if compiled_in_log "$tmp/crashed.log"; then echo "✗ selftest: un cierre con código ≠ 0 pasó por bueno" >&2; fails=1; fi
  if [[ $fails -eq 0 ]]; then
    echo "✓ selftest del analizador ok (limpio / con errores / sin falsos positivos / sin falso verde)"
    exit 0
  fi
  exit 1
fi

# ── Resolución del editor ────────────────────────────────────────────────────
pinned_version() {
  local f="$PROJECT/ProjectSettings/ProjectVersion.txt"
  [[ -f "$f" ]] || return 0
  sed -n 's/^m_EditorVersion: *//p' "$f" | tr -d '\r' | head -n 1
}

# En MSYS/Git Bash `stat("…/Unity")` RESUELVE hacia `…/Unity.exe`: un `-f`
# sobre el nombre sin extensión miente en Windows y devuelve una ruta que luego
# `exec` no encuentra. Por eso se prueba SIEMPRE primero el `.exe`.
prefer_exe() {
  if [[ -f "$1.exe" ]]; then printf '%s\n' "$1.exe"; return 0; fi
  if [[ -f "$1" ]]; then printf '%s\n' "$1"; return 0; fi
  return 1
}

find_editor() {
  [[ -n "$UNITY" ]] && { printf '%s\n' "$UNITY"; return 0; }
  [[ -n "${UNITY_PATH:-}" ]] && { printf '%s\n' "$UNITY_PATH"; return 0; }
  [[ -n "${UNITY_EDITOR:-}" ]] && { printf '%s\n' "$UNITY_EDITOR"; return 0; }

  local bases=() b dir hit
  # $LOCALAPPDATA / $PROGRAMFILES llegan con barras invertidas: cygpath -u las
  # pasa a forma POSIX, que es la que `exec` entiende.
  for b in "${LOCALAPPDATA:-}" "${PROGRAMFILES:-}"; do
    [[ -n "$b" ]] || continue
    command -v cygpath >/dev/null 2>&1 && b="$(cygpath -u "$b" 2>/dev/null || printf '%s' "$b")"
    bases+=("$b/Unity/Hub/Editor")
  done
  bases+=("$HOME/Unity/Hub/Editor" "/c/Program Files/Unity/Hub/Editor"
          "/Applications/Unity/Hub/Editor" "/opt/unity/editors")

  local version; version="$(pinned_version)"
  # 1º la versión FIJADA por el proyecto; 2º cualquier instalada.
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

# Ruta tal como la espera el binario del editor (Windows exe → ruta win).
to_native() {
  case "$UNITY" in
    *.exe|*.exe.?) if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi ;;
    *) printf '%s\n' "$1" ;;
  esac
}

# ── Compilación (si no se pidió solo analizar un log) ────────────────────────
TMP_LOG=""
# `return 0` explícito: con `trap … EXIT`, el estado del script pasa a ser el del
# último comando del trap, así que un `[[ ]]` que sale 1 CLASIFICA mal todos los
# códigos de salida de arriba (2 uso · 3 sin editor · 4 licencia).
cleanup() { [[ -n "$TMP_LOG" && -f "$TMP_LOG" ]] && rm -f "$TMP_LOG"; return 0; }
trap cleanup EXIT

if [[ $ANALYZE_ONLY -eq 0 ]]; then
  # Antes de gastar un arranque del editor: la ruta tiene que ser el proyecto del
  # juego. Una invocación con la RAÍZ del repo como proyecto dejó allí un
  # proyecto fantasma de 191 MB (Unity crea el esqueleto sin preguntar) y en
  # batchmode el resultado parecía un ÉXITO: log limpio, cero errores CS.
  PROJECT="$(require_unity_project "$PROJECT")" || exit 2

  if ! UNITY="$(find_editor)"; then
    echo "✗ editor de Unity no encontrado (usa --unity, \$UNITY_PATH o instala el Hub)" >&2
    exit 3
  fi
  TMP_LOG="$(mktemp)"
  log "▶ Compilando en batch (importa y compila Todos los scripts; tarda unos minutos)…"
  log "   editor:   $UNITY"
  log "   proyecto: $PROJECT"
  # MSYS_NO_PATHCONV: con un Unity.exe bajo Git Bash, si dejamos que MSYS
  # convierta las rutas el editor recibe rutas POSIX y no encuentra el proyecto.
  MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit \
      -projectPath "$(to_native "$PROJECT")" \
      -logFile "$(to_native "$TMP_LOG")" >/dev/null 2>&1 || true
  LOG="$TMP_LOG"

  if grep -qaE 'No valid Unity Editor license|Failed to activate|No valid license|license is not' "$LOG"; then
    echo "✗ el editor no arrancó por LICENCIA — activa una antes de compilar (ver .github/workflows/ci.yml)" >&2
    exit 4
  fi
  if [[ ! -s "$LOG" ]]; then
    echo "✗ el editor no escribió log (¿arrancó?) — prueba a lanzarlo a mano" >&2
    exit 4
  fi
  if ! compiled_in_log "$LOG"; then
    echo "✗ la instancia batch NO llegó a compilar (log sin 'Initialize engine version')" >&2
    echo "  causa habitual: OTRO editor de Unity tiene este proyecto abierto y la" >&2
    echo "  segunda instancia sale de inmediato — ciérralo, o comprueba una COPIA con" >&2
    echo "  --project <copia>. Sin esta comprobación el veredicto sería un FALSO VERDE:" >&2
    echo "  un log truncado no tiene 'error CS' y se leería como «compila sin errores»." >&2
    exit 5
  fi
fi

if [[ ! -f "$LOG" ]]; then
  echo "✗ no existe el log: $LOG" >&2
  exit 2
fi

# ── Veredicto ────────────────────────────────────────────────────────────────
set +e
ERR_LINES="$(analyze_log "$LOG")"
rc=$?
set -e

if [[ $rc -ne 0 ]]; then
  echo "✗ FALLO: la compilación de Unity tiene errores — la suite headless NO los ve" >&2
  printf '%s\n' "$ERR_LINES" >&2
  echo "    (el log completo está en $LOG)" >&2
  exit 1
fi

# `sort -u`: Unity repite cada aviso (salida del compilador + resumen), así que
# contar líneas infla el número. Se cuentan líneas únicas. El `|| true` es
# obligatorio: si no hubo recompilación no hay avisos, grep sale 1 y con
# `set -e` + `pipefail` mataría el script sin mensaje.
WARNS="$( { grep -aE 'warning CS[0-9]+' "$LOG" || true; } | sort -u | wc -l | tr -d ' ')"
log "✓ el proyecto Unity compila sin errores (${WARNS:-0} avisos CS, 0 errores)"
exit 0
