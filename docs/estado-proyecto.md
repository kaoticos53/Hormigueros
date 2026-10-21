# Estado del proyecto — consolidado

*Actualizado: 2026-10-07 · HEAD: `v0.8.0` (tag de cierre de la F5.3) ·
suite: 658/658 (Windows) · tags: `v0.4.0` (Fase 4), `v0.5.0` (F5.2a), `v0.6.0` (F5.2b),
`v0.6.1` (CI fix), `v0.7.0` (F5.2c NEAT), `v0.8.0` (F5.3 escala)*

Mapa de las fases del proyecto: qué está terminado, qué queda y dónde
estamos. Los detalles de cada fase viven en sus documentos; este es el
índice con veredicto. Enlaces: [`arquitectura.md`](arquitectura.md) (plan
global por fases), [`especificaciones.md`](especificaciones.md) (contratos),
[`fase5-plan.md`](fase5-plan.md) (plan vigente de Fase 5),
[`ideas-externas.md`](ideas-externas.md) (revisión de cuatro simuladores
afines con propuestas priorizadas por valor/coste) y
[`politicas-benchmark.md`](politicas-benchmark.md) (la vara externa: aleatoria
vs scripted vs evolucionada en la misma arena).

## Resumen en una tabla

| Fase | Alcance | Estado | Doc de cierre |
|---|---|---|---|
| 1–2 | Mundo determinista + neuroevolución (GA/MLP) | ✅ | `arquitectura.md` §Fase 2 |
| 3 / 3bis / 3ter | Pre-entrenamiento headless, arena realista, arranque en frío | ✅ | `fase3ter-resumen.md` |
| 4 (F4.0–F4.4) | Capa jugador: stream, HUD, inspector, import, CI con pines | ✅ `v0.4.0` | `fase4-resumen.md` |
| 5.0 | Mini-grafo MLP (canal F de activaciones) | ✅ | `fase5-plan.md` §1 |
| 5.1 + 5.1bis | Pulido Unity + multi-visor | ✅ | `fase5-plan.md` §2/§2bis |
| **5.2a** | **Atta: cortar → transportar → hongo** | ✅ **CERRADO** `v0.5.0` | `fase5-2a-atta.md` |
| **5.2b** | **Eciton: saqueo + combate** (sensor, botín, balance V6) | ✅ **CERRADO** `v0.6.0` | `fase5-2b-eciton.md` |
| **5.2c** | **NEAT / `.antgenome` v2 (topologías que evolucionan)** | ✅ **CERRADO** `v0.7.0` | `fase5-2c-neat.md` |
| — | **Cross-platform: CanonMath + cabecera del stream** | ✅ `v0.6.1` | `arquitectura.md` §CI |
| **5.3** | **Escala: SoA, compactación, CHC, benchmark, aprendizaje, LOD + instancing** | ✅ **CERRADO** `v0.8.0` (exit medido en un build de jugador) | `fase5-3-escala.md` |
| 6 | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **SEIS pines de hash**, verdes en Linux en el último
push (`v0.8.0`, run #37 del 2026-09-19): stream canónico, replay con drops a
3000 y 6000 ticks, la partida Atta canónica, la de INVASIÓN canónica y la
NEAT canónica. **658/658 tests** en Windows y verdes en Linux en CI en cada push,
igual que los 6 scripts de pin. El job `test` incluye además **seis pasos que
verifican instrumentos sin editor**: el analizador de compilación de Unity, las
guardas del Unity CLI, el veredicto del hook pre-push, el **selftest del barrido
de costes** (el que prueba el ajuste fijo/marginal de §4.10), el guardarraíl de
cifras documentales y —desde el 2026-09-20— el **verificador de equivalencia de
assets de Unity**.

**El churn de assets de Unity no entra al repo** (2026-09-20) — abrir el
proyecto, construir un player o dejar que el editor guarde reescribe la escena,
los materiales y los ajustes de URP SIN cambiar lo que significan. Medido ese
día, en una pasada del generador del multi-visor: **de 64 assets, 60 eran
idénticos** (sólo se movía el bloque `AssetVersion` y cambiaban de ancla) y 4
cambiaban de verdad. `scripts/check-unity-assets.sh` decide eso por equivalencia
estructural —documentos por clase, referencias locales resueltas a la huella de
su destino e iteradas a punto fijo— así que el reordenamiento y la renumeración
de anclas de Unity no cuentan, y los finales de línea tampoco —desde el
2026-09-20 los assets están fijados a LF por `.gitattributes`, así que el
`autocrlf` de la máquina ya no los reescribe—. En CI recorre los commits del
push y falla si alguno metió un asset que no cambia nada, con la lista de
ficheros a descartar. Desde el 2026-10-09 su `--discard` quita la forma de los
dos casos: restaura el churn puro y, a los que cambian de verdad, les reescribe
la forma canónica **sin tocar el contenido**, así que el diff queda reducido al
cambio —medido en `Game.unity` (425 documentos), una pasada que re-ancla todo y
cambia una línea escalar pasa de 10 669 líneas de diff a 2—. Sólo escribe si la
forma canónica pasa su propio `compare`, y después vuelve a clasificar y a mirar
el índice para confirmarlo.

**El pase también ESCRIBE, y por eso cierra su escritura y descarta lo que deja**
(2026-10-10, medido con el editor abierto, `Game.unity`, 425 documentos) — dos
defectos que sólo aparecieron al correr el Play pass de verdad:

  · la comprobación canónica del bloque 4 culpaba al **generador** de lo que había
escrito el **pase**: un `save_scene` sobre una escena ya canónica la deja
`DERIVARÍA` sin cambiarle el contenido (sólo anclas y orden), así que el bloque 4
se plantaba sin sondear nada. Ahora el veredicto del generador se toma justo tras
generar y ANTES de que el pase escriba, y cada `save_scene` del pase se cierra con
el mismo canonicalizador —no arregla lo del generador, arregla lo suyo—;
  · y ese cierre (y el descarte del churn al terminar) se hacen **desde dentro del
editor** (sonda `scripts/unity/ProbeCanonicalizeAssets.cs`: canonicaliza y recarga
la escena). Escribir la escena por detrás, con el editor teniéndola abierta, le
hace notar el cambio al vigilante y levanta «The open scene(s) have been modified
externally»: el editor deja de contestar a TODOS los comandos (`Main thread
operation timed out after 30000ms`) hasta que alguien pulsa el diálogo, y la
siguiente pasada muere en la primera sonda.

**«El editor no responde» NO es «la escena no tiene HUD»** (2026-10-10) — cuando
el hilo principal está parado en un diálogo, el CLI contesta con la salida
**vacía** (y `unity status` sigue diciendo `ready`, porque eso lo sirve su
registro, no el editor). El pass leía ese vacío como una respuesta y culpaba a la
escena: «la escena no tiene HUD presentable ()» —con el motivo en blanco— y moría
en «no se pudo configurar el presenter: » con rc 3, sin causa ni remedio. Costó ir
a buscar la causa a mano (enumerando las ventanas del proceso). Ahora el pass
CLASIFICA cada respuesta del CLI en cuatro cosas que no se pueden mezclar —`ok`,
`error` (el editor habló y la escena tiene la culpa), `mudo` (ni JSON) y
`bloqueado` (caducó)— y, si no responde, sale con rc 3 diciendo qué pasó, que NO
es la escena y el remedio (cerrar el diálogo —Reload o Ignore— y repetir; o mirar
`Logs/Editor.log` si la marca «Main thread operation timed out» está en su COLA,
no en cualquier parte del fichero).

Y al probarlo **sin editor** —un CLI de mentira que contesta por guion
(`bloqueado`, `mudo`, mudo a partir de la recompilación, y una escena sin
presenter)— salió un defecto que ningún pase real había tocado: `set -e` se
llevaba el pase por delante **en silencio, con rc 1**, porque el bucle de la
recompilación (y la función que configura el presenter) terminaban en una prueba
`[[ … ]] && …` que da FALSO justo en el camino bueno. Un pase real siempre había
pasado por ahí «con suerte»; el CLI falso lo destapó como un `rc 1` sin una línea
de salida.

**Las hormigas andaban DE LADO** (2026-10-10) — la caja del actor tiene el largo
en Y (`GameSpriteModel`: «Y = el largo, que es el eje de avance», y en la hormiga
la cabeza va hacia +Y) y el giro del presenter era `−rumbo` en vez de
`90° − rumbo`, así que el morro caía 90° a la izquierda del avance. Medido con la
matemática de Unity y la rotación del propio presenter, dentro del editor
(`scripts/unity/ProbeActorOrientation.cs`): **90° justos en los ocho rumbos de
prueba** antes del arreglo, 0° después. Lo que lo hacía peligroso es que no lo
veía nadie — los tres lóbulos estaban, el mundo aparecía, el conteo de hormigas
no cambiaba y las puertas de píxeles («se ven hormigas», «el tablero se lee como
tierra») seguían en verde; lo vio el jugador. Ahora es un PASO del Play pass: se
planta si el morro se desvía más de 2° del rumbo.

**Y los ítems y las hojas salían DE CANTO** (2026-10-10) — el arte de una pieza
es un sólido PLANO (se extruye en Z) y la cámara mira de arriba: dibujada con
`Quaternion.identity`, desde arriba se ve su canto, o sea un rectángulo, y del
dibujo no se lee nada. La hoja salía de 0,66 s × 1,10 s con su largo (1,24 u) en
el eje vertical y el `1,1` de la escala —que iba, y va, al grosor— estirado en el
plano: ni punta ni nervio, y el limbo apuntando al cielo. La carga, igual, y los
mordiscos con ella. El arreglo es el MISMO ayudante de la hormiga:
`FlatRotation` (que es `ActorRotation` con el rumbo que apunta al norte: no hay
rumbo que seguir, porque el `FoodItem` del Core no guarda ángulo y el stream no
lo lleva, así que inventarle un giro por pieza sería una decisión que el dato no
tiene) y `FlatScale` (1:1 en el plano del tablero y el grosor a la mitad), usadas
por ítem, hoja y mordisco a través de una sola `FlatTRS`. Medido en el editor por
`ProbeActorOrientation`: la hoja acuesta su eje (0° del plano, 0° al norte) y
deja **0,60 × 1,24 u** de planta con 0,50 u de alto, contra **0,66 × 1,10 u** de
canto y 0,62 u de alto antes; la carga pasa de 0,71 × 1,00 u con 0,78 u de alto a
0,71 × 0,78 u con 0,50 u. Y la sonda exige que la configuración VIEJA falle: si
pasa, el bloque sería decorativo (el probe lo dice y se planta).

**Y las muescas de la hoja comen del BORDE del limbo** (2026-10-10) — el borde de
la hoja es una ELIPSE (semiejes 0,30 s de ancho × 0,62 s de largo, los extremos del
polígono de `GameSpriteModel.Leaf()`), pero los cortes consumidos se repartían con
un radio ÚNICO de 0,55 s. Un radio no cae en un borde elíptico en ningún sitio:
por los costados queda FUERA del limbo (0,55 > 0,30) y por las puntas DENTRO
(0,55 < 0,62), así que las muescas se veían flotando en el vacío junto a la hoja.
Ahora `SimPresenterBehaviour.BiteOffset` las reparte sobre la elipse —los semiejes
salen de `GameSpriteModel.LeafHalfWidth`/`LeafHalfLength`, una sola definición— y
el PASO del Play pass que ya medía el acostado comprueba cada muesca contra el
borde (r = 1, tolerancia 0,02), exigiendo que el círculo viejo falle.

**Y el rumbo se mide también EN LOS PÍXELES** (2026-10-10) — la matriz correcta no
prueba el render: entre la matriz del presenter y lo que ve el jugador quedan el
encuadre de la cámara (¿mira al nadir? ¿su arriba es el norte de pantalla?), la
proyección, el rasterizado y el aliasing, que es exactamente donde se coló el giro
de 90° de la hormiga. `scripts/unity/ProbeAntHeadingPixels.cs` renderiza la malla y
el material REALES de la escena con un rumbo fijo en una cámara de usar y tirar que
copia la del juego (mismo `orthographicSize`, misma rotación) y que sólo ve la capa
de la hormiga, así que el fondo es plano y la silueta es la DIFERENCIA entre el
frame con ella y el mismo frame sin ella. Sobre esos píxeles mide el eje (segundo
momento) a la escala de la partida —18,69 px de hormiga, la escala real de un
viewport de 1080— y 6× más cerca, y el MORRO (el extremo LIGERO: el gáster pesa más
que la cabeza) **sólo a zoom**: a 19 px el reparto de área de los dos extremos deja
1 px de margen en los rumbos diagonales (medido: 16 px contra 15 px), y un dato así
no decide nada, así que ahí se publica sin usarse. Medido: el eje desvía **0°** en
los ocho rumbos a las dos escalas y el morro 0° a zoom; y el mismo render con el
giro viejo (`−rumbo`) desvía **90°**, que es lo que hace que la comprobación tenga
dientes en vez de ser decorativa. La
evidencia se mira: `artifacts/playpass-heading-pixels/antsim-heading-pixels.png`
(una hormiga por rumbo, el rumbo esperado pintado en magenta y el morro medido en
cian). Es un paso del Play pass y falla cerrado: sin la imagen, «medido en píxeles»
no se puede mirar.

**Finales de línea fijados por el repo, no por la máquina** (2026-09-20) —
`.gitattributes` fija el `eol` por tipo: **LF para todo el texto** —código,
documentos y los assets YAML de Unity en modo Force Text— y **CRLF sólo para lo
que interpreta Windows** (`.bat`, `.cmd`, `.ps1`); los binarios (`.png`,
`.antgenome`, `.antsave`) quedan `-text`. Antes lo decidía el `autocrlf` de cada
máquina: medido con `git ls-files --eol`, 442 ficheros en LF y 176 en CRLF **con
los MISMOS blobs**, y una lectura sin normalizar veía un fichero entero cambiado
(`AntMat.mat`: 71 inserciones / 71 borrados, sólo por el `\r`). El supuesto de
que el editor escribe CRLF resultó **falso al medirlo**: `MultiSim.unity` y el
asset de URP salidos del bootstrapper tienen hash sin filtros idéntico al del
blob —LF de verdad— y el CRLF del árbol era del checkout. La medición destapó
además un defecto del volcado de la sonda: `cmd_result` escribía por
`sys.stdout`, que en Windows es un `TextIOWrapper` que traduce `\n` a
`os.linesep`, así que `artifacts/playpass-scene.txt` quedó commiteado con CR
doble (sus 65 líneas eran `\r\r\n`); ahora escribe bytes en UTF-8 y el volcado
es LF en toda plataforma.

**Cada escena generada declara su entrada y su configuración** (2026-10-07) — las
dos escenas versionadas no salen de los defaults de su montador: `Game.unity` pide
la configuración del Play pass (ticks 7200, velocidad 2) y `MultiSim.unity` la del
medidor (grid 256, 12000 ticks, frameEvery 5), y ni el fichero ni el montador lo
decían. `GeneratedSceneCatalog` (puro) lo registra —entrada que la escribió,
configuración por vista, comando de regeneración y qué OTRAS entradas pisan esa
ruta— y `GeneratedSceneContractTests` lo contrasta campo a campo contra el YAML de
cada escena y contra las constantes de sus productores (`playpass-live.sh` y
`MultiSimBootstrapper`): regenerar deja de depender de recordar. Un pase que deje
otra configuración falla nombrando el campo, la escena y los dos valores.

**Guardarraíl de cifras documentales** (2026-09-20) — los documentos afirman el
número de pines y el de tests, y esas cifras se quedaron atrás más de una vez
(el README llegó a contar un pin de menos que hashes fijados; la lista de deuda
de F5.1 pedía cosas hechas) sin que nada pudiera verlo, porque los documentos no
estaban en ningún test. Ahora `scripts/check-doc-figures.sh` —paso propio en el
job `test`— mide la verdad del repo (pines = ficheros `scripts/*.expected`;
tests = casos descubiertos por la suite) y falla si un documento de estado
dice otra cosa. Dos escapatorias, las dos explícitas: las regiones
`<!-- cifras-historicas -->` (notas de versión, registro de rodajas, fases
cerradas: una cifra de un cierre pasado es correcta aunque no sea la de hoy) y
las bitácoras `docs/fase*-*.md`; un documento nuevo con cifras que no esté
clasificado **falla** en vez de colarse.

**Gate de Unity en CI — corregido, DORMIDO y con el alcance acotado** — el job
`unity-compile` compila los MonoBehaviours que `dotnet test` no ve, y hasta el
2026-09-20 nunca había corrido: necesita la variable `UNITY_CI` y un secreto de
licencia. Estaba
además mal para una licencia Personal (`unity license activate --personal` no
sirve en CI: el backend rechaza los tokens de cuenta de servicio), le
faltaban las librerías de runtime del editor y el `--yes --accept-eula` del
instalador. Los tres defectos se corrigieron contra el andamiaje oficial de
Unity (`unity ci init --dry-run`). Desde el 2026-09-19 tiene además
**disparador manual** (`workflow_dispatch`, input `unity`): `cache` ejercita
SOLO el paso de `actions/cache@v5` —lo que se quería comprobar del salto a v5
sin encender nada permanente— en segundos, sin licencia y saltando el job
`test` (que es el caro), con clave de sonda para no ocupar la real; `full` levanta el job entero en una sola pasada (y es
lo único que necesita el secreto de licencia).

El **2026-09-20 se ejercitó `unity: full` de verdad** (secreto de licencia
cargado, dispatch lanzado) y el resultado fue un **alcance acotado, no un
cierre**: la instalación del editor se arregló por el camino —`unity install`
resuelve contra un catálogo curado de una sola versión por línea, así que una
versión que sale del catálogo hay que instalarla con el `--changeset` que el
propio `ProjectVersion.txt` ya fija—, las librerías del editor también, y la
activación **falló con precisión**: con licencia Personal no hay vía en un
runner efímero. Dos pruebas independientes: `--personal --accept-eula` pide
sesión («Sign in with unity auth login»), y el `.xml` del Hub no es un ULF
(el cargador lo rechaza) y lleva cinco `Legacy.MachineBinding1..5` —hash y MAC
de esa máquina—, así que subirlo en base64 no podía funcionar. Salidas: (a)
`UNITY_RUNNER` apuntando el job a un **self-hosted** con editor y licencia del
Hub (implementado; sin ejercitar porque no hay runner registrado), (b) un
asiento de pago, o (c) la **puerta local**, que es la cobertura real de hoy:
`scripts/check-unity-compile.sh` —el MISMO script del job, **8 s** con la
Library caliente— corre en `.githooks/pre-push` (`scripts/install-hooks.sh`) y
bloquea **solo** con errores CS (exit 1); «no pude comprobar» avisa y deja
pasar. En Windows el mismo comando es `scripts/check-unity.bat`. El hook tiene
dos puertas y la primera es el **churn de assets** (`check-unity-assets.sh
--churn` sobre los commits que el push manda), que no necesita el editor: el
ruido de re-serialización se corta antes de salir de la máquina, no en CI.

**NEAT de extremo a extremo** — genomas estructurales (nodos/conexiones por
innovation), paridad bit a bit con MLP (`FromMlp`), operadores
estructurales (add-node, add-conn, toggle), especiation por parentesco,
arena fitness evaluando grafos reales, pool v2 exportado/importado con
re-innovación canónica, grafo visible por canal F en el inspector. Pool
sembrado: `pretrain-neat.antgenome` (64 genomas, tope arena 1064.9, 11
especies). El jugador puede entrenar con `--mode pretrain --neat` y
sembrar el resultado.

**Tres especies jugables** — Lasius (forrajera), Atta (cortadora con
hongo), Eciton (legionaria con combate). Transferencia cross-especie
validada: warm-v2 funciona en cuerpo Atta y Eciton. Cada especie tiene su
pin CI y su partida canónica.

**Presentación Unity** — escena bootstrapeada en un comando:
terreno terroso, hormigas visibles y orientadas, feromonas por
colonia/capa (selector F/G), tarjetas con gráfica de reserva, modal de
importación, vida útil real de fundadoras (240 s), velocidad de replay
+/= (1→2→4×) y la banda de aprendizaje por tarjeta (curva + cobertura). La
escena se regeneró con ella el 2026-09-17 (`53eda0b`).

**Fuente y sprites propios** (2026-10-06) — el HUD ya no pide prestada ninguna
fuente al motor. El bootstrapper **escribe** su tipografía (`TtfWriter`, un
escritor sfnt puro y determinista: fecha fija en `head` y checksum
`0xB1B0AFBA`) a partir de la tabla de glifos de `GameFontModel`, la importa y
la asigna a los 32 textos de la escena de juego y a los 8 del multi-visor
(`Assets/Fonts/AntSim.ttf`, 142 glifos, 72 504 B). Cubre el castellano
completo con acentos compuestos y los **36 signos que el HUD usa de verdad**
—«·», «─», «○ ◐ ●», «→», «↻», «≈», «⏸», «−», «■ ▶»…—, más tres iconos de uso
privado (U+E000–U+E002: hormiga, carga, huevo) que sustituyen el texto de las
líneas de cría y de flujo de las tarjetas. Desde F5.1ter los tres van en un run
`<size=28>` —el DOBLE del cuerpo, que es de 14 px— porque a 14 px medían 5-14 px
y se leían como manchas: el run escala el glifo sin mover el avance del texto,
que es de donde sale el ancho de la tarjeta (subir el cuerpo ENTERO habría
pedido 880×536 y la segunda tarjeta se salía de un canvas de 1080 de alto). La
misma medida destapó un defecto de layout PREVIO: `Panel()` elegía la esquina
por el signo del ancla y luego aplicaba `Mathf.Abs` a la posición, así que las
tarjetas quedaban ancladas 71 unidades POR ENCIMA del borde superior —medido con
la sonda, `screen=[16,1111..448,1374]` con pantalla de 1303: la tarjeta 0 medio
fuera y la 1 entera—. Corregido: las dos quedan enteras en pantalla. Y los actores dejan de ser
primitivas del motor: cápsula y esfera son ahora mallas propias
(`AntMesh`/`ItemMesh`/`LeafMesh` desde `ActorMeshModel`), construidas en la
MISMA caja de 2 u × 1 u para no tocar la escala ni la orientación del
presenter. Las hojas dejan de compartir malla con la carga, así que la
mordida se dibuja aparte. Regenerar dos veces deja el `.ttf` y las mallas con
idéntico md5; la escena sí cambia de bytes (renumeración de anclas `&N`) y por
eso va por el verificador de equivalencia de assets.

**Y los iconos se COMPARAN, no se suponen** (2026-10-07) — la legibilidad de los
tres iconos se venía comprobando sobre una muestra del `.ttf` renderizada
aparte, y eso prueba que el GLIFO es legible, no que la tarjeta lo pinte así.
Ahora el círculo se cierra con píxeles del Play pass: `ProbeHudIconShot` recorta
la tarjeta del frame en vivo y `scripts/playpass-icons.py` busca la FORMA de
cada glifo dentro del recorte (correlación cruzada normalizada sobre la tinta,
que no depende del brillo del panel). MEDIDO: el icono mide **2,00×** el cuerpo
(run 28 sobre cuerpo 14, leído del propio generador) y cada uno se clasifica
como SU glifo —correlación **0,51 · 0,57 · 0,81** y silueta IoU **0,79 · 0,85 ·
0,90** (hormiga, carga, huevo) frente a la referencia rasterizada al tamaño
EFECTIVO del icono— mientras que en ese mismo sitio los otros dos glifos puntúan
**≤0,22**. La correlación más baja es la de la hormiga (0,51) y tiene explicación
medida: sus patas de ~1 px son lo que más cambia entre el rasterizador de Unity y
el de FreeType, y por eso el instrumento publica además el IoU de siluetas, que
sí sobrevive al cambio de rasterizador (0,79). El
instrumento trae `--selftest`
(verifica la métrica sin capturas: una forma distinta tiene que puntuar bajo y
una ventana plana, 0,00 — antes puntuaba 0,9 y hacía pasar cualquier cosa).

**Y la referencia se redondeaba a píxel entero: escala disfrazada de forma**
(2026-10-08). El paso de iconos salió en ROJO por la hormiga (0,32 < 0,35) y no
era la hormiga: era la referencia. Con el canvas al 91,1 % el icono vivo mide
28 × 0,911 = **25,508 px**, pero `int(round(…))` la rasterizaba a **26 px**, un
1,9 % más grande — y en un glifo de patas de ~1 px un 1,9 % de escala no es un
matiz, es otra forma. MEDIDO sobre los mismos recortes, con la misma ventana:
contra la referencia a 26 px la hormiga puntúa 0,32 y contra la de 25,508 px
(supermuestreo 8× y reducción LANCZOS) **0,51**; el huevo sube de 0,63 a 0,81 y
la carga de 0,37 a 0,57. No es un defecto del HUD: la caja de tinta viva de la
hormiga medida en su ventana es 16×24 px y la referencia al tamaño corregido
(25,508 px) da 16×25, mientras la de 26 px da 17×26 — el icono se pinta al tamaño
fraccionario y era la REFERENCIA la que estaba crecida. Arreglado en el instrumento: la referencia se
rasteriza al tamaño FRACCIONARIO real, no a su redondeo (el hinting de FreeType
ajusta el AVANCE al píxel entero, pero el contorno se rasteriza al tamaño
fraccionario con antialiased — igual que hace el motor). **El mínimo NO se tocó**:
bajarlo a ~0,27 habría dejado solo 0,02 de margen contra un glifo EQUIVOCADO
(medido con el banco sintético del paso: con la hormiga sustituida por un huevo
el glifo ajeno puntúa 0,25 y el propio 0,09) — es decir, habría aflojado justo la
puerta que existe para cazar ese caso.
El recorte se respalda con una comprobación de PROCEDENCIA —tiene que ser un
trozo literal del frame en vivo, con la fila del PNG contada al revés que la
coordenada de pantalla—; la búsqueda libre por el frame pasó a ser informativa
porque, con la métrica honesta, el glifo es una aguja en el pajar de tinta del
terreno y dejaba falsos negativos.

**Y el veredicto CUENTA: los iconos son un PASO del Play pass** (2026-10-07). El
comparador existía y daba 0 fallos, pero se ejecutaba a mano: un icono roto en el
HUD pasaba el pass en verde. Ahora `playpass-live.sh` corre la clasificación
sobre los mismos recortes del frame en vivo y sale ≠ 0 con el glifo y sus números
si alguno no se clasifica como el suyo — o si **no se pudo medir** (la sonda no
guardó el recorte, o ningún python del PATH tiene Pillow), porque un paso que no
corre no puede contar como verde. Es un veredicto propio, separado del de
aspecto, así que el fallo no se confunde con un texto que no cabe; va ANTES del
bloque 4 a propósito, que reconstruye la escena y volvería a escribir el HUD.
La evidencia en píxeles —el frame vivo, el PNG de cada tarjeta y la hoja de
comparación que genera el propio comparador— se **conserva en
`artifacts/playpass-hud-icons/`**: la sonda los escribe en `Temp/`, que Unity
barre al cerrar el editor, y un paso cuyo objeto de medida desaparece con la
sesión deja el veredicto sin nada que mirar después. Con eso el mismo análisis se
repite sin editor con `scripts/playpass-live.sh --analyze-icons` (y
`--icons-crops` para apuntar a otros recortes), y el cableado —no la métrica, que
trae su propio `--selftest`— se verifica en `--selftest`: comparador en verde
pasa, glifo que no se clasifica falla, sin volcado no pasa y los PNG del HUD se
conservan (sin ninguno que copiar, la conservación falla).

**Tres textos desbordaban su panel — y el ajuste ahora es un TEST** (2026-10-07).
El Play pass medía tres textos fuera de su caja: la barra de PISTAS
(`rect=1100x26 pref=1325x13`) y las DOS líneas del panel de aprendizaje de cada
tarjeta (`rect=406x34 pref=648x13` y `pref=513x13`). La causa de las pistas es
medible: su caja (1100) se dimensionó cuando el HUD usaba la tipografía de uGUI y
siguió igual al pasar a la fuente propia, que a 14 px reserva **7 px por
carácter** (500/1000 em, redondeo del hinting incluido). Arreglado así: el texto
y su caja viven en `HudElementLayoutModel` (pistas a **1360**, 194 caracteres) y
la banda de aprendizaje tiene caja de contrato en el mismo modelo (406×34); las
dos líneas del panel se recortaron a lo que CABE —se cayeron los nacimientos
(que la tarjeta ya pinta en su ventana de 1 s), el radio máximo y la media de la
élite— porque 57 caracteres por línea son el techo duro de 406 px. Lo importante
no es el recorte sino la PUERTA: `TextosFijosDelHud_CabenEnLaCajaQueLaEscenaLesDa`
comprueba en headless, con la misma cuenta del `TextGenerator` y con el PEOR caso
de números (generación de 2 dígitos, cohorte y bajas al tope, el mundo entero
pisado), que pistas y las dos líneas caben — antes esto solo lo veía una captura y
el literal vivía en el bootstrapper, que no se compila en la suite. MEDIDO tras el
cambio: `hud.overflow=3` → **0** y el paso de aspecto en verde. La regeneración de
la escena que hizo falta para las pistas restauró además la textura de reposo de
`PheromoneMat` (su `_BaseMap` había quedado en `{fileID: 0}` tras una sesión del
editor, con el guardarraíl `PheromoneMaterialGuardTests` en rojo: ahora verde).

**El bloque 4 no podía ver su propia alerta** (2026-10-07). El paso C espera a que
salga una alerta ANCLADA y la sonda tarda ~1 s por vuelta. MEDIDO a la velocidad
por defecto (6): la partida entera (7200 ticks) se consumía antes de que C
empezara a sondear — su primera muestra salía ya en `tick=7200` con `toasts=0` —,
así que el gate era inalcanzable por cronómetro. La velocidad por defecto del
bloque 4 pasa a **2**, donde el toast vive ~3 s de reloj y C lo cazó en dos
muestras seguidas (tick 335 y 435). El defecto APARTE —la alerta sin ancla— está
arreglado (2026-10-07): en la partida canónica la ÚNICA alerta emitida es
`first-unload` y el Core la emitía con `x=-1,y=-1`, así que el salto de cámara
(`J`, Alt+click) no tenía a qué saltar. Ahora el `RelayTracker` guarda la posición
del primer `Unload` (`FirstUnloadX/Y`) y el `AlertDeriver` ancla el hito ahí —la
salida que ya estaba escrita en el comentario del propio script—. Los pines de
hash NO se mueven: el `AlertDeriver` es observador puro y el hash del stream es el
del MUNDO (`sim.HashLine()`), así que la alerta no entra en él.

**El avance del icono: 720 declarado, 643 es la TINTA** (2026-10-07) — la duda
«uGUI reserva 643 unidades donde el `.ttf` declara 720» tiene una respuesta
medida en dos partes. Primero, QUÉ ES 643: la caja de tinta del icono (0,643 em
= 9 px a 14 px y 18 px a 28 px), no un avance; el avance declarado es 720
unidades (0,72 em) y la tinta cabe dentro. Segundo, POR QUÉ EL RESERVADO NO ES
EL DECLARADO: Unity carga el glifo con el hinting de FreeType, que ajusta el
avance al PÍXEL ENTERO. Medido cargando la fuente por el motor, sin hinting el
icono da 719,76-719,87 unidades a 14, 15, 28, 29 y 31 px (0,72 em, clavado) y
una letra 500,00; con hinting —lo que pinta el HUD— reserva 10 px a 14, 11 a
15, 20 a 28, 21 a 29 y 22 a 31 (714, 733, 714, 724 y 710 unidades), y una LETRA
pasa a 8 px a 15 (500 → 533 unidades, un 6,7 % más). No lo arregla el ajuste
`shouldRoundAdvanceValue` del importador: probado en sus dos estados, los
avances son idénticos. Y en el HUD en vivo (canvas al 98,3 %, cara de 27,5 px)
el generador reserva 19 px, el 95,9 % del avance declarado —el resto lo come el
hinting—. Lo que sí se arregla es el MODELO: la cuenta del ancho del HUD
(`HudLayoutTests.HintedAdvance`) reserva ahora como reserva uGUI —avance a
tamaño del run redondeado al píxel— en vez de multiplicar la em a pelo, que a
15 px se quedaba corta un 7 % por letra; y la sonda publica `units`/`decl`/`ofDecl`
normalizando por el tamaño del RUN (antes lo hacía por el cuerpo del `Text` y
publicaba el doble de unidades justo en los iconos, que es lo que hacía parecer
que la fuente mentía). Un test nuevo ancla los nueve valores medidos.
631/631 tests (18 casos de `GameFontTests`/`ActorMeshTests`, 2 de la medición de
los iconos, 1 del avance reservado y 2 del contrato de los assets generados).

**Los assets generados tienen CONTRATO con sus modelos** (2026-10-07) — los
modelos son la fuente de verdad, pero el HUD y los actores se dibujan con los
ARTEFACTOS que el repo versiona: el `.ttf` y las quince mallas `*Mesh*.asset`.
Que los dos lados no se separen dependía de abrir el editor y regenerar, y no lo
comprobaba nadie: la suite miraba los modelos (que están bien) y el Play pass
miraba lo pintado (con el artefacto viejo, así que también «bien»).
`GeneratedAssetContractTests` cierra ese hueco en headless: la fuente se compara
BYTE A BYTE contra `TtfWriter.BuildFromModel` —legítimo porque el escritor sella
la fecha, así que la salida es determinista— y cada malla VÉRTICE A VÉRTICE
(posiciones decodificadas del blob `_typelessdata` de la malla en YAML, con el
paso entre vértices deducido del propio blob) contra
`ActorMeshModel.Build(GameSpriteModel.Build(…))`; las variantes `_V*` del
multi-visor se DESCUBREN en disco, así que una vista más entra sola. Que el
chequeo mira de verdad se comprobó mutando: con el exponente de un vértice de
`AntMesh_V3` cambiado falla nombrando el vértice (`x` del vértice 0: 1,16e9
frente a 0,27) y con un byte del `head` del `.ttf` cambiado falla nombrando el
byte (71036).

**Los avances del fichero, LEÍDOS de `hmtx`** (2026-10-07) — el contrato de assets
compara BYTES, y eso dice «el `.ttf` versionado es el que genera el modelo», pero
no dice QUÉ pone: un `hmtx` con el avance torcido seguiría siendo idéntico a sí
mismo, byte a byte, mientras el escritor y el modelo se torcieran JUNTOS.
`GameFontTests` cierra ese lado con su propio lector de sfnt: recorre
`GameFontModel.Codepoints`, llega al glifo por la `cmap` (formato 4, el mismo
camino que usa el juego) y exige que el avance que trae `hmtx` sea el del modelo
—500 unidades para toda letra y 720 para los tres iconos PUA—, no el que el modelo
diría si se le preguntara. Antes de mirar nada se comprueban las tres condiciones
que el lector da por supuestas (una entrada de 4 B por glifo, `numberOfHMetrics ==
numGlyphs`, sin la compresión de cola del formato) y al final que hay DOS avances
distintos en el fichero: si todos midieran igual, la comparación pasaría por
casualidad y no probaría nada. Y hay comprobación negativa del PROPIO lector: se
genera un `.ttf` con la «A» a 623 unidades y se exige que el lector lo VEA,
mientras el modelo sigue diciendo 500 — un lector que devolviera
`GameFontModel.AdvanceOf` estaría comparando el modelo consigo mismo. Sensibilidad
medida mutando el escritor (`hmtx` con +1 unidad): fallan los dos tests nuevos, y
sólo ellos. Todo headless y sin editor, como el resto del contrato.

**Los MATERIALES y las TEXTURAS también tienen contrato** (2026-10-07) — el contrato
de assets cubría la fuente y las mallas; el resto de artefactos del bootstrapper
—los 76 `.mat` y las 10 texturas que PINTAN el mundo— seguían sin modelo: su color
vivía como literal dentro de los montadores del editor (que no compilan en la
suite) y escrito DOS veces, una por escena. Ahora la paleta, los tres tipos de
material (plano con instancing, suelo con rejilla, capa transparente), la rejilla
—lado, colores y qué píxel es línea— y la textura de reposo viven en
`GameMaterialModel` (puro), los DOS montadores los APLICAN —el multi-visor ya no
tiene paleta propia: pide el mismo material con el sufijo de la vista— y
`GeneratedMaterialContractTests` compara el artefacto: biyección nombre a nombre
(ni un `.mat` de más ni uno de menos), color LEÍDO del fichero en `_BaseColor` y
`_Color` (Unity guarda el valor autorado, sin pasarlo a lineal: medido), la receta
entera por tipo —instancing en los planos; `_BaseMap` con las 24 repeticiones del
modelo en el suelo; `_SURFACE_TYPE_TRANSPARENT`, `_SrcBlend`/`_DstBlend`, `_ZWrite`
y `renderQueue` 3000 en la capa de feromonas— y los PÍXELES de las texturas
decodificados del blob: la rejilla píxel a píxel y el reposo blanco con alpha 0
(alpha 255 volvería a tapar el tablero, el defecto del Play pass de F5.1). Lleva
su comprobación negativa: con el color de un `.mat` cambiado a mano y el alpha de
la textura a 255, los lectores lo VEN —si devolvieran el modelo, el contrato
estaría comparando el modelo consigo mismo—. De regalo, el acento de colonia sale
del modelo, así que el disco del nido y la barra de su tarjeta son el mismo color
por construcción y no por coincidencia. Compila en Unity (0 errores, 0 avisos).

**Unity compila limpio** — 0 errores CS, 0 avisos CS en Unity 6000.6.0f1.
Canal F NEAT en el inspector (`ActivationViewModel.RenderNeat`
self-contained, sin dependencias de Core), pool NEAT en el picker
(`PoolPresets`), tarjeta de linaje con forma del cerebro (`n/h/c`).

**Multi-visor** (F5.1bis) — hasta 4 simulaciones EN VIVO en paralelo
(capas Sim0–3, tarjetas compactas por vista con su propio semáforo).

**Ítems compuestos + hongo** (F5.2a) — hojas con `CutsLeft`, segunda
reserva de hongo, digestión proporcional, eventos `LeafCut`/`LeafDepleted`
/`FungusFed`/`FungusDigested`.

**Saqueo + combate** (F5.2b) — Eciton roba stock (`ContactRadius 40`,
`StrikeDamage 2.5`, `StealPerStrike 5.0`), balance V6 calibrado con
sondas, canal 12 reconvertido a sensor de presa, eventos 17–19, bloque
`raids` en canal C.

## Lo que queda (en orden de dependencia)

<!-- cifras-historicas: registro de rodajas — cada cifra es la del cierre de SU rodaja, no la de hoy. `scripts/check-doc-figures.sh` ignora lo que va entre estas dos marcas; las cifras de HOY viven en la cabecera y en la tabla de arriba. -->

1. **F5.3 — registro de rodajas (TODAS HECHAS; sub-fase cerrada con `v0.8.0`).** Lo
   que queda FUERA de la sub-fase: la deuda menor de F5.1 (punto 2), el gate de Unity
   en CI —que sigue dormido tras la variable `UNITY_CI`— y una decisión de dependencias: los bloques
   3/4/5 del Play pass del editor necesitan `com.unity.pipeline`, que salió del
   manifest en la poda de paquetes (`unity command editor_status` responde «No
   Pipeline instance found»). La verificación visual end-to-end la cubre ahora la
   sonda del PLAYER, que corre la misma puerta de píxeles sobre frames reales.
   **F5.3 rodaja 1 — HECHA**: `AntSoA` (parallel arrays), infraestructura lista.
   **F5.3 rodaja 2 — HECHO (2026-09-17)**: el mundo COMPACTA las adultas muertas al final de cada `Step` (O(n), `BankAndCompactDeadAnts`). Claves del diseño: el fitness de por vida de las muertas va a un BANCO por colonia (el pool ya cobró en el tick de la muerte) que `HashLine`, el save y la arena suman para producir lo mismo que la lista con cadáveres; `HashLine` y los checkpoints solo describen vivas; los registros de shaping de la arena van claveados por `Id` (la posición en la lista ya no es identidad). Pines: solo invasión y NEAT se movieron (hay muertes en sus ventanas de hash); stream/replay/Atta son byte-idénticos. La fila del canal A desaparece en el tick de la muerte (contrato actualizado en `UnityStreamContractTests`): la muerte la captura el canal B.
   **F5.3 rodaja 2bis — HECHO (2026-09-17): huella CHC (4ª capa) y tropotaxis en ratio.**
   La señal NEGATIVA que faltaba, inspirada en [`ideas-externas.md`](ideas-externas.md) §1 (anthill):
   `PheromoneKind.Footprint` con depósito pasivo `Q_max_footprint·v·dt` (por unidad RECORRIDA, sin
   gasto de energía: es cutícula que se roza, no una decisión del cerebro), τ½ 240 s y difusión 0.06;
   el giro es un REFLEJO periférico `g·(der−izq)/(1+s·max)` — RATIO, no canal de sensor, así que los
   19 canales y los `.antgenome` no cambian. Calibrado con sonda: 0.08 u/u · g 8 · s 4 ⇒ ~0.33 rad/s
   (≈15 % de Ω_max). Evidencia del diseño: con 1 colonia a 7200 ticks el 5 % de celdas más pisadas
   pasa de concentrar el **49 % del tráfico al 23 %** y el máximo por celda baja de 0.98 a 0.83, sin
   coste económico (stock 41 → 46). Control A/B `--footprint 0` (depósito sí, respuesta no). Canal E:
   `k=4` y selector violeta en el HUD. Checkpoint **v5** (+1 capa; los v2–v4 cargan con huella a cero).
   Los 6 pines regenerados (el mundo cambia a propósito) y **443/443 tests** (463 con el benchmark de políticas y el panel de aprendizaje).
   **F5.3 rodaja 2ter — HECHO (2026-09-17): benchmark de políticas (idea nº 2 de `ideas-externas`).**
   La vara EXTERNA que faltaba: aleatoria vs scripted vs evolucionada en la misma arena, mismas
   semillas y **política congelada** (`WorldSim.ForcePolicy` — la descendencia hereda el mismo cerebro
   y no realimenta el pool, así que se mide el cerebro y no la evolución de la prueba). Nuevo
   `ScriptedBrain` (reglas explícitas: brújula + visión + rastro + vuelta al centro en la pared),
   `PolicyBenchmark` y `--mode bench`. Resultados (10 semillas, 2 bandas, `docs/politicas-benchmark.md`):
   la aleatoria está **muerta por construcción** (0 pickups en 20 partidas) mientras scripted y
   evolucionada empatan (diferencias ≤ 2 SE, y el orden se invierte con `--trials`); evolucionar
   durante la prueba no ayuda a estos horizontes. El hook es inerte sin política fijada (test de
   hash) ⇒ los 6 pines siguen valiendo.
   **F5.3 rodaja 2quater — HECHO (2026-09-17): curva de aprendizaje y cobertura en el canal C + HUD (idea nº 5 de `ideas-externas`).**
   `LearningTracker` en Core: por colonia, curva de fitness por GENERACIÓN (generación = nacimientos/64,
   la capacidad de la élite; y el fitness de una cohorte es el de TODAS sus hormigas, contabilizadas por
   deltas para no medir supervivencia) y cobertura del mundo leída de la huella CHC (celdas pisadas +
   radio máximo). Dos bloques nuevos en el canal C (`learning`, `fitcurve`) cada 120 ticks; `LearningPanelModel`
   + `LearningPanelBehaviour` en el HUD (curva por generación autoescalada, color por nivel de cobertura y
   dos líneas de datos en la tarjeta, que crece a 268 px). Telemetría pura: test de hash y 6 pines verdes;
   Unity compila en batch sin avisos. Evidencia: en 9000 ticks la colonia sembrada con warm-v2 conoce
   **2148/9216 celdas (23.3 %)** con fitness medio 4.7, y la fría 728 (7.9 %) con 1.8 — la diferencia de
   aprendizaje se ve en el HUD. **463/463 tests**.
   **F5.3 rodaja 3 — HECHO (2026-09-18): LOD de difusión EXACTO e instancing ([`fase5-3-escala.md`](fase5-3-escala.md)).**
   El LOD no aproxima: la difusión del proyecto nunca llena una celda nula (régimen original, no de esta
   rodaja), así que las nulas no pueden cambiar y visitarlas era trabajo puro. `PheromoneLayer` mantiene un
   contador de celdas no nulas por BLOQUE — **de 8×8 desde la rodaja 3bis**, que es el lado medido en
   [`fase5-3-escala.md`](fase5-3-escala.md) §4.6: el 16 histórico no ganaba en ningún mundo — y arma con ellos las regiones a visitar; el checkpoint
   DERIVA el soporte de los valores (`RebuildSupport`). La equivalencia se demuestra celda a celda contra una
   capa gemela con `LodEnabled = false` (implementación de referencia) sobre 400 operaciones aleatorias — el
   primer intento del test comparaba dos secuencias distintas y hay una nota en él sobre eso. Medido: 7.8–8.7 %
   del grid visitado en un mundo forrajeado (2.0 % en uno joven, 2.0 % a 512²) con el bloque 8 — **el ahorro se
   encoge según se extiende el rastro** (bloques, no celdas). Y en el render, el presenter agrupa por material y envía lotes de 1023
   instancias, con caída a una llamada por objeto si la plataforma no soporta instancing; el troceo es un modelo
   puro del esqueleto Unity (el Core no viaja a la vista) con contador en vivo (`LastDrawCalls`, presupuesto 32).
   Exit de F5.3 (grid 256, 6000 ticks, warm-v2, semilla 42): 1/2/4/8 colonias ⇒
   0.06/0.11/0.23/0.47 ms por tick (**2.8 % del frame a 60 fps con 8 colonias**), techo de velocidad ×596/×292/×144/×71.
   Y el lado de la VISTA, medido con `scripts/perf-scene.sh` (4 vistas × 2 colonias = 8 colonias en pantalla,
   grid 256) en sus **dos modos**: en batch **0.51 ms por frame** y, en el Play pass con ventana —bucle y presentación
   reales—, **2.44 ms por frame** (15 % del presupuesto a 60 fps), con **19 llamadas de dibujo** (todas instanciadas, 0
   de respaldo) y 292 hormigas + 684 ítems en pantalla en los dos casos. La sonda destapó **dos defectos reales**: `DrawMeshInstanced` LANZA si el
   material no tiene `enableInstancing` (los del bootstrapper no lo tenían: el tablero se quedaba SIN HORMIGAS, con una
   excepción por frame y 0 draw calls) — arreglado en los dos bootstrappers y con activación defensiva + contadores en
   el presenter; y la configuración de la sonda en statics se perdía en el reload de Play (movida a `SessionState`).
   **Los 6 pines NO se movieron** (el LOD es exacto: el mundo es byte a byte el mismo) y el proyecto Unity compila en
   batch con 0 errores y 0 avisos. **496/496 tests.**
   **F5.3 rodaja 3ter — HECHO (2026-09-19): el criterio de salida, medido en un BUILD de jugador.**
   `scripts/player-perf.sh` construye el player (escena del multi-visor, 4 vistas × 2 colonias, grid 256) y lo ejecuta
   con una sonda de runtime que muestrea frames/s y pasa la puerta de PÍXELES por vista. Medido: **59,99 fps de mediana
   con el refresco del monitor a 59,997 Hz y el vsync del proyecto aplicado** (peor muestra 55,6; 19 llamadas de dibujo
   todas instanciadas; 292 hormigas y 684 ítems en pantalla; tick 12 000) y **puerta verde en las cuatro vistas**
   (antPx 132–160). El primer build destapó **tres defectos reales**: el juego **no compilaba como player**
   (`DragAndDrop` de UnityEditor sin guarda → ocho CS0103; ahora `#if UNITY_EDITOR`), el módulo
   `com.unity.modules.screencapture` faltaba del manifest —la sonda del Play pass del editor llevaba sin compilar desde
   la poda— y la puerta medía con `AntMaterial` cuando el multi-visor pinta con `ColonyAntMaterials` (antPx=0 con 292
   hormigas dibujadas). La puerta es ahora UNA implementación pura (`FrameGate`) con 11 tests headless, compartida por
   el editor y el player. **512/512 tests · los 6 pines verdes sin regenerarse.** Evidencia: `artifacts/perf-player.json`.
   **F5.3 rodaja 3quater — HECHO (2026-09-19): el COSTE por frame dentro del build (5ª pieza del criterio).**
   El framerate presentado lo pone la pantalla: con el vsync entregando al refresco, el coste del frame queda tapado.
   `scripts/player-perf.sh --cpu` apaga el vsync, construye el player con los estadísticos de tiempos de frame
   (`enableFrameTimingStats`, que `PlayerBuild` enciende sólo para esa corrida y restaura al terminar) y la sonda
   cronometra el `FrameTimingManager` frame a frame. Medido con el mismo build y el mismo régimen que §4.7 (4 vistas ×
   2 colonias, grid 256, boost ×10, 292 hormigas + 684 ítems, tick 12 000): **1,294 ms de CPU por frame** (hilo
   principal 1,272 · hilo de render 0,277 · espera en Present 0,003) y **0,127 ms de GPU**, con **683,7 fps de techo sin
   vsync** (1,463 ms/frame producidos) = **7,8 % del presupuesto de 60 fps** y ×12,9 de margen; puerta de píxeles verde
   en las 4 vistas. El resumen es un modelo PURO (`FrameCost`, sin UnityEngine) con **12 tests headless**. **526/526
   tests · los 6 pines verdes sin regenerarse.** Evidencia: `artifacts/perf-player-cpu.json`.
   **F5.3 rodaja 3quinquies — HECHO (2026-09-19): el coste del SISTEMA COMPLETO (6ª pieza del criterio).**
   El número de la pieza anterior es el del **player**; el mundo lo simula el **CLI en otro proceso**, y esa pata no
   aparece en ningún tiempo de frame. `scripts/player-perf.sh --system` la mide: vsync apagado, calentamiento de 2 s
   (para que la fase de simulación caiga DENTRO de la ventana) y la CPU del proceso hijo leída en el mismo instante de
   muestreo. La primera corrida de este apartado subió el horizonte a 200 000 ticks «para que el CLI trabajara toda la
   ventana» y dio **cero datos** — este CLI simula el horizonte entero y escribe el stream al terminar—; la sonda lo
   detectó y suspendió con salida **10** en vez de publicar un verde vacío. Y al medir bien se destapó el defecto de
   fondo: **la CPU del CLI y la entrega de ticks están desacopladas** (6 s de los 9,7 s de CPU del hijo ya gastados a los
   2 s, cuando el player no había recibido ni un tick), así que emparejarlas por ventana daba **0,016 ms/tick** en vez de
   los 0,2 reales; el precio del tick es el **agregado de la vida entera del hijo** y la corrida exige que el CLI
   **termine su horizonte dentro de la ventana**. Medido (mismo build y régimen que §4.7/§4.8, 292 hormigas + 684 ítems,
   tick 12 000): **pata del player 1,116 ms/frame = 6,7 % del presupuesto** (margen ×14,9) y **pata del mundo 0,201
   ms/tick** (9 671,875 ms de CPU del CLI / 48 000 ticks) → con **200 ticks/frame** al reloj del juego, **41,415
   ms/frame agregados = 2,485 núcleos**, con el mundo siendo el **97,3 % de la CPU del sistema**; 9 ventanas simulando ·
   22 con el mundo en pantalla · 2 de solape. El modelo puro gana **10 tests headless** (`SystemCost`). **536/536 tests ·
   los 6 pines verdes sin regenerarse.** Evidencia: `artifacts/perf-player-system.json`.
   **Y cuánto juego cabe — HECHO (2026-09-19):** el barrido del precio del tick con **1, 2, 4 y 8 colonias por vista**
   (`bash scripts/system-sweep.sh`) mide **1,927 · 2,496 · 3,443 · 5,285 núcleos** al reloj del juego: doblar colonias
   **no** dobla el coste (×1,30 · ×1,38 · ×1,53, sublineal, porque **0,1203 ms/tick son fijos** —la difusión de las
   cuatro rejillas— y solo 0,00983 ms/tick siguen a cada colonia). El **coste fijo, medido y no extrapolado**: con 1
   colonia por vista (1,927 núcleos) el fijo son **1,507 núcleos** —1,443 del mundo + 0,064 de la vista—, el 78 % del
   sistema, y el ajuste sin ese punto lo predecía en 2,039 (−5,5 %): la incertidumbre de publicar un intercepto de tres
   puntos. La **amortización** explica la curva por colonia: 0,1187 + 1,507/n. La vista escala mejor que el mundo (1,064
   → 1,488 ms/frame) y con **32 colonias (1 075 hormigas)** el build sigue a 609 fps con la puerta verde en las cuatro
   vistas. Leído en presupuesto: **8 colonias en los 2,5 núcleos de §4.9**, 16 en 3,4, 32 en 5,3 (medidos). Evidencia:
   `artifacts/system-c{1,2,4,8}.json` + `artifacts/system-sweep.txt` (`docs/fase5-3-escala.md` §4.10).
   **Artefacto commiteado por detrás del generador — CORREGIDO (2026-09-19):** cuatro de los cinco materiales de
   feromonas tenían `_BaseMap` **sin textura** (`fileID: 0`), que con el material transparente y `_BaseColor` blanco
   significa **quad blanco OPACO tapando el tablero** mientras no llega ningún frame de feromonas (el defecto del Play
   pass de F5.1 por la puerta de atrás). El generador sí escribía la referencia: tres generaciones independientes —una
   de ellas borrando la textura de reposo para forzarla en la misma pasada— dan el material canónicamente idéntico. Los
   cuatro materiales quedan con su referencia (7 líneas) y **2 tests headless** (`PheromoneMaterialGuardTests`, en rojo
   sobre los materiales de HEAD antes del arreglo) impiden que vuelva en silencio: referencia presente, guía existente y
   textura 1×1 RGBA32 `ffffff00` (invisible).
2. **Deuda menor de F5.1** (no bloquea): la serie de DESCARGAS en la gráfica
   de reserva (hoy la sparkline guarda `Stock`; la serie de drops aún no se
   grafica).
   Ya HECHO, para no contarlo más como deuda: la **fuente y los sprites
   propios** (el `AntSim.ttf` que escribe el bootstrapper y las mallas de
   hormiga/carga/hoja de `ActorMeshModel`), el **chip de estado por runway**
   (canal A + `RunwayVerdict`/`RunwayChipModel`, en la línea del título de la
   tarjeta y en forma corta en el multi-visor), el drag & drop de `.antgenome`
   (`ImportDialogBehaviour`, `#if UNITY_EDITOR` — esa API es del editor y su guarda
   es lo que compila el player) y las hojas con mordiscos (`CutsInitial - CutsLeft`
   restado por el presenter).
3. **Fase 6 — 2D → 3D**: explícitamente fuera de Fase 5; el adaptador
   cambia, los contratos no.

<!-- /cifras-historicas -->

## Riesgos abiertos

| Riesgo | Estado |
|---|---|
| NEAT estanca la evolución | mitigado — fallback de topología fija; meritocracia de arena medida |
| `error CS` de MonoBehaviours | **abierto, con el alcance ya acotado (F5.3, 2026-09-20)** — el job existe y está corregido, pero dormido, y el 2026-09-20 se **ejercitó `unity: full` con un secreto de licencia real**: el camino **no cierra** con licencia Personal (Unity no la activa en un runner efímero: `--personal` pide sesión y el fichero del Hub está atado a la máquina — dos pruebas independientes en el comentario del job). Quedan tres salidas: (a) `UNITY_RUNNER` → self-hosted con el editor ya licenciado (implementado, **sin ejercitar**: no hay runner); (b) un asiento de pago (serial legacy o `.ulf` Enterprise/Industry); (c) la **puerta local**, que es la cobertura de HOY: `scripts/check-unity-compile.sh` (mismo script que el job, **8 s** con la Library caliente) corre en `.githooks/pre-push` y bloquea solo con errores CS de verdad |
| Feromonas costosas a escala | **mitigado (F5.3 rodajas 3 y 3bis)** — LOD exacto con bloque 8 medido: 7.8–8.7 % del grid visitado en un mundo forrajeado y la reconstrucción de la lista por debajo del 1 % del tick; el RLE ya resolvía el envío del canal E |
| Framerate real de Unity a N colonias | **cerrado (F5.3 rodajas 3 y 3ter)** — 8 colonias: 0.51 ms/frame en batch, **2.44 ms/frame con el editor en ventana** y, en un **build de jugador**, **59,99 fps con el refresco del monitor** (vsync aplicado) y 19 llamadas (todas instanciadas). El coste por frame DENTRO del build ya está medido (**1,294 ms de CPU, 7,8 % del presupuesto**, §4.8) y el del **sistema completo** también (**2,485 núcleos** al reloj del juego, §4.9), igual que su barrido por colonias (**1,927 · 2,496 · 3,443 · 5,285 núcleos** con 1, 2, 4 y 8 por vista, §4.10, con el fijo **medido** en 1,507 en vez de extrapolado). Lo que sigue sin medir: el precio del tick **por encima de 8 colonias por vista**, el **barrido de VISTAS** (con 4 fijas no se separa el fijo por mundo del fijo por montaje), los tiempos de GPU por otra vía y una segunda máquina |
| El juego compila como player | **cerrado (F5.3 rodaja 3ter)** — el primer build destapó CS0103 de `DragAndDrop` (API del editor sin guarda): el editor compilaba Assembly-CSharp con referencia a UnityEditor y el player no. `PlayerBuild` construye en 18 s y la sonda del player falla si la escena no pinta hormigas |
| Bug de Unity Search (Library fría en batch) | mitigado — fallback `PumpOneTick` en las sondas |
| Balance de especies | se calibrará con tests de balance contra el benchmark de pools |
| Pool Atta desde cero | **resuelto** — la sonda de transferencia validó warm-v2 en cuerpo Atta |
