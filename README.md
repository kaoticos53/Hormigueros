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

<!-- cifras-historicas: notas de versión — cada cifra es la de SU cierre, no la de hoy. La comprobación de cifras del repo (`scripts/check-doc-figures.sh`) ignora lo que va entre estas dos marcas. -->

### v0.8.0 — Fase 5.3: escala, el mundo deja de costar lo mismo (CERRADO)

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
- **CI del cierre**: el job de Unity —el único que compila los MonoBehaviours que
  la suite headless no ve— se puede levantar **a mano** con `workflow_dispatch`
  (`unity: cache` ejercita el paso del caché en 18 s, sin licencia; `unity: full`
  corre el job entero — y al ejercitarlo por fin con un secreto de licencia se
  midió que el camino no cierra: una licencia **Personal** no se activa en un
  runner alojado. La capa de vista queda cubierta por la puerta local y su
  pre-push, y el job admite `UNITY_RUNNER` para un self-hosted), las cuatro
  acciones del workflow van en v5 (Node 24) y la
  **clave del caché de descargas deriva de `ProjectVersion.txt`** en vez de estar
  escrita a mano, así que no puede quedar obsoleta al subir de versión de Unity.

`536/536` tests, 6 pines de hash y el proyecto Unity compilando en batch sin
errores ni avisos. Criterio de la fase —«N colonias estables a 60 fps»— **medido y
cumplido en un build de jugador**, con el coste por frame del build medido aparte
(**1,294 ms de CPU**, 7,8 % del presupuesto) y el coste del **sistema completo**
(añadiendo la simulación del CLI, que corre en otro proceso): **0,201 ms/tick** →
**2,485 núcleos** al reloj del juego, con la vista en 1,116 ms/frame. Y **cuánto juego
cabe**: el barrido con 1, 2, 4 y 8 colonias por vista da **1,927 · 2,496 · 3,443 · 5,285
núcleos** —doblar colonias no dobla el coste: hay **0,1203 ms/tick fijos** y 0,00983 por
colonia, y la curva por colonia (0,482 → 0,165) es la **amortización** de ese fijo
(0,1187 + 1,507/n)—, así que los 2,5 núcleos del criterio son **8 colonias** y 32 colonias
(1 075 hormigas en pantalla) siguen a 609 fps con la puerta de píxeles verde. Detalle:
[`docs/fase5-3-escala.md`](docs/fase5-3-escala.md) §4.7–§4.10. **Cierre de la
sub-fase: tag `v0.8.0`.**

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

**Suite 379/379 tests, 6 pins de hash verificados en Windows y Linux.**

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
  pines y 305 tests; hoy son **6 pines** —+invasión, +NEAT— y **536/536 tests**.)*
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

<!-- /cifras-historicas -->

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

# Entrenamiento con OBSTÁCULOS (F7): el terreno (laberinto del juego) empieza
# abierto y sube solo cuando la colonia cierra descargas de verdad; el cerebro
# crece 8→16→24 si se estanca en el terreno completo. Sin gráficos, cientos de
# generaciones en minutos, y el juego arranca con este pool por defecto.
bash scripts/pretrain-maze.sh                       # 600 gens → artifacts/pretrain-maze.antgenome
python scripts/pretrain-maze-transfer.py            # transferencia en la partida canónica (--maze)
python scripts/pretrain-maze-transfer.py --no-maze  # y la regresión en campo abierto

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

El juego tiene un modo visual completo: laberinto con paredes dibujadas desde la semilla, hormigas animadas, HUD con tarjetas de colonia, semáforo de relevo, inspector de cerebros y drag & drop de pools.

