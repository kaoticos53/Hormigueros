#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# check-doc-figures.sh — CI: las cifras de los documentos no pueden derivar.
#
# POR QUÉ EXISTE: los documentos de estado afirman el número de PINES de hash y
# el de TESTS de la suite, y esas cifras se movieron varias veces (5 pines → 6,
# 379 tests → 536) mientras los textos se quedaban atrás: el README llegó a
# decir «5 scripts de pin» con seis hashes fijados en `scripts/`, y la lista de
# deuda de F5.1 pedía cosas ya hechas. Ninguna suite detecta eso porque los
# documentos no están en ningún test — hasta ahora.
#
# QUÉ COMPRUEBA: toda afirmación de pines o de tests en los documentos
# ESCANEADOS tiene que coincidir con lo que el repo HACE:
#
#   pines = nº de hashes fijados = ficheros scripts/*.expected (cada uno es un
#           hash comparado byte a byte; el script de replay fija DOS, por eso
#           «6 pines» con 5 scripts de check)
#   tests = nº de casos descubiertos por la suite (dotnet test --list-tests)
#
# Y desde F11h también LAS CIFRAS DE LA PARTIDA que afirma el documento de estado
# (F11h, el eje del precio; F11j, el control del horizonte; F11k, el rastro
# depositado; F11l, el 2×2 completo) —la tabla de recetas del brazo de 10 s y los
# t de sus comparaciones—. Esas no se pueden MEDIR en cada push: cada celda son
# partidas de 216 000 ticks (las 1 056 de F11h son horas de máquina), así que se
# derivan del VOLCADO versionado que las produjo —`scripts/partida-figures.py`
# lee los volcados de `artifacts/` y devuelve cada cifra formateada como la
# escribe el documento— y se exige que el texto siga diciéndola. Dos detalles que
# no son cosméticos:
#
#   · el fragmento se busca en el texto LÓGICO del documento —párrafos y viñetas
#     unidos en una línea, sin el énfasis de markdown y sin las regiones
#     históricas—, no en sus líneas: el documento va re-envolviendo sus párrafos,
#     y buscar por líneas haría que un re-envolturaje silenciara la cifra.
#   · si el volcado no está o no trae la línea esperada, la comprobación FALLA
#     (fail-closed): una cifra que no se puede derivar dejaría el documento sin
#     comprobar, que es justo lo que este guardarraíl existe para impedir.
#
# DOS ESCAPATORIAS, las dos EXPLÍCITAS (una cifra sin marcar se comprueba
# siempre: el guardarraíl es fail-closed):
#
#   1. REGISTRO CON FECHA: lo que va entre `<!-- cifras-historicas -->` y
#      `<!-- /cifras-historicas -->` es historia, no una afirmación de hoy
#      (las notas de versión del README, el registro de rodajas de
#      estado-proyecto). Un número de un cierre pasado es correcto aunque ya no
#      sea el actual; corregirlo sería reescribir la historia.
#   2. DOCUMENTO DE FASE: `docs/fase*-*.md` son bitácoras de fases CERRADAS,
#      con la fecha en cada entrada. Un documento nuevo con cifras que no esté
#      ni escaneado ni clasificado FALLA: clasificarlo es una decisión
#      explícita, no un olvido silencioso.
#
# COSTE, medido en Git Bash (Windows), donde cada proceso cuesta ~50 ms: el
# análisis va en UNA pasada de awk por documento (no un grep por línea: eso
# tardaba minutos), y el barrido de documentos sin clasificar es un solo grep
# sobre el listado de git.
#
# Uso:
#   scripts/check-doc-figures.sh              # mide y comprueba
#   scripts/check-doc-figures.sh --tests N    # usa N en vez de medir la suite
#   scripts/check-doc-figures.sh --selftest   # verifica el analizador, sin repo
#   scripts/check-doc-figures.sh --list       # qué escanea, qué clasifica y con qué cifras
#   python scripts/partida-figures.py --list   # qué volcado alimenta cada cifra de partida
#
# Códigos: 0 sin deriva · 1 hay deriva (o un documento sin clasificar) · 2 uso
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# ── Los documentos que AFIRMAN el estado de hoy ───────────────────────────────
SCANNED=(
  "README.md"
  "docs/estado-proyecto.md"
  "docs/fase5-plan.md"
  "docs/arquitectura.md"
  "docs/especificaciones.md"
  "src/App/AntSim.Unity/README.md"
)

