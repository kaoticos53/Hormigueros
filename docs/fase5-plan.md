# Fase 5 — Plan (borrador)

Realismo, especies y escala — la fase que convierte el simulador verificado
en un mundo con contenido. Este documento es el PLAN de trabajo (no el
registro de lo hecho): hitos, decisiones abiertas y criterios de cierre.
Estado del que parte: Fase 4 cerrada
([`fase4-resumen.md`](fase4-resumen.md)) — 276/276 tests, contratos de HUD
§0–§8 implementados, determinismo fijado en CI (3 capas de pin de hash), y
un bucle de jugador completo demostrado end-to-end.

**Estado ACTUAL** (HEAD, suite, rodajas hechas):
[`estado-proyecto.md`](estado-proyecto.md) — este documento es el plan, no el
registro de lo hecho.

**La regla que Fase 5 hereda y no puede romper**: todo lo visible sale del
Core por el stream; toda decisión de mundo deja huella en el hash; el
determinismo (semilla + comandos ⇒ mundo) es inviolable. Cada hito de abajo
lleva su test de hash invariante cuando toca telemetría, como en F4.

## 1. El contenido que ya prometió la inspección (F5.0 — barato y vendible) — ✅ IMPLEMENTADO

**Mini-grafo MLP en la tarjeta de inspección** (aplazado explícitamente en
Fase 4, diseño UX §2.2). El cerebro es pequeño y estable — 19 sensores → 8
ocultas → 6 decisiones (`BrainSizes = {19, 8, 6}`), MLP feed-forward — así
que el grafo cabe y es legible:

- **Core (F5.0a) — HECHO**: nuevo **canal F** opt-in en el stream de juego:
  `--activ-every N --inspect <antId>` emite en cada tick múltiplo de N las
  activaciones del MLP de la hormiga inspeccionada como paquete base64 dentro
  del tick JSON (`"activ":""` formato `[total u16 LE][s8 × total]`, v = byte/128).
  `MlpBrain` registra las activaciones por Evaluate (sin evaluación extra,
  sin asignaciones por tick); la emisión re-construye sensores y re-evalúa —
  determinista, telemetría pura, **hash invariante** (test). Hormiga muerta o
  ausente ⇒ paquete vacío (la vista lo distingue por `alive` del canal A).
  Bono: fix estructural del cierre de línea del stream — el canal E (F4.5)
  añadía su paquete tras la llave de cierre y el JSONL quedaba inválido
  (los ticks con feromonas iban pegados al tick siguiente); ahora `Run`
  cierra cada objeto de tick tras los canales opt-in. Pins de CI intactos.
- **UI (F5.0b) — HECHO (v1 texto-art)**: `MlpAsciiGraph` en Core y
  `ActivationViewModel` (modelo puro de Unity, compilado headless) decodifican
  el paquete y renderizan el grafo por capas con nombres canónicos del
  contrato (sensores `FoodTrailCenter`…, salidas `Steer`…, `Interact`) y
  barra con signo por nodo. Aristas coloreadas por peso y uGUI/Texture: v2.
- **Contraste con decisiones validadas**: la tarjeta ya muestra la decisión
  tomada; el grafo muestra el POR QUÉ (qué sensor dominó).

**Criterio de cierre**: con warm-v2 sembrado, el grafo de una fundadora
muestra el canal de feromona-home dominando la decisión de volver al nido —
verificable headless contra las activaciones reales.

**Validación (10 tests nuevos, `ActivationCanalFTests`)**: hash invariante con
y sin canal; cada línea JSONL válida de principio a fin; paquete de 33 nodos
decodifica en rango; stream byte a bit determinista; hormiga ausente ⇒ paquete
vacío; `activ-every` sin `--inspect` rechazado por el CLI; render ASCII con
nombres canónicos y barra con signo; longitud incorrecta rechazada.

## 2. Pulido post-release de la capa Unity (F5.1 — deuda de presentación)

Lo que Fase 4 dejó como v1 de texto (todas las piezas existen, falta
acabado):

