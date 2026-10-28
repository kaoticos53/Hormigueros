#!/usr/bin/env python3
"""gland-bench.py — la partida completa juzga los dos brazos de F11b.

La tirada de `run-gland-200.sh` selecciona con 3 semillas × 24 000 ticks; esto
mide lo que la selección NO vio: la partida canónica de 216 000 ticks con
laberinto, reclutamiento y —esto es lo que cambia respecto a F11— el PRECIO del
rastro en los dos brazos (glándula de 25 s, 6 s de vacía a llena).

Cruza los dos pools (el entrenado con la puerta del cerebro y el entrenado con
el reflejo fijo) con las dos formas de decidir el depósito, y parea por semilla.
Así se separa «qué pool es mejor» de «qué puerta es mejor», que en F11 se
confundían.

Uso:
  python scripts/gland-bench.py                      # matriz 2×2, 24 semillas
  python scripts/gland-bench.py --seeds 42,99        # pasada corta
  python scripts/gland-bench.py --pools a=… b=… --ref a   # réplicas de una receta
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

# Las mismas 24 semillas de F11b/F11c: las 8 del veredicto de F11 y 16 nuevas.
# Con 8 únicamente, el subconjunto sale sesgado (en F11c el pool-fijo a 10 s da
# 212 con estas 8 y 189 con las 24): las réplicas se miden siempre con 24.
SEEDS = "42,7,99,1234,3,11,555,2024,5,13,17,23,64,128,256,512,777,888,999,1500,2025,31337,60013,98765"
POOL_GATE = "artifacts/pretrain-partida-200-gl-red.antgenome"
POOL_FIXED = "artifacts/pretrain-partida-200-gl-fijo.antgenome"
TICKS = 216000


def run(seed: int, pool: str, gate: bool, ticks: int,
        gland: float, refill: float) -> dict:
    cmd = [
        "dotnet", str(DLL), "--mode", "game", "--seed", str(seed),
        "--grid", "96", "--ticks", str(ticks), "--frame-every", "120",
        "--maze", "--recruitment", "--seed-pool", str(ROOT / pool),
        "--trail-gland", f"{gland:g}", "--trail-gland-refill", f"{refill:g}",
    ]
    if gate:
        cmd.append("--trail-by-brain")

    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:400]}")

    unloads = col0 = 0
    first = None
    end_tick = None
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
            "endTick": end_tick}


def paired(a: list[dict], b: list[dict]) -> tuple[float, float, int, int]:
    """Diferencia pareada a-b por semilla: media, error típico, ganadas, total."""
    d = [x["unloads"] - y["unloads"] for x, y in zip(a, b)]
    m = statistics.mean(d)
    sd = statistics.stdev(d) if len(d) > 1 else 0.0
    return m, (sd / len(d) ** 0.5 if d else 0.0), sum(1 for v in d if v > 0), len(d)


def build_cli() -> bool:
    """Compila el CLI en Release; sin él no hay partida que medir."""
    build = subprocess.run(["dotnet", "build", "src/Tools/AntSim.Cli", "-c", "Release",
                            "-v", "q", "--nologo"], cwd=ROOT, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
    return build.returncode == 0


def experiment(jobs_list, workers: int, pools: dict[str, str], ticks: int,
               refill: float, tag) -> dict:
    """Corre trabajos (etiqueta, precio, nombre-puerta, puerta, semilla)."""
    results: dict = {}
    t_all = time.time()
    done = 0

    def work(job):
        label, price, _gname, gate, seed = job
        return job, run(seed, pools[label], gate, ticks, price, refill)

    with ThreadPoolExecutor(max_workers=workers) as ex:
        for job, r in ex.map(work, jobs_list):
            results[job] = r
            done += 1
            print(f"  [{done:3d}/{len(jobs_list)}] {tag(job):46s} seed {r['seed']:5d}: "
                  f"{r['unloads']:3d} descargas · primera {r['firstUnload']}", flush=True)
    print(f"  ({time.time() - t_all:.0f} s)", flush=True)
    return results


def main_replicas(args) -> int:
    """Modo réplicas (F11d): N pools de la MISMA receta, cada uno jugado a los
    dos precios y con las dos formas de decidir el depósito, pareado por semilla.

    Sirve para separar «la receta es buena» de «esta tirada salió buena»: cada
    réplica es un entrenamiento independiente (semilla de GA distinta) y todas
    se miden en las mismas 24 semillas de partida que no vio ninguna.
    """
    pools: list[tuple[str, str]] = []
    for spec in args.pools:
        if "=" not in spec:
            print(f"--pools espera ETIQUETA=RUTA, no '{spec}'", file=sys.stderr)
            return 2
        label, path = spec.split("=", 1)
        pools.append((label.strip(), path.strip()))
    labels = [p[0] for p in pools]
    if len(set(labels)) != len(labels):
        print("las etiquetas de --pools se repiten", file=sys.stderr)
        return 2
    if args.ref not in labels:
        print(f"--ref tiene que ser una de las etiquetas: {', '.join(labels)}", file=sys.stderr)
        return 2

    prices = [float(p) for p in args.prices.split(",") if p.strip()]
    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    if not prices or not seeds:
        print("--prices y --seeds no pueden quedar vacíos", file=sys.stderr)
        return 2
    if not args.no_build and not build_cli():
        return 2

    gates = (("puerta", True), ("fijo", False))
    jobs_list = [(label, price, gname, gate, seed)
                 for label, _pool in pools
                 for price in prices
                 for gname, gate in gates
                 for seed in seeds]

    results = experiment(jobs_list, args.jobs, dict(pools), args.ticks, args.refill,
                         lambda j: f"{j[0]} · {j[1]:g}s · {j[2]}")

    gate_of = dict(gates)

    def cell(label: str, price: float, gname: str) -> list[dict]:
        return [results[(label, price, gname, gate_of[gname], g)] for g in seeds]

    def mean(xs) -> float:
        return statistics.mean(xs)

    def se(xs) -> float:
        return statistics.stdev(xs) / len(xs) ** 0.5 if len(xs) > 1 else 0.0

    lines = [
        f"# Réplicas de entrenamiento (F11d): la MISMA receta, {len(pools)} tiradas de GA",
        f"# grid 96, laberinto, {args.ticks} ticks, {len(seeds)} semillas de partida que "
        f"ninguna tirada vio: {args.seeds}",
        "# pp = pool entrenado con la puerta · ff = pool entrenado sin ella; cada pool se "
        "juzga con las dos puertas",
        "",
    ]

    for price in prices:
        lines += [f"## Jugado con la glándula a {price:g} s", "",
                  f"| pool ({args.recipe}) | rastro | descargas (media ± σ) | "
                  "total | semillas con descargas |",
                  "|---|---|---|---|---|"]
        for label, _pool in pools:
            for gname, _gate in gates:
                un = [r["unloads"] for r in cell(label, price, gname)]
                lines.append(f"| {label} | {gname} | {mean(un):.1f} ± "
                             f"{(statistics.stdev(un) if len(un) > 1 else 0.0):.1f} | "
                             f"{sum(un)} | {sum(1 for u in un if u > 0)}/{len(un)} |")
        lines.append("")

        for gname, _gate in gates:
            per_pool = [mean([r["unloads"] for r in cell(label, price, gname)]) for label in labels]
            agg = [mean([cell(label, price, gname)[i]["unloads"] for label, _p in pools])
                   for i in range(len(seeds))]
            ref = [r["unloads"] for r in cell(args.ref, price, gname)]
            d = [a - b for a, b in zip(agg, ref)]
            rango = f"{min(per_pool):.0f}–{max(per_pool):.0f}"
            lines.append(
                f"- **{gname} a {price:g} s**: las réplicas dan "
                + ", ".join(f"{v:.0f}" for v in per_pool)
                + f" → media **{mean(per_pool):.1f}** (rango {rango}); "
                f"la referencia {args.ref} da {mean(ref):.1f}")
            lines.append(
                f"  - pareado réplica-media − {args.ref} por semilla: "
                f"{mean(d):+.1f} ± {se(d):.1f} descargas "
                f"(gana en {sum(1 for v in d if v > 0)}/{len(d)} semillas)")

        lines += ["", f"### ¿La puerta paga dentro de cada pool, a {price:g} s?", ""]
        for label, _pool in pools:
            a = [r["unloads"] for r in cell(label, price, "puerta")]
            b = [r["unloads"] for r in cell(label, price, "fijo")]
            d = [x - y for x, y in zip(a, b)]
            lines.append(f"- **{label}**: {mean(d):+.1f} ± {se(d):.1f} "
                         f"(t = {mean(d) / se(d):+.1f}; gana {sum(1 for v in d if v > 0)}/{len(d)})"
                         if se(d) > 0 else f"- **{label}**: {mean(d):+.1f}")
        lines.append("")

    text = "\n".join(lines) + "\n"
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    print()
    print(text)
    print(f"evidencia → {args.out}")
    return 0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", default=SEEDS)
    ap.add_argument("--ticks", type=int, default=TICKS)
    ap.add_argument("--gland", type=float, default=25.0)
    ap.add_argument("--refill", type=float, default=6.0)
    ap.add_argument("--pool-gate", default=POOL_GATE,
                    help="pool entrenado con la puerta del cerebro")
    ap.add_argument("--pool-fixed", default=POOL_FIXED,
                    help="pool entrenado con el reflejo fijo")
    ap.add_argument("--jobs", type=int, default=1)
    ap.add_argument("--no-build", action="store_true",
                    help="no recompilar: usar el antsim.dll que ya está en bin/Release "
                         "(hace falta cuando otro proceso lo tiene cargado y el build no "
                         "puede copiarlo)")
    ap.add_argument("--out", default="artifacts/pretrain-partida-200-gl-bench.txt")
    ap.add_argument("--pools", action="extend", nargs="+", metavar="ETIQUETA=RUTA",
                    help="modo réplicas: mide cada pool a los dos precios y con las dos puertas")
    ap.add_argument("--prices", default="10,25",
                    help="precios de juego del modo réplicas (por defecto 10,25)")
    ap.add_argument("--ref", default=None,
                    help="etiqueta de --pools contra la que se parea")
    ap.add_argument("--recipe", default="receta",
                    help="qué receta son los pools de --pools, para el encabezado")
    args = ap.parse_args(argv)

    if args.pools:
        if args.ref is None:
            args.ref = args.pools[0].split("=", 1)[0].strip()
        return main_replicas(args)

    pools = {
        "puerta (entrenado con la puerta)": args.pool_gate,
        "fijo (entrenado con el reflejo)": args.pool_fixed,
    }
    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    if not args.no_build:
        build = subprocess.run(["dotnet", "build", "src/Tools/AntSim.Cli", "-c", "Release",
                                "-v", "q", "--nologo"], cwd=ROOT, capture_output=True, text=True)
        if build.returncode != 0:
            print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
            return 2

    arms = [(pool_name, gate_name, pool, gate)
            for pool_name, pool in pools.items()
            for gate_name, gate in (("puerta", True), ("fijo", False))]

    def work(job):
        pool_name, gate_name, pool, gate, seed = job
        return job, run(seed, pool, gate, args.ticks, args.gland, args.refill)

    jobs_list = [(pn, gn, pool, gate, seed)
                 for pn, gn, pool, gate in arms for seed in seeds]
    results: dict[tuple[str, str, int], dict] = {}
    t_all = time.time()
    done = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for (pool_name, gate_name, _pool, _gate, seed), r in ex.map(work, jobs_list):
            results[(pool_name, gate_name, seed)] = r
            done += 1
            print(f"  [{done:3d}/{len(jobs_list)}] {pool_name:34s} {gate_name:6s} "
                  f"seed {seed:5d}: {r['unloads']:3d} descargas (col0 {r['col0']:3d}) · "
                  f"primera {r['firstUnload']} · fin {r['endTick']}", flush=True)

    rows: dict[tuple[str, str], list[dict]] = {
        (pn, gn): [results[(pn, gn, s)] for s in seeds] for pn, gn, _p, _g in arms}

    lines = [
        f"# La partida completa con el PRECIO del rastro "
        f"(grid 96, laberinto, {args.ticks} ticks, semillas {args.seeds})",
        f"# glándula {args.gland:g} s / recarga {args.refill:g} s en los CUATRO brazos: "
        "aquí solo se compara qué pool y qué puerta, no el precio contra su ausencia",
        f"# pools: puerta={args.pool_gate} · fijo={args.pool_fixed}",
        "",
        "| pool entrenado | rastro | descargas (media ± σ) | total | semillas con descargas | primera descarga (media) | fin de ronda |",
        "|---|---|---|---|---|---|---|",
    ]
    for (pool_name, gate_name), rs in rows.items():
        un = [r["unloads"] for r in rs]
        firsts = [r["firstUnload"] for r in rs if r["firstUnload"]]
        sd = statistics.pstdev(un) if len(un) > 1 else 0.0
        first_txt = "—" if not firsts else f"{statistics.mean(firsts):.0f}"
        lines.append(f"| {pool_name} | {gate_name} | {statistics.mean(un):.1f} ± {sd:.1f} | "
                     f"{sum(un)} | {sum(1 for u in un if u > 0)}/{len(un)} | "
                     f"{first_txt} | {statistics.mean([r['endTick'] or 0 for r in rs]):.0f} |")

    lines += ["", "## Pareado por semilla (a − b)", ""]
    comparaciones = [
        ("tratamiento: pool-puerta con puerta − pool-fijo con reflejo",
         "puerta (entrenado con la puerta)", "puerta",
         "fijo (entrenado con el reflejo)", "fijo"),
        ("la puerta dentro del pool que la entrenó",
         "puerta (entrenado con la puerta)", "fijo",
         "puerta (entrenado con la puerta)", "puerta"),
        ("la puerta dentro del pool del reflejo",
         "fijo (entrenado con el reflejo)", "fijo",
         "fijo (entrenado con el reflejo)", "puerta"),
        ("de pool, con el reflejo fijo",
         "puerta (entrenado con la puerta)", "fijo",
         "fijo (entrenado con el reflejo)", "fijo"),
    ]
    for label, pa, ga, pb, gb in comparaciones:
        a, b = rows[(pa, ga)], rows[(pb, gb)]
        m, se, wins, n = paired(a, b)
        sign = "+" if m > 0 else ""
        lines.append(f"- **{label}**: {sign}{m:.0f} ± {se:.0f} descargas "
                     f"(a gana en {wins}/{n} semillas; {sum(x['unloads'] for x in a)} "
                     f"vs {sum(x['unloads'] for x in b)} en total)")

    lines += ["", "| semilla | " + " | ".join(f"{p.split(' ')[0]} {g}" for p, g in rows) + " |",
              "|---|" + "---|" * len(rows)]
    for i, seed in enumerate(seeds):
        lines.append(f"| {seed} | " + " | ".join(str(rows[k][i]["unloads"]) for k in rows) + " |")

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
