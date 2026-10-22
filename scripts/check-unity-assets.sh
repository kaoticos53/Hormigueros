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
# QUÉ HACE, SEGÚN EL MODO:
#   1. sin argumentos: coge los assets de Unity que el ÁRBOL tiene tocados, dice
#      cuáles son idénticos a HEAD y cuáles cambian de verdad, y termina con la
#      orden `git checkout --` que descarta el ruido sin perder nada.
#      → rc 1 si alguno cambia de verdad (revísalo antes de commitear).
#   2. `--discard`: hace eso mismo, pero ACTÚA en vez de dejar la orden escrita:
#      devuelve a REV las rutas certificadas idénticas (el churn de anclas y
#      orden) y a las que CAMBIAN de verdad les quita la forma conservando el
#      contenido —ver más abajo—. `--dry-run` lo deja en aviso, midiendo igual.
#   3. `--churn`: escanea un RANGO de commits (el del push en CI, o `HEAD~1..HEAD`
#      si no hay rango usable) y falla si algún commit metió un asset que no
#      cambia nada. Es el paso de CI: el churn no entra al repo.
#   4. `--selftest`: verifica el analizador con casos sintéticos, sin repo —
#      reordenar y re-anclar es idéntico, un escalar o un cableado distinto NO lo
#      es, y un commit de sólo anclas se detecta como churn. `--list` imprime el
#      alcance para que nadie lo suponga.
#
# QUÉ HACE CON ESE VEREDICTO (modo `--discard`): ACTÚA, y por los dos caminos
# que hacen falta para que el diff contra REV sea lo que cambió:
#
#   · los IDÉNTICO (el churn de forma: anclas y orden) se restauran a REV con un
#     `git checkout`, que es exacto y gratis;
#   · los CAMBIA (cambio de contenido de verdad) NO se pueden restaurar —eso
#     perdería el cambio—, así que se reescriben en su forma CANÓNICA (la de
#     `scripts/unity-canonicalize-assets.py`, la misma que escribe el generador)
#     conservando el contenido: su diff contra REV queda reducido al cambio.
#     Funciona porque el ancla canónica es el hash del contenido, así que un
#     documento que no cambió vuelve a ser el mismo byte que en REV (medido en
#     `Game.unity`, 425 documentos: de 10 669 líneas de diff a 2).
#   · los CANONICALIZA tampoco se tocan, porque descartarlos volvería a poner la
#     trampa.
#
# Y no se fía de sí mismo: antes de escribir, la forma canónica tiene que pasar el
# `compare` del verificador (si no significa lo mismo, no escribe y lo dice), y
# después vuelve a clasificar y a mirar el índice para comprobar lo que acaba de
# afirmar —el churn ya no está tocado, cada CAMBIA sigue cambiando y quedó
# canónico, y el árbol coincide con el índice—; si algo falla lo dice y sale con
# 4. `--dry-run` dice lo que haría SIN tocar nada, con la reducción medida de
# verdad (canonicaliza en memoria y diffea contra REV).
#
# LA EXCEPCIÓN, en los dos modos: un cambio equivalente que DEJA el asset en su
# forma canónica (la que escribe scripts/unity-canonicalize-assets.py) NO es
# churn. Es el que cierra el churn recurrente de un asset commiteado mal —el caso
# real: `PheromoneMat.mat`, cuyo bloque `AssetVersion` quedó delante y con ancla de
# sesión, así que cada pasada del editor lo reescribía y el diff de forma volvía
# para siempre—. Se reporta como CANONICALIZA: en el modo árbol se COMMITEA (no se
# descarta) y en `--churn` no bloquea el push. El re-anclado o el reordenado que no
# canonicaliza se rechaza como siempre.
#
# Uso:
#   bash scripts/check-unity-assets.sh                      # el árbol, contra HEAD
#   bash scripts/check-unity-assets.sh --rev v0.8.0         # el árbol, contra un tag
#   bash scripts/check-unity-assets.sh --rev HEAD~3 RUTA…   # rutas sueltas
#   bash scripts/check-unity-assets.sh --discard            # quita la forma del árbol
#   bash scripts/check-unity-assets.sh --discard --dry-run  # …diciendo qué haría
#   bash scripts/check-unity-assets.sh --churn              # el churn del último commit
#   bash scripts/check-unity-assets.sh --churn --range A..B # …de un rango concreto
#   bash scripts/check-unity-assets.sh --selftest           # el analizador, sin repo
#   bash scripts/check-unity-assets.sh --list               # alcance de la comprobación
#
# Salida: 0 sin nada que reprochar · 1 diferencias (o churn en el rango, o
# assets pendientes: churn que descartar o cambios de verdad que revisar) · 2 uso
# · 3 falta git o Python 3 · 4 la comprobación posterior al descarte falló (nada
# se da por bueno sin volver a mirarlo).
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