1. **uGUI por elemento** — ✅ HECHO (núcleo): `HudElementLayoutModel` (puro,
   headless-tested) produce un rect por toast con hit-test exacto
   (`ToastAt(px)` sustituye al «índice = mouse.y/22»), specs de barra de stock
   (fracción clampada + umbral 0.20 → color rojo) y specs de botones nativos
   (Confirmar/Cancelar/Reiniciar-con-plan con su condición de habilitación).
   `HudLayoutBehaviour` instancia un elemento POR toast (pool por key, sin
   parpadeos), pinta `fillAmount` por colonia y `BindNativeButtons` conecta
   los tres botones a las acciones ya verificadas. El bootstrapper crea
   contenedor/plantilla/barras/botones con referencias asignadas. Pendiente:
   chip de estado por runway y drag&drop.
2. **Drag & drop de `.antgenome`** — el diálogo de importación acepta ruta
   de texto; el punto de entrada prometido es soltar el archivo sobre la
   ventana (Unity `Application.openExternalFiles` / editor `DragAndDrop`).
3. **Gráficas por tarjeta** — ✅ HECHO (reserva, 2026-09-12):
   `ColonySparklineModel` (puro) guarda una muestra por SEGUNDO de simulación
   (30 ticks, el mismo ritmo que la ventana del canal C) con ventana de 90 s, y
   genera la textura RGBA de un gráfico de área; `ColonySparklineBehaviour` la
   sube a un `RawImage` de la tarjeta y la tiñe con el umbral del contrato
   (rojo < 20%, el mismo de la barra). La tarjeta creció a 204 px para que la
   banda no le robe sitio al texto. 11 tests headless (anillo, muestreo 1 Hz,
   reparto de columnas, umbral y una integración contra el stream real).
   Queda: la serie de DESCARGAS por ventana (hoy se grafica la reserva, que es
   la que explica la decisión) si el jugador la pide.
4. **Iconos, rich text y tipografía** — ✅ PARCIAL: el render v1 era texto
   plano con glifos emoji que la fuente por defecto de uGUI **no tiene** y
   pintaba como cajas (🥚 🐛 🛑 🟢 🔘 · 🐝); ahora los niveles son glifos de
   forma (○ ◐ ● ■ ·) y el título de la tarjeta va en negrita por rich text.
   Queda: fuente propia y sprites para hormiga/carga/huevo.
5. **Aspecto del mundo y entrada del HUD** — ✅ HECHO (pasada de pulido,
   2026-09-12):
   - **El tablero se lee como tierra**: el quad de feromonas llevaba el
     material por DEFECTO del primitivo (blanco y opaco) y tapaba el suelo y
     las hormigas — era el defecto que veía el jugador («el terreno es blanco
     y no se ven hormigas»). Material transparente propio + textura 1×1
     transparente de reposo (unlit-transparente SIN textura también es blanco
     opaco: por eso la escena se veía blanca sin darle a Play).
   - **Las hormigas se ven**: la cápsula se dibujaba VERTICAL (un poste de ~1 u
     de ancho, sub-píxel a cualquier zoom) y con una «escala» de 0,6 sin
     relación con el tamaño en pantalla. Ahora van tumbadas (Rx 90°),
     alargadas en la dirección de avance y con longitud DERIVADA del lado del
     mundo (0,018 × 768 u ≈ 14 u), con las portadoras un 25% mayores para que
     el relevo se lea sin abrir el HUD.
   - **Materiales unlit y medibles**: el color que se ve es el elegido (no
     depende del ambiente del editor) y la sonda de píxeles puede afirmar
     «hay hormigas» con tolerancias razonables. Mesa oscura bajo el tablero,
     marco claro, nido en dos piezas (montículo + disco de colonia).
   - **HUD en paneles**: barra de estado arriba (tick, reloj y resumen por
     colonia), tarjetas con barra de reserva dentro y título en negrita,
     historial y plan de drops apilados a la izquierda, inspector a la
     derecha, pistas de teclado centradas, y el diálogo de importación como
     modal CENTRADO con velo, campo de ruta y tres botones (Inspeccionar /
     Confirmar / Cancelar). Antes los botones del modal se pintaban siempre,
     flotando sobre el mundo, y no había ni forma de abrir el diálogo ni de
     darle la ruta del `.antgenome`: el pilar de importación era inalcanzable.
   - **Puertas de aspecto en el Play pass**: `ProbeVisualSample` mide píxeles
     (tablero terroso, hormigas visibles) y `ProbeSceneInspect` cuenta los
     textos que no caben en su panel; `playpass-live.sh` falla si el tablero
     no es terroso, si no se ve ninguna hormiga o si el HUD desborda. Todas
     las muestras de ESTADO estaban en verde mientras el mundo se veía blanco:
     el pass no podía fallar por algo que no medía.
