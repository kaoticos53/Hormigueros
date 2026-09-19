# F5.3 rodaja 3 — Escala: LOD de difusión e instancing (medido)

Estado: **HECHA (2026-09-18)** · padre: [`fase5-plan.md`](fase5-plan.md) §4 ·
estado consolidado: [`estado-proyecto.md`](estado-proyecto.md).

Es la última rodaja de F5.3 y la que cierra su criterio de salida. Aquí están las
dos piezas, la prueba de que ninguna de las dos cambia el mundo y las cifras con
las que se juzga (incluidas las que no son bonitas). Al final, §4.7 y §4.8 cierran
el criterio con las cinco lecturas: Core, vista en batch, vista con presentación,
framerate presentado del build y **coste por frame dentro del build**.

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
juego»): con 8 colonias, el Core cuesta **0,47 ms por tick** (2,8 % de un frame a 60
fps, con el bloque 8), la vista en batch **0,51 ms por frame** y la vista con ventana
—bucle y presentación reales— **2,44 ms por frame** (15 % del presupuesto), con **19
llamadas de dibujo** por frame y todas instanciadas. Las dos lecturas que faltaban
aquí —el **build de jugador** con el vsync del monitor y su **coste por frame dentro
del build**— están en §4.7 y §4.8.

### 4.7 El build de jugador: el framerate PRESENTADO y la puerta de píxeles

Las dos lecturas anteriores miden el COSTE de producir frames, y la de ventana (§4.4)
ni siquiera llega a presentar: en el editor el vsync no se aplica (`vSyncCount=1` y
403,8 fps). El criterio de la fase —«N colonias estables a 60 fps»— solo se cierra en
un BUILD, que entrega frames a la pantalla con siguiente frame vecino.

`bash scripts/player-perf.sh` hace las dos cosas en un comando: construye el player
(escena del multi-visor, 4 vistas × 2 colonias, grid 256 — `PlayerBuild` en el editor)
y lo ejecuta con la sonda del player, que muestrea frames/s y pasa la PUERTA DE
PÍXELES **por vista**. Medido el 2026-09-19, Unity 6000.6.0f1, backend Mono2x, build de
99 MB (18 s el primer build, 9 s el de §4.8), ventana 1280×720, 12 s de calentamiento +
30 s de ventana, 30/30 muestras con la ventana en primer plano (informe:
`artifacts/perf-player.json`, re-medido en la misma sesión que §4.8 — mismo build, sin
los estadísticos de tiempos de frame — con el mismo resultado):

| medida | valor |
|---|---|
| frames/s (mediana) | **59,997** |
| peor muestra / mejor | 55,9 / 60,1 |
| intervalo presentado | 16,668 ms/frame |
| refresco del monitor | 59,997 Hz · `vSyncCount`=1 · `targetFrameRate`=−1 |
| **presentado al refresco** | **sí** (dentro del 5 %) |
| llamadas de dibujo | 19, **todas instanciadas** (37 172 instancias, 0 de respaldo) |
| carga final | 292 hormigas y 684 ítems · tick 12 000 |
| puerta de píxeles | **verde en las 4 vistas** (antPx 132–160 · portadoras 34–76 · ítems 189–213 · suelo tierra 0,42:0,329:0,231) |

Lo que dice el número: el build **entrega al refresco del monitor** con el mundo
cargado — el techo de 60 fps lo pone la pantalla, no el juego. Lo que **no** dice: el
COSTE del frame en el build; con el vsync entregando al refresco, el coste queda tapado
por construcción. Eso lo mide §4.8, que apaga el vsync justo para que la pantalla deje
de taparlo.

La puerta mira PÍXELES, y en un player no hay PNG posible (ver defecto 2), así que cada
vista deja además su frame en una **mosaica de caracteres** (24×10, del frame real de
la cámara): `a` hormiga · `c` portadora · `i` ítem · `e` tierra. La vista 0 del informe:

```text
       iieeiieieiee      /      ieeeeieiiieii     /      eiieeeieeiei      /
       eeaeeeeeiiie      /     iiceaaeaaaaii      /     iaacaaecaaaee      /
       eaaeaaaeaaiei     /      ieieicieaeei      /      eeiiieeiiiii      /
       eiieiieieieei     /
```

