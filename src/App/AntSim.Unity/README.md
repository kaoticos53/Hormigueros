# AntSim.Unity — esqueleto F4.1

Proyecto Unity del presenter (Fase 4). Estado actual: **esqueleto** — los
scripts puros ya existen y están verificados contra el Core; la escena, los
materiales y el HUD rico llegan en F4.2+.

## Arquitectura (de cabo a cabo)

```
CLI (Core, headless)  --mode game  →  stream JSONL (canal A+B+C por tick)
        │                                        │
        │                              GameStreamParser (puro, sin UnityEngine)
        │                                        │
        │                              GameStreamPresenter (puro: interpola)
        │                                        │
        └─ --mode presets --json →  PoolPickerModel (puro: 2 niveles)
                                                 │
                                    MonoBehaviours finos (dibujan/UI)
```

**Regla del proyecto**: nada de lógica en los MonoBehaviours. Todo lo testeable
vive en clases puras (`Assets/Scripts/Streaming`, `.../Presenter`, `.../UI`)
que NO referencian UnityEngine — así se verifican en la suite headless del
Core (`AntSim.Core.Tests`, ver abajo) antes de abrir Unity.

## Scripts

| Archivo | Papel | ¿Puro? |
|---|---|---|
| `Scripts/Streaming/GameStreamParser.cs` | JSONL → TickView (poses, items, colonias, eventos, métricas, relevo) | ✅ |
| `Scripts/Streaming/StreamReader.cs` | lanza el CLI / lee archivo y bombea líneas | ✅ |
| `Scripts/Streaming/WorldPlaneRay.cs` | rayo → plano del mundo (el mapeo del click), compartido por los dos handlers | ✅ |
| `Scripts/Presenter/GameStreamPresenter.cs` | reproducción con buffer (`AdvanceTo`, drenado de presentados) + interpolación con retraso de 1 tick → RenderState | ✅ |
| `Scripts/UI/PoolPickerModel.cs` | JSON de presets → recomendados/especialistas | ✅ |
| `Scripts/UI/AntInspectorModel.cs` | tarjeta de inspección (12 campos canal A, muerte del canal B) + linaje de cerebros por huella | ✅ |
| `Scripts/UI/ColonyCardModel.cs` | tarjeta de colonia: semáforo del canal D, reserva, chips de cría, flujo 1 s, cerebros | ✅ |
| `Scripts/UI/HudToastsModel.cs` | pila de toasts del canal D: dedupe por clave, expiración en s de sim, tope de pila | ✅ |
| `Scripts/UI/CommandHistoryModel.cs` | panel de historial de comandos (§4): filas DropFood/SaveGame, total, «reproducir desde guardado» | ✅ |
| `Scripts/EditorTools/SceneBootstrapper.cs` | comando de menú que construye la escena de juego completa (cámara, suelo, nidos, HUD) | editor |
| `Scripts/Presenter/SimPresenterBehaviour.cs` | DrawMesh por frame, pausa/velocidad, estado para el raycast de selección | Unity |
| `Scripts/UI/PoolPickerBehaviour.cs` | alimenta la UI del picker | Unity |
| `Scripts/UI/AntInspectorBehaviour.cs` | selección (id/click) y pinta la tarjeta de inspección | Unity |
| `Scripts/UI/HudLayoutBehaviour.cs` | reparte cada TickView a tarjetas/toasts/inspector y pinta los Texts uGUI | Unity |

## Verificación headless (sin abrir Unity)

Los scripts puros se compilan también en la suite de tests del Core:
`AntSim.Core.Tests` incluye `UnityStreamContractTests`, que alimenta al parser
con la salida REAL de `GameScenario.Run` y al picker con la REAL de
`PresetScenario.RenderCards(json:true)` — cualquier cambio de contrato del
stream rompe el test antes de romper la UI.

```bash
dotnet test src/Core/AntSim.Core.Tests --filter "FullyQualifiedName~UnityStreamContract"
```

### Entradas sin dispositivo (por qué los handlers exponen métodos)

