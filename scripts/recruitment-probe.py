#!/usr/bin/env python3
"""recruitment-probe.py — la partida con reclutamiento contra el mundo clásico.

Mide la señal que el pase pide: DESCARGAS al nido en la partida canónica
(grid 96, laberinto, pool `pretrain-maze-los3`) para varias semillas, con
`--recruitment` y sin él (mundo clásico). Además del marcador, imprime lo que
explica el marcador —población, cría, entradas, fin de ronda— porque el
objetivo no es «tocar números hasta que gane» sino saber QUÉ limita el
forrajeo con el rastro encendido.

Todo es determinista: mismas semillas ⇒ mismos números.

Uso:
  python scripts/recruitment-probe.py                      # tabla canónica
  python scripts/recruitment-probe.py --ticks 72000        # pasada corta
  python scripts/recruitment-probe.py --seeds 42,7 --trace  # demografía por tramos
"""
from __future__ import annotations

import argparse
import json
import statistics
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CLI_PROJECT = ROOT / "src" / "Tools" / "AntSim.Cli"
DEFAULT_POOL = "artifacts/pretrain-maze-los3.antgenome"


def run_game(seed: int, ticks: int, pool: str | None, recruitment: bool,
             until_extinct: bool, frame_every: int = 120) -> dict:
    cmd = [
        "dotnet", "run", "--project", str(CLI_PROJECT), "-c", "Release", "--no-build", "--",
        "--mode", "game", "--seed", str(seed), "--grid", "96", "--ticks", str(ticks),
        "--frame-every", str(frame_every), "--maze",
    ]
    if pool:
        cmd += ["--seed-pool", str(ROOT / pool)]
    if recruitment:
        cmd.append("--recruitment")
    if until_extinct:
        cmd.append("--until-extinct")

    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"el CLI falló ({proc.returncode}): {proc.stderr.strip()[:400]}")

    unloads = 0
    col0 = 0
    first = None
    pickups = eggs = eclosed = deaths = 0
    peak_adults = 0
    peak_items = 0
    last_adults = 0
    last_stock = 0.0
    end_tick = None
    extinct = False
    trace: list[dict] = []

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
            extinct = bool(obj.get("extinct"))
            continue
        if "relays" in obj:
            for v in obj["relays"]:
                if v.get("empty"):
                    continue
                unloads = sum(x.get("unloads", 0) for x in obj["relays"] if "unloads" in x)
                col0 = next((x.get("unloads", 0) for x in obj["relays"] if x.get("col") == 0), 0)
                firsts = [x["firstUnload"] for x in obj["relays"] if x.get("firstUnload")]
                first = min(firsts) if firsts else None
        if "metrics" in obj:
            m = obj["metrics"]
            pickups += m.get("pickups", 0)
            eggs += m.get("eggs", 0)
            eclosed += m.get("eclosed", 0)
            deaths += m.get("deaths", 0)
        if "colonies" in obj:
            adults = sum(c.get("adults", 0) for c in obj["colonies"])
            peak_adults = max(peak_adults, adults)
            last_adults = adults
            last_stock = sum(c.get("stock", 0.0) for c in obj["colonies"])
            peak_items = max(peak_items, len(obj.get("items", ())))
            trace.append({
                "tick": obj.get("tick", 0), "adults": adults,
                "eggs": sum(c.get("eggs", 0) for c in obj["colonies"]),
                "larvae": sum(c.get("larvae", 0) for c in obj["colonies"]),
                "pupae": sum(c.get("pupae", 0) for c in obj["colonies"]),
                "stock": last_stock, "items": len(obj.get("items", ())),
            })

    return {
        "seed": seed, "unloads": unloads, "col0": col0, "firstUnload": first,
        "pickups": pickups, "eggs": eggs, "eclosed": eclosed, "deaths": deaths,
        "peakAdults": peak_adults, "lastAdults": last_adults, "lastStock": last_stock,
        "peakItems": peak_items, "endTick": end_tick, "extinct": extinct, "trace": trace,
    }


