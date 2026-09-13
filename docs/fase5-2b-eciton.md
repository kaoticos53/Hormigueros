# F5.2b — Eciton: incursiones y combate (diseño)

Estado: **DISEÑO CERRADO** (2026-09-13) · predecesor: [`fase5-2a-atta.md`](fase5-2a-atta.md)
(cerrado) · padre: [`fase5-plan.md`](fase5-plan.md) §3.1/§3.2.

## 1. El problema

F5.2a demostró que una especie puede cambiar la ECONOMÍA sin cambiar el
cerebro (hoja = ítem con `CutsLeft`, hongo = segunda reserva). Eciton exige
lo que Atta no necesitó: que una colonia haga daño a OTRA. Hoy el mundo no
tiene ninguna noción de ello:

- Las hormigas son intocables entre sí: `ApplyDeaths` solo mata por edad o
  inanición; no hay colisión ni daño hormiga-hormiga.
- La capa `Alarm` existe (canal 2, τ½ = 1 s, se lee en los sensores 11–13)
  pero es INFORMATIVA: depositarla no hace nada a quien la cruza.
- Los objetivos visibles son ítems y el propio nido: `AntSenses` nunca mira
  a otra hormiga, y la colonia de otro es invisible.
- Canal B no tiene ningún evento de interacción hostil.

Lo que NO se repite del diseño de Atta: aquí sí toca el cuerpo (`Ant`), el
paso de hormiga (`Act`) y la demografía (muertes que no son de edad ni de
hambre). La lección que SÍ se aplica: la novedad vive en el MUNDO, no en el
cerebro — el contrato 19-entra/6-sale permanece intacto y los `.antgenome`
siguen portables.

## 2. Decisión estructural: Eciton es una especie, no un agente nuevo

El plan §3.2 hablaba de "depredadores como agentes no-colonia". Se descarta
para F5.2b y queda para un hito futuro (F5.2b-bis si acaso):

- Un agente sin colonia necesita canal A propio, cerebro propio en la UI,
  inspección, cuarentena… todo el costo del F4 otra vez, para UN actor.
- Eciton como ESPECIE reusa todo: nido, reina, cría, pool genético,
  semáforo de relevo, inspección, cifras del HUD. Lo que cambia es SU
  economía: robar stock ajeno en vez de forrajear ítems.
- La partida canónica "invasión" es la misma partida de siempre con
  `--species lasius,eciton` — el picker no necesita saber nada nuevo.

## 3. Mecánicas de incursión (el mundo, no el cerebro)

### 3.1 Detección de presa: sensor canal 14 reconvertido como «Enemy»

Los 19 canales están congelados (contrato F1). Eciton no añade canales:
REUSA el par que menos información lleva hoy. Inspección de
`AntSensors`: los canales de proximidad (14 ProxFront, 15/16 izq/der) son
duplicados (los tres valen lo mismo: distancia al borde). Propuesta:

- **Canal 14 (ProxFront) pasa a significar "hormiga enemiga más cercana"**,
  normalizado `1 − dist/VisionRadius` en el marco local (adelante/atrás no
  aplica: es una magnitud). Canales 15/16 (ProxLeft/ProxRight) mantienen
  la pared — el borde sigue siendo el único obstáculo físico.
- Para NO romper genomas existentes: el significado se activa SOLO cuando
  hay ≥ 2 colonias en el mundo y la especie del observador es Eciton; en
  un mundo de una colonia (todos los benchmarks, arenas, el fixture
  canónico) canal 14 sigue siendo la pared. Regla: `rivalCount > 0` por
  especie. Los pines CI no se mueven (todas las partidas fijadas son de
  una colonia o de especies sin Eciton).
- No se da dirección (solo magnitud): Eciton la recupera del GRADIENTE de
  la feromona ajena — ver 3.2. Magnitud sin dirección es exactamente lo
  que el sensor de comida da como `FoodSize`: precedente del contrato.

