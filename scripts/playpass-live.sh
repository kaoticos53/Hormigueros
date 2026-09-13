#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# playpass-live.sh — Play pass (bloques 3, 4 y 5) verificado EN VIVO y de
# forma repetible, no a ojo.
#
# QUÉ VERIFICA. Conduce el editor abierto (paquete `com.unity.pipeline`) por la
# misma partida que el jugador juega y comprueba, sobre las muestras reales:
#
#   BLOQUE 3 (stream vivo)
#     · el mundo aparece: hormigas, ítems y los N nidos, con las poses dentro
#       del mundo (0…grid×8 u) y MOVIÉNDOSE entre muestras;
#     · las tarjetas de colonia existen para todas las colonias;
#     · los toasts del canal D aparecen con DEDUPE: una entrada por clave, sin
#       apilarse tick a tick, y con lo pintado == lo que dice el modelo;
#     · la consola de Unity no tiene ningún `stream falló`.
#
#   BLOQUE 5 (pool sembrado)
#     · el semáforo de relevo de la colonia sembrada LLEGA A VERDE (el Core lo
#       calcula con RelayVerdict; la UI solo pinta) y la competidora NO.
#
#   BLOQUE 4 (interacción) — `--no-block4` para saltarlo
#     · click en una hormiga → la tarjeta de inspección la sigue (canal A);
#     · `D` → modo marcar, click en el suelo → 1 drop en el plan, `Z` deshace;
#     · «reiniciar con plan» → la MISMA partida vuelve a empezar y el drop
#       aparece en el historial como CommandExecuted;
#     · `J` y Alt+click → la cámara salta al ancla (x,y) de la alerta, y un
#       click FUERA de la pila NO mueve la cámara.
#
#     Las acciones se disparan por los puntos de entrada SIN DISPOSITIVO de los
#     handlers (F5.2): `Input` no es inyectable desde el CLI, pero el rayo sale de
#     la cámara real y el plan es el real, así que el camino verificado es el que
#     recorre el jugador salvo la entrega de la tecla, que es del motor.
#
#   + ASPECTO (F5.1): una muestra de PÍXELES del frame en vivo y otra de la escena
#     sin darle a Play. Comprueba lo que ningún check de estado veía: que el
#     tablero se lea como tierra (ni blanco ni tapado por la capa de feromonas),
#     que las hormigas se VEAN en pantalla y que ningún texto del HUD se corte.
#     Fue el defecto del jugador: «el terreno es blanco y no se ven hormigas», con
#     todas las muestras de estado en verde.
#
# LA SEÑAL ES LA MUESTRA, NO UNA FOTO: el stream del CLI llega entero de golpe y
# el presenter lo reproduce con buffer, así que una sola lectura en el instante
# equivocado no ve nada. Las muestras se acumulan cada segundo hasta el último
# tick.
#
# Uso:
#   scripts/playpass-live.sh                     # contra el editor abierto
#   scripts/playpass-live.sh --no-block4         # solo los bloques 3 y 5
#   scripts/playpass-live.sh --samples FICHERO   # solo analiza muestras (3 y 5)
#   scripts/playpass-live.sh --analyze-block4 F  # solo analiza pasos del bloque 4
#   scripts/playpass-live.sh --analyze-visual    # solo analiza el aspecto (F5.1)
#   scripts/playpass-live.sh --selftest          # verifica los analizadores (sin editor)
#
# Opciones: --speed N (def. 3) · --ticks N (def. 7200) · --grid N (def. 96) ·
# --colonies N (def. 2) · --seed N (def. 42) · --seed-pool RUTA|- (def.
# artifacts/pretrain-warm-v2.antgenome) · --deadline SEG (def. 240) · --max-stack
# N (def. 4) · --seeded-colony N (def. 0) · --no-relay · --no-contrast ·
# --out FICHERO · --no-stream-check · --quiet · --no-block4 · --block4-speed N
# (def. 6) · --block4-deadline SEG (def. 200) · --block4-samples FICHERO ·
# --analyze-block4 FICHERO · --analyze-visual.
#
# Salida: 0 ok · 1 invariante roto · 2 uso · 3 no hay editor/CLI disponible.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"
PROBE_DIR="$ROOT/scripts/unity"

# Guarda de -projectPath compartida con check-unity-compile.sh (ver el motivo en
# scripts/lib/unity-project.sh).
# shellcheck source=lib/unity-project.sh
source "$ROOT/scripts/lib/unity-project.sh"

SAMPLES=""
ANALYZE_ONLY=0
SELFTEST=0
QUIET=0
STREAM_CHECK=1
SPEED=3
TICKS=7200
GRID=96
COLONIES=2
SEED=42
SEED_POOL="artifacts/pretrain-warm-v2.antgenome"
DEADLINE=240
MAX_STACK=4
SEEDED_COLONY=0
CONTRAST=1
OUT=""
INTERVAL=1
STREAM_FILE=""
BLOCK4=1
B4_SPEED=6
B4_DEADLINE=200
B4_SAMPLES=""
B4_ANALYZE=""
VIS_ANALYZE=0
VIS_PLAY=""
VIS_EDIT=""
VIS_SCENE=""

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --samples)   SAMPLES="$2"; ANALYZE_ONLY=1; shift 2 ;;
    --analyze-block4) B4_ANALYZE="$2"; shift 2 ;;
    --analyze-visual) VIS_ANALYZE=1; shift ;;
    --selftest)  SELFTEST=1; shift ;;
    --speed)     SPEED="$2"; shift 2 ;;
    --ticks)     TICKS="$2"; shift 2 ;;
    --grid)      GRID="$2"; shift 2 ;;
    --colonies)  COLONIES="$2"; shift 2 ;;
    --seed)      SEED="$2"; shift 2 ;;
    --seed-pool) SEED_POOL="$2"; shift 2 ;;
    --deadline)  DEADLINE="$2"; shift 2 ;;
    --max-stack) MAX_STACK="$2"; shift 2 ;;
    --seeded-colony) SEEDED_COLONY="$2"; shift 2 ;;
    --no-contrast)   CONTRAST=0; shift ;;
    --no-relay)      RELAY_EXPECT=0; shift ;;
    --relay)         RELAY_EXPECT=1; shift ;;
    --out)       OUT="$2"; shift 2 ;;
    --stream)    STREAM_FILE="$2"; shift 2 ;;
    --project)   PROJECT="$2"; shift 2 ;;
    --no-stream-check) STREAM_CHECK=0; shift ;;
    --no-block4) BLOCK4=0; shift ;;
    --block4)    BLOCK4=1; shift ;;
    --block4-speed)    B4_SPEED="$2"; shift 2 ;;
    --block4-deadline) B4_DEADLINE="$2"; shift 2 ;;
    --block4-samples)  B4_SAMPLES="$2"; shift 2 ;;
    --quiet)     QUIET=1; shift ;;
    --help|-h)   sed -n '2,58p' "$0"; exit 0 ;;
    *) echo "parámetro desconocido: $1 (ver --help)" >&2; exit 2 ;;
  esac
done

# El relevo solo puede ponerse verde si la colonia va sembrada con un pool: sin
# pool, la partida canónica no descarga y el semáforo se queda gris (bloque 3 sin
# bloque 5). Se puede forzar con --relay / --no-relay.
RELAY_EXPECT="${RELAY_EXPECT:-$([[ "$SEED_POOL" == "-" || -z "$SEED_POOL" ]] && echo 0 || echo 1)}"

# ── Analizador de muestras (puro: testeable con --selftest, sin editor) ──────
# Cada muestra: `tick|speed|keys|children|world|relay|anim`
#   world = ants=N,items=M,nests=K,oob=J     relay = 0:L,1:L (0 gris 1 ámbar 2 verde)
#   anim  = suma truncada de posiciones (dos muestras iguales ⇒ no se mueve)
# El orden no importa: las listas se comparan como conjuntos ordenados.
seg_field() { # seg_field "ants=3,items=2" ants
  local v; v="$(printf '%s' "$1" | tr ',' '\n' | sed -n "s/^$2=//p")"
  printf '%s' "${v:-0}"
}
light_of() { # light_of "0:2,1:0" 0
  printf '%s' "$1" | tr ',' '\n' | sed -n "s/^$2://p"
}

