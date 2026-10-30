# F6 — Terreno con laberinto, ritmo 120× y evolución por nacimientos

*Cerrado: 2026-10-22 · editor 6000.6.0f1 · suite headless 681/681 (Windows) ·
los 6 pines de hash de CI NO se mueven*

Tres cambios que van juntos porque se SOSTIENEN entre sí: un tablero con
obstáculos necesita más tiempo de simulación para que las colonias lo aprendan,
y ese tiempo solo se puede jugar si el reproductor va rápido — y, con una
partida de 2 h, la evolución no puede depender de que alguien pulse un botón.

| Pieza | Antes | Ahora |
|---|---|---|
| Terreno | plano vacío | laberinto con bucles generado de la semilla, con colisión y sensores |
| Velocidad base | 3× (lanzador) / 10× (escena) | **120×** = 100 % del control, rango 10×–160× |
| Horizonte | 36 000 ticks (escena) | **216 000 ticks = 2 h de simulación** (~30 min de reloj a 120×) |
| Evolución | botón «↻ Sig. Gen» que relanzaba la escena | nacimientos desde la élite + muertes que la actualizan, sin botón |
| Al terminar | nada | `artifacts/evolved-<semilla>.antgenome` |

## 1. El laberinto (`AntSim.Core/World/MazeLayout.cs`)

Rejilla de celdas de 8 u — **la misma de las feromonas**, así que lo que colisiona
y lo que se pinta salen del mismo bitmap.

- Bloque de 8 celdas (64 u): **1 celda de pared** (8 u) y 7 de interior
  (56 u de corredor). Salas en rejilla `ceil(grid/8)`: 12×12 en grid 96, 32×32 en 256.
- Árbol de expansión por DFS con un `DeterministicRandom` **propio**
  (`seed ^ 0x4D415A4535313241`): la generación no consume el RNG del mundo, que es
  la condición para que un mundo sin laberinto conserve su secuencia de ítems.
- **Puertas de 3 celdas (24 u)** en el centro de cada lado del árbol. Con 2
  celdas el hueco libre para el CENTRO de la hormiga quedaba en 8 u y una política
  sin memoria no lo encontraba (medido: 0 recogidas en 7 200 ticks).
- **Braid del 40 %**: se abren puertas en lados que el árbol no usó. Un árbol puro
  deja callejones sin salida; con bucles siempre hay ruta alternativa y el tablero
  se sigue leyendo como laberinto (paredes con puertas, no campo abierto).
- **Plaza de 96 u** despejada alrededor de cada nido: con `world·(i+1)/(colonies+1)`
  el nido cae justo sobre una línea de pared y las fundadoras nacerían dentro.
- `IsWall`, `Blocked(x, y, r)` (círculo contra celdas), campo de distancia a la
  pared con lectura bilineal (`ProximityAt`) para las antenas, `Rects` (paredes
  fundidas en rectángulos maximales: ~300 quads en vez de 9 000) y `LayoutHash`.

`FromCells` reconstruye el bitmap de un checkpoint; el hash FNV-1a del bitmap entra
en `WorldSim.HashLine()` **solo cuando hay laberinto** y en el `.antsave`.

## 2. El mundo

- `WorldSim(…, maze: true)` o `mazeLayout:` explícito. Sin ninguno, `Maze == null`
  y el mundo es el de siempre.
- **Movimiento**: intento completo; si choca, solo X y luego solo Y — deslizar por
  la pared en vez de bloquear, que es lo que permite recorrer un corredor pegado a
  ella sin calcular el ángulo exacto. Sin laberinto se conserva el `Clamp`.
- **Sensores** (`AntSenses.Build(..., maze:)`): cada antena mide la proximidad en
  SU punto, así los tres canales distinguen de qué lado está el obstáculo. Con
  rivales, el frontal sigue midiendo la presa.
- **Comida**: `SpawnItem` descarta puntos dentro de una pared o a menos de 6 u de
  ella; `DropFood` reubica al hueco abierto más cercano. La distancia mínima al
  nido baja a 80 u y el objetivo de ítems sube ×4,5 (medido; ver §5).
- **Checkpoint v6**: bloque `hasMaze` + bitmap `N²` bytes tras `gridCells`. v2–v5
  se siguen leyendo, sin laberinto.
- **Stream**: la cabecera lleva `"maze":[[x,y,w,h],…]` en celdas solo si hay
  terreno. Sin laberinto, el stream es byte a byte el de antes — los 6 pines de CI
  del stream canónico, el replay con drops, Atta, invasión y NEAT no cambian.

