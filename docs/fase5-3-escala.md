# F5.3 rodaja 3 — Escala: LOD de difusión e instancing (medido)

Estado: **HECHA (2026-09-18)** · padre: [`fase5-plan.md`](fase5-plan.md) §4 ·
estado consolidado: [`estado-proyecto.md`](estado-proyecto.md).

Es la última rodaja de F5.3 y la que cierra su criterio de salida. Aquí están las
dos piezas, la prueba de que ninguna de las dos cambia el mundo y las cifras con
las que se juzga (incluidas las que no son bonitas).

## 1. El problema, medido antes de tocar nada

Cuatro capas de feromonas por colonia (`food`, `home`, `alarm`, `footprint`), y
cada 10 ticks cada una hacía **dos pasadas sobre el grid completo** (evaporar +
difundir). En el mundo real (grid 256²):

| capas × colonias | celdas visitadas por actualización | celdas con rastro |
|---|---|---|
| 1 colonia (4 capas) | 262 144 | ~4 300 |
| 2 colonias | 524 288 | ~8 900 |
| 8 colonias | 2 097 152 | ~36 200 |

Es decir: el 98 % del trabajo de feromonas se gastaba en celdas **nulas**. Y en el
otro extremo, el presenter dibujaba **una llamada por hormiga, ítem y mordisco**
(`Graphics.DrawMesh`): con el multi-visor de 4 vistas, cuatro veces eso por frame.

## 2. LOD de difusión — exacto, no aproximado

La pregunta correcta no era «cómo difundo menos», sino «qué celdas PUEDEN
cambiar». La respuesta está en el régimen del propio proyecto: en `Diffuse`, una
celda nula se queda nula — no recibe de sus vecinas:

```csharp
float v = vals[i];
if (v <= 0f) { scr[i] = 0f; continue; }   // el original: la nula no se llena
```

(No es una decisión de esta rodaja: es el comportamiento con el que se
entrenaron los genomas, se calibró la arena y se fijaron los 6 pines. Cambiarlo
sería otra fase.) De ahí sale la consecuencia que hace el LOD gratis:

1. el soporte del rastro **nunca crece al difundir** (una nula no se llena), solo
   crece al depositar y se apaga al evaporar;
2. entonces visitar solo las celdas **con soporte** produce **los mismos valores
   celda a celda**, no unos parecidos.

**Cómo se implementa** (`PheromoneLayer`):

- el grid se divide en **bloques de 8×8** (`DefaultLodBlockSize`; el lado se fija
  por capa al construirla, `LodBlockSize`) — más finos que el tile de render de
  64, porque el tile mide *envíos* y el bloque mide *trabajo*. **Ese 8 no es a
  ojo: es el que sale de la tabla de §4.6** (con el 16 histórico se visitaba el
  13.8 % del grid; con 8, el 8.7 %, por el mismo mundo);
- cada bloque lleva un **contador de celdas no nulas**, mantenido por incremento
  en `Deposit` y por decremento en la transición a cero (`Commit`). Un bloque con
  contador 0 no puede tener una celda que cambie;
- `Evaporate` y `Diffuse` construyen su lista de **regiones** a partir de los
  bloques con soporte y recorren solo eso. La lista se reconstruye cuando el
  soporte cambia, no en cada celda — y **cuánto cuesta eso se cuenta**, no se
  estima: `BlockListRebuilds` y `BlockSlotsScanned` (ver §4.6);
- `LoadState` (checkpoint `.antsave`) **deriva** el soporte de los valores con
  `RebuildSupport()`: el soporte es un índice del estado, no estado.

`SumOfValues()` (el canal del hash) sigue recorriendo el grid completo a
propósito: el **orden de acumulación de floats** forma parte del hash y saltarse
ceros cambiaría el redondeo.

**La prueba.** `PheromoneLodTests` corre la MISMA secuencia de 400 operaciones
(depósitos en tres focos, goteo disperso, evaporaciones y difusiones con `k`
aleatorio) por los dos caminos —el LOD y una capa gemela con `LodEnabled = false`,
que es la implementación de referencia a grid completo— y compara **celda a
celda**, más contadores, versiones de tile y contador de mutaciones. Bit a bit,
tras cada paso y al cierre.

El primer intento de ese test **falló**, y no por el LOD: el test sortea los
operandos una vez y los aplica a las dos capas… salvo en la rama de difusión, que
consumía dos números aleatorios distintos y comparaba dos difusiones diferentes.
La lección queda escrita en el test: un arnés de equivalencia con dos flujos de
aleatoriedad es un arnés que no prueba nada.

