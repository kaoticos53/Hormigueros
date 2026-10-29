#!/usr/bin/env python3
"""gland-launcher-cost.py — ¿cuánto le cuesta al LANZADOR cobrar la glándula?

LA PREGUNTA. El lanzador del juego (`scripts/play-game.sh`, `play-game.ps1`)
decide el depósito con el REFLEJO de F8, no con la puerta del cerebro: la
partida carga un pool entrenado sin precio (`pretrain-maze-los3`) y no expone
`--trail-by-brain`. Con esa configuración el precio de la glándula no disciplina
ninguna decisión aprendida: sólo raciona lo que el reflejo pide. Este guion mide
lo que se pierde si el lanzador lo cobrase a 25 s —el precio que F11g/F11i
dejaron como condición de despliegue para ENTRENAR con la puerta—.

CÓMO SE MIDE. La MISMA partida del lanzador (grid 96, laberinto, reclutamiento,
216 000 ticks, el pool de fábrica, el reflejo decidiendo) jugada con la glándula
a 0 y a 25 s, con las 24 semillas de partida de F11b…F11k para poder parear. Se
lee el informe de rastro de `--trail-report` (F11k: lo depositado en comida y en
casa, lo que la glándula no pudo pagar y los segundos gastados) y las descargas
de la línea de relays.

QUÉ DICE CADA COLUMNA.
  · comida / casa: rastro que el reflejo LLEGÓ a dejar en toda la partida;
  · negado %: lo pedido que el presupuesto no pagó, sobre lo pedido total;
  · glándula s: segundos de presupuesto consumidos de verdad;
  · descargas: lo que la colonia cobró — si el precio no mueve esto, el coste
    del precio es el rastro visible, no la cosecha.

Uso:
  python scripts/gland-launcher-cost.py \\
      --pool artifacts/pretrain-maze-los3.antgenome \\
      --prices 0,25 --jobs 6 \\
      --out artifacts/pretrain-partida-200-lanzador-gland.txt
"""
from __future__ import annotations

import argparse
import json
import statistics
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DLL = ROOT / "src" / "Tools" / "AntSim.Cli" / "bin" / "Release" / "net8.0" / "antsim.dll"

SEEDS = "42,7,99,1234,3,11,555,2024,5,13,17,23,64,128,256,512,777,888,999,1500,2025,31337,60013,98765"


def run(seed: int, pool: str, ticks: int, price: float, refill: float) -> dict:
    cmd = [
        "dotnet", str(DLL), "--mode", "game", "--seed", str(seed),
        "--grid", "96", "--ticks", str(ticks), "--frame-every", "120",
        "--maze", "--recruitment", "--seed-pool", str(ROOT / pool),
        "--trail-gland", f"{price:g}", "--trail-gland-refill", f"{refill:g}",
        "--trail-report",
    ]
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:400]}")

    trail = None
    unloads = 0
    for line in proc.stdout.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            obj = json.loads(line)
        except json.JSONDecodeError:
            continue
        relays = obj.get("relays")
        if relays:
            unloads = sum(r.get("unloads", 0) for r in relays)
        if obj.get("end"):
            trail = obj.get("trail")
    if trail is None:
        raise RuntimeError("la línea `end` no trae el informe de rastro: ¿falta --trail-report?")

    return {"seed": seed, "food": float(trail["food"]), "home": float(trail["home"]),
            "denied": float(trail["denied"]), "gland": float(trail["gland"]),
            "unloads": unloads}


def se(xs: list[float]) -> float:
    return statistics.stdev(xs) / len(xs) ** 0.5 if len(xs) > 1 else 0.0


def sd(xs: list[float]) -> float:
    return statistics.stdev(xs) if len(xs) > 1 else 0.0


