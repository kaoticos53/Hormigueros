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
- **Umbrales de escasez** (runway en segundos; Lasius): `T_safe 40` → puesta a tope;
  `T_ooph 25` → oofagia (reabsorber huevos, η ≈ 0.3); `T_cann 15` → canibalismo
  larval (las más débiles primero, `η_larva·nutr`); `T_crit 8` → racionar adultas
  y, en último recurso, pupas. Orden de sacrificio: huevos → larvas débiles → pupas.
- Alimentación priorizada por paso: reina → adultas → larvas (si `T_runway > T_cann`);
  reparto equitativo; nodrizas limitan la tasa de alimentación larval.

## 4. Evolución (GA)

- Pool élite top-K; los **mejores candidatos se usan al nacer**; el fitness de por
  vida alimenta el acervo al morir. Demérito por decisiones inválidas (NaN).
- **Inmigración con cuarentena**: importación = comando con tick; los inmigrantes
  ocupan eclosiones dentro de una ventana (120 s) en orden de mérito de origen;
  entran a la élite si fitness ≥ p50 (modo estricto) o, con `D < D_floor` (0.05),
  fitness ≥ p25 **y** novedad > p75 (modo sensible a diversidad).
- **Diversidad** comportamental: 64 sondas canónicas × huella de salidas; distancia
  media sobre 256 pares muestreados con RNG determinista; comparable entre especies
  y entre MLP/NEAT. **σ adaptativa**: `σ·(1 + 3·(D_floor − D)/D_floor)` bajo el suelo.
- **NEAT (F5)**: genes estructurales en `.antgenome` (nodos con bias/activación,
  conexiones con peso/enabled/innovation, orden canónico por innovation); re-innovación
  determinista al importar (linaje extranjero con bloque local disjunto); feed-forward
  v1 con topes (500 nodos / 2 000 conexiones) y validación estructural estricta.

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
