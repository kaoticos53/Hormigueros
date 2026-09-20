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
PROG="$(mktemp)"; trap 'rm -f "$PROG"' EXIT
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
  echo "X $VIOL cifra(s) en deriva. Dos arreglos posibles, ninguno silencioso:"
  echo "  · el documento está desactualizado → corrige la cifra a $PINES pines / $TESTS tests"
  echo "  · la cifra es un registro con fecha → rodéala de las marcas del documento:"
  echo "      $OPEN_MARK … -->    y    $CLOSE_MARK -->"
  exit 1
fi
echo "✓ las cifras de los documentos coinciden con el repo ($PINES pines, $TESTS tests)"