#### Los tres defectos que el primer build destapó

1. **El juego NO compilaba como player.** `ImportDialogBehaviour` usa `DragAndDrop` y
   `DragAndDropVisualMode`, que son API de **UnityEditor**, sin guarda: el editor compila
   Assembly-CSharp con referencia a UnityEditor (por eso allí pasaba) y el build del
   jugador reventó con **ocho CS0103**. El drag & drop queda bajo `#if UNITY_EDITOR`
   —arrastrar un archivo desde el explorador es una capacidad del editor: un player no
   la tiene— con `IsDragHovering => false` en la otra rama para que el HUD no cambie.
2. **La captura de pantalla necesita un módulo que el manifest no tenía.**
   `ScreenCapture` vive en `com.unity.modules.screencapture`, que el manifest mínimo no
   incluía: la sonda del Play pass (`ProbeVisualSample`, F5.1) **no compilaba** — llevaba
   rota desde el recorte del manifest sin que nadie lo notara, porque el pass del editor
   no se había vuelto a correr. Se repuso el módulo (lo necesita la verificación visual
   del proyecto, no el juego) y **la sonda del player no lo usa**: su vista del mundo sale
   de la cámara a un `RenderTexture`, sin captura de pantalla.
3. **La puerta medía el color equivocado.** En el multi-visor las hormigas se pintan con
   `ColonyAntMaterials` (una por colonia: naranja y azul), no con `AntMaterial` (marrón
   oscuro): la puerta daba **antPx=0 con 292 hormigas dibujadas**. El Play pass del editor
   tenía el mismo defecto por el mismo motivo. `FrameGate` acepta ahora la LISTA de
   colores por familia.

#### Una sola puerta, verificada headless

`FrameGate` (pura, sin UnityEngine) es ya la única implementación del recuento de
píxeles: la usan `ProbeVisualSample` (editor) y la sonda del player. **11 tests
headless** cubren los tres casos que importan —tierra con hormigas pasa, tierra SIN
hormigas suspende, blanco suspende (el defecto del F5.1)—, más la ventana de muestreo
calculada del encuadre y el fondo declarado. Dos de esas pruebas salieron de defectos
reales medidos aquí:

- **La ventana no puede ser fija.** Una vista 16:9 deja el tablero en su 54 % central,
  así que con la ventana del editor (0,15–0,85) medio muestreo cae FUERA y la «mediana
  del suelo» es el fondo: la sonda calcula la ventana del encuadre real.
- **El fondo hay que declararlo.** El fondo del multi-visor (56,45,33) dista 12 del tono
  de hormiga: si no está en la paleta, cada píxel de fondo se cuenta como hormiga.

### 4.8 El COSTE por frame dentro del build (5ª pieza del criterio)

El framerate presentado (§4.7) tiene un problema de fondo: **con el vsync entregando al
refresco del monitor, 60 fps es el techo de la PANTALLA**, no del juego. Un build que
presenta a 59,997 fps no dice si produce frames en 16 ms o si los tendría listos en 2. La
única forma de publicar el coste es medirlo donde el juego no espera a nadie: dentro del
player, con el vsync apagado, y cronometrando la CPU desde dentro.

`bash scripts/player-perf.sh --cpu` hace eso: construye el player con los estadísticos
de tiempos de frame encendidos (`enableFrameTimingStats`, que `PlayerBuild` enciende
sólo para esta corrida y **restaura al terminar** — es un ajuste que vive en
`ProjectSettings` y no debe quedar cambiado) y lo ejecuta con `-antsimPerfNoVsync 1`. La
sonda lee el `FrameTimingManager` **frame a frame** (26 774 frames con dato de 26 778) y
resume cada ventana con la MEDIANA —una recolección de basura no debe mover el número
publicado—. Medido el 2026-09-19, Unity 6000.6.0f1, backend Mono2x, MISMO build y mismo
régimen que §4.7 (4 vistas × 2 colonias, grid 256, boost ×10, 292 hormigas y 684 ítems
en pantalla, tick 12 000), 12 s de calentamiento + 30 s de ventana, 30/30 muestras con
la ventana en primer plano (informe: `artifacts/perf-player-cpu.json`):

