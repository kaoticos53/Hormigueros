# 🐜 Freebuff Ants (AntSim)

Simulación en tiempo real de hormigueros realistas con **neuroevolución continua**
(cerebros MLP → NEAT evolucionados por algoritmos genéticos), natalidad ligada a
recursos, múltiples especies y render Unity 2D → 3D. Núcleo .NET **headless y
determinista** desacoplado del motor gráfico.

Estado actual: **Fases 0–4 completadas** (núcleo determinista + mundo + neuroevolución
+ pre-entrenamiento headless con transferencia validada + **modo evolución jugable**:
stream JSONL de 5 canales hacia la vista, escena Unity bootstrapeada con HUD —
tarjetas de colonia con semáforo de relevo, toasts, inspector con linaje de cerebros —,
importación con cuarentena, intervención `--drop` determinista y replay bit a bit)
y Fase 5 en marcha con **dos especies cerradas** (Atta la cortadora, Eciton la
legionaria — incursión y asfixia económica calibradas).
Ver [`docs/arquitectura.md`](docs/arquitectura.md) para el plan por fases completo,
[`docs/especificaciones.md`](docs/especificaciones.md) para los contratos cerrados
y [`docs/fase3ter-resumen.md`](docs/fase3ter-resumen.md) para el resumen de cierre,
[`docs/fase4-diseno-ux.md`](docs/fase4-diseno-ux.md) para el diseño de UX del modo evolución (Fase 4),
[`docs/fase4-hud-contrato.md`](docs/fase4-hud-contrato.md) para el contrato de datos del HUD (F4.2)
y [`docs/fase4-resumen.md`](docs/fase4-resumen.md) para el resumen de cierre de la Fase 4
(junto con [`docs/fase3ter-resumen.md`](docs/fase3ter-resumen.md): arranque en frío, salud del relevo y jerarquía de pools),
y [`docs/fase5-plan.md`](docs/fase5-plan.md) para el plan de la Fase 5 (realismo, especies y escala),
con [`docs/fase5-3-escala.md`](docs/fase5-3-escala.md) para el cierre de la escala (LOD de difusión e instancing, medidos).
Estado consolidado por fases: [`docs/estado-proyecto.md`](docs/estado-proyecto.md).

## Notas de versión

### F5.3 — Escala: el mundo deja de costar lo mismo (rodajas 1–3ter, sin tag todavía)

La sub-fase que hace sostenible todo lo anterior: el mismo mundo, con menos
trabajo por tick y por frame. Ninguna de las rodajas cambia el mundo salvo donde
se dice:

- **LOD de difusión EXACTO (rodajas 3 y 3bis)**: la difusión nunca llena una
  celda nula (régimen original del proyecto), así que las nulas no pueden cambiar
  y visitarlas era trabajo puro. `PheromoneLayer` mantiene el soporte por bloques
  de **8×8** (el lado se fija por capa) y recorre solo esos. La equivalencia se
  demuestra **celda a celda** contra una capa de referencia a grid completo, y los
  6 pines de hash **no se movieron**: el mundo es el mismo byte a byte. Medido:
  7.8–8.7 % del grid visitado en un mundo forrajeado. El lado del bloque no es un
  número a ojo: `--mode scale --lod-blocks` publica el equilibrio entre celdas
  ahorradas y coste de reconstruir la lista (`docs/fase5-3-escala.md` §4.6).
- **GPU instancing (rodaja 3)**: el presenter agrupa por material y envía lotes
  de hasta 1023 instancias en vez de una llamada por hormiga/ítem/mordisco, con
  caída al camino por objeto si la plataforma no lo soporta. Contador en vivo
  (`LastDrawCalls`, presupuesto 32).
- **Medidor de escala** (`--mode scale`): ms/tick, porcentaje del presupuesto de
  un frame a 60 fps, techo de velocidad y trabajo de feromonas por número de
  colonias. En grid 256 con 6000 ticks: **1/2/4/8 colonias ⇒ 0.06/0.12/0.24/0.50
  ms por tick**, el 3 % del frame a 60 fps con 8 colonias.
