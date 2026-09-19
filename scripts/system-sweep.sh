#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# system-sweep.sh — el precio del tick según las COLONIAS POR VISTA (F5.3 §4.10).
#
# QUÉ RESPONDE. La medida del sistema (player-perf.sh --system) da el coste del
# mundo a 2 colonias por vista: 0,201 ms/tick → 2,485 núcleos al reloj del juego.
# Eso no dice cuánto JUEGO cabe: falta saber cómo escala el precio del tick con
# las colonias. Este script corre el MISMO montaje (4 vistas, grid 256, horizonte
# 12 000, boost ×10) con 1, 2, 4 y 8 colonias por vista y publica la tabla, más los
# dos números derivados que se pueden leer sin hacer cuentas: NÚCLEOS POR COLONIA
# y CUÁNTAS COLONIAS CABEN en un presupuesto de núcleos dado.
#
# EL PUNTO DE 1 COLONIA ES LA PRUEBA DEL COSTE FIJO. Cuanto menos mundo hay, más
# pesa la parte que NO depende de las colonias (la difusión de las rejillas, la
# evaporación, los ítems): con 1 colonia por vista el fijo es ~3/4 del sistema, así
# que es donde el intercepto se puede medir en vez de extrapolar. El script publica
# además el AJUSTE (mínimos cuadrados) de los puntos medidos con sus residuos, la
# separación FIJO / MARGINAL, y la comprobación que de verdad lo pone a prueba:
# ajustar sin el punto menor y ver si lo predice. Todo eso es una INTERPRETACIÓN de
# la tabla (así se declara en la salida), no una ley medida.
#
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
#   scripts/system-sweep.sh                     # 1, 2, 4 y 8 colonias por vista
#   scripts/system-sweep.sh --colonies 1,2      # solo algunos puntos
#   scripts/system-sweep.sh --reuse             # sin Unity: reconstruye la tabla
#                                               # (y el ajuste) de los informes
#                                               # ya guardados en artifacts/
#   scripts/system-sweep.sh --unity RUTA        # editor concreto
#   scripts/system-sweep.sh --selftest          # verifica la tabla (sin Unity)
#
# Salida: 0 si TODOS los puntos midieron · 1 si alguno no midió o suspendió ·
# 2 uso · 3 sin editor de Unity. El código del punto que falle se conserva en su
# log (artifacts/system-c<N>.log) y en la columna de estado de la tabla.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIST="1,2,4,8"
GRID=256
CORES=""
TICKS=""
SECONDS_WINDOW=""
WARMUP=""
UNITY=""
QUIET=0
SELFTEST=0
REUSE=0
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
    --reuse)    REUSE=1; shift ;;
    --selftest) SELFTEST=1; shift ;;
    --help|-h)  sed -n '2,54p' "$0"; exit 0 ;;
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

# sweep_row <json> <cores> → "colonias|msTick|mundo|player|nucleos|nucleosPorColonia|caben|medido|coloniasTotales"
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
    printf "%s|%.4f|%.3f|%.3f|%.3f|%.4f|%.0f|%s|%.0f", c, m, w, p, s, pc, fit, ok, n
  }'
}

