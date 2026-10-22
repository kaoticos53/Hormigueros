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
#   + ICONOS (F5.1): el recorte NATIVO de las tarjetas de colonia, con la posición
#     y el avance que uGUI le da a cada icono de la fuente propia, y el frame
#     entero. Es la evidencia para comparar los iconos del HUD contra la
#     referencia del `.ttf` al tamaño efectivo (scripts/playpass-icons.py): la
#     legibilidad a 14 px se había verificado sobre una muestra del .ttf, no
#     sobre lo que la tarjeta pinta. El volcado va a artifacts/playpass-hud-icons.txt
#     (o $HUD_ICONS) y los PNG (frame y tarjetas) se conservan en artifacts/playpass-hud-icons/.
#
#     Y EL VEREDICTO CUENTA: `scripts/playpass-icons.py` no es un informe que se
#     mira sino un PASO de este pass — si algún icono del HUD no se clasifica
#     como SU glifo (correlación por encima del mínimo, margen sobre el segundo
#     glifo y tinta), el pass sale ≠ 0 con el glifo y sus números. Antes el
#     comparador se ejecutaba a mano y un icono roto pasaba en verde.
#
#   + DESCARGAS EN LA GRÁFICA (F5.1): la serie de descargas por ventana —las
#     barras ámbar sobre el área de reserva— se verificó headless sobre el modelo
#     puro (`ColonySparklineModel`). Eso prueba la TEXTURA, no que la tarjeta la
#     pinte. `scripts/unity/ProbeSparklinePixels.cs` inyecta una serie CONOCIDA
#     en el modelo real de cada tarjeta (cuatro ventanas con 1, 3, 5 y 7
#     descargas, separadas por ceros), deja dibujar un frame y recorta del frame
#     REAL la banda de la gráfica; `scripts/playpass-sparkline.py` mide los
#     píxeles: exactamente una barra por ventana con descargas, cada una en las
#     columnas de SU muestra, la altura creciendo con la cuenta, todo naciendo en
#     la base (la orientación se comprueba, no se supone) y ni un píxel ámbar
#     fuera de la banda. La serie real se restaura en la misma sonda, así que el
#     HUD no se queda con la de prueba. Volcado en
#     artifacts/playpass-sparkline-pixels.txt y PNG (frame, banda por tarjeta y
#     hoja de medida) en artifacts/playpass-sparkline-pixels/. Falla cerrado: sin
#     captura o sin PNG conservados, el paso NO es verde.
#
# LA SEÑAL ES LA MUESTRA, NO UNA FOTO: el stream del CLI llega entero de golpe y
# el presenter lo reproduce con buffer, así que una sola lectura en el instante
# equivocado no ve nada. Las muestras se acumulan cada segundo hasta el último
# tick.
##   + REFERENCIAS (F5.1): que el editor no dejara un asset sin su dato. La
#     canonicalización arregla los BYTES (anclas, orden) y no mira el contenido:
#     un material puede quedar canónico y sin su textura. Medido el 2026-10-08:
#     al cerrarse, el editor dejó `PheromoneMat.mat` con el `_BaseMap` en
#     `{fileID: 0}` y el quad de feromonas se pintó BLANCO OPACO tapando el
#     tablero — con todas las muestras de estado en verde. Se comprueba justo
#     después de generar y canonicalizar (scripts/unity-reference-check.py) y el
#     paso FALLA CERRADO: una pérdida de verdad bloquea el pase.
#
#   + FORMA CANÓNICA (el byte no cambia entre pasadas): el generador CIERRA su
#   propia escritura con anclas derivadas del CONTENIDO
#   (Assets/Scripts/EditorTools/GeneratedAssets.cs →
#   scripts/unity-canonicalize-assets.py), así que aquí no se arregla nada: se
#   COMPRUEBA, justo después de generar y antes de medir. Unity asigna el
#   `m_LocalIdentfierInFile` de un contador con base ALEATORIA por sesión —dos
#   pases del mismo montaje dieron bases 54570640 y 8954272— y URP añade a cada
#   `.mat` un bloque `AssetVersion` con un id de 64 bits que CAMBIA DE SITIO: sin
#   esto, cada pasada reescribe 17 assets (16 `.mat` y `Game.unity`) con bytes
#   distintos aunque la escena sea la misma, y el diff no dice nada de nadie.
#   Canónico, dos pasadas seguidas dan el MISMO `git diff`. Y el paso es un
#   VEREDICTO: si algún asset derivaría —o la herramienta no puede analizarlo— el
#   pase sale ≠ 0, porque eso es un fallo del generador y taparlo reescribiéndolo
#   aquí volvería a hacer que los assets dependan de este paso. `--recanonicalize`
#   reescribe lo que derive y sigue (árbol que no viene del generador), y
#   `--no-canonicalize` se salta la comprobación.
#
#   Y EL PASE, QUE TAMBIÉN ESCRIBE, CIERRA LO SUYO: el bloque 4 guarda la escena
#   con `save_scene` —esa configuración es la que se COMMITEA en `Game.unity`— y
#   un guardado del editor deja las anclas de sesión aunque el contenido no
#   cambie; medido el 2026-10-10, un `save_scene` sobre la escena ya canónica la
#   deja derivando (425 documentos, sólo forma) y, con la comprobación puesta
#   después, el pase se plantaba culpando al generador y el bloque 4 no llegaba a
#   sondear. Por eso el veredicto del GENERADOR se toma justo después de generar
#   y ANTES de que el pase escriba, y cada guardado del pase se cierra con el
#   MISMO canonicalizador y desde DENTRO del editor (que además recarga la escena
#   del fichero canónico): no arregla lo que dejó el generador, arregla lo suyo, y
#   no escribe por detrás de una escena abierta.
#
#   Y EL EDITOR QUE NO RESPONDE TIENE SU PROPIO MENSAJE: que el editor ESTÉ abierto
#   no es que CONTESTE —`unity status` lo sirve el registro del CLI, no el editor,
#   y seguía diciendo «ready» con el hilo principal bloqueado por un diálogo—. Al
#   empezar se le pregunta de verdad (`editor_status`) y CADA respuesta se CLASIFICA
#   en cuatro cosas que no se pueden confundir: `ok` (contestó), `error` (contestó y
#   la culpa es de la escena), `mudo` (ni JSON) y `bloqueado` (caducó, con el
#   envelope de error del CLI). Las dos últimas NO son la escena: el pase se planta
#   ahí mismo (rc 3) diciendo qué pasó, que no es la escena, y el remedio —con la
#   marca «Main thread operation timed out» de la COLA de su log como evidencia—,
#   en vez de seguir, hablar de la escena y morir más tarde con un campo vacío. Lo
#   medido: «The open scene(s) have been modified externally», que Unity levanta
#   cuando algo escribe en disco una escena abierta.
#
#   + ORIENTACIÓN DE LOS ACTORES (el morro al rumbo): la caja del actor tiene el
#   largo en Y y la hormiga mira hacia +Y; el giro del presenter tiene que llevar
#   ese eje al rumbo del Core —`90° menos el rumbo`— o el bicho anda de lado, que
#   es lo que se veía en pantalla (medido el 2026-10-10: 90° justos de desvío) y
#   lo que ningún check de estado ve. Lo mide dentro del editor
#   `scripts/unity/ProbeActorOrientation.cs`, con la rotación del propio presenter.
#
#   + Y LA MISMA PREGUNTA, MEDIDA EN PÍXELES: la matriz correcta no prueba el
#   RENDER. Entre la matriz y lo que ve el jugador quedan el encuadre de la cámara
#   (¿mira al nadir? ¿su arriba es el norte de pantalla?), la proyección, el
#   rasterizado y el aliasing —y el giro se cuela justo ahí—. `ProbeAntHeadingPixels`
#   renderiza la malla y el material REALES con un rumbo fijo, a la escala de la
#   partida (~19 px de hormiga) y 6× más cerca, y mide el eje y el MORRO de la
#   silueta en los píxeles; la evidencia (una hormiga por rumbo con el rumbo
#   esperado pintado encima) se conserva en artifacts/playpass-heading-pixels/.
#
#   + DESCARTE AL TERMINAR (el árbol no se queda sucio): el pase GENERA y GUARDA
#   (bootstrapper, `save_scene`, la propia re-serialización al sondear y cerrar
#   Play), y eso reescribe anclas, reordena documentos y mueve el `AssetVersion`.
#   Si los assets NO se van a commitear, ese churn es ruido de forma que el
#   siguiente `git diff` mezclaría con lo que el pase fue a verificar; así que al
#   terminar se descarta, por el MISMO camino que el reparto de commits
#   (`--discard-churn` de commit-layers.sh delega en check-unity-assets.sh: una
#   sola idea de qué es churn) — y NO antes: descartar mientras se juega dejaría
#   al editor reescribiendo lo recién limpiado. `--no-discard-churn` lo salta, para
#   + LA HUELLA DEL ÁRBOL (el pase no acumula estado): todo lo anterior descansa en
#   un supuesto que nadie comprobaba —que una SEGUNDA pasada del mismo montaje deja
#   el árbol exactamente como lo dejó la primera—. El paso 8bis toma una huella
#   (estado y blob de contenido de cada fichero del ALCANCE que difiere de HEAD: los
#   volcados de `artifacts/` y los documentos de asset del proyecto) y la guarda en
#   `artifacts/playpass-fingerprint.txt` junto al montaje (HEAD + semilla + grid +
#   ticks + velocidad + pool). La pasada SIGUIENTE calcula la suya y las compara:
#   mismo montaje ⇒ misma huella, y si no lo son dice QUÉ fichero difiere. Los
#   volcados de MEDIDA (muestras vivas, aspecto, bloque 4, recortes del HUD) cambian
#   con el reloj por construcción: de ellos se compara el nombre y el estado, no el
#   contenido. La maquinaria se prueba sin editor en el selftest, con un repositorio
#   de mentira.
#
# Uso:
#   scripts/playpass-live.sh                     # contra el editor abierto
#   scripts/playpass-live.sh --no-block4         # solo los bloques 3 y 5
#   scripts/playpass-live.sh --samples FICHERO   # solo analiza muestras (3 y 5)
#   scripts/playpass-live.sh --analyze-block4 F  # solo analiza pasos del bloque 4
#   scripts/playpass-live.sh --analyze-visual    # solo analiza el aspecto (F5.1)
#   scripts/playpass-live.sh --analyze-icons     # solo clasifica los iconos (F5.1)
#   scripts/playpass-live.sh --analyze-sparkline # solo mide las barras de la gráfica
#   scripts/playpass-live.sh --analyze-walls     # solo mide las paredes del terreno
#   scripts/playpass-live.sh --selftest          # verifica los analizadores (sin editor)
#
# Opciones: --speed N (def. 3) · --ticks N (def. 7200) · --grid N (def. 96) ·
# --colonies N (def. 2) · --seed N (def. 42) · --seed-pool RUTA|- (def.
# artifacts/pretrain-warm-v2.antgenome) · --deadline SEG (def. 240) · --max-stack
# N (def. 4) · --seeded-colony N (def. 0) · --no-relay · --no-contrast ·
# --out FICHERO · --no-stream-check · --quiet · --no-block4 · --block4-speed N
# (def. 2) · --block4-deadline SEG (def. 200) · --block4-samples FICHERO ·
# --analyze-block4 FICHERO · --analyze-visual · --analyze-icons · --icons-dump
# FICHERO · --icons-crops DIR · --analyze-sparkline · --spark-dump FICHERO ·
# --spark-crops DIR · --analyze-walls · --wall-dump FICHERO · --wall-crops DIR ·
# --no-canonicalize · --recanonicalize · --no-discard-churn.
#
# El comparador de iconos y los medidores de la gráfica y de las paredes
# necesitan Pillow (el primero rasteriza el `.ttf`; los otros leen los PNG de la
# captura): se usa el primer intérprete del PATH que lo tenga (medido: `python`
# sí, `python3` no) y se puede forzar con $ICONS_PY / $SPARK_PY / $WALL_PY.
#
# Salida: 0 ok · 1 invariante roto · 2 uso · 3 no se puede conducir el editor (no
# hay ninguno abierto, no está el CLI, o NO RESPONDE: un diálogo modal o el hilo
# principal atascado — que se dice con la evidencia y con el remedio, y nunca como
# «la escena no tiene HUD»).
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
# F6: el horizonte y la velocidad del bloque 4 son los de la PARTIDA publicada
# (`LaunchSettings.Defaults`: 216.000 ticks a 120×), porque el bloque 4 GUARDA la
# escena: si el pase midiera otra configuración, el `Game.unity` versionado
# volvería al horizonte viejo en la siguiente pasada. Los valores viejos (7200 /
# 2×) se quedaron cortos para la partida de 2 h; la velocidad alta no le quita
# nada a los gates —la alerta del paso C llega en menos de un segundo de reloj y
# el pase sondea muestras, no instantes únicos—.
TICKS=216000
GRID=96
COLONIES=2
SEED=42
# F7: el Play pass juega con el pool por defecto del juego — el entrenado con
# obstáculos — para que lo medido sea lo que el jugador arranca.
SEED_POOL="artifacts/pretrain-maze.antgenome"
DEADLINE=240
MAX_STACK=4
SEEDED_COLONY=0
CONTRAST=1
OUT=""
INTERVAL=1
STREAM_FILE=""
BLOCK4=1
# Velocidad del bloque 4: la de la escena publicada (120×). Antes era 2× para
# que la partida de 7200 ticks no se consumiera antes de que el paso C empezara
# a sondear; con el horizonte de F6 (216.000 ticks) el problema desaparece y lo
# que hay que garantizar es que `save_scene` deje la MISMA configuración que el
# bootstrapper (`LaunchSettings.Defaults`), o el `Game.unity` versionado
# derivaría en cada pasada. El paso C sigue viendo la alerta: con 120× el
# primer toast cae en la primera muestra.
B4_SPEED=120
B4_DEADLINE=200
B4_SAMPLES=""
B4_ANALYZE=""
VIS_ANALYZE=0
VIS_PLAY=""
VIS_EDIT=""
VIS_SCENE=""
HUD_ICONS=""
ICONS_ANALYZE=0
ICONS_CROPS=""
# Intérprete para el comparador de iconos (necesita Pillow): vacío = se resuelve
# entre los del PATH (ver python_with_pillow).
ICONS_PY=""
ICONS_SCRIPT="$ROOT/scripts/playpass-icons.py"
# Dónde viven los recortes del HUD. La sonda `ProbeHudIconShot` los escribe en
# `<proyecto>/Temp/`, que Unity BORRA al cerrar el editor, y son la evidencia que
# el paso de iconos mira (el frame vivo, el PNG de cada tarjeta y la hoja de
# comparación, que se genera en esta misma carpeta). Por eso se conservan aquí,
# junto al volcado: un paso cuyo objeto de medida desaparece con la sesión deja
# el veredicto sin nada que mirar después. `--icons-crops DIR` apunta a otros.
ICONS_DIR="$ROOT/artifacts/playpass-hud-icons"

