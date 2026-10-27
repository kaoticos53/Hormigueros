#!/usr/bin/env bash
# F11f — el brazo CON puerta a 25 s, con las mismas tres semillas de GA que las
# réplicas de 10 s (11, 13, 23). F11b dejó UN pool por brazo (semilla 7) y por
# eso no se puede saber si su ventaja es la receta o la tirada; estas tres lo
# responden. El pool de la semilla 7 con puerta ya existe
# (pretrain-partida-200-gl-red).
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p25g-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=25 refill=6 arms=red seeds=11,13,23" >> "$DRIVER"

PIDS=()
for SEED in 11 13 23; do
  GLAND=25 REFILL=6 SUFFIX="p25g$SEED" ARMS=red SEED="$SEED" \
    bash scripts/run-gland-200.sh &
  PIDS+=("$!")
done
echo "pids ${PIDS[*]}" >> "$DRIVER"

STATUS=0
for p in "${PIDS[@]}"; do wait "$p" || STATUS=1; done
echo "hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
exit "$STATUS"
