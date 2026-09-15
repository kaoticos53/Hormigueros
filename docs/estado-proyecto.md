# Estado del proyecto — consolidado

*Actualizado: 2026-09-14 · HEAD: F5.2b CERRADO (cuerpo → sensor → contratos → balance V6 → 5º pin) ·
suite: 338/338 (Windows y Linux) · tag: `v0.4.0` (cierre de Fase 4), `v0.5.0` (cierre de F5.2a)*

Mapa de las fases del proyecto: qué está terminado, qué queda y dónde
estamos. Los detalles de cada fase viven en sus documentos; este es el
índice con veredicto. Enlaces: [`arquitectura.md`](arquitectura.md) (plan
global por fases), [`especificaciones.md`](especificaciones.md) (contratos),
[`fase5-plan.md`](fase5-plan.md) (plan vigente de Fase 5).

## Resumen en una tabla

| Fase | Alcance | Estado | Doc de cierre |
|---|---|---|---|
| 1–2 | Mundo determinista + neuroevolución (GA/MLP) | ✅ | `arquitectura.md` §Fase 2 |
| 3 / 3bis / 3ter | Pre-entrenamiento headless, arena realista, arranque en frío | ✅ | `fase3ter-resumen.md` |
| 4 (F4.0–F4.4) | Capa jugador: stream, HUD, inspector, import, CI con pines | ✅ `v0.4.0` | `fase4-resumen.md` |
| 5.0 | Mini-grafo MLP (canal F de activaciones) | ✅ | `fase5-plan.md` §1 |
| 5.1 + 5.1bis | Pulido Unity + multi-visor | ✅ | `fase5-plan.md` §2/§2bis |
| **5.2a** | **Atta: cortar → transportar → hongo** | ✅ **CERRADO** (5 rodajas) | `fase5-2a-atta.md` |
| **5.2b** | **Eciton: saqueo + combate** (sensor, botín, balance V6) | ✅ **CERRADO** (5 rodajas) | `fase5-2b-eciton.md` |
| 5.2c | NEAT / `.antgenome` v2 (topologías que evolucionan) | 🚧 rodaja 1/7 | `fase5-2c-neat.md` |
| — | **Cross-platform: CanonMath + cabecera del stream** (CI de ubuntu rota desde 09-12) | ✅ | `arquitectura.md` §CI |
| 5.3 | Escala: SoA/ECS, LOD de feromonas, GPU instancing | 🔲 | — |
| 6 | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **CINCO pines de hash**, verificados los cinco
en una pasada el 2026-09-14 tras el cierre de F5.2b: stream canónico,
replay con drops a 3000 y 6000 ticks, la partida Atta canónica
(`check-atta-command.sh`) y la partida de INVASIÓN canónica
(`check-invasion-command.sh`, el primer pin con combate); 338/338 tests en Windows Y Linux (ver abajo).

**Pre-entrenamiento con transferencia validada** — cadena de 8+ pools
(frío → warm-starts encadenados → híbrido de dos bandas), benchmark de
referencia multi-semilla con `--verify` de regresión del relevo
(first-unload, drop-avg, carry-leg). El mejor pool del modo evolución:
`pretrain-warm-v2.antgenome`.

**Modo evolución jugable** (Fase 4, tag `v0.4.0`) — el bucle completo
demostrado end-to-end: elegir pool → ver en vivo → inspeccionar hormiga
(con linaje de cerebros y mini-grafo MLP) → intervenir (`--drop`
determinista) → verificar el hash. Import de `.antgenome` con cuarentena,
HUD por-elemento uGUI, alertas derivadas (AlertDeriver) y semáforo de
relevo por colonia.

**Presentación Unity** (F5.1) — escena bootstrapeada en un comando:
terreno terroso, hormigas visibles y orientadas, feromonas por
colonia/capa (selector F/G), tarjetas con gráfica de reserva, modal de
importación, vida útil real de fundadoras (240 s), velocidad de replay
+/= (1→2→4×).