# Lo mismo para la gráfica de descargas: la sonda deja en `Temp/` el frame vivo y
# el recorte de la banda de CADA tarjeta, y el medidor trabaja sobre esas copias.
SPARK_DUMP=""
SPARK_ANALYZE=0
SPARK_CROPS=""
SPARK_PY=""
SPARK_SCRIPT="$ROOT/scripts/playpass-sparkline.py"
SPARK_DIR="$ROOT/artifacts/playpass-sparkline-pixels"

# Y lo mismo para las PAREDES del terreno (F6): la sonda deja en `Temp/` el frame
# vivo y la hoja con las cajas esperadas (magenta) y los controles (cian), y el
# medidor vuelve a los píxeles para comprobar que el mapa del laberinto está en
# pantalla — color, sitio y escala. `LastWallDraws` se publicó durante una fase
# entera sin que nadie lo leyera: este paso es quien lo lee.
WALL_DUMP=""
WALL_ANALYZE=0
WALL_CROPS=""
WALL_PY=""
WALL_SCRIPT="$ROOT/scripts/playpass-walls.py"
WALL_DIR="$ROOT/artifacts/playpass-wall-pixels"

# Dónde vive la evidencia en PÍXELES del rumbo (`ProbeAntHeadingPixels` la escribe en
# `<proyecto>/Temp/`, que Unity borra al cerrar el editor): el PNG con una hormiga por
# rumbo y el rumbo esperado pintado encima es lo que se mira para creerse la medida.
HEADING_DIR="$ROOT/artifacts/playpass-heading-pixels"

# ── La HUELLA del árbol (paso 8bis): ¿dos pasadas del mismo montaje dejan el MISMO
# diff contra HEAD? El ALCANCE es lo que el PASE escribe: los volcados de
# `artifacts/` y los DOCUMENTOS DE ASSET del proyecto —la misma lista de extensiones
# con la que `scripts/unity-asset-equivalence.py` decide qué es un asset; el `.cs` es
# código de quien edita, no cosa del pase—. Los volcados de MEDIDA cambian con el
# reloj por construcción: de ellos se compara el NOMBRE y el estado, no el contenido.
FP_FILE="artifacts/playpass-fingerprint.txt"
# El alcance de `artifacts/` es `playpass-*` —lo que el pase escribe—, NO el
# directorio entero: cualquier otra cosa que viva ahí (un log, una prueba a mano, un
# artefacto viejo) no es del pase, y meterla haría que su simple presencia o su
# desaparición rompiera la comparación con un falso fallo.
FP_SCOPE=("artifacts/playpass-*" src/App/AntSim.Unity/Assets src/App/AntSim.Unity/ProjectSettings)
FP_ASSET_SUFFIXES=(".mat" ".unity" ".asset" ".prefab" ".controller" ".anim" ".meta")
FP_LIVE=(artifacts/playpass-samples.txt artifacts/playpass-visual.txt \
         artifacts/playpass-visual-edit.txt artifacts/playpass-block4.txt \
         artifacts/playpass-scene.txt artifacts/playpass-hud-icons.txt \
         artifacts/playpass-hud-icons/ artifacts/playpass-sparkline-pixels.txt \
         artifacts/playpass-sparkline-pixels/ artifacts/playpass-wall-pixels.txt \
         artifacts/playpass-wall-pixels/)
# (`playpass-scene.txt` entra en la lista por su línea `presenter.live tick=… ants=…`,
# que es una lectura del mundo EN MARCHA; lo que describe —materiales, rects— son
# ASSETS, y ésos sí se comparan, como ficheros del alcance.)
#
# Lo que NO está en la lista se compara por CONTENIDO, y eso incluye la evidencia en
# píxeles del rumbo (`artifacts/playpass-heading-pixels/`): es un render fuera de
# pantalla de una malla fija con un giro fijo, así que dos pasadas tienen que dejar
# el mismo PNG — si no, el propio invariante lo dice.

# Comprobación de la forma canónica de los assets GENERADOS (ver el encabezado).
# 1 = se comprueba; con --recanonicalize además se reescriben los que deriven.
CANONICALIZE=1
RECANONICALIZE=0
CANONICALIZE_SCRIPT="$ROOT/scripts/unity-canonicalize-assets.py"

# Verificador de REFERENCIAS de los assets generados (ver el encabezado). 1 = activo.
REFERENCES_SCRIPT="$ROOT/scripts/unity-reference-check.py"

# Descarte del churn de re-serialización AL TERMINAR (ver el encabezado). 1 = se
# descarta; `--no-discard-churn` lo deja como lo dejó el pase (assets que sí se
# van a commitear).
DISCARD_CHURN=1
ASSETS_SCRIPT="$ROOT/scripts/check-unity-assets.sh"

log() { [[ $QUIET -eq 1 ]] || echo "$@"; }

# ── La forma canónica de los assets generados: VEREDICTO, no reparación ──────
# El generador CIERRA su propia escritura canonicalizando lo que escribe
# (Assets/Scripts/EditorTools/GeneratedAssets.cs, que llama a este mismo
# scripts/unity-canonicalize-assets.py), así que aquí ya no hay nada que
# arreglar: se COMPRUEBA, y si algún asset derivaría del canónico el pase se
# planta. Eso es un fallo del GENERADOR —dejó anclas de sesión en la escena o en
# los materiales— y taparlo reescribiéndolos aquí sería volver a depender de que
# este paso lo arregle, que es justo lo que se quitó.
#
#   · `--recanonicalize`: reescribe lo que derive y SIGUE, avisando. Es para un
#     árbol que NO viene del generador (un checkout viejo, assets tocados a mano);
#     con el generador de hoy no debería hacer falta nunca.
#   · `--no-canonicalize`: se salta la comprobación entera.
#
# FALLA CERRADO también cuando la herramienta no puede dar veredicto (un asset
# ilegible devuelve el mismo rc 1): dar por bueno un árbol sin mirarlo sería el
# falso verde que ya nos mordió con los iconos.
check_canonical() {
  local label="$1" py rc=0 out line modo="--check"
  if [[ $CANONICALIZE -ne 1 ]]; then
    log "  (comprobación del canónico saltada con --no-canonicalize)"
    return 0
  fi
  if [[ ! -f "$CANONICALIZE_SCRIPT" ]]; then
    echo "✗ falta el canonicalizador: $CANONICALIZE_SCRIPT" >&2
    return 1
  fi
  py="$(command -v python3 || command -v python || true)"
  if [[ -z "$py" ]]; then
    echo "✗ falta Python 3 (la forma canónica de los assets se comprueba con él)" >&2
    return 1
  fi
  [[ $RECANONICALIZE -eq 1 ]] && modo=""
  # PYTHONUTF8=1 no es opcional: sin él, imprimir ✓/✗ en la consola cp1252 de
  # Windows revienta con UnicodeEncodeError aunque el trabajo esté hecho.
  out="$(PYTHONUTF8=1 "$py" "$CANONICALIZE_SCRIPT" --project "$PROJECT" $modo 2>&1)" || rc=$?
  while IFS= read -r line; do
    [[ -n "$line" ]] && log "  ${line%$'\r'}"
  done <<<"$out"
  if [[ $rc -eq 0 ]]; then
    [[ $RECANONICALIZE -eq 1 ]] && \
      log "  (--recanonicalize: reparación del canónico OK tras $label)"
    return 0
  fi
  if [[ $RECANONICALIZE -eq 1 ]]; then
    echo "✗ el canonicalizador no pudo repararlo tras $label (rc=$rc) — no se mide" >&2
    return 1
  fi
  echo "✗ los assets generados NO quedaron en forma canónica tras $label (rc=$rc):" >&2
  echo "  o algún asset deriva del canónico, o la herramienta no pudo analizarlo (mismo" >&2
  echo "  rc; su salida de arriba dice cuál). Es un fallo del generador: GeneratedAssets" >&2
  echo "  cierra cada escritura, así que" >&2
  echo "  su log del editor dice qué pasó (¿ANTSIM_CANONICALIZE=0? ¿sin Python?)." >&2
  echo "  Si el árbol NO viene del generador (checkout viejo, assets a mano), repite" >&2
  echo "  el pase con --recanonicalize para reescribirlos y seguir." >&2
  return 1
}

# ── El pase también ESCRIBE: cierra su propia escritura ──────────────────────
# `save_scene` no lo hace el generador: lo hace el pase, y ahí quien escribe es el
# serializador del EDITOR, que numera las anclas con el contador de SESIÓN. O sea
# que lo que el pase acaba de guardar no es canónico aunque la generación lo
# fuera. Medido (2026-10-10, proyecto real): un `save_scene` sobre una escena YA
# canónica, sin cambiarle el contenido, la deja DERIVANDO (425 documentos, sólo
# anclas y blancos) — y con la comprobación DESPUÉS del guardado el pase se
# plantaba culpando al generador de lo que había escrito él mismo, y el bloque 4
# no llegaba a sondear nada.
#
# Así que cada `save_scene` del pase se cierra con el MISMO canonicalizador (una
# sola definición de «canónico», la del generador) y sólo después se comprueba.
# El generador conserva su veredicto aparte y SIN reparación, inmediatamente
# después de generar —que es lo que este paso NO hace: no arregla lo que dejó el
# generador, arregla lo que dejó el pase.
#
# Y SE CIERRA DESDE DENTRO DEL EDITOR, no desde fuera (sonda
# `scripts/unity/ProbeCanonicalizeAssets.cs`), por lo mismo que el generador lo
# hace desde dentro: escribir la escena por detrás con el editor teniéndola
# abierta le hace notar el cambio al vigilante y levanta el modal «The open
# scene(s) have been modified externally», que deja el editor sordo a TODOS los
# comandos (medido: el pipeline devolviendo «Main thread operation timed out»
# hasta que alguien pulsa el diálogo). La sonda canonicaliza y, si la escena es
# la abierta, la RECARGA del fichero canónico: el editor queda mirando el fichero
# y su siguiente guardado escribe anclas canónicas.
cerrar_escritura_propia() {
  local label="$1" out kind detalle
  if [[ $CANONICALIZE -ne 1 ]]; then
    log "  (la forma de lo que guarda el pase no se cierra: --no-canonicalize)"
    return 0
  fi
  # `aviso|` (no medible: sin Python 3, sin la herramienta) no es fallo —«no
  # medido» no es «roto», como en las puertas del pre-push—; `error|` sí: la
  # herramienta corrió y dijo que algún asset deriva.
  out="$(UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeCanonicalizeAssets.cs")" \
           2>/dev/null | cmd_result 2>/dev/null || true)"
  kind="${out%%|*}"
  detalle="${out#*|}"
  case "$kind" in
    ok)
      log "  (lo que guardó el pase tras $label quedó en forma canónica, cerrado dentro del editor: ${detalle:-sin detalle})"
      return 0 ;;
    aviso)
      log "  (la forma de lo que guardó el pase tras $label NO se pudo medir: ${detalle:-sin detalle})"
      return 0 ;;
    error)
      echo "✗ no se pudo cerrar la forma de lo que guardó el pase tras $label: ${detalle:-sin detalle}" >&2
      return 1 ;;
    *)
      echo "✗ el editor no respondió al cerrar la forma de lo que guardó el pase tras $label" >&2
      return 1 ;;
  esac
}

# Guardar y cerrar, en ese orden y sin que nada se cuele en medio: separarlos fue
# justo lo que dejó la escena del bloque 4 derivando con el veredicto puesto.
guardar_escena() { # guardar_escena <ruta> <etiqueta>
  UCMD save_scene --path "$1" >/dev/null 2>&1 || true
  cerrar_escritura_propia "$2"
}