analyze_samples() {
  local file="$1" stream="$2"
  local n=0 with=0 peak=0 dups=0 mismatch=0 badline=0
  local line tick speed keys children world relay anim
  local allkeys=""
  local kset cset kdup cdup kn
  local ants items nests oob1
  local maxants=0 maxitems=0 maxnests=0 oob=0 moved=0 prev_anim="" prev_ants=0
  local world_ok=0 cards_missing=0 reached_green=0 seen_first_unload=0 last_relay=""

  while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    case "$line" in \#*) continue ;; esac
    case "$line" in error*) echo "  ✗ la sonda devolvió un error: $line"; return 1 ;; esac
    if [[ "$line" != *"|"*"|"*"|"* ]]; then badline=$((badline+1)); continue; fi
    n=$((n+1))
    IFS='|' read -r tick speed keys children world relay anim <<<"$line"

    kset="$(printf '%s' "$keys" | tr ',' '\n' | grep -v '^$' | sort || true)"
    cset="$(printf '%s' "$children" | tr ',' '\n' | grep -v '^$' | sort || true)"
    kdup="$(printf '%s\n' "$kset" | uniq -d || true)"
    cdup="$(printf '%s\n' "$cset" | uniq -d || true)"
    kn=0; [[ -n "$kset" ]] && kn="$(printf '%s\n' "$kset" | grep -c . || true)"
    if [[ -n "$kdup" || -n "$cdup" ]]; then dups=$((dups+1)); fi
    if [[ "$kset" != "$cset" ]]; then mismatch=$((mismatch+1)); fi
    if [[ $kn -gt $peak ]]; then peak=$kn; fi
    if [[ $kn -gt 0 ]]; then with=$((with+1)); fi
    allkeys+="$kset"$'\n'

    # — mundo —
    ants="$(seg_field "$world" ants)"; items="$(seg_field "$world" items)"
    nests="$(seg_field "$world" nests)"; oob1="$(seg_field "$world" oob)"
    if [[ $ants -ge 1 && $items -ge 1 && $nests -ge $COLONIES ]]; then world_ok=1; fi
    if [[ $ants -gt $maxants ]]; then maxants=$ants; fi
    if [[ $items -gt $maxitems ]]; then maxitems=$items; fi
    if [[ $nests -gt $maxnests ]]; then maxnests=$nests; fi
    oob=$((oob+oob1))
    if [[ $ants -gt 0 && $prev_ants -gt 0 && -n "$prev_anim" && "$anim" != "$prev_anim" ]]; then
      moved=$((moved+1))
    fi
    prev_anim="$anim"; prev_ants=$ants

    # — tarjetas + semáforo (solo cuando la partida ya arrancó) —
    if [[ "$tick" =~ ^[0-9]+$ && "$tick" -gt 0 ]]; then
      local i missing=""
      for ((i=0; i<COLONIES; i++)); do
        [[ -n "$(light_of "$relay" "$i")" ]] || missing+=" $i"
      done
      if [[ -n "$missing" ]]; then cards_missing=$((cards_missing+1)); fi
    fi
    if [[ -n "$relay" ]]; then last_relay="$relay"; fi
    if [[ "$(light_of "$relay" "$SEEDED_COLONY")" == "2" ]]; then reached_green=1; fi
    if [[ ",$keys," == *",first-unload,"* ]]; then seen_first_unload=1; fi
  done < "$file"

  local distinct=0
  distinct="$(printf '%s' "$allkeys" | grep -v '^$' | sort -u | grep -c . || true)"

  log "  muestras: $n · con toasts: $with · pila máxima: $peak (tope $MAX_STACK) · claves distintas: $distinct"
  log "  mundo: hormigas ≤$maxants · ítems ≤$maxitems · nidos $maxnests/$COLONIES · poses fuera del mundo: $oob · muestras con movimiento: $moved"
  log "  relevo: semáforo final [$last_relay] · colonia $SEEDED_COLONY llegó a verde: $([[ $reached_green -eq 1 ]] && echo sí || echo no)$([[ $seen_first_unload -eq 1 ]] && echo ' · first-unload visto' || echo '')"
  if [[ $badline -gt 0 ]]; then log "  ($badline líneas con formato inesperado, ignoradas)"; fi

  # — veredicto —
  if [[ $n -eq 0 ]]; then
    echo "  ✗ FALLO: sin muestras — la corrida no produjo ninguna lectura" >&2; return 1
  fi
  if [[ $world_ok -eq 0 ]]; then
    echo "  ✗ FALLO: el mundo no aparece (¿hormigas/ítems/nidos? el DrawMesh no está pintando)" >&2
    return 1
  fi
  if [[ $oob -gt 0 ]]; then
    echo "  ✗ FALLO: $oob poses vivas FUERA del mundo (grid × $GRID u) — la escala de la vista volvió a romperse" >&2
    return 1
  fi
  if [[ $moved -eq 0 ]]; then
    echo "  ✗ FALLO: el mundo no se mueve entre muestras (¿reproducción congelada?)" >&2
    return 1
  fi
  if [[ $cards_missing -gt 0 ]]; then
    echo "  ✗ FALLO: $cards_missing muestras sin tarjeta para alguna de las $COLONIES colonias" >&2
    return 1
  fi
  if [[ $with -eq 0 ]]; then
    echo "  ✗ FALLO: ningún toast en pantalla — la corrida no llegó a la 1ª alerta" >&2
    echo "    (sube --speed o --deadline; la 1ª descarga de warm-v2 en 96² es t≈3943)" >&2
    return 1
  fi
  if [[ $dups -gt 0 ]]; then
    echo "  ✗ FALLO: dedupe roto — $dups muestras repiten una clave (o un elemento)" >&2
    return 1
  fi
  if [[ $peak -gt $MAX_STACK ]]; then
    echo "  ✗ FALLO: la pila llegó a $peak con tope $MAX_STACK — se está apilando por tick" >&2
    return 1
  fi
  if [[ $mismatch -gt 0 ]]; then
    echo "  ✗ FALLO: $mismatch muestras con elementos pintados ≠ claves del modelo" >&2
    return 1
  fi
  if [[ $RELAY_EXPECT -eq 1 && $reached_green -eq 0 ]]; then
    echo "  ✗ FALLO: el relevo de la colonia $SEEDED_COLONY no llegó a VERDE (bloque 5)" >&2
    echo "    (¿pool sembrado? ¿--ticks suficientes? ¿--speed lo bastante alto para terminar?)" >&2
    return 1
  fi
  if [[ $RELAY_EXPECT -eq 1 && $CONTRAST -eq 1 ]]; then
    local other_green=""
    local k
    for ((k=0; k<COLONIES; k++)); do
      [[ $k -eq $SEEDED_COLONY ]] && continue
      if [[ "$(light_of "$last_relay" "$k")" == "2" ]]; then other_green+=" $k"; fi
    done
    if [[ -n "$other_green" ]]; then
      echo "  ✗ FALLO: la competidora también llegó a verde (colonia$other_green) — se esperaba el contraste del bloque 5" >&2
      return 1
    fi
  fi

  if [[ $STREAM_CHECK -eq 1 && -n "$stream" ]]; then
    if [[ ! -f "$stream" ]]; then
      echo "  ✗ FALLO: falta el stream del Core para comparar claves ($stream)" >&2
      return 1
    fi
    local unexplained="" k
    while IFS= read -r k; do
      [[ -n "$k" ]] || continue
      grep -qF "\"k\":\"$k\"" "$stream" || unexplained+=" $k"
    done <<<"$(printf '%s' "$allkeys" | grep -v '^$' | sort -u)"
    if [[ -n "$unexplained" ]]; then
      echo "  ✗ FALLO: la UI mostró claves que el canal D del Core no emite:$unexplained" >&2
      return 1
    fi
    log "  ✓ toda clave mostrada existe en el canal D del Core (la UI no inventa alertas)"
  fi

  log "  ✓ mundo en movimiento y dentro de escala · tarjetas por colonia · dedupe por clave ok (pila ≤ $MAX_STACK)"
  [[ $RELAY_EXPECT -eq 1 ]] && log "  ✓ relevo: colonia $SEEDED_COLONY en verde y la competidora no"
  return 0
}

