# Fixtures de test (trackeados)

Los `.antgenome` de `artifacts/` son artefactos de EJECUCIÓN (gitignoreados,
regenerables con el repro de cada fase). Los archivos de este directorio son
DISTINTOS: son entradas de tests y CI, se trackean en git y nunca se
regeneran en su sitio.

## `warm-v2.antgenome`

El pool pre-entrenado de referencia (Fase 3ter), tal como se usó en el smoke
end-to-end (`docs/fase4-smoke-e2e.md`) y en los fixtures de stream.

- **SHA-256**: `be40978608bfa4e25df68d4b296e7823d2bbcd30b723af55682338953efd94bc`
- **Contenido**: 24 genomas, fitness de exportación 428.061 (comprobable con
  `antsim --mode genome-info --import tests/fixtures/warm-v2.antgenome`)
- **Procedencia**: cadena de pre-entrenamiento Fase 3/3bis — cold (seed 7) →
  warm → warm2 → refinado banda 200–260 (`--mode pretrain --seed 7 --pop 24
  --generations 15 --warm-start artifacts/pretrain-warm2.antgenome
  --band-min 200 --band-max 260`), ver `docs/fase3ter-resumen.md` §repro.
- **Quién lo consume**:
  - `scripts/check-replay-command.sh` (CI): la partida con plan de drops
    (`--seed-pool tests/fixtures/warm-v2.antgenome --drop 1500:400:300 …`)
    debe reproducir el hash fijado en `scripts/replay-command.expected`.
  - `docs/fase4-smoke-e2e.md`: el smoke con `artifacts/pretrain-warm-v2.antgenome`
    es el MISMO pool — los hashes coinciden porque el archivo es idéntico
    byte a byte.

**Por qué no se regenera en CI**: la cadena completa de entrenamiento tarda
~25 min por pool; el pool es una ENTRADA del test de determinismo, no el
sujeto. Si algún día cambia el entrenamiento y el pool se regenera de forma
intencionada, copia el nuevo archivo aquí (el sha cambia → el hash del replay
cambia → `--update` en el script, todo revisable en el PR).

La regla `*.antgenome` de `.gitignore` lleva una excepción explícita para
este directorio.

## `pretrain-neat.antgenome`

El pool NEAT de cierre (F5.2c rodaja 7) — la entrada del 6º pin de CI
(`scripts/check-neat-command.sh`, hash de partida `021ed04f…`).

- **Formato**: `.antgenome` **v2** (grafos NEAT, writer canonizado) — 64
  genomas, todos 33n/200c densos (warm-v2 convertidos + hijos de arena).
- **Procedencia**: `--mode pretrain --seed 42 --pop 24 --generations 4
  --warm-start tests/fixtures/warm-v2.antgenome --band-min 200 --band-max 260
  --export artifacts/pretrain-neat.antgenome --neat` — `NeatCurriculumTrainer`
  (warm-v2 como semilla + 4 gens/etapa con mutación estructural activa),
  tope de arena 1064.9 (currículo 3 etapas), 11 especies.
- **Quién lo consume**: `check-neat-command.sh` (CI), `NeatClosureProbe`
  (suite: siembra de mundo NEAT + canal F con grafo), y cualquier
  `--seed-pool` (el lector v2 acepta v1 y v2).

## `checkpoint-v4.antsave`

Un checkpoint **v4** real, generado por el build ANTERIOR a la huella CHC
(commit `4509edb`) — la garantía de que la compatibilidad hacia atrás del
formato no es una promesa del doc sino un archivo que CI carga en cada run.

- **SHA-256**: `e1e2ec47a266508fc4fae194003f9ca687152bd7a2df5b82aa7fe2974360af53`
- **Contenido**: seed 42, grid 32, 1 colonia (lasius), tick 300, 36139 bytes.
  Hash v4 original: `35828ac7…`.
- **Procedencia**: `git worktree add .wt-v4 4509edb` y, desde ahí,
  `--mode world --seed 42 --ticks 300 --grid 32 --colonies 1
  --save tests/fixtures/checkpoint-v4.antsave`.
- **Quién lo consume**: `ChcFootprintTests.Checkpoint_V4_DeUnBuildAnterior…` —
  el checkpoint v4 carga con la huella a cero (el tráfico anterior no se puede
  reconstruir; el mundo sigue determinista desde ahí) y **no reproduce su hash
  v4**: el hash incluye la capa nueva, así que un archivo viejo cambia de hash
  al cruzar la versión. Eso es esperado y está fijado en el test.