`Input` lee el dispositivo real y NO es inyectable desde el CLI, así que las
acciones del jugador tenían que probarlas una persona. Los tres handlers
descompensan eso: la acción vive en un método público sin dispositivo
(`PickAtScreen`, `TogglePlaceMode`/`PlaceAtScreen`/`UndoLastDrop`,
`JumpToNewestAnchored`/`JumpToToastAtContainerY`) y `Update()` solo la cablea a
`Input`. El Play pass dispara esos métodos con la cámara y el estado
REALES —el raycast es el de verdad, el plan es el de verdad— y comprueba el
resultado; lo único que queda fuera es la entrega de la tecla por el motor.
La geometría del click es pura (`WorldPlaneRay`) y por eso sí tiene tests
headless, incluidos los dos casos degenerados que no deben seleccionar nada.

## Compilación: contexto nullable y `Assets/csc.rsp`

Los scripts puros se compilan **dos veces**: dentro de Unity (`Assembly-CSharp`)
y en la suite headless (`AntSim.Core.Tests`, arriba). Para que ambas vean el
MISMO código con la MISMA postura frente a nulos, el proyecto Unity trae un
único archivo:

```
Assets/csc.rsp   →   -nullable:enable
```

**Por qué existe.** Los dos `.csproj` headless declaran
`<Nullable>enable</Nullable>` (`AntSim.Core.csproj`, `AntSim.Core.Tests.csproj`),
pero el ensamblado predefinido de Unity **no trae contexto nullable**: cada
anotación `string?` / `Mesh?` de la capa de vista producía **CS8632** («nullable
annotation outside a `#nullable` context») — 204 avisos en la última medición. El
código ya estaba escrito *para* nulos (usa `?`, `??` y guardas), así que faltaba
activar el contexto, no reescribirlo.

**Por qué `-nullable:enable` y no `annotations`.** `enable` activa anotaciones **y
avisos**; `annotations` habría dejado las anotaciones silenciando los avisos. Se
eligió `enable` para que el editor compile igual que el headless: mismos archivos,
misma postura, mismos diagnósticos. Con posturas distintas, un `null` inseguro
pasaría en Unity y rompería la suite (o al revés), que es justo lo que el espejo
headless existe para evitar.

**`csc.rsp` no admite comentarios.** Unity pasa cada token del archivo a `csc` tal
cual: una línea `#` de comentario se convierte en argumento inválido (`CS2007:
Unrecognized option`, `CS2001: Source file … could not be found`) y **rompe la
compilación**. El archivo tiene una sola línea a propósito; la justificación vive
aquí.

**Estado con el contexto activado** (editor 6000.6.0f1):

| Aviso | Antes | Después |
|---|---|---|
| CS8632 (anotación sin contexto) | 204 | **0** |
| `error CS` | 0 | **0** |
| CS8618 (campo no inicializado) | 0 | **0** |
| CS8602 / CS0414 | 6 | 5 (preexistentes; solo 1 lo ve el headless) |

Al activar los avisos salieron **5 CS8618** en `SimPresenterBehaviour` — los
campos de render (`AntMesh`, `AntMaterial`, `CarrierMaterial`, `ItemMesh`,
`ItemMaterial`) que asigna la escena/bootstrapper, de modo que el compilador no ve
su inicialización. Se resolvieron con `null!`: neutro en comportamiento (el
inicializador solo corre en la construcción, los valores serializados lo
sobrescriben y `Draw` ya los guarda con `!= null`); marcarlos `?` habría propagado
CS8602 a cada uso.

Los 5 avisos restantes son **preexistentes**: 4× CS8602 (`ImportDialogModel`,
`DropFoodClickHandler` y dos en `HudLayoutBehaviour`) y 1× CS0414 en
`SimPresenterBehaviour` (`_streaming` se escribe y nunca se lee). No son CS8632;
arreglarlos exige razonar cada desreferencia, así que quedan como deuda menor.
El recuento lo imprime `scripts/check-unity-compile.sh` en cada pasada, así que
la tabla se puede contrastar con la medida en vez de creerla.