**Multi-visor** (F5.1bis) — hasta 4 simulaciones EN VIVO en paralelo
(capas Sim0–3, tarjetas compactas por vista con su propio semáforo).
Criterio de cierre verificado: 4 vistas a 60 tps reales (2× medido) con
gris → verde en la colonia sembrada de las cuatro. Verificado también
desde clon limpio (con fallback determinista `PumpOneTick` cuando el
bucle de jugador de un editor batch sobre Library fría se congela — bug
conocido del indexador de Search de Unity 6000.x, investigado y
documentado en `fase5-plan.md` §2bis).

**Ítems compuestos** (F5.2a.1) — las hojas son ítems con `CutsLeft`: el
pickup corta un fragmento y la hoja sobrevive; corte = `Interact`, sin
cambios en el cerebro. Eventos `LeafCut`/`LeafDepleted`, `.antsave` v3,
`--leaf-fraction` en el CLI, hash invariante sin hojas (los 3 pines de CI
no se movieron). Transferencia cross-especie VALIDADA: warm-v2 en cuerpo
Atta descarga igual o más que en su cuerpo de entrenamiento (12 vs 11
unloads a 12 000 ticks) y con hojas ambas especies cortan sin
entrenamiento — F5.2a-bis (currículo Atta) postergado por innecesario.

**El hongo** (F5.2a.2) — segunda reserva de la cortadora: la descarga de
fragmento alimenta `Colony.Fungus` (× LeafEfficiency 0.75) y la digestión
proporcional al llenado entra por `RecordInflow` — la demografía calibrada
lee la misma señal. Eventos `FungusFed`/`FungusDigested`, fungus en canal A
y `.antsave` v3, `--species atta,lasius` en el CLI. Humo real (7200 ticks,
hojas 100 %, warm-v2): 12 cortes → 3 descargas → 3 FungusFed → 1898 ticks
de digestión; la Lasius compite sin hongo. 293/293 suite, pines intactos.

## Dónde estamos: F5.2b cerrado — la siguiente es F5.2c (NEAT)

La cadena del SAQUEO está CERRADA ([`fase5-2b-eciton.md`](fase5-2b-eciton.md)):
las 5 rodajas HECHAS y el criterio de cierre §9 verificado 4/4 sobre la
partida canónica del 5º pin:

- ✅ **F5.2a.1 — ítems compuestos**: `FoodItem.CutsLeft/CutsInitial`,
  `LeafFraction` en spawn (por constructor), eventos 13/14, hash solo con
  hojas, `.antsave` v3, canal A con `[id,x,y,amount,cutsLeft,cutsInitial]`
  en hojas, `--leaf-fraction` (modes `world`/`game`). 10 tests nuevos;
  humo sembrado (warm-v2, 6000 ticks, hojas 100 %): 14 cortes → 3
  descargas, 9 hojas mordidas.
- ✅ **F5.2a.2 — hongo**: `Colony.Fungus/FungusMax`, descarga por especie,
  digestión proporcional que alimenta el inflow existente, eventos
  `FungusFed`/`FungusDigested`, fungus en canal A y `.antsave` v3. 7 tests
  nuevos; humo real con la cadena completa visible (corte → descarga →
  FungusFed → digestión).
- ✅ **F5.2a.3 — contratos + parser Unity** (`d491f6b`, `9f053d5`): canal C
  con `leafCuts`/`fungusFed` y bloque `cutters` por colonia;
  `GameStreamParser` consume `cuts`/`fungus` (fix de `ArrayBody`, que
  truncaba cada colonia en su array `nest` anidado).
- ✅ **F5.2a.4 — pin Atta canónico en CI — HECHO** (`854dcad`; los 4 pines
  verificados en una pasada el 2026-09-13, todos verdes): cuarto pin de
  regresión (`check-atta-command.sh`) sobre la partida de la cortadora —
  el primero cuyo mundo DEPENDE de los campos nuevos.
- ✅ **F5.2a.5 — humo visual del multi-visor**: tarjeta de vista con la
  cadena de la cortadora — modelo puro que acumula las ventanas de
  cutters (canal C), línea `hongo [#.........]` solo para colonias con
  hongo, línea de cortes solo con actividad, y BARRA ocre del hongo por
  colonia en `ViewCardBehaviour`. Criterio verificado dos veces: en vivo
  (`MultiViewSmokeProbe.RunSmokeAtta`, batch, 8 aserciones, salida 0) y
  headless (`AttaViewCardTests` contra el stream canónico).

