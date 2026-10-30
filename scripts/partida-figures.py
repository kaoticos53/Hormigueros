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

Y desde F11j/F11k, del mismo documento:
  · la tabla 2×2 del HORIZONTE (precio × horizonte de selección) con su efecto y su relativo,
    calculado como efecto/referencia —el volcado trae el efecto, no el porcentaje—;
  · los deltas pareados que dicen cuánto mueve el horizonte al efecto de la puerta, el pareado por
    sorteo y el reparto de rangos entre tiradas;
  · la tabla del rastro depositado por brazo (comida, casa y negado), las medias de casa que el
    volcado sólo publica por pool —se promedian aquí—, el crecimiento de la cresta con el precio y
    el factor con que la ventana dobla el acumulado.

Y desde F11l, del mismo documento:
  · la tabla del 2×2 completo (precio de entrenamiento × horizonte de selección) jugada a 10 s de
    glándula, con el nivel de los dos brazos —que se promedian aquí de las medias por pool—, el
    efecto que trae el volcado y su relativo;
  · el salto de horizonte a los dos precios con sus tres deltas por sorteo —el volcado los nombra
    sin cifra y se calculan aquí—, el reparto del castigo con su predicción aditiva y la
    interacción en las cuatro formas de jugar.

Y desde F11n, del mismo documento:
  · el enfrentamiento del lanzador —el par de la puerta a 25 s contra el juego de fábrica—: los
    tres pareados con su t y sus semillas, la dispersión de cada receta, lo que pone cada pieza,
    el pedestal de fábrica que calibra el banco y la cuenta de brazos y de partidas. Este volcado
    no comparte formato con los demás: escribe viñetas con el nombre en negrita y una tabla de
    brazos, y se lee por el nombre de la viñeta, no por su posición.

DOS CIFRAS QUE EL VOLCADO OBLIGÓ A CORREGIR EN EL DOCUMENTO, y que por eso están aquí escritas:
  · el relativo de la celda 25 s · puerta a 48k: con las medias redondeadas del volcado (205,8 y
    247,8) da −16,9 %, no el −17,0 % que salía de las medias sin redondear;
  · la casa media del brazo del reflejo a 48k: la media de los tres pools publicados es 9 641, no
    9 642. El volcado es la verdad versionada; el documento se ajustó a lo que se puede derivar.

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
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# ── Los volcados: la evidencia de F11h–F11k, versionada en `artifacts/` ───────
P107_REPLICAS = "artifacts/pretrain-partida-200-p107-replicas.txt"
P10G4_REPLICAS = "artifacts/pretrain-partida-200-p10g4-replicas.txt"
P107_VS_P257_FIJO = "artifacts/pretrain-partida-200-p107-vs-p257-fijo.txt"
P10G4_VS_P25G7_FIJO = "artifacts/pretrain-partida-200-p10g4-vs-p25g7-fijo.txt"
P10G4_VS_P25G7_PUERTA = "artifacts/pretrain-partida-200-p10g4-vs-p25g7-puerta.txt"
P10G4_VS_P107_FIJO = "artifacts/pretrain-partida-200-p10g4-vs-p107-fijo.txt"
P10G4_VS_P107_PUERTA = "artifacts/pretrain-partida-200-p10g4-vs-p107-puerta.txt"
P107_CHAMPION = "artifacts/pretrain-partida-200-p107-champion.txt"
P25G7_CHAMPION = "artifacts/pretrain-partida-200-p25g7-champion.txt"
# F11n: el enfrentamiento del lanzador, que no comparte formato con los de arriba.
P10H2_DUEL = "artifacts/pretrain-partida-200-p10h2-duel.txt"

# ── El signo y el guion DEL DOCUMENTO, no los del volcado ─────────────────────
# Los volcados escriben el menos con guion ASCII (-6.7) y el rango con guion medio
# (144–152); el documento escribe el menos con U+2212 (−6,7) y el rango igual. La
# coma decimal es del documento: los volcados la mezclan (165.1 en la media, 189,
# 140 en la lista).
MINUS = "\u2212"
GUION = "\u2013"
MIDOT = "\u00b7"  # el «·» con que el volcado del enfrentamiento separa brazo, pool y glándula


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


# ── F11j y F11k: los volcados del horizonte y del rastro ─────────────────────
P10H2_VEREDICTO = "artifacts/pretrain-partida-200-p10h2-veredicto.txt"
P10H2G_REPLICAS = "artifacts/pretrain-partida-200-p10h2g-replicas.txt"
P10H2S_REPLICAS = "artifacts/pretrain-partida-200-p10h2s-replicas.txt"
P10H2_DEPOSITS = "artifacts/pretrain-partida-200-p10h2-deposits.txt"

RE_H_TABLA_SECCION = re.compile(r"^## Jugado con la glándula a (\d+) s · rastro (\w+)")
RE_H_DELTA_SECCION = re.compile(r"^### jugado (\d+) s · rastro (\w+)")
RE_H_FILA = re.compile(r"^\| (\d+)k \| ([\d.]+) \| ([\d.]+) \| (-?[\d.]+) \|")
RE_H_DELTA = re.compile(
    r"→ (-?[\d.]+) ± ([\d.]+) descargas \(t = (-?[\d.]+); (\d+)/3 sorteos a favor\)")
RE_H_SORTEO = re.compile(r"= ([+-][\d.]+)")
RE_REPLICA_JUEGO = re.compile(r"^\| p10h2[gs]-s\d+ \| (puerta|fijo) \| ([\d.]+) ±")
RE_DEP_PRECIO = re.compile(r"^## Precio de juego (\d+) s")
RE_DEP_VENTANA = re.compile(r"^### Ventana de (\d+) ticks")
# Los cuatro prefijos de pool del volcado: los dos entrenados a 48 000 (p10h2g/p10h2s) y los dos a
# 24 000 (p10g/p10s). Los cuatro cuentan: los factores de ventana y el negado se miden sobre los 24
# brazos del volcado, no sólo sobre el brazo nuevo.
BRAZO = r"(p10h2g|p10h2s|p10g|p10s)"
RE_DEP_FILA = re.compile(
    r"^\| " + BRAZO + r"-s(\d+) \| (puerta|fijo) \| ([\d.]+) \| ([\d.]+) \| (\d+) % \| ([\d.]+) \|")
RE_DEP_MEDIA = re.compile(r"^- \*\*" + BRAZO + r" · (puerta|fijo)\*\*: comida ([\d.]+) ± ([\d.]+)")
RE_DEP_DIF = re.compile(
    r"^- \*\*" + BRAZO + r"-s(\d+): puerta " + MINUS +
    r" fijo\*\* → ([+-][\d.]+) ± ([\d.]+) de rastro \(gana (\d+)/24")
RE_DEP_RATIO = re.compile(
    r"^- \*\*" + BRAZO + r"-s(\d+) · (puerta|fijo)\*\*: ([\d.]+) → ([\d.]+) \(×([\d.]+)\)")

PALABRAS = {0: "ninguna", 1: "una", 2: "dos", 3: "tres", 4: "cuatro", 5: "cinco",
            6: "seis", 7: "siete", 8: "ocho", 9: "nueve", 10: "diez", 11: "once", 12: "doce"}


def miles(x: float, dec: int = 0) -> str:
    """Al uso del documento para las cifras de rastro: «7 320», «19 714,7»."""
    entero, _, frac = f"{x:,.{dec}f}".partition(".")
    s = entero.replace(",", " ") + (("," + frac) if frac else "")
    return MINUS + s[1:] if s.startswith("-") else s


def horizonte(raiz: Path) -> dict:
    """Las cuatro celdas del 2×2 del horizonte y sus deltas, del volcado del comparador."""
    out: dict = {}
    clave = None
    for linea in leer(raiz, P10H2_VEREDICTO):
        m = RE_H_TABLA_SECCION.match(linea) or RE_H_DELTA_SECCION.match(linea)
        if m:
            clave = (int(m.group(1)), m.group(2))
            out.setdefault(clave, {"filas": {}, "delta": None, "sorteos": []})
            continue
        if clave is None:
            continue
        d = out[clave]
        m = RE_H_FILA.match(linea)
        if m:
            d["filas"][int(m.group(1))] = (float(m.group(2)), float(m.group(3)), float(m.group(4)))
            continue
        m = RE_H_DELTA.search(linea)
        if m:
            d["delta"] = (float(m.group(1)), float(m.group(2)), float(m.group(3)), int(m.group(4)))
            continue
        if "por sorteo" in linea:
            d["sorteos"] = [float(x) for x in RE_H_SORTEO.findall(linea)]
    if not out:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: no se reconoce ninguna celda del horizonte")
    return out


def celda_horizonte(raiz: Path, precio: int, rastro: str, h: int) -> dict:
    """Una fila de la tabla de F11j: el nivel de los dos brazos, el efecto y su relativo."""
    d = horizonte(raiz).get((precio, rastro))
    if not d or h not in d["filas"]:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: no hay fila {h}k en «{precio} s · {rastro}»")
    pu, fi, ef = d["filas"][h]
    if fi == 0:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: la referencia de {h}k en «{precio} s · {rastro}» es 0")
    return {"pu": num(pu), "fi": num(fi), "ef": num(ef), "pct": num(ef / fi * 100)}


def delta_horizonte(raiz: Path, precio: int, rastro: str) -> dict:
    """Cuánto mueve el horizonte al efecto de la puerta, pareado por sorteo de GA."""
    d = horizonte(raiz).get((precio, rastro))
    if not d or d["delta"] is None:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: no hay delta 48k − 24k en «{precio} s · {rastro}»")
    m, se, t, wins = d["delta"]
    return {"m": num(m), "se": num(se), "t": num(t), "wins": str(wins)}


def sorteos_horizonte(raiz: Path, precio: int, rastro: str) -> list[float]:
    """Los tres deltas por sorteo de una celda del horizonte."""
    d = horizonte(raiz).get((precio, rastro))
    if not d or len(d["sorteos"]) != 3:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: faltan los deltas por sorteo de «{precio} s · {rastro}»")
    return d["sorteos"]


