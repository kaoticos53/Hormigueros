# Estado del proyecto — consolidado

*Actualizado: 2026-10-25 · HEAD: `v0.8.0` (tag de cierre de la F5.3) ·
suite: 784/784 (Windows) · tags: `v0.4.0` (Fase 4), `v0.5.0` (F5.2a), `v0.6.0` (F5.2b),
`v0.6.1` (CI fix), `v0.7.0` (F5.2c NEAT), `v0.8.0` (F5.3 escala)*

Mapa de las fases del proyecto: qué está terminado, qué queda y dónde
estamos. Los detalles de cada fase viven en sus documentos; este es el
índice con veredicto. Enlaces: [`arquitectura.md`](arquitectura.md) (plan
global por fases), [`especificaciones.md`](especificaciones.md) (contratos),
[`fase5-plan.md`](fase5-plan.md) (plan de Fase 5),
[`fase6-terreno.md`](fase6-terreno.md) (F6: laberinto, ritmo 120× y evolución por nacimientos),
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
| **6** | **Terreno con laberinto, ritmo 120× (216 000 ticks) y evolución por nacimientos** | ✅ **CERRADO** | `fase6-terreno.md` |
| **7** | **Pre-entrenamiento con obstáculos, currículo adaptativo y cerebro 8→16→24** | ✅ **CERRADO** | `fase7-obstaculos.md` |
| 6bis | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

**F6 (cerrada, sin tag todavía)**: el tablero es un laberinto con bucles generado
de la semilla (paredes visibles, colisión con deslizamiento, antenas que ven el
muro de su lado), la partida arranca a **120×** y dura **216 000 ticks = 2 h de
simulación**, y la evolución va por nacimientos —sin botón de «siguiente
generación»— guardando la élite en `artifacts/evolved-<semilla>.antgenome` al
terminar. Sin `--maze` el mundo y el stream son byte a byte los de antes: los 6
pines de CI no se mueven. Detalle y medidas en
[`fase6-terreno.md`](fase6-terreno.md).

**F7 (cerrada, sin tag todavía)**: el pre-entrenamiento headless monta el
laberinto del juego con dificultad graduable (`MazeLayout.Pruned`: `d=1` es el
mapa del juego byte a byte, `d=0` campo abierto) y la sube por **competencia
medida** —descargas reales de la mejor colonia, con histéresis—; cuando el
terreno llega al máximo y el fitness se estanca, la capa oculta crece 8→16→24
reproduciendo la salida anterior. La receta tiene DOS fases medidas: una cuenca
con mantenimiento clásico (1 de cada 5 generaciones, banda y horizonte del
currículo) y un refinado de laberinto que cierra con 60 generaciones en el
régimen de la partida sin laberinto (comida en todo el mapa, horizonte largo).
**1 260 generaciones en 679 s** con semilla reproducible (mismo SHA). El canal de
comida exige **línea de visión** desde esta rodaja (`MazeLayout.SegmentClear`,
DDA sobre el bitmap de colisión) y solo con terreno: sin `--maze` la selección es
la clásica y los pines no se mueven. El pool por defecto sale de la tirada corta
de la misma receta (660 gen, 424 s) y forrajea en LAS DOS superficies: **102
descargas con `--maze` contra 39** del de referencia y **271 sin laberinto contra
242**, 4/4 semillas en ambas. El juego arranca con él por defecto (lanzador,
escena y catálogo) y **encadena rondas**: cuando no queda ninguna hormiga ni
cría el CLI corta en ese tick (`--until-extinct`), exporta la élite y otra
ronda arranca con **laberinto nuevo** (semilla derivada) sembrada con esos
cerebros. Detalle y medidas en [`fase7-obstaculos.md`](fase7-obstaculos.md).

**F11 → F11b (medida; paga con el rastro CON PRECIO, aún sin adoptar en el
lanzador) — ¿debe decidir la red cuándo dejar feromona?**
La vara de F10 (seleccionar por **descargas en la partida del lanzador** en vez de
por el fitness del currículo: grid 96, dos colonias con rival salvaje, cúmulos,
rastro y relevo) sirvió para preguntar si el rastro de reclutamiento tiene que
abrirlo el cerebro en vez del reflejo fijo de F8. Con `--trail-by-brain` las dos
gotas del reflejo se escalan por `DepositFood`/`DepositHome` (0 = no deja nada,
1 = el reflejo de siempre) y la partida de evaluación corre con esa misma puerta.
**No paga.** Dos tiradas de 200 generaciones con el mismo warm-start, semilla y
presupuesto (pop 32, 3 semillas × 24 000 ticks por genoma, 28 núcleos, 2 h 48
min en paralelo): el brazo con puerta acaba **8 % por debajo** en la media de la
población (76,6 vs 82,8 en la escala de descargas) y **5 % en el pico** del mejor
(117 vs 123); los dos
arrancan igual (gen 1–10: 49,7 vs 50,5 de media). En la partida completa
(216 000 ticks, 8 semillas que la selección no vio y comprobación de que cada
partida llega al último tick) el MISMO pool con la puerta rinde **473 contra 705**
descargas del reflejo fijo (−33 %; pareado: +232 ± 106, 7/8 semillas), y el pool
entrenado con la puerta queda **peor incluso con el reflejo fijo encendido**
(422 vs 705; pareado: +283 ± 58, 8/8 semillas): la puerta no solo empobrece el
rastro, desvía la selección a una cuenca peor. De referencia, en esas mismas 8
semillas: warm-start **599** → 200 generaciones con el reflejo fijo **705**
(+106 ± 75, 7/8) → las 400 de F10 **911** (+205 ± 60 frente a las 200, 8/8). La
causa más probable es mecánica: la puerta solo puede ADELGAZAR el rastro —la
salida está en [0,1] y no tiene razón para sentarse en 1 todo el rato— y F8 ya
midió que menos rastro es menos reclutamiento (331 → 371 descargas al subir el
seguimiento de 0 a 8 rad/s); con un depósito gratis, además, no hay nada que
empuje la salida hacia 1 salvo el propio pico de fitness. No se ha medido la
salida media del enjambre (no hay sonda para eso todavía), así que esa parte es
inferencia, no dato.
Lo que faltaba para que la pregunta fuera justa era un PRECIO, y ya está: **F11b,
la glándula**. Cada gota de feromona de comida y de casa sale de la reserva
personal de la hormiga (`Ant.Gland`), que se gasta al depositar y **solo se
rellena dentro del nido** (`--trail-gland 25 --trail-gland-refill 6`: 25 s de
depósito a plena tasa, 6 s de vacía a llena). Con eso la cargada ya no puede
marcar los primeros metros a plena tasa y dejar el corredor vacío: si quiere que
el rastro llegue lejos tiene que RACIONARLO, y ahí la salida del cerebro tiene
algo que decidir en vez de una constante que solo podía empeorar. Se cobran los
dos canales del cerebro y los dos del reclutamiento; la alarma y la huella CHC
no salen de la glándula. Sin precio (`--trail-gland 0`, el default) el mundo es
byte a byte el de F8. Cubierto por cuatro pruebas de `GameFitTests` (la puerta
cerrada no deposita, la mitad de salida es la mitad de feromona, la glándula
cobra el depósito y solo se rellena en el nido) y el lanzador no expone las
banderas.
La tirada que lo juzga —200 generaciones, pop 32, semilla 7, mismo warm-start,
un brazo con la puerta del cerebro y otro con el reflejo fijo, el MISMO precio en
los dos— está hecha (`scripts/run-gland-200.sh`, 1 h 45 min en paralelo; logs en
`artifacts/pretrain-partida-200-gl-*.log`): **con el precio, la puerta SÍ paga**.
En la selección el brazo con puerta acaba con más media de población (70 954 vs
66 857; diferencia pareada +4 097 por generación, sd 3 485, t = 16,6; gana
174/200 generaciones) y más descargas por generación (92,8 vs 88,2; pareado
+4,54, t = 10,0, 146/200), aunque en las últimas 20 generaciones los dos se
igualan (77 578 vs 76 117). El mejor genoma también es mejor (121 465 vs 110 125).
Lo que decide es la partida completa (`scripts/gland-bench.py`: 216 000 ticks,
laberinto, las 8 semillas del veredicto de F11 —42,7,99,1234,3,11,555,2024—, el
precio encendido en los CUATRO brazos, todas terminan en el tick 216 000):

| pool entrenado | quién decide el rastro | descargas por partida | total |
|---|---|---|---|
| puerta | la puerta del cerebro | 207,6 ± 53,0 | 1 661 |
| puerta | reflejo fijo | 177,9 ± 50,7 | 1 423 |
| reflejo fijo | la puerta del cerebro | 163,8 ± 56,6 | 1 310 |
| reflejo fijo | reflejo fijo | 169,6 ± 58,0 | 1 357 |

- **De punta a punta** (lo que se desplegaría: pool-puerta con puerta contra
  pool-reflejo con reflejo): **+38 ± 13** descargas por partida (error típico;
  gana en 6/8 semillas), **+22 %** en total. Con 24 semillas (F11c) la misma
  comparación queda en +22,8 ± 8,2 y gana 17/24: la muestra de 8 exageraba el
  tamaño, no el signo.
- **La puerta dentro del pool que la entrenó**: 177,9 → 207,6 (+30 ± 11, 7/8).
  En el pool entrenado SIN puerta es neutra (169,6 → 163,8, −6 ± 10, 4/8): la
  puerta no vale por sí sola, vale con un cerebro que aprendió a racionarla.
- **Entrenar con la puerta deja mejor pool**: 1 423 contra 1 357 con el reflejo
  fijo en los dos (+8 ± 8, 6/8) — lo contrario que en F11, donde entrenar con la
  puerta desviaba la selección a una cuenca peor.
El precio no es gratis, y se mide dentro de esta misma tirada (el pool que aprendió
con la puerta, las mismas 8 semillas, solo cambian la glándula y la puerta):
**218,1** descargas de media sin precio y con el reflejo → **207,6** con precio y
con puerta → **177,9** con precio y sin puerta. El precio cuesta ~19 % al rastro
fijo y la puerta lo recupera casi entero (−4,8 % frente al mundo gratis). O sea:
el precio se paga en descargas y se cobra en una decisión que la red puede
aprender a tomar; el mundo sin precio sigue rindiendo más en absoluto, pero es el
mundo de F11, donde la decisión no tenía nada que decidir. Que la puerta y el
precio pasen visiblemente al lanzador o a la escena —donde hasta F11n no se
exponían— quedó pendiente de decisión: aquí solo se mide, y la decisión se tomó
en F11n (sí: el enfrentamiento del mismo día contra el pool de fábrica la
aprobó).
El precio, además, se puede BARRER, y se ha barrido (`scripts/gland-price-sweep.py`:
216 000 ticks, laberinto, 24 semillas que la selección no vio —las 8 de F11 más 16
nuevas—, los dos pools × las dos puertas, la glándula en 0/10/25/40/60 s y la
recarga fija en 6 s; 480 partidas, evidencia en
`artifacts/pretrain-partida-gl-price-sweep.txt`). Lo que aporta la puerta dentro
del pool que aprendió con ella, precio por precio:

| glándula | puerta − reflejo fijo | t | semillas | total |
|---|---|---|---|---|
| gratis (0 s) | +30,5 ± 13,6 | 2,2 | 14/24 | 5 174 vs 4 443 |
| 10 s | +27,2 ± 7,2 | 3,8 | 17/24 | 4 292 vs 3 638 |
| 25 s | +13,1 ± 6,7 | 2,0 | 15/24 | 4 330 vs 4 016 |
| 40 s | +5,3 ± 9,0 | 0,6 | 13/24 | 4 386 vs 4 259 |
| 60 s | +23,8 ± 8,1 | 2,9 | 17/24 | 4 705 vs 4 133 |

- **Ningún precio gana a otro de forma clara** (la mayor diferencia entre precios
  es 25,2 ± 14,6 descargas, t = 1,7): el barrido no corona un precio, dice dónde
  la ventaja de la puerta es ROBUSTA y dónde no lo es.
- **A 10 s es donde más se repite**: t = 3,8, el mayor, 17/24 semillas y ningún
  desastre (la peor semilla pierde 15). Es también el presupuesto que más duele
  —el reflejo fijo baja a 151,6 de media contra 185,1 gratis, −18 %— y la puerta
  recupera 27 de esas 33 descargas. Si hubiera que desplegar un precio hoy, es
  este —con el matiz de F11c, más abajo: sirve para ENTRENAR bajo él, y sin la
  puerta.
- **A 60 s también paga** (t = 2,9), pero por otra razón: con 60 s de depósito
  acumulado el presupuesto casi no ata y el mundo se parece al gratis, que sigue
  siendo el que más rinde en absoluto (215,6 con puerta).
- **A 40 s se desdibuja** (+5,3, t = 0,6). No es significativamente peor que los
  otros precios y no hay mecanismo que explique un valle justo ahí: se lee como
  ruido, no como óptimo intermedio.
- **El precio no rescata la puerta**: en el pool entrenado sin ella, la puerta
  aporta entre −14 y +10 a todos los precios, ninguno con t > 1. Lo que hace que
  la puerta valga sigue siendo haberla entrenado, no el precio elegido.
Con 24 semillas el retrato de 8 se suaviza, y conviene decirlo: a 25 s la puerta
gana +13,1 ± 6,7, no +30 ± 11, y el precio le cuesta al reflejo 10 % (185,1 →
167,3), no 19 %. La dirección se mantiene; el tamaño medido con 8 semillas era
optimista.
**F11c — el precio emparejado (2026-10-25): entrenar a 10 s hunde la puerta y
mejora el reflejo.** La duda que dejaba el barrido era si un cerebro que aprende
a racionar 10 s rinde más que uno que aprendió a racionar 25. Se repitió la
tirada entera con el precio emparejado (`GLAND=10 SUFFIX=p10
scripts/run-gland-200.sh`: 200 generaciones por brazo, mismo warm-start, semilla
7, pop 32, 3 × 24 000 ticks por genoma, 1 h 45 min en paralelo, los dos brazos
con EXIT=0) y los dos pools nuevos se jugaron a los DOS precios con las 24
semillas que la selección no vio (`scripts/gland-bench.py --gland 10|25
--pool-gate … --pool-fixed …`: 384 partidas; veredicto completo en
`artifacts/pretrain-partida-200-p10-veredicto.txt`). En la selección el brazo con
puerta se hunde: **61 213** de media contra **70 053** del reflejo a 10 s
(−8 840 por generación, sd 279, t = −31,7; pierde 200/200 generaciones). Es el
mismo signo que F11 con el rastro gratis y el contrario que F11b a 25 s
(+4 097, t = 16,6): con 10 s de presupuesto la puerta no tiene nada que ganar y
sí mucho que perder.

| entrena | juega | puerta+puerta | puerta+fijo | fijo+puerta | fijo+fijo |
|---|---|---|---|---|---|
| 10 s | 10 s | 159,5 | 144,3 | 179,1 | **189,0** |
| 10 s | 25 s | 156,9 | 150,3 | 190,2 | **200,7** |
| 25 s | 10 s | **178,8** | 151,6 | 171,9 | 161,5 |
| 25 s | 25 s | **180,4** | 167,3 | 163,8 | 157,6 |

- **Entrenar a 10 s es PEOR con la puerta**: mismo cerebro y mismo precio de
  juego, el pool entrenado a 10 s rinde 19,4 ± 6,9 descargas menos jugando con la
  puerta a 10 s (gana 6/24 semillas) y 23,5 ± 6,9 menos a 25 s (5/24).
- **Y es MUCHO mejor sin ella**: el reflejo entrenado a 10 s da 27,4 ± 7,9 más
  jugando a 10 s (16/24) y 43,1 ± 8,9 más a 25 s (20/24). Es el mejor pool medido
  hasta ahora en los dos precios (189,0 y 200,7), por encima del campeón de F11b
  (178,8 y 180,4).
- **La puerta dentro de su propio pool sigue pagando, pero menos**: +15,1 ± 5,4
  a 10 s (t = 2,8, 16/24) y +13,1 ± 6,7 a 25 s (t = 2,0).
- **De punta a punta a 10 s la puerta pierde**: −29,5 ± 6,2 (gana 4/24) cuando los
  dos pools se entrenaron a 10 s; a 25 s sigue ganando +22,8 ± 8,2 (17/24).

Lo que compra rendimiento no es la puerta: es **entrenar con el rastro caro**.
Con 10 s el reflejo deja rastros más flacos —el presupuesto los trunca—, la
colonia no puede apoyarse tanto en la feromona y la selección acaba premiando
hormigas que forrajean mejor por su cuenta; al devolverles 25 s rinden más que
las entrenadas con 25 desde el principio. La puerta, en cambio, necesita
presupuesto holgado para tener algo que racionar: a 10 s desvía la selección a
una cuenca peor, como en F11. Aviso de tamaño: cada celda de esa tabla es UNA
tirada de 200 generaciones (semilla de GA 7); las comparaciones pareadas por
semilla de juego son sólidas para ESTOS pools, pero «entrenar a 10 s sin la
puerta» necesitaría réplicas con otras semillas de GA antes de ser receta.
Esas réplicas están hechas en F11d, justo abajo, y cambian la lectura: el 200,7
era el mejor sorteo de cuatro, no el nivel de la receta, y la ventaja de entrenar
a 10 s no sobrevive al ruido de reentrenar.

**F11d — las réplicas (2026-10-26): el pool campeón era la tirada más afortunada
de cuatro.** El aviso de tamaño de F11c se pagó: la misma receta —200
generaciones a 10 s **sin** la puerta, mismo warm-start, pop 32, 3 × 24 000 ticks
por genoma— repetida con tres semillas de GA más (`SEED=11 ARMS=fijo GLAND=10
SUFFIX=p10s11 scripts/run-gland-200.sh`, y lo mismo con 13 y 23: 2 h 33 min las
tres en paralelo, las tres con EXIT=0), y los cuatro pools jugados a los dos
precios con las mismas 24 semillas no vistas (`scripts/gland-bench.py --pools
p10-s7=… p10-s11=… p10-s13=… p10-s23=… --ref p10-s7 --prices 10,25`: 384
partidas; evidencia en `artifacts/pretrain-partida-200-p10-replicas.txt`). Como
`--seed` siembra también las partidas de la selección —las dos cosas salen del
mismo entero—, cada réplica cubre el sorteo entero: el GA y los mundos en que
puntúa.

| pool (10 s, sin puerta) | selección (descargas/gen, últimas 20) | jugado 10 s | jugado 25 s |
|---|---|---|---|
| semilla 7 | 97,8 | 189,0 | **200,7** |
| semilla 11 | 94,2 | 140,5 | 145,2 |
| semilla 13 | 106,1 | 180,6 | 189,3 |
| semilla 23 | 95,9 | 187,0 | 190,9 |
| **media de las cuatro** | — | **174,3 ± 11,4** | **181,5 ± 12,4** |

Cifras con el reflejo fijo, que es como se entrenaron los cuatro; ± es el error
típico de la media de las cuatro y la dispersión entre pools es sd 22,8 (10 s) y
24,7 (25 s).

- **La semilla 7 es la mejor de las cuatro en los dos precios**, y esa es justo
  la ventaja que F11c leyó como ventaja de la receta: las otras tres dan
  −14,7 ± 5,3 descargas pareadas a 10 s (ganan 6/24 semillas) y −19,1 ± 5,4 a
  25 s (4/24) frente a ella.
- **Dos pools de la MISMA receta se llevan 48,5 descargas a 10 s** (189,0 contra
  140,5) y 55,5 a 25 s (200,7 contra 145,2). La sd de reentrenar (23–25) es del
  tamaño de los efectos que F11c midió (+27,4 y +43,1): aquello era un sorteo
  contra otro, no una receta contra otra.
- **Frente a los pools de F11b (entrenados a 25 s) la media queda a la par del
  campeón** con puerta —181,5 contra 180,4 a 25 s, 174,3 contra 178,8 a 10 s— y
  por encima del brazo sin puerta (157,6 / 161,5), pero con UNA sola tirada
  enfrente y ±12 de error: no confirma ni descarta que entrenar a 10 s sea
  mejor. Lo que sí queda claro es que 200,7 no es el nivel de la receta, sino su
  mejor sorteo; el nivel ronda 180.
- **La puerta no aporta nada en estos pools** —entrenados sin ella—: +0,9 de
  media a 10 s y +0,2 a 25 s (rangos −9,8…+13,6 y −10,4…+14,8), consistente con
  el +10 ± 10 que F11c ya daba por no significativo en ese brazo. La puerta paga
  en los pools que la entrenaron (F11b/F11c), no en estos.
- **La selección ordena mal el juego**: la semilla 13 lideró la selección (106,1
  descargas por generación en las últimas 20) y quedó segunda jugando (180,6);
  la 7 fue segunda en selección (97,8) y primera jugando (189,0). El rango de
  selección (94–106) es mucho más estrecho que el de juego (140–189).

Veredicto práctico: desplegar el pool de la semilla 7 sigue siendo defendible
—es el mejor de cuatro sorteos—, pero «entrenar a 10 s sin la puerta» no puede
venderse como mejor receta: para eso faltan réplicas del brazo de 25 s, que
están hechas en F11e, justo abajo, y no la cierran. Los `*.antgenome` de las
réplicas son locales (están en `.gitignore`); lo versionado es el cómo (`SEED`,
`ARMS`) y la medición.

