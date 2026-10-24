# F9 — Memoria episódica de hitos con genes de política

> **Estado: DISEÑO. Nada de esto está implementado.**
> Este documento fija el contrato antes de tocar código: canales nuevos, estado
> por hormiga, genes, hash, checkpoint, migración de cerebros y criterio de
> aceptación. Si la §0 no pasa, no se implementa el resto.

La idea en una frase: **la hormiga deja migas de pan mientras busca y las
desanda cuando vuelve cargada**. Las migas son suyas (no del mundo), viven en el
estado del individuo, y el cerebro decide cuándo marcar y cuándo seguirlas. Los
genes deciden cuántas migas caben, cada cuánto se pone una, cuánto dura y con
cuánta fuerza tira de la hormiga.

---

## 0. Hipótesis y puerta de evidencia

### 0.1 Qué se supone que aporta

Hoy el mundo ya resuelve la vuelta al nido **sin memoria del individuo**:

| necesidad | cómo se resuelve hoy | archivo |
|---|---|---|
| «¿dónde está mi casa?» | campo BFS por pasillos (`NestGuides`) leído por `ApplyCorridorHomeSense` en los canales 9–10 | `WorldSim.cs`, `ApplyCorridorHomeSense` |
| «vuelve cuando llevas carga» | reflejo `ApplyLoadHoming` (mismo campo) | `WorldSim.cs` |
| «vuelve cuando te queda poca reserva» | reflejo `ApplyReturnWhenTired` | `WorldSim.cs` |
| «¿por dónde iba el camino?» | rastro de feromonas de una celda, vida media 2 100 s | `WorldSim.Step`, capas de feromona |

Por tanto una memoria que solo memorice **el nido** no aporta nada: el campo es
exacto y no olvida. La memoria tiene que pagar por lo que el campo **no** sabe:
**dónde estaba la comida y qué camino se recorrió hasta ella**. Ese es el hueco
que queda cuando el rastro se corta (una colonia rival lo pisa, el viento lo
borra, la hormiga se aleja del corredor) o cuando la comida ya no está.

### 0.2 La hipótesis, falsable

> Con memoria de hitos, una colonia entrenada sube el **número de descargas por
> ronda** y el **% de cargadas que llegan al nido**, con el mismo pool, el mismo
> laberinto y la misma semilla, porque la cargada deja de depender del rastro
> para volver.

Umbral pre-registrado (3 semillas, `--until-extinct`, pool `pretrain-maze-los3`,
grid 96, laberinto, reclutamiento): **+10 % de descargas** y **+10 % de
cargadas que llegan**, con **−15 % en el tiempo nido→fuente→nido**. Si no se
mueve ninguna de las tres, la memoria no se queda: es un adorno caro.

### 0.3 Rodaja 0 — la prueba barata, ANTES del contrato v2

Antes de pagar 23 canales, 8 salidas y la conversión de los pools clásicos al
sembrarlos en un mundo con memoria, se prueba la idea **como reflejo puro**
detrás de `--memory`, sin canales nuevos y sin genes:

- al andar sin carga, el mundo apunta una miga cada 20 u (máximo 6, anillo);
- al llevar carga, el mundo tira de la hormiga hacia la miga más reciente y las
  va consumiendo al llegar;
- al descargar, el anillo se conserva para el viaje siguiente;
- el cerebro **no se entera**: no hay sensores ni salidas nuevas.

Es una versión degradada (el cerebro no decide nada), pero mide lo único que
importa ahora: **si el camino de vuelta sirve para algo cuando el rastro ya no
es la guía**. El reflejo se engancha en el mismo punto donde luego vivirá
`ApplyMemoryHoming` (antes de `ApplyLoadHoming`, §5.1), así que el prototipo no
miente sobre el sitio del cambio. Coste estimado: ~60 líneas en `WorldSim` + un
test A/B. Si el número no sube, se para aquí y se documenta el resultado
negativo.

Todo lo que sigue asume que la rodaja 0 pasa el umbral de §0.2.

---

## 1. Modelo: qué es una miga y qué no

### 1.1 Una miga es una posición, no un objeto

```
miga := (x, y, tick_en_que_se_puso)
```

- **No** guarda el id de un ítem: los ítems desaparecen (`FoodItem` se consume) y
  el id quedaría colgando.
- **No** guarda una celda del grid: la posición en unidades evita depender del
  tamaño de celda y del LOD.
- **No** guarda el rumbo: al desandarlas, el rumbo lo pone el control hacia la
  miga, no el rumbo que se llevaba.

### 1.2 El anillo, no la pila

El anillo tiene dos ventajas sobre una pila: cuando se llena, la miga **más
vieja** se cae (no se rechaza la nueva, que es la que interesa), y el mismo
anillo sirve para los dos sentidos:

```
migas:  [ m0  m1  m2  m3  m4 ]        m0 = más antigua, m4 = más reciente
                          ^ cursor

sin carga, siguiendo  →  cursor avanza m0 → m4   (volver a la fuente)
con carga, siguiendo  →  cursor retrocede m4 → m0 (volver al nido)
```

- **Buscando** (`Remember ≥ 0,5`): se empuja la posición actual en el extremo
  nuevo. Si el anillo está lleno, se descarta la más vieja.
- **Volviendo cargada** (`Follow ≥ 0,5`): el cursor arranca en la más reciente y
  **retrocede**: es el camino de ida al revés, que es exactamente «saber la
  vuelta».
- **Volviendo a la fuente** (sin carga, `Follow ≥ 0,5`): el cursor arranca en la
  más antigua y **avanza** hasta la más reciente, que es donde se encontró la
  comida. Esto convierte la memoria en un **segundo viaje sin rastro**, que es
  donde debería verse la ventaja.
- El cursor **avanza solo** al pisar el radio de la miga
  (`WaypointRadius = 10 u`, menor que `PickupRadius = 12 u`), aunque `Follow`
  esté a 0: una miga ya alcanzada no puede dejar a la hormiga orbitando.
- Al **descargar** en el nido no se borra nada: el anillo pasa a ser «memoria
  de la fuente» y el cursor se reinicia en la más antigua.
