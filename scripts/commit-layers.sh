#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# commit-layers.sh — el árbol de trabajo, repartido en commits atómicos por
# capas. Envoltorio de `scripts/commit-layers.py` (el algoritmo vive ahí; aquí
# sólo se resuelve el intérprete de Python y el repo, igual que hacen los otros
# verificadores del repo).
#
# POR QUÉ. El árbol de este repo casi nunca tiene una sola cosa pendiente:
# assets que el editor reescribió, scripts y ganchos de una hebra, el modelo de
# otra, documentos y el volcado del play pass. Repartirlo a mano es mecánico
# pero se hace mal —y se hace tarde— cuando hay veinticinco rutas de seis
# naturalezas. Esto lo hace igual cada vez, y por omisión NO ESCRIBE NADA:
# primero el plan, después el reparto.
#
# Uso:
#   bash scripts/commit-layers.sh                          # el plan (no toca nada)
#   bash scripts/commit-layers.sh --list                   # las capas y sus reglas
#   bash scripts/commit-layers.sh --layer docs             # sólo esa capa
#   bash scripts/commit-layers.sh --json plan.json         # plan + hash por ruta
#   bash scripts/commit-layers.sh --apply                  # commitea por capas
#   bash scripts/commit-layers.sh --apply-plan plan.json   # …el plan revisado
#   bash scripts/commit-layers.sh --apply --discard-churn  # …descartando el churn
#   bash scripts/commit-layers.sh --subject docs='docs: lo que cambié'
#   bash scripts/commit-layers.sh --selftest               # extremo a extremo
#
# Salida: 0 el plan (o el reparto) salió · 1 hay rutas fuera de toda capa (`otros`)
# · 2 uso, índice sucio sin `--include-index`, o churn pendiente sin
# `--discard-churn` · 3 falta git o Python 3, o no es un repo · 4 el plan revisado
# ya no vale (cambió una ruta) · 5 un commit no contiene exactamente lo previsto.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOL="$ROOT/scripts/commit-layers.py"

if [[ ! -f "$TOOL" ]]; then
  echo "✗ falta el motor del reparto: $TOOL" >&2
  exit 3
fi

# El intérprete: en el runner de CI es `python3`; en Windows (Git Bash) suele ser
# `python`. `py` es el lanzador de Windows, último recurso.
PYTHON=""
for cand in python3 python py; do
  if command -v "$cand" >/dev/null 2>&1; then PYTHON="$cand"; break; fi
done
if [[ -z "$PYTHON" ]]; then
  echo "✗ no hay Python 3 en el PATH (el reparto por capas lo necesita: python3, python o py)" >&2
  exit 3
fi

# PYTHONUTF8=1 no es opcional: sin él, imprimir ✓/✗ en la consola cp1252 de
# Windows revienta con UnicodeEncodeError aunque el trabajo esté hecho.
export PYTHONUTF8=1
exec "$PYTHON" "$TOOL" --repo "$ROOT" "$@"
