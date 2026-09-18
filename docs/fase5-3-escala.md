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

- el grid se divide en **bloques de 16×16** (`LodBlockSize`) — más finos que el
  tile de render de 64, porque el tile mide *envíos* y el bloque mide *trabajo*;
- cada bloque lleva un **contador de celdas no nulas**, mantenido por incremento
  en `Deposit` y por decremento en la transición a cero (`Commit`). Un bloque con
  contador 0 no puede tener una celda que cambie;
- `Evaporate` y `Diffuse` construyen su lista de **regiones** a partir de los
  bloques con soporte y recorren solo eso. La lista se reconstruye cuando el
  soporte cambia, no en cada celda;
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
| mundo sin forrajeo, 1 500 ticks | **1.2 %** del grid | 249 |
| con warm-v2, 6 000 ticks, 1 colonia | **12.0 %** | 4 307 |
| con warm-v2, 6 000 ticks, 8 colonias | **13.8 %** | 36 228 |

**El ahorro se encoge a medida que el rastro se extiende** (de 80× a 7×), y eso es
la verdad del método: el LOD no mide celdas no nulas, mide *bloques que contienen
algo*. Un rastro fino que cruza el mapa activa muchos bloques de 256 celdas, y cada
bloque activo cuesta su área completa. La palanca para afinarlo es `LodBlockSize`
(8² bajaría la visita a ~la mitad); no se tocó porque 7× ya resuelve el problema y
el contador por bloque se paga en `EnsureActiveBlocks`.

### 4.2 Coste del tick y criterio de salida

`antsim --mode scale --grid 256 --ticks 6000 --colony-counts 1,2,4,8 --seed 42
--seed-pool tests/fixtures/warm-v2.antgenome`:

| colonias | hormigas | ítems | ms/tick | % frame (60 fps) | velocidad máx. | celdas LOD visitadas | soporte |
|---|---|---|---|---|---|---|---|
| 1 | 26 | 171 | 0.06 | 0.4 % | ×553 | 31 488/262 144 (12.0 %) | 4 307 |
| 2 | 51 | 171 | 0.12 | 0.7 % | ×279 | 68 864/524 288 (13.1 %) | 8 882 |
| 4 | 101 | 171 | 0.24 | 1.5 % | ×137 | 138 496/1 048 576 (13.2 %) | 17 836 |
| 8 | 193 | 171 | 0.50 | 3.0 % | ×67 | 289 024/2 097 152 (13.8 %) | 36 228 |

Lectura honesta de la tabla:

- **el Core no es el cuello de botella**: 8 colonias consumen el **3 % del
  presupuesto de un frame a 60 fps** (16,6 ms), y el coste crece lineal en colonias
  (0.06 → 0.50 ms/tick con 8× colonias);
- «velocidad máx.» es la velocidad de la vista (×, base 30 ticks/s del presenter)
  que el CLI podría sostener si solo tuviera que simular. Con 2 colonias —el modo
  de juego real— el techo es **×279**, muy por encima del ×100 del control de
  velocidad; con 8 colonias baja a **×67**, es decir que a ese tamaño la velocidad
  alta de la vista ya no la sostendría el Core solo (y ahí el LOD es lo que evita
  que empeore);
- las llamadas de dibujo del plan instanciado, para el escenario del multi-visor
  (800 hormigas por vista con 4 materiales), son **4 por vista** frente a **800**
  de la línea base — el test lo fija (`instanciado × 100 < base`).

### 4.3 La VISTA medida en el editor (Play pass)

`bash scripts/perf-scene.sh` monta la escena del multi-visor con **4 vistas × 2
colonias = 8 colonias en pantalla** en el grid real del modo juego (256), entra en
Play en batch y muestrea una vez por segundo: frames/s reales, `LastDrawCalls`,
hormigas e ítems por vista. Corrida de 40 s, boost ×10 (grid 256², 12 000 ticks):

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
los fotogramas que verá el jugador con la pantalla refrescando. Se publica como
cota, y por eso el criterio de salida se lee con las dos piezas juntas: **el Core
cuesta 0,50 ms/tick con 8 colonias y la vista 0,51 ms/frame**.

### 4.4 Dos defectos reales que el medidor destapó

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

**Criterio de salida de F5.3** («N colonias estables a 60 fps en la escena de
juego»): con 8 colonias, el Core cuesta **0,50 ms por tick** (3 % de un frame) y
la vista **0,51 ms por frame** con 19 llamadas de dibujo, todas instanciadas. Las
dos piezas están medidas; lo que sigue sin medir es el framerate con presentación
real a pantalla (el Play pass interactivo) y el gate de píxeles.

## 5. Qué NO demuestra esta rodaja

- **Los fps con presentación real.** Lo medido es el coste de producir frames en
  batch (sin vsync ni swap): 0,51 ms/frame con 8 colonias. Eso es una **cota** de
  CPU, no el framerate que verá el jugador — el tiempo de GPU y de presentación no
  está medido, y el Play pass interactivo sigue pendiente.
- **El provecho del canal E.** El stream ya emitía RLE de celdas no nulas, así que
  el LOD **no** cambia lo que viaja al presenter; cambia el coste de la difusión
  en el Core. Son dos ahorros distintos y conviene no confundirlos.
- **Que el LOD valga para mundos más grandes que 256².** A 512² los bloques
  activos se reparten más, y el porcentaje visitado podría subir (más rastro) o
  bajar (mismo rastro en más grid). Está sin medir.

## 6. Verificación

- **480/480 tests** headless (17 nuevos: 5 de equivalencia LOD y contadores, 10
  del plan de lotes, 2 del reporte de escala).
- **Los 6 pines de hash pasan sin regenerarse**: el LOD es exacto, así que el
  mundo no se movió — stream canónico, replay con drops, Atta, invasión y NEAT.
- **El proyecto Unity compila en batch con 0 errores y 0 avisos** con el presenter
  instanciado (`scripts/check-unity-compile.sh`).
- **Cómo re-medir**: `--mode scale` (Core), `bash scripts/perf-scene.sh` (la
  vista: frames/s y draw calls con 8 colonias; guarda el informe en
  `artifacts/perf-scene.json` y conserva el log si falla), `scripts/check-*.sh`
  (los 6 pines), `bash scripts/check-unity-compile.sh` (MonoBehaviours).
- **Ojo con lo que deja en el árbol**: el medidor monta la escena con el
  bootstrapper, así que regenera `MultiSim.unity` y sus materiales. Son artefactos
  generados y el contenido queda canónicamente idéntico (ids locales y orden son
  de cada sesión de Unity), pero el `git status` sale con ~60 ficheros: el script
  lo avisa y deja el comando para dejarlo limpio.