# ── Verificación de REFERENCIAS de los assets generados ──────────────────────
# Justo DESPUÉS de que el bootstrapper genere la escena y los materiales (y de
# canonicalizarlos): es el momento en el que el editor REESCRIBE los assets, y el
# único en el que una referencia se pierde sin dejar error. Medido: una pasada del
# editor dejó `PheromoneMat.mat` con el `_BaseMap` en `{fileID: 0}` —sin su
# textura de reposo— y el quad de feromonas se pintó BLANCO OPACO sobre el
# tablero; el YAML era válido y ningún check de estado lo veía.
#
# FALLA CERRADO por el mismo motivo que la canonicalización: si el verificador no
# puede leer el árbol (sin git, sin Python, un asset ilegible), el pase se planta
# en vez de medir un mundo al que le falta un dato. Si el fallo es de ENTORNO y no
# de referencias, el script lo distingue (3 = no medible) y se avisa sin romper el
# paso; un 1 —referencia perdida de verdad— sí bloquea.
check_references() {
  local label="$1" py rc=0 out line
  if [[ ! -f "$REFERENCES_SCRIPT" ]]; then
    echo "✗ falta el verificador de referencias: $REFERENCES_SCRIPT" >&2
    return 1
  fi
  py="$(command -v python3 || command -v python || true)"
  if [[ -z "$py" ]]; then
    echo "  (sin Python 3: no se comprueban las referencias de los assets)"
    return 0
  fi
  out="$(PYTHONUTF8=1 "$py" "$REFERENCES_SCRIPT" --project "$PROJECT" --quiet 2>&1)" || rc=$?
  while IFS= read -r line; do
    [[ -n "$line" ]] && log "  ${line%$'\r'}"
  done <<<"$out"
  case $rc in
    0) return 0 ;;
    3) echo "  (referencias NO comprobadas tras $label: el verificador no pudo medir, rc=3)" >&2; return 0 ;;
    *)
      echo "✗ referencias perdidas tras $label (rc=$rc): el editor dejó un asset sin su dato" >&2
      return 1 ;;
  esac
}

# ── Descarte del churn de re-serialización al TERMINAR ──────────────────────
# El pase GENERA (bootstrapper), GUARDA (`save_scene`) y sondea en Play, y el
# editor re-serializa mientras tanto: anclas nuevas, otro orden, el `AssetVersion`
# de cada `.mat` movido. Si los assets NO se van a commitear, ese churn es ruido
# de forma que el siguiente `git diff` mezclaría con lo que el pase fue a
# verificar — así que al terminar se descarta.
#
# Se descarta AQUÍ, al final, y no antes: mientras hay Play vivo, el editor sigue
# reescribiendo y limpiar a mitad sería escribir y volver a ensuciar. Y se hace por
# el MISMO camino que el reparto de commits (`commit-layers.py --discard-churn`
# delega en `check-unity-assets.sh`, que a su vez usa el verificador de
# equivalencia), para que no haya dos ideas de qué es churn ni de qué se puede
# tirar: los idénticos vuelven a REV y los que CAMBIAN de verdad se quedan, sólo
# sin la forma que les puso la pasada.
#
# `--no-discard-churn` lo salta (el árbol se queda como lo dejó el pase): es lo
# que quieres cuando SÍ vas a commitear los assets.
#
# El rc del descarte NO se confunde con el veredicto del pase: 0 es «nada
# pendiente» y 1 es «además del churn hay cambios de VERDAD» —los dos son un
# descarte correcto, y el 1 sólo dice que hay algo que revisar. 3 (no puede
# actuar) y 4 (no quedó como afirmó) sí son un fallo de este paso: se dice y el
# pase sale ≠ 0, porque un árbol a medio limpiar es peor que uno sucio a la vista.
discard_churn() {
  local rc=0 out line
  if [[ $DISCARD_CHURN -ne 1 ]]; then
    log "  (descarte del churn saltado con --no-discard-churn: el árbol se queda como lo dejó el pase)"
    return 0
  fi
  if [[ ! -f "$ASSETS_SCRIPT" ]]; then
    echo "✗ falta el verificador de assets: $ASSETS_SCRIPT" >&2
    return 1
  fi
  # PYTHONUTF8=1 por el mismo motivo que en la forma canónica: los ✓/✗ de su
  # salida revientan en la consola cp1252 de Windows.
  out="$(PYTHONUTF8=1 bash "$ASSETS_SCRIPT" --discard 2>&1)" || rc=$?
  while IFS= read -r line; do
    [[ -n "$line" ]] && log "  ${line%$'\r'}"
  done <<<"$out"
  case $rc in
    0) log "  (el churn de re-serialización quedó descartado: el árbol no se queda sucio)" ;;
    1) log "  (descartado el churn; queda algún cambio de VERDAD de los assets, en forma canónica, para revisar — el pase sí se puede commitear)" ;;
    *) echo "✗ el descarte del churn no dejó el árbol en condiciones (rc=$rc) — revísalo antes de seguir" >&2
       return 1 ;;
  esac
  return 0
}

# ── La ORIENTACIÓN de los actores: el morro tiene que apuntar al rumbo ───────
# La caja del actor (`GameSpriteModel`) tiene el LARGO en Y y en la hormiga la
# cabeza va hacia +Y, así que el presentador tiene que llevar ese +Y al rumbo del
# Core (que mapea `X→X` e `Y→Z`). Si el giro está mal, el bicho sale andando DE
# LADO —patas y antenas perpendiculares al rumbo— y NINGÚN check de estado lo ve:
# los tres lóbulos están, el mundo aparece, el conteo de hormigas no cambia.
# Medido el 2026-10-10 en el proyecto real: con `-rumbo` en vez de `90 − rumbo` el
# morro salía a 90° justos del rumbo en los ocho rumbos de prueba.
#
# La medida la hace DENTRO del editor una sonda que llama a la rotación del propio
# presenter (`ProbeActorOrientation` → `SimPresenterBehaviour.ActorRotation`), con
# la matemática de Unity: aquí no se reimplementa la convención de `Euler`, que es
# exactamente cómo se cuela el giro.
#
# ── Y lo mismo para las OTRAS siluetas: ítem, hoja y mordiscos ───────────────
# El arte es un sólido PLANO (se extruye en Z) y la cámara mira de arriba, así que
# una pieza dibujada sin acostar enseña el CANTO: con `Quaternion.identity` el ítem
# y la hoja se veían como rectángulos —la hoja de 0,66 s × 1,10 s, con su largo de
# 1,24 u en el eje vertical y el 1,1 del grosor en el plano— sin punta, sin nervio
# y sin apuntar a nada. El eje de dibujo (su +Y) tiene que quedar en el plano y al
# norte: no hay rumbo que seguir —el `FoodItem` del Core no guarda ángulo y el
# stream no lo lleva—, así que inventarle un giro por pieza sería una decisión que
# el dato no tiene.
#
# Y las MUESCAS tienen que COMER DEL BORDE del limbo: el borde de la hoja es una
# ELIPSE (0,30 × 0,62 s), así que repartirlas con un radio ÚNICO de 0,55 s las
# dejaba FUERA por los costados (0,55 > 0,30) y DENTRO por las puntas (0,55 < 0,62)
# —flotando en el vacío junto a la hoja—; ahora `SimPresenterBehaviour.BiteOffset`
# las reparte sobre la elipse. El probe mide cada muesca contra el borde (r = 1,
# tolerancia 0,02) y comprueba que su comprobación tiene dientes: la configuración
# vieja —el canto y el círculo de 0,55 s— tiene que FALLAR.
check_actor_orientation() {
  local out kind detalle
  out="$(UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeActorOrientation.cs")" \
           2>/dev/null | cmd_result 2>/dev/null || true)"
  kind="${out%%|*}"
  detalle="${out#*|}"
  case "$kind" in
    ok)
      log "  ✓ ${detalle%% | *}"
      return 0 ;;
    error)
      echo "✗ ${detalle%% | *}" >&2
      echo "  (el morro de la malla es su +Y, el eje de avance de la caja del juego, y
  el giro del presenter tiene que llevarlo al rumbo: 90° menos el rumbo. El arte de
  ítems y hojas es un sólido PLANO: su eje de dibujo tiene que quedar acostado en el
  tablero —al norte, que es el único que el dato no obliga a inventar—, o desde
  arriba se ve el canto. Y las muescas de una hoja tienen que caer sobre el BORDE del
  limbo, que es una ELIPSE (0,30 × 0,62 s): con un radio único (0,55 s) quedan fuera
  por los costados y dentro por las puntas. Los desvíos y las extensiones del probe
  dicen cuánto se va.)" >&2
      return 1 ;;
    *)
      echo "✗ el editor no respondió a la orientación de los actores" >&2
      return 1 ;;
  esac
}

# ── Y LO MISMO, MEDIDO EN PÍXELES ────────────────────────────────────────────
# La matriz correcta NO prueba el render: entre la matriz y lo que ve el jugador
# quedan el encuadre de la cámara (¿mira al nadir? ¿su arriba es el norte de
# pantalla?), la proyección, el rasterizado y el aliasing. El defecto que este paso
# persigue (la hormiga andando de lado, con su morro a 90° del rumbo) vivía justo
# ahí: la malla y la rotación podían estar bien y el bicho verse mal.
#
# `ProbeAntHeadingPixels` renderiza la MALLA y el MATERIAL reales de la escena con un
# rumbo FIJO y mide en los píxeles el eje de la silueta (segundo momento) a la escala
# de la partida (~19 px de hormiga, donde manda el aliasing) y 6× más cerca, más el
# MORRO (el extremo LIGERO: el gáster pesa más que la cabeza) en el render de zoom,
# que es donde el reparto de área de los extremos tiene margen —a 19 px deja 1 px de
# diferencia en los diagonales, así que ahí se publica pero no decide—. Comprueba
# además que la comprobación tiene dientes: el mismo render con el giro que se coló
# (-rumbo) tiene que desviarse ~90°.
#
# Un `ok` sin la imagen no es una medida de píxeles: la evidencia se conserva fuera
# de `Temp/` (que Unity borra al cerrar) o el paso falla.
check_actor_heading_pixels() {
  local out kind detalle
  out="$(UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeAntHeadingPixels.cs")" \
           2>/dev/null | cmd_result 2>/dev/null || true)"
  kind="${out%%|*}"
  detalle="${out#*|}"
  case "$kind" in
    ok)
      log "  ✓ ${detalle%% | *}"
      preserve_heading "$PROJECT/Temp" "${HEADING_DIR:-$ROOT/artifacts/playpass-heading-pixels}" || return 1
      return 0 ;;
    error)
      echo "✗ ${detalle%% | *}" >&2
      echo "  (el eje y el morro de la silueta se miden sobre la MALLA REAL: si no caen en el
  rumbo del Core —que el mundo mapea X→X, Y→Z—, lo que se ve en pantalla no es lo que
  dice la simulación. Se mide a la escala de partida y a zoom 6×, y el probe dice el
  desvío de cada rumbo; el giro viejo (-rumbo) tiene que fallar ~90° o la comprobación
  no mira el render.)" >&2
      return 1 ;;
    *)
      echo "✗ el editor no respondió a la medida en píxeles del rumbo" >&2
      return 1 ;;
  esac
}

# preserve_heading <dir de la sonda> <destino> → copia el PNG de la evidencia en
# píxeles (`antsim-heading-*.png`) fuera de `Temp/` y devuelve ≠ 0 si no había
# ninguno. FALLA CERRADO por el mismo motivo que la conservación de los iconos: un
# «medido en píxeles» sin la imagen que se mira es una afirmación sin nada detrás.
preserve_heading() {
  local from="$1" to="$2" n=0 f
  mkdir -p "$to" || return 1
  for f in "$from"/antsim-heading-*.png; do
    [[ -f "$f" ]] || continue
    cp -f "$f" "$to/" || return 1
    n=$((n+1))
  done
  if [[ $n -eq 0 ]]; then
    echo "✗ FALLO (píxeles del rumbo): la sonda no dejó ningún PNG en $from — sin la imagen, la medida de píxeles no se puede mirar" >&2
    return 1
  fi
  log "  ✓ evidencia en píxeles conservada en $to ($n PNG: la hormiga por rumbo, arriba a escala de partida y abajo a zoom)"
  return 0
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --samples)   SAMPLES="$2"; ANALYZE_ONLY=1; shift 2 ;;
    --analyze-block4) B4_ANALYZE="$2"; shift 2 ;;
    --analyze-visual) VIS_ANALYZE=1; shift ;;
    --analyze-icons)  ICONS_ANALYZE=1; shift ;;
    --icons-dump)     HUD_ICONS="$2"; shift 2 ;;
    --icons-crops)    ICONS_CROPS="$2"; shift 2 ;;
    --analyze-sparkline) SPARK_ANALYZE=1; shift ;;
    --spark-dump)     SPARK_DUMP="$2"; shift 2 ;;
    --spark-crops)    SPARK_CROPS="$2"; shift 2 ;;
    --analyze-walls)  WALL_ANALYZE=1; shift ;;
    --wall-dump)      WALL_DUMP="$2"; shift 2 ;;
    --wall-crops)     WALL_CROPS="$2"; shift 2 ;;
    --no-canonicalize) CANONICALIZE=0; shift ;;
    --recanonicalize)  RECANONICALIZE=1; shift ;;
    --no-discard-churn) DISCARD_CHURN=0; shift ;;
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
    # El encabezado entero, hasta la primera línea que NO es comentario (`set -euo
    # pipefail`). Antes era `2,97p` y añadir un párrafo al encabezado dejaba el
    # bloque «Uso:»/«Opciones:» fuera del --help, en silencio.
    --help|-h)   sed -n '2,/^[^#]/p' "$0" | sed '$d'; exit 0 ;;
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

# ── Comparador de ICONOS del HUD (F5.1) ─────────────────────────────────────
# El veredicto es de `scripts/playpass-icons.py` (correlación normalizada + IoU
# de silueta + margen sobre el segundo glifo, por icono); lo que aporta ESTE
# script es la POLÍTICA: ese veredicto es un PASO del play pass, no un informe.
# Un icono que no se clasifica como su propio glifo —o que directamente no se
# pudo medir— deja el pass en ≠ 0, con el glifo y sus números delante. Es el
# fallo que antes se escapaba: el comparador existía y daba 0 fallos a mano,
# pero nadie lo miraba.
#
# POR QUÉ FALLA TAMBIÉN SI NO SE PUDO MEDIR: un paso que no corre no puede
# contar como verde (el modo de fallo es «la sonda no guardó el PNG» o «este
# intérprete no tiene Pillow»), y ese falso verde es justo el que este script
# persigue. El mensaje distingue «el icono no se clasifica» de «no se pudo
# medir»; los dos bloquean.