**F11e — el precio de entrenamiento, réplica a réplica (2026-10-26): 10 s gana
en 3 de 4 sorteos, y eso no basta para llamarlo receta.** Faltaban las réplicas
del otro brazo y ya están: las mismas semillas de GA (7, 11, 13 y 23) pero con la
glándula a **25 s** durante el entrenamiento (`GLAND=25 ARMS=fijo SEED=…
SUFFIX=p25s… scripts/run-gland-200.sh`, 2 h 32 min las tres nuevas en paralelo,
las tres con EXIT=0). Los cuatro pools nuevos se jugaron con las mismas 384
partidas que los de F11d —4 pools × 2 precios × 2 puertas × 24 semillas no
vistas— y la comparación entre recetas se parea **por semilla de GA**, que es lo
que comparten: mismo sorteo y mismos genomas de arranque, sólo cambia el precio
del rastro al entrenar (`scripts/gland-recipe-compare.py`; evidencia en
`artifacts/pretrain-partida-200-p25-replicas.txt` y los dos veredictos
`…-p25-veredicto.txt` y `…-p25-veredicto-puerta.txt`).

| receta (sin puerta) | jugado 10 s | jugado 25 s |
|---|---|---|
| entrenada a 10 s (4 tiradas) | **174,3** (140–189) | **181,5** (145–201) |
| entrenada a 25 s (4 tiradas) | **152,6** (119–184) | **153,3** (122–185) |

- **La media favorece a 10 s en los dos precios**: +21,6 ± 3,0 descargas pareadas
  por semilla de partida a 10 s (gana 24/24) y +28,2 ± 4,7 a 25 s (23/24). Los
  mismos signos que F11c leyó con una tirada por brazo, con la magnitud
  recortada (F11c: +27,4 y +43,1).
- **Pero por tirada de GA el efecto se sostiene en 3 de 4**: +27, −43, +34, +68 a
  10 s y +43, −40, +40, +69 a 25 s. La sd entre tiradas es 46–47, el doble de la
  media, así que el error típico de la media de cuatro es ±23 y t ≈ 1 (0,93 y
  1,19). A ese ritmo harían falta decenas de pares de réplicas (~36) para que la
  diferencia llegue a t = 2, no las dos o tres que estimaba F11d: «entrenar a
  10 s» queda como indicio repetido, no como receta demostrada.
- **La semilla 11 invierte el signo, y lo hace en las cuatro celdas** (−40…−45):
  es justo la tirada en que el pool de 10 s es el peor de su receta (140,5) y el
  de 25 s el mejor de la suya (183,5). La interacción tirada × precio se come el
  efecto principal.
- **Con la puerta encendida el retrato es el mismo**: 175,1 contra 157,9 jugando
  a 10 s (+17,2 ± 24,5 por tirada, 3/4) y 181,7 contra 158,8 a 25 s (+22,9 ±
  25,4, 3/4).
- **En los pools entrenados a 25 s la puerta paga un poco**, y es lo único que
  cambia de signo respecto a F11d: +10,3 / +4,2 / +2,2 / +4,3 a 10 s de juego y
  +6,2 / +8,5 / +0,9 / +6,0 a 25 s, los ocho positivos pero pequeños (media
  +5,3, el mayor con t = 1,4). En los entrenados a 10 s sigue mezclada (−9,8…
  +13,6 jugando a 10 s, −10,4…+14,8 a 25 s). Es plausible que un presupuesto
  entrenado holgado le deje a la puerta algo que racionar; con cuatro tiradas no
  pasa de sospecha.

Veredicto: el precio del rastro durante el entrenamiento **no** es la palanca
que F11c sugería. Mueve 20–28 descargas de media a favor de 10 s, pero dos
sorteos de la misma receta se llevan hasta 65: elegir la tirada pesa más que
elegir el precio, y la forma barata de aprovecharlo es **seleccionar el pool por
la partida completa** —varias tiradas, quedarse la que mejor juega— en vez de
confiar en una. El aviso vale también para F11b: su campeón con puerta es UNA
tirada por brazo, y esta campaña muestra que un sorteo se lleva ±45. Para
sostener «la puerta paga» con el rastro caro faltan réplicas del brazo CON
puerta; ese es el experimento que queda abierto, y está hecho en F11f, justo
abajo.

**F11f — la puerta, réplica a réplica (2026-10-26): el campeón de F11b es su
mejor sorteo, y lo que compra la receta con puerta es suelo, no techo.** Se
repitieron las tres tiradas que le faltaban al brazo CON puerta: la misma receta
—200 generaciones a 25 s, mismo warm-start, pop 32, 3 × 24 000 ticks por
genoma— con las semillas de GA 11, 13 y 23 (`GLAND=25 ARMS=red SEED=…
SUFFIX=p25g… scripts/run-gland-200.sh`: 2 h 30 min las tres en paralelo, 9 005 /
9 001 / 9 017 s, las tres con EXIT=0), y los cuatro pools jugados con las mismas
384 partidas de siempre —4 pools × 2 precios × 2 puertas × 24 semillas no
vistas— (`scripts/gland-bench.py --pools p25g-s7=… p25g-s11=… p25g-s13=…
p25g-s23=… --ref p25g-s7 --prices 10,25`; evidencia en
`artifacts/pretrain-partida-200-p25g-replicas.txt`).

| pool (25 s, con puerta) | selección (descargas/gen, últimas 20) | jugado 10 s | jugado 25 s |
|---|---|---|---|
| semilla 7 (el campeón de F11b) | 100,3 | **178,8** | **180,4** |
| semilla 11 | 104,0 | 147,3 | 155,0 |
| semilla 13 | 95,4 | 170,8 | 171,7 |
| semilla 23 | 97,0 | 157,2 | 163,4 |
| **media de las cuatro** | — | **163,5** | **167,6** |

Cifras con la puerta ENCENDIDA al jugar, que es como se entrenaron; los mismos
cuatro pools con el reflejo fijo dan 152,8 (10 s) y 162,6 (25 s).

- **El campeón es el primer sorteo de cuatro, por un margen normal**: 178,8 y
  180,4 con la puerta puesta, +8,1 y +8,7 sobre el segundo, a z ≈ +1,1 de la
  media de sus cuatro tiradas (dispersión 11–14) — un máximo de cuatro sorteos
  suele estar a +1,0. Así que 180,4 no es un milagro ni el nivel de la receta: el
  nivel es 167,6 jugando con ella a 25 s, y el campeón lo exagera en 13.
- **Con el reflejo fijo el campeón cae a media tabla de su propia receta**:
  167,3 (segundo, −1,8) a 25 s y 151,6 (tercero, −15,2) a 10 s, z = −0,08. El
  pool de F11b es campeón sólo con la puerta puesta: su ventaja vive en la
  interacción, no en el pool.
- **La receta con puerta no tiene el techo más alto, tiene el suelo**: entre
  tiradas se mueve 8,8–14,8 (rango 149,7–169,1 con el reflejo a 25 s) mientras la
  receta sin puerta se mueve 26,1–27,0 (rango 121,5–185,0). El mejor sorteo de la
  receta sin puerta (185,0 / 193,5) le gana al mejor de la receta con puerta
  (180,4), pero su peor sorteo cae a 121,5 cuando el de la puerta no baja de
  149,7: para desplegar UN pool, como es el caso, la receta con puerta es la
  apuesta con menos varianza.
- **Receta contra receta, pareado por tirada de GA** (`scripts/
  gland-recipe-compare.py`; veredictos en `…-p25g-vs-p25-fijo.txt` y
  `…-puerta.txt`): +9,2 ± 3,0 descargas a 25 s con el reflejo (t = 3,1; 22/24
  semillas) y +8,9 ± 3,4 con la puerta puesta (t = 2,6; 19/24); a 10 s se queda en
  +0,2 (t = 0,1) y +5,6 (t = 1,4). El signo se sostiene en 3 de 4 sorteos en las
  cuatro celdas, y la que invierte es otra vez la semilla 11 (−35…−51), la misma
  que domina F11e.
- **La puerta paga dentro del pool, ahora también en los que no la entrenaron**
  (`scripts/gland-gate-replicas.py`; evidencia en
  `artifacts/pretrain-partida-200-p25g-champion.txt`): sumando los OCHO
  entrenamientos independientes de 25 s —cuatro por receta— jugar el depósito con
  el cerebro vale +8,0 ± 3,3 descargas a 10 s de precio (t = 2,4; 7/8 tiradas a
  favor) y +5,2 ± 1,6 a 25 s (t = 3,4; 7/8). Por receta: +10,7 (10 s) y +5,1
  (25 s) en los entrenados con puerta (t = 2,9 / 1,7), y +5,3 / +5,4 en los
  entrenados con el reflejo (t = 1,9 / 1,4). La sospecha de F11e —que en los
  pools de 25 s la puerta aportaba un poco— sobrevive en el agregado, con el
  aviso de que los ocho comparten las 24 semillas de partida.
- **La selección vuelve a ordenar mal el juego**: el pool que lideró la selección
  (semilla 11, 104,0 descargas por generación) es el PEOR jugando (155,0) y el
  que fue segundo (semilla 7, 100,3) es el mejor (180,4). El rango de selección
  (95–104) es tres veces más estrecho que el de juego (155–180).

Veredicto: F11b acertaba el signo y exageraba el tamaño. Con el rastro caro,
entrenar con la puerta puesta deja una receta cuyo nivel es ~168 —jugando con la
puerta a 25 s— y cuyo peor sorteo de cuatro sigue 28 descargas por encima del
peor sorteo de la receta del reflejo; y la puerta, además, paga al jugar en 7 de
8 entrenamientos, incluidos los que no la entrenaron. Lo que no puede venderse es
«el pool de la puerta da 180»: eso es su mejor sorteo, y el mismo sorteo puesto a
jugar con el reflejo fijo baja a 167. Queda abierto si con más sorteos el nivel
de la receta con puerta se separa del de la receta sin puerta —+9 con t ≈ 3 sobre
cuatro tiradas, con una que invierte— o si las dos convergen hacia 160; eso es
F11g, justo abajo.

