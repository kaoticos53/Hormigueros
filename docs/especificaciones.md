# 📐 Especificaciones cerradas — Contratos del Core y de la vista

> Registro duradero de las decisiones de diseño. Los contratos de canales y los
> formatos de archivo son **ABI estable**: ampliar algo añade al final y sube la
> versión; nunca se reordena ni se reinterpreta.

## 1. Contrato `IBrain` (v1)

- **Entrada `AntSensors` — 19 canales** (orden canónico):

| # | Canal | Rango | Origen |
|---|---|---|---|
| 0–2 | `cFood/cHome/cAlarm` | [0,1] | Intensidad central en la sonda (bilineal) |
| 3–5 | `dFood/dHome/dAlarm` | [−1,1] | Diferencia lateral antenas (izq − der) |
| 6–8 | `foodDX/foodDY/foodSize` | [−1,1],[0,1] | Vector al ítem de comida visible + fracción |
| 9–10 | `homeDX/homeDY` | [−1,1] | Brújula innata al nido propio (marco local) |
| 11–13 | `proxL/proxF/proxR` | [0,1] | Antenas táctiles (obstáculos) |
| 14–15 | `hasLoad/loadFrac` | {0,1},[0,1] | Carga |
| 16–17 | `energy/ageNorm` | [0,1] | Estado interno |
| 18 | `colonyFood` | [0,1] | Reserva del nido S/Smax |

- **Salida `AntDecision` — 6 canales**: `steer [−1,1]`, `speed [0,1]`,
  `depositFood/depositHome/depositAlarm [0,1]`, `interact [0,1]` (≥ 0.5 = intento).
- **Validación en el Core (4 etapas, en orden fijo y sin RNG)**:
  1. *Sanitizar*: NaN/Inf ⇒ valores neutros y genoma marcado inválido (fitness ×0.5 al morir).
  2. *Clamp* a límites del actuador.
  3. *Gating* físico/químico: `depositFood` solo con carga; unload solo en el nido;
     pickup solo sin carga y en contacto; alarma refleja forzada al recibir daño.
  4. *Cooldowns* (0.5 s) y resolución de conflictos por `antId` (determinista).
- Geométrica de sonda por especie: 3 puntos (centro ±18°, radio 24 u ≈ 3 celdas);
  rango visual rV por especie. La especie cambia constantes **fuera** del cerebro,
  nunca los canales ⇒ los genomas son portables entre especies.

## 2. Feromonas

- Capas **por colonia**: `FoodTrail` (τ½ 10 s, λ 0.0693), `Home` (20 s, 0.0347),
  `Alarm` (1 s, 0.693), `Territory` (60 s, F5). Índice: `colonyId·LayersPerColony + kind`.
- Celda 8 u; grid por defecto 512×512; tope `Cmax = 1`.
- **Depósito**: `v = min(v + Q·dt, Cmax)`.
- **Evaporación** exponencial: `v *= exp(−λ·dt)` (estable, independiente del dt).
- **Difusión** explícita de 4 vecinos, `k ≤ 0.25` por paso; bordes tratan el
  exterior como 0 (pérdida documentada); nunca negativa ni overshoot.
- Actualización por **región activa**; versionado por **tiles de 64×64** (cada
  escritura que cambia una celda incrementa la versión de su tile y el contador
  global de la capa) para el render incremental por tiles sucios.
- Sonda de olfato de 3 puntos con **bilineal**; la hormiga solo lee capas de su colonia.

## 3. `ColonyController` (demografía)

- **Puesta**: `λ_eggs = clamp((A_target − A)·k_repl + A·λ_death, 0, λ_max)
  · ρ_res(T_runway) · q_reina · G(t) · huecos(E)` con acumulador discreto.
  `A_target = 40`; `T_runway = S / consumoEMA`.
- **ρ_res** = `clamp((T_runway − T_crit)/(T_safe − T_crit), 0, 1)`.
- **Vigor al nacer**: nutrición larvaria acumulada; eclosión fuerte si `nutr ≥ K_full`,
  *minim* débil si `K_min ≤ nutr < K_full` en `τ_larvaMax`, muerte si no llega.
  `v = clamp(g0·(0.55 + 0.45·n̄), 0.15, 1.15)` modula energía inicial, vida,
  velocidad, carga y radios. Atta: `n̄ ≥ 1.15` habilita la casta soldado (F5).
  `b_ideal` se deriva de las constantes (`K_full/τ_larvaMax × 1.1`) para que una
  larva bien alimentada siempre pueda alcanzar K_full dentro del plazo.
