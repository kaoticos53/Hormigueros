#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# system-sweep.sh — el precio del tick según las COLONIAS POR VISTA (F5.3 §4.10).
#
# QUÉ RESPONDE. La medida del sistema (player-perf.sh --system) da el coste del
# mundo a 2 colonias por vista: 0,201 ms/tick → 2,485 núcleos al reloj del juego.
# Eso no dice cuánto JUEGO cabe: falta saber cómo escala el precio del tick con
# las colonias. Este script corre el MISMO montaje (4 vistas, grid 256, horizonte
# 12 000, boost ×10) con 2, 4 y 8 colonias por vista y publica la tabla, más los
# dos números derivados que se pueden leer sin hacer cuentas: NÚCLEOS POR COLONIA
# y CUÁNTAS COLONIAS CABEN en un presupuesto de núcleos dado.
#
# QUÉ MIDE CADA COLUMNA. No son la misma cosa y por eso van separadas:
#   · ms/tick      — la pata del MUNDO: toda la CPU de los CLI entre todos los
#                    ticks que simularon (agregado, no mediana de ventanas: su
#                    CPU se gasta antes de que el stream llegue, §4.9).
#   · mundo ms/frame — esos ticks costeados al reloj del juego (4 vistas × 50).
#   · player ms/frame— la pata de la VISTA con el mundo cargado.
#   · núcleos      — las dos patas juntas contra el presupuesto de 16,667 ms. Es
#                    un coste AGREGADO, repartible entre procesos: se compara con
#                    núcleos, no con la latencia de un frame.
#
# CADA PUNTO ES UN BUILD. Las colonias van dentro de la escena, así que el barrido
# reconstruye el player en cada paso (~20 s) y cada corrida dura ~45 s. Los
# informes de cada punto se conservan (artifacts/system-c<N>.json) porque son la
# evidencia cruda; el resumen va a artifacts/system-sweep.txt.
#
# OJO CON EL ÁRBOL: los builds regeneran la escena y sus materiales (artefactos
# del bootstrapper, de contenido idéntico). Al terminar, este script lo avisa con
# el comando para dejarlo limpio.
#
# Uso:
#   scripts/system-sweep.sh                     # 2, 4 y 8 colonias por vista
#   scripts/system-sweep.sh --colonies 1,2,4
#   scripts/system-sweep.sh --cores 8           # colonias que caben en 8 núcleos
#   scripts/system-sweep.sh --ticks 6000 --warmup 1 --seconds 40
#   scripts/system-sweep.sh --unity RUTA        # editor concreto
#   scripts/system-sweep.sh --selftest          # verifica la tabla (sin Unity)
#
# Salida: 0 si TODOS los puntos midieron · 1 si alguno no midió o suspendió ·
# 2 uso · 3 sin editor de Unity. El código del punto que falle se conserva en su
# log (artifacts/system-c<N>.log) y en la columna de estado de la tabla.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIST="2,4,8"
GRID=256
CORES=""
TICKS=""
SECONDS_WINDOW=""
WARMUP=""
UNITY=""
QUIET=0
SELFTEST=0
OUT="$ROOT/artifacts/system-sweep.txt"
REPORT="$ROOT/artifacts/perf-player-system.json"

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --colonies) LIST="$2"; shift 2 ;;
    --grid)     GRID="$2"; shift 2 ;;
    --cores)    CORES="$2"; shift 2 ;;
    --ticks)    TICKS="$2"; shift 2 ;;
    --seconds)  SECONDS_WINDOW="$2"; shift 2 ;;
    --warmup)   WARMUP="$2"; shift 2 ;;
    --unity)    UNITY="$2"; shift 2 ;;
    --out)      OUT="$2"; shift 2 ;;
    --quiet)    QUIET=1; shift ;;
    --selftest) SELFTEST=1; shift ;;
    --help|-h)  sed -n '2,43p' "$0"; exit 0 ;;
    *) echo "✗ parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

# ── Analizador de la tabla (aislado: testeable sin Unity) ─────────────────────
# El informe escribe una clave por línea: aquí se extrae el valor crudo (sin
# comas ni comillas), igual que hace player-perf.sh con su propia evidencia.
jget() {
  sed -n "s/^ *\"$2\": *//p" "$1" | head -n 1 | tr -d '\r' \
    | sed 's/,$//' | sed 's/^"//; s/"$//'
}