# ── Analizador del BLOQUE 4 (puro: testeable con --selftest, sin editor) ─────
# Cada paso es una línea `PASO|clave=valor|…` producida por scripts/unity/
# ProbeInputBlock.cs. Se exige, no se describe:
#   A   pick>0 · pickdist≤2.5 · dmode=1 · dmarks=1 · derr=- · dundook=1 ·
#       dundo=0 · args=1 · dmarks2=1
#   P   cardlines≥10 · cardhas=1 (la tarjeta nombra el cuerpo elegido) ·
#       cardbrain=1 (huella del cerebro) · marks=1 (el plan sigue armado)
#   A2  reload=ok (la escena se pudo recargar)
#   B   cmdrows≥1 (el drop del plan llegó al Core como CommandExecuted)
#   C   anchored=1 · jnew=1 · jmoved=1 · jtarget=janchor · jalt=1 ·
#       jaltx=janchor · joutside=1 · jmissflag=1 · joutmoved=0
b4field() { # b4field "A|pick=7" pick
  local v; v="$(printf '%s' "$1" | tr '|' '\n' | sed -n "s/^$2=//p" | head -1)"
  printf '%s' "$v"
}

analyze_block4() {
  local file="$1"
  local A="" P="" R="" B="" C="" line
  while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    case "$line" in \#*) continue ;; esac
    case "$line" in error*)
      echo "  ✗ FALLO (bloque 4): la sonda devolvió un error: $line" >&2
      return 1 ;;
    esac
    case "${line%%|*}" in
      A) A="$line" ;; P) P="$line" ;; A2) R="$line" ;; B) B="$line" ;; C) C="$line" ;;
    esac
  done < "$file"

  local bad=0
  eq() { # eq <etiqueta> <paso> <campo> <esperado> <pista>
    local got; got="$(b4field "$2" "$3")"
    if [[ "$got" != "$4" ]]; then
      echo "  ✗ FALLO (bloque 4): $1 — $3='$got' (esperado '$4'): $5" >&2
      bad=1
    fi
  }
  nonempty() { # nonempty <etiqueta> <paso> <campo> <pista>
    local got; got="$(b4field "$2" "$3")"
    if [[ -z "$got" || "$got" == "-" ]]; then
      echo "  ✗ FALLO (bloque 4): $1 — falta $3: $4" >&2
      bad=1
    fi
  }

  # — A: las acciones y el plan —
  if [[ -z "$A" ]]; then echo "  ✗ FALLO (bloque 4): falta el paso A (acciones)" >&2; return 1; fi
  local pk; pk="$(b4field "$A" pick)"
  if ! [[ "$pk" =~ ^[0-9]+$ ]] || [[ "$pk" -eq 0 ]]; then
    echo "  ✗ FALLO (bloque 4): el click no seleccionó ninguna hormiga (pick='$pk')" >&2
    bad=1
  fi
  eq "modo apagado no marca" "$A" doffmarks 0 "un click SIN el modo marcar marcó un drop"
  eq "modo apagado avisa" "$A" dofferr "modo apagado" "el click sin modo no explicó por qué no marcó"
  eq "modo marcar" "$A" dmode 1 "la tecla D no activó el modo marcar"
  eq "drop marcado" "$A" dmarks 1 "el click en el suelo no marcó un drop"
  eq "drop sin error" "$A" derr - "el plan rechazó el drop (¿fuera del mundo?)"
  eq "deshacer" "$A" dundook 1 "Z no deshizo nada"
  eq "deshacer deja 0" "$A" dundo 0 "Z no vació el plan"
  eq "deshacer sin nada" "$A" dzerook 0 "Z dijo haber deshecho con el plan vacío"
  # Un drop = dos tokens del CLI (`--drop <tick:x:y>`).
  eq "args del reinicio" "$A" args 2 "el plan no produjo los args del CLI"
  if [[ "$(b4field "$A" argsline)" != *"--drop"* ]]; then
    echo "  ✗ FALLO (bloque 4): los args no llevan ningún --drop: '$(b4field "$A" argsline)'" >&2
    bad=1
  fi
  eq "plan rearmado" "$A" dmarks2 1 "volver a marcar no dejó el plan listo"
  local pd; pd="$(b4field "$A" pickdist)"
  if [[ "$pd" != "-" && -n "$pd" ]]; then
    if ! awk -v d="$pd" 'BEGIN{exit !(d>=0 && d<=2.5)}'; then
      echo "  ✗ FALLO (bloque 4): el cuerpo seleccionado está a $pd u del click (el mapeo pantalla→mundo se rompió)" >&2
      bad=1
    fi
  fi

  # — P: tarjeta e inspector tras un frame —
  if [[ -z "$P" ]]; then echo "  ✗ FALLO (bloque 4): falta el paso P (tarjeta)" >&2; bad=1; fi
  if [[ -n "$P" ]]; then
    eq "tarjeta del canal A" "$P" cardhas 1 "la tarjeta no nombra la hormiga seleccionada"
    eq "tarjeta: cerebro" "$P" cardbrain 1 "la tarjeta no trae la huella del cerebro"
    eq "plan armado" "$P" marks 1 "el plan se vació antes del reinicio"
    # Los campos del canal A que la tarjeta debe mostrar (estado, posición,
    # carga, vigor, energía, edad, inmigrante, cerebro).
    local cf; cf="$(b4field "$P" cardfields)"
    if ! [[ "$cf" =~ ^[0-9]+$ ]] || [[ "$cf" -lt 8 ]]; then
      echo "  ✗ FALLO (bloque 4): la tarjeta solo muestra $cf/8 campos del canal A" >&2
      bad=1
    fi
  fi

  # — A2: el reinicio —
  if [[ -z "$R" ]]; then echo "  ✗ FALLO (bloque 4): falta el paso A2 (reiniciar con plan)" >&2; bad=1; fi
  if [[ -n "$R" ]]; then
    local rl; rl="$(b4field "$R" reload)"
    if [[ "$rl" != "ok" ]]; then
      echo "  ✗ FALLO (bloque 4): «reiniciar con plan» no pudo recargar la escena ($rl)" >&2
      echo "    (la escena debe estar en build settings: la registra el bootstrapper)" >&2
      bad=1
    fi
  fi

  # — B: el drop del plan llegó al Core —
  if [[ -z "$B" ]]; then echo "  ✗ FALLO (bloque 4): falta el paso B (historial tras el reinicio)" >&2; bad=1; fi
  if [[ -n "$B" ]]; then
    local cr; cr="$(b4field "$B" cmdrows)"
    if ! [[ "$cr" =~ ^[0-9]+$ ]] || [[ "$cr" -lt 1 ]]; then
      echo "  ✗ FALLO (bloque 4): el historial no registró ningún CommandExecuted tras el reinicio" >&2
      echo "    (el plan no llegó al CLI: el drop del jugador se perdería al relanzar)" >&2
      bad=1
    fi
    nonempty "historial" "$B" dropxy "el CommandExecuted no traía coordenadas"
  fi

  # — C: salto de cámara —
  if [[ -z "$C" ]]; then echo "  ✗ FALLO (bloque 4): falta el paso C (salto de cámara)" >&2; bad=1; fi
  if [[ -n "$C" ]]; then
    eq "alerta anclada en pantalla" "$C" anchored 1 "no hubo ninguna alerta con ancla (¿--block4-deadline corto?)"
    eq "J salta" "$C" jnew 1 "la tecla J no saltó a ninguna alerta anclada"
    eq "J mueve la cámara" "$C" jmoved 1 "J registró el salto pero la cámara no se movió"
    eq "J al ancla" "$C" jtarget "$(b4field "$C" janchor)" "la cámara no fue al ancla (x,y) de la alerta"
    eq "Alt+click salta" "$C" jalt 1 "el click exacto sobre el toast no encontró su franja"
    eq "Alt+click al ancla" "$C" jaltx "$(b4field "$C" janchor)" "el click sobre el toast no fue a su ancla"
    eq "click fuera: no salta" "$C" joutside 1 "un click fuera de la pila movió la cámara"
    eq "click fuera: marcado" "$C" jmissflag 1 "el click fuera no se registró como fallo"
    eq "click fuera: quieto" "$C" joutmoved 0 "un click fuera de la pila movió la cámara"
  fi

  [[ $bad -eq 0 ]] || return 1
  log "  ✓ selección → tarjeta del canal A · D/click/Z → plan (1 drop) · deshacer ok"
  log "  ✓ reiniciar con plan recargó la partida y el drop salió como CommandExecuted"
  log "  ✓ J y Alt+click al ancla de la alerta · click fuera de la pila quieto"
  return 0
}

