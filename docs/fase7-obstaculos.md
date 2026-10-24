# F7 — Pre-entrenamiento con obstáculos, currículo adaptativo y cerebro que crece

Lo que faltaba para que la partida arrancase ENTRENADA en el laberinto que F6
monta. El mundo ya era el del juego; la política, no: el pool de referencia
(`pretrain-warm-v2`) se entrenó en campo abierto y gasta la partida aprendiendo a
rodear paredes. F7 entrena en headless con el terreno del juego, sube la
dificultad SOLO cuando la colonia demuestra que sabe volver al nido, ensancha el
cerebro cuando se estanca en el laberinto completo y deja un pool que forrajea en
LAS DOS superficies: el pool por defecto hace **102 descargas con `--maze` y 271
sin laberinto** en la partida canónica (el de referencia hace 39 y 242).

Sin `--obstacles` (y sin `--maze` en la partida) nada de esto se ejecuta: los 6
pines de CI y el camino clásico siguen byte a byte.

## 1. Terreno graduable (`AntSim.Core/World/MazeLayout.cs`)

`MazeLayout.Pruned(seed, cells, colonyCount, difficulty)` construye el mapa de
`Braided` con la MISMA semilla y le retira TRAMOS COMPLETOS de pared interior
—el segmento entre dos cruces, no celdas sueltas: una pared con un píxel de
menos es una trampa, y un tramo ausente es un obstáculo legible—.

- Una tirada del RNG por tramo, en orden fijo: `paredes(d1) ⊆ paredes(d2)` para
  `d1 < d2`. Subir la dificultad solo AÑADE obstáculos; nunca cambia los que la
  política ya conocía.
- `d = 0` ⇒ campo abierto (solo el anillo exterior); `d = 1` ⇒ el mapa del juego
  byte a byte. Como el mapa completo es conexo y aquí solo se retira, todo nivel
  intermedio sigue siendo conexo por construcción.
- Anillo exterior, plazas de los nidos y cruces huérfanos intactos: un cruce
  sobrevive solo si le queda algún tramo contiguo, así que a `d = 0` no queda un
  enrejado de postes.
- `SegmentClear(x0, y0, x1, y1)` — recorrido DDA sobre el MISMO bitmap que la
  colisión: ¿la línea recta entre dos puntos cruza alguna celda de pared? Es lo
  que convierte la distancia en visión: sin él el sensor de comida entregaba
  comida al otro lado del muro (ver §2).

## 2. Arena con obstáculos (`Training/ArenaEvaluator.cs`)

El evaluador acepta un `MazeLayout?`. Con terreno:

- corre el MISMO `WorldSim` (mismo `AntSenses`: los canales 11/12/13 ya leen el
  muro con las tres antenas) — no hay física paralela que pueda divergir del
  juego;
- el canal de comida visible exige **línea de visión**: con `maze` no nulo
  `AntSenses.Build` descarta los ítems cuyo segmento hormiga→ítem cruza una
  pared (`MazeLayout.SegmentClear`). Sin esa condición el sensor elegía el más
  cercano dentro de 60 u por distancia pura y en el laberinto el 83 % de lo
  «visible» estaba detrás de un muro: la política obedecía el vector, se clavaba
  en la pared y la colonia se extinguía con >100 ítems sin recoger. Sin
  laberinto la selección sigue siendo la clásica (distancia pura), así que
  ningún mundo sin terreno cambia;
- la comida nace con las reglas de la partida real: `ItemFits` (nada dentro de
  muro), ≥ `MazeLayout.ItemSpawnMinDistance` (80 u) del nido y densidad
  ×`ItemDensityFactor` (4,5);
- el tope del densado de exploración pasa a ser el radio útil del mundo (la
  banda de distancia de la etapa no aplica: el rodeo lo impone el terreno).

Fitness y densados no cambian: es la misma señal que el juego (recogida,
descarga, homing, carry-leg). Sin `maze` el resultado es idéntico al de siempre.

## 3. Currículo por competencia (`Training/ObstacleCurriculum.cs`)

La dificultad no sigue un calendario sino DESCARGAS reales de la mejor
evaluación de la generación — el ciclo completo del juego, que no se puede
farmear paseando (el fitness sí: lleva densados).

- Ventana de 3 generaciones; sube un peldaño (0,1) si las tres cierran
  ≥ `CompetenceUnloads` (3) descargas; baja si las tres se quedan a 0; entre
  ambos extremos no se mueve (histéresis: ni una generación mala hunde el
  terreno ni una buena lo devuelve).
- Estado en décimas ENTERAS: sumar 0,1 en coma flotante acumula error y la
  misma receta dejaría de dar el mismo archivo.
- El horizonte crece con el terreno: 1 800 ticks (fácil) → 2 700 → 3 600 (el
  laberinto completo). Las generaciones fáciles son baratas.

## 4. Escalera de capacidad (`Training/CapacityLadder.cs`)

Con el terreno en su tope y el mejor fitness plano (ventana de 30 generaciones,
mejora < 2 %), la capa oculta se ensancha un peldaño: 8 → 16 → 24. `MlpGenome.
Expand` copia los pesos existentes y deja las neuronas nuevas a cero, así que la
red expandida reproduce la salida anterior (la escalera no tira lo aprendido).
Fuera del terreno máximo la ventana no corre: crecer en campo abierto no es la
señal que se busca. Un warm-start más ancho que la escalera manda (continuar un
pool de 24 ocultas no puede empezar en 8: la escalera no tiene bajada).

