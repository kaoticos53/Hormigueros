#!/usr/bin/env python3
"""gland-launcher-duel.py — el enfrentamiento que F11n dejó pendiente.

LA PREGUNTA. F11n decidió NO exponer en el lanzador el par «puerta del cerebro +
glándula de 25 s» porque, aunque el par paga dentro de su propia familia, nunca se
había medido contra el pool que el juego carga de fábrica (`pretrain-maze-los3`,
el campeón de laberinto de F7). Esto lo mide: la partida del lanzador —grid 96,
laberinto, reclutamiento, 216 000 ticks— con las 24 semillas de partida que
ninguna tirada vio.

LAS ARMAS. Dos bloques, y el segundo replicado:

  · la FÁBRICA: el pool de fábrica con el reflejo y la glándula apagada, que es
    el juego de hoy; y las tres variantes que separan piezas (la puerta sola sin
    precio, el precio solo, y el par sobre ese mismo pool).
  · el PAR candidato: los SIETE pools entrenados a 25 s con la puerta, jugados
    con la puerta y con el precio de 25 s; y los siete SIN la puerta (la receta
    del reflejo replicada) para que la comparación no confunda receta con sorteo.
    Cada uno de los catorce se juega además con la otra forma de decidir, que es
    lo que dice cuánto pone la puerta dentro de su pool.

POR QUÉ NO VALE CON OCHO SEMILLAS. Con 8 el subconjunto sale sesgado —en F11c el
pool del reflejo a 10 s daba 212 con 8 y 189 con 24—, así que el criterio de
F11n (≥16/24 y margen por encima de la dispersión) se juzga con 24 y no menos.

EL CRITERIO, escrito en el documento antes de medir: el par se adopta si le gana
al juego de hoy en ≥16 de las 24 semillas y su margen supera la dispersión entre
tiradas de la receta (la sd de las medias por pool, que es lo que mueve reentrenar).

Uso:
  python scripts/gland-launcher-duel.py --seeds 42            # pasada corta
  python scripts/gland-launcher-duel.py --jobs 12             # las 24 semillas
  python scripts/gland-launcher-duel.py --no-build --jobs 12  # sin recompilar

Salida: 0 reparto escrito · 2 falta un pool, una etiqueta o el CLI.
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
TICKS = 216000
REFILL = 6.0

# El pool que el lanzador carga por defecto (LaunchSettings.Pool).
FABRICA = "artifacts/pretrain-maze-los3.antgenome"

# Las DOS recetas de 25 s, siete tiradas de GA cada una. La semilla 7 de cada
# receta se entrenó con el sufijo «gl» (F11b) y las otras seis con «p25g»/«p25s».
RECETA_PUERTA = {
    7: "artifacts/pretrain-partida-200-gl-red.antgenome",
    11: "artifacts/pretrain-partida-200-p25g11-red.antgenome",
    13: "artifacts/pretrain-partida-200-p25g13-red.antgenome",
    23: "artifacts/pretrain-partida-200-p25g23-red.antgenome",
    29: "artifacts/pretrain-partida-200-p25g29-red.antgenome",
    31: "artifacts/pretrain-partida-200-p25g31-red.antgenome",
    37: "artifacts/pretrain-partida-200-p25g37-red.antgenome",
}
RECETA_REFLEJO = {
    7: "artifacts/pretrain-partida-200-gl-fijo.antgenome",
    11: "artifacts/pretrain-partida-200-p25s11-fijo.antgenome",
    13: "artifacts/pretrain-partida-200-p25s13-fijo.antgenome",
    23: "artifacts/pretrain-partida-200-p25s23-fijo.antgenome",
    29: "artifacts/pretrain-partida-200-p25s29-fijo.antgenome",
    31: "artifacts/pretrain-partida-200-p25s31-fijo.antgenome",
    37: "artifacts/pretrain-partida-200-p25s37-fijo.antgenome",
}

# (etiqueta, pool, puerta del cerebro, glándula en segundos)
ARMS = (
    [("fabrica·reflejo·0s", FABRICA, False, 0.0),
     ("fabrica·puerta·0s", FABRICA, True, 0.0),
     ("fabrica·reflejo·25s", FABRICA, False, 25.0),
     ("fabrica·puerta·25s", FABRICA, True, 25.0)]
    + [(f"puerta-s{k}·puerta·25s", p, True, 25.0) for k, p in RECETA_PUERTA.items()]
    + [(f"puerta-s{k}·reflejo·25s", p, False, 25.0) for k, p in RECETA_PUERTA.items()]
    + [(f"reflejo-s{k}·reflejo·25s", p, False, 25.0) for k, p in RECETA_REFLEJO.items()]
    + [(f"reflejo-s{k}·puerta·25s", p, True, 25.0) for k, p in RECETA_REFLEJO.items()]
)


def run(seed: int, pool: Path, gate: bool, gland: float) -> dict:
    cmd = [
        "dotnet", str(DLL), "--mode", "game", "--seed", str(seed),
        "--grid", "96", "--ticks", str(TICKS), "--frame-every", "120",
        "--maze", "--recruitment", "--seed-pool", str(pool),
        "--trail-gland", f"{gland:g}", "--trail-gland-refill", f"{REFILL:g}",
    ]
    if gate:
        cmd.append("--trail-by-brain")

    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:400]}")

    unloads = first = end_tick = None
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
            firsts = [r["firstUnload"] for r in relays if r.get("firstUnload")]
            first = min(firsts) if firsts else None
    return {"seed": seed, "unloads": unloads or 0, "firstUnload": first, "endTick": end_tick}


def mean(xs) -> float:
    return statistics.mean(xs)


def sd(xs) -> float:
    return statistics.stdev(xs) if len(xs) > 1 else 0.0


def se(xs) -> float:
    return sd(xs) / len(xs) ** 0.5 if len(xs) > 1 else 0.0


def build_cli() -> bool:
    build = subprocess.run(["dotnet", "build", "src/Tools/AntSim.Cli", "-c", "Release",
                            "-v", "q", "--nologo"], cwd=ROOT, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
    return build.returncode == 0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seeds", default=SEEDS)
    ap.add_argument("--jobs", type=int, default=1)
    ap.add_argument("--no-build", action="store_true",
                    help="no recompilar: usar el antsim.dll que ya está en bin/Release")
    ap.add_argument("--out", default="artifacts/pretrain-partida-200-f11n-duel.txt")
    args = ap.parse_args(argv)

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    if not seeds:
        print("--seeds no puede quedar vacío", file=sys.stderr)
        return 2
    faltan = [p for _l, p, _g, _d in ARMS if not (ROOT / p).is_file()]
    if faltan:
        print("faltan pools: " + ", ".join(faltan), file=sys.stderr)
        return 2
    if not args.no_build and not build_cli():
        return 2

    jobs = [(label, pool, gate, gland, seed)
            for label, pool, gate, gland in ARMS for seed in seeds]
    results: dict[tuple[str, int], dict] = {}
    t_all = time.time()

    def work(job):
        label, pool, gate, gland, seed = job
        return job, run(seed, ROOT / pool, gate, gland)

    done = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for (label, _p, _g, _d, seed), r in ex.map(work, jobs):
            results[(label, seed)] = r
            done += 1
            print(f"  [{done:3d}/{len(jobs)}] {label:26s} seed {r['seed']:5d}: "
                  f"{r['unloads']:3d} descargas · fin {r['endTick']}", flush=True)
    print(f"  ({time.time() - t_all:.0f} s)", flush=True)

    def arm(label: str) -> list[int]:
        return [results[(label, s)]["unloads"] for s in seeds]

    def media_receta(prefijo: str, cola: str) -> list[float]:
        """Media por semilla de las SIETE tiradas de una receta, jugadas de una forma."""
        return [mean([results[(f"{prefijo}-s{k}·{cola}", s)]["unloads"]
                      for k in (RECETA_PUERTA if prefijo == "puerta" else RECETA_REFLEJO)])
                for s in seeds]

    def pareado(a: list[float], b: list[float]) -> tuple[float, float, int, int, int]:
        d = [x - y for x, y in zip(a, b)]
        t = mean(d) / se(d) if se(d) > 0 else 0.0
        return mean(d), se(d), sum(1 for v in d if v > 0), len(d), t

    hoy = arm("fabrica·reflejo·0s")
    par = media_receta("puerta", "puerta·25s")
    receta_ref = media_receta("reflejo", "reflejo·25s")

    m_par, se_par, w_par, n_par, t_par = pareado(par, hoy)
    m_ref, se_ref, w_ref, n_ref, t_ref = pareado(receta_ref, hoy)
    m_rec, se_rec, w_rec, n_rec, t_rec = pareado(par, receta_ref)

    dentro = [pareado(arm(f"puerta-s{k}·puerta·25s"), arm(f"puerta-s{k}·reflejo·25s"))
              for k in RECETA_PUERTA]
    dentro_m = [d[0] for d in dentro]
    dentro_agg = [mean([results[(f"puerta-s{k}·puerta·25s", s)]["unloads"] for k in RECETA_PUERTA])
                  - mean([results[(f"puerta-s{k}·reflejo·25s", s)]["unloads"] for k in RECETA_PUERTA])
                  for s in seeds]

    dispersion = sd([mean(arm(f"puerta-s{k}·puerta·25s")) for k in RECETA_PUERTA])
    dispersion_ref = sd([mean(arm(f"reflejo-s{k}·reflejo·25s")) for k in RECETA_REFLEJO])

    adopta = w_par >= 16 and abs(m_par) > dispersion

    lines = [
        "# F11n — el enfrentamiento que faltaba: el par a 25 s contra el juego de fábrica",
        f"# la partida del lanzador: grid 96, laberinto, reclutamiento, {TICKS} ticks; "
        f"{len(seeds)} semillas de partida",
        f"# semillas: {args.seeds}",
        "# el par candidato promedia las SIETE tiradas de cada receta por semilla de partida, "
        "así que el pareado es (receta − juego de hoy) y no (sorteo afortunado − juego de hoy)",
        "",
        "| brazo | pool | puerta | glándula | descargas (media ± σ) | total | semillas |",
        "|---|---|---|---|---|---|---|",
    ]
    for label, pool, gate, gland in ARMS:
        un = arm(label)
        lines.append(f"| {label} | {Path(pool).name} | {'sí' if gate else 'no'} | "
                     f"{gland:g} s | {mean(un):.1f} ± {sd(un):.1f} | {sum(un)} | "
                     f"{sum(1 for u in un if u > 0)}/{len(un)} |")

    lines += [
        "",
        "## Los tres pareados que deciden (por semilla de partida)",
        "",
        f"- **el par candidato − el juego de hoy**: {m_par:+.1f} ± {se_par:.1f} descargas "
        f"(t = {t_par:+.1f}; gana en {w_par}/{n_par} semillas)",
        f"- **la receta del reflejo a 25 s − el juego de hoy**: {m_ref:+.1f} ± {se_ref:.1f} "
        f"(t = {t_ref:+.1f}; gana en {w_ref}/{n_ref} semillas)",
        f"- **el par candidato − la receta del reflejo a 25 s**: {m_rec:+.1f} ± {se_rec:.1f} "
        f"(t = {t_rec:+.1f}; gana en {w_rec}/{n_rec} semillas; las dos recetas con siete tiradas)",
        "",
        "## Lo que pone cada pieza",
        "",
        f"- **la puerta dentro de su pool a 25 s**: {mean(dentro_agg):+.1f} ± {se(dentro_agg):.1f} "
        f"descargas al promediar las siete tiradas "
        f"(rango por tirada {min(dentro_m):+.1f} a {max(dentro_m):+.1f})",
    ]
    for nombre, a, b in (
        ("la puerta sola, con la glándula apagada (fábrica)", "fabrica·puerta·0s", "fabrica·reflejo·0s"),
        ("el precio solo, con el reflejo (fábrica)", "fabrica·reflejo·25s", "fabrica·reflejo·0s"),
        ("el par sobre el pool de fábrica", "fabrica·puerta·25s", "fabrica·reflejo·0s"),
    ):
        m, s, w, n, t = pareado(arm(a), arm(b))
        lines.append(f"- **{nombre}**: {m:+.1f} ± {s:.1f} descargas (t = {t:+.1f}; {w}/{n} semillas)")

    lines += [
        "",
        "## El criterio de F11n, aplicado",
        "",
        f"- semillas a favor del par: **{w_par}/{n_par}** (el criterio pide ≥16/24)",
        f"- margen del par: **{m_par:+.1f}** descargas; dispersión entre tiradas de su receta: "
        f"**{dispersion:.1f}** (el criterio pide superarla)",
        f"- dispersión entre tiradas de la receta del reflejo: **{dispersion_ref:.1f}**",
        f"- **veredicto: {'SE ADOPTA' if adopta else 'NO SE ADOPTA'}** "
        f"({'las dos' if w_par >= 16 else 'no llega'} a 16/24"
        f"{'' if abs(m_par) > dispersion else ' y el margen no supera la dispersión'})",
        "",
    ]

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