def replicas_juego(raiz: Path, fuente: str, rastro: str, precio: int = 10) -> list[float]:
    """Las medias por pool de un rastro, jugando con la glándula a `precio`."""
    vals: list[float] = []
    dentro = False
    for linea in leer(raiz, fuente):
        if linea.startswith("## Jugado con la glándula a"):
            dentro = linea.startswith(f"## Jugado con la glándula a {precio} s")
            continue
        if linea.startswith("###"):
            dentro = False
            continue
        m = RE_REPLICA_JUEGO.match(linea)
        if dentro and m and m.group(1) == rastro:
            vals.append(float(m.group(2)))
    if not vals:
        raise FaltaCifra(f"{fuente}: no hay filas «{rastro}» jugadas a {precio} s")
    return vals


def depositos(raiz: Path) -> dict:
    """El volcado del rastro: celdas por pool, medias por brazo, diferencias y factores."""
    out: dict = {"celdas": {}, "medias": {}, "difs": {}, "ratios": {}}
    precio = ventana = None
    for linea in leer(raiz, P10H2_DEPOSITS):
        m = RE_DEP_PRECIO.match(linea)
        if m:
            precio, ventana = int(m.group(1)), None
            continue
        m = RE_DEP_VENTANA.match(linea)
        if m:
            ventana = int(m.group(1))
            continue
        if linea.startswith("### La ventana"):
            ventana = "ratio"
            continue
        if precio is None or ventana is None:
            continue
        if ventana == "ratio":
            m = RE_DEP_RATIO.match(linea)
            if m:
                out["ratios"][(precio, m.group(1), int(m.group(2)), m.group(3))] = (
                    float(m.group(4)), float(m.group(5)), float(m.group(6)))
            continue
        m = RE_DEP_FILA.match(linea)
        if m:
            out["celdas"][(precio, ventana, m.group(1), int(m.group(2)), m.group(3))] = (
                float(m.group(4)), float(m.group(5)), int(m.group(6)), float(m.group(7)))
            continue
        m = RE_DEP_MEDIA.match(linea)
        if m:
            out["medias"][(precio, ventana, m.group(1), m.group(2))] = float(m.group(3))
            continue
        m = RE_DEP_DIF.match(linea)
        if m:
            out["difs"][(precio, ventana, m.group(1), int(m.group(2)))] = (
                float(m.group(3)), int(m.group(5)))
    if not out["celdas"]:
        raise FaltaCifra(f"{P10H2_DEPOSITS}: no se reconoce ninguna celda de rastro")
    return out


def valores_pool(raiz: Path, precio: int, ventana: int, brazo: str, rastro: str,
                 campo: str) -> list[float]:
    """Los tres sorteos de un brazo para un campo de la tabla de rastro."""
    idx = {"comida": 0, "casa": 1, "negado": 2, "glandula": 3}[campo]
    vals = [v[idx] for (p, w, b, _s, r), v in depositos(raiz)["celdas"].items()
            if (p, w, b, r) == (precio, ventana, brazo, rastro)]
    if not vals:
        raise FaltaCifra(f"{P10H2_DEPOSITS}: no hay celdas de {brazo} · {rastro} a {precio} s / {ventana} ticks")
    return vals


def media_celdas(raiz: Path, precio: int, ventana: int, brazo: str, rastro: str,
                 campo: str) -> float:
    """La media de los tres sorteos de un brazo para un campo de la tabla de rastro."""
    return statistics.mean(valores_pool(raiz, precio, ventana, brazo, rastro, campo))


def celda_pool(raiz: Path, precio: int, ventana: int, brazo: str, sorteo: int, rastro: str,
               campo: str) -> float:
    """Una celda concreta (un pool): las cifras de un solo sorteo no se promedian."""
    idx = {"comida": 0, "casa": 1, "negado": 2, "glandula": 3}[campo]
    v = depositos(raiz)["celdas"].get((precio, ventana, brazo, sorteo, rastro))
    if v is None:
        raise FaltaCifra(f"{P10H2_DEPOSITS}: no hay celda {brazo}-s{sorteo} · {rastro} a "
                         f"{precio} s / {ventana} ticks")
    return v[idx]


def k_tabla(raiz: Path, brazo: str, rastro: str) -> dict:
    """Una fila de la tabla de rastro de F11k (48 000 ticks, glándula de 10 s)."""
    return {"comida": num(media_celdas(raiz, 10, 48000, brazo, rastro, "comida")),
            "casa": miles(media_celdas(raiz, 10, 48000, brazo, rastro, "casa")),
            "negado": str(round(media_celdas(raiz, 10, 48000, brazo, rastro, "negado")))}


def k_cresta(raiz: Path) -> dict:
    """La cresta de comida sin precio y con la glándula de 10 s, y su factor.

    El brazo de la puerta se lee con el rastro puerta y el del reflejo con el fijo: son
    los dos brazos que el documento compara, cada uno con el rastro que lo entrena.
    """
    sin_p = media_celdas(raiz, 0, 48000, "p10h2g", "puerta", "comida")
    con_p = media_celdas(raiz, 10, 48000, "p10h2g", "puerta", "comida")
    sin_p2 = media_celdas(raiz, 0, 48000, "p10h2s", "fijo", "comida")
    con_p2 = media_celdas(raiz, 10, 48000, "p10h2s", "fijo", "comida")
    # El factor se calcula sobre los valores YA redondeados: es el número que un lector
    # puede reproducir dividiendo las dos cifras que el documento imprime a su lado.
    return {"a": miles(sin_p, 1), "b": num(con_p), "c": miles(sin_p2, 1), "d": num(con_p2),
            "x": str(round(round(sin_p, 1) / round(con_p, 1))),
            "y": str(round(round(sin_p2, 1) / round(con_p2, 1)))}


def k_sube(raiz: Path) -> dict:
    """Cuánto sube la cresta al abrir la puerta: extremos y semillas a favor."""
    difs = depositos(raiz)["difs"]
    vals = [v[0] for (p, _w, _b, _s), v in difs.items() if p == 10]
    if len(vals) != 24:
        raise FaltaCifra(f"{P10H2_DEPOSITS}: esperaba 24 diferencias con precio, hay {len(vals)}")
    c48 = [v for (p, w, _b, _s), v in difs.items() if p == 10 and w == 48000]
    n24 = sum(1 for v in c48 if v[1] == 24)
    n23 = sum(1 for v in c48 if v[1] == 23)
    if n24 not in PALABRAS or n23 not in PALABRAS:
        raise FaltaCifra(f"{P10H2_DEPOSITS}: fuera de la tabla de palabras ({n24} y {n23})")
    return {"a": num_mas(min(vals)), "b": num_mas(max(vals)),
            "n24": PALABRAS[n24], "n23": PALABRAS[n23]}


def k_ventana(raiz: Path, precio: int, solo_48k: bool = False) -> dict:
    """El factor con que la ventana dobla el acumulado, sin precio o con la glándula de 10 s."""
    ratios = depositos(raiz)["ratios"]
    vals = [v[2] for (p, b, _s, _r), v in ratios.items()
            if p == precio and (not solo_48k or b in ("p10h2g", "p10h2s"))]
    if not vals:
        raise FaltaCifra(f"{P10H2_DEPOSITS}: no hay factores de ventana a {precio} s")
    return {"a": num(min(vals), 2), "b": num(max(vals), 2)}


def k_ventana_por_rastro(raiz: Path) -> dict:
    """Con la glándula de 10 s, el factor de cada rastro al doblar la ventana."""
    ratios = depositos(raiz)["ratios"]
    out = {}
    for rastro, letras in (("fijo", ("a", "b")), ("puerta", ("c", "d"))):
        vals = [v[2] for (p, _b, _s, r), v in ratios.items() if p == 10 and r == rastro]
        if not vals:
            raise FaltaCifra(f"{P10H2_DEPOSITS}: no hay factores de «{rastro}» a 10 s")
        out[letras[0]], out[letras[1]] = num(min(vals), 2), num(max(vals), 2)
    return out


def k_casa_y_glandula(raiz: Path) -> dict:
    """El rastro de casa y el presupuesto del pool que cita el documento (p10h2g-s41).

    Va por pool y no por media de brazo porque la frase nombra uno: «en p10h2g-s41».
    """
    return {"a": miles(celda_pool(raiz, 10, 24000, "p10h2g", 41, "puerta", "casa")),
            "b": miles(celda_pool(raiz, 10, 48000, "p10h2g", 41, "puerta", "casa")),
            "c": miles(celda_pool(raiz, 10, 24000, "p10h2g", 41, "puerta", "glandula"), 1),
            "d": miles(celda_pool(raiz, 10, 48000, "p10h2g", 41, "puerta", "glandula"), 1)}


def k_puerta_contra_reflejo(raiz: Path) -> dict:
    """La cresta del brazo de la puerta contra la del reflejo, con y sin precio."""
    return {"a": num(media_celdas(raiz, 10, 48000, "p10h2g", "puerta", "comida")),
            "b": num(media_celdas(raiz, 10, 48000, "p10h2s", "puerta", "comida")),
            "c": num(media_celdas(raiz, 10, 48000, "p10h2g", "fijo", "comida")),
            "d": num(media_celdas(raiz, 10, 48000, "p10h2s", "fijo", "comida"))}


# ── F11l: el 2×2 completo (precio de entrenamiento × horizonte de selección) ──
P25H2_VEREDICTO = "artifacts/pretrain-partida-200-p25h2-veredicto.txt"
P25H2G_REPLICAS = "artifacts/pretrain-partida-200-p25h2g-replicas.txt"
P25H2S_REPLICAS = "artifacts/pretrain-partida-200-p25h2s-replicas.txt"
P25G7_REPLICAS = "artifacts/pretrain-partida-200-p25g7-replicas.txt"
P257_REPLICAS = "artifacts/pretrain-partida-200-p257-replicas.txt"
P10G7_REPLICAS = "artifacts/pretrain-partida-200-p10g7-replicas.txt"
P10X10_REPLICAS = "artifacts/pretrain-partida-200-p10x10-replicas.txt"

# Las cuatro casillas del 2×2, cada una con sus dos brazos: los pools entrenados con la puerta y los
# entrenados con el reflejo. Las de 24 000 ticks no se repitieron (F11g y F11i ya las tenían).
CELDAS_2X2 = {
    (10, 24): (P10G7_REPLICAS, P10X10_REPLICAS),
    (10, 48): (P10H2G_REPLICAS, P10H2S_REPLICAS),
    (25, 24): (P25G7_REPLICAS, P257_REPLICAS),
    (25, 48): (P25H2G_REPLICAS, P25H2S_REPLICAS),
}