## 5. Las dos competencias: dos regímenes clásicos, medidos

Entrenar solo en el laberinto OLVIDA el campo abierto (medido en la partida
canónica sin `--maze`: 242 → 40 descargas). La pregunta era qué evaluación
clásica hay que meter en el bucle, y la respuesta medida es que hay dos papeles
distintos:

- **Mantenimiento** (1 de cada N generaciones, banda y horizonte del currículo
  clásico: 200–260 u, 3 600 ticks). No deja que la política se vaya del régimen
  de forrajeo clásico mientras manda el laberinto. Es el que conserva el ciclo
  completo: sin él, 40 descargas; con él, 172.
- **Cola** (60 generaciones al final, en el régimen de la PARTIDA sin laberinto:
  comida uniforme en todo el mapa a ≥ 200 u del nido y horizonte de 9 000 ticks,
  tres generaciones de hormigas dentro de una evaluación). Es el que recupera la
  métrica del juego: 172 → 421.

Lo que se descartó, con su medida:

- **Banda estrecha y horizonte corto como objetivo de recuperación**: afinarla
  SUBÍA el fitness de la arena (1,9 → 3,5 descargas) mientras la partida caía de
  172 a 46. A 9 000 ticks el orden de la arena coincide con el de la partida.
- **Fitness mezclado (maze + clásico al 50 %)**: 47 y 10 descargas — peor en las
  dos superficies; cada generación seleccionaba con una vara distinta.
- **Mezclar pools** (16 especialistas de cada terreno en un archivo): 41 y 119;
  la colonia diluye a los dos.
- **Solo cola, sin cuenca**: si la política ya salió del régimen clásico, la cola
  no vuelve (150–171 descargas). La cola AMPLIFICA una competencia que existe; no
  la crea.
- **Generaciones clásicas con el horizonte del currículo de obstáculos**: a
  dificultad baja el presupuesto caía a 1 800 ticks —menos que un viaje de ida y
  vuelta a 200 u (~4 400)— y 600 generaciones seguidas vieron `unloads=0`: eran
  selección de ruido. Por eso el mantenimiento corre con el horizonte de su
  etapa y la cola con el suyo.

## 6. La receta (`scripts/pretrain-maze.sh`)

Dos fases encadenadas, cada una un CLI determinista:

1. **Cuenca** — seed 7, pop 32, 600 generaciones, `--obstacles --hidden 8 --grow
   --classic-every 5 --classic-tail 0`, warm-start `pretrain-warm-v2`.
   Sale el pool base (57 descargas en el laberinto, 172 sin él).
2. **Laberinto + cola** — 600 generaciones con `--classic-every 0` y
   `--classic-tail 60`, warm-start el pool de la fase 1. El pool final es la
   última evaluación (clásica).

Total **1 260 generaciones en 679–698 s** de reloj en esta máquina, con log por
generación en `artifacts/pretrain-maze.log` (las clásicas llevan `classic=1`) y
dificultad final 1,0 (10 escalones, 0 descensos). Cerebro **19→24→6** con dos
crecimientos. Reproducible: la misma semilla y receta dan el mismo SHA-256.

## 7. Medido (no prometido)

Partida canónica (`--mode game`, grid 96, 216 000 ticks, semillas 42/7/99/1234,
descargas al nido agregadas). Evidencia regenerable:
`python scripts/pretrain-maze-transfer.py` y
`… --no-maze --out artifacts/pretrain-maze-transfer-classic.txt`
(`artifacts/pretrain-maze-transfer*.txt`).

| pool | con laberinto (`--maze`) | sin laberinto |
|---|---|---|
| frío (sin pool) | 2 | 0 |
| `pretrain-warm-v2` | 39 | 242 |
| `pretrain-maze` (default anterior, 1 260 gen) | 79 | 421 |
| **`pretrain-maze-los3`** (default, 660 gen) | **102** | **271** |

Las tres primeras filas son de la tirada anterior del MISMO instrumento y las
semillas; el archivo `artifacts/pretrain-maze-transfer*.txt` que se regenera hoy
lleva la fila `maze` = `pretrain-maze-los3`, que es el pool por defecto (§8).

- 4/4 semillas con descargas en las dos superficies; en el laberinto, primera
  descarga en el tick ~1 262 de media (1 141 / 2 229 / 805 / 871).
- Sin laberinto: 71/57/63/80 (media 67,8 ± 8,6) frente a 64/66/49/63 (media 60,5)
  del de referencia. Gana el agregado, pero pierde en dos semillas sueltas (57
  contra 66 y 63 contra 64) y queda muy por debajo de los 421 de la tirada larga:
  la tirada corta cambia campo abierto por laberinto. Es el intercambio que se
  acepta al adoptarlo, no un efecto de una semilla suerte.
- La fase 1 sola da 57/172: es la cuenca, no el producto.

### Línea de visión en el canal de comida

