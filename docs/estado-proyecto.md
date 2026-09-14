# Estado del proyecto — consolidado

*Actualizado: 2026-09-14 · HEAD: F5.2b CERRADO (cuerpo → sensor → contratos → balance V6 → 5º pin) ·
suite: 315/315 · tag: `v0.4.0` (cierre de Fase 4), `v0.5.0` (cierre de F5.2a)*

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
| 5.2c | NEAT / `.antgenome` v2 (topologías que evolucionan) | 🔲 | — |
| 5.3 | Escala: SoA/ECS, LOD de feromonas, GPU instancing | 🔲 | — |
| 6 | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **CINCO pines de hash**, verificados los cinco
en una pasada el 2026-09-14 tras el cierre de F5.2b: stream canónico,
replay con drops a 3000 y 6000 ticks, la partida Atta canónica
(`check-atta-command.sh`) y la partida de INVASIÓN canónica
(`check-invasion-command.sh`, el primer pin con combate); 315/315 tests.

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

El diseño de la cadena cortar→transportar→hongo está CERRADO
([`fase5-2a-atta.md`](fase5-2a-atta.md)) y las DOS primeras rodajas están
HECHAS:

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

**F5.2a CERRADO.** Lo que sigue es F5.2b (Eciton) — la deuda menor de
renderizado de hojas en el tablero Unity queda anotada en el doc de Atta
(§6bis).

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