## 3. GPU instancing — lotes por material

`Graphics.DrawMeshInstanced` dibuja la misma malla con el mismo material muchas
veces en UNA llamada, con dos límites que sí son contrato:

- **1023 instancias por llamada** (límite del motor, `InstancedDrawPlan.BatchLimit`);
- **un material por lote** — no se pueden mezclar.

El presenter (`SimPresenterBehaviour`) acumula las matrices del frame en un
agrupador por material (`InstanceSlotDrawer`: búferes reutilizados, **cero basura
por frame**) y las envía troceadas. Si la plataforma no soporta instancing
(`SystemInfo.supportsInstancing`) o alguien apaga `UseInstancing`, cae al camino
de una llamada por objeto: el mismo dibujo, más lento.

El troceo (cuántos lotes, de qué tamaño) vive en un **modelo puro**,
`Assets/Scripts/Streaming/InstancedDrawPlan.cs`, que la suite headless compila
enlazado como el resto de modelos puros del esqueleto Unity. Vive ahí y no en el
Core a propósito: **la vista no referencia el Core** (consume el stream), y esa
frontera no se rompe por una optimización. Y hay una cifra en vivo para
verificarlo sin píxeles: `SimPresenterBehaviour.LastDrawCalls`, con presupuesto
(`DrawCallBudget = 32`).

## 4. Las cifras

### 4.1 Trabajo de feromonas (el LOD)

Medido con la sonda de la suite y con `--mode scale`, grid 256², semilla 42:

| régimen | celdas visitadas | celdas con soporte |
|---|---|---|
| mundo joven, 1 500 ticks, 8 colonias | **2.0 %** del grid | 7 479 |
| con warm-v2, 6 000 ticks, 1 colonia | **7.8 %** | 4 307 |
| con warm-v2, 6 000 ticks, 8 colonias | **8.7 %** | 36 228 |
| con warm-v2, 6 000 ticks, 8 colonias, grid 512² | **2.0 %** | 35 905 |

**El ahorro se encoge a medida que el rastro se extiende**, y eso es la verdad del
método: el LOD no mide celdas no nulas, mide *bloques que contienen algo*. Un
rastro fino que cruza el mapa activa muchos bloques, y cada bloque activo cuesta su
área completa. La palanca para afinarlo es el lado del bloque, y ya no es una
intuición: **§4.6 mide el equilibrio completo** y de esa tabla salió el valor por
defecto (8 celdas, que bajó la visita del 13.8 % al 8.7 % con 8 colonias).

### 4.2 Coste del tick y criterio de salida

`antsim --mode scale --grid 256 --ticks 6000 --colony-counts 1,2,4,8 --seed 42
--seed-pool tests/fixtures/warm-v2.antgenome`:

| colonias | hormigas | ítems | ms/tick | % frame (60 fps) | velocidad máx. | celdas LOD visitadas | soporte |
|---|---|---|---|---|---|---|---|
| 1 | 26 | 171 | 0.06 | 0.3 % | ×596 | 20 352/262 144 (7.8 %) | 4 307 |
| 2 | 51 | 171 | 0.11 | 0.7 % | ×292 | 45 184/524 288 (8.6 %) | 8 882 |
| 4 | 101 | 171 | 0.23 | 1.4 % | ×144 | 86 656/1 048 576 (8.3 %) | 17 836 |
| 8 | 193 | 171 | 0.47 | 2.8 % | ×71 | 181 504/2 097 152 (8.7 %) | 36 228 |

Lectura honesta de la tabla:

- **el Core no es el cuello de botella**: 8 colonias consumen el **2,8 % del
  presupuesto de un frame a 60 fps** (16,6 ms), y el coste crece lineal en colonias
  (0.06 → 0.47 ms/tick con 8× colonias);
- «velocidad máx.» es la velocidad de la vista (×, base 30 ticks/s del presenter)
  que el CLI podría sostener si solo tuviera que simular. Con 2 colonias —el modo
  de juego real— el techo es **×292**, muy por encima del ×100 del control de
  velocidad; con 8 colonias baja a **×71**, es decir que a ese tamaño la velocidad
  alta de la vista ya no la sostendría el Core solo (y ahí el LOD es lo que evita
  que empeore);
- las llamadas de dibujo del plan instanciado, para el escenario del multi-visor
  (800 hormigas por vista con 4 materiales), son **4 por vista** frente a **800**
  de la línea base — el test lo fija (`instanciado × 100 < base`).