El sensor de comida elegía el ítem más cercano dentro de 60 u **por distancia
pura**, sin mirar si había una pared en medio. En el laberinto eso es comida
invisible-al-alcance: con la élite congelada, el 83 % de lo «visible» estaba
detrás de un muro, la hormiga acampaba contra la pared y la colonia se extinguía
con más de 100 ítems en el mapa. Desde esta rodaja el canal exige
`MazeLayout.SegmentClear` (recorrido DDA del segmento hormiga→ítem sobre el mismo
bitmap que la colisión) **solo cuando hay terreno**: sin `maze` la selección es
la clásica y los pines de CI no se mueven.

Medido en la misma partida canónica (descargas totales, 4 semillas):

| condición | semillas 42/7/99/1234 | total | media ± σ |
|---|---|---|---|
| `pretrain-maze` sin línea de visión | 12/10/14/22 | 58 | 14,5 ± 4,6 |
| `pretrain-maze` **con** línea de visión (sin reentrenar) | 20/13/21/25 | **79** | 19,8 ± 4,3 |
| + reentrenamiento desde el pool (280 gen, pop 16) | 12/11/29/26 | 78 | 19,5 ± 8,1 |
| + reentrenamiento desde `pretrain-warm-v2` (300+300 gen + 60 clásicas, 424 s) → `pretrain-maze-los3` | 26/18/29/29 | **102** | 25,5 ± 4,5 |

- La percepción sola sube +36 % y no basta: la colonia sigue muriendo hacia el
  tick 33 000–35 000. Arregla el bucle patológico (empujar la pared), no el
  balance demográfico.
- Reentrenar **desde el pool terminado** con 280 generaciones no aporta media
  (78 ≈ 79) y sube la varianza: la política ya está en un óptimo local de la
  percepción vieja. Reentrenar **desde la cuenca** con la receta completa sí
  aporta (+29 % sobre la percepción sola): 660 generaciones en 424 s y el pool
  `artifacts/pretrain-maze-los3.antgenome` (19→24→6, 32 genomas). **Es el pool
  por defecto del juego**: siembra el lanzador, la escena y el catálogo (§8), y
  es el que mide `scripts/pretrain-maze-transfer.py`.

## 8. El juego arranca entrenado

- `scripts/play-game.sh`: `POOL=pretrain-maze-los3` por defecto (`--pool` sigue
  eligiendo otro, y los pools viejos se importan con `I`).
- `LaunchSettings.Pool = "pretrain-maze-los3"` y la escena `Game.unity`
  (`SeedPoolPath`) sembrada con ese archivo; `GeneratedSceneCatalog` lo declara
  en el contrato de escenas y `scripts/playpass-live.sh` juega con el mismo
  `SEED_POOL` (el contrato de escenas exige que catálogo y Play pass coincidan).
  El diálogo de importación arranca con el pool de la propia escena, así que
  regenerarla no lo deja apuntando a otro archivo.
- Los cuatro sitios que declaran el default están atados por
  `DefaultPoolContractTests`: `LaunchSettings.Defaults().Pool` es la fuente de la
  verdad y el `.sh`, el `.ps1`, la ayuda del `.bat`, `Game.unity` y el catálogo
  tienen que decir el mismo nombre (y la misma ruta `artifacts/<pool>.antgenome`).
  Cambiar el default en uno solo deja la suite roja con el archivo que se quedó
  atrás, en vez de arrancar la partida con un cerebro y declarar otro.
- El mundo adopta el ancho de cerebro del archivo (19→24→6): no hay que tocar
  ninguna constante al cambiar de pool.
- Tirada que lo produce, reproducible con la receta de §6:
  `GENS=300 OUT=artifacts/pretrain-maze-los3.antgenome \
  BASE_OUT=artifacts/pretrain-maze-los3-base.antgenome \
  LOG=artifacts/pretrain-maze-los3.log bash scripts/pretrain-maze.sh`
  (660 generaciones en 424 s, sha256 `4e8300a3…`). Como todo `artifacts/`, el
  archivo NO se versiona: en un clon limpio el pool se regenera con ese comando.

## 8bis. Rastro de reclutamiento en la partida (2026-10-23)

El canal E existía pero **nunca tuvo rastro que enseñar**: el juego solo emitía
la capa `home` de la colonia 0 y el rastro de comida solo aparecía si el cerebro
decidía depositarlo — algo que los pools entrenados no hacen porque nadie se lo
pagó. Resultado: feromonas invisibles y, en la práctica, inertes.

Con `--recruitment` (encendido en la partida; `--no-recruitment` en el lanzador
para el mundo clásico) pasan tres cosas, y solo en la partida:

- **Cúmulos ADEMÁS de lo aleatorio, RICOS y AL ALCANCE.** La siembra clásica
  sigue intacta (mismo flujo del RNG, mismos pines) y encima se siembran 8
  montones de 6–10 ítems de **10 ep** (el tope del contrato; una migaja suelta
  vale 4–6) a **100–220 u de una colonia**. Las dos cosas están medidas, no
  elegidas a ojo: las obreras patrullan ~100 u alrededor del nido (mediana 93 u,
  solo un 11 % pasa de 200 u) y un montón sembrado «en cualquier sitio» no lo
  visita nadie — el rastro no tenía a quién guiar y el viaje pagaba un acarreo
  largo. Con la banda de forrajeo y el bocado rico, el viaje se paga solo.