- Al **recoger** comida, el cursor se reinicia en la más reciente.

Ejemplo con 4 ranuras y `Spacing = 20 u`:

```text
ida:     nido ──m0(24 u)──m1(45 u)──m2(63 u)──► fuente
vuelta:  fuente ◄──m2──m1──m0── nido        (cursor retrocede)
2.º viaje sin rastro:  nido ──m0──m1──m2──► fuente   (cursor avanza)
```

Si la hormiga vuelve a salir y encuentra comida por otro camino, las migas
viejas siguen ahí hasta que caducan o hasta que el anillo las desplace: no hay
que «borrar» nada a mano.

### 1.3 Qué NO hace la memoria

- No sustituye al campo del nido ni a `ApplyLoadHoming`: si el anillo está
  vacío, todo funciona como hoy.
- No es un mapa: 8 posiciones como máximo, sin celdas intermedias, sin
  conectividad.
- No es recursiva ni tiene estado oculto en el cerebro: `IBrain.Evaluate` sigue
  siendo **pura** (mismos sensores ⇒ misma decisión). La memoria es estado del
  **mundo**, como la energía o la carga, y entra al cerebro por sensores.

---

## 2. Contrato de cerebro v2 — 23 sensores / 8 decisiones

**El contrato v1 NO se sustituye: se elige.** `BrainContract.CurrentVersion`
sigue valiendo **1** (el mundo clásico, 19→8→6) y aparece
`BrainContract.MemoryVersion = 2` (23→8→8). El mundo escoge uno u otro al
construirse, según `--memory`.

Por qué no un bump global: el número de pesos del pool aleatorio cambia con el
contrato (214 → 264), así que los pesos sorteados serían otros y **todos los
mundos pinneados se moverían aunque la memoria estuviera apagada**. Eso rompe la
disciplina que F7 y F8 mantuvieron (bandera apagada ⇒ byte a byte). Con dos
topologías, el mundo clásico conserva su RNG, sus pesos y sus 6 pines, y la
memoria es un mundo nuevo solo cuando se enciende.

En código, `WorldSim.DefaultBrainSizes` deja de ser un `static readonly` y pasa
a dos valores: `DefaultBrainSizes` (19→8→6) y `MemoryBrainSizes` (23→8→8). El
constructor valida el par completo según la bandera —19 con 6, o 23 con 8— y
rechaza las mezclas (por ejemplo 23 entradas con 6 salidas) con el mismo mensaje
accionable que ya usa `RequireCompatibleSizes`.

Los canales nuevos van **al final** (regla ya escrita en `AntSensorChannel.cs`:
nunca se reordena). Un cerebro clásico de 19 entradas recibe los 19 primeros y
no ve los 4 nuevos: `AntSensors.CopyTo(target, count)` escribe exactamente
`count` canales (lanza si no es 19 ni 23) y cada cerebro pasa **su** número de
entradas
(`MlpBrain`/`NeatBrain` ya lo tienen en `_inputCount`). La sobrecarga sin
contador escribe los 23 y queda para tests y telemetría. Sin esta separación,
un pool 19→24→6 con el buffer de 24 dejaría meter 23 canales en un cerebro de
19: no rompe la suma (la primera capa solo lee 19), pero es un footgun que un
test de contrato debe cerrar.

### 2.1 Canales nuevos (`AntSensors`, índices 19–22)

| # | nombre | rango | definición |
|---|---|---|---|
| 19 | `MemoDx` | [−1,1] | Componente **adelante** del vector a la miga del cursor, en el marco local (X = rumbo, Y = izquierda). 0 si no hay miga. |
| 20 | `MemoDy` | [−1,1] | Componente **izquierda** del mismo vector. 0 si no hay miga. |
| 21 | `MemoDistance` | [0,1] | `min(1, d / 128 u)` a la miga del cursor. 0 si no hay miga. |
| 22 | `MemoFill` | [0,1] | `Count / Slots` (0 si `Slots = 0`). Cuánto queda de memoria, no cuántas migas «valen». |

El vector se calcula con el **mismo** álgebra que `AntSenses` y
`ApplyCorridorHomeSense` (marco local, `CanonMath.Cos/Sin`, clamp). No se
reutiliza `AntSenses.Build` para esto: los canales de memoria se rellenan en
`WorldSim.Act` **después** de `AntSenses.Build`, igual que hace hoy
`ApplyCorridorHomeSense`, para no meter estado del individuo dentro del
muestreo sensorial del mundo.

`AntSensorChannelInfo.Count` sigue siendo **19** (contrato clásico) y se añade
`AntSensorChannelInfo.MemoryCount = 23`. Los dos valores conviven; el enum gana
las cuatro entradas 19–22, que solo lee un cerebro de 23 entradas.

### 2.2 Decisiones nuevas (`AntDecision`, índices 6–7)

| # | nombre | rango | umbral | efecto |
|---|---|---|---|---|
| 6 | `Remember` | [0,1] | ≥ 0,5 | Empuja una miga en la posición **final** del tick, si se cumplen las reglas de §5. |
| 7 | `Follow` | [0,1] | ≥ 0,5 | Tira de la hormiga hacia la miga del cursor (reflejo `ApplyMemoryHoming`). |

`AntDecision.DecisionCount` sigue siendo **6** (contrato clásico) y se añade
`AntDecision.MemoryDecisionCount = 8`. Los dos campos nuevos existen siempre en
el struct (valen 0 con un cerebro de 6 salidas): `CopyTo` escribe 8,
`AllFinite` los incluye y `DecisionValidator.SanitizeAndClamp` los recorta a
[0,1], con `WantsRemember` / `WantsFollow` al estilo de `WantsInteraction`.

`MlpBrain` no cambia de forma: la convención de activaciones ya es «canal 0 =
tanh, el resto = sigmoid», así que las dos salidas nuevas nacen sigmoideas y
`_outputCount` ya protege los índices que no existen (`> 5`); se añaden los dos
guards `> 6` y `> 7`.