- **Umbrales de escasez** (runway en segundos; Lasius): `T_safe 40` → puesta a tope;
  `T_ooph 25` → oofagia (reabsorber huevos, η ≈ 0.3); `T_cann 15` → canibalismo
  larval (las más débiles primero, `η_larva·nutr`); `T_crit 8` → racionar adultas
  y, en último recurso, pupas. Orden de sacrificio: huevos → larvas débiles → pupas.
  El acumulador de canibalismo **solo come lo que puede pagar** (compara contra la
  recuperación de la víctima más barata): no sobregira, es determinista y corta
  los bucles con recuperación 0.
- Alimentación priorizada por paso: reina → adultas → larvas (si `T_runway > T_cann`);
  reparto equitativo; nodrizas limitan la tasa de alimentación larval.

## 4. Evolución (GA) — Fase 2 implementada

- **`MlpGenome`** (v1): topología + pesos; crossover uniforme por peso (padre A/B
  con el RNG), mutación gaussiana Box-Muller (σ 0.05), clonación, distancia = media
  de |Δw| (proxy v1 de diversidad; sondas comportamentales en F5).
- **`GenomePool`** por colonia (élite top-K=64): **los mejores candidatos se usan al
  nacer** (torneo binario → crossover → mutación); el fitness de por vida alimenta
  el acervo al morir (recompensas: +0.5/pickup, +2·ep/descarga, +0.01·s⁻¹/supervivencia).
- **Inmigración con cuarentena**: la importación encola inmigrantes que ocupan
  eclosiones dentro de la ventana (120 s) en orden de mérito de origen; al morir la
  hormiga de prueba entran a la élite si fitness ≥ p50 (modo estricto) o, con
  `D < D_floor` (0.03 en espacio de pesos), fitness ≥ p25 **y** novedad ≥ diversidad
  media (modo sensible a diversidad). Si no lo demuestran, se descartan.
- **`.antgenome` v1**: layout canónico (magic `ANTGENOM`, formatVersion 1,
  contractVersion, count, nombre/speciesHint UTF-8, originSeed, generación,
  fitness de exportación informativo, por genoma: tamaños u16 + pesos f32 bit
  exactos + fitness, SHA-256 final). El orden de mérito descendente se conserva;
  el fitness absoluto no es comparable entre partidas.
- **NEAT (F5)**: genes estructurales en `.antgenome` (nodos con bias/activación,
  conexiones con peso/enabled/innovation, orden canónico por innovation); re-innovación
  determinista al importar (linaje extranjero con bloque local disjunto); feed-forward
  v1 con topes (500 nodos / 2 000 conexiones) y validación estructural estricta.
- **⚠️ RNG por referencia (corregido en F3)**: `DeterministicRandom` es un struct;
  `MlpGenome.Random/Crossover/Mutate` lo recibían por valor (cada llamada mutaba una
  copia: la secuencia nunca avanzaba y todos los genomas salían idénticos) y los
  campos `_rng` de `GenomePool`/`CurriculumTrainer` eran `readonly` (copia defensiva
  en `Tournament`). Ahora toman `ref` y los campos son mutables; hay tests de regresión
  (dos extracciones o dos nacimientos consecutivos difieren).

## 4bis. Pre-entrenamiento headless — Fase 3 implementada

### Arena realista (Fase 3bis, vigente)

> La arena original de la Fase 3 (hormiga sola, ítem a la vista, rastro plantado)
> **no transfería** al mundo real: sembrar la partida con su población producía
> pickup 0 en 300 s. La arena vigente replica el régimen del mundo real.

- **Colonia completa** (`ArenaEvaluator`): un `WorldSim` nuevo por prueba con las
  10 fundadoras y las 4 crías del constructor, con el `ColonyController` operando
  (alimentación, puesta, cría, canibalismo). Las fundadoras llevan clones del
  genoma evaluado y el pool se siembra con él — exactamente el régimen de
  `--seed-pool` en el mundo real (la descendencia nace de variantes mutadas).
- **Comida real**: 24 ítems de 4 ep (densidad del mundo, `TargetItemsDefault`)
  con el muestreo del mundo (uniforme, ≥ `NestMinSpawnDistance` del nido), dentro
  de la banda de la etapa. Sin rastro plantado y sin respawn (`TargetItems=0`).
- **Señal de colonia**: fitness = Σ aptitud de TODAS las adultas (los muertos
  conservan su fitness de por vida) + dos densados RÉCORD (monótonos, no
  explotables por oscilación): · exploración SIN carga: pago por récord de
  distancia hacia fuera, solo bajo el tope de la etapa (`ExplorePerUnit 0.015`) —
  sin él el primer eslabón (alcanzar la banda a ≥ 200 u) nunca se muestrea (un
  paseo aleatorio apenas llega a ~190 u); · homing CON carga: pago por récord de
  acercamiento al nido desde el último pickup (`HomeShapingPerUnit 0.06`), la
  descarga resetea el ciclo. La recompensa real (pickup 0.5, descarga 2/ep +
  bonus 4, supervivencia 0.002/s, depósito 0) sigue siendo la señal objetivo.
