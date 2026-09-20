#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-unity-assets.sh — ¿un asset de Unity cambió, o sólo lo reescribió el
# editor? Envoltorio de `scripts/unity-asset-equivalence.py` (el algoritmo vive
# ahí; aquí sólo se resuelve el intérprete de Python, que el repo no usaba hasta
# ahora).
#
# POR QUÉ. Abrir el proyecto, construir un player o dejar que el editor guarde
# reescribe la escena, los materiales y los ajustes de URP SIN cambiar lo que
# significan: reordena documentos, renumera anclas `&N` y mueve el bloque
# `AssetVersion`. Son miles de líneas de diff que esconden, si acaso, un cambio
# pequeño. Medido (2026-09-20): una pasada del generador reescribió 64 assets del
# multi-visor y 60 no cambiaban nada — a ojo no se puede decidir, y commitear ese
# ruido contamina la historia del repo para siempre.
#
# QUÉ HACE, EN LOS TRES MODOS:
#   1. sin argumentos: coge los assets de Unity que el ÁRBOL tiene tocados, dice
#      cuáles son idénticos a HEAD y cuáles cambian de verdad, y termina con la
#      orden `git checkout --` que descarta el ruido sin perder nada.
#      → rc 1 si alguno cambia de verdad (revísalo antes de commitear).
#   2. `--churn`: escanea un RANGO de commits (el del push en CI, o `HEAD~1..HEAD`
#      si no hay rango usable) y falla si algún commit metió un asset que no
#      cambia nada. Es el paso de CI: el churn no entra al repo.
#   3. `--selftest`: verifica el analizador con casos sintéticos, sin repo —
#      reordenar y re-anclar es idéntico, un escalar o un cableado distinto NO lo
#      es, y un commit de sólo anclas se detecta como churn. `--list` imprime el
#      alcance para que nadie lo suponga.
#
# Uso:
#   bash scripts/check-unity-assets.sh                      # el árbol, contra HEAD
#   bash scripts/check-unity-assets.sh --rev v0.8.0         # el árbol, contra un tag
#   bash scripts/check-unity-assets.sh --rev HEAD~3 RUTA…   # rutas sueltas
#   bash scripts/check-unity-assets.sh --churn              # el churn del último commit
#   bash scripts/check-unity-assets.sh --churn --range A..B # …de un rango concreto
#   bash scripts/check-unity-assets.sh --selftest           # el analizador, sin repo
#   bash scripts/check-unity-assets.sh --list               # alcance de la comprobación
#
# Salida: 0 sin nada que reprochar · 1 diferencias (o churn en el rango) · 2 uso ·
# 3 falta git o Python 3.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOL="$ROOT/scripts/unity-asset-equivalence.py"

if [[ ! -f "$TOOL" ]]; then
  echo "✗ falta el verificador: $TOOL" >&2
  exit 3
fi

# El intérprete: en el runner de CI es `python3`; en Windows (Git Bash) suele ser
# `python`. `py` es el lanzador de Windows, último recurso.
PYTHON=""
for cand in python3 python py; do
  if command -v "$cand" >/dev/null 2>&1; then PYTHON="$cand"; break; fi
done
if [[ -z "$PYTHON" ]]; then
  echo "✗ no hay Python 3 en el PATH (el verificador de assets lo necesita: python3, python o py)" >&2
  exit 3
fi

exec "$PYTHON" "$TOOL" "$@"