### 2.3 Conteo de pesos

| topología | v1 (19/6) | v2 (23/8) | Δ |
|---|---|---|---|
| 19→8→6 / 23→8→8 | 8·20 + 6·9 = **214** | 8·24 + 8·9 = **264** | +50 |
| 19→16→6 / 23→16→8 | 16·20 + 6·17 = **422** | 16·24 + 8·17 = **520** | +98 |
| 19→24→6 / 23→24→8 | 24·20 + 6·25 = **630** | 24·24 + 8·25 = **776** | +146 |

Nodos totales del snapshot de activaciones (canal F): 33 en v1 → **39** en v2.

`AntGenomeFile` escribe el contrato que **el genoma tiene** (derivado de su
`sizes`), no una constante global; `WorldSimSave` no escribe contrato, pero sus
`sizes` ya lo determinan. Así un archivo clásico sigue diciendo «v1» y uno con
memoria dice «v2», y un mundo rechaza el que no le toca con el mensaje
accionable que ya existe (`RequireCompatibleSizes`).

---

## 3. Estado por hormiga

### 3.1 Estructura

Un struct **blittable**, embebido por valor en `Ant` (que ya es una clase), sin
asignaciones por hormiga ni arrays por individuo:

```csharp
public struct WaypointMemory
{
    public const int MaxWaypoints = 8;

    // Anillo en orden de inserción: no hay índice "cabeza" persistido.
    // La representación concreta se decide al implementar (24 campos + switch
    // de 8 casos, o un `fixed` buffer solo si Core adopta `unsafe`).
    public float X0, Y0;  public ulong Born0;   // miga 0 (la más vieja)
    // … X1..X7, Y1..Y7, Born1..Born7 …
    public byte Slots;    // capacidad efectiva 1..8 (gen)
    public byte Count;    // migas vivas 0..Slots
    public byte Cursor;   // índice lógico 0..Count-1
}
```

- **Coste**: 132 B por hormiga con el anillo lleno; 300 adultas ≈ 39 KB por
  colonia en el peor caso. Con 2 colonias y 600 adultas, < 80 KB — despreciable
  frente a las cuatro capas de feromonas.
- **Sin array**: el anillo se direcciona con `X0..X7` y un `switch`/`Span` sobre
  el propio struct; si el compilador no deja indexar campos, se implementa con
  dos arrays de 8 embebidos como `fixed` buffers **solo si** el proyecto acepta
  `unsafe` en Core (hoy no lo usa). Alternativa preferida: métodos
  `GetX(int)`/`SetX(int,v)` con `switch` (8 casos, JIT los convierte en salto).
- **Invariantes** (los fija un test, no solo el comentario):
  1. `Count ≤ Slots ≤ 8`;
  2. `Slots == 0` ⇒ `Count == 0` y `Cursor == 0`;
  3. `Count > 0` ⇒ `Cursor < Count`;
  4. `Born[0] ≤ Born[1] ≤ … ≤ Born[Count−1]` (orden de inserción);
  5. todas las coordenadas finitas y dentro de `[0, WorldWidth] × [0, WorldHeight]`.

### 3.2 Transiciones

| evento | efecto |
|---|---|
| nace | `Slots` = `slots` del genoma (o `DefaultMemorySlots` si no hay genoma, p. ej. política fijada); `Count = Cursor = 0`. |
| `Remember` (sin carga) | si `dist(última, actual) ≥ Spacing` y `SegmentClear(última, actual)` ⇒ empuja; si `Count == Slots`, cae la más vieja. |
| `Follow` (con carga) | el cursor ya apunta a la más reciente desde el pickup; al pisarla retrocede (`Cursor--`) hasta la más vieja. |
| `Follow` (sin carga) | el cursor avanza hacia la más reciente (vuelta a la fuente). |
| pisa una miga | `dist ≤ WaypointRadius` ⇒ avanza/retrocede el cursor (una miga por tick como máximo). |
| recoge comida | `Cursor = Count − 1` (a desandar desde el final). |
| descarga en el nido | `Cursor = 0` (el anillo queda como memoria de la fuente). |
| muere | el struct se va con el cuerpo; no hay nada que limpiar (`BankAndCompactDeadAnts` compacta la lista). |
| miga caducada | solo se comprueba la **más vieja** (las edades son monótonas): si `Tick − Born[0] > LifeTicks`, se descarta, `Count--` y `Cursor = max(0, Cursor − 1)` para seguir apuntando a la misma miga. O(1), sin recorrer el anillo. |

La caducidad **perezosa** es deliberada: recorrer las 8 migas de cada hormiga
en cada tick para tirar una es trabajo que casi nunca cambia nada.

---

## 4. Genes de política (lo de «dentro del genoma»)

### 4.1 Los cuatro genes

`MlpGenome` gana un vector fijo `Policy` de 4 floats en [0,1]. Cada uno se lee
como parámetro del mundo al nacer la hormiga:

| gen | nombre | mapeo | defecto | significado |
|---|---|---|---|---|
| 0 | `MemorySlots` | `Slots = 1 + round(7·g)` → 1..8 | 0,43 → **4** | cuántas migas caben. |
| 1 | `MemorySpacing` | `Spacing = 8 + 24·g` u → 8..32 | 0,50 → **20 u** | distancia mínima entre migas. |
| 2 | `MemoryLife` | `Life = 60 + 1740·g` s → 60..1800 | 0,50 → **930 s** | vida de una miga (≈ la mitad del rastro de comida). |
| 3 | `MemoryGain` | `Gain = 0,5 + 5,5·g` → 0,5..6,0 | 0,50 → **3,25** | ganancia del reflejo `Follow`. |

No hay gen «memoria sí/no»: **la bandera del mundo es el interruptor**
(`--memory`). Los genes solo ajustan el cómo. Eso evita que un pool importado
llegue «sin memoria» y que la selección tenga que inventar el rasgo desde cero.

`DefaultMemorySlots = 4` es el valor que usan las hormigas sin genoma (política
fijada con `ForcePolicy`) y el que llevan los genes de un genoma migrado o de un
archivo sin bloque de genes.

