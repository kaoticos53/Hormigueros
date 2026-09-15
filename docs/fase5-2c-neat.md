# F5.2c — NEAT: topologías que evolucionan (.antgenome v2)

Estado: **EN CURSO — rodaja 1 HECHA** (2026-09-15) · predecesores: [`fase5-2a-atta.md`](fase5-2a-atta.md)
y [`fase5-2b-eciton.md`](fase5-2b-eciton.md) (cerrados) · padre:
[`fase5-plan.md`](fase5-plan.md) §3.4 · estado global:
[`estado-proyecto.md`](estado-proyecto.md).

## 1. El problema

Tres hitos de especies demostraron que el **genoma es portable entre cuerpos**
(Atta §8ter, Eciton §8quater/§8quies) pero el cerebro sigue siendo una MLP de
topología fija: `sizes[]` congelado en el contrato IBrain v1 (**19 sensores →
capas ocultas → 6 salidas**). Consecuencias medibles:

1. **La mutación solo mueve pesos.** Tres fases de pre-entrenamiento (Fase 3)
   y dos cadenas de refinado nunca crearon un circuito nuevo: el relevo se
   resolvió apretando coeficientes de shaping, no añadiendo capacidad.
2. **La diversidad es un número, no una forma.** `DistanceTo` (media de |Δw|)
   devuelve 1.0 entre topologías distintas — proxy v1 que F5.2c reemplaza por
   distancia genómica real (disjoint/excess + pesos, la fórmula NEAT clásica).
3. **El jugador ve pesos, no historia.** El mini-grafo de F5.0 renderiza la
   MLP fija; sin topología evolucionada no hay "cerebro que crece" que
   enseñar, que es LA promesa del modo evolución.

NEAT (Stanley & Miikkulainen, 2002) resuelve exactamente esto: mutación
estructural (añadir nodo/conexión), **innovation numbers** para cruzar padres
con topologías distintas sin perder circuitos, yespeciation por distancia genómica para que el injerto estructural no muera
al instante en el pool.

## 2. Decisiones de diseño cerradas

| Decisión | Alternativas | Elección y por qué |
|---|---|---|
| Implementación NEAT | librería externa vs propia | **Propia**: el Core no admite dependencias con RNG no determinista; NEAT en sí es ~600 líneas y todo lo crítico (orden canónico, re-innovación) es de todas formas código nuestro |
| Contrato IBrain | v2 (nº canales cambia) vs v1 congelado | **v1 congelado**: los 19/6 canales no se tocan; NEAT añade NODOS ocultos, nunca sensores ni salidas. Así Atta/Eciton/Lasius y todos los pools v1 siguen validando sin migración |
| `.antgenome` | extensible v1 vs v2 nuevo | **v2 con `formatVersion=2`**: el lector v2 ACEPTA v1 (lo convierte a NEAT equivalente en carga); el v1 rechaza v2 — el mismo patrón v3→v4 ya probado en `.antsave` |
| Cruzar topologías distintas | rechazar vs align-by-innovation | **align-by-innovation**: las conexiones se emparejan por nº de innovación; los genes disjoint/excess van al padre con mejor fitness (empate: padre A) |
| Speciation | sí vs un solo pool | **Sí, con umbral único ajustable**: sin especies, la primera mutación estructural (fitness ≈ padre) muere en la selección truncada; ya tenemos `GenomePool` donde colgarla |
| Capas vs grafo | MLP con capas vs grafo arbitrario | **Grafo arbitrario acíclico con orden topológico cacheado**: la activación de `MlpBrain` es por capas; el NEATBrain ordena una vez en construcción (el grafo puede ser recurrente a FUTURO — v2 lo reserva con un flag, v2.0 solo feed-forward) |
| Tope estructural | ilimitado vs tope | **500 nodos / 2000 conexiones** (los del plan): una hormiga no necesita más, y acota el costo de orden topológico y del canal F |

## 3. El genoma v2

### 3.1 Genes