# ── Analizador de ASPECTO (F5.1, puro: testeable con --selftest) ────────────
# Los invariantes del mundo y del HUD que el pass de estados no puede ver: que
# el tablero se LEA como tierra y que la colonia se VEA. El defecto reportado
# («el terreno es blanco y no se ven hormigas») pasaba todos los checks de estado
# —el mundo tenía 40 hormigas— y ninguna muestra lo decía.
#
#   $1 = muestra visual EN VIVO (sonda VIS)
#   $2 = muestra visual en MODO EDICIÓN (la que ve el jugador al abrir la escena)
#   $3 = inspección de escena (overflows de texto)
analyze_visual() {
  local play="$1" edit="$2" scene="$3" bad=0
  local ptxt="" etxt="" stxt=""
  [[ -f "$play" ]] && ptxt="$(cat "$play")"
  [[ -f "$edit" ]] && etxt="$(cat "$edit")"
  [[ -f "$scene" ]] && stxt="$(cat "$scene")"

  eq() { # eq <etiqueta> <texto> <clave> <esperado> <motivo>
    local got
    # `tr -d '\r'`: la salida del CLI llega con CRLF y un `0\r` no es `0`.
    got="$(printf '%s' "$2" | tr -d '\r' | tr '|' '\n' | sed -n "s/^$3=//p" | head -1)"
    if [[ "$got" != "$4" ]]; then
      log "  ✗ $1 ($3=$got, esperado $4) — $5"
      bad=1
    else
      log "  ✓ $1"
    fi
  }

  eq "el tablero se lee como tierra (en partida)" "$ptxt" brown 1 \
     "el suelo no es terroso: ¿otra capa lo tapa (feromonas sin textura) o el material cambió?"
  local antp
  antp="$(printf '%s' "$ptxt" | tr -d '\r' | tr '|' '\n' | sed -n 's/^antPx=//p' | head -1)"
  if [[ "${antp:-0}" -gt 0 ]]; then
    log "  ✓ se ven hormigas en pantalla (antPx=$antp)"
  else
    log "  ✗ no se ve NINGUNA hormiga (antPx=$antp) — ¿escala sub-píxel, capa que las tapa o stream vacío?"
    bad=1
  fi
  eq "el tablero se lee como tierra AL ABRIR la escena" "$etxt" brown 1 \
     "en modo edición el mundo se ve blanco: es el defecto que reportó el jugador"
  eq "el HUD no desborda (sin texto cortado)" "$stxt" hud.overflow 0 \
     "algún Text no cabe en su panel (mira hud.OVERFLOW en la inspección)"

  [[ $bad -eq 0 ]] || return 1
  return 0
}