RE_R_SECCION = re.compile(r"^## Jugado con la glándula a (\d+) s")
RE_R_FILA = re.compile(r"^\|\s*(\S+)\s*\|\s*(puerta|fijo)\s*\|\s*([\d.]+)\s*±")
# El volcado del 2×2 reutiliza los títulos de sus secciones para dos cosas distintas (la tabla de
# efectos y, más abajo, el reparto), así que hay que recordar en cuál de las dos se está.
RE_M_TABLA = re.compile(r"^## Jugado con la glándula a (\d+) s · rastro (\w+)")
RE_M_DELTAS = re.compile(r"^### jugado (\d+) s · rastro (\w+)")
RE_M_FILA = re.compile(r"^\| (\d+) s \| (\d+)k \| ([+-]?[\d.]+) ± ([\d.]+) \|")
RE_M_DELTA = re.compile(
    r"^- \*\*horizonte a (\d+) s\*\*: .* = ([+-][\d.]+) ± ([\d.]+) descargas "
    r"\(t = ([+-][\d.]+);")
RE_M_BASE = re.compile(r"^- castigo base \(10 s / 24k\): \*\*([+-][\d.]+)\*\*")
RE_M_HORIZONTE2 = re.compile(r"^- horizonte ×2 con el precio quieto: \*\*([+-][\d.]+)\*\*")
RE_M_PRECIO = re.compile(
    r"^- precio 10 s → 25 s con el horizonte corto: \*\*([+-][\d.]+)\*\*")
RE_M_REPARTO = re.compile(
    r"^- celda 25 s / 48k: \*\*([+-][\d.]+)\*\* · predicción aditiva ([+-][\d.]+) ⇒ "
    r"interacción \*\*([+-][\d.]+)\*\*")


def _sorteo(pool: str) -> int:
    """El sorteo de GA de un pool, leído de su sufijo («p25h2g-s11» → 11)."""
    cola = pool.rsplit("-s", 1)[-1]
    if not cola.isdigit():
        raise FaltaCifra(f"pool sin sorteo reconocible: {pool}")
    return int(cola)


@lru_cache(maxsize=None)
def _medias_pool(raiz: Path, fuente: str) -> dict[tuple[int, str, int], float]:
    """Las medias por pool de una campaña de réplicas: (precio de juego, rastro, sorteo) → media."""
    out: dict[tuple[int, str, int], float] = {}
    precio = None
    for linea in leer(raiz, fuente):
        linea = linea.replace("\r", "")
        m = RE_R_SECCION.match(linea)
        if m:
            precio = int(m.group(1))
            continue
        if linea.startswith("#"):
            precio = None
            continue
        m = RE_R_FILA.match(linea)
        if m and precio is not None:
            out[(precio, m.group(2), _sorteo(m.group(1)))] = float(m.group(3))
    if not out:
        raise FaltaCifra(f"{fuente}: no hay medias por pool en el modo réplicas")
    return out


@lru_cache(maxsize=None)
def _replicas_2x2(raiz: Path) -> dict:
    """Las ocho campañas del 2×2: casilla → (medias del brazo con puerta, del brazo con reflejo)."""
    return {celda: tuple(_medias_pool(raiz, f) for f in fuentes)
            for celda, fuentes in CELDAS_2X2.items()}


@lru_cache(maxsize=None)
def sorteos_matriz(raiz: Path) -> dict:
    """Los sorteos con que se lee cada casilla.

    Primero los comunes a los dos brazos de la casilla; después, dentro de una fila de PRECIO, los
    comunes a las dos casillas —así el salto de horizonte sale pareado sorteo a sorteo, que es la
    razón de haber entrenado la celda nueva con 7/11/13 y no con otros—. Es la misma regla que
    aplica `scripts/gland-matrix-compare.py` al repartir.
    """
    d = _replicas_2x2(raiz)
    lectura = {c: {k[2] for k in g} & {k[2] for k in s} for c, (g, s) in d.items()}
    for precio in {c[0] for c in d}:
        fila = [c for c in d if c[0] == precio]
        if len(fila) != 2:
            raise FaltaCifra(f"la fila de {precio} s no tiene dos casillas: {fila}")
        comunes = lectura[fila[0]] & lectura[fila[1]]
        if len(comunes) >= 2:
            for c in fila:
                lectura[c] = comunes
    for celda, s in lectura.items():
        if len(s) < 2:
            raise FaltaCifra(f"la casilla {celda[0]}s/{celda[1]}k no tiene dos sorteos con las "
                             f"dos recetas: {sorted(s)}")
    return {c: sorted(s) for c, s in lectura.items()}


def _nivel(raiz: Path, celda: tuple[int, int], juego: int, rastro: str, brazo: str,
           sort: int) -> float:
    """La media de un pool: una casilla, un precio de juego, un rastro y un sorteo."""
    armas = _replicas_2x2(raiz)[celda]
    tabla = armas[0] if brazo == "puerta" else armas[1]
    try:
        return tabla[(juego, rastro, sort)]
    except KeyError as e:
        raise FaltaCifra(f"falta el pool del sorteo {sort} en {celda[0]}s/{celda[1]}k · "
                         f"{brazo} a {juego} s · {rastro}") from e


def nivel_matriz(raiz: Path, celda: tuple[int, int], juego: int, rastro: str,
                 brazo: str) -> float:
    """El nivel de un brazo en una casilla: la media de los pools de los sorteos que la leen."""
    return statistics.mean([_nivel(raiz, celda, juego, rastro, brazo, s)
                            for s in sorteos_matriz(raiz)[celda]])


def matriz(raiz: Path) -> dict:
    """El volcado del 2×2: el efecto de cada casilla, los saltos de horizonte y el reparto."""
    out: dict = {"efecto": {}, "delta": {}, "reparto": {}}
    estado = None
    en_reparto = False
    for linea in leer(raiz, P25H2_VEREDICTO):
        linea = linea.replace("\r", "")
        m = RE_M_TABLA.match(linea)
        if m:
            estado = ("tabla", int(m.group(1)), m.group(2))
            continue
        if linea.startswith("## Reparto del castigo"):
            en_reparto, estado = True, None
            continue
        m = RE_M_DELTAS.match(linea)
        if m:
            estado = ("reparto" if en_reparto else "deltas", int(m.group(1)), m.group(2))
            continue
        if linea.startswith("## ") or linea.startswith("### "):
            estado = None
            continue
        if estado is None:
            continue
        tipo, juego, rastro = estado
        if tipo == "tabla":
            m = RE_M_FILA.match(linea)
            if m:
                out["efecto"][(juego, rastro, int(m.group(1)), int(m.group(2)))] = float(m.group(3))
            continue
        if tipo == "deltas":
            m = RE_M_DELTA.match(linea)
            if m:
                out["delta"][(juego, rastro, int(m.group(1)))] = (
                    float(m.group(2)), float(m.group(3)), float(m.group(4)))
            continue
        r = out["reparto"].setdefault((juego, rastro), {})
        m = RE_M_BASE.match(linea)
        if m:
            r["base"] = float(m.group(1))
            continue
        m = RE_M_HORIZONTE2.match(linea)
        if m:
            r["horizonte"] = float(m.group(1))
            continue
        m = RE_M_PRECIO.match(linea)
        if m:
            r["precio"] = float(m.group(1))
            continue
        m = RE_M_REPARTO.match(linea)
        if m:
            r["celda"], r["prediccion"], r["interaccion"] = (
                float(m.group(1)), float(m.group(2)), float(m.group(3)))
    if not out["efecto"]:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: no se reconoce ninguna casilla del 2×2")
    if not out["delta"]:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: no se reconoce ningún salto de horizonte")
    return out


def celda_matriz(raiz: Path, precio: int, horizonte: int) -> dict:
    """Una fila de la tabla de F11l, jugada a 10 s de glándula y con el rastro fijo.

    El efecto sale del volcado (media pareada por sorteo); los dos niveles se promedian aquí de las
    medias por pool de las réplicas. Por eso el efecto puede diferir en una décima de la resta de
    las dos columnas redondeadas: el volcado resta medias sin redondear.
    """
    pu = nivel_matriz(raiz, (precio, horizonte), 10, "fijo", "puerta")
    fi = nivel_matriz(raiz, (precio, horizonte), 10, "fijo", "reflejo")
    ef = matriz(raiz)["efecto"].get((10, "fijo", precio, horizonte))
    if ef is None:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: no hay efecto de {precio}s/{horizonte}k a 10 s · fijo")
    if fi == 0:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: la referencia de {precio}s/{horizonte}k es 0")
    return {"pu": num(pu), "fi": num(fi), "ef": num(ef), "pct": num(ef / fi * 100)}


def salto_horizonte_matriz(raiz: Path, precio: int) -> dict:
    """Cuánto mueve doblar el horizonte al efecto de la puerta, con ese precio de entrenamiento."""
    v = matriz(raiz)["delta"].get((10, "fijo", precio))
    if v is None:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: no hay salto de horizonte a {precio} s a 10 s · fijo")
    return {"m": num_mas(v[0]), "se": num(v[1]), "t": num_mas(v[2])}


def _delta_sorteo(raiz: Path, precio: int, sort: int) -> float:
    """El salto de horizonte de UN sorteo: el mismo pool a 24k y a 48k, misma semilla de partida."""
    def efecto(celda: tuple[int, int]) -> float:
        return (_nivel(raiz, celda, 10, "fijo", "puerta", sort)
                - _nivel(raiz, celda, 10, "fijo", "reflejo", sort))
    return efecto((precio, 48)) - efecto((precio, 24))


def saltos_por_sorteo(raiz: Path, precio: int) -> dict:
    """Los tres deltas individuales del salto de horizonte.

    El volcado publica la media pareada y el t, pero no los deltas de cada sorteo —los nombra sin
    cifra—, así que se calculan aquí de las medias por pool. Es lo que hizo concluyente el control de
    10 s: no la media, sino que los tres sorteos se movieran juntos.
    """
    ds = [_delta_sorteo(raiz, precio, s) for s in sorteos_matriz(raiz)[(precio, 48)]
          if s in sorteos_matriz(raiz)[(precio, 24)]]
    if len(ds) != 3:
        raise FaltaCifra(f"el salto de horizonte a {precio} s no tiene tres sorteos pareados: {ds}")
    return {"a": num_mas(ds[0]), "b": num_mas(ds[1]), "c": num_mas(ds[2])}