- **Sin corte por estancamiento**: el corte por distancia máxima amputaba las
  pruebas productivas del relevo (un portador que vuelve a casa REDUCE esa
  distancia; ciclar a radio constante no marca récords). Presupuesto completo
  para todos los genomas: comparabilidad y determinismo estrictos.
- **Física verificada que condiciona el diseño**: una ida-y-vuelta SOLA a ≥ 200 u
  es imposible para cualquier política (~148 s a VMax 2.7 frente a ~90–110 s de
  vida de fundadora). La competencia observable de generación 0 es explorar +
  cargar + volver parcialmente (homing ~97 u ≈ óptimo físico: pickup a 200 u
  cuesta 74 s y deja ~36 s de vuelta). La descarga exige el RELEVO (muerte con
  carga → el mundo re-spawnea el ítem donde cae → otra hormiga completa el tramo)
  que necesita inflow primero → cría → eclosión (~45+ s huevo-adulta).
- **Currículo por HORIZONTE temporal** (`CurriculumTrainer.DefaultStages`), no por
  banda de distancia: `cercana` (banda 200–260 u, 3 600 ticks, máx 30 gens) →
  `relevo` (misma banda, 5 400, 40) → `mundo` (misma banda, 9 000, 60).
  `CompetenceFitness 0` (sin umbral: el producto de cada etapa es su élite
  evaluada) y `MinGenerations 4`. Ensanchar la banda estaba calibrado y
  RECHAZADO empíricamente: la banda ancha (200–520 u) multiplica ×27 el área del
  anillo, la comida se vuelve irencontrable y el densado de exploración domina —
  el campeón de banda ancha olvida el pickup (0 transferidos) mientras el de
  banda cercana transfiere.
- **Tres bugs corregidos durante la calibración (F3bis)**:
  1. **Export sin evaluar**: `Run()` devolvía la población tras el último
     `Evolve()` (25 % élite + 75 % hijos con `Fitness=0`): el `.antgenome`
     contenía 3 de cada 4 genomas sin evaluar. Ahora no se reproduce tras la
     última evaluación de cada etapa.
  2. **Fundadoras sin cerebro entrenado**: en el constructor de `WorldSim` las
     fundadoras nacen ANTES de que exista la élite sembrada (`Pool.Birth` cae al
     genoma aleatorio); `SeedPoolFromGenomes` solo sembraba el pool. Ahora
     también re-asigna cerebros a las adultas vivas — causa directa de que
     `--seed-pool` no mejorara la supervisión inicial pese a que los genomas
     entrenados SÍ forrajean en el régimen del mundo.
  3. **Olvido por banda ancha**: descrito arriba; el eje correcto del currículo
     es el horizonte temporal.
- **Transferencia revalidada** (`--mode evolve --ticks 9000 --colonies 1
  --seed-pool`, semillas 42/7/99): baseline (élite aleatoria) → pickup 0, best
  1.06, colonia extinta; sembrada (seed 7, pop 32, 60 gens) → **pickup 4–7**,
  pool best 48.7, portadores que homing hasta ~105 u del nido. `unload 0` es
  estructural del cold-start del mundo (todas las fundadoras mueren a la vez y el
  relevo necesita una eclosión, que necesita inflow), no de la arena.
- **Evolución del trainer**: élite (25 %) + torneo (k=3) + crossover uniforme +
  mutación σ 0.08; `Evolve()` vive DENTRO del bucle de generaciones y SOLO si
  habrá otra evaluación. Reporte determinista por generación (`GenerationStats`).
- **Siembra de partidas**: `GenomePool.ReplaceElite` / `WorldSim.SeedPoolFromGenomes`
  sustituyen la élite por la población pre-entrenada, re-aseñan cerebros a las
  adultas vivas y los nacimientos usan esa élite. CLI `--mode pretrain` con
  `--pop/--generations/--export`; `--mode evolve` con `--seed-pool`. Además
  `--warm-start archivo.antgenome` siembra la población INICIAL del
  entrenamiento desde un pool existente (clon+mutación hasta `PopSize`,
  re-evaluación en la arena actual, rechazo por topología distinta) en vez de
  genomas aleatorios.

### Arranque en frío — Fase 3ter (implementada)

> Objetivo: que una colonia fundada pre-entrenada alcance su PRIMERA descarga y
> su primera eclosión. El bloqueo del arranque en frío no era de la arena sino
> del MUNDO: descompuesto en tres mecanismos acoplados y corregido con cambios
> de diseño mínimos, realistas y deterministas (72→77 tests, luego 72 tras
> retirar las sondas).

