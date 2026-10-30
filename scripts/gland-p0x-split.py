#!/usr/bin/env python3
"""gland-p0x-split.py — parte el «+47,2» en lo que pone el entrenamiento y lo
que pone el precio del rastro.

F11n midió que la receta del reflejo con el precio de 25 s le gana a la fábrica
por ~+47,2 descargas en la partida del lanzador. Pero esa cuenta compara DOS
cosas a la vez contra el campeón de laberinto de F7: 200 generaciones más de
entrenamiento sobre la partida (y con warm-start), y el precio encendido durante
esas 200 generaciones. Este script las separa con una tercera familia, `p0x`,
que es la MISMA receta de 200 generaciones con el precio APAGADO y la puerta
APAGADA, entrenada con las mismas siete semillas de GA (7, 11, 13, 23, 29, 31,
37) que las dos familias ya medidas.

Las tres familias, todas jugadas en la partida del lanzador (grid 96, laberinto,
216 000 ticks) con las 24 semillas que ninguna tirada vio:

  · `fabrica`  el pool de fábrica (`pretrain-maze-los3`), el juego de hoy
  · `p0x-s*`   reentrenada 200 generaciones SIN precio y SIN puerta
  · `p25-s*`   reentrenada 200 generaciones CON el precio de 25 s

y el reparto, leído donde se juega (glándula a 25 s, reflejo fijo):

  fabrica ──entrenamiento──▶ p0x ──precio──▶ p25

El término de precio se parea por MANO: cada `p0x-sk` contra su `p25-sk`, que
comparten la semilla de GA y por tanto el sorteo. Se informa de las dos
variabilidades que importan: la que queda al repetir la mano en las 24 semillas
de partida, y la que hay entre las siete manos (la que decide si el hallazgo se
repite con otra tirada).

Uso:
  python scripts/gland-p0x-split.py
  python scripts/gland-p0x-split.py --log artifacts/…-bench.log --out artifacts/…-split.txt
  python scripts/gland-p0x-split.py --selftest    # el analizador, con un log de mentira

Salida: 0 reparto escrito · 1 el selftest no cuadra · 2 log ausente, sin las tres
familias o sin dos manos que parear.
"""
from __future__ import annotations

import argparse
import re
import statistics
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# "  [ 97/1440] p0x-s7 · 0s · puerta                           seed    42: 181 descargas · primera 1557"
LINE = re.compile(r"\]\s+(\S+)\s+·\s+(\d+(?:\.\d+)?)s\s+·\s+(\w+)\s+seed\s+(\d+):\s+(\d+)\s+descargas")

# Dónde se juega: el lanzador usa el reflejo fijo y la glándula de 25 s.
PRICE_LIVE = 25.0
GATE_LIVE = "fijo"
PRICE_OFF = 0.0


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


def t_text(xs: list[float]) -> str:
    """«t = +2,1», o el aviso de que con σ = 0 no hay t que calcular."""
    s = se(xs)
    return f"t = {statistics.mean(xs) / s:+.1f}" if s > 0 else "σ = 0, sin t"


def partes(data) -> dict | None:
    """Las tres familias y las manos que comparten; None si el log no las trae."""
    fabrica = sorted({k[0] for k in data if k[0] == "fabrica"})
    p0x = sorted({k[0] for k in data if k[0].startswith("p0x-")})
    p25 = sorted({k[0] for k in data if k[0].startswith("p25-")})
    if not fabrica or not p0x or not p25:
        return None
    ga = lambda p: p.rsplit("-", 1)[-1]
    hands = sorted({ga(p) for p in p0x} & {ga(p) for p in p25})
    if len(hands) < 2:
        return None
    seeds = sorted(set.intersection(*(set(v) for v in data.values())))
    if not seeds:
        return None
    return {
        "fabrica": fabrica[0],
        "p0x": p0x,
        "p25": p25,
        "hands": hands,
        "p0x_by": {ga(p): p for p in p0x},
        "p25_by": {ga(p): p for p in p25},
        "seeds": seeds,
        "prices": sorted({k[1] for k in data}),
    }