# El comparador rasteriza el `.ttf` de referencia, así que necesita Pillow — y
# no lo tiene cualquier python del equipo (medido aquí: `python` sí, `python3`
# no). Se prueba en orden y se publica cuál se usó, en vez de asumir `python3`
# (que es lo que usan los demás pasos, y que solo necesitan la stdlib).
python_with_pillow() {
  local p
  for p in python python3 py; do
    command -v "$p" >/dev/null 2>&1 || continue
    if "$p" -c "import PIL" >/dev/null 2>&1; then printf '%s' "$p"; return 0; fi
  done
  return 1
}

# preserve_icons <dir de la sonda> <destino> → copia los PNG del HUD (`antsim-hud-*`:
# el frame vivo y el recorte de cada tarjeta) fuera de `Temp/` y devuelve ≠ 0 si no
# había ninguno. FALLA CERRADO por el mismo motivo que el paso de iconos: si la
# sonda no dejó recortes, no hay evidencia que conservar —y decir «conservada» sin
# nada que conservar sería el falso verde que este pass persigue. El destino por
# defecto es artifacts/playpass-hud-icons/ (ver $ICONS_DIR).
preserve_icons() {
  local from="$1" to="$2" n=0 f
  mkdir -p "$to" || return 1
  for f in "$from"/antsim-hud-*.png; do
    [[ -f "$f" ]] || continue
    cp -f "$f" "$to/" || return 1
    n=$((n+1))
  done
  if [[ $n -eq 0 ]]; then
    echo "✗ FALLO (iconos): la sonda no dejó ningún PNG en $from — sin recortes no hay evidencia que conservar" >&2
    return 1
  fi
  log "  ✓ evidencia del HUD conservada en $to ($n PNG: frame vivo + recortes de tarjeta)"
  return 0
}

# analyze_icons <volcado de la sonda> <dir de los recortes> → 0 solo si TODOS
# los iconos del HUD se clasifican como el suyo y la procedencia del recorte
# cuadra. Imprime el informe del comparador y, si falla, un veredicto propio.
analyze_icons() {
  local dump="$1" crops="$2" py="${ICONS_PY:-}" rc=0 out

  if [[ -z "$py" ]]; then py="$(python_with_pillow || true)"; fi
  if [[ ! -f "$ICONS_SCRIPT" ]]; then
    echo "  ✗ FALLO (iconos): falta el comparador $ICONS_SCRIPT" >&2
    return 1
  fi
  if [[ -z "$py" ]]; then
    echo "  ✗ FALLO (iconos): ningún python del PATH tiene Pillow — sin él no se puede rasterizar la referencia del .ttf" >&2
    echo "    (comprobación: python -c 'import PIL'; o fija el intérprete con \$ICONS_PY)" >&2
    return 1
  fi
  if [[ ! -f "$dump" ]]; then
    echo "  ✗ FALLO (iconos): no hay volcado de la sonda ($dump)" >&2
    echo "    (sin el volcado no se clasifica ningún icono: ¿corrió ProbeHudIconShot con Play vivo?)" >&2
    return 1
  fi

  out="$("$py" "$ICONS_SCRIPT" "$dump" "$crops" 2>&1)" || rc=$?
  # El informe se publica entero: sin los números por glifo, «falló» no dice qué
  # mirar. El CR se cae aquí (la salida del CLI llega con CRLF en Windows).
  local line
  while IFS= read -r line; do log "  ${line%$'\r'}"; done <<<"$out"

  if [[ $rc -eq 0 ]]; then
    log "  ✓ los tres iconos del HUD se clasifican como SU glifo (comparador en verde)"
    return 0
  fi
  if [[ $rc -eq 2 ]]; then
    echo "  ✗ FALLO (iconos): el comparador NO pudo medir (rc=2: falta Pillow o uso incorrecto) — el pass no puede dar esto por verde" >&2
    return 1
  fi
  local detalle
  detalle="$(printf '%s' "$out" | tr -d '\r' | grep -E '✗|fallo' | tail -4 || true)"
  echo "  ✗ FALLO (iconos): algún icono del HUD no se clasifica como SU glifo contra la referencia del .ttf" >&2
  echo "    (correlación por debajo del mínimo, sin margen sobre el segundo glifo o sin tinta; comparador rc=$rc)" >&2
  if [[ -n "$detalle" ]]; then printf '%s\n' "$detalle" | sed 's/^/    /' >&2; fi
  return 1
}

# ── Gráfica de DESCARGAS de la tarjeta (F5.1) ───────────────────────────────
# Mismo reparto que los iconos: la sonda captura y guarda, el medidor
# (`scripts/playpass-sparkline.py`, con Pillow) clasifica los píxeles, y ESTE
# script aplica la política — un fallo del medidor, o no poder medir, deja el
# pass en ≠ 0. Es el tramo que el modelo puro no puede ver: la textura puede
# estar perfecta y la tarjeta no pintarla (banda fuera de la tarjeta, color
# tapado, gráfica del revés).
preserve_sparkline() {
  local from="$1" to="$2" n=0 f
  mkdir -p "$to" || return 1
  for f in "$from"/antsim-spark-*.png; do
    [[ -f "$f" ]] || continue
    cp -f "$f" "$to/" || return 1
    n=$((n+1))
  done
  if [[ $n -eq 0 ]]; then
    echo "✗ FALLO (barras de la gráfica): la sonda no dejó ningún PNG en $from — sin la captura no hay píxeles que medir" >&2
    return 1
  fi
  log "  ✓ evidencia de la gráfica conservada en $to ($n PNG: frame vivo + banda de cada tarjeta)"
  return 0
}

# analyze_sparkline <volcado> <dir de los recortes> → 0 solo si TODAS las
# tarjetas pasan las reglas del medidor. El informe se publica entero.
analyze_sparkline() {
  local dump="$1" crops="$2" py="${SPARK_PY:-}" rc=0 out line

  if [[ -z "$py" ]]; then py="$(python_with_pillow || true)"; fi
  if [[ ! -f "$SPARK_SCRIPT" ]]; then
    echo "  ✗ FALLO (gráfica): falta el medidor $SPARK_SCRIPT" >&2
    return 1
  fi
  if [[ -z "$py" ]]; then
    echo "  ✗ FALLO (gráfica): ningún python del PATH tiene Pillow — sin él no se leen los PNG de la captura" >&2
    echo "    (comprobación: python -c 'import PIL'; o fija el intérprete con \$SPARK_PY)" >&2
    return 1
  fi
  if [[ ! -f "$dump" ]]; then
    echo "  ✗ FALLO (gráfica): no hay volcado de la sonda ($dump)" >&2
    echo "    (sin el volcado no se mide ninguna barra: ¿corrió ProbeSparklinePixels con Play vivo?)" >&2
    return 1
  fi

  out="$("$py" "$SPARK_SCRIPT" "$dump" "$crops" 2>&1)" || rc=$?
  while IFS= read -r line; do log "  ${line%$'\r'}"; done <<<"$out"

  if [[ $rc -eq 0 ]]; then
    log "  ✓ las barras de descargas se ven en la tarjeta: una por ventana, en sus columnas, nacidas en la base"
    return 0
  fi
  if [[ $rc -eq 2 ]]; then
    echo "  ✗ FALLO (gráfica): el medidor NO pudo medir (rc=2: falta Pillow o uso incorrecto) — el pass no puede dar esto por verde" >&2
    return 1
  fi
  local detalle
  detalle="$(printf '%s' "$out" | tr -d '\r' | grep -E '✗|fallo' | tail -5 || true)"
  echo "  ✗ FALLO (gráfica): las barras de descargas no se miden como deben en la tarjeta" >&2
  echo "    (número de barras, columnas de su muestra, altura por cuenta, base o fuga fuera de la banda; medidor rc=$rc)" >&2
  if [[ -n "$detalle" ]]; then printf '%s\n' "$detalle" | sed 's/^/    /' >&2; fi
  return 1
}

# ── Paredes del LABERINTO (F6) ──────────────────────────────────────────────
# El tramo que ningún check de estado puede ver: la escena puede tener el
# `WallMesh` y el `WallMaterial` cableados, el mapa diciendo dónde están las
# paredes y la partida corriendo un laberinto invisible (mal alto, escala cero,
# capa que la cámara no ve). `ProbeWallPixels` renderiza la cámara del juego con
# la partida viva y compara el frame con el mapa CELDA A CELDA; el medidor
# (`scripts/playpass-walls.py`, con Pillow) vuelve a los píxeles, exige color,
# sitio y escala de las muestras, y aquí se aplica la política: un fallo del
# medidor, o no poder medir, deja el pass en ≠ 0.
preserve_walls() {
  local from="$1" to="$2" n=0 f
  mkdir -p "$to" || return 1
  for f in "$from"/antsim-wall-*.png; do
    [[ -f "$f" ]] || continue
    cp -f "$f" "$to/" || return 1
    n=$((n+1))
  done
  if [[ $n -eq 0 ]]; then
    echo "✗ FALLO (paredes): la sonda no dejó ningún PNG en $from — sin el frame no hay píxeles que medir" >&2
    return 1
  fi
  log "  ✓ evidencia de las paredes conservada en $to ($n PNG: frame vivo + hoja con las cajas y los controles)"
  return 0
}

# analyze_walls <volcado> <dir de la evidencia> → 0 solo si el medidor da verde.
# El informe se publica entero: sin los números por muestra, «falló» no dice qué
# mirar (una celda sin pintar, una caja vacía, un control con muro…).
analyze_walls() {
  local dump="$1" crops="$2" py="${WALL_PY:-}" rc=0 out line

  if [[ -z "$py" ]]; then py="$(python_with_pillow || true)"; fi
  if [[ ! -f "$WALL_SCRIPT" ]]; then
    echo "  ✗ FALLO (paredes): falta el medidor $WALL_SCRIPT" >&2
    return 1
  fi
  if [[ -z "$py" ]]; then
    echo "  ✗ FALLO (paredes): ningún python del PATH tiene Pillow — sin él no se leen los PNG del frame" >&2
    echo "    (comprobación: python -c 'import PIL'; o fija el intérprete con \$WALL_PY)" >&2
    return 1
  fi
  if [[ ! -f "$dump" ]]; then
    echo "  ✗ FALLO (paredes): no hay volcado de la sonda ($dump)" >&2
    echo "    (sin el volcado no se sabe qué celdas deberían tener muro: ¿corrió ProbeWallPixels con Play vivo?)" >&2
    return 1
  fi

  out="$("$py" "$WALL_SCRIPT" "$dump" "$crops" 2>&1)" || rc=$?
  while IFS= read -r line; do log "  ${line%$'\r'}"; done <<<"$out"

  if [[ $rc -eq 0 ]]; then
    log "  ✓ el mapa del laberinto está en pantalla: color, sitio y escala (medidor en verde)"
    return 0
  fi
  if [[ $rc -eq 2 ]]; then
    echo "  ✗ FALLO (paredes): el medidor NO pudo medir (rc=2: falta Pillow o uso incorrecto) — el pass no puede dar esto por verde" >&2
    return 1
  fi
  local detalle
  detalle="$(printf '%s' "$out" | tr -d '\r' | grep -E '✗|fallo' | tail -6 || true)"
  echo "  ✗ FALLO (paredes): el terreno que colisiona no se mide como está en pantalla" >&2
  echo "    (acuerdo celda a celda, color del material, caja llena, escala o controles; medidor rc=$rc)" >&2
  if [[ -n "$detalle" ]]; then printf '%s\n' "$detalle" | sed 's/^/    /' >&2; fi
  return 1
}

# ── 8bis. La HUELLA del árbol: el pase no acumula estado ─────────────────────
# POR QUÉ. Todo lo que este pase afirma —«los assets quedaron canónicos», «el árbol
# no se queda sucio»— descansa en un supuesto que hasta ahora nadie comprobaba: que
# una SEGUNDA pasada del mismo montaje deja el árbol exactamente como lo dejó la
# primera. Si el pase acumulara estado (un asset que se reescribe cada vez, un
# fichero que aparece, un volcado que crece), el diff contra HEAD de cada corrida
# sería otro y ninguna medida sería comparable con la de la pasada anterior.
#
# CÓMO. Se toma una HUELLA —estado y blob (`git hash-object`: el contenido que un
# commit guardaría, con sus filtros) de cada fichero del ALCANCE que difiere de
# HEAD— y se guarda en `artifacts/playpass-fingerprint.txt` junto al MONTAJE (HEAD +
# semilla + grid + ticks + velocidad + pool). La pasada SIGUIENTE calcula la suya y
# la compara: mismo montaje ⇒ mismas líneas, y si no lo son el pase dice QUÉ fichero
# difiere (la línea «-» es lo que había y la «+» lo que hay).
#
# LA EXCEPCIÓN, declarada y comprobada: los volcados de MEDIDA (muestras vivas,
# aspecto, bloque 4, recortes del HUD) cambian con el reloj por construcción, así que
# su CONTENIDO no se compara; de ellos se exige que estén, que no estén vacíos y que
# su nombre y su estado salgan en las dos pasadas. Un fichero que aparece o
# desaparece, o que cambia fuera de esa lista, es un fallo de esta comprobación.
#
# Va DESPUÉS del descarte del churn porque es el estado FINAL del árbol el que tiene
# que coincidir con el que dejó la pasada anterior.
FP_COMPARED=0