Ojo con el recuento: **solo el de `ImportDialogModel` lo ve el headless** (es un
script puro, de los que el csproj compila con `Link="UnityPure/…"`). Los otros 5
viven en MonoBehaviours que la suite headless **no compila**, así que solo salen
en el editor — la misma asimetría que hace que un `error CS` de la capa de vista
pueda pasar el `dotnet build` y reventar al abrir Unity.

**Los hashes no se tocan.** Las anotaciones son metadatos de compilación: no
cambian el IL, así que el mundo, los tres pins de CI y los fixtures siguen
idénticos. El cambio es estrictamente sustractivo — cero avisos nuevos.

## Abrir el proyecto (arranque rápido)

1. Publica el CLI:
   ```bash
   dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim
   ```
   (en Windows el `StreamSource` resuelve `build/antsim.exe` automáticamente).
2. Abre Unity (2022.3+, probado con 6000.0) → Open Project → `src/App/AntSim.Unity`.
3. Menú **AntSim → Crear escena de juego**: construye cámara, suelo, nidos,
   meshes/materials y el Canvas del HUD completo (tarjetas, toasts, historial,
   inspección) con todas las referencias asignadas. Guarda la escena (Ctrl+S).
4. **Play**: el mundo llega por el stream del CLI; HUD en vivo.

Para inspeccionar sin CLI: `ReplayFile` en `SimPresenterBehaviour` apunta a un
stream volcado (`artifacts/stream-fixture-256.jsonl`) y lo reproduce como fue.

## La ruta del proyecto está guardada (`-projectPath`)

Unity **no falla** si le das un directorio que no es un proyecto: le crea el
esqueleto (`Packages/`, `ProjectSettings/`, `Assets/`, `Library/`, `Logs/`,
`UserSettings/`) sin preguntar. Una invocación apuntada a la **raíz del repo**
dejó ahí un proyecto fantasma de 191 MB que hubo que borrar a mano.

Peor en `-batchmode`: el log sale **limpio** (no había scripts que compilar), así
que el veredicto se leía como «compila sin errores» — un falso verde.

Por eso los scripts que entregan la ruta a Unity (`scripts/check-unity-compile.sh`
y `scripts/playpass-live.sh`) la validan antes con **`scripts/lib/unity-project.sh`**,
que rechaza (a) la raíz del repo, (b) directorios que no son proyectos Unity y
(c) proyectos Unity de OTRO juego: el de este repo declara `com.unity.pipeline`,
el paquete con el que se conduce el editor, y un proyecto nuevo solo trae
`com.unity.modules.*`. La librería se puede probar sin editor ni Unity:

```bash
bash scripts/lib/unity-project.sh --selftest   # 6 casos; también corre en CI
```

## Aspecto del mundo y del HUD (F5.1)

Lo que se ve y por qué, en una tabla — el resto de decisiones están comentadas en
`SceneBootstrapper`:

| Pieza | Decisión | Motivo |
|---|---|---|
| Materiales del mundo | **Unlit** (Color/Texture) | El color que se ve es el elegido y no depende del ambiente del editor; además la sonda de píxeles puede afirmar «se ven hormigas» con tolerancias razonables. Un material unlit **sin textura pinta blanco opaco**: de ahí que el quad de feromonas nazca con una textura 1×1 transparente. |
| Suelo | Tierra clara + rejilla cada world/24 | Referencia de escala: un mundo de 768 u no da ninguna pista de tamaño. |
| Mesa | Plano oscuro 2,6× el mundo | La vista es ancha y el tablero cuadrado: sin ella el 38% de la pantalla era vacío negro. |
| Cámara | Ortográfica, `orthoSize = world·0.52` | Encuadra la altura con 4% de margen y deja la mesa llenar los lados. |
| Hormigas | Cápsula tumbada (Rx 90°), largo = 0,018 × mundo, ancho 0,33 × largo | De pie y a escala 0,6 eran postes sub-píxel: «no se ven hormigas». Las portadoras van un 25% mayores (el relevo se lee sin HUD). |
| Ítems | Esfera natural × 0,010 × mundo | El diámetro dice la cantidad restante. |
| Nidos | Montículo + disco del color de la colonia | El mismo acento que usa la tarjeta. |
| Feromonas | Quad transparente a y=0,3, por debajo de los actores | El canal E solo le cambia la RenderTexture. |
| HUD | Paneles con acento, barra de estado, tarjetas 440×176, modal centrado | Todo el texto vive sobre panel (antes el mundo se comía el texto) y ningún `Text` desborda su caja. |

