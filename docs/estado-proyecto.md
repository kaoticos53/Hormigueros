# Estado del proyecto — consolidado

*Actualizado: 2026-09-16 · HEAD: `dbc994f` (Unity NEAT wiring) ·
suite: 379/379 (Windows y Linux) · tags: `v0.4.0` (Fase 4), `v0.5.0` (F5.2a), `v0.6.0` (F5.2b), `v0.6.1` (CI fix), `v0.7.0` (F5.2c NEAT)*

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
| **5.2a** | **Atta: cortar → transportar → hongo** | ✅ **CERRADO** `v0.5.0` | `fase5-2a-atta.md` |
| **5.2b** | **Eciton: saqueo + combate** (sensor, botín, balance V6) | ✅ **CERRADO** `v0.6.0` | `fase5-2b-eciton.md` |
| **5.2c** | **NEAT / `.antgenome` v2 (topologías que evolucionan)** | ✅ **CERRADO** `v0.7.0` | `fase5-2c-neat.md` |
| — | **Cross-platform: CanonMath + cabecera del stream** | ✅ `v0.6.1` | `arquitectura.md` §CI |
| 5.3 | Escala: SoA/ECS, LOD de feromonas, GPU instancing | 🔲 | — |
| 6 | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **SEIS pines de hash**, verificados en una pasada
el 2026-09-15: stream canónico, replay con drops a 3000 y 6000 ticks, la
partida Atta canónica, la partida de INVASIÓN canónica, y la partida NEAT
canónica (nuevo). **379/379 tests** en Windows Y Linux.

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
+/= (1→2→4×).

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

1. **F5.3 — Escala**: SoA/ECS del `WorldSim` (migración con pin de hash —
   el arnés de regresión más estricto posible), LOD de difusión de
   feromonas, GPU instancing en el presenter. Exit: N colonias a 60 fps.
2. **Deuda menor de F5.1** (no bloquea): drag & drop de `.antgenome`,
   chip de estado por runway, fuente propia y sprites
   (hormiga/carga/huevo), serie de descargas en la gráfica, y render de
   hojas con mordiscos en el presenter (el canal A ya emite `cuts` y la
   tarjeta ya lee el hongo/cortes; el tablero aún pinta las hojas como
   esferas simples).
3. **Fase 6 — 2D → 3D**: explícitamente fuera de Fase 5; el adaptador
   cambia, los contratos no.

## Riesgos abiertos

| Riesgo | Estado |
|---|---|
| NEAT estanca la evolución | mitigado — fallback de topología fija; meritocracia de arena medida |
| Feromonas costosas a escala | abierto — RLE resuelve render; la difusión es F5.3 |
| Bug de Unity Search (Library fría en batch) | mitigado — fallback `PumpOneTick` en las sondas |
| Balance de especies | se calibrará con tests de balance contra el benchmark de pools |
| Pool Atta desde cero | **resuelto** — la sonda de transferencia validó warm-v2 en cuerpo Atta |
