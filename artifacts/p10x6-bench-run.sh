#!/usr/bin/env bash
# F11i — juega el brazo de entrenamiento a 10 s con las 24 semillas de partida que
# no vio ninguna tirada, a los dos precios de juego y con las dos formas de
# decidir el depósito, ya con las seis tiradas nuevas (41/43/47 en las dos
# recetas).
#
# Dos campañas, una por receta, porque `scripts/gland-recipe-compare.py` compara
# las réplicas de un log contra las del otro: la del reflejo lleva DIEZ pools
# (7 de F11h + 3 nuevas) y la de la puerta SIETE (la semilla 7 de F11c + 3 de
# F11h + 3 nuevas). 10 + 7 pools × 2 precios × 2 puertas × 24 semillas = 1 632
# partidas.
#
# El volcado anterior (p107 / p10g4) se conserva: lo que cambia es el tamaño de
# la muestra, no la receta con la que se jugó.
set -euo pipefail
cd "$(dirname "$0")/.."

P10=(p10-s7=artifacts/pretrain-partida-200-p10-fijo.antgenome
     p10-s11=artifacts/pretrain-partida-200-p10s11-fijo.antgenome
     p10-s13=artifacts/pretrain-partida-200-p10s13-fijo.antgenome
     p10-s23=artifacts/pretrain-partida-200-p10s23-fijo.antgenome
     p10-s29=artifacts/pretrain-partida-200-p10s29-fijo.antgenome
     p10-s31=artifacts/pretrain-partida-200-p10s31-fijo.antgenome
     p10-s37=artifacts/pretrain-partida-200-p10s37-fijo.antgenome
     p10-s41=artifacts/pretrain-partida-200-p10s41-fijo.antgenome
     p10-s43=artifacts/pretrain-partida-200-p10s43-fijo.antgenome
     p10-s47=artifacts/pretrain-partida-200-p10s47-fijo.antgenome)
P10G=(p10g-s7=artifacts/pretrain-partida-200-p10-red.antgenome
      p10g-s29=artifacts/pretrain-partida-200-p10g29-red.antgenome
      p10g-s31=artifacts/pretrain-partida-200-p10g31-red.antgenome
      p10g-s37=artifacts/pretrain-partida-200-p10g37-red.antgenome
      p10g-s41=artifacts/pretrain-partida-200-p10g41-red.antgenome
      p10g-s43=artifacts/pretrain-partida-200-p10g43-red.antgenome
      p10g-s47=artifacts/pretrain-partida-200-p10g47-red.antgenome)

for spec in "${P10[@]}" "${P10G[@]}"; do
  [ -f "${spec#*=}" ] || { echo "falta el pool ${spec#*=}" >&2; exit 2; }
done

# `--no-build`: las tres tiradas del control de F11j tienen cargado el antsim.dll
# y `dotnet build` no puede copiarlo (MSB3021). No hace falta compilar —ninguna
# fuente de C# cambió desde el binario— y que el DLL es el mismo que produjo la
# evidencia de F11h se comprueba reproduciendo una de sus partidas publicadas:
# p10-s7 · 10 s · puerta · semilla 42 → 129 descargas (y da 129).
JOBS="${JOBS:-12}"
python scripts/gland-bench.py --no-build --pools "${P10[@]}" --ref p10-s7 --prices 10,25 --jobs "$JOBS" \
  --out artifacts/pretrain-partida-200-p10x10-replicas.txt \
  > artifacts/pretrain-partida-200-p10x10-replicas.log 2>&1
python scripts/gland-bench.py --no-build --pools "${P10G[@]}" --ref p10g-s7 --prices 10,25 --jobs "$JOBS" \
  --out artifacts/pretrain-partida-200-p10g7-replicas.txt \
  > artifacts/pretrain-partida-200-p10g7-replicas.log 2>&1
echo "bench hecho $(date +%F_%H:%M:%S)"