La partida arranca a **120×** (100 % de la base del control de velocidad, rango 10×–160×) y dura **216 000 ticks = 2 h de simulación**. No hay botón de «siguiente generación»: la evolución ocurre sola, porque los nacimientos salen del torneo sobre la élite ordenada por fitness de por vida y cada muerte la actualiza; al terminar el horizonte la élite se guarda en `artifacts/evolved-<semilla>.antgenome` (`ExportElite` del presenter).

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
- `Ticks` = 216000 (2 h de simulación; 30 min de reloj a 120×)
- `Speed` = 120 (100 % de la base; 10×–160× en el control)
- `Maze` = laberinto con bucles generado de la semilla (sin él, el tablero es el plano clásico)
- `ExportElite` = guarda la élite evolucionada al acabar la partida
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

## Repartir el árbol en commits (`scripts/commit-layers.sh`)

El árbol casi nunca tiene una sola cosa pendiente: los assets que reescribió el
editor, los scripts de una hebra, el modelo de otra, los documentos y el volcado
del play pass. Repartir eso a mano se hace mal —y se hace tarde— cuando hay
veinticinco rutas de seis naturalezas. `scripts/commit-layers.sh` clasifica cada
ruta en una **capa** por su sitio en el repo y commitea capa por capa, en el
orden en que unas se apoyan en otras (reglas de forma → herramientas → modelo →
vista → assets → documentos → evidencia). Sin argumentos **no escribe nada**:
primero el plan, con el asunto propuesto para cada commit y sus rutas.

Las tres cosas que ensucian un árbol están tratadas: lo que ya está **preparado
en el índice** no se mezcla con lo que se reparte (el reparto se planta y dice
qué hay, o `--include-index` lo commitea primero tal cual se preparó), lo que
está **en el árbol** va cada cosa a su capa, y el **churn de assets** lo decide
el verificador de assets de siempre: la capa de assets se planta si el editor
sólo reescribió, y `--discard-churn` deja que el descarte ocurra antes del
commit en vez de reimplementar su veredicto. `--json` guarda el plan con el hash
de cada ruta y `--apply-plan` lo aplica sólo si nada cambió desde que se revisó;
tras cada commit comprueba que contiene exactamente lo previsto. Es una
herramienta de trabajo, no una puerta de CI.

```bash
bash scripts/commit-layers.sh                  # el plan (no toca nada)
bash scripts/commit-layers.sh --apply          # commitea por capas
bash scripts/commit-layers.sh --list           # las capas y sus reglas
bash scripts/commit-layers.sh --selftest       # el reparto entero, en un repo de usar y tirar
```

## CI (`.github/workflows/ci.yml`)