# ── Selftest: el analizador, sin editor ──────────────────────────────────────
if [[ $SELFTEST -eq 1 ]]; then
  # El temporal se borra AL FINAL, de forma explícita: un `trap … EXIT`
  # ejecutaba `rm -rf` también al salir de las SUBSHELLS que crean los
  # analizadores (sustituciones de comando dentro del bucle `while read`), es
  # decir, borraba el directorio MIENTRAS el analizador leía uno de sus ficheros
  # — y en Windows borrar un fichero en uso BLOQUEA: el selftest se quedaba
  # colgado en la primera llamada. Ninguna guarda por `BASHPID` sirvió (MSYS
  # bash no le da un pid distinto a esas subshells).
  tmp="$(mktemp -d)"
  clean="$tmp/clean.txt"; dup="$tmp/dup.txt"; over="$tmp/over.txt"
  mism="$tmp/mism.txt"; none="$tmp/none.txt"; oobf="$tmp/oob.txt"; norel="$tmp/norel.txt"
  {
    echo "# tick|speed|keys|children|world|relay|anim"
    echo "62|3|||ants=20,items=24,nests=2,oob=0|0:0,1:0|1000"
    echo "3989|3|first-unload|first-unload|ants=38,items=24,nests=2,oob=0|0:0,1:0|7777"
    echo "4200|3|||ants=38,items=22,nests=2,oob=0|0:2,1:0|8888"
    echo "7200|3|||ants=40,items=20,nests=2,oob=0|0:2,1:0|9999"
  } > "$clean"
  echo "4816|3|laying:0,laying:0|laying:0|ants=1,items=1,nests=2,oob=0|0:2,1:0|1" > "$dup"
  echo "4816|3|a,b,c,d,e|a,b,c,d,e|ants=1,items=1,nests=2,oob=0|0:2,1:0|1" > "$over"
  echo "4816|3|first-unload||ants=1,items=1,nests=2,oob=0|0:2,1:0|1" > "$mism"
  { echo "100|3|||ants=1,items=1,nests=2,oob=0|0:0,1:0|1"; echo "200|3|||ants=1,items=1,nests=2,oob=0|0:0,1:0|2"; } > "$none"
  echo "100|3|||ants=1,items=1,nests=2,oob=3|0:0,1:0|1" > "$oobf"
  { echo "100|3|||ants=1,items=1,nests=2,oob=0|0:0,1:0|1"; echo "200|3|first-unload|first-unload|ants=1,items=1,nests=2,oob=0|0:0,1:0|2"; } > "$norel"

  f1=0
  rc=0
  QUIET=1 analyze_samples "$clean" "" >/dev/null 2>&1 || rc=$?
  if [[ $rc -ne 0 ]]; then echo "✗ selftest: set limpio → fallo" >&2; f1=1; fi
  for bad in "$dup:clave duplicada" "$over:tope de pila" "$mism:render ≠ modelo" \
             "$none:sin toasts" "$oobf:poses fuera del mundo" "$norel:relevo sin verde"; do
    f="${bad%%:*}"; why="${bad#*:}"
    r=0; QUIET=1 analyze_samples "$f" "" >/dev/null 2>&1 || r=$?
    if [[ $r -eq 0 ]]; then echo "✗ selftest: no se detectó ($why)" >&2; f1=1; fi
  done
  if [[ $f1 -eq 0 ]]; then
    echo "✓ selftest del analizador ok (limpio + 6 fallos detectados)"
  fi

  # — Bloque 4 —
  b4_ok="$tmp/b4-ok.txt"; b4_nopick="$tmp/b4-nopick.txt"; b4_nodrop="$tmp/b4-nodrop.txt"
  b4_reload="$tmp/b4-reload.txt"; b4_nocmd="$tmp/b4-nocmd.txt"; b4_nojump="$tmp/b4-nojump.txt"
  b4_badanchor="$tmp/b4-badanchor.txt"; b4_nomode="$tmp/b4-nomode.txt"
  {
    echo "# bloque 4 (pasos A/P/A2/B/C)"
    echo "A|want=812|pick=812|pickdist=0.6|doffmarks=0|dofferr=modo apagado|dmode=1|dmarks=1|derr=-|dundook=1|dundo=0|dzerook=0|args=2|argsline=--drop 62:301.5:402.25|dmarks2=1|derr2=-"
    echo "P|sel=812|cardlines=7|cardhas=1|cardbrain=1|cardfields=8|card=Hormiga #812 · colonia 0|cerebro #981|marks=1|summary=drops: 1/5 · t62|mode=1|tick=64"
    echo "A2|args=2|reload=ok"
    echo "B|rows=3|cmdrows=1|totalcmd=1|firstcmd=62|dropxy=301.5:402.25|tick=180"
    echo "C|toasts=1|anchored=1|jnew=1|jtarget=498.2:421.7|janchor=498.2:421.7|jmoved=1|jalt=1|jaltx=498.2:421.7|jaltsel=first-unload|joutside=1|jmissflag=1|joutmoved=0"
  } > "$b4_ok"
  sed 's/|pick=812/|pick=0/' "$b4_ok" > "$b4_nopick"
  sed 's/|dmarks=1/|dmarks=0/' "$b4_ok" > "$b4_nodrop"
  sed 's/|doffmarks=0/|doffmarks=1/' "$b4_ok" > "$b4_nomode"
  sed 's/|reload=ok/|reload=error:Scene Game no está en build settings/' "$b4_ok" > "$b4_reload"
  sed 's/|cmdrows=1/|cmdrows=0/' "$b4_ok" > "$b4_nocmd"
  sed 's/|jnew=1/|jnew=0/' "$b4_ok" > "$b4_nojump"
  sed 's/|jtarget=498.2:421.7/|jtarget=0:0/' "$b4_ok" > "$b4_badanchor"

  f4=0
  rc=0; QUIET=1 analyze_block4 "$b4_ok" >/dev/null 2>&1 || rc=$?
  if [[ $rc -ne 0 ]]; then echo "✗ selftest(bloque 4): set limpio → fallo" >&2; f4=1; fi
  for bad in "$b4_nopick:click sin selección" "$b4_nodrop:modo/click sin drop" \
             "$b4_nomode:click sin modo marcó" \
             "$b4_reload:escena no recargable" "$b4_nocmd:drop perdido al reiniciar" \
             "$b4_nojump:J no salta" "$b4_badanchor:cámara al ancla equivocada"; do
    f="${bad%%:*}"; why="${bad#*:}"
    r=0; QUIET=1 analyze_block4 "$f" >/dev/null 2>&1 || r=$?
    if [[ $r -eq 0 ]]; then echo "✗ selftest(bloque 4): no se detectó ($why)" >&2; f4=1; fi
  done
  if [[ $f4 -eq 0 ]]; then
    echo "✓ selftest del bloque 4 ok (limpio + 7 fallos detectados)"
  fi

  # — Aspecto (F5.1) —
  vs_play="$tmp/vis-play.txt"; vs_edit="$tmp/vis-edit.txt"; vs_scene="$tmp/vis-scene.txt"
  vis_ok() { printf 'VIS|world=320x240|floor=0.42:0.329:0.231|brown=%s|antPx=%s|carrierPx=4|itemPx=20|ui=SCREEN\n' "$1" "$2"; }
  vis_ok 1 87 > "$vs_play"
  vis_ok 1 0 > "$vs_edit"
  printf 'presenter.grid=96 speed=8\nhud.overflow=0\n' > "$vs_scene"

  f5=0
  rc=0; QUIET=1 analyze_visual "$vs_play" "$vs_edit" "$vs_scene" >/dev/null 2>&1 || rc=$?
  if [[ $rc -ne 0 ]]; then echo "✗ selftest(aspecto): set limpio → fallo" >&2; f5=1; fi

  vis_flat="$tmp/vis-flat.txt"; vis_ok 0 87 > "$vis_flat"                                # suelo blanco
  vis_noants="$tmp/vis-noants.txt"; vis_ok 1 0 > "$vis_noants"                            # sin hormigas en partida
  vis_whiteedit="$tmp/vis-edit-white.txt"; vis_ok 0 0 > "$vis_whiteedit"                  # blanco al abrir
  vis_over="$tmp/vis-scene-over.txt"; printf 'hud.overflow=2\nhud.OVERFLOW Status rect=1x-10 pref=602x18\n' > "$vis_over"
  for bad in "$vis_flat:$vs_edit:$vs_scene:suelo no terroso" \
             "$vs_play:$vs_edit:$vis_over:texto desbordado"; do
    p="${bad%%:*}"; rest="${bad#*:}"; e="${rest%%:*}"; rest="${rest#*:}"; s="${rest%%:*}"; why="${rest#*:}"
    r=0; QUIET=1 analyze_visual "$p" "$e" "$s" >/dev/null 2>&1 || r=$?
    if [[ $r -eq 0 ]]; then echo "✗ selftest(aspecto): no se detectó ($why)" >&2; f5=1; fi
  done
  for bad in "$vis_noants:$vs_edit:$vs_scene:sin hormigas" \
             "$vs_play:$vis_whiteedit:$vs_scene:blanco en modo edición"; do
    p="${bad%%:*}"; rest="${bad#*:}"; e="${rest%%:*}"; rest="${rest#*:}"; s="${rest%%:*}"; why="${rest#*:}"
    r=0; QUIET=1 analyze_visual "$p" "$e" "$s" >/dev/null 2>&1 || r=$?
    if [[ $r -eq 0 ]]; then echo "✗ selftest(aspecto): no se detectó ($why)" >&2; f5=1; fi
  done
  if [[ $f5 -eq 0 ]]; then
    echo "✓ selftest del aspecto ok (limpio + 4 fallos detectados)"
  fi

  rm -rf "$tmp"
  [[ $f1 -eq 0 && $f4 -eq 0 && $f5 -eq 0 ]] || exit 1
  exit 0
fi

# ── Modo solo-análisis (aspecto) ─────────────────────────────────────────────
if [[ $VIS_ANALYZE -eq 1 ]]; then
  VIS_PLAY="${VIS_PLAY:-$ROOT/artifacts/playpass-visual.txt}"
  VIS_EDIT="${VIS_EDIT:-$ROOT/artifacts/playpass-visual-edit.txt}"
  VIS_SCENE="${VIS_SCENE:-$ROOT/artifacts/playpass-scene.txt}"
  log "▶ Analizando aspecto: $VIS_PLAY · $VIS_EDIT · $VIS_SCENE"
  if analyze_visual "$VIS_PLAY" "$VIS_EDIT" "$VIS_SCENE"; then exit 0; fi
  exit 1
fi

# ── Modo solo-análisis (bloque 4) ────────────────────────────────────────────
if [[ -n "$B4_ANALYZE" ]]; then
  log "▶ Analizando los pasos del bloque 4: $B4_ANALYZE"
  if analyze_block4 "$B4_ANALYZE"; then exit 0; fi
  exit 1
fi

# ── Modo solo-análisis ───────────────────────────────────────────────────────
if [[ $ANALYZE_ONLY -eq 1 ]]; then
  log "▶ Analizando muestras: $SAMPLES"
  if [[ $STREAM_CHECK -eq 1 && -z "$STREAM_FILE" ]]; then STREAM_CHECK=0; fi
  if analyze_samples "$SAMPLES" "$STREAM_FILE"; then exit 0; fi
  exit 1
fi

# ── Ruta del proyecto ────────────────────────────────────────────────────────
# Los modos de solo-análisis ya salieron arriba (no tocan el editor), así que
# aquí solo llega quien va a conducirlo: la ruta tiene que ser el proyecto del
# juego. Con la RAÍZ del repo, Unity convertiría el repo en un proyecto fantasma
# (el accidente que borró 191 MB a mano: scripts/lib/unity-project.sh).
PROJECT="$(require_unity_project "$PROJECT")" || exit 2

# ── Resolución del Unity CLI ─────────────────────────────────────────────────
# Encontrar el binario (`find_unity_cli`) y convertir rutas (`winpath`) viven en
# la librería compartida: el envoltorio scripts/unity-cli.sh usa EXACTAMENTE las
# mismas funciones, para que no haya dos ideas de dónde está el editor.
# shellcheck source=lib/unity-cli.sh
source "$ROOT/scripts/lib/unity-cli.sh"

if ! UNITY_CLI_BIN="$(find_unity_cli)"; then
  echo "✗ no encuentro el Unity CLI (usa \$UNITY_CLI o instálalo con el Hub)" >&2
  exit 3
fi