6. **Feromonas por colonia** — ✅ HECHO (2026-09-12): el canal E clásico emitía
   home de la colonia 0, así que con dos colonias compitiendo se veía media
   partida. Ahora `--phero-every N --phero-layers 0:home,1:home,1:alarm` emite
   `pheroSet:[{c,k,d},…]` (colonia + tipo por paquete, en el orden pedido) y el
   render tiene selector: `F` cicla la capa (home/food/alarm), `G` la colonia, y
   cada capa tiene su color (verde casa, ámbar comida, rojo peligro). El canal
   clásico no cambia de forma (fixtures y pines intactos) y si la capa pedida no
   viene en el tick se pinta VACÍO en vez de dejar el rastro anterior. 21 tests
   (12 del emisor + 9 del selector), incluido el que compara los ordinales del
   Core con los del enum propio de Unity — la app Unity NO referencia el Core, y
   esa frontera es la que mantiene los modelos puros compilables headless.
   Queda: capa de territorio (cuando se deposite) y el chip de la capa activa en
   el HUD (hoy la pinta el render y la nombra `LayerLabel`).
7. **Audio y accesibilidad** — sonidos de evento (unload, eclosión, alerta)
   y escala de UI; sin diseño urgente, es lo único sin contrato previo.

**Criterio de cierre**: una partida de 10 min contra el fixture 256 jugable
solo con ratón, sin texto plano excepto la tarjeta de inspección.

## 2bis. Dos fases de experiencia (F5.1bis — la decisión del jugador, 2026-09-13)

El jugador pide explícitamente DOS maneras de usar el simulador, y la
arquitectura ya las soporta — lo que faltaba es nombrarlas y darles su pieza
de UI:

- **Fase A — EVOLUCIÓN (sin gráficos)**: mejorar cerebros rápido. Es el modo
  que ya existe y está maduro: `--mode pretrain` (arranque en frío),
  `--mode pretrain --warm-start` (refinado), `scripts/pipeline.sh` (cadena
  completa con `--verify` de regresión del relevo). Sin UI: corre headless y
  tan rápido como la CPU permita. Su salida es un `.antgenome` que la Fase B
  consume. **Nada que implementar — solo documentarlo como LA fase 1 del
  flujo del jugador.**
- **Fase B — OBSERVACIÓN/INTERVENCIÓN (en vivo, Unity)**: ver varias
  simulaciones a la vez, sembradas con los pools de la Fase A, e intervenir
  (drops). La pieza nueva es el **multi-visor**: N instancias del stream en
  una escena, cada una con su cámara-viewport (Camera.rect) y su
  `SimPresenterBehaviour` propio (Seed/SeedPoolPath independientes). El
  presenter ya renderiza por `Graphics.DrawMesh` parametrizado por su propio
  estado; el HUD por colonia ya existe. El montaje concreto:
  `MultiSimBootstrapper` crea N cámaras con `rect` dividido (2×2 para 4,
  1×N para 2), un presenter por viewport, y comparte el mundo de etiquetas.
  Coste real: el CLI corre un proceso por stream (ya es así para 1);
  `frame-every 2–4` baja el peso por stream.

