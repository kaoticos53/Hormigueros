#!/usr/bin/env bash
# F11b — la tirada de 200 generaciones CON PRECIO en el rastro.
#
# Dos brazos, mismo mundo y misma tirada, una sola diferencia: la puerta del
# cerebro (--trail-by-brain). El precio es el mismo en los dos (glándula de 25 s
# que solo se rellena en el nido, 6 s de vacía a llena): así lo que se compara es
# si, con un presupuesto que hay que racionar, la salida de la red rinde más que
# el reflejo fijo.
# El PRECIO y el sufijo de los artefactos son parámetros, para poder repetir la
# tirada con el precio emparejado (F11c: GLAND=10 SUFFIX=p10). Por defecto, los
# de F11b: glándula de 25 s y artefactos `pretrain-partida-200-gl-*`.
#
# SEED y ARMS son los de la replicación (F11d): con ARMS=fijo solo corre el
# brazo sin puerta, y SEED cambia la tirada del GA —y con ella las semillas de
# las partidas de selección, que salen de la misma semilla— para ver si el pool
# que deja entrenar a 10 s se debe a la receta o a la tirada.
# TICKS es el HORIZONTE de evaluación de la selección (F10: 24 000 ticks = 400 s
# de partida). Se parametriza para el control de F11j —qué parte del castigo de
# la puerta es el precio de la glándula y qué parte el horizonte—, que sube SOLO
# este número. El valor por omisión es el de todas las tiradas anteriores, así
# que las recetas ya publicadas se reproducen tal cual.
GLAND="${GLAND:-25}"
REFILL="${REFILL:-6}"
SEED="${SEED:-7}"
SUFFIX="${SUFFIX:-gl}"
ARMS="${ARMS:-red fijo}"
TICKS="${TICKS:-24000}"

set -euo pipefail
cd "$(dirname "$0")/.."

COMMON=(--mode pretrain --seed "$SEED" --pop 32 --band-min 200 --band-max 260 --hidden 24
        --fitness downloads --game-ticks "$TICKS" --game-trials 3 --generations 200
        --warm-start artifacts/pretrain-maze-3k.antgenome
        --trail-gland "$GLAND" --trail-gland-refill "$REFILL")

# Release, no Debug: el GA de esta tirada tarda ~18 s/gen en Release y ~2,3 min/gen
# en Debug, y son 200 generaciones por brazo.
DLL=src/Tools/AntSim.Cli/bin/Release/net8.0/antsim.dll

run() { # $1=etiqueta  $2...=banderas extra
  local tag="$1"; shift
  local log="artifacts/pretrain-partida-200-$SUFFIX-$tag.log"
  local start; start=$(date +%s)
  dotnet "$DLL" "${COMMON[@]}" "$@" --export "artifacts/pretrain-partida-200-$SUFFIX-$tag.antgenome" > "$log" 2>&1
  echo "EXIT=$? SECONDS=$(( $(date +%s) - start ))" >> "$log"
}

DRIVER="artifacts/pretrain-partida-200-$SUFFIX-driver.log"
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gens=200 gland=${GLAND}s refill=${REFILL}s seed=${SEED} arms=${ARMS}" >> "$DRIVER"

PIDS=()
for arm in $ARMS; do
  case "$arm" in
    red) run red --trail-by-brain & ;;
    fijo) run fijo & ;;
    *) echo "brazo desconocido: $arm (red|fijo)" >&2; exit 2 ;;
  esac
  PIDS+=("$!")
done
echo "pids ${PIDS[*]}" >> "$DRIVER"
# `wait` devuelve el estado del último hijo; se esperan todos para no dar por
# buena una tirada a medias si un brazo falla.
STATUS=0
for p in "${PIDS[@]}"; do wait "$p" || STATUS=1; done
echo "hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
exit "$STATUS"