**Trampas ya encontradas** (no repetirlas):

- Un `Text` de uGUI con la fuente por defecto **no tiene emoji**: los glifos 🥚
  🐛 🛑 🟢 salían como cajas. Los niveles son ahora glifos de forma (○ ◐ ● ■ ·).
- `Panel()` con `anchorMin.y == anchorMax.y` pierde el `sizeDelta`: hay que dar
  las dos esquinas del rect o la barra de estado queda con altura 0 (y su texto
  con altura NEGATIVA — lo destapó la inspección de escena).
- `material.color` en un shader sin `_Color` (Unlit/Texture, Unlit/Transparent)
  **escribe un error** en la consola de Unity; se consulta `HasProperty("_Color")`.
- El Game view en modo **Play Focused** pausa el juego cuando pierde el foco: con
  el pass corriendo desde la terminal, el mundo se congela (tick clavado, sin
  error). `playpass-live.sh` lo detecta y lo reanuda.

Para volver a generar la escena con este aspecto: menú **AntSim → Crear escena de
juego** (o `unity command menu --path "AntSim/Crear escena de juego"`) y Play. La
escena se guarda y se registra en build settings sola.

### Ajustes finos

- `SimPresenterBehaviour`: semilla/grid/colonias/ticks/`SeedPoolPath` (p. ej.
  `artifacts/pretrain-warm-v2.antgenome` para ver el relevo pre-entrenado) y
  velocidad 0/1/2/4/16×.
- **Siembra desde el picker (F4.3)**: `PoolPickerBehaviour.SeedPoolFor(id)`
  devuelve la ruta del `.antgenome` que el propio preset declara en su repro
  canónico — asígnala a `SeedPoolPath` y la partida arranca con ese pool.
- **Selección por click (F4.2)**: `AntPickClickHandler` (creado por el
  bootstrapper) lanza el ray al plano del mundo y llama `PickNearest` — la
  tarjeta de inspección sigue a la hormiga pinchada.
- `AntInspectorBehaviour`: `SelectAntId` sigue una hormiga por id;
  `FollowTrackedBrain()` activa el modo linaje: `cerebro #F · N cuerpos ·
  M vivos` con el linaje completo.
- **Verificar desde el HUD (§4)**: `CommandHistoryModel.BuildVerifyCommand(
  save, antlog)` produce el comando `--mode verify` del CLI (el oráculo es el
  CLI, la UI solo muestra ✓/✗ según su exit code).
- **Intervenir con DropFood (F4.4)**: tecla D activa el modo marcar; cada
  click registra un drop futuro en `DropFoodPlanModel` (cuota 5/partida,
  causalidad validada) y «reiniciar con plan» relanza la misma partida con
  `--drop tick:x:y` vía el CLI. El resumen vive junto al historial.
- **Importar con cuarentena (F4.3)**: `ImportDialogBehaviour` acepta una ruta
  `.antgenome`, consulta el oráculo (`--mode genome-info`) y muestra la
  tarjeta de cuarentena (sha, fitness, genomas, reglas); Confirmar siembra la
  partida vía `SeedPoolPath`, Cancelar no toca nada. `ImportDialogModel`
  (puro) parsea el JSON canónico — testado headless contra el pool real.
- **Feromonas visibles (F4.5)**: lanza el CLI con `--phero-every 30` y el quad
  `PheromoneTiles` (lo crea el bootstrapper) pinta el rastro de la colonia 0
  vía RenderTexture. `PheromoneTileModel` (puro) decodifica el paquete RLE —
  la emisión es telemetría pura: el hash del mundo no cambia (test).