**Criterio de cierre**: partida de 4 vistas — 4 semillas/4 pools corriendo
en paralelo a velocidad ≥2×, con el semáforo de relevo visible por vista.

**El montaje — HECHO (2026-09-13)**:
- `MultiViewportModel` (puro, 10 tests headless): geometría de los rects
  (1 = pantalla · 2 = columnas · 3–4 = cuadrícula 2×2, gap 0.005 entre
  vistas, vista 0 arriba-izquierda), el contrato de capas (Sim0..3 en los
  slots 8..11 del TagManager — un test fija el ordinal) y las etiquetas.
- `MultiSimBootstrapper` (menú **AntSim → Crear escena multi-visor**): por
  vista, una cámara con `Camera.rect` + `cullingMask` propio, el mundo
  COMPLETO en su capa (suelo, mesa, nidos, quad de feromonas, presenter),
  y su `SimPresenterBehaviour` con Seed/SeedPoolPath independientes.
  Guarda en `Assets/Scenes/MultiSim.unity` (nunca sobre Game.unity).
  Los pools se autodetectan de artifacts/ en el orden de la cadena validada
  (warm-v2 → warm-4 → warm3 → warm2); sin artifacts/, vistas sin sembrar
  listas para asignar pool desde el inspector.
- `SimPresenterBehaviour.RenderLayer` (nuevo campo): los DrawMesh de
  hormigas e ítems van a esa capa; 0 = Default (la escena simple no cambia).
- Verificado headless: compilación batch rc=0 / 0 error CS, ejecución del
  bootstrapper en batch (rc=0) e inspección del .unity como texto — 4
  cámaras con rects 0.4975² y culling masks 289/545/1057/2081, 8 objetos
  por capa en 8/9/10/11, seeds 42–45.
- **Tarjeta compacta por vista** (HECHO, 2026-09-13): `ViewCardBehaviour`
  bajo el rótulo de cada vista, alimentado por SU presenter. El texto sale
  de `ColonyCardModel.RenderCompact()` (puro, 4 tests): una línea POR
  COLONIA con semáforo (el mismo glifo del canal D), adultas, reserva y
  flujo rec/desc de la ventana — cabe en media pantalla, donde la tarjeta
  de 440 px no cabe. La reserva lleva además una franja vertical por
  colonia (Image fill con la MISMA regla del HUD grande: spec de
  `HudElementLayoutModel.StockBars`, rojo bajo el 20%).
- Defecto destapado en batch y corregido: un GameObject solo admite UN
  Graphic — el rótulo de vista lleva el fondo (Image) en el padre y el
  texto (Text) en un hijo.
- **Smoke EN VIVO del multi-visor (HECHO, 2026-09-13)**: dos streams
  sembrados volcados a archivo (v0: seed 42 + warm-v2 · v1: seed 77 +
  hybrid, grid 96, frame-every 2, 3602 líneas cada uno) y reproducidos por
  `MultiSimBootstrapper.CreateMultiSimSmokeScene` (2 vistas con
  `ReplayFile`, el mismo camino del jugador sin procesos del CLI). La sonda
  `MultiViewSmokeProbe` (batch, `-executeMethod …RunSmoke`: monta la escena
  y ENTRA en Play — Play sí corre en batchmode) muestreó tras 25 s:
  `V0 layer=8 mask=289 tick=748 ants=20 items=24` y `V1 layer=9 mask=545
  tick=748 ants=20 items=24`, con POSES DISTINTAS por vista (mundos
  independientes, semillas distintas) y tarjetas compactas con datos
  (`colonia 0 ○ 10h 88.3%` ×2 por vista, canal D llegando). Capas y máscaras
  exactamente las del contrato del modelo puro.
  Defectos destapados por el smoke y corregidos: la sonda batch no entraba
  en Play (añadido `RunSmoke` que la pide desde `update`), y el `ReplayFile`
  relativo se resuelve contra el repo-root del PROYECTO (una copia sin
  artifacts/ falla con «esperando stream…» — el smoke necesita los dumps
  junto al proyecto, o resolver contra el repo real).