# ── Bitácoras de fases cerradas: cada entrada lleva su fecha ──────────────────
RECORD_GLOBS=(
  "docs/fase*-*.md"
  "docs/politicas-benchmark.md"
  "docs/ideas-externas.md"
)

# ── Marcas de región histórica (en el propio documento) ───────────────────────
# Se comparan como PREFIJO, así que la marca puede llevar su motivo detrás —y es
# lo que se espera: quien la lea dentro de un año tiene que saber por qué esa
# cifra no se comprueba—. El cierre no colisiona con la apertura (lleva «/»).
OPEN_MARK="<!-- cifras-historicas"
CLOSE_MARK="<!-- /cifras-historicas"

# ── Analizador: UNA pasada de awk por documento ───────────────────────────────
# Sin `\b` ni `IGNORECASE` (mawk no los tiene): se baja la línea a minúsculas y
# se usan patrones en minúscula, con `tolower()` (POSIX, la tienen gawk y mawk).
#
# El programa va a un fichero temporal, no a una variable: `read` con IFS por
# defecto COLAPSA los saltos de línea del texto que captura (los trata como
# separadores), y el programa llegaba a awk como una sola línea deformada.
PROG="$(mktemp)"
# Los temporales de las cifras de partida se crean ANTES del trap y este los
# recoge todos: si el trap se declarara antes, un fallo en medio dejaría basura
# en /tmp, y si se declarara dos veces valdría solo el último.
PARTIDA_FIGS="$(mktemp)"
PARTIDA_TXT="$(mktemp)"
PARTIDA_ERR="$(mktemp)"
PARTIDA_SALIDA="$(mktemp)"
trap 'rm -f "$PROG" "$PARTIDA_FIGS" "$PARTIDA_TXT" "$PARTIDA_ERR" "$PARTIDA_SALIDA"' EXIT
cat > "$PROG" <<'AWK'
BEGIN { dentro = 0 }
{
  if (index($0, OPEN) > 0)  { dentro = 1; next }
  if (index($0, CLOSE) > 0) { dentro = 0; next }
  if (dentro) next
  low = tolower($0)

  # pines: «6 pines», «6 pins», «6 scripts de pin»
  s = low
  while (match(s, /[0-9]+ *(pines|pins|scripts de pin)/)) {
    m = substr(s, RSTART, RLENGTH)
    n = m + 0
    if (n != PINS) {
      printf "%s:%d: afirma «%s» y el repo tiene %d pines\n", FILE, NR, m, PINS
      viol++
    }
    s = substr(s, RSTART + RLENGTH)
  }

  # tests: solo cuenta si la línea habla de tests/pruebas/suite
  if (index(low, "test") > 0 || index(low, "prueba") > 0 || index(low, "suite") > 0) {
    t = low
    while (match(t, /[0-9]+\/[0-9]+|[0-9]+ +tests?|suite *[0-9]+/)) {
      # El alcance del match exterior se GUARDA antes de cualquier otro
      # `match()`: el de dentro (para sacar el primer número) pisa
      # RSTART/RLENGTH, y sin guardarlos el avance era de un par de caracteres
      # → el mismo «6/6» se recontaba una y otra vez.
      rs = RSTART; rl = RLENGTH
      m = substr(t, rs, rl)
      if (match(m, /[0-9]+/)) { n = substr(m, RSTART, RLENGTH) + 0 } else { n = -1 }
      # Un ratio SOLO se lee como cifra de suite si es N/N: la suite siempre se
      # escribe así (536/536), mientras que «replay 3000/6000» o «banda
      # 200/450» son otra cosa y no se comprueban.
      ratio = 0
      if (index(m, "/") > 0) {
        split(m, partes, "/")
        if (partes[1] + 0 != partes[2] + 0) { ratio = 1 }
      }
      if (ratio == 0 && n != TESTS) {
        printf "%s:%d: afirma «%s» y la suite tiene %d tests\n", FILE, NR, m, TESTS
        viol++
      }
      t = substr(t, rs + rl)
    }
  }
}
END { exit (viol > 0 ? 1 : 0) }
AWK