| Check | Qué protege |
|---|---|
| `dotnet test` | la suite headless (711/711), incluido el pin del hash del stream canónico y los **contratos de lo generado**: los ASSETS (el `.ttf`, las mallas, y los materiales y texturas versionados contra sus modelos: editar el modelo sin regenerar falla aquí) y las **ESCENAS** (qué entrada y qué configuración produjo cada `.unity` del repo, contrastado contra `GeneratedSceneCatalog` y contra las constantes de sus productores: regenerar deja de depender de recordar los valores del Play pass y del medidor) |
| `scripts/check-stream-fixture.sh` | determinismo: regenera el stream canónico y compara su hash fijado |
| `scripts/check-replay-command.sh` | la partida con plan de drops (3000 y 6000 ticks) reproduce sus hashes |
| `scripts/check-atta-command.sh` | la partida canónica de la cortadora (F5.2a) |
| `scripts/check-invasion-command.sh` | la partida canónica del saqueo (F5.2b) |
| `scripts/check-neat-command.sh` | la invasión con pool NEAT v2 (cierre F5.2c) |
| `scripts/system-sweep.sh --selftest` | el ajuste fijo/marginal del barrido de costes (§4.10), sin editor |
| `scripts/check-doc-figures.sh` | **las cifras de los documentos no derivan**: toda afirmación de pines o de tests tiene que coincidir con el repo (6 pines, 711 tests), y un documento nuevo con cifras sin clasificar falla en vez de pasar desapercibido |
| `scripts/check-unity-assets.sh` | **el editor no mete ruido al repo**: abrir el proyecto o construir un player reescribe la escena, los materiales y los ajustes de URP sin cambiar lo que significan (orden de los documentos, numeración de las anclas `&N`, sitio del bloque `AssetVersion`). El verificador lo decide por equivalencia estructural —mismos objetos, mismos campos y el mismo cableado— y falla si algún commit del push metió un asset así, con la lista exacta de ficheros a descartar. `--selftest` prueba el analizador y `--list` publica su alcance. `--discard` da el paso siguiente y ACTÚA, por los dos caminos que hacen falta para que el diff contra HEAD sea lo que cambió: restaura las rutas que certifica idénticas (el churn puro) y a las que **cambian de verdad** les quita la forma —anclas de sesión, orden, blancos— reescribiéndolas en canónico **sin tocar el contenido**, que es exactamente lo que hace el generador al cerrar una escritura; funciona porque el ancla canónica es el hash del contenido, así que un documento intacto vuelve a ser el mismo byte que en HEAD (medido en `Game.unity`, 425 documentos: una pasada que re-ancla todo y cambia UNA línea escalar pasa de 10 669 líneas de diff a 2). No toca las que canonicalizan —son las que quitan la trampa— y sólo escribe si la forma canónica pasa el `compare` (si no significa lo mismo, se planta y lo dice); después vuelve a clasificar y a mirar el índice para confirmarlo. `--dry-run` mide lo mismo sin tocar nada. El MISMO chequeo es la **puerta 1** de `.githooks/pre-push`, con el rango sacado de las refs que el push manda |
| `scripts/check-unity-compile.sh --selftest` | el analizador de logs de compilación (no necesita editor) |
| `scripts/check-unity-references.sh` | **un commit no deja un asset sin su dato**: un editor puede destruir una referencia sin cambiar nada más y sin dejar error —medido dos veces: los materiales de feromonas que quedaron con `_BaseMap`/`_MainTex` en `fileID: 0` (commiteados en `d4aebc3`, el material de reposo pintado BLANCO OPACO sobre el tablero) y `PheromoneMat.mat` al cerrarse el editor el 2026-10-08—. El YAML sigue siendo válido, así que nada lo veía hasta que una prueba se topaba con la pérdida. `--range A..B` compara cada commit con SU PADRE —lo que empuja un push son los commits, no el árbol— y nombra el slot y la guía perdidos; sin argumentos comprueba el árbol contra HEAD, `--watch` espera al cierre del editor y `--selftest` verifica los dos detectores y el modo del push sin repo. Un borrado deliberado de un objeto con su propia guía (p. ej. el botón «Sig. Gen» de F6, que se llevó consigo su `Text`) se declara CON MOTIVO en `scripts/unity-reference-allowlist.txt`, con alcance ruta+guía. Es la **puerta 2** de `.githooks/pre-push` |
| `.githooks/pre-push --selftest` | el veredicto de las **tres puertas locales**, en su orden: el churn de assets y las referencias perdidas (sin editor) y los errores CS (con el editor). Solo un rojo de verdad bloquea —churn detectado, una referencia perdida o `error CS`—; «no pude comprobar» avisa y deja pasar. Cubre también la derivación de rangos desde las refs del push, incluida la **ref nueva** (arranca donde la rama diverge de lo publicado, no en `HEAD~1`), y que el rango llegue a las dos puertas que escanean commits |
| `scripts/check-unity-compile.sh` (job `unity-compile`) | **la capa de vista de Unity compila**: los MonoBehaviours no están en la suite headless, así que un `error CS` solo se veía al abrir el editor. El job está dormido y con licencia Personal no puede encenderse en un runner alojado, así que la cobertura diaria es ese mismo script en local, vía `.githooks/pre-push` |

El job `unity-compile` está **dormido**: descargar Unity en cada push es caro y
necesita licencia. Tiene dos puertas:

- **a mano, UNA pasada** — `workflow_dispatch` (Actions → CI → «Run workflow»),
  con el input `unity`:
  - `unity: cache` → ejercita **solo el paso del caché** `actions/cache@v5`
    (segundos: sin licencia, sin descargar el editor y **saltando el job `test`**,
    que es el caro). Usa una clave de sonda (`<versión>-sonda`) para no ocupar la
    clave real, que solo se toca en `full`. La clave **deriva de la versión que
    fija `src/App/AntSim.Unity/ProjectSettings/ProjectVersion.txt`**, no de una
    constante escrita a mano: al subir de versión de Unity cambia sola y no
    puede quedar restaurando la caché de la versión anterior.
  - `unity: full` → el job completo: instala ~5 GB de editor, activa licencia,
    compila y devuelve la licencia (en el runner alojado; ver la licencia abajo).
  - `unity: none` (por defecto) → el dispatch corre solo el job `test`.
- **permanente** — la variable de repositorio `UNITY_CI = true`: el job corre en
  cada push y PR, siempre con alcance `full`.
- **en tu máquina** — la variable `UNITY_RUNNER` (p. ej. `self-hosted`) decide
  DÓNDE corre el job: con ella, corre en esa máquina —la que ya tiene editor y
  licencia del Hub— y se salta instalación, librerías, activación, preflight y
  devolución de licencia, compilando con el editor que ya está. Es la única vía a
  una puerta **permanente** en CI sin pagar asiento (hoy sin ejercitar: no hay
  runner registrado).

```bash
gh workflow run ci.yml -f unity=cache   # ejercita el paso del caché (sin licencia)
gh workflow run ci.yml -f unity=full    # compila el proyecto Unity entero
```

El alcance `full` **en el runner alojado** necesita UN secreto de licencia en
Settings → Secrets and variables → Actions: `UNITY_LICENSE_SERIAL` (serial
legacy Pro/Plus) o `UNITY_LICENSE_FILE_BASE64` (el `.ulf` de un asiento
Enterprise/Industry). Sin ninguno de los dos el run falla en ~2 s, a propósito,
antes de descargar nada.

