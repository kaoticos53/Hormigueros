#!/usr/bin/env bash
# F11l — el bench y el reparto del 2×2, en cuanto terminen los seis
# entrenamientos de 25 s a 48 000 ticks.
#
# QUÉ JUZGA. Los seis pools nuevos (tres con la puerta, tres con el reflejo,
# semillas de GA 41/43/47) con las dos formas de decidir el depósito y los dos
# precios de juego, 24 semillas: 3 pools × 2 precios × 2 rastros × 24 = 288
# partidas por receta, 576 en total.
#
# POR QUÉ `--no-build`. El entrenamiento puede tener el CLI cargado; ya se
# comprobó en F11i/F11j que el `antsim.dll` de Release reproduce las partidas
# publicadas y no cambia ninguna fuente de C# entre esas tiradas y estas.
#
# LAS OTRAS TRES CASILLAS ya están jugadas y no se repiten: se leen de sus
# volcados. Repetirlas costaría 1 632 partidas para volver a dar las mismas
# cifras. La de 25 s a 24 000 es la de las siete tiradas de F11g, con sus dos
# recetas.
#
# SI EL ENTRENAMIENTO NO DA SEÑALES, NO HAY REPARTO: un veredicto sobre volcados
# a medias es peor que ninguno, y este guion lo dice en su log en vez de
# escribirlo.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p25h2-driver.log
LOG=artifacts/pretrain-partida-200-p25h2-verdict.log
: > "$LOG"
echo "espera: $DRIVER" >> "$LOG"

# El driver escribe «hecho …» al final de las dos tandas. Se espera a esa línea
# y no a que existan los .antgenome: un pool aparece al acabar su tirada y la
# segunda tanda aún no habría empezado.
ESPERA_MAX=$((40 * 3600))
ESPERADO=0
while [ "$ESPERADO" -lt "$ESPERA_MAX" ]; do
  if grep -q '^hecho' "$DRIVER" 2>/dev/null; then
    echo "entrenamiento terminado: $(grep '^hecho' "$DRIVER") — bench $(date +%F_%H:%M:%S)" >> "$LOG"
    break
  fi
  sleep 60
  ESPERADO=$((ESPERADO + 60))
done
if [ "$ESPERADO" -ge "$ESPERA_MAX" ]; then
  echo "aviso: el entrenamiento no dio señales en 40 h; NO se produce reparto $(date +%F_%H:%M:%S)" >> "$LOG"
  exit 1
fi

PUERTA=(p25h2g-s7=artifacts/pretrain-partida-200-p25h2g7-red.antgenome
        p25h2g-s11=artifacts/pretrain-partida-200-p25h2g11-red.antgenome
        p25h2g-s13=artifacts/pretrain-partida-200-p25h2g13-red.antgenome)
REFLEJO=(p25h2s-s7=artifacts/pretrain-partida-200-p25h2s7-fijo.antgenome
         p25h2s-s11=artifacts/pretrain-partida-200-p25h2s11-fijo.antgenome
         p25h2s-s13=artifacts/pretrain-partida-200-p25h2s13-fijo.antgenome)
for spec in "${PUERTA[@]}" "${REFLEJO[@]}" \
            artifacts/pretrain-partida-200-p10g7-replicas.log \
            artifacts/pretrain-partida-200-p10x10-replicas.log \
            artifacts/pretrain-partida-200-p10h2g-replicas.log \
            artifacts/pretrain-partida-200-p10h2s-replicas.log \
            artifacts/pretrain-partida-200-p25g7-replicas.log \
            artifacts/pretrain-partida-200-p257-replicas.log; do
  [ -f "${spec#*=}" ] || { echo "falta ${spec#*=}" >> "$LOG"; exit 2; }
done

JOBS="${JOBS:-12}"
STATUS=0
python scripts/gland-bench.py --no-build --pools "${PUERTA[@]}" --ref p25h2g-s7 \
  --prices 10,25 --jobs "$JOBS" --recipe "48k 25s con puerta" \
  --out artifacts/pretrain-partida-200-p25h2g-replicas.txt \
  > artifacts/pretrain-partida-200-p25h2g-replicas.log 2>&1 || STATUS=1
python scripts/gland-bench.py --no-build --pools "${REFLEJO[@]}" --ref p25h2s-s7 \
  --prices 10,25 --jobs "$JOBS" --recipe "48k 25s con reflejo" \
  --out artifacts/pretrain-partida-200-p25h2s-replicas.txt \
  > artifacts/pretrain-partida-200-p25h2s-replicas.log 2>&1 || STATUS=1

if [ "$STATUS" -ne 0 ]; then
  echo "el bench falló; NO se produce reparto $(date +%F_%H:%M:%S)" >> "$LOG"
  exit 1
fi

# El reparto de las cuatro casillas. Los brazos van explícitos aunque el
# comparador podría deducirlos: escrito así, si un volcado pierde una casilla el
# guion falla en vez de repartir tres con dos sin decirlo.
python scripts/gland-matrix-compare.py \
  --arm "10s/24k=puerta:artifacts/pretrain-partida-200-p10g7-replicas.log" \
  --arm "10s/24k=reflejo:artifacts/pretrain-partida-200-p10x10-replicas.log" \
  --arm "10s/48k=puerta:artifacts/pretrain-partida-200-p10h2g-replicas.log" \
  --arm "10s/48k=reflejo:artifacts/pretrain-partida-200-p10h2s-replicas.log" \
  --arm "25s/24k=puerta:artifacts/pretrain-partida-200-p25g7-replicas.log" \
  --arm "25s/24k=reflejo:artifacts/pretrain-partida-200-p257-replicas.log" \
  --arm "25s/48k=puerta:artifacts/pretrain-partida-200-p25h2g-replicas.log" \
  --arm "25s/48k=reflejo:artifacts/pretrain-partida-200-p25h2s-replicas.log" \
  --out artifacts/pretrain-partida-200-p25h2-veredicto.txt >> "$LOG" 2>&1 || STATUS=1

echo "reparto hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$LOG"
exit "$STATUS"