## 3. Ritmo: 120× y 2 h

- `SpeedControlModel`: base **120×**, rango **10–160×**, presets 10/30/60/90/120/160.
  El techo sale de la medida del CLI (~6 000 ticks/s por núcleo en 96², 0,165 ms/tick):
  160× pide 4 800 ticks/s y deja margen para el laberinto.
- `LaunchSettings` (y el lanzador `play-game.ps1/.sh/.bat`): `ticks=216000`,
  `speed=120`, `maze=1`.
- **Stream tick a tick** (`GameScenario.RunTo`): 216 000 ticks en un `StringBuilder`
  son gigabytes; el CLI escribe y vacía cada 32 ticks (~1 s de simulación).
- **Contrapresión** en el presenter: `MaxPending = 600` ticks (20 s de simulación de
  colchón). Al llenarse, el hilo del stream espera y el CLI baja al ritmo del
  reproductor. Parar Play mata el hijo (`StreamSource.Stop`) en vez de dejarlo
  bloqueado en la tubería.

## 4. Evolución por nacimientos

No hay «nueva generación» que reinicie la partida: el `GenomePool` de cada colonia
se alimenta de las MUERTES (`CompleteTrial` con el fitness de por vida, que inserta
ordenado en la élite de 64) y los NACIMIENTOS hacen torneo binario sobre esa élite,
cruce y mutación. La colonia evoluciona mientras juega, y al terminar el horizonte
`ExportEliteFile` deja la élite en `artifacts/evolved-<semilla>.antgenome`
(`--export` en el CLI, `ExportElite` en el presenter). El HUD perdió el botón y el
contador de generación de sesión; la generación que se muestra es la de nacimientos
(`births/64`), que ya existía.

## 5. Medido (no prometido)

Semilla 42, grid 96, 2 colonias, pool `pretrain-warm-v2`, CLI `--mode game`:

| 20 000 ticks | recogidas | descargas | adultas al final |
|---|---|---|---|
| mundo clásico | 58 | 44 | 40 / 27 |
| laberinto, objetivo ×1 (densidad del clásico) | 11 | 3 | colonias apagadas |
| laberinto, mínimo 120 u, objetivo ×3 | 31 | 8 | — |
| **laberinto, mínimo 80 u, objetivo ×4,5** | **35** | **12** | **37 / 26** |

La demografía empata con el mundo abierto a 20 000 ticks; lo que queda por debajo
es NAVEGACIÓN (la vuelta con carga se pierde en las paredes), no comida
disponible — llenar el mapa de ítems no lo arregla, lo arregla la evolución por
nacimientos. **El mundo clásico también se extinguía** hacia los 30 000–36 000
ticks con este pool (es la economía de la colonia, anterior a F6): el laberinto no
adelantaba el final, y el horizonte de 2 h se eligió por ritmo de partida, no
porque la colonia estuviera garantizada hasta el final.

### 5bis. La ronda ya llega al horizonte (2026-10-30)

Aquella frase —«no está garantizada hasta el final»— dejó de ser cierta y merece
el número: la ronda de **dos colonias** con laberinto y reclutamiento se apagaba
hacia el tick **33 000–46 000 de 216 000**, con **974 ep de comida sin recoger en
151 ítems** en el tablero. No era escasez: era alcance.

- Los montones caían a **100–220 u** del nido y las obreras patrullan ~100 u
  (mediana 93 u; sólo un 11 % pasa de 200). La colonia 0 hacía **8 descargas y
  41 ep en 40 000 ticks** (0,001 ep/s): un viaje cada ~38 min de simulación.
- La cuota de montones era **global**: con dos colonias cada nido se quedaba con
  **4 de los 8**, la mitad de la oferta de una colonia sola.

La siembra del mundo con reclutamiento pasó a **banda 40–120 u** y a **montones
por colonia** (repartidos en rueda, también en la reposición). El mundo clásico
no cambia ni un byte y los 6 pines de CI no se mueven.

**Medido** (216 000 ticks, grid 96, laberinto + reclutamiento, semillas 42, 7, 11
y 13):

| partida | antes | después |
|---|---|---|
| 2 × Lasius, `pretrain-maze-los3` | 69 600 / 92 400 (semilla 42) | **4/4 semillas con las dos colonias vivas** |
| pareja Lasius+Eciton, `pretrain-maze-los3` | 47 040 / 39 120 (semilla 42) | **4/4 semillas con las dos colonias vivas** |
| pareja con `warm-v2` (pool del fixture) | 33 720 / 83 160 (semilla 42) | **2/2 medidas con las dos vivas** (semilla 7: c0 toca 0 en 132 000 y se repone) |