| medida | valor |
|---|---|
| frames/s sin vsync (mediana) | **683,7** (peor 518,7 · mejor 717,2) |
| ms por frame producidos | **1,463** |
| **CPU por frame** (`cpuFrameTime`) | **1,294 ms** (mín 1,248 · máx 1,661) |
| … hilo principal (`cpuMainThreadFrameTime`) | 1,272 ms |
| … hilo de render (`cpuRenderThreadFrameTime`) | 0,277 ms |
| espera en Present | 0,003 ms |
| GPU por frame (`gpuFrameTime`) | 0,127 ms (mín 0,061 · máx 0,132) |
| **% del presupuesto de 16,667 ms** | **7,8 %** de CPU (8,8 % contando el intervalo producido) |
| margen sobre el presupuesto | **×12,9** (la CPU sola cabría a 773 fps) |
| cuello de botella (clasificación de Unity) | **CPU** |
| vsync | efectivo 0 (el del proyecto es 1) · `syncInterval`=1 (ver nota) |
| puerta de píxeles | **verde en las 4 vistas** |

Lectura de los números, sin adornos:

- **El coste es una fracción del presupuesto.** 1,294 ms de CPU por frame contra los
  16,667 ms de 60 fps: **7,8 %**. El intervalo realmente producido (1,463 ms/frame) es
  un 13 % mayor que el `cpuFrameTime` —diferencia de dónde empieza y acaba cada
  ventana de medida— y aun así son **8,8 %**. Publico las dos y uso la conservadora.
- **El techo medido es 683,7 fps** con el mundo cargado. Es decir: sin el vsync, el
  build tendría margen para ~11,4 veces los 60 fps del criterio.
- **La GPU no es el cuello** (0,127 ms), pero la clasificación «CPU» de Unity aquí es
  casi formal: con 1,3 ms de frame total, lo que dice es que el hilo principal está más
  cerca del total que la GPU, no que la CPU esté apretada. Lo que importa es que ambas
  cifras están muy por debajo del presupuesto.
- **La espera en Present es 0,003 ms**, que es la prueba de que el vsync estaba de
  verdad apagado (si no, la espera se comería los ~15 ms que faltan hasta el refresco).
  Ojo con un detalle de la API: `FrameTiming.syncInterval` sigue reportando **1**, así
  que ese campo NO sirve como evidencia aquí; la prueba son la espera y los 683 fps.

**Qué NO dice este número.** El coste medido es el del **player**, que lee el stream y
dibuja; la simulación del mundo corre en **otro proceso** (el CLI, que es quien emite el
stream). Ese coste es el de §4.9. Los tiempos de GPU los declara el backend (D3D11) y no
se han verificado por otra vía, y todo esto es de **una máquina** (la del repo).

**El modelo es puro y está probado.** El resumen (medianas, fracción del presupuesto,
margen y clasificación de cuello de botella) vive en `FrameCost`, sin UnityEngine, con
**12 tests headless**: la mediana ignora los ceros (un frame sin dato no costó 0 ms) y la
clasificación es la del ejemplo oficial de `FrameTiming`, con sus umbrales. Dos de las
pruebas son los casos que cambian la decisión: sin tiempos de GPU lo declara
(`indeterminado`) en vez de inventar, y un coste por encima del presupuesto suspende.

### 4.9 El coste del SISTEMA COMPLETO (6ª pieza del criterio)

El número de §4.8 es el del **player**. Pero el mundo lo simula **otro proceso**: el CLI
que emite el stream. Y no es un detalle contable — la simulación compite por los mismos
núcleos y, como se ve abajo, es **el 97 % de la CPU que el sistema gasta** para que un
frame exista. `bash scripts/player-perf.sh --system` la mide.

#### El montaje tuvo que cambiar por lo que la primera corrida enseñó

