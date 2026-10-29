#!/usr/bin/env python3
"""gland-deposits.py — CUÁNTO RASTRO DEPOSITA CADA BRAZO (F11k).

POR QUÉ EXISTE. El veredicto de F11j dejó una explicación plausible y NO
medida: con 800 s de selección el rastro del reflejo tiene el doble de tiempo
para acumularse y reclutar, mientras que el cerebro entrenado con la glándula
barata (10 s) habría aprendido a depositar poco — y esa diferencia se cobraría
más cuanto más larga es la partida. El bench sólo contaba descargas, así que la
hipótesis nunca se había mirado. Esto la mira: cuenta el rastro que cada pool
deposita en la partida, a la ventana de selección (24 000 y 48 000 ticks), con
los dos rastros y con y sin precio.

QUÉ CUENTA. `--trail-report` (F11k) añade a la línea `end` el acumulado de las
capas de comida y de casa —el reflejo de reclutamiento MÁS el canal genérico del
cerebro—, lo que la glándula no pudo pagar y el presupuesto gastado. La capa al
final no sirve: evapora, y una ventana más larga tiene más tiempo de evaporar.

CÓMO SE LEE.
  · si los dos brazos depositan lo MISMO y «negado» es alto en los dos, la
    glándula es el tope y «depositar poco» no distingue a los brazos: lo que
    cambia es CUÁNDO y DÓNDE se gasta, no cuánto;
  · si el cerebro entrenado a 10 s deja claramente menos rastro, la hipótesis de
    F11j gana un mecanismo medido;
  · sin precio (`--prices 0,10`) se ve lo que cada cerebro PIDE, separado de lo
    que el presupuesto le deja depositar;
  · el doble de ventana (`--ticks 24000,48000`) comprueba si el acumulado crece
    con el tiempo de partida — es la mitad «tiene el doble de tiempo» de la
    hipótesis.

CONVENCIÓN DE ETIQUETAS. La familia de un pool es su etiqueta sin el sufijo
`-s<semilla>` (`p10h2g-s41` → `p10h2g`); el guion agrega por familia, así que
las cuatro familias (24k/48k × puerta/reflejo) no se mezclan entre sí.

Uso:
  python scripts/gland-deposits.py \\
      --pools p10h2g-s41=artifacts/…-p10h2g41-red.antgenome … \\
      --prices 0,10 --ticks 24000,48000 --jobs 12 \\
      --out artifacts/pretrain-partida-200-p10h2-deposits.txt
"""
from __future__ import annotations

import argparse
import json
import re
import statistics
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DLL = ROOT / "src" / "Tools" / "AntSim.Cli" / "bin" / "Release" / "net8.0" / "antsim.dll"

# Las mismas 24 semillas de partida de F11b…F11j: parear con los volcados ya
# publicados exige que sean éstas y no un subconjunto.
SEEDS = "42,7,99,1234,3,11,555,2024,5,13,17,23,64,128,256,512,777,888,999,1500,2025,31337,60013,98765"
FAMILIA = re.compile(r"-s\d+$")


def run(seed: int, pool: str, gate: bool, ticks: int, price: float,
        refill: float) -> dict:
    cmd = [
        "dotnet", str(DLL), "--mode", "game", "--seed", str(seed),
        "--grid", "96", "--ticks", str(ticks), "--frame-every", "120",
        "--maze", "--recruitment", "--seed-pool", str(ROOT / pool),
        "--trail-gland", f"{price:g}", "--trail-gland-refill", f"{refill:g}",
        "--trail-report",
    ]
    if gate:
        cmd.append("--trail-by-brain")

    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:400]}")

    trail = None
    end_tick = None
    for line in proc.stdout.splitlines():
        if '"end"' not in line:
            continue
        try:
            obj = json.loads(line.strip())
        except json.JSONDecodeError:
            continue
        if obj.get("end"):
            end_tick = obj.get("tick")
            trail = obj.get("trail")
    if trail is None:
        raise RuntimeError("la línea `end` no trae el informe de rastro: ¿falta --trail-report?")

    return {"seed": seed, "ticks": end_tick,
            "food": float(trail["food"]), "home": float(trail["home"]),
            "denied": float(trail["denied"]), "gland": float(trail["gland"])}


def se(xs: list[float]) -> float:
    return statistics.stdev(xs) / len(xs) ** 0.5 if len(xs) > 1 else 0.0


