#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-unity-references.sh — ¿un editor perdió una referencia al cerrarse?
# Envoltorio de `scripts/unity-reference-check.py` (el algoritmo vive ahí; aquí
# sólo se resuelve el intérprete de Python, como en check-unity-assets.sh).
#
# POR QUÉ. Es el hueco que dejó el accidente del 2026-10-08: al cerrarse, el
# editor dejó `PheromoneMat.mat` con el `_BaseMap` en `{fileID: 0}` —sin la guía
# de su textura de reposo— y el quad de feromonas se pintaba BLANCO OPACO encima
# del tablero. El YAML era válido y ningún check de estado lo veía, así que la
# pérdida sobrevivió en el árbol hasta que la siguiente suite la encontró. Esto
# la caza en segundos y sin abrir Unity.
#
# LOS CUATRO MODOS:
#   1. sin argumentos: comprueba el árbol contra HEAD —guías muertas y
#      referencias perdidas— y sale 1 si alguna se fue, nombrando el fichero y el
#      slot (`_BaseMap`, `m_Texture`…).
#   2. `--range A..B`: cada commit del rango contra SU PADRE, sin mirar el disco
#      —lo que empuja un push son los commits, no el árbol—. Es la puerta 2 del
#      pre-push. `--range` sin valor deja que el verificador resuelva el rango
#      (`CI_RANGE`, que es lo que declara el evento, o el último commit).
#   3. `--watch`: espera a que el editor CIERRE (Unity borra
#      `<proyecto>/Temp/UnityLockfile` al salir) y comprueba entonces, que es el
#      instante en el que deja el árbol escrito. `--timeout SEG` acota la espera.
#   4. `--selftest`: los dos detectores y la espera del cierre, sin repo ni
#      editor. `--list` imprime el ámbito y los límites.
#
# Uso:
#   bash scripts/check-unity-references.sh                 # el árbol, contra HEAD
#   bash scripts/check-unity-references.sh --rev v0.8.0    # contra un tag
#   bash scripts/check-unity-references.sh --range A..B    # cada commit vs su padre
#   bash scripts/check-unity-references.sh --range         # …el rango deducible
#   bash scripts/check-unity-references.sh --watch         # al cerrarse el editor
#   bash scripts/check-unity-references.sh --watch --timeout 600
#   bash scripts/check-unity-references.sh --selftest
#   bash scripts/check-unity-references.sh --list
#
# UN BORRADO A PROPÓSITO se declara, con motivo, en
# `scripts/unity-reference-allowlist.txt` (ruta | guía | motivo): el alcance es
# exacto, así que cualquier otra pérdida del mismo fichero sigue fallando.
#
# Salida: 0 referencias intactas · 1 se perdió alguna · 2 uso · 3 no medible (sin
# git o Python, o el editor no cerró antes del --timeout).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOL="$ROOT/scripts/unity-reference-check.py"

if [[ ! -f "$TOOL" ]]; then
  echo "✗ falta el verificador: $TOOL" >&2
  exit 3
fi

# El intérprete: `python3` en el runner de CI, `python` en Git Bash de Windows y
# `py` (el lanzador de Windows) como último recurso.
PYTHON=""
for cand in python3 python py; do
  if command -v "$cand" >/dev/null 2>&1; then PYTHON="$cand"; break; fi
done
if [[ -z "$PYTHON" ]]; then
  echo "✗ no hay Python 3 en el PATH (el verificador de referencias lo necesita: python3, python o py)" >&2
  exit 3
fi

# PYTHONUTF8=1: los mensajes llevan ✓/✗/· y la consola cp1252 de Windows revienta
# con UnicodeEncodeError aunque el trabajo esté hecho (el script también lo
# fuerza, pero aquí no depende del intérprete que se elija).
cd "$ROOT"
PYTHONUTF8=1 exec "$PYTHON" "$TOOL" "$@"
