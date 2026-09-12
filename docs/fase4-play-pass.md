# Fase 4/5 — Play pass en el editor Unity: preparación y checklist

La única validación que ningún test headless sustituye: abrir Unity, crear la
escena con el bootstrapper y ver la partida en vivo. Este documento congela el
estado de compatibilidad de los scripts con **Unity 6000** (migración real,
verificada con compilación en batch y ejecución headless del bootstrapper) y el
checklist de verificación visual en un comando de menú. Todo el código implicado
está ya verificado headless (228/228 tests) y los bloques 1–5 los corre un solo
comando contra el editor vivo (`scripts/playpass-live.sh`); lo que queda para el
ojo humano es la estética y el juicio de jugabilidad, no el contrato.

## 1. Estado de la cadena antes de abrir Unity (verificado hoy)

| Pieza | Estado |
|---|---|
| Suite headless | 228/228 verdes |
| Pins de CI | fixture canónico ✅ · replay 3000 (drops) ✅ · replay 6000 (relevo) ✅ |
| CLI publicado en `build/antsim` | vigente, con el canal F (`--activ-every`, `--inspect` en `--help` y en el stream) |
| Proyecto Unity | migrado a `6000.6.0f1`: compila limpio en el editor, sin Safe Mode |
| Rutas relativas | `RepoPathResolver` resuelve `build/antsim`, `artifacts/…` y `ReplayFile` contra el repo root derivado de `Application.dataPath` — el cwd del editor (carpeta del proyecto) deja de romper el Play |
| Interacción `Z` | `DropFoodClickHandler.UndoKey` (Z por defecto) deshace el último drop del plan |
| Interacción (D·click·Z·J·click) | acciones en métodos sin dispositivo (F5.2) + geometría del click pura (`WorldPlaneRay`): verificadas EN VIVO por el bloque 4 del harness |
| Escena en build settings | el bootstrapper la guarda y la registra — sin eso el reinicio con plan no puede recargarla (defecto 5) |
| Tag de cierre | `v0.4.0` (Fase 4 completa) |

## 2. Migración real a Unity 6000 (verificada compilando)

`ProjectVersion.txt` pasó de `2022.3.0f1` a `6000.6.0f1`. La primera apertura en
6000 cayó en **Safe Mode**: 9 errores de compilación de 4 causas. La migración no
se auditó sobre el papel — se verificó con una **compilación real en batch**
(`Unity.exe -batchmode -nographics -quit -projectPath …`: exit 0, 0 `error CS`) y
ejecutando el bootstrapper headless (`-executeMethod …CreateGameScene`: escena
creada, sin excepciones). Lo que **realmente** hubo que cambiar:

| Punto | Síntoma en 6000 | Fix aplicado |
|---|---|---|
| `Object.FindObjectOfType<T>()` | CS0618, deprecada | `FindAnyObjectByType<T>()` (SceneBootstrapper → EventSystem) |
| `CameraClearFlags.SolidCamera` | CS0117: no existe | `CameraClearFlags.SolidColor` |
| `com.unity.ugui` ausente del manifest | `UnityEngine.UI` (Canvas/Text/Image/Button) no resuelve | añadido al manifest; fijado en `packages-lock.json` |
| `OperatingSystem.IsWindows()` | CS0117: API .NET 5+ fuera del perfil C# 9 de Unity | `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` (`StreamReader`) |
| `string.Create(IFormatProvider, …)` · `StringBuilder.Append(IFormatProvider, …)` | CS7036: APIs .NET 6 | composición con `ToString(format, CultureInfo.InvariantCulture)` (`DropFoodPlanModel`) |
| `GameStreamParser.ColonyView?` con `!` | CS1061: `!` no desenvuelve un struct nullable | `.Value` (`HudLayoutBehaviour`) |