### 3.2 La alarma ofensiva: reutilizar la capa Alarm como rastro de invasión

La semántica cambia de "peligro para mí" a "rastro de mi incursión":

- Eciton deposita Alarm (su `QMaxAlarm = 2.4`, ya el doble que Lasius)
  mientras avanza fuera del nido — el mismo gesto que Lasius usa con
  FoodTrail al volver cargada. Los scouts dejan un rastro que la
  compañía sigue: el mecanismo de reclutamiento es EL MISMO que el
  forrajeo (FoodTrailDiff → Steer), ya aprendido por cualquier genoma.
- Las capas siguen siendo POR COLONIA (una hormiga solo lee las suyas):
  el rastro de Eciton no es leíble por la presa. Lo que la presa recibe
  es el SENSOR: canal 14 poblado por presencia enemiga (3.1).
- Para la presa, `AlarmCenter/Diff` (canales 11–13) cobran significado
  propio: Eciton deposita Alarm TAMBIÉN en su propia capa al herir, y el
  mundo — solo en mundos con Eciton — INYECTA alarma en la capa de la
  colonia asaltada cerca de cada herido (ver 3.4). Presa con genomas
  entrenados en Fase 3 ya tiene política de evasión de alarma si la
  evolución la seleccionó: la señal existe, el hueso está.

### 3.3 Daño: combate en el paso de hormiga

Regla determinista, sin RNG adicional (el combate es lo bastante
frecuente para que el orden de iteración sea la fuente de azar que ya es):

- **Condición**: en `Act`, tras el movimiento, si hay una hormiga de OTRA
  colonia a distancia ≤ `ContactRadius` (nuevo campo de especie:
  Eciton 6 u, resto 0 = sin combate), y el atacante no está en cooldown
  de interacción, hay un golpe.
- **Daño**: el atacante inflige `StrikeDamage` (nuevo campo: Eciton 0.35
  ep de capacidad de la presa) a la energía de la presa y roba
  `StealPerStrike` (Eciton 0.30) que TRANSPORTA como carga (`HasLoad`).
  La presa muere si su energía llega a 0: causa de muerte nueva
  `DeathCause.Combat = 2`.
- **Cooldown**: 0.5 s (el mismo `InteractCooldown`; nada nuevo).
- **Robo como forraje**: el botín viaja en el canal existente de carga —
  el portador robador vuelve a SU nido y lo descarga con el `Unload`
  de siempre, que hace `RecordInflow` en su colonia. La incursión es,
  literalmente, un forrajeo cuyo ítem es el stock ajeno: relevo,
  fitness, semáforo y métricas funcionan sin cambios.

### 3.4 Muerte y alarma en la presa

- Muerte por combate: `AntDied` con `Cause = 2` (canal B ya transporta
  `cause`; la UI ya muestra causa dominante en toasts). La hormiga muere
  SIN soltar carga al suelo (el botón del saqueo es su robador, no un
  ítem nuevo — evita bifurcar `FoodItem`).
- En cada golpe, el mundo inyecta `0.8 u` de Alarm en la capa de la
  colonia PRESA en la celda del golpe (τ½ = 1 s, se disipa en ~3 s):
  la colmena "siente" el ataque sin ningún canal nuevo.
- La presa NO daña de vuelta en F5.2b (retaliación = futuro ajuste de
  balance; el juego asimétrico es el diseño).

## 4. Contrato en canal B (y canales auxiliares)

### 4.1 Eventos nuevos (kinds 17–19)

| kind | nombre | payload | semántica |
|---|---|---|---|
| 17 | `Strike` | colonyId = ATACANTE, antId = atacante, X/Y = celda del golpe, cause = id de la presa | golpe de incursión (agregable en canal C) |
| 18 | `RaidInflow` | colonyId = la que ROBA, antId = portador, X/Y = su nido | descarga de botín en el nido saqueador (equivale a Unload para el relevo) |
| 19 | `StockRobbed` | colonyId = la SAQUEADA, antId = 0, X/Y = nido saqueado, cause = ep robados·100 | la víctima registra la pérdida (telemetría de tarjeta) |

