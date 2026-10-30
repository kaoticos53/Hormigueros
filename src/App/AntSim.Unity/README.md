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
`ItemMaterial` — y desde F5.1 §2 el `LeafMesh`) que asigna la
escena/bootstrapper, de modo que el compilador no ve
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
cambian el IL, así que el mundo, los seis pins de CI y los fixtures siguen
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
   El mismo paso se puede correr **en batch, sin abrir el editor**, para
   comprobar que la escena se construye sin excepciones (es lo que valida los
   cambios del montaje cuando no hay sesión abierta — y lo que usó la revisión
   de la gráfica de reserva):
   ```bash
   Unity.exe -batchmode -nographics -quit -projectPath <proyecto> \
     -executeMethod AntSim.Unity.Scripts.EditorTools.SceneBootstrapper.CreateGameScene \
     -logFile <log>
   ```
   El `.unity` resultante se puede inspeccionar como texto (objetos, `ColonyId`,
   `m_SizeDelta`), así que una regresión de layout también se detecta sin ojos.
   Desde aquí mismo, el montador CIERRA lo que escribe: canonicaliza los assets
   generados y recarga la escena (ver «El byte lo estabiliza el GENERADOR» más
   abajo), así que en batch sale el mismo byte canónico que con el editor abierto
   — y `ANTSIM_CANONICALIZE=0` apaga ese cierre para medir el antes/después.
4. **Play**: el mundo llega por el stream del CLI; HUD en vivo.

Para inspeccionar sin CLI: `ReplayFile` en `SimPresenterBehaviour` apunta a un
stream volcado (`artifacts/stream-fixture-256.jsonl`) y lo reproduce como fue.

### Qué entrada y qué config produjo cada escena (registro)

Las dos escenas que el repo versiona **no** salen de los valores por defecto de su
montador, y eso lo sabía sólo quien las ejecutó: el `.unity` guarda los campos, no
su procedencia, y el montador dice otra cosa. El registro vive en
`GeneratedSceneCatalog` (modelo puro, compilado también en la suite headless) y lo
comprueba `GeneratedSceneContractTests` campo a campo contra el YAML serializado,
así que un pase que deje otra configuración falla nombrando el campo, la escena y
los dos valores:

| Escena | Entrada que la escribió | grid | colonias | ticks | frameEvery | velocidad | semilla(s) | pool(s) |
|---|---|---|---|---|---|---|---|---|
| `Assets/Scenes/Game.unity` | **`SceneBootstrapper.CreateGameScene`** con `LaunchSettings.Defaults()` (F6). El Play pass la reescribe en su bloque 4 con `TICKS`/`B4_SPEED`, que siguen a esos defaults | 96 | 2 | 216000 | 1 | 120 | 42 | `pretrain-maze-los3` |
| `Assets/Scenes/MultiSim.unity` | **Medidor de rendimiento**: `-executeMethod …MultiSimBootstrapper.CreateMultiSimPerfScene` | 256 | 2 por vista | 12000 | 5 | 10 | 42–45 (una por vista) | warm-v2 · warm-4 · warm3 · warm2 (autodetectados de `artifacts/`) |

Lo que **no** hay que confundir, porque es justo lo que se perdía:

- El menú **«Crear escena de juego»** (`SceneBootstrapper.CreateGameScene`) monta
  con `LaunchSettings.Defaults()` —F6: ticks **216000**, velocidad **120×** y
  laberinto desde la semilla—, y ésa es EXACTAMENTE la configuración del
  `Game.unity` del repo. El Play pass la reproduce en su bloque 4: sus `TICKS`,
  `B4_SPEED`, `GRID`, `COLONIES`, `SEED` y `SEED_POOL` tienen que coincidir con el
  catálogo, o el pase guardaría otra partida. `frameEvery` y la especie no los
  toca el pase: la escena conserva los del montador, y el registro los ancla a
  `LaunchSettings` en vez de a la memoria.
- El menú **«Crear escena multi-visor»** (`CreateMultiSimScene`) monta grid 96 /
  36000 ticks / frameEvery 2 —la escena de F5.1bis—, y las variantes
  `CreateMultiSimSmokeScene*`, `PerfSceneProbe.RunPerf` y
  `PlayerBuild.BuildMultiViewPlayer` escriben **la misma ruta**
  `Assets/Scenes/MultiSim.unity`. Regenerar «el multi-visor» con cualquiera de
  ellas pisa la escena del medidor, y el contrato lo dice en la siguiente corrida.