- **Medidor de la vista** (`scripts/perf-scene.sh`): monta la escena con **8
  colonias en pantalla** (4 vistas × 2) en grid 256, entra en Play y mide
  frames/s y `LastDrawCalls` por vista. Dos modos: en batch da **0.51 ms por
  frame** (el coste de producir frames) y con `--live` —el Play pass real, editor
  con ventana, bucle y presentación de verdad— **2.44 ms por frame** (15 % del
  presupuesto a 60 fps), en ambos casos con **19 llamadas de dibujo, todas
  instanciadas** y 292 hormigas + 684 ítems en pantalla. Destapó un defecto real
  que dejaba el tablero sin hormigas (`DrawMeshInstanced` exige
  `enableInstancing` en el material).
- **Build de jugador (rodaja 3ter)**: `scripts/player-perf.sh` construye el player
  del multi-visor y lo mide con una sonda de runtime. El build entrega **59,99 fps
  con el refresco del monitor (59,997 Hz) y el vsync del proyecto aplicado** —el
  techo lo pone la pantalla, no el juego— y **la puerta de píxeles pasa en las
  cuatro vistas**. Y `scripts/player-perf.sh --cpu` apaga el vsync para medir lo que
  la pantalla tapa: **el COSTE por frame dentro del build** — **1,294 ms de CPU**
  (hilo principal 1,272 · hilo de render 0,277 · espera en Present 0,003) y
  **0,127 ms de GPU**, con un techo de **683,7 fps** sin vsync: **7,8 % del
  presupuesto de 60 fps**, calculado por un modelo puro (`FrameCost`) con 12 tests
  headless. El primer build destapó tres defectos reales: el juego **no
  compilaba como player** (`DragAndDrop`, API del editor sin guarda), faltaba el
  módulo `com.unity.modules.screencapture` (la sonda visual del pass no compilaba
  desde la poda de paquetes) y la puerta medía `AntMaterial` cuando el multi-visor
  pinta con `ColonyAntMaterials`. La puerta es ahora una sola implementación pura
  (`FrameGate`, 11 tests headless) compartida por el editor y el player.
- **Huella CHC (rodaja 2bis)**: cuarta capa repelente + tropotaxis en ratio — la
  señal negativa que faltaba, sin canal nuevo y sin invalidar los `.antgenome`.
- **Benchmark de políticas (rodaja 2ter)**: aleatoria vs scripted vs evolucionada
  en la misma arena ([`docs/politicas-benchmark.md`](docs/politicas-benchmark.md)).
- **Curva de aprendizaje y cobertura (rodaja 2quater)**: dos bloques nuevos en el
  canal C y panel por tarjeta en el HUD.

`512/512` tests, 6 pines de hash y el proyecto Unity compilando en batch sin
errores ni avisos. Criterio de la fase —«N colonias estables a 60 fps»— **medido y
cumplido en un build de jugador**. Detalle: [`docs/fase5-3-escala.md`](docs/fase5-3-escala.md).

### v0.7.0 — Fase 5.2c: NEAT, topologías que evolucionan (CERRADO)

El cerebro de la hormiga deja de ser una red fija: los 7 rodajas de F5.2c
(convirtieron el MLP 19·8·6 en grafos NEAT arbitrarios que MUTAN estructura
en la arena) están cerrados, con el criterio de fin del plan cumplido.

- **Genomas v2**: genes de nodo y conexión con innovations, binario
  `.antgenome` v2 canonizado (roundtrip bit a bit, acepta v1, re-innovación
  determinista al importar — doble importación ⇒ genomas idénticos).
- **Cerebro de grafo**: `NeatBrain` con orden topológico Kahn y paridad
  BIT A BIT con el MLP denso (la conversión no pierde nada: transferencia
  warm-v2 con paridad en la generación 1).
- **Operadores estructurales**: add-node (split que conserva la función),
  add-conn (jamás ciclos ni duplicados), toggle (poda que no huerfana
  outputs), crossover align-by-innovation (el circuito del mejor padre
  sobrevive y se evalúa).