def reparto_matriz(raiz: Path) -> dict:
    """El reparto del castigo: base, los dos saltos, la predicción aditiva y la interacción."""
    r = matriz(raiz)["reparto"].get((10, "fijo"))
    if not r or len(r) < 5:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: no hay reparto a 10 s · fijo ({sorted(r or {})})")
    return {"base": num(r["base"]), "dh": num(r["horizonte"]), "dp": num_mas(r["precio"]),
            "celda": num(r["celda"]), "pred": num(r["prediccion"]),
            "inter": num_mas(r["interaccion"])}


def interacciones_matriz(raiz: Path) -> dict:
    """Los extremos de la interacción en las cuatro formas de jugar el 2×2."""
    vals = [d["interaccion"] for d in matriz(raiz)["reparto"].values() if "interaccion" in d]
    if len(vals) != 4:
        raise FaltaCifra(f"{P25H2_VEREDICTO}: esperaba cuatro interacciones y hay {len(vals)}")
    return {"a": num_mas(min(vals)), "b": num_mas(max(vals))}


def rango_celda_nueva(raiz: Path) -> dict:
    """El efecto de la celda nueva sorteo a sorteo: la dispersión contra la que hay que leerla."""
    celda = (25, 48)
    ef = [_nivel(raiz, celda, 10, "fijo", "puerta", s)
          - _nivel(raiz, celda, 10, "fijo", "reflejo", s) for s in sorteos_matriz(raiz)[celda]]
    return {"lo": num(min(ef)), "hi": num_mas(max(ef))}


def k_sin_precio_menos(raiz: Path) -> dict:
    """Cuánto menos de cresta deja el brazo de la puerta sin precio."""
    g = media_celdas(raiz, 0, 48000, "p10h2g", "puerta", "comida")
    s = media_celdas(raiz, 0, 48000, "p10h2s", "fijo", "comida")
    return {"p": str(round((1 - g / s) * 100)), "a": miles(g, 1), "b": miles(s, 1)}


# ── F11n: el enfrentamiento del lanzador (par a 25 s contra el juego de fábrica) ──
# Este volcado es el único que escribe pareados como viñetas —«- **nombre**: cifra»— y los
# brazos como filas de tabla, así que se lee por el NOMBRE de la viñeta y no por su posición:
# reordenar el generador no mueve ninguna cifra, y cambiarle el nombre a una viñeta se declara
# en vez de derivar otra cosa.
RE_DUEL_VINETA = re.compile(r"^- \*\*(?P<n>[^*]+)\*\*: (?P<resto>.+)$")
# El generador no es regular con la palabra «descargas»: unas viñetas la llevan y otras van de la
# media al «(t = …)» directamente. Se acepta en las dos formas, y el «gana en» también es opcional
# porque las viñetas de piezas dicen «(t = …; 13/24 semillas)».
RE_DUEL_PAR = re.compile(
    r"^(?P<m>[+-][\d.]+) ± (?P<se>[\d.]+) (?:descargas )?\(t = (?P<t>[+-][\d.]+); "
    r"(?:gana en )?(?P<w>\d+)/(?P<n>\d+) semillas")
RE_DUEL_DENTRO = re.compile(
    r"^(?P<m>[+-][\d.]+) ± (?P<se>[\d.]+) descargas al promediar las siete tiradas "
    r"\(rango por tirada (?P<lo>[+-][\d.]+) a (?P<hi>[+-][\d.]+)\)$")


def duel_vineta(raiz: Path, nombre: str) -> str:
    """Lo que sigue a los dos puntos en la viñeta que lleva ese nombre en negrita."""
    for linea in leer(raiz, P10H2_DUEL):
        m = RE_DUEL_VINETA.match(linea)
        if m and m["n"].strip() == nombre:
            return m["resto"].strip()
    raise FaltaCifra(f"{P10H2_DUEL}: no hay viñeta «{nombre}»")


def duel_pareado(raiz: Path, nombre: str) -> dict:
    """Media, error típico, t y semillas a favor de un pareado del enfrentamiento."""
    m = RE_DUEL_PAR.match(duel_vineta(raiz, nombre))
    if not m:
        raise FaltaCifra(f"{P10H2_DUEL}: la viñeta «{nombre}» no trae media ± se, t y semillas")
    return {"m": num_mas(float(m["m"])), "se": num(float(m["se"])),
            "t": num_mas(float(m["t"])), "w": m["w"], "n": m["n"]}


def duel_dentro(raiz: Path) -> dict:
    """La puerta dentro del pool que la entrenó: promediada sobre las siete tiradas, con su rango."""
    m = RE_DUEL_DENTRO.match(duel_vineta(raiz, "la puerta dentro de su pool a 25 s"))
    if not m:
        raise FaltaCifra(f"{P10H2_DUEL}: la viñeta de la puerta dentro de su pool no trae su rango")
    return {"m": num_mas(float(m["m"])), "se": num(float(m["se"])),
            "lo": num(float(m["lo"])), "hi": num_mas(float(m["hi"]))}


def duel_criterio(raiz: Path, nombre: str) -> str:
    """Lo que sigue a los dos puntos en una viñeta del criterio (ahí el nombre NO va en negrita)."""
    patron = re.compile(r"^- " + re.escape(nombre) + r": (?P<resto>.+)$")
    for linea in leer(raiz, P10H2_DUEL):
        m = patron.match(linea)
        if m:
            return m["resto"].strip()
    raise FaltaCifra(f"{P10H2_DUEL}: no hay viñeta del criterio «{nombre}»")


def duel_dispersion(raiz: Path) -> dict:
    """La dispersión entre tiradas de cada receta, que el volcado publica al lado del margen."""
    resto = duel_criterio(raiz, "margen del par")
    m = re.search(r"dispersión entre tiradas de su receta: \*\*(?P<v>[\d.]+)\*\*", resto)
    if not m:
        raise FaltaCifra(f"{P10H2_DUEL}: el margen del par no trae la dispersión de su receta")
    dr = duel_criterio(raiz, "dispersión entre tiradas de la receta del reflejo").strip("*")
    try:
        return {"d": num(float(m["v"])), "dr": num(float(dr))}
    except ValueError as e:
        raise FaltaCifra(f"{P10H2_DUEL}: la dispersión del reflejo no es un número ({dr!r})") from e


def duel_brazo(raiz: Path, etiqueta: str) -> dict:
    """Media y σ de un brazo de la tabla: el pedestal con el que se calibra el banco."""
    for linea in leer(raiz, P10H2_DUEL):
        if not linea.startswith("| "):
            continue
        celdas = [c.strip() for c in linea.strip().strip("|").split("|")]
        if len(celdas) < 6 or celdas[0] != etiqueta:
            continue
        partes = celdas[4].split("±")
        if len(partes) != 2:
            raise FaltaCifra(f"{P10H2_DUEL}: la fila «{etiqueta}» no trae «media ± σ»")
        return {"mean": num(float(partes[0].strip())), "sd": num(float(partes[1].strip()))}
    raise FaltaCifra(f"{P10H2_DUEL}: no hay fila de brazo «{etiqueta}»")


def duel_cuenta(raiz: Path) -> dict:
    """Cuántas partidas sostienen el reparto: brazos de la tabla por semillas de la cabecera."""
    brazos = semillas = 0
    for linea in leer(raiz, P10H2_DUEL):
        t = linea.strip()
        if t.startswith("| ") and not set(t) <= set("|- "):
            # La cabecera empieza por «brazo»; las filas de dato, por la etiqueta del brazo.
            if t.strip("|").split("|")[0].strip() != "brazo":
                brazos += 1
        if t.startswith("# semillas: "):
            semillas = len([s for s in t.split(":", 1)[1].split(",") if s.strip()])
    if not brazos or not semillas:
        raise FaltaCifra(f"{P10H2_DUEL}: no pude contar brazos ({brazos}) o semillas ({semillas})")
    return {"brazos": str(brazos), "semillas": str(semillas), "partidas": str(brazos * semillas)}