- ✅ **Sonda de transferencia** (§4.1): el genoma es portable entre
  especies — validado empíricamente, no solo por diseño.
- ✅ **Sonda del bucle biológico** (§6.1): eclosiones sostenidas por el
  hongo tras agotar la reserva fundadora (8 eclosiones, 6 698 ticks de
  digestión) — la cadena cortar→transportar→hongo→cría cierra.

- ✅ **F5.2b.1 — cuerpo del combate** (`6bd1216`, `99a05f5`, `252b72d`):
  `ContactRadius`/`StrikeDamage`/`StealPerStrike` en el descriptor (0 =
  pacífica), `TryStrike` determinista en `Act` (presa más cercana, empate
  por antId menor, sin RNG nuevo), robo como carga (`LoadIsLoot`), eventos
  17/18/19, alarma ofensiva en la capa de la presa, `.antsave` v4. 7 tests.
- ✅ **F5.2b.2 — sensor de presa** (`ba355ff`, `cd6c25a`, `75e8215`): canal
  12 (`ProxFront`) reconvertido a «enemigo más cercana» con gating por
  especie + mundo multi-colonia (mundos sin Eciton bit-idénticos). Sonda
  honesta: ×9 contactos pero el robo no sube — el cuello era económico.
- ✅ **F5.2b.3 — contratos** (`c508238`): bloque `raids` del canal C con
  contadores ACUMULADOS (`TakeRaids` — los golpes son raros y la ventana
  de 1 s los pasaba sin verlos), `RaidView` en el parser Unity, causa
  combate (byte 2) en el inspector. 4 tests (`RaidContractTests`).
- ✅ **F5.2b.4 — balance V6** (`8ab2452`): calibración con 3 sondas —
  `ContactRadius 6→40` (la aguja real), `StrikeDamage 0.35→2.5`,
  `StealPerStrike 0.30→5.0`, economía de la legionaria igualada a la de
  la presa. Asfixia económica: 2/5 semillas con extinción acelerada
  ≥500 ticks, resto Δ≤185 (juego, no aniquilación); el botín ya paga
  (+1113 ticks de vida del saqueador con contacto). Daño alto
  contraproduce: la presa muerta sale del contacto. `CombatTests` a
  escena remota (con R=40 el nido de la presa ya no aísla).
- ✅ **F5.2b.5 — tarjeta raids + 5º pin** (`8b25375`): línea
  `raids N · M al nido` SOLO en la colonia beligerante (modelo puro +
  `RaidViewCardTests`), quinto pin CI (`check-invasion-command.sh`,
  hash `ac53b753…` — Eciton SEMBRADA con warm-v2, transferencia
  validada §8quater). Criterio §9 4/4: 12 Strike, 1 RaidInflow,
  9 StockRobbed (21.88 ep), 2 muertes combate.

**CI VERDE OTRA VEZ — DETERMINISMO CROSS-PLATFORM (F5.2c, 2026-09-15).**
La CI de ubuntu fallaba desde el 2026-09-12 y la suite local (Windows) no lo
veía. Dos causas raíz, ambas encontradas reproduciendo la CI en WSL: (1) las
trascendentes de `MathF` difieren 1 ULP entre UCRT y glibc — el mundo diverge
desde el tick 1 y los pines de hash eran imposibles cross-platform; sustituidas
por `Sim/CanonMath` (series en double, bit-exactas en todo SO, 10 tests
propios); (2) `GameScenario.Run` recortaba la cabecera con `sb.Length -= 3`
asumiendo `\r\n` de Windows — en Linux se comía una llave y TODOS los juegos
con canales opt-in (E/E-mult/F) emitían JSONL inválido. Los 5 pines se
regeneraron (el mundo es ligeramente distinto bajo CanonMath: cambio
INTENCIONAL y documentado) y hoy pasan en ambos SO. Además: el resolutor de
rutas del Unity CLI es simétrico cross-platform (carpeta de publicación en
Linux también) y su test de `.exe` está portado.