Los dos últimos **no** son APIs de Unity sino C# puro: el headless no los ve
porque solo compila los modelos puros, nunca los MonoBehaviours, así que solo
aparecen en una compilación del editor. Por eso este paso es ahora un **check de
CI** (`scripts/check-unity-compile.sh`, job `unity-compile`), no una auditoría
manual: compila el proyecto en batch y falla con cualquier `error CS` (dormido
hasta definir `UNITY_CI` + licencia; ver el README).

### Puntos que siguen dependiendo del entorno (no rotos, aislados)

| Punto | Dónde | Estado en 6000 | Sustituto si el proyecto cambia |
|---|---|---|---|
| `Text` uGUI (no TMP) + `LegacyRuntime.ttf`→`Arial.ttf` | `NewText` | ⚠️ funciona: 6000 trae el legacy Text y las built-in fonts (TMP es opt-in) | migrar a TMP: único punto, `NewText` |
| `Shader.Find("Standard")` | `NewMat` | ⚠️ en URP/HDRP devuelve null → rosa; el manifest no declara pipeline, así que aplica Built-in RP | URP: `Universal Render Pipeline/Lit` (`NewMat`) |
| `StandaloneInputModule` + `Input` legacy | SceneBootstrapper, AntPickClickHandler, DropFoodClickHandler, ToastClickCameraJump | ✅ el proyecto no activa `com.unity.inputsystem` | con el nuevo Input System: `InputSystemUIInputModule` |
| `Camera.main` | handlers de click/toast | ✅ el bootstrapper etiqueta la cámara `MainCamera` | — |
| `Graphics.DrawMesh`/`Blit`, `EditorSceneManager`, `Canvas`/`CanvasScaler`/`GraphicRaycaster`, `Resources.GetBuiltinResource<Font>` | presenter, bootstrapper | ✅ estables en 6000 | — |
| `#if UNITY_EDITOR` | SceneBootstrapper completo | ✅ el resto de scripts compilan en player | — |

**Conclusión**: corregidos los 6 fallos de arriba, el proyecto compila limpio en
6000.6.0f1 (0 `error CS`, verificado en batch) y el bootstrapper crea la escena
sin excepciones. Quedan tres dependencias de contexto (fuente uGUI, shader
Standard, Input legacy), todas aisladas con su sustituto documentado.

## 3. Checklist de validación en un comando (≤ 15 min)

Preparación (una vez, fuera de Unity):

```bash
dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim
```

El bootstrapper crea la escena con `Grid = 96`, `Ticks = 7200`, `FrameEvery = 1`:
sembrado con warm-v2, el relevo despierta dentro de la sesión. El mundo del
relevo real es **256²**, donde la primera descarga llega más tarde — hay que subir
`Ticks` (≥ 6000) o usar `ReplayFile` con el fixture de 256. Timing por grid
(semilla 42, warm-v2 sembrado):

| Grid | Ticks mínimos | Primera descarga | Referencia |
|---|---|---|---|
| 96 (default del bootstrapper) | 7200 | ≈ **t3950** | `docs/fase4-smoke-e2e.md` |
| 256 (mundo del relevo) | ≥ 6000 | ≈ **t5154** | `artifacts/stream-fixture-256.jsonl` |

1. **Abrir** Unity 6000 → Open Project → `src/App/AntSim.Unity`.
   - [x] La consola no muestra errores de compilación (solo avisos del importer).
2. **Crear escena**: menú `AntSim → Crear escena de juego`.
   - [x] Se crea la escena con Main Camera, Directional Light, Floor, 2 nidos
         (posiciones con la fórmula de `WorldSim` para el grid), `PheromoneTiles`
         y `SimPresenter`; el Canvas del HUD con los bloques de texto v1
         (`ColonyCard_0/1`, `Toasts`, `CommandHistory`, `AntInspector`, `DropPlan`,
         `ImportDialog`) **más** los elementos F5.1 por-elemento: `ToastContainer`
         (con `ToastTemplate`), las barras `StockBar_0/1` (cada una con su `Fill`)
         y los botones nativos `ImportConfirmBtn` (Confirmar), `ImportCancelBtn`
         (Cancelar) y `RestartWithPlanBtn` (Reiniciar con plan).
   - [x] `ToastContainer`/`ToastTemplate`, `StockBars` y los 3 botones quedan
         cableados por `BindNativeButtons` — sin referencias null en el inspector.
   - [x] Log del bootstrapper en consola.
   - [x] Guardar con Ctrl+S (`Assets/Scenes/Game.unity`).
