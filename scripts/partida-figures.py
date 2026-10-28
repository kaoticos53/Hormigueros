#!/usr/bin/env python3
"""partida-figures.py — las cifras de la PARTIDA que afirma un documento, derivadas de los volcados.

POR QUÉ EXISTE. `check-doc-figures.sh` comprueba los pines y los tests porque puede MEDIRLOS: cuenta
los `scripts/*.expected` y lista los casos de la suite. Las cifras de F11h —la tabla de recetas del
brazo de 10 s y los t de sus comparaciones— no se pueden medir así: cada celda cuesta partidas de
216 000 ticks (las 1 056 de F11h son horas de máquina) y ningún guardarraíl paga eso en cada push. Lo
que sí queda es el VOLCADO que dejaron esas partidas, versionado en `artifacts/`, y ese volcado es la
única verdad disponible: este derivador lo lee y devuelve cada cifra FORMATEADA COMO LA ESCRIBE EL
DOCUMENTO, para que el guardarraíl pueda exigir que el texto la siga diciendo. Es lo que hace el
guardarraíl con los pines: fijar la cifra, no confiar en que nadie la toque.

QUÉ DERIVA (F11h, `docs/estado-proyecto.md`):
  · la tabla 2×2 de las réplicas del brazo de 10 s (media y rango de cada celda);
  · los t de las comparaciones de precio (receta del reflejo y receta de la puerta) y de la puerta
    frente al reflejo dentro del brazo de 10 s;
  · los agregados de 11 y 14 pools —la puerta pagando al promediar tiradas—;
  · la dispersión «entre tiradas» y el t del pareado por sorteo de la receta del reflejo.

DOS CONVENCIONES DEL DOCUMENTO QUE HAY QUE RESPETAR, y por eso están escritas aquí en vez de
suponerse:
  1. La tabla juega cada receta con el rastro QUE LA ENTRENÓ: la fila «sin puerta» sale de las líneas
     `fijo a … s` de sus réplicas y la fila «con puerta» de las `puerta a … s`. Mezclar rastros daría
     otras cifras (166,6 y 147,4 donde el documento dice 165,1 y 154,1) y no es lo que dice.
  2. «la puerta cuesta 15,8 … y 12,8 …» son la columna de JUGADO A 25 s de esas comparaciones, no la
     de 10 s (que da 17,7 y 12,4). El documento no nombra la columna; queda dicho aquí para que cada
     cifra tenga un origen que se pueda seguir a mano.

Uso:
  python scripts/partida-figures.py             # clave<TAB>fragmento, una por línea
  python scripts/partida-figures.py --list      # qué volcado alimenta cada cifra
  python scripts/partida-figures.py --selftest  # el derivador, con volcados de mentira

Salida: 0 se derivaron todas · 1 falta un volcado, una cifra o el formato (fail-closed: una cifra que
no se puede derivar NO se salta en silencio, porque entonces el documento quedaría sin comprobar).
"""
from __future__ import annotations

import argparse
import re
import statistics
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# ── Los volcados: la evidencia de F11h, versionada en `artifacts/` ────────────
P107_REPLICAS = "artifacts/pretrain-partida-200-p107-replicas.txt"
P10G4_REPLICAS = "artifacts/pretrain-partida-200-p10g4-replicas.txt"
P107_VS_P257_FIJO = "artifacts/pretrain-partida-200-p107-vs-p257-fijo.txt"
P10G4_VS_P25G7_FIJO = "artifacts/pretrain-partida-200-p10g4-vs-p25g7-fijo.txt"
P10G4_VS_P25G7_PUERTA = "artifacts/pretrain-partida-200-p10g4-vs-p25g7-puerta.txt"
P10G4_VS_P107_FIJO = "artifacts/pretrain-partida-200-p10g4-vs-p107-fijo.txt"
P10G4_VS_P107_PUERTA = "artifacts/pretrain-partida-200-p10g4-vs-p107-puerta.txt"
P107_CHAMPION = "artifacts/pretrain-partida-200-p107-champion.txt"
P25G7_CHAMPION = "artifacts/pretrain-partida-200-p25g7-champion.txt"