### 4.3 La VISTA medida en batch (el coste de producir frames)

`bash scripts/perf-scene.sh` monta la escena del multi-visor con **4 vistas × 2
colonias = 8 colonias en pantalla** en el grid real del modo juego (256), entra en
Play y muestrea una vez por segundo: frames/s reales, `LastDrawCalls`, hormigas e
ítems por vista. Corrida de 40 s, boost ×10 (grid 256², 12 000 ticks), **sin
`--live`**: `-batchmode`, sin presentación. El informe queda en
`artifacts/perf-scene.json`.

| medida | valor |
|---|---|
| frames/s, fase ESTABLE (30 muestras) | **1 964–1 994** → **0,51 ms/frame** |
| frames/s, peor muestra | 292 → **3,42 ms/frame** (arranque del stream, no dibujo) |
| llamadas de dibujo (4 vistas) | **19** — peor vista 5 (presupuesto 32/vista) |
| llamadas instanciadas / de respaldo | **1 121 667 / 0** |
| carga final en pantalla | **292 hormigas + 684 ítems** (73 hormigas por vista) |
| stream agotado | t≈9 s (tick 12 000; el CLI va muy por delante de la vista) |
| bucle del jugador | real (no hubo que empujarlo) |

Lectura: con las 8 colonias dibujándose, la vista gasta **medio milisegundo de CPU
por frame** — un 3 % del presupuesto de 16,6 ms a 60 fps — y la peor racha de la
corrida (3,4 ms) NO es de dibujo: es el arranque del stream (los cuatro procesos
del CLI y el llenado del búfer, con 0 hormigas todavía en pantalla). El valor es
reproducible: dos corridas dieron 1 963 y 1 994 frames/s con el mundo idéntico
(292 hormigas, 684 ítems, 19 llamadas).

**Qué es y qué no es este número.** Es el coste de *producir* frames en batch:
sin vsync, sin presentación a pantalla y con `Time.captureDeltaTime` fijo (1/30 s),
de modo que mide el camino de CPU —parseo + `DrawMeshInstanced` + el contador—, no
los fotogramas que ve el jugador con la pantalla refrescando. Es una **cota**, y la
medición con presentación real es la de §4.4.

### 4.4 La VISTA medida en el Play pass REAL (editor con ventana)

El modo que faltaba: `bash scripts/perf-scene.sh --live` **no** pasa `-batchmode`.
Abre el editor de verdad, con ventana, y deja correr el bucle del jugador y la
presentación tal cual, así que lo que se lee es el framerate —con el coste de
pintar las cuatro vistas a pantalla incluido—. La escena es idéntica a la de §4.3
(4 vistas × 2 colonias = 8 colonias, grid 256², 12 000 ticks, boost ×10), corrida
de 40 s, editor 6000.6.0f1, ventana de 1536×1303, `bucle=real` (no hubo que
empujarlo) y **39/39 muestras con la ventana del editor en primer plano**. Los
informes quedan en `artifacts/perf-scene-live.json` (vsync apagado) y
`artifacts/perf-scene-live-vsync.json` (vsync del proyecto).

| medida | `--live` | `--live --vsync` |
|---|---|---|
| frames/s, fase ESTABLE (31 muestras) | **410,3** → **2,44 ms/frame** | 408,3 → 2,45 ms/frame |
| frames/s, mediana de la corrida (39 muestras) | 407,8 | 403,8 |
| peor muestra | 213,7 → **4,68 ms/frame** | 219,8 → 4,55 ms/frame |
| llamadas de dibujo (4 vistas) | **19** — peor vista 5 (presupuesto 32/vista) | 19 — peor vista 5 |
| instanciadas / de respaldo | **244 893 / 0** | 243 285 / 0 |
| carga final en pantalla | **292 hormigas + 684 ítems** | ídem |
| tick final / stream agotado | 12 000 a t≈8 s | 12 000 a t≈8 s |
| frames totales de la corrida | 15 233 | 15 150 |

Lectura:

- **La escena se sostiene con 2,44 ms por frame con las 8 colonias dibujándose en
  pantalla**: un **15 % del presupuesto de 16,6 ms a 60 fps** (contra el 3 % de
  §4.3). El factor ~5× entre los dos modos es lo que cuesta la presentación real:
  en modo ventana el editor pinta de verdad las cuatro vistas, con su UI alrededor,
  mientras que en batch solo se recorre el camino de CPU.
