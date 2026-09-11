# Fase 4.2 — Contrato de datos del HUD (canal B/C → alertas e indicadores)

Documento de trabajo para el equipo Unity: qué mostrar y cuándo, con los datos
que el stream ya entrega. **La derivación de alertas está IMPLEMENTADA en el
Core**: `Scenario.AlertDeriver` (observador puro de canal B/C, mismo patrón que
`RelayTracker`) produce las `Alert`s de §1 con niveles y textos finales — la UI
solo las consume y las colorea; los umbrales y cadencias viven en el Core y
están cubiertos por `AlertDeriverTests` (8 tests: un gatillo por alerta más
purity y la integración warm-v2 → hito verde con el pool real). **Ningún dato del HUD se inventa ni se calcula en la
UI** — todo viene del stream JSONL de `--mode game` (o, en proceso, de los
canales A/B/C del Core). Referencias: [`fase4-diseno-ux.md`](fase4-diseno-ux.md)
(diseño de UX), [`arquitectura.md`](arquitectura.md) §3 (canales),
[`fase3ter-resumen.md`](fase3ter-resumen.md) (semántica del relevo).

## 0. De dónde viene cada cosa

| Dato del HUD | Fuente en el stream | Ritmo |
|---|---|---|
| Poses de hormigas, items, colonias (canal A) | `ants` / `items` / `colonies` por tick (cada `frameEvery`) | cada tick |
| Eventos discretos (canal B) | `events:[[kind,colony,ant,x,y,cause],…]` | cada tick (si hubo) |
| Métricas de ventana (canal C) | `metrics:{t0,t1,pickups,unloads,births,deaths,eggs,eclosed,consumed,commands}` | cada 30 ticks |
| Salud del relevo | `relay:{firstUnload,dropAvg,unloadAvg,carryLeg}` | cada 120 ticks |
| Hash de verificación | línea `{"end":true,...,"hash"}` | al final |

Los códigos de `kind` (canal B) son los de `SimEventKind`:
0 AntBorn · 1 AntDied · 2 ItemSpawned · 3 ItemConsumed · 4 Pickup · 5 Unload ·
6 EggLaid · 7 Eclosed · 8 ColonyFounded · 9 GenomeEnteredElite ·
10 GenomeDiscarded · **11 CommandExecuted** (campo `cause` = kind del comando;
1 = SaveGame, y para DropFood x/y son la posición del ítem).

`cause` en AntDied: 0 = edad, 1 = inanición (útil para la línea de estado de la
colonia, ver §3).

## 1. Alertas (canal B → cola de notificaciones)

Cada alerta es clicable: la cámara salta a (x, y) del evento (o al nido de la
colonia si el evento no lleva posición). Descarte: las alertas NO se
acumulan sin límite — cola de 8, la más vieja sale.

| Gatillo (canal B) | Alerta | Nivel | Texto sugerido | Cadencia |
|---|---|---|---|---|
| `Pickup` con `ant` que NO tenía carga registrada y es el primer pickup de la partida | "Primera comida encontrada" | info (gris) | *"Una exploradora encontró comida a {d} u del nido"* (d = distancia del evento al nido, calculable desde canal A) | única |
| **Primer `Unload` de la partida** (`relay.firstUnload` pasa de null a N) | "El relevo arrancó" | **verde (hito)** | *"Primera descarga en t={N} ({N/30}s de sim) — la colonia completa ciclos"* | única |
| `GenomeEnteredElite` | "Cerebro en élite" | info (azul) | *"Un genoma {importado/nativo} entró en la élite"* | máx 1 por 10 s de sim |
| `GenomeDiscarded` | "Cuarentena descartó" | info (gris) | *"Un inmigrante no rindió ≥ la mediana — fuera"* | máx 1 por 10 s de sim |
| `ColonyExtinct`**\*** | "Colonia extinta" | **roja** | *"La colonia {id} murió en t={tick}"* | única por colonia |
| `metrics.deaths ≥ 5` en una ventana (1 s) | "Mortalidad en picada" | ámbar | *"{deaths} muertes en el último segundo (causa dominante: {edad|hambre})"* | máx 1 por 30 s de sim |
| `metrics.eggs == 0 && metrics.births == 0` durante 5 ventanas seguidas (5 s) Y `colonies[c].stock < 20%` de `stockMax` | "Puesta parada" | ámbar | *"La colonia {id} no pone huevos: reserva baja"* | hasta que cambie |
| `relay.carryLeg` cae >20% respecto a la media de las últimas 5 emisiones | "Relevo débil" | ámbar | *"Las cargas completan tramos más cortos ({leg} u vs {media} u)"* | máx 1 por 60 s |
| **O** `relay.dropAvg` > umbral DEL MUNDO o ratio `carryLeg/chainAvg` < `LegRatioMin` (umbral absoluto de `RelayVerdict` — F4.3: sin umbrales propios; F4.4: el tramo se juzga por ratio, no absoluto; el texto enseña el umbral del mundo) | "Relevo débil" | ámbar | *"Las sueltas caen demasiado lejos del nido para este mundo ({drop} u > {max} u)"* / *"Las cargas completan menos de 55% de la cadena disponible ({leg} u de {chain} u)"* | máx 1 por 60 s (misma alerta, prioridad drop > ratio > encogimiento) |
| — **POR COLONIA** (F4.2): cada fila anterior se evalúa con `RelayTracker.ForColony(c)` — la tarjeta de la colonia {c} recibe SU alerta: clave `relay-weak:{id}`, `ColonyId={id}`, ancla de cámara en SU nido y texto prefijado `"Colonia {id}: …"`. Cadencia e historia de encogimiento son propias de cada colonia. | | | | |