# ── El signo y el guion DEL DOCUMENTO, no los del volcado ─────────────────────
# Los volcados escriben el menos con guion ASCII (-6.7) y el rango con guion medio
# (144–152); el documento escribe el menos con U+2212 (−6,7) y el rango igual. La
# coma decimal es del documento: los volcados la mezclan (165.1 en la media, 189,
# 140 en la lista).
MINUS = "\u2212"
GUION = "\u2013"


def num(x: float, dec: int = 1) -> str:
    """Formatea al uso del documento: coma decimal y menos tipográfico."""
    s = f"{x:.{dec}f}"
    if s.startswith("-"):
        s = MINUS + s[1:]
    return s.replace(".", ",")


def num_mas(x: float, dec: int = 1) -> str:
    """Como `num`, con el «+» que el documento escribe cuando la cifra es positiva."""
    return ("+" if x >= 0 else "") + num(x, dec)


# ── Los tres formatos de línea de los volcados ───────────────────────────────
# Cada uno es de un generador distinto y se lee tal cual: si un generador cambia
# su salida, aquí se ve (falta la cifra) en vez de derivar otra cosa.
RE_REPLICA = re.compile(
    r"^- \*\*(?P<gate>fijo|puerta) a (?P<price>\d+) s\*\*: las réplicas dan .* → "
    r"media \*\*(?P<mean>[\d.]+)\*\* \(rango (?P<lo>\d+)" + GUION + r"(?P<hi>\d+)\)")
RE_COMPARA = re.compile(
    r"^- \*\*(?P<a>.+?) " + MINUS + r" (?P<b>.+?) \((?P<gate>fijo|puerta), jugado "
    r"(?P<price>\d+) s\)\*\*: (?P<m>[+-][\d.]+) ± (?P<se>[\d.]+) descargas "
    r"\(t = (?P<t>[+-][\d.]+); gana en (?P<wins>\d+)/(?P<n>\d+) semillas\)")
RE_POR_TIRADA = re.compile(r"^- \*\*por tirada de GA \((?P<n>\d+)\):\*\*(?P<resto>.*)$")
RE_DELTA = re.compile(r"= ([+-]\d+)")
RE_POOLS = re.compile(
    r"^- \*\*(?P<n>\d+) pools · jugado (?P<price>\d+) s\*\*: la puerta paga "
    r"(?P<m>[+-][\d.]+) ± (?P<se>[\d.]+) descargas al promediar las tiradas "
    r"\(t = (?P<t>[+-][\d.]+); (?P<wins>\d+)/(?P<n2>\d+) tiradas a favor\)")


class FaltaCifra(RuntimeError):
    """El volcado no trae la línea que se esperaba: no se deriva nada por su cuenta."""


def leer(raiz: Path, fuente: str) -> list[str]:
    p = raiz / fuente
    if not p.is_file():
        raise FaltaCifra(f"no existe el volcado {fuente}")
    return p.read_text(encoding="utf-8", errors="replace").splitlines()


def celda_replicas(raiz: Path, fuente: str, gate: str, price: int) -> dict:
    """Media y rango de las réplicas de una receta, con un rastro y un precio de juego."""
    for linea in leer(raiz, fuente):
        m = RE_REPLICA.match(linea)
        if m and m["gate"] == gate and int(m["price"]) == price:
            return {"mean": num(float(m["mean"])), "lo": m["lo"], "hi": m["hi"]}
    raise FaltaCifra(f"{fuente}: no hay réplicas «{gate} a {price} s»")


def celda_compara(raiz: Path, fuente: str, gate: str, price: int) -> dict:
    """Una comparación pareada: media, error típico, t y semillas ganadas."""
    for linea in leer(raiz, fuente):
        m = RE_COMPARA.match(linea)
        if m and m["gate"] == gate and int(m["price"]) == price:
            return {"m": num(float(m["m"])), "am": num(abs(float(m["m"]))),
                    "se": num(float(m["se"])), "t": num(float(m["t"])),
                    "wins": m["wins"], "n": m["n"]}
    raise FaltaCifra(f"{fuente}: no hay comparación «{gate}, jugado {price} s»")


