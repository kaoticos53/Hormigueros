#!/usr/bin/env python3
"""gland-gate-replicas.py — lee campañas de réplicas (`gland-bench.py --pools`)
y responde las dos preguntas que la tabla de medias no contesta:

  · ¿el mejor pool de una campaña es el nivel de la receta o su mejor sorteo?
    Da el puesto en cada celda, el margen sobre el segundo y la distancia del
    campeón a la media de sus tiradas, en unidades de la dispersión entre
    tiradas (si esa distancia es de orden 1, es un máximo de N sorteos normal).
  · ¿paga la puerta del cerebro dentro de cada pool, y cuánto al promediar las
    tiradas? El efecto se mide por semilla de partida (dentro del pool) y se
    agrega entre pools (fuera del pool), que es la unidad independiente cuando
    lo que se varía es el entrenamiento.

Uso:
  python scripts/gland-gate-replicas.py \\
      --log "25 s con puerta=artifacts/pretrain-partida-200-p25g-replicas.log" \\
      --log "25 s sin puerta=artifacts/pretrain-partida-200-p25-replicas.log" \\
      --champion p25g-s7 --out artifacts/pretrain-partida-200-p25g-champion.txt
"""
from __future__ import annotations

import argparse
import re
import statistics
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# "  [  1/384] p25g-s7 · 10s · puerta   seed    42: 129 descargas · primera 1442"
LINE = re.compile(r"\]\s+(\S+)\s+·\s+(\d+(?:\.\d+)?)s\s+·\s+(\w+)\s+seed\s+(\d+):\s+(\d+)\s+descargas")


def parse(path: Path) -> dict[tuple[str, float, str], dict[int, int]]:
    out: dict[tuple[str, float, str], dict[int, int]] = {}
    if not path.is_file():
        print(f"no existe el log {path}", file=sys.stderr)
        raise SystemExit(2)
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = LINE.search(line)
        if not m:
            continue
        pool, price, gate, seed, unloads = m.groups()
        out.setdefault((pool, float(price), gate), {})[int(seed)] = int(unloads)
    if not out:
        print(f"el log {path} no tiene líneas del modo réplicas", file=sys.stderr)
        raise SystemExit(2)
    return out


