# Fase 4/5 — Play pass en el editor Unity: preparación y checklist

La única validación que ningún test headless sustituye: abrir Unity, crear la
escena con el bootstrapper y ver la partida en vivo. Este documento congela el
estado de compatibilidad de los scripts con **Unity 6000** (migración real,
verificada con compilación en batch y ejecución headless del bootstrapper) y el
checklist de verificación visual en un comando de menú. Todo el código implicado
está ya verificado headless (208/208 tests); aquí solo falta el ojo humano.

## 1. Estado de la cadena antes de abrir Unity (verificado hoy)

| Pieza | Estado |
|---|---|
| Suite headless | 208/208 verdes (216 en el árbol de trabajo: +8 del resolutor de rutas aún sin commitear) |
| Pins de CI | fixture canónico ✅ · replay 3000 (drops) ✅ · replay 6000 (relevo) ✅ |
| CLI publicado en `build/antsim` | vigente, con el canal F (`--activ-every`, `--inspect` en `--help` y en el stream) |
| Proyecto Unity | migrado a `6000.6.0f1`: compila limpio en el editor, sin Safe Mode |
| Rutas relativas | `RepoPathResolver` resuelve `build/antsim`, `artifacts/…` y `ReplayFile` contra el repo root derivado de `Application.dataPath` — el cwd del editor (carpeta del proyecto) deja de romper el Play |
| Interacción `Z` | `DropFoodClickHandler.UndoKey` (Z por defecto) deshace el último drop del plan |
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
aparecen en una compilación del editor. Por eso este paso merece ser un check de
CI, no una auditoría manual.

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
   - [x] HUD: las 2 tarjetas de colonia actualizan reserva/cría; el semáforo
         de relevo arranca gris y pasa a ámbar/verde al llegar la primera descarga
         (≈t3950 en 96² semilla 42; ≈t5154 en 256² — ver la tabla de timing).
   - [ ] Toasts del canal D aparecen con dedupe (no se repiten cada tick).
         — requiere una partida con alertas EN VIVO: en las corridas del pass la
         colonia no emitió alertas (`Events=0`) y los toasts ya habían expirado.
   - [x] Consola de Unity sin `stream falló` — `build/antsim` se resuelve contra
         el repo root aunque el cwd del editor sea la carpeta del proyecto.
4. **Interacción** (todas por Input legacy) — ⏳ pendiente humano: la `Input`
   class lee el dispositivo real y no es inyectable desde el CLI, así que el
   agente no puede verificar las pulsaciones (sí el resto de la cadena):
   - [ ] Click en una hormiga → la tarjeta de inspección la sigue (12 campos
         del canal A, huella del cerebro).
   - [ ] Tecla `D` → modo marcar; click en el suelo → `drops: 1/5 · …` en el
         panel DropPlan; `Z` deshace; «reiniciar con plan» relanza la misma
         partida sembrada y los CommandExecuted aparecen en el historial.
   - [ ] Tecla `J` (o Alt+click en un toast) → la cámara salta al ancla (x,y)
         de la alerta.
5. **Sembrar un pool (relevo pre-entrenado)**: en `SimPresenterBehaviour`,
   `SeedPoolPath = artifacts/pretrain-warm-v2.antgenome` (relativa al repo root;
   el presenter la resuelve con `RepoPathResolver`), Play.
   - [x] La colonia 0 muestra tráfico de relevo real y el semáforo verde
         (`first-unload 3943`, 🟢); la competidora sigue gris (🔘) — el contraste
         que promete el picker, leído de la tarjeta REAL del HUD.
6. **Replay determinista (opcional, sin CLI)**:
   `ReplayFile = artifacts/stream-fixture-256.jsonl` (generar con el repro del
   README), Play → la misma partida se reproduce tick a tick.
7. **Canal F (F5.0, opcional)**: en el inspector del `SimPresenter`,
   `ActivEvery = 30` e `InspectId = <id de una hormiga>` (o seleccionarla con
   click); el inspector puede pintar el grafo MLP vía
   `ActivationViewModel.Render`. `PheroEvery = 30` pinta el canal E.

**Cierre del pass**: con los checkboxes 1–5 marcados, la Fase 4 queda
visualmente validada en el editor y el esqueleto Unity pasa a mantenimiento.
Al 2026-09-12 el bloque 4 (interacción física) es lo único que sigue exigiendo
un humano; 6 y 7 son opcionales.

## 3bis. Pass ejecutado por el agente (2026-09-12)

El pass se corrió sin humano sobre el editor vivo vía `unity-cli` (paquete
`com.unity.pipeline`): abrir el proyecto, crear la escena con el bootstrapper,
entrar en Play y verificar el estado LEYENDO el modelo real por reflexión dentro
del editor. Resultado por bloque:

| Bloque | Resultado |
|---|---|
| 1 compilar | ✅ 0 errores (solo avisos CS8632 preexistentes del perfil sin `#nullable`) |
| 2 crear escena | ✅ jerarquía completa y cableado F5.1 verificados con `get_serialized_fields` |
| 3 stream vivo | ✅ `finalTick 7200`, hash `e94a9a9e…`, 44 hormigas / 24 ítems, tarjetas con reserva/cría, sin `stream falló` |
| 4 interacción | ⏳ no verificable sin inyectar input |
| 5 pool sembrado | ✅ `first-unload 3943`; tarjeta 0 🟢 vs tarjeta 1 🔘 |
| 6 replay / 7 canal F | ⏳ opcionales, no ejecutados |

### Dos defectos reales que el pass destapó (corregidos)

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

### Cómo reproducir el pass automático

```bash
unity open <ruta del proyecto>        # GUI con com.unity.pipeline instalado
unity status --format json            # esperar state "ready"
unity command menu "AntSim/Crear escena de juego"
unity command save_scene --path Assets/Scenes/Game.unity
unity command editor_play
unity command run_script --file probe.cs --entry Probe.Run   # leer estado real
unity command editor_stop
```

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