- **Mini-grafo MLP (F5.0, canal F)**: lanza el CLI con `--activ-every 30
  --inspect <antId>` y el stream trae en cada tick las 33 activaciones del
  cerebro de la hormiga inspeccionada (base64 s8). `ActivationViewModel`
  (puro) decodifica y renderiza el grafo por capas con los nombres canónicos
  del contrato — el jugador ve al cerebro decidir; hash del mundo invariante.
  **Desde el inspector**: `PheroEvery` (canal E; el bootstrapper lo deja en 30
  para que el quad de feromonas salga vivo) y `ActivEvery` + `InspectId`
  (canal F) son campos públicos de `SimPresenterBehaviour` — se activan sin
  tocar código; ambos son telemetría pura (hash invariante).
- **Salto de cámara por toast (§1)**: tecla J (o Alt+click en la pila) mueve
  la cámara al ancla (x, y) que el canal D trae en cada alerta. **F5.1**: con
  el contenedor per-elemento, cada toast es un rect clicable — el hit-test
  exacto vive en `HudElementLayoutModel.ToastAt` (modelo puro, testeado), no
  en coordenadas de pantalla.
- **Per-elemento uGUI (F5.1)**: el bootstrapper crea `ToastContainer` (un
  elemento por toast, pool por key), barras de stock (`Image.fillAmount` por
  colonia, color rojo bajo el umbral 0.20 del contrato) y botones nativos
  (Confirmar/Cancelar del import, Reiniciar con plan) — conectados por
  `BindNativeButtons` a los métodos ya verificados; la semántica sigue en los
  modelos puros (specs de habilitación testeables headless).
- `HudLayoutBehaviour`: tarjetas/toasts/historial se reparten del stream; las
  tarjetas pintan el semáforo del canal D (el Core lo calcula con
  `RelayVerdict`: la UI no evalúa umbrales).
- `PoolPickerBehaviour` alimenta el selector; los especialistas muestran su
  advertencia ⚠ (campo `card` del JSON canónico).

## Fixtures de stream (artifacts/, no trackeados — se regeneran con el repro)

| Archivo | Mundo | Uso |
|---|---|---|
| `stream-fixture.jsonl` | seed 42 · grid 96 · 2 colonias · 7200 ticks · frameEvery 1 | contrato base; su hash final está FIJADO en CI (`scripts/check-stream-fixture.sh`) |
| `stream-fixture-256.jsonl` | seed 42 · **grid 256** · 2 colonias · **48000 ticks** · frameEvery 30 · **warm-v2 sembrado** | mundo grande con relevo REAL: descarga en tick 5154, semáforo verde (RelayVerdict: carryLeg 64, dropAvg 182.3) en colonia 0 y competidora gris |

Repro del fixture 256 (mismo hash `f28f4132…` en cualquier máquina):

```bash
dotnet run --project src/Tools/AntSim.Cli -c Debug -- --mode game \
  --grid 256 --colonies 2 --ticks 48000 --seed 42 \
  --seed-pool artifacts/pretrain-warm-v2.antgenome --frame-every 30 \
  > artifacts/stream-fixture-256.jsonl
```

Nota: con `frameEvery 30` el canal A sale a 1 Hz — para HUD, picker,
semáforo de relevo e inspector es el formato ideal (~13 MB); para probar
interpolación suave de movimiento usa `stream-fixture.jsonl` (30 Hz) o
regenera con `--frame-every 1` (376 MB: los ~170 ítems del 256² dominan
cada línea). El hash final no depende de frameEvery: misma partida.

## Próximos hitos

- **F4.1**: regresión visual con semillas fijas (escena real ya montada por el bootstrapper).
- **F4.2**: HUD lógico completo (tarjetas, toasts, semáforo, inspector, historial) — falta pulido uGUI por elemento.
- **F4.5**: UI del picker uGUI con los dos niveles; drag&drop de .antgenome.