def denied_pct(rows: list[dict]) -> float:
    asked = sum(r["denied"] + r["food"] + r["home"] for r in rows)
    return 100.0 * sum(r["denied"] for r in rows) / asked if asked else 0.0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--pool", default="artifacts/pretrain-maze-los3.antgenome",
                    help="pool que carga el lanzador")
    ap.add_argument("--seeds", default=SEEDS)
    ap.add_argument("--prices", default="0,25",
                    help="precios a comparar (0 = sin glándula, el default del lanzador)")
    ap.add_argument("--ticks", type=int, default=216000)
    ap.add_argument("--refill", type=float, default=6.0)
    ap.add_argument("--jobs", type=int, default=6)
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--out", default="artifacts/pretrain-partida-200-lanzador-gland.txt")
    args = ap.parse_args(argv)

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    prices = [float(p) for p in args.prices.split(",") if p.strip()]
    if len(prices) != 2:
        raise SystemExit("--prices necesita exactamente dos precios (el par que se compara)")
    if not (ROOT / args.pool).is_file():
        raise SystemExit(f"no existe el pool {args.pool}")
    if not args.no_build:
        build = subprocess.run(["dotnet", "build", "src/Tools/AntSim.Cli", "-c", "Release",
                                "-v", "q", "--nologo"], cwd=ROOT, capture_output=True, text=True)
        if build.returncode != 0:
            print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
            return 2

    jobs = [(seed, price) for price in prices for seed in seeds]
    rows: dict[tuple[int, float], dict] = {}
    t0 = time.time()
    done = 0

    def work(job):
        seed, price = job
        return job, run(seed, args.pool, args.ticks, price, args.refill)

    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for (seed, price), r in ex.map(work, jobs):
            rows[(seed, price)] = r
            done += 1
            print(f"  [{done:3d}/{len(jobs)}] semilla {seed:5d} · {price:g} s: "
                  f"comida {r['food']:9.1f} · casa {r['home']:9.1f} · "
                  f"{r['unloads']:3d} descargas", flush=True)
    print(f"  ({time.time() - t0:.0f} s)", flush=True)

    by_price = {p: [rows[(s, p)] for s in seeds] for p in prices}

    lines = [
        "# ¿Qué le cuesta al lanzador cobrar la glándula?",
        f"# pool {args.pool} · grid 96 · laberinto · reclutamiento · {args.ticks} ticks · "
        f"reflejo decidiendo (sin --trail-by-brain) · recarga {args.refill:g} s",
        f"# {len(seeds)} semillas de partida: {args.seeds}",
        "# 0 s = la partida de fábrica del lanzador; 25 s = el precio con el que F11g/F11i "
        "entrenan y con el que juegan los benches de despliegue",
        "",
        "| glándula | comida | casa | negado % | glándula (s) | descargas |",
        "|---|---|---|---|---|---|",
    ]
    for p in prices:
        rs = by_price[p]
        lines.append(f"| {p:g} s | {statistics.mean([r['food'] for r in rs]):.1f} ± "
                     f"{sd([r['food'] for r in rs]):.1f} | "
                     f"{statistics.mean([r['home'] for r in rs]):.0f} ± "
                     f"{sd([r['home'] for r in rs]):.0f} | "
                     f"{denied_pct(rs):.1f} % | "
                     f"{statistics.mean([r['gland'] for r in rs]):.0f} | "
                     f"{statistics.mean([r['unloads'] for r in rs]):.1f} ± "
                     f"{sd([r['unloads'] for r in rs]):.1f} |")
    lines.append("")

    a, b = prices[1], prices[0]
    for key, label in (("food", "rastro de comida"), ("home", "rastro de casa"),
                       ("unloads", "descargas")):
        d = [rows[(s, a)][key] - rows[(s, b)][key] for s in seeds]
        m, e = statistics.mean(d), se(d)
        lines.append(f"- **{label}: {a:g} s − {b:g} s** = {m:+.1f} ± {e:.1f} "
                     f"(t = {m / e:+.1f})" if e > 0 else
                     f"- **{label}: {a:g} s − {b:g} s** = {m:+.1f}")
        lines.append(f"  - {a:g} s deja más en {sum(1 for v in d if v > 0)}/{len(d)} semillas")

    lines += [
        "",
        "## Cómo se lee",
        "",
        "El precio a 25 s no disciplina ninguna decisión en el lanzador —el reflejo deposita",
        "siempre lo mismo—: sólo decide cuánto de lo pedido se paga. Si el negado es alto y el",
        "rastro depositado cae frente a 0 s, el precio no cambia la cosecha de la colonia",
        "(descargas), cambia lo que el jugador VE: el rastro de F8 que el lanzador existe",
        "para enseñar.",
        "",
        "Estas cifras no las deriva `scripts/partida-figures.py`: el documento que las cite",
        "las acompaña de este volcado y de la orden que lo reproduce.",
    ]

    text = "\n".join(lines) + "\n"
    print()
    print(text)
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    print(f"evidencia → {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
