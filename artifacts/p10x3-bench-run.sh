#!/usr/bin/env bash
# F11h — juega los pools del brazo de entrenamiento a 10 s con las 24 semillas
# de partida que no vio ninguna tirada, a los dos precios de juego y con las dos
# formas de decidir el depósito.
#
# Dos campañas, una por receta, porque `scripts/gland-recipe-compare.py` compara
# las réplicas de un log contra las del otro: la del reflejo lleva SIETE pools
# (4 de F11d + 3 nuevas, pareado 7↔7 con el brazo de 25 s de F11g) y la de la
# puerta CUATRO (la semilla 7 de F11c + 3 nuevas), que es todo lo que existe a
# 10 s. 7 + 4 pools × 2 precios × 2 puertas × 24 semillas = 1 056 partidas.
# Los logs de la campaña de cuatro tiradas (p10-replicas) se conservan.
set -euo pipefail
cd "$(dirname "$0")/.."

P10=(p10-s7=artifacts/pretrain-partida-200-p10-fijo.antgenome
     p10-s11=artifacts/pretrain-partida-200-p10s11-fijo.antgenome
     p10-s13=artifacts/pretrain-partida-200-p10s13-fijo.antgenome
     p10-s23=artifacts/pretrain-partida-200-p10s23-fijo.antgenome
     p10-s29=artifacts/pretrain-partida-200-p10s29-fijo.antgenome
     p10-s31=artifacts/pretrain-partida-200-p10s31-fijo.antgenome
     p10-s37=artifacts/pretrain-partida-200-p10s37-fijo.antgenome)
P10G=(p10g-s7=artifacts/pretrain-partida-200-p10-red.antgenome
      p10g-s29=artifacts/pretrain-partida-200-p10g29-red.antgenome
      p10g-s31=artifacts/pretrain-partida-200-p10g31-red.antgenome
      p10g-s37=artifacts/pretrain-partida-200-p10g37-red.antgenome)

for spec in "${P10[@]}" "${P10G[@]}"; do
  [ -f "${spec#*=}" ] || { echo "falta el pool ${spec#*=}" >&2; exit 2; }
done

JOBS="${JOBS:-12}"
python scripts/gland-bench.py --pools "${P10[@]}" --ref p10-s7 --prices 10,25 --jobs "$JOBS" \
  --out artifacts/pretrain-partida-200-p107-replicas.txt \
  > artifacts/pretrain-partida-200-p107-replicas.log 2>&1
python scripts/gland-bench.py --pools "${P10G[@]}" --ref p10g-s7 --prices 10,25 --jobs "$JOBS" \
  --out artifacts/pretrain-partida-200-p10g4-replicas.txt \
  > artifacts/pretrain-partida-200-p10g4-replicas.log 2>&1
echo "bench hecho $(date +%F_%H:%M:%S)"