**Criterio de cierre §2bis — VERIFICADO (2026-09-13)**:
«partida de 4 vistas — 4 semillas/4 pools corriendo en paralelo a velocidad
≥2×, con el semáforo de relevo visible por vista». Ejecución
`-executeMethod …MultiViewSmokeProbe.RunSmoke4` (batch, ventana 115 s):

| vista | pool (semilla) | capa/máscara | boost | tps | tick final | semáforo col.0 | col.1 |
|---|---|---|---|---|---|---|---|
| V0 | warm-v2 (42) | 8 / 289 | 2× | **60** | 6895 | **● verde** | ○ gris |
| V1 | hybrid (77) | 9 / 545 | 2× | **60** | 6895 | **● verde** | ○ gris |
| V2 | warm-4 (1234) | 10 / 1057 | 2× | **60** | 6895 | **● verde** | ○ gris |
| V3 | warm3 (777) | 11 / 2081 | 2× | **60** | 6895 | **● verde** | ○ gris |

- tps=60 = exactamente 2× (30 ticks/s de la arquitectura ×2): la velocidad
  ≥2× es MEDIDA, no estimada. Streams de 7200 ticks volcados con el primer
  unload dentro de la ventana (v0 t3943 · v3 t4007 · v1 t4457 · v2 t5414)
  para que el semáforo CAMBIE de estado en vivo: gris → verde en la colonia
  sembrada; la competidora sin sembrar queda gris — el contraste que el
  criterio pide, en las cuatro vistas a la vez.
- Defecto destapado y corregido: entrar en Play RECARGA el dominio y los
  statics de la sonda volvían a su inicializador (ventana 25 s, boost
  perdido) — la primera corrida del criterio midió 2× pero nunca llegó al
  unload. Los parámetros viajan ahora por `SessionState`, que sobrevive al
  reload, y el boost se re-aplica cada frame.
- Los cuatro fixtures (artifacts/multiview-v0..v3.jsonl, 7202 líneas cada
  uno) sustituyen a los de 3600: el criterio necesita ver el relevo vivo.

**Clon limpio (2026-09-13)** — el bootstrapper funciona desde `git clone`:
`CreateMultiSimScene` (4 vistas) y `CreateMultiSimSmokeScene` (2 vistas con
los fixtures trackeados) generan su escena en batch con 0 error CS, seeds
42–45, capas 8–11 y máscaras disjuntas idénticas a las del repo. La partida
en vivo del clon destapó un defecto de ENTORNO, no de código: el bucle de
jugador de un editor batch sobre un `Library` recién importado puede quedar
congelado en el frame 1 (`Time.frameCount` clavado, `dt=0`; correlado con la
excepción `SearchDatabase.EnumerateAll` de Unity 6 al indexar — 0 veces en
un Library caliente). La sonda lo detecta y, SOLO en batch, avanza el
reloj por `SimPresenterBehaviour.PumpOneTick()` (nueva entrada sin
dispositivo, misma filosofía F5.2): la reproducción del stream llegó a
tick 3600/3600 en las 2 vistas con poses distintas por vista y buffer
vacío al final — determinismo intacto, distinto quién empuja el reloj.
En interactivo el camino del jugador no cambia.

**Investigación del defecto (2026-09-13)** — qué es y qué no:
- **La excepción es de Unity, no del proyecto.**
  `ArgumentOutOfRangeException` en `SearchDatabase.EnumerateAll →
  SearchDatabase.GetDefaultSearchDatabase → SearchInit.IndexationOnStartup`
  desde `Internal_CallDelayFunctions` (el arranque del indexado de Search).
  Aparece EXACTAMENTE 2 veces por sesión en el clon (una por reload de
  dominio: arranque + entrada en Play) y 0 veces con Library caliente.
  Reproducida también reportada en el foro del beta de Unity 6.5 (hilo
  «Unity 6.5 Beta is now available», marzo 2026, con el MISMO stack) — bug
  conocido del indexador de Search en 6000.x, corregido en 6000.5+
  («Search: Fixed exceptions…», UUM-122130/UUM-141720 lineage). El proyecto
  lleva `6000.6.0f1`, que NO lo incluye.