fp_es_medida() { # ¿el contenido de este fichero cambia con el reloj?
  local p="$1" pre
  for pre in "${FP_LIVE[@]}"; do case "$p" in "$pre"*) return 0 ;; esac; done
  return 1
}

fp_es_alcance() { # ¿este camino es de los que el PASE escribe?
  local p="$1" s
  case "$p" in artifacts/playpass-*) return 0 ;; esac
  for s in "${FP_ASSET_SUFFIXES[@]}"; do case "$p" in *"$s") return 0 ;; esac; done
  return 1
}

fp_listado() { # "<estado> <blob|medida> <camino>" por fichero del alcance que difiere de HEAD
  git -C "$ROOT" status --porcelain -uall -- "${FP_SCOPE[@]}" 2>/dev/null | while IFS= read -r linea; do
    [[ -n "$linea" ]] || continue
    local xy p sha
    xy="${linea:0:2}"
    p="${linea:3}"
    [[ "$p" == "$FP_FILE" ]] && continue
    fp_es_alcance "$p" || continue
    if fp_es_medida "$p"; then
      sha="medida"
    elif [[ -f "$ROOT/$p" ]]; then
      sha="$(git -C "$ROOT" hash-object -- "$p" 2>/dev/null || echo '?')"
    else
      sha="-"
    fi
    printf '%s %s %s\n' "$xy" "$sha" "$p"
  done | LC_ALL=C sort
}

fp_montaje() { # lo que hace comparables (o no) dos pasadas
  printf 'HEAD %s seed=%s grid=%s ticks=%s speed=%s pool=%s' \
    "$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo '?')" \
    "$SEED" "$GRID" "$TICKS" "$SPEED" "$SEED_POOL"
}

fp_blob() { git -C "$ROOT" hash-object --stdin 2>/dev/null || true; }

fp_cuenta() { # cuántos ficheros y cuántos de ellos son de medida
  FP_N="$(printf '%s\n' "$1" | grep -c . || true)"
  FP_MED="$(printf '%s\n' "$1" | grep -c ' medida ' || true)"
}

check_tree_fingerprint() {
  local abs="$ROOT/$FP_FILE" montaje nuevo previo="" prev_montaje huella prev_listado
  montaje="$(fp_montaje)"
  if [[ -f "$abs" ]]; then previo="$(cat "$abs")"; fi
  nuevo="$(fp_listado)"
  huella="$(printf '%s\n' "$nuevo" | fp_blob)"
  fp_cuenta "$nuevo"

  # La huella de ESTA pasada se escribe siempre: describe el estado que deja, y es la
  # que comparará la siguiente. Sin fechas dentro: dos pasadas idénticas tienen que
  # dejar el fichero byte a byte igual (si no, la huella no podría converger).
  {
    printf '# Huella del árbol del Play pass — la compara la pasada SIGUIENTE si el montaje coincide.\n'
    printf '# montaje: %s\n' "$montaje"
    printf '# alcance: %s (assets: %s)\n' "${FP_SCOPE[*]}" "${FP_ASSET_SUFFIXES[*]}"
    printf '# medida (sólo nombre y estado se comparan): %s\n' "${FP_LIVE[*]}"
    printf '# huella: %s\n' "$huella"
    printf '%s\n' "$nuevo"
  } > "$abs" || return 1

  if [[ -z "$previo" ]]; then
    log "  ✓ huella guardada en $FP_FILE ($FP_N ficheros del alcance, $FP_MED de medida): primera pasada de este montaje, no hay con qué comparar — la compara la siguiente"
    return 0
  fi

  prev_montaje="$(printf '%s\n' "$previo" | sed -n 's/^# montaje: //p' | head -1)"
  if [[ "$prev_montaje" != "$montaje" ]]; then
    log "  (huella NO comparable: el montaje cambió — es otra corrida)"
    log "    antes: $prev_montaje"
    log "    ahora: $montaje"
    return 0
  fi

  prev_listado="$(printf '%s\n' "$previo" | grep -v '^#' || true)"
  if [[ "$prev_listado" == "$nuevo" ]]; then
    FP_COMPARED=1
    log "  ✓ huella IDÉNTICA a la pasada anterior (${huella:0:12}…): los $FP_N ficheros del alcance quedan igual — el pase no acumula estado (de ellos, $FP_MED volcados de medida, que sólo se comparan por nombre)"
    return 0
  fi

  echo "✗ el árbol NO quedó igual que en la pasada anterior (mismo montaje): cambió algo del alcance" >&2
  # `|| true`: `diff` sale con 1 cuando los ficheros difieren —que es justo el caso—
  # y con `set -o pipefail` ese 1 sería el del pipeline.
  diff <(printf '%s\n' "$prev_listado") <(printf '%s\n' "$nuevo") 2>/dev/null | sed 's/^/      /' | head -20 >&2 || true
  echo "    (línea «-» = lo que dejó la pasada anterior; «+» = lo que deja ésta. Un fichero que cambia fuera de los volcados de medida, o uno que aparece o desaparece, rompe la idempotencia del pase.)" >&2
  return 1
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

  # — Iconos del HUD: se verifica el CABLEADO, no la métrica —
  # La métrica trae su propio `--selftest` en scripts/playpass-icons.py y aquí no
  # hay tarjeta ni `.ttf` que rasterizar. Lo que aporta ESTE script —y por tanto
  # lo que se prueba— es la política: un veredicto ≠ 0 del comparador tiene que
  # romper el paso, un 0 no, y no poder medir (sin volcado) NO puede contar como
  # verde. Para eso se sustituyen el comparador (por un doble) y el intérprete
  # (por bash): el doble imprime el informe y sale con el código que se le pide.
  icons_dump="$tmp/hud-icons.txt"
  printf 'em=13.758|scale=0.983|t=Body|t=Card|file=card0.png|rect=35,1093,435,1226\n' > "$icons_dump"
  icons_ok="$tmp/iconos-ok"; icons_fail="$tmp/iconos-fail"
  printf '#!/usr/bin/env bash\necho "  ✓ Ant: correlación 0.41 · IoU 0.78 · margen +0.34"\nexit 0\n' > "$icons_ok"
  printf '#!/usr/bin/env bash\necho "  ✗ Egg: correlación 0.11 · margen -0.05 · tinta 0.30"\necho "3 glifo(s) · 0 fallo(s)"\nexit 1\n' > "$icons_fail"

  f6=0
  ICONS_PY=bash
  ICONS_SCRIPT="$icons_ok"
  r=0; QUIET=1 analyze_icons "$icons_dump" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -ne 0 ]]; then echo "✗ selftest(iconos): el comparador en verde rompió el paso" >&2; f6=1; fi
  ICONS_SCRIPT="$icons_fail"
  r=0; QUIET=1 analyze_icons "$icons_dump" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then echo "✗ selftest(iconos): un icono que NO se clasifica pasó en verde" >&2; f6=1; fi
  # Sin volcado no hay nada que clasificar: «no pude medir» nunca es verde.
  ICONS_SCRIPT="$icons_ok"
  r=0; QUIET=1 analyze_icons "$tmp/no-existe-icons.txt" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then echo "✗ selftest(iconos): la falta de volcado pasó en verde" >&2; f6=1; fi

  # La CONSERVACIÓN de la evidencia: los PNG del HUD salen de `Temp/` y quedan en
  # el destino, y sin ningún PNG el paso falla (no puede decir «conservada» sin
  # nada que conservar). Se prueba aquí porque es lo único de este paso que no
  # depende de píxeles: la métrica y la captura sí necesitan editor.
  sonda="$tmp/sonda"; mkdir -p "$sonda"
  printf x > "$sonda/antsim-hud-frame.png"
  printf x > "$sonda/antsim-hud-card0.png"
  printf x > "$sonda/otro.png"
  r=0; QUIET=1 preserve_icons "$sonda" "$tmp/evidencia" >/dev/null 2>&1 || r=$?
  if [[ $r -ne 0 || ! -f "$tmp/evidencia/antsim-hud-frame.png" || ! -f "$tmp/evidencia/antsim-hud-card0.png" ]]; then
    echo "✗ selftest(iconos): no se conservaron los PNG del HUD" >&2; f6=1
  fi
  if [[ -f "$tmp/evidencia/otro.png" ]]; then
    echo "✗ selftest(iconos): se copió un PNG que no es del HUD" >&2; f6=1
  fi
  r=0; QUIET=1 preserve_icons "$tmp/sin-pngs" "$tmp/evidencia2" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then
    echo "✗ selftest(iconos): sin PNGs la conservación pasó en verde" >&2; f6=1
  fi
  if [[ $f6 -eq 0 ]]; then
    echo "✓ selftest de los iconos ok (verde pasa · glifo roto falla · sin volcado falla · PNG conservados)"
  fi

  # — Barras de descargas de la gráfica: el CABLEADO, no la métrica —
  # La métrica trae su propio `--selftest` en scripts/playpass-sparkline.py (con
  # imágenes sintéticas); aquí se prueba la POLÍTICA: un veredicto ≠ 0 rompe el
  # paso, un 0 no, no poder medir no es verde, y la evidencia se conserva (o el
  # paso falla). Se sustituyen medidor e intérprete por dobles, como en iconos.
  f8=0
  spark_dump="$tmp/spark-dump.txt"
  printf 'SPARKPREP|cards=1|pattern=0,3|injected=1\nSPARK|stage=measure|row0=top|cards=1\n' > "$spark_dump"
  spark_ok="$tmp/spark-ok"; spark_fail="$tmp/spark-fail"
  printf '#!/usr/bin/env bash\necho "  ✓ Sparkline_0: 4 barra(s) ámbar, cada una en las columnas de su muestra"\nexit 0\n' > "$spark_ok"
  printf '#!/usr/bin/env bash\necho "  ✗ Sparkline_0: 0 barra(s) ámbar, esperadas 4"\nexit 1\n' > "$spark_fail"

  SPARK_PY=bash
  SPARK_SCRIPT="$spark_ok"
  r=0; QUIET=1 analyze_sparkline "$spark_dump" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -ne 0 ]]; then echo "✗ selftest(gráfica): el medidor en verde rompió el paso" >&2; f8=1; fi
  SPARK_SCRIPT="$spark_fail"
  r=0; QUIET=1 analyze_sparkline "$spark_dump" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then echo "✗ selftest(gráfica): unas barras que no se miden pasaron en verde" >&2; f8=1; fi
  SPARK_SCRIPT="$spark_ok"
  r=0; QUIET=1 analyze_sparkline "$tmp/no-existe-spark.txt" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then echo "✗ selftest(gráfica): la falta de volcado pasó en verde" >&2; f8=1; fi

  # La evidencia de la captura: sale de `Temp/` y queda en el destino; sin PNGs
  # el paso falla (no puede decir «medida» sin nada que mirar).
  spark_sonda="$tmp/spark-sonda"; mkdir -p "$spark_sonda"
  printf x > "$spark_sonda/antsim-spark-frame.png"
  printf x > "$spark_sonda/antsim-spark-card0.png"
  printf x > "$spark_sonda/otro.png"
  r=0; QUIET=1 preserve_sparkline "$spark_sonda" "$tmp/spark-evidencia" >/dev/null 2>&1 || r=$?
  if [[ $r -ne 0 || ! -f "$tmp/spark-evidencia/antsim-spark-frame.png" || ! -f "$tmp/spark-evidencia/antsim-spark-card0.png" ]]; then
    echo "✗ selftest(gráfica): no se conservaron los PNG de la captura" >&2; f8=1
  fi
  if [[ -f "$tmp/spark-evidencia/otro.png" ]]; then
    echo "✗ selftest(gráfica): se copió un PNG que no es de la gráfica" >&2; f8=1
  fi
  r=0; QUIET=1 preserve_sparkline "$tmp/spark-sin-pngs" "$tmp/spark-evidencia2" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then
    echo "✗ selftest(gráfica): sin PNGs la conservación pasó en verde" >&2; f8=1
  fi
  if [[ $f8 -eq 0 ]]; then
    echo "✓ selftest de la gráfica ok (verde pasa · barras rotas fallan · sin volcado falla · PNG conservados)"
  fi

  # — Paredes del terreno: el CABLEADO, no la métrica —
  # La métrica trae su propio `--selftest` en scripts/playpass-walls.py (con frames
  # sintéticos: sin paredes, a media escala, de otro color, un polígono gigante,
  # `draws` corto y guías encima). Aquí se prueba la POLÍTICA de este script: un
  # veredicto ≠ 0 rompe el paso, un 0 no, no poder medir no es verde, y la
  # evidencia del frame se conserva (o el paso falla). Dobles, como en la gráfica.
  f9=0
  wall_dump="$tmp/wall-dump.txt"
  printf 'WALL|stage=measure|frame=1280x960|row0=top|cells=1|agree=1\n' > "$wall_dump"
  wall_ok="$tmp/wall-ok"; wall_fail="$tmp/wall-fail"
  printf '#!/usr/bin/env bash\necho "  ✓ el mapa está en pantalla: 8836/8836 celdas de acuerdo (100.0 %%)"\nexit 0\n' > "$wall_ok"
  printf '#!/usr/bin/env bash\necho "  ✗ el mapa NO está en pantalla: 431/484 celdas de acuerdo (89.0 %%)"\nexit 1\n' > "$wall_fail"

  WALL_PY=bash
  WALL_SCRIPT="$wall_ok"
  r=0; QUIET=1 analyze_walls "$wall_dump" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -ne 0 ]]; then echo "✗ selftest(paredes): el medidor en verde rompió el paso" >&2; f9=1; fi
  WALL_SCRIPT="$wall_fail"
  r=0; QUIET=1 analyze_walls "$wall_dump" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then echo "✗ selftest(paredes): un laberinto que no está en pantalla pasó en verde" >&2; f9=1; fi
  WALL_SCRIPT="$wall_ok"
  r=0; QUIET=1 analyze_walls "$tmp/no-existe-wall.txt" "$tmp" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then echo "✗ selftest(paredes): la falta de volcado pasó en verde" >&2; f9=1; fi

  # La evidencia del frame: sale de `Temp/` y queda en el destino; sin PNGs el paso
  # falla (no puede decir «medido» sin nada que mirar).
  wall_sonda="$tmp/wall-sonda"; mkdir -p "$wall_sonda"
  printf x > "$wall_sonda/antsim-wall-frame.png"
  printf x > "$wall_sonda/antsim-wall-marked.png"
  printf x > "$wall_sonda/otro.png"
  r=0; QUIET=1 preserve_walls "$wall_sonda" "$tmp/wall-evidencia" >/dev/null 2>&1 || r=$?
  if [[ $r -ne 0 || ! -f "$tmp/wall-evidencia/antsim-wall-frame.png" || ! -f "$tmp/wall-evidencia/antsim-wall-marked.png" ]]; then
    echo "✗ selftest(paredes): no se conservó el frame de la medida" >&2; f9=1
  fi
  if [[ -f "$tmp/wall-evidencia/otro.png" ]]; then
    echo "✗ selftest(paredes): se copió un PNG que no es de las paredes" >&2; f9=1
  fi
  r=0; QUIET=1 preserve_walls "$tmp/wall-sin-pngs" "$tmp/wall-evidencia2" >/dev/null 2>&1 || r=$?
  if [[ $r -eq 0 ]]; then
    echo "✗ selftest(paredes): sin PNGs la conservación pasó en verde" >&2; f9=1
  fi
  if [[ $f9 -eq 0 ]]; then
    echo "✓ selftest de las paredes ok (verde pasa · laberinto invisible falla · sin volcado falla · frame conservado)"
  fi

  # — Huella del árbol: el invariante «dos pasadas dejan el mismo diff» —
  # Las funciones son `git status` + `git hash-object` sobre el alcance, así que se
  # prueban con GIT de verdad, en un repositorio de mentira: el caso «cambió un asset»
  # y el caso «cambió un volcado de medida» se distinguen por lo que las funciones
  # HACEN, no por lo que dicen que hacen.
  f7=0
  fp_repo="$tmp/fp-repo"
  mkdir -p "$fp_repo"
  (
    cd "$fp_repo" || exit 1
    git init -q . && git config user.email t@t && git config user.name t
    mkdir -p artifacts src/App/AntSim.Unity/Assets/Scripts src/App/AntSim.Unity/ProjectSettings
    printf 'escena\n' > src/App/AntSim.Unity/Assets/Escena.unity
    printf 'build\n'  > src/App/AntSim.Unity/ProjectSettings/EditorBuildSettings.asset
    printf 'codigo\n' > src/App/AntSim.Unity/Assets/Scripts/Algo.cs
    git add -A && git commit -qm base
  ) >/dev/null 2>&1
  fp_root_save="$ROOT"; fp_seed_save="$SEED"
  ROOT="$fp_repo"; SEED=42

  # (1) Primera pasada: guarda la huella y NO compara (no hay con qué).
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -ne 0 ]] || ! grep -q 'primera pasada de este montaje' <<<"$out"; then
    echo "✗ selftest(huella): la primera pasada no dijo que no había con qué comparar (rc=$rc)" >&2; f7=1
  fi

  # (2) Segunda pasada idéntica: huella idéntica… y el fichero de la huella converge
  # (dos pasadas iguales tienen que dejarlo byte a byte igual).
  cp -f "$fp_repo/$FP_FILE" "$tmp/fp-1.txt"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -ne 0 ]] || ! grep -q 'huella IDÉNTICA' <<<"$out"; then
    echo "✗ selftest(huella): dos pasadas iguales no dieron huella idéntica (rc=$rc)" >&2; f7=1
  fi
  if ! cmp -s "$tmp/fp-1.txt" "$fp_repo/$FP_FILE"; then
    echo "✗ selftest(huella): el fichero de la huella no es byte a byte el mismo entre dos pasadas iguales" >&2; f7=1
  fi

  # (3) Un ASSET que cambia entre pasadas: falla y dice CUÁL.
  printf 'escena cambiada\n' > "$fp_repo/src/App/AntSim.Unity/Assets/Escena.unity"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -eq 0 ]] || ! grep -q 'Escena.unity' <<<"$out"; then
    echo "✗ selftest(huella): un asset cambiado no rompió la huella (rc=$rc)" >&2; f7=1
  fi

  # (4) El CONTENIDO de un volcado de MEDIDA no se compara: cambia con el reloj.
  printf 'muestra 1\n' > "$fp_repo/artifacts/playpass-samples.txt"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  printf 'muestra 2, distinta\n' > "$fp_repo/artifacts/playpass-samples.txt"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -ne 0 ]]; then
    echo "✗ selftest(huella): un volcado de medida que cambia rompió la huella (rc=$rc)" >&2; f7=1
  fi

  # (5) Un fichero del alcance que APARECE, y otro que DESAPARECE: las dos cosas fallan.
  printf 'nuevo\n' > "$fp_repo/src/App/AntSim.Unity/Assets/Otro.mat"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -eq 0 ]] || ! grep -q 'Otro.mat' <<<"$out"; then
    echo "✗ selftest(huella): un fichero que aparece no rompió la huella (rc=$rc)" >&2; f7=1
  fi
  rm -f "$fp_repo/src/App/AntSim.Unity/Assets/Otro.mat"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -eq 0 ]]; then
    echo "✗ selftest(huella): un fichero que desaparece no rompió la huella (rc=$rc)" >&2; f7=1
  fi

  # (6) El CÓDIGO no es cosa del pase: cambiarlo no rompe la huella.
  printf 'codigo distinto\n' > "$fp_repo/src/App/AntSim.Unity/Assets/Scripts/Algo.cs"
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -ne 0 ]]; then
    echo "✗ selftest(huella): cambiar un .cs rompió la huella (no es del alcance) (rc=$rc)" >&2; f7=1
  fi

  # (7) Otro MONTAJE (otra semilla): no se compara — y no falla, porque es otra corrida.
  SEED=43
  rc=0; out="$(check_tree_fingerprint 2>&1)" || rc=$?
  if [[ $rc -ne 0 ]] || ! grep -q 'NO comparable' <<<"$out"; then
    echo "✗ selftest(huella): con otro montaje no dijo que no era comparable (rc=$rc)" >&2; f7=1
  fi
  ROOT="$fp_root_save"; SEED="$fp_seed_save"
  if [[ $f7 -eq 0 ]]; then
    echo "✓ selftest de la huella del árbol ok (idempotente · asset cambiado falla · medida no · aparece/desaparece falla · el .cs no es del alcance · montaje distinto no compara)"
  fi

  rm -rf "$tmp"
  [[ $f1 -eq 0 && $f4 -eq 0 && $f5 -eq 0 && $f6 -eq 0 && $f7 -eq 0 && $f8 -eq 0 && $f9 -eq 0 ]] || exit 1
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