### 4.2 Herencia y mutación sin mover los pines

Este es el punto delicado: `MlpGenome.Random`, `Crossover` y `Mutate` consumen el
RNG del pool, y ese flujo determina los genomas de todas las generaciones. Un
sorteo extra cambiaría el mundo clásico y **movería los pines de CI**.

Reglas:

- `MlpGenome.Random`: los genes se inicializan a **constantes** (los defectos de
  la tabla). **Cero tiradas extra**: los pesos se sortean exactamente igual que
  hoy.
- `MlpGenome.Crossover`: los genes se copian del **padre A** (el primero del
  torneo), no se sortean. Cero tiradas extra.
- `MlpGenome.Mutate(ref rng, sigma, bool mutatePolicy)`: con
  `mutatePolicy = false` no toca los genes ni consume RNG (modo clásico). Con
  `true`, muta los 4 con gaussianas del mismo RNG y los recorta a [0,1].
- `GenomePool` recibe `memoryEnabled` en el constructor y pasa
  `mutatePolicy: memoryEnabled` en `Birth()`. Así:
  - mundo clásico (`--memory 0`) ⇒ **byte a byte el de siempre**, genes
    congelados en el defecto, pines intactos;
  - mundo con memoria ⇒ 4 tiradas extra por nacimiento: el flujo del pool es
    nuevo, pero **no afecta a ningún pin** porque ningún comando pinneado
    enciende la memoria.
- `MlpGenome.DistanceTo` **sigue midiendo solo pesos**: `DiversityFloor` y la
  novedad de la cuarentena conservan su significado. Los genes evolucionan por
  selección, no por distancia.

`Clone` y `Expand` copian los genes tal cual. `Expand` no cambia entrada ni
salida (ya lo exige), así que la escalera 8→16→24 no toca la política.

`MlpGenome.ContractVersion` deja de ser la constante global y se deriva de
`sizes`: 19/6 ⇒ 1, 23/8 ⇒ 2. `NeatGenome` hace lo mismo con sus nodos. Es lo que
permite que un archivo declare su contrato sin una bandera externa.

---

## 5. Dinámica dentro del tick

### 5.1 Dónde encaja

El orden de etapas de `Step()` no cambia. Dentro de `Act(colony, ant)`, el orden
pasa a ser:

```text
1. AntSenses.Build(...)                        // sensores del mundo (sin memoria)
2. ApplyCorridorHomeSense(...)                 // brújula del nido (canales 9–10)
3. RellenarMemoSensors(ant)                    // canales 19–22 desde el anillo
4. brain.Evaluate(...)                         // cerebro PURO
5. ApplyFootprintRepulsion / TrailRecruitment  // reflejos existentes
6. ApplyMemoryHoming(...)  ← NUEVO            // reclama el giro si Follow
7. ApplyLoadHoming / ApplyReturnWhenTired      // los dos se saltan si está reclamado
8. SanitizeAndClamp
9. Movimiento + deslizamiento de pared (sin cambios)
10. UpdateMemoryAfterMove(ant, decision)       // NUEVO: marcar, avanzar cursor, caducar
11. Combate, trofalaxia, depósitos, recarga, interacción (sin cambios)
```

Dos decisiones de orden, las dos con motivo:

- **La miga se empuja con la pose FINAL del tick** (paso 10, después del
  movimiento). Guardar la pose de entrada dejaría la primera miga «detrás» de la
  hormiga por un tick, y con spacing 8 u eso sesga el primer tramo.
- **`ApplyMemoryHoming` va antes que `ApplyLoadHoming`** porque si el cerebro
  pide seguir la memoria y la miga está a la vista, el camino recordado manda
  sobre la recta al nido (es el único caso en que la memoria aporta algo). Si no
  hay miga, o la línea a la miga cruza una pared, **no reclama** y la vuelta
  sigue siendo la de siempre: el campo de pasillos nunca se queda huérfano.

### 5.2 El reflejo `ApplyMemoryHoming`

```text
si Follow < 0,5            → no hace nada
si Count == 0              → no hace nada
si Slots == 0              → no hace nada
target = miga[Cursor]
si hay laberinto y !SegmentClear(ant, target) → no hace nada, NO reclama
steer += Gain · ladoLocal(target)          // mismo control que SteerHome
claimed = true
```

`SegmentClear` es el mismo DDA de `MazeLayout` que ya usa el sensor de comida
desde F7: una miga al otro lado de una pared **no se sigue en línea recta**. Si
la miga no está a la vista, el reflejo **no reclama** y el tick sigue como hoy:
la política del cerebro manda y, si empuja el muro, `MazeBumpTurn` y el
deslizamiento de pared ya la devuelven al corredor. No se añade un contador de
«miga inalcanzable»: sería estado nuevo (hash y save) para un caso que el
marcado con `SegmentClear` ya hace raro. Se documenta como limitación: una miga
detrás de un muro no se consume, y caduca por tiempo.

### 5.3 Reglas de marcado (paso 10)

```text
si !memoryEnabled o Slots == 0 → return
// caducidad perezosa de la más vieja
si Count > 0 y Tick − Born[0] > LifeTicks → descartar la más vieja
si !WantsRemember(decision) o ant.HasLoad → return
si Count > 0 y dist(última, actual) < Spacing → return       // no llena el anillo de ruido
si Count > 0 y !SegmentClear(última, actual) → return        // el tramo no fue recto: no sirve de atajo
empujar (X, Y, Tick); si Count == Slots, cae la más vieja
```

Notas:

- **Solo marca sin carga.** La que vuelve cargada no reescribe el camino: lo
  desanda. Si marcara, el anillo se llenaría de migas del viaje de vuelta y el
  «desandado» dejaría de ser el de ida.
- **`SegmentClear` al marcar** convierte el anillo en una polilínea
  *transitable*: cada par de migas consecutivas tiene línea recta libre. Sin esa
  condición, un tramo de 20 u que dobla una esquina guardaría una cuerda que
  cruza la pared y la vuelta se atascaría justo donde la memoria debería ayudar.
  Con ella, un tramo que dobla se rechaza y la miga se pone en el siguiente
  intento, ya en el corredor nuevo.
