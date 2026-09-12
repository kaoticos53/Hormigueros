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
| `Scripts/Presenter/GameStreamPresenter.cs` | interpolación con retraso de 1 tick → RenderState | ✅ |
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
- **Salto de cámara por toast (§1)**: tecla J (o Alt+click en la pila) mueve
  la cámara al ancla (x, y) que el canal D trae en cada alerta.
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
