# Fase 4 — Resumen de cierre

El modo evolución de cara al jugador: **importar · espectar · intervenir**,
con el determinismo como promesa de producto. Este documento resume el estado
FINAL de la fase: decisiones de arquitectura, estado del contrato del HUD y
lo que queda para release. Los detalles de cada paso están en
[`fase4-diseno-ux.md`](fase4-diseno-ux.md) (diseño),
[`fase4-hud-contrato.md`](fase4-hud-contrato.md) (contrato dato a dato) y
[`arquitectura.md`](arquitectura.md) (registro cronológico).

**Estado: 221/221 tests verdes · hash canónico fijado en CI · los tres
pilares de jugador cerrados end-to-end.**

## 1. Las decisiones de arquitectura (y por qué)

### 1.1 El stream es la única frontera UI↔Core

Unity **nunca referencia el Core ni ejecuta simulación**: consume el stream
JSONL que `GameScenario` emite (en vivo por CLI, o un dump a archivo para
replay). Todo lo que el jugador ve — poses, eventos, métricas, semáforo,
alertas, feromonas — viaja en el stream, en cinco canales:

| Canal | Contenido | Ritmo |
|---|---|---|
| **A** | poses (12 campos por hormiga), ítems, colonias | cada `frameEvery` ticks |
| **B** | eventos (pickup, unload, births, `CommandExecuted`, `GenomeEntered/Discarded`, `ColonyExtinct`) | siempre |
| **C** | `MetricFrame` por colonia (pickups/unloads/stock/…) | ventana 1 s |
| **D** | alertas derivadas (`AlertDeriver`) + semáforo `RelayVerdict` por colonia | cadenciado / 120 ticks |
| **E** | feromonas (capa HomeTrail colonia 0, RLE+base64) | opt-in `--phero-every` |

La consecuencia práctica: **todo lo visible es verificable headless**. Los
modelos puros de Unity se compilan dentro de la suite del Core (`<Compile
Include>` de `AntSim.Core.Tests`) y se prueban contra la salida REAL de
`GameScenario`, sin abrir Unity.

### 1.2 "El Core manda, la vista obedece" (regla dura §6.4)

La UI **nunca inventa umbrales ni re-deriva nada**:

- El semáforo de relevo lo calcula `RelayVerdict` en Core (drop escalado al
  grid, ratio leg/cadena normalizado F4.4) y viaja en el canal D como byte.
- Las alertas las deriva `AlertDeriver` en Core — con la cadencia, el texto
  definitivo y el ancla de cámara — y viajan en el canal D.
- La regla de cuarentena la redacta `GenomeImportInfo` (oráculo), no el HUD.
- Verificar una partida es **ejecutar el modo `verify` del CLI**, nunca una
  re-implementación en la vista.

La UI solo hace dos cosas: *pintar* lo que llega y *mapear pantalla→mundo*
(raycasts de selección y de click-to-place).

### 1.3 Determinismo como feature de producto

"Misma semilla + mismos comandos ⇒ mismo mundo" es la promesa vendible, y se
sostiene con tres capas de verificación:

1. **Comandos con tick** (F4.0): `DropFood`/`SaveGame` encolados con tick
   exacto; el `.antlog` registra la causa; save/load/replay bit a bit
   (checkpoints `.antsave` en FormatVersion 2, con versión validada — los v1
   fallan explícitamente).
2. **Pin de hash en CI** (dos capas): `scripts/check-stream-fixture.sh`
   regenera la partida canónica (seed 42 · 96² · 7200 ticks) y compara el
   hash final; un test lo repite dentro de la suite. La inyección de
   comandos tiene su propio pin: `scripts/check-replay-command.sh` replaya
   el smoke e2e (warm-v2 + 3 drops) contra `tests/fixtures/warm-v2.antgenome`
   (trackeado — los .antgenome de artifacts/ son de ejecución).
3. **Telemetría pura verificada**: cada canal añadido (D, E, inspección de
   12 campos) llegó con un test que prueba que el hash del mundo NO cambia.