1. **Extinción sincronizada** — las 10 fundadoras nacían con vigor IDÉNTICO
   (0.8) ⇒ esperanza de vida idéntica (106.2 s) ⇒ morían el MISMO tick. El
   relevo por suelta al morir era imposible por construcción. Ahora el vigor
   fundador se ESCALONA 0.6→1.0 por índice (sin RNG): muertes desincronizadas
   (~84→117 s) y sueltas escalonadas a lo largo de la generación.
2. **Reserva fundadora** — la colonia funda con stock COMPLETO (`StockMax`, 100
   ep en lugar de 50): la reserva cubre la ventana de forrajeo (~80–110 s: la
   comida está a ≥ 200 u, fuera de visión). Al 50 % el runway se agotaba antes
   de la primera oportunidad.
3. **La cría no podía eclosionar NUNCA** — `NurseRate` 0.04 daba un presupuesto
   de 0.06 ep/s frente a los 0.08 ep/s que exige UNA larva para pupar en su
   ventana (la propia `LarvaIdeal` de la especificación es 0.088). La constante
   ahora DERIVA de la especificación (0.088). Además el reparto equitativo sobre
   la oleada de puesta (hasta ~30 larvas a 0.004 ep/s) no maduraba a ninguna:
   ahora la alimentación larval es SERIALIZADA y PRIORIZADA (la larva más
   invertida primero; empate a la más JOVEN — la más vieja está a un tick de
   morir y rotar el presupuesto sobre ella era improductivo, detectado con
   sonda). Resultado: eclosiones desde ~40 s y ~1 cada ~20 s.
4. **La reina fundadora inundaba de huevos** — el término de déficit
   `(MaxAdults − A)·k_repl` (1.6 huevos/s = 0.8 ep/s ≈ 47 % de la reserva
   fundadora) quemaba el stock en ~120 s para "crecer a 40" SIN comida. La
   expansión ahora se escala por la entrada REAL (`inflowGate = clamp(InflowEma·10)`):
   sin inflow solo se reponen bajas (`A·λ_death`); la expansión espera al
   primer ciclo de comida — biología de fundación real (primera puesta
   limitada, expansión ligada a la entrada).
5. **Física del relevo (verificada con sonda de política artesanal)**: ningún
   individuo puede cerrar un ciclo (ida y vuelta a ≥ 200 u ≈ 148 s a VMax vs
   ~125 s máx de vida a vigor 1.15); ni siquiera una suelta a ~200 u es
   completable por una sola descendiente (0.76·d s de presupuesto). La primera
   descarga es el RELEVO MULTIGENERACIONAL: portador homing máximo → suelta a
   ~115 u del nido → descendiente joven la encuentra y completa el tramo final.
   Con ítems plantados a 120 u la política artesanal consigue 5 descargas;
   en el mundo abierto (sonda, 800 s): **eclosiones 0→13, descargas 0→2,
   portador a 24 u (el propio umbral de descarga)**.
- **Revalidación con `--seed-pool`** (`--ticks 24000`, semillas 42/7/99/1234/777):
  baseline → pickup 0, descarga 0, colonia extinta; sembrada con la población
  de 60 gens (exploración + homing fuerte) → **pickup 6–12, descarga 1–3 en 3
  de 4 semillas**, eclosiones 11–14 (baseline también eclosiona: el arranque en
  frío demográfico está resuelto por diseño). El mismo campeón en la arena
  nueva: fitness 295.7, 22 pickups, **6 descargas** — la señal de la arena
  selecciona el relevo; el pool recién entrenado (15–30 gens) aún no ha
  convergido al homing por brújula (configuración rara en el espacio de pesos;
  el pool antiguo de 60 gens sí lo encontró) — cuestión de tiempo de
  entrenamiento, no de diseño del mundo.
- **Historia (arena original de la F3)**: hormiga sola, un ítem a `FoodDistance`
  fija al este, rastro sembrado (0.9), umbral de competencia 8.5, estancamiento
  a 1 200 ticks y currículo 12→25→40→60 u. Sustituida por la arena realista al
  demostrar la validación de transferencia que no transfería.

### Warm-start — siembra desde `.antgenome` (implementada)

> Objetivo: continuar el entrenamiento de un pool existente en lugar de partir
> de genomas aleatorios. `CurriculumTrainer` acepta una población inicial
> opcional: si se provee, se clona y muta para llenar `PopSize` (los genomas
> del archivo se RE-EVALUAN en la arena actual — sus `Fitness` almacenadas
> quedan obsoletas con cada cambio de mundo); si no, nace aleatoria. CLI:
> `--mode pretrain --warm-start pool.antgenome`. Se rechaza un archivo con
> topología distinta (otro layout de red).

