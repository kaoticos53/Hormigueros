#!/usr/bin/env python3
"""pretrain-maze-transfer.py — transferencia del pool con obstáculos (F7).

Mide la señal que importa EN LA PARTIDA REAL: descargas al nido por colonia en
la partida canónica con laberinto (`--mode game --maze`, grid 96, 216 000 ticks),
comparando el pool entrenado con obstáculos contra el pool de referencia de la
Fase 3ter y contra el frío (sin pool). El objetivo del pase es que el pool nuevo
NO empeore la transferencia: se exige que gane al frío y que no quede por debajo
del de referencia en el agregado de semillas.

- Las descargas se leen del bloque `relays` del stream (contador ACUMULADO por
  colonia): no hay que sumar ventanas ni depender del frame-every.
- `--no-maze` repite la medida en campo abierto para descartar que entrenar con
  obstáculos haya roto el forrajeo clásico.
- Todo es determinista: mismas semillas ⇒ mismos números.

Uso:
  python scripts/pretrain-maze-transfer.py                     # la tabla canónica
  python scripts/pretrain-maze-transfer.py --no-maze           # regresión clásica
  python scripts/pretrain-maze-transfer.py --ticks 72000       # pasada corta
"""
from __future__ import annotations

import argparse
import json
import re
import statistics
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CLI_PROJECT = ROOT / "src" / "Tools" / "AntSim.Cli"

POOLS = {
    "frío": None,
    "warm-v2": "artifacts/pretrain-warm-v2.antgenome",
    "maze": "artifacts/pretrain-maze.antgenome",
}


def run_game(seed: int, ticks: int, pool: str | None, maze: bool) -> dict:
    """Ejecuta la partida canónica y devuelve las descargas del último frame."""
    cmd = [
        "dotnet", "run", "--project", str(CLI_PROJECT), "-c", "Release", "--no-build", "--",
        "--mode", "game", "--seed", str(seed), "--grid", "96", "--ticks", str(ticks),
        "--frame-every", "120",
    ]
    if maze:
        cmd.append("--maze")
    if pool:
        cmd += ["--seed-pool", str(ROOT / pool)]

    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:300]}")

    # El bloque `relays` viaja en cada frame: el ÚLTIMO es el acumulado de la partida.
    last = None
    for line in proc.stdout.splitlines():
        if '"relays"' in line:
            last = line
    if last is None:
        raise RuntimeError("el stream no trae bloque relays")

    m = re.search(r'"relays":\[(.*?)\](?:,"|})', last)
    if not m:
        raise RuntimeError("no pude aislar el array relays")
    colonies = []
    for entry in re.findall(r"\{.*?\}", m.group(1)):
        cols = dict(re.findall(r'"(\w+)":(null|[-0-9.]+)', entry))
        colonies.append({
            "col": int(cols.get("col", "0")),
            "unloads": int(cols.get("unloads", "0")),
            "firstUnload": None if cols.get("firstUnload") == "null" else int(cols.get("firstUnload", "0")),
            "carryLeg": None if cols.get("carryLeg") == "null" else float(cols.get("carryLeg", "0")),
        })

    return {
        "seed": seed,
        "unloads": sum(c["unloads"] for c in colonies),
        "col0": next((c["unloads"] for c in colonies if c["col"] == 0), 0),
        "col1": next((c["unloads"] for c in colonies if c["col"] == 1), 0),
        "firstUnload": min((c["firstUnload"] for c in colonies if c["firstUnload"] is not None), default=None),
        "carryLeg": [c["carryLeg"] for c in colonies if c["carryLeg"] is not None],
    }


def main(argv: list[str]) -> int:
    # En Windows la consola por defecto es cp1252 y la tabla lleva σ: sin esto el
    # informe revienta al imprimir (y el artefacto se quedaría sin escribir).
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", default="42,7,99,1234")
    ap.add_argument("--ticks", type=int, default=216000)
    ap.add_argument("--no-maze", action="store_true")
    ap.add_argument("--out", default="artifacts/pretrain-maze-transfer.txt")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    shape = "sin laberinto" if args.no_maze else "con laberinto"

    # Una sola compilación Release: las 12 partidas usan el mismo binario
    # (`--no-build` evita que cada corrida revalide el proyecto).
    build = subprocess.run(["dotnet", "build", str(CLI_PROJECT), "-c", "Release", "-v", "q", "--nologo"],
                           cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if build.returncode != 0:
        print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
        return 2
    lines: list[str] = []
    rows: dict[str, list[dict]] = {}

    for name, pool in POOLS.items():
        if pool and not (ROOT / pool).exists():
            print(f"✗ falta el pool {pool}", file=sys.stderr)
            return 2
        rows[name] = []
        for seed in seeds:
            r = run_game(seed, args.ticks, pool, maze=not args.no_maze)
            rows[name].append(r)
            if not args.quiet:
                print(f"  {name:8s} seed {seed:4d}: {r['unloads']:3d} descargas "
                      f"(col0 {r['col0']:2d} · col1 {r['col1']:2d}) · primera {r['firstUnload']}")

    # — Tabla resumen —
    lines.append(f"# Transferencia del pool con obstáculos — partida canónica {shape} "
                 f"(grid 96, {args.ticks} ticks, semillas {','.join(map(str, seeds))})")
    lines.append("")
    lines.append("| pool | descargas (media ± σ) | descargas (total) | semillas con descargas | col0 (media) | primera descarga (media) |")
    lines.append("|---|---|---|---|---|---|")
    summary = {}
    for name, rs in rows.items():
        unloads = [r["unloads"] for r in rs]
        firsts = [r["firstUnload"] for r in rs if r["firstUnload"] is not None]
        summary[name] = {
            "mean": statistics.mean(unloads) if unloads else 0.0,
            "sd": statistics.pstdev(unloads) if len(unloads) > 1 else 0.0,
            "total": sum(unloads),
            "with": sum(1 for u in unloads if u > 0),
            "col0": statistics.mean([r["col0"] for r in rs]),
            "first": statistics.mean(firsts) if firsts else None,
        }
        s = summary[name]
        first_txt = "—" if s["first"] is None else f"{s['first']:.0f}"
        lines.append(f"| {name} | {s['mean']:.1f} ± {s['sd']:.1f} | {s['total']} | "
                     f"{s['with']}/{len(rs)} | {s['col0']:.1f} | {first_txt} |")

    lines.append("")
    for name, rs in rows.items():
        cells = ", ".join(f"seed {r['seed']}={r['unloads']}" for r in rs)
        lines.append(f"- **{name}**: {cells}")

    # — Veredicto (solo con laberinto: es la condición del pase) —
    verdict = ""
    if not args.no_maze:
        maze, cold, warm = summary["maze"], summary["frío"], summary["warm-v2"]
        ok = (maze["total"] > cold["total"] and maze["total"] >= warm["total"]
              and maze["with"] == len(seeds))
        verdict = ("✓ el pool con obstáculos transfiere a la partida: gana al frío "
                   f"({maze['total']} vs {cold['total']} descargas) y no queda por debajo "
                   f"del de referencia ({maze['total']} vs {warm['total']})."
                   if ok else
                   "✗ el pool con obstáculos NO cumple: "
                   f"maze={maze['total']} · frío={cold['total']} · warm-v2={warm['total']} · "
                   f"semillas con descargas={maze['with']}/{len(seeds)}")
        lines.append("")
        lines.append(verdict)

    text = "\n".join(lines) + "\n"
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    if not args.quiet:
        print()
        print(text)
    print(f"evidencia → {args.out}")
    return 0 if (args.no_maze or verdict.startswith("✓")) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