- **Dos reflejos innatos, no salidas del cerebro.** La hormiga deja el rastro
  según lo que está HACIENDO: la que **busca** va dejando rastro de **casa**
  (el camino de vuelta al nido) y la que vuelve **cargada** deja rastro de
  **comida** (hacia el montón). Una celda, la que pisa. Además, la que busca
  **gira hacia el lado que huele más comida** (ganancia 8 en ratio, como la
  tropotaxis de huella). No hay canales de sensor nuevos: ningún genoma cambia
  de forma y el cerebro puede reforzar o corregir el reflejo.
- **El rastro de comida vive 2 100 s** en este mundo (la vida media clásica son
  10 s). El viaje nido→montón→nido tarda 1–2 min; con 10 s el camino se borraba
  entre visita y visita, y cada hormiga dejaba su tramo sin que nadie
  encontrara el del anterior. Además crece hacia la FUENTE (suelo 0,4 en el
  nido), no hacia el tráfico, y se deposita en **una sola celda**: la que ocupa
  la hormiga. La primera versión repartía el depósito en un kernel 3×3 (centro
  0,4 + cuatro vecinas 0,15) para que la antena lo encontrara antes; el
  resultado en el tablero era una banda de **24 u como mínimo** —la difusión no
  puede agrandar el soporte, así que ese era el ancho— y, con decenas de
  hormigas volviendo por caminos paralelos, una nube de humo de 100 u que no se
  parecía a nada que hubiera pisado una hormiga. Ahora todo el depósito va a su
  celda (8 u) y el rastro se lee como un camino pegado al suelo.

**Qué se descartó, medido.** Seguir el rastro barriendo 8 direcciones y
orientarse «alejándose del nido» (única forma de desambiguar una cresta escalar)
**empeora** el forrajeo: las hormigas se quedan sobre el camino en vez de
llegar al montón (19–22 descargas frente a 26 del clásico, semilla 42). Seguir
el rastro de CASA con la cargada tampoco ayuda, porque el cerebro ya deposita
`home` por todas partes y el gradiente no apunta al nido. Ninguna de las dos
quedó en el código. Lo que sí quedó es la tropotaxis lateral de una sola sonda,
que es la que convierte el rastro en una guía (`--trail-follow 0` es el control
A/B: mismo mundo y mismo depósito, sin seguimiento).

**Medido** (96², laberinto, pool `pretrain-maze-los3`, `--until-extinct`,
`scripts/recruitment-probe.py` → `artifacts/recruitment-probe-ida-vuelta.txt`):
**349 vs 206 descargas** en 8 semillas (media 43,6 ± 10,0 vs 25,8 ± 6,1) y
**8/8 semillas ganadas**, la más ajustada 32 vs 26. En la semilla 42, 32 vs 26
descargas y 66 vs 56 recogidas. Los dos cambios de la revisión cuestan un **6 %**
en total —depósito de una celda en vez de 3×3 (371 → 357) y el reparto
ida/vuelta: casa para la que busca, comida para la cargada (357 → 349)— con la
rama clásica reproduciendo los 206 al byte: el rastro sigue guiando y ahora se
ve como un camino. En la partida viva el rastro de comida
pasa de 2 celdas con señal (residuo del cerebro) a **cientos de celdas** (204
con señal, máx. 171, a los 22 176 ticks de la última medición) siguiendo los
pasillos que pisan las hormigas.

**Las perillas están en el lanzador (2026-10-23).** Comparar configuraciones de
reclutamiento era tocar código: los valores vivían en el mundo y el lanzador
solo encendía/apagaba la bandera. Ahora `play-game.ps1/.bat/.sh` acepta
`--clusters`, `--cluster-size MIN:MAX`, `--cluster-radius`, `--trail-deposit`,
`--trail-half-life` y `--trail-follow` (los mismos nombres en el CLI, junto a
`--recruitment`), los escribe en el archivo de ajustes (`clusters=`,
`clusterSize=`, …) y la escena los imprime al entrar en Play. Los defaults son
los del mundo (`8`, `6:10`, `18 u`, `1`, `2100 s`, `8`) y se aplican **antes de
sembrar**: el mundo recibe `RecruitmentTuning` en su constructor, porque los
montones se crean ahí y asignarlos después no cambiaría nada. Los rangos se
validan en una sola tabla (`RecruitmentTuning.TryValidate`) para que el CLI y
los dos lanzadores no discrepen. `--clusters 0` y `--trail-follow 0` son los
controles A/B: mismo mundo, sin montones o sin seguimiento.

En el tablero, el canal E pasa a llevar `food` y `home` de **cada** colonia
(`--phero-layers 0:food,0:home,1:food,1:home`; desde §8quater van las cuatro
capas y se pintan a la vez), la capa que se pinta por defecto es la de comida y
el quad **conserva el último paquete** entre emisiones: antes
lo borraba en los 29 ticks de cada 30 en que el canal no traía paquete, que es
el parpadeo con el que el rastro «no se pintaba».

**La curva de pantalla se reescribió para que el rastro se lea (2026-10-23).**
Antes el alfa de la textura era **el byte del canal E tal cual**: el camino se
quedaba en el valor del byte (87 % de opacidad en la capa medida, máximo
222/255) y la cola que deja la difusión (8–40/255 alrededor del depósito)
quedaba repartida por una superficie varias veces mayor que el camino, con un
alfa bajo pero continuo. En el tablero eso se lee como una
mancha: no hay un borde donde acabe el rastro ni un centro que destaque. Ahora
`PheromoneRamp` (modelo puro, tests en la suite) **descarta la cola** por debajo
del 18 % del máximo de la capa (suelo absoluto 6/255, para no amplificar el ruido
de una capa vacía) y lo que queda va del **borde al 30 % al núcleo opaco**, con
gamma 0,75. El corte es RELATIVO a la capa: un rastro incipiente (máximo 8/255)
sigue viéndose entero. **El dato no se toca** — el modelo, el HUD y las hormigas
siguen leyendo los bytes del canal E; solo cambia lo que se pinta.