def _casilla_2x2(precio: int, horizonte: int) -> str:
    """Las fuentes de una casilla del 2×2: el volcado del reparto y sus dos campañas de réplicas."""
    return " + ".join((P25H2_VEREDICTO,) + CELDAS_2X2[(precio, horizonte)])


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

    # ── F11j: la tabla del horizonte (cada fila completa: nivel, efecto y relativo) ──
    ("F11j tabla · 10 s · fijo · 24k", P10H2_VEREDICTO,
     "| 10 s · fijo | 24k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 10 s · fijo · 48k", P10H2_VEREDICTO,
     "| 10 s · fijo | 48k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 10 s · puerta · 24k", P10H2_VEREDICTO,
     "| 10 s · puerta | 24k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 10 s · puerta · 48k", P10H2_VEREDICTO,
     "| 10 s · puerta | 48k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 25 s · fijo · 24k", P10H2_VEREDICTO,
     "| 25 s · fijo | 24k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 25 s · fijo · 48k", P10H2_VEREDICTO,
     "| 25 s · fijo | 48k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 25 s · puerta · 24k", P10H2_VEREDICTO,
     "| 25 s · puerta | 24k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11j tabla · 25 s · puerta · 48k", P10H2_VEREDICTO,
     "| 25 s · puerta | 48k | {pu} | {fi} | {ef} | {pct} % |"),
    # los deltas pareados por sorteo: la respuesta del control
    ("F11j delta · 10 s · fijo", P10H2_VEREDICTO,
     "{m} ± {se} descargas jugando a 10 s con el reflejo (t = {t}; {wins}/3 sorteos a favor)"),
    ("F11j delta · 10 s · puerta", P10H2_VEREDICTO,
     "{m} ± {se} a 10 s con la puerta (t = {t}; {wins}/3)"),
    ("F11j delta · 25 s · fijo", P10H2_VEREDICTO,
     "{m} ± {se} a 25 s con el reflejo (t = {t}; {wins}/3)"),
    ("F11j delta · 25 s · puerta", P10H2_VEREDICTO,
     "{m} ± {se} a 25 s con la puerta (t = {t}; {wins}/3)"),
    ("F11j deltas por sorteo · 10 s · fijo", P10H2_VEREDICTO, "({a} / {b} / {c})"),
    ("F11j el único delta a favor", P10H2_VEREDICTO,
     "la semilla 47 a 25 s con la puerta ({v})"),
    ("F11j rangos entre tiradas", P10H2_VEREDICTO,
     "va de {lo} a {hi} entre sorteos y la del reflejo de {lo2} a {hi2}"),
    ("F11j rango de los rangos", P10H2_VEREDICTO, "un rango de {a} a {b} descargas"),
    ("F11j relativo de las cuatro celdas", P10H2_VEREDICTO,
     "el castigo pasa de {a} % a {b} % (10 s, reflejo), de {c} % a {d} % (10 s, puerta), "
     "de {e} % a {f} % (25 s, reflejo) y de {g} % a {h} % (25 s, puerta)"),
    ("F11j nivel con el reflejo a 10 s", P10H2_VEREDICTO,
     "con el reflejo a 10 s pasa de {a} a {b} descargas"),

    # ── F11k: lo que deposita cada brazo ──
    ("F11k tabla · puerta · puerta", P10H2_DEPOSITS,
     "| entrenado con puerta | puerta | {comida} | {casa} | {negado} % |"),
    ("F11k tabla · puerta · reflejo", P10H2_DEPOSITS,
     "| entrenado con puerta | reflejo | {comida} | {casa} | {negado} % |"),
    ("F11k tabla · reflejo · puerta", P10H2_DEPOSITS,
     "| entrenado con reflejo | puerta | {comida} | {casa} | {negado} % |"),
    ("F11k tabla · reflejo · reflejo", P10H2_DEPOSITS,
     "| entrenado con reflejo | reflejo | {comida} | {casa} | {negado} % |"),
    ("F11k cresta · brazo de la puerta", P10H2_DEPOSITS,
     "cae de {a} (sin precio) a {b} en el brazo de la puerta"),
    ("F11k cresta · brazo del reflejo", P10H2_DEPOSITS,
     "de {c} a {d} en el del reflejo: ×{x} y ×{y}"),
    ("F11k casa al abrir la puerta", P10H2_DEPOSITS,
     "baja el rastro de casa de {a} a {b} (y de {c} a {d} en el brazo del reflejo)"),
    ("F11k la cresta sube", P10H2_DEPOSITS,
     "de {a} a {b}, con 24/24 semillas a favor en {n24} de las doce celdas y 23/24 en las otras {n23}"),
    ("F11k ventana sin precio", P10H2_DEPOSITS,
     "sin precio el acumulado crece ×{a}–{b} en los veinticuatro brazos"),
    ("F11k ventana sin precio · 48k", P10H2_DEPOSITS, "×{a}–{b} en los doce entrenados a 48 000"),
    ("F11k ventana con precio", P10H2_DEPOSITS,
     "crece ×{a}–{b} con el reflejo fijo y ×{c}–{d} con la puerta"),
    ("F11k casa y glándula de un pool", P10H2_DEPOSITS,
     "({a} → {b} en p10h2g-s41) y el presupuesto gastado también ({c} → {d} s)"),
    ("F11k el negado", P10H2_DEPOSITS, "entre el {a} y el {b} % de lo pedido se queda sin presupuesto"),
    ("F11k dispersión de la cresta", P10H2_DEPOSITS, "va de {a} a {b} a 48k"),
    ("F11k puerta contra reflejo", P10H2_DEPOSITS,
     "{a} de cresta contra {b} del entrenado con reflejo; con el reflejo fijo, {c} contra {d}"),
    ("F11k sin precio 30 % menos", P10H2_DEPOSITS,
     "un {p} % MENOS de cresta que el del reflejo ({a} contra {b})"),

    # ── F11l: el 2×2 completo (tabla jugada a 10 s de glándula y con el rastro fijo) ──
    # Los niveles vienen de las réplicas y el efecto del volcado del reparto: las dos fuentes de
    # cada fila van declaradas, porque `--list` es lo que un lector sigue para llegar a la cifra.
    ("F11l tabla · 10s/24k", _casilla_2x2(10, 24), "| 10 s | 24k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11l tabla · 10s/48k", _casilla_2x2(10, 48), "| 10 s | 48k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11l tabla · 25s/24k", _casilla_2x2(25, 24), "| 25 s | 24k | {pu} | {fi} | {ef} | {pct} % |"),
    ("F11l tabla · 25s/48k", _casilla_2x2(25, 48), "| 25 s | 48k | {pu} | {fi} | {ef} | {pct} % |"),
    # el salto de horizonte, que es la pregunta de la celda: a 10 s cuesta, a 25 s no
    ("F11l salto de horizonte", P25H2_VEREDICTO,
     "{m10} ± {se10} descargas a 10 s (t = {t10}) y {m25} ± {se25} a 25 s (t = {t25})"),
    ("F11l deltas por sorteo · 25 s", _casilla_2x2(25, 48), "({a} / {b} / {c} al doblar la ventana)"),
    # el reparto: aditivo o con interacción
    ("F11l reparto", P25H2_VEREDICTO,
     "base {base}, horizonte ×2 a 10 s {dh}, precio a 24k {dp}, predicción aditiva {pred} "
     "y celda real {celda}: interacción {inter}"),
    ("F11l interacciones en las cuatro formas de jugar", P25H2_VEREDICTO,
     "la interacción va de {a} a {b} descargas en las cuatro formas de jugar"),
    ("F11l dispersión de la celda nueva", _casilla_2x2(25, 48), "va de {lo} a {hi} entre sorteos"),

    # ── F11n: el enfrentamiento del lanzador (par a 25 s contra el juego de hoy) ──
    # Los pareados se citan con sus semillas porque son la mitad del criterio pre-registrado:
    # sin ellas, la cifra no dice si se adoptó con margen o por un sorteo afortunado.
    ("F11n · el par contra fábrica", P10H2_DUEL,
     "el par a 25 s gana {m} ± {se} descargas al juego de hoy (t = {t}; {w}/{n} semillas)"),
    ("F11n · la receta del reflejo contra fábrica", P10H2_DUEL,
     "la receta del reflejo a 25 s gana {m} ± {se} (t = {t}; {w}/{n} semillas)"),
    ("F11n · el par sobre su receta", P10H2_DUEL,
     "el par le gana a su propia receta del reflejo {m} ± {se} descargas (t = {t}; {w}/{n} semillas)"),
    ("F11n · la dispersión de cada receta", P10H2_DUEL,
     "la dispersión entre tiradas es {d} en la receta del par y {dr} en la del reflejo"),
    ("F11n · la puerta dentro de su pool", P10H2_DUEL,
     "la puerta dentro del pool que la entrenó: {m} ± {se} descargas al promediar las siete "
     "tiradas (rango por tirada {lo} a {hi})"),
    ("F11n · la puerta sola", P10H2_DUEL,
     "la puerta sola sobre el pool de fábrica: {m} ± {se} (t = {t}; {w}/{n} semillas)"),
    ("F11n · el precio solo", P10H2_DUEL,
     "el precio solo sobre el pool de fábrica: {m} ± {se} (t = {t}; {w}/{n} semillas)"),
    ("F11n · el par sobre el pool de fábrica", P10H2_DUEL,
     "el par completo sobre el pool de fábrica: {m} ± {se} (t = {t}; {w}/{n} semillas)"),
    ("F11n · pedestal de fábrica", P10H2_DUEL,
     "los dos brazos de fábrica dan {a} ± {b} con la glándula apagada y {c} ± {d} a 25 s"),
    ("F11n · la cuenta de partidas", P10H2_DUEL,
     "{brazos} brazos por {semillas} semillas: {partidas} partidas"),
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
    # F11j
    "F11j tabla · 10 s · fijo · 24k": lambda r: celda_horizonte(r, 10, "fijo", 24),
    "F11j tabla · 10 s · fijo · 48k": lambda r: celda_horizonte(r, 10, "fijo", 48),
    "F11j tabla · 10 s · puerta · 24k": lambda r: celda_horizonte(r, 10, "puerta", 24),
    "F11j tabla · 10 s · puerta · 48k": lambda r: celda_horizonte(r, 10, "puerta", 48),
    "F11j tabla · 25 s · fijo · 24k": lambda r: celda_horizonte(r, 25, "fijo", 24),
    "F11j tabla · 25 s · fijo · 48k": lambda r: celda_horizonte(r, 25, "fijo", 48),
    "F11j tabla · 25 s · puerta · 24k": lambda r: celda_horizonte(r, 25, "puerta", 24),
    "F11j tabla · 25 s · puerta · 48k": lambda r: celda_horizonte(r, 25, "puerta", 48),
    "F11j delta · 10 s · fijo": lambda r: delta_horizonte(r, 10, "fijo"),
    "F11j delta · 10 s · puerta": lambda r: delta_horizonte(r, 10, "puerta"),
    "F11j delta · 25 s · fijo": lambda r: delta_horizonte(r, 25, "fijo"),
    "F11j delta · 25 s · puerta": lambda r: delta_horizonte(r, 25, "puerta"),
    "F11j deltas por sorteo · 10 s · fijo": lambda r: {
        "a": num(sorteos_horizonte(r, 10, "fijo")[0]),
        "b": num(sorteos_horizonte(r, 10, "fijo")[1]),
        "c": num(sorteos_horizonte(r, 10, "fijo")[2])},
    "F11j el único delta a favor": lambda r: _unico_a_favor(r),
    "F11j rangos entre tiradas": lambda r: _rangos_entre_tiradas(r),
    "F11j rango de los rangos": lambda r: _rangos_entre_tiradas(r),
    "F11j relativo de las cuatro celdas": lambda r: {
        "a": celda_horizonte(r, 10, "fijo", 24)["pct"],
        "b": celda_horizonte(r, 10, "fijo", 48)["pct"],
        "c": celda_horizonte(r, 10, "puerta", 24)["pct"],
        "d": celda_horizonte(r, 10, "puerta", 48)["pct"],
        "e": celda_horizonte(r, 25, "fijo", 24)["pct"],
        "f": celda_horizonte(r, 25, "fijo", 48)["pct"],
        "g": celda_horizonte(r, 25, "puerta", 24)["pct"],
        "h": celda_horizonte(r, 25, "puerta", 48)["pct"]},
    "F11j nivel con el reflejo a 10 s": lambda r: {
        "a": celda_horizonte(r, 10, "fijo", 24)["fi"],
        "b": celda_horizonte(r, 10, "fijo", 48)["fi"]},
    # F11k
    "F11k tabla · puerta · puerta": lambda r: k_tabla(r, "p10h2g", "puerta"),
    "F11k tabla · puerta · reflejo": lambda r: k_tabla(r, "p10h2g", "fijo"),
    "F11k tabla · reflejo · puerta": lambda r: k_tabla(r, "p10h2s", "puerta"),
    "F11k tabla · reflejo · reflejo": lambda r: k_tabla(r, "p10h2s", "fijo"),
    "F11k cresta · brazo de la puerta": lambda r: k_cresta(r),
    "F11k cresta · brazo del reflejo": lambda r: k_cresta(r),
    "F11k casa al abrir la puerta": lambda r: {
        "a": miles(media_celdas(r, 10, 48000, "p10h2g", "fijo", "casa")),
        "b": miles(media_celdas(r, 10, 48000, "p10h2g", "puerta", "casa")),
        "c": miles(media_celdas(r, 10, 48000, "p10h2s", "fijo", "casa")),
        "d": miles(media_celdas(r, 10, 48000, "p10h2s", "puerta", "casa"))},
    "F11k la cresta sube": lambda r: k_sube(r),
    "F11k ventana sin precio": lambda r: k_ventana(r, 0),
    "F11k ventana sin precio · 48k": lambda r: k_ventana(r, 0, solo_48k=True),
    "F11k ventana con precio": lambda r: k_ventana_por_rastro(r),
    "F11k casa y glándula de un pool": lambda r: k_casa_y_glandula(r),
    "F11k el negado": lambda r: {
        "a": str(min(v[2] for (_p, _w, _b, _s, _r), v in depositos(r)["celdas"].items()
                      if _p == 10)),
        "b": str(max(v[2] for (_p, _w, _b, _s, _r), v in depositos(r)["celdas"].items()
                      if _p == 10))},
    "F11k dispersión de la cresta": lambda r: {
        "a": str(round(min(valores_pool(r, 10, 48000, "p10h2g", "puerta", "comida")))),
        "b": str(round(max(valores_pool(r, 10, 48000, "p10h2g", "puerta", "comida"))))},
    "F11k puerta contra reflejo": lambda r: k_puerta_contra_reflejo(r),
    "F11k sin precio 30 % menos": lambda r: k_sin_precio_menos(r),
    # F11l
    "F11l tabla · 10s/24k": lambda r: celda_matriz(r, 10, 24),
    "F11l tabla · 10s/48k": lambda r: celda_matriz(r, 10, 48),
    "F11l tabla · 25s/24k": lambda r: celda_matriz(r, 25, 24),
    "F11l tabla · 25s/48k": lambda r: celda_matriz(r, 25, 48),
    "F11l salto de horizonte": lambda r: {
        **{k + "10": v for k, v in salto_horizonte_matriz(r, 10).items()},
        **{k + "25": v for k, v in salto_horizonte_matriz(r, 25).items()}},
    "F11l deltas por sorteo · 25 s": lambda r: saltos_por_sorteo(r, 25),
    "F11l reparto": lambda r: reparto_matriz(r),
    "F11l interacciones en las cuatro formas de jugar": lambda r: interacciones_matriz(r),
    "F11l dispersión de la celda nueva": lambda r: rango_celda_nueva(r),
    # F11n
    "F11n · el par contra fábrica": lambda r: duel_pareado(r, "el par candidato " + MINUS +
                                                          " el juego de hoy"),
    "F11n · la receta del reflejo contra fábrica": lambda r: duel_pareado(
        r, "la receta del reflejo a 25 s " + MINUS + " el juego de hoy"),
    "F11n · el par sobre su receta": lambda r: duel_pareado(
        r, "el par candidato " + MINUS + " la receta del reflejo a 25 s"),
    "F11n · la dispersión de cada receta": lambda r: duel_dispersion(r),
    "F11n · la puerta dentro de su pool": lambda r: duel_dentro(r),
    "F11n · la puerta sola": lambda r: duel_pareado(
        r, "la puerta sola, con la glándula apagada (fábrica)"),
    "F11n · el precio solo": lambda r: duel_pareado(r, "el precio solo, con el reflejo (fábrica)"),
    "F11n · el par sobre el pool de fábrica": lambda r: duel_pareado(r, "el par sobre el pool de fábrica"),
    "F11n · pedestal de fábrica": lambda r: {
        "a": duel_brazo(r, "fabrica" + MIDOT + "reflejo" + MIDOT + "0s")["mean"],
        "b": duel_brazo(r, "fabrica" + MIDOT + "reflejo" + MIDOT + "0s")["sd"],
        "c": duel_brazo(r, "fabrica" + MIDOT + "reflejo" + MIDOT + "25s")["mean"],
        "d": duel_brazo(r, "fabrica" + MIDOT + "reflejo" + MIDOT + "25s")["sd"]},
    "F11n · la cuenta de partidas": lambda r: duel_cuenta(r),
}