- **Especiation con parentesco**: el hallazgo medido — el δ estructural es
  ~50× más pequeño que el ruido de pesos entre densos no emparentados, así
  que la agrupación mide LINAJE (δ < 0.05 = parientesco; si no, funda
  especie). 200 generaciones sin colapso, cuotas sharing por especie.
- **Meritocracia de arena medida**: con señal comparable, el tope cae en
  gen 1–16 y la media del élite sube +29 % en 3 semillas — el pool NEAT
  refina al patrón oro en vez de degradarlo.
- **El mundo siembra v2**: `--seed-pool` acepta v1 y v2, las fundadoras
  portan cerebros de grafo, y `--mode pretrain --neat` entrena y exporta.
- **El grafo se ve**: canal F emite la topología (campo `graph` n/h/c) y
  el inspector muestra el linaje con las formas de los cerebros.
- **6º pin de CI** (`check-neat-command.sh`, hash `021ed04f…`): la partida
  de invasión lasius+eciton con pool NEAT propio — el cierre del plan §3.

**Suite 463/463 tests, 6 pins de hash verificados en Windows y Linux.**

### v0.6.1 — CI verde cross-platform: determinismo canónico reparado

La CI de master llevaba rota desde el 12 de septiembre mientras la suite local
(Windows) estaba verde. La investigación, reproducida en WSL/Ubuntu, destapó
**tres causas apiladas**, cada una enmascarada por la siguiente
([detalle en `docs/estado-proyecto.md`](docs/estado-proyecto.md)):

- **CanonMath — matemática canónica multiplataforma (la profunda).**
  `MathF.Tanh/Exp/Log/Sin/Cos` NO están especificadas bit a bit en .NET: cada
  runtime delega en la libm del sistema (UCRT en Windows, glibc en Linux) y
  difieren en 1 ULP. Con sensores alimentando el cerebro, ese ULP cambia la
  decisión de una hormiga y el mundo diverge: el hash canónico de Windows era
  imposible de reproducir en Linux DESDE EL TICK 1. Fix: `Sim/CanonMath`
  implementa las cinco trascendentes con series en doble precisión usando solo
  operaciones IEEE básicas (2^k por manipulación de bits, reducción de
  argumento exacta, el núcleo exp de tanh queda en double para esquivar la
  cancelación `1−t`). `Sqrt` se queda en `MathF` (IEEE exacto). Consecuencia
  documentada: los 5 hashes de los pins CI se regeneraron — un cambio de mundo
  intencional, el nuevo patrón oro.
- **Cabecera del stream**: `GameScenario.Run` recortaba la cabecera con
  `sb.Length -= 3`, asumiendo el `\r\n` de Windows; en Linux se comía una
  llave de más y cada juego con canal opt-in (E/E-múltiple/F) emitía JSONL
  inválido. Ahora recorta saltos de línea reales + exactamente una llave,
  idéntico en ambas plataformas.
- **Exec-bit en los selftests**: los scripts viajan como `100644` en el índice
  (el checkout en Windows pierde el bit de ejecución) y CI los invoca vía
  `bash scripts/…`, que funciona — pero los selftests se re-invocaban a sí
  mismos con `"$0" --fake-log` (ejecución directa) → exit 126. Fix:
  `bash "$0"` en `check-unity-compile.sh` y `unity-cli.sh`.

También de paso: `RepoPathResolver` era asimétrico (la variante de publicación
por carpeta solo existía en Windows) y su test de `.exe` está portado a ambas
plataformas. **Suite 338/338 en Windows Y Linux; los 5 pins de hash pasan en
ambas.** CI verde en `a2a35ec` — primera corrida verde de master desde el
empuje de la cola de Fase 4.

### v0.6.0 — Fase 5: Eciton, la legionaria (CERRADO)

La segunda especie de Fase 5, cerrada slice a slice con sondas borradas y
datos acumulados en [`docs/fase5-2b-eciton.md`](docs/fase5-2b-eciton.md):

