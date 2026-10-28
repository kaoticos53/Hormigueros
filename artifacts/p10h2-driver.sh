#!/usr/bin/env bash
# F11j — CONTROL DEL HORIZONTE: ¿cuánto del castigo de la puerta a 10 s es el
# precio de la glándula y cuánto el horizonte de selección?
#
# LA PREGUNTA, TAL COMO LA DEJÓ F11h. A la MISMA receta de entrenamiento, la
# puerta del cerebro CUESTA 15,8 ± 3,5 descargas cuando se entrena a 10 s de
# glándula (t = −4,5) y GANA +4…+6 cuando se entrena a 25 s (F11g). Dos causas
# posibles con la misma pinta en los pools ya entrenados, porque el pool es lo
# que es y jugarlo más largo no lo cambia:
#   (P) el PRECIO: a 10 s el depósito cuesta cuatro veces menos por evento, y el
#       fitness sólo cuenta descargas, así que decide la puerta casi gratis;
#   (H) el HORIZONTE: la selección juzga 24 000 ticks (400 s) de una partida de
#       216 000 (3 600 s), así que la ventaja de reclutar bien —que se cobra más
#       tarde— puede caer fuera de la ventana que la selección ve.
#
# EL CONTROL. Se entrena a 10 s de glándula —el precio donde la puerta pierde— y
# se alarga SOLO el horizonte: `TICKS=48000`, el doble (800 s de partida). Todo
# lo demás idéntico: 200 generaciones, pop 32, banda 200–260, Ocultas 24,
# fitness=downloads, 3 semillas por genoma, calentado desde el mismo
# `pretrain-maze-3k.antgenome`. Y las DOS recetas, porque lo que se mide es el
# efecto de la puerta DENTRO de un horizonte (puerta menos reflejo, mismos
# sorteos), no el nivel absoluto de un pool.
#
# CÓMO SE LEE. La celda de 24 000 ticks ya existe (F11i, mismas semillas de GA).
# Si el efecto de la puerta a 48 000 ticks sube desde −15,8 hacia cero o positivo,
# el horizonte explica buena parte del castigo a 10 s; si sigue clavado en −15, el
# precio es la causa y el horizonte no la salva. Las tres semillas de GA son las
# MISMAS que las de F11i (41/43/47) para poder leer el cambio por sorteo.
#
# LO QUE ESTE CONTROL NO CIERRA, para que nadie lea de más:
#   · 2× es una manipulación, no el horizonte de la partida (que es 9×). Un nulo
#     a 48 000 deja abiertos los horizontes mayores, no los descarta.
#   · Falta la celda de 25 s a 48 000, que sería el 2×2 completo (precio ×
#     horizonte) y lo único que separa del todo las dos causas: aquí se mide el
#     horizonte SÓLO donde la puerta pierde, que es donde importa la pregunta.
#   · La escala del fitness no es comparable entre horizontes (a 48 000 la
#     gen 1 rinde ~102 descargas donde a 24 000 daba ~68): lo comparable es el
#     efecto de la puerta dentro de cada horizonte, no la suma de descargas.
#
# COSTE MEDIDO ANTES DE LANZARLO: 159 s/generación a 48 000 ticks contra 91 s a
# 24 000, en el mismo régimen de carga (~1,75×). Se corre en dos tandas de tres
# —primero las tres con puerta, luego las tres sin ella— porque la máquina rinde
# igual con tres tiradas en paralelo que con una sola, y con seis cada una tarda
# el doble.
#
# LA ESPERA. No arranca hasta que la campaña de F11i (10 s a 24 000 ticks) haya
# terminado: la máquina no da más rendimiento agregado con nueve tiradas que con
# seis, y así el veredicto de F11i no se retrasa por culpa de este control.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p10h2-driver.log
PREV=artifacts/pretrain-partida-200-p10x6-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=10 refill=6 ticks=48000 seeds=41,43,47 arms=red,luego-fijo espera=$PREV" >> "$DRIVER"

# ── Espera activa: hasta 40 h, sondeando cada minuto ────────────────────────
ESPERA_MAX=$((40 * 3600))
ESPERADO=0
while [ "$ESPERADO" -lt "$ESPERA_MAX" ]; do
  if [ -f "$PREV" ] && grep -q '^hecho' "$PREV"; then
    echo "F11i terminado: $(grep '^hecho' "$PREV") — arranca el control $(date +%F_%H:%M:%S)" >> "$DRIVER"
    break
  fi
  sleep 60
  ESPERADO=$((ESPERADO + 60))
done
if [ "$ESPERADO" -ge "$ESPERA_MAX" ]; then
  echo "aviso: F11i no dio señales en 40 h; arranca el control igualmente $(date +%F_%H:%M:%S)" >> "$DRIVER"
fi

# ── Las dos tandas ─────────────────────────────────────────────────────────
tanda() { # $1=etiqueta  $2=red|fijo
  local etiqueta="$1" brazo="$2"
  local PIDS=()
  echo "tanda $etiqueta start $(date +%F_%H:%M:%S)" >> "$DRIVER"
  for SEED in 41 43 47; do
    if [ "$brazo" = red ]; then
      GLAND=10 REFILL=6 TICKS=48000 SUFFIX="p10h2g$SEED" ARMS=red SEED="$SEED" \
        bash scripts/run-gland-200.sh &
    else
      GLAND=10 REFILL=6 TICKS=48000 SUFFIX="p10h2s$SEED" ARMS=fijo SEED="$SEED" \
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
for t in p10h2g41-red p10h2g43-red p10h2g47-red p10h2s41-fijo p10h2s43-fijo p10h2s47-fijo; do
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
