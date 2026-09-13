#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# unity-cli.sh — encontrar el CLI de Unity y convertir rutas (se hace `source`).
#
# Una sola definición para todos los consumidores: `playpass-live.sh`, el
# envoltorio `scripts/unity-cli.sh` y cualquier script futuro. Duplicar este
# bloque hacía que dos scripts discrepasen sobre dónde vive el CLI o cómo se
# escribe una ruta para Windows — y eso se nota tarde, cuando el pass falla
# «no encuentro el editor» en una máquina donde sí está.
#
# `find_unity_cli`: $UNITY_CLI → `unity` en el PATH → las instalaciones del Hub
# (~/Unity/bin, %LOCALAPPDATA%/Unity/bin). Imprime la ruta o devuelve 1.
# `winpath`: ruta con el formato que espera el CLI (`E:/…`) cuando hay cygpath.
# ─────────────────────────────────────────────────────────────────────────────

find_unity_cli() {
  if [[ -n "${UNITY_CLI:-}" ]]; then printf '%s\n' "$UNITY_CLI"; return 0; fi
  if command -v unity >/dev/null 2>&1; then command -v unity; return 0; fi
  local b
  for b in "${LOCALAPPDATA:-}" "$HOME"; do
    [[ -n "$b" ]] || continue
    case "$b" in *\\*) command -v cygpath >/dev/null 2>&1 && b="$(cygpath -u "$b" 2>/dev/null || printf '%s' "$b")" ;; esac
    for c in "$b/Unity/bin/unity.exe" "$b/Unity/bin/unity"; do
      [[ -f "$c" ]] && { printf '%s\n' "$c"; return 0; }
    done
  done
  return 1
}

# El CLI de Unity espera rutas de Windows con barras normales (`E:/…`).
winpath() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi
}

# ── Selftest (sin editor): `bash scripts/lib/unity-cli.sh --selftest` ────────
ucli_selftest() {
  local fails=0 tmp got
  tmp="$(mktemp -d)"

  # 1. $UNITY_CLI manda sobre todo lo demás (es lo que usan los pases para fijar
  #    un binario concreto sin depender del PATH de la máquina).
  got="$(UNITY_CLI=/falso/unity find_unity_cli)"
  if [[ "$got" != "/falso/unity" ]]; then
    echo "✗ \$UNITY_CLI no se respetó (got=$got)" >&2; fails=1
  fi

  # 2. Con un HOME/LOCALAPPDATA simulados, encuentra la instalación del Hub.
  #    En subshell con el entorno sustituido: `env` no sirve para funciones del
  #    shell, y aquí se está probando justamente la función.
  mkdir -p "$tmp/home/Unity/bin"
  : > "$tmp/home/Unity/bin/unity"
  got="$( ( unset UNITY_CLI; HOME="$tmp/home"; LOCALAPPDATA="$tmp/nada";
            PATH=/usr/bin:/bin; find_unity_cli ) 2>/dev/null || true)"
  if [[ "$got" != "$tmp/home/Unity/bin/unity" ]]; then
    echo "✗ no se encontró el CLI en ~/Unity/bin (got=$got)" >&2; fails=1
  fi

  # 3. Sin nada instalado devuelve 1 en vez de imprimir basura.
  if ( unset UNITY_CLI; HOME="$tmp/vacio"; LOCALAPPDATA="$tmp/vacio";
       PATH=/usr/bin:/bin; find_unity_cli ) >/dev/null 2>&1; then
    echo "✗ sin CLI disponible no devolvió fallo" >&2; fails=1
  fi

  # 4. winpath no revienta sin argumentos raros y es estable.
  got="$(winpath "/e/repo/src/App/AntSim.Unity")"
  if [[ -z "$got" ]]; then
    echo "✗ winpath devolvió vacío" >&2; fails=1
  fi

  rm -rf "$tmp"
  if [[ $fails -ne 0 ]]; then return 1; fi
  echo "✓ selftest de la librería del Unity CLI ok (4 casos: \$UNITY_CLI, Hub, ausencia, winpath)"
  return 0
}

if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  case "${1:-}" in
    --selftest) ucli_selftest || exit 1 ;;
    --help|-h)  sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "uso: $0 --selftest   (esta librería se usa con 'source'; ver --help)" >&2; exit 2 ;;
  esac
fi
