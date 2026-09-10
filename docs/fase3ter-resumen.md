# Fase 3ter — Resumen de cierre

Arranque en frío, salud del relevo, telemetría sin sondas y jerarquía final
de pools pre-entrenados. Este documento resume el estado FINAL de la fase;
los detalles de cada paso están en
[`especificaciones.md`](especificaciones.md) (secciones 4bis/4ter) y
[`arquitectura.md`](arquitectura.md).

## 1. El problema y su descomposición

El modo evolución arrancaba muerto: la colonia fundada jamás completaba un
ciclo forrajero (pickup → descarga → eclosión → relevo). La descomposición
encontró TRES mecanismos acoplados, corregidos con cambios mínimos,
realistas y deterministas:

1. **Extinción sincronizada**: las 10 fundadoras con vigor uniforme morían
   el mismo tick — el relevo por suelta al morir era imposible por
   construcción. → Vigor fundador ESCALONADO (final: 0.75–1.15).
2. **Cría inviable**: `NurseRate = 0.04` daba menos de lo que UNA larva
   necesita (0.08 ep/s) — ninguna eclosión jamás, con abundancia o sin
   ella. → `NurseRate = LarvaIdeal` (0.088, derivada de la especificación)
   + alimentación larval SERIALIZADA (la más invertida primero, empate a
   la más joven — dos trampas detectadas con sondas).
3. **Puesta sin ingresos**: la reina fundadora inundaba ~1.6 huevos/s
   (47 % de la reserva) sin comida a la vista. → Puesta ligada al inflow
   real (solo repone bajas sin ingresos — biología de fundación real).

Resultado: primera descarga y primera eclosión del proyecto, verificadas
con una política artesanal casi óptima y luego con `--seed-pool`.

## 2. Decisiones de arquitectura (en orden)

| Decisión | Mecanismo | Motivo |
|---|---|---|
| Telemetría de relevo | `RelayTracker` (solo lectura) → `first-unload`, `drop-avg`, `unload-avg`, `carry-leg` | Detectar regresiones sin sondas; hashes byte a byte intactos |
| Emisión por tick | `TickLine` compartido CLI + `WorldScenario` | Evolución del relevo DURANTE la partida, formato sin divergencias |
| Último eslabón | `carry-leg` = distancia pickup→descarga por carga completada | Separa "descarga del fundador" de "descarga por relevo" |
| Regresión en CI | `pipeline.sh --verify`: first-unload desaparece / drop-avg sube >10 % / carry-leg se encoge >20 % | Fallas reproducibles entre pools consecutivos |
| Warm-start | `CurriculumTrainer(seedGenomes:)`, CLI `--warm-start` | Refinar pools existentes sin re-entrenar desde cero |
| Densado de carry-leg | `CarryLegPerUnit = 0.15` ep/u pagado SOLO en descarga real, proporcional al tramo | Seleccionar relevo profundo sin farmear (exige pickup + descarga reales) |
| Currículo híbrido | `--hybrid`: generaciones impares en banda media, pares en ancha | Mantener DOS competencias en la misma población |
| 4ª etapa mundo completo | `--full-world`: arena de 176 celdas (1408 u), banda 200–700 u, horizonte 14 400 ticks | Cubrir el mundo real del juego (256²) con relevo multi-salto |
| Dead zone del modo juego | Vigor fundador 0.75–1.15 + cría inicial adelantada (`Age = EggTime/2`) | El pico forrajero experto solapa con la primera descendencia |
| Densidad constante | Ítems ∝ área (24 por 96² ⇒ ~171 en 256²) | Misma probabilidad de encuentro en cualquier tamaño de mundo |

## 3. Benchmark de referencia (mundo final, 8 pools × 10 semillas × 24 000 ticks)

Fuente canónica con hashes por celda: `artifacts/benchmark-fase3ter.txt`.

| pool | pickups | descargas | semillas c/ desc. | drop-avg | carry-leg |
|---|---|---|---|---|---|
| baseline | 0 | 0 | 0/10 | — | — |
| pretrain-60 (frío, 60 gens) | 79 | 6 | 5/10 | 210.9 | 85.5 |
| pretrain-warm (pop 6, OBSOLETO) | 6 | 0 | 0/10 | 229.3 | — |
| **pretrain-warm-v2 (200–260)** | 99 | 17 | **10/10** | **167.9** | **80.0** |
| pretrain-warm2 (200–350) | 91 | 8 | 7/10 | 192.3 | 76.1 |
| **pretrain-warm3 (200–450)** | **107** | **22** | **10/10** | 171.1 | 61.1 |
| pretrain-warm3c (200–450 + carry-leg) | 97 | 13 | 9/10 | 185.2 | 71.6 |
| pretrain-hybrid (alternado) | 107 | 14 | 9/10 | 175.2 | 64.2 |