# ── Modo solo-análisis (iconos del HUD) ─────────────────────────────────────
# La MISMA clasificación que decide el paso de la ruta viva, pero sobre la
# captura ya en disco: así un icono roto se reproduce sin abrir Unity, y el paso
# del pass se puede verificar en los dos sentidos (verde y roto).
if [[ $ICONS_ANALYZE -eq 1 ]]; then
  HUD_ICONS="${HUD_ICONS:-$ROOT/artifacts/playpass-hud-icons.txt}"
  # Por defecto se analizan los recortes CONSERVADOS (artifacts/…): es justo lo
  # que permite repetir el análisis sin editor, que `Temp/` ya no existe.
  ICONS_CROPS="${ICONS_CROPS:-$ICONS_DIR}"
  log "▶ Clasificando los iconos del HUD: $HUD_ICONS · recortes en $ICONS_CROPS"
  if analyze_icons "$HUD_ICONS" "$ICONS_CROPS"; then exit 0; fi
  exit 1
fi

# ── Modo solo-análisis (barras de descargas de la gráfica) ──────────────────
# La MISMA medida que decide el paso de la ruta viva, sobre la captura ya en
# disco: así una barra rota se reproduce sin abrir Unity y el paso se puede
# verificar en los dos sentidos.
if [[ $SPARK_ANALYZE -eq 1 ]]; then
  SPARK_DUMP="${SPARK_DUMP:-$ROOT/artifacts/playpass-sparkline-pixels.txt}"
  SPARK_CROPS="${SPARK_CROPS:-$SPARK_DIR}"
  log "▶ Midiendo las barras de la gráfica: $SPARK_DUMP · captura en $SPARK_CROPS"
  if analyze_sparkline "$SPARK_DUMP" "$SPARK_CROPS"; then exit 0; fi
  exit 1
fi

# ── Modo solo-análisis (paredes del terreno) ────────────────────────────────
# La MISMA medida que decide el paso de la ruta viva, sobre el frame ya en disco:
# así un laberinto que no se ve se reproduce sin abrir Unity, y el paso se puede
# verificar en los dos sentidos (verde y roto).
if [[ $WALL_ANALYZE -eq 1 ]]; then
  WALL_DUMP="${WALL_DUMP:-$ROOT/artifacts/playpass-wall-pixels.txt}"
  WALL_CROPS="${WALL_CROPS:-$WALL_DIR}"
  log "▶ Midiendo las paredes del laberinto: $WALL_DUMP · frame en $WALL_CROPS"
  if analyze_walls "$WALL_DUMP" "$WALL_CROPS"; then exit 0; fi
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

# La guarda de identidad ya no exige com.unity.pipeline (la marca es el código
# del juego), pero ESTE script sí lo necesita: es el paquete con el que se
# conduce el editor (simulate_key/simulate_pointer). Sin él, el fallo aparecería
# a mitad del pass con un error opaco de la CLI.
if ! grep -q '"com.unity.pipeline"' "$PROJECT/Packages/manifest.json"; then
  echo "✗ falta com.unity.pipeline en $PROJECT/Packages/manifest.json" >&2
  echo "  El pass EN VIVO conduce el editor y no puede funcionar sin ese paquete." >&2
  echo "  Añádelo (0.7.0-exp.1) al manifest del proyecto, o usa los modos de análisis." >&2
  exit 4
fi

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
# Escribe el resultado del script de Unity tal cual, SIN traducir finales de
# línea. `sys.stdout` en Windows es un TextIOWrapper con newline=None, así que
# cada `\n` que se le escribe se convierte en `os.linesep`: como el resultado de
# `StringBuilder.AppendLine` YA trae `\r\n` en Windows, el fichero salía con
# `\r\r\n` (CR doble) —medido: `artifacts/playpass-scene.txt` tenía las 65
# líneas así, y por eso ni siquiera lo normalizaba git al añadirlo. Escribir en
# `stdout.buffer` en UTF-8 fija LF en todas las plataformas y hace que estos
# volcados (que son evidencia commiteada) no dependan de quién los generó. La
# entrada se lee igual de explícita (`stdin.buffer` + UTF-8) porque el JSON trae
# acentos y glifos (`●`, `·`) y la codificación por defecto de un pipe en
# Windows no es UTF-8.
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
sys.stdout.buffer.write(out.encode('utf-8'))
sys.stdout.buffer.flush()
"
}

command -v python3 >/dev/null 2>&1 \
  || { echo "✗ falta python3 (se usa para leer el JSON del CLI)" >&2; exit 3; }

# ── «El editor no respondió» NO es «la escena no tiene HUD» ───────────────────
# El CLI contesta al instante cuando el editor está vivo; si el HILO PRINCIPAL del
# editor está esperando a un DIÁLOGO, TODOS los comandos caducan a los 30 s. El
# medido: «The open scene(s) have been modified externally», que Unity levanta
# cuando algo escribe en disco una escena abierta (el descarte de churn de un pase
# anterior, un `git checkout`, otro editor).
#
# Y el síntoma no se parece al problema: la salida útil queda VACÍA. Con eso el
# pase decía «la escena no tiene HUD presentable ()» —culpando a la escena— y
# moría en «no se pudo configurar el presenter: » con rc 3: sin causa y sin
# remedio, y con un campo vacío donde iba el motivo. Costó ir a buscar la causa a
# mano (enumerando ventanas del proceso) el 2026-10-10.
#
# `cli_error_msg`: el mensaje del CLI (su sobre trae `errors[]`, que es donde va
# «Pipeline command 'x' timed out after 30000ms») o vacío.
cli_error_msg() {
  python3 -c "
import sys, json
try: d = json.loads(sys.stdin.buffer.read().decode('utf-8'))
except Exception: sys.exit(0)
errs = d.get('errors') or []
if errs and isinstance(errs[0], dict):
    m = str(errs[0].get('message') or errs[0].get('code') or '')
    sys.stdout.buffer.write(m.encode('utf-8'))
"
}