- **El vsync no llegó a aplicarse, y se dice en vez de venderse.** El proyecto tiene
  el nivel Ultra (`vSyncCount: 1`), pero la corrida `--live --vsync` midió 403,8
  frames/s: dentro del 1 % de la de vsync apagado. La causa probable está en cómo
  lo aplica el editor: el vsync lo paga el Game view, y una corrida
  `-executeMethod` no arranca con una ventana de juego en primer plano. Se deja como
  hipótesis con su evidencia (las dos cifras coinciden dentro del 1 %), no como
  explicación cerrada. Conclusión honesta: **no hay un número de «60 fps
  presentados» en esta tabla**; lo que hay es coste de CPU con presentación —una
  cota mucho mejor que la de batch, pero no el refresco de la pantalla—. Ese número
  exige el Play pass a mano (o un build).
- Las muestras t1…t6 miden **0 hormigas y 0 llamadas** (arranque del stream, con
  los cuatro procesos del CLI y el búfer llenándose) y son también las del peor
  caso (213,7 frames/s). La fase estable —la que se publica— empieza cuando el
  mundo ya está en pantalla, y el stream se agota en t≈8 s, así que esas 31
  muestras son el coste de repintar el estado final.

### 4.5 Dos defectos reales que el medidor destapó

1. **El instancing dejaba el tablero SIN HORMIGAS.** `Graphics.DrawMeshInstanced`
   *lanza* `InvalidOperationException: Material needs to enable instancing` si el
   material no lo tiene activado — y los materiales de la escena los crea el
   bootstrapper sin esa marca. La excepción abortaba el resto del `Draw`, así que
   **cada frame** moría con 20 hormigas en pantalla y 0 llamadas de dibujo. Es el
   mismo síntoma que reportó el jugador en su día («no se ven hormigas»), con otra
   causa. Arreglado en los dos bootstrappers (instancing por construcción) y en el
   presenter (activación defensiva, lista negra por material con caída a una
   llamada por objeto, y contadores `LastInstancedCalls`/`LastFallbackCalls`). El
   medidor ahora **falla** si hay llamadas de dibujo pero ninguna instanciada.
2. **El informe se escribía en una ruta vacía.** `Path` «Invalid path»: la
   configuración de la sonda vivía en statics y **entrar en Play recarga el
   dominio**, así que los valores volvían al inicializador. Movida a
   `SessionState` — exactamente el defecto que la sonda del multi-visor ya había
   documentado y que aquí volvió a morder por copiar el patrón a medias.

Los dos se destaparon porque la sonda **falla en vez de reportar**: su guard de
«medición vacía» (0 hormigas o 0 llamadas con el mundo en marcha) y el de
«instanciadas = 0» convierten un verde falso en un rojo.

### 4.6 El TAMAÑO del bloque: el equilibrio medido (rodaja 3bis)

El lado del bloque es lo único que el LOD elige, y la elección es un intercambio
puro entre sus dos costes:

- **más fino** ⇒ menos área desperdiciada por bloque activo (un bloque cuesta su
  área entera aunque solo una celda tenga rastro) ⇒ menos celdas visitadas;
- **más fino** ⇒ más contadores que escanear al reconstruir la lista
  (`(lado/bloque)²`) ⇒ más caro mantenerla.

Se mide con `--mode scale --lod-blocks 4,8,16,32,64`, que corre **el mismo mundo**
con cada lado (misma semilla, mismo pool, mismo calentamiento, mismos ticks): la
columna de celdas con soporte sale idéntica en todas las filas, que es la prueba
de que la tabla compara trabajo y no mundos distintos. Los `ns/reconstrucción` son
de una micro-medida aparte (20 000 llamadas sobre una capa propia con patrón
disperso, `ForceBlockListRebuild`), porque medirlos dentro del mundo mezclaría el
depósito que ensucia el soporte. La última fila es la referencia **sin LOD** (grid
completo): la implementación contra la que el test de equivalencia compara celda a
celda. Informes crudos en `artifacts/lod-sweep-*.txt`.

**Grid 256², 8 colonias, 6 000 ticks** — rastro forrajeado, 36 228 celdas con
soporte (1.7 % del grid):