UCMD() { MSYS_NO_PATHCONV=1 "$UNITY_CLI_BIN" command "$@" --project-path "$(winpath "$PROJECT")" --format json; }

# Extrae el valor útil del sobre JSON del CLI. `run_script` anida la salida de
# la sonda en `data.result.result`; otros comandos (editor_status, …) devuelven
# un objeto que se conserva tal cual; un fallo de la sonda se normaliza a
# `error|…` para que el analizador lo vea.
cmd_result() {
  python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
except Exception:
    sys.exit(0)
r = (d.get('data') or {}).get('result')
if isinstance(r, dict):
    if r.get('success') is False or r.get('error'):
        sys.stdout.write('error|' + str(r.get('error') or 'fallo'))
    elif isinstance(r.get('result'), str):
        sys.stdout.write(r['result'])
    else:
        sys.stdout.write(json.dumps(r))
elif isinstance(r, str):
    sys.stdout.write(r)
elif r is not None:
    sys.stdout.write(json.dumps(r))
"
}

command -v python3 >/dev/null 2>&1 \
  || { echo "✗ falta python3 (se usa para leer el JSON del CLI)" >&2; exit 3; }

# Al entrar en Play el editor recarga el dominio y el servidor del pipeline
# reinicia: la PRIMERA llamada puede salir ≠ 0 sin salida alguna. Se espera a
# que Play esté vivo y se reintenta, en vez de abortar (comprobado: exit 6 a la
# primera, 0 a partir de la segunda). Nada de `| grep -q` en estas tuberías:
# `grep -q` cierra el pipe, el productor muere con SIGPIPE y `pipefail` da la
# tubería por fallida.
# `editor_status` trae un objeto (no un run_script) — se leen sus campos a mano.
editor_field() {
  UCMD editor_status 2>/dev/null | python3 -c "
import sys, json
try: d = json.load(sys.stdin)
except Exception: sys.exit(0)
r = d.get('data') or {}
print(r.get('$1', ''))
" 2>/dev/null || true
}

wait_for_play() {
  local i st
  for i in $(seq 1 40); do
    st="$(UCMD editor_status 2>/dev/null | cmd_result 2>/dev/null || true)"
    case "$st" in *playing*) return 0 ;; esac
    sleep 0.5
  done
  return 1
}

# Espera a que el editor termine de compilar y de recargar el dominio. Un
# `run_script` sobre una sonda RECIÉN EDITADA dispara compilación + recarga, y la
# recarga en Play deja el mundo congelado (tick que no avanza) o incluso en
# PAUSA: es lo que dejó 73 muestras idénticas en tick 105 en una ejecución real.
wait_for_compiled() {
  local i c r
  for i in $(seq 1 120); do
    c="$(editor_field compiling)"; r="$(editor_field domainReloadInProgress)"
    case "$c$r" in *true*) sleep 1 ;; *) return 0 ;; esac
  done
  return 1
}

sample_once() {
  local attempt=1 line=""
  while [[ $attempt -le 8 ]]; do
    line="$(UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeToastsSample.cs")" \
              2>/dev/null | cmd_result 2>/dev/null)" || line=""
    if [[ -n "$line" ]]; then printf '%s\n' "$line"; return 0; fi
    sleep 0.5
    attempt=$((attempt+1))
  done
  printf 'error|sin respuesta del editor tras %s intentos\n' "$((attempt-1))"
  return 0
}

# ── 1. ¿Hay un editor abierto sobre este proyecto? ───────────────────────────
if ! MSYS_NO_PATHCONV=1 "$UNITY_CLI_BIN" status --format json 2>/dev/null \
     | grep -qF "$(basename "$PROJECT")"; then
  echo "✗ no hay un editor abierto sobre $PROJECT — ábrelo (con com.unity.pipeline) y reintenta" >&2
  exit 3
fi

log "▶ Play pass: bloques 3 (stream vivo) y 5 (pool sembrado)"
log "   editor:   $UNITY_CLI_BIN"
log "   proyecto: $PROJECT"

# Se compila ANTES de jugar: verificar contra una Assembly-CSharp vieja no
# valdría de nada. Si la compilación falla, el sitio del error está en
# scripts/check-unity-compile.sh.
log "▶ Recompilando los scripts del editor…"
UCMD recompile >/dev/null 2>&1 || true
RC=""
for i in $(seq 1 90); do
  RC="$(UCMD recompile_status 2>/dev/null | cmd_result 2>/dev/null || true)"
  case "$RC" in *compiling*) sleep 1 ;; *) [[ -n "$RC" ]] && break ;; esac
done
# El estado llega como string JSON sin espacios (lo serializa Unity).
if ! printf '%s' "$RC" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
except Exception:
    sys.exit(0)
sys.exit(1 if (d.get('compilationFailed') or d.get('failed')) else 0)
"; then
  echo "✗ los scripts del editor NO compilan — corre scripts/check-unity-compile.sh" >&2
  exit 3
fi

STATUS="$(UCMD editor_status 2>/dev/null | cmd_result 2>/dev/null || true)"
case "$STATUS" in
  *playing*) log "   (había Play activo: se detiene)"; UCMD editor_stop >/dev/null 2>&1 || true ;;
esac

# La consola se limpia para que el chequeo de `stream falló` hable SOLO de esta
# corrida (el resto del pass puede haber dejado ruido).
UCMD clear_console >/dev/null 2>&1 || true

# ── 2. Config del presenter ──────────────────────────────────────────────────
CONFIG="$PROJECT/Temp/antsim-playpass.json"
mkdir -p "$PROJECT/Temp"
printf '{"seed":%s,"grid":%s,"colonies":%s,"ticks":%s,"speed":%s,"seedPool":"%s","replay":""}\n' \
  "$SEED" "$GRID" "$COLONIES" "$TICKS" "$SPEED" "${SEED_POOL:--}" > "$CONFIG"

set_probe() { UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeToastsSet.cs")" \
                 2>/dev/null | cmd_result 2>/dev/null || true; }

SET="$(set_probe)"
case "$SET" in
  ok*) log "   config:   ${SET#ok|}" ;;
  *)
    echo "   la escena no tiene HUD presentable ($SET) — se construye con el bootstrapper…"
    UCMD menu "AntSim/Crear escena de juego" >/dev/null 2>&1 || true
    UCMD save_scene --path "Assets/Scenes/Game.unity" >/dev/null 2>&1 || true
    SET="$(set_probe)"
    case "$SET" in ok*) log "   config:   ${SET#ok|}" ;; *)
      echo "✗ no se pudo configurar el presenter: $SET" >&2; exit 3 ;; esac
    ;;
esac

# ── 3. Play + muestreo hasta el último tick ──────────────────────────────────
SAMPLES="${OUT:-$ROOT/artifacts/playpass-samples.txt}"
mkdir -p "$(dirname "$SAMPLES")"
echo "# tick|speed|keys|children|world|relay|anim (playpass-live.sh, seed $SEED grid $GRID speed $SPEED ticks $TICKS)" > "$SAMPLES"

# El script arranca Play: si falla a mitad, no se deja el editor jugando.
PLAY_STARTED=0
cleanup_play() {
  if [[ $PLAY_STARTED -eq 1 ]]; then UCMD editor_stop >/dev/null 2>&1 || true; fi
}
trap cleanup_play EXIT

# Las sondas se compilan ANTES de entrar en Play. Un `.cs` de sonda compilado por
# primera vez DURANTE la partida recarga el dominio y congela el mundo: pasó de
# verdad (73 muestras clavadas en el tick 105, sin un solo error en la consola).
log "▶ Compilando las sondas (antes de Play, para no recargar el dominio en partida)…"
for _probe in ProbeToastsSet ProbeToastsSample ProbeSceneInspect ProbeVisualSample; do
  UCMD run_script --file "$(winpath "$PROBE_DIR/$_probe.cs")" >/dev/null 2>&1 || true
done
wait_for_compiled || true

UCMD editor_play >/dev/null 2>&1 || true
if ! wait_for_play; then
  echo "✗ el editor no llegó a entrar en Play" >&2
  exit 3
fi
PLAY_STARTED=1

