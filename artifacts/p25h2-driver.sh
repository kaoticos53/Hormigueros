#!/usr/bin/env bash
# F11l — LA CELDA QUE FALTA DEL 2×2 PRECIO × HORIZONTE: 25 s de glándula con la
# selección mirando 48 000 ticks (800 s de partida).
#
# EL HUECO. F11j dobló el horizonte SÓLO a 10 s, que es donde la puerta pierde, y
# encontró que el castigo crece. Con eso el horizonte queda descartado como causa
# del castigo a 10 s, pero no dice si el horizonte hace lo mismo cuando el precio
# es holgado: si a 25 s doblar la ventana también cuesta ~18 descargas, el efecto
# del horizonte es ADITIVO —no depende del precio— y lo que reparte el castigo es
# el nivel del precio más un peaje fijo por mirar poco; si a 25 s no pasa nada,
# las dos causas no son separables y el horizonte sólo muerde donde el depósito
# es barato.
#
# LAS SEMILLAS. 7/11/13, no las 41/43/47 de F11j. El eje que este control tiene
# que dejar pareado es el HORIZONTE a 25 s, y la celda de 24 000 a ese precio ya
# existe con las siete tiradas de F11g (7,11,13,23,29,31,37): entrenar la de
# 48 000 con tres de ELLAS permite leer el salto de horizonte sorteo a sorteo, que
# es justo lo que hizo concluyente el control de F11j (−18,1 ± 1,1 con t = −17).
# Con 41/43/47 se parearía el salto de precio a 48 000 y se perdería el de
# horizonte a 25 s, que es la pregunta de esta celda. El de precio queda sin
# parear en las dos filas —las dos tiradas de GA son distintas— y el comparador lo
# dice en vez de disimularlo.
#
# EL COSTE. Medido en F11j con este mismo horizonte y esta misma receta de
# búsqueda: 32 454 s de puerta y 40 008 s de reflejo con tres tiradas en
# paralelo. A 25 s la glándula dura 2,5× más y el rastro de casa se queda más
# tiempo en el mundo, así que la tirada puede tardar algo más. Se corre en dos
# tandas de tres, como allí: la máquina no da más rendimiento agregado con seis.
#
# LO QUE NO SE HACE AQUÍ: el bench y el reparto los lanza `p25h2-bench-run.sh`
# cuando este guion escribe «hecho». Un reparto sobre volcados a medias es peor
# que ninguno, así que el guion del bench se niega a producirlo si falta una
# tirada.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p25h2-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=25 refill=6 ticks=48000 seeds=7,11,13 arms=red,luego-fijo" >> "$DRIVER"

tanda() { # $1=etiqueta  $2=red|fijo
  local etiqueta="$1" brazo="$2"
  local PIDS=()
  echo "tanda $etiqueta start $(date +%F_%H:%M:%S)" >> "$DRIVER"
  for SEED in 7 11 13; do
    if [ "$brazo" = red ]; then
      GLAND=25 REFILL=6 TICKS=48000 SUFFIX="p25h2g$SEED" ARMS=red SEED="$SEED" \
        bash scripts/run-gland-200.sh &
    else
      GLAND=25 REFILL=6 TICKS=48000 SUFFIX="p25h2s$SEED" ARMS=fijo SEED="$SEED" \
        bash scripts/run-gland-200.sh &
    fi
    PIDS+=("$!")
  done
  echo "tanda $etiqueta pids ${PIDS[*]}" >> "$DRIVER"
  local STATUS=0
  for p in "${PIDS[@]}"; do wait "$p" || STATUS=1; done
  echo "tanda $etiqueta hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
  return "$STATUS"
}

STATUS=0
tanda "con-puerta" red || STATUS=1
tanda "sin-puerta" fijo || STATUS=1

# Cada tirada deja su EXIT y sus segundos en su propio log; aquí sólo se deja
# dicho lo que hay, para no tener que leer seis ficheros para saber si acabó.
for t in p25h2g7-red p25h2g11-red p25h2g13-red p25h2s7-fijo p25h2s11-fijo p25h2s13-fijo; do
  LOG="artifacts/pretrain-partida-200-$t.log"
  if [ -f "$LOG" ]; then
    echo "  $t: $(grep -c '^gen ' "$LOG") generaciones · $(tail -2 "$LOG" | tr -d '\r' | grep '^EXIT=' || echo 'SIN EXIT')" >> "$DRIVER"
  else
    echo "  $t: sin log" >> "$DRIVER"
    STATUS=1
  fi
done
echo "hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
exit "$STATUS"