def build_cli() -> bool:
    build = subprocess.run(["dotnet", "build", "src/Tools/AntSim.Cli", "-c", "Release",
                            "-v", "q", "--nologo"], cwd=ROOT, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
    return build.returncode == 0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--pools", action="extend", nargs="+", required=True,
                    metavar="ETIQUETA=RUTA", help="pools a medir")
    ap.add_argument("--seeds", default=SEEDS)
    ap.add_argument("--prices", default="0,10",
                    help="precios de juego (0 = sin glándula), por defecto 0,10")
    ap.add_argument("--ticks", default="24000,48000",
                    help="ventanas de partida, por defecto 24000,48000")
    ap.add_argument("--refill", type=float, default=6.0)
    ap.add_argument("--jobs", type=int, default=12)
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--out", default="artifacts/pretrain-partida-200-p10h2-deposits.txt")
    args = ap.parse_args(argv)

    pools: list[tuple[str, str]] = []
    for spec in args.pools:
        if "=" not in spec:
            raise SystemExit(f"--pools espera ETIQUETA=RUTA, no '{spec}'")
        label, path = spec.split("=", 1)
        pools.append((label.strip(), path.strip()))
    labels = [l for l, _p in pools]
    if len(set(labels)) != len(labels):
        raise SystemExit("las etiquetas se repiten")

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    prices = [float(p) for p in args.prices.split(",") if p.strip()]
    ticks = [int(t) for t in args.ticks.split(",") if t.strip()]
    if not seeds or not prices or not ticks:
        raise SystemExit("--seeds, --prices y --ticks no pueden quedar vacíos")
    if not args.no_build and not build_cli():
        return 2

    path_of = dict(pools)
    gates = (("puerta", True), ("fijo", False))
    gate_of = dict(gates)
    jobs_list = [(label, gate, seed, tick, price)
                 for label, _p in pools
                 for gate, _g in gates
                 for seed in seeds
                 for tick in ticks
                 for price in prices]

    results: dict = {}
    done = 0
    t_all = time.time()
    print(f"{len(jobs_list)} partidas · {args.jobs} en paralelo", flush=True)

    def work(job):
        label, gate, seed, tick, price = job
        return job, run(seed, path_of[label], gate_of[gate], tick, price, args.refill)

    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for job, r in ex.map(work, jobs_list):
            results[job] = r
            done += 1
            if done % 24 == 0 or done == len(jobs_list):
                print(f"  [{done:4d}/{len(jobs_list)}] {job[0]} · {job[1]} · {job[3]} ticks "
                      f"· {job[4]:g}s → rastro {r['food']:.1f}", flush=True)
    print(f"  ({time.time() - t_all:.0f} s)", flush=True)

    def cell(label: str, gate: str, tick: int, price: float, field: str) -> list[float]:
        return [results[(label, gate, s, tick, price)][field] for s in seeds]

    def mean(xs) -> float:
        return statistics.mean(xs)

    def familia(label: str) -> str:
        return FAMILIA.sub("", label)

    families: list[str] = []
    for label in labels:
        if familia(label) not in families:
            families.append(familia(label))

    lines = [
        "# Cuánto rastro deposita cada brazo (F11k): ¿aprendió el cerebro barato a depositar poco?",
        "# pools: " + " · ".join(f"{l} ({p})" for l, p in pools),
        f"# {len(seeds)} semillas de partida · ventanas {args.ticks} ticks · precios de juego "
        f"{args.prices} s (0 = sin glándula) · recarga {args.refill:g} s",
        "# comida+casa = lo depositado por el reflejo de reclutamiento Y por el canal genérico "
        "del cerebro; negado = lo pedido que la glándula no pudo pagar",
        "",
    ]

    for price in prices:
        lines += [f"## Precio de juego {price:g} s" + (" (sin glándula)" if price <= 0 else ""), ""]
        for tick in ticks:
            lines += [f"### Ventana de {tick} ticks", "",
                      "| pool | rastro | comida | casa | negado % | glándula (s) |",
                      "|---|---|---|---|---|---|"]
            for label in labels:
                for gate, _g in gates:
                    food = cell(label, gate, tick, price, "food")
                    home = cell(label, gate, tick, price, "home")
                    denied = cell(label, gate, tick, price, "denied")
                    gland = cell(label, gate, tick, price, "gland")
                    tot = mean(food) + mean(home)
                    neg = 100.0 * mean(denied) / (mean(denied) + tot) if mean(denied) + tot > 0 else 0.0
                    lines.append(f"| {label} | {gate} | {mean(food):.1f} | {mean(home):.1f} | "
                                 f"{neg:.0f} % | {mean(gland):.1f} |")
            lines.append("")

            # Agregado por familia: los tres pools de cada receta y horizonte.
            for fam in families:
                group = [l for l in labels if familia(l) == fam]
                for gate, _g in gates:
                    xs = [mean(cell(l, gate, tick, price, "food")) for l in group]
                    lines.append(f"- **{fam} · {gate}**: comida {mean(xs):.1f} ± {se(xs):.1f} "
                                 f"({', '.join(f'{v:.0f}' for v in xs)})")
            lines.append("")

            # La puerta dentro de cada pool: ¿abrirla deposita más?
            for label in labels:
                d = [a - b for a, b in zip(cell(label, "puerta", tick, price, "food"),
                                           cell(label, "fijo", tick, price, "food"))]
                lines.append(f"- **{label}: puerta − fijo** → {mean(d):+.1f} ± {se(d):.1f} de rastro "
                             f"(gana {sum(1 for v in d if v > 0)}/{len(d)} semillas)")
            lines.append("")

        # El doble de ventana, por brazo: ¿crece el acumulado con el tiempo?
        if len(ticks) >= 2:
            lo, hi = ticks[0], ticks[-1]
            lines += [f"### La ventana {hi} contra {lo} ticks (precio {price:g} s)", ""]
            for label in labels:
                for gate, _g in gates:
                    a = mean(cell(label, gate, lo, price, "food"))
                    b = mean(cell(label, gate, hi, price, "food"))
                    lines.append(f"- **{label} · {gate}**: {a:.1f} → {b:.1f}" +
                                 (f" (×{b / a:.2f})" if a > 0 else ""))
            lines.append("")

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