- **Sin RNG.** Ni una tirada: el determinismo del mundo no depende de la memoria.

### 5.4 Coste por tick

| operación | cuándo | coste |
|---|---|---|
| rellenar 4 sensores | siempre con memoria | O(1), 4 flops + clamp |
| caducidad | siempre | O(1) (solo la más vieja) |
| marcar | `Remember` alto, sin carga | 1 DDA de ≤ 32 u ≈ 4 celdas |
| seguir | `Follow` alto | 1 DDA de ≤ 128 u ≈ 16 celdas + 1 control proporcional |

Con 300 hormigas y memoria encendida, el peor caso es ~300 × 20 celdas de DDA
por tick; el DDA ya se ejecuta hoy para el sensor de comida. Se medirá en la
rodaja 3 contra el presupuesto de `docs/fase5-3-escala.md` (0,2 ms/tick con 2
colonias) y se publicará el número.

---

## 6. Determinismo y hash

### 6.1 Qué entra en `HashLine`

Hoy `HashLine` escribe, por hormiga viva: id, pose, energía, edad, carga,
`Alive`, cooldown, fitness e `IsImmigrantTrial`. Se añade **al final del bloque
de cada hormiga**, y **solo si la memoria está encendida**:

```text
si _memoryEnabled:
    AppendByte(Slots)
    AppendByte(Count)
    AppendByte(Cursor)
    para i en 0..Count−1:            // orden ANTIGUO → NUEVO
        AppendFloat(X[i]); AppendFloat(Y[i]); AppendUInt64(Born[i])
```

- **El índice físico no se hashea**: el anillo se escribe en orden lógico. Dos
  estados con las mismas migas y edades son equivalentes aunque el array esté
  rotado, porque la siguiente inserción siempre cae sobre la más vieja.
- **`Born` sí se hashea**: la caducidad depende del tick de depósito; sin él, dos
  mundos con las mismas posiciones divergen en el futuro.
- **El cursor sí**: cambia qué miga se sigue en el tick siguiente.
- Con `--memory 0` **no se añade ni un byte y no cambia la topología**: los 6
  pines actuales siguen byte a byte, sin regenerarlos. Es el mismo patrón que
  usaron `leaves`, `fungus`, `maze` y `recruitment`.
- Con `--memory 1` el hash es nuevo a propósito, pero **solo en ese mundo**: los
  comandos pinneados corren sin la bandera y no se ven afectados.

### 6.2 Lo que no puede pasar

- Nada de `DateTime`, `Random` del sistema ni `Dictionary` con orden no
  determinista dentro del anillo.
- Nada de caducidad evaluada «cuando toque» con un temporizador: la caducidad se
  evalúa **en el tick de la hormiga** y solo sobre la más vieja.
- Nada de memoria en el cerebro: `IBrain.Evaluate` sigue siendo pura y sin
  asignaciones. Si algún día se quisiera **recurrencia** (realimentar el estado
  oculto al tick siguiente), sería otro contrato de cerebro y otro documento:
  aquí la memoria es estado del mundo, no del grafo.

---

## 7. Checkpoint `.antsave` v7

`WorldSimSave.FormatVersion` pasa de 6 a **7**. Los v6 se siguen leyendo.

### 7.1 Cambios de layout

1. **Cabecera**, justo después del bloque de laberinto (antes no existe):
   `bool memoryEnabled` (1 byte).
2. **Genoma** (cuerpo común de élite, inmigrantes y adultas): antes de
   `nLayers u32` se añade
   `u16 nGenes` + `f32 × nGenes` (hoy 4). Un v7 escrito por una versión futura
   con más genes se puede rechazar por `nGenes > 16`.
3. **Por adulta**, tras `isImmigrantTrial u8`:
   `u8 slots · u8 count · u8 cursor` y, por cada miga,
   `f32 x · f32 y · u64 bornTick`.
4. **Nada más**: el resto del layout (capas, cría, RNG, contadores) no se toca.

### 7.2 Compatibilidad

- **v6 → v7**: `memoryEnabled = false`, `Slots = Count = Cursor = 0` para todas
  las adultas. Como el mundo se construye en topología **v1 (19/6)**, los
  genomas del v6 encajan tal cual: **no hay migración** y el checkpoint se
  reproduce como hoy. Los genes no existen en un v6; si más tarde ese pool se
  lleva a un mundo con memoria, se ensancha allí (ver §8).
- **v7 → v6**: no. Un lector v6 no conoce la versión 7 y la rechaza, igual que
  hoy rechaza la 1.
- **El constructor del mundo en la carga** tiene que recibir `memoryEnabled`
  **antes** de crear colonias: hoy `Deserialize` hace
  `new WorldSim(seed, gridCells, colonyCount: 1, mazeLayout: maze)` y no pasa ni
  `recruitment`; con memoria hay que pasar `memory: memoryFlag` o el pool nace
  con el flag apagado y el checkpoint se “carga” con la memoria muerta.
- `ClearColoniesForLoad` no necesita cambios: el anillo vive en `Ant`, no en la
  colonia.

### 7.3 Por qué el checkpoint importa aquí

La memoria es el primer estado del individuo que **no** se puede reconstruir
desde la pose. Si no viaja en el save, cargar en el tick T y re-ejecutar produce
un hash distinto en el primer tick en que una hormiga marque o caduque una miga.
Es exactamente el fallo que el checkpoint v3 evitó con los acumuladores ocultos
de la reina.

---

## 8. Migración de cerebros existentes

Aquí está la ventaja de no sustituir el contrato: **un pool clásico sigue
cargando sin tocarse** en un mundo clásico. La migración ocurre solo en la
frontera, cuando se enciende la memoria y el mundo corre 23/8. Es obligatoria en
esa frontera (un genoma de 19 entradas no se puede evaluar con 23) y el lector
la hace en un único sitio: `Widen`.

Matriz de compatibilidad:

| genoma \ mundo | `--memory 0` (19/6) | `--memory 1` (23/8) |
|---|---|---|
| 19/6 (v1, `.antgenome` v1/v3) | se usa tal cual | se **ensancha** a 23/8 (§8.1) |
| 23/8 (v2, `.antgenome` v3) | se **rechaza** con el mensaje de `RequireCompatibleSizes` | se usa tal cual |
| NEAT 19/6 | se usa tal cual | se **remapea** a 23/8 (§8.2) |
| NEAT 23/8 | se rechaza | se usa tal cual |

### 8.1 MLP: `Widen(from 19/6, to 23/8)`

La disposición canónica de pesos (documentada en `MlpBrain`) es
*destination-major*: por cada destino, `n_src` pesos seguidos de su bias.

```text
para cada capa destino l = 1..L−1:
    oldDst = sizes[l], oldSrc = sizes[l−1]
    newDst = targetSizes[l], newSrc = targetSizes[l−1]
    para d en 0..oldDst−1:
        copiar oldSrc pesos a las primeras oldSrc posiciones
        rellenar con 0 las columnas oldSrc..newSrc−1   // sensores nuevos
        bias = old bias, al final del bloque nuevo
    para d en oldDst..newDst−1:                        // salidas nuevas
        todos los pesos 0
        bias = −12,0
```

- **Entrada**: las 4 columnas nuevas valen 0, así que la capa oculta calcula
  exactamente lo mismo. Sumar `0·x` a un acumulador float **no cambia el valor**
  (0·x = 0 exacto para x finito; acc + 0 = acc). La decisión de los 6 canales
  viejos sale **bit a bit igual**, salvo el signo del cero en un acumulador que
  sea exactamente −0 (irrelevante para las posiciones y el hash).
- **Salida**: las 2 filas nuevas tienen pesos 0 y bias −12 ⇒
  `sigmoid(−12) ≈ 6,1·10⁻⁶ < 0,5`, así que `Remember` y `Follow` **nunca**
  disparan en un genoma migrado. Un bias 0 daría `sigmoid(0) = 0,5`, que
  dispararía el umbral: sería un bug silencioso, no una migración.
- `Fit`/`Fitness` no se tocan y los genes de política se copian tal cual.

Vive en `MlpGenome`, como hermana de `Expand` (que ya sabe crecer en ocultas pero
prohíbe cambiar entrada y salida), y es la única función que convierte un genoma
v1 en v2. Si el destino no es exactamente 23/8, lanza: no hay conversiones
parciales.

### 8.2 NEAT: remapeo de ids

`NeatGenome` deriva los ids fijos del contrato: entradas 0..18, salidas 19..24,
ocultas desde 25. Un genoma NEAT clásico que se siembre en un mundo con memoria
hay que remapearlo, o sus conexiones quedan corruptas:

| viejo | nuevo |
|---|---|
| entradas 0..18 | 0..18 (sin cambio) |
| salidas 19..24 | **23..28** |
| ocultas ≥ 25 | **≥ 31** (desplazamiento +6) |

`NeatGenome.ContractVersion` se deriva de sus nodos (¿hay entradas 19–22 y
salidas 29–30?), igual que `MlpGenome` lo deriva de `sizes`. Los ids fijos dejan
de ser constantes estáticas de `NodeGene` y pasan a depender del contrato:
`FirstOutputId` = 19 o 23, `FirstHiddenId` = 25 o 31. `NeatBrain` recibe esos
dos valores del genoma (los usa en el orden topológico, en el registro de
activaciones y en `_outIdx`) y su `_outIdx` pasa a tener 6 u 8 entradas; con 8
escribe también `Remember` y `Follow`.

Al sembrar un NEAT de contrato 1 en un mundo con memoria: se remapean `from`/`to`
de todas las conexiones y las `innovation` se re-renumeran con
`RenumberCanonically` (que ya existe y ya se aplica al importar). Después se
añaden:

- 4 nodos de entrada (19..22) sin conexiones;
- 2 nodos de salida (29..30) con `bias = −12`, sin conexiones entrantes.

Si el remapeo se olvida, una conexión que iba a la salida 0 («steer») acabaría
en la entrada 19 y el cerebro se vuelve basura sin lanzar ninguna excepción: por
eso el test de migración compara **la decisión de los 6 canales viejos** en 100
sensores aleatorios, no solo el número de nodos.

**Alcance**: los genes de política viven en `MlpGenome`. Un pool NEAT puede
correr con memoria usando los valores por defecto (`DefaultMemorySlots`), pero
no evoluciona los genes. Se documenta como limitación de la rodaja 3, no como
deuda oculta. Extenderlos a NEAT exige un `.antgenome` v4 con genes por nodo; se
deja para otra fase.

### 8.3 Puntos de entrada que hay que tocar

| punto | cambio |
|---|---|
| `AntGenomeFile.Deserialize` | aceptar el contrato del mundo **o el 1 cuando el mundo es v2**; un v2 en un mundo clásico se rechaza. Devuelve el genoma con el contrato que **trae**, sin migrar. |
| `AntGenomeFile.DeserializeNeat` | ídem; el remapeo (§8.2) se hace al **sembrar** en un mundo v2, no al leer. |
| `AntGenomeFile.PeekFirstSizes` | sin cambios de contrato: devuelve los `sizes` del archivo; para v3 salta antes el bloque de genes. |
| `WorldSim.RequireCompatibleSizes` | compara contra `_brainSizes` como hoy: si no encaja, el mensaje actual ya dice que siembres el pool. En el alta del mundo se antepone la conversión (§8.1/§8.2). |
| `WorldSim.ImportGenomes` / `SeedPoolFromGenomes` / `SeedPoolFromNeatGenomes` | si el mundo es v2, ensanchar/remapear antes de `QueueImmigrant`/`ReplaceElite`/`ToBrain`. |
| `GameScenario` / `PolicyBenchmarkScenario` / `ScaleScenario` | leen con la versión del mundo; el lector admite ambas y la conversión ocurre en el mundo. |
| `WorldSimSave.ReadGenomeOrNull` | validar contra la topología del mundo que se está cargando: 19/6 en un v6, 23/8 en un v7 con `memoryEnabled`. |
| `Tools/AntSim.Cli/Program.cs` (`--seed-pool`) | si `--memory 1` y el archivo trae 19/6, ensanchar `sizes` antes de construir el mundo (como hace hoy `PeekFirstSizes` con el ancho de las ocultas). |
| `ScriptedBrain` | no cambia: `AntDecision.Neutral()` deja `Remember = Follow = 0`. Un test lo fija. |