# `clasificar_respuesta <rc> <salida cruda>` — las cuatro cosas que puede pasar,
# sin confundirlas:
#   ok|VALOR      el editor respondió y VALOR es lo que contestó (la sonda, o el
#                 objeto de `editor_status`)
#   error|MOTIVO  el editor RESPONDIÓ con un error (p. ej. la sonda dice que en la
#                 escena no hay presenter): es una respuesta, no un mudo, y el
#                 llamante puede hacer algo con ella (construir la escena)
#   mudo          no hubo respuesta legible: sin JSON, o el CLI murió sin decir
#                 nada. No se sabe NADA de la escena — y eso es lo que hay que
#                 decir en vez de inventarse el motivo.
#   bloqueado|M   el CLI caducó: el editor está ahí pero no atiende (diálogo o hilo
#                 atascado). M es el mensaje del CLI.
clasificar_respuesta() { # clasificar_respuesta <rc> <salida cruda>
  local rc="$1" crudo="$2" payload msg
  payload="$(printf '%s' "$crudo" | cmd_result)"
  case "$payload" in
    '') msg="$(printf '%s' "$crudo" | cli_error_msg)"
        case "$msg" in
          '') if [[ "$rc" == 0 ]]; then printf 'mudo'
              else printf 'bloqueado|el CLI salió con rc=%s y sin salida' "$rc"; fi ;;
          *timed\ out*|*[Tt]imeout*|*timeout*) printf 'bloqueado|%s' "$msg" ;;
          *) printf 'error|%s' "$msg" ;;
        esac ;;
    error\|*) printf 'error|%s' "${payload#error|}" ;;
    *)       printf 'ok|%s' "$payload" ;;
  esac
}

# El DETALLE de una clasificación. Ojo con `${x#*|}` a pelo: SIN `|` devuelve la
# cadena ENTERA, así que un `mudo` suelto acababa imprimiéndose como motivo
# («✗ el editor NO respondió: mudo») — que es el campo vacío que se quería evitar,
# pero con una palabra dentro. Con `|` hay motivo; sin `|` no hay nada que decir.
detalle_clasif() { # detalle_clasif <clasificación>
  if [[ "$1" == *'|'* ]]; then printf '%s' "${1#*|}"; fi
}

# El otro lado del error: decir QUÉ es y QUÉ HACER. Con evidencia, no con una
# corazonada: la marca que el propio paquete del pipeline deja en el log del
# editor cuando su hilo principal no llega a atender la petición.
diagnostico_editor_bloqueado() { # diagnostico_editor_bloqueado [detalle del CLI]
  local detalle="${1:-}" log="$PROJECT/Logs/Editor.log" marca=0
  # `mudo`/`bloqueado`/… son CLAVES, no motivos: si llega una aquí es que el
  # llamante pasó la clasificación entera, y el motivo se quedaría en esa palabra.
  case "$detalle" in mudo|bloqueado|error|ok) detalle="" ;; esac
  # Nunca un campo vacío: si el CLI no dijo por qué, se dice ESO.
  if [[ -n "$detalle" ]]; then echo "✗ el editor NO respondió: $detalle" >&2
  else echo "✗ el editor NO respondió: el CLI no devolvió ni JSON" >&2; fi
  echo "  (no es la escena: el editor no llegó a decir nada de la escena)" >&2
  # La marca se busca en la COLA del log: es de este intento, no de una vez que
  # pasó hace media hora (el log no se vacía y la marca de ayer seguiría ahí).
  if [[ -f "$log" ]] && tail -n 400 "$log" 2>/dev/null | grep -q 'Main thread operation timed out'; then
    marca=1
    echo "  su log lo confirma: «Main thread operation timed out» en las últimas líneas de $log" >&2
  fi
  echo "  Casi siempre es un DIÁLOGO modal esperando en la ventana del editor. El medido:" >&2
  echo "  «The open scene(s) have been modified externally», que sale cuando algo escribe" >&2
  echo "  en disco una escena que el editor tiene abierta (el descarte de churn de otro" >&2
  echo "  pase, un \`git checkout\` a mano, otro editor sobre el mismo proyecto)." >&2
  echo "  remedio: cierra ese diálogo en la ventana del editor (Reload o Ignore) y repite" >&2
  if [[ $marca -eq 0 ]]; then
    echo "  el pase. Si no hay ningún diálogo, el hilo está atascado: mira $log y, si no" >&2
    echo "  avanza, reinicia el editor." >&2
  else
    echo "  el pase (con el diálogo ya cerrado, Unity tiene que volver a atender)." >&2
  fi
  return 1
}

# Al entrar en Play el editor recarga el dominio y el servidor del pipeline
# reinicia: la PRIMERA llamada puede salir ≠ 0 sin salida alguna. Se espera a
# que Play esté vivo y se reintenta, en vez de abortar (comprobado: exit 6 a la
# primera, 0 a partir de la segunda). Nada de `| grep -q` en estas tuberías:
# `grep -q` cierra el pipe, el productor muere con SIGPIPE y `pipefail` da la
# tubería por fallida.
# `editor_status` trae un objeto (no un run_script) — se leen sus campos a mano.
# DÓNDE viven esos campos se pregunta al sobre REAL, no a la memoria: el editor
# contesta `{"data":{"command":"editor_status","result":{"compiling":false,…}}}`,
# así que buscar `compiling` en `data` (y no en `data.result`) devolvía vacío
# SIEMPRE y `wait_for_compiled` no esperaba nunca — que es justo el paso que evita
# medir con el dominio recargándose en Play (mundo congelado). Se miran los dos
# sitios para no volver a depender de cuál de ellos usa hoy el paquete.
editor_field() {
  UCMD editor_status 2>/dev/null | python3 -c "
import sys, json
try: d = json.load(sys.stdin)
except Exception: sys.exit(0)
r = d.get('data') or {}
v = r.get('$1')
if v is None:
    v = (r.get('result') or {}).get('$1')
# Los booleanos en MINÚSCULA, como los escribe el JSON del CLI: print(True) saca
# «True» y el case «… in *true*)» de wait_for_compiled no lo veía, así que la
# espera por recarga de dominio era un no-op silencioso. (Y ojo con las COMILLAS
# INVERTIDAS dentro de este bloque: el fuente va dentro de unas comillas dobles de
# bash, así que unas comillas invertidas ejecutarían aquí dentro lo que encierran.
# Medido: se colaron en un comentario y cada llamada imprimía un error de sintaxis
# y el bucle de espera se quedaba 120 s durmiendo por una letra.)
if v is True: sys.stdout.write('true')
elif v is False: sys.stdout.write('false')
elif v is not None: sys.stdout.write(str(v))
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

# Que HAYA un editor abierto no es que CONTESTE: `unity status` lo sirve el
# registro del CLI, no el editor (medido: seguía diciendo «ready» con el hilo
# principal bloqueado por el diálogo). Así que se le pregunta de verdad una vez
# —`editor_status`, lo más barato— ANTES de gastar la recompilación y de hablar de
# la escena: si no contesta, el pase se planta diciendo qué pasa y qué hacer.
ED_RC=0
ED_RAW="$(UCMD editor_status 2>/dev/null)" || ED_RC=$?
ED="$(clasificar_respuesta "$ED_RC" "$ED_RAW")"
case "$ED" in
  mudo|bloqueado*) diagnostico_editor_bloqueado "$(detalle_clasif "$ED")" || true; exit 3 ;;
esac

# Se compila ANTES de jugar: verificar contra una Assembly-CSharp vieja no
# valdría de nada. Si la compilación falla, el sitio del error está en
# scripts/check-unity-compile.sh.
log "▶ Recompilando los scripts del editor…"
UCMD recompile >/dev/null 2>&1 || true
# `*) break` y no `[[ -n "$RC" ]] && break`: con el editor mudo, esa prueba
# falsa dejaba el bucle con rc 1 y `set -e` se llevaba el pase por delante SIN
# decir nada. Un estado que no llega lo trata el reintento de abajo, que sí
# puede hablar.
RC=""
for i in $(seq 1 90); do
  RC="$(UCMD recompile_status 2>/dev/null | cmd_result 2>/dev/null || true)"
  case "$RC" in *compiling*) sleep 1 ;; *) break ;; esac
done
# Un estado que no llega NO es «compila»: antes, con `RC` vacío, el chequeo de
# abajo salía en verde y el pase medía contra una Assembly-CSharp que no sabía
# cuál era. Se reintenta (el editor puede estar recargando el dominio) y, si sigue
# sin llegar, se dice: es el mismo síntoma del editor que no atiende.
if [[ -z "$RC" ]]; then
  for i in $(seq 1 10); do
    RC="$(UCMD recompile_status 2>/dev/null | cmd_result 2>/dev/null || true)"
    if [[ -n "$RC" ]]; then break; fi
    sleep 1
  done
fi
if [[ -z "$RC" ]]; then
  diagnostico_editor_bloqueado "no llegó el estado de la recompilación" || true
  exit 3
fi
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

case "$ED" in
  *playing*) log "   (había Play activo: se detiene)"; UCMD editor_stop >/dev/null 2>&1 || true ;;
esac

# La consola se limpia para que el chequeo de `stream falló` hable SOLO de esta
# corrida (el resto del pass puede haber dejado ruido).
UCMD clear_console >/dev/null 2>&1 || true

# ── 2. Config del presenter ──────────────────────────────────────────────────
CONFIG="$PROJECT/Temp/antsim-playpass.json"
mkdir -p "$PROJECT/Temp"
# `maze:true`: la partida canónica es la que el jugador recibe por defecto
# (`LaunchSettings.Maze`), y sin terreno el paso de las paredes no tendría nada
# que medir — además de que «terreno con obstáculos» es lo que el juego promete.
printf '{"seed":%s,"grid":%s,"colonies":%s,"ticks":%s,"speed":%s,"seedPool":"%s","replay":"","maze":true}\n' \
  "$SEED" "$GRID" "$COLONIES" "$TICKS" "$SPEED" "${SEED_POOL:--}" > "$CONFIG"

# La sonda del presenter, con la respuesta CLASIFICADA (`set_probe` deja la clave
# en SET_CLAVE y el detalle en SET_DETALLE): «la escena no tiene presenter» es una
# RESPUESTA del editor; «no contestó» es otra cosa, y el pase no puede hablar de
# la escena cuando nadie ha dicho nada de la escena.
set_probe() {
  local rc=0 raw c
  raw="$(UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeToastsSet.cs")" 2>/dev/null)" || rc=$?
  c="$(clasificar_respuesta "$rc" "$raw")"
  SET_CLAVE="${c%%|*}"
  SET_DETALLE="$(detalle_clasif "$c")"
  # `return 0` explícito: el camino BUENO termina en una prueba que da FALSO, y
  # con `set -e` eso mataba el pase en silencio (rc 1, sin ningún mensaje) justo
  # después de «Recompilando los scripts del editor…» — medido con el CLI falso.
  return 0
}

set_probe
case "$SET_CLAVE" in
  ok) log "   config:   $SET_DETALLE" ;;
  mudo|bloqueado)
    diagnostico_editor_bloqueado "$SET_DETALLE" || true; exit 3 ;;
  *)
    echo "   la escena no tiene HUD presentable ($SET_DETALLE) — se construye con el bootstrapper…"
    UCMD menu "AntSim/Crear escena de juego" >/dev/null 2>&1 || true
    # El veredicto del GENERADOR se toma aquí, antes de que el pase guarde nada:
    # después, la escena la habría escrito el editor y el fallo del generador
    # quedaría tapado —o, al revés, se le culparía de lo que escribió el pase—.
    if ! check_canonical "la generación de la escena"; then
      echo "✗ el generador no dejó la escena en forma canónica — no se mide" >&2
      exit 1
    fi
    if ! guardar_escena "Assets/Scenes/Game.unity" "la construcción de la escena"; then
      echo "✗ no se pudo cerrar la forma de lo que guardó el pase — no se mide" >&2
      exit 1
    fi
    set_probe
    case "$SET_CLAVE" in
      ok) log "   config:   $SET_DETALLE" ;;
      mudo|bloqueado)
        diagnostico_editor_bloqueado "$SET_DETALLE" || true; exit 3 ;;
      *)
        echo "✗ la escena construida no tiene presenter configurable: ${SET_DETALLE:-sin motivo que el editor haya dicho}" >&2
        exit 3 ;;
    esac
    ;;
esac

# La forma canónica de lo que acaba de GENERAR el bootstrapper se COMPRUEBA aquí
# (no se repara: la cierra el propio generador) para que no se mida sobre un árbol
# inestable — dos pasadas del mismo montaje tienen que dar el mismo diff.
if ! check_canonical "la construcción de la escena"; then
  echo "✗ el árbol no está en forma canónica — no se mide sobre bytes inestables" >&2
  exit 1
fi

# Y las REFERENCIAS, que la canonicalización no mira: un asset puede quedar
# perfectamente canónico y sin su textura (es exactamente el accidente medido).
if ! check_references "la construcción de la escena"; then
  echo "✗ no se mide sobre un árbol al que le falta una referencia" >&2
  exit 1
fi

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
for _probe in ProbeToastsSet ProbeToastsSample ProbeSceneInspect ProbeVisualSample ProbeHudIconShot ProbeSparklinePixels ProbeWallPixels ProbeCanonicalizeAssets ProbeActorOrientation ProbeAntHeadingPixels; do
  UCMD run_script --file "$(winpath "$PROBE_DIR/$_probe.cs")" >/dev/null 2>&1 || true
done
wait_for_compiled || true

# Los actores, ya compilados: ¿el morro apunta al rumbo? Va aquí y no antes para
# no provocar una recarga de dominio entre las comprobaciones del árbol.
if ! check_actor_orientation; then
  echo "✗ las siluetas no apuntan a su eje — no se mide un mundo que se dibuja de lado o de canto" >&2
  exit 1
fi

# Y la misma pregunta EN EL RENDER: la matriz correcta no prueba lo que se ve (el
# encuadre de la cámara, la proyección, el rasterizado y el aliasing quedan en medio).
# Va aquí, con las sondas ya compiladas y sin Play, por el mismo motivo que la de
# arriba: el render de la hormiga no necesita la partida para ser el render del juego.
if ! check_actor_heading_pixels; then
  echo "✗ el eje de la silueta no cae sobre el rumbo en los píxeles — no se mide un mundo que el jugador ve torcido" >&2
  exit 1