- **Resultado (entrenando desde el pool de 60 gens, pop 24, 10 gens/etapa)**:
  la población conserva el comportamiento en la arena NUEVA desde la primera
  generación — **7–14/16 genomas competentes (descargas ≥ 1) en el smoke y
  21/24 en el entrenamiento completo, best 358.2 vs ~47 del arranque en frío**
  (el entrenamiento en frío necesita 60+ gens para converger al homing por
  brújula; el warm-start lo conserva y lo refina).
- **Revalidación `--seed-pool` con el pool warm-entrenado** (`--ticks 24000`,
  semillas 42/7/99/1234/777): **pickup 6–10, descarga 1–2 en 4/5 semillas,
  eclosiones 24–27** — frente a pickup 0/descarga 0 del baseline. Determinismo
  entre procesos intacto (hash final byte a byte idéntico, verificado con la
  semilla 777 en dos procesos).
- **Salud del relevo en el reporte (sin sondas)**: la línea `totals` del CLI
  (`--mode evolve` y `--mode world`) incluye ahora `first-unload` (primer tick
  de descarga; `-` si no hubo) y `drop-avg` (distancia media al nido de las
  sueltas por muerte de portadora; `-` si no hubo). Los detecta `RelayTracker`
  desde el flujo de eventos sin tocar la simulación — los hashes son byte a
  byte idénticos con y sin el tracker — y distingue la suelta (ItemSpawned con
  ColonyId real) del spawn regular (centinela ColonyId = -1). Ejemplo real:
  baseline `first-unload - drop-avg -` vs pool warm2 (semilla 42)   `first-unload 5130 drop-avg 186.7`. El script `scripts/pipeline.sh` muestra
   ambas columnas en su tabla (`unload1st`/`dropavg`): una `drop-avg` alta con
   `first-unload` ausente es la firma de un relevo que no cierra.
 - **Salud del relevo por INTERVALO**: además de en `totals`, el CLI emite una
   línea `relay tick=N first-unload … drop-avg … unload-avg … carry-leg …` en
   cada hito de tick (cada 120), acumulada hasta ese instante: permite ver la
   evolución del relevo DURANTE la partida (p. ej. drops aparecen en tick 3600,
   primera descarga en 5130) sin sondas. `carry-leg` es la distancia media
   pickup→descarga de las cargas completadas — el ÚLTIMO eslabón medido
   directamente: cuánto tramo cierra el portador que termina (relevo real
   ~80–250 u; descarga solo del fundador que llega sola, ~0–50 u).
 - **Modo `--verify` del pipeline** (`scripts/pipeline.sh --verify`): salta el
   entrenamiento y revalida una lista de pools (`--pools "baseline a.antgenome
   b.antgenome"`); falla (exit 1) si entre dos pools CONSECUTIVOS desaparece
   `first-unload` (el previo descargaba en alguna semilla, el siguiente en
   ninguna), `drop-avg` sube >10 % (homing degradado: sueltas más lejos) o
   `carry-leg` se encoge >20 % (la descendencia completa menos tramo por
   carga: el relevo pierde alcance sin dejar de descargar — un umbral más
   holgado que el de `drop-avg` porque la media sobre pocas semillas con
   descarga es ruidosa). Pares sin métrica en el pool previo no se comparan
   en ese campo.