- **El byte lo estabiliza el GENERADOR al escribir, no un pase posterior**: Unity
  renumera las anclas `&N` y los `fileID` al recrear los assets
  (`CreateOrReplaceAsset` borra y vuelve a crear) y URP añade a cada `.mat` un
  bloque `AssetVersion` con un id de 64 bits que cambia de sitio, así que la
  escena salía con bytes distintos aunque fuese la misma. Lo cierra el propio
  montador: al terminar de escribir, `GeneratedAssets` corre
  `scripts/unity-canonicalize-assets.py` —anclas derivadas del **contenido**, sin
  tocar el objeto principal (`clase × 100000`, que la escena cita entre
  ficheros) ni `SceneRoots`— y **recarga la escena** desde el  fichero canónico, de forma que un guardado posterior escriba esas anclas y no
  las de sesión. El Play pass ya no repara lo del generador: lo comprueba
  (`--check`) justo después de generar y se planta si algún asset derivaría. Lo
  que sí cierra es **su propia** escritura —el bloque 4 guarda la escena con la
  configuración que se commitea, y un `save_scene` deja anclas de sesión aunque
  el contenido no cambie: medido el 2026-10-10, 425 documentos de sólo forma— y
  la cierra **desde dentro del editor** (`scripts/unity/ProbeCanonicalizeAssets.cs`:
  canonicaliza y recarga la escena), porque escribir por detrás de una escena
  abierta le levanta a Unity el modal «The open scene(s) have been modified
  externally» y el editor deja de responder a todos los comandos. Con
  eso, **dos pasadas seguidas dan el mismo `git diff`**. Lo que este registro fija
  es, además, la equivalencia estructural —de eso decide
  `scripts/check-unity-assets.sh`— y los valores de juego.

### La fuente y los sprites son del juego (no del motor)

El HUD no pide ninguna fuente prestada al motor. El bootstrapper **escribe** el
`.ttf` del juego y lo importa (`GameAssetBuilder.EnsureFont` →
`Assets/Fonts/AntSim.ttf`), y las siluetas de los actores son mallas propias
(`GameAssetBuilder.EnsureActorMesh` → `AntMesh`/`ItemMesh`/`LeafMesh` en
`Assets/Materials/`, una por vista en el multi-visor).

- **La tipografía** sale de `GameFontModel`: 1000 upem, avance de 0,5 em (el
  ancho de las tarjetas no cambia) y una tabla de ~142 glifos escrita en una DSL
  corta y total —`p:` polilínea a trazo, `e:` anillo, `d:` disco, `o:` elipse,
  `b:` caja, `t:` triángulo, `h:` medio disco, con `-`/`+` para adelgazar y
  engrosar— y el castellano completo por acentos **compuestos** (una base más un
  acento de la misma DSL, en vez de un dibujo por letra acentuada). Además de los
  36 signos que el HUD usa de verdad, trae tres **iconos de uso privado**
  (U+E000 hormiga, U+E001 carga, U+E002 huevo) con los que las tarjetas
  sustituyen texto. Las tarjetas los pintan al **doble del cuerpo**
  (`GameFontModel.CardIconSize` = 2 × `CardBodySize`): van en un run de rich text
  `<size=28>` dentro del cuerpo de 14 px, así que el glifo crece sin mover el
  avance del texto del que sale el ancho de la tarjeta. A 14 px medían 5–14 px y
  se leían como manchas; al doble, la comparación contra la referencia del `.ttf`
  —cada icono del recorte vivo frente a su glifo rasterizado a 28 px— los
  clasifica a los tres como su propio glifo: silueta IoU 0,79–0,87 y 0,42–0,69 de
  correlación, con los otros dos glifos ≤0,19 en el mismo sitio.

### El avance del icono: 720 declarado, 643 es la tinta

Si al medir aparece un «643» donde la fuente declara 720, hay dos cosas que
decir, las dos medidas:

- **643 es la caja de TINTA del icono**, no un avance: 0,643 em, o sea 9 px a
  14 px y 18 px a 28 px. El avance declarado es 720 unidades (0,72 em) y la
  tinta cabe dentro.
- **El avance RESERVADO no es el declarado porque Unity carga el glifo con el
  hinting de FreeType**, que ajusta el avance al píxel entero. Cargando la
  fuente por el motor: sin hinting el icono da 719,76–719,87 unidades a 14, 15,
  28, 29 y 31 px y una letra 500,00 clavadas; con hinting —lo que pinta uGUI—
  reserva 10, 11, 20, 21 y 22 px (714, 733, 714, 724 y 710 unidades) y una letra
  pasa a 8 px a 15 (500 → 533). En el HUD en vivo (canvas al 98,3 %, cara de
  27,5 px) el generador reserva 19 px: el 95,9 % del avance declarado.

### Lo generado tiene contrato con sus modelos

Los modelos son la fuente de verdad, pero lo que el juego dibuja son los
ARTEFACTOS: el `.ttf` y las mallas `.asset` que el bootstrapper deja en
`Assets/`. Editarlos por separado no lo notaba nadie —la suite miraba los
modelos y el Play pass miraba lo pintado, cada uno con su lado de la pareja—, así
que `GeneratedAssetContractTests` (headless, sin editor) compara los dos: la
fuente BYTE A BYTE contra `TtfWriter.BuildFromModel` (el escritor sella la fecha,
por eso es determinista) y cada malla VÉRTICE A VÉRTICE contra
`ActorMeshModel.Build(GameSpriteModel.Build(…))`, leyendo las posiciones del blob
`_typelessdata` de la malla en YAML. El mensaje nombra el fichero y el vértice, y
la reparación es siempre la misma: abrir el proyecto y dejar que el bootstrapper
regenere.

