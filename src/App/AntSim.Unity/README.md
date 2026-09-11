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

## Abrir el proyecto

1. Unity 2022.3+ → Open Project → `src/App/AntSim.Unity`.
2. Publica el CLI (`dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim-cli`)
   y ajusta `CliPath` en `SimPresenterBehaviour`.
3. Crea una escena con un plano + los meshes/materials de hormiga/ítem y engancha
   `SimPresenterBehaviour`. Play: el mundo llega por el stream.
4. `PoolPickerBehaviour` alimenta el selector; los especialistas muestran su
   advertencia ⚠ (campo `card` del JSON canónico).
5. `AntInspectorBehaviour`: asigna un uGUI Text y (opcional) el
   `SimPresenterBehaviour` — con `SelectAntId` se sigue una hormiga por id, o
   llama `PickNearest(point)` desde el raycast de la F4.2 para click-seleccionar.
   El botón de la tarjeta llama `FollowTrackedBrain()` para el modo linaje:
   rastrea todos los cuerpos del mismo cerebro (misma huella de genoma) y la
   tarjeta muestra `cerebro #F · N cuerpos · M vivos` con el linaje completo.
6. **HUD (F4.2)**: `HudLayoutBehaviour` — asigna `Presenter` (el
   `SimPresenterBehaviour`), una uGUI Text por colonia en `ColonyCardTexts`, y
   una Text vertical en `ToastsText`. Las tarjetas pintan el semáforo que
   llega del canal D (el Core lo calcula con `RelayVerdict`: la UI no evalúa
   umbrales) y los toasts llegan ya derivados con su nivel y ancla de cámara.

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

- **F4.1**: escena real, feromonas por tiles (RenderTexture), regresión visual con semillas fijas.
- **F4.2**: HUD (tarjetas de colonia + semáforo de relevo del stream), alertas por eventos.
- **F4.5**: UI del picker uGUI con los dos niveles.
