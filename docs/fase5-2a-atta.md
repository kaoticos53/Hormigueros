# F5.2a — Diseño: Atta, la cadena cortar → transportar → hongo

Documento de DISEÑO del primer hito de especies (plan de Fase 5, §3.1):
ítems compuestos (hoja = N cortes) y un segundo objetivo de reserva (hongo).
Estado del que parte: Fase 5.1bis cerrada — 276/276 tests, 3 pines de hash
en CI, bucle de jugador completo y multi-visor verificado.

**La regla que gobierna todo el diseño**: los 19 sensores y las 6 salidas
son COMUNES a todas las especies (`arquitectura.md`, `SpeciesDescriptor`) —
un genoma `.antgenome` debe seguir siendo portable entre Lasius, Atta y
Eciton. Por eso la cortadora no necesita un cerebro distinto: necesita un
MUNDO donde la misma política descubra una economía diferente.

## 1. La decisión clave: «cortar» ES «coger», en un ítem compuesto

Hoy `WorldSim.Act` hace, en el gating de `Interact`:

- sin carga: pickup del ítem más cercano en `PickupRadius` → el ítem se
  elimina y `ant.LoadValue = item.Amount` (ítems simples de 4–6 ep);
- con carga: descarga al nido dentro de `NestRadius` → `RecordInflow`.

La propuesta: **la mecánica no cambia, el ítem sí**. Un ítem compuesto
(hoja) tiene `CutsLeft > 0`; el pickup sobre una hoja NO la elimina: le
resta un corte y la hormiga se lleva un FRAGMENTO de valor
`Amount / CutsInicial`. Con esto:

- el cerebro no distingue especies ni ítems: `Interact` cerca de comida —
  lo que ya sabe hacer; la hoja simplemente rinde varios viajes;
- Lasius y Eciton sobre una hoja se comportan como cortadoras ocasionales
  (realismo: es lo que hacen las generalistas);
- la fitness de entrenamiento existente (reward por pickup + unload por ep)
  sigue funcionando SIN cambios para la arena.

## 2. Cambios de mundo (Core)

### 2.1 Ítem compuesto — `FoodItem`

```
FoodItem:
  + public int CutsLeft;        // 0 = ítem simple (comportamiento actual)
  + public int CutsInitial;     // para valorar el fragmento: Amount / CutsInitial
```

- **Spawn de hojas**: `WorldSim` gana `LeafFraction` (0..1, **default 0**).
  Con `LeafFraction = 0` el mundo es byte a byte el actual (los 3 pines de
  CI NO se mueven); con `f > 0`, cada `SpawnItem` decide con `_worldRng`
  hoja vs simple: la hoja vale `8–14 ep` con `CutsInitial 3–5`
  (fragmento ≈ 2.7–3.5 ep — carga normal, el sensor `foodSize` ya
  normaliza contra `MaxValue = 10` y el fragmento no lo desborda).
- **Consumo**: el corte que agota `CutsLeft` elimina el ítem y emite
  `ItemConsumed` (además del `Pickup` del fragmento) — la métrica de
  consumo del mundo no se rompe.
- **Hash** (`HashLine`): `CutsLeft` entra en el bloque de ítems. En mundos
  con `LeafFraction = 0` todos los ítems tienen 0 ⇒ hash idéntico al de hoy.

### 2.2 El hongo — segunda reserva de la colonia

`Colony` gana:

```
Colony:
  + public float Fungus;        // ep de hongo (0 = especies sin hongo)
  + public float FungusMax;     // de la especie (Atta: 60)
```

Flujo (solo tiene sentido para Atta; Lasius/Eciton mantienen `Fungus = 0`
y el camino actual):

1. **Descarga de fragmento al nido** → `Fungus += load × LeafEfficiency`
   (0.75: pérdida de procesado) **en vez de** `RecordInflow` directo al
   stock. El evento `Unload` se emite igual (el relevo y sus métricas no
   distinguen destino); nuevo evento `FungusFed` (canal B) para la vista.
2. **Digestión** (en el paso de `ColonyController`, ANTES de decidir cría):
   `tasa = DigestionRate × (Fungus / FungusMax)`, con `DigestionRate` Atta
   ≈ 0.4 ep/s a hongo lleno; `Fungus -= digerido` y **ese caudal alimenta
   `RecordInflow(digerido)`** — el `InflowEma`, el gate de puesta y toda la
   dinámica demográfica calibrada en F1–F3 siguen leyendo la MISMA señal.
   La sima del hongo lenta es el cuello de botella real de una cortadora:
   forrajeo rápido, digestión lenta.
3. **Hambre extrema**: bajo `TCrit`, el controlador puede morder el hongo
   directamente (hongo → stock sin tasa, penalizando `Fungus`) — el hongo
   como reserva de emergencia, como en la especie real. Recorte mínimo:
   solo si los tests de balance lo piden; se deja anotado, no implementado.

Constantes nuevas de `SpeciesDescriptor.Atta`:

```
FungusMax       = 60f     // ep
DigestionRate   = 0.4f    // ep/s a hongo lleno
LeafEfficiency  = 0.75f   // ep de hongo por ep de hoja descargada
```

`LasiusNiger` y `Eciton`: `FungusMax = 0` — sin hongo, `Fungus` permanece
0 y la descarga va al stock como hoy.

- **Hash**: `Fungus` entra en el bloque de colonia. Colonias sin hongo
  aportan 0.0 ⇒ hash idéntico en los mundos actuales.

### 2.3 Determinismo

- La elección hoja/simple en `SpawnItem` consume `_worldRng` SOLO cuando
  `LeafFraction > 0`; con 0 no hay tirada ⇒ mismos hashes de hoy.
- La digestión es una función determinista del estado (sin RNG).
- `.antsave`: **v2** — fungus por colonia y `CutsLeft/CutsInitial` por
  ítem. El patrón v1→v2 ya probado (carga de v1 rechazada con mensaje
  claro). Los checkpoints viejos no tienen hongo: se rechazan.

## 3. Cambios de contrato (stream + HUD)

| Canal | Cambio | Nota |
|---|---|---|
| A (`items`) | campo `cuts` por ítem (ausente/0 = simple) | el presenter dibuja la hoja más grande y con «mordiscos» (un anillo o sprite por corte restante) |
| A (`colonies`) | `fungus`, `fungusMax` | segunda barra en la tarjeta Atta (mismo estilo de stock bar, color hongo) |
| B | eventos nuevos: `LeafCut = 13`, `LeafDepleted = 14`, `FungusFed = 15` | `Pickup`/`Unload` NO cambian de semántica (relevo intacto) |
| C | contadores por colonia: cortes y fungus-fed en la ventana | la tarjeta puede mostrar «3 cortes / 2 descargas al hongo» |
| F (activaciones) | sin cambios | el cerebro es el mismo |

**Alertas (AlertDeriver)**: el semáforo de relevo NO cambia (mide
pickup→unload). Alerta nueva opcional para Atta: `fungus-vacio` (hongo en 0
con stock bajo > X s) — derivada del canal C, misma pauta de rate-limit.
Se especifica aquí, se implementa con el HUD si el humo la pide.

**Modelos puros de Unity** (`GameStreamParser`, tarjeta, sparkline): campos
nuevos con defaults tolerantes (ausencia = 0) para que streams viejos sigan
parseando — los fixtures actuales no se regeneran por esto.

## 4. CLI y escenarios

- `--leaf-fraction f` (modes `world` y `game`; 0 default).
- `--species lista` (coma, una por colonia, `lasius` default):
  `--species atta,lasius` = colonia 0 Atta, colonia 1 Lasius — la partida
  comparativa del criterio de cierre. Los nombres aceptan el prefijo
  (`atta`, `lasius`, `eciton`).
- La autodetección de pools del picker (F4.5) gana una dimensión: los
  presets declaran especie (`PoolPresets` + campo en la tarjeta).
- **Pre-entrenamiento (abierto)**: la arena pone `TargetItems = 0` y siembra
  la suya; entrenar cortadoras requiere sembrar hojas (`LeafFraction` de la
  arena). NO es parte de F5.2a: un pool Atta puede nacer por transferencia
  (genomas Lasius en cuerpo Atta — el cerebro es portable) y la evolución
  en vivo ajusta. Anotado como F5.2a-bis si la transferencia falla.

### 4.1 SONDA DE TRANSFERENCIA — VALIDADA (2026-09-13)

Warm-v2 (entrenado en cuerpo Lasius) sembrado en la colonia 0, cuerpo y
mundo variados, 12 000 ticks, seed 42, colonia 1 Lasius sin sembrar como
control de entorno (sonda borrada tras su uso; resultado registrado aquí):

| mundo | pickups | unloads | cortes | fitness medio |
|---|---|---|---|---|
| Lasius sin hojas (control) | 21 | 11 | 0 | **13,15** |
| Atta sin hojas (transfer pura) | 19 | 12 | 0 | 10,70 |
| Atta hojas 100 % | 19 | 11 | 17 | 7,53 |
| Lasius hojas 100 % | 22 | 13 | 20 | 9,62 |

**Veredicto: portable.** El pool warm-v2 en cuerpo Atta descarga lo mismo
o más que en su cuerpo de entrenamiento (12 vs 11 unloads) — las constantes
de especie (VMax, sensores, vida) no rompen la política. Con hojas, ambas
especies cortan de inmediato (17–20 cortes, cero entrenamiento): el corte
= `Interact` funciona con cerebros existentes. El fitness medio cae con
hojas (fragmentos más pequeños ⇒ menos ep por viaje) — es el precio
esperado, no un defecto de transferencia. F5.2a-bis (currículo Atta) queda
postergado: la evolución en vivo partiendo de warm-v2 es suficiente.

## 5. Rodajas de implementación (cada una con su test)

