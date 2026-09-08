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

### Fase 4 — Aplicación Unity 2D (primer hito jugable)
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