La primera versión subió el horizonte a 200 000 ticks «para que el CLI siguiera
simulando durante toda la ventana». Resultado: **cero datos**. Este CLI simula el
horizonte entero y **escribe el stream cuando termina**, así que con un mundo 17× más
largo no llegó a emitir ni una línea en 42 s; la sonda lo detectó (0 ticks, 0 hormigas,
0 draw calls) y suspendió con salida **10** en vez de publicar un verde vacío. Lo que
hace falta es lo contrario: que la ventana **contenga las dos fases**, y para eso el
calentamiento baja a **2 s** y el horizonte se queda en el del criterio (12 000 ticks).

Y eso destapó el defecto de fondo del diseño de la medida: **la CPU del CLI y la entrega
de ticks están desacopladas**. En la corrida de abajo, de los 9,7 s de CPU del hijo, **6 s
ya estaban gastados a los 2 s**, cuando el player no había recibido ni un tick; el resto se
consumió entregándolos (con la tubería y el parseo como cuello). Emparejar CPU y ticks
**por ventana** daba **0,016 ms/tick** en vez de los 0,2 reales — un número 12× barato que
habría parecido perfectamente razonable. Por eso el precio del tick se mide como el
**agregado de la vida entera del hijo** (toda su CPU entre todos los ticks que simuló) y
la corrida exige que el CLI **termine su horizonte dentro de la ventana**: si no, su CPU
es parcial y el cociente mentiría.

#### La medida

Medido el 2026-09-19, Unity 6000.6.0f1, backend Mono2x, MISMO montaje que §4.7/§4.8
(4 vistas × 2 colonias, grid 256, boost ×10, 292 hormigas y 684 ítems en pantalla, tick
12 000), vsync apagado, 2 s de calentamiento + 30 s de ventana, 30/30 muestras con la
ventana en primer plano (informe: `artifacts/perf-player-system.json`):

| medida | valor |
|---|---|
| **pata del player** (mundo cargado) | **1,116 ms/frame** = **6,7 %** del presupuesto de 16,667 ms (margen ×14,9) |
| CPU del CLI (las 4 vistas, toda su vida) | **9 671,875 ms** — 2 343,8 · 2 484,4 · 2 390,6 · 2 453,1 ms |
| ticks simulados | **48 000** (4 × 12 000) · CLI **terminado dentro de la corrida** |
| **precio del tick** | **0,201 ms/tick** |
| ticks que avanza un frame al reloj del juego | **200** (4 vistas × 50) |
| **pata del mundo** | **40,299 ms/frame** |
| **SISTEMA** | **41,415 ms/frame agregados = 2,485 núcleos** al reloj del juego |
| qué parte es el mundo | **97,3 %** del sistema |
| fases de la corrida | **9** ventanas simulando · **22** con el mundo en pantalla · **2** de solape |
| agregado de la corrida (las dos fases mezcladas) | 1,463 ms/frame |
| frames/s sin vsync (mediana) | 786,9 |
| puerta de píxeles | **verde en las 4 vistas** |
| vsync | efectivo 0 (el del proyecto es 1) |

Y la comprobación que hace creíble la proyección, con el mismo binario y los mismos
argumentos, en solitario: **12 000 ticks en 2,09 s de pared con 1,98 s de CPU** =
0,165 ms/tick, es decir **6 047 ticks/s de un núcleo**. El reloj del juego pide 3 000
ticks/s por vista, así que la proyección es **alcanzable**: lo que cuesta no es imposible,
es 2,5 núcleos. (La corrida en pareja sale un 22 % más caro por tick que en solitario:
contención y tubería.)

#### Cómo se lee, porque son dos cosas distintas

- **El lado de los fps es la pata del player**: 1,116 ms de 16,667 → la vista va
  sobrada (×14,9). Coincide con §4.8 (1,294 ms) dentro del ruido entre corridas, y la
  diferencia se explica porque esa corrida tenía el mundo cargado durante toda la
  ventana y ésta solo en 22 de 29 muestras.
- **El lado del mundo es un coste AGREGADO**: 41,4 ms de CPU por frame. **No** es una
  latencia de frame —la simulación corre en otros procesos y se reparte entre
  núcleos—, así que no se compara contra los 16,667 ms de un frame sino contra
  **núcleos**: 41,415 / 16,667 = **2,485 núcleos** sostenidos al ritmo del juego. Ese es
  el número que dice cuántas vistas o colonias caben en una máquina.