1. **F5.2a.1 — Ítems compuestos** — ✅ HECHO (2026-09-13): `FoodItem.CutsLeft/
   CutsInitial`, corte en el pickup de `Act`, `LeafFraction` en spawn (por
   constructor — el spawn inicial nace ANTES que el inicializador de objeto),
   eventos `LeafCut=13`/`LeafDepleted=14`, hash SOLO con hojas (mundos sin
   hojas producen los mismos bytes — pines de CI intactos), `.antsave` v3
   (cuts ×2 por ítem), canal A con `[id,x,y,amount,cutsLeft,cutsInitial]` en
   hojas y 4 elementos en simples (tolerante), CLI `--leaf-fraction` (modes
   `world`/`game`). 10 tests (`CompoundItemTests`); 286/286 suite + 3 pines
   de CI verificados. Humo real sembrado (warm-v2, seed 42, 6000 ticks,
   hojas 100 %): 14 cortes → 3 descargas, 9 hojas mordidas.
2. **F5.2a.2 — Hongo** — ✅ HECHO (2026-09-13): `Colony.Fungus/FungusMax`
   (Atta: 60 ep, nace VACÍO — la reina lo construye), descarga por especie
   (× `LeafEfficiency` 0.75, excedente sobre hongo lleno se pierde),
   digestión PROPORCIONAL al llenado como etapa 0 de `ColonyController` que
   entra por `RecordInflow` (la demografía calibrada lee la misma señal),
   eventos `FungusFed=15`/`FungusDigested=16`, fungus/fungusMax en canal A
   y `.antsave` v3, hash SOLO con hongo (colonias sin hongo no alteran los
   bytes). 7 tests (`FungusTests`); 293/293 suite, 3 pines de CI intactos.
   Humo real: `--species atta,lasius --leaf-fraction 1.0 --seed-pool
   warm-v2` (7200 ticks) — 12 cortes → 3 descargas → 3 FungusFed → 1898
   ticks de digestión; lasius sin hongo (fungusMax 0). `--species` (parte
   de 5.2a.4) se adelantó aquí para poder ejecutar el humo.
3. **F5.2a.3 — Contratos**: canales A/B/C (campos + eventos), parser puro
   de Unity con defaults, tests de contrato contra stream real.
4. **F5.2a.4 — CLI y escenarios**: `--leaf-fraction`, `--species`,
   presets por especie, nuevos pines de hash para la partida Atta canónica
   (fixture nuevo, no sustituye a los 3 existentes).
5. **F5.2a.5 — Humo visual**: partida Atta vs Lasius en el multi-visor;
   sonda: la tarjeta Atta muestra su barra de hongo y el canal B trae
   `LeafCut` — criterio de cierre abajo.

## 6. Criterio de cierre

Una partida `game` de 2 colonias (`--species atta,lasius`, la Atta sembrada
con un pool de la cadena, `--leaf-fraction 0.35`):

1. los 3 pines de CI actuales VERDES (mundos sin hojas idénticos) + 1 pin
   nuevo para la partida Atta canónica;
2. la colonia Atta alcanza semáforo de relevo verde con la cadena completa
   visible: `LeafCut` → portador → `Unload` + `FungusFed` → digestión →
   cría nueva (eclosión post-hongo);
3. la tarjeta Atta muestra hongo; la Lasius, no;
4. suite completa verde (≥ 285 tests) y docs actualizadas
   (`arquitectura.md`, `especificaciones.md`, este doc a «HECHO»).

## 7. Decisiones tomadas (y por qué, estilo plan §5)

| Decisión | Opciones | Elección |
|---|---|---|
| Mecánica de corte | salida nueva del cerebro vs reusar `Interact` | **reusar `Interact`**: el genoma sigue portable; la novedad vive en el ítem, no en el cerebro |
| Hoja: nuevo tipo vs campo | clase nueva vs `CutsLeft` en `FoodItem` | **campo**: un solo tipo evita bifurcar spawn/hash/save/stream por algo que se modela con un entero |
| Hongo: reserva vs conversión directa a stock | stock único con retardo vs segunda reserva visible | **segunda reserva**: es el objetivo visual y la decisión económica del jugador; la digestión alimenta el stock existente para no recalibrar la demografía |
| Digestión | constante vs proporcional al llenado | **proporcional** (`rate × llenado`): el hongo vacío no digiere — el cuello de botella real de la cortadora |
| Hojas en el mundo | por especie (solo Atta las ve) vs global (`LeafFraction`) | **global**: el mundo no conoce la dieta de cada colonia; la generalista que corte hojas también se alimenta — competencia real |
| `.antsave` | v1 extendido vs v2 | **v2** con rechazo de v1: el patrón ya probado y el estado cambia de forma |

## 8. Qué NO es F5.2a

- Depredadores, alarma ofensiva, combate (F5.2b — Eciton).
- NEAT / `.antgenome` v2 (F5.2c).
- Currículo de pre-entrenamiento específico Atta (abierto, §4).
- Balance fino: se calibra con tests de balance contra el benchmark de
  pools cuando exista el primer pool Atta, no a mano.