3. **Play (stream vivo)**.
   - [x] El mundo aparece: hormigas moviéndose (DrawMesh), ítems verdes, 2 nidos.
         **Cableado**: la corrida del harness ve hormigas ≤43, ítems ≤28, 2/2
         nidos, **0** poses fuera del mundo y 44 de 45 muestras con el mundo
         moviéndose (checksum de posiciones).
   - [x] HUD: las 2 tarjetas de colonia actualizan reserva/cría; el semáforo
         de relevo arranca gris y pasa a ámbar/verde al llegar la primera descarga
         (≈t3950 en 96² semilla 42; ≈t5154 en 256² — ver la tabla de timing).
         **Cableado**: el harness exige tarjeta para CADA colonia en toda muestra
         con tick > 0 y comprueba el semáforo final (`[0:2,1:0]`: verde en la
         sembrada, gris en la competidora).
   - [x] Toasts del canal D aparecen con dedupe (no se repiten cada tick).
         **Cableado** en `scripts/playpass-live.sh`: conduce el editor y
         lo comprueba solo (exit ≠ 0 si el dedupe se rompe). Corrida real: 43
         muestras, 2 con toasts, pila máxima **2** (tope 4), **3 claves**
         distintas en 7200 ticks, toda clave mostrada existe en el canal D del
         Core. En t≈3989 la pila es `first-unload` (lvl 2, verde) y el
         `ToastContainer` tiene exactamente ese elemento; después entran
         `laying:0`/`laying:1`. Ni una repetición por tick.
   - [x] Consola de Unity sin `stream falló` — `build/antsim` se resuelve contra
         el repo root aunque el cwd del editor sea la carpeta del proyecto.
         **Cableado**: el harness limpia la consola antes de jugar y falla si
         aparece `stream falló` (0 entradas de error en la corrida).
4. **Interacción** — `Input` sigue leyendo el dispositivo real, pero las cuatro
   acciones tienen ahora un punto de entrada SIN DISPOSITIVO (F5.2) y `Update()`
   solo las cablea a `Input`, así que el pass las dispara y las comprueba:
   - [x] Click en una hormiga → la tarjeta de inspección la sigue (los campos del
         canal A, huella del cerebro).
         **Cableado** (`scripts/unity/ProbeInputBlock.cs`): se pulsa sobre el
         punto de PANTALLA de una hormiga viva —el rayo sale de la cámara real— y
         se exige `pick>0`, que el cuerpo elegido esté a ≤2.5 u del click y que
         la tarjeta lo nombre con los 8 campos del canal A presentes.
   - [x] Tecla `D` → modo marcar; click en el suelo → `drops: 1/5 · …` en el
         panel DropPlan; `Z` deshace; «reiniciar con plan» relanza la misma
         partida sembrada y los CommandExecuted aparecen en el historial.
         **Cableado**: se exige además el NO del contrato —con el modo apagado el
         click no marca nada—, que `Z` sobre el plan vacío no mienta, que los
         args lleven exactamente un `--drop`, que la ESCENA se recargue y que el
         drop del plan aparezca en el historial como `CommandExecuted`.
   - [x] Tecla `J` (o Alt+click en un toast) → la cámara salta al ancla (x,y)
         de la alerta.
         **Cableado**: se compara el destino REAL del salto con el ancla que el
         HUD trae del canal D, y se exige el negativo: un click FUERA de la pila
         no mueve la cámara.
   - Corrida real (22–30 s de fase, dentro del mismo comando que los bloques 3 y
     5): `pick=1 pickdist=0` · `doffmarks=0` · `dmarks=1` · `dundo=0 dzerook=0` ·
     `args=--drop 340:303.31:415.88` · tarjeta `#1` con 8/8 campos del canal A ·
     tras el reinicio `cmdrows=1` en **t=341** con esas MISMAS coordenadas ·
     `jtarget=256:384 == janchor` (el nido de la colonia 0, ancla real de la
     alerta `relay-weak:0`) y `joutmoved=0`. En otra corrida el ancla fue la del
     nido de la colonia 1 (`laying:1`, 512:384): el salto va al ancla REAL de la
     alerta que haya en pantalla, no a una constante.

   Lo que NO cubre, dicho claro: la ENTREGA de la tecla (`Input.GetKeyDown`,
   `GetMouseButtonDown`) sigue siendo del motor. Lo verificado es la reacción a
   cada acción —el raycast, el modo, el plan, el reinicio y el salto—, no que
   Unity reparta la tecla pulsada.