| bloque | contadores por capa | celdas visitadas por operación | reconstr./tick | contadores escaneados/tick | ns/reconstr. | reconstr. (µs/tick) | % del tick | ms/tick |
|---|---|---|---|---|---|---|---|---|
| **4** | 131 072 | **5.3 %** | 2.47 | 10 136 | 4 907 | 12.1 | **2.55 %** | 0.48 |
| **8** | 32 768 | **8.7 %** | 2.47 | 2 534 | 858 | 2.1 | **0.44 %** | **0.48** |
| 16 | 8 192 | 13.8 % | 2.47 | 634 | 227 | 0.6 | 0.11 % | 0.50 |
| 32 | 2 048 | 23.4 % | 2.47 | 158 | 86 | 0.2 | 0.04 % | 0.54 |
| 64 | 512 | 38.7 % | 2.47 | 40 | 30 | 0.1 | 0.01 % | 0.60 |
| **sin LOD** | 32 768 | **100 %** | 0.00 | 0 | — | — | — | 1.10 |

**Grid 512², 8 colonias, 6 000 ticks** — 35 905 celdas con soporte (0.4 %):

| bloque | contadores por capa | celdas visitadas por operación | reconstr./tick | contadores escaneados/tick | ns/reconstr. | reconstr. (µs/tick) | % del tick | ms/tick |
|---|---|---|---|---|---|---|---|---|
| **4** | 524 288 | **1.3 %** | 2.52 | 41 310 | 16 530 | 41.7 | **4.55 %** | 0.92 |
| **8** | 131 072 | **2.0 %** | 2.52 | 10 327 | 3 409 | 8.6 | **0.95 %** | **0.90** |
| 16 | 32 768 | 3.2 % | 2.52 | 2 582 | 1 100 | 2.8 | 0.30 % | 0.92 |
| 32 | 8 192 | 5.1 % | 2.52 | 645 | 376 | 0.9 | 0.10 % | 0.96 |
| 64 | 2 048 | 8.9 % | 2.52 | 161 | 94 | 0.2 | 0.02 % | 1.06 |
| **sin LOD** | 131 072 | **100 %** | 0.00 | 0 | — | — | — | 3.51 |

**Grid 256², 8 colonias, 1 500 ticks** — mundo joven, rastro fino, 7 479 celdas con
soporte (0.4 %):

| bloque | contadores por capa | celdas visitadas por operación | reconstr./tick | contadores escaneados/tick | ns/reconstr. | reconstr. (µs/tick) | % del tick | ms/tick |
|---|---|---|---|---|---|---|---|---|
| **4** | 131 072 | **1.2 %** | 2.31 | 9 464 | 4 816 | 11.1 | **3.95 %** | 0.28 |
| **8** | 32 768 | **2.0 %** | 2.31 | 2 366 | 828 | 1.9 | **0.67 %** | **0.28** |
| 16 | 8 192 | 3.4 % | 2.31 | 592 | 229 | 0.5 | 0.18 % | 0.29 |
| 32 | 2 048 | 5.8 % | 2.31 | 148 | 66 | 0.2 | 0.05 % | 0.31 |
| 64 | 512 | 15.6 % | 2.31 | 37 | 26 | 0.1 | 0.02 % | 0.36 |
| **sin LOD** | 32 768 | **100 %** | 0.00 | 0 | — | — | — | 0.94 |

**El veredicto, y por qué es 8.** El valor histórico era 16, elegido a ojo cuando
el LOD se implementó; medido, **no era el mejor en ninguno de los tres mundos**:

1. **El bloque 4 pierde por su propia lista.** Ahorra entre 0.7 y 3.4 puntos de
   celdas sobre el 8 (según el mundo), pero reconstruir la lista le cuesta
   **2.55 % del tick** (256²) y
   **4.55 %** (512²) frente al 0.44 % y 0.95 % del bloque 8 — con los mismos
   ms/tick o peor. La razón está en los contadores: 131 072 por capa (256²) y
   524 288 (512²) escaneados en cada reconstrucción.
2. **El 16 y todo lo más grueso regalan celdas por un ahorro que ya es ruido.**
   Del 16 al 64 la reconstrucción baja del 0.11 % al 0.01 % del tick (nada), y las
   celdas visitadas suben del 13.8 % al 38.7 % (mucho): el intercambio ya está
   desequilibrado en la otra dirección.
3. **Solo el 8 está en el codo de las tres curvas**: la reconstrucción se queda por
   debajo del 1 % del tick (0.44 / 0.95 / 0.67 %) y las celdas visitadas, las
   mínimas alcanzables sin pagar lista. Ningún mundo lo cambia — el 512² se lleva
   el bloque 4 al 4.55 % de su tick, y ahí el 8 gana también en ms/tick (0.90
   frente a 0.92).