**F11g — la comparación entre recetas con siete tiradas (2026-10-27): el signo
aguanta, el tamaño se queda en la mitad y las dos recetas convergen a ~160.** Se
repitió la campaña de F11f con tres sorteos nuevos por receta —semillas de GA 29,
31 y 37, ninguna usada en F11b/F11d/F11f— en las DOS recetas a la vez, con la
receta de siempre (200 generaciones a 25 s, mismo warm-start, pop 32, 3 ×
24 000 ticks por genoma; `GLAND=25 ARMS=red|fijo SEED=… SUFFIX=p25g…|p25s…
scripts/run-gland-200.sh`, las seis tiradas en paralelo: 20 108 / 20 110 /
20 110 / 20 110 / 20 110 / 20 109 s, las seis con EXIT=0), y los siete pools de
cada receta se jugaron con las mismas 24 semillas no vistas —14 pools × 2 precios
× 2 puertas × 24 semillas = 1 344 partidas— (`scripts/gland-bench.py --pools …
--prices 10,25`, una campaña por receta; evidencia en
`artifacts/pretrain-partida-200-p25g7-replicas.txt` y `…-p257-replicas.txt`).

| receta (7 tiradas) | ¿entrenada con puerta? | jugado 10 s | jugado 25 s |
|---|---|---|---|
| con puerta | puerta | 160,3 (144–179) | **166,3** (155–180) |
| con puerta | reflejo | 154,1 (133–171) | 161,7 (147–177) |
| sin puerta | puerta | 158,0 (124–188) | 162,2 (128–194) |
| sin puerta | reflejo | 154,9 (119–184) | 155,6 (122–185) |

- **El hallazgo de F11f se queda en la mitad y pierde el t = 3**: la comparación
  pareada por semilla de partida (24) da, receta con puerta menos receta sin
  puerta, +6,1 ± 2,4 descargas a 25 s con el reflejo (t = 2,5; 19/24 semillas) y
  +4,1 ± 3,2 con la puerta puesta (t = 1,3; 17/24); a 10 s se queda en −0,8 ± 2,1
  y +2,3 ± 2,4. Con cuatro tiradas los mismos números eran +9,2 ± 3,0 (t = 3,1),
  +8,9 ± 3,4 (t = 2,6), +0,2 y +5,6: **siete de los doce pares nuevos caen por
  debajo de la media de su celda** y el efecto se recorta sin desaparecer.
- **Por tirada de GA el signo se sostiene en 5 de 7 pares** en la celda del
  reflejo a 25 s y en 4 de 7 en la de la puerta —y en 5/7 y 3/7 a 10 s—. La
  semilla 11 —la que invierte desde F11e— vuelve a ir en contra
  en las dos celdas de 25 s (−35 y −38); sin ella, los otros seis pares van
  0…+43 con el reflejo y −9…+36 con la puerta.
- **El campeón de F11b sigue siendo el primero de siete, con la puerta puesta**
  (180,4 y 178,8, +8,7 y +8,1 sobre el segundo, z = +1,64 y +1,46; un máximo de
  siete sorteos suele estar a +1,35). Con el reflejo fijo cae a media tabla de su
  propia receta: 167,3 (puesto 3/7, z = +0,52) a 25 s y 151,6 (5/7, z = −0,19) a
  10 s. Que un sorteo gane en las dos celdas donde el entrenamiento y el juego
  comparten la puerta, y en ninguna de las otras dos, es la interacción que F11f
  describió, ahora con siete tiradas detrás.
- **Lo que compra la receta con puerta sigue siendo suelo, con menos margen del
  que parecía**: la dispersión entre sus tiradas es 8,6–13,2 donde la receta del
  reflejo se mueve 18,7–20,3 (con cuatro tiradas eran 8,8–14,8 contra 26,1–27,9:
  el hueco se estrecha porque los tres sorteos nuevos del reflejo son de media
  tabla, no porque la puerta se estire). Su peor sorteo de los siete queda en
  155,0 con la puerta a 25 s y 146,7 con el reflejo (144,0 y 132,8 a 10 s),
  contra 127,5 y 121,5 del peor sin puerta con la puerta, y 123,7 y 119,3 con el
  reflejo.
- **La puerta paga al jugar, ahora sobre CATORCE entrenamientos independientes**
  (`scripts/gland-gate-replicas.py`; evidencia en
  `artifacts/pretrain-partida-200-p25g7-champion.txt`): +4,7 ± 2,3 descargas a
  10 s de precio (t = 2,0; 10/14 tiradas a favor) y +5,6 ± 1,9 a 25 s (t = 2,9;
  12/14). Por receta: +6,2 (10 s) y +4,6 (25 s) en los siete entrenados con
  puerta, +3,1 y +6,6 en los siete entrenados con el reflejo. Los ocho de F11f
  daban +8,0 y +5,2: el agregado a 25 s aguanta (t ≈ 3), el de 10 s se recorta a
  la mitad.
- **La selección vuelve a ordenar mal en la receta con puerta**: el pool que
  lideró la selección de sus siete (semilla 11, 104,0 descargas por generación)
  es el PEOR jugando (155,0 con la puerta a 25 s) y el que iba segundo (semilla
  31, 102,0) queda en media tabla (163,3 con la puerta, 176,9 con el reflejo). El
  rango de selección de los siete (95,4–104,0) sigue siendo tres veces más
  estrecho que el de juego (155,0–180,4). En la receta del reflejo el orden es
  algo mejor —sus dos extremos de selección (84,9 y 101,3) son su peor y su mejor
  jugada, 127,5 y 193,5— pero el rango de selección (84,9–102,3) es igual de
  estrecho frente a 127,5–193,5 de juego.

Veredicto: la pregunta que F11f dejó abierta se responde con la segunda mitad —
**las dos recetas convergen**, no se separan. Con siete sorteos cada una, el nivel
con puerta puesta al juego es 166,3 contra 162,2 (con el reflejo, 161,7 contra
155,6) y ninguna celda llega a t = 3: la ventaja sigue del lado de la receta con
puerta en tres de las cuatro celdas, con la mitad del tamaño que F11f midió y con
la semilla 11 en contra. Lo que no encoge es lo que sostiene la decisión de
despliegue: los siete sorteos de la receta con puerta no bajan de 146,7 (155,0
jugando con ella) mientras los del reflejo llegan a 119,3, y la puerta del
cerebro paga al jugar en 12 de 14 entrenamientos (t = 2,9 a 25 s). Para cerrar
el eje del todo haría falta lo mismo
que F11e pedía para el precio: más réplicas, no más partidas; eso es F11h, justo
abajo.

**F11h — el brazo de 10 s, de cuatro a siete tiradas (2026-10-27): el precio
sigue pagando en la receta del reflejo —la mitad de lo que decía F11e— y en la
de la puerta se vuelve un castigo de 12 a 18 descargas.** Se añadieron las mismas
tres semillas de GA nuevas (29, 31, 37) a las DOS recetas del brazo de 10 s, con
la receta de siempre pero `GLAND=10 REFILL=6` (`ARMS=red|fijo SEED=…
SUFFIX=p10g…|p10s… scripts/run-gland-200.sh`; las seis en paralelo: 17 615 /
17 674 / 17 702 / 17 615 / 17 678 / 17 699 s, las seis con EXIT=0), y se jugaron
los pools con las 24 semillas de siempre —7 pools sin puerta × 2 precios × 2
puertas × 24 + 4 pools con puerta × lo mismo = 1 056 partidas—
(`scripts/gland-bench.py --pools … --prices 10,25`; evidencia en
`artifacts/pretrain-partida-200-p107-replicas.txt` y `…-p10g4-replicas.txt`). El
lado CON puerta no llega a siete: a 10 s solo existía el pool de F11c, así que
sus tres nuevas lo llevan a CUATRO y ese lado del eje se compara sobre cuatro
sorteos, no sobre siete.

| entrenada a 10 s | jugado 10 s | jugado 25 s |
|---|---|---|
| sin puerta (7 tiradas) | 165,1 (114–189) | **168,7** (115–201) |
| con puerta (4 tiradas) | 154,1 (147–159) | 154,4 (150–159) |

- **En la receta del reflejo el precio sigue pagando, con la mitad del tamaño y
  con la mitad de sorteos a favor**: 10 s menos 25 s da +10,2 ± 2,6 descargas
  jugando a 10 s (t = 4,0; 18/24 semillas) y +13,1 ± 3,4 jugando a 25 s
  (t = 3,8; 18/24); F11e medía +21,6 ± 3,0 y +28,2 ± 4,7. Por tirada de GA gana
  en 5 de 7 en las dos celdas. Pero el pareado por SORTEO (que es la unidad
  independiente: la sd entre tiradas es 42) deja t ≈ 0,6 y 0,8 — el precio sigue
  siendo palanca de segundo orden frente a elegir la tirada, ahora con siete
  sorteos y no con cuatro.
- **Los dos sorteos que pierden son distintos de los de F11e**: la semilla 11
  vuelve a ir en contra (−43 y −40), pero ahora la acompaña la 37 (−47 y −42),
  que es un entrenamiento malo de verdad —su selección se hunde a 85,6 descargas
  por generación donde las otras tres tiradas del brazo están en 97,8–100,5, y
  su fitness medio de las últimas 20 generaciones es 61 796 contra 77 512–79 714
  de las otras tres—. Es la primera vez que la métrica de selección avisa de algo
  que luego se
  ve jugando, y no la salva: en el mismo brazo el pool de la puerta de F11c
  tiene la selección más baja (87,8) y es el que mejor juega.
- **En la receta de la puerta el precio se invierte y ya no es una tirada
  afortunada**: 10 s menos 25 s da −6,7 ± 2,2 (t = −3,1) y −8,7 ± 2,1 (t = −4,1)
  con el reflejo al jugar, y −6,2 ± 3,2 y −11,9 ± 2,6 (t = −4,5) con la puerta;
  gana 10 s en 1 de 4 sorteos, y en la celda de 25 s con la puerta en 0 de 4. La
  señal no es una tirada mala: la dispersión entre sus cuatro tiradas es 3,2–5,1
  descargas (contra 8,6–13,2 en la receta con puerta de 25 s), es decir las
  cuatro salen igual de mediocres. F11c decía −19 y −23 con UNA tirada por
  precio; con cuatro la mitad del castigo es real y el resto era el sorteo.
- **La interacción es el hallazgo**: la puerta necesita el presupuesto holgado.
  Al MISMO precio de entrenamiento, la puerta cuesta 15,8 ± 3,5 descargas con el
  reflejo al jugar (t = −4,5; 5/24 semillas) y 12,8 ± 2,8 con la puerta (t =
  −4,6) cuando se entrena a 10 s —y 3 de 4 sorteos lo confirman—, mientras que a
  25 s la puerta GANA +4…+6 (F11g). Y un pool entrenado a 10 s tampoco sabe
  aprovecharla al jugar: con 11 pools, la puerta paga +3,4 ± 2,3 a 10 s de
  precio (t = 1,5; 8/11) y −0,5 ± 2,6 a 25 s (t = −0,2; 5/11), justo lo
  contrario de los 14 pools entrenados a 25 s (+5,6 ± 1,9, t = 2,9; F11g).