def celdas_por_tirada(raiz: Path, fuente: str) -> dict[tuple[str, int], list[float]]:
    """Las diferencias de cada tirada de GA, colgadas de la comparación que las precede.

    El volcado no repite el rastro ni el precio en la línea «por tirada de GA»: van
    detrás de la comparación, así que se recuerda cuál fue la última.
    """
    out: dict[tuple[str, int], list[float]] = {}
    actual: tuple[str, int] | None = None
    for linea in leer(raiz, fuente):
        m = RE_COMPARA.match(linea)
        if m:
            actual = (m["gate"], int(m["price"]))
            continue
        d = RE_POR_TIRADA.match(linea)
        if d and actual is not None:
            out[actual] = [float(x) for x in RE_DELTA.findall(d["resto"])]
    return out


def celda_por_tirada(raiz: Path, fuente: str, gate: str, price: int) -> dict:
    """La dispersión y el t del pareado por SORTEO (la tirada como unidad independiente)."""
    ds = celdas_por_tirada(raiz, fuente).get((gate, price))
    if not ds or len(ds) < 2:
        raise FaltaCifra(f"{fuente}: no hay lista «por tirada de GA» para {gate} a {price} s")
    sd = statistics.stdev(ds)
    t = statistics.mean(ds) / (sd / len(ds) ** 0.5) if sd > 0 else 0.0
    return {"sd": f"{sd:.0f}", "t": num(t)}


def celda_pools(raiz: Path, fuente: str, price: int) -> dict:
    """La puerta promediando las tiradas de todos los pools de un brazo."""
    for linea in leer(raiz, fuente):
        m = RE_POOLS.match(linea)
        if m and int(m["price"]) == price:
            return {"m": num(float(m["m"])), "am": num(abs(float(m["m"]))),
                    "se": num(float(m["se"])), "t": num(float(m["t"])),
                    "wins": m["wins"], "n": m["n"], "pools": m["n"]}
    raise FaltaCifra(f"{fuente}: no hay agregado de pools «jugado {price} s»")


# ── Las cifras del documento, una por línea: clave · volcado · plantilla ──────
# La plantilla es el texto del documento CON HUECOS: los huecos los llena la cifra
# derivada, y el resto (las palabras) es lo que hace falta que el texto siga
# diciendo para que la cifra sea localizable. Cambiar una palabra de aquí es
# cambiar lo que el guardarraíl exige; no se hace por comodidad.
CIFRAS: list[tuple[str, str, str]] = [
    # la tabla 2×2 (cada receta con el rastro que la entrenó)
    ("tabla · sin puerta · jugado 10 s", P107_REPLICAS, "{mean} ({lo}" + GUION + "{hi})"),
    ("tabla · sin puerta · jugado 25 s", P107_REPLICAS, "{mean} ({lo}" + GUION + "{hi})"),
    ("tabla · con puerta · jugado 10 s", P10G4_REPLICAS, "{mean} ({lo}" + GUION + "{hi})"),
    ("tabla · con puerta · jugado 25 s", P10G4_REPLICAS, "{mean} ({lo}" + GUION + "{hi})"),
    # receta del reflejo: el precio de entrenamiento sigue pagando
    ("precio · reflejo · jugado 10 s", P107_VS_P257_FIJO,
     "10 s menos 25 s da +{m} ± {se} descargas"),
    ("precio · reflejo · 10 s · semillas", P107_VS_P257_FIJO,
     "jugando a 10 s (t = {t}; {wins}/{n} semillas)"),
    ("precio · reflejo · jugado 25 s", P107_VS_P257_FIJO, "+{m} ± {se} jugando a 25 s"),
    ("precio · reflejo · 25 s · semillas", P107_VS_P257_FIJO, "t = {t}; {wins}/{n}"),
    ("precio · reflejo · dispersión y t por sorteo", P107_VS_P257_FIJO,
     "deja t ≈ {t10} y {t25}"),
    ("precio · reflejo · sd entre tiradas", P107_VS_P257_FIJO, "la sd entre tiradas es {sd}"),
    # receta de la puerta: el precio se invierte
    ("precio · puerta · reflejo al jugar · 10 s", P10G4_VS_P25G7_FIJO, "{m} ± {se} (t = {t})"),
    ("precio · puerta · reflejo al jugar · 25 s", P10G4_VS_P25G7_FIJO, "{m} ± {se} (t = {t})"),
    ("precio · puerta · con la puerta · 10 y 25 s", P10G4_VS_P25G7_PUERTA,
     "{m10} ± {se10} y {m25} ± {se25} (t = {t25})"),
    # la puerta frente al reflejo dentro del brazo de 10 s (columna de jugado a 25 s)
    ("puerta a 10 s · reflejo al jugar", P10G4_VS_P107_FIJO,
     "{am} ± {se} descargas con el reflejo al jugar (t = {t}; {wins}/{n} semillas)"),
    ("puerta a 10 s · con la puerta", P10G4_VS_P107_PUERTA, "{am} ± {se} con la puerta (t = {t})"),
    # los agregados de pools (la puerta al promediar tiradas)
    ("11 pools · jugado 10 s", P107_CHAMPION,
     "+{m} ± {se} a 10 s de precio (t = {t}; {wins}/{n})"),
    ("11 pools · jugado 25 s", P107_CHAMPION, "{m} ± {se} a 25 s (t = {t}; {wins}/{n})"),
    ("14 pools · jugado 25 s (F11g)", P25G7_CHAMPION, "(+{m} ± {se}, t = {t}; F11g)"),
]