**Cuánto cambia, medido sobre la capa real** (semilla 42, grid 96, laberinto,
pool `pretrain-maze-los3`, `--recruitment --phero-every 600`, decodificando el
paquete E y aplicando la curva del render): la capa de **comida** del tick
10 800 (máx. 222) pasa de **292 celdas pintadas a 75** y el corte que cruza su
celda más fuerte de **104×40 u a 56×40 u**; la de **casa** del tick 10 200
(máx. 222) pasa de **695 a 258 celdas** y de **216×208 u a 72×128 u**. El núcleo,
que antes se quedaba en alfa 0,87, llega a 1: menos superficie y más contraste
es lo que hace que el camino se lea como camino. Los renders de antes/después
de esas dos capas están en `artifacts/rastro-{food,home}-{antes,despues}.png`.

**El HUD publica las cifras del tablero (2026-10-24).** La línea de reclutamiento
solo decía lo que se PEDÍA al mundo (`montones=8 · rastro=1× / 2100 s`): si los
montones ya se habían comido o la capa no tenía una sola celda pintada, el HUD
seguía prometiéndolos. Debajo va ahora una segunda línea con lo que HAY, contado
del tick presentado con un modelo puro (`RecruitmentLiveModel`): montones vivos
(grupos de ≥3 ítems dentro del radio de montón), ítems y el rastro que el render
pinta de verdad —celdas con alfa > 0 según `PheromoneRamp`, su máximo y la banda
que cruza la celda más fuerte—. Como las celdas se cuentan con la curva del
render, la cifra del HUD y los píxeles no pueden discrepar: si dice «75 celdas»
y el tablero está limpio, el fallo es del render.

**El mapa iba ESPEJADO (2026-10-23).** El canal E llega con la celda (0,0) en el
mundo (0,0) —el Core deposita en `Cell(ant.X), Cell(ant.Y)`—, pero el quad es un
`PrimitiveType.Plane` de Unity y su UV no empieza ahí: el vértice local (5,5)
—el rincón opuesto, mundo (+x,+z)— lleva uv (0,0), así que el rastro se pintaba
**rotado 180°** en la otra mitad del tablero y parecía moverse solo (las
hormigas iban por un lado y la nube por el simétrico). Se corrige al componer la
textura (`PheromoneTileBehaviour`), no girando el quad, para que el modelo siga
diciendo «celda (x,y) = mundo (x·8, y·8)». Lo mide `ProbeRecruitmentShot`
proyectando cada celda con la cámara real contra las cuatro simetrías del quad:
**93 % de aciertos en su sitio frente al 2,5 % girado 180°** (6 % con el eje X
espejado y solo, 65 % con el Y solo: el defecto es la rotación, no un volteo);
en las partidas con el rastro estrecho, el **96–100 %** de las celdas fuertes
caen sobre píxel ámbar (la cola débil no se cuenta: su píxel se confunde con el
suelo).

**El ancho, medido en el corte de la celda más fuerte** (`ProbeRecruitmentShot`,
corte horizontal/vertical del dato): con el kernel 3×3 era **12×36 celdas** a
partir de q≥8 (96 u en el eje corto) y 8×5 (mínimo 40 u) a partir de q≥96; con
una celda es **5×6** (40 u) y 3×3 (24 u). La hormiga mide 13,8 u de largo, así
que el rastro es ahora 1–2 hormigas de ancho en vez de una nube de decenas de
unidades, y la difusión no puede ensancharlo: una celda nula sigue nula.

## 8ter. La ronda ya no se apaga por hambre (2026-10-24)

**El síntoma.** La partida canónica (96², laberinto, pool `pretrain-maze-los3`,
reclutamiento, `--until-extinct`) cerraba a los **35 541 ticks ≈ 19,7 min** con
**150 ítems en el tablero**. No era falta de comida: era que la comida no
llegaba al nido. Medido sobre el stream real de esa partida:

- de **44 hormigas** que cogieron comida, solo **3** se acercaron a 40 u del
  nido (distancia mediana de las cargadas: **226 u**);
- **55 hormigas** pasaban más de 3 s sin moverse, con el bocado en la boca:
  empujaban una pared del laberinto de frente, y el deslizamiento por un eje
  daba milésimas de avance (no las suficientes para que el «chocó» se notara);
- la que no encontraba comida se quedaba en el campo hasta morir de hambre: el
  gasto de caminar y depositar vacía la reserva en **~400 s**, la mitad de la
  longevidad (720 s), así que la colonia pagaba su mantenimiento sin cobrar su
  trabajo;
- y el nido descontaba de la reserva la ración teórica de **todas** las
  adultas, estuvieran llenas o no: **~0,3 ep/s** de comida que salía del stock
  sin alimentar a nadie. Con una entrada de ~0,4 ep/s, eso era la puntilla.

