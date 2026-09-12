# Fase 4/5 — Play pass en el editor Unity: preparación y checklist

La única validación que ningún test headless sustituye: abrir Unity, crear la
escena con el bootstrapper y ver la partida en vivo. Este documento congela el
estado de compatibilidad de los scripts con **Unity 6000** (auditado estáticamente
contra las APIs deprecadas conocidas) y el checklist de verificación visual en
un comando de menú. Todo el código implicado está ya verificado headless
(200/200 tests); aquí solo falta el ojo humano.

## 1. Estado de la cadena antes de abrir Unity (verificado hoy)

| Pieza | Estado |
|---|---|
| Suite headless | 200/200 verdes |
| Pins de CI | fixture canónico ✅ · replay 3000 (drops) ✅ · replay 6000 (relevo) ✅ |
| CLI publicado en `build/antsim` | **re-publicado** con el canal F (`--activ-every`, `--inspect` en `--help` y en el stream) — el binario previo era anterior a F5.0 |
| Tag de cierre | `v0.4.0` (Fase 4 completa) |

## 2. Auditoría de APIs Unity 6000 (estática, sin abrir el editor)

Versión del proyecto: `ProjectVersion.txt` dice `2022.3.0f1` — Unity 6000 lo
abre y actualiza el proyecto sin cambios de scripts. Resultado por riesgo:

| API en uso | Dónde | Estado en Unity 6000 | Acción |
|---|---|---|---|
| `Object.FindFirstObjectByType<T>()` | SceneBootstrapper (EventSystem) | ✅ API actual (reemplaza a `FindObjectOfType`, deprecada) | ninguna |
| `EditorSceneManager.NewScene/MarkSceneDirty` | SceneBootstrapper | ✅ estable | ninguna |
| `Canvas` + `CanvasScaler` + `GraphicRaycaster` | SceneBootstrapper | ✅ estable | ninguna |
| `Text` uGUI (no TMP) + `LegacyRuntime.ttf` con fallback `Arial.ttf` | `NewText` | ⚠️ funciona: el legacy Text y las built-in fonts siguen presentes en 6000 (TMP es opt-in por paquete). El fallback cubre builds sin `LegacyRuntime.ttf` | si algún día se migra a TMP, es el único punto (método `NewText`) |
| `Shader.Find("Standard")` | `NewMat` | ⚠️ en proyectos URP/HDRP devuelve null → material rosa. El `manifest.json` del proyecto NO declara render-pipeline, así que el Built-in RP estándar aplica y funciona | si el proyecto migra a URP: cambiar a `Universal Render Pipeline/Lit` (único punto, método `NewMat`) |
| `StandaloneInputModule` | SceneBootstrapper | ✅ correcto: el proyecto no incluye `com.unity.inputsystem` activo (solo el Input legacy), que es lo que el módulo espera | si se añade el nuevo Input System: sustituir por `InputSystemUIInputModule` (único punto) |
| `Input.GetMouseButton/Key/mousePosition` (Input class) | AntPickClickHandler, DropFoodClickHandler, ToastClickCameraJump | ✅ con Input legacy activo (estado por defecto del proyecto, sin `activeInputHandler` personalizado) | ídem |
| `Camera.main` | AntPickClickHandler, DropFoodClickHandler, ToastClickCameraJump | ✅ el bootstrapper etiqueta la cámara `MainCamera` | ninguna |
| `Graphics.DrawMesh` / `Graphics.Blit` | SimPresenterBehaviour, PheromoneTileBehaviour | ✅ estables | ninguna |
| `Resources.GetBuiltinResource<Font>` | SceneBootstrapper | ✅ con dual-path LegacyRuntime/Arial | ninguna |
| `#if UNITY_EDITOR` guard | SceneBootstrapper completo | ✅ el resto de scripts de escena compilan en player | ninguna |

