# Fase 4 — Diseño de UX del modo evolución (importar · espectar · intervenir)

Plan de la capa jugador sobre el pool ya listo (Fase 3ter). Detalles de
arquitectura en [`arquitectura.md`](arquitectura.md), contratos en
[`especificaciones.md`](especificaciones.md), jerarquía de pools en
[`fase3ter-resumen.md`](fase3ter-resumen.md).

## 1. Principios (lo que la UX promete y el Core ya garantiza)

1. **El determinismo es una feature de producto, no un detalle técnico.**
   "Misma semilla + mismos comandos ⇒ mismo mundo" se vende al jugador como:
   *cada partida es reproducible y verificable*; el botón "reproducir desde
   guardado" ejecuta literalmente el modo `verify` existente (exit 3 si
   diverge). Nada que el jugador vea puede romper esa garantía.
2. **El Core manda, la vista obedece.** Toda intervención del jugador es un
   **comando con tick** registrado en `.antlog` (canal de comandos de la
   arquitectura); la simulación nunca es mutada por la vista directamente.
   Consecuencia UX: el historial de intervenciones es *la partida* — guardar,
   compartir y verificar una partida es compartir el save + el log.
3. **Observación pura por defecto.** El jugador es primero un naturalista:
   el HUD consume solo canales A (snapshot), B (eventos), C (métricas), D
   (traza de inspección). Ninguna pantalla del juego requiere intervenir.

## 2. Los tres pilares de jugador

### 2.1 Importar — "trae cerebros a tu mundo"

Momento de entrada del jugador: posee un `.antgenome` (de la comunidad, del
pre-entrenamiento propio, o exportado de otra partida).

- **Arrastrar-y-soltar** de un `.antgenome` sobre la ventana = punto único de
  entrada. El Core ya tiene `WorldSim.ImportGenomesFromFile` (cuarentena,
  ventana 120 s, umbral p50/p25+novedad) — la UX lo expone sin parámetros.
- **Diálogo de cuarentena** (pre-vista, 0 riesgo): al soltar, el HUD muestra
  *antes* de confirmar: nº de genomas, tamaño/red, hash SHA-256 del archivo,
  fitness si el archivo lo lleva (los pools pre-entrenados lo llevan; los
  exportados en vivo re-evalúan), y una proyección honesta: *"ocuparán las
  próximas eclosiones; entran solo si rinden ≥ mediana del pool; nunca
  degradan la élite"*. Confirmar encola; cancelar no toca nada.
- **Elección de pool al fundar** (nueva partida): selector con los pools
  validados como presets con nombre y datos reales del benchmark Fase 3ter:
  *Ninguno (naturalista — arranca en frío)*, *warm-v2 (fiable)*,
  *warm-4 (mapa completo)*, *warm3 (máx. descargas)*, *Archivo propio…*.
  Los presets son `--seed-pool`; los datos de la tarjeta salen de la tabla de
  referencia, no de marketing.
- **Feedback de por vida**, no de momento: los eventos de genoma del canal B
  (`GenomeEnteredElite` / `GenomeDiscarded`) alimentan una línea "cerebros" en
  la tarjeta de colonia: *12 importados · 7 entraron en élite · 5 descartados*.
  El jugador ve el veredicto del pool sobre su importación, no un toast efímero.

### 2.2 Espectar — el naturalismo como juego

- **Cámara libre + follow-ant**: clic en una hormiga = inspección (canal D,
  observación pura): tarjeta con sus sensores clave, decisión actual, edad,
  vigor, carga, genoma (mini-grafo MLP en Fase 5; en v1, hash corto del
  genoma + linaje). Botón "seguir" fija la cámara a ella hasta su muerte.
- **Tarjeta de colonia** (HUD principal, una por colonia): adultas/cría/stock,
  tasa de puesta, y — heredado directamente del trabajo Fase 3ter — la **línea
  de salud del relevo**: `primera descarga: tick N · sueltas a ~X u · tramo
  portado ~Y u`. Es la métrica que ya emiten `totals`/`tick` en CLI; en el HUD
  es un mini-indicador: gris (sin relevo), ámbar (relevo iniciado), verde
  (descargas periódicas con tramo sano).
- **Alertas de eventos (canal B)** con gravedad: `ColonyExtinct` (roja),
  extinción inminente (heurística de stock/cría), primera descarga de la
  partida (verde — el hito que Fase 3ter hizo alcanzable), entrada en élite de
  un importado (azul). Cada alerta es clicable: salta la cámara al punto.
- **Ritmo de render, física fija**: pausa / 1× / 2× / 4× / 16× (headless entre
  renders). El tiempo de sim siempre es de ticks; la interpolación (retraso de
  1 tick, presupuesto del presenter) hace suave cualquier velocidad. **16× es
  el "espectador impaciente": mismas reglas, mundo acelerado** — nunca cambia
  el RNG ni el orden.
- **Gráficas de métricas** (canal C, 1 s sim): población, stock, pickups,
  descargas, mejor/mediana de fitness del pool. Ventana con zoom; exportar CSV
  = los formatos `.antmetrics`/`.antevents.csv` ya especificados.

### 2.3 Intervenir — manos con precio en el log

Toda acción es un comando con tick, aparece en el historial y por tanto en la
reproducción. V1 con **cuatro** palancas (pocas, legibles, con coste claro):