`AntDied.cause = 2` (Combat) es la otra cara: la víctima se entera por
su propio canal de muertes, sin evento nuevo.

La codificación del stream no cambia (canal B ya lleva `[kind, col,
ant, x, y, cause]`); los parsers viejos ignoran kinds desconocidos si
los modelos puros validan por rango — nota para F5.2b.3: relajar esa
validación donde esté escrita.

### 4.2 Canal C

`MetricRecorder` añade por ventana de 1 s y por colonia:
`strikes` (golpes infligidos), `robbed` (ep perdidos, solo víctimas).
En el stream: bloque `raids:[[col, strikes, robbedEp],…]` bajo el mismo
patrón tolerante de `cutters` — solo si hubo actividad.

### 4.3 Canal A

Sin cambios estructurales: la carga robada es `HasLoad/LoadValue` de
siempre. La única adición tolerante: `strikes` por colonia en el bloque
de métricas del header (cero si no hay Eciton).

## 5. Demografía saqueada: el stock de la víctima

`StockRobbed` descuenta directamente `victim.Stock` (clamp a 0). No pasa
por `RecordInflow` de la víctima (eso es para ENTRADAS): la pérdida va
directa al stock, y el ColonyController la percibe por la bajada del
runway — la demografía existente reacciona sin recalibrar (menos stock ⇒
menos huevos ⇒ posibilidad de canibalismo si entra en TCann). Es el
mismo acople que la digestión de Atta, en espejo: la señal que la Fase 1
calibró, tocada por fuera.

## 6. `.antsave`

v3 → **v4**: se añaden `ContactRadius`/`StrikeDamage`/`StealPerStrike`
por especie (van en el descriptor, que ya se serializa por nombre +
constant), y los contadores de combate por colonia si deciden
persistirse (decisión: NO persistirlos — son telemetría de ventana, no
estado del mundo; v4 solo cambia por la tabla de constantes de especie).
Rechazo de v3: patrón establecido.

## 7. CLI y escenarios

- `--species lasius,eciton` ya funciona (5.2a.4); nada nuevo.
- La partida canónica de incursión (futura 4º… 5º pin): seed fijo,
  2 colonias `lasius,eciton`, la Eciton SIN sembrar (nace salvaje),
  `--ticks 7200 --grid 96`. Su hash se fijará como pin cuando las
  mecánicas estén estables (F5.2b.5).

## 8. Slicing (rodajas con su test)

1. **F5.2b.1 — Cuerpo del combate — HECHO (2026-09-13)**:
   `ContactRadius`/`StrikeDamage`/`StealPerStrike` en el descriptor (0 por
   defecto = sin combate), `TryStrike` en `Act` (después del movimiento,
   presa = la más cercana, empate por antId menor, vive en la ventana de
   interacción con cooldown 0.5 s y gating `WantsInteraction`), robo como
   carga (`LoadIsLoot`/`LootFromColony`), `DeathCause.Combat = 2` vía
   `Ant.DiedInCombat`, eventos 17/18/19 (RaidInflow se emite en el Unload
   del botín), alarma inyectada en la capa de la presa (0.8 u en la celda
   del golpe). `.antsave` v4: por hormiga se persisten `LoadIsLoot`,
   `LootFromColony` y `DiedInCombat`. 7 tests (`CombatTests`): daño con
   control gemelo, robo clampado al stock real, RaidInflow + descarga,
   muerte con causa 2, mundo sin Eciton invariante, cooldown/carga no
   golpean, roundtrip v4 con hash idéntico. **305/305 suite; los 4 pines
   de CI intactos** (el combate no consume RNG: solo muta por contacto
   geométrico, y sin Eciton no hay contacto).
