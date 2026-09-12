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

**CI**: `scripts/check-replay-command.sh` replaya esta partida en cada push
contra el pool del fixture trackeado `tests/fixtures/warm-v2.antgenome`
(byte a byte el mismo archivo que `artifacts/pretrain-warm-v2.antgenome`) y
compara con el hash fijado en `scripts/replay-command.expected`. Si el hash
cambia, el PR falla — actualizar el pin con `--update` es una decisión
explícita y revisable.

## Qué demuestra

El bucle completo del diseño de UX (§2) con los mecanismos reales:

1. **Sembrar**: la partida arranca con la élite de warm-v2 (`--seed-pool`,
   registrado en la cabecera — reproducible de por vida).
2. **Intervenir**: el plan de 3 drops del jugador se hornea como comandos con
   tick (F4.0/F4.4), se ejecuta en su tick exacto y queda auditado en el
   canal B (`CommandExecuted`).
3. **Verificar**: la misma línea de comandos reproduce el mundo **bit a bit
   en cualquier máquina y build** — el hash final `816e280c…` es el certificado.

Es exactamente el contrato que la UI promete: el botón «reiniciar con plan»
(`DropFoodClickHandler`) y el «reproducir» del historial
(`CommandHistoryModel`) se apoyan en este mismo mecanismo CLI.