# Qué celda del volcado alimenta cada cifra. Va aparte de la lista de arriba para
# que se lea de un golpe QUÉ se comprueba y DE DÓNDE sale, sin mezclar las dos cosas.
CELDAS = {
    "tabla · sin puerta · jugado 10 s": lambda r: celda_replicas(r, P107_REPLICAS, "fijo", 10),
    "tabla · sin puerta · jugado 25 s": lambda r: celda_replicas(r, P107_REPLICAS, "fijo", 25),
    "tabla · con puerta · jugado 10 s": lambda r: celda_replicas(r, P10G4_REPLICAS, "puerta", 10),
    "tabla · con puerta · jugado 25 s": lambda r: celda_replicas(r, P10G4_REPLICAS, "puerta", 25),
    "precio · reflejo · jugado 10 s": lambda r: celda_compara(r, P107_VS_P257_FIJO, "fijo", 10),
    "precio · reflejo · 10 s · semillas": lambda r: celda_compara(r, P107_VS_P257_FIJO, "fijo", 10),
    "precio · reflejo · jugado 25 s": lambda r: celda_compara(r, P107_VS_P257_FIJO, "fijo", 25),
    "precio · reflejo · 25 s · semillas": lambda r: celda_compara(r, P107_VS_P257_FIJO, "fijo", 25),
    "precio · reflejo · dispersión y t por sorteo": lambda r: {
        "t10": celda_por_tirada(r, P107_VS_P257_FIJO, "fijo", 10)["t"],
        "t25": celda_por_tirada(r, P107_VS_P257_FIJO, "fijo", 25)["t"]},
    "precio · reflejo · sd entre tiradas": lambda r: celda_por_tirada(r, P107_VS_P257_FIJO, "fijo", 10),
    "precio · puerta · reflejo al jugar · 10 s": lambda r: celda_compara(r, P10G4_VS_P25G7_FIJO, "fijo", 10),
    "precio · puerta · reflejo al jugar · 25 s": lambda r: celda_compara(r, P10G4_VS_P25G7_FIJO, "fijo", 25),
    "precio · puerta · con la puerta · 10 y 25 s": lambda r: {
        **{k + "10": v for k, v in celda_compara(r, P10G4_VS_P25G7_PUERTA, "puerta", 10).items()},
        **{k + "25": v for k, v in celda_compara(r, P10G4_VS_P25G7_PUERTA, "puerta", 25).items()}},
    "puerta a 10 s · reflejo al jugar": lambda r: celda_compara(r, P10G4_VS_P107_FIJO, "fijo", 25),
    "puerta a 10 s · con la puerta": lambda r: celda_compara(r, P10G4_VS_P107_PUERTA, "puerta", 25),
    "11 pools · jugado 10 s": lambda r: celda_pools(r, P107_CHAMPION, 10),
    "11 pools · jugado 25 s": lambda r: celda_pools(r, P107_CHAMPION, 25),
    "14 pools · jugado 25 s (F11g)": lambda r: celda_pools(r, P25G7_CHAMPION, 25),
}