**Lo que se corrigió** (todo bajo la bandera F8: con el mundo clásico no se
mueve ni un flotante, y los seis pines de CI siguen byte a byte):

- **Vuelta de la cargada por el CAMPO DE PASILLOS.** Un BFS de 4 vecinos desde
  la celda del nido de cada colonia (entero, sin RNG, construido una vez y
  cacheado) da la distancia al nido por corredor. La cargada gira hacia el
  centro de la celda vecina que baja la distancia —corrección lateral, no rumbo
  impuesto— así que rodea el muro en vez de empujarlo. Sin laberinto el
  objetivo es el nido y el reflejo es una brújula.
- **Descarga al pisar el nido.** La cargada que entra en el radio del nido
  suelta el bocado sin que el cerebro pulse interacción: el bocado venía para
  casa y llegó.
- **La pared no se atraviesa: la hormiga se apoya y gira.** Si el paso real es
  menos del 25 % del pretendido, la dirección gira `MazeBumpTurn` (0,5 rad). Es
  el rebote físico que faltaba; sin él, una cargada con el nido justo detrás
  del muro se quedaba empujando para siempre.
- **Vuelta a recargar.** Con menos del 40 % de reserva (`TiredEnergy`), la
  hormiga vuelve al nido con el mismo guiado y recarga; después sale otra vez.
  Es lo que cierra el ciclo de la ENERGÍA, no solo el de la comida.
- **La feromona de la partida no se paga con la reserva.** En esta ronda el
  rastro lo pone el reflejo (gratis); los canales del cerebro seguían
  cobrándose a 0,02 ep por unidad —hasta 0,03 ep/s por hormiga, más que su
  mantenimiento entero— y vaciaban el buche de las forrajeras.
- **El nido solo paga el alimento que se come** (`Colony.RecruitmentEconomy`):
  el presupuesto de adultas se recorta al hueco real de energía de cada una.
  De paso, la puesta se mide con la entrada NETA (lo que sobra por encima de lo
  que ya cuestan las adultas) en vez de con el gate clásico, que trataba
  cualquier goteo ≥0,1 ep/s como abundancia y hacía poner ~0,9 huevos/s con
  0,35 ep/s de entrada.

**Medido** (mismos parámetros, `--until-extinct`, cuatro semillas, muriendo por
edad o al horizonte):

| semilla | antes | después | muertes por hambre |
|---|---|---|---|
| 42 | 19,7 min | **84,2 min** | 0 |
| 7 | 19,7 min | **48,4 min** | 0 |
| 123 | 19,7 min | **69,1 min** | 0 |
| 2024 | 19,7 min | **60,8 min** | 0 |

Las descargas al nido pasan de 26–32 en toda la ronda a **90–150** (semilla 42:
137 descargas y 178 recogidas a los 60 000 ticks), y la colonia sostiene las 40
adultas del tope durante la primera hora con el stock oscilando en vez de
caer a cero. **Ninguna hormiga muere ya de hambre**: las que caen, caen de
edad. Lo que queda es el techo del propio forrajeo —el pool entrenado tarda
~200 s de media en encontrar cada bocado, y una entrada de ~0,4 ep/s paga justo
el mantenimiento de 40 adultas, sin margen para criar—; por eso la ronda sigue
terminando, más tarde y por vejez, no por inanición. Subir la comida del
tablero (más montones, banda más cercana, bocados de 10 ep también fuera de los
montones) se probó y **no** alarga la ronda: el límite no es la oferta, es el
ritmo al que la política la encuentra.

## 8quater. El rastro es una huella: ancho de hormiga, vida ×7 y colores por tipo (2026-10-24)

**El síntoma.** El rastro se veía como humo: el depósito de una celda se difundía
(k = 0,10/s) hasta una mancha de decenas de unidades —más ancha que la hormiga—
y con una cola de niebla alrededor; la casa (τ½ 20 s) y la alarma (1 s) se
borraban casi al pintarse, y el tablero enseñaba **una** capa cada vez, así que
para saber dónde estaba el peligro había que pulsar F. Además, con laberinto el
nido se «veía» a través de la pared: la brújula innata del cerebro era la recta
al nido, y con el muro en medio la hormiga lo empujaba.

**Lo que se cambió** (todo bajo la bandera F8; el mundo clásico conserva su λ y
su difusión, y los 6 pines de CI siguen byte a byte):

- **Una celda de ancho, sin difusión.** En la ronda `Diffuse(0)` en las cuatro
  capas: el depósito se queda en la celda pisada (8 u, menos que el largo de una
  obrera, 13,8 u). El camino es exactamente lo que se ha caminado, sin cola.
- **Vida ×7.** Comida 10 s → **2 100 s** (`--trail-half-life`), casa 20 → 140 s,
  alarma 1 → 7 s y huella 240 → 1 680 s. El rastro se borra por tiempo, no por
difusión.
- **Todas las capas a la vez, un color por tipo.** El canal E lleva `food`,
  `home`, `alarm` y `footprint` de cada colonia, y el quad compone las cuatro con
  la paleta del selector (ámbar, verde, rojo, violeta). Donde se cruzan, la celda
  enseña la mezcla. F pasa de «todas» a una capa suelta y la cicla; G cambia de
  colonia.
