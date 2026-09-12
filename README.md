# 🐜 Freebuff Ants (AntSim)

Simulación en tiempo real de hormigueros realistas con **neuroevolución continua**
(cerebros MLP → NEAT evolucionados por algoritmos genéticos), natalidad ligada a
recursos, múltiples especies y render Unity 2D → 3D. Núcleo .NET **headless y
determinista** desacoplado del motor gráfico.

Estado actual: **Fases 0–4 completadas** (núcleo determinista + mundo + neuroevolución
+ pre-entrenamiento headless con transferencia validada + **modo evolución jugable**:
stream JSONL de 5 canales hacia la vista, escena Unity bootstrapeada con HUD —
tarjetas de colonia con semáforo de relevo, toasts, inspector con linaje de cerebros —,
importación con cuarentena, intervención `--drop` determinista y replay bit a bit).
Ver [`docs/arquitectura.md`](docs/arquitectura.md) para el plan por fases completo,
[`docs/especificaciones.md`](docs/especificaciones.md) para los contratos cerrados
y [`docs/fase3ter-resumen.md`](docs/fase3ter-resumen.md) para el resumen de cierre,
[`docs/fase4-diseno-ux.md`](docs/fase4-diseno-ux.md) para el diseño de UX del modo evolución (Fase 4),
[`docs/fase4-hud-contrato.md`](docs/fase4-hud-contrato.md) para el contrato de datos del HUD (F4.2)
y [`docs/fase4-resumen.md`](docs/fase4-resumen.md) para el resumen de cierre de la Fase 4
(junto con [`docs/fase3ter-resumen.md`](docs/fase3ter-resumen.md): arranque en frío, salud del relevo y jerarquía de pools),
y [`docs/fase5-plan.md`](docs/fase5-plan.md) para el plan de la Fase 5 (realismo, especies y escala).

## Notas de versión

### v0.4.0 — Fase 4: modo evolución jugable

El bucle completo del jugador funciona y está verificado de extremo a extremo:
**elegir pool → ver en vivo → inspeccionar hormiga → intervenir → verificar ✓**.

- **Determinismo como producto**: comandos sellados por tick (F4.0); misma semilla
  + mismos comandos ⇒ mundo idéntico bit a bit, probado en CI con tres pins de
  hash (stream canónico, replay con plan de drops a 3000 ticks, fase de relevo
  a 6000 ticks con primera descarga en t3950).
- **Stream de 5 canales** (A poses · B eventos · C métricas · D alertas derivadas
  por el Core · E feromonas opt-in): todo lo visible es verificable headless.
- **HUD completo** (contrato §0–§8): tarjetas de colonia con semáforo de relevo
  escalado por grid (`RelayVerdict`), toasts con rate-limit, inspector de hormiga
  con **linaje de cerebros**, historial de comandos y verificación vía CLI.
- **Tres pilares de jugador**: importar (picker de pools + diálogo de cuarentena
  `genome-info`), espectar (semáforo, toasts, salto de cámara al ancla de alerta),
  intervenir (DropFood click-to-place, plan de 5 drops por partida, relanzar con
  el plan determinista).
- **Escena Unity ensamblada con un comando de menú** (`SceneBootstrapper`), modelos
  puros compilados en la suite headless y `228/228` tests verdes.
- Detalles: [`docs/fase4-resumen.md`](docs/fase4-resumen.md) · smoke e2e:
  [`docs/fase4-smoke-e2e.md`](docs/fase4-smoke-e2e.md) · checklist del Play pass
  en el editor: [`docs/fase4-play-pass.md`](docs/fase4-play-pass.md).

Ver [`docs/fase5-plan.md`](docs/fase5-plan.md) para lo que sigue (mini-grafo MLP,
pulido uGUI, especies Atta/Eciton y escala).

## Requisitos

- .NET SDK 8+ (el Core apunta a `netstandard2.1` para compatibilidad futura con Unity).
- (Futuro, Fase 4) Unity 2021+ para el proyecto gráfico.