El ajuste `shouldRoundAdvanceValue` del importador no cambia nada de eso
(probado en sus dos estados, avances idénticos), así que la cuenta del ancho
vive en el modelo que sí decide: `HudLayoutTests.HintedAdvance` reserva como
reserva uGUI —avance al tamaño del run redondeado al píxel— y un test ancla los
nueve valores medidos. La sonda publica `units`, `decl` y `ofDecl` por icono,
normalizando por el tamaño del RUN (hacerlo por el cuerpo del `Text` duplicaba
las unidades justo en los iconos y hacía parecer que la fuente mentía).
  `HudLayoutTests` fija las dos mitades: que los tres iconos viajan en su run
  grande (y que ningún glifo crudo se cuela) y que las cinco líneas siguen
  cabiendo en la caja útil de 406×135 px, contadas con los avances reales de la
  fuente.
- **El escritor** es `TtfWriter`: sfnt puro —10 tablas, `cmap` format 4, `glyf`
  con puntos on-curve y sin instrucciones—, sin dependencias, con fecha FIJA en
  `head` y el `checksumAdjustment` del sfnt. Es determinista: la misma entrada da
  los mismos bytes, así que el `.ttf` se commitea y no se reescribe en cada
  pasada. `.gitattributes` lo marca binario (`*.ttf -text`) justo por eso: un
  filtro de texto le cambiaría bytes al payload y el checksum dejaría de cuadrar.
- **Los sprites** salen de `GameSpriteModel` —contornos CONVEXOS, todos en el
  mismo sentido, porque el relleno es NON-ZERO y los solapes suman— y
  `ActorMeshModel` los extruye a una malla. Hormiga, carga y hoja se construyen
  en la **misma caja** que la cápsula y la esfera que sustituyen (2 u × 1 u), así
  que la Caja del contrato no cambia (la escala del presenter sí tuvo que decir
  qué eje es el largo: ver «de canto» abajo), y la hoja estrena pasada propia
  (los mordiscos ya no comparten malla con la carga).
- **Regenerar** es volver a correr el bootstrapper: `EnsureFont` sólo reescribe
  el `.ttf` si los bytes difieren, y `EnsureActorMesh` usa
  `EditorUtility.CopySerialized` para no cambiar el GUID del asset — cambiarlo
  rompería las referencias de las escenas ya guardadas.

Para mirar el `.ttf` sin abrir Unity, `python scripts/unity/validate-font.py
<ruta>/AntSim.ttf` valida tablas, nombres, avances y `cmap` con fontTools y
rasteriza una muestra a 14/17/24 px con Pillow. En headless, los 787 tests de la
suite incluyen `GameFontTests` —que cubre también el alfabeto contra los
literales que el HUD usa de verdad, y **los avances LEÍDOS de la tabla `hmtx`**
frente a los del modelo, glifo a glifo por la `cmap`— y
`GeneratedMaterialContractTests`, que comprueba contra `GameMaterialModel` los
**76 `.mat` y las 10 texturas** que el bootstrapper deja en `Assets/Materials`
(color, tipo, textura a la que apunta y píxeles de esa textura),
`ActorMeshTests`, que triangula y mide la malla, y `GeneratedSceneContractTests`,
que contrasta cada escena versionada con `GeneratedSceneCatalog` —la entrada y la
configuración que la produjeron— y con las constantes de sus productores.

### Multi-visor: varias simulaciones a la vez (F5.1bis)

<!-- cifras-historicas: sub-fase cerrada — las cifras de tests son las de su entrega. -->

La fase B del flujo del jugador: ver N partidas EN VIVO en una pantalla, cada
una sembrada con su pool de la fase A (evolución headless). Menú
**AntSim → Crear escena multi-visor**: construye y guarda
`Assets/Scenes/MultiSim.unity` con 4 vistas en cuadrícula 2×2.

- Cada vista es una cámara con `Camera.rect` propio y SU capa (`Sim0..3`,
  slots 8–11 del TagManager): suelo, mesa, nidos, quad de feromonas y los
  DrawMesh del presenter de esa vista viven en la capa, y el `cullingMask`
  de la cámara solo ve su capa + UI. Sin capas, los cuatro mundos se
  dibujarían superpuestos (misma posición del mundo).
- Cada vista tiene su `SimPresenterBehaviour` con `Seed`/`SeedPoolPath`
  independientes: N procesos del CLI, un stream por vista. Los pools se
  autodetectan de `artifacts/` (warm-v2 → warm-4 → warm3 → warm2); si no
  están, la vista arranca sin sembrar y se le asigna pool en el inspector.
- `FrameEvery = 2` por vista: 15 Hz de canal A por stream, el coste de 4
  streams a la vez. Velocidad por vista con **+/=** (cicla 1→2→4×).
- La geometría y las capas son decisión de `MultiViewportModel` (puro,
  10 tests headless — incluido el que fija el ordinal de las capas contra
  el TagManager). El bootstrapper solo aplica lo que el modelo dicta.