- **El campeón de F11c aguanta a medias**: p10-s7 sigue siendo el primero de
  siete con el reflejo al jugar (189,0 a 10 s y 200,7 a 25 s; z = +0,85 y
  +1,06) pero con la puerta puesta cae al segundo, donde la semilla 23 le saca
  15–21 descargas (200,5 y 205,7). Su z es bajo aunque su ventaja absoluta sea
  grande porque la receta de 10 s se mueve 27–30 descargas entre tiradas: el
  doble de lo que se mueve la receta con puerta de 25 s.

Veredicto: el eje del precio de entrenamiento queda medido con siete pares en el
lado que importaba y con cuatro en el otro, y lo que dice es más específico que
«10 s entrena mejor»: **10 s paga en la receta del reflejo (+10 a +13, la mitad
de lo que F11e estimó) y castiga en la receta de la puerta (−7 a −12, y 12 a 18
contra el reflejo al mismo precio)**. La puerta del cerebro no es una salida
que se pueda regalar: con presupuesto corto el cerebro aprende a no depositarla
y el pool sale plano; con presupuesto holgado, y sólo entonces, la puerta paga
al jugar. Queda abierto cuánto de eso es la glándula y cuánto el horizonte de
selección (24 000 ticks, 400 s): a 10 s de glándula el depósito cuesta cuatro
veces menos por evento de lo que cuesta a 25 s, y el fitness no lo nota porque
sólo cuenta descargas.

**F11i — la celda corta llega a siete tiradas (2026-10-28): el castigo del
precio en la receta de la puerta se confirma y crece, y el campeón de F11c
pierde el primer puesto.** F11h dejó el brazo de 10 s con cuatro tiradas en la
receta de la puerta y siete en la del reflejo, así que esa celda estimaba su
media con cuatro sorteos. Aquí se añaden las mismas tres semillas de GA (41, 43
y 47) a las DOS recetas —sembrar sólo un lado no añadiría ni un par, sólo
desplazaría una media sin con qué emparejarla— con la receta de siempre y
`GLAND=10 REFILL=6` (`ARMS=red|fijo SEED=… SUFFIX=p10g…|p10s…
scripts/run-gland-200.sh`; las seis en paralelo: 18 149 / 18 100 / 18 131 /
18 099 / 18 102 / 18 097 s, las seis con EXIT=0), y se jugaron los pools con las
24 semillas de siempre —10 pools sin puerta × 2 precios × 2 puertas × 24 + 7
pools con puerta × lo mismo = 1 632 partidas— (`scripts/gland-bench.py --pools …
--prices 10,25`; evidencia en `artifacts/pretrain-partida-200-p10x10-replicas.txt`
y `…-p10g7-replicas.txt`, el contraste entre recetas en `…-p10x10-vs-p257.txt` y
`…-p10g7-vs-p25g7.txt`, y el efecto de la puerta en `…-p10x10-champion.txt`).
Las tres tiradas nuevas son de tabla media-alta —190,0 / 161,3 / 150,6 con el
reflejo al jugar a 10 s— así que no mueven el nivel del eje; lo que hacen es
llevar cada celda de 10 s a siete.

| entrenada a 10 s | jugado 10 s | jugado 25 s |
|---|---|---|
| sin puerta (10 tiradas) | 165,7 (114–190) | **170,2** (115–202) |
| con puerta (7 tiradas) | 153,1 (144–162) | 154,5 (146–159) |

- **El precio en la receta del reflejo se sostiene y crece un poco**: 10 s menos
  25 s da +10,9 ± 2,3 descargas jugando a 10 s (t = +4,7; 21/24 semillas) y
  +14,6 ± 3,2 jugando a 25 s (t = +4,6; 20/24); F11h medía +10,2 ± 2,6 y
  +13,1 ± 3,4, con 18/24 semillas en las dos celdas. Por tirada de GA gana en
  5 de 7 otra vez, y los dos sorteos en contra siguen siendo la 11 (−43 y −40) y
  la 37 (−47 y −42).
- **El castigo en la receta de la puerta se confirma y es algo más grande, no
  menor**: 10 s menos 25 s da −7,3 ± 3,1 descargas jugando a 10 s (t = −2,3;
  6/24 semillas) y −11,7 ± 2,6 jugando a 25 s (t = −4,5; 4/24); F11h daba
  −6,7 ± 2,2 y −8,7 ± 2,1. Por tirada de GA gana 10 s en 1 de 4 sorteos —los
  nuevos 41/43/47 no existen a 25 s— y en la celda de 25 s en 0 de 4. Lo que
  cambia con las tres tiradas nuevas es la DISPERSIÓN: con cuatro se estimaba
  3,2–5,1 descargas dentro de la receta y con siete es 6,5 en la celda de 10 s y
  8,1 en la de 25 s, así que el efecto es mayor pero su t de 10 s baja: las
  tiradas no salen tan iguales como parecía.
- **La puerta sobre las DIECISIETE tiradas de 10 s llega a t ≈ 2**: al jugar a
  10 s paga +3,1 ± 1,5 descargas (t = +2,1; 13/17 tiradas a favor) y al jugar a
  25 s −0,4 ± 2,2 (t = −0,2; 6/17); con once pools F11h medía +3,4 ± 2,3
  (t = 1,5; 8/11). El signo es el mismo y ahora es nominalmente significativo,
  pero sigue siendo la mitad del efecto de entrenar a 25 s (+5,6 ± 1,9, t = 2,9;
  F11g): «un pool entrenado a corto no sabe aprovechar la puerta» se suaviza a
  «la aprovecha a medias».
- **El campeón de F11c pierde el primer puesto, y quien manda ahora es un
  sorteo, no una receta**: con diez tiradas, p10-s7 es SEGUNDO con el reflejo
  (189,0 a 10 s y 200,7 a 25 s; z = +0,93 y +1,10, a 1,0 descarga del líder en
  las dos celdas) y TERCERO con la puerta (179,1 y 190,2; z = +0,48 y +0,76). La
  semilla 41 —una de las tres nuevas— encabeza tres de las cuatro celdas de
  10 s (190,0 y 201,7 entrenando sin puerta, 161,6 entrenando con ella) y es
  tercera en la cuarta por 0,4. Su z es +0,98 en su mejor celda: un máximo de
  diez sorteos suele estar a +1,54, así que el nuevo líder tampoco se separa del
  pelotón —lo que se mueve la receta entre tiradas (24,9 de dispersión) es el
  doble de lo que separa a las dos recetas (10,9)—.
- **La selección acierta los extremos y no el resto**: sobre las diez tiradas
  del reflejo, el rango de selección va de 61,8k a 82,8k (media de las últimas 20
  generaciones) y el de juego de 114,0 a 190,0. El peor en las dos cosas es la
  semilla 37 (61,8k → 114,0) y el peor jugando de los demás también está abajo
  en selección (la 11: 72,5k → 140,5); el mejor jugando es la 41, segunda en
  selección (82,1k). Pero las seis primeras de selección (75,5–82,8k) juegan
  entre 150,6 y 190,0: dentro de la cabeza, el orden de selección es ruido.

Veredicto: el eje del precio queda ahora con siete tiradas en las DOS celdas de
10 s y el castigo de la receta de la puerta no era un artefacto de cuatro
sorteos —**al revés: con siete es mayor (−7 a −12)— y el precio sigue pagando en
la del reflejo, ahora con más semillas de partida a favor (21/24 y 20/24, donde
F11h veía 18/24 en las dos celdas)**. Lo que el eje NO puede separar sigue siendo lo mismo que F11h
declaró abierto: a 10 s de glándula el depósito cuesta cuatro veces menos por
evento y la selección sólo mira 400 s de una partida de 3 600, y las dos cosas
van juntas en el mismo brazo. Eso es lo que mide el control de F11j, que sube
sólo el horizonte a 800 s sin tocar el precio.

**F11j — el control del horizonte (2026-10-29): el horizonte de selección no
explica el castigo de la puerta a 10 s — doblarlo lo agranda.** F11h dejó
abiertas dos causas con la misma pinta en los pools de 10 s: el PRECIO de la
glándula (a 10 s el depósito cuesta cuatro veces menos por evento, y un fitness
que sólo cuenta descargas no puede ver ese coste) y el HORIZONTE de selección
(24 000 ticks = 400 s de una partida de 216 000 = 3 600 s, así que la ventaja de
reclutar bien puede caer fuera de lo que la selección mira). Este control mueve
sólo la segunda: la misma receta con `GLAND=10 REFILL=6` y `TICKS=48000`
(`GLAND=10 REFILL=6 TICKS=48000 ARMS=red|fijo SEED=… SUFFIX=p10h2g…|p10h2s…
scripts/run-gland-200.sh`; dos tandas de tres —03:38→12:39 con puerta y
12:39→23:53 con el reflejo—: 32 454 / 32 453 / 32 453 s y 40 008 / 40 008 /
40 442 s, las seis con EXIT=0), y se juegan los seis pools nuevos con las 24
semillas de siempre —6 pools × 2 precios × 2 rastros × 24 semillas = 576
partidas— (`scripts/gland-bench.py --pools … --prices 10,25`; evidencia en
`artifacts/pretrain-partida-200-p10h2g-replicas.txt` y `…-p10h2s-replicas.txt`).
Las semillas de GA son las mismas tres de F11i (41/43/47) para poder leer el
cambio por sorteo, y las celdas de 24 000 ticks NO se repiten: ya están jugadas
en F11i con esas mismas semillas, así que aquí se recalculan sobre los tres
sorteos pareados y no sobre los diez pools de aquel eje.

| jugado | horizonte | con puerta | con reflejo | efecto | en relativo |
|---|---|---|---|---|---|
| 10 s · fijo | 24k | 149,6 | 167,3 | −17,7 | −10,6 % |
| 10 s · fijo | 48k | 197,6 | 233,4 | **−35,8** | −15,3 % |
| 10 s · puerta | 24k | 151,6 | 170,2 | −18,5 | −10,9 % |
| 10 s · puerta | 48k | 201,6 | 234,6 | **−33,0** | −14,1 % |
| 25 s · fijo | 24k | 158,9 | 173,8 | −14,8 | −8,5 % |
| 25 s · fijo | 48k | 195,4 | 230,3 | **−34,9** | −15,2 % |
| 25 s · puerta | 24k | 154,8 | 177,3 | −22,5 | −12,7 % |
| 25 s · puerta | 48k | 205,8 | 247,8 | **−42,0** | −16,9 % |

