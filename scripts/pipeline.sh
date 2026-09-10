#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# pipeline.sh — Pipeline de pre-entrenamiento encadenado (Fases 3/3bis/3ter)
#
#   pretrain EN FRÍO (población aleatoria)
#     → warm-start de REFINADO (continúa desde el pool en frío, banda opcional)
#     → revalidación MULTI-SEMILLA con --seed-pool
#       (baseline sin sembrar vs pool en frío vs pool refinado), TABLA final.
#
# Uso:
#   scripts/pipeline.sh [--help]
#   scripts/pipeline.sh [--pop N] [--gens N] [--seed-pretrain N]
#                       [--band-min F] [--band-max F]
#                       [--ticks N] [--seeds "42 7 99"] [--out DIR]
#
# Parámetros:
#   --pop N          población del entrenamiento (def. 24)
#   --gens N         generaciones máx POR ETAPA (3 etapas ⇒ 3×N; def. 10).
#                    --gens 60 con banda 200–260 replica el pool de referencia
#                    de 60 gens (competente); 10 es un humo rápido (~15–20 min
#                    el pipeline completo con los def. por defecto).
#   --seed-pretrain N  semilla del entrenamiento (def. 4242)
#   --band-min/max F   re-banda todas las etapas (def. 200–260, lo calibrado;
#                    p. ej. --band-max 350 refina contra un anillo ~3× mayor)
#   --ticks N        ticks de cada partida de revalidación (def. 24000 = 800 s)
#   --seeds "S..."   semillas de revalidación (def. "42 7 99 1234 777")
#   --out DIR        carpeta de salida (def. artifacts/)
#
# Determinista: mismas semillas + mismos parámetros ⇒ mismos resultados y la
# misma tabla (el mundo es byte a byte idéntico entre procesos).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# ── Parámetros ────────────────────────────────────────────────────────────────
OUT="$ROOT/artifacts"
SEED_PRETRAIN=4242
POP=24
GENS=10
BAND_MIN=200
BAND_MAX=260
EVOLVE_TICKS=24000
EVOLVE_SEEDS="42 7 99 1234 777"
# Nombres propios del pipeline (no pisan pools validados como
# pretrain-warm.antgenome/pretrain-warm2.antgenome de vueltas anteriores).
COLD_POOL="pipe-cold"
WARM_POOL="pipe-warm"

usage() { sed -n '2,24p' "$0"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --help|-h) usage; exit 0 ;;
        --pop) POP="$2"; shift 2 ;;
        --gens) GENS="$2"; shift 2 ;;
        --seed-pretrain) SEED_PRETRAIN="$2"; shift 2 ;;
        --band-min) BAND_MIN="$2"; shift 2 ;;
        --band-max) BAND_MAX="$2"; shift 2 ;;
        --ticks) EVOLVE_TICKS="$2"; shift 2 ;;
        --seeds) EVOLVE_SEEDS="$2"; shift 2 ;;
        --out) OUT="$2"; shift 2 ;;
        *) echo "Argumento desconocido: $1" >&2; usage >&2; exit 2 ;;
    esac
done

mkdir -p "$OUT"
read -r -a SEEDS <<< "$EVOLVE_SEEDS"

echo "== 0) build"
dotnet build "$ROOT/src/Tools/AntSim.Cli" -c Debug --nologo -v q 2>&1 | tail -1

# ── Entrenamiento ─────────────────────────────────────────────────────────────
# $1 = etiqueta (nombre de archivo), resto = flags extra (p. ej. --warm-start)
run_pretrain() {
    local label="$1"; shift
    echo "== pretrain: $label (seed $SEED_PRETRAIN, pop $POP, ${GENS} gens/etapa, banda $BAND_MIN-$BAND_MAX)"
    (cd "$ROOT" && dotnet run --project src/Tools/AntSim.Cli -c Debug --no-build -- \
        --mode pretrain --seed "$SEED_PRETRAIN" --pop "$POP" --generations "$GENS" \
        --band-min "$BAND_MIN" --band-max "$BAND_MAX" "$@" \
        --export "$OUT/$label.antgenome") > "$OUT/$label.txt" 2>&1
}

start=$SECONDS
run_pretrain "$COLD_POOL"
run_pretrain "$WARM_POOL" --warm-start "$OUT/$COLD_POOL.antgenome"