| Comando | Efecto en mundo | Coste/diseño | Por qué este |
|---|---|---|---|
| `Pause` / `Speed(n)` | no toca el mundo | ninguno | control básico, ni siquiera es "trampa" |
| `DropFood(x, y)` | 1 ítem donde el jugador clic | limitado por cuota por minuto de sim | el más físico y pedagógico: tira comida y ves cómo el relevo la descubre |
| `ImportGenome(file, colony)` | encola cuarentena | ya cubierto por 2.1 | intervención "genética" sin tocar física |
| `SaveGame(name)` | escribe `.antsave` + `.antlog` | ninguno | guardar es intervenir y queda en el historial |

- **Cuota de comida**: el "poder divino" de tirar comida es el único comando
  que altera la economía; una cuota por minuto (p. ej. 3 ítems/min de sim,
  configurable en partida avanzada) evita que el modo sea trivial mientras
  conserva el juguete. La cuota es parte del estado serializado.
- **Historial de comandos visible**: panel lateral con cada comando y su tick
  (`t=4 512 — DropFood @ (312, 87)`). Es también la UI de auditoría del
  determinismo: "reproducir desde guardado" (modo `verify`) está a un clic y
  muestra ✓/✗.
- **Futuro post-v1 (fuera de alcance, listado para no diseñar contra él)**:
  matar/curar hormigas, muros, feromonas manuales, clima. Ninguno rompe el
  patrón comando+log; solo añaden filas a la tabla.

## 3. Huecos del Core que la Fase 4 debe cerrar (trabajo real)

El Core tiene eventos (B), telemetría de relevo, import/seed y persistencia
verificada. **No existen aún** las piezas que la vista consume:

1. **Canal A — `SimSnapshot`**: poses interpolables por tick + `ColonyStat`.
   Nuevo tipo en Core (netstandard2.1, sin Unity), construido con coste O(N)
   por tick *solo cuando hay un suscriptor* (headless = coste 0, los benchmarks
   no se tocan).
2. **Canal C — `MetricFrame`**: agregación 1 s sim de los contadores
   incrementales existentes; los `ColonyStat` ya casi son esto.
3. **Sistema de comandos**: `ISimCommand` con `Tick`, aplicados en
   `WorldSim.Step()` en punto canónico (antes de los hashes de hito),
   serializados en `.antlog`. La cuota de comida es estado, no evento.
4. **`src/App/AntSim.Unity`**: proyecto Unity (presenter + HUD) según la
   estructura de la solución. Primera prueba de fricción Unity↔netstandard2.1
   — riesgo ya identificado en la arquitectura; su mitigación es integrar
   temprano con un HUD mínimo antes de pulir pantallas.
5. **Modo `game` en CLI** (opcional pero barato): `evolve` + snapshot a
   stdout/JSON por tick, para desarrollar el presenter sin abrir Unity
   (contrato probado en el mundo headless primero — el patrón de todo el
   proyecto).

## 4. Flujos de jugador (felices y de borde)

- **Primera partida (feliz)**: Nueva partida → tarjeta "elige cerebros" →
  *warm-v2* → mundo arranca con relevo en ~2 min de sim → alerta verde
  "primera descarga" → el jugador sigue a la portadora → suelta a 170 u →
  una hija cierra el tramo. El arco de Fase 3ter contado como momento de juego.
- **Naturalista (feliz)**: Nueva partida → *Ninguno* → ve morir la colonia
  fundadora → entiende por qué existe el pool (la dead zone está documentada;
  la alerta de extinción lo explica en una línea).
- **Importado débil**: el jugador suelta un genoma aleatorio → cuarentena lo
  descarta → línea "cerebros" lo muestra sin drama → prueba con warm-v2 →
  entra en élite. Aprendizaje por contraste, sin tutorial textual.
- **Borde**: `.antgenome` corrupto o topología distinta → el diálogo de
  importación muestra el error exacto del validador del Core (ya existen los
  rechazos de topología), nada se encola.
- **Borde**: guardar → seguir jugando → cargar → el mundo regenera bit a bit
  (`.antlog` verifica) → la UI marca la partida "verificada ✓" con el hash.

## 5. Hitos propuestos (orden de integración temprana)

| M | Contenido | Exit |
|---|---|---|
| F4.0 | Canales A/C + comandos en Core (+ tests de determinismo: comandos en el log ⇒ hashes idénticos) | suite verde con comandos en el hash |
| F4.1 | Proyecto Unity mínimo: render de poses + feromonas por tiles, pausa/velocidad | demo 60 fps 2 colonias, regresión con semillas fijas |
| F4.2 | HUD: tarjetas de colonia + línea de relevo + alertas (todo canal B/C) | todos los datos del HUD provienen de A/B/C sinconsultas al mundo |
| F4.3 | Importar: drag&drop + diálogo de cuarentena + línea "cerebros" | importar → cuarentena → élite visible end-to-end |
| F4.4 | Intervenir: DropFood con cuota, historial de comandos, guardar/cargar + "verificado ✓" | partida con comandos reproduce bit a bit desde UI |
| F4.5 | Pulido: gráficas, inspección con follow, presets de pool con datos del benchmark | demo jugable del arco completo de Fase 3ter |

## 6. Riesgos específicos de UX

| Riesgo | Mitigación |
|---|---|
| La cuota de comida rompe determinismo si se aplica fuera del punto canónico | comando pasa por el mismo camino que cualquier mutación; test F4.0 con/y sin comandos |
| El espectador pierde interés tras la primera descarga (arco corto) | gráficas + competencia 2 colonias (ya existe) + la escala Fase 5 como contenido; v1 no promete más |
| Fricción Unity↔netstandard2.1 (riesgo histórico del proyecto) | F4.1 mínimo e integrado antes de cualquier HUD rico |
| Presets de pool con promesas de marketing desalineadas | los números de las tarjetas se generan del benchmark de referencia, con su semilla y hash |