- **Y por eso la pieza publica las dos patas por separado**: la del player decide si se
  cumplen los 60 fps, la del mundo decide cuánto juego cabe. Juntarlas en un solo «coste
  del sistema» habría escondido justo lo que se acaba de aprender.

**El modelo es puro y está probado.** El resumen vive en `FrameCost` (sin UnityEngine),
con **10 tests headless** que fijan las decisiones de arriba: el precio del tick es el
agregado y no la mediana de ventanas (con el caso real de este apartado como prueba), un
frame de tablero **vacío** no es un frame del juego, sin el CLI **terminado** el precio
por tick no vale, y sin alguna de las dos patas el resumen **no se declara medido** — el
falso verde que esta pieza existe para no cometer.

#### Un artefacto commiteado que iba por detrás del generador

Los materiales del bootstrapper se commitean (son lo que ve un checkout limpio), y
**cuatro de los cinco materiales de feromonas tenían `_BaseMap` SIN textura**
(`m_Texture: {fileID: 0}`) mientras el generador escribía la referencia. Salieron a la
luz al comparar el árbol con HEAD después de una pasada de medición: la escena y 73 de 76
materiales salían canónicamente idénticos, y en los de feromonas el generador no coincidía.

Qué significa ese `fileID: 0`, que es lo que lo hace algo más que cosmética: el material es
**transparente** con `_BaseColor` blanco y alpha 1, así que sin textura el shader muestrea
**blanco** y el quad se pinta **blanco opaco tapando el tablero** mientras no llega ningún
frame de feromonas (escena recién abierta, canal E apagado, primer tick). Con su textura de
reposo —1×1 blanca con alpha 0— el reposo es **invisible**. Es el mismo defecto del Play pass
de F5.1 («el terreno es blanco») por la puerta de atrás, y el síntoma exacto con el que el
17-09 apareció un material sin su textura tras una pasada del generador.

**El generador va por delante, y se comprobó por medición.** Tres generaciones independientes
—dos del multi-visor y una de la escena simple, con una **borrando la textura de reposo** para
forzar su recreación en la MISMA pasada— producen el material de cada vista **canónicamente
idéntico** (`f0c6f473…`, `3ed42a58…`, `4c953c4b…`, `3525b9d9…`) y siempre con la referencia.
Eso descarta la hipótesis de que la referencia se pierda cuando la textura se crea en la misma
pasada: la pérdida es de una corrida vieja (la misma clase de puntero muerto que arregló la
idempotencia del relleno el 17-09), y el set commiteado era **inconsistente consigo mismo**
—`PheromoneMat_V2` sí tenía su textura y los otros cuatro no—, que es la firma de una pérdida
parcial y no de un diseño.

Arreglo: los cuatro materiales quedan con su referencia (7 líneas) y el contenido es
canónicamente idéntico al que escribe el generador (comparado archivo a archivo). Y para que
no vuelva a pasar en silencio, **2 tests headless** (`PheromoneMaterialGuardTests`) leen los
`.mat` del repo y exigen lo que un checkout limpio necesita: la referencia no puede faltar, la
guía tiene que **existir** (un puntero muerto es el mismo defecto y más difícil de ver) y la
textura tiene que ser 1×1 RGBA32 `ffffff00`, es decir invisible. La guarda **se verificó en
rojo** sobre los materiales de HEAD antes del arreglo.

### 4.10 Cuánto juego cabe: el precio del tick con 2, 4 y 8 colonias por vista

§4.9 mide el precio del tick **a un solo tamaño de mundo** (2 colonias por vista), y ese
número solo responde «cuánto juego cabe» si se sabe cómo escala. `bash
scripts/system-sweep.sh` corre el MISMO montaje (4 vistas, grid 256, horizonte 12 000,
boost ×10, vsync apagado) con **2, 4 y 8 colonias por vista**, reconstruyendo el player en
cada paso porque las colonias van dentro de la escena. Medido el 2026-09-19, 30/30 muestras
enfocadas en cada punto (`artifacts/system-c*.json`, tabla en `artifacts/system-sweep.txt`):

