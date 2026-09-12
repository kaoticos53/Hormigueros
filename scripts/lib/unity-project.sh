#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# unity-project.sh — guarda de `-projectPath` (se hace `source`, no se ejecuta).
#
# POR QUÉ EXISTE. Unity NO falla si le das un directorio que no es un proyecto:
# le crea el esqueleto (Packages/, ProjectSettings/, Assets/, Library/, Logs/,
# UserSettings/) sin preguntar. Una invocación apuntada a la RAÍZ del repo dejó
# ahí un proyecto fantasma de 191 MB que aparecía en `git status` y que hubo que
# borrar a mano. En `-batchmode` el daño es peor que un error: el log sale
# limpio (no había scripts que compilar), así que el pass reporta ÉXITO.
#
# QUÉ EXIGE a la ruta:
#   · que exista y sea un directorio;
#   · que NO sea la raíz del repo (el caso del fantasma, con mensaje propio);
#   · que tenga las marcas de un proyecto Unity (Packages/manifest.json y
#     ProjectSettings/ProjectVersion.txt);
#   · que sea ESTE proyecto: su manifest declara `com.unity.pipeline`, el
#     paquete con el que los passes conducen el editor. Un proyecto nuevo de
#     Unity trae solo `com.unity.modules.*`, así que no pasa.
#
# Uso desde un script:
#   source "$ROOT/scripts/lib/unity-project.sh"
#   PROJECT="$(require_unity_project "$PROJECT")" || exit 2
#
# Imprime en stdout la ruta ABSOLUTA normalizada (para que el llamante la
# reutilice) y devuelve 2 con el motivo en stderr si no vale.
# ─────────────────────────────────────────────────────────────────────────────

# Raíz del repo: por la posición de esta librería (scripts/lib/../..) y, si no
# hay marcador ahí, subiendo desde el cwd. Es lo único que distingue «la raíz»
# de «un proyecto», y no depende de por dónde se llame al script.
uproj_repo_root() {
  local d
  d="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." 2>/dev/null && pwd)" || d=""
  if [[ -n "$d" && -f "$d/AntSim.slnx" ]]; then printf '%s\n' "$d"; return 0; fi
  d="$PWD"
  while [[ -n "$d" && "$d" != "/" && "$d" != "." ]]; do
    if [[ -e "$d/.git" || -f "$d/AntSim.slnx" ]]; then printf '%s\n' "$d"; return 0; fi
    d="$(dirname "$d")"
  done
  printf '%s\n' "$PWD"
}

require_unity_project() {
  local given="${1:-}" abs root manifest version
  if [[ -z "$given" ]]; then
    echo "✗ -projectPath vacío" >&2
    return 2
  fi
  if [[ ! -e "$given" ]]; then
    echo "✗ -projectPath no existe: $given" >&2
    return 2
  fi
  if [[ ! -d "$given" ]]; then
    echo "✗ -projectPath no es un directorio: $given" >&2
    return 2
  fi

  abs="$(cd "$given" && pwd)"
  root="$(uproj_repo_root)"

  # El accidente que motivó esta guarda: la raíz del repo NO es un proyecto
  # Unity, y Unity la convertiría en uno (fantasma) sin avisar.
  if [[ "$abs" == "$root" ]]; then
    echo "✗ -projectPath apunta a la RAÍZ del repo: $abs" >&2
    echo "  Unity crearía ahí un proyecto fantasma (Packages/, ProjectSettings/," >&2
    echo "  Library/…) sin preguntar. El proyecto del juego es src/App/AntSim.Unity." >&2
    return 2
  fi

  manifest="$abs/Packages/manifest.json"
  version="$abs/ProjectSettings/ProjectVersion.txt"
  if [[ ! -f "$manifest" || ! -f "$version" ]]; then
    echo "✗ -projectPath no parece un proyecto Unity: $abs" >&2
    echo "  falta Packages/manifest.json y/o ProjectSettings/ProjectVersion.txt." >&2
    return 2
  fi

  if ! grep -q '"com.unity.pipeline"' "$manifest"; then
    echo "✗ -projectPath no es ESTE proyecto: $abs" >&2
    echo "  su Packages/manifest.json no declara com.unity.pipeline (el paquete con" >&2
    echo "  el que los passes conducen el editor). ¿Un proyecto Unity nuevo o vacío?" >&2
    echo "  El proyecto del juego es src/App/AntSim.Unity." >&2
    return 2
  fi

  printf '%s\n' "$abs"
  return 0
}