**\*** **Implementado (F4.2)**: `ColonyExtinct` es el kind **12** del canal B —
un único evento por colonia, por transición de estado (sin adultas NI cría).
La alerta roja se gatilla con ese evento; x/y = posición del nido.

## 2. Tarjeta de colonia (canal A + C + relay)

Una por colonia, actualizada cada tick (canal A) con refresco de gráficas cada
ventana de métricas (1 s). Datos y componentes, en orden de tarjeta:

| Componente | Dato | Regla de presentación |
|---|---|---|
| **Semáforo de relevo** | `relays[c].*` del stream (por colonia; el `relay` global queda para compatibilidad) | 🔘 gris: sin datos de relevo · 🟡 ámbar: descarga pero `carryLeg < 60 u` o `dropAvg > 190 × grid/96` · 🟢 verde: resto. **Regla implementada como código en `Core/Scenario/RelayVerdict.cs`** (`Evaluate(leg, drop, grid)`): el drop escala con la distancia de forrajeo del mundo (190 u en el 96 de calibración ⇒ 506.7 en 256), el tramo es invariante — umbrales del benchmark Fase 3ter (warm-v2 sano 167.9/80.0; regresión pipeline.sh: drop +10% / leg −20%) |
| Adultas | `colonies[c].adults` | "12 / 40" (tope duro del diseño) |
| Cría | `eggs / larvae / pupae` | tres chips "🥚 4 · 🐛 2 · 🛑 1" |
| Reserva | `stock` vs `stockMax` | barra horizontal; <20% = rojo (dispara alerta de puesta parada) |
| Flujo 1 s | `metrics.pickups/unloads/births/deaths/eggs/eclosed` | mini-barras de la ventana; las muertes coloreadas por causa dominante si se registra (`AntDied.cause`) |
| Cerebros | contadores de `GenomeEnteredElite`/`GenomeDiscarded` acumulados por la UI desde el arranque del stream | *"7 élite · 3 descartados"* — línea permanente, no toast |
| Comandos | `metrics.commands` acumulado | *"3 comandos"* — enlaza al panel de historial (§4) |

## 3. Estado global (barra superior)

| Indicador | Dato | Nota |
|---|---|---|
| Reloj de sim | `tick` actual (÷30 = s de sim) | con pausa/velocidad del presenter al lado |
| Semilla + hash corto | header `seed`; `hash` final (al terminar) | "t=5123 · semilla 42 · ✓ verificable" — el hash final habilita el botón de reproducir (modo verify) |
| Velocidad | estado del presenter (0/1/2/4/16×) | nunca toca la física |
| Semáforo global de relevo | el mismo de §2 | también en la barra para visibilidad a zoom de colonia |

## 4. Panel de historial de comandos (canal B, `CommandExecuted`)

Cada evento kind 11 es una fila: `t={tick} — {DropFood @ (x, y) | SaveGame → slot {cause}}`.
El panel es también la auditoría de determinismo: botón "reproducir desde
guardado" (lanza el modo verify del CLI con el `.antsave` + `.antlog` de la
partida) y muestra ✓/✗ del resultado.

## 5. Inspección de hormiga (canal A + seguimiento)

**Implementado (F4.2)**: cada fila `ants` del canal A lleva 12 campos —
`[id, colony, x, y, heading, load, alive, vigor, energy, age, immigrant,
genomeFingerprint]`. La huella del genoma es un hash FNV de (tamaño + primeros
4 pesos): identifica "el mismo cerebro" sin serializar pesos, determinista.
La tarjeta de inspección muestra vigor, energía, edad, si está en cuarentena y
su huella; el seguimiento detecta "el mismo cerebro" entre hormigas distintas.