5. **Sembrar un pool (relevo pre-entrenado)**: en `SimPresenterBehaviour`,
   `SeedPoolPath = artifacts/pretrain-warm-v2.antgenome` (relativa al repo root;
   el presenter la resuelve con `RepoPathResolver`), Play.
   - [x] La colonia 0 muestra tráfico de relevo real y el semáforo verde
         (`first-unload 3943`, 🟢); la competidora sigue gris (🔘) — el contraste
         que promete el picker, leído de la tarjeta REAL del HUD.
         **Cableado**: el harness falla si la colonia sembrada NO llega a verde
         (`--seeded-colony`, def. 0) o si la competidora también llega. Corrida
         real: `first-unload` visto, semáforo final `[0:2,1:0]`.
6. **Replay determinista (opcional, sin CLI)**:
   `ReplayFile = artifacts/stream-fixture-256.jsonl` (generar con el repro del
   README), Play → la misma partida se reproduce tick a tick.
7. **Canal F (F5.0, opcional)**: en el inspector del `SimPresenter`,
   `ActivEvery = 30` e `InspectId = <id de una hormiga>` (o seleccionarla con
   click); el inspector puede pintar el grafo MLP vía
   `ActivationViewModel.Render`. `PheroEvery = 30` pinta el canal E.

**Cierre del pass**: con los checkboxes 1–5 marcados, la Fase 4 queda
visualmente validada en el editor y el esqueleto Unity pasa a mantenimiento.
Al 2026-09-12 los bloques 1–5 están cableados y los corre un solo comando
(`scripts/playpass-live.sh`); 6 y 7 son opcionales. Lo único que sigue sin poder
verificarse desde el CLI es la ENTREGA de la tecla por parte del motor, no la
reacción a ella.

## 3bis. Pass ejecutado por el agente (2026-09-12)

El pass se corrió sin humano sobre el editor vivo vía `unity-cli` (paquete
`com.unity.pipeline`): abrir el proyecto, crear la escena con el bootstrapper,
entrar en Play y verificar el estado LEYENDO el modelo real por reflexión dentro
del editor. Resultado por bloque:

| Bloque | Resultado |
|---|---|
| 1 compilar | ✅ 0 errores (solo avisos CS8632 preexistentes del perfil sin `#nullable`) |
| 2 crear escena | ✅ jerarquía completa y cableado F5.1 verificados con `get_serialized_fields` |
| 3 stream vivo | ✅ reproducción EN ORDEN (`curTick` avanza 37→…→7200, ya no salta al final), hash `e94a9a9e…`, 44 hormigas / 24 ítems, tarjetas con reserva/cría, semáforo 🟢 en la colonia 0 tras `first-unload`, toasts del canal D con dedupe (3 claves), sin `stream falló` |
| 4 interacción | ✅ acciones disparadas por los puntos de entrada sin dispositivo (F5.2): click→tarjeta, D/click/Z→plan, reinicio→CommandExecuted, J/Alt+click→ancla. `scripts/playpass-live.sh` |
| 5 pool sembrado | ✅ `first-unload 3943`; tarjeta 0 🟢 vs tarjeta 1 🔘 |
| 6 replay / 7 canal F | ⏳ opcionales, no ejecutados |

