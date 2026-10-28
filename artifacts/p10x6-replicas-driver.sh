#!/usr/bin/env bash
# F11i — el brazo de entrenamiento a 10 s, tres tiradas más por receta (semillas
# de GA 41, 43 y 47), para estrechar el error del eje de precios.
#
# DE DÓNDE VIENE. F11h dejó el eje así: 10 s sin puerta con SIETE tiradas
# (semillas 7/11/13/23/29/31/37), 10 s con puerta con CUATRO (7/29/31/37), 25 s
# sin puerta con cuatro (7/11/13/23) y 25 s con puerta con siete. La celda corta
# —10 s con puerta— estimaba su media con cuatro sorteos, y su σ bajaba con
# pasos de 1/√n; con siete queda al nivel del resto del eje.
#
# POR QUÉ LAS TRES VAN A LAS DOS RECETAS. Las semillas nuevas se añaden a las
# DOS recetas de 10 s por la misma razón por la que F11h usó 29/31/37 en las dos:
# el pareado que el eje compara es POR TIRADA DE GA (mismos sorteos en las dos
# recetas), así que sembrar sólo el lado corto no añadiría ni un par nuevo —sólo
# desplazaría su media sin nada con que emparejarla—. Con 41/43/47 en ambas, el
# pareado de 10 s pasa de 4 a 7 pares y las dos celdas quedan por encima de seis.
#
# LO QUE ESTA CAMPAÑA NO ARREGLA, para que nadie lea de más: las semillas nuevas
# no existen a 25 s, así que el pareado 10 g contra 25 g sigue con las cuatro
# semillas viejas (7/29/31/37). Y la celda de 25 s sin puerta sigue con cuatro.
# Eso es el otro lado del eje y no lo toca esta tirada.
#
# Receta idéntica a la de las tiradas hermanas (mismo DLL de Release, ya
# verificado contra los registros de F11h): 200 generaciones, pop 32, banda
# 200–260, Ocultas 24, fitness=downloads, 24 000 ticks, 3 semillas por genoma,
# calentado desde artifacts/pretrain-maze-3k.antgenome, rastro por la red en el
# brazo `red`, glándula de 10 s con 6 s de vacía a llena en los dos.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p10x6-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=10 refill=6 seeds=41,43,47 arms=red,fijo" >> "$DRIVER"

PIDS=()
TAGS=()
for SEED in 41 43 47; do
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