- El bootstrapper corre en batch como el de la escena simple:
  `-executeMethod AntSim.Unity.Scripts.EditorTools.MultiSimBootstrapper.CreateMultiSimScene`.

Trampa ya encontrada: un GameObject solo admite UN `Graphic` — el rótulo de
cada vista lleva el fondo (`Image`) en el padre y el texto (`Text`) en un hijo.
Intentar los dos en el mismo GO falla en batch con «a 'Image' is already added
to the game object».

Cada vista lleva además su **tarjeta compacta** (`ViewCardBehaviour`, bajo el
rótulo): una línea por colonia con semáforo de relevo, adultas, reserva y
flujo rec/desc — el texto sale de `ColonyCardModel.RenderCompact()` (puro y
testeado headless), y la reserva añade una franja vertical por colonia con la
misma regla de «reserva baja» del HUD grande. Cada tarjeta consume SOLO el
stream de su vista: cuatro partidas, cuatro semáforos independientes.

<!-- /cifras-historicas -->

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

Para las **sesiones a mano** está `scripts/unity-cli.sh`: pone la misma guarda
delante del CLI y rellena el `--project-path`, así que no hay forma de apuntar
al sitio equivocado ni de olvidarlo. Es el `UCMD()` de los pases, a mano:

```bash
bash scripts/unity-cli.sh editor_status              # JSON del estado del editor
bash scripts/unity-cli.sh run_script --file sonda.cs # ejecuta una sonda
bash scripts/unity-cli.sh --text console             # salida humana (sin --format)
bash scripts/unity-cli.sh --verbose console          # y enseña lo que ejecuta
```

El binario lo resuelve el mismo código que los pases (`scripts/lib/unity-cli.sh`:
`$UNITY_CLI` → `unity` en el PATH → instalaciones del Hub), de modo que no hay dos
ideas de dónde está el editor.

## Unity ensució los assets: ¿churn o cambio de verdad?

Abrir el proyecto, construir un player o simplemente guardar una escena
**reescribe** la escena, los materiales y los ajustes de URP sin cambiar lo que
significan: reordena los documentos del YAML, renumera las anclas `&N` y mueve el
bloque `AssetVersion` de sitio. El diff resultante son miles de líneas que
esconden, si acaso, un cambio pequeño — y commitearlas contamina la historia del
repo para siempre. Medido (2026-09-20) en una pasada del generador del
multi-visor: de 64 assets reescritos, 60 no cambiaban nada.

Lo que **no** ensucia es abrir y cerrar sin tocar nada. Medido (2026-10-09, Unity
6000.6.0f1, árbol canónico y `Library/` caliente): dos ciclos de abrir el
proyecto —cargando `Game.unity`, que el log confirma— y cerrarlo con el mismo
camino que la X de la ventana **no dejaron ni un byte**: `Assets/Scenes` y
`Assets/Materials` salen idénticos a HEAD, `Packages/manifest.json` intacto, el
verificador no tiene nada que descartar (`✓ no hay churn que descartar`) y los
assets siguen en forma canónica. El ruido lo mete GENERAR —y el guardado que eso
conlleva—, no el simple abrir y cerrar.

A ojo eso no se decide. El verificador lo hace por **equivalencia estructural**
(documentos por clase, cada referencia local `{fileID: N}` sin `guid` resuelta a
la huella de su destino e iterada hasta el punto fijo, así que el mismo cableado
da la misma huella aunque las anclas se renumeren):

```bash
bash scripts/check-unity-assets.sh            # los assets que el árbol tiene tocados
bash scripts/check-unity-assets.sh --list     # qué comprueba y qué no
bash scripts/check-unity-assets.sh --churn    # ¿el último commit metió ruido?
```

Si ese ruido ya está en el árbol —una pasada que no vas a commitear—,
`--discard` lo quita él mismo, y por los dos caminos: devuelve a HEAD las rutas
cuyo único cambio es de forma (anclas y orden) y a las que **cambian de verdad**
les quita la forma **sin tocar el contenido** —las reescribe en canónico, que es
lo que el generador hace al cerrar cada escritura—, así que el diff se queda en
el cambio. Y funciona por lo mismo que todo lo demás: el ancla canónica es el
hash del contenido, así que un documento que no cambió vuelve a ser el mismo
byte que en HEAD aunque la pasada le hubiera puesto otra. Medido (2026-10-09,
`Game.unity`, 425 documentos): una pasada que re-ancla todo y cambia UNA línea
escalar pasa de **10 669 líneas de diff a 2**. Sólo escribe si la forma canónica
pasa el `compare` —si no significa lo mismo, se planta y lo dice—, y con
`--dry-run` mide igual sin tocar nada:

```bash
bash scripts/check-unity-assets.sh --discard --dry-run   # sólo avisa (y mide)
bash scripts/check-unity-assets.sh --discard             # quita la forma
```