- **F5.2b.1 — el cuerpo de combate**: `ContactRadius`/`StrikeDamage`/
  `StealPerStrike` en el descriptor de especie (solo Eciton ≠ 0 = pacífico),
  golpe determinista sin RNG nuevo con reparto de botín (`LoadIsLoot`) que
  fluye por el `Unload` de siempre, eventos 17–19 (Strike/RaidInflow/
  StockRobbed) en canal B, `DeathCause.Combat` y alarma inyectada en la capa
  de la víctima; `.antsave` v4.
- **Sondas de transferencia**: warm-v2 en cuerpo Eciton forrajea MEJOR que
  su control Lasius (13 descargas vs 11) y reacciona al sensor de presa sin
  entrenamiento (strikes 2→18, ×9) — el genoma sigue portable en la segunda
  especie tras Atta.
- **F5.2b.2 — el sensor de presa**: canal 12 `ProxFront` reconvertido a
  «hormiga enemiga más cercana» con gating por especie + mundo multi-colonia;
  dos invariancias de hash (mundo clásico multi-colonia y mundo unicolonia
  Eciton, byte a byte).
- **F5.2b.3 — contratos**: bloque `raids` en canal C con contadores
  acumulados entre lecturas (la ventana de 1 s no veía golpes tan raros —
  descubierto por el propio test), parser Unity tolerante y causa «combate»
  en la tarjeta del inspector.
- **F5.2b.4 — calibración V6**: tres sondas demostraron que escalar el robo
  solo NO mata (golpes tardíos con despensa vacía; un robo carga al
  saqueador y le prohíbe volver a golpear) y que la palanca real era el
  radio de contacto: `ContactRadius 6→40`, `StrikeDamage 0.35→2.5`,
  `StealPerStrike 0.30→5.0`. Resultado: 2/5 semillas con extinción
  ACCELERADA (hasta 1 669 ticks antes bajo 15.3 ep saqueados), asfixia
  económica en vez de carnaza — y el botín ya PAGA (saqueador +1 113 ticks
  vs su gemela sin contacto). Los CombatTests pasaron a escenario remoto
  (>400 u de ambos nidos) para sobrevivir a futuros radios.
- **F5.2b.5 — pin y tarjeta**: quinto pin de hash CI
  (`check-invasion-command.sh`, partida de invasión canónica con Eciton
  sembrada — el primer pin cuyo mundo depende del combate) y línea
  `raids N · M al nido` en la tarjeta del multi-visor, solo para la colonia
  beligerante. Criterio de cierre §9 verificado 4/4.
- CI con **5 pins de hash**, suite **315/315** verde, Unity compila en
  batch con 0 error CS.

### v0.5.0 — Fase 5 (mitad): especies, multi-visor y la cortadora

Dos hitos de contenido sobre el bucle jugable de v0.4.0, ambos con su
criterio de cierre verificado en vivo y headless:

- **F5.0 — el cerebro visible**: canal F de activaciones opt-in para la
  hormiga inspeccionada (invariante de hash garantizado) y el mini-grafo
  MLP renderizado headless; la neuroevolución se VE, no se presume.
- **F5.1 / F5.1bis — pulido uGUI y multi-visor**: HUD por-elemento con
  click exacto por toast, barras de stock, botones nativos, feromonas por
  RenderTexture, diálogo de importación con cuarentena — y el **multi-visor**
  de hasta 4 simulaciones en paralelo (una semilla y un pool por vista),
  verificado a 2× medidos (60 tps) con semáforo de relevo por vista. El
  escenario también sobrevive a un clon limpio de git (sonda con
  `PumpOneTick` como respaldo del bucle batch).
- **F5.2a — Atta, la cortadora (CERRADO)**: primera especie con economía
  propia, y el genoma sigue portable (el corte ES `Interact`):
  - ítems compuestos (`CutsLeft`): la hoja aguanta N cortes, spawn con
    `--leaf-fraction`, hash invariante sin hojas;
  - el **hongo**: segunda reserva (`Colony.Fungus`) que recibe la descarga
    de fragmentos y digiere proporcionalmente al llenado hacia el inflow
    existente — la demografía de F1–F3 lee la misma señal;
  - `--species atta,lasius` en el CLI, canales A/C con `cuts`/`fungus`/
    `cutters`, cuarto pin de hash CI (la partida Atta canónica) y tarjeta
    de vista con barra de hongo y cortes acumulados;
  - **criterio biológico SUPERADO**: cut → transport → `FungusFed` →
    digestión → eclosión con la reserva fundadora agotada — la colonia
    sostiene cría ALIMENTADA SOLO por el hongo (8 eclosiones, 6 698 ticks
    de digestión).
