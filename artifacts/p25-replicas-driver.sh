#!/usr/bin/env bash
# F11d bis — el brazo SIN puerta a 25 s, con las mismas tres semillas de GA que
# las réplicas de 10 s (11, 13, 23), para que la comparación de precios de
# entrenamiento sea réplica contra réplica y no una tirada contra tres.
# El pool de la semilla 7 a 25 s sin puerta ya existe (pretrain-partida-200-gl-fijo).
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p25-driver.log
: > "$DRIVER"
echo "start $(date +%F_%H:%M:%S) gland=25 refill=6 arms=fijo seeds=11,13,23" >> "$DRIVER"

PIDS=()
for SEED in 11 13 23; do
  GLAND=25 REFILL=6 SUFFIX="p25s$SEED" ARMS=fijo SEED="$SEED" \
    bash scripts/run-gland-200.sh &
  PIDS+=("$!")
done
echo "pids ${PIDS[*]}" >> "$DRIVER"

STATUS=0
for p in "${PIDS[@]}"; do wait "$p" || STATUS=1; done
echo "hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$DRIVER"
exit "$STATUS"