2. **F5.2b.2 — Sensor canal 14**: modo `rivalCount > 0` (magnitud de
   enemigo más cercano) activo solo para Eciton en mundos multi-colonia;
   pines intactos. Tests: canal 14 con/sin rival, invariancia de hash en
   mundos sin Eciton, genoma portado de Lasius→Eciton que ROBA con política
   de forrajeo (sonda de transferencia, como §4.1 de Atta).
3. **F5.2b.3 — Contratos**: canal C `raids`, canal B kinds 17–19 en
   stream, parser puro de Unity con defaults tolerantes, `DeathCause=2`
   en toasts (contrato HUD §causa dominante). Tests de contrato contra
   stream real.
4. **F5.2b.4 — Demografía y balance**: `StockRobbed` descontando stock,
   reacción del controlador (runway/caída de puesta), calibración de
   `StrikeDamage`/`StealPerStrike` con partidas de invasión a 5 seeds:
   la Eciton salvaje debe poder MATAR una Lasius sin sembrar en ~40 %
   de las semillas y NO extermarla en todas (juego, no aniquilación).
5. **F5.2b.5 — Pin + humo visual**: 5º pin CI de la partida de invasión
   canónica; tarjeta de vista con línea `raids` (golpes/botín) en el
   multi-visor; criterio de cierre abajo.

## 8bis. Estado de las rodajas

| rodaja | estado |
|---|---|
| 5.2b.1 cuerpo del combate | ✅ HECHO (2026-09-13 — 305/305, 4 pines intactos) |
| 5.2b.2 sensor canal 12 | ✅ HECHO (2026-09-13 — 310/310, 4 pines intactos, sonda §8quater) |
| 5.2b.3 contratos (canal C raids + parser Unity) | 🔲 |
| 5.2b.4 demografía y balance | 🔲 |
| 5.2b.5 pin + humo visual | 🔲 |

### 8ter. SONDA DE TRANSFERENCIA A ECITON — VALIDADA CON MATICES (2026-09-13)

Warm-v2 (entrenado en cuerpo Lasius para FORRAJEAR) sembrado en la colonia
0 como Eciton, cuerpo Eciton con combate activo, colonia 1 Lasius sin
sembrar. 12 000 ticks, seed 42 (sonda borrada tras su uso; resultado
registrado aquí):

| escenario | strikes | robos | RaidInflow | pickups | unloads | c0 max → final |
|---|---|---|---|---|---|---|
| A: Lasius+pool vs Lasius (control) | 0 | 0 | 0 | 21 | 11 | 21 → **1** |
| B: Eciton+pool (transfer) | 2 | 2 | 2 | 19 | **13** | 18 → 0 (muere t8781) |
| C: Eciton salvaje | 6 | 6 | 5 | 1 | 5 | 18 → 0 (muere t8630) |
| D: B con nidos a 240 u | 2 | 2 | 2 | 20 | 13 | 18 → 0 |

**Hallazgos:**

1. **El genoma es portable y la política de interacción SE TRANSFIERE**:
   en cuerpo Eciton la colonia sembrada golpea y roba (2 strikes → 2 robos
   → 2 RaidInflow) SIN entrenamiento alguno, y su forrajeo no solo
   sobrevive sino que MEJORA (13 unloads vs 11 del control) — VMax 3.6 y
   sensor reach más corto favorecen la ida-vuelta. La sonda de F5.2a
   (portabilidad a Atta) se confirma para el segundo cuerpo nuevo.
2. **El combate emerge sin dirección**: la salvaje hace MÁS strikes (6) que
   la sembrada (2) — el azar del vagabundeo produce contacto, y el golpe
   paga fitness (RewardPickup/2). Nadie «busca» al rival: falta el sensor
   de F5.2b.2 para que la incursión sea DIRIGIDA y no casual.
