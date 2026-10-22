# F7 — Pre-entrenamiento con obstáculos, currículo adaptativo y cerebro que crece

Lo que faltaba para que la partida arrancase ENTRENADA en el laberinto que F6
monta. El mundo ya era el del juego; la política, no: el pool de referencia
(`pretrain-warm-v2`) se entrenó en campo abierto y gasta la partida aprendiendo a
rodear paredes. F7 entrena en headless con el terreno del juego, sube la
dificultad SOLO cuando la colonia demuestra que sabe volver al nido, ensancha el
cerebro cuando se estanca en el laberinto completo y deja el pool nuevo como
default del juego.

Sin `--obstacles` (y sin `--maze` en la partida) nada de esto se ejecuta: los 6
pines de CI y el camino clásico siguen byte a byte.

## 1. Terreno graduable (`AntSim.Core/World/MazeLayout.cs`)

`MazeLayout.Pruned(seed, cells, colonyCount, difficulty)` construye el mapa de
`Braided` con la MISMA semilla y le retira TRAMOS COMPLETOS de pared interior
—el segmento entre dos cruces, no celdas sueltas: una pared con un píxel de
menos es una trampa, y un tramo ausente es un obstáculo legible—.

- Una tirada del RNG por tramo, en orden fijo: `paredes(d1) ⊆ paredes(d2)` para
  `d1 < d2`. Subir la dificultad solo AÑADE obstáculos; nunca cambia los que la
  política ya conocía.
- `d = 0` ⇒ campo abierto (solo el anillo exterior); `d = 1` ⇒ el mapa del juego
  byte a byte. Como el mapa completo es conexo y aquí solo se retira, todo nivel
  intermedio sigue siendo conexo por construcción.
- Anillo exterior, plazas de los nidos y cruces huérfanos intactos: un cruce
  sobrevive solo si le queda algún tramo contiguo, así que a `d = 0` no queda un
  enrejado de postes.

## 2. Arena con obstáculos (`Training/ArenaEvaluator.cs`)

El evaluador acepta un `MazeLayout?`. Con terreno:

- corre el MISMO `WorldSim` (mismo `AntSenses`: los canales 11/12/13 ya leen el
  muro con las tres antenas) — no hay física paralela que pueda divergir del
  juego;
- la comida nace con las reglas de la partida real: `ItemFits` (nada dentro de
  muro), ≥ `MazeLayout.ItemSpawnMinDistance` (80 u) del nido y densidad
  ×`ItemDensityFactor` (4,5);
- el tope del densado de exploración pasa a ser el radio útil del mundo (la
  banda de distancia de la etapa no aplica: el rodeo lo impone el terreno).

Fitness y densados no cambian: es la misma señal que el juego (recogida,
descarga, homing, carry-leg). Sin `maze` el resultado es idéntico al de siempre.

## 3. Currículo por competencia (`Training/ObstacleCurriculum.cs`)

La dificultad no sigue un calendario sino DESCARGAS reales de la mejor
evaluación de la generación — el ciclo completo del juego, que no se puede
farmear paseando (el fitness sí: lleva densados).

- Ventana de 3 generaciones; sube un peldaño (0,1) si las tres cierran
  ≥ `CompetenceUnloads` (3) descargas; baja si las tres se quedan a 0; entre
  ambos extremos no se mueve (histéresis: ni una generación mala hunde el
  terreno ni una buena lo devuelve).
- Estado en décimas ENTERAS: sumar 0,1 en coma flotante acumula error y la
  misma receta dejaría de dar el mismo archivo.
- El horizonte crece con el terreno: 1 800 ticks (fácil) → 2 700 → 3 600 (el
  laberinto completo). Las generaciones fáciles son baratas.

## 4. Escalera de capacidad (`Training/CapacityLadder.cs`)