- **Refinado con banda extendida (200–350 u)**: `--band-min/--band-max`
  re-bandan todas las etapas (mínimo respetado: ≥ `NestMinSpawnDistance`).
  Segundo warm-start desde `pretrain-warm.antgenome` (seed 7, pop 24, 15
  gens/etapa): el anillo 200–350 u es ~3× el área del 200–260, así que la
  etapa corta (3600 ticks) no completa ciclos (competente 0) pero relevo y
  mundo recuperan **19–23/24 competentes**; el fitness de arena baja (~358 →
  ~245–298, ítems más dispersos) SIN pérdida de transferencia — en el mundo
  abierto (5 semillas, 24 000 ticks): **pickup 7–10, descarga 1–3 en 3/5
  semillas (máximo histórico 3 en la semilla 42), eclosiones 24–27** vs
   baseline 0/0. El pool de banda extendida no olvida el relevo: lo conserva y
   generaliza a distancias mayores.
 - **Tercer eslabón de la cadena (200–450 u) — límite de generalización**:
   tercer warm-start desde `pretrain-warm2.antgenome` (misma receta: seed 7,
   pop 24, 15 gens/etapa). En arena, la etapa corta sigue sin ciclos (0
   competentes) pero relevo y mundo convergen (competentes 13→19 y 20–24/24,
   best ~282). En el mundo abierto (5 semillas, 24 000 ticks) el forrajeo
   MEJORA (pickups 45→51, descargas 6→7, 4/5 semillas con descarga, drop-avg
   186→163: las sueltas caen más cerca del nido) pero el `carry-leg` se
   ENCOGE 91.8→46.6 u — la descendencia completa tramos más cortos, señal de
   que la política está optimizando el forrajeo del anillo amplio en vez del
   cierre de relevo largo. `--verify` lo detecta como regresión
   (`carry-leg se encogió 91.8 → 46.6 (>20%)`): el límite práctico del
   currículo actual está en ~350 u de banda; más allá, la competencia de
   forrajeo crece a costa de la profundidad del relevo. - **Benchmark de referencia Fase 3ter** (`artifacts/benchmark-fase3ter.txt`,
   regenerado dos veces): 8 filas × 10 semillas × 24 000 ticks, determinista
   (hashes byte a byte). La ÚLTIMA regeneración corre bajo las constantes
   finales del mundo (vigor fundador 0.75–1.15, cría inicial adelantada y
   densidad de comida constante por área) — las filas anteriores corresponden
   al mundo pre-dead-zone y solo tienen valor histórico. Los POOLS no se
   re-entrenaron (están congelados: entrenados bajo las constantes viejas y
   transferidos al mundo nuevo — la prueba de robustez de la transferencia).
   Resumen agregado (mundo final):

   | pool | pickups | descargas | semillas c/ descarga | drop-avg | carry-leg |
   |---|---|---|---|---|---|
   | baseline | 0 | 0 | 0/10 | — | — |
   | pretrain-60 (frío, 60 gens) | 79 | 6 | 5/10 | 210.9 | 85.5 |
   | pretrain-warm (200–260, pop 6 — OBSOLETO) | 6 | 0 | 0/10 | 229.3 | — |
   | pretrain-warm-v2 (200–260) | 99 | 17 | **10/10** | **167.9** | 80.0 |
   | pretrain-warm2 (200–350) | 91 | 8 | 7/10 | 192.3 | 76.1 |
   | pretrain-warm3 (200–450) | **107** | **22** | 10/10 | 171.1 | 61.1 |
   | pretrain-warm3c (200–450 + carry-leg) | 97 | 13 | 9/10 | 185.2 | 71.6 |
   | pretrain-hybrid (alternado 200–325/200–450) | 107 | 14 | 9/10 | 175.2 | 64.2 |

   Lecturas (mundo final, con las constantes del dead zone rotas y densidad
   constante): baseline sin forrajeo; el pool en frío de 60 gens transfiere
   forrajeo y algo de relevo. **warm3 se convierte en el líder de descargas
   (22, 10/10 semillas)**: la densidad de comida constante por área llena el
   anillo amplio de objetivos y su banda de entrenamiento 200–450 pasa de
   handicap a ventaja. warm-v2 mantiene su corona de FIABILIDAD con drop-avg
   más sano (167.9, único pool con verify OK contra warm2) y carry-leg 80.0.
   El antiguo carry-leg máximo (warm2, 80.5) cae a 76.1 y su drop-avg se
   degrada (192.3, verify REGRESIÓN contra warm-v2): con más comida fuera de
   su banda, sus portadoras sueltan más lejos. La banda extendida ya no es
   un trade: con densidad constante, warm3 domina en descargas y pickups,
   pero su carry-leg (61.1) sigue siendo la más corta de los pools fuertes.

   **Revalidación del eslabón warm (200–260)**: la fila original
   `pretrain-warm` era un artefacto de RECETA, no del mundo — el pool viejo
   se entrenó con pop 6 × 2 gens/etapa (un humo del pipeline), así que sus 0
   descargas reflejaban un pool débil, no una pérdida de transferencia.
   Re-entrenado con la receta estándar de la cadena (warm2 → banda 200–260,
   seed 7, pop 24, 15 gens/etapa, best 458.8 y 23–24/24 competentes en
   arena), el pool recalibrado `pretrain-warm-v2` es el MEJOR del benchmark
   en fiabilidad de relevo: **14 descargas en 9/10 semillas**, pickups 94,
   drop-avg 175, carry-leg 71.7. La cadena warm-v2 → warm2 verifica OK:
   acortar la banda tras el refinado amplio CONSOLIDA el relevo (fiabilidad
   9/10 vs 6/10) a costa de algo de tramo medio (71.7 vs 80.5).   Recomendación
   de pool: warm-v2 para el modo evolución general; warm2 si se prioriza el
   tramo medio del relevo.

   **Currículo híbrido alternado** (`CurriculumTrainer.HybridStages`, flag CLI
   `--hybrid`): dentro de cada etapa, las generaciones IMPARES evalúan en la
   banda media (200–325) y las PARES en la ancha (200–450) — función pura de
   (etapa, generación), determinista y sin coste extra (mismo presupuesto por
   generación). Obliga a la selección a mantener AMBAS competencias: un
   genoma que explote solo el anillo amplio pierde posición en las
   generaciones de banda media y viceversa. Resultado (10 semillas,
   `pretrain-hybrid`, warm-start desde warm2): pickups 91, **13 descargas en
   9/10 semillas**, drop-avg 179.4, carry-leg 71.8 — la combinación buscada:
   la FIABILIDAD de la banda corta (9/10, como warm-v2) con el FORRAJEO de la
   banda ancha (91 pickups, entre warm2 88 y warm3 98) y tramo medio
   intermedio.   Verify de la cadena warm-v2 → warm2 → hybrid: OK en ambos
   pares.

   **Evaluación de MODO JUEGO (2 colonias competidoras, 48 000 ticks =
   1600 s, grid 256², 5 semillas; `artifacts/game-mode-probe.txt`)**: la
   colonia 0 sembrada con el pool, la 1 baseline (la atrición a los 6 min
   da simetría total entre ambas — los eventos identicos por colonia son la
   prueba de que el resultado no depende del seeding cuando el mundo se
   vuelve estático). Resultados (colonia sembrada): warm-v2 5 pickups /
   1 descarga; warm2 5/2; hybrid 6/0 — todos los pools se degradan frente
   a la ventana de 800 s (donde warm-v2 lograba 9/10 semillas) porque el
   96 % del horizonte de 1600 s es mundo muerto: los founders mueren
   ~3600 ticks y sin inflow la colonia no puede sostener más generaciones
   que las que el stock fundador alimenta. La evaluación en ventana larga
   con UNA colonia (24 000 ticks) sigue siendo el mejor discriminador de
   pools; el modo juego a 1600 s NO distingue warm-v2/warm2/hybrid y las
   diferencias observadas (±1 descarga) son ruido de muestreo.   Decisión:
   el pool para el modo juego es **warm-v2** (mejor fiabilidad en el
   benchmark de referencia) y la mejora pendiente es de DISEÑO DEL MUNDO
   (hacer que el relevo arranque antes de los 6 min), no de pool.

 - **Ruptura del dead zone (Fase 3ter, diseño del mundo)**: dos cambios
   mínimos y realistas en la fundación:
   (1) **Vigor fundador 0.75–1.15** (antes 0.6–1.0): las reinas fundadoras
   producen primeras obreras más robustas que la media (inversión
   fundadora). La última fundadora vive ~125 s (antes ~117) y la más
   frágil ~103.5 s (antes ~84): el pico forrajero experto se extiende y
   solapa más con la primera descendencia.
   (2) **Cría inicial adelantada**: los huevos de la fundación nacen con la
   mitad de su tiempo de huevo ya consumido (`Age = EggTime/2`) — una
   reina fundadora pone su primera puesta antes de que la colonia exista
   como tal. La primera cohorte eclosiona ~4 s antes y entra en la
   ventana forrajera de las fundadoras.
   Resultado (2 colonias, 48 000 ticks, grid 96, 5 semillas): el relevo
   se dispara — first-unload medio **4548 ticks (~69 s antes que antes)**,
   8/10 combinaciones pool×semilla con descarga (warm-v2 5/5 semillas,
   warm2 4/5, hybrid 5/5), pickups 7–15 por corrida. En grid 256 (mundo
   grande) la mejora es parcial (2/4 semillas con descarga): la densidad
   de ítems por área cae ×7 y el forrajeo inicial de la banda 200–260
   acierta menos — la falta de comida temprana sigue siendo el cuello de
   botella del mundo grande, no el timing fundador. Determinismo intacto
   (hash idéntico entre procesos).

 - **Densidad de comida constante por área (Fase 3ter, cierre del gap del
   mundo grande)**: el objetivo de ítems escala ahora con el área — 24
   ítems por mundo 96² (la calibración de la arena) ⇒ ~171 ítems en 256².
   Antes el mundo grande tenía los mismos 24 ítems en 7.1× el área:
   densidad ×7 menor y el forrajeo inicial fallaba en la mitad de las
   semillas. El escalado se aplica al spawn inicial y al respawn SOLO
   cuando `TargetItems` sigue en su default (la arena lo fija a 0 y el
   mundo de 96² no cambia: hash byte a byte idéntico). Resultado del modo
   juego en grid 256 (2 colonias, 48 000 ticks, 5 semillas): warm-v2
   **descarga en 5/5 semillas** (8 descargas, pickups 4–13, first-unload
   3745–5876, carry-leg hasta 174 u), hybrid 5/5 (7 descargas), warm2 3/5
   (4 descargas, la más débil: su banda de entrenamiento estrecha no
   cubre los ítems que ahora aparecen por todo el mundo). El gap del
   mundo grande está CERRADO y el veredicto se reconfirma: **warm-v2** es
   el pool del modo juego (único con 5/5 en ambos tamaños de mundo).
   Veredictos verify de la cadena original:
   warm2→warm3 REGRESIÓN (drop-avg 167.9→192.3 en el mundo final),
   warm3→warm3c OK, warm3c→hybrid OK. La comparación fiable
   entre pools es por métricas, no por orden de cadena.
 - **Densado de carry-leg en la arena (mitigación del shallowing)**: nuevo
   término de fitness `CarryLegPerUnit` (0.15 ep/u): en cada descarga REAL de
   la arena se paga la distancia recta pickup→descarga de esa carga — pagar
   solo al cerrar el ciclo y proporcional al tramo hace farmeable exactamente
   lo que se quiere (exige pickup + descarga reales, y el pago crece con la
   longitud del tramo completado; una cría que recoge una suelta a 200 u y
   llega cobra ~30 ep, más que el ciclo básico). Retrenando warm3 con el
   término (warm3b, 0.08 → warm3c, 0.15) el carry-leg medio del mundo abierto
   sube 46.6 → 57.7 u (10 semillas) y aparecen los mejores tramos de toda la
   cadena (100.5 u); el residual frente a la banda 200–350 (80.5 u) es
   GEOMÉTRICO, no de selección: con ítems a ≥ 200 u y sueltas más lejos del
   nido, el presupuesto de vida de la cría que completa trunca los tramos
   largos. El shallowing parcial de la banda 200–450 es estructural; el
   densado lo recupera en parte y previene el colapso total.

