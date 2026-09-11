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

**\*** **Implementado (F4.2)**: `ColonyExtinct` es el kind **12** del canal B —
un único evento por colonia, por transición de estado (sin adultas NI cría).
La alerta roja se gatilla con ese evento; x/y = posición del nido.

## 2. Tarjeta de colonia (canal A + C + relay)

Una por colonia, actualizada cada tick (canal A) con refresco de gráficas cada
ventana de métricas (1 s). Datos y componentes, en orden de tarjeta:

| Componente | Dato | Regla de presentación |
|---|---|---|
| **Semáforo de relevo** | `relay.*` (global del stream; v1 no es por colonia) | 🔘 gris: `firstUnload == null` · 🟡 ámbar: descarga pero `carryLeg < 60 u` o `dropAvg > 190 u` · 🟢 verde: `carryLeg ≥ 60 u` y `dropAvg ≤ 190 u` (umbrales del benchmark: warm-v2 здоров 167.9/80.0; el límite de regresión de pipeline.sh es drop +10% / leg −20%) |
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
apareció). **Los datos de genoma/vigor/energía NO están en el stream todavía**
(`TODO(F4.2)`: añadir al canal A en `GameScenario` — decisión de ancho de banda
pendiente: enriquecer `ants` o emitir solo en "modo inspección").

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