3. **El riesgo de balance es la propia Eciton**: en B/C/D la colonia
   atacante muere de hambre ~t8700 (AdultUpkeep 0.045 + Sin semilla de
   stock tras el gasto inicial), aunque max población 18 y botín cobrado.
   La economÍa del botín (0.30 ep/robo) NO sostiene a la legionaria: el
   balance de F5.2b.4 (robo mayor, forrajeo Eciton viable, o botín que
   alimente más) decide si la invasión es juego o suicidio.
4. **Cero muertes de combate en 12 000 ticks**: StrikeDamage 0.35 sobre
   capacidades ~10 ep necesita ~29 golpes para matar una hormiga — el daño
   hoy hostiga pero no mata. F5.2b.4 recalibrará (más daño, más robo, o
   ambos).

**Veredicto: portable, con balance pendiente** — se autoriza F5.2b.2 (el
sensor dirigirá la incursión y multiplicará los strikes efectivos) y la
calibración económica queda anotada como el trabajo REAL de F5.2b.4.

### 8quater. SONDA DEL SENSOR DIRIGIDO — RESULTADO HONESTO (2026-09-13)

El sensor (F5.2b.2) pobló el canal 12 correctamente (verificado con
`EnemySensorTests`: magnitud exacta 1−dist/visión, pared intacta en los
canales laterales, gating por especie y por multi-colonia). La misma
sonda de transferencia (warm-v2 en cuerpo Eciton, seed 42, 12 000 ticks)
corrida CON sensor: **strikes=18, robos=1, RaidInflow=1**.

Comparación contra §8ter (B, sin sensor, MISMA seed y pool): strikes
2 → 18 (×9) pero robos 1 → 1. Lectura:

1. **El sensor multiplica el CONTACTO** (18 strikes vs 2): el canal 12
   poblado dispara la salida Interact del genoma importado — la política
   reacciona a la nueva entrada aunque nadie la entrenara para ella.
2. **NO multiplica el robo (1 → 1)**: casi todos los strikes caen sobre
   presas cuya colonia ya está en stock 0 (la víctima es pobre) o fuera
   de la ventana de interacción. Golpear sin robar es hostigar: el
   cuello de botella NO es sensorial sino ECONÓMICO — `StealPerStrike`
   0.30 sobre un stock que la víctima no repone, y `StrikeDamage` 0.35
   que no mata (0 muertes de combate en 12 000 ticks).
3. La sonda cruzada (§8ter B vs §8quater) compara mundos cuyo hash DIFIERE
   por diseño (el sensor cambia decisiones ⇒ mundo distinto): la semilla
   igual solo garantiza el mismo PUNTO de partida.

**Veredicto: el sensor funciona; el robo ya no es su problema.** El
trabajo de F5.2b.4 se redefine: calibrar `StealPerStrike`/`StrikeDamage`
Y la reposición del stock víctima (o el botín alimentando mejor) para que
golpear pague. F5.2b.3 (contratos Unity) no depende de esto y sigue.

Sonda borrada tras su uso (patrón del repo); resultados registrados aquí.

### 8quinquies. LÍNEA BASE MULTI-SEMILLA (2026-09-13, sonda borrada)

El protocolo de §8quater extendido a 5 semillas (warm-v2 en cuerpo
Eciton, sensor + combate, 12 000 ticks) — la línea base que F5.2b.4
debe mejorar:

| seed | strikes | robidos | RaidInflow | muertes combate | c0 | c1 |
|---|---|---|---|---|---|---|
| 42 | 18 | 0.30 ep | 1 | 1 | muere t8781 | viva (4) |
| 77 | 5 | 0.30 ep | 1 | 0 | muere t9121 | viva (4) |
| 1234 | 7 | 0.60 ep | 2 | 0 | muere t9072 | **muere t11763** |
| 777 | 0 | 0 ep | 0 | 0 | muere t8691 | viva (2) |
| 2024 | 0 | 0 ep | 0 | 0 | muere t9655 | viva (1) |
| **total** | **30** | **1.20 ep** | **4** | **1** | **5/5 extinta** | 1/5 extinta |