Clic en una hormiga (dentro de ~0.5 u en coords de mundo): tarjeta flotante con
los datos del canal A de esa pose (id, colonia, carga, viva). Botón "seguir"
fija la cámara a su id hasta que `alive == 0` — entonces la tarjeta cierra con
"murió a los {edad≈(ticks con vida)/30}s de sim" (contado por la UI desde que
apareció). Los datos de genoma/vigor/energía YA están en el stream (ver arriba).

**Implementado en el esqueleto Unity (F4.2)**: `AntInspectorModel` (puro,
verificado headless en `UnityStreamContractTests`) sigue a la hormiga
seleccionada a través de los TickViews — serie temporal de los 12 campos,
muerte capturada del canal B (con causa: vejez/inanición), tarjeta en formato
fijo con estados sin selección / esperando datos / viva / muerta. Regla
descubierta por los tests: el canal A NO emite filas de hormigas muertas, así
que una muerte de hormiga nunca rastreada se conserva como expediente (tick +
causa, sin datos de posición) — una muerte no se pierde por llegar antes que
la selección. `AntInspectorBehaviour` es el componente fino: selección por id
(v1) o por click del raycast (`PickNearest`, usando el estado interpolado del
presenter) y pinta la tarjeta en el uGUI Text asignado.

**Linaje de cerebros (`FollowBrain`/`FollowBrainOfTracked`)**: la huella del
genoma es la identidad del CEREBRO; el modo linaje rastrea TODOS los cuerpos
que la porten (presentes y futuros) y la tarjeta muestra
`cerebro #F · N cuerpos · M vivos`, el cuerpo actual (#id, energía, edad), y
el linaje completo `#id t{aparición}†{muerte}` por cuerpo — con posición
solo si hay un cuerpo único (con varios no hay "la posición del cerebro").
Al morir el cuerpo actual, la tarjeta queda en "esperando relevo" hasta que
aparece el siguiente portador (o "sin cuerpo vivo — esperando relevo" si el
modo es permanente). `FollowAnt(id)` vuelve al modo hormiga. Nota de mundo anclada en test: con el
modo POR DEFECTO cada nacimiento cruza+y muta (`GenomePool.Birth`), así que
1 cerebro = 1 cuerpo. **El modo opcional de REUSO DE CEREBROS
(`GenomePool.SetCloneFromElite`, opt-in por colonia vía
`new WorldSim(..., cloneFromElite: true)` / `GameScenario.Run(...,
cloneFromElite: true)`; el header del stream lleva
`"cloneFromElite":true`) hace que los nacimientos CLONEN un genoma de la
élite (mismo torneo determinista que el modo cruce, sin cruce ni mutación)**
— con élite pequeña, varios cuerpos comparten huella y el linaje existe en
partida real (test `StreamReal_ConModo_CuerposCompartenCerebro` sobre stream
genuino). El modo es parte del estado del mundo: serializado en .antsave v2
(FormatVersion 2 — los checkpoints v1 ya no se cargan) y el determinismo por
semilla se mantiene dentro de cada modo.

## 6. Qué NO hace el HUD (reglas duras)

1. **Nunca consulta el Core directamente**: el stream es la única fuente (en
   proceso o por CLI) — garantiza que lo mostrado es lo que el determinismo
   verifica.
2. **Nunca suaviza ni interpola métricas**: los valores de `metrics`/`relay`
   se muestran tal cual (son los que comparan los benchmarks).
3. **Nunca traduce**: los textos de alerta fijados aquí son los definitivos de
   la v1 (español, como la documentación del proyecto).
4. **No inventa umbrales**: los de §2 salen del benchmark Fase 3ter y del
   pipeline de CI; si el benchmark se regenera, `PoolPresetsTests` obliga a
   actualizar presets y esta tabla se revisa con él.

## 7. Tareas del Core que este contrato destapa (baratas, para F4.2)

Los tres TODOs de F4.2 están **CERRADOS**:

- ✅ `ColonyExtinct` (kind 12) — un evento por colonia, transición de estado.
- ✅ Inspección por hormiga en canal A — vigor/energía/edad/inmigrante/huella
  del genoma en cada fila `ants` (siempre, no solo en modo inspección: 5
  campos extra por hormiga es ancho de banda asumible a 30 Hz).
- ✅ `relay` y `metrics` por colonia — el stream emite `relays:[{col,
  firstUnload, unloadAvg, carryLeg, dropAvg, unloads}|{col,empty:true}]` y
  `colmetrics:[[col,pickups,unloads,births,deaths,eggs,eclosed]]` junto al
  `relay` global cada 120 ticks. El parser de Unity ya los consume
  (`ColonyRelays`/`ColonyMetrics` en `TickView`).

## 8. Validación del semáforo con datos reales (smoke warm-v2, 5 semillas)

Smoke end-to-end del contrato: `--mode game --grid 256 --colonies 2 --ticks
48000 --seed-pool artifacts/pretrain-warm-v2.antgenome` sobre las 5 semillas
de referencia, evaluando `relays[0]` final con los umbrales de §2:

| semilla | firstUnload | dropAvg | carryLeg | unloads | semáforo (escalado) | regla fija 96² (antes) |
|---|---|---|---|---|---|---|
| 42   | 5154 | 182.3 | 64.0  | 1 | 🟢 verde | 🟢 verde |
| 7    | 4354 | 131.6 | 49.3  | 3 | 🟡 ámbar (leg < 60) | 🟡 ámbar (leg) |
| 99   | 5298 | 190.3 | 85.0  | 1 | 🟢 verde | 🟡 ámbar (drop) |
| 1234 | 5876 | 177.0 | 71.0  | 2 | 🟢 verde | 🟢 verde |
| 777  | 3745 | 240.0 | 174.0 | 1 | 🟢 verde | 🟡 ámbar (drop) |

**Resultado con la regla escalada (RelayVerdict, drop máx = 190 × 256/96 ≈
506.7): 4/5 semillas en verde, 0 en gris o rojo.** Las 5 semillas arrancan
el relevo (firstUnload 3 745–5 876, muy por debajo del fin de partida): el
pool transfiere de forma consistente. Con la regla fija de 96² eran 2/5 —
los drops largos de las semillas 99 (190.3) y 777 (240.0) eran artefactos
del mundo grande, no degradación del homing (240 u en 256² es proporcional-
mente MÁS cerca del nido que 167.9 en 96²). El único ámbar real es la
semilla 7 por tramo corto (49.3 < 60), que la regla invariante castiga en
cualquier mundo. La colonia competidora sin sembrar queda gris en las 5
(`relays[1] = {col,empty:true}`), exactamente el contraste que el selector
promete.

✅ **TODO(F4.3) CERRADO**: umbrales recalibrados como código en
`Core/Scenario/RelayVerdict.cs` — `Evaluate(carryLeg, dropAvg, grid)` con
drop escalado linealmente al grid (190 × grid/96) y tramo invariante ≥ 60 u.
Tests: `RelayVerdictTests` (centinelas reales de esta tabla) e
integración `IntegracionCompleta_WarmV2_Grid256_SemaforoVerde` (partida real
seed 42 / grid 256 / 24 000 ticks ⇒ verde).

Nota de recuento: los "eventos de descarga" de esta tabla se leen de
`relays[c].unloads` (JSON del stream), NO de un grep naive de `[5,` — ese
patrón también casa filas de hormigas con id 5 (contaminación: contaba
hasta 9 en la seed 42 cuando la real es 1, tick 5154, consistente con
`unloads:1`).

### §8.1 Diagnóstico del ámbar de la seed 7 (trazado de portadores)

Investigación con el dump real (`artifacts/smoke-seeds/warmv2-7.jsonl`):
pickup/unload emparejados por hormiga + canal A a 1 Hz + anillo de spawn.
**Veredicto: la colonia es competente; el ámbar es un artefacto del techo
de la métrica, no una degradación del relevo.**

- **Ni muerte ni hambre ni desvíos.** Los dos portadores (hormigas 31 y 37)
  son cría joven del pool sembrado (eclosionan ~t=3750, justo tras morir las
  fundadoras), vigor 0.72/0.96, energía 1.00 en todo el trayecto, ninguna
  muere durante su carga. Las 3 descargas caen a 24.0 u del nido (radio de
  descarga) — homing perfecto.
- **El tramo es corto porque la COMIDA estaba cerca.** Legs reales: 6.7 /
  93.2 / 49.1 u con pickups a 30.6 / 116.9 / 70.7 u del nido. El tramo está
  acotado por (distancia del ítem − radio de descarga): con un ítem a 30.6 u
  el tramo máximo posible es ~6.6 u — la hormiga 37 lo completó entera.
- **Ratio de tramo completado ≈ 100% en las 3 cargas**: leg/(pickup − 24) =
  1.02 / 1.00 / 1.05. El relevo no dejó eslabón por completar: no había más
  eslabón disponible. La seed 7 tuvo solo 12 spawns en 48 000 ticks (hambre
  extrema de mundo) y los cercanos al nido (28–128 u) son los que se
  encontraron — cadena corta por oferta, no por torpeza.

TODO(F4.4): métrica de tramo NORMALIZADA — `leg / max(pickup − radio, ε)`
(el % de la cadena disponible que la cría completa), tanto en el semáforo
(`RelayVerdict`) como en el umbral absoluto del deriver. El leg absoluto
(≥ 60 u) solo tiene sentido como medida de RELEVO LARGO; penalizar con él
un forrajeo de proximidad confunde oferta corta con competencia baja.
Mientras tanto: el ámbar por leg corto de la §8 se lee como "cadena corta
observada", no como colonia enferma.
