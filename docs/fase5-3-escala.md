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

**Criterio de salida de F5.3** («N colonias estables a 60 fps en la escena de
juego»): medido hasta donde la medición headless llega — **8 colonias en grid 256
cuestan el 3 % del frame en el Core y 17 llamadas de dibujo** (contador en vivo del
presenter, presupuesto 32). Lo que **no** está medido aquí es el framerate real de
Unity: eso exige el editor y el gate de píxeles del Play pass.

## 5. Qué NO demuestra esta rodaja

- **Los fps de Unity.** El Core se mide headless y el render se mide como plan
  (lotes) y contador (llamadas), no como tiempo de GPU. El número de 60 fps que
  cierra el criterio es una **cota**: el presupuesto de CPU de un frame lo consume
  el Core en un 3 %, y el envío de geometría bajó de cientos de llamadas a una
  docena. La medida directa sigue pendiente del Play pass en el editor.
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
- **Cómo re-medir**: `--mode scale` (tabla), `scripts/check-*.sh` (pines),
  `bash scripts/check-unity-compile.sh` (MonoBehaviours).