**Una licencia Personal NO se puede activar en un runner alojado**, y no es una
limitación de este workflow: es de Unity. Medido el 2026-09-20 con dos pruebas
independientes: `unity license activate --personal --accept-eula` responde «Sign
in with unity auth login» (y en un runner efímero no hay sesión que valga), y el
`UnityEntitlementLicense.xml` que el Hub deja en `%LOCALAPPDATA%\Unity\licenses\`
no es un ULF —el cargador lo rechaza por el namespace de su firma— y además lleva
cinco identificadores `Legacy.MachineBinding1..5` (el hash de la máquina y su
MAC): está atado a ESA máquina, así que copiarlo no activaría nada ni con el
formato correcto. El detalle completo está en el comentario del job. Quien tenga
Personal cubre la capa de vista por la **puerta local** o apuntando el job a un
self-hosted con `UNITY_RUNNER`.

El dispatch se ve desde `master`: hasta que esta versión del workflow no esté en
la rama por defecto, ni el botón de la UI ni `gh workflow run` lo conocen.

La **puerta local** es la que HOY cubre el riesgo sin infraestructura, y es el
MISMO script que corre el job: `bash scripts/check-unity-compile.sh` compila en
batch con el editor del Hub (lo encuentra por la versión fijada en
`ProjectSettings/ProjectVersion.txt`) y tarda **8 s** medidos con la Library
caliente (`--selftest` verifica su analizador, `--log FICHERO` analiza un log ya
generado). En Windows el mismo comando es `scripts/check-unity.bat`, y para que
no dependa de acordarse, `.githooks/pre-push` lo corre antes de cada push:
**bloquea solo con errores CS de verdad** (exit 1); editor ausente, sin licencia
o instancia que no llegó a compilar AVISAN y dejan pasar, porque «no medido» no
es «roto».

Ese hook tiene **tres puertas**, y van en este orden porque las dos primeras no
necesitan el editor: (1) el **churn de assets** —`scripts/check-unity-assets.sh
--churn`—, (2) las **referencias perdidas** —`scripts/check-unity-references.sh
--range`, que compara cada commit con su padre— sobre el rango de commits que el
push manda, así que también protegen en una máquina sin Unity instalado, y (3) la
compilación de la capa de vista. Las tres comparten la misma decisión asimétrica:
solo el exit 1 bloquea, el resto avisa.

```bash
bash scripts/install-hooks.sh     # activa el pre-push del repo (idempotente)
scripts/check-unity.bat           # (Windows) la misma comprobación a mano
```

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
de su panel. Y mide también hacia dónde MIRAN: la caja del actor tiene el largo en
Y y la hormiga mira hacia +Y, así que el giro del presenter tiene que llevar ese
eje al rumbo del stream (`90° − rumbo`); con el giro a 90° el bicho anda de lado y
**ningún check de estado lo ve** —lo destapó el jugador, y ahora lo mide
`scripts/unity/ProbeActorOrientation.cs` en ocho rumbos, con la matemática de
Unity y la rotación del propio presenter—. El mismo paso mide las otras
siluetas: el arte de ítems y hojas es un sólido PLANO y con `Quaternion.identity`
se dibujaba **de canto** —desde la cámara de arriba se veía un rectángulo, y la
hoja llevaba su largo de 1,24 u al eje vertical con el `1,1` de la escala en el
grosor—, así que las tres usan el mismo ayudante de la hormiga (`FlatRotation` =
`ActorRotation(π/2)`, `FlatScale` = 1:1 en el plano y el grosor a la mitad) y el
probe exige además que la configuración vieja falle, para que la comprobación no
sea decorativa. Ese mismo paso comprueba que las **muescas de una hoja comen del
borde**: el limbo es una elipse (0,30 × 0,62 s), así que repartirlas con un radio
único (0,55 s) las dejaba fuera por los costados y dentro por las puntas —ahora
caen sobre la elipse, y el círculo viejo tiene que fallar—.

La matriz correcta, con todo, no prueba lo que se VE: entre la matriz del presenter
y el píxel que dibuja el jugador quedan el encuadre de la cámara (¿mira al nadir?
¿su arriba es el norte de pantalla?), la proyección, el rasterizado y el aliasing, y
un giro se cuela justo ahí. Por eso el pass mide lo mismo **en PÍXELES**: la sonda
`scripts/unity/ProbeAntHeadingPixels.cs` renderiza la malla y el material reales con
un rumbo fijo —a la escala de la partida, unos 19 px de hormiga, y 6× más cerca—,
saca la silueta de la diferencia entre el frame con la hormiga y el mismo frame sin
ella, y mide su eje (y su morro a zoom, que es donde el reparto de área de los
extremos tiene margen: a 19 px deja 1 px y no decide); el giro viejo tiene que fallar
~90° o la comprobación no estaría mirando el render. La evidencia queda en
`artifacts/playpass-heading-pixels/` (una hormiga por rumbo con el rumbo esperado
pintado encima). Ese bloque existe porque el defecto que reportó el jugador («el
terreno es blanco y no se ven hormigas») pasaba en verde **todas** las muestras de
estado.

Y los **iconos del HUD son un paso del pass, no un informe**: la sonda recorta la
tarjeta del frame en vivo y `scripts/playpass-icons.py` clasifica cada glifo
contra su referencia rasterizada del `.ttf` al tamaño del run (correlación
normalizada, IoU de silueta y margen sobre el segundo glifo). Si algún icono no
se clasifica como el suyo —o si no se pudo medir, porque la sonda no guardó el
recorte o falta Pillow— el pass sale ≠ 0 con el glifo y sus números. Antes el
comparador se ejecutaba a mano y un icono roto pasaba en verde.

La **serie de descargas de la gráfica** también se mide ahí, y por el mismo
motivo: el modelo puro puede producir una textura perfecta que la tarjeta no
pinte. La sonda inyecta una serie conocida (ventanas de 1, 3, 5 y 7 descargas),
deja dibujar un frame y recorta la banda de la gráfica del frame real; el
analizador exige una barra por ventana en las columnas de SU muestra, la altura
creciente con la cuenta, todo nacido en la base (un PNG del revés no pasa) y
ningún píxel ámbar fuera de la banda. La serie de la partida se restaura en la
misma sonda, y la evidencia —el frame, la banda de cada tarjeta y una hoja de
medida— queda en `artifacts/playpass-sparkline-pixels/` para repetirla sin
editor con `--analyze-sparkline`.

El pass también cuida el ÁRBOL que deja. El generador cierra su escritura
(canonicaliza y recarga la escena), y el pase cierra la suya —el bloque 4 guarda
la escena con la configuración que se commitea— **desde dentro del editor**
(`scripts/unity/ProbeCanonicalizeAssets.cs`), no escribiéndola por detrás: con la
escena abierta, un cambio hecho desde fuera le levanta a Unity el modal «The open
scene(s) have been modified externally» y el editor deja de contestar a todos los
comandos: caducan a los 30 s y devuelven la salida **vacía** — con `unity status`
diciendo `ready` igual, porque ese `status` lo sirve el registro del CLI y no el
editor. Ese vacío NO es «la escena no tiene HUD», y el pass ya no los confunde:
clasifica cada respuesta (ok / error de la escena / mudo / bloqueado) y, si el
editor no atiende, sale con **rc 3 diciendo qué pasó, que no es la escena y el
remedio** (cerrar el diálogo —Reload o Ignore— y repetir; o mirar `Logs/Editor.log`
si la marca «Main thread operation timed out» está en su cola), en vez de morir
con el motivo en blanco. Y al terminar descarta el churn de re-serialización que haya quedado
—por el mismo verificador de assets que el reparto de commits, no por una copia
de su criterio— cuando esos assets no se van a commitear; `--no-discard-churn` lo
salta, que es lo que quieres si sí vas a commitearlos.

Y comprueba el supuesto en el que descansan todos los demás: que **el pase no
acumula estado**, o sea que una segunda pasada del mismo montaje deja el árbol
exactamente como lo dejó la primera. Toma una HUELLA —estado y contenido (`git
hash-object`) de cada fichero del alcance que difiere de HEAD: los volcados
`artifacts/playpass-*` y los documentos de asset del proyecto— y la guarda en
`artifacts/playpass-fingerprint.txt` junto al montaje (HEAD + semilla + grid +
ticks + velocidad + pool); la pasada siguiente calcula la suya y las compara, y si
difieren **dice qué fichero** (la línea vieja contra la nueva). Los volcados de
MEDIDA —muestras vivas, aspecto, bloque 4, el estado del mundo en marcha, los
recortes del HUD— cambian con el reloj por construcción, así que de ellos se compara
el nombre y el estado, no el contenido; el resto, incluida la evidencia en píxeles
del rumbo (un render fuera de pantalla con una malla y un giro fijos), tiene que
salir **byte a byte igual**.

Falla con el primer invariante roto. No es un job de CI porque necesita un
editor interactivo con licencia; se corre a mano:

```bash
bash scripts/playpass-live.sh            # bloques 3, 4, 5 + aspecto e iconos, en vivo
bash scripts/playpass-live.sh --no-block4     # solo 3, 5 y aspecto (más rápido)
bash scripts/playpass-live.sh --no-discard-churn  # deja el árbol como quedó (vas a commitear los assets)
bash scripts/playpass-live.sh --recanonicalize # reescribe lo que derive y sigue (árbol que no viene del generador)
bash scripts/playpass-live.sh --analyze-visual   # solo el aspecto
bash scripts/playpass-live.sh --analyze-icons   # solo los iconos (sobre la captura)
bash scripts/playpass-live.sh --analyze-sparkline # solo las barras de la gráfica
bash scripts/playpass-live.sh --selftest # los analizadores, sin editor
```

## Garantía de determinismo

Misma semilla + mismos parámetros ⇒ misma simulación bit a bit (RNG propio
xoshiro256**, aritmética en orden fijo, floats por bits exactos, hashes de hito
por tick). Esta es la base de los checkpoints reproducibles y del modo
verificación de las fases posteriores.
