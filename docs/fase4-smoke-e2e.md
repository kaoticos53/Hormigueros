# Smoke end-to-end del bucle de juego (Fase 4) — sembrar · intervenir · verificar

Fecha: 2026-09-12 · binario: `build/antsim/antsim.exe` (Release, public) +
cruce con `dotnet run -c Debug`. Sin cambios de código: verificación del
pipeline completo tal como el jugador lo usa desde la UI.

## Comando canónico (mismo para A y B)

```bash
antsim --mode game --seed 42 --grid 96 --colonies 2 --ticks 3000 \
  --frame-every 30 \
  --seed-pool artifacts/pretrain-warm-v2.antgenome \
  --drop 1500:400:300 --drop 1600:500:250 --drop 1700:200:450 \
  > smoke-e2e-X.jsonl
```

## Resultados

| Verificación | Resultado |
|---|---|
| **Replay bit a bit** (A vs B, mismo binario) | ✅ `816e280c410c26f70b21cc7110162c27d9c012054dc742e1cae39c8500e9ca59` idéntico |
| **Cross-build** (Debug via `dotnet run` vs Release publicado) | ✅ mismo hash |
| **Baseline sin drops** | ✅ difiere: `9065919376e73e0f1b8a743ef7d94e1099efed0c309d4ab189e0b212c544ed88` — la intervención tuvo efecto |
| **Drops ejecutados** (canal B, kind 11 vía JSON, no grep) | ✅ 3/3: t1501 @(400,300), t1601 @(500,250), t1701 @(200,450) — un tick tras el tick solicitado (aplicación en el punto canónico del `Step`) |
| **Siembra visible** | ✅ `seedPool` en la cabecera del stream; `relays` aún `empty:true` a t3000 (esperado: firstUnload de referencia ≈ t5154, el smoke acaba antes) |

Archivos (no trackeados, regenerables con el repro): `artifacts/smoke-e2e-{A,B,baseline}.jsonl`.

**CI**: `scripts/check-replay-command.sh` replaya ambas fases en cada push
contra el pool del fixture trackeado `tests/fixtures/warm-v2.antgenome`
(byte a byte el mismo archivo que `artifacts/pretrain-warm-v2.antgenome`):
la fase 3000 contra `scripts/replay-command.expected` (intervención) y la
fase 6000 contra `scripts/replay-command-6k.expected` (relevo). Si un hash
cambia, el PR falla — actualizar el pin con `--update` es una decisión
explícita y revisable.

## Fase extendida (6000 ticks): el relevo entra en la verificación

Misma partida (mismos drops, fixture pool) corrida hasta 6000 ticks — el
territorio donde el relevo sembrado ya despierta:

| Verificación | Resultado |
|---|---|
| **Replay bit a bit** (6000 ticks, mismo binario) | ✅ `87fbc8edb04314d5f0335c747d54af116d8eed9ae4e3e7639d1225e22c776db7` |
| **Baseline sin drops (6000)** | ✅ difiere: `083d9309…` |
| **Primera descarga** (evento Unload, colonia 0) | ✅ **t3950** — el relevo sembrado despierta bien dentro de la partida |
| **Semáforo del canal D** | ✅ gris→ámbar en t5880/6000 (`light [[0,1],[1,0]]`): `RelayVerdict` con dropAvg 191.7 vs umbral 190 (grid 96) — la comida del JUGADOR se descargó lejos y el veredicto lo nota: la intervención es visible también en el semáforo |
| **relays[0] final** | `firstUnload 3950 · unloads 2 · carryLeg 66 · dropAvg 191.7 · chainAvg 44.5` |
| **Colonia competidora (sin sembrar)** | gris todo el stream (`light [1,0]` constante) — el contraste del selector |

Contraste con el baseline sin drops (6000): firstUnload t3943, **1 descarga,
semáforo VERDE** (`[[0,2],[1,0]]`, dropAvg 187.4 < 190). Los drops del
jugador añadieron comida que se descargó lejos → 2 descargas pero dropAvg
191.7 → ámbar. El relevo y su semáforo son parte del mundo determinista:
cambian con la intervención y se reproducen bit a bit con ella.

## Qué demuestra

El bucle completo del diseño de UX (§2) con los mecanismos reales:

1. **Sembrar**: la partida arranca con la élite de warm-v2 (`--seed-pool`,
   registrado en la cabecera — reproducible de por vida).
2. **Intervenir**: el plan de 3 drops del jugador se hornea como comandos con
   tick (F4.0/F4.4), se ejecuta en su tick exacto y queda auditado en el
   canal B (`CommandExecuted`).
3. **Verificar**: la misma línea de comandos reproduce el mundo **bit a bit
   en cualquier máquina y build** — el hash final `816e280c…` es el certificado
   (y su extensión a 6000 ticks, `87fbc8ed…`, certifica además el relevo:
   primera descarga t3950 y semáforo del canal D incluidos en el mundo fijado).

Es exactamente el contrato que la UI promete: el botón «reiniciar con plan»
(`DropFoodClickHandler`) y el «reproducir» del historial
(`CommandHistoryModel`) se apoyan en este mismo mecanismo CLI.