El verificador dice si dos versiones **significan** lo mismo; quien hace que no
haya dos versiones distintas sin motivo es `scripts/unity-canonicalize-assets.py`,
que el GENERADOR corre al cerrar cada escritura
(`Assets/Scripts/EditorTools/GeneratedAssets.cs`: el bootstrapper lo invoca tras
`SaveScene` y recarga la escena desde el fichero canónico). El Play pass no lo
reejecuta para arreglar al generador: le toma el veredicto con `--check` justo
después de generar y se planta si algún asset derivaría — eso es un fallo suyo.
Lo que sí cierra es la escritura que hace el propio pase (el `save_scene` del
bloque 4), desde dentro del editor y con el mismo canonicalizador
(`scripts/unity/ProbeCanonicalizeAssets.cs`); y al terminar descarta el churn que
haya quedado, si esos assets no se van a commitear. La herramienta se puede usar
sola:

```bash
python scripts/unity-canonicalize-assets.py --check    # ¿algún asset derivaría del canónico?
python scripts/unity-canonicalize-assets.py            # reescribirlos en sitio
python scripts/unity-canonicalize-assets.py --selftest # invariantes, sin repo
python scripts/unity-canonicalize-assets.py --list     # qué toca y qué no
```

El paso del generador tiene dos perillas: `ANTSIM_CANONICALIZE=0` lo apaga (medir
el antes/después, o trabajar a mano) y `ANTSIM_PYTHON` apunta al intérprete cuando
el PATH del editor no lo tiene. Si no encuentra ninguno **avisa** y sigue —«no
medido» no es «roto»—, pero entonces la garantía no se cumplió y la comprobación
del Play pass lo dirá.

La respuesta termina con la orden exacta para descartar el ruido sin perder nada
(`git checkout -- …`) y sale con código 1 si alguno **sí** cambia de verdad. En CI
el paso `Assets de Unity (el churn de re-serialización no entra)` recorre los
commits del push y falla si alguno metió un asset que no cambia nada; su selftest
es también un paso del job `test`, junto a los de las otras herramientas.

## Rondas encadenadas (F8)

La partida no termina en un final seco: cuando no queda **ninguna hormiga ni cría**
el CLI corta en ese tick (`--until-extinct`), exporta la élite de la colonia 0 y
el presentador abre otra ronda **con un laberinto nuevo** sembrada con esos
cerebros.

Cómo está partido, y por qué:

- **Quién decide** — `RoundModel` (puro, sin UnityEngine, en la suite):
  `AllColoniesDead` es la condición (adultas, huevos, larvas y pupas a cero en
  TODAS las colonias; una lista vacía no es extinción), `NextSeed` deriva la
  semilla de la ronda siguiente (splitmix64 sobre semilla + ronda: determinista y
  distinta en cada ronda) y `NextSeedPool` elige la élite exportada si existe o
  mantiene el pool actual si no.
- **Quién corta la partida** — el CLI, no el editor: `GameScenario` comprueba
  `WorldSim.AllColoniesExtinct` tras escribir cada tick, sale del bucle y la línea
  `end` viaja con `"extinct":true`. Así la élite se exporta EN EL TICK de la
  extinción, no 180 000 ticks después.
- **Quién cambia el mundo** — una recarga de escena, el mismo mecanismo del
  «reiniciar con plan»: el HUD, las gráficas, la cámara y el mundo arrancan
  limpios, y la semilla/ronda/pool de la siguiente viajan en estáticos
  (`ArmSeedForNextScene`, `ArmSeedPoolForNextScene`, `ArmRoundForNextScene`) que
  `ClearRoundArming` borra al empezar un Play nuevo — si no, un Play nuevo
  heredaría la ronda 7 de la sesión anterior.
- **Lo que se ve**: `[AntSim] Ronda 1 · semilla 42 …`, `Ronda 1 terminada en el
  tick 35537 (extinción) · nueva ronda en 2,5 s` y `Ronda 2: semilla … (laberinto
  nuevo) · cerebros de la élite evolucionada (evolved-42.antgenome)`. La pausa de
  2,5 s deja ver la colonia vacía antes de recargar. Un replay no encadena: es una
  sola corrida.

## Aspecto del mundo y del HUD (F5.1)

Lo que se ve y por qué, en una tabla — el resto de decisiones están comentadas en
`SceneBootstrapper`:

| Pieza | Decisión | Motivo |
|---|---|---|
| Materiales del mundo | **Unlit** (Color/Texture) | El color que se ve es el elegido y no depende del ambiente del editor; además la sonda de píxeles puede afirmar «se ven hormigas» con tolerancias razonables. Un material unlit **sin textura pinta blanco opaco**: de ahí que el quad de feromonas nazca con una textura 1×1 transparente. |
| Suelo | Tierra clara + rejilla cada world/24 | Referencia de escala: un mundo de 768 u no da ninguna pista de tamaño. |
| Mesa | Plano oscuro 2,6× el mundo | La vista es ancha y el tablero cuadrado: sin ella el 38% de la pantalla era vacío negro. |
| Cámara | Ortográfica, `orthoSize = world·0.52` | Encuadra la altura con 4% de margen y deja la mesa llenar los lados. |
| Hormigas | Silueta propia (`AntMesh`, de `GameSpriteModel`) tumbada con `Rx 90°` y **girada al rumbo del stream** (`90° − rumbo`), largo = 0,018 × mundo (= 18,69 px en un viewport de 1080), ancho 0,33 × largo | De pie y a escala 0,6 eran postes sub-píxel: «no se ven hormigas». La caja del actor tiene el largo en Y y la hormiga mira hacia +Y, así que el giro del presenter lleva ese eje al rumbo. Las portadoras van un 25% mayores (el relevo se lee sin HUD). |
| Ítems | Esfera natural × 0,010 × mundo | El diámetro dice la cantidad restante. |
| Nidos | Montículo + disco del color de la colonia | El mismo acento que usa la tarjeta. |
| Feromonas | Quad transparente a y=0,3, por debajo de los actores | El canal E solo le cambia la RenderTexture. |
| HUD | Paneles con acento, barra de estado, tarjetas 440×204, modal centrado | Todo el texto vive sobre panel (antes el mundo se comía el texto) y ningún `Text` desborda su caja. La tarjeta creció 28 px para la gráfica de reserva. |
| Gráfica de reserva y descargas (F5.1) | `RawImage` con textura generada por `ColonySparklineModel` (puro) | Serie de 1 muestra/s (30 ticks, el ritmo del canal C) con ventana de 90 s: una gráfica de 10 minutos en 400 px no dice nada. Se dibuja con **área** (relleno + borde), teñida en rojo por debajo del 20% (el mismo umbral que la barra), y las **descargas por ventana** como barras ámbar encima, contadas del canal B (`Unload`) y apuntadas en la misma ventana que la muestra; la altura es relativa a la mayor cuenta de la ventana vista. Que la tarjeta las PINTE se mide en píxeles en el Play pass (`ProbeSparklinePixels` + `scripts/playpass-sparkline.py`). |

**Trampas ya encontradas** (no repetirlas):

- **La hormiga andaba de lado**: la caja del actor tiene el largo en Y y la
  hormiga mira hacia +Y, pero el giro del presenter era `−rumbo` en vez de
  `90° − rumbo`, así que el morro caía 90° a la izquierda del avance —patas y
  antenas perpendiculares al rumbo—. Se veía a simple vista y **no lo veía ningún
  check**: los tres lóbulos estaban, el mundo aparecía, el conteo de hormigas no
  cambiaba y los píxeles de «se ven hormigas» seguían en verde. Lo mide
  `scripts/unity/ProbeActorOrientation.cs` (dentro del editor, con la matemática
  de Unity y la rotación del propio presenter: reimplementar la convención de
  `Euler` es justo cómo se cuela este giro) y es un PASO del Play pass: mide el
  morro contra el rumbo en ocho rumbos y se planta si el desvío pasa de 2° (el
  mismo paso mide el acostado de ítems y hojas, ver «de canto» abajo).
  Medido el 2026-10-10 antes del arreglo: **90° justos** en los ocho. Y desde el
  mismo día la misma pregunta se mide **en PÍXELES**, porque la matriz no prueba el
  render: `scripts/unity/ProbeAntHeadingPixels.cs` dibuja la malla y el material
  reales con un rumbo fijo (cámara de usar y tirar que copia la del juego y sólo ve
  la capa de la hormiga, silueta = diferencia entre el frame con y sin ella) y mide
  su eje a la escala de la partida (18,69 px) y a 6×, más su morro a zoom —a 19 px el
  reparto de área de los dos extremos deja 1 px de margen en los diagonales y no
  decide—. Medido: 0° de desvío en los ocho rumbos, y 90° con el giro viejo `−rumbo`.
  La evidencia que se mira queda en `artifacts/playpass-heading-pixels/`.

- **Los ítems y las hojas salían DE CANTO**: el arte es un sólido PLANO (se
  extruye en Z) y la cámara mira de arriba, así que una pieza dibujada con
  `Quaternion.identity` enseña su canto: desde arriba se veía un rectángulo y
  nada del dibujo. La hoja salía de 0,66 s × 1,10 s con su largo (1,24 u) en el
  eje VERTICAL —y el `1,1` de la escala, que iba al grosor, estirado en el
  plano—, así que no se veían ni la punta ni el nervio; la carga, igual, y los
  mordiscos con ella. Ahora las tres usan el MISMO ayudante que la hormiga
  (`SimPresenterBehaviour.FlatRotation` = `ActorRotation` con el rumbo que apunta
  al norte, y `FlatScale` = 1:1 en el plano y el grosor a la mitad, con lo que
  `x` es ancho, `y` largo y `z` grosor, como en la hormiga): medido en el editor
  por `ProbeActorOrientation`, la hoja acuesta su eje (0° del plano, 0° al norte)
  y ocupa 0,60 × 1,24 u de planta con 0,50 u de alto, contra 0,66 × 1,10 u de
  canto y 0,62 u de alto antes. No hay rumbo que seguir —el `FoodItem` del Core
  no guarda ángulo y el stream no lo lleva—, así que el eje se acuesta y se queda
  al norte en vez de inventarle un giro por pieza; el probe además exige que la
  configuración vieja FALLE, para que la comprobación no sea decorativa.

