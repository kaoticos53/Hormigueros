#!/usr/bin/env bash
# F11o — la medida del contrafactual: ¿cuánto del +47,2 es el precio y cuánto el
# entrenamiento? Tres familias de pools en la partida del lanzador (grid 96,
# laberinto, reclutamiento, 216 000 ticks, las 24 semillas que ninguna tirada
# vio), cada una jugada a los dos precios y con las dos formas de decidir:
#
#   fabrica   el pool de fábrica (`pretrain-maze-los3`), el juego de hoy
#   p0x-s*    las SIETE tiradas de 200 generaciones SIN precio y SIN puerta
#   p25-s*    las SIETE tiradas de la receta del reflejo CON el precio de 25 s
#
# Leído a glándula 0 y rastro fijo: fabrica → p0x = lo que paga reentrenar sin
# precio; p0x → p25 = lo que paga además entrenar con el precio encendido.
#
# `--no-build`: el antsim.dll de Release lo tiene cargado la tirada de 200
# generaciones y el build no podría copiarlo.
set -euo pipefail
cd "$(dirname "$0")/.."

P0X=(p0x-s7=artifacts/pretrain-partida-200-p0x7-fijo.antgenome
     p0x-s11=artifacts/pretrain-partida-200-p0x11-fijo.antgenome
     p0x-s13=artifacts/pretrain-partida-200-p0x13-fijo.antgenome
     p0x-s23=artifacts/pretrain-partida-200-p0x23-fijo.antgenome
     p0x-s29=artifacts/pretrain-partida-200-p0x29-fijo.antgenome
     p0x-s31=artifacts/pretrain-partida-200-p0x31-fijo.antgenome
     p0x-s37=artifacts/pretrain-partida-200-p0x37-fijo.antgenome)
P25=(p25-s7=artifacts/pretrain-partida-200-gl-fijo.antgenome
     p25-s11=artifacts/pretrain-partida-200-p25s11-fijo.antgenome
     p25-s13=artifacts/pretrain-partida-200-p25s13-fijo.antgenome
     p25-s23=artifacts/pretrain-partida-200-p25s23-fijo.antgenome
     p25-s29=artifacts/pretrain-partida-200-p25s29-fijo.antgenome
     p25-s31=artifacts/pretrain-partida-200-p25s31-fijo.antgenome
     p25-s37=artifacts/pretrain-partida-200-p25s37-fijo.antgenome)

for spec in "${P0X[@]}" "${P25[@]}"; do
  [ -f "${spec#*=}" ] || { echo "falta el pool ${spec#*=}" >&2; exit 2; }
done

python scripts/gland-bench.py --no-build --jobs "${JOBS:-12}" --prices 0,25 \
  --pools fabrica=artifacts/pretrain-maze-los3.antgenome \
          "${P0X[@]}" "${P25[@]}" \
  --recipe "sin precio y sin puerta (contrafactual de F11n)" \
  --ref p0x-s7 \
  --out artifacts/pretrain-partida-200-p0x-bench.txt \
  > artifacts/pretrain-partida-200-p0x-bench.log 2>&1
echo "bench hecho $(date +%F_%H:%M:%S) → artifacts/pretrain-partida-200-p0x-bench.txt"
