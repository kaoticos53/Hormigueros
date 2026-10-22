# F7 — Pre-entrenamiento con obstáculos, currículo adaptativo y cerebro que crece

Lo que faltaba para que la partida arrancase ENTRENADA en el laberinto que F6
monta. El mundo ya era el del juego; la política, no: el pool de referencia
(`pretrain-warm-v2`) se entrenó en campo abierto y gasta la partida aprendiendo a
rodear paredes. F7 entrena en headless con el terreno del juego, sube la
dificultad SOLO cuando la colonia demuestra que sabe volver al nido, ensancha el
cerebro cuando se estanca en el laberinto completo y deja un pool que forrajea en
LAS DOS superficies: **58 descargas con `--maze` y 421 sin laberinto** en la
partida canónica (el de referencia hace 37 y 242).

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
señal que se busca. Un warm-start más ancho que la escalera manda (continuar un
pool de 24 ocultas no puede empezar en 8: la escalera no tiene bajada).

## 5. Las dos competencias: dos regímenes clásicos, medidos

Entrenar solo en el laberinto OLVIDA el campo abierto (medido en la partida
canónica sin `--maze`: 242 → 40 descargas). La pregunta era qué evaluación
clásica hay que meter en el bucle, y la respuesta medida es que hay dos papeles
distintos:

- **Mantenimiento** (1 de cada N generaciones, banda y horizonte del currículo
  clásico: 200–260 u, 3 600 ticks). No deja que la política se vaya del régimen
  de forrajeo clásico mientras manda el laberinto. Es el que conserva el ciclo
  completo: sin él, 40 descargas; con él, 172.
- **Cola** (60 generaciones al final, en el régimen de la PARTIDA sin laberinto:
  comida uniforme en todo el mapa a ≥ 200 u del nido y horizonte de 9 000 ticks,
  tres generaciones de hormigas dentro de una evaluación). Es el que recupera la
  métrica del juego: 172 → 421.

Lo que se descartó, con su medida:

- **Banda estrecha y horizonte corto como objetivo de recuperación**: afinarla
  SUBÍA el fitness de la arena (1,9 → 3,5 descargas) mientras la partida caía de
  172 a 46. A 9 000 ticks el orden de la arena coincide con el de la partida.
- **Fitness mezclado (maze + clásico al 50 %)**: 47 y 10 descargas — peor en las
  dos superficies; cada generación seleccionaba con una vara distinta.
- **Mezclar pools** (16 especialistas de cada terreno en un archivo): 41 y 119;
  la colonia diluye a los dos.
- **Solo cola, sin cuenca**: si la política ya salió del régimen clásico, la cola
  no vuelve (150–171 descargas). La cola AMPLIFICA una competencia que existe; no
  la crea.
- **Generaciones clásicas con el horizonte del currículo de obstáculos**: a
  dificultad baja el presupuesto caía a 1 800 ticks —menos que un viaje de ida y
  vuelta a 200 u (~4 400)— y 600 generaciones seguidas vieron `unloads=0`: eran
  selección de ruido. Por eso el mantenimiento corre con el horizonte de su
  etapa y la cola con el suyo.

## 6. La receta (`scripts/pretrain-maze.sh`)

Dos fases encadenadas, cada una un CLI determinista:

1. **Cuenca** — seed 7, pop 32, 600 generaciones, `--obstacles --hidden 8 --grow
   --classic-every 5 --classic-tail 0`, warm-start `pretrain-warm-v2`.
   Sale el pool base (57 descargas en el laberinto, 172 sin él).
2. **Laberinto + cola** — 600 generaciones con `--classic-every 0` y
   `--classic-tail 60`, warm-start el pool de la fase 1. El pool final es la
   última evaluación (clásica).

Total **1 260 generaciones en 679–698 s** de reloj en esta máquina, con log por
generación en `artifacts/pretrain-maze.log` (las clásicas llevan `classic=1`) y
dificultad final 1,0 (10 escalones, 0 descensos). Cerebro **19→24→6** con dos
crecimientos. Reproducible: la misma semilla y receta dan el mismo SHA-256.

## 7. Medido (no prometido)

Partida canónica (`--mode game`, grid 96, 216 000 ticks, semillas 42/7/99/1234,
descargas al nido agregadas). Evidencia regenerable:
`python scripts/pretrain-maze-transfer.py` y `… --no-maze`
(`artifacts/pretrain-maze-transfer*.txt`).

| pool | con laberinto (`--maze`) | sin laberinto |
|---|---|---|
| frío (sin pool) | 4 | 0 |
| `pretrain-warm-v2` | 37 | 242 |
| **`pretrain-maze`** | **58** (+57 %) | **421** (+74 %) |

- 4/4 semillas con descargas en las dos superficies; en el laberinto, primera
  descarga en el tick ~1 100 de media.
- Sin laberinto las semillas dan 143/99/74/105 (media 105) frente a 64/66/49/63
  (media 60) del pool de referencia: la mejora no depende de una semilla suerte.
- La fase 1 sola da 57/172: es la cuenca, no el producto.

## 8. El juego arranca entrenado

- `scripts/play-game.sh`: `POOL=pretrain-maze` por defecto (`--pool` sigue
  eligiendo otro, y los pools viejos se importan con `I`).
- `LaunchSettings.Pool = "pretrain-maze"` y la escena `Game.unity`
  (`SeedPoolPath`) sembrada con el pool nuevo; `GeneratedSceneCatalog` lo declara
  en el contrato de escenas.
- El mundo adopta el ancho de cerebro del archivo (19→24→6): no hay que tocar
  ninguna constante al cambiar de pool.

## 9. Verificación

- `dotnet test`: suite completa en verde (28 de `ObstacleTrainingTests`, incluidas
  la cola clásica y el warm-start más ancho que la escalera).
- Los 6 pines de CI no cambian ni un byte (stream canónico, replay 3000/6000,
  Atta, invasión, NEAT y la constante del fixture en la suite).
- `scripts/check-doc-figures.sh`: 6 pines y el número de tests de la suite.