def analizar(data, gland: float, baseline: float, out_name: str) -> tuple[list[str], dict]:
    """El reparto y las tablas. Devuelve (líneas, cifras) para poder probarlo."""
    p = partes(data)
    if p is None:
        raise ValueError("el log no tiene las tres familias o le faltan manos que parear")
    fabrica, p0x, p25 = p["fabrica"], p["p0x"], p["p25"]
    hands, seeds = p["hands"], p["seeds"]
    p0x_by, p25_by = p["p0x_by"], p["p25_by"]

    def cell(pool: str, price: float, gate: str) -> list[int]:
        return [data[(pool, price, gate)][s] for s in seeds]

    def fam_mean(pools: list[str], price: float, gate: str) -> list[float]:
        """Media de la familia, semilla de partida a semilla."""
        return [statistics.mean([data[(pool, price, gate)][s] for pool in pools])
                for s in seeds]

    def hand_mean(pool: str, price: float, gate: str) -> float:
        return statistics.mean(cell(pool, price, gate))

    lines = [
        "# El reparto del contrafactual (F11o): cuánto del +47,2 es entrenamiento y cuánto precio",
        f"# fuente: {out_name} · {len(seeds)} semillas de partida que ninguna tirada vio",
        f"# familias: fabrica=1 pool · p0x-s*={len(p0x)} tiradas sin precio · "
        f"p25-s*={len(p25)} tiradas con precio de 25 s",
        f"# manos (semilla de GA) compartidas por p0x y p25: {', '.join(hands)}",
        "",
        "## Las tres familias en la partida del lanzador",
        "",
        "| precio de juego | rastro | fabrica | p0x (sin precio) | p25 (con precio) |",
        "|---|---|---|---|---|",
    ]
    for price in (PRICE_OFF, gland):
        for gate in ("fijo", "puerta"):
            fa = statistics.mean(cell(fabrica, price, gate))
            a = statistics.mean(fam_mean(p0x, price, gate))
            b = statistics.mean(fam_mean(p25, price, gate))
            lines.append(f"| {price:g} s | {gate} | {fa:.1f} | {a:.1f} | {b:.1f} |")

    # --- El reparto, donde se juega: glándula 25 s y reflejo fijo. La referencia
    # del +47,2 de F11n es la fábrica CON LA GLÁNDULA APAGADA (el juego de hoy),
    # leída aquí a 25 s como todo lo demás para que el reparto sea aditivo.
    g, hoy = gland, baseline
    fa = statistics.mean(cell(fabrica, hoy, GATE_LIVE))
    fa_live = statistics.mean(cell(fabrica, g, GATE_LIVE))
    # Cada mano aporta su propio término: la familia es la media de las manos.
    train_terms = [hand_mean(pool, g, GATE_LIVE) - fa for pool in p0x]
    price_terms = [hand_mean(p25_by[h], g, GATE_LIVE) - hand_mean(p0x_by[h], g, GATE_LIVE)
                   for h in hands]
    total_terms = [hand_mean(p25_by[h], g, GATE_LIVE) - fa for h in hands]
    solo_fabrica = fa_live - fa
    tr, pr = statistics.mean(train_terms), statistics.mean(price_terms)
    tot = statistics.mean(total_terms)

    lines += ["", f"## El reparto, jugado a {g:g} s y con el reflejo fijo", "",
              f"- **fabrica, el juego de hoy** (glándula apagada): {fa:.1f} descargas "
              f"(media de las {len(seeds)} semillas)",
              f"- **la glándula sola sobre la fábrica** (apagada → {g:g} s): "
              f"{solo_fabrica:+.1f} descargas — el mismo interruptor de F11n",
              f"- **entrenamiento** (fabrica → p0x, sin precio ni en entrenamiento ni al "
              f"jugar): {tr:+.1f} ± {se(train_terms):.1f} descargas "
              f"(rango entre tiradas {min(train_terms):+.0f}…{max(train_terms):+.0f}; "
              f"{sum(1 for v in train_terms if v > 0)}/{len(train_terms)} tiradas a favor)",
              f"- **precio** (p0x → p25, pareado por mano, jugado a {g:g} s los dos): "
              f"{pr:+.1f} ± {se(price_terms):.1f} descargas "
              f"(rango entre tiradas {min(price_terms):+.0f}…{max(price_terms):+.0f}; "
              f"{sum(1 for v in price_terms if v > 0)}/{len(price_terms)} tiradas a favor)",
              f"- **total** (fabrica → p25): {tot:+.1f} ± {se(total_terms):.1f} descargas"]
    if abs(tot) > 1e-9:
        lines.append(f"  - de ese total, el entrenamiento pone {tr / tot:.0%} y el precio "
                     f"{pr / tot:.0%}")
    lines.append(f"  - la cuenta cuadra sin residuo: {tr:+.1f} + {pr:+.1f} = {tr + pr:+.1f} "
                 f"(el total medido es {tot:+.1f})")

    # --- El término de precio con su otra variabilidad: repetir la mano en las 24 semillas.
    per_seed = []
    for s in seeds:
        d = [data[(p25_by[h], g, GATE_LIVE)][s] - data[(p0x_by[h], g, GATE_LIVE)][s]
             for h in hands]
        per_seed.append(statistics.mean(d))
    lines.append(f"  - pareado por semilla de partida (media de las {len(hands)} manos): "
                 f"{statistics.mean(per_seed):+.1f} ± {se(per_seed):.1f} descargas "
                 f"(gana en {sum(1 for v in per_seed if v > 0)}/{len(per_seed)} semillas)")

    # --- Contrapeso: el precio entrenado, jugado SIN precio. Si ahí no cuesta
    # nada, el término de precio es un beneficio condicional y no un sesgo.
    off = [hand_mean(p25_by[h], PRICE_OFF, GATE_LIVE) - hand_mean(p0x_by[h], PRICE_OFF, GATE_LIVE)
           for h in hands]
    media_off = statistics.mean(off)
    lines += ["", f"## El mismo pareado jugado SIN precio (glándula a {PRICE_OFF:g} s)", "",
              f"- **p25 − p0x**: {media_off:+.1f} ± {se(off):.1f} descargas "
              f"({t_text(off)}; "
              f"{sum(1 for v in off if v > 0)}/{len(off)} tiradas a favor del pool entrenado "
              f"con precio)",
              "  - si fuera ≈ 0, entrenar con el precio no costaría nada cuando el juego no lo "
              "cobra; si es negativo, el pool aprendió a depender de un rastro que el juego de "
              "fábrica nunca produce"]

    # --- Por mano, para que se vea si el signo depende de una tirada.
    lines += ["", "| mano (GA) | p0x a 0 s | p25 a 0 s | sin precio | p0x a 25 s | p25 a 25 s | precio |",
              "|---|---|---|---|---|---|---|"]
    for h in hands:
        a0, b0 = hand_mean(p0x_by[h], PRICE_OFF, GATE_LIVE), hand_mean(p25_by[h], PRICE_OFF, GATE_LIVE)
        a, b = hand_mean(p0x_by[h], g, GATE_LIVE), hand_mean(p25_by[h], g, GATE_LIVE)
        lines.append(f"| {h} | {a0:.1f} | {b0:.1f} | {b0 - a0:+.1f} | {a:.1f} | {b:.1f} | "
                     f"{b - a:+.1f} |")
    lines.append(f"| **media de las {len(hands)}** | "
                 f"{statistics.mean([hand_mean(pool, PRICE_OFF, GATE_LIVE) for pool in p0x]):.1f} | "
                 f"{statistics.mean([hand_mean(p25_by[h], PRICE_OFF, GATE_LIVE) for h in hands]):.1f} | "
                 f"{media_off:+.1f} | "
                 f"{statistics.mean([hand_mean(pool, g, GATE_LIVE) for pool in p0x]):.1f} | "
                 f"{statistics.mean([hand_mean(p25_by[h], g, GATE_LIVE) for h in hands]):.1f} | "
                 f"{pr:+.1f} |")

    cifras = {
        "fabrica": fa, "fabrica_viva": fa_live, "glándula sola": solo_fabrica,
        "entrenamiento": tr, "precio": pr, "total": tot,
        "sin precio": media_off,
        "entrenamiento se": se(train_terms), "precio se": se(price_terms),
        "total se": se(total_terms), "sin precio se": se(off),
        "manos a favor entrenamiento": sum(1 for v in train_terms if v > 0),
        "manos a favor precio": sum(1 for v in price_terms if v > 0),
        "manos": len(hands),
        "semillas a favor precio": sum(1 for v in per_seed if v > 0),
        "semillas": len(seeds),
    }
    return lines, cifras


