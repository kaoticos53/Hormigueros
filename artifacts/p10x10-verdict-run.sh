#!/usr/bin/env bash
# F11i — el veredicto, en cuanto el bench de las dos campañas termine.
#
# Espera a que `p10x6-bench-run.sh` diga «bench hecho» en su launcher (no a que
# exista un fichero: el volcado aparece al final y el log se va llenando). Y si
# el bench no da señales en 12 h, lo dice y NO produce veredicto: un veredicto
# sobre volcados a medias sería peor que ninguno.
#
# Las tres piezas, con los mismos comparadores de F11h para que las cifras sean
# comparables:
#   · precio en la receta del REFLEJO: 10 pools a 10 s contra 7 a 25 s, siete
#     pares de GA (semillas 7/11/13/23/29/31/37) donde F11h tenía siete y siete;
#   · precio en la receta de la PUERTA: 7 pools a 10 s contra 7 a 25 s, cuatro
#     pares de GA (7/29/31/37) —los sorteos nuevos 41/43/47 no existen a 25 s—;
#   · la puerta del cerebro sobre las DIECISIETE tiradas de 10 s (10 + 7).
set -uo pipefail
cd "$(dirname "$0")/.."

PREV=artifacts/pretrain-partida-200-p10x10-launcher.log
LOG=artifacts/pretrain-partida-200-p10x10-verdict.log
: > "$LOG"
echo "espera: $PREV" >> "$LOG"

ESPERA_MAX=$((12 * 3600))
ESPERADO=0
while [ "$ESPERADO" -lt "$ESPERA_MAX" ]; do
  if grep -q '^bench hecho' "$PREV" 2>/dev/null; then
    echo "bench terminado: $(grep '^bench hecho' "$PREV") — veredicto $(date +%F_%H:%M:%S)" >> "$LOG"
    break
  fi
  sleep 60
  ESPERADO=$((ESPERADO + 60))
done

if [ "$ESPERADO" -ge "$ESPERA_MAX" ]; then
  echo "aviso: el bench no dio señales en 12 h; NO se produce veredicto $(date +%F_%H:%M:%S)" >> "$LOG"
  exit 1
fi

STATUS=0
python scripts/gland-recipe-compare.py \
  --a 10s=artifacts/pretrain-partida-200-p10x10-replicas.log \
  --b 25s=artifacts/pretrain-partida-200-p257-replicas.log \
  --gate fijo \
  --out artifacts/pretrain-partida-200-p10x10-vs-p257.txt >> "$LOG" 2>&1 || STATUS=1

python scripts/gland-recipe-compare.py \
  --a 10s=artifacts/pretrain-partida-200-p10g7-replicas.log \
  --b 25s=artifacts/pretrain-partida-200-p25g7-replicas.log \
  --gate puerta \
  --out artifacts/pretrain-partida-200-p10g7-vs-p25g7.txt >> "$LOG" 2>&1 || STATUS=1

python scripts/gland-gate-replicas.py \
  --log "10 s sin puerta=artifacts/pretrain-partida-200-p10x10-replicas.log" \
  --log "10 s con puerta=artifacts/pretrain-partida-200-p10g7-replicas.log" \
  --champion p10-s7 \
  --out artifacts/pretrain-partida-200-p10x10-champion.txt >> "$LOG" 2>&1 || STATUS=1

echo "veredicto hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$LOG"
exit "$STATUS"