### Seis defectos reales que el pass destapó (corregidos)

1. **Escala del mundo (8×).** El Core simula en UNIDADES: `WorldWidth = grid ×
   SimConstants.CellSizeUnits` (8 u/celda ⇒ 768 u para grid 96) y emite los nidos
   en (256,384)/(512,384). La escena se dimensionaba con `grid` u (cámara en
   `(48,80,48)` con `orthographicSize 60`, suelo 96 u), dejando el mundo ENTERO
   8× fuera de cámara, y `DropFoodPlanModel.TryPlace` rechazaba por «fuera del
   mundo» todo drop con x ≥ grid siendo x unidades. Corregido con
   `Streaming.WorldUnits` (espejo de `CellSizeUnits`, verificado por test contra
   el Core) para escena/cámara/nidos/quad y para el límite de los drops.
   Ningún test headless lo veía: los MonoBehaviours no se compilan ahí.
2. **`Application.runInBackground` en falso.** Con el editor sin foco el bucle
   del jugador se CONGELA a los ~2 frames (`frameCount=2`, `time=0.02`): ni el
   mundo ni el HUD avanzan, y el sim terminaba sin pintar nada. Es lo que hacía
   que el pass automático fuera imposible (y lo que ocultó el defecto 1). Se
   activa en el `Start` del presenter.
3. **La reproducción saltaba al último tick (sin buffer).** El CLI construye el
   JSONL ENTERO en memoria y lo escribe de golpe; `StreamSource` bombea las
   líneas a toda velocidad y `GameStreamPresenter` solo guardaba el ÚLTIMO par de
   ticks. Resultado: al primer frame `CurrentTick` ya era el tick 7200 y la vista
   mostraba el estado FINAL — nunca se veía la partida, ni el semáforo pasar a
   verde, ni una alerta del canal D (cada alerta existe solo en su tick). De ahí
   que en las corridas anteriores la colonia saliera siempre «sin alertas».
   Corregido con **reproducción con buffer**: `Feed2` encola, `AdvanceTo` avanza
   el cursor al ritmo de simulación y los ticks presentados se drenan ordenados
   (`TryDequeuePresented`), de modo que un frame a velocidad alta que presenta
   varios ticks no pierde ninguna alerta. Verificado con dos tests headless
   (`Presenter_Buffered_…`: orden 1..600 sin saltos, y alertas del canal D
   idénticas a las del stream) y en vivo (ver bloque 3).
4. **Los toasts expirados dejaban su fondo en pantalla.** Al cablear el check
   del bloque 3, el invariante «lo pintado == el modelo» falló en **17 muestras**:
   la pila del modelo estaba VACÍA y el `ToastContainer` seguía mostrando
   elementos. El pool de F5.1 desactiva en vez de destruir —pero apagaba el
   **Text** (un hijo) mientras el fondo (`Image`) vive en la **raíz**: el toast
   expiraba y quedaba su barra oscura sobre el HUD, con su raycast, porque la
   `Image` absorbe clics. Corregido guardando la RAÍZ por clave y apagándola
   entera. Re-verificado: las muestras con elementos fuera del modelo pasaron de
   17 a **0**. Lo destapó el propio check, no una inspección visual.