La intervención del jugador (F4.4) respeta la causalidad: un drop se marca a
tick futuro, se relanza la MISMA partida con `--drop tick:x:y` horneado, y el
hash final cambia — la intervención es determinismo, no caos.

### 1.4 Modelos puros + MonoBehaviours finos

Cada pieza de UI tiene un modelo puro sin UnityEngine (testeado headless) y
un componente fino que solo bombea datos:

| Modelo puro | Componente | Qué gobierna |
|---|---|---|
| `GameStreamParser` / `GameStreamPresenter` | `SimPresenterBehaviour` | stream → TickView; interpolación con retraso de 1 tick |
| `ColonyCardModel` | (Texts por colonia) | tarjeta §2: semáforo, stock, cerebros |
| `HudToastsModel` | `HudLayoutBehaviour` | toasts: dedupe por clave, expiración en sim-s, tope |
| `CommandHistoryModel` | (Text historial) | auditoría §4 + comando verify |
| `AntInspectorModel` | `AntInspectorBehaviour` | inspección 12 campos + modo linaje |
| `PoolPickerModel` | `PoolPickerBehaviour` | presets recomendados/especialistas + siembra |
| `DropFoodPlanModel` | `DropFoodClickHandler` | plan de drops: cuota 5, causalidad, args CLI |
| `ImportDialogModel` | `ImportDialogBehaviour` | flujo inspección→confirmar/cancelar |
| `PheromoneTileModel` | `PheromoneTileBehaviour` | decodificador RLE → RenderTexture |

La escena completa se monta sola: **AntSim → Crear escena de juego**
(`SceneBootstrapper`) crea cámara, suelo, nidos, meshes, quad de feromonas y
el Canvas con las 6 zonas del HUD, con todas las referencias asignadas.

### 1.5 El CLI es el oráculo de datos, también fuera del stream

Las operaciones que no caben en el stream son modos del CLI que la UI invoca
como procesos: `--mode genome-info --import f` (tarjeta de cuarentena JSON),
`--mode presets --json` (tarjetas del selector con métricas del benchmark),
`--mode verify` (auditoría de reproducción). La UI parsea la salida canónica
y pinta; los números que muestra siempre tienen respaldo en el repo.

## 2. Estado del contrato del HUD (§0–§8)

| Sección | Estado | Notas |
|---|---|---|
| §0 Fuentes (canales A–E) | ✅ | canal E añadido en el pulido final |
| §1 Alertas por colonia | ✅ | `relay-weak:{id}` con ancla en el nido propio; cuota, cadencia y shrink por colonia; salto de cámara al ancla (tecla J / Alt+click) |
| §2 Tarjeta de colonia | ✅ lógica | semáforo del canal D, `/40` duro, chips de cría, `¡RESERVA BAJA!`, cerebros élite/descartados |
| §3 Estado global | ✅ lógico | tick/semilla/hash, velocidad, semáforo global |
| §4 Historial de comandos | ✅ | filas `CommandExecuted`, total acumulado, "reproducir desde guardado" = modo verify del CLI; DropFood click-to-place (cuota 5, causalidad) con "reiniciar con plan" |
| §5 Inspección de hormiga | ✅ | 12 campos, selección por click (raycast), **modo linaje**: `cerebro #F · N cuerpos · M vivos` con todos los cuerpos de la misma huella |
| §6 Reglas duras | ✅ | la UI no consulta el Core, no suaviza métricas, no traduce, no inventa umbrales |
| §7 TODOs destapados | ✅ cerrados | `ColonyExtinct` (kind 12), inspección de 12 campos, relay/métricas por colonia |
| §8 Validación con datos reales | ✅ | smoke 5 semillas warm-v2 grid 256: **4/5 verde, 0 gris/rojo** (semáforo escalado F4.3); el ámbar de la seed 7 diagnosticado como cadena corta por oferta (§8.1), no torpeza — motivó la métrica normalizada F4.4 |

