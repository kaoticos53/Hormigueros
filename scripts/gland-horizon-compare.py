#!/usr/bin/env python3
"""gland-horizon-compare.py — ¿cuánto del castigo de la puerta a 10 s viene del
PRECIO de la glándula y cuánto del HORIZONTE de selección?

LA PREGUNTA. A la MISMA receta y al MISMO precio de entrenamiento, entrenar con
la puerta del cerebro cuesta descargas a 10 s de glándula y las gana a 25 s
(F11g/F11i). En el brazo de 10 s van juntas dos cosas que los pools ya
entrenados no pueden separar, porque un pool es lo que es:

  (P) el PRECIO: a 10 s el depósito cuesta cuatro veces menos por evento, así
      que la puerta decide casi gratis y el fitness —que sólo cuenta
      descargas— no nota la diferencia;
  (H) el HORIZONTE: la selección juzga 24 000 ticks (400 s) de una partida de
      216 000 (3 600 s), y la ventaja de reclutar bien se cobra más tarde, así
      que puede caer fuera de la ventana que la selección ve.

El control de F11j sube SÓLO el horizonte (48 000 ticks, el doble) a 10 s. Este
comparador lee las dos campañas de réplicas y responde cuánto cambia el efecto
de la puerta al hacerlo.

QUÉ COMPARA. Dos horizontes × las MISMAS semillas de GA × las mismas 24
semillas de partida. Por cada horizonte, el efecto es el brazo entrenado con la
puerta menos el brazo entrenado con el reflejo, emparejado POR TIRADA DE GA (el
mismo sorteo en las dos recetas, que es lo que el eje del precio viene
pareando desde F11h). Luego compara ese efecto entre horizontes, pareado por
sorteo: es la cifra que dice si el horizonte recupera el castigo.

LO QUE NO COMPARA, y es a propósito: el NIVEL de descargas entre horizontes no
es comparable —a 48 000 la generación 1 rinde ~102 descargas donde a 24 000
daba ~68, porque la partida es el doble de larga—. Lo comparable es el efecto
de la puerta DENTRO de cada horizonte, no la suma de descargas de uno con la
del otro. Tampoco dice nada de horizontes mayores: 2× es una manipulación, no
el horizonte de la partida (que es 9×).

Uso:
  python scripts/gland-horizon-compare.py \\
      --arm 24k=puerta:artifacts/pretrain-partida-200-p10g7-replicas.log \\
      --arm 24k=reflejo:artifacts/pretrain-partida-200-p10x10-replicas.log \\
      --arm 48k=puerta:artifacts/pretrain-partida-200-p10h2g-replicas.log \\
      --arm 48k=reflejo:artifacts/pretrain-partida-200-p10h2s-replicas.log \\
      --out artifacts/pretrain-partida-200-p10h2-veredicto.txt

Salida: 0 veredicto escrito · 2 falta un log, un brazo o un sorteo de GA común
(no se rellena el hueco: un veredicto sobre datos a medias es peor que ninguno).
"""
from __future__ import annotations

import argparse
import re
import statistics
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# "  [  1/672] p10g-s7 · 10s · puerta                         seed    42:  99 descargas · primera 1370"
LINE = re.compile(r"\]\s+(\S+)\s+·\s+(\d+(?:\.\d+)?)s\s+·\s+(\w+)\s+seed\s+(\d+):\s+(\d+)\s+descargas")

RECIPES = ("puerta", "reflejo")


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