- **Las muescas de la hoja flotaban fuera del limbo**: el borde de la hoja es una
  ELIPSE —semiejes 0,30 s de ancho × 0,62 s de largo, los extremos del polígono de
  `GameSpriteModel.Leaf()`—, pero `AddBites` repartía los cortes consumidos con un
  radio ÚNICO de 0,55 s. Un radio no cae en un borde elíptico en ningún sitio: por
  los costados queda FUERA del limbo (0,55 &gt; 0,30) y por las puntas DENTRO
  (0,55 &lt; 0,62), así que las muescas se veían flotando en el vacío junto a la
  hoja en vez de comer de ella. Ahora `SimPresenterBehaviour.BiteOffset` las reparte
  sobre la elipse (los semiejes salen de `LeafHalfWidth`/`LeafHalfLength`, una sola
  definición) y el MISMO paso del Play pass que mide el acostado comprueba cada
  muesca contra el borde (r = 1, tolerancia 0,02) —y exige que el círculo viejo de
  0,55 s falle, o la comprobación sería decorativa.

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
- El editor **añade paquetes al `Packages/manifest.json` por su cuenta** (pasó
  con `com.unity.ai.assistant` —pre-release— y `com.unity.ai.inference`, con sus
  90 líneas transitivas en `packages-lock.json`): abrir una sesión con funciones
  de IA activadas basta. El juego no los usa — ni un `using` de sus assemblies —
  y el manifest se mantiene con lo mínimo (`pipeline`, `ugui` y los módulos que
  el juego toca). Si tras una sesión de Unity el `status` muestra esos dos
  ficheros modificados, la decisión por defecto es `git checkout --` y dejarlos
  fuera: si algún día el juego los necesita (p. ej. un modelo ONNX en el
  inspector), se añaden a PROPÓSITO, en su propio commit y con justificación.
  Con la misma regla, el manifest lleva **`com.unity.modules.screencapture`**: no
  lo usa el juego, lo usa la **verificación visual** del proyecto
  (`ProbeVisualSample` del Play pass lee píxeles del backbuffer). Sin él, ese
  probe **no compila** y la fase visual del pass deja de poder correr — que es lo
  que estuvo pasando, en silencio, desde la poda de paquetes hasta que el primer
  build de jugador lo destapó (`docs/fase5-3-escala.md` §4.7).
- **El juego NO compila como player si un script de runtime usa API de
  `UnityEditor`.** El editor compila `Assembly-CSharp` con referencia a
  `UnityEditor`, así que allí pasa y aquí no: `DragAndDrop`/`DragAndDropVisualMode`
  (el drag & drop del diálogo de importación) costaron ocho `CS0103` en el primer
  build. Todo lo del editor va bajo `#if UNITY_EDITOR`, con una rama de
  comportamiento neutro (`IsDragHovering => false`) para que la vista no cambie.
  El build se comprueba con `bash scripts/player-perf.sh` (construye el player) o
  `bash scripts/check-unity-compile.sh` (compilación en batch, más rápida: **8 s**
  con la Library caliente; en Windows, `scripts/check-unity.bat` es el mismo
  comando). Esa comprobación está enganchada a `.githooks/pre-push`
  (`scripts/install-hooks.sh`) porque el job de CI que la hace está dormido y, con
  licencia Personal, no puede activarse en un runner alojado: la puerta local es
  la que cubre este riesgo hoy. El hook corre además, **antes** de esa puerta y
  sin necesidad del editor, dos chequeos sobre los commits que el push manda: el
  churn de assets (`scripts/check-unity-assets.sh`) y las referencias perdidas
  (`scripts/check-unity-references.sh --range`, cada commit contra su padre). Así
  ni el ruido de re-serialización que este proyecto genera al abrir la escena ni
  un asset que se quedó sin su dato —el `_BaseMap` en `fileID: 0`, que dibuja el
  objeto con el valor por defecto— llegan siquiera a la rama.
- **Medir el COSTE por frame del build necesita `enableFrameTimingStats`.** Con el
  vsync entregando al refresco, el coste del frame queda tapado (60 fps es el techo
  del monitor): `bash scripts/player-perf.sh --cpu` construye con ese ajuste
  encendido —`PlayerBuild` lo **restaura** al terminar, para no dejar
  `ProjectSettings` cambiado— y corre el player con el vsync apagado, de donde sale
  el `FrameTimingManager` (`docs/fase5-3-escala.md` §4.8).
- **El player no es el sistema.** El mundo lo simula el CLI en otro proceso, y su CPU
  no aparece en ningún tiempo de frame: `bash scripts/player-perf.sh --system` añade esa
  pata (la lee del proceso hijo que este assembly lanza, `StreamSource.CliCpuMs`) con el
  vsync apagado y un calentamiento de 2 s, para que la fase de simulación caiga dentro
  de la ventana (`docs/fase5-3-escala.md` §4.9).
