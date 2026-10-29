#!/usr/bin/env python3
"""gland-matrix-compare.py — el 2×2 PRECIO × HORIZONTE completo.

LA PREGUNTA. F11h/F11i dejaron medido que entrenar con la puerta del cerebro
cuesta descargas a 10 s de glándula y las gana a 25 s. F11j dobló el horizonte
de selección SÓLO a 10 s y el castigo creció en vez de cerrarse, con lo que el
horizonte quedó descartado como causa del castigo a 10 s. Faltaba la cuarta
casilla —25 s a 48 000 ticks—, y sin ella no se sabe si las dos causas se suman
o si el horizonte sólo muerde donde el depósito es barato.

LAS CUATRO CASILLAS (precio de ENTRENAMIENTO × horizonte de SELECCIÓN):

    10 s / 24 000 · 10 s / 48 000 · 25 s / 24 000 · 25 s / 48 000

En cada casilla el EFECTO es el brazo entrenado con la puerta menos el entrenado
con el reflejo, con las mismas semillas de GA y las mismas 24 semillas de
partida: dentro de una casilla el pareado es exacto (cada sorteo aporta su pool
con puerta y su pool con reflejo). Entre casillas se alinean los sorteos por
FILA de precio —las dos de 10 s sobre 41/43/47, las dos de 25 s sobre 7/11/13—,
así que el salto de HORIZONTE va pareado en las dos filas. El salto de PRECIO no
comparte ningún sorteo (las filas se entrenaron con tiradas distintas), y ahí el
comparador resta medias y lo declara «sin parear» en vez de inventar un error
típico que no existe.

LO QUE NO ES COMPARABLE, y es a propósito: el NIVEL de descargas entre
horizontes. A 48 000 la selección juega partidas del doble de largas y hasta el
brazo de referencia sube de nivel; lo comparable es el efecto de la puerta
DENTRO de cada casilla y el tamaño del salto entre casillas.

Uso:
  python scripts/gland-matrix-compare.py \\
      --arm 10s/24k=puerta:artifacts/pretrain-partida-200-p10g7-replicas.log \\
      --arm 10s/24k=reflejo:artifacts/pretrain-partida-200-p10x10-replicas.log \\
      --arm 10s/48k=puerta:artifacts/pretrain-partida-200-p10h2g-replicas.log \\
      --arm 10s/48k=reflejo:artifacts/pretrain-partida-200-p10h2s-replicas.log \\
      --arm 25s/24k=puerta:artifacts/pretrain-partida-200-p25g7-replicas.log \\
      --arm 25s/24k=reflejo:artifacts/pretrain-partida-200-p257-replicas.log \\
      --arm 25s/48k=puerta:artifacts/pretrain-partida-200-p25h2g-replicas.log \\
      --arm 25s/48k=reflejo:artifacts/pretrain-partida-200-p25h2s-replicas.log \\
      --out artifacts/pretrain-partida-200-p25h2-veredicto.txt

Salida: 0 reparto escrito · 2 falta una casilla, un brazo o un precio de juego
común (no se rellena el hueco: un reparto sobre datos a medias es peor que
ninguno).
"""
from __future__ import annotations

import argparse
import math
import re
import statistics
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# "  [  1/672] p10h2g-s41 · 10s · puerta  seed 42:  99 descargas · primera 1370"
LINE = re.compile(r"\]\s+(\S+)\s+·\s+(\d+(?:\.\d+)?)s\s+·\s+(\w+)\s+seed\s+(\d+):\s+(\d+)\s+descargas")
RECIPES = ("puerta", "reflejo")
# «10s/24k» → precio de entrenamiento 10 s, horizonte 24 000 ticks.
CELL = re.compile(r"^(\d+(?:\.\d+)?)s/(\d+(?:\.\d+)?)k$")


def parse(path: Path) -> dict[tuple[str, float, str], dict[int, int]]:
    """(pool, precio de juego, rastro) → {semilla de partida: descargas}."""
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


def tstat(m: float, e: float) -> str:
    return f"{m / e:+.1f}" if e > 0 else "—"


def mean(xs) -> float:
    return statistics.mean(xs)