def print_trace(r: dict, every: int = 2400) -> None:
    print(f"    demografía (cada {every} ticks): tick · adultas · cría(egg/larva/pupa) · stock · ítems")
    for s in r["trace"]:
        if s["tick"] % every:
            continue
        print(f"      {s['tick']:>6} · {s['adults']:>2} · "
              f"{s['eggs']:>2}/{s['larvae']:>2}/{s['pupae']:>2} · "
              f"{s['stock']:>6.1f} · {s['items']:>2}")


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", default="42,7,99,1234")
    ap.add_argument("--ticks", type=int, default=216000)
    ap.add_argument("--pool", default=DEFAULT_POOL)
    ap.add_argument("--until-extinct", action="store_true")
    ap.add_argument("--trace", action="store_true")
    ap.add_argument("--out", default="artifacts/recruitment-probe.txt")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    pool = None if args.pool in ("", "none", "frio") else args.pool

    build = subprocess.run(["dotnet", "build", str(CLI_PROJECT), "-c", "Release", "-v", "q", "--nologo"],
                           cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if build.returncode != 0:
        print(build.stdout[-2000:] + build.stderr[-2000:], file=sys.stderr)
        return 2

    rows = {"clásico": [], "reclutamiento": []}
    for seed in seeds:
        for name, rec in (("clásico", False), ("reclutamiento", True)):
            r = run_game(seed, args.ticks, pool, rec, args.until_extinct)
            rows[name].append(r)
            if not args.quiet:
                print(f"  {name:13s} seed {seed:4d}: {r['unloads']:3d} descargas "
                      f"(col0 {r['col0']:2d}) · pickups {r['pickups']:3d} · "
                      f"pico adultas {r['peakAdults']:2d} · eclosiones {r['eclosed']:3d} · "
                      f"primera {r['firstUnload']} · fin {r['endTick']}")
            if args.trace:
                print_trace(r)

    lines = [
        f"# Reclutamiento contra el mundo clásico — partida canónica "
        f"(grid 96, laberinto, {args.ticks} ticks, semillas {','.join(map(str, seeds))})",
        f"# pool: {pool or 'frío (sin sembrar)'}",
        "",
        "| mundo | descargas (media ± σ) | total | semillas con descargas | pico adultas | eclosiones | primera descarga (media) | fin de ronda (media) |",
        "|---|---|---|---|---|---|---|---|",
    ]
    summary = {}
    for name, rs in rows.items():
        un = [r["unloads"] for r in rs]
        firsts = [r["firstUnload"] for r in rs if r["firstUnload"]]
        summary[name] = {
            "mean": statistics.mean(un), "sd": statistics.pstdev(un) if len(un) > 1 else 0.0,
            "total": sum(un), "with": sum(1 for u in un if u > 0),
            "adults": statistics.mean([r["peakAdults"] for r in rs]),
            "eclosed": statistics.mean([r["eclosed"] for r in rs]),
            "first": statistics.mean(firsts) if firsts else None,
            "end": statistics.mean([r["endTick"] for r in rs if r["endTick"]]),
        }
        s = summary[name]
        first_txt = "—" if s["first"] is None else f"{s['first']:.0f}"
        lines.append(f"| {name} | {s['mean']:.1f} ± {s['sd']:.1f} | {s['total']} | "
                     f"{s['with']}/{len(rs)} | {s['adults']:.1f} | {s['eclosed']:.1f} | "
                     f"{first_txt} | {s['end']:.0f} |")

    lines.append("")
    for name, rs in rows.items():
        lines.append(f"- **{name}**: " + ", ".join(
            f"seed {r['seed']}={r['unloads']} (pickups {r['pickups']}, adultas pico "
            f"{r['peakAdults']}, eclosiones {r['eclosed']}, fin {r['endTick']})" for r in rs))

    classic, rec = summary["clásico"], summary["reclutamiento"]
    wins = sum(1 for a, b in zip(rows["reclutamiento"], rows["clásico"]) if a["unloads"] > b["unloads"])
    ok = rec["total"] > classic["total"] and wins == len(seeds)
    lines += ["", ("✓ el reclutamiento gana al mundo clásico en TODAS las semillas "
                   f"({rec['total']} vs {classic['total']} descargas; {wins}/{len(seeds)} semillas)"
                   if ok else
                   "✗ el reclutamiento NO gana en todas las semillas: "
                   f"{rec['total']} vs {classic['total']} descargas; {wins}/{len(seeds)} semillas")]

    text = "\n".join(lines) + "\n"
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    if not args.quiet:
        print()
        print(text)
    print(f"evidencia → {args.out}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