## 5. Telemetría

- `ColonyStat` en cada `SimSnapshot` (por colonia): A/E/L/P, S, runway, fitness
  medio, generación. Refresco visual ~15 Hz.
- `MetricFrame` cada **1 s sim** (pull): niveles + contadores del intervalo
  (nacimientos, muertes por causa, oofagia, canibalizados, eclosiones con vigor
  medio, forrajeo, histograma de tareas, feromona depositada, diversidad).
  Contadores incrementales O(1) reseteados al emitir.
- Las alertas del HUD son **función pura** de `MetricFrame` + eventos con debounce
  e histéresis (escalera: reservas bajando → oofagia inminente → canibalismo →
  colapso → extinción por evento).

## 6. Archivos y reproducción

| Archivo | Contenido |
|---|---|
| `.antsave` | Estado completo del mundo en un límite de tick (RNG, acumuladores ocultos, nutrición larvaria, genomas, grids con valores + revisiones por tile) |
| `.antlog` | Eventos + comandos de usuario con tick + hashes de hito (cada 1024 ticks) |
| `.antgenome` | Cerebros (1+ por archivo, orden por fitness); MLP: capas+activaciones+pesos bit exactos; NEAT: genes estructurales |
| `.antmetrics` / `.antmetrics.csv` | Diario binario canónico + CSV derivado determinista (cultura invariante, floats round-trip en modo `--exact`) |
| `.antevents.csv` / `.anttrace` | Eventos y traza por hormiga exportables |