**F5.2b CERRADO.** Lo que sigue es F5.2c (NEAT / `.antgenome` v2), ya en
curso: la rodaja 1 (genes estructurales + cerebro de grafo + conversión v1→v2
con paridad BIT A BIT) está hecha — `NeatGenome`/`NeatBrain` con 13 tests
propios, incluyendo la paridad contra genomas reales del pool warm-v2. La
sonda de paridad destapó además un defecto latente del `MlpBrain`: el registro
de activaciones del canal F escribía con offsets de float donde `Buffer.BlockCopy`
esperaba BYTES, por lo que las ocultas aterrizaron siempre en el lugar
equivocado; corregido — el canal F v1 ahora emite el layout documentado
`[entradas, ocultas, salidas]` (los 5 pines de hash siguen intactos: el canal
F es opt-in y ningún mundo fijado lo activa). La
deuda menor de renderizado de hojas en el tablero Unity sigue anotada en
el doc de Atta (§6bis).

## Lo que queda (en orden de dependencia)

1. **F5.2b — Eciton + depredadores** — ✅ **CERRADO** (2026-09-14, las 5
   rodajas, ver [`fase5-2b-eciton.md`](fase5-2b-eciton.md)): Eciton como
   especie que ROBA stock ajeno (no agente libre): combate en el paso de
   hormiga (`ContactRadius 40`/`StrikeDamage 2.5`/`StealPerStrike 5.0` —
   calibración V6, asfixia económica: 2/5 semillas con extinción
   acelerada ≥500 ticks y el botín ya paga +1113 ticks de vida al
   saqueador), botín como carga con el `Unload` existente, canal 12
   reconvertido a sensor de presa con gating por especie, Alarm reusada
   como rastro de incursión, eventos 17–19 en canal B, bloque `raids` en
   canal C, tarjeta de vista con línea de saqueo, `.antsave` v4 y el 5º
   pin CI. El agente libre sin colonia queda como posible F5.2d.
3. **F5.2c — NEAT / `.antgenome` v2**: genes estructurales
   (nodos/conexiones por innovation), inspector de grafos generalizado,
   re-innovación determinista al importar. El hito más caro; red de
   seguridad: topología MLP fija como fallback documentado.
   **Diseño cerrado** (2026-09-15) en
   [`fase5-2c-neat.md`](fase5-2c-neat.md): contrato IBrain v1 congelado
   (19/6), innovación local + re-innovación canónica al importar (sin
   contador global), align-by-innovation + especiation por δ, topes
   500/2000, v2 acepta v1 en carga, 7 rodajas con métricas de
   no-estancamiento explícitas.
4. **F5.3 — Escala**: SoA/ECS del `WorldSim` (migración con pin de hash —
   el arnés de regresión más estricto posible), LOD de difusión de
   feromonas, GPU instancing en el presenter. Exit: N colonias a 60 fps.
5. **Deuda menor de F5.1** (no bloquea): drag & drop de `.antgenome`,
   chip de estado por runway, fuente propia y sprites
   (hormiga/carga/huevo), serie de descargas en la gráfica, y render de
   hojas con mordiscos en el presenter (el canal A ya emite `cuts` y la
   tarjeta ya lee el hongo/cortes; el tablero aún pinta las hojas como
   esferas simples).
6. **Fase 6 — 2D → 3D**: explícitamente fuera de Fase 5; el adaptador
   cambia, los contratos no.

## Riesgos abiertos

| Riesgo | Estado |
|---|---|
| NEAT estanca la evolución | abierto — empieza con fallback de topología fija |
| Feromonas costosas a escala | abierto — RLE resuelve render; la difusión es F5.3 |
| Bug de Unity Search (Library fría en batch) | mitigado — fallback `PumpOneTick` en las sondas; pre-import de CI documentado como alternativa |
| Balance de especies | se calibrará con tests de balance contra el benchmark de pools, no a mano |
| Pool Atta desde cero | **resuelto** — la sonda de transferencia validó warm-v2 en cuerpo Atta; el currículo propio (F5.2a-bis) solo si la evolución en vivo no alcanza |