- **Ninguna de las cuatro celdas se mueve hacia cero: las cuatro se alejan.** El
  cambio del efecto al doblar el horizonte, pareado por sorteo de GA
  (`scripts/gland-horizon-compare.py`; evidencia en
  `artifacts/pretrain-partida-200-p10h2-veredicto.txt`), es −18,1 ± 1,1
  descargas jugando a 10 s con el reflejo (t = −17,0; 0/3 sorteos a favor),
  −14,5 ± 6,0 a 10 s con la puerta (t = −2,4; 0/3), −20,1 ± 5,2 a 25 s con el
  reflejo (t = −3,9; 0/3) y −19,5 ± 13,8 a 25 s con la puerta (t = −1,4; 1/3).
  La celda más limpia es la de 10 s con el reflejo, donde los tres deltas por
  sorteo van de −16,8 a −20,2 (−17,3 / −16,8 / −20,2); el único delta individual
  a favor es la semilla 47 a 25 s con la puerta (+1,3), y no rescata su celda.
- **El nivel absoluto no es comparable entre horizontes; el relativo sí, y dice
  lo mismo.** A 48 000 ticks la partida del fitness dura el doble (800 s contra
  400 s) y por eso el brazo de referencia también sube: con el reflejo a 10 s
  pasa de 167,3 a 233,4 descargas. Leído como porcentaje de su brazo de
  referencia, el castigo pasa de −10,6 % a −15,3 % (10 s, reflejo), de −10,9 % a
  −14,1 % (10 s, puerta), de −8,5 % a −15,2 % (25 s, reflejo) y de −12,7 % a
  −16,9 % (25 s, puerta).
- **Entre tiradas sigue mandando el sorteo, no el horizonte**: dentro de los seis
  pools nuevos, la celda de 10 s con la puerta va de 183,0 a 266,9 entre sorteos
  y la del reflejo de 170,8 a 273,2 —un rango de 84 a 102 descargas, más grande
  que el efecto que se mide—. La conclusión no descansa en esas medias sueltas
  sino en los deltas pareados, que van los cuatro al mismo lado: un t de −17
  sobre tres sorteos significa «los tres se movieron igual», no una población de
  sorteos.
- **Lo que queda en pie como causa es el precio.** A 10 s de glándula el
  depósito cuesta cuatro veces menos por evento, y darle el doble de partida
  para juzgar no rescata al cerebro que decide el depósito: lo hunde más. El
  mecanismo plausible —y **no medido aquí**— es que con 800 s el rastro del
  reflejo tiene el doble de tiempo para acumularse y reclutar, mientras que el
  cerebro entrenado a precio barato aprendió a depositar poco, y esa diferencia
  se cobra más cuanto más larga es la partida. El bench sólo registra descargas,
  no depósitos, así que eso es una hipótesis a contrastar, no un resultado.

Veredicto: el horizonte de selección **no** explica el castigo de la puerta a
10 s —doblar la ventana que mira la selección no lleva el efecto hacia cero, lo
duplica— así que la causa que queda en pie es el **precio** de la glándula. El
control sale en contra de la hipótesis que lo motivó, y la decisión de
despliegue no cambia: entrenar con la puerta pide presupuesto holgado (25 s), y
a 10 s la puerta se paga al jugar sólo a medias (F11i). Lo que este control NO
cierra: 2× es una manipulación, no el horizonte de la partida (216 000 ticks,
nueve veces la ventana de 24 000), así que un nulo a 48 000 deja abiertos los
horizontes mayores; falta la celda de 25 s a 48 000, que es el 2×2 completo
(precio × horizonte) y lo único que reparte el castigo entre las dos causas con
todas las casillas; y las cifras de esta sección las deriva `scripts/partida-figures.py`
de los tres volcados del control, así que el guardarraíl las exige en cada push.

**F11k — cuánto rastro deposita cada brazo (2026-10-29): la hipótesis del
mecanismo de F11j no se sostiene — a 10 s la glándula niega el 85 % y el rastro
de casa se come el presupuesto.** F11j dejó el castigo de la puerta a 10 s
explicado por el precio y con un mecanismo plausible y sin medir: con 800 s de
selección el rastro del reflejo tendría el doble de tiempo para acumularse y
reclutar, mientras que el cerebro entrenado a precio barato habría aprendido a
depositar poco. Para mirarlo se instrumentó el mundo: `--trail-report` —nueva
bandera de `--mode game`— añade a la línea `end` el rastro acumulado de comida y
de casa —el reflejo de reclutamiento MÁS el canal genérico del cerebro—, lo que
la glándula no pudo pagar y los segundos de presupuesto gastados; leer la capa
al final no sirve porque evapora. Se jugaron los doce pools de F11i/F11j (seis
de 48 000 ticks y seis de 24 000, sorteos 41/43/47) con
`scripts/gland-deposits.py` a las dos ventanas de selección, con los dos rastros
y con y sin precio —12 pools × 2 rastros × 2 ventanas × 2 precios × 24 semillas
= 2 304 partidas— (evidencia en
`artifacts/pretrain-partida-200-p10h2-deposits.txt`).

Ventana de 48 000 ticks, glándula de 10 s (media de 24 semillas de partida):

| brazo (48k) | jugado con | rastro de comida | rastro de casa | negado |
|---|---|---|---|---|
| entrenado con puerta | puerta | **211,9** | 7 320 | 85 % |
| entrenado con puerta | reflejo | 38,8 | 9 223 | 87 % |
| entrenado con reflejo | puerta | 134,6 | 7 396 | 85 % |
| entrenado con reflejo | reflejo | 35,7 | 9 641 | 87 % |

- **La glándula es el tope, no la intención**: entre el 82 y el 89 % de lo
  pedido se queda sin presupuesto en las veinticuatro celdas de 10 s. La cresta
  de comida cae de 19 714,7 (sin precio) a 211,9 en el brazo de la puerta y de
  28 027,3 a 35,7 en el del reflejo: ×93 y ×785. Con el reflejo fijo la cresta
  queda prácticamente apagada.
- **El rastro de casa se come el presupuesto**: abrir la puerta baja el rastro
  de casa de 9 223 a 7 320 (y de 9 641 a 7 396 en el brazo del reflejo) y, con
  el presupuesto que eso deja libre, la cresta sube en los doce pools: de
  +16,3 a +382,2, con 24/24 semillas a favor en diez de las doce celdas y 23/24
  en las otras dos. Sin precio, en cambio, el brazo de la puerta deja un 30 %
  MENOS de cresta que el del reflejo (19 714,7 contra 28 027,3): es un grifo que
  sólo se nota cuando el agua escasea.
- **El doble de ventana no dobla la cresta**: sin precio el acumulado crece
  ×1,72–2,10 en los veinticuatro brazos —×1,90–2,10 en los doce entrenados a
  48 000, ahí sí «el doble de tiempo»—; con la glándula de 10 s crece ×1,15–1,23
  con el reflejo fijo y ×1,22–1,60 con la puerta, mientras el rastro de casa sí
  se dobla (3 538 → 7 124 en p10h2g-s41) y el presupuesto gastado también
  (4 538,7 → 9 090,1 s). Lo que no escala con la ventana es la señal que
  recluta: la cresta está limitada por
  viaje, no por el tiempo de partida.
- **El brazo que pierde en descargas no es el que deposita menos**: con la
  puerta puesta, el pool entrenado con puerta deja 211,9 de cresta contra 134,6
  del entrenado con reflejo; con el reflejo fijo, 38,8 contra 35,7 —lo mismo—.
  «El cerebro entrenado a 10 s aprendió a depositar poco» no se ve por ningún
  lado; lo que se ve es un reparto distinto del mismo presupuesto.

Veredicto: las dos mitades de la explicación de F11j fallan a la vez. El brazo
del reflejo no acumula «el doble de rastro» con el doble de ventana —al precio
donde vive el castigo su cresta está apagada y sólo dobla el rastro de casa—, y
el cerebro entrenado con la glándula barata no aprendió a depositar poco: con la
puerta abierta deja seis veces más rastro de comida que el brazo del reflejo
(211,9 contra 35,7) y con el reflejo fijo prácticamente lo mismo (38,8 contra
35,7). El precio sigue siendo la causa que F11j midió, pero el canal no es el
volumen sino el **reparto del presupuesto**: a 10 s la glándula niega el 85 % y
el reflejo se lo gasta en el camino de vuelta, dejando la señal de reclutamiento
casi a cero. Es también por qué alargar la ventana no ayuda: la cresta está
limitada por viaje, no por tiempo de partida.

Lo que esta medición NO dice: ni dónde queda la cresta ni cómo de continua es
—lo que recluta es la colocación, no la masa—; todo está medido en las ventanas
de selección (24 000 y 48 000 ticks), no en la partida completa de 216 000; los
contadores suman las dos colonias y los dos canales, y con tres sorteos por
receta la dispersión entre pools es grande (la cresta del brazo de la puerta va
de 106 a 381 a 48k). Las cifras de esta sección las deriva `scripts/partida-figures.py`
del volcado de depósitos, así que el guardarraíl las exige en cada push.


**F11m — el lanzador NO cobra la glándula (2026-10-29): a 25 s el rastro de
comida cae ×767 y la cosecha no se mueve.** F11j dejó el precio como única
causa del castigo y F11g/F11i dejaron el 25 s como condición de despliegue para
entrenar con la puerta. La pregunta que quedaba era si la partida de fábrica —la
que abre el lanzador— debía jugar también con ese precio. Se midió el par
completo con la MISMA partida del lanzador (grid 96, laberinto, reclutamiento,
216 000 ticks, el pool de fábrica `pretrain-maze-los3`, el REFLEJO decidiendo y
sin puerta del cerebro) a 0 y a 25 s, con las 24 semillas de partida de siempre
(`scripts/gland-launcher-cost.py`; evidencia en
`artifacts/pretrain-partida-200-lanzador-gland.txt`).

| glándula | rastro de comida | rastro de casa | negado | glándula gastada | descargas |
|---|---|---|---|---|---|
| 0 s (de fábrica) | 34 223,4 ± 9 735,7 | 89 816 ± 20 253 | 0 % | 0 s | 108,4 ± 31,4 |
| 25 s | **44,6 ± 26,6** | **15 326 ± 4 974** | **87,8 %** | 19 213 s | 113,9 ± 32,6 |

- **En la partida que el lanzador abre de fábrica el precio no disciplina
  ninguna decisión: sólo raciona.** Esa partida decide el depósito con el reflejo
  de F8 y no expone `--trail-by-brain`, así que no hay salida del cerebro que
  cobrar; y el pool que carga está entrenado sin precio. (El par de F11n, que sí
  la expone, entra como configuración con su pool: esta medición es la del juego
  que el lanzador abre por defecto.) La evidencia del 25 s (F11g/F11h/F11i/F11j) es sobre
  ENTRENAR con la puerta, y no se traslada a este par pool/reflejo.
