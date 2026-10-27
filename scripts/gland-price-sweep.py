#!/usr/bin/env python3
"""gland-price-sweep.py — ¿a qué precio del rastro paga más la puerta del cerebro?

F11b dejó la glándula a 25 s y midió una sola cosa: qué pool y qué puerta rinden
con ese precio. Aquí se barre el PRECIO, que es la variable que decide cuánto
cuesta cada depósito:

  glándula 0 s (gratis) · 10 s · 25 s · 40 s · 60 s   (recarga 6 s en todas)

Dos pools × dos formas de decidir el depósito × las 8 semillas que la selección
no vio. Todo determinista: mismo precio y misma semilla ⇒ mismo número de
descargas.

La pregunta que contesta, en dos lecturas:

  · dentro de cada pool, `puerta − fijo` a cada precio: cuánto aporta que la red
    decida el depósito cuando el rastro cuesta P;
  · de punta a punta, `pool-puerta/puerta − pool-fijo/fijo`: lo desplegable.

Barato porque la partida es de ~10 s y se corren en paralelo; el entrenamiento
no se toca (los dos pools son los de F11b, entrenados a 25 s).

Uso:
  python scripts/gland-price-sweep.py                    # barrido canónico
  python scripts/gland-price-sweep.py --prices 0,25      # pasada corta
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

SEEDS = "42,7,99,1234,3,11,555,2024"
PRICES = "0,10,25,40,60"
REFILL = "6"
POOLS = {
    "puerta": "artifacts/pretrain-partida-200-gl-red.antgenome",
    "fijo": "artifacts/pretrain-partida-200-gl-fijo.antgenome",
}
TICKS = 216000


def run(seed: int, pool: str, gate: bool, gland: float, ticks: int) -> dict:
    cmd = [
        "dotnet", str(DLL), "--mode", "game", "--seed", str(seed),
        "--grid", "96", "--ticks", str(ticks), "--frame-every", "120",
        "--maze", "--recruitment", "--seed-pool", str(ROOT / pool),
        "--trail-gland", f"{gland:g}", "--trail-gland-refill", REFILL,
    ]
    if gate:
        cmd.append("--trail-by-brain")

    t0 = time.time()
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:400]}")

    unloads = col0 = 0
    first = None
    end_tick = None
    deposits = 0
    for line in proc.stdout.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
        except json.JSONDecodeError:
            continue
        if obj.get("end"):
            end_tick = obj.get("tick")
            continue
        relays = obj.get("relays")
        if relays and any(r.get("unloads") for r in relays):
            unloads = sum(r.get("unloads", 0) for r in relays)
            col0 = next((r.get("unloads", 0) for r in relays if r.get("col") == 0), 0)
            firsts = [r["firstUnload"] for r in relays if r.get("firstUnload")]
            first = min(firsts) if firsts else None

    return {"seed": seed, "unloads": unloads, "col0": col0, "firstUnload": first,
            "endTick": end_tick, "seconds": time.time() - t0}


def paired(a: list[dict], b: list[dict]) -> tuple[float, float, int, int]:
    d = [x["unloads"] - y["unloads"] for x, y in zip(a, b)]
    m = statistics.mean(d)
    se = statistics.stdev(d) / len(d) ** 0.5 if len(d) > 1 else 0.0
    return m, se, sum(1 for v in d if v > 0), len(d)


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", default=SEEDS)
    ap.add_argument("--prices", default=PRICES)
    ap.add_argument("--ticks", type=int, default=TICKS)
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--out", default="artifacts/pretrain-partida-gl-price-sweep.txt")
    args = ap.parse_args(argv)

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    prices = [float(s) for s in args.prices.split(",") if s.strip()]

    build = subprocess.run(["dotnet", "build", "src/Tools/AntSim.Cli", "-c", "Release",
                            "-v", "q", "--nologo"], cwd=ROOT, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
        return 2

    jobs = [(p, pool_name, gate_name, gate, seed)
            for p in prices
            for pool_name, pool in POOLS.items()
            for gate_name, gate in (("puerta", True), ("fijo", False))
            for seed in seeds]

    rows: dict[tuple[float, str, str], list[dict]] = {(p, pn, gn): []
                                                     for p in prices for pn in POOLS
                                                     for gn in ("puerta", "fijo")}

    def work(job):
        p, pool_name, gate_name, gate, seed = job
        return job, run(seed, POOLS[pool_name], gate, p, args.ticks)

    t_all = time.time()
    done = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for (p, pool_name, gate_name, _gate, seed), r in ex.map(work, jobs):
            rows[(p, pool_name, gate_name)].append(r)
            done += 1
            print(f"  [{done:3d}/{len(jobs)}] P={p:>4g}s {pool_name:6s} {gate_name:6s} "
                  f"seed {seed:5d}: {r['unloads']:3d} descargas · {r['seconds']:.0f}s", flush=True)

    for key in rows:
        rows[key].sort(key=lambda r: seeds.index(r["seed"]))

    price_txt = lambda p: "gratis" if p == 0 else f"{p:g} s"

    lines = [
        f"# Barrido del PRECIO del rastro — partida canónica (grid 96, laberinto, "
        f"{args.ticks} ticks, semillas {args.seeds})",
        f"# glándula 0/10/25/40/60 s, recarga {REFILL} s en todos los brazos; "
        f"pools de F11b (entrenados a 25 s), mismo mundo para todos",
        "",
        "| precio | pool | rastro | descargas (media ± σ) | total | primera descarga (media) |",
        "|---|---|---|---|---|---|",
    ]
    for p in prices:
        for pool_name in POOLS:
            for gate_name in ("puerta", "fijo"):
                rs = rows[(p, pool_name, gate_name)]
                un = [r["unloads"] for r in rs]
                firsts = [r["firstUnload"] for r in rs if r["firstUnload"]]
                sd = statistics.pstdev(un) if len(un) > 1 else 0.0
                first_txt = "—" if not firsts else f"{statistics.mean(firsts):.0f}"
                lines.append(f"| {price_txt(p)} | {pool_name} | {gate_name} | "
                             f"{statistics.mean(un):.1f} ± {sd:.1f} | {sum(un)} | {first_txt} |")

    lines += ["", "## Lo que aporta la puerta a cada precio (dentro del mismo pool)", "",
              "| precio | pool-puerta: puerta − fijo | pool-fijo: puerta − fijo | de punta a punta |",
              "|---|---|---|---|"]
    for p in prices:
        d1 = paired(rows[(p, "puerta", "puerta")], rows[(p, "puerta", "fijo")])
        d2 = paired(rows[(p, "fijo", "puerta")], rows[(p, "fijo", "fijo")])
        e2e = paired(rows[(p, "puerta", "puerta")], rows[(p, "fijo", "fijo")])
        fmt = lambda d: f"{d[0]:+.0f} ± {d[1]:.0f} ({d[2]}/{d[3]})"
        lines.append(f"| {price_txt(p)} | {fmt(d1)} | {fmt(d2)} | {fmt(e2e)} |")

    lines += ["", "## Pareado por semilla: puerta − fijo en el pool entrenado con la puerta", "",
              "| semilla | " + " | ".join(price_txt(p) for p in prices) + " |",
              "|---|" + "---|" * len(prices)]
    for i, seed in enumerate(seeds):
        vals = []
        for p in prices:
            a = rows[(p, "puerta", "puerta")][i]["unloads"]
            b = rows[(p, "puerta", "fijo")][i]["unloads"]
            vals.append(f"{a} − {b} = {a - b:+d}")
        lines.append(f"| {seed} | " + " | ".join(vals) + " |")

    lines += ["", f"# {len(jobs)} partidas en {time.time() - t_all:.0f} s "
                  f"({args.jobs} en paralelo)"]

    text = "\n".join(lines) + "\n"
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    print()
    print(text)
    print(f"evidencia → {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