- **El camino manda sobre el sustrato.** No todas las capas ocupan lo mismo, así
  que no se pintan con la misma opacidad: medido en la partida (semilla 3, grid
  96, tick 24 000), la comida ocupa el **1,5 %** del tablero y la alarma el 1,8 %,
  pero la casa el **21,7 %** y la huella CHC el **31,2 %** —las deposita cada
  hormiga que anda, así que el suelo pisado se satura—. A opacidad plena ese velo
  verde y violeta enterraba el camino ámbar: el «humo» volvía por la puerta de
  atrás. El compuesto pondera cada tipo (comida 1,00, alarma 0,85, casa 0,55,
  huella 0,25) antes de mezclar, y el resultado es que el camino sale con alfa
  medio **0,93** y el sustrato queda en **0,19**: se ven los cuatro, pero el
  rastro que interesa es el que se lee primero.
- **Paredes opacas al nido.** Con laberinto, `HomeDx`/`HomeDy` (canales 9–10 del
  cerebro) ya no son la recta al nido: apuntan al centro de la celda vecina que
  baja el campo de distancia por pasillos —el mismo que usa la vuelta de la
  cargada— y valen 0 si la celda no está conectada. Ninguna celda abierta del
  laberinto da una dirección que cruce una pared.

**Medido** (stream real, semilla 42, grid 96, laberinto, pool
`pretrain-maze-los3`, reclutamiento, tick 20 000 ≈ 11 min): la capa de comida
tiene **383 celdas con señal**, el **recorrido mediano de un corte es 1 celda**
(máx. 57 a lo largo de un pasillo) y cada celda con rastro tiene **2,55 vecinas
con rastro** de media — lo que mide una línea de una celda, no una mancha. La
ronda completa (3 semillas, `--until-extinct`): **71,0 descargas y 42,6 min** de
media, frente a **31,7 y 23,0 min** del mundo clásico. El seguimiento del rastro
pasa a aportar: con `--trail-follow 0` (mismo mundo, mismo rastro depositado) la
media baja a 63,7 descargas — **+7,3 entregas (+11 %)** por seguirlo.

**Coste.** El rastro estrecho también es más difícil de oler —una línea de 8 u
que las antenas cruzan o no—: con el paquete completo la ronda baja de ~54 a
~43 min frente a la versión con difusión y 300 s, pero sigue doblando al clásico
y el seguimiento del rastro ya suma (+11 %) en vez de ser neutro. El ancho se
controla con la geometría del depósito, no con la difusión.

## 8quinquies. Comprobado en el tablero (2026-10-24)

**El tablero parpadeaba.** La composición de capas se rehacía en CADA tick, y en
los 29 de cada 30 que no traen paquete del canal E (`--phero-every 30`) se
quedaba sin capas y llamaba a `ClearTiles()`. El rastro se pintaba un frame y se
borraba al siguiente: el espectador veía un fogonazo cada medio segundo. El
camino de una sola capa ya conservaba el último mapa; la composición no. Ahora
un tick sin paquete conserva el frame pintado y solo se limpia cuando el tick
trae feromonas pero **ninguna de la colonia elegida** (ahí dejar la anterior
sería mentir con la etiqueta del HUD).

Medido en el Play del Editor (semilla 42, grid 96, laberinto, reclutamiento,
pool `pretrain-maze-los3`, 2 colonias, 120×, canal E cada 30 ticks):

- **Duración.** 12 muestras seguidas de `ProbeLiveBoard`, las 12 SIN paquete E
  en el tick leído: los píxeles con rastro van de **873 a 1 162** sin caer a cero
  en ninguna (antes del arreglo, esas 12 habrían dado 0).
- **Ancho.** En el corte más fuerte (tick 35 039) el ámbar mide **12,8 × 4,3 u**
  en el frame; el corte mediano del dato es **1 celda (8 u)** y la banda del HUD
  es 32 u en el nodo más grueso. La obrera mide 13,8 u de largo: el rastro es una
  línea, no una mancha.
- **Cuatro colores.** En la misma ventana el compuesto da **322 px ámbar (comida),
  536 verdes (casa), 12 violetas (huella) y 0–9 rojos (alarma)**. El rojo es
  escaso porque la alarma vive **7 s** y solo 24 de sus 183 celdas superan el
  corte de la curva; aislada (F → alarma) la capa pinta **36 px rojos de 98
  celdas**, así que el canal y la paleta están bien: es el dato el que casi nunca
  tiene alarma.