**Conclusión**: no hay ninguna API deprecada o rota en 6000 en los scripts
actuales. Los tres puntos de dependencia de contexto (fuente, shader, input)
están aislados en métodos únicos del bootstrapper o tienen fallback, y están
documentados arriba con su sustituto si el proyecto cambia de pipeline/input.

## 3. Checklist de validación en un comando (≤ 15 min)

Preparación (una vez, fuera de Unity):

```bash
dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim
```

1. **Abrir** Unity 6000 → Open Project → `src/App/AntSim.Unity`.
   - [ ] La consola no muestra errores de compilación (solo avisos del importer).
2. **Crear escena**: menú `AntSim → Crear escena de juego`.
   - [ ] Se crea la escena con Main Camera, Directional Light, Floor, 2 nidos,
         `PheromoneTiles`, `SimPresenter` y el Canvas del HUD completo
         (2 tarjetas de colonia, Toasts, CommandHistory, AntInspector,
         DropPlan, ImportDialog) — sin referencias null en el inspector.
   - [ ] Log del bootstrapper en consola.
   - [ ] Guardar con Ctrl+S (`Assets/Scenes/Game.unity`).
3. **Play (stream vivo)**.
   - [ ] El mundo aparece: hormigas moviéndose (DrawMesh), ítems verdes, 2 nidos.
   - [ ] HUD: las 2 tarjetas de colonia actualizan reserva/cría; el semáforo
         de relevo arranca gris y pasa a ámbar/verde cerca de la primera descarga
         (t≈3950 en el fixture 96²).
   - [ ] Toasts del canal D aparecen con dedupe (no se repiten cada tick).
   - [ ] Consola de Unity sin `stream falló` (si aparece: CLI no publicado o
         ruta `build/antsim` incorrecta desde la carpeta del proyecto).
4. **Interacción** (todas por Input legacy):
   - [ ] Click en una hormiga → la tarjeta de inspección la sigue (12 campos
         del canal A, huella del cerebro).
   - [ ] Tecla `D` → modo marcar; click en el suelo → `drops: 1/5 · …` en el
         panel DropPlan; `Z` deshace; «reiniciar con plan» relanza la misma
         partida sembrada y los CommandExecuted aparecen en el historial.
   - [ ] Tecla `J` (o Alt+click en un toast) → la cámara salta al ancla (x,y)
         de la alerta.
5. **Sembrar un pool (relevo pre-entrenado)**: en `SimPresenterBehaviour`,
   `SeedPoolPath = artifacts/pretrain-warm-v2.antgenome`, Play.
   - [ ] La colonia 0 muestra tráfico de relevo real y el semáforo verde;
         la competidora sigue gris — el contraste que promete el picker.
6. **Replay determinista (opcional, sin CLI)**:
   `ReplayFile = artifacts/stream-fixture-256.jsonl` (generar con el repro del
   README), Play → la misma partida se reproduce tick a tick.
7. **Canal F (F5.0, opcional)**: añadir `--activ-every 30 --inspect <id>` a los
   argumentos del CLI en `StreamSource` o al campo correspondiente; el
   inspector puede pintar el grafo MLP vía `ActivationViewModel.Render`.

**Cierre del pass**: con los checkboxes 1–5 marcados, la Fase 4 queda
visualmente validada en el editor y el esqueleto Unity pasa a mantenimiento.

## 4. Riesgos conocidos (fuera de alcance de este pass)

- El texto uGUI legacy puede recortar líneas largas (Overflow activado en
  `NewText` lo mitiga); el pulido por elementos uGUI es F5.1.
- `Shader.Find("Standard")` en editor devuelve el shader aunque no haya
  referencia de proyecto; en build player requiere que el shader esté incluido
  (para builds, añadir a *Graphics → Always Included Shaders*).
- El fixture 96² con `frameEvery 1` pesa ~MB por minuto de sim; para sesiones
  largas usar `--frame-every 30` (canal A a 1 Hz, suficiente para HUD).
