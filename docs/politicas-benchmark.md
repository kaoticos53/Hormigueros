# Benchmark de políticas (F5.3bis) — aleatoria vs scripted vs evolucionada

La pregunta que este experimento responde es la primera que hace cualquiera que
mire el proyecto desde fuera: **¿esto es mejor que no hacer nada, y que un
puñado de reglas a mano?** Hasta ahora teníamos entrenamiento, currículos y
tablas de pools comparados entre sí, pero ninguna vara EXTERNA. Esta es la vara.

Origen: idea nº 2 de [`ideas-externas.md`](ideas-externas.md) (inspirada en
`jeffasante/ant-colony-rl`), que señalaba la carencia — «no tenemos benchmark de
políticas, y hoy no podemos afirmar que la evolución gana a nada».

## 1. Protocolo (idéntico para las tres políticas)

Toda la medición vive en la misma arena del entrenamiento
([`ArenaEvaluator`](../src/Core/AntSim.Core/Training/ArenaEvaluator.cs)):
colonia completa del mundo (10 fundadoras + cría inicial), comida real de 4 ep
en la banda pedida, sin rastro plantado, sin respawn y presupuesto COMPLETO de
ticks. Las tres filas comparten semilla, banda, arena y presupuesto.

Tres decisiones de método que hacen comparable la tabla:

- **Política congelada** (`WorldSim.ForcePolicy`): todas las hormigas —las 10
  fundadoras y cada una que eclosiona durante la prueba— llevan el MISMO cerebro
  y ninguna realimenta el pool. Así la prueba mide el **cerebro**, no la
  evolución ocurrida mientras se medía. (La fila de referencia sí deja evolucionar
  al pool; se marca como tal porque es otro régimen.)
- **Misma topología** en la política aleatoria que en la evolucionada (19→8→6,
  214 pesos, semilla fija): la comparación aísla los PESOS, no el tamaño de la red.
- **La política scripted son reglas explícitas y deterministas**
  ([`ScriptedBrain`](../src/Core/AntSim.Core/Brain/ScriptedBrain.cs)): vuelve al
  nido por la brújula innata marcando el camino con rastro de comida; persigue el
  ítem visible y lo marca; si no ve nada mantiene el rumbo (dispersión en línea
  recta desde el nido — lo que hace que una fundadora alcance la banda de comida
  a ≥ 200 u antes de agotar su vida); y si la pared está cerca, vuelve hacia el
  centro. Es la política del «experto a mano», del mismo orden que la que se usó
  en Fase 3ter para validar la geometría del relevo.

## 2. Tabla — banda cercana 200–260 u (arena 96 celdas, 9000 ticks, 10 semillas)

| política | qué es | fitness (media ± σ) | pickups | descargas | semillas c/ desc. | mejor |
|---|---|---|---|---|---|---|
| aleatoria | pesos al azar, misma topología | 24.7 ± 0.8 | 0.0 | 0.0 | 0/10 | 26.5 |
| scripted | reglas a mano (brújula + visión + rastro) | 1197.9 ± 110.3 | 21.4 | 17.0 | 10/10 | 1403.5 |
| evolucionada (congelada) | élite de warm-v2 (fitness 428.1), pesos congelados | 1259.7 ± 108.1 | 22.0 | 17.5 | 10/10 | 1460.4 |
| evolucionada + pool (referencia) | la élite con su pool evolucionando en la prueba | 1268.7 ± 98.6 | 22.0 | 17.6 | 10/10 | 1477.6 |

Con `--trials 3` (mejor de 3 pruebas por semilla, el protocolo nativo de la
arena para reducir ruido):

| política | fitness (media ± σ) | pickups | descargas | semillas c/ desc. | mejor |
|---|---|---|---|---|---|
| aleatoria | 24.7 ± 0.8 | 0.0 | 0.0 | 0/10 | 26.5 |
| scripted | 1335.3 ± 73.2 | 22.7 | 18.9 | 10/10 | 1419.7 |
| evolucionada (congelada) | 1306.6 ± 86.0 | 22.2 | 18.0 | 10/10 | 1460.4 |
| evolucionada + pool (referencia) | 1315.6 ± 112.0 | 22.3 | 18.0 | 10/10 | 1477.6 |

## 3. Tabla — banda ancha 200–450 u (arena 176 celdas, 14400 ticks, 10 semillas)

La misma banda que la 4ª etapa del currículo (`--full-world` usa 200–700 u con
arena de 176 celdas), donde el relevo necesita descendencia que complete tramos
largos.

| política | qué es | fitness (media ± σ) | pickups | descargas | semillas c/ desc. | mejor |
|---|---|---|---|---|---|---|
| aleatoria | pesos al azar, misma topología | 42.8 ± 0.9 | 0.0 | 0.0 | 0/10 | 44.2 |
| scripted | reglas a mano (brújula + visión + rastro) | 1850.6 ± 175.2 | 21.6 | 18.8 | 10/10 | 2071.2 |
| evolucionada (congelada) | élite de warm-v2, pesos congelados | 1900.7 ± 146.2 | 22.0 | 18.7 | 10/10 | 2204.3 |
| evolucionada + pool (referencia) | la élite con su pool evolucionando en la prueba | 1870.6 ± 207.6 | 22.1 | 18.3 | 10/10 | 2250.3 |