Los POOLS están congelados (entrenados bajo constantes previas) — la tabla
es además una prueba de robustez de la transferencia al mundo final.

## 4. Pools adicionales evaluados fuera del benchmark

| pool | receta | mundo 96² | modo juego grid 256 (5 semillas) |
|---|---|---|---|
| warm-4 | warm3c → 4ª etapa mundo-completo | 14 descargas, 7/10 | **5/5**, 8 descargas |
| warm-5 | warm-4 → híbrido + mundo-completo | **118 pickups (récord)**, 17 descargas, 9/10 | 2/5 — la alternancia estrecha sacrifica el cierre lejano |
| warm3-v2 | warm2 → 200–450 bajo constantes finales | 18 descargas, 10/10, drop-avg 164.7 (el más sano) | 5/5, 7 descargas |

## 5. Veredictos

- **warm-v2**: el pool por defecto del modo juego. Fiabilidad 10/10 y 5/5
  en ambos tamaños de mundo, drop-avg más sano, carry-leg 80.0.
- **warm-4**: alternativa para mundo grande (5/5 en grid 256) con cobertura
  de mapa completo — el único entrenado para ítems a > 450 u.
- **warm3**: corona de VOLUMEN de descargas (22), inalcanzada por su
  re-entrenamiento (warm3-v2, 18) — el hallazgo es que su ventaja era
  robusta al cambio de constantes.
- **warm3-v2**: banda ancha con la mejor salud de relevo (drop-avg 164.7)
  y fiabilidad perfecta; confirma que el shallowing del carry-leg en
  bandas anchas es ESTRUCTURAL (presupuesto de vida de la cría que
  completa), no un artefacto de entrenamiento.
- **warm-5**: especialista de forrajeo en mundo pequeño (récord de
  pickups); su decaimiento en mundo grande documenta que híbrido +
  mundo-completo combinados no son gratis.
- **baseline**: muerto por construcción sin pool sembrado — el arranque en
  frío es insoluble sin pre-entrenamiento o sin relevo guiado.

## 6. Límites honestos

- El carry-leg en bandas anchas (~61 u) no se recupera con señal de
  entrenamiento: es física (vida de la cría que completa el tramo).
- La descarga no es 10/10 garantizada en el mundo grande para todos los
  pools: solo warm-v2, warm-4, warm3 y warm3-v2 la logran.
- El benchmark está congelado a las semillas `42 7 99 1234 777 314 555 808
  1000 2026`; las comparaciones de ±1–2 descargas entre pools fuertes son
  ruido de muestreo.
- El modo juego a 1600 s sigue sin discriminar pools con precisión (el
  88 % del horizonte es mundo estático tras la extinción): la métrica
  fiable sigue siendo la ventana de 24 000 ticks con una colonia.

## 7. Reproducción

```bash
# suite completa (89 tests)
dotnet test

# benchmark de referencia (8 pools × 10 semillas, ~25 min)
bash scripts/pipeline.sh --verify --pools "baseline \
  artifacts/pretrain-60.antgenome artifacts/pretrain-warm.antgenome \
  artifacts/pretrain-warm-v2.antgenome artifacts/pretrain-warm2.antgenome \
  artifacts/pretrain-warm3.antgenome artifacts/pretrain-warm3c.antgenome \
  artifacts/pretrain-hybrid.antgenome" \
  --seeds "42 7 99 1234 777 314 555 808 1000 2026" --ticks 24000

# re-entrenar el pool por defecto del modo juego (determinista)
dotnet run --project src/Tools/AntSim.Cli -- --mode pretrain --seed 7 \
  --pop 24 --generations 15 --warm-start artifacts/pretrain-warm2.antgenome \
  --band-min 200 --band-max 260 --export artifacts/pretrain-warm-v2.antgenome

# modo juego con el pool recomendado
dotnet run --project src/Tools/AntSim.Cli -- --mode evolve --seed 42 \
  --ticks 48000 --grid 256 --colonies 2 \
  --seed-pool artifacts/pretrain-warm-v2.antgenome
```

Estado final: 89/89 tests verdes, determinismo verificado entre procesos
(hash idéntico), toda la Fase 3ter commiteada.