# sweep_row <json> <cores> → "colonias|msTick|mundo|player|nucleos|nucleosPorColonia|caben|medido|estado"
sweep_row() {
  local f="$1" cores="$2"
  local c v mstick world player sys measured
  c="$(jget "$f" coloniesPerView)"
  v="$(jget "$f" views)"
  mstick="$(jget "$f" cliMsPerTick)"
  world="$(jget "$f" cliMsPerFrame)"
  player="$(jget "$f" systemPlayerMsPerFrame)"
  sys="$(jget "$f" systemCores)"
  measured="$(jget "$f" systemMeasured)"
  awk -v c="$c" -v v="$v" -v m="$mstick" -v w="$world" -v p="$player" -v s="$sys" \
      -v cores="$cores" -v ok="$measured" 'BEGIN{
    n = (v > 0) ? v * c : c;                    # colonias en pantalla
    pc = (n > 0 && s > 0) ? s / n : 0;          # núcleos por colonia
    fit = (pc > 0) ? cores / pc : 0;            # colonias que caben en `cores`
    printf "%s|%.4f|%.3f|%.3f|%.3f|%.4f|%.0f|%s", c, m, w, p, s, pc, fit, ok
  }'
}

if [[ $SELFTEST -eq 1 ]]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  # Un punto medido, como lo escribe la sonda.
  cat > "$tmp/a.json" <<'JSON'
{
  "mode": "player-system",
  "views": 4,
  "coloniesPerView": 2,
  "cliMsPerTick": 0.201,
  "cliMsPerFrame": 40.299,
  "systemPlayerMsPerFrame": 1.116,
  "systemCores": 2.485,
  "systemMeasured": true
}
JSON
  got="$(sweep_row "$tmp/a.json" 28)"
  want="2|0.2010|40.299|1.116|2.485|0.3106|90|true"
  if [[ "$got" != "$want" ]]; then
    echo "✗ selftest: fila de 2 colonias dio «$got» (esperado «$want»)" >&2
    exit 1
  fi
  # Otro punto: 8 colonias, más caro. Los núcleos por colonia tienen que salir más
  # bajos que con 2 (el coste es sublineal: el grid se lleva una parte fija).
  sed 's/"coloniesPerView": 2/"coloniesPerView": 8/; s/"cliMsPerTick": 0.201/"cliMsPerTick": 0.633/; s/"cliMsPerFrame": 40.299/"cliMsPerFrame": 126.6/; s/"systemCores": 2.485/"systemCores": 7.71/' \
    "$tmp/a.json" > "$tmp/b.json"
  r2="$(sweep_row "$tmp/a.json" 28)"
  r8="$(sweep_row "$tmp/b.json" 28)"
  pc2="$(echo "$r2" | cut -d'|' -f6)"
  pc8="$(echo "$r8" | cut -d'|' -f6)"
  if ! awk -v a="$pc2" -v b="$pc8" 'BEGIN{exit !(b < a)}'; then
    echo "✗ selftest: los núcleos por colonia no bajaron al subir colonias ($pc2 → $pc8)" >&2
    exit 1
  fi
  # Un punto que NO midió: la tabla lo tiene que declarar, no inventar ceros.
  sed 's/"systemMeasured": true/"systemMeasured": false/; s/"systemCores": 2.485/"systemCores": 0/' \
    "$tmp/a.json" > "$tmp/c.json"
  rc8="$(sweep_row "$tmp/c.json" 28)"
  if [[ "$(echo "$rc8" | cut -d'|' -f8)" != "false" ]]; then
    echo "✗ selftest: un punto sin medir no se declaró" >&2; exit 1
  fi
  if [[ "$(echo "$rc8" | cut -d'|' -f7)" != "0" ]]; then
    echo "✗ selftest: un punto sin medir dio colonias que caben ≠ 0" >&2; exit 1
  fi
  echo "✓ selftest del barrido ok (fila medida, sublinealidad de los núcleos por colonia y punto sin medir)"
  exit 0
fi

if [[ -z "$CORES" ]]; then
  CORES="$(nproc 2>/dev/null || echo 4)"
fi

