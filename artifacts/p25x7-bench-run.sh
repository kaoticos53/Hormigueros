#!/usr/bin/env bash
# F11g — juega las dos campañas de SIETE tiradas (25 s) con las 24 semillas de
# partida que no vio ninguna, a los dos precios de juego y con las dos formas de
# decidir el depósito: 14 pools × 2 precios × 2 puertas × 24 semillas = 1 344
# partidas. Una campaña por receta, en su propio log, porque
# `scripts/gland-recipe-compare.py` compara las réplicas de un log contra las del
# otro (los logs de cuatro tiradas de F11d/F11f se conservan).
set -euo pipefail
cd "$(dirname "$0")/.."

P25G=(p25g-s7=artifacts/pretrain-partida-200-gl-red.antgenome
      p25g-s11=artifacts/pretrain-partida-200-p25g11-red.antgenome
      p25g-s13=artifacts/pretrain-partida-200-p25g13-red.antgenome
      p25g-s23=artifacts/pretrain-partida-200-p25g23-red.antgenome
      p25g-s29=artifacts/pretrain-partida-200-p25g29-red.antgenome
      p25g-s31=artifacts/pretrain-partida-200-p25g31-red.antgenome
      p25g-s37=artifacts/pretrain-partida-200-p25g37-red.antgenome)
P25=(p25-s7=artifacts/pretrain-partida-200-gl-fijo.antgenome
     p25-s11=artifacts/pretrain-partida-200-p25s11-fijo.antgenome
     p25-s13=artifacts/pretrain-partida-200-p25s13-fijo.antgenome
     p25-s23=artifacts/pretrain-partida-200-p25s23-fijo.antgenome
     p25-s29=artifacts/pretrain-partida-200-p25s29-fijo.antgenome
     p25-s31=artifacts/pretrain-partida-200-p25s31-fijo.antgenome
     p25-s37=artifacts/pretrain-partida-200-p25s37-fijo.antgenome)

for spec in "${P25G[@]}" "${P25[@]}"; do
  [ -f "${spec#*=}" ] || { echo "falta el pool ${spec#*=}" >&2; exit 2; }
done

JOBS="${JOBS:-12}"
python scripts/gland-bench.py --pools "${P25G[@]}" --ref p25g-s7 --prices 10,25 --jobs "$JOBS" \
  --out artifacts/pretrain-partida-200-p25g7-replicas.txt \
  > artifacts/pretrain-partida-200-p25g7-replicas.log 2>&1
python scripts/gland-bench.py --pools "${P25[@]}" --ref p25-s7 --prices 10,25 --jobs "$JOBS" \
  --out artifacts/pretrain-partida-200-p257-replicas.txt \
  > artifacts/pretrain-partida-200-p257-replicas.log 2>&1
echo "bench hecho $(date +%F_%H:%M:%S)"
