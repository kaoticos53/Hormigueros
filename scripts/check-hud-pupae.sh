#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-hud-pupae.sh — el HUD VIVO nunca pinta más pupas que el techo de nodrizas.
#
# QUÉ VIGILA. La puesta de la ronda se para cuando la cola de cría cubre lo que
# crían las nodrizas (`adultas · NurseShare`), así que `pupae` tiene un techo
# duro: 6 con la tropa llena. El HUD pinta el número del stream TAL CUAL —no lo
# recorta— y por eso el techo se puede vigilar desde la vista. La suite fija el
# lado del Core (`NurseCeilingTests`); esto fija lo que la PARTIDA enseña, con el
# stream, el parser, la tarjeta y el `Text` en medio.
#
# CÓMO. Muestrea el texto REAL de las tarjetas con `scripts/unity/ProbeHudPupae.cs`
# cada `--interval` segundos, `--samples` veces, y falla si alguna muestra pinta
# más pupas que el techo. Una sola foto no puede afirmar «nunca»: se acumula y se
# decide al final.
#
# Uso:
#   scripts/play-game.sh --species lasius,eciton \
#       --pool tests/fixtures/warm-v2.antgenome --speed 10     # en otra terminal
#   scripts/check-hud-pupae.sh                                  # 12 muestras cada 10 s
#   scripts/check-hud-pupae.sh --samples 20 --interval 5
#   scripts/check-hud-pupae.sh --analyze artifacts/playpass-hud-pupae.txt
#   scripts/check-hud-pupae.sh --selftest                       # el veredicto, sin editor
#
# `--analyze` juzga un volcado YA tomado (una línea `HUDPUPAE|…` por muestra) con
# el MISMO veredicto que el muestreo: es lo que usa el play pass, que muestrea
# dentro de su propia partida en vez de abrir otra.
#
# Códigos de salida: 0 verde · 1 alguna muestra pasó el techo · 2 falló la sonda
# · 3 no hay editor en Play (o falta el CLI).
#
# No lanza Unity: medir una partida que hay que arrancar y cerrar es otra cosa
# (y ensucia `Game.unity`, que el lanzador reescribe con su configuración). Este
# guard se engancha a la que ya esté corriendo, y el play pass lo hace sin
# cambiar de sesión.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/App/AntSim.Unity"
PROBE="$ROOT/scripts/unity/ProbeHudPupae.cs"

SAMPLES=12
INTERVAL=10
ANALYZE=""
SELFTEST=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --samples)  SAMPLES="$2";  shift 2 ;;
        --interval) INTERVAL="$2"; shift 2 ;;
        --analyze)  ANALYZE="$2";  shift 2 ;;
        --selftest) SELFTEST=1;    shift ;;
        -h|--help)
            sed -n '2,/^set -euo pipefail/p' "${BASH_SOURCE[0]}" | sed '$d' | sed 's/^# \{0,1\}//'
            exit 0 ;;
        *) echo "✗ opción desconocida: $1" >&2; exit 3 ;;
    esac
done

# ── El veredicto (puro: lo usan el muestreo, --analyze y --selftest) ────────
# Lee una línea `HUDPUPAE|…` por muestra y decide. Acumula picos por colonia y
# el número de muestras que excedieron: una sola no dice «nunca».
#
# ASCII a propósito: la consola de Windows es cp1252 y una flecha o un punto
# medio en un print matan el veredicto con UnicodeEncodeError (medido: la
# comprobación salía en verde por fuera y el script moría antes de decidir).
veredicto() {
    python3 -c "
import sys

techo = None
pico = {}
excede = 0
sin_tarjetas = 0
ticks = []

for raw in sys.stdin:
    raw = raw.strip()
    if not raw or not raw.startswith('HUDPUPAE'):
        continue
    campos = dict(p.split('=', 1) for p in raw.split('|')[1:] if '=' in p)
    if 'techo' in campos:
        techo = int(campos['techo'])
    if 'tick' in campos:
        ticks.append(int(campos['tick']))
    if campos.get('error') == 'sin-tarjetas':
        sin_tarjetas += 1
    for k, v in campos.items():
        if not k.startswith('c'):
            continue
        adultos, pupas = v.split('/')[:2]
        pico[k] = max(pico.get(k, 0), int(pupas))
    if campos.get('excede') == '1':
        excede += 1

if techo is None:
    print('ERROR: las muestras no traen techo')
    sys.exit(2)

print()
print(f'techo de nodrizas: {techo}')
print('pico por colonia: ' + ' | '.join(f'{k} {v}' for k, v in sorted(pico.items())))
print(f'ventana: tick {min(ticks)} -> {max(ticks)}' if ticks else 'ventana: sin tick')
if sin_tarjetas:
    print(f'! {sin_tarjetas} muestra(s) sin tarjeta pintada (arranque o cambio de ronda)')

if excede:
    print(f'FALLO: {excede} muestra(s) pintaron MAS pupas que el techo {techo}')
    sys.exit(1)
maxp = max(pico.values()) if pico else 0
print(f'OK: ninguna muestra paso el techo ({maxp} <= {techo} en todas)')
sys.exit(0)
"
}

