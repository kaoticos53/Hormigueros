#!/usr/bin/env bash
# F11h — el brazo de entrenamiento a 10 s, de cuatro a siete tiradas por receta.
#
# F11e cerró la comparación de precios de entrenamiento (10 s contra 25 s) con
# CUATRO sorteos por receta y un aviso propio: con una sd entre tiradas de 46
# hacían falta decenas de pares para que la diferencia llegara a t = 2, así que
# «entrenar a 10 s» quedó como indicio repetido, no como receta. El brazo de 25 s
# ya tiene SIETE tiradas (F11g, semillas de GA 7/11/13/23/29/31/37); con las
# mismas tres semillas nuevas —29, 31 y 37— el pareado por tirada de GA pasa a
# siete pares en los dos lados.
#
# OJO con el lado CON puerta: a 10 s solo existe UN pool (la semilla 7 de F11c,
# la tirada cuya selección se hundió). Sus tres nuevas lo llevan a cuatro, no a
# siete, y por eso ese lado del eje sigue a la mitad —mejor que 1 contra 7, pero
# no pareado—. El driver lo deja dicho para que nadie lea sus medias como si
# tuvieran la precisión del otro lado.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p10x3-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=10 refill=6 seeds=29,31,37 arms=red,fijo" >> "$DRIVER"

PIDS=()
TAGS=()
for SEED in 29 31 37; do
  GLAND=10 REFILL=6 SUFFIX="p10g$SEED" ARMS=red SEED="$SEED" \
    bash scripts/run-gland-200.sh &
  PIDS+=("$!"); TAGS+=("red-$SEED")
  GLAND=10 REFILL=6 SUFFIX="p10s$SEED" ARMS=fijo SEED="$SEED" \
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