def derivar(raiz: Path) -> list[tuple[str, str]]:
    """Devuelve (clave, fragmento) de cada cifra, en el formato del documento."""
    salida = []
    for clave, _fuente, plantilla in CIFRAS:
        valores = CELDAS[clave](raiz)
        try:
            salida.append((clave, plantilla.format(**valores)))
        except KeyError as e:
            raise FaltaCifra(f"{clave}: la celda no trae {e}") from e
    return salida


# ── selftest: el derivador, con volcados de mentira ──────────────────────────
def selftest() -> int:
    """Comprueba el derivador sin repo: formato, aritmética y que la deriva se note."""
    lineas = {
        P107_REPLICAS: (
            "- **fijo a 10 s**: las réplicas dan 189, 140 → media **165.1** (rango 114" + GUION + "189); la referencia da 189.0\n"
            "- **fijo a 25 s**: las réplicas dan 201, 145 → media **168.7** (rango 115" + GUION + "201); la referencia da 200.7\n"),
        P10G4_REPLICAS: (
            "- **puerta a 10 s**: las réplicas dan 159, 155 → media **154.1** (rango 147" + GUION + "159); la referencia da 159.5\n"
            "- **puerta a 25 s**: las réplicas dan 157, 151 → media **154.4** (rango 150" + GUION + "159); la referencia da 156.9\n"),
        P107_VS_P257_FIJO: (
            "- **10s " + MINUS + " 25s (fijo, jugado 10 s)**: +10.2 ± 2.6 descargas (t = +4.0; gana en 18/24 semillas)\n"
            "- **por tirada de GA (7):** s11 140 vs 184 = -43 · s13 181 vs 146 = +34 · s23 187 vs 119 = +68\n"
            "- **10s " + MINUS + " 25s (fijo, jugado 25 s)**: +13.1 ± 3.4 descargas (t = +3.8; gana en 18/24 semillas)\n"
            "- **por tirada de GA (7):** s11 145 vs 185 = -40 · s13 189 vs 149 = +40 · s23 191 vs 122 = +69\n"),
        P10G4_VS_P25G7_FIJO: (
            "- **10 g " + MINUS + " 25 g (fijo, jugado 10 s)**: -6.7 ± 2.2 descargas (t = -3.1; gana en 5/24 semillas)\n"
            "- **10 g " + MINUS + " 25 g (fijo, jugado 25 s)**: -8.7 ± 2.1 descargas (t = -4.1; gana en 3/24 semillas)\n"),
        P10G4_VS_P25G7_PUERTA: (
            "- **10 g " + MINUS + " 25 g (puerta, jugado 10 s)**: -6.2 ± 3.2 descargas (t = -1.9; gana en 8/24 semillas)\n"
            "- **10 g " + MINUS + " 25 g (puerta, jugado 25 s)**: -11.9 ± 2.6 descargas (t = -4.5; gana en 4/24 semillas)\n"),
        P10G4_VS_P107_FIJO: (
            "- **10 g " + MINUS + " 10 (fijo, jugado 25 s)**: -15.8 ± 3.5 descargas (t = -4.5; gana en 5/24 semillas)\n"),
        P10G4_VS_P107_PUERTA: (
            "- **10 g " + MINUS + " 10 (puerta, jugado 25 s)**: -12.8 ± 2.8 descargas (t = -4.6; gana en 6/24 semillas)\n"),
        P107_CHAMPION: (
            "- **11 pools · jugado 10 s**: la puerta paga +3.4 ± 2.3 descargas al promediar las tiradas (t = +1.5; 8/11 tiradas a favor)\n"
            "- **11 pools · jugado 25 s**: la puerta paga -0.5 ± 2.6 descargas al promediar las tiradas (t = -0.2; 5/11 tiradas a favor)\n"),
        P25G7_CHAMPION: (
            "- **14 pools · jugado 25 s**: la puerta paga +5.6 ± 1.9 descargas al promediar las tiradas (t = +2.9; 12/14 tiradas a favor)\n"),
    }
    fallo = 0

    def decir(ok: bool, texto: str) -> None:
        nonlocal fallo
        print(("  ok " if ok else "  X ") + texto)
        if not ok:
            fallo = 1

    with tempfile.TemporaryDirectory() as tmp:
        raiz = Path(tmp)
        (raiz / "artifacts").mkdir()
        for fuente, texto in lineas.items():
            (raiz / fuente).write_text(texto, encoding="utf-8")

        figs = dict(derivar(raiz))
        print(f"partida-figures --selftest ({len(CIFRAS)} cifras, volcados de mentira)")

        # Formato: coma decimal, menos tipográfico y guion medio del rango.
        decir(figs["tabla · sin puerta · jugado 10 s"] == "165,1 (114" + GUION + "189)",
              "la celda de réplicas sale «165,1 (114–189)»: coma decimal y guion medio")
        decir(figs["precio · reflejo · jugado 10 s"] == "10 s menos 25 s da +10,2 ± 2,6 descargas",
              "la comparación sale con el «+» y la coma del documento")
        decir(figs["tabla · con puerta · jugado 25 s"] == "154,4 (150" + GUION + "159)",
              "la fila «con puerta» se lee de las líneas «puerta a …» de sus réplicas")
        decir(figs["puerta a 10 s · reflejo al jugar"].startswith("15,8 ± 3,5 descargas") and
              MINUS + "4,5" in figs["puerta a 10 s · reflejo al jugar"],
              "el negativo sale con el menos tipográfico y el t con su signo")
        decir(figs["11 pools · jugado 25 s"] == MINUS + "0,5 ± 2,6 a 25 s (t = " + MINUS + "0,2; 5/11)",
              "el agregado de pools negativo sale como lo escribe el documento")

        # Aritmética del pareado por sorteo: sd muestral y t = media/(sd/√n).
        # Con deltas -43, +34, +68 la sd muestral es 56,9 (→ «57») y el t 0,60 (→
        # «0,6»); con -40, +40, +69, sd 56,5 y t 0,71 (→ «0,7»).
        decir(figs["precio · reflejo · sd entre tiradas"] == "la sd entre tiradas es 57",
              "la sd entre tiradas sale de las diferencias por sorteo")
        decir(figs["precio · reflejo · dispersión y t por sorteo"] == "deja t ≈ 0,6 y 0,7",
              "los t del pareado por sorteo se calculan, no se copian")

        # Que la deriva SE NOTE: cambiar el volcado cambia el fragmento exigido.
        (raiz / P107_REPLICAS).write_text(
            lineas[P107_REPLICAS].replace("**165.1**", "**167.9**"), encoding="utf-8")
        figs2 = dict(derivar(raiz))
        decir(figs2["tabla · sin puerta · jugado 10 s"] == "167,9 (114" + GUION + "189)",
              "si el volcado cambia, el fragmento exigido cambia con él")

        # Fail-closed: sin volcado, no se deriva en silencio.
        (raiz / P10G4_VS_P107_PUERTA).unlink()
        try:
            derivar(raiz)
            decir(False, "un volcado que falta tiene que parar la derivación")
        except FaltaCifra as e:
            decir(P10G4_VS_P107_PUERTA in str(e), f"un volcado que falta se declara: {e}")

        # Y una línea con el formato de otro generador tampoco se acepta como cifra.
        (raiz / P10G4_VS_P107_PUERTA).write_text("- **10 g " + MINUS + " 10 (puerta): -12.8\n",
                                                encoding="utf-8")
        try:
            derivar(raiz)
            decir(False, "una línea sin el formato del generador tiene que parar la derivación")
        except FaltaCifra:
            decir(True, "una línea con otro formato no se lee como cifra")

    print("✓ derivador de cifras de partida correcto" if fallo == 0
          else "✗ derivador de cifras de partida roto")
    return fallo


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="qué volcado alimenta cada cifra")
    ap.add_argument("--selftest", action="store_true", help="el derivador, con volcados de mentira")
    args = ap.parse_args(argv)

    if args.selftest:
        return selftest()

    if args.list:
        ancho = max(len(c) for c, _f, _p in CIFRAS)
        for clave, fuente, plantilla in CIFRAS:
            print(f"  {clave:<{ancho}}  {fuente}")
            print(f"  {'':<{ancho}}  → {plantilla}")
        print(f"\n{len(CIFRAS)} cifras de {len({f for _c, f, _p in CIFRAS})} volcados de F11h")
        return 0

    try:
        for clave, fragmento in derivar(ROOT):
            print(f"{clave}\t{fragmento}")
    except FaltaCifra as e:
        print(f"✗ no pude derivar una cifra de la partida: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