def se(xs: list[float]) -> float:
    return statistics.stdev(xs) / len(xs) ** 0.5 if len(xs) > 1 else 0.0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--log", action="append", required=True, metavar="ETIQUETA=RUTA")
    ap.add_argument("--champion", required=True,
                    help="pool cuyo papel de campeón se interroga (etiqueta de --pools)")
    ap.add_argument("--out", default=None)
    args = ap.parse_args(argv)

    campaigns: list[tuple[str, dict]] = []
    for spec in args.log:
        if "=" not in spec:
            print(f"--log espera ETIQUETA=RUTA, no '{spec}'", file=sys.stderr)
            return 2
        name, path = spec.split("=", 1)
        campaigns.append((name.strip(), parse(ROOT / path.strip())))

    all_data = [d for _n, d in campaigns]
    prices = sorted({k[1] for d in all_data for k in d})
    gates = sorted({k[2] for d in all_data for k in d},
                   key=lambda g: (g != "fijo", g))
    pools_of = {name: sorted({k[0] for k in d}) for name, d in campaigns}
    if args.champion not in {p for ps in pools_of.values() for p in ps}:
        print(f"--champion {args.champion} no está en ninguna campaña", file=sys.stderr)
        return 2
    seeds = sorted(set.intersection(*(set(v) for d in all_data for v in d.values())))

    def mean_of(data, pool: str, price: float, gate: str) -> float:
        return statistics.mean(data[(pool, price, gate)][s] for s in seeds)

    def per_seed(data, pool: str, price: float, gate: str) -> list[float]:
        return [data[(pool, price, gate)][s] for s in seeds]

    lines = [
        f"# El campeón de una campaña de réplicas y el efecto de la puerta del cerebro",
        f"# campañas: " + " · ".join(f"{n} ({', '.join(pools_of[n])})" for n, _d in campaigns),
        f"# {len(seeds)} semillas de partida que vio cada pool: {','.join(str(s) for s in seeds)}",
        "",
    ]

    for name, data in campaigns:
        lines += [f"## {name}: el campeón «{args.champion}» celda a celda" if
                  args.champion in pools_of[name] else f"## {name}: puestos por celda", ""]
        for price in prices:
            for gate in gates:
                pool_means = {p: mean_of(data, p, price, gate) for p in pools_of[name]}
                ranked = sorted(pool_means.items(), key=lambda kv: -kv[1])
                recipe = statistics.mean(pool_means.values())
                sd = statistics.stdev(pool_means.values()) if len(pool_means) > 1 else 0.0
                if args.champion in pool_means:
                    rank = [p for p, _v in ranked].index(args.champion) + 1
                    champ = pool_means[args.champion]
                    second = ranked[1][1] if ranked[0][0] == args.champion else ranked[0][1]
                    z = (champ - recipe) / sd if sd else 0.0
                    lines.append(
                        f"- **{args.champion} · {price:g} s · {gate}**: {champ:.1f} descargas — "
                        f"puesto {rank}/{len(pool_means)}; el segundo da {second:.1f} "
                        f"(margen {champ - second:+.1f}); la media de sus {len(pool_means)} "
                        f"tiradas {recipe:.1f} con dispersión {sd:.1f} → z = {z:+.2f}")
                else:
                    lines.append(f"- **{price:g} s · {gate}**: media de las tiradas "
                                 f"{recipe:.1f}, dispersión {sd:.1f}")
                lines.append("  - " + " > ".join(f"{p} {v:.1f}" for p, v in ranked))
        lines.append("")

    lines += ["## ¿Paga la puerta dentro de cada pool?", "",
              "Efecto = descargas con la puerta − descargas con el reflejo fijo, mismo "
              "pool y mismas semillas de partida.", ""]

    every_effect: dict[float, list[float]] = {price: [] for price in prices}
    for name, data in campaigns:
        for price in prices:
            lines.append(f"### {name} · jugado {price:g} s")
            for pool in pools_of[name]:
                d = [x - y for x, y in zip(per_seed(data, pool, price, "puerta"),
                                           per_seed(data, pool, price, "fijo"))]
                every_effect[price].append(statistics.mean(d))
                lines.append(f"- **{pool}**: {statistics.mean(d):+.1f} ± {se(d):.1f} "
                             f"(t = {statistics.mean(d) / se(d):+.1f}; "
                             f"gana {sum(1 for v in d if v > 0)}/{len(d)})"
                             if se(d) > 0 else f"- **{pool}**: {statistics.mean(d):+.1f}")
            agg = [statistics.mean([per_seed(data, p, price, "puerta")[i] -
                                    per_seed(data, p, price, "fijo")[i] for p in pools_of[name]])
                   for i in range(len(seeds))]
            lines.append(f"- **las {len(pools_of[name])} tiradas a la vez, por semilla de "
                         f"partida**: {statistics.mean(agg):+.1f} ± {se(agg):.1f} "
                         f"(t = {statistics.mean(agg) / se(agg):+.1f}; "
                         f"gana {sum(1 for v in agg if v > 0)}/{len(agg)})")
            lines.append("")

    for price in prices:
        eff = every_effect[price]
        lines.append(f"- **{len(eff)} pools · jugado {price:g} s**: la puerta paga "
                     f"{statistics.mean(eff):+.1f} ± {se(eff):.1f} descargas al promediar las "
                     f"tiradas (t = {statistics.mean(eff) / se(eff):+.1f}; "
                     f"{sum(1 for v in eff if v > 0)}/{len(eff)} tiradas a favor)")

    text = "\n".join(lines) + "\n"
    print(text)
    if args.out:
        out = ROOT / args.out
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text, encoding="utf-8")
        print(f"evidencia → {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