F4.3 (diálogo de importación con cuarentena, pilar «importar») está cerrado:
oráculo `genome-info` + diálogo 0-riesgo + siembra desde la UI. El selector
de pools (F4.5-datos) ofrece los 4 presets recomendados del benchmark + 2
especialistas con su contrapartida ⚠.

## 3. Validación con datos reales

- **Fixture 96²** (CI): seed 42 · 2 colonias · 7200 ticks · frameEvery 1 —
  hash final fijado; su regeneración falla CI si algo rompe el determinismo.
- **Fixture 256²** (artifacts, 13.5 MB): warm-v2 sembrado · 48000 ticks —
  relevo real (firstUnload 5154, verde colonia 0, gris la competidora).
- **Smoke 5 semillas** (§8): 4/5 verdes con el semáforo escalado; las 5
  arrancan relevo (firstUnload 3745–5876) — la transferencia del pool es
  consistente y visible en el HUD.
- **Integración Unity↔Core sin Unity**: los 190 tests incluyen la
  reconstrucción fiel del stream real (poses/items/eventos/métricas/relevo/
  alertas/feromonas) por los modelos puros.
- **Smoke end-to-end del bucle completo** ([`fase4-smoke-e2e.md`](fase4-smoke-e2e.md)):
  sembrar warm-v2 + intervenir con 3 drops + replay bit a bit — hash final
  `816e280c…` idéntico entre builds y ejecuciones; baseline sin drops difiere.
  La fase extendida a 6000 ticks (`87fbc8ed…`) certifica además el RELEVO:
  primera descarga t3950 y semáforo del canal D dentro del mundo fijado —
  y ambos pines se verifican en CI (`check-replay-command.sh`).

## 4. Qué queda para release

Lo que falta es **capa de presentación y pulido**, no arquitectura:

1. **Montaje visual en el editor** — abrir el proyecto una vez en Unity
   (6000.0.55f1, con `FindFirstObjectByType` compatible 2022.3), ejecutar el
   bootstrapper, guardar la escena y validar la partida contra el fixture
   256. Todo el cableado existe; falta la pasada humana de Play.
2. **uGUI por elemento** — hoy el HUD v1 pinta textos (6 zonas). Para release:
   rects por toast (click exacto en vez de Alt+click por índice), barras de
   stock reales, botones nativos Confirmar/Cancelar/Reiniciar-con-plan.
3. **Drag & drop de `.antgenome`** — el diálogo acepta ruta de texto; el
   punto de entrada prometido (soltar el archivo sobre la ventana) es el
   último trozo del pilar «importar».
4. **Gráficas por tarjeta** — las series ya viajan en el canal C; falta el
   mini-gráfico de stock/descargas por tarjeta de colonia.
5. **Iconos/rich text y tipografía** — el render v1 es texto plano sin
   fuente custom (glifos emoji incluidos).
6. **Mini-grafo MLP en la inspección** — explícitamente aplazado a Fase 5
   (diseño §2.2); hoy la identidad del cerebro es la huella + linaje.

Aplazado conscientemente fuera de Fase 4: multijugador/seed sharing UI,
localización (los textos del contrato son definitivos en español), y
opciones gráficas.

## 5. La promesa jugable, ya demostrada

La cadena completa del diseño (§2) funciona de punta a punta con los
mecanismos reales:

> Elegir **warm-v2** en el selector (datos del benchmark, no marketing) →
> la partida arranca sembrada → **espectar**: tarjetas con semáforo, toasts
> del deriver, relevo verde a los 5154 ticks → click en una hormiga:
> inspección y **linaje de su cerebro** → **intervenir**: D + click marca
> drops, reiniciar con plan demuestra el efecto en el hash → el historial
> audita cada comando y el botón de guardado ejecuta el **verify** real.

Todo eso está verificado headless con los mismos modelos que Unity ejecuta.
Lo que queda es pintarlo bonito.