def _unico_a_favor(raiz: Path) -> dict:
    """El único delta por sorteo positivo de las cuatro celdas (y que sea único)."""
    todos = [d for clave in ((10, "fijo"), (10, "puerta"), (25, "fijo"), (25, "puerta"))
             for d in sorteos_horizonte(raiz, *clave)]
    if sum(1 for d in todos if d > 0) != 1:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: no hay un único delta por sorteo a favor")
    return {"v": num_mas(max(todos))}


def _rangos_entre_tiradas(raiz: Path) -> dict:
    """Los extremos de las medias por pool jugadas a 10 s, y el ancho de cada rastro."""
    pu = replicas_juego(raiz, P10H2G_REPLICAS, "puerta") + \
        replicas_juego(raiz, P10H2S_REPLICAS, "puerta")
    fi = replicas_juego(raiz, P10H2G_REPLICAS, "fijo") + \
        replicas_juego(raiz, P10H2S_REPLICAS, "fijo")
    if len(pu) != 6 or len(fi) != 6:
        raise FaltaCifra(f"{P10H2_VEREDICTO}: esperaba seis pools nuevos por rastro")
    return {"lo": num(min(pu)), "hi": num(max(pu)), "lo2": num(min(fi)), "hi2": num(max(fi)),
            "a": str(round(max(pu) - min(pu))), "b": str(round(max(fi) - min(fi)))}


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
def _falsos_f11jk() -> dict[str, str]:
    """Volcados de mentira para F11j y F11k: formato real y cifras legibles a mano.

    No se copian las cifras de `artifacts/`: el selftest tiene que correr sin repo, y si copiara
    el volcado real no probaría el parser, sólo la igualdad con un fichero.
    """
    veredicto = "\n".join([
        "## Jugado con la glándula a 10 s · rastro fijo",
        "| horizonte | brazo con puerta | brazo con reflejo | efecto |",
        "|---|---|---|---|",
        "| 24k | 149.6 | 167.3 | -17.7 |",
        "| 48k | 197.6 | 233.4 | -35.8 |",
        "## Jugado con la glándula a 10 s · rastro puerta",
        "| 24k | 151.6 | 170.2 | -18.5 |",
        "| 48k | 201.6 | 234.6 | -33.0 |",
        "## Jugado con la glándula a 25 s · rastro fijo",
        "| 24k | 158.9 | 173.8 | -14.8 |",
        "| 48k | 195.4 | 230.3 | -34.9 |",
        "## Jugado con la glándula a 25 s · rastro puerta",
        "| 24k | 154.8 | 177.3 | -22.5 |",
        "| 48k | 205.8 | 247.8 | -42.0 |",
        "## Cuánto mueve el horizonte el efecto de la puerta",
        "### jugado 10 s · rastro fijo",
        "- **48k − 24k**: el efecto pasa de -17.7 a -35.8 → -18.1 ± 1.1 descargas "
        "(t = -17.0; 0/3 sorteos a favor)",
        "  - por sorteo: s41 -33.5 → -50.8 = -17.3 · s43 -19.7 → -36.5 = -16.8 · "
        "s47 +0.0 → -20.2 = -20.2",
        "### jugado 10 s · rastro puerta",
        "- **48k − 24k**: el efecto pasa de -18.5 a -33.0 → -14.5 ± 6.0 descargas "
        "(t = -2.4; 0/3 sorteos a favor)",
        "  - por sorteo: s41 -30.7 → -52.2 = -21.5 · s43 -19.1 → -38.4 = -19.3 · "
        "s47 -5.8 → -8.4 = -2.6",
        "### jugado 25 s · rastro fijo",
        "- **48k − 24k**: el efecto pasa de -14.8 a -34.9 → -20.1 ± 5.2 descargas "
        "(t = -3.9; 0/3 sorteos a favor)",
        "  - por sorteo: s41 -31.4 → -59.5 = -28.0 · s43 -24.2 → -34.7 = -10.4 · "
        "s47 +11.2 → -10.6 = -21.7",
        "### jugado 25 s · rastro puerta",
        "- **48k − 24k**: el efecto pasa de -22.5 a -42.0 → -19.5 ± 13.8 descargas "
        "(t = -1.4; 1/3 sorteos a favor)",
        "  - por sorteo: s41 -37.8 → -51.8 = -14.0 · s43 -19.7 → -65.4 = -45.7 · "
        "s47 -10.1 → -8.8 = +1.3",
    ])

    def replicas(media: dict[str, tuple[float, float, float]]) -> str:
        filas = ["## Jugado con la glándula a 10 s", "",
                 "| pool | rastro | descargas (media ± σ) | total | semillas con descargas |",
                 "|---|---|---|---|---|"]
        for pool in ("s41", "s43", "s47"):
            for rastro in ("puerta", "fijo"):
                m = media[rastro][{"s41": 0, "s43": 1, "s47": 2}[pool]]
                filas.append(f"| {pool} | {rastro} | {m} ± 1.0 | 1 | 24/24 |")
        return "\n".join(filas)

    rep_g = replicas({"puerta": (183.0, 228.5, 193.3), "fijo": (185.1, 236.8, 170.8)})
    rep_s = replicas({"puerta": (235.2, 266.9, 201.7), "fijo": (235.9, 273.2, 191.0)})
    # los pools del volcado del reflejo van con su prefijo, como en el fichero real
    for viejo, nuevo in (("| s41 |", "| p10h2s-s41 |"), ("| s43 |", "| p10h2s-s43 |"),
                         ("| s47 |", "| p10h2s-s47 |")):
        rep_s = rep_s.replace(viejo, nuevo)
    for viejo, nuevo in (("| s41 |", "| p10h2g-s41 |"), ("| s43 |", "| p10h2g-s43 |"),
                         ("| s47 |", "| p10h2g-s47 |")):
        rep_g = rep_g.replace(viejo, nuevo)

    pools = [(b, s) for b in ("p10h2g", "p10h2s", "p10g", "p10s") for s in (41, 43, 47)]
    comida = {"p10h2g": {"puerta": (100.0, 102.0, 104.0), "fijo": (20.0, 22.0, 24.0)},
              "p10h2s": {"puerta": (40.0, 42.0, 44.0), "fijo": (10.0, 12.0, 14.0)}}
    casa = {"p10h2g": {"puerta": (7123.5, 7076.25, 7100.25), "fijo": (9200.0, 9300.0, 9400.0)},
            "p10h2s": {"puerta": (7400.0,) * 3, "fijo": (9600.0,) * 3}}
    negado = {"puerta": (85, 86, 84), "fijo": (87, 88, 86)}
    difs48 = [16.3, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 90.0, 100.0, 120.0, 382.2]
    dep = ["# volcado de mentira (F11k)"]
    for precio in (0, 10):
        dep.append(f"\n## Precio de juego {precio} s")
        for ventana in (24000, 48000):
            dep.append(f"\n### Ventana de {ventana} ticks\n")
            dep.append("| pool | rastro | comida | casa | negado % | glándula (s) |")
            dep.append("|---|---|---|---|---|---|")
            for i, (b, s) in enumerate(pools):
                ci = {41: 0, 43: 1, 47: 2}[s]
                for rastro in ("puerta", "fijo"):
                    if precio == 10 and b in comida:
                        com = comida[b][rastro][ci] if ventana == 48000 else 25.0
                        cas = casa[b][rastro][ci] if ventana == 48000 else 3538.0
                        neg = negado[rastro][ci]
                        gl = 9090.1 if ventana == 48000 else 4538.7
                    elif precio == 10:
                        com, cas, neg, gl = 50.0, 8000.0, 86, 4700.0
                    else:
                        com = 18000.0 if (b == "p10h2s" and rastro == "fijo") else 20000.0
                        com = com if ventana == 48000 else com / 2
                        cas, neg, gl = (36000.0 if ventana == 48000 else 18000.0), 0, 0.0
                    dep.append(f"| {b}-s{s} | {rastro} | {com:.1f} | {cas:.1f} | {neg} % | {gl:.1f} |")
            for i, (b, s) in enumerate(pools):
                if precio == 10:
                    val = (5.0 + i) if ventana == 24000 else difs48[i]
                    wins = 23 if (ventana == 48000 and i in (1, 9)) else 24
                else:
                    val, wins = -500.0 - i, 0
                dep.append(f"- **{b}-s{s}: puerta {MINUS} fijo** → {val:+.1f} ± 10.0 de rastro "
                           f"(gana {wins}/24 semillas)")
        dep.append(f"\n### La ventana 48000 contra 24000 ticks (precio {precio} s)\n")
        for i, (b, s) in enumerate(pools):
            for rastro in ("puerta", "fijo"):
                if precio == 0:
                    r = 1.90 + i * 0.04 if b in ("p10h2g", "p10h2s") else 1.72 + (i - 6) * 0.01
                else:
                    r = 1.15 + (i % 5) * 0.02 if rastro == "fijo" else 1.22 + (i % 5) * 0.095
                dep.append(f"- **{b}-s{s} · {rastro}**: 10.0 → 20.0 (×{r:.2f})")

    return {P10H2_VEREDICTO: veredicto + "\n", P10H2G_REPLICAS: rep_g + "\n",
            P10H2S_REPLICAS: rep_s + "\n", P10H2_DEPOSITS: "\n".join(dep) + "\n"}


