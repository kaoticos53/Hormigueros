# Estado del proyecto — consolidado

*Actualizado: 2026-09-20 · HEAD: `v0.8.0` (tag de cierre de la F5.3) ·
suite: 536/536 (Windows) · tags: `v0.4.0` (Fase 4), `v0.5.0` (F5.2a), `v0.6.0` (F5.2b),
`v0.6.1` (CI fix), `v0.7.0` (F5.2c NEAT), `v0.8.0` (F5.3 escala)*

Mapa de las fases del proyecto: qué está terminado, qué queda y dónde
estamos. Los detalles de cada fase viven en sus documentos; este es el
índice con veredicto. Enlaces: [`arquitectura.md`](arquitectura.md) (plan
global por fases), [`especificaciones.md`](especificaciones.md) (contratos),
[`fase5-plan.md`](fase5-plan.md) (plan vigente de Fase 5),
[`ideas-externas.md`](ideas-externas.md) (revisión de cuatro simuladores
afines con propuestas priorizadas por valor/coste) y
[`politicas-benchmark.md`](politicas-benchmark.md) (la vara externa: aleatoria
vs scripted vs evolucionada en la misma arena).

## Resumen en una tabla

| Fase | Alcance | Estado | Doc de cierre |
|---|---|---|---|
| 1–2 | Mundo determinista + neuroevolución (GA/MLP) | ✅ | `arquitectura.md` §Fase 2 |
| 3 / 3bis / 3ter | Pre-entrenamiento headless, arena realista, arranque en frío | ✅ | `fase3ter-resumen.md` |
| 4 (F4.0–F4.4) | Capa jugador: stream, HUD, inspector, import, CI con pines | ✅ `v0.4.0` | `fase4-resumen.md` |
| 5.0 | Mini-grafo MLP (canal F de activaciones) | ✅ | `fase5-plan.md` §1 |
| 5.1 + 5.1bis | Pulido Unity + multi-visor | ✅ | `fase5-plan.md` §2/§2bis |
| **5.2a** | **Atta: cortar → transportar → hongo** | ✅ **CERRADO** `v0.5.0` | `fase5-2a-atta.md` |
| **5.2b** | **Eciton: saqueo + combate** (sensor, botín, balance V6) | ✅ **CERRADO** `v0.6.0` | `fase5-2b-eciton.md` |
| **5.2c** | **NEAT / `.antgenome` v2 (topologías que evolucionan)** | ✅ **CERRADO** `v0.7.0` | `fase5-2c-neat.md` |
| — | **Cross-platform: CanonMath + cabecera del stream** | ✅ `v0.6.1` | `arquitectura.md` §CI |
| **5.3** | **Escala: SoA, compactación, CHC, benchmark, aprendizaje, LOD + instancing** | ✅ **CERRADO** `v0.8.0` (exit medido en un build de jugador) | `fase5-3-escala.md` |
| 6 | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **SEIS pines de hash**, verdes en Linux en el último
push (`v0.8.0`, run #37 del 2026-09-19): stream canónico, replay con drops a
3000 y 6000 ticks, la partida Atta canónica, la de INVASIÓN canónica y la
NEAT canónica. **536/536 tests** en Windows y verdes en Linux en CI en cada push,
igual que los 6 scripts de pin. El job `test` incluye además **cuatro pasos que
verifican instrumentos sin editor**: el analizador de compilación de Unity, las
guardas del Unity CLI y —desde el 2026-09-19— el **selftest del barrido de
costes**, que es el que prueba el ajuste fijo/marginal de §4.10.

**Guardarraíl de cifras documentales** (2026-09-20) — los documentos afirman el
número de pines y el de tests, y esas cifras se quedaron atrás más de una vez
(el README llegó a contar un pin de menos que hashes fijados; la lista de deuda
de F5.1 pedía cosas hechas) sin que nada pudiera verlo, porque los documentos no
estaban en ningún test. Ahora `scripts/check-doc-figures.sh` —paso propio en el
job `test`— mide la verdad del repo (pines = ficheros `scripts/*.expected`;
tests = casos descubiertos por la suite) y falla si un documento de estado
dice otra cosa. Dos escapatorias, las dos explícitas: las regiones
`<!-- cifras-historicas -->` (notas de versión, registro de rodajas, fases
cerradas: una cifra de un cierre pasado es correcta aunque no sea la de hoy) y
las bitácoras `docs/fase*-*.md`; un documento nuevo con cifras que no esté
clasificado **falla** en vez de colarse.

**Gate de Unity en CI — corregido, DORMIDO y con el alcance acotado** — el job
`unity-compile` compila los MonoBehaviours que `dotnet test` no ve, y hasta el
2026-09-20 nunca había corrido: necesita la variable `UNITY_CI` y un secreto de
licencia. Estaba
además mal para una licencia Personal (`unity license activate --personal` no
sirve en CI: el backend rechaza los tokens de cuenta de servicio), le
faltaban las librerías de runtime del editor y el `--yes --accept-eula` del
instalador. Los tres defectos se corrigieron contra el andamiaje oficial de
Unity (`unity ci init --dry-run`). Desde el 2026-09-19 tiene además
**disparador manual** (`workflow_dispatch`, input `unity`): `cache` ejercita
SOLO el paso de `actions/cache@v5` —lo que se quería comprobar del salto a v5
sin encender nada permanente— en segundos, sin licencia y saltando el job
`test` (que es el caro), con clave de sonda para no ocupar la real; `full` levanta el job entero en una sola pasada (y es
lo único que necesita el secreto de licencia).

El **2026-09-20 se ejercitó `unity: full` de verdad** (secreto de licencia
cargado, dispatch lanzado) y el resultado fue un **alcance acotado, no un
cierre**: la instalación del editor se arregló por el camino —`unity install`
resuelve contra un catálogo curado de una sola versión por línea, así que una
versión que sale del catálogo hay que instalarla con el `--changeset` que el
propio `ProjectVersion.txt` ya fija—, las librerías del editor también, y la
activación **falló con precisión**: con licencia Personal no hay vía en un
runner efímero. Dos pruebas independientes: `--personal --accept-eula` pide
sesión («Sign in with unity auth login»), y el `.xml` del Hub no es un ULF
(el cargador lo rechaza) y lleva cinco `Legacy.MachineBinding1..5` —hash y MAC
de esa máquina—, así que subirlo en base64 no podía funcionar. Salidas: (a)
`UNITY_RUNNER` apuntando el job a un **self-hosted** con editor y licencia del
Hub (implementado; sin ejercitar porque no hay runner registrado), (b) un
asiento de pago, o (c) la **puerta local**, que es la cobertura real de hoy:
`scripts/check-unity-compile.sh` —el MISMO script del job, **8 s** con la
Library caliente— corre en `.githooks/pre-push` (`scripts/install-hooks.sh`) y
bloquea **solo** con errores CS (exit 1); «no pude comprobar» avisa y deja
pasar. En Windows el mismo comando es `scripts/check-unity.bat`.

**NEAT de extremo a extremo** — genomas estructurales (nodos/conexiones por
innovation), paridad bit a bit con MLP (`FromMlp`), operadores
estructurales (add-node, add-conn, toggle), especiation por parentesco,
arena fitness evaluando grafos reales, pool v2 exportado/importado con
re-innovación canónica, grafo visible por canal F en el inspector. Pool
sembrado: `pretrain-neat.antgenome` (64 genomas, tope arena 1064.9, 11
especies). El jugador puede entrenar con `--mode pretrain --neat` y
sembrar el resultado.

**Tres especies jugables** — Lasius (forrajera), Atta (cortadora con
hongo), Eciton (legionaria con combate). Transferencia cross-especie
validada: warm-v2 funciona en cuerpo Atta y Eciton. Cada especie tiene su
pin CI y su partida canónica.

**Presentación Unity** — escena bootstrapeada en un comando:
terreno terroso, hormigas visibles y orientadas, feromonas por
colonia/capa (selector F/G), tarjetas con gráfica de reserva, modal de
importación, vida útil real de fundadoras (240 s), velocidad de replay
+/= (1→2→4×) y la banda de aprendizaje por tarjeta (curva + cobertura). La
escena se regeneró con ella el 2026-09-17 (`53eda0b`).

**Unity compila limpio** — 0 errores CS, 0 avisos CS en Unity 6000.6.0f1.
Canal F NEAT en el inspector (`ActivationViewModel.RenderNeat`
self-contained, sin dependencias de Core), pool NEAT en el picker
(`PoolPresets`), tarjeta de linaje con forma del cerebro (`n/h/c`).

**Multi-visor** (F5.1bis) — hasta 4 simulaciones EN VIVO en paralelo
(capas Sim0–3, tarjetas compactas por vista con su propio semáforo).

**Ítems compuestos + hongo** (F5.2a) — hojas con `CutsLeft`, segunda
reserva de hongo, digestión proporcional, eventos `LeafCut`/`LeafDepleted`
/`FungusFed`/`FungusDigested`.

**Saqueo + combate** (F5.2b) — Eciton roba stock (`ContactRadius 40`,
`StrikeDamage 2.5`, `StealPerStrike 5.0`), balance V6 calibrado con
sondas, canal 12 reconvertido a sensor de presa, eventos 17–19, bloque
`raids` en canal C.

## Lo que queda (en orden de dependencia)

<!-- cifras-historicas: registro de rodajas — cada cifra es la del cierre de SU rodaja, no la de hoy. `scripts/check-doc-figures.sh` ignora lo que va entre estas dos marcas; las cifras de HOY viven en la cabecera y en la tabla de arriba. -->

1. **F5.3 — registro de rodajas (TODAS HECHAS; sub-fase cerrada con `v0.8.0`).** Lo
   que queda FUERA de la sub-fase: la deuda menor de F5.1 (punto 2), el gate de Unity
   en CI —que sigue dormido tras la variable `UNITY_CI`— y una decisión de dependencias: los bloques
   3/4/5 del Play pass del editor necesitan `com.unity.pipeline`, que salió del
   manifest en la poda de paquetes (`unity command editor_status` responde «No
   Pipeline instance found»). La verificación visual end-to-end la cubre ahora la
   sonda del PLAYER, que corre la misma puerta de píxeles sobre frames reales.
   **F5.3 rodaja 1 — HECHA**: `AntSoA` (parallel arrays), infraestructura lista.
   **F5.3 rodaja 2 — HECHO (2026-09-17)**: el mundo COMPACTA las adultas muertas al final de cada `Step` (O(n), `BankAndCompactDeadAnts`). Claves del diseño: el fitness de por vida de las muertas va a un BANCO por colonia (el pool ya cobró en el tick de la muerte) que `HashLine`, el save y la arena suman para producir lo mismo que la lista con cadáveres; `HashLine` y los checkpoints solo describen vivas; los registros de shaping de la arena van claveados por `Id` (la posición en la lista ya no es identidad). Pines: solo invasión y NEAT se movieron (hay muertes en sus ventanas de hash); stream/replay/Atta son byte-idénticos. La fila del canal A desaparece en el tick de la muerte (contrato actualizado en `UnityStreamContractTests`): la muerte la captura el canal B.
   **F5.3 rodaja 2bis — HECHO (2026-09-17): huella CHC (4ª capa) y tropotaxis en ratio.**
   La señal NEGATIVA que faltaba, inspirada en [`ideas-externas.md`](ideas-externas.md) §1 (anthill):
   `PheromoneKind.Footprint` con depósito pasivo `Q_max_footprint·v·dt` (por unidad RECORRIDA, sin
   gasto de energía: es cutícula que se roza, no una decisión del cerebro), τ½ 240 s y difusión 0.06;
   el giro es un REFLEJO periférico `g·(der−izq)/(1+s·max)` — RATIO, no canal de sensor, así que los
   19 canales y los `.antgenome` no cambian. Calibrado con sonda: 0.08 u/u · g 8 · s 4 ⇒ ~0.33 rad/s
   (≈15 % de Ω_max). Evidencia del diseño: con 1 colonia a 7200 ticks el 5 % de celdas más pisadas
   pasa de concentrar el **49 % del tráfico al 23 %** y el máximo por celda baja de 0.98 a 0.83, sin
   coste económico (stock 41 → 46). Control A/B `--footprint 0` (depósito sí, respuesta no). Canal E:
   `k=4` y selector violeta en el HUD. Checkpoint **v5** (+1 capa; los v2–v4 cargan con huella a cero).
   Los 6 pines regenerados (el mundo cambia a propósito) y **443/443 tests** (463 con el benchmark de políticas y el panel de aprendizaje).
   **F5.3 rodaja 2ter — HECHO (2026-09-17): benchmark de políticas (idea nº 2 de `ideas-externas`).**
   La vara EXTERNA que faltaba: aleatoria vs scripted vs evolucionada en la misma arena, mismas
   semillas y **política congelada** (`WorldSim.ForcePolicy` — la descendencia hereda el mismo cerebro
   y no realimenta el pool, así que se mide el cerebro y no la evolución de la prueba). Nuevo
   `ScriptedBrain` (reglas explícitas: brújula + visión + rastro + vuelta al centro en la pared),
   `PolicyBenchmark` y `--mode bench`. Resultados (10 semillas, 2 bandas, `docs/politicas-benchmark.md`):
   la aleatoria está **muerta por construcción** (0 pickups en 20 partidas) mientras scripted y
   evolucionada empatan (diferencias ≤ 2 SE, y el orden se invierte con `--trials`); evolucionar
   durante la prueba no ayuda a estos horizontes. El hook es inerte sin política fijada (test de
   hash) ⇒ los 6 pines siguen valiendo.
   **F5.3 rodaja 2quater — HECHO (2026-09-17): curva de aprendizaje y cobertura en el canal C + HUD (idea nº 5 de `ideas-externas`).**
   `LearningTracker` en Core: por colonia, curva de fitness por GENERACIÓN (generación = nacimientos/64,
   la capacidad de la élite; y el fitness de una cohorte es el de TODAS sus hormigas, contabilizadas por
   deltas para no medir supervivencia) y cobertura del mundo leída de la huella CHC (celdas pisadas +
   radio máximo). Dos bloques nuevos en el canal C (`learning`, `fitcurve`) cada 120 ticks; `LearningPanelModel`
   + `LearningPanelBehaviour` en el HUD (curva por generación autoescalada, color por nivel de cobertura y
   dos líneas de datos en la tarjeta, que crece a 268 px). Telemetría pura: test de hash y 6 pines verdes;
   Unity compila en batch sin avisos. Evidencia: en 9000 ticks la colonia sembrada con warm-v2 conoce
   **2148/9216 celdas (23.3 %)** con fitness medio 4.7, y la fría 728 (7.9 %) con 1.8 — la diferencia de
   aprendizaje se ve en el HUD. **463/463 tests**.
   **F5.3 rodaja 3 — HECHO (2026-09-18): LOD de difusión EXACTO e instancing ([`fase5-3-escala.md`](fase5-3-escala.md)).**
   El LOD no aproxima: la difusión del proyecto nunca llena una celda nula (régimen original, no de esta
   rodaja), así que las nulas no pueden cambiar y visitarlas era trabajo puro. `PheromoneLayer` mantiene un
   contador de celdas no nulas por BLOQUE — **de 8×8 desde la rodaja 3bis**, que es el lado medido en
   [`fase5-3-escala.md`](fase5-3-escala.md) §4.6: el 16 histórico no ganaba en ningún mundo — y arma con ellos las regiones a visitar; el checkpoint
   DERIVA el soporte de los valores (`RebuildSupport`). La equivalencia se demuestra celda a celda contra una
   capa gemela con `LodEnabled = false` (implementación de referencia) sobre 400 operaciones aleatorias — el
   primer intento del test comparaba dos secuencias distintas y hay una nota en él sobre eso. Medido: 7.8–8.7 %
   del grid visitado en un mundo forrajeado (2.0 % en uno joven, 2.0 % a 512²) con el bloque 8 — **el ahorro se
   encoge según se extiende el rastro** (bloques, no celdas). Y en el render, el presenter agrupa por material y envía lotes de 1023
   instancias, con caída a una llamada por objeto si la plataforma no soporta instancing; el troceo es un modelo
   puro del esqueleto Unity (el Core no viaja a la vista) con contador en vivo (`LastDrawCalls`, presupuesto 32).
   Exit de F5.3 (grid 256, 6000 ticks, warm-v2, semilla 42): 1/2/4/8 colonias ⇒
   0.06/0.11/0.23/0.47 ms por tick (**2.8 % del frame a 60 fps con 8 colonias**), techo de velocidad ×596/×292/×144/×71.
   Y el lado de la VISTA, medido con `scripts/perf-scene.sh` (4 vistas × 2 colonias = 8 colonias en pantalla,
   grid 256) en sus **dos modos**: en batch **0.51 ms por frame** y, en el Play pass con ventana —bucle y presentación
   reales—, **2.44 ms por frame** (15 % del presupuesto a 60 fps), con **19 llamadas de dibujo** (todas instanciadas, 0
   de respaldo) y 292 hormigas + 684 ítems en pantalla en los dos casos. La sonda destapó **dos defectos reales**: `DrawMeshInstanced` LANZA si el
   material no tiene `enableInstancing` (los del bootstrapper no lo tenían: el tablero se quedaba SIN HORMIGAS, con una
   excepción por frame y 0 draw calls) — arreglado en los dos bootstrappers y con activación defensiva + contadores en
   el presenter; y la configuración de la sonda en statics se perdía en el reload de Play (movida a `SessionState`).
   **Los 6 pines NO se movieron** (el LOD es exacto: el mundo es byte a byte el mismo) y el proyecto Unity compila en
   batch con 0 errores y 0 avisos. **496/496 tests.**
   **F5.3 rodaja 3ter — HECHO (2026-09-19): el criterio de salida, medido en un BUILD de jugador.**
   `scripts/player-perf.sh` construye el player (escena del multi-visor, 4 vistas × 2 colonias, grid 256) y lo ejecuta
   con una sonda de runtime que muestrea frames/s y pasa la puerta de PÍXELES por vista. Medido: **59,99 fps de mediana
   con el refresco del monitor a 59,997 Hz y el vsync del proyecto aplicado** (peor muestra 55,6; 19 llamadas de dibujo
   todas instanciadas; 292 hormigas y 684 ítems en pantalla; tick 12 000) y **puerta verde en las cuatro vistas**
   (antPx 132–160). El primer build destapó **tres defectos reales**: el juego **no compilaba como player**
   (`DragAndDrop` de UnityEditor sin guarda → ocho CS0103; ahora `#if UNITY_EDITOR`), el módulo
   `com.unity.modules.screencapture` faltaba del manifest —la sonda del Play pass del editor llevaba sin compilar desde
   la poda— y la puerta medía con `AntMaterial` cuando el multi-visor pinta con `ColonyAntMaterials` (antPx=0 con 292
   hormigas dibujadas). La puerta es ahora UNA implementación pura (`FrameGate`) con 11 tests headless, compartida por
   el editor y el player. **512/512 tests · los 6 pines verdes sin regenerarse.** Evidencia: `artifacts/perf-player.json`.
   **F5.3 rodaja 3quater — HECHO (2026-09-19): el COSTE por frame dentro del build (5ª pieza del criterio).**
   El framerate presentado lo pone la pantalla: con el vsync entregando al refresco, el coste del frame queda tapado.
   `scripts/player-perf.sh --cpu` apaga el vsync, construye el player con los estadísticos de tiempos de frame
   (`enableFrameTimingStats`, que `PlayerBuild` enciende sólo para esa corrida y restaura al terminar) y la sonda
   cronometra el `FrameTimingManager` frame a frame. Medido con el mismo build y el mismo régimen que §4.7 (4 vistas ×
   2 colonias, grid 256, boost ×10, 292 hormigas + 684 ítems, tick 12 000): **1,294 ms de CPU por frame** (hilo
   principal 1,272 · hilo de render 0,277 · espera en Present 0,003) y **0,127 ms de GPU**, con **683,7 fps de techo sin
   vsync** (1,463 ms/frame producidos) = **7,8 % del presupuesto de 60 fps** y ×12,9 de margen; puerta de píxeles verde
   en las 4 vistas. El resumen es un modelo PURO (`FrameCost`, sin UnityEngine) con **12 tests headless**. **526/526
   tests · los 6 pines verdes sin regenerarse.** Evidencia: `artifacts/perf-player-cpu.json`.
   **F5.3 rodaja 3quinquies — HECHO (2026-09-19): el coste del SISTEMA COMPLETO (6ª pieza del criterio).**
   El número de la pieza anterior es el del **player**; el mundo lo simula el **CLI en otro proceso**, y esa pata no
   aparece en ningún tiempo de frame. `scripts/player-perf.sh --system` la mide: vsync apagado, calentamiento de 2 s
   (para que la fase de simulación caiga DENTRO de la ventana) y la CPU del proceso hijo leída en el mismo instante de
   muestreo. La primera corrida de este apartado subió el horizonte a 200 000 ticks «para que el CLI trabajara toda la
   ventana» y dio **cero datos** — este CLI simula el horizonte entero y escribe el stream al terminar—; la sonda lo
   detectó y suspendió con salida **10** en vez de publicar un verde vacío. Y al medir bien se destapó el defecto de
   fondo: **la CPU del CLI y la entrega de ticks están desacopladas** (6 s de los 9,7 s de CPU del hijo ya gastados a los
   2 s, cuando el player no había recibido ni un tick), así que emparejarlas por ventana daba **0,016 ms/tick** en vez de
   los 0,2 reales; el precio del tick es el **agregado de la vida entera del hijo** y la corrida exige que el CLI
   **termine su horizonte dentro de la ventana**. Medido (mismo build y régimen que §4.7/§4.8, 292 hormigas + 684 ítems,
   tick 12 000): **pata del player 1,116 ms/frame = 6,7 % del presupuesto** (margen ×14,9) y **pata del mundo 0,201
   ms/tick** (9 671,875 ms de CPU del CLI / 48 000 ticks) → con **200 ticks/frame** al reloj del juego, **41,415
   ms/frame agregados = 2,485 núcleos**, con el mundo siendo el **97,3 % de la CPU del sistema**; 9 ventanas simulando ·
   22 con el mundo en pantalla · 2 de solape. El modelo puro gana **10 tests headless** (`SystemCost`). **536/536 tests ·
   los 6 pines verdes sin regenerarse.** Evidencia: `artifacts/perf-player-system.json`.
   **Y cuánto juego cabe — HECHO (2026-09-19):** el barrido del precio del tick con **1, 2, 4 y 8 colonias por vista**
   (`bash scripts/system-sweep.sh`) mide **1,927 · 2,496 · 3,443 · 5,285 núcleos** al reloj del juego: doblar colonias
   **no** dobla el coste (×1,30 · ×1,38 · ×1,53, sublineal, porque **0,1203 ms/tick son fijos** —la difusión de las
   cuatro rejillas— y solo 0,00983 ms/tick siguen a cada colonia). El **coste fijo, medido y no extrapolado**: con 1
   colonia por vista (1,927 núcleos) el fijo son **1,507 núcleos** —1,443 del mundo + 0,064 de la vista—, el 78 % del
   sistema, y el ajuste sin ese punto lo predecía en 2,039 (−5,5 %): la incertidumbre de publicar un intercepto de tres
   puntos. La **amortización** explica la curva por colonia: 0,1187 + 1,507/n. La vista escala mejor que el mundo (1,064
   → 1,488 ms/frame) y con **32 colonias (1 075 hormigas)** el build sigue a 609 fps con la puerta verde en las cuatro
   vistas. Leído en presupuesto: **8 colonias en los 2,5 núcleos de §4.9**, 16 en 3,4, 32 en 5,3 (medidos). Evidencia:
   `artifacts/system-c{1,2,4,8}.json` + `artifacts/system-sweep.txt` (`docs/fase5-3-escala.md` §4.10).
   **Artefacto commiteado por detrás del generador — CORREGIDO (2026-09-19):** cuatro de los cinco materiales de
   feromonas tenían `_BaseMap` **sin textura** (`fileID: 0`), que con el material transparente y `_BaseColor` blanco
   significa **quad blanco OPACO tapando el tablero** mientras no llega ningún frame de feromonas (el defecto del Play
   pass de F5.1 por la puerta de atrás). El generador sí escribía la referencia: tres generaciones independientes —una
   de ellas borrando la textura de reposo para forzarla en la misma pasada— dan el material canónicamente idéntico. Los
   cuatro materiales quedan con su referencia (7 líneas) y **2 tests headless** (`PheromoneMaterialGuardTests`, en rojo
   sobre los materiales de HEAD antes del arreglo) impiden que vuelva en silencio: referencia presente, guía existente y
   textura 1×1 RGBA32 `ffffff00` (invisible).
2. **Deuda menor de F5.1** (no bloquea): chip de estado por runway, fuente propia y
   sprites (hormiga/carga/huevo), y la serie de DESCARGAS en la gráfica de reserva
   (hoy la sparkline guarda `Stock`; la serie de drops aún no se grafica).
   Ya HECHO, para no contarlo más como deuda: el drag & drop de `.antgenome`
   (`ImportDialogBehaviour`, `#if UNITY_EDITOR` — esa API es del editor y su guarda
   es lo que compila el player) y las hojas con mordiscos (`CutsInitial - CutsLeft`
   restado por el presenter).
3. **Fase 6 — 2D → 3D**: explícitamente fuera de Fase 5; el adaptador
   cambia, los contratos no.

<!-- /cifras-historicas -->

## Riesgos abiertos

| Riesgo | Estado |
|---|---|
| NEAT estanca la evolución | mitigado — fallback de topología fija; meritocracia de arena medida |
| `error CS` de MonoBehaviours | **abierto, con el alcance ya acotado (F5.3, 2026-09-20)** — el job existe y está corregido, pero dormido, y el 2026-09-20 se **ejercitó `unity: full` con un secreto de licencia real**: el camino **no cierra** con licencia Personal (Unity no la activa en un runner efímero: `--personal` pide sesión y el fichero del Hub está atado a la máquina — dos pruebas independientes en el comentario del job). Quedan tres salidas: (a) `UNITY_RUNNER` → self-hosted con el editor ya licenciado (implementado, **sin ejercitar**: no hay runner); (b) un asiento de pago (serial legacy o `.ulf` Enterprise/Industry); (c) la **puerta local**, que es la cobertura de HOY: `scripts/check-unity-compile.sh` (mismo script que el job, **8 s** con la Library caliente) corre en `.githooks/pre-push` y bloquea solo con errores CS de verdad |
| Feromonas costosas a escala | **mitigado (F5.3 rodajas 3 y 3bis)** — LOD exacto con bloque 8 medido: 7.8–8.7 % del grid visitado en un mundo forrajeado y la reconstrucción de la lista por debajo del 1 % del tick; el RLE ya resolvía el envío del canal E |
| Framerate real de Unity a N colonias | **cerrado (F5.3 rodajas 3 y 3ter)** — 8 colonias: 0.51 ms/frame en batch, **2.44 ms/frame con el editor en ventana** y, en un **build de jugador**, **59,99 fps con el refresco del monitor** (vsync aplicado) y 19 llamadas (todas instanciadas). El coste por frame DENTRO del build ya está medido (**1,294 ms de CPU, 7,8 % del presupuesto**, §4.8) y el del **sistema completo** también (**2,485 núcleos** al reloj del juego, §4.9), igual que su barrido por colonias (**1,927 · 2,496 · 3,443 · 5,285 núcleos** con 1, 2, 4 y 8 por vista, §4.10, con el fijo **medido** en 1,507 en vez de extrapolado). Lo que sigue sin medir: el precio del tick **por encima de 8 colonias por vista**, el **barrido de VISTAS** (con 4 fijas no se separa el fijo por mundo del fijo por montaje), los tiempos de GPU por otra vía y una segunda máquina |
| El juego compila como player | **cerrado (F5.3 rodaja 3ter)** — el primer build destapó CS0103 de `DragAndDrop` (API del editor sin guarda): el editor compilaba Assembly-CSharp con referencia a UnityEditor y el player no. `PlayerBuild` construye en 18 s y la sonda del player falla si la escena no pinta hormigas |
| Bug de Unity Search (Library fría en batch) | mitigado — fallback `PumpOneTick` en las sondas |
| Balance de especies | se calibrará con tests de balance contra el benchmark de pools |
| Pool Atta desde cero | **resuelto** — la sonda de transferencia validó warm-v2 en cuerpo Atta |
