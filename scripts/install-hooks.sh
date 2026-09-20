#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# install-hooks.sh — activa (o desactiva) los hooks del repo (.githooks/).
#
# POR QUÉ UN SCRIPT Y NO UN `git config` A MANO: `core.hooksPath` es configuración
# LOCAL del repo. Ponerla a ciegas puede pisar un valor que ya estaba —o peor,
# dejar en silencio los hooks que el repo tuviera en .git/hooks— y el día que
# algo no se ejecute nadie recordará por qué. Este script mira antes de tocar,
# dice lo que encontró y solo cambia lo que hace falta.
#
# Uso:
#   scripts/install-hooks.sh          # activa .githooks/ (idempotente)
#   scripts/install-hooks.sh --status  # qué hay ahora
#   scripts/install-hooks.sh --off     # desactiva (solo si lo puso este script)
#   scripts/install-hooks.sh --force   # pisa un core.hooksPath distinto
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WANT=".githooks"
CURRENT="$(cd "$ROOT" && git config --get core.hooksPath || true)"

status() {
  if [[ -z "$CURRENT" ]]; then
    echo "core.hooksPath: (sin definir — los hooks son los de .git/hooks/)"
  else
    echo "core.hooksPath: $CURRENT"
  fi
  local n
  n="$(ls -1 "$ROOT/.githooks" 2>/dev/null | wc -l | tr -d ' ')"
  echo "hooks versionados en $WANT/: $n"
  # Aviso, no error: .git/hooks/ casi nunca tiene hooks reales (los .sample no
  # cuentan), pero si los tiene, apuntar hooksPath a otro sitio los desactiva.
  local real
  # `|| true` dentro de la sustitución: con `pipefail`, un `grep` sin coincidencias
  # (no hay hooks propios, el caso normal) devuelve 1 y mataba el script ANTES de
  # imprimir el resultado.
  real="$(ls -1 "$ROOT/.git/hooks" 2>/dev/null | grep -v '\.sample$' | wc -l | tr -d ' ' || true)"
  if [[ "$real" != "0" && "$CURRENT" != "$WANT" ]]; then
    echo "OJO: .git/hooks/ tiene $real hook(s) propios que dejarían de correr al activar $WANT/"
    ls -1 "$ROOT/.git/hooks" | grep -v '\.sample$' | sed 's/^/     /'
  fi
}

case "${1:-}" in
  --status)
    status
    exit 0
    ;;
  --off)
    if [[ "$CURRENT" == "$WANT" ]]; then
      (cd "$ROOT" && git config --unset core.hooksPath)
      echo "✓ hooks desactivados (el pre-push ya no corre; la puerta sigue a mano: scripts/check-unity-compile.sh)"
    else
      echo "= no hay nada que desactivar (core.hooksPath: '${CURRENT:-sin definir}')"
    fi
    exit 0
    ;;
  --force)
    ;;
  "")
    ;;
  *)
    echo "uso: scripts/install-hooks.sh [--status|--off|--force]" >&2
    exit 2
    ;;
esac

if [[ -n "$CURRENT" && "$CURRENT" != "$WANT" ]]; then
  if [[ "${1:-}" != "--force" ]]; then
    echo "X core.hooksPath ya apunta a '$CURRENT'." >&2
    echo "  No lo piso por si es tuyo: pásame --force para cambiarlo a '$WANT'." >&2
    status >&2
    exit 1
  fi
  echo "! --force: cambio core.hooksPath de '$CURRENT' a '$WANT'"
fi

(cd "$ROOT" && git config core.hooksPath "$WANT")
chmod +x "$ROOT/.githooks/pre-push" 2>/dev/null || true

echo "✓ hooks activados: core.hooksPath = $WANT"
echo "  pre-push: compila la capa de vista antes de empujar (solo bloquea con errores CS)"
echo "  saltarlo una vez: git push --no-verify"
echo "  desactivar:       scripts/install-hooks.sh --off"