5. **«Reiniciar con plan» no podía funcionar en un checkout limpio.** El botón
   relanza la partida con `SceneManager.LoadScene`, y eso exige que la escena
   esté en build settings — pero `EditorBuildSettings.asset` tenía
   `m_Scenes: []` (el estado de un repo recién clonado, donde `Scenes/` ni
   existe) y el bootstrapper solo pedía «guarda con Ctrl+S». Sobra decir que un
   player sin escenas tampoco arranca. Y aunque la escena estuviera registrada,
   el plan se perdía igual: la recarga DESTRUYE la instancia del presenter, así
   que `PendingDropArgs` se iba con ella y el CLI arrancaba sin los drops —el
   botón parecía funcionar y no marcaba nada—. Corregido en los dos extremos: el
   bootstrapper **guarda** la escena y la **registra** en build settings, y los
   args pendientes (y el pool del importador) viajan a la PRÓXIMA instancia por
   un campo estático que `Start()` recoge y limpia. Lo destapó el bloque 4
   cableado: sin este check el fallo se habría quedado en «pendiente humano».
6. **El mundo se veía BLANCO y sin hormigas — el defecto que reportó el
   jugador.** La sonda de píxeles (F5.1) midió lo que ninguna muestra de ESTADO
   veía: el frame en vivo era blanco (`floor=1:1:1`) y no tenía ni un píxel del
   color de las hormigas, con 40 hormigas perfectamente vivas en el modelo.
   Tres causas encadenadas:
   - el quad de feromonas conservaba el material por defecto del primitivo
     (blanco, opaco) y tapaba el tablero entero;
   - aun con material propio, un **unlit sin textura pinta blanco opaco**, así
     que la escena se veía blanca al abrirla (sin darle a Play) hasta que
     llegaba el primer paquete del canal E — nunca, en modo edición;
   - las hormigas se dibujaban **de pie** (cápsula vertical de ~1 u de ancho,
     3 px) con una «escala» de 0,6 sin relación con el tamaño en pantalla.
   Corregido con una capa de feromonas transparente (textura 1×1 transparente
   de reposo), materiales unlit de color elegido y medible, hormigas tumbadas y
   alargadas en la dirección de avance con longitud derivada del lado del mundo
   (0,018 × 768 u ≈ 14 u ≈ 12 px). El pass ahora lo COMPRUEBA: mide píxeles en
   vivo y en modo edición, y falla si el tablero no es terroso o si no se ve
   ninguna hormiga.
7. **El editor en pausa (o en «Play Focused» sin foco) congelaba el mundo sin
   dejar rastro.** Una ejecución real produjo **73 muestras idénticas clavadas
   en el tick 105** y ni un solo error en la consola: el editor estaba en
   `playMode: paused` (el Game view en «Play Focused» pausa el juego al perder
   el foco, y una recarga de dominio durante la partida puede dejarlo pausado).
   El pass anterior no distinguía «mundo parado» de «mundo sin datos». Ahora: se
   compilan las sondas ANTES de entrar en Play (una compilación en partida
   recarga el dominio y congela el mundo), se quita la pausa, y no se muestrea
   hasta que el tick AVANZA de verdad (no basta con «hay hormigas»), con
   recuperación explícita y registro del motivo si se congela a mitad.

### Cómo reproducir el pass automático

```bash
unity open <ruta del proyecto>        # GUI con com.unity.pipeline instalado
unity status --format json            # esperar state "ready"
unity command menu "AntSim/Crear escena de juego"
unity command save_scene --path Assets/Scenes/Game.unity
unity command editor_play
unity command run_script --file probe.cs     # la entrada del probe es Run()
unity command editor_stop
```

**Los bloques 3, 4 y 5 están cableados**: `scripts/playpass-live.sh` conduce el
editor (recompila, escribe el config, ajusta el presenter, entra en Play,
muestrea cada segundo hasta el último tick y para) y analiza las muestras contra
el mundo (aparición, escala, movimiento), las tarjetas (una por colonia), el dedupe
de toasts (sin clave repetida, pila ≤ tope, lo pintado == el modelo), el canal D
del Core (la UI no inventa claves) y el relevo (la sembrada llega a verde y la
competidora no), más la consola sin `stream falló`. Después entra en Play otra vez
para el **bloque 4**, donde dispara las cuatro acciones por los puntos de entrada
sin dispositivo y exige la tarjeta del inspector, el plan, el deshacer, el
reinicio con plan y el salto de cámara.