def normaliza_sorteo(token: str) -> str:
    """«41» y «s41» son el mismo sorteo: el sufijo de la etiqueta del pool.

    Los pools se etiquetan `p10g-s41` (entrenado con la puerta) y `p10-s41`
    (con el reflejo), así que el sorteo de GA es lo que va tras el último
    guion. Se acepta la forma corta porque escribir --ga-seeds 41,43,47 es lo
    natural, y la larga por si alguien copia el sufijo del volcado.
    """
    token = token.strip()
    return token if token.startswith("s") and token[1:].isdigit() else f"s{token}"


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--arm", action="append", required=True, metavar="HORIZONTE=BRAZO:LOG",
                    help="campaña de réplicas de un brazo en un horizonte; "
                         "hace falta puerta y reflejo para cada horizonte")
    ap.add_argument("--ga-seeds", default=None,
                    help="sorteos de GA a comparar (por defecto, los que comparten "
                         "los cuatro brazos)")
    ap.add_argument("--out", default=None)
    args = ap.parse_args(argv)

    arms: dict[tuple[str, str], dict] = {}
    logs: dict[tuple[str, str], str] = {}
    horizons: list[str] = []
    for spec in args.arm:
        if "=" not in spec:
            print(f"--arm espera HORIZONTE=BRAZO:LOG, no '{spec}'", file=sys.stderr)
            return 2
        horizon, rest = spec.split("=", 1)
        if ":" not in rest:
            print(f"--arm espera HORIZONTE=BRAZO:LOG, no '{spec}'", file=sys.stderr)
            return 2
        recipe, log = (s.strip() for s in rest.split(":", 1))
        horizon = horizon.strip()
        if recipe not in RECIPES:
            print(f"brazo desconocido '{recipe}' (puerta|reflejo)", file=sys.stderr)
            return 2
        if (horizon, recipe) in arms:
            print(f"el horizonte {horizon} tiene dos brazos «{recipe}»", file=sys.stderr)
            return 2
        if not (ROOT / log).is_file():
            print(f"no existe el log {log}", file=sys.stderr)
            return 2
        data = parse(ROOT / log)
        if not data:
            print(f"el log {log} no tiene líneas de resultados del modo réplicas", file=sys.stderr)
            return 2
        arms[(horizon, recipe)] = data
        logs[(horizon, recipe)] = log
        if horizon not in horizons:
            horizons.append(horizon)

    for horizon in horizons:
        missing = [r for r in RECIPES if (horizon, r) not in arms]
        if missing:
            print(f"al horizonte {horizon} le falta el brazo {', '.join(missing)}: "
                  "sin las dos recetas no hay efecto que medir", file=sys.stderr)
            return 2
    if len(horizons) < 2:
        print("hace falta más de un horizonte para comparar (--arm dos veces con el "
              "mismo brazo y horizontes distintos)", file=sys.stderr)
        return 2

    # ── Qué se puede comparar: precios y sorteos comunes a los cuatro brazos ──
    each = [d for d in arms.values()]
    prices = sorted(set.intersection(*(set(k[1] for k in d) for d in each)))
    gates = [g for g in ("fijo", "puerta")
             if all(g in {k[2] for k in d} for d in each)]
    if not prices:
        print("los brazos no comparten ningún precio de juego", file=sys.stderr)
        return 2
    if not gates:
        print("los brazos no comparten forma de decidir el depósito (fijo|puerta)",
              file=sys.stderr)
        return 2

    # Etiqueta de pool por sorteo de GA, en cada brazo. Un sorteo con dos pools
    # en el mismo brazo es un volcado mal armado: se declara, no se promedia.
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

    common = set.intersection(*(set(t) for t in by_suffix.values()))
    if args.ga_seeds:
        wanted = [normaliza_sorteo(s) for s in args.ga_seeds.split(",") if s.strip()]
        faltan = [s for s in wanted if s not in common]
        if faltan:
            print(f"los sorteos {', '.join(faltan)} no están en los cuatro brazos; "
                  f"comunes: {', '.join(sorted(common))}", file=sys.stderr)
            return 2
        seeds = wanted
    else:
        seeds = sorted(common)

    def seed_order(suffix: str) -> tuple[int, str]:
        num = suffix[1:]
        return (int(num), suffix) if num.isdigit() else (10 ** 9, suffix)

    seeds = sorted(seeds, key=seed_order)
    if len(seeds) < 2:
        print("hace falta más de un sorteo de GA común para dar error típico: "
              f"comunes {', '.join(seeds) or 'ninguno'}", file=sys.stderr)
        return 2

    def pool_mean(key: tuple[str, str], suffix: str, price: float, gate: str) -> float:
        data = arms[key]
        return statistics.mean(data[(by_suffix[key][suffix], price, gate)].values())

    lines = [
        "# El horizonte de selección contra el precio de la glándula",
        "# brazos: " + " · ".join(f"{h} {r} ({logs[(h, r)]})" for h in horizons for r in RECIPES),
        f"# {len(seeds)} sorteos de GA pareados por receta ({', '.join(seeds)}) · "
        f"precios de juego {', '.join(f'{p:g} s' for p in prices)} · "
        f"rastro {' y '.join(gates)}",
        "# El NIVEL no es comparable entre horizontes (la partida a 48k es el doble de "
        "larga): se compara el EFECTO de la puerta dentro de cada horizonte, no la suma.",
        "",
    ]

    # ── El efecto de la puerta, horizonte a horizonte ─────────────────────────
    effect: dict[tuple[str, float, str], list[float]] = {}
    for price in prices:
        for gate in gates:
            lines += [f"## Jugado con la glándula a {price:g} s · rastro {gate}", "",
                      "| horizonte | brazo con puerta | brazo con reflejo | efecto |",
                      "|---|---|---|---|"]
            for horizon in horizons:
                gp = [pool_mean((horizon, "puerta"), s, price, gate) for s in seeds]
                gf = [pool_mean((horizon, "reflejo"), s, price, gate) for s in seeds]
                d = [a - b for a, b in zip(gp, gf)]
                effect[(horizon, price, gate)] = d
                lines.append(f"| {horizon} | {statistics.mean(gp):.1f} | "
                             f"{statistics.mean(gf):.1f} | {statistics.mean(d):+.1f} |")
            lines.append("")

            for horizon in horizons:
                d = effect[(horizon, price, gate)]
                m, e = statistics.mean(d), se(d)
                lines.append(f"- **{horizon} · jugado {price:g} s · {gate}**: entrenar con la "
                             f"puerta da {m:+.1f} ± {e:.1f} descargas (t = {tstat(m, e)}; "
                             f"{sum(1 for v in d if v > 0)}/{len(d)} sorteos a favor)")
                lines.append("  - por sorteo: " + " · ".join(
                    f"{s} {a:.1f} vs {b:.1f} = {x:+.1f}" for s, a, b, x in
                    zip(seeds,
                        [pool_mean((horizon, "puerta"), s, price, gate) for s in seeds],
                        [pool_mean((horizon, "reflejo"), s, price, gate) for s in seeds],
                        d)))
            lines.append("")

    # ── Cuánto cambia ese efecto al alargar el horizonte ─────────────────────
    base = horizons[0]
    lines += ["## Cuánto mueve el horizonte el efecto de la puerta", "",
              f"Base: {base} (24 000 ticks). Delta = efecto en el horizonte mayor − efecto "
              f"en {base}, pareado por sorteo de GA. Positivo = el horizonte recupera parte "
              "del castigo; ≈0 = no lo recupera.", ""]
    for price in prices:
        for gate in gates:
            lines.append(f"### jugado {price:g} s · rastro {gate}")
            for horizon in horizons[1:]:
                d = [x - y for x, y in zip(effect[(horizon, price, gate)],
                                           effect[(base, price, gate)])]
                m, e = statistics.mean(d), se(d)
                lines.append(
                    f"- **{horizon} − {base}**: el efecto pasa de "
                    f"{statistics.mean(effect[(base, price, gate)]):+.1f} a "
                    f"{statistics.mean(effect[(horizon, price, gate)]):+.1f} → "
                    f"{m:+.1f} ± {e:.1f} descargas (t = {tstat(m, e)}; "
                    f"{sum(1 for v in d if v > 0)}/{len(d)} sorteos a favor)")
                lines.append("  - por sorteo: " + " · ".join(
                    f"{s} {a:+.1f} → {b:+.1f} = {x:+.1f}" for s, a, b, x in zip(
                        seeds, effect[(base, price, gate)], effect[(horizon, price, gate)], d)))
            lines.append("")

    lines += [
        "## Cómo se lee",
        "",
        "El castigo a 10 s que dejó medido F11i se reparte entre el precio y el horizonte "
        "según cuánto se mueva el efecto al doblar el horizonte, con el precio quieto:",
        "",
        "- si el delta es ≈ 0, el horizonte no explica el castigo y la causa es el precio;",
        "- si el delta lleva el efecto de un castigo a ≈ 0 o a positivo, el horizonte "
        "explica buena parte —la ventaja de reclutar bien se cobraba fuera de la ventana "
        "que la selección miraba—;",
        "- un delta parcial deja las dos causas repartiéndose el castigo, y entonces lo que "
        "hace falta es el 2×2 completo (también 25 s a 48 000 ticks), que este control no "
        "corre.",
        "",
        "Estas cifras todavía NO las deriva `scripts/partida-figures.py` (cubre el eje del "
        "precio de F11h, no este control): lo que el documento afirme de ellas hay que "
        "añadirlo al derivador y al guardarraíl antes de dar la sección por comprobada.",
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
