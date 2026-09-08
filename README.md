# 🐜 Freebuff Ants (AntSim)

Simulación en tiempo real de hormigueros realistas con **neuroevolución continua**
(cerebros MLP → NEAT evolucionados por algoritmos genéticos), natalidad ligada a
recursos, múltiples especies y render Unity 2D → 3D. Núcleo .NET **headless y
determinista** desacoplado del motor gráfico.

Estado actual: **Fase 0 y Fase 1 completadas** (núcleo determinista + mundo con
hormigas, comida, nido y ColonyController). Ver
[`docs/arquitectura.md`](docs/arquitectura.md) para el plan por fases completo y
[`docs/especificaciones.md`](docs/especificaciones.md) para los contratos cerrados.

## Requisitos

- .NET SDK 8+ (el Core apunta a `netstandard2.1` para compatibilidad futura con Unity).
- (Futuro, Fase 4) Unity 2021+ para el proyecto gráfico.

## Estructura

```
AntSim.slnx
├─ src/Core/AntSim.Core          # núcleo headless determinista (netstandard2.1)
├─ src/Core/AntSim.Core.Tests    # tests xUnit
├─ src/Tools/AntSim.Cli          # CLI headless (microcosmos + hashes)
└─ docs/                         # arquitectura y especificaciones
```

## Uso rápido

```bash
# Tests
dotnet test AntSim.slnx

# Microcosmos determinista (dos ejecuciones con la misma semilla → salida idéntica)
dotnet run --project src/Tools/AntSim.Cli -- --mode micro --seed 42 --ticks 600 --grid 64

# Mundo completo de Fase 1 (2 colonias con hormigas, comida, cría y ColonyController)
dotnet run --project src/Tools/AntSim.Cli -- --mode world --seed 7 --ticks 1200 --grid 128 --colonies 2
```

## Garantía de determinismo

Misma semilla + mismos parámetros ⇒ misma simulación bit a bit (RNG propio
xoshiro256**, aritmética en orden fijo, floats por bits exactos, hashes de hito
por tick). Esta es la base de los checkpoints reproducibles y del modo
verificación de las fases posteriores.