| colonias por vista | **2** | **4** | **8** |
|---|---|---|---|
| colonias en total (4 vistas) | 8 | 16 | 32 |
| **precio del tick** | **0,197 ms** | **0,280 ms** | **0,430 ms** |
| pata del mundo (200 ticks/frame) | 39,32 ms/frame | 55,99 ms/frame | 86,07 ms/frame |
| pata del player | 1,129 ms/frame | 1,219 ms/frame | 1,491 ms/frame |
| **SISTEMA** | **2,427 núcleos** | **3,433 núcleos** | **5,254 núcleos** |
| núcleos por colonia | 0,303 | 0,215 | 0,164 |
| hormigas en pantalla | 292 (+684 ítems) | 553 | 1 075 |
| frames/s sin vsync | 833 | 720 | 589 |
| puerta de píxeles · CLI terminado | verde ×4 · sí | verde ×4 · sí | verde ×4 · sí |

#### Doblar colonias NO dobla el coste

Los núcleos pasan de 2,427 a 3,433 (×1,42) y de 3,433 a 5,254 (×1,53): **sublineal**. La
razón está en las dos partes de la simulación — una **fija**, que no depende de cuántas
colonias haya (la difusión de feromonas de las cuatro rejillas de 256², la evaporación, los
ítems), y otra **variable**, que sigue a las hormigas. Un ajuste por los tres puntos las
separa: **0,122 ms/tick fijos + 0,0097 por colonia** (el marginal sale 0,0104 entre 2 y 4
y 0,0094 entre 4 y 8: prácticamente constante, coherente con que las hormigas por colonia
se quedan en 36,5 · 34,6 · 33,6). En núcleos al reloj del juego, contando la pata del
player, el modelo queda **≈ 1,53 + 0,117 × colonias** y reproduce los tres puntos medidos
dentro del 2 %. El ajuste es una **interpretación de tres puntos**, no una ley medida: lo
medido es la tabla.

#### La respuesta, y por qué no es «más colonias gratis»

La pregunta era cuántas colonias caben en los **2,5 núcleos** de §4.9, y la respuesta es
incómoda pero clara: **esos 2,5 núcleos son exactamente las 8 colonias que ya estaban
medidas** (2,427). El coste es **monótono**: ninguna configuración con más colonias vuelve
a bajar de 2,5. Lo que baja es el **precio unitario** —0,303 → 0,215 → 0,164 núcleos por
colonia—, y eso no es que las colonias extra sean gratis, es que **el coste fijo se reparte
entre más**. En el margen cada colonia extra cuesta **≈ 0,117 núcleos** (0,0097 ms/tick):

| presupuesto | colonias que caben | de dónde sale |
|---|---|---|
| **2,5 núcleos** | **8** | medido (§4.9 y punto «2» de esta tabla) |
| **3,5 núcleos** | **16** | medido (punto «4»: 3,433) |
| **5,3 núcleos** | **32** | medido (punto «8»: 5,254) |
| 8 núcleos | ~55 | **extrapolado** del modelo |
| 28 núcleos | ~226 | **extrapolado** del modelo |

Las dos últimas filas son extrapolación y así hay que leerlas: el barrido mide 8, 16 y 32
colonias, y por encima de ahí lo que hay es el modelo, no una medida. El script publica
además una tercera cifra más conservadora, la del **solo punto medido** (presupuesto ÷ su
núcleos por colonia: 92 · 130 · 171 con 28 núcleos); sale más baja que el modelo porque el
cociente de un punto ya incluye amortizar su propio coste fijo. Las tres cifras son la misma
evidencia leída con distinto atrevimiento, y la que no se atreve a nada es la tabla de los
tres puntos medidos.

#### Lo que el barrido sí decide

- **La vista no es el problema, y escala mejor que el mundo**: de 2 a 8 colonias por vista
  la pata del player sube 1,129 → 1,491 ms/frame (×1,32) mientras el mundo sube ×2,19. Con
  32 colonias en pantalla (1 075 hormigas) el build sigue a **589 fps sin vsync** y la
  puerta de píxeles sale verde en las cuatro vistas: 8 colonias por vista **se ven** de
  sobra.