- **No es ProjectSettings**: `EditorSettings.asset`, `QualitySettings`,
  `manifest.json` y `packages-lock.json` son byte-idénticos entre clon
  (falla) y copyproj (funciona). La diferencia es el ESTADO de `Library/`:
  el proyecto de copia fue importado por el editor interactivo del usuario;
  el clon, por un proceso batch. No hay setting de proyecto que lo
  controle — el toggle de indexado («Index Manager» / preferencias de
  Search) es por-USUARIO (EditorPrefs), no versionable.
- **La excepción NO es la causa directa del bucle congelado** (ocurre 2×
  y no cada tick), pero es su marcador fiable: nunca coexistieron «excepción
  presente» y «bucle vivo» en ninguna corrida. La hipótesis operativa: la
  primera pasada de indexado de un Library fresco consume el arranque del
  bucle del jugador en batch (con o sin excepción visible); en copyproj el
  indexado ya estaba hecho.
- **Mitigación adoptada**: la sonda detecta `frameCount ≤ 1` en batch y
  avanza por `PumpOneTick` (verificado: 3600/3600, 2× corridas del clon,
  reproducible). Mitigación de entorno para CI: pre-importar el proyecto
  con un `-batchmode -quit` DESCARTANDO la primera sesión antes del smoke
  (no verifica nada; solo calienta Library) — no lo hace innecesario el
  fallback de la sonda, que cubre ambos estados.

## 3. Realismo y especies (F5.2 — el contenido nuevo)

Lo que `arquitectura.md` §Fase 5 promete, en orden de dependencia:

1. **Especies diferenciadas**:
   - *Atta* (cortadora): cadena cortar→transportar→hongo. Necesita ítems
     compuestos (hoja = N carga) y un segundo objetivo de reserva (hongo);
     la economía ya soporta stock por colonia.
     **CERRADO** (2026-09-13): [`fase5-2a-atta.md`](fase5-2a-atta.md) —
     las 5 rodajas HECHAS (§6bis del doc): corte = `Interact` sobre un
     ítem con `CutsLeft` (genoma portable), hongo = segunda reserva con
     digestión proporcional que alimenta el inflow existente, `.antsave`
     v3, canales A/C con cuts/fungus/cutters, `--species`/`--leaf-fraction`
     en el CLI, 4º pin de hash en CI y humo visual del multi-visor con la
     tarjeta de la cortadora (barra de hongo + cortes) verificado en vivo
     (`RunSmokeAtta`) y headless (`AttaViewCardTests`).
   - *Eciton* (legionaria): ciclos nómadas y predación. Necesita feromona
     de alarma ofensiva (la capa Alarm ya existe y el canal la puede emitir)
     y objetivos móviles (las otras colonias).
     **CERRADO** (2026-09-14, `v0.6.0`): [`fase5-2b-eciton.md`](fase5-2b-eciton.md)
     — Eciton como ESPECIE que roba (no agente libre): combate en el paso
     de hormiga con `ContactRadius`/`StrikeDamage`/`StealPerStrike` por
     especie, botín como carga que el `Unload` existente convierte en
     inflow, detección por el canal 14 reconvertido (gating por especie),
     Alarm reusada como rastro de incursión, eventos 17–19 en canal B y
     `.antsave` v4. Las 5 rodajas HECHAS.
2. **Depredadores y agresividad inter-colonia** (como agentes libres):
   agentes no-colonia con cerebro propio (el contrato `IBrain`/`MlpBrain` es
   agnóstico del dueño); los eventos de combate entran al canal B como
   kinds nuevos. NOTA F5.2b: el combate inter-colonia SE HACE en F5.2b
   (Eciton especie que roba — [`fase5-2b-eciton.md`](fase5-2b-eciton.md));
   lo que aquí queda abierto es solo el agente SIN colonia (F5.2d posible).