```csharp
public sealed record NodeGene(int Id, NodeKind Kind, float Bias, sbyte Act)
//   Kind: Input(0) | Hidden(1) | Output(2).  Act: 0=relu 1=tanh 2=sigmoid
//   Los ids de Input son 0..18 (fijos por el contrato v1, en el orden
//   canónico de AntSensorChannel) y los de Output 19..24. Los Hidden se
//   asignan desde 25 en adelante, monotónicos por pool.

public sealed record ConnGene(int Innovation, int From, int To,
                              float Weight, bool Enabled)
//   From/To son ids de NodeGene. Enabled=false = gen marcado (NEAT clásico:
//   nunca se borra, se apaga) — preserva el histórico para el alineamiento.
```

### 3.2 Invariantes (validadas en carga, excepción en construcción)

1. **Sin ciclos** (v2.0): el orden topológico debe existir; se valida con Kahn
   en la deserialización y tras cada mutación estructural.
2. **Ids acotados**: `0 ≤ From/To < Nodos.Count`, todo nodo referenciado existe.
3. **Sin duplicados**: no hay dos `ConnGene` con el mismo `(From,To)` activo,
   ni dos nodos con el mismo `Id`.
4. **Tope 500/2000**: `Nodos.Count ≤ 500`, `Conns.Count ≤ 2000`.
5. **Contrato v1 intacto**: exactamente 19 Input con ids 0..18 y 6 Output con
   ids 19..24; sus canales corresponden 1:1 con `AntSensorChannel`.

### 3.3 Innovation numbers y re-innovación determinista

El problema central del NEAT multi-importación: el pool A y el pool B añaden
independientemente una conexión `(3, 12)` — ¿es el MISMO circuito o dos? La
solución clásica (contador global compartido) exige estado compartido entre
partidas, lo que rompe el determinismo por semilla.

**Elección: innovación LOCAL al genoma + re-innovación canónica al importar.**

- **En vivo (durante el entrenamiento/partida):** el pool lleva
  `_nextInnovation` y un diccionario `(from,to) → innovation` de la población
  actual. Dos padres del MISMO pool que mutaron la misma conexión nueva
  comparten innovation → el crossover los alinea. El diccionario se reconstruye
  al inicio de cada generación desde los genes existentes (no hay estado que
  persista entre generaciones: misma semilla ⇒ misma secuencia completa).
- **Al importar (`--seed-pool`):** los genomas del archivo se **re-numeran
  canónicamente**: todas las conexiones de todos los genomas del archivo se
  ordenan por `(from, to)` y reciben innovations 1..K consecutivos — igual
  estructura ⇒ igual número en todo el archivo, distinta ⇒ distinto. El orden
  de carga del archivo (mérito descendente, ya canónico en v1) no afecta al
  resultado. **Dos importaciones del mismo archivo producen pools bit a bit
  idénticos; dos archivos con los mismos grafos en distinto orden de genomas
  también.** El diccionario de la primera generación posterior a la importación
  absorbe los innovations canónicos como estado de partida.

La re-innovación es la pieza que hace el formato portable SIN contador global:
el innovation number solo necesita ser consistente DENTRO del pool activo, y
el importador lo garantiza por construcción.

### 3.4 Operadores (todos `ref DeterministicRandom`, sin RNG nuevo)

| Operador | Regla | Consumo RNG |
|---|---|---|
| Peso | como hoy: gaussiana σ por gen activo | 2 doubles/gen |
| Bias | gaussiana sobre `NodeGene.Bias` de los Hidden | 2 doubles/nodo |
| Toggle | invertir `Enabled` de una conexión activable al azar | 1 int |
| AddConn | par (nodo→nodo) aleatorio SIN ciclo y sin duplicado; si no existe candidato tras 20 intentos, no-op | 2 ints/intento |
| AddNode | partir la conexión más profunda (orden topológico) de las activas: la vieja se desactiva (innovation conservado), la nueva entra con peso 1 y la de salida con el peso viejo | 2 ints |
| Crossover | align-by-innovation (§3.3); disjoint/excess del mejor padre; empate → padre A | 1 bool/gen |
| Mutación estructural | probabilidades p_add_conn=0.03, p_add_node=0.02, p_toggle=0.01 por genoma/generación (arranque; ajustables por constantes del descriptor) | 3 doubles/genoma |

**Fase de arranque (warm-start v1→v2):** un genoma v1 se convierte a v2 como
grafo completo de su `sizes[]` (todas las conexiones activas, innovations
canónicas por orden `(from,to)`). La conversión es EXACTA: el NEATBrain del
genoma convertido produce las mismas activaciones que el `MlpBrain` original
(test de paridad obligatorio).