# sweep_fit — lee por stdin filas «n|nucleos|msTick» (puntos MEDIDOS) y publica
# el ajuste, la separación FIJO/MARGINAL y la prueba de la extrapolación.
sweep_fit() {
  awk -F'|' '
    function fit(xs, ys, kk,   i,mx,my,sxy,sxx) {
      mx=0; my=0
      for (i=1;i<=kk;i++) { mx+=xs[i]; my+=ys[i] }
      mx/=kk; my/=kk
      sxy=0; sxx=0
      for (i=1;i<=kk;i++) { sxy+=(xs[i]-mx)*(ys[i]-my); sxx+=(xs[i]-mx)*(xs[i]-mx) }
      FA = my - (sxy/sxx)*mx; FB = sxy/sxx
    }
    function maxdev(xs, ys, kk, a, b,   i,worst,d) {
      worst=0
      for (i=1;i<=kk;i++) {
        d = 100*(ys[i]-(a+b*xs[i]))/(a+b*xs[i]); if (d<0) d=-d
        if (d>worst) worst=d
      }
      return worst
    }
    { k++; N[k]=$1; C[k]=$2; M[k]=$3 }
    END {
      printf "\nAjuste por mínimos cuadrados de los puntos MEDIDOS (n = vistas × colonias por vista)\n"
      if (k < 2) {
        printf "  con %d punto(s) medidos no hay ajuste que publicar (miden la tabla y ya)\n", k
        exit
      }
      fit(N,C,k); ac=FA; bc=FB
      fit(N,M,k); am=FA; bm=FB
      printf "  núcleos = %.3f + %.4f × n          (residuo máximo %.1f %%)\n", ac, bc, maxdev(N,C,k,ac,bc)
      printf "  ms/tick = %.4f + %.5f × n   (residuo máximo %.1f %%)\n", am, bm, maxdev(N,M,k,am,bm)
      printf "\n  FIJO (no depende de las colonias): %.3f núcleos\n", ac
      printf "  MARGINAL: %.4f núcleos por colonia (%.5f ms/tick, o sea %.1f %% del tick)\n", \
             bc, bm, 100*bm/am
      printf "  «núcleos por colonia» = MARGINAL + FIJO/n: la curva por colonia ES esa\n"
      printf "  amortización del fijo, no una ley aparte:\n"
      printf "    %-4s %-10s %-10s %-9s %-12s %-10s\n", "n", "medido", "modelo", "desvío", "medido/colonia", "modelo/colonia"
      for (i=1;i<=k;i++) {
        mod = ac + bc*N[i]
        printf "    %-4s %-10.3f %-10.3f %+8.1f %%  %-12.4f %-10.4f\n", \
               N[i], C[i], mod, 100*(C[i]-mod)/mod, C[i]/N[i], mod/N[i]
      }
      if (k >= 3) {
        lo=1
        for (i=2;i<=k;i++) if (N[i]<N[lo]) lo=i
        kk=0
        for (i=1;i<=k;i++) if (i!=lo) { kk++; X[kk]=N[i]; Y[kk]=C[i] }
        fit(X,Y,kk); pred=FA+FB*N[lo]
        printf "\n  A la prueba: el ajuste SIN el punto menor (n=%s) predice %.3f y el medido es %.3f (%+.1f %%)\n", \
               N[lo], pred, C[lo], 100*(C[lo]-pred)/pred
      } else {
        printf "\n  A la prueba: hacen falta ≥3 puntos para ajustar sin el menor y predecirlo\n"
      }
      printf "\n  El ajuste es una INTERPRETACIÓN de la tabla; lo medido es la tabla. Con cuatro\n"
      printf "  vistas fijas, el fijo es CONSTANTE respecto a las colonias, pero este barrido\n"
      printf "  NO separa «fijo por mundo» de «fijo por montaje»: eso pide barrer las VISTAS.\n"
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
  want="2|0.2010|40.299|1.116|2.485|0.3106|90|true|8"
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
  # ── El ajuste: con los tres puntos del barrido de 2/4/8 tiene que reproducir lo
  #    medido, y ajustando SIN el punto de 1 colonia tiene que predecirlo cerca.
  #    Las cifras son las medidas reales de §4.10 (artifacts/system-c1|2|4|8.json).
  fit_rows() { printf '%s\n' "$@"; }
  f4="$(fit_rows "4|1.927|0.1550" "8|2.496|0.2020" "16|3.443|0.2810" "32|5.285|0.4330" | sweep_fit)"
  if ! echo "$f4" | grep -q "FIJO (no depende de las colonias): 1.507 núcleos"; then
    echo "✗ selftest del ajuste: el intercepto de los cuatro puntos no dio 1,507" >&2; exit 1
  fi
  if ! echo "$f4" | grep -q "MARGINAL: 0.1187 núcleos por colonia"; then
    echo "✗ selftest del ajuste: el marginal de los cuatro puntos no dio 0,1187" >&2; exit 1
  fi
  # La prueba del coste fijo: ajustado SIN el punto de 1 colonia, ¿lo predice?
  pred="$(echo "$f4" | sed -n 's/.*predice \([0-9.]*\) y el medido.*/\1/p')"
  if [[ -z "$pred" ]]; then
    echo "✗ selftest del ajuste: no publicó la predicción sin el punto menor" >&2; exit 1
  fi
  if ! awk -v p="$pred" 'BEGIN{ d=p-2.039; if (d<0) d=-d; exit !(d < 0.01) }'; then
    echo "✗ selftest del ajuste: la predicción en n=4 dio $pred (esperado 2.039)" >&2; exit 1
  fi
  # Con tres puntos también ajusta (y el «menor» es entonces n=8).
  f3="$(fit_rows "8|2.496|0.2020" "16|3.443|0.2810" "32|5.285|0.4330" | sweep_fit)"
  if ! echo "$f3" | grep -q "(n=8) predice"; then
    echo "✗ selftest del ajuste: con tres puntos no eligió n=8 como el menor" >&2; exit 1
  fi
  # Un solo punto: no hay ajuste que publicar, y lo tiene que DECIR (no inventar una recta).
  f1="$(fit_rows "4|1.927|0.1550" | sweep_fit)"
  if ! echo "$f1" | grep -q "no hay ajuste que publicar"; then
    echo "✗ selftest del ajuste: con un punto no se declaró que no hay ajuste" >&2; exit 1
  fi
  echo "✓ selftest del barrido ok (fila medida, sublinealidad de los núcleos por colonia,"
  echo "  punto sin medir y ajuste: predicción del punto menor y fijo/marginal de los cuatro)"
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

# El informe «de trabajo» de player-perf.sh es también la evidencia de la medida
# del sistema de §4.9 (2 colonias), y cada punto del barrido lo pisa. Se guarda
# antes y se devuelve al salir: el barrido no tiene que borrar la medida previa
# para publicar la suya (los puntos van a artifacts/system-c<N>.json).
REPORT_BACKUP=""
if [[ $REUSE -eq 0 && -f "$REPORT" ]]; then
  REPORT_BACKUP="$(mktemp)"
  cp "$REPORT" "$REPORT_BACKUP"
  trap 'cp "$REPORT_BACKUP" "$REPORT" 2>/dev/null || true; rm -f "$REPORT_BACKUP"' EXIT
fi
for n in "${POINTS[@]}"; do
  n="$(echo "$n" | tr -d ' ')"
  [[ "$n" =~ ^[0-9]+$ ]] || { echo "✗ colonias no numéricas: «$n»" >&2; exit 2; }
  if [[ $REUSE -eq 1 ]]; then
    # Sin medir: la tabla se reconstruye del informe del punto (lo que permite
    # volver a derivarla —y re-ajustarla— sin gastar un arranque del editor).
    if [[ -f "$ROOT/artifacts/system-c$n.json" ]]; then
      row="$(sweep_row "$ROOT/artifacts/system-c$n.json" "$CORES")"
      ROWS+=("$row")
      log "  ✓ (reuse) $(echo "$row" | awk -F'|' '{printf "%s ms/tick · %s nucleos", $2, $5}')"
    else
      FAILS=1
      log "  ✗ (reuse) falta artifacts/system-c$n.json"
      ROWS+=("$n|—|—|—|—|—|—|false|0")
    fi
    continue
  fi
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
    ROWS+=("$n|—|—|—|—|—|—|false|0")
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
  for row in "${ROWS[@]}"; do
    [[ "$(echo "$row" | cut -d'|' -f8)" == "true" ]] || continue
    echo "$row" | awk -F'|' '{ print $9 "|" $5 "|" $2 }'
  done | sweep_fit
  echo
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