- **Cuántas colonias caben se mide moviendo las colonias por vista.** El coste del mundo
  depende de cuántas simula cada vista, así que el montaje la declara hacia fuera: el
  bootstrapper (`CreateMultiSimPerfScene(grid, ticks, coloniesPerView)`), el build
  (`ANTSIM_PERF_COLONIES`) y el medidor (`--colonies N`) llevan el mismo número, y el
  informe del player publica `coloniesPerView` para que no haya que adivinarlo. El
  barrido completo es `bash scripts/system-sweep.sh`, que mide **1, 2, 4 y 8** colonias por
  vista, publica el ajuste (fijo/marginal) y reconstruye la tabla y el ajuste de los
  informes ya guardados con `--reuse` (`docs/fase5-3-escala.md` §4.10).

Para volver a generar la escena con este aspecto: menú **AntSim → Crear escena de
juego** (o `unity command menu --path "AntSim/Crear escena de juego"`) y Play. La
escena se guarda y se registra en build settings sola.

### Ajustes finos

- `SimPresenterBehaviour`: semilla/grid/colonias/ticks/`SeedPoolPath` (el de
  por defecto es `artifacts/pretrain-maze-los3.antgenome`, entrenado en el
  laberinto del juego; `artifacts/pretrain-warm-v2.antgenome` es el de
  referencia) y velocidad 0/1/2/4/16×.
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
- **Feromonas visibles (F4.5 · selector en F5.1)**: lanza el CLI con
  `--phero-every 30` y el quad `PheromoneTiles` (lo crea el bootstrapper) pinta
  el rastro de la colonia 0 vía RenderTexture. `PheromoneTileModel` (puro)
  decodifica el paquete RLE. **Con dos colonias compitiendo**, añade
  `--phero-layers` con `food`, `home`, `alarm` y `footprint` de cada colonia:
  cada tick trae entonces las cuatro capas y el tablero las pinta **a la vez**,
  cada tipo con su color (ámbar comida, verde casa, rojo alarma, violeta huella)
  mediante `PheromoneCompositeModel` (puro), que mezcla donde se cruzan. `F`
  pasa de «todas» a una capa suelta y la cicla; `G` cambia de colonia. El render
  pinta **vacío** si la colonia elegida no trae capas (nunca deja el frame
  anterior, que se leería como dato de otra colonia). La emisión es telemetría
  pura: el hash del mundo no cambia (test). En la ronda el rastro es de **una
  celda de ancho** (sin difusión) y vive **7×** más (comida 2 100 s), así que el
  camino permanece entre visita y visita.
  **Desde F8 (reclutamiento)** el juego arranca con `Recruitment = true`: la
  comida se siembra además en 8 cúmulos ricos (10 ep) y al alcance de la
  colonia, la que busca deja rastro de casa y la cargada de comida, y la que
  busca gira hacia el lado que huele más. El presenter pide entonces
  `--recruitment` y las DOS capas (`food` + `home`) de **cada** colonia, la
  escena pinta la capa de comida por defecto y el quad conserva el último
  paquete entre emisiones (el canal E llega cada `PheroEvery` ticks; borrarlo
  en el resto era el parpadeo). El depósito de comida va a **una celda** (el
  camino que pisa la hormiga, no una banda de 3) y al componer la textura se
  gira 180°: las UV del `Plane` de Unity empiezan en el rincón opuesto y el mapa
  salía espejado (`ProbeRecruitmentShot` lo mide: 93 % de celdas sobre su píxel
  ámbar, 2,5 % girado). La textura no pinta el byte como alfa: `PheromoneRamp`
  (puro) descarta la cola de difusión por debajo del 18 % del máximo de la capa y
  lleva el resto del borde (0,30) al núcleo opaco, así que el rastro se lee como
  camino y no como mancha — menos superficie y más contraste, sin tocar el dato.
  El HUD publica **dos** líneas: la configuración que viaja al CLI (`montones=… ·
  rastro=…`) y lo que HAY en el tablero (`montones vivos · ítems · rastro <capa>
  N celdas · máx · banda`), contado con la MISMA curva del render; si la cifra no
  cuadra con los píxeles, el fallo está en el render. Las dos líneas las contrasta
  con el tablero `scripts/unity/ProbeRecruitmentHud.cs` (guarda el frame en
  `Temp/`, y una copia en `artifacts/`). Las consecuencias se documentan en
  [`docs/fase7-obstaculos.md` §8bis](../../../docs/fase7-obstaculos.md).
  Apagarlo (mundo clásico) es `play-game.sh --no-recruitment` o desmarcar
  `Recruitment` en el presenter.
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

Los que consumen las escenas del multi-visor también viven solo en disco:
`multiview-v0..v3.jsonl` y `multiview-atta.jsonl` (10 MB cada uno, los que
reproducen `CreateMultiSimSmokeScene*`) y `smoke-seeds/*.jsonl` +
`smoke-warmv2-stream.jsonl` (13 MB cada uno, el e2e del relevo). Un clon limpio
NO los trae: hay que volcar esas partidas con el CLI antes de montar esas
escenas, o la vista se queda sin stream que reproducir.

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
