#!/usr/bin/env python3
"""gland-recipe-compare.py — compara dos recetas de entrenamiento réplica a
réplica, a partir de los logs del modo réplicas de `gland-bench.py`.

Cada log es una campaña de réplicas: N pools de la MISMA receta (una tirada de
GA cada uno), jugados a los dos precios y con las dos formas de decidir el
depósito, en las mismas 24 semillas de partida. Con dos campañas —una por
receta— esto responde «¿el precio de entrenamiento importa?» sin confundirlo
con la suerte de una tirada:

  · pareado por semilla de partida (24): media de las réplicas de una receta
    menos la de la otra, con su error típico;
  · pareado por semilla de GA (N): para cada tirada, su pool de una receta
    menos el de la otra; dice si el signo se sostiene en todas las tiradas o si
    depende de cuál salió buena;
  · dispersión dentro de cada receta (rango de las medias de sus pools) frente
    a la diferencia entre recetas: si son del mismo tamaño, la receta no
    explica nada que la tirada no explique ya.

Uso:
  python scripts/gland-recipe-compare.py \\
      --a 10s=artifacts/pretrain-partida-200-p10-replicas.log \\
      --b 25s=artifacts/pretrain-partida-200-p25-replicas.log \\
      --out artifacts/pretrain-partida-200-p25-veredicto.txt
"""
from __future__ import annotations

import argparse
import re
import statistics
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# "  [  1/384] p10-s7 · 10s · puerta                          seed    42: 129 descargas · primera 1442"
LINE = re.compile(r"\]\s+(\S+)\s+·\s+(\d+(?:\.\d+)?)s\s+·\s+(\w+)\s+seed\s+(\d+):\s+(\d+)\s+descargas")


def parse(path: Path) -> dict[tuple[str, float, str], dict[int, int]]:
    """(pool, precio, puerta) → {semilla de partida: descargas}."""
    out: dict[tuple[str, float, str], dict[int, int]] = {}
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = LINE.search(line)
        if not m:
            continue
        pool, price, gate, seed, unloads = m.groups()
        out.setdefault((pool, float(price), gate), {})[int(seed)] = int(unloads)
    return out


def se(xs: list[float]) -> float:
    return statistics.stdev(xs) / len(xs) ** 0.5 if len(xs) > 1 else 0.0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--a", required=True, metavar="ETIQUETA=LOG")
    ap.add_argument("--b", required=True, metavar="ETIQUETA=LOG")
    ap.add_argument("--gate", default="fijo", choices=("fijo", "puerta"),
                    help="qué rastro se compara; por defecto fijo")
    ap.add_argument("--out", default=None)
    args = ap.parse_args(argv)

    (name_a, log_a), (name_b, log_b) = (s.split("=", 1) for s in (args.a, args.b))
    for log in (log_a, log_b):
        if not (ROOT / log).is_file():
            print(f"no existe el log {log}", file=sys.stderr)
            return 2
    data_a, data_b = parse(ROOT / log_a), parse(ROOT / log_b)
    if not data_a or not data_b:
        print("algún log no tiene líneas de resultados del modo réplicas", file=sys.stderr)
        return 2

    prices = sorted({k[1] for k in data_a} & {k[1] for k in data_b})
    pools_a = sorted({k[0] for k in data_a})
    pools_b = sorted({k[0] for k in data_b})
    seeds = sorted(set.intersection(*(set(v) for v in data_a.values())))
    if not prices or not seeds or not pools_a or not pools_b:
        print("los logs no comparten precios/semillas o están vacíos", file=sys.stderr)
        return 2

    def cell(data, pool: str, price: float, gate: str) -> list[int]:
        return [data[(pool, price, gate)][s] for s in seeds]

    def mean_of(data, pools: list[str], price: float, gate: str) -> list[float]:
        """Media de las réplicas de una receta, semilla de partida a semilla."""
        return [statistics.mean([data[(p, price, gate)][s] for p in pools]) for s in seeds]

    lines = [
        f"# {name_a} contra {name_b}: réplicas de dos recetas de entrenamiento",
        f"# {name_a}: {', '.join(pools_a)} · {name_b}: {', '.join(pools_b)}",
        f"# {len(seeds)} semillas de partida que no vio ninguna tirada, "
        f"rastro comparado: {args.gate}",
        "",
    ]

    for price in prices:
        lines += [f"## Jugado con la glándula a {price:g} s", "",
                  f"| receta | pool | descargas (media ± σ) |", "|---|---|---|"]
        for name, data, pools in ((name_a, data_a, pools_a), (name_b, data_b, pools_b)):
            for p in pools:
                un = cell(data, p, price, args.gate)
                lines.append(f"| {name} | {p} | {statistics.mean(un):.1f} ± "
                             f"{statistics.stdev(un):.1f} |")
        lines.append("")

        ma = mean_of(data_a, pools_a, price, args.gate)
        mb = mean_of(data_b, pools_b, price, args.gate)
        d = [x - y for x, y in zip(ma, mb)]
        m, e = statistics.mean(d), se(d)
        per_pool_a = [statistics.mean(cell(data_a, p, price, args.gate)) for p in pools_a]
        per_pool_b = [statistics.mean(cell(data_b, p, price, args.gate)) for p in pools_b]
        lines.append(f"- **{name_a} − {name_b} ({args.gate}, jugado {price:g} s)**: "
                     f"{m:+.1f} ± {e:.1f} descargas (t = {m / e:+.1f}; "
                     f"gana en {sum(1 for v in d if v > 0)}/{len(d)} semillas)")
        lines.append(f"  - {name_a}: media de sus {len(pools_a)} réplicas {statistics.mean(per_pool_a):.1f} "
                     f"(rango {min(per_pool_a):.0f}–{max(per_pool_a):.0f}) · "
                     f"{name_b}: {statistics.mean(per_pool_b):.1f} "
                     f"(rango {min(per_pool_b):.0f}–{max(per_pool_b):.0f})")
        spread = max(max(per_pool_a) - min(per_pool_a), max(per_pool_b) - min(per_pool_b))
        lines.append(f"  - la tirada mueve hasta {spread:.0f} descargas dentro de una receta; "
                     f"la receta mueve {abs(m):.1f}")
        lines.append("")

        # Pareado por semilla de GA: exige que las dos campañas compartan sufijo.
        suffix = lambda p: p.rsplit("-", 1)[-1]
        shared = sorted({suffix(p) for p in pools_a} & {suffix(p) for p in pools_b})
        if shared:
            by_ga = []
            for s in shared:
                pa = next(p for p in pools_a if suffix(p) == s)
                pb = next(p for p in pools_b if suffix(p) == s)
                va = statistics.mean(cell(data_a, pa, price, args.gate))
                vb = statistics.mean(cell(data_b, pb, price, args.gate))
                by_ga.append((s, va, vb, va - vb))
            lines.append(f"- **por tirada de GA ({len(by_ga)}):** " + " · ".join(
                f"{s} {va:.0f} vs {vb:.0f} = {d:+.0f}" for s, va, vb, d in by_ga))
            ds = [d for *_r, d in by_ga]
            lines.append(f"  - {sum(1 for v in ds if v > 0)}/{len(ds)} tiradas a favor de "
                         f"{name_a}; diferencia entre tiradas {min(ds):+.0f}…{max(ds):+.0f}")
            lines.append("")

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