# ── Revalidación ──────────────────────────────────────────────────────────────
# $1 = seed, $2 = pool (vacío ⇒ baseline sin sembrar); imprime la línea totals.
run_evolve() {
    local seed="$1" pool="$2"
    local out
    if [[ -n "$pool" ]]; then
        out="$(cd "$ROOT" && dotnet run --project src/Tools/AntSim.Cli -c Debug --no-build -- \
            --mode evolve --seed "$seed" --ticks "$EVOLVE_TICKS" --seed-pool "$pool" 2>&1)" || true
    else
        out="$(cd "$ROOT" && dotnet run --project src/Tools/AntSim.Cli -c Debug --no-build -- \
            --mode evolve --seed "$seed" --ticks "$EVOLVE_TICKS" 2>&1)" || true
    fi
    # El CLI escribe `final-hash` ANTES que `totals`; acumular en END hace el
    # parse independiente del orden de las líneas.
    echo "$out" | awk '/^totals / { for (i = 1; i <= NF; i++) {
            if      ($i == "pickup") p = $(i+1)
            else if ($i == "unload") u = $(i+1)
            else if ($i == "eclosed") e = $(i+1)
            else if ($i == "died")   d = $(i+1)
            else if ($i == "eggs")   g = $(i+1)
            else if ($i == "first-unload") fu = $(i+1)
            else if ($i == "drop-avg") da = $(i+1) }
        t = p " " u " " e " " d " " g " " fu " " da }
        /^final-hash / { h = substr($2, 1, 8) }
        END { print t, h }'
}

# Declarar el mapa pool|seed → métricas y la lista de pools.
declare -A PICK UNL ECL DIE EGG HASH FU DA
POOLS=("baseline" "$COLD_POOL" "$WARM_POOL")

echo "== revalidación --seed-pool (ticks $EVOLVE_TICKS, semillas: ${SEEDS[*]})"
for pool in "${POOLS[@]}"; do
    for seed in "${SEEDS[@]}"; do
        if [[ "$pool" == "baseline" ]]; then
            read -r p u e d g fu da h <<< "$(run_evolve "$seed" "")"
        else
            read -r p u e d g fu da h <<< "$(run_evolve "$seed" "$OUT/$pool.antgenome")"
        fi
        PICK["$pool|$seed"]=$p; UNL["$pool|$seed"]=$u; ECL["$pool|$seed"]=$e
        DIE["$pool|$seed"]=$d;  EGG["$pool|$seed"]=$g; HASH["$pool|$seed"]=$h
        FU["$pool|$seed"]=$fu;  DA["$pool|$seed"]=$da
    done
done

# ── Salida ────────────────────────────────────────────────────────────────────
echo
echo "== Entrenamiento (mejor fitness por etapa, última generación) =="
for label in "$COLD_POOL" "$WARM_POOL"; do
    echo "--- $label"
    grep "^gen " "$OUT/$label.txt" | tail -3 | \
        sed -E 's/^gen stage=([0-9]+) gen=([0-9]+) best=([0-9.]+) mean=([0-9.]+) competent=([0-9]+)/  etapa \1 gen \2: best=\3 mean=\4 competentes=\5/'
done

echo
echo "== Revalidación multi-semilla (--seed-pool, ticks $EVOLVE_TICKS) =="
printf "%-14s %-6s %6s %6s %6s %6s %6s %7s %7s   %s\n" pool seed pickup unload eclosed died eggs unload1st dropavg hash
for pool in "${POOLS[@]}"; do
    for seed in "${SEEDS[@]}"; do
        printf "%-14s %-6s %6d %6d %6d %6d %6d %7s %7s   %s\n" \
            "$pool" "$seed" \
            "${PICK[$pool|$seed]}" "${UNL[$pool|$seed]}" "${ECL[$pool|$seed]}" \
            "${DIE[$pool|$seed]}" "${EGG[$pool|$seed]}" \
            "${FU[$pool|$seed]}" "${DA[$pool|$seed]}" "${HASH[$pool|$seed]}"
    done
done

echo
echo "== Resumen (totales sobre ${#SEEDS[@]} semillas) =="
for pool in "${POOLS[@]}"; do
    tp=0; tu=0; te=0; with_unload=0
    for seed in "${SEEDS[@]}"; do
        tp=$((tp + PICK[$pool|$seed]))
        tu=$((tu + UNL[$pool|$seed]))
        te=$((te + ECL[$pool|$seed]))
        (( UNL[$pool|$seed] >= 1 )) && with_unload=$((with_unload + 1))
    done
    printf "%-10s pickups %3d | descargas %3d (%d/%d semillas con descarga) | eclosiones %3d\n" \
        "$pool" "$tp" "$tu" "$with_unload" "${#SEEDS[@]}" "$te"
done

echo
echo "== fin ($((SECONDS - start)) s); pools: $OUT/$COLD_POOL.antgenome, $OUT/$WARM_POOL.antgenome"