- **El mundo manda, y su parte fija es la palanca**: de los ≈1,5 núcleos de intercepto,
  ~1,46 son simulación (0,122 ms/tick costeados a 200 ticks/frame) y el resto la vista.
  Esa parte fija no depende de las colonias — es la difusión de las cuatro rejillas a
  256², exactamente lo que ataca el LOD de §4.2. O sea que bajar el coste fijo del LOD no
  ahorra «un poco de tick»: **compra colonias**.
- **La media de 2 colonias por vista sigue siendo buena para el criterio**: 8 colonias al
  reloj del juego caben en 2,5 núcleos, menos del 9 % de una máquina de 28, y las 16 se
  quedan en 3,4.

#### El ruido, medido de paso

El punto «2» de este barrido mide **el mismo montaje que §4.9** con otro build: 0,197 frente
a 0,201 ms/tick, y 2,427 frente a 2,485 núcleos — un **2 %**. Esa es la resolución de estas
medidas entre corridas, y por eso los puntos de 4 y 8 colonias (**+42 %** y **+116 %** sobre
el de 2 en la misma tanda) son diferencias reales y no ruido. Las dos corridas de 2 colonias
se conservan (`artifacts/perf-player-system.json` y `artifacts/system-c2.json`) porque son
la única manera de tener ese ruido medido.

## 5. Qué NO demuestra esta rodaja

- **El coste ya NO es una incógnita, ni el del player ni el del sistema** (§4.8:
  **1,294 ms de CPU por frame** del player, 7,8 % del presupuesto, y 0,127 ms de GPU;
  §4.9: **0,201 ms/tick** de simulación, **41,4 ms/frame agregados = 2,485 núcleos** al
  reloj del juego, con el 97,3 % de la CPU del sistema en el mundo). Lo que sigue sin
  medir es más estrecho: los tiempos de GPU los declara el backend y no se han verificado
  por otra vía; el precio del tick **ya está barrido** con 2, 4 y 8 colonias por vista
  (§4.10: 2,427 → 5,254 núcleos, sublineal, con un fijo de ~0,122 ms/tick y ~0,117 núcleos
  por colonia), así que falta **por encima de 8 colonias por vista** —donde la cifra es
  modelo y no medida— y **por debajo de 2**, con una sola colonia, donde manda el coste
  fijo; y todo es de **una máquina** y de una sesión de medida.
- **El provecho del canal E.** El stream ya emitía RLE de celdas no nulas, así que
  el LOD **no** cambia lo que viaja al presenter; cambia el coste de la difusión
  en el Core. Son dos ahorros distintos y conviene no confundirlos.
- **Que el LOD valga para mundos más grandes que 256²** — ya no es una incógnita:
  §4.6 lo mide a 512² y sale **mejor** que a 256² (2.0 % de celdas visitadas con
  bloque 8, y el ahorro frente al grid completo pasa de 11× a 50×). Lo que sigue sin
  medir es por encima de 512² y con más de 8 colonias a la vez.

## 6. Verificación

- **536/536 tests** headless (los 480 de la rodaja 3, 16 de la 3bis, 11 de la puerta de
  píxeles `FrameGate`, 12 del modelo de coste por frame `FrameCost`, 10 del coste del
  sistema `SystemCost`, 5 del ancla de rutas del player y 2 de la guarda de los materiales
  de feromonas).