### 3.5 Distancia genómica y especiation

```
δ = c1·(E/N) + c2·(D/N) + c3·W̄      N = max(nodos conn de ambos), c = 1.0/1.0/0.4
```

E=excess, D=disjoint (por innovation), W̄=media de |Δw| de los genes alineados.
`GenomePool` agrupa por δ < δt (δt=1.0 inicial, constante ajustable): cada
especie comparte su cuota de élites y cría proporcional a su fitness medio
(sharing NEAT clásico). Un genoma sin especie compatible funda una nueva.

## 4. Formato `.antgenome` v2

```
magic "ANTGENOM" (8) · formatVersion u32 (=2) · contractVersion u32 (=1) ·
count u32 · nombre · speciesHint · originSeed u64 · generation u32 ·
bestFitness f64 ·
por genoma:
  nNodes u16 · nConns u16 ·
  por nodo:   id u16 · kind u8 · bias f32 · act u8          (8 B)
  por conn:   innovation u16 · from u16 · to u16 · weight f32 · enabled u8 (12 B)
  fitness f64
SHA-256 (32) de todo lo anterior
```

- **Orden de nodos/conexiones: canónico** (nodos por id, conexiones por
  innovation) — un mismo grafo serializa siempre igual, lo que hace que el
  hash del archivo sea estable y el diff del `.antgenome` tenga sentido.
- **u16 para ids/innovations**: suficiente para el tope de 500/2000 con
  margen, y mantiene el archivo compacto (un pool de 60 genomas de ~120
  conexiones ≈ 100 KB, vs 34 KB en v1).
- **El lector v2 acepta v1** convirtiéndolo a v2 en memoria (grafo denso de
  su `sizes[]`); **el lector v1 rechaza v2** con error claro. El writer por
  defecto escribe v2; se conserva la ruta v1 solo para tests.
- `GenomeImportInfo` (F4.3, cuarentena) gana campos: `FormatVersion`,
  `NodeCount`, `ConnCount` — la tarjeta de importación muestra la forma del
  cerebro antes de sembrar.

## 5. Cerebro y telemetría

- **`NeatBrain : IBrain`**: `ContractVersion = 1` (los canales NO cambian).
  Orden topológico calculado una vez en el constructor (Kahn), cacheado en un
  array plano; la activación es un solo pase lineal — el costo por tick es
  comparable a la MLP de 1 capa oculta y muy inferior a 2 ocultas.
- **Canal F generalizado**: `MlpAsciiGraph` se generaliza a grafos arbitrarios
  (capas = profundidad topológica del nodo; los ids fijos 0..24 anclan las
  columnas de entrada/salida y los Hidden se colocan por profundidad). La
  activación base64 del canal F no cambia de formato: solo el grafo que la
  explica.
- **Canal A / inspector**: `genome fingerprint` (ya existe para linaje) pasa a
  incluir un resumen topológico `n/h/c` (nodos ocultos/conexiones activas) —
  el linaje de cerebros ahora muestra cerebros que CRECEN entre generaciones.
- **`.antsave`**: sin cambios (los saves serializan pesos del cerebro activo;
  el NEATBrain serializa su lista plana de pesos activados — se añade a
  `WorldSimSave` la misma disciplina de versión que ya usamos: sin NEAT en el
  mundo guardado, el formato no se toca).

## 6. Rodajas

