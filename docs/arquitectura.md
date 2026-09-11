# 🐜 Freebuff Ants — Documento maestro de arquitectura y plan por fases

## 1. Visión

Aplicación para Windows (.NET/C#) que simula en tiempo real uno o más hormigueros
realistas. Cada hormiga tiene un **cerebro** (MLP → NEAT) evolucionado por
**algoritmos genéticos continuos**: los mejores genomas se usan al nacer y el
fitness de por vida alimenta el acervo. Un **pre-entrenamiento headless** produce
cerebros mínimamente competentes antes de la simulación visual. Múltiples
**especies** con parámetros naturales diferenciados. Render **Unity 2D** ahora,
**3D** en fases posteriores — el contrato Core⇄Vista hace ese cambio transparente.

**Sellos del proyecto**: determinismo bit-a-bit (misma semilla ⇒ misma
simulación, verificable); Core 100 % desacoplado de Unity; evolución infinita sin
tiempo de fin; natalidad ligada a recursos (pocos ⇒ pocas y débiles; muchos ⇒
muchas y fuertes); máximo 40 adultas por colonia.

## 2. Decisiones confirmadas

| Decisión | Elección |
|---|---|
| Motor | Unity 2D → 3D; núcleo .NET sin dependencia de Unity (headless) |
| IA | MLP + GA primero; NEAT como plug-in posterior (`IBrain`/`IGenome`) |
| Especies v1 | Lasius niger; Atta (cortadora) y Eciton (legionaria) en Fase 5 |
| Mundo | Continuo 2D, celdas de feromona 8 u, grid 512×512; varios nidos compitiendo |
| Ritmo | Tick fijo 30 Hz; render libre (60–144 Hz) con interpolación y retraso de 1 tick |
| Documentación | Español, en `docs/` del repo |

## 3. Arquitectura en tres capas

```
┌─────────────────────────────────────────────────────────────┐
│  CORE (classlib .NET, headless, determinista)               │
│  Mundo · Hormigas · IBrain · Feromonas · ColonyController   │
│  GA/NEAT · Telemetría · Checkpoints · Serialización canónica│
└──────────────▲───────────────────────────────▲──────────────┘
        Canales A/C/D (pull)      Canal B (eventos)   Comandos con tick
┌──────────────┴───────────────────────────────┴──────────────┐
│  PRESENTER (adaptador Unity, reemplazable 2D↔3D)             │
│  Interpolación · Pool/instancing · RenderTextures de feromona│
└──────────────────────────▲───────────────────────────────────┘
┌──────────────────────────┴───────────────────────────────────┐
│  VISTA (HUD/UI): tarjetas, gráficas, inspección, alertas,    │
│  biblioteca de cerebros · todo consumidor puro del contrato  │
└─────────────────────────────────────────────────────────────┘
```

**Canales de comunicación** (ningún tipo de Unity cruza al Core):

- **A — `SimSnapshot`** por tick: poses interpolables + `ColonyStat`.
- **B — `SimEvent`** discreto: nacimientos, muertes, pickups, eclosiones,
  `ColonyExtinct`, eventos de genoma (`GenomeImported/EnteredElite/Discarded`).
- **C/D — Telemetría y traza**: `MetricFrame` (1 s sim) por pull; traza por
  hormiga inspeccionada (canal D, observación pura).
- **Comandos de usuario con tick**: toda acción que toca el mundo (pausa, comida
  manual, importar, guardar) es un comando registrado en `.antlog`.

## 4. Estructura de soluciones

```
AntSim.slnx
├─ src/Core/AntSim.Core            // netstandard2.1: simulación, sin Unity  ✅ Fase 0
├─ src/Core/AntSim.Core.Tests      // xUnit: determinismo, gating, feromonas… ✅ Fase 0
├─ src/Tools/AntSim.Cli            // headless: entrenar, export, verify     ✅ Fase 0 (mín.)
├─ src/Ga/AntSim.Ga                // IGenome, pool élite                  (Fase 2)
├─ src/Ga/AntSim.Ga.Tests                                                    (Fase 2)
├─ src/App/AntSim.Unity            // proyecto Unity (Presenter + HUD)      (Fase 4)
└─ docs/                           // este maestro + especificaciones
```

## 5. Subsistemas del Core (especificaciones cerradas)

Detalles y fórmulas en [`especificaciones.md`](especificaciones.md).

| Subsistema | Contrato clave |
|---|---|
| Cerebro | `IBrain`: 19 canales `AntSensors` → 6 salidas `AntDecision`; validación en 4 etapas (sanitizar NaN, clamp, gating físico/químico, cooldowns) |
| Feromonas | Capas por colonia (Food/Home/Alarm + Territory F5); celdas 8 u; evaporación exponencial + difusión estable; sonda de 3 puntos bilineal; actualización por región activa |
| Demografía | `ColonyController`: `λ_eggs` con ρ_res por `T_runway`; vigor del recién nacido `v = g0·(0.55+0.45·n̄)`; canibalismo escalonado determinista por umbrales |
| Evolución | GA: pool élite (torneo + crossover uniforme + mutación σ), fitness de por vida; inmigración con cuarentena (ventana 120 s, fitness ≥ p50; modo diversidad si D < D_floor); diversidad v1 en espacio de pesos (sondas comportamentales y NEAT en F5) |
| Telemetría | `ColonyStat` (tick) + `MetricFrame` (1 s sim); contadores incrementales O(1); determinista |
| Persistencia | `.antsave` (mundo) · `.antlog` (eventos + comandos + hashes de hito) · `.antgenome` (cerebros MLP/NEAT) · `.antmetrics/.csv` · `.antevents.csv` · `.anttrace` |
| Determinismo | RNG propio por flujo serializado; floats por bits exactos; orden canónico; etapas en orden fijo |

## 6. Determinismo y reproducción (regla transversal)

1. Misma semilla + mismos comandos ⇒ mismo mundo, mismos eventos, mismas métricas.
2. **Se guarda la causa, no los efectos**: los checkpoints serializan todo estado
   mutado (incluidos EWMA, acumuladores de puesta/canibalismo, nutrición larvaria);
   eventos y cambios de grid se regeneran.
3. Hashes de hito cada 1024 ticks verifican reproducciones y exportaciones bit a bit.
4. Los cerebros (`.antgenome`) son portables entre especies (contrato de canales
   común); los números de innovación NEAT se re-innovan deterministamente al importar.

## 7. Plan por fases

### Fase 0 — Andamiaje ✅ (completada)
- Solución .NET, proyectos Core/Tests/Cli, CI (build + tests).
- RNG determinista (xoshiro256**), serialización canónica (bits exactos + SHA-256),
  contratos v1 (`AntSensors` 19 / `AntDecision` 6), `MlpBrain` determinista,
  validador de decisiones, capa de feromonas con versiones por tile.
- Microcosmos headless con hashes de hito (determinismo fin-a-fin verificado).
- **Exit**: 36 tests verdes; dos ejecuciones de la CLI con la misma semilla ⇒ salida idéntica.

### Fase 1 — Core de simulación ✅ (completada)
- Mundo continuo con hormigas (movimiento, energía, muerte por edad/hambre), ítems
  de comida con respawn y nidos; feromonas por colonia (Food/Home/Alarm) con
  sonda de 3 puntos y evaporación/difusión periódica.
- `IBrain` MLP conectado a la simulación; validación con sanitización/clamp y
  gating físico/químico (pickup/unload/depósito con carga).
- `ColonyController` completo: puesta con ρ_res por T_runway, vigor del recién
  nacido (fuerte/minim), cría huevo→larva→pupa→adulta con tope duro de 40
  adultas, y canibalismo escalonado por umbrales (oofagia → larvas débiles → pupas).
- Eventos del Canal B (nacimientos, muertes, pickups, unloads, puesta, eclosiones).
- Hashes de hito del mundo completo (`WorldSim.HashLine`); CLI `--mode world`.
- **Exit**: 54 tests verdes; dos ejecuciones del mundo con la misma semilla ⇒ salida idéntica.

### Fase 2 — Neuroevolución (GA) ✅ (completada)
- `IGenome` + `MlpGenome`: crossover uniforme, mutación gaussiana (σ 0.05),
  clonación y distancia de pesos.
- `GenomePool` por colonia: élite con reemplazo por fitness (los mejores al
  nacer), nacimiento por torneo binario, diversidad v1 en espacio de pesos.
- Fitness de por vida (pickups, descargas, supervivencia) alimenta el acervo al morir.
- **Cuarentena de inmigrantes**: genomas importados ocupan eclosiones dentro de
  una ventana de 120 s; entran si fitness ≥ p50 (o p25 + novedad si la diversidad
  cae bajo el suelo); nunca degradan el pool.
- Formato **`.antgenome` v1** canónico (magic, versiones, metadatos, pesos bit
  exactos, SHA-256) con exportación/importación; CLI `--mode evolve` con `--import/--export`.
- **Exit**: 66 tests verdes; evolución y exportación deterministas entre procesos
  (salida y archivo byte a byte idénticos); la inmigración no degrada el pool.

### Fase 3 — Pre-entrenamiento headless ✅ (completada)
- **`ArenaEvaluator`**: arena de evaluación determinista de un genoma sobre
  `WorldSim` (una hormiga, un ítem a distancia fija del nido, rastro de comida
  sembrado, sin respawn ni cría, recompensas re-equilibradas). Rumbo inicial
  aleatorio determinista y **varias pruebas por genoma** (suaviza los puntos
  fijos reactivos); detección de estancamiento por progreso (distancia máxima al
  nido) para no gastar ticks en genomas muertos.
- **`CurriculumTrainer`**: poblaciones, selección élite + torneo, crossover
  uniforme y mutación σ; **currículo por etapas** con criterio de competencia
  mínima (ida-vuelta con comida: fitness ≥ 8.5). Currículo por defecto
  CALIBRADO empíricamente a 12→25→40→60 u (las 4 etapas se superan en ~100 s
  con pop 32; el borrador inicial de 120/300 u era inalcanzable: la visión no
  llega y el rastro se evapora antes de volver).
- **Integración**: `GenomePool.ReplaceElite` + `WorldSim.SeedPoolFromGenomes`
  siembran partidas con la población pre-entrenada; CLI `--mode pretrain` con
  `--pop/--generations/--export` (reporte determinista por generación y
  `.antgenome` exportado).
- **Bug raíz corregido (latente desde Fase 2)**: `DeterministicRandom` es un
  struct y `MlpGenome.Random/Crossover/Mutate` lo recibían **por valor** — cada
  llamada mutaba una copia y el flujo nunca avanzaba (todos los genomas salían
  idénticos; además `_rng` era `readonly` en `GenomePool`/`CurriculumTrainer`,
  con copias defensivas en `Tournament`). Ahora toman `ref` y los campos ya no
  son readonly; se añadieron tests de regresión (dos extracciones/nacimientos
  consecutivos difieren).
- **Exit**: 73 tests verdes; el entrenador converge a competencia con semillas
  conocidas y el pre-entrenamiento completo es determinista entre procesos
  (salida y `.antgenome` byte a byte idénticos).

### Fase 3bis — Arena realista y transferencia ✅ (completada)
- La validación de transferencia de la Fase 3 falló: sembrar el mundo con la
  población entrenada en la arena (hormiga sola, ítem a la vista, rastro
  plantado) daba pickup 0 en el mundo abierto. **`ArenaEvaluator` reescrita**
  para replicar el régimen real: colonia completa (10 fundadoras + cría +
  `ColonyController`), 24 ítems reales a ≥ 200 u con el spawn del mundo, sin
  rastro ni respawn, señal de COLONIA (Σ fitness de adultas + densados récord
  de exploración sin carga y homing con carga, monótonos y no explotables).
- **Currículo por horizonte temporal** (cercana→relevo→mundo, banda fija
  200–260 u): ensanchar la banda estaba calibrado y rechazado — el campeón de
  banda ancha olvida el pickup; el de banda cercana transfiere.
- Bugs corregidos en la calibración: export de población SIN evaluar (75 % de
  hijos con Fitness=0 en el `.antgenome`), fundadoras sin cerebro entrenado
  (`SeedPoolFromGenomes` ahora re-asigna cerebros a las adultas vivas) y olvido
  por banda ancha. Física verificada: la ida-y-vuelta sola a ≥ 200 u es
  imposible (~148 s vs ~110 s de vida); la generación 0 compite explorando,
  cargando y volviendo parcialmente — la descarga exige relevo con inflow.
- **Transferencia revalidada** (`--seed-pool`): pickup 0 → 4–7, pool best
  1.06 → 48.7, portadores a ~105 u del nido (óptimo físico de la generación 0).
- **Exit**: 72 tests verdes; determinismo arena↔mundo intacto.

### Fase 3ter — Arranque en frío del mundo ✅ (implementada)
- El bloqueo no era de la arena sino del MUNDO: descompuesto en tres mecanismos
  acoplados y corregido con cambios mínimos y realistas en `WorldSim` /
  `ColonyController` (todos deterministas; 72/72 tests verdes tras retirar
  sondas temporales):
  1. **Vigor fundador escalonado** 0.6→1.0 por índice (antes 0.8 uniforme ⇒ las
     10 fundadoras morían el mismo tick y el relevo por suelta era imposible).
  2. **Reserva fundadora completa** (`Stock = StockMax`): la ventana de forrajeo
     (~80–110 s) sobrevive al arranque en frío.
  3. **`NurseRate` = LarvaIdeal (0.088)** — antes 0.04, matemáticamente
     incapaz de pupar ni una larva; y **alimentación larval serializada,
     priorizando la más invertida y, en empate, la más joven** (el reparto
     equitativo sobre la oleada no maduraba ninguna; priorizar la más vieja
     rotaba el presupuesto sobre larvas a punto de morir — detectado con sonda).
  4. **Puesta ligada a la entrada real** (`inflowGate` sobre el término de
     déficit): la reina fundadora ya no inunda ~1.6 huevos/s (0.8 ep/s ≈ 47 %
     de la reserva) para "crecer a 40" sin comida; sin inflow solo repone bajas.
- **Física del relevo verificada con sonda de política artesanal**: la primera
  descarga es el relevo multigeneracional (portador homing máximo → suelta a
  ~115 u → descendiente completa el tramo final); con ítems plantados a 120 u:
  5 descargas; mundo abierto 800 s: eclosiones 0→13, descargas 0→2.
- **Revalidación `--seed-pool`** (5 semillas, 800 s): baseline pickup 0 /
  descarga 0; sembrada (pool 60 gens) **pickup 6–12, descarga 1–3 en 3/4
  semillas**; el campeón en la arena nueva: fitness 295.7, 22 pickups,
  6 descargas (la señal de la arena selecciona el relevo). El pool recién
  entrenado (15–30 gens) aún no converge al homing por brújula — configuración
  rara en el espacio de pesos; el de 60 gens sí — tiempo de entrenamiento, no
  diseño del mundo.

### Warm-start — siembra del entrenamiento desde `.antgenome` ✅ (implementada)
- `CurriculumTrainer` acepta población inicial opcional (`--warm-start f`): los
  genomas del archivo se clonan y mutan hasta `PopSize` y se RE-EVALUAN en la
  arena actual (las `Fitness` del archivo quedan obsoletas con cada cambio de
  mundo); rechazo si la topología de red difiere. Sin archivo: nacimiento
  aleatorio como antes.
- **Resultado**: entrenando desde el pool de 60 gens (pop 24, 10 gens/etapa)
  el comportamiento se conserva en la arena nueva desde la generación 1 —
  **21/24 genomas competentes (descargas ≥ 1), best 358.2 vs ~47 del arranque
  en frío**; el warm-start evita las 60+ gens que el frío necesita para
  encontrar el homing por brújula.
- **Revalidación `--seed-pool`** con el pool warm-entrenado (5 semillas,
  24 000 ticks): **pickup 6–10, descarga 1–2 en 4/5 semillas, eclosiones
  24–27**; determinismo entre procesos intacto (hash byte a byte, semilla 777
  verificada en dos procesos). 74/74 tests verdes.
- **Salud del relevo en el reporte**: `RelayTracker` (Scenario) observa el
  flujo de eventos de solo lectura y expone `first-unload` (primer tick de
  descarga) y `drop-avg` (distancia media al nido de las sueltas por muerte de   portadora, distinguidas del spawn regular por el centinela ColonyId = -1);
   la línea `totals` de `evolve`/`world` los imprime (`-` si no hay), y cada
   hito de tick emite una línea `relay` acumulada (first-unload/drop-avg/
   unload-avg/carry-leg) vía `TickLine` (emisor compartido CLI+escenario, que
   además evita divergencias de formato). No toca la simulación: hashes
   idénticos con y sin el tracker (verificado, semilla 42). 86/86 tests verdes.
   `scripts/pipeline.sh` muestra las columnas `unload1st`/`dropavg`/`carryleg`
   y añade el modo `--verify`: revalida pools consecutivos y falla (exit 1) si
   `first-unload` desaparece, `drop-avg` sube >10 % o `carry-leg` se encoge
   >20 % — regresión del relevo en CI sin sondas.
- **Refinado con banda extendida**: `--band-min/--band-max` re-bandan todas las
  etapas del currículo (validación ≥ `NestMinSpawnDistance`). Segundo
  warm-start desde `pretrain-warm.antgenome` con el anillo 200–350 u (~3×
  área): la etapa corta pierde competencia (el relevo necesita horizonte) pero
  relevo+mundo recuperan 19–23/24; fitness de arena menor (~245–298, ítems más
  dispersos) SIN pérdida de transferencia — mundo abierto: pickup 7–10,
  descarga 1–3 en 3/5 semillas (máx histórico 3), eclosiones 24–27 vs baseline
  0/0. El pool de banda ancha conserva el relevo y generaliza a distancias
  mayores. 74/74 tests verdes; determinismo verificado (hash idéntico, semilla
  42, dos procesos).

### Fase 4 — Aplicación Unity 2D (primer hito jugable)
- Contrato de datos del HUD (F4.2): [`fase4-hud-contrato.md`](fase4-hud-contrato.md)
  — mapeo canal B/C → alertas, tarjeta de colonia (semáforo de relevo con los
  umbrales del benchmark), estado global, historial de comandos, inspección
  y las reglas duras del HUD (nunca consulta el Core, nunca suaviza métricas).
- Diseño de UX aprobado: [`fase4-diseno-ux.md`](fase4-diseno-ux.md) (importar ·
  espectar · intervenir como comandos con tick; huecos del Core a cerrar:
  canal A `SimSnapshot`, canal C `MetricFrame`, sistema de comandos).
- **F4.0 ✅ (Canales A/C + comandos)**: `SimCommand` (`DropFood` — el ítem del
  jugador es determinista: amount constante 5 ep, sin RNG; clamp al mundo) con
  cola `EnqueueCommand` aplicada en el PUNTO CANÓNICO del `Step` (tras avanzar
  el tick, antes de que actúe cualquier hormiga), registrada en el Canal B como
  evento `CommandExecuted` (el .antlog y el modo verify la capturan sin tocar
  su layout). `Telemetry.SimSnapshot` (canal A: poses + vista de colonia, pull
  por tick) y `MetricRecorder`/`MetricFrame` (canal C: ventanas de 30 ticks =
  1 s sim, mismo patrón observador que RelayTracker). **97/97 tests verdes**,:
  incluye los 8 de F4.0 — misma semilla + mismos comandos ⇒ mundo bit a bit
  (con y sin telemetría adjunta), timing/posición distintos ⇒ mundo distinto,
  snapshot fiel al mundo sin mutarlo, y .antlog con comandos ⇒ hitos
  reproducibles byte a byte.
- **F4.1-parcial ✅ (feed de juego headless)**: `GameScenario` emite un stream
  JSONL por tick — canal A (poses/items/colonias cada `frameEvery` ticks),
  canal B (eventos siempre), canal C (`MetricFrame` al cerrar la ventana de
  1 s), telemetría de relevo cada 120 ticks y línea `end` con hash — función
  pura de (seed, ticks, comandos, pool). CLI `--mode game` con `--frame-every`,
  `--drop tick:x:y` (inyección de comandos F4.0) y `--seed-pool` reutilizado.
  El presenter Unity consume este mismo contrato headless antes de existir.
  **101/101 tests verdes** (4 nuevos: stream byte a byte, hash idéntico a
  `WorldScenario` sin comandos, comandos ⇒ mundo distinto, `CommandExecuted`
  registrado con el ítem cayendo).
- **F4.5-parcial ✅ (datos del selector de pools)**: `Telemetry.PoolPresets` —
  los cuatro presets del diseño de UX (naturalista, warm-v2, warm-4, warm3)
  con las métricas REALES del benchmark de referencia (benchmark-fase3ter.txt:
  10 semillas × 24 000 ticks) y del modo juego grid 256 (5 semillas), banda,
  archivo `.antgenome` y procedencia documental por preset, más el comando de  reproducción. Test de contrato con valores centinela: si el benchmark se
  regenera con otro mundo, los tests obligan a actualizar las tarjetas — la UI
  nunca muestra números que el repo no respalde. **112/112 tests verdes**.
  La cadena completa está en el picker (6 presets): los 4 recomendados del
  diseño de UX más dos especialistas NO por defecto con su contrapartida
  (`IsRecommended=false` + `TradeOff`) — warm3-v2 (banda ancha con el
  drop-avg más sano, 164.7, sin la corona de warm3) y warm-5 (récord de
  forrajeo, 118 pickups, pero 2/5 en mundo grande). Los especialistas se
  ordenan tras los recomendados y su tarjeta lleva la advertencia ⚠ con el
  pool alternativo sugerido.
- **F4.1 ✅-parcial (esqueleto Unity, verificable headless)**: `src/App/AntSim.Unity`
  — scripts PUROS sin UnityEngine (`GameStreamParser` JSONL → TickView,
  `GameStreamPresenter` interpolación con retraso de 1 tick,
  `PoolPickerModel` con los dos niveles del selector) que se COMPILAN en la
  suite del Core (`AntSim.Core.Tests` los incluye vía `<Compile Include>` y
  `UnityStreamContractTests` verifica el parser y el picker contra la salida
  REAL de `GameScenario`/`PresetScenario`: reconstrucción fiel de poses/items/
  colonias/eventos/métricas/relevo, movimiento coherente, tiers recomendados/
  especialistas). MonoBehaviours finos (`SimPresenterBehaviour` con
  `Graphics.DrawMesh` y pausa/velocidad; `PoolPickerBehaviour`). La escena,
  feromonas por tiles y HUD rico quedan para el resto de F4.1/F4.2.
  **120/120 tests verdes** (5 nuevos de contrato).
- **Fixture de mundo grande (F4.1/F4.2)**: `artifacts/stream-fixture-256.jsonl`
  (no trackeado, repro en el README de Unity) — la partida warm-v2 sembrada de
  grid 256 / 48000 ticks / 2 colonias con `frameEvery 30`: canal A a 1 Hz y
  eventos SIEMPRE por tick (garantía always-tick del contrato), así que el
  relevo completo (la descarga de tick 5154 con semáforo verde colonia 0 /
  gris colonia 1) es
  verificable sin el peso de ~170 ítems por línea a 30 Hz (13.5 MB vs 376 MB).
  Mismo hash final que con frameEvery 1: la cadencia de frames no toca la
  simulación — util para probar HUD/inspector/semáforo contra datos reales.
- **CI del contrato del stream**: el hash final de la partida canónica
  (seed 42, grid 96, 2 colonias, 7200 ticks — la de
  `artifacts/stream-fixture.jsonl`) está FIJADO en dos sitios que deben
  mantenerse iguales: el test `FixtureHash_ElStreamCanonicoEsByteAByteEstable`
  (constante `CanonicalStreamHash`) y `scripts/stream-fixture.expected`, que
  lee `scripts/check-stream-fixture.sh`. El script regenera el stream con el
  CLI y falla (exit 1) si el hash difiere — cualquier deriva del mundo sin
  decisión consciente rompe CI; un cambio intencional se incorpora con
  `--update` (reescribe solo el archivo de datos, nunca el script en
  ejecución) más la edición del test. `.github/workflows/ci.yml` corre la
  suite completa y el script en cada push/PR.
- **F4.3 ✅ (semáforo de relevo recalibrado)**: `Core/Scenario/RelayVerdict.cs`
  — la regla del semáforo como código (la UI nunca inventa umbrales):
  tramo invariante ≥ 60 u (propiedad del portador) y drop máximo escalado
  linealmente al mundo (190 × grid/96: propiedad de la distancia de
  forrajeo). Calibrado en 96² (benchmark Fase 3ter), validado en 256² con
  el smoke de 5 semillas: 4/5 verdes (antes 2/5 con la regla fija), el
  único ámbar real es tramo corto — degradación genuina, castigada en
  cualquier mundo. Tests: `RelayVerdictTests` + integración grid-256 con
  warm-v2 real.
- **F4.2 ✅-parcial (los tres TODOs del contrato HUD cerrados)**:
  `ColonyExtinct` como evento 12 del canal B (UNA vez por colonia, por
  transición de estado sin adultas ni cría; el camino de carga de checkpoints
  inicializa su bandera perezosamente — verificado en test), inspección por
  hormiga en el canal A (12 campos: +vigor/energía/edad/inmigrante/huella
  FNV del genoma — identifica "el mismo cerebro" sin serializar pesos) y
  desglose por colonia de relevo (`RelayTracker.ForColony` → `relays:[…]` en
  el stream) y de métricas (`MetricRecorder.ColonyWindows` →
  `colmetrics:[[…]]`); el parser de Unity ya consume ambos. Los desgloses
  suman EXACTAMENTE los totales (tests). **126/126 tests verdes** (5 nuevos).
- **F4.0-ampliación ✅ (comando SaveGame)**: `SimCommandKind.SaveGame` es un
  comando de OBSERVACIÓN — no muta el mundo (los hashes con y sin él son
  idénticos; verificado en test), queda en el Canal B como `CommandExecuted`
  con el slot en `Cause`, y expone `WorldSim.SaveRequests` tras el Step para
  que el presenter escriba el `.antsave` con `WorldSimSave.Save`. Roundtrip
  verificado bit a bit: partida con drops + save → checkpoint en el tick del
  save → `WorldSimSave.Load` reproduce el hash exacto del momento → 299 ticks
  de reproducción con hash a hash idénticos a la partida original.
  **115/115 tests verdes** (3 nuevos).
- **F4.5-parcial ✅ (diff del selector)**: `--mode presets [--json]` — emite las
  tarjetas canónicas del selector (texto legible con fuente y comando de
  reproducción, o JSON estructurado por preset con el campo `card` incluido).
  Determinista byte a byte: la salida del CLI es el ORÁULO contra el que el
  equipo Unity diffea su UI — cualquier desviación es un bug del HUD, no de
  los datos.
- `SimPresenter`: interpolación con retraso de 1 tick, pool, feromonas GPU por tiles.
- HUD, inspección con traza, alertas, biblioteca de cerebros, checkpoints desde UI.
- **Exit**: demo jugable 60 fps con 2 colonias; regresión visual con seeds fijas.

### Fase 5 — Realismo, especies y escala
- Atta (soldados por sobrealimentación, hongo) y Eciton (legionaria, ciclos nómadas).
- Competencia entre colonias y depredadores; NEAT en vivo con inspector de grafos.
- Refactor SoA/ECS y LOD de feromonas.
- **Exit**: 3 especies diferenciadas; N colonias estables a 60 fps.

### Fase 6 — Migración 2D→3D y pulido
- Adaptador 3D (mismos contratos), terreno con altura, decals de feromonas.
- Balance, benchmarks, sonido, accesibilidad.
- **Exit**: v1.0 publicable para Windows.

## 8. Riesgos principales y mitigación

| Riesgo | Mitigación |
|---|---|
| Determinismo roto | RNG propio, timestep fijo, orden canónico, tests de hash desde Fase 0 |
| Fricción Unity ↔ netstandard | Perfil conservador en el Core; prueba de integración temprana (Fase 4) |
| Pre-entrenamiento no converge | Currículo incremental + sondas canónicas + brújula innata como ancla |
| Neuroevolución estancada | Diversidad + σ adaptativa + inmigración/cuarentena + alertas de meseta |
| Feromonas costosas | Actualización por región activa y tiles sucios; LOD en Fase 5 |

## 9. Decisiones abiertas (se resuelven en su fase)
- Atta (cadena cortar→transportar→hongo) y Eciton (ciclos nómadas): Fase 5.
- Depredadores y agresividad inter-colonia: Fase 5.
- Calibración numérica (economía, umbrales): tests de balance en Fase 1–3.