3. **Competencia con apuestas observables**: hoy 2 colonias compiten y el
   semáforo contrasta sembrada vs natural; con especies el picker gana una
   dimensión (¿qué pool resiste a una invasora Eciton?).
4. **NEAT en vivo** (especificaciones §4, `NEAT (F5)`) — **CERRADO** (2026-09-15, `v0.7.0`, [`fase5-2c-neat.md`](fase5-2c-neat.md)): genes estructurales
   en `.antgenome` v2 (nodos/conexiones por innovation, orden canónico,
   topes 500/2000), inspector de grafos para topologías no fijas (el
   mini-grafo de F5.0 se generaliza), re-innovación determinista al
   importar. Es el hito más caro y el que más valor le da al modo evolución:
   topologías que EVOLUCIONAN delante del jugador.

**Criterio de cierre — CUMPLIDO con matiz** (2026-09-15): 3 especies con recetas distintas jugables; una
partida de invasión Eciton vs colonia Atta sembrada con pool propio, con el
grafo NEAT del mejor cortador visible. Matiz: las partidas canónicas usan presa Lasius (invasión) y presa + pool v2 (NEAT), no una Atta sembrada.

## 4. Escala y rendimiento (F5.3 — sostenibilidad de todo lo anterior)

- **SoA/ECS del `WorldSim` — HECHO (rodaja 1)**: `AntSoA` (arrays paralelos) ya existe; el layout actual (objetos por hormiga) aguanta
  2 colonias a 60 fps, pero N colonias y depredadores exigen migrar el bucle interior.
  Migración con pin de hash: los benchmarks y los 6 pines de CI son el
  arnés de regresión más estricto posible (el mundo NO cambió en las rodajas 1 y 2).
- **Compactación de muertas — HECHA (rodaja 2)**: el mundo apila las adultas
  muertas al final de cada `Step` (`BankAndCompactDeadAnts`); el fitness de por
  vida de las caídas va a un banco por colonia y `HashLine`/los checkpoints solo
  describen vivas (movió los pines de invasión y NEAT, a propósito).
- **Huella CHC — HECHA (rodaja 2bis)**: cuarta capa repelente + tropotaxis en
  ratio, sin canal nuevo y sin invalidar los `.antgenome`; checkpoint v5.
- **Benchmark de políticas — HECHO (rodaja 2ter)**: aleatoria vs scripted vs
  evolucionada con política congelada ([`politicas-benchmark.md`](politicas-benchmark.md)).
- **Curva de aprendizaje y cobertura — HECHAS (rodaja 2quater)**: bloques
  `learning`/`fitcurve` en el canal C + panel por tarjeta en el HUD.
- **LOD de feromonas — HECHO (rodaja 3)**: LOD **exacto** por bloques con
  soporte (16×16) en `PheromoneLayer`: la difusión del proyecto nunca llena una
  celda nula, así que las nulas no pueden cambiar y visitarlas era trabajo puro.
  La equivalencia se demuestra celda a celda contra la implementación de
  referencia a grid completo, y los 6 pines no se movieron (el mundo es el
  mismo). Medido: 12–14 % del grid visitado en un mundo forrajeado (1.2 % en un
  mundo casi limpio) — el ahorro se encoge según se extiende el rastro.
- **GPU instancing — HECHO (rodaja 3)**: el presenter agrupa por material y
  envía lotes de hasta 1023 instancias (`Graphics.DrawMeshInstanced`), con
  caída al camino de una llamada por objeto si la plataforma no lo soporta. El
  troceo vive en un modelo puro del esqueleto Unity
  (`Streaming/InstancedDrawPlan.cs`) compilado en la suite headless, y hay
  contador en vivo (`LastDrawCalls`, presupuesto 32).