---

## 9. CLI, lanzador y HUD (mínimo)

- **Bandera**: `--memory 0|1` (por defecto **0**). Viaja a `WorldSim` y a
  `WorldScenario`/`GameScenario`. **No** se añade a la cabecera del stream: el
  inspector y el lanzador leen el flag del propio mundo, y así no se toca el
  contrato del canal A ni sus fixtures.
- **Perilla de banco**: `--memory-slots N` **solo para A/B** (0..8), fuerza
  `Slots` en todas las hormigas e ignora el gen. Es la forma de medir «memoria
  con el mismo cerebro» sin esperar a que evolucione. No sustituye a los genes:
  es una herramienta de laboratorio, como `--trail-follow 0`.
- **Inspector**: una línea `memoria n/slots · cursor k` y, opcionalmente, la
  polilínea de migas de la hormiga inspeccionada. La polilínea es solo de
  depuración: no viaja en el canal A ni cambia el contrato del stream.
- **HUD**: no se añade tarjeta nueva en la primera rodaja. Si la métrica sube, se
  añade una línea a la tarjeta de reclutamiento con `memoria` media de la
  colonia, sin bloque nuevo del canal C.

---

## 10. Criterios de aceptación

Se miden **tres semillas**, mismo pool (`pretrain-maze-los3`), grid 96,
laberinto, reclutamiento, `--until-extinct`, y se comparan dos corridas con el
**mismo binario**:

| métrica | base (`--memory 0`) | objetivo (`--memory 1`) |
|---|---|---|
| descargas por ronda | medida en la rodaja 0 | **+10 %** |
| % de cargadas que llegan al nido | medida en la rodaja 0 | **+10 %** |
| tiempo mediano nido→fuente→nido | medida en la rodaja 0 | **−15 %** |
| muertes por hambre | 0 | 0 |
| duración de la ronda | no baja | no baja más de un 5 % |

Controles obligatorios:

1. **`--memory 0` reproduce el mundo de hoy**: un test de hash sobre un mundo
   fijo (semilla, 3 000 ticks, pool canónico) da el mismo valor que el pin
   actual, **sin regenerarlo**. Sin esto, el mundo clásico habría cambiado sin
   decirlo.
2. **`--memory 1 --memory-slots 0`** se comporta como `--memory 0` en
   posiciones y decisiones (los 4 sensores valen 0 y los reflejos no actúan).
   Se compara la traza de posiciones, no el hash: con la bandera encendida el
   hash incluye `Count = 0`.
3. **Un genoma migrado decide igual**: 100 sensores aleatorios, comparar los 6
   canales viejos entre el genoma v1 y su `Widen`.
4. **El anillo no crece**: `Count ≤ Slots ≤ 8` en cada tick de una partida de
   2 000 ticks con `--memory 1`, incluidas muertes, recogidas y descargas.
5. **Ninguna miga caducada se sigue**: con `LifeTicks` corto forzado, tras
   `Life + 1` ticks el anillo queda vacío y la hormiga vuelve por el campo.

---

## 11. Plan por rodajas

| rodaja | qué entra | qué se verifica |
|---|---|---|
| **0. Reflejo sin contrato** | anillo fijo de 6 migas, marcar cada 20 u, seguir al volver, `--memory`, sin canales ni genes | A/B de §0.2. **Puerta: si no sube, se para.** |
| **1. Contrato v2 opt-in** | 23/8 sin tocar v1 (`MemoryCount`, `MemoryDecisionCount`, topología por bandera), `AntSensors.CopyTo` con truncado, `Widen`, NEAT remap | hash clásico intacto **sin regenerar**; decisión idéntica de genomas v1; los 6 pines siguen; suite en verde |
| **2. Estado y reflejos** | `WaypointMemory` en `Ant`, sensores 19–22, `ApplyMemoryHoming`, marcado en `Act`, hash condicional | invariantes del anillo; no cruzar paredes; determinismo a 3 000 ticks; hash solo con la bandera |
| **3. Genes y formatos** | 4 genes en `MlpGenome`, `Crossover`/`Mutate` sin mover el RNG clásico, `.antgenome` v3, `.antsave` v7 | ida y vuelta de genoma con genes; checkpoint T → recargar → mismo hash; clásico sin cambios |
| **4. CLI, lanzador y medida** | `--memory`, `--memory-slots`, inspector, cabecera del stream | ronda de 3 semillas; tabla de §10; coste por tick medido |

Cada rodaja cierra con `dotnet test`, `scripts/check-doc-figures.sh` y, si toca
Unity, `scripts/check-unity-compile.sh`. La rodaja 0 no toca Unity.

### Tests previstos (nombres de trabajo)

- `MemoryShapeTests`: anillo lleno descarta la más vieja; `Count ≤ Slots`;
  caducidad solo de la más vieja; `Cursor` avanza al pisar.
- `MemoryMazeTests`: una miga al otro lado de un muro no se sigue en recta;
  `SegmentClear` rechaza el marcado que dobla una esquina; la recuperación por
  `GuideStep` no consume la miga.
- `MemoryContractTests`: 23/8; `CopyTo(target, 19)` escribe 19 y
  `CopyTo(target, 23)` escribe 23; `AllFinite` cubre los canales nuevos;
  `ScriptedBrain` deja `Remember = Follow = 0`.
- `MemoryMigrationTests`: `Widen` reproduce los 6 canales viejos; bias −12;
  `PeekFirstSizes` sigue devolviendo 19/6 para un v1 y solo el CLI lo ensancha
  con `--memory 1`; NEAT remapea salidas y ocultas sin perder conexiones.