# `editor_status` dice «playing» también cuando el editor está PAUSADO, y con
# pausa el mundo no avanza: las muestras salían congeladas (tick fijo, sin
# hormigas) y el pass no distinguía «mundo parado» de «mundo sin datos». Se
# quita la pausa antes de muestrear.
for i in $(seq 1 10); do
  ST="$(UCMD editor_status 2>/dev/null | cmd_result 2>/dev/null || true)"
  case "$ST" in
    *paused*) log "▶ el editor estaba en PAUSA: reanudando…"; UCMD editor_pause >/dev/null 2>&1 || true; sleep 1 ;;
    *) break ;;
  esac
done

# Y no se muestrea a ciegas: se espera a que el stream tenga mundo Y a que el
# mundo AVANCE (el tick sube). «Hay hormigas» no basta: con el editor en pausa o
# recién recargado hay hormigas dibujadas y el tick clavado, y las muestras
# saldrían idénticas — que es justo lo que un analizador puede leer como
# «sin anomalías». Ambas condiciones se exigen aquí.
STREAM_OK=0
prev_tick=-1
for i in $(seq 1 40); do
  LIVE="$(sample_once)"
  tick="${LIVE%%|*}"
  # El campo 5 de la muestra es `ants=N,items=M,nests=K,oob=J`.
  ants="$(printf '%s' "$LIVE" | tr '|' '\n' | sed -n 's/^ants=\([0-9-]*\).*/\1/p' | head -1)"
  if [[ "$tick" =~ ^[0-9]+$ ]] && [[ "$tick" -gt 0 && "${ants:--1}" -gt 0 ]]; then
    if [[ $prev_tick -ge 0 && "$tick" -gt "$prev_tick" ]]; then STREAM_OK=1; break; fi
    prev_tick="$tick"
  fi
  case "$(editor_field playMode)" in *paused*) UCMD editor_pause >/dev/null 2>&1 || true ;; esac
  sleep 1
done
if [[ $STREAM_OK -ne 1 ]]; then
  echo "✗ el mundo no AVANZA (tick estancado): ¿CLI publicado?, ¿pool?, ¿editor en pausa?" >&2
  exit 3
fi
log "▶ Muestreando cada ${INTERVAL}s hasta el tick $TICKS (tope ${DEADLINE}s)…"

start="$(date +%s)"
samples=0; finished=0; frozen=0; last_tick=-1; recoveries=0
while :; do
  el=$(( $(date +%s) - start ))
  if [[ $el -ge $DEADLINE ]]; then break; fi
  line="$(sample_once)"
  printf '%s\n' "$line" >> "$SAMPLES"
  samples=$((samples+1))
  tick="${line%%|*}"
  # Tick estancado: la recarga de dominio que dispara un script recién compilado
  # puede dejar el mundo congelado (o el editor en pausa) sin dejar error. Se
  # intenta recuperar una vez; si sigue igual, la muestra se queda y el
  # invariante de movimiento la delata (mejor fallar con nombre que pasar en verde).
  if [[ "$tick" =~ ^[0-9]+$ && "$tick" == "$last_tick" ]]; then
    frozen=$((frozen+1))
    if [[ $frozen -ge 3 && $recoveries -lt 3 ]]; then
      recoveries=$((recoveries+1))
      log "▶ el mundo está CONGELADO en tick $tick (recuperación $recoveries): " \
          "pausa=$(editor_field playMode) compilando=$(editor_field compiling)"
      case "$(editor_field playMode)" in *paused*) UCMD editor_pause >/dev/null 2>&1 || true ;; esac
      wait_for_compiled || true
      sleep 2
      frozen=0; last_tick=-1
      continue
    fi
  else
    frozen=0
    [[ "$tick" =~ ^[0-9]+$ ]] && last_tick="$tick"
  fi
  if [[ "$tick" =~ ^[0-9]+$ && "$tick" -ge "$TICKS" ]]; then finished=1; break; fi
  sleep "$INTERVAL"
done

VIS_PLAY="${VIS_PLAY:-$ROOT/artifacts/playpass-visual.txt}"
VIS_SCENE="${VIS_SCENE:-$ROOT/artifacts/playpass-scene.txt}"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeVisualSample.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null > "$VIS_PLAY" || true
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeSceneInspect.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null > "$VIS_SCENE" || true

UCMD editor_stop >/dev/null 2>&1 || true
PLAY_STARTED=0
log "▶ Play detenido tras ${samples} muestras ($(( $(date +%s) - start )) s)$([[ $finished -eq 1 ]] && echo ' — reproducción completa' || echo ' — TOPE de tiempo, sin llegar al final')."

# El estado de la escena AL ABRIRLA (sin Play): es lo que ve el jugador antes de
# darle al botón, y era justo lo que estaba mal (plano blanco sobre el tablero).
sleep 2
VIS_EDIT="${VIS_EDIT:-$ROOT/artifacts/playpass-visual-edit.txt}"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeVisualSample.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null > "$VIS_EDIT" || true

# ── 4. Consola: sin `stream falló` (bloque 3) ───────────────────────────────
CONSOLE="$(UCMD console 2>/dev/null | cmd_result 2>/dev/null || true)"
CONSOLE_FAIL=0
case "$CONSOLE" in
  *"stream falló"*|*"stream fallo"*) CONSOLE_FAIL=1 ;;
esac
if [[ $CONSOLE_FAIL -eq 1 ]]; then
  echo "✗ FALLO: la consola de Unity tiene 'stream falló' (el CLI no arrancó o murió)" >&2
  echo "$CONSOLE" | tr ',' '\n' | grep -a "stream fall" | head -5 >&2 || true
  exit 1
fi
NAVERR="$(printf '%s' "$CONSOLE" | grep -o '"logType": "Error"' | grep -c . || true)"
log "  consola: sin 'stream falló' ($NAVERR entradas de error, informativas)"

# ── 5. Stream del Core para el invariante «la UI no inventa claves» ─────────
if [[ $STREAM_CHECK -eq 1 ]]; then
  STREAM_FILE="$ROOT/artifacts/playpass-stream.jsonl"
  log "▶ Regenerando el canal D del Core (misma partida) para comparar claves…"
  POOL_ARG=()
  [[ "$SEED_POOL" != "-" && -n "$SEED_POOL" ]] && POOL_ARG=(--seed-pool "$SEED_POOL")
  if ! dotnet run --project "$ROOT/src/Tools/AntSim.Cli" -c Debug -- --mode game \
       --seed "$SEED" --ticks "$TICKS" --grid "$GRID" --colonies "$COLONIES" \
       "${POOL_ARG[@]}" > "$STREAM_FILE" 2>/dev/null; then
    echo "  ⚠ no se pudo generar el stream del Core: se omite el chequeo de claves" >&2
    STREAM_FILE=""
  fi
fi

# ── 6. Veredicto (bloques 3 y 5) ─────────────────────────────────────────────
log "▶ Analizando $(wc -l < "$SAMPLES" | tr -d ' ') líneas de $SAMPLES"
RC35=0
if analyze_samples "$SAMPLES" "$STREAM_FILE"; then
  log "✓ bloques 3 y 5 verificados en vivo (muestras en $SAMPLES)"
else
  RC35=1
fi

# ── 6bis. Aspecto (F5.1) ────────────────────────────────────────────────────
RCF5=0
log "▶ Aspecto (píxeles del frame en vivo y de la escena en edición)"
if analyze_visual "$VIS_PLAY" "$VIS_EDIT" "$VIS_SCENE"; then
  log "✓ aspecto verificado (tablero terroso, hormigas visibles, HUD sin desbordes)"
else
  RCF5=1
fi