def _falsos_f11l() -> dict[str, str]:
    """Volcados de mentira para F11l: el 2×2 del horizonte, con cifras legibles a mano.

    Los niveles de las réplicas se eligen para que la resta de los dos brazos de cada casilla sea la
    que la tabla falsa del veredicto declara, y para que el salto de horizonte de 25 s salga con el
    mismo signo en los tres sorteos: es lo único que la sección afirma de ellos.
    """
    # (juego, rastro) → (efecto 10s/24k, 25s/24k, 10s/48k, 25s/48k)
    efectos = {
        (10, "fijo"): ("-20.0", "-20.0", "-40.0", "-1.0"),
        (10, "puerta"): ("-25.0", "-25.0", "-45.0", "-6.0"),
        (25, "fijo"): ("-21.0", "-21.0", "-41.0", "-2.0"),
        (25, "puerta"): ("-26.0", "-26.0", "-46.0", "-12.0"),
    }
    filas = ["# volcado de mentira (F11l)"]
    for (juego, rastro), (e24, e2425, e48, e2548) in efectos.items():
        filas += [
            f"## Jugado con la glándula a {juego} s · rastro {rastro}",
            "| precio de entrenamiento | horizonte | efecto (puerta − reflejo) | sorteos |",
            "|---|---|---|---|",
            f"| 10 s | 24k | {e24} ± 9.7 | 3 |",
            f"| 25 s | 24k | {e2425} ± 3.0 | 3 |",
            f"| 10 s | 48k | {e48} ± 8.8 | 3 |",
            f"| 25 s | 48k | {e2548} ± 1.0 | 3 |",
        ]
    filas.append("## Cuánto mueve cada eje el efecto de la puerta")
    for (juego, rastro), (e24, e2425, e48, e2548) in efectos.items():
        filas += [
            f"### jugado {juego} s · rastro {rastro}",
            f"- **horizonte a 10 s**: 10 s / 24k → 10 s / 48k · {e24} → {e48} = -20.0 ± 1.1 "
            "descargas (t = -31.5; pareado por sorteo: s41 · s43 · s47)",
            f"- **horizonte a 25 s**: 25 s / 24k → 25 s / 48k · {e2425} → {e2548} = +19.0 ± 1.0 "
            "descargas (t = +33.0; pareado por sorteo: s7 · s11 · s13)",
        ]
    filas.append("## Reparto del castigo entre las cuatro casillas")
    for (juego, rastro), (e24, _e2425, _e48, e2548) in efectos.items():
        filas += [
            f"### jugado {juego} s · rastro {rastro}",
            f"- castigo base (10 s / 24k): **{e24}**",
            "- horizonte ×2 con el precio quieto: **-20.0**",
            "- precio 10 s → 25 s con el horizonte corto: **+0.0**",
            f"- celda 25 s / 48k: **{e2548}** · predicción aditiva {float(e24) - 20.0:.1f} ⇒ "
            "interacción **%+.1f**" % (float(e2548) - float(e24) + 20.0),
            "",
        ]
    veredicto = "\n".join(filas)

    def replicas(prefijo: str, sorts: tuple[int, ...], niveles: dict[int, float]) -> str:
        """Un volcado de réplicas de mentira: los dos rastros de cada pool, en los dos precios."""
        salida = ["# volcado de mentira (F11l)"]
        for juego in (10, 25):
            salida += [
                f"\n## Jugado con la glándula a {juego} s", "",
                "| pool (receta) | rastro | descargas (media ± σ) | total | semillas |",
                "|---|---|---|---|---|",
            ]
            for i, sort in enumerate(sorts):
                base = niveles[juego] + i
                salida.append(f"| {prefijo}-s{sort} | fijo | {base:.1f} ± 1.0 | 1 | 24/24 |")
                salida.append(f"| {prefijo}-s{sort} | puerta | {base + 5:.1f} ± 1.0 | 1 | 24/24 |")
        return "\n".join(salida) + "\n"

    s10, s25 = (41, 43, 47), (7, 11, 13)
    return {
        # casilla 10 s / 24k: 150 contra 170
        P10G7_REPLICAS: replicas("p10g", s10, {10: 150.0, 25: 140.0}),
        P10X10_REPLICAS: replicas("p10", s10, {10: 170.0, 25: 160.0}),
        # casilla 10 s / 48k: 190 contra 230
        P10H2G_REPLICAS: replicas("p10h2g", s10, {10: 190.0, 25: 180.0}),
        P10H2S_REPLICAS: replicas("p10h2s", s10, {10: 230.0, 25: 220.0}),
        # casilla 25 s / 24k: 148 contra 168
        P25G7_REPLICAS: replicas("p25g", s25, {10: 148.0, 25: 138.0}),
        P257_REPLICAS: replicas("p25", s25, {10: 168.0, 25: 158.0}),
        # casilla 25 s / 48k: 160 contra 161, el efecto que deja de verse
        P25H2G_REPLICAS: replicas("p25h2g", s25, {10: 160.0, 25: 150.0}),
        P25H2S_REPLICAS: replicas("p25h2s", s25, {10: 161.0, 25: 151.0}),
        P25H2_VEREDICTO: veredicto + "\n",
    }


