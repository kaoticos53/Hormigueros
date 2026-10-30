#!/usr/bin/env bash
# F11o — el contrafactual que F11n dejó escrito: el MISMO reentrenamiento de 200
# generaciones, con warm-start `pretrain-maze-3k`, pop 32 y el mismo currículo de
# la partida, pero con el precio APAGADO (--trail-gland 0) y la puerta APAGADA
# (reflejo fijo). Es lo que separa «entrenar con el precio paga» de «entrenar
# más paga»: el +47,2 de la receta del reflejo a 25 s se midió contra el campeón
# de laberinto de F7, que son 660 generaciones sin precio, y nadie había corrido
# las 200 generaciones equivalentes sin el precio.
#
# Las siete semillas de GA son las de la receta coronada (7, 11, 13, 23, 29, 31,
# 37), para poder parear tirada con tirada contra `pretrain-partida-200-gl-fijo`
# y `pretrain-partida-200-p25s????-fijo`. Una sola tirada no sirve: F11d costó
# cuatro aprender que el sorteo manda (sd entre tiradas 8,6–18,7).
#
# Nombres: `pretrain-partida-200-p0x<seed>-fijo.antgenome`, con su log al lado.
set -euo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p0x-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gens=200 gland=0 refill=6 arms=fijo seeds=7,11,13,23,29,31,37" >> "$DRIVER"

PIDS=()
for SEED in 7 11 13 23 29 31 37; do
  GLAND=0 REFILL=6 SUFFIX="p0x$SEED" ARMS=fijo SEED="$SEED" \
    bash scripts/run-gland-200.sh &
  PIDS+=("$!")
done
echo "pids ${PIDS[*]}" >> "$DRIVER"

STATUS=0
for p in "${PIDS[@]}"; do wait "$p" || STATUS=1; done
echo "hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
exit "$STATUS"