# ── 7. Bloque 4: interacción (entradas sin dispositivo) ─────────────────────
RC4=0
if [[ $BLOCK4 -eq 1 ]]; then
  log ""
  log "▶ Bloque 4: interacción (selección · D · click · Z · reiniciar con plan · J)"
  B4_SAMPLES="${B4_SAMPLES:-$ROOT/artifacts/playpass-block4.txt}"
  mkdir -p "$(dirname "$B4_SAMPLES")"
  echo "# bloques 4: pasos A/P/A2/B/C (playpass-live.sh, seed $SEED grid $GRID speed $B4_SPEED)" > "$B4_SAMPLES"

  # La escena se reconstruye con el bootstrapper: es lo que la guarda y la
  # registra en build settings, sin lo cual «reiniciar con plan» no puede
  # recargarla (defecto que encontró este mismo check).
  UCMD menu "AntSim/Crear escena de juego" >/dev/null 2>&1 || true
  UCMD save_scene --path "Assets/Scenes/Game.unity" >/dev/null 2>&1 || true

  printf '{"seed":%s,"grid":%s,"colonies":%s,"ticks":%s,"speed":%s,"seedPool":"%s","replay":""}\n' \
    "$SEED" "$GRID" "$COLONIES" "$TICKS" "$B4_SPEED" "${SEED_POOL:--}" > "$CONFIG"
  SET="$(set_probe)"
  case "$SET" in ok*) ;; *) echo "✗ no se pudo configurar el presenter para el bloque 4: $SET" >&2; RC4=1 ;; esac
  # El config se GUARDA en la escena: el reinicio con plan la recarga desde el
  # asset, así que lo que solo viva en memoria (velocidad, semilla, pool) se
  # perdería al recargarla y la segunda mitad de la fase correría con los
  # valores por defecto del bootstrapper.
  UCMD save_scene --path "Assets/Scenes/Game.unity" >/dev/null 2>&1 || true

  STEP_FILE="$PROJECT/Temp/antsim-block4.step"
  b4_step() { # b4_step <paso>
    printf '%s' "$1" > "$STEP_FILE"
    local attempt=1 line=""
    while [[ $attempt -le 8 ]]; do
      line="$(UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeInputBlock.cs")" \
                2>/dev/null | cmd_result 2>/dev/null)" || line=""
      if [[ -n "$line" ]]; then printf '%s\n' "$line"; return 0; fi
      sleep 0.5
      attempt=$((attempt+1))
    done
    printf 'error|sin respuesta del editor\n'
    return 0
  }

  # Diagnóstico: separa «el editor no está en Play» de «Play sí, pero el mundo
  # no arranca» — dos fallos que desde la sonda del bloque 4 se ven iguales.
  b4_diag() { UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeB4Diag.cs")" \
                2>/dev/null | cmd_result 2>/dev/null || true; }

  # El mundo está VIVO cuando el editor está en Play Y el presenter tiene ticks
  # en el buffer: eso prueba que el CLI arrancó y el stream está llegando (que el
  # editor diga «playing» no lo prueba).
  b4_ready() {
    local dg b
    dg="$(b4_diag)"
    case "$dg" in *"playing=1|"*) ;; *) return 1 ;; esac
    b="$(printf '%s' "$dg" | tr '|' '\n' | sed -n 's/^buffer=//p')" || b=0
    case "$b" in ''|*[!0-9]*) return 1 ;; esac
    [[ "$b" -gt 0 ]] || return 1
    return 0
  }

  b4_enter_play() {
    UCMD editor_stop >/dev/null 2>&1 || true
    sleep 1
    UCMD editor_play >/dev/null 2>&1 || true
    wait_for_play || true
    local i
    for i in $(seq 1 40); do
      if b4_ready; then PLAY_STARTED=1; return 0; fi
      sleep 1
    done
    return 1
  }

  if [[ $RC4 -eq 0 ]]; then
    UCMD clear_console >/dev/null 2>&1 || true
    if b4_enter_play; then
      PLAY_STARTED=1
      b4_start="$(date +%s)"

      # — A: se reintenta hasta que el mundo tenga hormigas que seleccionar —
      # La PRIMERA llamada tras `editor_play` puede volver vacía o fallar (el
      # pipeline reinicia durante la recarga de dominio): se reintenta hasta que
      # el mundo tenga hormigas que seleccionar.
      ALINE=""
      for i in $(seq 1 60); do
        ALINE="$(b4_step A)"
        case "$ALINE" in error*|"") sleep 1 ;; *) break ;; esac
      done
      case "$ALINE" in
        error*|"")
          printf '#DIAG %s\n' "$(b4_diag)" >> "$B4_SAMPLES"
          log "  (el mundo no apareció: $(b4_diag | tr '|' ' '))" ;;
      esac
      printf '%s\n' "$ALINE" >> "$B4_SAMPLES"
      log "  A  $(printf '%s' "$ALINE" | tr '|' ' ' | cut -c1-150)"

      sleep 1
      PLINE="$(b4_step P)"
      printf '%s\n' "$PLINE" >> "$B4_SAMPLES"

      # — A2: destructivo (recarga la escena); su respuesta puede perderse —
      RLINE="$(b4_step A2)"
      printf '%s\n' "$RLINE" >> "$B4_SAMPLES"
      ok_play=0
      for i in $(seq 1 40); do
        if UCMD editor_status 2>/dev/null | cmd_result 2>/dev/null | grep -q playing; then ok_play=1; break; fi
        sleep 0.5
      done
      [[ $ok_play -eq 1 ]] || log "  (aviso: el editor no reporta Play tras el reinicio)"

      # — B: el drop del plan debe aparecer en el historial —
      BLINE=""
      for i in $(seq 1 40); do
        BLINE="$(b4_step B)"
        case "$BLINE" in *"cmdrows="*) cr="$(b4field "$BLINE" cmdrows)" ;; *) cr=0 ;; esac
        [[ "${cr:-0}" =~ ^[0-9]+$ && "${cr:-0}" -ge 1 ]] && break
        [[ $(( $(date +%s) - b4_start )) -ge $B4_DEADLINE ]] && break
        sleep 1
      done
      printf '%s\n' "$BLINE" >> "$B4_SAMPLES"
      log "  B  $(printf '%s' "$BLINE" | tr '|' ' ' | cut -c1-150)"

      # — C: se espera a que haya una alerta ANCLADA en pantalla (1ª descarga) —
      # Cada muestra se guarda como comentario: si el salto no encuentra ancla,
      # las líneas dicen en qué tick iba la partida y qué alertas había.
      CLINE=""
      for i in $(seq 1 120); do
        CLINE="$(b4_step C)"
        printf '#C %s\n' "$CLINE" >> "$B4_SAMPLES"
        case "$CLINE" in *"anchored=1"*) break ;; esac
        [[ $(( $(date +%s) - b4_start )) -ge $B4_DEADLINE ]] && break
        sleep 1
      done
      printf '%s\n' "$CLINE" >> "$B4_SAMPLES"
      log "  C  $(printf '%s' "$CLINE" | tr '|' ' ' | cut -c1-190)"

      UCMD editor_stop >/dev/null 2>&1 || true
      PLAY_STARTED=0
      log "  ($(( $(date +%s) - b4_start )) s de bloque 4)"
    else
      echo "✗ el editor no llegó a entrar en Play para el bloque 4" >&2
      RC4=1
    fi
  fi

  if [[ $RC4 -eq 0 ]]; then
    log "▶ Analizando el bloque 4 ($B4_SAMPLES)"
    if ! analyze_block4 "$B4_SAMPLES"; then RC4=1; fi
  fi
else
  log "(bloque 4 saltado con --no-block4)"
fi

# ── 8. Resumen ───────────────────────────────────────────────────────────────
log ""
if [[ $RC35 -eq 0 && $RC4 -eq 0 && $RCF5 -eq 0 ]]; then
  [[ $BLOCK4 -eq 1 ]] && log "✓ Play pass completo: bloques 3, 4 y 5 + aspecto (F5.1) verificados en vivo" \
                      || log "✓ bloques 3 y 5 + aspecto (F5.1) verificados en vivo"
  exit 0
fi
[[ $RC35 -ne 0 ]] && echo "✗ el bloque 3/5 falló (ver arriba)" >&2
[[ $RCF5 -ne 0 ]] && echo "✗ el aspecto falló (ver arriba)" >&2
[[ $RC4 -ne 0 ]] && echo "✗ el bloque 4 falló (ver arriba)" >&2
exit 1