Con `--trials 3`:

| política | fitness (media ± σ) | pickups | descargas | semillas c/ desc. | mejor |
|---|---|---|---|---|---|
| aleatoria | 43.0 ± 0.9 | 0.0 | 0.0 | 0/10 | 44.2 |
| scripted | 2110.5 ± 141.9 | 23.2 | 20.7 | 10/10 | 2376.5 |
| evolucionada (congelada) | 2059.4 ± 169.8 | 22.6 | 20.6 | 10/10 | 2365.0 |
| evolucionada + pool (referencia) | 1982.2 ± 164.5 | 22.7 | 19.3 | 10/10 | 2250.3 |

## 4. Lo que dicen los números

1. **La búsqueda aleatoria está muerta por construcción.** En 20 partidas de
   arena (10 semillas × 2 bandas) la política aleatoria hizo **0 pickups**:
   el paseo aleatorio no alcanza la banda de comida antes de morir y nunca se
   muestrea un ciclo. Fitness 24.7/42.8 = supervivencia pura. Es el suelo contra
   el que se leen las otras dos filas: **20+ pickups y 18–21 descargas no son
   gratis**.
2. **El scripted y el evolucionado EMPATAN.** La diferencia entre ambos es de
   −4.9 %/+2.2 % (banda cercana) y −2.6 %/+2.5 % (banda ancha) según se use una
   prueba o el mejor de tres, con σ de 73–175 sobre n=10 (error estándar 23–55):
   todas las diferencias están **dentro de ~2 errores estándar**. Conclusión
   honesta: en la arena, un puñado de reglas explícitas iguala a la política
   pre-entrenada. No podemos decir «la evolución es mejor forrajeando» — sí
   podemos decir **«la evolución al menos iguala al experto a mano, y además
   tiene que resolver cosas que las reglas no codifican»** (relevo con
   descendencia, drop-avg, transferencia a bandas fuera de su entrenamiento).
3. **El orden se invierte con el protocolo, y eso es información.** El mejor de
   tres pruebas favorece sistemáticamente al scripted (una política sin
   varianza interna: repetir la prueba le da casi lo mismo), mientras el
   evolucionado gana con una sola prueba. Es el efecto esperado de comparar una
   política determinista contra una que depende de la suerte del reparto de
   comida — y la razón de publicar las dos tablas en vez de una.
4. **Evolucionar durante la prueba no ayuda a este horizonte.** La fila de
   referencia («evolucionada + pool») queda en medio o por debajo del cerebro
   congelado, incluso en la banda ancha (1982 vs 2059 con best-of-3). El pool
   paga por la selección guiada por los densados de la arena; el cerebro
   congelado cobra la tarifa completa de la comida. La evolución necesita más
   horizonte (el mundo real, 24000 ticks) para rendir.
5. **Ambas son competentes 10/10 en las dos bandas** (≥ 1 descarga en cada
   semilla): el mínimo del currículo está cubierto y las comparaciones
   interesantes pasan al mundo (salud del relevo, drop-avg, primera descarga),
   no al volumen de la arena.

## 5. Límites honestos

- **Empate no es victoria**: las diferencias observadas no permiten ordenar las
  dos políticas buenas. Harían falta muchas más semillas (o `--trials` alto) para
  afirmar una jerarquía, y el orden cambiaría con el protocolo.
- **El fitness de la arena no es una medida pura de forrajeo**: incluye los
  densados (exploración, homing, carry-leg) que la política evolucionada optimiza
  DIRECTAMENTE y la scripted ignora. La columna honesta es **descargas**; la de
  fitness premia a quien juega mejor el juego de la señal.
- **La élite evolucionada viene de la banda cercana** (warm-v2, etapas de
  3600–9000 ticks). El empate en banda ancha no prueba que «la evolución no
  sirva a distancia»: prueba que **este** pool no se entrenó para 450 u.
- **El scripted no es un baseline independiente**: lo escribió quien ajustó los
  densados de la arena, con conocimiento del problema. Es un baseline FUERTE
  (por eso es útil), no neutral.
- Una especie (`lasius`), una colonia, una réplica por semilla.

## 6. Reproducción

```bash
# banda cercana 200-260 (arena 96, 9000 ticks, 10 semillas)
dotnet run --project src/Tools/AntSim.Cli -- --mode bench \
  --band-min 200 --band-max 260 --ticks 9000 --trials 3

# banda ancha 200-450 (arena 176 celdas = 1408 u, 14400 ticks)
dotnet run --project src/Tools/AntSim.Cli -- --mode bench \
  --band-min 200 --band-max 450 --arena-cells 176 --ticks 14400 --trials 3

# la élite evolucionada sale de aquí por defecto (--seed-pool para cambiarla)
# tests/fixtures/warm-v2.antgenome
```

El comando imprime la tabla en texto y en markdown; es determinista (mismas
semillas + mismo pool + misma banda ⇒ misma tabla), y `--seeds "42 7 …"` cambia
el eje de semillas.