# ── Selftest (sin Unity): `bash scripts/lib/unity-project.sh --selftest` ─────
uproj_selftest() {
  local fails=0 tmp out rc root
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' RETURN 2>/dev/null || true

  mkdir -p "$tmp/real/Packages" "$tmp/real/ProjectSettings" \
           "$tmp/ghost/Packages" "$tmp/ghost/ProjectSettings" "$tmp/vacio"
  printf '{"dependencies":{"com.unity.pipeline":"0.7.0-exp.1","com.unity.ugui":"2.0.0"}}\n' \
    > "$tmp/real/Packages/manifest.json"
  printf 'm_EditorVersion: 6000.6.0f1\n' > "$tmp/real/ProjectSettings/ProjectVersion.txt"
  printf '{"dependencies":{"com.unity.modules.ai":"1.0.0"}}\n' \
    > "$tmp/ghost/Packages/manifest.json"
  printf 'm_EditorVersion: 6000.6.0f1\n' > "$tmp/ghost/ProjectSettings/ProjectVersion.txt"

  # 1. Proyecto real (marcas + com.unity.pipeline) → ok, una sola línea en stdout.
  out="$(require_unity_project "$tmp/real" 2>/dev/null)"; rc=$?
  if [[ $rc -ne 0 || "$out" != "$(cd "$tmp/real" && pwd)" ]]; then
    echo "✗ un proyecto válido se rechazó (rc=$rc, out=$out)" >&2; fails=1
  fi
  if [[ "$(printf '%s' "$out" | wc -l)" -ne 0 ]]; then
    echo "✗ el caso válido imprimió más de una línea" >&2; fails=1
  fi

  # 2. Ruta relativa y con barra final → misma ruta absoluta.
  out="$(cd "$tmp" && require_unity_project real/ 2>/dev/null)"; rc=$?
  if [[ $rc -ne 0 || "$out" != "$(cd "$tmp/real" && pwd)" ]]; then
    echo "✗ la ruta relativa/con barra final no se normalizó (rc=$rc, out=$out)" >&2; fails=1
  fi

  # 3. La RAÍZ del repo → rechazada, y el motivo lo dice (el fantasma de 191 MB).
  root="$(uproj_repo_root)"
  out="$(require_unity_project "$root" 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$out" != *"RAÍZ"* ]]; then
    echo "✗ la raíz del repo NO se rechazó (rc=$rc)" >&2; fails=1
  fi

  # 4. Proyecto Unity ajeno (manifest por defecto, sin pipeline) → rechazado.
  out="$(require_unity_project "$tmp/ghost" 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$out" != *"com.unity.pipeline"* ]]; then
    echo "✗ un proyecto Unity sin com.unity.pipeline NO se rechazó (rc=$rc)" >&2; fails=1
  fi

  # 5. Directorio vacío, ruta inexistente y fichero → rechazados.
  out="$(require_unity_project "$tmp/vacio" 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$out" != *"no parece un proyecto Unity"* ]]; then
    echo "✗ un directorio vacío NO se rechazó (rc=$rc)" >&2; fails=1
  fi
  out="$(require_unity_project "$tmp/no-existe" 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$out" != *"no existe"* ]]; then
    echo "✗ una ruta inexistente NO se rechazó (rc=$rc)" >&2; fails=1
  fi
  out="$(require_unity_project "$tmp/real/Packages/manifest.json" 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$out" != *"no es un directorio"* ]]; then
    echo "✗ un fichero NO se rechazó (rc=$rc)" >&2; fails=1
  fi

  # 6. Cadena vacía → rechazada.
  out="$(require_unity_project "" 2>&1 >/dev/null)"; rc=$?
  if [[ $rc -ne 2 || "$out" != *"vacío"* ]]; then
    echo "✗ la cadena vacía NO se rechazó (rc=$rc)" >&2; fails=1
  fi

  rm -rf "$tmp"
  if [[ $fails -ne 0 ]]; then return 1; fi
  echo "✓ selftest del guardián de -projectPath ok (6 casos: válido, relativo, raíz del repo, proyecto ajeno, vacío/inexistente/fichero)"
  return 0
}

if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  case "${1:-}" in
    --selftest) uproj_selftest || exit 1 ;;
    --help|-h)  sed -n '2,28p' "$0"; exit 0 ;;
    *) echo "uso: $0 --selftest   (esta librería se usa con 'source'; ver --help)" >&2; exit 2 ;;
  esac
fi