Desde la pasada de pulido (F5.1) hay además un **bloque de aspecto**: una muestra
de PÍXELES del frame en vivo (`ProbeVisualSample`) y otra de la escena SIN darle
a Play (`ProbeSceneInspect`), que exigen tablero terroso, hormigas visibles y
ningún texto del HUD fuera de su panel. Es lo único que podía fallar por el
defecto 6 — y por eso ahora falla. Sale ≠ 0 si cualquiera se rompe:

```bash
bash scripts/playpass-live.sh                 # bloques 3, 4, 5 y aspecto
bash scripts/playpass-live.sh --no-block4     # solo 3, 5 y aspecto (más rápido)
bash scripts/playpass-live.sh --samples artifacts/playpass-samples.txt
bash scripts/playpass-live.sh --analyze-block4 artifacts/playpass-block4.txt
bash scripts/playpass-live.sh --analyze-visual   # solo el aspecto
bash scripts/playpass-live.sh --selftest      # los tres analizadores, sin editor
```

El bloque de aspecto guarda tres artefactos para poder revisarlos sin volver a
jugar: `artifacts/playpass-visual.txt` (en vivo), `playpass-visual-edit.txt` (al
abrir la escena) y `playpass-scene.txt` (materiales, rects y desbordes).
`scripts/unity/ProbeWhiteDiag.cs` queda como diagnóstico: apaga los objetos del
mundo uno a uno y mide el color del centro, para saber QUÉ superficie pinta el
blanco sin depender de mirar un PNG.

El bloque 4 necesita que la escena esté **registrada en build settings** (el
reinicio la recarga): el harness la reconstruye con el bootstrapper, que la
guarda y la registra. `--block4-speed` (def. 6) y `--block4-deadline` (def. 200 s)
controlan esa fase; la primera descarga (la alerta CON ancla, que es a la que
salta `J`) cae hacia el tick 3943, así que a velocidad 6 el bloque tarda ~30 s.

Para verlo a mano hay que **frenar la reproducción**: el stream llega entero en
un instante, así que el buffer avanza el cursor al ritmo de `Speed`. Con
`Speed = 3` la alerta `first-unload` (t≈3943) aparece al segundo real ~47 y
permanece 6 s de sim — una sola foto en el instante equivocado no ve nada, y de
ahí que el check acumule muestras en lugar de mirar una vez.

Dos trampas del CLI, aprendidas en este pass: (a) el intérprete `eval` NO
referencia `Assembly-CSharp`, así que para leer tipos del proyecto hay que usar
`run_script` (compila un `.cs` con acceso a los tipos) o `get_serialized_fields`
sobre componentes de escena; (b) en Git Bash hay que exportar
`MSYS_NO_PATHCONV=1` para que `--target /HUD Canvas/…` no se convierta en una
ruta de Windows.

## 4. Riesgos conocidos (fuera de alcance de este pass)

- El texto uGUI legacy puede recortar líneas largas (Overflow activado en
  `NewText` lo mitiga). El uGUI **por elemento** de F5.1 ya está hecho (toasts con
  hit-test exacto, barras de stock, botones nativos — ver el bloque 2): lo que
  queda de F5.1 es chip de estado por runway, drag&drop del `.antgenome`,
  gráficas por tarjeta, tipografía/iconos y audio/accesibilidad.
- `Shader.Find("Standard")` en editor devuelve el shader aunque no haya
  referencia de proyecto; en build player requiere que el shader esté incluido
  (para builds, añadir a *Graphics → Always Included Shaders*).
- El fixture 96² con `frameEvery 1` pesa ~MB por minuto de sim (es el default
  del bootstrapper, válido para el pass de 15 min); para sesiones largas subir
  `FrameEvery` a 30 (canal A a 1 Hz, suficiente para HUD).