- `MemoryDeterminismTests`: mismo hash con la misma semilla; hash distinto si se
  cambia una miga; `--memory 0` byte a byte.
- `MemorySaveTests`: v7 ida y vuelta; v6 carga con memoria apagada.
- `MemoryABTests` (ronda, no unitario): las tres semillas de §10.

---

## 12. Riesgos y decisiones abiertas

1. **Redundancia con el rastro.** El rastro de comida ya dura 2 100 s y el de
   casa 140 s. Si la memoria no aporta sobre eso, la rodaja 0 lo dirá. Es el
   riesgo principal y por eso hay puerta.
2. **Migas al otro lado de un muro.** Resuelto en dos capas: `SegmentClear` al
   marcar (la polilínea guardada es transitable) y no reclamar el giro cuando la
   miga no está a la vista (nunca se cruza una pared en recta; el apoyo en pared
   de siempre devuelve la hormiga al corredor). Queda el caso de una miga que la
   hormiga ya no puede alcanzar: no se consume y caduca por tiempo.
3. **Colapso del gen de capacidad.** Si la selección encuentra que 1 miga basta,
   `Slots` baja a 1 y la memoria se vuelve un rumbo más. No se corrige a mano: es
   información, y el A/B con `--memory-slots` la hará visible.
4. **Doble contabilidad con el rastro en el fitness.** La memoria no paga
   fitness por sí misma: solo se mide por descargas y llegadas. No se añade
   `RewardMemory` en la primera versión (sería pagar por usar la función, no por
   el resultado).
5. **El checkpoint v6 no puede reconstruir la memoria.** Un v6 cargado empieza
   con anillos vacíos: es correcto y está documentado, como la huella CHC a cero
   en los v2–v4.
6. **Churn de tests por el contrato.** Con el contrato v2 opt-in, los tests que
   fijan 33 nodos (`ActivationCanalFTests`) y los ids 19/25
   (`NeatGenomeTests`/`NeatOperatorTests`) **siguen válidos para v1**, que es el
   defecto. Solo hay que tocarlos si algún caso enciende la memoria; los tests
   de v2 son nuevos y cubren 39 nodos y los ids 23/31.
7. **NEAT sin genes.** Limitación declarada (§8.2). Si el juego usa el pool MLP
   (lo hace), no bloquea la fase; si se quiere NEAT con política evolucionada,
   hace falta un formato v4.

---

## 13. Archivos que tocaría (mapa de impacto)

| archivo | cambio |
|---|---|
| `src/Core/AntSim.Core/Brain/IBrain.cs` | `MemoryVersion = 2` junto a `CurrentVersion = 1` |
| `src/Core/AntSim.Core/Contracts/AntSensorChannel.cs` | canales 19–22, `MemoryCount = 23` (sin tocar `Count = 19`) |
| `src/Core/AntSim.Core/Contracts/AntSensors.cs` | 4 campos, `CopyTo` con truncado, `AllFinite` |
| `src/Core/AntSim.Core/Contracts/AntDecision.cs` | `Remember`, `Follow`, `MemoryDecisionCount = 8` (sin tocar `DecisionCount = 6`) |
| `src/Core/AntSim.Core/Validation/DecisionValidator.cs` | clamp y umbrales nuevos |
| `src/Core/AntSim.Core/World/Ant.cs` | `WaypointMemory Memory` |
| `src/Core/AntSim.Core/World/WorldSim.cs` | `memory`, topología según bandera, sensores, `ApplyMemoryHoming`, marcado, hash, conversión al sembrar |
| `src/Core/AntSim.Core/World/MazeLayout.cs` | nada: se reutiliza `SegmentClear` |
| `src/Core/AntSim.Core/Evolution/MlpGenome.cs` | genes, `ContractVersion` derivado de `sizes`, `Crossover`/`Mutate`/`Expand`/`Clone` |
| `src/Core/AntSim.Core/Evolution/GenomePool.cs` | `memoryEnabled` en el constructor y en `Birth` |
| `src/Core/AntSim.Core/Evolution/AntGenomeFile.cs` | formato v3 (genes), lectura v1/v2/v3, contrato por genoma, NEAT remap al sembrar |
| `src/Core/AntSim.Core/Serialization/WorldSimSave.cs` | v7 (flag, genes, anillos), lectura v6 |
| `src/Core/AntSim.Core/Brain/MlpBrain.cs` | `ContractVersion` según `_inputCount`; pasar `_inputCount` a `CopyTo`; guards de las salidas 6–7 (sigmoid por convención) |
| `src/Core/AntSim.Core/Brain/NeatBrain.cs` | `ContractVersion`, `FirstOutputId`/`FirstHiddenId` y `_outIdx` (6 u 8) según el genoma; pasar `_inputCount` a `CopyTo` |
| `src/Core/AntSim.Core/Evolution/NeatGenome.cs` | ids fijos por contrato (19/25 o 23/31), validación de 19 o 23 entradas y 6 u 8 salidas |
| `src/Core/AntSim.Core/Brain/ScriptedBrain.cs` | nada: `Neutral()` deja 0 |
| `src/Core/AntSim.Core/Scenario/GameScenario.cs` | bandera en la cabecera y en el mundo |
| `src/Tools/AntSim.Cli/Program.cs` | `--memory`, `--memory-slots`, siembra migrada |
| `src/App/AntSim.Unity/.../LaunchSettings.cs` | opción del lanzador (rodaja 4) |
| `src/App/AntSim.Unity/.../AntInspectorBehaviour.cs` | línea de memoria (rodaja 4) |

**Lo que este diseño no decide** (y hay que decidir al implementar):

- si `WaypointMemory` usa campos `X0..X7` con `switch` o `fixed` buffers (depende
  de si Core acepta `unsafe`; hoy no lo usa);
- si el inspector pinta la polilínea o solo el contador (coste de UI frente a
  valor de depuración);
- los valores por defecto de los cuatro genes (la tabla de §4.1 es la
  propuesta; se congelan con un test antes de medir, no después).
