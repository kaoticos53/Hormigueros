#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# unity-cli.sh — envoltorio del Unity CLI con la ruta del proyecto VALIDADA.
#
# POR QUÉ. Los scripts del repo ya pasan por `scripts/lib/unity-project.sh`, pero
# una sesión a mano (`unity command run_script --project-path …`) no: basta
# apuntar al directorio equivocado para que Unity le CREE el esqueleto de un
# proyecto (Packages, ProjectSettings, Library…). Así apareció el proyecto
# fantasma de 191 MB en la raíz del repo. Este envoltorio pone la misma guarda
# delante de cualquier invocación manual, y de paso rellena el `--project-path`
# para no tener que escribirlo.
#
# Hace lo mismo que el `UCMD()` de playpass-live.sh:
#   unity command <COMANDO> [ARGS…] --project-path <PROYECTO> --format json
#
# Uso (con `bash`, como el resto de scripts del repo: el bit de ejecución no
# viaja por git en Windows):
#   bash scripts/unity-cli.sh editor_status              # JSON del estado del editor
#   bash scripts/unity-cli.sh run_script --file sonda.cs # ejecuta una sonda
#   bash scripts/unity-cli.sh --text console             # salida humana (sin --format)
#   bash scripts/unity-cli.sh --project RUTA <COMANDO> … # otro proyecto (validado igual)
#   bash scripts/unity-cli.sh --verbose <COMANDO> …      # imprime lo que ejecuta
#   bash scripts/unity-cli.sh --selftest                 # verifica el envoltorio (sin editor)
#
# El binario se resuelve como en los pases: $UNITY_CLI → `unity` en el PATH →
# instalaciones del Hub (ver scripts/lib/unity-cli.sh).
#
# Salida: la del comando · 2 uso o proyecto inválido · 3 no hay Unity CLI.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"
FORMAT_JSON=1
VERBOSE=0

# shellcheck source=lib/unity-project.sh
source "$ROOT/scripts/lib/unity-project.sh"
# shellcheck source=lib/unity-cli.sh
source "$ROOT/scripts/lib/unity-cli.sh"

usage() { sed -n '2,28p' "$0"; }

# ── Selftest (sin editor): el orden importa ──────────────────────────────────
# Lo que se prueba aquí no es «rechaza lo malo» (eso lo hace la guarda), sino que
# la guarda corre ANTES de tocar el CLI: con un proyecto inválido el envoltorio
# sale 2 sin intentar siquiera resolver el binario. Si ese orden se invierte, en
# una máquina con Unity instalado el fallo pasa desapercibido hasta que alguien
# ve un proyecto nuevo donde no lo esperaba.
wcli_selftest() {
  local fails=0 root err rc

  # 1. La raíz del repo (el caso del fantasma) → rc 2, y sin resolver el CLI.
  root="$(uproj_repo_root)"
  err="$("$0" --project "$root" editor_status 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 ]]; then
    echo "✗ la raíz del repo no se rechazó (rc=$rc)" >&2; fails=1
  fi
  if [[ "$err" != *"-projectPath"* ]]; then
    echo "✗ la raíz del repo falló por algo que no es la guarda: $err" >&2; fails=1
  fi

  # 2. Un directorio que no es proyecto → rc 2.
  local tmp; tmp="$(mktemp -d)"
  err="$("$0" --project "$tmp" editor_status 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$err" != *"no parece un proyecto Unity"* ]]; then
    echo "✗ un directorio vacío no se rechazó (rc=$rc): $err" >&2; fails=1
  fi
  rm -rf "$tmp"

  # 3. Opción desconocida → rc 2 y uso.
  err="$("$0" --no-existe 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$err" != *"desconocida"* ]]; then
    echo "✗ una opción desconocida no se rechazó (rc=$rc): $err" >&2; fails=1
  fi

  # 4. --help → rc 0 y el uso.
  if ! "$0" --help >/dev/null 2>&1; then
    echo "✗ --help no devolvió 0" >&2; fails=1
  fi

  if [[ $fails -ne 0 ]]; then return 1; fi
  echo "✓ selftest del envoltorio del Unity CLI ok (4 casos: raíz, no-proyecto, opción rara, --help)"
  return 0
}

if [[ "${1:-}" == "--selftest" ]]; then wcli_selftest || exit 1; exit 0; fi

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project|--project-path)
      [[ $# -ge 2 ]] || { echo "✗ $1 necesita una ruta (ver --help)" >&2; exit 2; }
      PROJECT="$2"; shift 2 ;;
    --text)    FORMAT_JSON=0; shift ;;
    --verbose) VERBOSE=1; shift ;;
    --help|-h) usage; exit 0 ;;
    --) shift; break ;;
    -*) echo "✗ opción desconocida: $1 (ver --help)" >&2; exit 2 ;;
    *) break ;;
  esac
done

if [[ $# -eq 0 ]]; then
  echo "✗ falta el comando (p. ej. editor_status). Ver --help" >&2
  exit 2
fi

# La guarda ANTES de resolver el binario: el orden es lo que hace que un proyecto
# inválido no llegue nunca a Unity.
PROJECT="$(require_unity_project "$PROJECT")" || exit 2

if ! UNITY_CLI_BIN="$(find_unity_cli)"; then
  echo "✗ no encuentro el Unity CLI (usa \$UNITY_CLI o instálalo con el Hub)" >&2
  exit 3
fi

ARGS=(command "$@" --project-path "$(winpath "$PROJECT")")
if [[ $FORMAT_JSON -eq 1 ]]; then ARGS+=(--format json); fi

if [[ $VERBOSE -eq 1 ]]; then
  echo "· proyecto: $PROJECT" >&2
  echo "· editor:   $UNITY_CLI_BIN" >&2
  printf '· ejecuta:  %s %s\n' "$UNITY_CLI_BIN" "${ARGS[*]}" >&2
fi

# MSYS_NO_PATHCONV: bajo Git Bash, MSYS convertiría `--project-path E:/…` a una
# ruta POSIX y el CLI no encontraría el proyecto.
exec env MSYS_NO_PATHCONV=1 "$UNITY_CLI_BIN" "${ARGS[@]}"