De esa tabla salió el valor por defecto actual (`DefaultLodBlockSize = 8`), que
de paso **no cambia ninguna celda del mundo**: los 6 pines de hash siguieron
pasando sin regenerarse, y hay test de ello (equivalencia bit a bit con lados 1,
2, 3, 4, 8, 16, 32, 64 y 96 —incluidos los que no dividen el grid, que ejercitan
el recorte de los bloques de borde— y un mundo corrido con bloques 4 y 32 que
cierra con el mismo `HashLine`).

**El coste que la tabla deja fuera, y que conviene no olvidar.** Los contadores del
soporte se pagan también al **depositar** (un incremento por celda que pasa de nula
a con rastro) y en `RebuildSupport` al cargar un checkpoint (que escanea el grid
completo una vez, con el bloque que sea). Lo que la tabla mide es el TICK, que es
donde está el presupuesto.

**Criterio de salida de F5.3** («N colonias estables a 60 fps en la escena de
juego»), con todas las piezas ya medidas: con 8 colonias, el Core cuesta **0,47 ms
por tick** (2,8 % de un frame a 60 fps, con el bloque 8), la vista en batch **0,51
ms por frame** y la vista con ventana —bucle y presentación reales— **2,44 ms por
frame** (15 % del presupuesto), con **19 llamadas de dibujo** por frame y todas
instanciadas. Lo que sigue sin medir es un **build de jugador** (el framerate con
el vsync del monitor aplicado, fuera del editor) y el gate de píxeles.

## 5. Qué NO demuestra esta rodaja

- **Los fps de un build de jugador.** Hay dos lecturas de la vista: **0,51 ms/frame**
  en batch (coste de CPU, §4.3) y **2,44 ms/frame** en el editor con ventana (§4.4).
  La segunda incluye presentación real, pero es **dentro del editor** (con su UI, su
  Game view y sin vsync aplicado). Un build de jugador con el vsync del monitor
  sigue sin medir, y el tiempo de GPU no está desglosado.
- **El provecho del canal E.** El stream ya emitía RLE de celdas no nulas, así que
  el LOD **no** cambia lo que viaja al presenter; cambia el coste de la difusión
  en el Core. Son dos ahorros distintos y conviene no confundirlos.
- **Que el LOD valga para mundos más grandes que 256²** — ya no es una incógnita:
  §4.6 lo mide a 512² y sale **mejor** que a 256² (2.0 % de celdas visitadas con
  bloque 8, y el ahorro frente al grid completo pasa de 11× a 50×). Lo que sigue sin
  medir es por encima de 512² y con más de 8 colonias a la vez.

## 6. Verificación

- **496/496 tests** headless (los 480 de la rodaja 3 más 16 de la 3bis: la
  equivalencia bit a bit con 9 lados de bloque, la contabilidad de las
  reconstrucciones, el mundo invariante al bloque en 3 parejas y la dirección del
  equilibrio, y 2 del reporte del barrido).
- **Los 6 pines de hash pasan sin regenerarse**: el LOD es exacto, así que el
  mundo no se movió — stream canónico, replay con drops, Atta, invasión y NEAT.
- **El proyecto Unity compila en batch con 0 errores y 0 avisos** con el presenter
  instanciado (`scripts/check-unity-compile.sh`).
- **Cómo re-medir**: `--mode scale` (Core) y `--mode scale --lod-blocks 4,8,16,32,64
  --lod-colonies 8` (el equilibrio del bloque del LOD, §4.6);
  `bash scripts/perf-scene.sh` (la vista
  en batch: el coste de producir frames) y `bash scripts/perf-scene.sh --live`
  (el Play pass con ventana: bucle y presentación reales; `--vsync` intenta respetar
  el del proyecto). Cada modo escribe su informe — `artifacts/perf-scene.json` y
  `artifacts/perf-scene-live.json` — y conserva el log si falla. Los analizadores se
  verifican sin editor con `bash scripts/perf-scene.sh --selftest`; los 6 pines, con
  `scripts/check-*.sh`, y los MonoBehaviours, con `bash scripts/check-unity-compile.sh`.
- **Ojo con lo que deja en el árbol**: el medidor monta la escena con el
  bootstrapper, así que regenera `MultiSim.unity` y sus materiales. Son artefactos
  generados y el contenido queda canónicamente idéntico (ids locales y orden son
  de cada sesión de Unity), pero el `git status` sale con ~60 ficheros: el script
  lo avisa y deja el comando para dejarlo limpio.
