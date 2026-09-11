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
| `Scripts/Presenter/SimPresenterBehaviour.cs` | DrawMesh por frame, pausa/velocidad | Unity |
| `Scripts/UI/PoolPickerBehaviour.cs` | alimenta la UI del picker | Unity |

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

## Próximos hitos

- **F4.1**: escena real, feromonas por tiles (RenderTexture), regresión visual con semillas fijas.
- **F4.2**: HUD (tarjetas de colonia + semáforo de relevo del stream), alertas por eventos.
- **F4.5**: UI del picker uGUI con los dos niveles.