# ── El veredicto, sin editor: sobre un volcado ya tomado ────────────────────
if [[ -n "$ANALYZE" ]]; then
    [[ -f "$ANALYZE" ]] || { echo "✗ no hay volcado: $ANALYZE" >&2; exit 2; }
    veredicto < "$ANALYZE"
    exit $?
fi

# ── El veredicto, sin editor: con muestras de mentira ───────────────────────
# Prueba las dos caras que importan —verde con 6, rojo con 7— porque el guard
# sólo vale si se le ha visto fallar.
if [[ $SELFTEST -eq 1 ]]; then
    rc_ok=0
    printf 'HUDPUPAE|tick=100|techo=6|c0=40/6/ok|c1=40/6/ok|pico=6|excede=0\n' | veredicto >/dev/null || rc_ok=$?
    rc_bad=0
    printf 'HUDPUPAE|tick=100|techo=6|c0=40/7/EXCEDE|c1=40/6/ok|pico=7|excede=1\n' | veredicto >/dev/null || rc_bad=$?
    if [[ $rc_ok -eq 0 && $rc_bad -eq 1 ]]; then
        echo "✓ selftest: verde con 6 · rojo con 7"
        exit 0
    fi
    echo "✗ selftest: verde rc=$rc_ok (esperado 0) · rojo rc=$rc_bad (esperado 1)" >&2
    exit 2
fi

[[ -f "$PROBE" ]] || { echo "✗ falta la sonda: $PROBE" >&2; exit 2; }
command -v python3 >/dev/null 2>&1 \
    || { echo "✗ falta python3 (se usa para leer el JSON del CLI)" >&2; exit 3; }

source "$ROOT/scripts/lib/unity-cli.sh"

if ! UNITY_CLI_BIN="$(find_unity_cli)"; then
    echo "✗ no encuentro el Unity CLI (usa \$UNITY_CLI o instálalo con el Hub)" >&2
    exit 3
fi

UCMD() { MSYS_NO_PATHCONV=1 "$UNITY_CLI_BIN" command "$@" --project-path "$(winpath "$PROJECT")" --format json; }

# El sobre del CLI anida la salida de la sonda en `data.result.result`; un fallo
# de compilación se normaliza a `error|…` para que el analizador lo vea.
cmd_result() {
    python3 -c "
import sys, json
try:
    d = json.loads(sys.stdin.buffer.read().decode('utf-8'))
except Exception:
    sys.exit(0)
r = (d.get('data') or {}).get('result')
out = ''
if isinstance(r, dict):
    if r.get('success') is False or r.get('error'):
        out = 'error|' + str(r.get('error') or 'fallo')
    elif isinstance(r.get('result'), str):
        out = r['result']
    else:
        out = json.dumps(r)
elif isinstance(r, str):
    out = r
elif r is not None:
    out = json.dumps(r)
sys.stdout.write(out)
"
}

# ¿Hay editor y está en Play? Sin Play la tarjeta no existe y el guard diría
# «verde» por no haber mirado nada — el falso positivo que este script existe
# para no repetir.
status="$(UCMD editor_status 2>/dev/null || true)"
if [[ -z "$status" ]]; then
    echo "✗ no hay ningún Unity Editor conectado al proyecto ($PROJECT)" >&2
    echo "  ábrelo con: scripts/play-game.sh --species lasius,eciton --pool tests/fixtures/warm-v2.antgenome" >&2
    exit 3
fi
play="$(printf '%s' "$status" | python3 -c "
import sys, json
try: d = json.load(sys.stdin)
except Exception: sys.exit(0)
r = d.get('data') or {}
v = r.get('playMode')
if v is None: v = (r.get('result') or {}).get('playMode')
print(str(v).lower() if v is not None else '')" 2>/dev/null || true)"
if [[ "$play" != *playing* ]]; then
    echo "✗ el editor está conectado pero NO está en Play (playMode=${play:-?})" >&2
    echo "  lanza la partida con: scripts/play-game.sh --species lasius,eciton --pool tests/fixtures/warm-v2.antgenome" >&2
    exit 3
fi

echo "▶ Muestreando el HUD cada ${INTERVAL}s (${SAMPLES} muestras)…"

MUESTRAS=()
for i in $(seq 1 "$SAMPLES"); do
    line="$(UCMD run_script --file "$(winpath "$PROBE")" 2>/dev/null | cmd_result || true)"
    if [[ "$line" != HUDPUPAE* ]]; then
        echo "✗ la sonda no devolvió una medida: '${line:-<vacío>}'" >&2
        echo "  (si acaba de compilarse, la primera llamada puede fallar: repite)" >&2
        exit 2
    fi
    MUESTRAS+=("$line")
    echo "  ${line#HUDPUPAE|}"
    if [[ $i -lt $SAMPLES ]]; then sleep "$INTERVAL"; fi
done

printf '%s\n' "${MUESTRAS[@]}" | veredicto