- **Se guarda la causa, no los efectos**: reproducción = cargar `.antsave` en T y
  re-ejecutar; eventos y cambios de grid se regeneran idénticos.
- Verificación: `antsim verify` re-ejecuta y compara hashes de hito (mundo y métricas).

**Estado (implementado):** `.antsave` v1 binario canónico (mundo RNG + identidad,
colonias/nidos/cría/adultas con genomas MLP bit exactos, comida, grids de
feromonas con revisión por tile, RNG del pool élite; SHA-256 del cuerpo en la
cabecera), `.antlog` texto (eventos tick+kind+ant+datos y hashes de hito cada
1024 ticks) y `antsim --mode verify` (carga el checkpoint, re-ejecuta y contrasta
eventos e hitos; exit 0 idéntico / 3 divergencia). Pendiente: `.antmetrics`/
`.antevents.csv`/`.anttrace` (exportaciones de telemetría).

## 7. Vista (Unity, Fase 4)

- `SimPresenter` consume Canales A/B/C/D; **nunca** escribe en el Core.
- Interpolación con **retraso de 1 tick** (latencia [1,2) ticks); sin extrapolación.
- Pool de hormigas por `antId` (slots estables, reutilización en muerte); GPU
  instancing para escala (F5); animación procedural por desplazamiento.
- Feromonas: una `RenderTexture` RGBAHalf por colonia (R=Food, G=Home, B=Alarm);
  subida por tiles de 64×64 con presupuesto de 16 tiles/frame.
- HUD: tarjeta por colonia (chip de estado por runway, stock, crías, fitness,
  diversidad), gráficas 1 Hz desde `MetricFrame`, controles de simulación,
  inspector de hormiga (19 sensores + 6 decisiones crudas vs validadas + capa
  oculta / grafo NEAT), alertas, biblioteca de cerebros.
- Cambio 2D→3D: cambia solo el adaptador (materiales, rig); los contratos y el HUD
  (screen-space) permanecen intactos.