- **F5.2b — Eciton, la legionaria (diseño cerrado, primera rodaja HECHA)**:
  combate de incursión en el mundo (golpe/robo/botín como carga con el
  `Unload` de siempre, eventos 17–19 en canal B, `DeathCause.Combat`,
  alarma inyectada en la capa de la víctima), transferencia warm-v2 →
  cuerpo Eciton validada por sonda. Sensor dirigido y balance: en curso.
- CI con pins de hash (stream canónico, replay 3000/6000 con drops, partida
  Atta) y compilación batch de Unity. *(Cuando se escribió esta nota eran 4
  pines y 305 tests; hoy son **6 pines** —+invasión, +NEAT— y **496/496 tests**.)*
- Detalles: [`docs/fase5-plan.md`](docs/fase5-plan.md) ·
  [`docs/fase5-2a-atta.md`](docs/fase5-2a-atta.md) ·
  [`docs/fase5-2b-eciton.md`](docs/fase5-2b-eciton.md) ·
  [`docs/estado-proyecto.md`](docs/estado-proyecto.md).

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
  puros compilados en la suite headless y `276/276` tests verdes.
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

## Jugar en Unity (modo visual)

El juego tiene un modo visual completo: hormigas animadas, HUD con tarjetas de colonia, semáforo de relevo, inspector de cerebros y drag & drop de pools.

```bash
# Lanzar Unity con la escena lista para Play (los ajustes se inyectan
# en la escena ANTES de entrar en Play: pool, grid, colonias, ticks,
# especie, velocidad y frame-every, con geometria acorde al grid):
scripts\play-game.bat                                   # Windows (CMD)
.\scripts\play-game.ps1 --grid 256 --species lasius,eciton
bash scripts/play-game.sh                                # Linux/macOS

# --dry-run imprime los ajustes y el comando sin abrir Unity.

# O abrir el proyecto manualmente en Unity 6000.x:
#   1. File - Open Project - selecciona src/App/AntSim.Unity
#   2. Menu AntSim - Jugar (o AntSim - Crear escena de juego + Play)
```

**Controles en juego:**
| Tecla | Acción |
|-------|--------|
| Click | Inspeccionar hormiga (ver cerebro MLP/NEAT) |
| D     | Modo marcar drops (click en el suelo, Z deshace) |
| F     | Rotar capa de feromonas (home → food → alarm) |
| G     | Rotar colonia en la capa de feromonas |
| I     | Abrir diálogo de importar pool (.antgenome) |
| J     | Saltar cámara a la alerta seleccionada |
| Espacio | Pausar/reanudar |
| ⬇     | Arrastrar .antgenome desde el explorador para importar |

**Configurar desde el inspector de SimPresenterBehaviour:**
- `SeedPoolPath` = `artifacts/pretrain-warm-v2.antgenome` (o `pretrain-neat.antgenome`)
- `Species` = `lasius` (o `lasius,eciton` para invasión)
- `Grid` = 96 (grid 96² = 768 u) o 256 (grid 256² = 2048 u)
- `Ticks` = 7200 (2 horas de juego) o más
- `Speed` = 3 (velocidad del presenter, 3× real)
- `InspectId` = id de la hormiga a inspeccionar

**Pools disponibles:**
- `pretrain-warm-v2` — pool de referencia, buen relevo y forrajeo
- `pretrain-neat` — pool NEAT con grafos evolutivos
- `pretrain-warm3-v2` — especialista en relevo profundo
- `pretrain-warm-5` — especialista en forrajeo lejano

**Multi-visor (comparar pools):**
- Menú AntSim → Multi-visor (4 simulaciones en paralelo)
- Cada vista tiene su propio stream, pool y relay light

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
| `dotnet test` | la suite headless (463/463), incluido el pin del hash del stream canónico |
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