**Lectura de línea base:**

1. **La Economía mata a la atacante ANTES que el rival**: c0 extinta
   5/5 (~t8700–9700, hambre — solo 1 muerte de combate en total). El
   botín cobrado en 5 semillas es 1.2 ep frente a un upkeep de
   0.045 ep/s · 9500 s ≈ 428 ep necesarios: el robo cubre ~0.3 %.
2. **Varianza de contacto alta**: strikes 0–18 según semilla. Con nidos
   a ~128 u y visión 90, la detectabilidad depende de dónde caen las
   rutas de forrajeo: dos semillas NO alcanzan contacto alguno.
3. **El objetivo de F5.2b.4 queda cuantificado**: para que la invasión
   sea juego (y no suicidio), el robo debe pasar de ~0.24 ep/semilla a
   del orden del upkeep de la atacante (×1000), vía robo por golpe mayor
   + stock víctima que se reponga (la víctima forrajeará y el saqueo
   será sostenible), o botín que no compita con el forrajeo propio.

## 9. Criterio de cierre

Una partida `--species lasius,eciton` (2 colonias, seed fijo, 7200 ticks):

1. los 4 pines CI existentes VERDES (mundos sin Eciton bit-idénticos) +
   el 5º pin nuevo de la partida de invasión;
2. en la partida canónica: ≥ 1 `Strike`, ≥ 1 `RaidInflow`, la víctima
   registra `StockRobbed` > 0, y al menos una muerte con `cause = 2`;
3. la colonia Eciton mantiene su demografía con el botín como fuente
   dominante de inflow (su forrajeo de ítems es posible pero secundario);
4. la tarjeta de la víctima muestra la pérdida en la línea de raids; la
   suite completa verde (≥ 310 tests) y docs al día.

## 10. Decisiones tomadas (y por qué, estilo plan §5)

| Decisión | Opciones | Elección |
|---|---|---|
| Eciton: especie vs agente no-colonia | agente nuevo con canal A propio vs especie que roba | **especie**: reusa TODO el F4 (inspección, relevo, HUD); el agente libre es el hito más caro y el que menos juego da |
| Detección de presa | canal nuevo vs reusar 14 | **reusar ProxFront (14)**: es el canal más redundante (los 3 de prox valen lo mismo); con gating por especie+mundo multi-colonia, los pines no se mueven |
| Botín | ítem dropeado al morir vs carga del robador | **carga del robador**: un ítem nuevo bifurcaría `FoodItem` y el relevo; la carga ya tiene toda la maquinaria (Unload, fitness, semáforo) |
| Retaliación de la presa | simétrica vs asimétrica | **asimétrica en F5.2b**: el juego de invasión necesita que la presa NO pelee (balance después); `ContactRadius = 0` en la presa ya lo expresa |
| Combate con RNG aparte | dado por golpe vs orden determinista | **sin RNG nuevo**: el orden de iteración ya es determinista; un dado extra rompería la reproductibilidad de partidas antiguas |
| Alarm ofensiva | capa nueva vs reusar Alarm | **reusar** (ya lo decía el plan §5): la capa existe, τ½ = 1 s es exactamente la vida de un rastro de incursión |
| Persistir contadores de combate | en `.antsave` v4 vs solo telemetría | **solo telemetría**: son métricas de ventana; v4 cambia por las constantes de especie, no por contadores |

## 11. Qué NO es F5.2b

- Agentes no-colonia (depredadores libres): descartado por costo/beneficio
  — queda anotado como posible F5.2d si el juego lo pide.
- Retaliación y defensa territorial (Territory layer sigue sin usarse).
- Horda dirigida por reina (las incursiones emergen de la política, no de
  órdenes): el mundo solo da los huesos — detección, daño, botín.
- Cambios al contrato 19/6 de sensores/salidas: los genomas siguen
  portables; las redes de la Fase 3 juegan en cuerpos Eciton tal cual.