def sort_key(token: str) -> tuple[int, str]:
    num = token[1:]
    return (int(num), token) if num.isdigit() else (10 ** 9, token)


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--arm", action="append", required=True,
                    metavar="PRECIO/HORIZONTE=BRAZO:LOG",
                    help="campaña de réplicas de un brazo en una casilla del 2×2")
    ap.add_argument("--out", default=None)
    args = ap.parse_args(argv)

    arms: dict[tuple[str, str], dict] = {}
    logs: dict[tuple[str, str], str] = {}
    for spec in args.arm:
        if "=" not in spec:
            print(f"--arm espera PRECIO/HORIZONTE=BRAZO:LOG, no '{spec}'", file=sys.stderr)
            return 2
        cell, rest = spec.split("=", 1)
        if ":" not in rest:
            print(f"--arm espera PRECIO/HORIZONTE=BRAZO:LOG, no '{spec}'", file=sys.stderr)
            return 2
        recipe, log = (s.strip() for s in rest.split(":", 1))
        cell = cell.strip()
        if not CELL.match(cell):
            print(f"casilla '{cell}' no tiene la forma PRECIOs/HORIZONTEk (p. ej. 10s/24k)",
                  file=sys.stderr)
            return 2
        if recipe not in RECIPES:
            print(f"brazo desconocido '{recipe}' (puerta|reflejo)", file=sys.stderr)
            return 2
        if (cell, recipe) in arms:
            print(f"la casilla {cell} tiene dos brazos «{recipe}»", file=sys.stderr)
            return 2
        if not (ROOT / log).is_file():
            print(f"no existe el log {log}", file=sys.stderr)
            return 2
        data = parse(ROOT / log)
        if not data:
            print(f"el log {log} no tiene líneas de resultados del modo réplicas",
                  file=sys.stderr)
            return 2
        arms[(cell, recipe)] = data
        logs[(cell, recipe)] = log

    cells = sorted({c for c, _ in arms}, key=lambda c: (
        float(CELL.match(c).group(2)), float(CELL.match(c).group(1))))
    prices_train = sorted({float(CELL.match(c).group(1)) for c in cells})
    horizons = sorted({float(CELL.match(c).group(2)) for c in cells})
    if len(cells) != len(prices_train) * len(horizons):
        faltan = [(p, h) for p in prices_train for h in horizons
                  if not any(CELL.match(c).group(1) == f"{p:g}"
                             and CELL.match(c).group(2) == f"{h:g}" for c in cells)]
        print("el 2×2 está incompleto: faltan las casillas "
              + ", ".join(f"{p:g}s/{h:g}k" for p, h in faltan), file=sys.stderr)
        return 2
    for cell in cells:
        missing = [r for r in RECIPES if (cell, r) not in arms]
        if missing:
            print(f"a la casilla {cell} le falta el brazo {', '.join(missing)}: "
                  "sin las dos recetas no hay efecto que medir", file=sys.stderr)
            return 2

    # ── Qué se puede comparar: precios de juego y rastros comunes a las ocho ──
    each = [d for d in arms.values()]
    game_prices = sorted(set.intersection(*(set(k[1] for k in d) for d in each)))
    game_gates = [g for g in ("fijo", "puerta")
                  if all(g in {k[2] for k in d} for d in each)]
    if not game_prices:
        print("las casillas no comparten ningún precio de juego", file=sys.stderr)
        return 2
    if not game_gates:
        print("las casillas no comparten forma de decidir el depósito (fijo|puerta)",
              file=sys.stderr)
        return 2

    # Etiqueta de pool por sorteo de GA, en cada brazo. Dos pools con el mismo
    # sorteo en el mismo brazo es un volcado mal armado: se declara.
    by_suffix: dict[tuple[str, str], dict[str, str]] = {}
    for key, data in arms.items():
        table: dict[str, str] = {}
        for pool in sorted({k[0] for k in data}):
            suffix = pool.rsplit("-", 1)[-1]
            if suffix in table:
                print(f"{logs[key]}: dos pools con el mismo sorteo {suffix} "
                      f"({table[suffix]} y {pool})", file=sys.stderr)
                return 2
            table[suffix] = pool
        by_suffix[key] = table

    sorts: dict[str, list[str]] = {}
    for cell in cells:
        common = set(by_suffix[(cell, "puerta")]) & set(by_suffix[(cell, "reflejo")])
        sorts[cell] = sorted(common, key=sort_key)
        if len(sorts[cell]) < 2:
            print(f"la casilla {cell} no tiene dos sorteos de GA con las dos recetas: "
                  f"{', '.join(sorts[cell]) or 'ninguno'}", file=sys.stderr)
            return 2

    def cell_of(price: float, horizon: float) -> str:
        return next(c for c in cells
                    if CELL.match(c).group(1) == f"{price:g}"
                    and CELL.match(c).group(2) == f"{horizon:g}")

    # Alineación por FILA de precio: si las dos casillas de un mismo precio de
    # entrenamiento comparten sorteos, las dos se leen sobre esa intersección, de
    # modo que el salto de horizonte de esa fila sale pareado. Si no comparten
    # ninguno, cada casilla se queda con los suyos y el salto se declara sin
    # parear.
    read_sorts: dict[str, list[str]] = {}
    for cell in cells:
        p = float(CELL.match(cell).group(1))
        row = [cell_of(p, h) for h in horizons]
        common = set(sorts[row[0]]) & set(sorts[row[1]])
        read_sorts[cell] = sorted(common, key=sort_key) if len(common) >= 2 else sorts[cell]

    def pool_mean(cell: str, recipe: str, sort: str, price: float, gate: str) -> float:
        pool = by_suffix[(cell, recipe)][sort]
        return mean(arms[(cell, recipe)][(pool, price, gate)].values())

    # Efecto de la puerta por sorteo de GA: misma tirada, mismo precio de juego.
    effect: dict[tuple[str, float, str], dict[str, float]] = {}
    for cell in cells:
        for price in game_prices:
            for gate in game_gates:
                effect[(cell, price, gate)] = {
                    s: pool_mean(cell, "puerta", s, price, gate)
                       - pool_mean(cell, "reflejo", s, price, gate)
                    for s in read_sorts[cell]}

    def pair_detail(a_cell: str, b_cell: str, how: str, common: list[str]) -> str:
        if how == "pareado":
            return "pareado por sorteo: " + " · ".join(common)
        return (f"sin parear: {a_cell} tiene {len(read_sorts[a_cell])} sorteos y "
                f"{b_cell} {len(read_sorts[b_cell])}, y no comparten ninguno")

    def compare(a_cell: str, b_cell: str, price: float, gate: str):
        """Efecto(a) − efecto(b): pareado si comparten sorteos, si no medias."""
        a, b = effect[(a_cell, price, gate)], effect[(b_cell, price, gate)]
        common = [s for s in read_sorts[a_cell] if s in read_sorts[b_cell]]
        if len(common) >= 2:
            d = [a[s] - b[s] for s in common]
            return "pareado", common, d, mean(d), se(d)
        m = mean(list(a.values())) - mean(list(b.values()))
        e = math.hypot(se(list(a.values())), se(list(b.values())))
        return "sin parear", common, None, m, e

    def fmt_cell(cell: str) -> str:
        g = CELL.match(cell)
        return f"{float(g.group(1)):g} s / {float(g.group(2)):g}k"

    lines = [
        "# El 2×2 precio × horizonte: ¿dónde se cobra el castigo de la puerta?",
        "# casillas: " + " · ".join(f"{c} ({logs[(c, 'puerta')]}, {logs[(c, 'reflejo')]})"
                                   for c in cells),
        f"# precios de juego {', '.join(f'{p:g} s' for p in game_prices)} · "
        f"rastro {' y '.join(game_gates)} · "
        f"sorteos con los que se lee cada casilla "
        f"{', '.join(f'{c}: {len(read_sorts[c])}' for c in cells)}",
        "# Los sorteos se alinean por fila de precio (las dos casillas de 10 s sobre "
        "41/43/47 y las dos de 25 s sobre 7/11/13): el salto de HORIZONTE va pareado. "
        "El de PRECIO no comparte ningún sorteo y por eso se resta sin parear.",
        "# El NIVEL no es comparable entre horizontes (a 48k la partida es el doble de "
        "larga): se compara el EFECTO de la puerta dentro de cada casilla.",
        "",
    ]

    # ── Las cuatro casillas, casilla a casilla ────────────────────────────────
    for price in game_prices:
        for gate in game_gates:
            lines += [f"## Jugado con la glándula a {price:g} s · rastro {gate}", "",
                      "| precio de entrenamiento | horizonte | efecto (puerta − reflejo) | "
                      "sorteos |", "|---|---|---|---|"]
            for cell in cells:
                g = CELL.match(cell)
                vals = list(effect[(cell, price, gate)].values())
                lines.append(f"| {float(g.group(1)):g} s | {float(g.group(2)):g}k | "
                             f"{mean(vals):+.1f} ± {se(vals):.1f} | {len(vals)} |")
            lines.append("")

    # ── Los saltos a lo largo de cada eje ─────────────────────────────────────
    lines += ["## Cuánto mueve cada eje el efecto de la puerta", ""]
    for price in game_prices:
        for gate in game_gates:
            lines.append(f"### jugado {price:g} s · rastro {gate}")
            for p in prices_train:
                h0, h1 = horizons[0], horizons[1]
                a, b = cell_of(p, h1), cell_of(p, h0)
                how, common, _, m, e = compare(a, b, price, gate)
                detail = pair_detail(a, b, how, common)
                lines.append(
                    f"- **horizonte a {p:g} s**: {fmt_cell(b)} → {fmt_cell(a)} · "
                    f"{mean(list(effect[(b, price, gate)].values())):+.1f} → "
                    f"{mean(list(effect[(a, price, gate)].values())):+.1f} = "
                    f"{m:+.1f} ± {e:.1f} descargas (t = {tstat(m, e)}; {detail})")
            for h in horizons:
                p0, p1 = prices_train[0], prices_train[1]
                a, b = cell_of(p1, h), cell_of(p0, h)
                how, common, _, m, e = compare(a, b, price, gate)
                detail = pair_detail(a, b, how, common)
                lines.append(
                    f"- **precio a {h:g}k**: {fmt_cell(b)} → {fmt_cell(a)} · "
                    f"{mean(list(effect[(b, price, gate)].values())):+.1f} → "
                    f"{mean(list(effect[(a, price, gate)].values())):+.1f} = "
                    f"{m:+.1f} ± {e:.1f} descargas (t = {tstat(m, e)}; {detail})")
            lines.append("")

    # ── El reparto: aditivo o con interacción ─────────────────────────────────
    lines += ["## Reparto del castigo entre las cuatro casillas", "",
              "El castigo de referencia es el de la casilla donde la puerta pierde: "
              "10 s de entrenamiento y 24 000 ticks de selección. Se descompone en el "
              "salto de HORIZONTE con el precio quieto, el salto de PRECIO con el "
              "horizonte corto, y lo que queda —la interacción— cuando se mueven los "
              "dos a la vez. Aditivo = las dos causas se suman sin tocarse.", ""]
    for price in game_prices:
        for gate in game_gates:
            e00 = mean(list(effect[(cell_of(prices_train[0], horizons[0]), price, gate)].values()))
            e01 = mean(list(effect[(cell_of(prices_train[0], horizons[1]), price, gate)].values()))
            e10 = mean(list(effect[(cell_of(prices_train[1], horizons[0]), price, gate)].values()))
            e11 = mean(list(effect[(cell_of(prices_train[1], horizons[1]), price, gate)].values()))
            d_h = e01 - e00
            d_p = e10 - e00
            inter = e11 - (e00 + d_h + d_p)
            lines += [f"### jugado {price:g} s · rastro {gate}", "",
                      f"- castigo base (10 s / 24k): **{e00:+.1f}**",
                      f"- horizonte ×2 con el precio quieto: **{d_h:+.1f}**",
                      f"- precio 10 s → 25 s con el horizonte corto: **{d_p:+.1f}**",
                      f"- celda 25 s / 48k: **{e11:+.1f}** · predicción aditiva "
                      f"{e00 + d_h + d_p:+.1f} ⇒ interacción **{inter:+.1f}**", ""]

    lines += [
        "## Cómo se lee",
        "",
        "- si el salto de horizonte es parecido a 10 s y a 25 s, el horizonte cobra un "
        "peaje que no depende del precio, y el reparto es «nivel del precio + peaje del "
        "horizonte»;",
        "- si a 25 s el horizonte no mueve nada, las dos causas no son separables: el "
        "horizonte sólo muerde donde el depósito es barato;",
        "- una interacción grande y del mismo signo que los dos saltos significa que el "
        "2×2 no se reparte en dos sumandos: hay que leerlo casilla a casilla.",
        "",
        "Estas cifras todavía NO las deriva `scripts/partida-figures.py` (cubre el eje "
        "del precio de F11h): lo que el documento afirme de ellas hay que añadirlo al "
        "derivador y al guardarraíl antes de dar la sección por comprobada.",
    ]

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