- **El criterio de salida de la fase, cerrado con SEIS piezas**: **Core 0,47 ms/tick**
  (8 colonias, grid 256) · **vista 0,51 ms/frame** en batch · **2,44 ms/frame** con
  presentación en el editor · **el build presentado al refresco** (59,997 fps sobre un
  monitor de 59,997 Hz, con la puerta de píxeles verde en las cuatro vistas y 19
  llamadas de dibujo, §4.7) · **el coste por frame DENTRO del build** (1,294 ms de CPU y
  0,127 ms de GPU, 7,8 % del presupuesto, §4.8) · **el coste del SISTEMA COMPLETO**
  (0,201 ms/tick de simulación, 41,4 ms/frame agregados = **2,485 núcleos** al reloj del
  juego, con la pata del player en 1,116 ms = 6,7 % del presupuesto, §4.9) — y **el barrido
  de esa 6ª pieza** (§4.10: 2, 4 y 8 colonias por vista → 2,427 · 3,433 · 5,254 núcleos, y
  las 32 colonias con 1 075 hormigas en pantalla siguen a 589 fps sin vsync con la puerta
  verde en las cuatro vistas).
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
- **El criterio de la fase se mide con dos comandos**: `bash scripts/player-perf.sh`
  (build + sonda del player + puerta de píxeles; informe `artifacts/perf-player.json`,
  y `--skip-build` para reusar el build, `--selftest` para verificar el analizador sin
  Unity y `--log` para reanalizar una corrida). Sale **7** si el vsync del proyecto está
  apagado: sin vsync, esa corrida mide coste y no refresco presentado, y darle el número
  por bueno sería el error que el script existe para no cometer.
  `bash scripts/player-perf.sh --cpu` (informe `artifacts/perf-player-cpu.json`) es el
  modo simétrico: apaga el vsync para medir el **coste**, y por eso allí el vsync
  apagado es el requisito y sale **8** si no llegó a apagarse y **9** si el build no
  trae tiempos de frame. Los informes son ficheros distintos a propósito: con un
  solo nombre, la corrida siguiente borraría la evidencia de la anterior.
- **El coste del sistema se mide con `bash scripts/player-perf.sh --system`** (informe
  `artifacts/perf-player-system.json`), que añade la pata del CLI al modo coste y baja el
  calentamiento a 2 s para que la fase de simulación caiga **dentro** de la ventana:
  con los 12 s del criterio el mundo se termina durante el calentamiento y lo que se
  mediría es un player solo. Sale **10** si falta alguna de las dos fases (el CLI no
  terminó su horizonte, o el mundo no llegó a la pantalla), y `--ticks` /`--warmup`
  ajustan el montaje. Los dos modos exigen el vsync apagado, y las guardas inversas
  (7/8/9/10) existen porque cada corrida mide una cosa distinta y publicar una con el
  nombre de otra es el error que estos scripts existen para no cometer.
- **El barrido del precio del tick se mide con `bash scripts/system-sweep.sh`**
  (2, 4 y 8 colonias por vista; `--colonies 1,2,4` y `--cores N` para el presupuesto con el
  que se lee «cuántas caben»). Cada punto es **un build** (~20 s) más una corrida (~45 s),
  y deja su informe crudo en `artifacts/system-c<N>.json` con el resumen en
  `artifacts/system-sweep.txt`; `--selftest` verifica la tabla sin Unity (la fila medida,
  la sublinealidad de los núcleos por colonia y que un punto sin medir **no** se declare).
  Las colonias por vista viajan al build por `ANTSIM_PERF_COLONIES`, que es el mismo camino
  que el grid y el horizonte.
- **El Play pass del editor ya no se puede conducir**: los bloques 3, 4 y 5 de
  `playpass-live.sh` necesitan `com.unity.pipeline`, que salió del manifest en la poda
  de paquetes (`unity command editor_status` responde «No Pipeline instance found»). La
  verificación visual end-to-end la cubre ahora la sonda del player, que corre la MISMA
  puerta de píxeles sobre frames reales; reactivar el pass es una decisión de
  dependencias (una línea en `Packages/manifest.json`), no una tarea de código.
- **Ojo con lo que deja en el árbol**: el medidor monta la escena con el
  bootstrapper, así que regenera `MultiSim.unity` y sus materiales. Son artefactos
  generados y el contenido queda canónicamente idéntico (ids locales y orden son
  de cada sesión de Unity), pero el `git status` sale con ~60 ficheros: el script
  lo avisa y deja el comando para dejarlo limpio. Con una excepción que conviene
  mirar antes de restaurar a ciegas: si el generador cambió, restaurar deja el
  artefacto **por detrás**, que es como apareció el cuarto defecto de §4.7. La
  guarda de los materiales de feromonas lo detecta sin abrir Unity.