fi

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
# Los ICONOS de la fuente del juego, recortados del frame REAL (F5.1). Va aquí,
# con Play vivo, por el mismo motivo que la muestra visual: los iconos solo
# existen pintados, y su legibilidad a 14 px no la ve ninguna muestra de estado.
HUD_ICONS="${HUD_ICONS:-$ROOT/artifacts/playpass-hud-icons.txt}"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeHudIconShot.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null > "$HUD_ICONS" || true
# La evidencia en PÍXELES se saca de `Temp/` AHORA: con Play vivo, antes de que
# nada más toque el editor y antes de que Unity barre `Temp/` al cerrar. El paso
# de iconos (6ter) mide entonces sobre estas copias conservadas, no sobre un
# directorio efímero — así el mismo análisis se puede repetir mañana sin editor.
ICONS_CROPS="${ICONS_CROPS:-$ICONS_DIR}"
# Si no había recortes, el paso 6ter lo dice con el nombre del fichero que falta
# y el pass sale ≠ 0: el fallo de la sonda no se puede quedar en un aviso.
preserve_icons "$PROJECT/Temp" "$ICONS_CROPS" || true

# Las DESCARGAS de la gráfica, medidas en píxeles de la tarjeta (F5.1). En DOS
# invocaciones a propósito: lo que se escribe en la textura de la gráfica no está
# en el framebuffer hasta que el motor dibuja OTRO frame, y `run_script` corre
# entero dentro de un frame. `prepare` inyecta la serie conocida y congela la
# alimentación del HUD; entre las dos llamadas pasan frames de verdad; `measure`
# captura, guarda y RESTAURA la serie real. `restore` queda como red de
# seguridad (si `measure` murió antes de restaurar, el HUD no se queda mintiendo).
SPARK_DUMP="${SPARK_DUMP:-$ROOT/artifacts/playpass-sparkline-pixels.txt}"
mkdir -p "$(dirname "$SPARK_DUMP")"
SPARK_STEP="$PROJECT/Temp/antsim-spark.step"
printf 'prepare' > "$SPARK_STEP"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeSparklinePixels.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null > "$SPARK_DUMP" || true
# `cmd_result` imprime el resultado SIN salto final (lo sirve una sustitución de
# comandos), así que dos volcados seguidos quedaban pegados: `injected=1SPARK|…`
# y el analizador no encontraba la línea de MEDIDA. Cada paso cierra su línea.
printf '\n' >> "$SPARK_DUMP"
sleep 2
printf 'measure' > "$SPARK_STEP"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeSparklinePixels.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null >> "$SPARK_DUMP" || true
printf '\n' >> "$SPARK_DUMP"
printf 'restore' > "$SPARK_STEP"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeSparklinePixels.cs")" >/dev/null 2>&1 || true
# La evidencia sale de `Temp/` con Play todavía vivo: el paso 6quater mide sobre
# estas copias, no sobre un directorio que Unity barre al cerrar.
SPARK_CROPS="${SPARK_CROPS:-$SPARK_DIR}"
preserve_sparkline "$PROJECT/Temp" "$SPARK_CROPS" || true

# Las PAREDES del terreno (F6), en píxeles del MISMO frame. Una sola llamada (la
# sonda renderiza la cámara a su propia RenderTexture y la lee en el acto, sin
# esperar a que el motor dibuje el frame siguiente) y antes del bloque 4, que
# reconstruye la escena y vuelve a entrar en Play: esta captura es la de ESTA
# partida. La evidencia sale de `Temp/` con Play vivo, por el mismo motivo que las
# otras: Unity barre `Temp/` al cerrar.
WALL_DUMP="${WALL_DUMP:-$ROOT/artifacts/playpass-wall-pixels.txt}"
mkdir -p "$(dirname "$WALL_DUMP")"
UCMD run_script --file "$(winpath "$PROBE_DIR/ProbeWallPixels.cs")" 2>/dev/null \
  | cmd_result 2>/dev/null > "$WALL_DUMP" || true
# `cmd_result` no cierra la línea (va por sustitución de comandos): sin esto el
# volcado se queda sin salto y el medidor no encuentra la línea `WALL|`.
printf '\n' >> "$WALL_DUMP"
WALL_CROPS="${WALL_CROPS:-$WALL_DIR}"
preserve_walls "$PROJECT/Temp" "$WALL_CROPS" || true

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

# ── 6ter. Iconos del HUD (F5.1) ─────────────────────────────────────────────
# Va ANTES del bloque 4 a propósito: esa fase reconstruye la escena con el
# bootstrapper y vuelve a entrar en Play, así que los recortes de esa sesión ya no
# serían los que se capturaron con el HUD de la partida. Y el veredicto es propio,
# no de aspecto: un fallo aquí es un GLIFO, no un texto que no cabe, y el resumen
# tiene que decir cuál de los dos fue.
RCICON=0
log ""
log "▶ Iconos del HUD: cada glifo contra su referencia del .ttf (F5.1)"
if analyze_icons "$HUD_ICONS" "${ICONS_CROPS:-$ICONS_DIR}"; then
  log "✓ los iconos del HUD se clasifican como SU glifo (comparador scripts/playpass-icons.py en verde)"
else
  RCICON=1
fi

# ── 6quater. Descargas de la gráfica, en píxeles (F5.1) ──────────────────────
# También antes del bloque 4: esa fase reconstruye la escena y vuelve a entrar en
# Play, así que la captura de ESTA partida ya no sería la misma. Veredicto
# propio: un fallo aquí es una BARRA (color, columnas, base o fuga), no un glifo
# ni un texto que no cabe.
RCSPARK=0
log ""
log "▶ Descargas en la tarjeta: barras ámbar medidas en los píxeles del HUD (F5.1)"
if analyze_sparkline "$SPARK_DUMP" "${SPARK_CROPS:-$SPARK_DIR}"; then
  log "✓ la serie de descargas se ve en la tarjeta (medidor scripts/playpass-sparkline.py en verde)"
else
  RCSPARK=1
fi

# ── 6quinquies. Paredes del terreno, en píxeles (F6) ────────────────────────
# Antes del bloque 4 por el mismo motivo que la gráfica: esa fase reconstruye la
# escena y vuelve a entrar en Play, así que el frame de ESTA partida ya no estaría.
# Veredicto propio: un fallo aquí es el TERRENO (celdas sin pintar, color, escala o
# controles), no un glifo ni un texto que no cabe.
RCWALL=0
log ""
log "▶ Terreno: las paredes del laberinto, medidas en los píxeles del frame (F6)"
if analyze_walls "$WALL_DUMP" "${WALL_CROPS:-$WALL_DIR}"; then
  log "✓ el terreno que colisiona está en pantalla (medidor scripts/playpass-walls.py en verde)"
else
  RCWALL=1
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
  # El veredicto del GENERADOR, antes de que el pase guarde (mismo paso que en la
  # construcción de la escena: sin esto, el guardado del pase taparía su fallo y
  # el del generador se atribuiría al pase).
  if ! check_canonical "la generación de la escena (bloque 4)"; then
    echo "✗ el generador no dejó la escena en forma canónica — no se sondea" >&2
    RC4=1
  fi
  if ! guardar_escena "Assets/Scenes/Game.unity" "la reconstrucción de la escena (bloque 4)"; then
    echo "✗ no se pudo cerrar la forma de lo que guardó el pase — no se sondea" >&2
    RC4=1
  fi

  printf '{"seed":%s,"grid":%s,"colonies":%s,"ticks":%s,"speed":%s,"seedPool":"%s","replay":""}\n' \
    "$SEED" "$GRID" "$COLONIES" "$TICKS" "$B4_SPEED" "${SEED_POOL:--}" > "$CONFIG"
  set_probe
  case "$SET_CLAVE" in
    ok) ;;
    mudo|bloqueado) diagnostico_editor_bloqueado "$SET_DETALLE" || true; RC4=1 ;;
    *) echo "✗ no se pudo configurar el presenter para el bloque 4: $SET_DETALLE" >&2; RC4=1 ;;
  esac
  # El config se GUARDA en la escena: el reinicio con plan la recarga desde el
  # asset, así que lo que solo viva en memoria (velocidad, semilla, pool) se
  # perdería al recargarla y la segunda mitad de la fase correría con los
  # valores por defecto del bootstrapper. Guardar y cerrar la escritura del pase
  # van juntos: este es el guardado que decide la configuración que se COMMITEA
  # en `Game.unity` (lo dice `GeneratedSceneCatalog`), así que su forma canónica
  # la tiene que cerrar quien escribe.
  if ! guardar_escena "Assets/Scenes/Game.unity" "el guardado del bloque 4"; then
    echo "✗ no se pudo cerrar la forma de lo que guardó el pase — no se sondea" >&2
    RC4=1
  fi

  # El bloque 4 RECONSTRUYE la escena y la vuelve a guardar con la configuración
  # del bloque 4 dentro. El generador ya dio su veredicto al generar y cada
  # guardado del pase se cerró al guardarlo; aquí se comprueba que el árbol quedó
  # canónico de verdad — si no, la sonda mediría sobre bytes inestables y dos
  # pasadas del mismo montaje no darían el mismo diff.
  if ! check_canonical "la reconstrucción del bloque 4"; then
    echo "✗ el árbol no está en forma canónica antes del bloque 4 — no se sondea" >&2
    RC4=1
  fi
  if ! check_references "la reconstrucción del bloque 4"; then
    echo "✗ una referencia se perdió al reconstruir la escena del bloque 4 — no se sondea" >&2
    RC4=1
  fi

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
      #
      # La partida canónica emite UNA sola alerta — `first-unload` — y va ANCLADA
      # a la posición de su descarga desde 2026-10-07: el `RelayTracker` guarda la
      # posición del primer `Unload` (FirstUnloadX/Y) y el `AlertDeriver` la usa
      # para el hito. Antes salía en x=-1,y=-1 (muestras #C con anchored=0) y este
      # paso —que exige a qué saltar— era inalcanzable. Si vuelve a llegar sin
      # ancla, el fallo está en ese par (Unload con posición en el evento), no
      # aquí: las muestras #C de $B4_SAMPLES dicen el tick y las alertas que había.
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

# ── 8. Churn de re-serialización del editor: se descarta ─────────────────────
# Va antes del resumen y por TODAS las salidas que llegan hasta aquí (verde o
# rojo): lo que se limpia es lo que escribió el editor, y eso no depende del
# veredicto. Los caminos que salen ANTES de jugar (sin editor, forma canónica
# derivada, referencia perdida) no lo llaman a propósito: ahí el árbol es la
# evidencia del fallo y tocarlo la borraría.
if [[ $ANALYZE_ONLY -eq 0 ]]; then
  log ""
  log "▶ Churn de re-serialización: descartando lo que dejó el editor (--no-discard-churn lo salta)"
  RCDISCARD=0
  discard_churn || RCDISCARD=1
fi

# ── 8bis. La HUELLA del árbol: el pase no acumula estado ─────────────────────
# La maquinaria (`fp_*` y `check_tree_fingerprint`) vive junto a los analizadores,
# antes del selftest: es donde se prueba sin editor, con un repositorio de mentira.
# Aquí sólo se LLAMA, y después del descarte del churn porque es el estado FINAL
# del árbol el que tiene que coincidir con el que dejó la pasada anterior.
if [[ $ANALYZE_ONLY -eq 0 ]]; then
  log ""
  log "▶ Huella del árbol: ¿una segunda pasada del mismo montaje deja el MISMO diff contra HEAD?"
  RCFP=0
  check_tree_fingerprint || RCFP=1
fi

# ── 9. Resumen ───────────────────────────────────────────────────────────────
log ""
if [[ $RC35 -eq 0 && $RC4 -eq 0 && $RCF5 -eq 0 && $RCICON -eq 0 && $RCSPARK -eq 0 && $RCWALL -eq 0 && $RCDISCARD -eq 0 && $RCFP -eq 0 ]]; then
  [[ $BLOCK4 -eq 1 ]] && log "✓ Play pass completo: bloques 3, 4 y 5 + aspecto, iconos, gráfica y terreno (F5.1/F6) verificados en vivo" \
                      || log "✓ bloques 3 y 5 + aspecto, iconos, gráfica y terreno (F5.1/F6) verificados en vivo"
  [[ $FP_COMPARED -eq 1 ]] && log "  ✓ y el árbol quedó con la MISMA huella que la pasada anterior ($FP_FILE): el pase no acumula estado"
  exit 0
fi
# Con `|| true` cada línea: una prueba FALSA (`[[ ]]`) deja el status en 1 y, con
# `set -e`, el pase salía antes de imprimir los demás veredictos (el bloque que
# falló sí se nombra arriba, pero el resumen se quedaba a medias).
[[ $RC35 -ne 0 ]] && echo "✗ el bloque 3/5 falló (ver arriba)" >&2 || true
[[ $RCF5 -ne 0 ]] && echo "✗ el aspecto falló (ver arriba)" >&2 || true
[[ $RCICON -ne 0 ]] && echo "✗ los iconos del HUD fallaron (ver arriba)" >&2 || true
[[ $RCSPARK -ne 0 ]] && echo "✗ las barras de descargas de la gráfica fallaron (ver arriba)" >&2 || true
[[ $RCWALL -ne 0 ]] && echo "✗ las paredes del terreno no se miden como están en pantalla (ver arriba)" >&2 || true
[[ $RC4 -ne 0 ]] && echo "✗ el bloque 4 falló (ver arriba)" >&2 || true
[[ $RCDISCARD -ne 0 ]] && echo "✗ el descarte del churn no dejó el árbol limpio (ver arriba)" >&2 || true
[[ $RCFP -ne 0 ]] && echo "✗ la huella del árbol cambió entre dos pasadas del mismo montaje (ver arriba)" >&2 || true
exit 1