def _falsos_f11n() -> dict[str, str]:
    """Volcado de mentira para F11n: el formato del enfrentamiento, con cifras legibles a mano.

    Lleva TRES semillas (el real lleva 24) para que la cuenta de partidas se pueda comprobar de
    cabeza, y una fila de fábrica con σ distinta en cada precio para que el σ no se confunda.
    """
    filas = [
        "# F11n — volcado de mentira: el enfrentamiento del lanzador",
        "# la partida del lanzador: grid 96, laberinto, reclutamiento, 216000 ticks",
        "# semillas: 42,7,99",
        "",
        "| brazo | pool | puerta | glándula | descargas (media ± σ) | total | semillas |",
        "|---|---|---|---|---|---|---|",
        "| fabrica·reflejo·0s | pretrain-maze-los3.antgenome | no | 0 s | 100.0 ± 10.0 | 300 | 3/3 |",
        "| fabrica·puerta·0s | pretrain-maze-los3.antgenome | sí | 0 s | 101.0 ± 10.0 | 303 | 3/3 |",
        "| fabrica·reflejo·25s | pretrain-maze-los3.antgenome | no | 25 s | 105.0 ± 11.0 | 315 | 3/3 |",
        "| fabrica·puerta·25s | pretrain-maze-los3.antgenome | sí | 25 s | 102.0 ± 11.0 | 306 | 3/3 |",
        "",
        "## Los tres pareados que deciden (por semilla de partida)",
        "",
        "- **el par candidato " + MINUS + " el juego de hoy**: +50.5 ± 9.1 descargas "
        "(t = +5.5; gana en 18/24 semillas)",
        "- **la receta del reflejo a 25 s " + MINUS + " el juego de hoy**: +40.0 ± 8.0 "
        "(t = +5.0; gana en 18/24 semillas)",
        "- **el par candidato " + MINUS + " la receta del reflejo a 25 s**: +10.5 ± 3.0 "
        "(t = +3.5; gana en 17/24 semillas; las dos recetas con siete tiradas)",
        "",
        "## Lo que pone cada pieza",
        "",
        "- **la puerta dentro de su pool a 25 s**: +4.0 ± 2.5 descargas al promediar las siete "
        "tiradas (rango por tirada -12.0 a +11.0)",
        "- **la puerta sola, con la glándula apagada (fábrica)**: +1.0 ± 3.0 descargas "
        "(t = +0.3; 2/3 semillas)",
        "- **el precio solo, con el reflejo (fábrica)**: +5.0 ± 4.0 descargas (t = +1.2; 2/3 semillas)",
        "- **el par sobre el pool de fábrica**: +2.0 ± 5.0 descargas (t = +0.4; 2/3 semillas)",
        "",
        "## El criterio de F11n, aplicado",
        "",
        "- semillas a favor del par: **18/24** (el criterio pide ≥16/24)",
        "- margen del par: **+50.5** descargas; dispersión entre tiradas de su receta: "
        "**8.0** (el criterio pide superarla)",
        "- dispersión entre tiradas de la receta del reflejo: **18.0**",
        "- **veredicto: SE ADOPTA** (las dos a 16/24)",
        "",
    ]
    return {P10H2_DUEL: "\n".join(filas)}


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
    lineas.update(_falsos_f11jk())
    lineas.update(_falsos_f11l())
    lineas.update(_falsos_f11n())
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

        # ── F11j: la tabla del horizonte, con el relativo calculado y el delta pareado ──
        decir(figs["F11j tabla · 25 s · puerta · 48k"] ==
              "| 25 s · puerta | 48k | 205,8 | 247,8 | " + MINUS + "42,0 | " + MINUS + "16,9 % |",
              "la fila del horizonte sale con su relativo calculado (−16,9 % de 205,8/247,8)")
        decir(figs["F11j delta · 10 s · fijo"] ==
              MINUS + "18,1 ± 1,1 descargas jugando a 10 s con el reflejo (t = " + MINUS + "17,0; "
              "0/3 sorteos a favor)",
              "el delta del horizonte se lee pareado y con su t")
        decir(figs["F11j el único delta a favor"] ==
              "la semilla 47 a 25 s con la puerta (+1,3)",
              "el único delta por sorteo positivo se identifica y ninguno más pasa de cero")

        # ── F11k: las medias de casa que el volcado sólo da por pool, y los factores ──
        decir(figs["F11k tabla · reflejo · reflejo"] ==
              "| entrenado con reflejo | reflejo | 12,0 | 9 600 | 87 % |",
              "la fila del rastro sale con el separador de millar y el negado redondeado")
        decir(figs["F11k casa y glándula de un pool"] ==
              "(3 538 → 7 124 en p10h2g-s41) y el presupuesto gastado también "
              "(4 538,7 → 9 090,1 s)",
              "la casa de un pool se lee de su celda, no de la media del brazo")
        decir(figs["F11k ventana sin precio"] ==
              "sin precio el acumulado crece ×1,72" + GUION + "2,10 en los veinticuatro brazos",
              "el factor de ventana recorre los veinticuatro brazos, no sólo los nuevos")
        decir(figs["F11k la cresta sube"] ==
              "de +5,0 a +382,2, con 24/24 semillas a favor en diez de las doce celdas y "
              "23/24 en las otras dos",
              "los extremos y el recuento de semillas salen de las doce diferencias de 48 000")

        # ── F11l: el 2×2 completo, los deltas por sorteo y el reparto ──
        decir(figs["F11l tabla · 25s/48k"] ==
              "| 25 s | 48k | 161,0 | 162,0 | " + MINUS + "1,0 | " + MINUS + "0,6 % |",
              "la celda nueva sale con sus dos niveles, su efecto y su relativo")
        decir(figs["F11l salto de horizonte"] ==
              MINUS + "20,0 ± 1,1 descargas a 10 s (t = " + MINUS + "31,5) y "
              "+19,0 ± 1,0 a 25 s (t = +33,0)",
              "el salto de horizonte sale a los dos precios, con su t y su signo")
        decir(figs["F11l deltas por sorteo · 25 s"] ==
              "(+19,0 / +19,0 / +19,0 al doblar la ventana)",
              "los deltas por sorteo se calculan de las medias por pool, no se copian")
        decir(figs["F11l reparto"] ==
              "base " + MINUS + "20,0, horizonte ×2 a 10 s " + MINUS + "20,0, precio a 24k "
              "+0,0, predicción aditiva " + MINUS + "40,0 y celda real " + MINUS + "1,0: "
              "interacción +39,0",
              "el reparto sale con la predicción aditiva y la interacción")
        decir(figs["F11l interacciones en las cuatro formas de jugar"] ==
              "la interacción va de +34,0 a +39,0 descargas en las cuatro formas de jugar",
              "los extremos de la interacción salen de las cuatro formas de jugar")

        # ── F11n: el enfrentamiento del lanzador, leído por el nombre de cada viñeta ──
        decir(figs["F11n · el par contra fábrica"] ==
              "el par a 25 s gana +50,5 ± 9,1 descargas al juego de hoy (t = +5,5; 18/24 semillas)",
              "el pareado del par sale con su t y sus semillas, con el «+» del documento")
        decir(figs["F11n · el par sobre su receta"] ==
              "el par le gana a su propia receta del reflejo +10,5 ± 3,0 descargas "
              "(t = +3,5; 17/24 semillas)",
              "el pareado entre recetas se encuentra por su nombre, no por su posición")
        decir(figs["F11n · la dispersión de cada receta"] ==
              "la dispersión entre tiradas es 8,0 en la receta del par y 18,0 en la del reflejo",
              "las dos dispersiones salen de las dos viñetas del criterio")
        decir(figs["F11n · la puerta dentro de su pool"] ==
              "la puerta dentro del pool que la entrenó: +4,0 ± 2,5 descargas al promediar las "
              "siete tiradas (rango por tirada " + MINUS + "12,0 a +11,0)",
              "el rango por tirada sale con el menos tipográfico y el «+» del lado alto")
        decir(figs["F11n · pedestal de fábrica"] ==
              "los dos brazos de fábrica dan 100,0 ± 10,0 con la glándula apagada y 105,0 ± 11,0 a 25 s",
              "el pedestal se lee de las filas de tabla, cada precio con su σ")
        decir(figs["F11n · la cuenta de partidas"] == "4 brazos por 3 semillas: 12 partidas",
              "la cuenta sale de los brazos de la tabla y las semillas de la cabecera")

        # Fail-closed en el enfrentamiento: sin su volcado no hay cifra que derivar.
        with tempfile.TemporaryDirectory() as tmp3:
            raiz3 = Path(tmp3)
            (raiz3 / "artifacts").mkdir()
            for fuente, texto in lineas.items():
                if fuente != P10H2_DUEL:
                    (raiz3 / fuente).write_text(texto, encoding="utf-8")
            try:
                duel_pareado(raiz3, "el par candidato " + MINUS + " el juego de hoy")
                decir(False, "un enfrentamiento sin su volcado tiene que parar la derivación")
            except FaltaCifra as e:
                decir(P10H2_DUEL in str(e), f"el volcado del enfrentamiento que falta se declara: {e}")

        # Y una viñeta renombrada tampoco se acepta: se lee por nombre, no por posición.
        # Se restaura después, porque las comprobaciones de abajo vuelven a derivar este volcado.
        (raiz / P10H2_DUEL).write_text(
            lineas[P10H2_DUEL].replace("el par candidato " + MINUS + " el juego de hoy",
                                       "el par frente al juego de ayer"), encoding="utf-8")
        try:
            duel_pareado(raiz, "el par candidato " + MINUS + " el juego de hoy")
            decir(False, "una viñeta renombrada tiene que parar la derivación")
        except FaltaCifra:
            decir(True, "una viñeta con otro nombre no se lee como cifra")
        (raiz / P10H2_DUEL).write_text(lineas[P10H2_DUEL], encoding="utf-8")

        # Que la deriva SE NOTE: cambiar el volcado cambia el fragmento exigido.
        (raiz / P107_REPLICAS).write_text(
            lineas[P107_REPLICAS].replace("**165.1**", "**167.9**"), encoding="utf-8")
        figs2 = dict(derivar(raiz))
        decir(figs2["tabla · sin puerta · jugado 10 s"] == "167,9 (114" + GUION + "189)",
              "si el volcado cambia, el fragmento exigido cambia con él")

        # Fail-closed: sin volcado, no se deriva en silencio.
        (raiz / P10H2_DEPOSITS).unlink()
        try:
            derivar(raiz)
            decir(False, "un volcado de F11k que falta tiene que parar la derivación")
        except FaltaCifra as e:
            decir(P10H2_DEPOSITS in str(e), f"el volcado de F11k que falta se declara: {e}")
        (raiz / P10H2_DEPOSITS).write_text(_falsos_f11jk()[P10H2_DEPOSITS], encoding="utf-8")

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

        # Fail-closed en el 2×2: sin una de sus réplicas no hay casilla que derivar. Va en un
        # directorio NUEVO porque las medias por pool se cachean por ruta: borrar el fichero del
        # directorio ya leído devolvería la copia en memoria y el test no probaría nada.
        with tempfile.TemporaryDirectory() as tmp2:
            raiz2 = Path(tmp2)
            (raiz2 / "artifacts").mkdir()
            for fuente, texto in _falsos_f11l().items():
                if fuente != P25H2G_REPLICAS:
                    (raiz2 / fuente).write_text(texto, encoding="utf-8")
            try:
                celda_matriz(raiz2, 25, 48)
                decir(False, "un 2×2 sin una de sus réplicas tiene que parar la derivación")
            except FaltaCifra as e:
                decir(P25H2G_REPLICAS in str(e), f"la réplica que falta se declara: {e}")

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
        # Una casilla del 2×2 lee varios volcados a la vez: para no contar de más, el resumen
        # cuenta los que son un fichero y deja dicho que los demás suman sus réplicas.
        sueltos = {f for _c, f, _p in CIFRAS if " + " not in f}
        print(f"\n{len(CIFRAS)} cifras de {len(sueltos)} volcados de F11h–F11n "
              f"(más las réplicas que cada casilla del 2×2 declara)")
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