- **Paredes: 9 de 150 hormigas (6,0 %) se quedaban en la cara del muro.** En la
  ronda viva, 4 muestras seguidas —≈230 s de simulación— sin moverse más de 2 u;
  7 de ellas cargadas. No era una parada corta: la 60 se quedaba con `y = 188,0`
  exacto mientras la x oscilaba entre 589 y 597, y la 96 con `x ≈ 396,2` mientras
  la y oscilaba 396↔408, con el nido al otro lado. La brújula por pasillos y el
  rebote existen, pero no las sacaban del rincón: en la esquina el objetivo es el
  centro de la celda siguiente y queda en el borde del hueco, así que la hormiga
  gira hacia él, apoya el cuadro y orbita.

  **El arreglo (`BreakGuideStall`).** Si una hormiga que vuelve —cargada o sin
  reserva— pasa **5 s sin bajar de celda en el campo** (150 ticks,
  `GuideStallTicks`), **sale del rincón por una celda libre**: de las cuatro
  celdas ortogonales de la suya toma la que más baja el campo (empate: el orden
  fijo −X, +X, −Y, +Y del BFS), y el reflejo de vuelta se toma una tregua que
  **no se agota por tiempo sino al cambiar de celda** (`HomingTruceTicks` = 90
  como tope). Determinista (no usa el RNG) y solo con reclutamiento y laberinto.

  La primera versión —media vuelta a ciegas y 3 s de tregua— no bastaba: el
  giro de 180° devolvía a la hormiga al mismo pasillo y la tregua terminaba
  ANTES de que cambiara de celda, así que el reflejo volvía a engancharla en el
  mismo rincón y el ciclo se repetía indefinidamente. De ahí las dos piezas
  nuevas: elegir la dirección de salida (ortogonal y libre, nunca una diagonal,
  que es la que corta la esquina) y medir la tregua en celdas, no en segundos.

  **Medido en headless** (sonda temporal —no se commitea—, partida canónica
  96², laberinto y reclutamiento, 40 000 ticks, una muestra cada 2 000,
  ventana de 4 muestras con deriva < 2 u, semillas 42/7/123): con la media
  vuelta **5,4 / 4,9 / 3,2 %** de muestras-hormiga atascadas → con el escape
  **1,6 / 2,7 / 2,2 %** (mismo mundo, misma compilación, A/B sobre la misma
  sonda: el atasco baja a la mitad o a un tercio en las tres semillas).

  **Medido en la partida viva** (Play del editor con el lanzador canónico,
  semilla 42, grid 96, laberinto, reclutamiento, 120×; sonda `ProbeStuckLive`
  cada ~1,5 s, ~4 700 ticks entre muestras, ventana de 4):

  - Con la media vuelta, **2 de 305** muestras-hormiga atascadas —y eran las
    dos cargadas deslizándose por la cara del muro: la `124` con `x = 396,0`
    exacto (la y oscilando 405,8↔408,1) y la `147` con `y = 460,0` exacto—,
    ambas con el nido al otro lado.
  - Con el escape, **1 de 720** (0,14 %) repartidas en cuatro rondas seguidas
    (volcado de las tres tandas en `artifacts/live-stuck.txt`)
    (291 + 128 + 172 + 129 muestras-hormiga): tres rondas sin ninguna y una con
    una sola cargada, `x = 332,0` clavada, deriva 1,58 u. La firma sigue
    existiendo, pero ya no es un estado estable de la ronda: aparece una vez y
    no se repite en las ventanas siguientes.

## 9. Verificación

- `dotnet test`: **767/767** en verde (28 de `ObstacleTrainingTests` —cola clásica
  y warm-start más ancho que la escalera—, 3 de `MazeTests` para la línea de
  visión —el recorrido DDA, el filtrado del ítem tras el muro y el gating sin
  laberinto— y 7 nuevos para §8quater: ancho de una celda sin difusión, vida ×7
  en las tres capas, el mundo clásico intacto, la composición de colores, los
  pesos de pantalla —el camino de comida se lee más que la casa y bastante más
  que la huella, y donde se cruzan la celda sigue saliendo cálida— y la brújula
  del nido que no cruza paredes en ninguna celda del laberinto).
- `scripts/unity/ProbeRecruitmentShot.cs` (Unity, partida viva): lee la escena y
  el frame real y publica la línea del HUD, los montones agrupados por
  proximidad, el rastro del canal E (celdas, máximo, cobertura nido→montón) y
  las celdas proyectadas contra los píxeles ámbar. Es la sonda que destapó el
  mapa espejado y la que mide el ancho del rastro; guarda el frame y el recorte
  del montón en `Temp/`.
- `scripts/unity/ProbeRecruitmentHud.cs` (Unity, partida viva): lee las DOS
  líneas que pinta el HUD, las contrasta con el modelo (`RecruitmentLive`) y con
  la capa que el quad está pintando, y guarda el frame en
  `Temp/antsim-hud-recruit.png` (copia del Play pass:
  `artifacts/verificacion-hud-reclutamiento.png`). Medido a tick 27 041 de una
  partida con el pool y el laberinto canónicos, a 160×: `tablero · montones 12 ·
  ítems 162 · rastro food 177
  celdas (máx 209, banda 40×48 u)`, con **177 de 610 celdas con dato** pintadas
  (la curva descarta el 71 %). En la misma partida (tick 11 789)
  `ProbeRecruitmentShot` da el mapa en su sitio —**91,2 %** de las celdas
  fuertes sobre píxel ámbar, 0 % girado 180° (39,8 % solo con el eje Y
  espejado)— y una banda ámbar de
  **40,6×56,3 u** frente a una hormiga de 13,8 u. La consola del editor queda sin
  errores (`console --level error` → 0).
- Los 6 pines de CI no cambian ni un byte (stream canónico, replay 3000/6000,
  Atta, invasión, NEAT y la constante del fixture en la suite).
- `scripts/check-doc-figures.sh`: 6 pines y el número de tests de la suite.
- `scripts/recruitment-probe.py` (no es una puerta: es la medición de §8bis):
  corre la partida con y sin `--recruitment` en las semillas que se le pidan y
  escribe la tabla en `artifacts/recruitment-probe.txt`; sale con 1 si el
  reclutamiento no gana en TODAS las semillas.