- **Lo que se pierde es exactamente lo que el lanzador enseña.** El rastro de
  comida cae de 34 223,4 a 44,6 —99,9 %, ×767— en las 24 semillas (0/24 a favor;
  −34 178,8 ± 1 987,8, t = −17,2) y el de casa de 89 816 a 15 326 (−74 490,4 ±
  3 515,4, t = −21,2; 0/24). Lo que queda de comida no es un rastro: es el
  residuo de los depósitos que el presupuesto alcanzó a pagar.
- **La cosecha no cambia.** Las descargas pasan de 108,4 a 113,9 (+5,5 ± 4,6,
  t = +1,2; 16/24 semillas), dentro del ruido: el precio no compra ni cuesta
  comida recogida, sólo borra la señal.
- **El negado sigue siendo el 88 %**, no el 0 % que sugiere «el doble de
  presupuesto»: a 25 s el reflejo pide mucho más de lo que la glándula puede
  pagar en una partida de 3 600 s (19 213 s gastados entre las dos colonias).

Decisión: **no**. La partida de fábrica sigue con `--trail-gland 0` —el default
que el mundo ya trae— y el lanzador no gana perillas nuevas. El precio sigue
siendo una palanca de entrenamiento y de evaluación, y quien quiera mirar la
condición de despliegue la reproduce en el CLI, que es donde juegan todos los
benches: `--mode game --maze --recruitment --trail-gland 25 --trail-gland-refill 6`.

Coste de haberlo adoptado: el 99,9 % del rastro de comida y el 83 % del de casa
a cambio de +5,5 descargas que no son señal, más dos ajustes nuevos que
atravesarían `LaunchSettings`, `RecruitmentKnobs`, el cableado al CLI de la
vista, la línea del HUD y sus pruebas de espejo (ocho ficheros) para enseñar una
decisión que nadie toma. Coste de no adoptarlo: el lanzador no muestra la
condición de 25 s de un vistazo; se compensa con el CLI, que sí la reproduce.

Lo que cambiaría la decisión: que el lanzador expusiera la puerta del cerebro y
cargara un pool entrenado a 25 s —entonces el precio sería la condición de ESE
pool y no un impuesto a un reflejo que no decide—, o una glándula bastante más
holgada que dejara de negar el 88 % sin borrar el rastro.

**F11l — el 2×2 completo (2026-10-29): a 25 s el horizonte deja de cobrar, y el
castigo no se reparte en dos sumandos.** F11j dobló el horizonte de selección
SÓLO a 10 s —donde la puerta pierde— y encontró que el castigo crece: con eso el
horizonte quedaba descartado como causa a ese precio, pero no decía qué hace
cuando el depósito es holgado. Ésta es la casilla que faltaba para poblar las
cuatro del 2×2 (precio de ENTRENAMIENTO × horizonte de SELECCIÓN). Se entrena con
la misma receta de búsqueda (`GLAND=25 REFILL=6 TICKS=48000 ARMS=red|fijo
SEED=… SUFFIX=p25h2g…|p25h2s… scripts/run-gland-200.sh`; dos tandas de tres
—10:43:43→16:11:26 y 16:11:26→21:37:04—, 19 662 / 19 617 / 19 538 s y 19 454 /
19 536 / 19 536 s, las seis con EXIT=0) y con las semillas de GA 7/11/13, no las
41/43/47 de F11j: el eje que esta celda tiene que dejar pareado es el HORIZONTE a
25 s, y las tiradas de 24 000 a ese precio ya existen con esas tres en F11g, así
que el salto se lee sorteo a sorteo —el de precio, en cambio, queda sin parear en
las dos filas—. Los seis pools nuevos se juegan con las 24 semillas de partida de
siempre —6 pools × 2 precios × 2 rastros × 24 = 576 partidas—
(`artifacts/p25h2-bench-run.sh`), y las otras tres casillas no se repiten: se leen
de sus volcados, 10 s · 24k de F11i, 10 s · 48k de F11j y 25 s · 24k de F11g.

| precio entrenado | horizonte | con puerta | con reflejo | efecto | en relativo |
|---|---|---|---|---|---|
| 10 s | 24k | 149,6 | 167,3 | −17,7 | −10,6 % |
| 10 s | 48k | 197,6 | 233,4 | −35,8 | −15,3 % |
| 25 s | 24k | 150,4 | 163,7 | −13,4 | −8,2 % |
| 25 s | 48k | 201,2 | 202,1 | **−0,8** | −0,4 % |

La tabla se juega a 10 s de glándula y con el rastro fijo, que es la condición
con la que F11j publicó sus dos filas de 10 s —las de arriba son esas mismas
cifras, recalculadas desde los mismos pools—; las otras tres formas de jugar
(10 s con la puerta, 25 s con las dos) están en el volcado y no cambian ninguna
lectura. El efecto es la media pareada por sorteo de GA —la misma tirada
entrenada de las dos maneras y jugada con el mismo rastro, con las mismas semillas
de partida— y el relativo la lleva a porcentaje de su brazo de referencia; por eso
puede diferir en una décima de la resta de las dos columnas redondeadas.

- **El horizonte cobra a 10 s y no cobra a 25 s.** Doblarlo mueve el efecto −18,1 ± 1,1 descargas a 10 s (t = −17,0) y +12,5 ± 31,4 a 25 s (t = +0,4). El segundo no es un cero medido con precisión: es un cero en el que los tres sorteos se van cada uno por su lado (+6,4 / +69,7 / −38,6 al doblar la ventana), que es justo lo que el control de 10 s no tenía —allí los tres se movían juntos—.
- **La celda nueva cierra el 2×2, pero el ruido entre sorteos es mayor que el efecto.** Su efecto es el 0,4 % de su referencia, y sorteo a sorteo va de −18,0 a +19,0 entre sorteos: lo que la celda dice es «a 25 s × 48k no se mide castigo», no «la puerta gana».
- **El reparto no cierra por suma.** Descompuesto sobre la casilla que peor le sienta a la puerta —base −17,7, horizonte ×2 a 10 s −18,1, precio a 24k +4,3, predicción aditiva −31,5 y celda real −0,8: interacción +30,6—, lo que sobra no es un residuo: la interacción es del tamaño de los dos sumandos juntos.
- **Y no es un accidente de la condición de juego:** la interacción va de +25,1 a +44,4 descargas en las cuatro formas de jugar, siempre del mismo signo y siempre por encima del castigo que se quería repartir.
- **Lo que la casilla cierra** es el reparto que F11j dejó abierto: el horizonte no es un peaje fijo que se sume al precio —a 25 s no cobra—, y el castigo grande vive en el depósito barato. Con el precio holgado queda en −13,4 a 24 000 ticks y desaparece a 48 000 (−0,8), que es la condición de despliegue que F11g y F11i ya pedían por otro camino.

Veredicto: el 2×2 queda **completo y no separable**. A 10 s doblar la ventana de
selección agranda el castigo (−18,1 ± 1,1, t = −17,0); a 25 s no lo mueve
(+12,5 ± 31,4, t = +0,4), y la interacción (+30,6, y de +25,1 a +44,4 en las
cuatro formas de jugar) se come la predicción aditiva, así que las dos causas no
se reparten en dos sumandos y hay que leer el 2×2 casilla a casilla. Lo que NO
cierra: con tres sorteos por casilla la interacción no tiene error típico propio;
2× sigue siendo una manipulación y no el horizonte de la partida (216 000 ticks,
nueve veces la ventana de 24 000); y esta dispersión no distingue entre «a 25 s el
horizonte es indiferente» y «esconde un efecto menor». Coste: seis tiradas de 200
generaciones en dos tandas de tres —10 h 53 m de pared, 19 454–19 662 s por
tirada— y 576 partidas del bench (23 min); las cifras de la sección las deriva
`scripts/partida-figures.py` de los volcados del 2×2, así que el guardarraíl las
exige en cada push.

**F11n — el lanzador expone el par de la puerta, con el pool que lo entrenó
(decisión, 2026-10-30): paga, y contra el juego de hoy también.** F11l cerró el
2×2 y dejó dicha la condición del hallazgo: a 25 s de glándula el castigo de la
puerta desaparece y a 10 s se agranda. La pregunta que quedaba es de producto, no
de medida: ¿debe el lanzador ofrecer el PAR —puerta del cerebro más glándula de
25 s— con un pool entrenado bajo ese régimen?

Lo medido, con siete tiradas por receta y 24 semillas de partida que la selección
no vio: la puerta paga dentro del pool que la entrenó (+5,6 ± 1,9 descargas al
promediar las catorce tiradas de 25 s, t = 2,9; 12/14 a favor,
`artifacts/pretrain-partida-200-p25g7-champion.txt`), pero de punta a punta —el
mejor pool con puerta jugando con puerta contra el mejor pool sin ella jugando con
el reflejo— la ventaja se queda en +4,1 ± 3,2 (t = 1,3; 17/24 semillas,
`artifacts/pretrain-partida-200-p25g7-vs-p25-puerta.txt`), que no se distingue de
cero. Y contra el pool que el juego carga de fábrica —`pretrain-maze-los3`, el
campeón de laberinto de F7— no hay **ninguna** comparación: todas las de F11 son
dentro de su propia familia, mismo warm-start y misma receta, así que ese +4,1 no
dice cómo quedaría el juego de hoy.

Primera decisión, con lo que había el 30 por la mañana: **no se expone todavía**,
y el motivo no es la puerta sino el pool. Con el pool de fábrica —entrenado antes
de que existiera el precio y sin la puerta— la puerta del cerebro es neutra o algo
peor: F11b la midió en −6 ± 10 descargas (4/8) dentro del pool entrenado sin ella,
y el barrido de precios la deja entre −14 y +10 a todos los precios, ninguno con
t > 1. Exponer un interruptor que con el pool que el juego carga no paga es
ofrecer una palanca que enseña lo contrario de lo medido. Y las dos piezas no se
pueden separar: la puerta sin precio es F11 —la red solo puede adelgazar el
rastro y empeora— y el precio sin puerta es F11m
—sólo raciona y borra la señal—. Si algún día entra, entra el par.

Criterio para adoptarlo, y es medible sin opinión: un enfrentamiento en la
partida del lanzador —216 000 ticks, laberinto, las 24 semillas de siempre—
entre lo que hay hoy (pool de fábrica, reflejo, glándula 0) y el par candidato
(pool entrenado a 25 s con la puerta, puerta, glándula 25 s), con **las dos
recetas replicadas**: F11d costó cuatro tiradas aprender que una sola tirada es
el sorteo más afortunado, no la receta. Se adopta si el par gana con ≥16/24
semillas y su margen supera la dispersión entre tiradas —que en este eje manda:
F11h ya vio que parear por sorteo deja t por debajo de 1—; si no, el hallazgo se
queda en el CLI, que es donde juegan todos los benches.