| # | Rodaja | Contenido | Criterio |
|---|---|---|---|
| 1 | **Genes + cerebro** | `NodeGene`/`ConnGene`/`NeatGenome`/`NeatBrain` + conversión exacta v1→v2 | paridad de activaciones MLP↔NEAT bit a bit; Kahn valida aciclicidad; suite verde, 5 pins intactos |
| ^ | **Sonda de economía (2026-09-15)** | pool warm-v2 completo convertido: 24 genomas, grafo 33 nodos / 200 conexiones cada uno (8·19+6·8, denso — la conversión no añade nodos) | coste de activación medido (200 k evals, mejor de 3): MLP 516 ns/eval vs NEAT 810 ns/eval ⇒ **1.56× más lento** el intérprete de grafo sobre el denso. Aceptable: el cerebro es una fracción del tick de mundo, y los grafos EVOLUCIONADOS que podan conexiones (Enabled=false se salta) pueden quedar MÁS RÁPIDOS que el MLP fijo. Los topes 500/2000 dejan margen de 2.5× en conexiones antes de tocar el coste |
| ^ | **HECHO (2026-09-15)** | `NeatGenome` (invariantes 1–6, `FromMlp`, `DistanceTo`), `NeatBrain` (Kahn menor-id, activación bias-primero con fuentes por id ascendente, canal F layout canónico) | 13 tests: paridad bit a bit (200 semillas × 4 topologías + genomas reales warm-v2, decisiones Y activaciones), invariantes, determinismo, distancia. Descubrimiento colateral: el registro de activaciones del `MlpBrain` tenía un defecto de offsets (BlockCopy en bytes con offset float) — corregido, canal F v1 ahora emite el layout documentado; 328/328 tests, 5 pins intactos |
| 2 | **Operadores** | mutaciones estructurales + crossover align-by-innovation + distancia genómica | determinismo (misma semilla ⇒ misma secuencia); el crossover no pierde el circuito del mejor padre (test del clásico XOR de NEAT adaptado) |
| 3 | **Especiation en GenomePool** | agrupación por δ, cuotas por especie, `GenomePool.Stats` con nº de especies | una corrida de 200 generaciones con mutación estructural activa NO colapsa a 1 especie (mín 3 sostenidas) |
| 4 | **Formato v2** | writer/reader + acepta-v1 + re-innovación canónica al importar + campos de cuarentena | roundtrip bit a bit; dos importaciones del mismo archivo ⇒ pools idénticos; `GenomeImportInfo` con la forma |
| 5 | **Arena + transferencia** | `ArenaEvaluator`/`CurriculumTrainer` aceptan NEATGenome; warm-start de warm-v2 v1→v2 con mutación estructural activa | sonda: el pool NEAT alcanza el fitness del pool v1 en ≤ 2× generaciones y SUPERA una métrica estructural (nº de conexiones activas de los élites cambia) |
| 6 | **Canal F generalizado + inspector** | `MlpAsciiGraph` → grafo arbitrario; linaje con resumen n/h/c | el grafo del mejor cortador de la partida canónica Atta se renderiza headless y difiere del MLP fijo |
| 7 | **Cierre** | partida canónica con grafo NEAT visible + 6º pin CI + docs | criterio de cierre del plan §3: invasión Eciton vs Atta sembrada con pool NEAT propio, grafo del mejor cortador visible |

## 7. Riesgos y mitigaciones

1. **NEAT estanca la evolución** (el riesgo abierto del plan): las rodajas 3 y
   5 tienen métricas de NO-estancamiento explícitas; el fallback documentado
   es desactivar la mutación estructural por constantes (vuelve a ser MLP
   con pasos extra) sin tocar formato ni código.
2. **Costo de orden topológico**: 500 nodos/2000 conexiones acotan a Kahn a
   O(V+E)=2500 por genoma construido, una vez por genoma (no por tick). El
   hash invariante del mundo se re-verifica en cada rodaja.
3. **Compatibilidad de pools v1**: todos los flujos existentes (`--seed-pool`,
   cuarentena, pins) aceptan v1 sin cambios — el riesgo de migración vive solo
   en el lector v2, cubierto por roundtrips.
4. **El crossover destruye adaptaciones**: align-by-innovation + especiation
   es exactamente el mecanismo NEAT para esto; el test de rodaja 2 lo verifica
   explícitamente con un circuito plantado a mano.
5. **Recurrencia accidental** (una AddConn a un ancestro): Kahn la rechaza y
   el operador reintenta con otro par — nunca se escribe un grafo cíclico
   (v2.0 feed-forward; el flag de recurrencia del formato queda reservado).

## 8. Fuera de alcance (v2.x posterior)

- Recurrencia real (flags `Enabled` recurrentes con activación por estado).
- Hiper-neuroevolución (HyperNEAT/ES-HyperNEAT) y CPPNs.
- Innovación GLOBAL persistente entre archivos (la re-innovación canónica la
  hace innecesaria para el juego; si algún día los pools se cruzan ENTRE
  archivos en vivo, se añade un mapeador de innovations en la importación).
- NEAT en depredadores sin colonia (F5.2d): comparte `NeatBrain` tal cual.
