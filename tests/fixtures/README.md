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