IFS=',' read -r -a POINTS <<< "$LIST"
[[ ${#POINTS[@]} -gt 0 ]] || { echo "✗ --colonies vacío" >&2; exit 2; }

mkdir -p "$ROOT/artifacts"
ROWS=()
FAILS=0
for n in "${POINTS[@]}"; do
  n="$(echo "$n" | tr -d ' ')"
  [[ "$n" =~ ^[0-9]+$ ]] || { echo "✗ colonias no numéricas: «$n»" >&2; exit 2; }
  log "▶ $n colonias por vista (4 vistas · grid $GRID · horizonte ${TICKS:-12000} ticks)"
  args=(--system --colonies "$n" --grid "$GRID")
  [[ -n "$TICKS" ]] && args+=(--ticks "$TICKS")
  [[ -n "$SECONDS_WINDOW" ]] && args+=(--seconds "$SECONDS_WINDOW")
  [[ -n "$WARMUP" ]] && args+=(--warmup "$WARMUP")
  [[ -n "$UNITY" ]] && args+=(--unity "$UNITY")
  logfile="$ROOT/artifacts/system-c$n.log"
  set +e
  bash "$ROOT/scripts/player-perf.sh" "${args[@]}" > "$logfile" 2>&1
  rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    FAILS=1
    log "  ✗ no midió (rc=$rc) · log: artifacts/system-c$n.log"
    ROWS+=("$n|—|—|—|—|—|—|false")
    continue
  fi
  cp "$REPORT" "$ROOT/artifacts/system-c$n.json"
  rm -f "$logfile"
  row="$(sweep_row "$ROOT/artifacts/system-c$n.json" "$CORES")"
  ROWS+=("$row")
  log "  ✓ $(echo "$row" | awk -F'|' '{printf "%s ms/tick · mundo %s ms/frame · player %s ms/frame · %s nucleos", $2, $3, $4, $5}')"
done

# ── La tabla ──────────────────────────────────────────────────────────────────
{
  echo "Barrido del precio del tick por COLONIAS POR VISTA (F5.3 §4.10)"
  echo "Montaje: 4 vistas · grid $GRID · horizonte ${TICKS:-12000} ticks · boost ×10 · vsync apagado"
  echo "Presupuesto de núcleos para «caben»: $CORES (los de esta máquina si no se pasa --cores)"
  echo
  printf '%-9s %-12s %-14s %-16s %-11s %-16s %-12s\n' \
    "colonias" "ms/tick" "mundo ms/fr" "player ms/fr" "núcleos" "núcleos/colonia" "caben(=$CORES)"
  for row in "${ROWS[@]}"; do
    echo "$row" | awk -F'|' '{
      printf "%-9s %-12s %-14s %-16s %-11s %-16s %-12s\n", $1, $2, $3, $4, $5, $6, $7
    }'
  done
  echo
  echo "Columnas: la pata del MUNDO (ms/tick, y esos ticks costeados a 200 ticks/frame),"
  echo "la pata de la VISTA (player ms/frame) y las dos juntas en núcleos al reloj del juego."
  echo "«núcleos/colonia» = núcleos del sistema / (vistas × colonias) — es un coste agregado,"
  echo "repartible entre procesos: se compara con núcleos, no con la latencia de un frame."
  echo "«caben» = los núcleos dados / núcleos por colonia, es decir cuántas colonias"
  echo "simultáneas caben en ese presupuesto al ritmo del juego (boost ×10)."
  echo
  echo "Informes crudos de cada punto: artifacts/system-c<N>.json"
  echo "Generado: $(date +%Y-%m-%d\ %H:%M)"
} | tee "$OUT"

log ""
log "   resumen: artifacts/system-sweep.txt · informes: artifacts/system-c<N>.json"
log "   OJO: cada punto reconstruye el player, así que la escena y los materiales"
log "   generados quedan «modificados» con contenido idéntico. Para dejarlo limpio:"
log "     git checkout -- src/App/AntSim.Unity/Assets/Materials \\"
log "                     src/App/AntSim.Unity/Assets/Scenes/MultiSim.unity"

if [[ $FAILS -ne 0 ]]; then
  echo "✗ algún punto del barrido no midió (ver artifacts/system-c<N>.log)" >&2
  exit 1
fi
exit 0
