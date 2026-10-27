#!/usr/bin/env bash
# F11g — la campaña de réplicas a 25 s, de cuatro a siete tiradas por receta.
#
# F11f dejó la comparación entre recetas en +9 descargas con t ≈ 3 sobre CUATRO
# sorteos por receta, y con una tirada (la semilla 11) que invierte el signo: el
# eje del precio de entrenamiento sigue abierto. Tres semillas de GA nuevas
# (29, 31, 37 — ninguna usada en F11b/F11d/F11f) por receta lo estrechan a siete
# sorteos. Se replican las dos recetas a la vez —la que entrena CON la puerta del
# cerebro (--trail-by-brain) y la que no— para que el pareado por tirada de GA
# siga teniendo las dos mitades.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p25x7-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=25 refill=6 seeds=29,31,37 arms=red,fijo" >> "$DRIVER"

PIDS=()
TAGS=()
for SEED in 29 31 37; do
  GLAND=25 REFILL=6 SUFFIX="p25g$SEED" ARMS=red SEED="$SEED" \
    bash scripts/run-gland-200.sh &
  PIDS+=("$!"); TAGS+=("red-$SEED")
  GLAND=25 REFILL=6 SUFFIX="p25s$SEED" ARMS=fijo SEED="$SEED" \
    bash scripts/run-gland-200.sh &
  PIDS+=("$!"); TAGS+=("fijo-$SEED")
done
echo "pids ${PIDS[*]}" >> "$DRIVER"
echo "etiquetas ${TAGS[*]}" >> "$DRIVER"

# `wait` devuelve el estado del último hijo; se esperan todos para no dar por
# buena la campaña si una tirada falla.
STATUS=0
for p in "${PIDS[@]}"; do wait "$p" || STATUS=1; done
echo "hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
exit "$STATUS"