# analizar_fichero FICHERO PINES TESTS -> violaciones por stdout (rc 1 si hay)
analizar_fichero() {
  awk -v FILE="$1" -v PINS="$2" -v TESTS="$3" \
      -v OPEN="$OPEN_MARK" -v CLOSE="$CLOSE_MARK" -f "$PROG" "$1"
}

# ── Las cifras de la PARTIDA (F11h–F11k): el derivador y su comprobación ────
# El derivador va en Python (`scripts/partida-figures.py`) porque lee NUEVE
# volcados de tres formatos distintos y les calcula una dispersión y un t; el
# resto sigue siendo bash. El intérprete se resuelve como en los demás
# guardarraíles: `python3` en el runner de CI, `python` en Git Bash de Windows y
# `py` como último recurso.
PARTIDA_TOOL="scripts/partida-figures.py"

resolver_python() {
  local cand
  for cand in python3 python py; do
    if command -v "$cand" >/dev/null 2>&1; then printf '%s' "$cand"; return 0; fi
  done
  return 1
}

# texto_normalizado FICHERO -> el documento en TEXTOS LÓGICOS por stdout.
# Une cada párrafo y cada viñeta en UNA línea (una continuación es una línea
# sangrada que no abre bloque nuevo), quita el énfasis de markdown —`**` no es
# parte del texto— y descarta las regiones históricas, que son la escapatoria
# declarada del propio documento. Se hace por texto y no por línea porque el
# documento va re-envolviendo sus párrafos: buscar la cifra dentro de una línea
# haría que un re-envolturaje la dejara de ver.
texto_normalizado() {
  awk -v OPEN="$OPEN_MARK" -v CLOSE="$CLOSE_MARK" '
    BEGIN { dentro = 0; buf = "" }
    {
      sub(/\r$/, "")
      if (index($0, OPEN) > 0)  { dentro = 1; next }
      if (index($0, CLOSE) > 0) { dentro = 0; next }
      if (dentro) next
      linea = $0
      gsub(/\*\*/, "", linea)
      sub(/^[ \t]+/, "", linea)
      if (linea == "") { if (buf != "") { print buf; buf = "" } ; next }
      if (linea ~ /^([-*#>]|[0-9]+\.)/) { if (buf != "") print buf; buf = linea; next }
      if (buf == "") { buf = linea; next }
      buf = buf " " linea
    }
    END { if (buf != "") print buf }
  ' "$1"
}

# comprobar_fragmentos TEXTO CLAVES SALIDA -> escribe en SALIDA las que faltan y
# deja el recuento en FALTAN. Las claves llegan del derivador: «clave<TAB>lo que
# el documento tiene que decir». La salida va a un FICHERO y no por stdout porque
# el llamador la capturaría con `$(...)`, y eso corre la función en un subproceso:
# el recuento se quedaría en 0 sin que nada avisara (medido al probar esto).
FALTAN=0
comprobar_fragmentos() {
  local texto="$1" claves="$2" salida="$3" clave frag
  FALTAN=0
  : > "$salida"
  while IFS=$'\t' read -r clave frag; do
    # El fichero de claves lo escribe Python: en Windows sus líneas acaban en
    # CRLF y el CR se colaría dentro del fragmento, que entonces no casaría
    # nunca. Se quita de los dos campos, venga el fichero de donde venga.
    clave="${clave%$'\r'}"; frag="${frag%$'\r'}"
    [[ -n "$clave" ]] || continue
    if grep -qF -- "$frag" "$texto"; then
      continue
    fi
    printf '%s: el documento no dice «%s»\n' "$clave" "$frag" >> "$salida"
    FALTAN=$((FALTAN + 1))
  done < "$claves"
}

# ── Medición de la verdad ────────────────────────────────────────────────────
medir_pines() { ls -1 "$ROOT"/scripts/*.expected 2>/dev/null | wc -l | tr -d ' '; }

medir_tests() {
  # `--list-tests` imprime una línea INDENTADA por caso descubierto (el
  # encabezado y los avisos del compilador no van indentados). Verificado
  # contra una corrida real: 536 líneas indentadas = «Total: 536» de la suite,
  # y el recuento no depende del idioma del runner (el resumen sí: «Superado»
  # frente a «Passed»).
  #
  # `</dev/null`: sin esto, un `dotnet test` lanzado desde una terminal se queda
  # esperando entrada y el script cuelga (medido: con la redirección, 2 s).
  local out
  out="$(cd "$ROOT" && dotnet test --nologo -v q --list-tests 2>&1 </dev/null)" || {
    echo "XX no pude listar los tests de la suite" >&2; return 1; }
  printf '%s\n' "$out" | grep -cE '^[[:space:]]+[^[:space:]]'
}

clasificado() { # clasificado RUTA -> 0 si es bitácora de fase cerrada
  local f="$1" g
  for g in "${RECORD_GLOBS[@]}"; do
    # shellcheck disable=SC2053
    [[ "$f" == $g ]] && return 0
  done
  return 1
}

escaneado() {
  local f="$1" s
  for s in "${SCANNED[@]}"; do [[ "$f" == "$s" ]] && return 0; done
  return 1
}

# ── selftest: el analizador, sin repo ────────────────────────────────────────
if [[ "${1:-}" == "--selftest" ]]; then
  T="$(mktemp -d)"
  printf 'ok: 2 pines y 7 tests\n'                        > "$T/ok.md"
  printf 'mal: 3 pines\n'                                > "$T/mal-pines.md"
  printf 'mal: 9 tests\n'                                > "$T/mal-tests.md"
  printf 'mal: 6/6 tests\n'                              > "$T/mal-ratio.md"
  printf 'ok: suite 7/7\n'                               > "$T/ok-suite.md"
  # Dentro: cifras que fallarían. Fuera: cifras correctas. Si el analizador
  # calla, la región exime lo de dentro.
  printf '<!-- cifras-historicas: prueba -->\n3 pines y 99 tests\n<!-- /cifras-historicas -->\n2 pines y 7 tests\n' > "$T/region.md"
  # Al revés: fuera queda una cifra que NO cuadra (línea 4) → debe salir.
  printf '<!-- cifras-historicas: prueba -->\n2 pines y 7 tests\n<!-- /cifras-historicas -->\n3 pines\n' > "$T/region-fuera.md"
  printf 'ruido: 7/7 en cuadrantes del mundo\n'          > "$T/ruido.md"
  printf 'mal: suite 9\n'                                > "$T/mal-suite.md"
  printf 'ruido: replay 3000/6000 y la suite verde\n'    > "$T/ruido-ratio.md"
  printf 'mayusculas: 3 PINES\n'                         > "$T/mal-mayus.md"

  FALLO=0
  esperar() { # esperar FICHERO N_VIOLACIONES
    local salida got
    salida="$(analizar_fichero "$1" 2 7)"
    got="$(printf '%s' "$salida" | grep -c . || true)"
    if [[ "$got" != "$2" ]]; then
      echo "  X $(basename "$1"): esperaba $2 violaciones y salieron $got"
      printf '%s\n' "$salida" | sed 's/^/      /'
      FALLO=1
    else
      echo "  ok $(basename "$1") -> $got violación(es)"
    fi
  }
  echo "check-doc-figures --selftest (pines=2, tests=7)"
  esperar "$T/ok.md" 0
  esperar "$T/mal-pines.md" 1
  esperar "$T/mal-tests.md" 1
  esperar "$T/mal-ratio.md" 1
  esperar "$T/ok-suite.md" 0
  esperar "$T/region.md" 0
  esperar "$T/region-fuera.md" 1
  esperar "$T/ruido.md" 0
  esperar "$T/ruido-ratio.md" 0
  esperar "$T/mal-suite.md" 1
  esperar "$T/mal-mayus.md" 1
  # La línea de fuera de la región (la 4ª) sí se comprueba. La salida se captura
  # ANTES de grepear: `set -o pipefail` haría fallar el `if` porque el analizador
  # sale con 1 justo cuando encuentra algo — que es lo que buscamos.
  fuera="$(analizar_fichero "$T/region-fuera.md" 2 7)"
  if printf '%s\n' "$fuera" | grep -q ':4:'; then
    echo "  ok la región exime solo lo de dentro"
  else
    echo "  X la región histórica no exime la línea de fuera"; FALLO=1
  fi
  # ── Las cifras de la PARTIDA: el fragmento se busca en TEXTOS, no en líneas ──
  # La celda va partida por un salto de línea y el número en negrita, como en el
  # documento real: las dos cosas tienen que dar igual. Una cifra dentro de la
  # región histórica no cuenta (es la escapatoria declarada) y una que no está se
  # declara. Con las tres claves a la vez, «viva» y «negrita» se encuentran y
  # faltan exactamente dos.
  printf -- '- **una celda\n  partida por la mitad**: 12,3 (4–9)\n' > "$T/partida.md"
  printf -- '| fila | **165,1** (114–189) |\n' >> "$T/partida.md"
  printf -- '<!-- cifras-historicas: prueba -->\n9,9 (1–2)\n<!-- /cifras-historicas -->\n' \
    >> "$T/partida.md"
  printf 'viva\t12,3 (4–9)\nnegrita\t165,1 (114–189)\nhistorica\t9,9 (1–2)\nausente\tno está por ningún lado\n' \
    > "$T/partida.claves"
  texto_normalizado "$T/partida.md" > "$T/partida.norm"
  comprobar_fragmentos "$T/partida.norm" "$T/partida.claves" "$T/partida.faltan"
  if [[ "$FALTAN" == "2" ]] && grep -q '^historica:' "$T/partida.faltan" &&
     grep -q '^ausente:' "$T/partida.faltan" &&
     ! grep -q '^viva:' "$T/partida.faltan"; then
    echo "  ok la cifra de partida se busca en el texto (envuelta y en negrita) y se exime la histórica"
  else
    echo "  X la comprobación de cifras de partida falla (faltan=$FALTAN)"
    sed 's/^/      /' "$T/partida.faltan"
    FALLO=1
  fi

  # El derivador de las cifras de partida también se prueba aquí: con volcados de
  # mentira (formato, aritmética del pareado por sorteo, fail-closed) y sin tocar
  # el repo. Va con el mismo criterio que el resto —sin intérprete es un fallo, no
  # un salto—: el guardarraíl entero depende de él.
  PY="$(resolver_python || true)"
  if [[ -z "$PY" ]]; then
    echo "  X no hay Python 3 en el PATH: no puedo probar el derivador de cifras de partida"
    FALLO=1
  elif salida="$(PYTHONUTF8=1 "$PY" "$ROOT/$PARTIDA_TOOL" --selftest 2>&1)"; then
    printf '%s\n' "$salida" | tail -3 | sed 's/^/  /'
  else
    echo "  X el selftest del derivador de cifras de partida falla"
    printf '%s\n' "$salida" | sed 's/^/      /'
    FALLO=1
  fi

  # Clasificación de documentos: bitácora vs estado.
  if clasificado "docs/fase5-2b-eciton.md" && clasificado "docs/fase4-resumen.md" &&
     ! clasificado "README.md" && ! clasificado "docs/estado-proyecto.md"; then
    echo "  ok la clasificación distingue bitácoras de documentos de estado"
  else
    echo "  X la clasificación de bitácoras falla"; FALLO=1
  fi
  rm -rf "$T"
  [[ "$FALLO" -eq 0 ]] && echo "✓ analizador de cifras documentales correcto" || echo "✗ analizador roto"
  exit "$FALLO"
fi

# ── Uso y modo listado ───────────────────────────────────────────────────────
TESTS=""
case "${1:-}" in
  "") ;;
  --tests)
    TESTS="${2:-}"
    [[ "$TESTS" =~ ^[0-9]+$ ]] || { echo "uso: --tests N" >&2; exit 2; }
    ;;
  --list) ;;
  *) echo "uso: scripts/check-doc-figures.sh [--tests N|--selftest|--list]" >&2; exit 2 ;;
esac

cd "$ROOT" || exit 2
PINES="$(medir_pines)"
if [[ -z "$TESTS" ]]; then
  TESTS="$(medir_tests)" || exit 1
fi

if [[ "${1:-}" == "--list" ]]; then
  echo "cifras del repo: $PINES pines · $TESTS tests"
  echo "escaneados (afirman el estado de hoy):"
  for f in "${SCANNED[@]}"; do printf '  %s\n' "$f"; done
  echo "clasificados como bitácora de fase cerrada (con fecha en cada entrada):"
  for g in "${RECORD_GLOBS[@]}"; do printf '  %s\n' "$g"; done
  echo "cifras de la partida (F11h–F11l): las deriva $PARTIDA_TOOL de los volcados de artifacts/"
  echo "  el detalle cifra a cifra y su volcado: python $PARTIDA_TOOL --list"
  exit 0
fi

echo "cifras del repo: $PINES pines · $TESTS tests"
VIOL=0
for f in "${SCANNED[@]}"; do
  [[ -f "$f" ]] || continue
  salida="$(analizar_fichero "$f" "$PINES" "$TESTS")"
  if [[ -n "$salida" ]]; then
    printf '%s\n' "$salida"
    VIOL=$((VIOL + $(printf '%s\n' "$salida" | grep -c . || true)))
  fi
done

# ── Las cifras de la PARTIDA (F11h–F11k) contra sus volcados ───────────────
# El derivador decide QUÉ dice el documento y de dónde lo saca; aquí solo se
# exige que lo diga. Si el derivador no puede trabajar (sin Python, sin volcado,
# sin la línea esperada) eso es una violación, no un «no comprobado»: un
# documento que se queda sin comprobar es exactamente lo que este guardarraíl
# existe para impedir.
FIGURAS=0
PY="$(resolver_python || true)"
if [[ -z "$PY" ]]; then
  echo "$PARTIDA_TOOL: no hay Python 3 en el PATH (python3, python o py)"
  VIOL=$((VIOL + 1))
else
  if PYTHONUTF8=1 "$PY" "$ROOT/$PARTIDA_TOOL" > "$PARTIDA_FIGS" 2> "$PARTIDA_ERR" &&
     [[ -s "$PARTIDA_FIGS" ]]; then
    : > "$PARTIDA_TXT"
    for f in "${SCANNED[@]}"; do
      [[ -f "$f" ]] && texto_normalizado "$f" >> "$PARTIDA_TXT"
    done
    FIGURAS="$(grep -c . "$PARTIDA_FIGS")"
    comprobar_fragmentos "$PARTIDA_TXT" "$PARTIDA_FIGS" "$PARTIDA_SALIDA"
    if [[ "$FALTAN" -gt 0 ]]; then
      cat "$PARTIDA_SALIDA"
      VIOL=$((VIOL + FALTAN))
    fi
  else
    echo "$PARTIDA_TOOL no pudo derivar las cifras de la partida:"
    sed 's/^/    /' "$PARTIDA_ERR" | head -5
    VIOL=$((VIOL + 1))
  fi
fi

# Un documento CON CIFRAS que no esté escaneado ni clasificado es un olvido: el
# guardarraíl no puede cubrir lo que nadie declaró. Un solo grep sobre el
# listado de git (no un grep por fichero: `git ls-files '*.md'` trae cientos).
while IFS= read -r f; do
  [[ -f "$f" ]] || continue
  escaneado "$f" && continue
  clasificado "$f" && continue
  if grep -qiE '[0-9]+ *(pines|pins|scripts de pin)|[0-9]+ *tests?|suite *[0-9]' "$f"; then
    echo "$f: documento con cifras sin clasificar (añádelo a SCANNED o a RECORD_GLOBS)"
    VIOL=$((VIOL + 1))
  fi
done < <(git ls-files '*.md')

if [[ "$VIOL" -gt 0 ]]; then
  echo ""
  echo "X $VIOL cifra(s) en deriva. Arreglos posibles, ninguno silencioso:"
  echo "  · el documento está desactualizado → corrige la cifra a $PINES pines / $TESTS tests"
  echo "  · la cifra es un registro con fecha → rodéala de las marcas del documento:"
  echo "      $OPEN_MARK … -->    y    $CLOSE_MARK -->"
  echo "  · es una cifra de PARTIDA (F11h–F11l) → o el documento cambió, o cambió el volcado que la"
  echo "    alimenta: el detalle de cada cifra y su volcado lo da python $PARTIDA_TOOL --list"
  exit 1
fi
echo "✓ las cifras de los documentos coinciden con el repo ($PINES pines, $TESTS tests y $FIGURAS cifras de partida contra sus volcados)"