Con el pool de fábrica las **40 adultas** se sostienen todo el horizonte y el
stock oscila **55–150** en vez de caer a cero: el saqueo ya se juega sobre una
colonia que está viva. Los pines que lo impiden volver están en
`RecruitmentTests` (`Cumulos_SeRepartenPorColonia_NoComoCuotaUnica` y
`Cumulos_CaenDentroDelRadioQueLaColoniaPatrulla`).

Al mirar la ronda con el HUD apareció la otra mitad del problema: la puesta
apilaba **479 y 943 pupas** que nunca eclosionaban —con las 40 adultas llenas y
**3 322 huevos para 789 eclosiones**— porque el término de déficit no descontaba
la cría en vuelo. Con el déficit contando el embudo y la puesta acotada por las
nodrizas (`adultas · NurseShare`), el **pico de pupas baja a 6**, los huevos
puestos a **845** y las eclosiones se quedan en **780**, con las 40 adultas
igual. El mundo clásico no tiene embudo —es su economía cerrada— y sus 6 pines no
se mueven. Se probó además reponer a la mortalidad real en vez de a `DeathRate`:
medido por separado no rescataba ninguna semilla y costaba descargas, así que está
fuera. Tabla de las tres semillas en [`estado-proyecto.md`](estado-proyecto.md); el
pin es `RoundSurvivalTests.Puesta_ConTropaLlena_NoApilaPupasSinVacante`.

Partida completa (216 000 ticks, `--maze --export`): termina en 15,6 s de CPU,
escribe `artifacts/evolved-42.antgenome` (53 KB, 61 genomas en la élite de la
colonia 0) y la línea `end` declara `"exported":true`.

## 6. La vista

- `GameAssetBuilder.EnsureWallMesh` genera un quad unitario en XZ (idempotente, con
  `CopySerialized` para no cambiar el GUID) y `GameMaterialModel` añade `WallMat`
  (piedra oscura, unlit) — 77 materiales en `Assets/Materials`.
- `SimPresenterBehaviour.WallMesh` / `WallMaterial`: las paredes se pintan ANTES de
  actores e ítems, con `Graphics.DrawMeshInstanced` sobre las matrices que salen de
  los rectángulos de la cabecera (subidas 0,35 u, sobre el quad de feromonas y bajo
  los actores).
- La escena `Game.unity` se regenera con `SceneBootstrapper.CreateGameScene`
  (216 000 ticks, 120×, laberinto, sin botón «Sig. Gen», presets 10×–160×) y el
  contrato de escenas fija ahora también el campo `Maze`. El multi-visor y el
  medidor siguen en mundo clásico a 10× (`PerfSpeed`, explícito desde F6 para que
  regenerar no cambie la medida).

## 7. Verificación

- `dotnet test`: **681/681** (14 nuevas de laberinto + checkpoint; las de velocidad
  y lanzador actualizadas al rango nuevo).
- Los 6 pines de CI (stream canónico, replay 3000/6000, Atta, invasión, NEAT) no se
  tocan: el camino sin `--maze` no cambia ni un byte.
- `scripts/check-doc-figures.sh`: 6 pines · 681 tests.
- El editor compila la capa de vista (`recompile_status: completed, failed:false`).
- El contrato de escenas y el de materiales/mallas pasan con los assets nuevos
  (`WallMat.mat`, `WallMesh.asset`) versionados.
- **Las paredes se miden en los píxeles del frame vivo** (paso del Play pass, no
  un check de estado): `ProbeWallPixels` compara el render con el mapa de la
  cabecera celda a celda (8 836/8 836 de acuerdo, 1 202 celdas de muro, 0 sin
  pintar) y `scripts/playpass-walls.py` exige `draws == rects` (313/313), el
  color del material en 32 paredes y en ningún control, cajas 94–100 % llenas y
  escala de 9,61 px/celda. Evidencia reanalizable en
  `artifacts/playpass-wall-pixels/` (`--analyze-walls` la repite sin editor).
  Las paredes del borde quedan tapadas por el marco del tablero (`RimMat`), así
  que la medida celda a celda se hace sobre el interior y las muestras descartan
  los rectángulos que tocan el anillo exterior.
