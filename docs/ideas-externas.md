# Ideas de simuladores externos (revisión 2026-09-17)

Cuatro proyectos leídos a fondo para contrastar decisiones con lo que ya
tenemos. El documento separa **qué hace cada uno**, **qué gap nuestro tapa** y
**qué coste tiene**; nada de aquí es un compromiso de fase, es una lista de
candidatos con dueño y precio.

Referencias: [anthill](https://github.com/151henry151/anthill) ·
[flygym](https://github.com/NeLy-EPFL/flygym) ·
[ant-colony-rl](https://github.com/jeffasante/ant-colony-rl) ·
[iraca](https://github.com/epiverse-trace/iraca)

## 1. anthill — el más cercano a nosotros (Godot 4.2+, GPL-3.0)

Simulación de colonia de *Lasius niger* a escala de grano: mundo voxel de
arena, dinámica de colonia en tiempo discreto, reclutamiento por rastro,
**campo de hidrocarburos cuticulares (CHC)**, señal de alarma tipo glándula de
Dufour, **excavación del nido en sustrato granular**, desarrollo de cría,
poliestimia, y un HUD científico con **export CSV de validación**.

Su valor no es el motor (Godot vs nuestro Core determinista), es el **modelo
químico y la honestidad metodológica**:

- **Footprint de CHC como señal NEGATIVA** (`footprint_field.gd`): depósito
  pasivo al caminar, decaimiento lento, peso **repelente** — el sustrato
  visitado se vuelve menos atractivo. Nosotros tenemos tres capas
  (home/food/alarm) y **todas son positivas o de peligro**: nada desincentiva
  volver a peinar la misma zona, así que la colonia sobreexplota el camino
  bueno y la exploración depende del ruido del cerebro.
- **Tropotaxis en forma de ratio** (`TROPOTAXIS_*`): decisión de bifurcación =
  rastro en el numerador, **huella + aglomeración en el denominador**. Es una
  regla de elección explícita y local, no un sesgo difuso.
- **Beacon de reclutamiento**: el depósito de vuelta se multiplica por un
  decaimiento exponencial con la distancia horizontal al parche **recordado**
  (no por tiempo de viaje) → el rastro se ilumina cerca de la fuente.
- **Crowding en el pickup**: la congestión junto al alimento reduce la
  recogida (nosotros no tenemos ninguna noción de aglomeración).
- **Fundación claustral + nanitics**: la reina funda en clausura y las primeras
  obreras son más pequeñas. Nosotros escalonamos el vigor de las fundadoras,
  pero no el TAMAÑO por cohorte.
- **Trazabilidad biología↔código**: mantienen una tabla explícita de qué del
  texto de referencia está implementado, parcial o ausente. Es exactamente el
  antídoto contra "el doc promete más que el código".
- **Nido excavado en voxels**: galerías procedurales, scoring de excavación.
  Es una obra aparte (física granular) — **no entra** en Fase 5.

**Coste de traerlo**: los tres primeros puntos viven en el sistema de feromonas
que ya existe (`PheromoneLayer`, canal E multi-capa desde F5.1). Requieren pin
de hash regenerado (la huella alimenta los sensores → cambia el mundo).

> **Estado (F5.3)**: el primero de los tres está implementado — cuarta capa
> `Footprint` con depósito pasivo, vida larga (τ½ 240 s), reflejo de tropotaxis
> en RATIO (no un canal de sensor: el contrato del cerebro no cambia) y control
> A/B por CLI (`--footprint 0`). Los otros dos (tropotaxis sobre el rastro con
> aglomeración en el denominador, y marcado más fuerte junto a la fuente
> recordada) siguen pendientes y se probarían con el mismo arnés.

## 2. flygym — física, sentidos con sustrato anatómico y jerarquía

NeuroMechFly v2: gemelo digital de *Drosophila* sobre MuJoCo/Warp. Lo
aprovechable no es el insecto, es cómo tratan **percepción, control y
rendimiento**:

- **Visión y olfato muestreados donde están los órganos**: la retina se simula
  ommatidio a ommatidio y el olor se evalúa en antenas y palpos. Nosotros
  tenemos 19 sensores con sondas frontales genéricas; mover parte de las sondas
  a posiciones anatómicas (delantera/laterales/trasera) es realismo barato con
  coste de hash.
- **Control jerárquico** con interfaz explícita cerebro ↔ cordón ventral
  (representaciones descendentes y ascendentes). Nuestro cerebro es plano
  (19→8→6). Dos módulos acoplados son la extensión natural de NEAT, pero
  reescriben el contrato del cerebro: **experimento de fase, no rodaja**.
- **Rendimiento con múltiplos medidos y públicos** (~10× CPU, ~300× GPU tras
  su reescritura 2.x). Nosotros medimos tps en sondas, pero no publicamos una
  tabla reproducible → ver §5.
- **Wrapper de entorno (gymnasium en 1.x)**: el simulador como entorno
  `step(action) → obs, reward` para entrenar con RL externo (PyTorch/JAX). Es
  la capacidad de mayor valor estratégico de esta lista: dejaría de ser "nuestro
  GA" y pasaría a ser un banco de pruebas. Nuestro canal B (comandos con tick)
  ya es la mitad del contrato.
- **Migración de API documentada** (1.x → 2.x incompatible, con guía): nosotros
  ya versionamos contratos en `especificaciones.md`; sirve como recordatorio de
  publicar la nota de migración cuando cambiemos formato (.antsave v3→v4 fue un
  caso).

## 3. ant-colony-rl — comparar algoritmos y mostrar el aprendizaje

JS + Canvas: hormigas con **Q-learning** tabular, epsilon-greedy, rastros de
feromona, fuentes de comida que se agotan, episodios con reset y **carga de
Q-tables preentrenadas** (el mismo patrón que nuestro `.antgenome`: confirma
que la decisión de artefacto portable era la buena).

Lo que nos falta de su lista:

- **Baseline no evolutivo y comparación de algoritmos**. Hoy no tenemos una
  tabla "aleatorio vs scripted vs evolucionado" en la misma arena. Tenemos las
  piezas (`ArenaEvaluator` y una política a mano de las sondas de Fase 3ter):
  falta el experimento formal. Es lo primero que pediría un revisor externo.
- **Curva de aprendizaje visible**. El proyecto muestra métricas de recompensa
  y tasa de exploración por episodio. Nuestro HUD enseña reserva y relevo, pero
  **no** la curva de fitness por generación — que es justo lo que hace atractivo
  el modo evolución que pediste (fase A rápida + fase B en vivo).
- **Métricas de exploración** (cobertura del mundo, tasa de acciones nuevas):
  baratas de calcular desde los eventos y hoy inexistentes en el canal C.
- **Comparación de intervenciones** en su roadmap (obstáculos/depredadores).

## 4. iraca — calibración, políticas y ciclo de vida del software

ABM de dengue mosquito–humano en Colombia (R + C++ vía Rcpp). Aunque el dominio
es epidemiología, aporta tres cosas de método:

- **Calibración contra datos observados por RMSE** (`setup()` recibe datos
  demográficos, climáticos y casos observados y ajusta parámetros). Nosotros
  calibramos con sondas INTERNAS (coherencia del mundo), sin anclaje externo a
  datos publicados de *Lasius niger*. Un script que ajuste 2–3 constantes a una
  tabla de referencia y reporte el error es lo que separa "juguete bonito" de
  "modelo con supuestos defendibles".
- **Marco de intervenciones comparables** (mosquiteras, insecticida, limpieza
  de recipientes): escenarios A/B con la misma semilla y una tabla de resultado.
  Nosotros tenemos drops y siembra de pools; falta el envoltorio de escenario
  ("misma semilla, dos políticas, mismas métricas").
- **Ciclo de vida y ética del software** (etiqueta RECON "concept" y parada
  documentada con razones, incluida la de no publicar una herramienta sensible
  mal calibrada). Para un simulador de ecosistemas el equivalente es una sección
  de **alcance y límites**: qué NO se puede inferir de nuestras partidas.

## 5. Propuestas ordenadas por valor/coste

| # | Propuesta | Origen | Coste | Por qué |
|---|---|---|---|---|
| 1 | ~~**Huella CHC como 4ª capa repelente** + tropotaxis en ratio~~ — **HECHA (F5.3)** | anthill | medio | Señal negativa que hoy no existía: reparte el forrajeo por el espacio en vez de reforzar un solo camino. Medido: el 5 % de celdas más pisadas pasa de concentrar el 49 % del tráfico al 23 %, y la colonia no pierde economía (stock 41 → 46). Detalle en `especificaciones.md` §2bis |
| 2 | ~~**Benchmark de políticas**: aleatoria vs scripted vs evolucionada, misma arena y semillas, tabla~~ — **HECHO (F5.3bis)** | ant-colony-rl | bajo | Ya se puede afirmar: la aleatoria está MUERTA por construcción (0 pickups en 20 partidas) y scripted ≈ evolucionada (empate dentro de 2 SE). Tabla y límites en [`politicas-benchmark.md`](politicas-benchmark.md) |
| 3 | **Export científico CSV + tabla biología↔código** | anthill | bajo | El stream JSONL sirve al juego; falta la salida que un investigador quiere leer, y la tabla evita que el doc prometa de más |
| 4 | **Crowding en pickup + beacon de retorno** | anthill | bajo-medio | Dos reglas locales con efecto visible (no se apilan; el rastro se ilumina junto a la fuente) |
| 5 | ~~**Curva de aprendizaje y cobertura en el HUD**~~ — **HECHO (F5.3ter)** | ant-colony-rl | medio | Hace VISIBLE el aprendizaje: curva de fitness por generación (canal C) + cobertura del mundo leída de la huella CHC, pintadas en la tarjeta de cada colonia. Ver `fase4-hud-contrato.md` §2 |
| 6 | **Calibración por RMSE contra una tabla publicada** | iraca | medio | Anclaje externo de 2–3 constantes, con error reportado |
| 7 | **Benchmark de rendimiento reproducible** (ticks/s por escenario, antes/después de F5.3) | flygym | bajo | Convierte "va más rápido" en un número que CI puede vigilar |
| 8 | **Sondas sensoriales en posiciones anatómicas** | flygym | medio (hash) | Realismo de percepción sin tocar el contrato del cerebro |
| 9 | **Escenarios de intervención comparables** (misma semilla, dos políticas) | iraca | bajo-medio | Convierte las pruebas de balance en experimentos |
| 10 | **Cerebro de dos niveles** (decisión + patrón de marcha) | flygym | alto | La extensión ambiciosa de NEAT; reescribe el contrato del cerebro |
| 11 | **Wrapper tipo gymnasium** (`step`/`reset`) | flygym | alto | Abre el simulador a RL externo; el canal B ya es medio contrato |
| 12 | **Fundación claustral, nanitics, excavación de nido** | anthill | alto (voxels) | Solo el tamaño por cohorte es barato; la excavación es un proyecto aparte |

## 6. Qué NO copiar

- **Voxels de arena con física granular**: nuestro rendimiento y determinismo
  viven en un mundo 2D de campos; la excavación es otra fase (y otro motor).
- **Dependencia de un motor externo para el modelo**: anthill simula DENTRO de
  Godot y flygym dentro de MuJoCo. Nuestra separación Core determinista ↔
  presentación es lo que hace posibles los seis pines de hash; no se toca.
- **Q-learning tabular como cerebro principal**: el genoma estructural (NEAT) y
  la arena de pre-entrenamiento ya superan ese techo; el Q-learning interesa
  como **baseline del punto 2**, no como motor.
