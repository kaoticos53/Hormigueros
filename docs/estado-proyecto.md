# Estado del proyecto — consolidado

*Actualizado: 2026-09-13 · HEAD: `acccfce` (post F5.1bis) · suite: 276/276 ·
tag: `v0.4.0` (cierre de Fase 4)*

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
| **5.2a** | **Atta: cortar → transportar → hongo** | 🔲 **diseño cerrado, sin implementar** | `fase5-2a-atta.md` |
| 5.2b | Eciton + depredadores (alarma ofensiva, combate) | 🔲 | — |
| 5.2c | NEAT / `.antgenome` v2 (topologías que evolucionan) | 🔲 | — |
| 5.3 | Escala: SoA/ECS, LOD de feromonas, GPU instancing | 🔲 | — |
| 6 | Migración 2D → 3D | 🔲 fuera de alcance de Fase 5 | — |

## Lo que ya funciona (verificado, no prometido)

**Core headless y determinista** — semilla + comandos ⇒ mundo idéntico bit
a bit. Fijado en CI con **3 pines de hash** (fixture canónico, replay con
drops a 3000 y 6000 ticks) más el pin del stream; 276/276 tests.

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

## Dónde estamos: cerrando Fase 5, entrando en especies

La Fase 5 lleva dos hitos entregados (F5.0, F5.1/5.1bis). **El siguiente
trabajo real de contenido es F5.2a (Atta)**, y su diseño está CERRADO
([`fase5-2a-atta.md`](fase5-2a-atta.md), 2026-09-13):

- **Corte = `Interact`** sobre ítems compuestos (`FoodItem.CutsLeft`,
  spawn con `LeafFraction`): el genoma sigue portable entre especies, la
  novedad vive en el ítem, no en el cerebro.
- **Hongo = segunda reserva** (`Colony.Fungus`) cuya digestión alimenta
  el inflow existente — toda la demografía calibrada en F1–F3 no se toca.
- Canales A/B/C con campos/eventos nuevos, `.antsave` v2, `--species` y
  `--leaf-fraction` en el CLI. Implementación en 5 rodajas con hash
  invariante cuando `LeafFraction = 0`.

## Lo que queda (en orden de dependencia)

1. **F5.2a — Atta** (diseño cerrado): rodajas 5.2a.1 ítems compuestos →
   5.2a.2 hongo + `.antsave` v2 → 5.2a.3 contratos A/B/C → 5.2a.4 CLI +
   pin de hash de la partida Atta canónica → 5.2a.5 humo visual en el
   multi-visor. Criterio: Atta sembrada muestra la cadena completa
   (`LeafCut` → portador → `FungusFed` → digestión → eclosión) y los 3
   pines actuales siguen verdes.
2. **F5.2b — Eciton + depredadores**: alarma ofensiva (reusar la capa
   Alarm), objetivos móviles, combate en canal B. Criterio de fase: una
   partida de invasión Eciton vs colonia Atta sembrada.
3. **F5.2c — NEAT / `.antgenome` v2**: genes estructurales
   (nodos/conexiones por innovation), inspector de grafos generalizado,
   re-innovación determinista al importar. El hito más caro; red de
   seguridad: topología MLP fija como fallback documentado.
4. **F5.3 — Escala**: SoA/ECS del `WorldSim` (migración con pin de hash —
   el arnés de regresión más estricto posible), LOD de difusión de
   feromonas, GPU instancing en el presenter. Exit: N colonias a 60 fps.
5. **Deuda menor de F5.1** (no bloquea): drag & drop de `.antgenome`,
   chip de estado por runway, fuente propia y sprites
   (hormiga/carga/huevo), serie de descargas en la gráfica.
6. **Fase 6 — 2D → 3D**: explícitamente fuera de Fase 5; el adaptador
   cambia, los contratos no.

## Riesgos abiertos

| Riesgo | Estado |
|---|---|
| NEAT estanca la evolución | abierto — empieza con fallback de topología fija |
| Feromonas costosas a escala | abierto — RLE resuelve render; la difusión es F5.3 |
| Bug de Unity Search (Library fría en batch) | mitigado — fallback `PumpOneTick` en las sondas; pre-import de CI documentado como alternativa |
| Balance de especies | se calibrará con tests de balance contra el benchmark de pools, no a mano |
| Pool Atta desde cero | abierto — el plan es transferencia (genomas Lasius en cuerpo Atta); currículo propio solo si falla (F5.2a-bis) |