Con el terreno en su tope y el mejor fitness plano (ventana de 30 generaciones,
mejora < 2 %), la capa oculta se ensancha un peldaño: 8 → 16 → 24. `MlpGenome.
Expand` copia los pesos existentes y deja las neuronas nuevas a cero, así que la
red expandida reproduce la salida anterior (la escalera no tira lo aprendido).
Fuera del terreno máximo la ventana no corre: crecer en campo abierto no es la
señal que se busca.

## 5. Mantener el forrajeo clásico

Entrenar solo en el laberinto OLVIDA el campo abierto (medido en la partida
canónica sin `--maze`: 242 → 40 descargas). `--classic-every N` (default 5 con
`--obstacles`) evalúa 1 de cada N generaciones sin terreno y esas generaciones no
alimentan ni el currículo ni la escalera. Se probaron dos alternativas y perdieron:
sin la pata clásica (40 descargas) y con el fitness mezclado al 50 % en cada
genoma (10 descargas y peor laberinto — cada generación seleccionaba con una vara
distinta).

## 6. La receta (`scripts/pretrain-maze.sh`)

Semilla 7, pop 32, 600 generaciones, banda 200–260, warm-start
`pretrain-warm-v2`, `--obstacles --hidden 8 --grow --classic-every 5`, export a
`artifacts/pretrain-maze.antgenome`.

- **254–319 s** de reloj en esta máquina (600 generaciones, log por generación en
  `artifacts/pretrain-maze.log`).
- Dificultad final **1,0** (10 escalones, 0 descensos); cerebro **19→24→6** con
  dos crecimientos (16 y 24).
- **Reproducible**: dos tiradas independientes con la misma semilla y receta
  dieron el mismo SHA-256, `3d9380f1e28d6e738b13013c02a9d10d687c1bd3a951a8cee893eeb1452f9e5c`
  (81 321 bytes).

## 7. Medido (no prometido)

Partida canónica (`--mode game`, grid 96, 216 000 ticks, semillas 42/7/99/1234,
descargas al nido agregadas). Evidencia regenerable:
`python scripts/pretrain-maze-transfer.py` y `… --no-maze`
(`artifacts/pretrain-maze-transfer*.txt`).

| pool | con laberinto (`--maze`) | sin laberinto |
|---|---|---|
| frío (sin pool) | 4 | 0 |
| `pretrain-warm-v2` | 37 | **242** |
| **`pretrain-maze`** | **57** | 172 |

- **Laberinto: +54 %** sobre el pool de referencia (57 vs 37) y 4/4 semillas con
  descargas; primera descarga en el tick ~1 228 de media (vs 1 176 del de
  referencia: no compra descargas a costa de arrancar antes).
- **Campo abierto: 172 vs 242** (−29 %) — el precio de especializar la política.
  Se conserva el ciclo completo en 4/4 semillas y se barre al pool frío (0), pero
  NO es paridad: el juego monta laberinto por defecto, y ese es el terreno que la
  partida usa. Con `CLASSIC=3` el laberinto subía a 60 y el campo abierto caía a
  73; con `CLASSIC=0`, a 40 — la alternancia 1/5 es el mejor equilibrio medido.

## 8. El juego arranca entrenado

- `scripts/play-game.sh`: `POOL=pretrain-maze` por defecto (`--pool` sigue
  eligiendo otro, y los pools viejos se importan con `I`).
- `LaunchSettings.Pool = "pretrain-maze"` y la escena `Game.unity`
  (`SeedPoolPath`) sembrada con el pool nuevo; `GeneratedSceneCatalog` lo declara
  en el contrato de escenas.
- El mundo adopta el ancho de cerebro del archivo (19→24→6): no hay que tocar
  ninguna constante al cambiar de pool.

## 9. Verificación

- `dotnet test`: **711/711** (24 de `ObstacleTrainingTests` + los de vista F7;
  contratos de escena y de lanzador actualizados al pool nuevo).
- Los 6 pines de CI no cambian ni un byte (stream canónico, replay 3000/6000,
  Atta, invasión, NEAT y la constante del fixture en la suite).
- `scripts/check-doc-figures.sh`: 6 pines · 711 tests.