- **Exit de fase** (de arquitectura): N colonias estables a 60 fps en la
  escena de juego. **Medido hasta donde llega el headless**: 8 colonias en
  grid 256 consumen el **3 %** del presupuesto de un frame a 60 fps en el Core
  y **17 llamadas de dibujo**; el framerate real de Unity sigue pendiente del
  Play pass en el editor. Detalle y cifras: [`fase5-3-escala.md`](fase5-3-escala.md).

## 5. Decisiones abiertas (se resuelven aquí, no antes)

| Decisión | Opciones | Recomendación |
|---|---|---|
| Orden F5.0 vs F5.1 | grafo primero vs pulido primero | **F5.0 primero**: es barato (1 semana), vendible (el cerebro visible es LA feature del modo evolución) y desbloquea el inspector NEAT de F5.2 |
| `.antgenome` v2 (NEAT) | formato nuevo vs extensible v1 | v2 con version validation (el camino v1→v2 de `.antsave` ya probó el patrón) |
| Feromona ofensiva de Eciton | reusar capa Alarm vs capa nueva | reusar Alarm (el canal E ya la puede emitir; solo cambia la semántica de depósito) |
| Formato de gráficas del HUD | historia en el stream vs reconstruida en UI | historia EN el stream (regla §6.2: la UI no suaviza ni re-deriva; el canal C ya trae las series) |
| Alcance del 2D→3D | Fase 6, sin adelantar | mantenerlo en Fase 6: el adaptador cambia, los contratos no |

## 6. Riesgos heredados y su estado

| Riesgo | Estado (Fase 5 en curso) |
|---|---|
| Determinismo roto | mitigado: 6 pines de hash en CI (stream canónico, replay 3000/6000, Atta, invasión, NEAT) + la constante del fixture en la suite; 463/463 en Windows y Linux |
| Fricción Unity↔netstandard | resuelto: modelos puros compilados en la suite desde F4.1 |
| Pre-entrenamiento no converge | resuelto: cadena de pools validada con transferencia 4/5 verdes |
| Feromonas costosas | abierto: RLE funciona para render y la huella CHC ya añadió una capa; el LOD de difusión es la rodaja 3 de F5.3 |
| NEAT estanca la evolución | mitigado: topología de respaldo + especiation medida en el pool (`.antgenome` v2, v0.7.0) |
| `error CS` de MonoBehaviours | abierto: el job `unity-compile` está corregido pero DORMIDO (necesita `UNITY_CI` + licencia); hoy solo lo caza la pasada local |

## 7. Qué NO es Fase 5

- **2D→3D** (Fase 6): el adaptador cambia, los contratos no; no adelantar.
- **Multijugador / seed-sharing UI**: fuera de alcance hasta v1.0.
- **Localización**: los textos del contrato del HUD son definitivos en
  español; traducirlos es decisión de release, no de fase.
- **Balance fino de especies**: se calibra con tests de balance como en
  F1–F3, no a mano.

## 8. Orden propuesto (resumen ejecutable)

```
F5.0  mini-grafo MLP (canal de activaciones opt-in + render headless)   ✅
F5.1  pulido uGUI (toasts-rect, drag&drop, gráficas, iconos)            ✅ (+5.1bis multi-visor)
F5.2a Atta: cortar→transportar→hongo (ítems compuestos + hongo)         ✅ CERRADO (2026-09-13)
F5.2b Eciton + depredadores (Alarm ofensiva, combate en canal B)        ✅ CERRADO (2026-09-14, v0.6.0)
F5.2c NEAT v2 (.antgenome v2 + inspector de grafos generalizado)        ✅ CERRADO (2026-09-15, v0.7.0)
F5.3  SoA/ECS + LOD feromonas + GPU instancing (pin de hash)            🔄 rodajas 1, 2, 2bis, 2ter, 2quater HECHAS
      └ siguiente: rodaja 3 — LOD de difusión de feromonas + GPU instancing
        (exit de fase: N colonias a 60 fps en la escena de juego)
```

Cada hito sale con: tests de hash (si toca el mundo o la telemetría),
tarjeta de contrato actualizada, y entrada cronológica en `arquitectura.md`.
