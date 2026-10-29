#!/usr/bin/env bash
# F11j — el bench y el veredicto del control del horizonte, en cuanto el
# entrenamiento termine.
#
# QUÉ MIDE. Los seis pools de 48 000 ticks (tres entrenados con la puerta y tres
# con el reflejo, semillas de GA 41/43/47) contra los de 24 000 ticks que ya
# están medidos en F11i (`…-p10g7-replicas.log` y `…-p10x10-replicas.log`). El
# precio de entrenamiento es el mismo en los dos horizontes —glándula de 10 s—,
# así que lo único que cambia entre las dos filas es la VENTANA que miró la
# selección: 400 s contra 800 s de partida.
#
# POR QUÉ SÓLO SE JUZGAN LOS SEIS NUEVOS. Las dos campañas de 24 000 ticks ya
# están jugadas con las mismas 24 semillas de partida; repetirlas costaría 1 632
# partidas para volver a dar las mismas cifras. Aquí son 6 pools × 2 precios ×
# 2 rastros × 24 semillas = 576 partidas.
#
# POR QUÉ `--no-build`. El bench corre mientras el CLI puede seguir cargado; ya
# se comprobó en F11i que el `antsim.dll` de Release reproduce las partidas
# publicadas, y no cambia ninguna fuente de C# entre esas tiradas y estas.
#
# SI EL ENTRENAMIENTO NO DA SEÑALES, NO HAY VEREDICTO: un veredicto sobre
# volcados a medias es peor que ninguno, y este guion lo dice en su log en vez
# de escribirlo.
set -uo pipefail
cd "$(dirname "$0")/.."

DRIVER=artifacts/pretrain-partida-200-p10h2-driver.log
LOG=artifacts/pretrain-partida-200-p10h2-verdict.log
: > "$LOG"
echo "espera: $DRIVER" >> "$LOG"

# El driver del control escribe «hecho …» al final de las dos tandas. Se espera
# a esa línea y no a que existan los .antgenome: un pool aparece al acabar su
# tirada y la segunda tanda aún no habría empezado.
ESPERA_MAX=$((20 * 3600))
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
  echo "aviso: el control no dio señales en 20 h; NO se produce veredicto $(date +%F_%H:%M:%S)" >> "$LOG"
  exit 1
fi

PUERTA=(p10h2g-s41=artifacts/pretrain-partida-200-p10h2g41-red.antgenome
        p10h2g-s43=artifacts/pretrain-partida-200-p10h2g43-red.antgenome
        p10h2g-s47=artifacts/pretrain-partida-200-p10h2g47-red.antgenome)
REFLEJO=(p10h2s-s41=artifacts/pretrain-partida-200-p10h2s41-fijo.antgenome
         p10h2s-s43=artifacts/pretrain-partida-200-p10h2s43-fijo.antgenome
         p10h2s-s47=artifacts/pretrain-partida-200-p10h2s47-fijo.antgenome)
for spec in "${PUERTA[@]}" "${REFLEJO[@]}" \
            artifacts/pretrain-partida-200-p10g7-replicas.log \
            artifacts/pretrain-partida-200-p10x10-replicas.log; do
  [ -f "${spec#*=}" ] || { echo "falta ${spec#*=}" >> "$LOG"; exit 2; }
done

# Una campaña por receta, como en F11i: así el volcado de cada brazo se lee
# solo y el comparador recibe un log por brazo y horizonte.
JOBS="${JOBS:-12}"
STATUS=0
python scripts/gland-bench.py --no-build --pools "${PUERTA[@]}" --ref p10h2g-s41 \
  --prices 10,25 --jobs "$JOBS" --recipe "48k con puerta" \
  --out artifacts/pretrain-partida-200-p10h2g-replicas.txt \
  > artifacts/pretrain-partida-200-p10h2g-replicas.log 2>&1 || STATUS=1
python scripts/gland-bench.py --no-build --pools "${REFLEJO[@]}" --ref p10h2s-s41 \
  --prices 10,25 --jobs "$JOBS" --recipe "48k con reflejo" \
  --out artifacts/pretrain-partida-200-p10h2s-replicas.txt \
  > artifacts/pretrain-partida-200-p10h2s-replicas.log 2>&1 || STATUS=1

if [ "$STATUS" -ne 0 ]; then
  echo "el bench falló; NO se produce veredicto $(date +%F_%H:%M:%S)" >> "$LOG"
  exit 1
fi

# El veredicto. Los sorteos van explícitos aunque el comparador los deduciría
# del cruce de los cuatro brazos: escrito así, si un volcado pierde un sorteo
# el guion falla en vez de comparar tres con dos sin decirlo.
python scripts/gland-horizon-compare.py \
  --arm "24k=puerta:artifacts/pretrain-partida-200-p10g7-replicas.log" \
  --arm "24k=reflejo:artifacts/pretrain-partida-200-p10x10-replicas.log" \
  --arm "48k=puerta:artifacts/pretrain-partida-200-p10h2g-replicas.log" \
  --arm "48k=reflejo:artifacts/pretrain-partida-200-p10h2s-replicas.log" \
  --ga-seeds 41,43,47 \
  --out artifacts/pretrain-partida-200-p10h2-veredicto.txt >> "$LOG" 2>&1 || STATUS=1

echo "veredicto hecho $(date +%F_%H:%M:%S) status=$STATUS" >> "$LOG"
exit "$STATUS"