## Estructura

```
AntSim.slnx
├─ src/Core/AntSim.Core          # núcleo headless determinista (netstandard2.1)
├─ src/Core/AntSim.Core.Tests    # tests xUnit
├─ src/Tools/AntSim.Cli          # CLI headless (microcosmos + hashes)
└─ docs/                         # arquitectura y especificaciones
```

## Uso rápido

```bash
# Tests
dotnet test AntSim.slnx

# Microcosmos determinista (dos ejecuciones con la misma semilla → salida idéntica)
dotnet run --project src/Tools/AntSim.Cli -- --mode micro --seed 42 --ticks 600 --grid 64

# Mundo completo de Fase 1 (2 colonias con hormigas, comida, cría y ColonyController)
dotnet run --project src/Tools/AntSim.Cli -- --mode world --seed 7 --ticks 1200 --grid 128 --colonies 2

# Neuroevolución (Fase 2): pool élite + fitness + cuarentena, con exportación
# e importación de cerebros (.antgenome)
dotnet run --project src/Tools/AntSim.Cli -- --mode evolve --seed 42 --ticks 900 --grid 96 --colonies 2 --export pool.antgenome
dotnet run --project src/Tools/AntSim.Cli -- --mode evolve --seed 1 --ticks 240 --grid 96 --colonies 1 --import pool.antgenome

# Pre-entrenamiento headless (Fase 3bis): arena realista (colonia completa, comida
# a ≥ 200 u sin rastro), currículo por horizonte temporal; exporta la población
# entrenada como .antgenome
dotnet run --project src/Tools/AntSim.Cli -- --mode pretrain --seed 4242 --pop 32 --export pretrained.antgenome

# Warm-start: continuar el entrenamiento desde un pool existente (clon+mutación
# hasta --pop, re-evaluado en la arena actual) en vez de genomas aleatorios;
# --band-min/--band-max re-bandan todas las etapas (p. ej. extender el anillo de
# forrajeo 200-350 u) sin romper la transferencia
dotnet run --project src/Tools/AntSim.Cli -- --mode pretrain --seed 4242 --pop 24 --warm-start pretrained.antgenome --band-min 200 --band-max 350 --export refined.antgenome

# Sembrar una partida con la población pre-entrenada (transferencia validada:
# pickups 0 → 4–7 frente a la élite aleatoria en 300 s)
dotnet run --project src/Tools/AntSim.Cli -- --mode evolve --seed 42 --ticks 9000 --colonies 1 --seed-pool pretrained.antgenome

# Persistencia (Fase 4): grabar partida con log de eventos y checkpoint a mitad
dotnet run --project src/Tools/AntSim.Cli -- --mode world --seed 42 --ticks 2400 --grid 128 \
    --save partida.antsave --save-tick 1200 --antlog partida.antlog

# Verificar: cargar el checkpoint y re-ejecutar; el hash final y cada evento
# deben coincidir byte a byte con el log (exit 0 = reproducción idéntica)
dotnet run --project src/Tools/AntSim.Cli -- --mode verify --ticks 1200 \
    --load partida.antsave --antlog partida.antlog
```

## Pipeline encadenado (scripts/pipeline.sh)

`scripts/pipeline.sh` encadena el flujo completo: pretrain en frío → warm-start
de refinado desde el pool en frío → revalidación multi-semilla con
`--seed-pool` (baseline vs pool en frío vs pool refinado), con tabla final de
métricas (pickups, descargas, eclosiones, hash, `unload1st`, `dropavg`,
`carryleg` por semilla) y resumen agregado.
Determinista; flags para `--pop`, `--gens` (por etapa), `--band-min/max`,
`--ticks`, `--seeds` y `--out`. El modo `--verify` salta el entrenamiento y
solo revalida una lista de pools (`--pools "baseline a.antgenome b.antgenome"`),
fallando (exit 1) si entre dos pools consecutivos desaparece `first-unload`,
`drop-avg` sube >10 % o `carry-leg` se encoge >20 % — detección de regresión
del relevo lista para CI:

```bash
bash scripts/pipeline.sh                         # defaults: pop 24, 10 gens/etapa, 5 semillas
bash scripts/pipeline.sh --gens 60 --band-max 350  # réplica del pool de referencia con banda extendida
bash scripts/pipeline.sh --pop 6 --gens 2 --seeds "42 7"  # humo rápido (~45 s)
bash scripts/pipeline.sh --verify --pools "artifacts/pretrain-warm2.antgenome" --seeds "42 7"  # regresión del relevo (exit ≠ 0 si falla)
```

## CI (`.github/workflows/ci.yml`)

| Check | Qué protege |
|---|---|
| `dotnet test` | la suite headless (228/228), incluido el pin del hash del stream canónico |
| `scripts/check-stream-fixture.sh` | determinismo: regenera el stream canónico y compara su hash fijado |
| `scripts/check-replay-command.sh` | la partida con plan de drops (3000 y 6000 ticks) reproduce sus hashes |
| `scripts/check-unity-compile.sh --selftest` | el analizador de logs de compilación (no necesita editor) |
| `scripts/check-unity-compile.sh` (job `unity-compile`) | **la capa de vista de Unity compila**: los MonoBehaviours no están en la suite headless, así que un `error CS` solo se veía al abrir el editor |

El job `unity-compile` está **dormido** hasta que existan la variable de
repositorio `UNITY_CI = true` y el secreto `UNITY_LICENSE` (o `UNITY_SERIAL`):
descargar Unity en cada push es caro y necesita licencia. El script sí corre en
local — encuentra el editor del Hub por la versión fijada en
`ProjectSettings/ProjectVersion.txt` (`--selftest` verifica su analizador, y
`--log FICHERO` analiza un log ya generado).

El **Play pass** en el editor —lo único que valida los MonoBehaviours EN VIVO—
también está cableado: `scripts/playpass-live.sh` conduce el editor abierto
(recompila, configura el presenter, entra en Play, muestrea cada segundo hasta el
último tick y para) y verifica el mundo (aparición, escala, movimiento), las
tarjetas y el semáforo de relevo por colonia, el dedupe de toasts del canal D y
la consola sin `stream falló` — los bloques 3 y 5 del checklist. Después entra en
Play otra vez para el **bloque 4** y dispara las cuatro acciones de interacción
(seleccionar, `D`/click/`Z`, reiniciar con plan, `J`/Alt+click) por los puntos de
entrada sin dispositivo de los handlers: `Input` no es inyectable desde el CLI,
pero el raycast sale de la cámara real, el plan es el real y el ancla es la que
el Core puso en la alerta. Desde la pasada de pulido (F5.1) verifica también el
**aspecto** midiendo PÍXELES: el tablero tiene que leerse como tierra (en partida
y al abrir la escena) y tienen que verse hormigas, más ningún texto del HUD fuera
de su panel. Ese bloque existe porque el defecto que reportó el jugador («el
terreno es blanco y no se ven hormigas») pasaba en verde **todas** las muestras de
estado. Falla con el primer invariante roto. No es un job de CI porque necesita un
editor interactivo con licencia; se corre a mano:

```bash
bash scripts/playpass-live.sh            # bloques 3, 4, 5 y aspecto, en vivo
bash scripts/playpass-live.sh --no-block4     # solo 3, 5 y aspecto (más rápido)
bash scripts/playpass-live.sh --analyze-visual   # solo el aspecto
bash scripts/playpass-live.sh --selftest # los analizadores, sin editor
```

## Garantía de determinismo

Misma semilla + mismos parámetros ⇒ misma simulación bit a bit (RNG propio
xoshiro256**, aritmética en orden fijo, floats por bits exactos, hashes de hito
por tick). Esta es la base de los checkpoints reproducibles y del modo
verificación de las fases posteriores.