def selftest() -> int:
    """El analizador con un log de mentira: aritmética exacta y fail-closed.

    No lee `artifacts/`: las cifras se eligen para que la cuenta salga a mano
    (entrenamiento +40, precio +5, total +45) y así una resta mal puesta no pueda
    pasar por casualidad.
    """
    fallo = 0

    def decir(ok: bool, texto: str) -> None:
        nonlocal fallo
        print(("  ok " if ok else "  X ") + texto)
        if not ok:
            fallo = 1

    def linea(pool: str, price: float, gate: str, seed: int, unloads: int) -> str:
        return (f"  [  1/24] {pool} · {price:g}s · {gate}"
                f"{' ' * 20}seed {seed:5d}: {unloads:3d} descargas · primera 1200")

    filas: list[str] = []
    for seed in (1, 2, 3):
        for gate in ("fijo", "puerta"):
            # Fábrica: 10 en las dos lecturas → la glándula sola no aporta nada.
            for price in (PRICE_OFF, PRICE_LIVE):
                filas.append(linea("fabrica", price, gate, seed, 10))
            # p0x a 50 y p25 a 55 jugando; sin precio, p25 se cae a 40.
            filas.append(linea("p0x-s1", price := PRICE_LIVE, gate, seed, 50))
            filas.append(linea("p0x-s2", price, gate, seed, 50))
            filas.append(linea("p25-s1", price, gate, seed, 55))
            filas.append(linea("p25-s2", price, gate, seed, 55))
            filas.append(linea("p0x-s1", PRICE_OFF, gate, seed, 50))
            filas.append(linea("p0x-s2", PRICE_OFF, gate, seed, 50))
            filas.append(linea("p25-s1", PRICE_OFF, gate, seed, 40))
            filas.append(linea("p25-s2", PRICE_OFF, gate, seed, 40))

    print("gland-p0x-split --selftest (log de mentira, 2 manos × 3 semillas)")
    with tempfile.TemporaryDirectory() as tmp:
        log = Path(tmp) / "bench.log"
        log.write_text("\n".join(filas) + "\n", encoding="utf-8")
        data = parse(log)
        decir(len(data) == 20,
              f"el parser ve {len(data)} celdas (pool, precio, rastro), no las 24 líneas")
        lines, c = analizar(data, PRICE_LIVE, PRICE_OFF, str(log))
        decir(not str(log.resolve()).startswith(str(ROOT.resolve())),
              "el log de mentira vive fuera del repo: el selftest no lee artifacts/")

    decir(c["fabrica"] == 10.0, "la fábrica con la glándula apagada sale 10,0")
    decir(c["glándula sola"] == 0.0, "la glándula sola sobre la fábrica sale +0,0")
    decir(c["entrenamiento"] == 40.0, "el entrenamiento sale +40,0 (fabrica 10 → p0x 50)")
    decir(c["precio"] == 5.0, "el precio sale +5,0 pareando mano con mano (p0x 50 → p25 55)")
    decir(c["total"] == 45.0, "el total sale +45,0 (fabrica 10 → p25 55)")
    decir(c["entrenamiento"] + c["precio"] == c["total"],
          "el reparto es aditivo: entrenamiento + precio = total")
    decir(c["sin precio"] == -10.0,
          "jugado sin precio el pool entrenado con precio pierde -10,0 (55 → 40 al revés)")
    decir(c["manos a favor entrenamiento"] == 2 and c["manos"] == 2,
          "las dos manos cuentan a favor del entrenamiento")
    decir(any("entrenamiento pone 89% y el precio 11%" in ln for ln in lines),
          "el documento recibe el reparto en porcentaje (40/45 y 5/45)")

    # Fail-closed: cada ausencia tiene que cortar, no inventar un reparto.
    with tempfile.TemporaryDirectory() as tmp:
        vacio = Path(tmp) / "vacio.log"
        vacio.write_text("", encoding="utf-8")
        decir(partes(parse(vacio)) is None, "un log sin líneas corta")

        solo_p0x = Path(tmp) / "sin-p25.log"
        solo_p0x.write_text(linea("p0x-s1", PRICE_LIVE, "fijo", 1, 50), encoding="utf-8")
        decir(partes(parse(solo_p0x)) is None, "un log sin las tres familias corta")

        una_mano = Path(tmp) / "una-mano.log"
        una_mano.write_text("\n".join([
            linea("fabrica", PRICE_LIVE, "fijo", 1, 10),
            linea("p0x-s1", PRICE_LIVE, "fijo", 1, 50),
            linea("p25-s1", PRICE_LIVE, "fijo", 1, 55),
        ]) + "\n", encoding="utf-8")
        decir(partes(parse(una_mano)) is None, "con una sola mano no hay pareo que hacer")

        try:
            analizar(parse(una_mano), PRICE_LIVE, PRICE_OFF, "x")
            decir(False, "analizar lanza con una sola mano")
        except ValueError:
            decir(True, "analizar lanza con una sola mano")

    print(f"gland-p0x-split --selftest: {'ok' if fallo == 0 else 'FALLA'}")
    return fallo


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--log", default="artifacts/pretrain-partida-200-p0x-bench.log")
    ap.add_argument("--out", default="artifacts/pretrain-partida-200-p0x-split.txt")
    ap.add_argument("--gland", type=float, default=25.0,
                    help="precio al que se lee el reparto (por defecto 25 s)")
    ap.add_argument("--baseline", type=float, default=0.0,
                    help="precio de juego de la línea base 'hoy' (por defecto 0 s: la "
                         "fábrica con la glándula apagada, que es contra quien se midió el +47,2)")
    ap.add_argument("--selftest", action="store_true",
                    help="el analizador, con un log de mentira (no lee artifacts/)")
    args = ap.parse_args(argv)

    if args.selftest:
        return selftest()

    log = ROOT / args.log
    if not log.is_file():
        print(f"no existe el log {args.log}", file=sys.stderr)
        return 2
    try:
        lines, _cifras = analizar(parse(log), args.gland, args.baseline, args.log)
    except ValueError as exc:
        print(f"{exc}: {args.log}", file=sys.stderr)
        return 2

    text = "\n".join(lines) + "\n"
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    print(text)
    print(f"evidencia → {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