Ese enfrentamiento existe desde el mismo 30 (`scripts/gland-launcher-duel.py`;
evidencia en `artifacts/pretrain-partida-200-p10h2-duel.txt`): 32 brazos por 24
semillas: 768 partidas de la partida del lanzador, todas terminadas en el tick
216 000. El reparto, con las dos recetas promediadas por semilla de partida —lo
que se compara es la receta, no el sorteo que cada una tuvo por suerte—:

- el par a 25 s gana +57,9 ± 9,6 descargas al juego de hoy (t = +6,0; 20/24
  semillas): el margen y las semillas que el criterio pide, las dos cosas.
- la receta del reflejo a 25 s gana +47,2 ± 9,0 (t = +5,2; 20/24 semillas): el
  pool reentrenado bajo el precio, sin la puerta, ya se lleva la mayor parte.
- el par le gana a su propia receta del reflejo +10,7 ± 3,4 descargas (t = +3,2;
  19/24 semillas): con las dos recetas replicadas la puerta deja de ser
  indistinguible de cero. En el primer F11n eran +4,1 ± 3,2 con una tirada por
  brazo; aquí son siete, y el signo se sostiene.
- la dispersión entre tiradas es 8,6 en la receta del par y 18,7 en la del
  reflejo: el margen la supera con holgura, que es la segunda mitad del criterio.

Lo que pone cada pieza, del mismo volcado, y es lo que decide CÓMO se adopta:

- la puerta dentro del pool que la entrenó: +4,6 ± 2,8 descargas al promediar las
  siete tiradas (rango por tirada −13,6 a +13,1): real y pequeño —un 3 % del
  nivel—, y ninguna tirada suelta lo demuestra.
- la puerta sola sobre el pool de fábrica: +1,2 ± 3,2 (t = +0,4; 13/24 semillas):
  sin reentrenar, el interruptor no mueve la aguja.
- el precio solo sobre el pool de fábrica: +5,5 ± 4,6 (t = +1,2; 16/24 semillas).
- el par completo sobre el pool de fábrica: +2,8 ± 5,0 (t = +0,6; 13/24 semillas).

Leído así, el +57,9 no es un interruptor que se encienda sobre el `.antgenome`
que el juego carga: es un pool nuevo que viene con la puerta. La puerta aporta
+10,7 sobre su propia receta y el resto lo pone reentrenar bajo el precio; sobre
el pool de fábrica el par no se distingue de cero. Y el banco está calibrado para
decirlo: los dos brazos de fábrica dan 108,4 ± 31,4 con la glándula apagada y
113,9 ± 32,6 a 25 s, que son las cifras que F11m ya tenía registradas para esta
misma partida, y el precio solo repite aquí su +5,5 del 29.

**Decisión, con el criterio cumplido: se adopta.** El lanzador expone el par
—puerta del cerebro más glándula de 25 s de depósito y 6 s de recarga— con el
pool que su receta corona desde F11f (`pretrain-partida-200-gl-red`, primero de
siete con la puerta puesta), y el juego de fábrica NO cambia: sigue siendo el que
F11m decidió, con `--trail-gland 0`, porque el volcado dice precisamente que el
par sobre ese pool no paga. Que entre como configuración y no como default es
además lo que deja quietos los seis pines de hash: no cambia el mundo de nadie,
aparece una forma de jugarlo que antes solo existía en la línea del CLI.

Coste de lo adoptado, ya implementado: tres perillas nuevas en `RecruitmentKnobs`
y `LaunchSettings` —puerta, precio y recarga, con campo, serialización, parseo,
validación y descripción—, los tres centinelas de `SimPresenterBehaviour` con su
asignación en `SceneBootstrapper`, las tres banderas en la línea del CLI que arma
`StreamReader` y la ayuda y los rangos de `play-game.sh` y `play-game.ps1`; la
prueba de espejo las cubre en los dos sentidos (defaults, rangos, motivos de
rechazo y la descripción del par). Entran apagadas: sin banderas la línea del CLI
es la de siempre, así que los seis pines de hash no se mueven y la partida de
fábrica sigue en glándula 0. La glándula se VE al cerrar la ronda: con precio, el
lanzador añade `--trail-report`, `GameStreamParser` lee el bloque `trail` de la
línea `end` y la línea del tablero lo publica —los segundos de presupuesto
gastados y la parte de lo pedido que quedó sin pagar, con el MISMO cociente que
los guiones de medida—. Durante la partida no hay cifra que enseñar: el mundo no
publica la glándula tick a tick, y una línea que dijera cero antes del primer
cierre diría lo contrario de lo que pasa. Y el par se pide con su pool
(`--pool pretrain-partida-200-gl-red`), que es el que su receta corona, no con el
de fábrica.

Lo que la decisión NO compra, y conviene tenerlo escrito al lado: el +47,2 y el
+57,9 comparan una receta de 200 generaciones con warm-start desde
`pretrain-maze-3k` y el precio encendido contra el campeón de laberinto de F7, que
son 660 generaciones sin precio. Nadie ha separado todavía «entrenar con el
precio paga» de «entrenar más paga», y mientras no se separe la ventaja se lee
como receta completa y no como mérito del precio. Lo segundo que queda abierto es
la puerta sola: +4,6 ± 2,8 dentro de su pool es real pero pequeño, y ninguna de
las siete tiradas lo demuestra por separado (el rango por tirada va de −13,6 a
+13,1). Y lo que cerraría el asunto sin más vueltas: el mismo reentrenamiento de
200 generaciones SIN precio y sin puerta, que diría cuánto del +47,2 es el precio
y cuánto el entrenamiento.

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **SEIS pines de hash**, verdes en Linux en el último
push (`v0.8.0`, run #37 del 2026-09-19): stream canónico, replay con drops a
3000 y 6000 ticks, la partida Atta canónica, la de INVASIÓN canónica y la
NEAT canónica. <!-- cifras-historicas: el cierre de v0.8.0 (run #37). -->Aquel
cierre fue con 773/773.<!-- /cifras-historicas --> Hoy la suite tiene **784/784
tests** en Windows y verdes en Linux en CI en cada push, igual que los 6 scripts
de pin. El job `test` incluye además **seis pasos que
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

**La gráfica de la tarjeta enseña también las DESCARGAS** (2026-10-21) — la
reserva dice CUÁNDO se cayó; las barras dicen POR QUÉ se estaba llenando. La
serie sale de los eventos `Unload` del canal B —uno por descarga, con su
colonia— contados en la misma ventana de 1 s que la muestra del área, y no de
`colmetrics`, que es la ventana ABIERTA que el Core publica cada 120 ticks y no
sirve para una serie a 1 Hz. Va en su propia máscara y con su color (ámbar) para
no confundirse con el estado de la reserva, que es el que cambia a rojo, y la
altura es relativa a la mayor cuenta de la ventana que se ve: la misma ventana
dibujada da la misma imagen. El HUD la alimenta con **cada tick drenado**: leer
`CurrentTick` en el `Update` bastaba para la reserva (muestrea a 1 Hz y repetir
un tick no cambia nada) pero a velocidad alta un frame presenta varios ticks y
las descargas de los del medio se perdían; y el mismo tick visto dos veces ya no
cuenta doble. Nueve tests headless nuevos, uno contra el stream real: la suma de las
barras es EXACTAMENTE el número de `Unload` que emitió el Core en la ventana.

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

**Y las descargas de la gráfica se MIDEN en los píxeles de la tarjeta** (2026-10-22).
La serie de descargas por ventana quedó dibujada y verificada headless (modelo
puro, textura, tests), pero eso deja fuera el tramo que va de la textura al
ojo: el `RawImage`, su rect en pantalla, el tinte de la vista y el framebuffer.
`scripts/unity/ProbeSparklinePixels.cs` inyecta una serie CONOCIDA en el modelo
real de cada tarjeta —cuatro ventanas con 1, 3, 5 y 7 descargas, separadas por
ceros—, congela la alimentación del HUD para que los ticks de la partida no
metan muestras entre los dos pasos, deja dibujar un frame y recorta del frame
REAL la banda de la gráfica; después restaura la serie de la partida. La medida
es un paso del Play pass (`scripts/playpass-sparkline.py`, con su `--selftest`
sobre imágenes sintéticas): exige una barra por ventana con descargas y en las
columnas de SU muestra (el mismo mapeo que el área), altura creciente con la
cuenta, todo naciendo en la base —la orientación se comprueba, no se supone— y
ni un píxel ámbar fuera de la banda, con la banda dentro de la tarjeta y el
recorte comprobado como trozo literal del frame. Falla cerrado: sin captura o sin
PNG conservados no es verde. La evidencia queda en
`artifacts/playpass-sparkline-pixels/` y se repite sin editor con
`--analyze-sparkline`.

**Medido en vivo el 2026-10-22** (primera pasada completa, editor 6000.6.0f1):
las dos tarjetas dieron **4 barras cada una** en las columnas de su muestra, con
alturas **5 / 11 / 18 / 24 px** para 1 / 3 / 5 / 7 descargas en una banda de 24
px, el borde del área siguiendo la reserva inyectada (±3 px), **0 px ámbar fuera
de la banda** y el recorte idéntico al frame (`29,837,377x30` y
`29,582,377x30`). La primera pasada de verdad destapó dos defectos que el
`--selftest` no podía ver, y los dos eran del tramo sonda–medidor, no de la
gráfica: (1) **los `static` no sobreviven** entre dos `run_script` —cada llamada
compila una asamblea nueva, medido con un contador que vuelve a 1—, así que
`measure` no veía la serie inyectada y se plantaba sin capturar; ahora el estado
(si el HUD se congeló y la serie real de cada tarjeta) vive en
`Temp/antsim-spark-state.txt`, y `measure` **comprueba tarjeta por tarjeta** que
el patrón sigue en el modelo antes de capturar. (2) **El PNG va del derecho**:
`GetPixels32` indexa desde abajo, pero `EncodeToPNG` guarda fila 0 = arriba, así
que contar la fila 0 como base medía la gráfica al revés — todas las barras al
alto de la banda y el área al 100 %. El volcado declara `row0=top` y el medidor
convierte el `inner` con `alto - iy - ih`; la comprobación de procedencia prueba
las dos colocaciones y exige igualdad byte a byte.

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
2. **Deuda menor de F5.1** (no bloquea): **audio y accesibilidad** (sonidos de
   evento —descarga, eclosión, alerta— y escala de UI; es lo único sin contrato
   previo), el **chip de la capa de feromona activa** en el HUD (el render la
   pinta y la nombra, el HUD no la rotula) y la **capa de territorio**, que el
   `PheromoneKind` reserva y el escenario ya lee pero que nadie deposita todavía.
   La serie de DESCARGAS en la gráfica quedó HECHA el 2026-10-21 (ver §Lo que ya
   funciona): la sparkline guarda reserva y descargas por ventana.
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
