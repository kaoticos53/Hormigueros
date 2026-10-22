#!/usr/bin/env python3
# ─────────────────────────────────────────────────────────────────────────────
# commit-layers.py — reparte el árbol de trabajo en commits atómicos POR CAPAS,
# para no repetirlo a mano.
#
# POR QUÉ. El árbol de este repo casi nunca tiene una sola cosa pendiente: los
# assets de Unity que el editor reescribió, los scripts y ganchos de una hebra,
# el modelo de otra, los documentos, y el volcado que dejó el play pass. Meter
# todo eso en un commit mezcla historias que se revisan por separado y hace que
# `git log` deje de contar qué cambió y por qué. Repartirlo a mano es mecánico
# pero se hace mal —y se hace tarde— cuando hay 25 rutas de seis naturalezas.
#
# QUÉ HACE. Mira el estado real (`git status --porcelain -z -uall`) y reparte
# cada ruta en una CAPA por su sitio en el repo; luego commitea capa por capa,
# en el orden en que las capas se apoyan unas en otras. Las tres fuentes que
# maneja son las que ensucian el árbol:
#
#   1. EL ÍNDICE. Lo que ya está preparado NO se mezcla con lo que se reparte.
#      Con el índice sucio el reparto se planta (rc 2) y dice qué hay; con
#      `--include-index` lo preparado se commitea primero, tal cual está en el
#      índice, y lo que además tenga cambios en el árbol vuelve a salir en su
#      capa (media cosa staged, media en el árbol: dos commits honestos).
#   2. EL ÁRBOL. El resto de rutas, cada una en su capa, con el asunto puesto
#      para cada una (`--subject capa=texto` para escribir el tuyo).
#   3. EL CHURN. La capa de assets consulta al verificador de assets que ya
#      existe (`scripts/check-unity-assets.sh`): si el árbol tiene assets que
#      sólo se reescribieron, el reparto NO los commitea y pide
#      `--discard-churn` —que delega en su `--discard` en vez de reimplementar
#      el veredicto—. Committear eso sería meter el ruido del editor en la
#      historia, que es justo lo que el verificador existe para cortar.
#
# PLAN POR OMISIÓN, Y VERIFICACIÓN. Sin `--apply` no escribe NADA: imprime las
# capas, sus rutas y los asuntos propuestos. `--json FICHERO` guarda el plan con
# el hash de contenido de cada ruta, y `--apply-plan FICHERO` lo aplica
# comprobando que cada ruta sigue con el mismo hash: si algo cambió entre el
# plan y el apply —otra hebra, el editor, tú— se planta (rc 4) en vez de
# commitear algo que nadie revisó. Tras cada commit comprueba que contiene
# EXACTAMENTE las rutas previstas (rc 5 si no): un commit no se da por bueno
# leyendo su mensaje.
#
# Uso:
#   bash scripts/commit-layers.sh                     # el plan (no toca nada)
#   bash scripts/commit-layers.sh --list              # las capas y sus reglas
#   bash scripts/commit-layers.sh --layer docs        # sólo esa capa
#   bash scripts/commit-layers.sh --json plan.json    # plan + hashes
#   bash scripts/commit-layers.sh --apply             # commitea las capas
#   bash scripts/commit-layers.sh --apply-plan plan.json   # …el plan revisado
#   bash scripts/commit-layers.sh --apply --discard-churn  # …descartando el churn
#   bash scripts/commit-layers.sh --subject docs='docs: lo que cambié'
#   bash scripts/commit-layers.sh --selftest          # extremo a extremo, en un repo de usar y tirar
#
# Salida: 0 el plan (o el reparto) salió · 1 hay rutas fuera de toda capa
# (`otros`: no se commitean hasta que les digas su sitio) · 2 uso, índice sucio
# sin `--include-index`, o churn pendiente sin `--discard-churn` · 3 falta git o
# Python 3, o no es un repo · 4 el plan revisado ya no vale (cambió una ruta) · 5
# un commit no contiene exactamente lo previsto (nada se da por bueno sin
# volver a mirarlo).
# ─────────────────────────────────────────────────────────────────────────────
from __future__ import annotations

import contextlib
import io
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timezone

for _flujo in (sys.stdout, sys.stderr):
    try:
        _flujo.reconfigure(encoding="utf-8")
    except Exception:  # consolas raras: mejor no reventar por un ✓
        pass


# ── Las capas, EN ORDEN DE COMMIT ────────────────────────────────────────────
# El orden no es alfabético ni casual: primero lo que define las reglas (el eol
# del repo), luego las herramientas con las que se trabaja, después el código
# —modelo antes que vista, y la vista antes que los assets que la vista
# GENERA—, luego los documentos que cuentan lo anterior y al final la evidencia,
# que es un producto de correr las herramientas, no una decisión.
CAPAS = [
    {"nombre": "indice", "prefijo": "índice",
     "resumen": "lo que ya estaba preparado, tal cual estaba en el índice",
     "solo_indice": True},
    {"nombre": "config", "prefijo": "eol",
     "resumen": "las reglas de forma que valen para todo el repo"},
    {"nombre": "herramienta", "prefijo": "herramienta",
     "resumen": "los scripts, los ganchos y el CI del árbol"},
    {"nombre": "simulacion", "prefijo": "simulación",
     "resumen": "el modelo, el CLI y sus pruebas"},
    {"nombre": "vista", "prefijo": "vista",
     "resumen": "el proyecto de Unity: su código y sus ajustes"},
    {"nombre": "assets", "prefijo": "assets",
     "resumen": "los assets de Unity que el editor reescribió",
     "churn": True},
    {"nombre": "docs", "prefijo": "docs",
     "resumen": "los documentos del árbol"},
    {"nombre": "evidencia", "prefijo": "evidencia",
     "resumen": "el volcado que dejó el play pass"},
    {"nombre": "otros", "prefijo": "otros",
     "resumen": "lo que no casa con ninguna capa",
     "aviso": True},
]

# ── Reglas de clasificación: DE LA MÁS ESPECÍFICA A LA MÁS GENERAL ───────────
# La primera que casa manda. `**` cruza barras y `*` no (ver `casa`), así que
# `**/*.md` es «cualquier documento, a cualquier profundidad» y `*.md` sería
# sólo los de la raíz. Un README dentro del proyecto de Unity es un DOCUMENTO,
# no vista: por eso las reglas de docs van antes que las de `src/App/**`.
REGLAS = [
    ("config", ".gitattributes"),
    ("config", ".gitignore"),
    ("config", ".editorconfig"),
    ("herramienta", ".githooks/**"),
    ("herramienta", ".github/**"),
    ("herramienta", "scripts/**"),
    ("docs", "**/*.md"),
    ("vista", "src/App/**/*.cs"),
    ("vista", "src/App/**/*.cs.meta"),
    ("assets", "src/App/AntSim.Unity/Assets/**"),
    ("vista", "src/App/**"),
    ("simulacion", "src/Core/**"),
    ("simulacion", "src/Tools/**"),
    ("docs", "docs/**"),
    ("evidencia", "artifacts/**"),
    ("otros", "**"),
]

CAPA_POR_NOMBRE = {c["nombre"]: c for c in CAPAS}
ORDEN_NUM = {c["nombre"]: i for i, c in enumerate(CAPAS)}

# El verificador de assets que ya existe: el churn NO se reimplementa aquí.
CHURN_CMD_ENV = "COMMIT_LAYERS_CHURN_CMD"

USO = """\
commit-layers.py — el árbol de trabajo, repartido en commits atómicos por capas.

  --apply                  commitea las capas (sin él sólo imprime el plan)
  --discard-churn          descarta el churn de assets antes de commitear (verificador de assets)
  --include-index          commitea como primera capa lo que YA está preparado
  --layer CAPA             sólo esa capa (repetible)
  --subject CAPA=TEXTO     el asunto del commit de esa capa (repetible)
  --json FICHERO           guarda el plan, con el hash de cada ruta
  --apply-plan FICHERO     aplica un plan revisado (rc 4 si algo cambió desde el plan)
  --repo DIR               el repo a repartir (por omisión, el directorio actual)
  --list                   las capas y sus reglas de clasificación
  --selftest               el reparto entero, en un repo de usar y tirar
  -h, --help               esto
"""


# ── Globs ────────────────────────────────────────────────────────────────────

_RX_CACHE: dict[str, re.Pattern] = {}


def a_regex(pat: str) -> re.Pattern:
    """Glob del repo → regex: `**` cruza barras, `*` no, `?` un carácter."""
    rx = _RX_CACHE.get(pat)
    if rx is not None:
        return rx
    trozos, i = [], 0
    while i < len(pat):
        if pat.startswith("**/", i):
            trozos.append("(?:.*/)?"); i += 3
        elif pat.startswith("**", i):
            trozos.append(".*"); i += 2
        elif pat[i] == "*":
            trozos.append("[^/]*"); i += 1
        elif pat[i] == "?":
            trozos.append("[^/]"); i += 1
        else:
            trozos.append(re.escape(pat[i])); i += 1
    rx = re.compile("^" + "".join(trozos) + "$")
    _RX_CACHE[pat] = rx
    return rx


def casa(pat: str, ruta: str) -> bool:
    return bool(a_regex(pat).match(ruta))


def capa_de(ruta: str) -> str:
    """La capa de una ruta; `otros` si ninguna regla la reconoce."""
    for nombre, pat in REGLAS:
        if casa(pat, ruta):
            return nombre
    return "otros"


# ── git ──────────────────────────────────────────────────────────────────────

def git(*args: str, cwd: str | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(
        ["git", *args], cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        stdin=subprocess.DEVNULL,
    )


def git_ok(*args: str, cwd: str | None = None) -> str | None:
    """La salida de git, o None si falló (nunca una salida a medias)."""
    p = git(*args, cwd=cwd)
    if p.returncode != 0:
        return None
    return p.stdout.decode("utf-8", "replace")


def repo_de(destino: str | None) -> str | None:
    cwd = os.path.abspath(destino) if destino else os.getcwd()
    tope = git_ok("rev-parse", "--show-toplevel", cwd=cwd)
    return tope.strip() if tope else None


def hash_de(repo: str, ruta: str) -> str | None:
    """El hash del contenido que hay AHORA en esa ruta; None si no existe."""
    salida = git_ok("hash-object", "--", ruta, cwd=repo)
    return salida.strip() if salida else None


# ── El estado del árbol ──────────────────────────────────────────────────────

def leer_estado(repo: str) -> list[dict] | None:
    """Las rutas sucias, con su código de estado de dos letras (X=índice, Y=árbol)."""
    salida = git_ok("status", "--porcelain", "-z", "--untracked-files=all", cwd=repo)
    if salida is None:
        return None
    campos = salida.split("\0")
    entradas, i = [], 0
    while i < len(campos):
        c = campos[i]
        i += 1
        if not c:
            continue
        x, y, ruta = c[0], c[1], c[3:]
        if x in "RC":  # un renombrado trae el nombre original tras el nuevo
            i += 1
        entradas.append({"ruta": ruta, "x": x, "y": y, "estado": x + y})
    return entradas


def es_untracked(e: dict) -> bool:
    return e["x"] == "?" and e["y"] == "?"


def preparada(e: dict) -> bool:
    return e["x"] not in " ?"


def en_arbol(e: dict) -> bool:
    return es_untracked(e) or e["y"] not in " ?"


# ── El plan ──────────────────────────────────────────────────────────────────

def construir_plan(repo: str, filtro: list[str] | None = None,
                   incluir_indice: bool = False) -> dict:
    entradas = leer_estado(repo)
    if entradas is None:
        return {"rc": 3, "error": f"no pude leer el estado del árbol en {repo}"}

    preparadas = [e for e in entradas if preparada(e)]
    if preparadas and not incluir_indice:
        return {"rc": 2, "tipo": "indice_sucio", "rutas": [e["ruta"] for e in preparadas]}

    capas = {c["nombre"]: [] for c in CAPAS}
    for e in entradas:
        if preparada(e):
            capas["indice"].append({"ruta": e["ruta"], "estado": e["estado"],
                                    "papel": "índice", "hash": None})
        if en_arbol(e):
            capas[capa_de(e["ruta"])].append({"ruta": e["ruta"], "estado": e["estado"],
                                              "papel": "árbol", "hash": None})

    for nombre, rutas in capas.items():
        for r in rutas:
            r["hash"] = hash_de(repo, r["ruta"])
        rutas.sort(key=lambda r: r["ruta"])

    if filtro:
        for nombre in capas:
            if nombre not in filtro:
                capas[nombre] = []

    revisado = git_ok("rev-parse", "HEAD", cwd=repo)
    return {
        "rc": 0,
        "repo": repo,
        "filtro": list(filtro) if filtro else None,
        "indice_si": bool(incluir_indice),
        "rev": revisado.strip() if revisado else None,
        "creado": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "capas": [{"nombre": c["nombre"], "prefijo": c["prefijo"],
                   "resumen": c["resumen"], "rutas": capas[c["nombre"]]} for c in CAPAS],
    }


def rutas_de(capa: dict) -> list[str]:
    return [r["ruta"] for r in capa["rutas"]]


def asunto_de(capa: dict) -> str:
    return f'{capa["prefijo"]}: {capa["resumen"]}'


# ── El paso de churn: delega en el verificador de assets ─────────────────────

def ruta_shell(ruta: str) -> str:
    """La ruta en la forma que entiende el shell que va a leerla.

    En Windows una `C:\\Users\\…` no llega: el shell se come las barras invertidas
    como escapes. Con barras normales —`C:/Users/…`— la abren igual Git Bash, git y
    Python, así que la conversión no rompe nada y quita el dialecto de en medio.
    (En POSIX la ruta se deja tal cual: ahí la barra invertida es un carácter más,
    y un nombre de fichero puede llevarla.)
    """
    return ruta.replace("\\", "/") if os.name == "nt" else ruta


def churn_cmd() -> list[str]:
    seam = os.environ.get(CHURN_CMD_ENV)
    if seam:
        return shlex.split(seam)
    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return ["bash", ruta_shell(os.path.join(raiz, "scripts", "check-unity-assets.sh"))]


def churn(repo: str, descartar: bool) -> dict:
    """¿Hay assets que sólo se reescribieron? Lo dice el verificador de assets.

    `--discard` ACTÚA; sin él se pregunta con `--discard --dry-run`, que no toca
    nada. El veredicto es suyo, no mío: aquí sólo se lee el rc y, en el modo de
    aviso, la línea que anuncia que hay churn que descartar —una marca que el
    propio verificador imprime—, porque su rc 1 también significa «hay cambios
    de verdad», que sí se commitean.
    """
    cmd = churn_cmd() + (["--discard"] if descartar else ["--discard", "--dry-run"])
    try:
        p = subprocess.run(cmd, cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                           stdin=subprocess.DEVNULL, env={**os.environ, "PYTHONUTF8": "1"})
    except FileNotFoundError as exc:
        return {"rc": 3, "nota": f"no pude ejecutar el verificador de assets: {exc}", "salida": ""}
    salida = (p.stdout or b"").decode("utf-8", "replace") + (p.stderr or b"").decode("utf-8", "replace")
    hay = "tocados SÓLO en la forma" in salida
    return {"rc": p.returncode, "hay_churn": hay, "salida": salida, "cmd": cmd}


# ── El reparto ───────────────────────────────────────────────────────────────

def pares_de(salida: str | None) -> set[str]:
    if not salida:
        return set()
    return {t for t in salida.replace("\0", "\n").split("\n") if t.strip()}


def aplicar(plan: dict, descartar_churn: bool, asuntos: dict[str, str],
            log) -> int:
    repo = plan["repo"]
    capas = [c for c in plan["capas"] if c["rutas"]]

    if not capas:
        log("· no hay nada que repartir: el árbol está limpio")
        return 0
    otros = next((c for c in plan["capas"] if c["nombre"] == "otros"), None)
    if otros and otros["rutas"]:
        log("✗ hay rutas fuera de toda capa (`otros`): no entran al reparto")
        for r in rutas_de(otros):
            log(f"    {r}")
        log("  dales sitio con --layer otros, o muévelas a su capa")
        return 1

    # El churn, ANTES de leer el árbol: descartar devuelve rutas a HEAD, y el
    # plan tiene que ver el árbol que va a commitear, no el anterior.
    hay_assets = any(c["nombre"] == "assets" for c in capas)
    if hay_assets and not descartar_churn:
        # Sin `--discard-churn` el reparto no commitea assets cuando el editor
        # sólo los reescribió: eso es ruido de forma, y meterlo en la historia
        # sería exactamente lo que el verificador de assets existe para cortar.
        # Se planta ANTES de tocar nada, no a mitad del reparto.
        aviso = churn(repo, descartar=False)
        if aviso.get("hay_churn"):
            for linea in aviso.get("salida", "").rstrip().split("\n"):
                if linea.strip():
                    log("  " + linea)
            log("✗ la capa de assets tiene churn pendiente: no se commitea nada todavía")
            log("  repite con --discard-churn para descartarlo antes (el veredicto es del verificador)")
            return 2
    if descartar_churn:
        aviso = churn(repo, descartar=True)
        for linea in aviso.get("salida", "").rstrip().split("\n"):
            if linea.strip():
                log("  " + linea)
        if aviso["rc"] not in (0, 1):
            log(f"✗ el descarte del churn no dejó el árbol en condiciones (rc={aviso['rc']}): "
                f"no se commitea la capa de assets")
            return 2
        plan = rehacer(plan)
        if plan.get("rc") != 0:
            log("✗ tras descartar el churn no pude volver a mirar el árbol")
            return plan.get("rc") or 3

    acumulado: set[str] = set()
    commits = 0
    for capa in capas:
        nombre = capa["nombre"]
        rutas = rutas_de(capa)
        asunto = asuntos.get(nombre) or asunto_de(capa)

        if capa.get("solo_indice"):
            # Lo preparado se commitea tal cual está en el índice: nada de `git
            # add`, que arrastraría la parte del árbol que NO se preparó.
            previsto = set(rutas)
            reales = pares_de(git_ok("diff", "--cached", "--name-only", "-z", cwd=repo))
            if reales != previsto:
                log(f"✗ la capa `{nombre}` no coincide con el índice: "
                    f"previsto {sorted(previsto)}, preparado {sorted(reales)}")
                return 5
            p = git("commit", "-m", asunto, cwd=repo)
        else:
            p = git("add", "--", *rutas, cwd=repo)
            if p.returncode != 0:
                log("✗ no pude preparar: " + p.stderr.decode("utf-8", "replace").strip())
                return 5
            acumulado |= set(rutas)
            previsto = set(rutas)  # tras add, el índice tiene que ser el plan
            reales = pares_de(git_ok("diff", "--cached", "--name-only", "-z", cwd=repo))
            if reales != previsto:
                log(f"✗ el índice no quedó como el plan tras preparar `{nombre}`: "
                    f"sobra {sorted(reales - previsto)}")
                return 5
            p = git("commit", "-m", asunto, cwd=repo)

        if p.returncode != 0:
            log(f"✗ el commit de `{nombre}` falló: " +
                (p.stderr.decode("utf-8", "replace").strip() or p.stdout.decode("utf-8", "replace").strip()))
            return 5
        acumulado = set()
        sha = git_ok("rev-parse", "--short", "HEAD", cwd=repo).strip()
        traidas = pares_de(git_ok("show", "--name-only", "--pretty=format:", "-z", cwd=repo))
        if traidas != set(rutas):
            log(f"✗ el commit {sha} no contiene exactamente lo previsto: "
                f"sobra {sorted(traidas - set(rutas))}, falta {sorted(set(rutas) - traidas)}")
            return 5
        commits += 1
        log(f"✓ {sha}  {asunto}  ({len(rutas)} ruta(s))")

    return 0


def rehacer(plan: dict) -> dict:
    """Vuelve a mirar el árbol (tras descartar churn) y rearma el plan."""
    return construir_plan(plan["repo"], filtro=plan.get("filtro"),
                          incluir_indice=plan.get("indice_si", False))


# ── La verificación del plan revisado ────────────────────────────────────────

def comprobar_plan(plan: dict) -> list[str]:
    """Las rutas cuyo contenido cambió desde que se hizo el plan (o [] si ninguna)."""
    cambiadas = []
    for capa in plan["capas"]:
        for r in capa["rutas"]:
            if r.get("hash") is None and hash_de(plan["repo"], r["ruta"]) is None:
                continue
            if hash_de(plan["repo"], r["ruta"]) != r.get("hash"):
                cambiadas.append(r["ruta"])
    return cambiadas


# ── La salida ────────────────────────────────────────────────────────────────

def pintar_plan(plan: dict, log, solochurn: dict | None = None) -> int:
    repo = plan["repo"]
    con_rutas = [c for c in plan["capas"] if c["rutas"]]
    total = sum(len(c["rutas"]) for c in plan["capas"])
    otros = next((c for c in plan["capas"] if c["nombre"] == "otros"), None)
    hay_otros = bool(otros and otros["rutas"])

    log(f"▶ Reparto del árbol en commits atómicos — {repo}")
    log(f"  {len(con_rutas)} capa(s), {total} ruta(s) · revisión {plan.get('rev') or 'sin commits'}")
    if not con_rutas:
        log("  (el árbol está limpio)")
    log("")
    for capa in con_rutas:
        asunto = asunto_de(capa)
        nota = ""
        if capa["nombre"] == "assets":
            if solochurn and solochurn.get("hay_churn"):
                nota = "  [churn pendiente: usa --discard-churn]"
            elif solochurn and solochurn.get("rc") == 3:
                nota = "  [churn no medible: el verificador de assets no pudo correr]"
        aviso = "  [fuera de capa: no se commitea]" if capa["nombre"] == "otros" else ""
        log(f"  {ORDEN_NUM[capa['nombre']]}. {capa['nombre']}  ({len(capa['rutas'])} ruta(s)){nota}{aviso}")
        log(f"       asunto: {asunto}")
        for r in capa["rutas"]:
            log(f"       {r['estado']}  {r['ruta']}")
        log("")
    if not hay_otros:
        log("  · ninguna ruta quedó fuera de las capas")
    log("")
    log("  para commitear por capas: " +
        ("bash scripts/commit-layers.sh --apply" if not hay_otros
         else "bash scripts/commit-layers.sh --apply   (las de `otros` se quedan fuera)"))
    return 1 if hay_otros else 0


def pintar_lista(log) -> None:
    log("▶ Capas, en orden de commit:")
    for i, capa in enumerate(CAPAS):
        log(f"  {i}. {capa['nombre']:<11} asunto «{capa['prefijo']}: {capa['resumen']}»")
        if capa.get("solo_indice"):
            log("       (sin reglas: es lo que YA está preparado en el índice)")
        elif capa.get("aviso"):
            log("       (la red de seguridad: lo que ninguna regla reconoce; no se commitea solo)")
    log("")
    log("▶ Reglas de clasificación, EN ORDEN DE EVALUACIÓN (la primera que casa manda):")
    ancho = max(len(n) for n, _ in REGLAS)
    for i, (nombre, pat) in enumerate(REGLAS, 1):
        log(f"  {i:>2}. {nombre:<{ancho}}  {pat}")
    log("")
    log("  Van de la más específica a la más general a propósito: un README dentro del")
    log("  proyecto de Unity es un documento, no vista, y el código de Assets/Scripts es")
    log("  vista, no asset.")


# ── Selftest: el reparto entero, en un repo de usar y tirar ──────────────────

def selftest() -> int:
    fallos, hechos = [], 0

    def check(ok, titulo, detalle=""):
        nonlocal hechos
        hechos += 1
        if ok:
            print(f"  ✓ {titulo}")
        else:
            print(f"  ✗ {titulo}" + (f"\n      {detalle}" if detalle else ""))
            fallos.append(titulo)

    tmp = tempfile.mkdtemp(prefix="commit-layers-")
    repo = os.path.join(tmp, "repo")

    def sh(*args, cwd=repo):
        return subprocess.run(list(args), cwd=cwd, stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)

    def escribir(ruta, texto="x\n"):
        p = os.path.join(repo, ruta)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as fh:
            fh.write(texto)

    def commits():
        out = git_ok("log", "--format=%H", cwd=repo) or ""
        return [l for l in out.split("\n") if l.strip()]

    def commit_paths(sha):
        return pares_de(git_ok("show", "--name-only", "--pretty=format:", "-z", sha, cwd=repo))

    def asunto(sha):
        return (git_ok("log", "-1", "--format=%s", sha, cwd=repo) or "").strip()

    # Un stub del verificador de assets: la capa de assets NO debe invocar al de
    # verdad desde aquí (el repo es de mentira), y así se ejercita su veredicto.
    #
    # Y se ejecuta con el MISMO intérprete que corre esto, no con `bash` a secas: el
    # seam es una ORDEN, y en Windows la `bash` del PATH no siempre es la de Git
    # Bash —una consola de PowerShell resuelve antes la de WSL, que no sabe abrir
    # una ruta `C:\…`— y el stub salía sin ejecutarse (5 de 50 comprobaciones en
    # rojo, todas las del churn; medido el 2026-10-10). Con el intérprete no hay
    # shell que resolver ni dialecto de rutas que adivinar.
    stub = os.path.join(tmp, "churn-stub.py")
    modo = {"valor": "limpio"}

    def escribir_stub():
        # La salida va por `stdout.buffer` en UTF-8: `churn()` lee las dos
        # tuberías como UTF-8 y aquí hay `✓` y `ó`, así que no se deja al criterio
        # de codificación del intérprete de turno (que en Windows cambia).
        salida, rc, err = {
            "limpio": ("✓ no hay churn que descartar\n", 0, ""),
            "churn": ("• 2 asset(s) tocados SÓLO en la forma: se descartarían sin perder nada\n", 1, ""),
            "descartado": ("DESCARTADO  Assets/Escena.unity\n"
                           "✓ 1 asset(s) de re-serialización descartados\n", 0, ""),
            "roto": ("", 4, "✗ no pude\n"),
            "sinmedir": ("", 3, "no hay python\n"),
        }[modo["valor"]]
        with open(stub, "w", encoding="utf-8") as fh:
            fh.write("import sys\n"
                     f"sys.stdout.buffer.write({salida!a}.encode('utf-8'))\n"
                     f"sys.stderr.buffer.write({err!a}.encode('utf-8'))\n"
                     f"raise SystemExit({rc})\n")

    escribir_stub()
    os.environ[CHURN_CMD_ENV] = (f"{shlex.quote(ruta_shell(sys.executable))} "
                                 f"{shlex.quote(ruta_shell(stub))}")

    try:
        os.makedirs(repo, exist_ok=True)
        sh("git", "init", "-q", ".")
        for clave, valor in (("user.email", "selftest@example.com"),
                             ("user.name", "selftest"),
                             ("commit.gpgsign", "false"),
                             ("core.hooksPath", os.path.join(tmp, "sin-ganchos"))):
            sh("git", "config", clave, valor)
        os.makedirs(os.path.join(tmp, "sin-ganchos"), exist_ok=True)

        # ── Base ────────────────────────────────────────────────────────────
        escribir(".gitattributes", "*.ttf -text\n")
        escribir("scripts/hola.sh", "echo hola\n")
        escribir("src/Core/Modelo.cs", "class Modelo {}\n")
        escribir("src/Tools/Cli/Program.cs", "class Program {}\n")
        escribir("src/App/AntSim.Unity/Assets/Escena.unity", "%YAML 1.1\n")
        escribir("src/App/AntSim.Unity/Assets/Scripts/Controlador.cs", "class C {}\n")
        escribir("src/App/AntSim.Unity/ProjectSettings/EditorBuildSettings.asset", "ajustes\n")
        escribir("docs/guia.md", "# guía\n")
        escribir("README.md", "# repo\n")
        escribir("artifacts/volcado.txt", "volcado viejo\n")
        sh("git", "add", "-A")
        sh("git", "commit", "-q", "-m", "base")
        base = commits()[0]

        # ── Suciedad: una ruta de cada naturaleza ───────────────────────────
        escribir(".gitattributes", "*.ttf -text\n*.png -text\n")
        escribir("scripts/hola.sh", "echo hola mundo\n")
        escribir("scripts/nuevo.py", "print(1)\n")
        escribir("src/Core/Modelo.cs", "class Modelo { int x; }\n")
        escribir("src/App/AntSim.Unity/Assets/Escena.unity", "%YAML 1.1\n# tocada\n")
        escribir("src/App/AntSim.Unity/Assets/Scripts/Nueva.cs", "class N {}\n")
        escribir("docs/guia.md", "# guía 2\n")
        escribir("README.md", "# repo 2\n")
        escribir("docs/notas.txt", "notas\n")
        escribir("artifacts/volcado.txt", "volcado nuevo\n")
        escribir("artifacts/playpass-hud-icons.txt", "iconos\n")
        escribir("sin-sitio.bin", "binario\n")

        # ── Clasificación ───────────────────────────────────────────────────
        esperado = {
            ".gitattributes": "config",
            "scripts/hola.sh": "herramienta",
            "scripts/nuevo.py": "herramienta",
            "src/Core/Modelo.cs": "simulacion",
            "src/App/AntSim.Unity/Assets/Escena.unity": "assets",
            "src/App/AntSim.Unity/Assets/Scripts/Nueva.cs": "vista",
            "src/App/AntSim.Unity/Assets/Scripts/Controlador.cs": "vista",
            "src/App/AntSim.Unity/ProjectSettings/EditorBuildSettings.asset": "vista",
            "docs/guia.md": "docs",
            "README.md": "docs",
            "docs/notas.txt": "docs",
            "artifacts/volcado.txt": "evidencia",
            "sin-sitio.bin": "otros",
            "src/App/AntSim.Unity/README.md": "docs",
            "src/Core/X.cs.meta": "simulacion",
        }
        mal = {r: capa_de(r) for r, c in esperado.items() if capa_de(r) != c}
        check(not mal, "las reglas clasifican cada ruta en su capa", f"{mal}")

        # ── El plan no escribe nada ─────────────────────────────────────────
        antes = commits()
        plan = construir_plan(repo)
        check(plan.get("rc") == 0, "el plan se construye")
        check(commits() == antes, "el plan no crea ningún commit")
        capas = {c["nombre"]: rutas_de(c) for c in plan["capas"]}
        check(capas["config"] == [".gitattributes"], "capas[config] = .gitattributes", str(capas["config"]))
        check(capas["herramienta"] == ["scripts/hola.sh", "scripts/nuevo.py"],
              "capas[herramienta] con las dos rutas", str(capas["herramienta"]))
        check(capas["simulacion"] == ["src/Core/Modelo.cs"], "capas[simulación]", str(capas["simulacion"]))
        check(capas["vista"] == ["src/App/AntSim.Unity/Assets/Scripts/Nueva.cs"],
              "capas[vista]: assets/Scripts es vista", str(capas["vista"]))
        check(capas["assets"] == ["src/App/AntSim.Unity/Assets/Escena.unity"],
              "capas[assets]: la escena", str(capas["assets"]))
        check(capas["docs"] == ["README.md", "docs/guia.md", "docs/notas.txt"],
              "capas[docs] con los tres documentos", str(capas["docs"]))
        check(capas["evidencia"] == ["artifacts/playpass-hud-icons.txt", "artifacts/volcado.txt"],
              "capas[evidencia]", str(capas["evidencia"]))
        check(capas["otros"] == ["sin-sitio.bin"], "capas[otros]: lo que no casa", str(capas["otros"]))
        check(capas["indice"] == [], "índice limpio: la capa del índice está vacía")

        # ── El índice sucio se planta, y con --include-index va primero ─────
        sh("git", "add", "--", "docs/guia.md")
        sucio = construir_plan(repo)
        check(sucio.get("rc") == 2 and sucio.get("tipo") == "indice_sucio",
              "índice sucio sin --include-index → rc 2 (no mezcla)")
        con_indice = construir_plan(repo, incluir_indice=True)
        capas_i = {c["nombre"]: rutas_de(c) for c in con_indice["capas"]}
        check(capas_i["indice"] == ["docs/guia.md"], "con --include-index lo preparado es la capa 1",
              str(capas_i["indice"]))
        check("docs/guia.md" not in capas_i["docs"],
              "lo preparado no se vuelve a repartir en su capa")
        sh("git", "reset", "-q")

        # ── El plan revisado que ya no vale se planta ───────────────────────
        previos = len(commits())
        ruta_plan = os.path.join(tmp, "plan.json")
        with open(ruta_plan, "w", encoding="utf-8") as fh:
            json.dump(plan, fh, ensure_ascii=False)
        escribir("src/Core/Modelo.cs", "class Modelo { int y; }\n")
        cambiadas = comprobar_plan(json.load(open(ruta_plan, encoding="utf-8")))
        check(cambiadas == ["src/Core/Modelo.cs"], "el plan revisado detecta la ruta que cambió",
              str(cambiadas))
        check(len(commits()) == previos, "comprobar el plan no commitea")
        escribir("src/Core/Modelo.cs", "class Modelo { int x; }\n")  # vuelve al plan

        # ── El seam de VERDAD, sin stub: es el mismo defecto del stub, pero en el
        # camino de producción (la ruta del verificador se le entrega a `bash`).
        guardado = os.environ.pop(CHURN_CMD_ENV, None)
        cmd_real = churn_cmd()
        if guardado is not None:
            os.environ[CHURN_CMD_ENV] = guardado
        check(not any("\\" in parte for parte in cmd_real),
              "el verificador de verdad se invoca con una ruta que el shell entiende",
              " ".join(cmd_real))
        check(cmd_real[0] == "bash" and cmd_real[1].endswith("/scripts/check-unity-assets.sh"),
              "y sigue siendo `bash` sobre el verificador de assets del repo",
              " ".join(cmd_real))

        # ── El churn bloquea la capa de assets hasta que se descarta ────────
        modo["valor"] = "churn"
        escribir_stub()
        aviso = churn(repo, descartar=False)
        check(aviso["rc"] == 1 and aviso["hay_churn"],
              "el verificador avisa: hay churn que descartar", str(aviso))

        # ── El reparto entero ───────────────────────────────────────────────
        modo["valor"] = "limpio"
        escribir_stub()
        plan2 = construir_plan(repo)
        asuntos = {"docs": "docs: mis documentos, ya escritos a mano"}
        log_salida: list[str] = []
        rc = aplicar(plan2, descartar_churn=False, asuntos=asuntos, log=log_salida.append)
        check(rc == 1, "con rutas en `otros` el reparto para y avisa (rc 1)", "\n".join(log_salida))
        nuevos = commits()
        check(len(nuevos) == previos, "y no commitea nada mientras haya rutas fuera de capa")

        # `sin-sitio.bin` fuera: ahora sí, todo el reparto
        os.remove(os.path.join(repo, "sin-sitio.bin"))
        plan3 = construir_plan(repo)
        log_salida = []
        rc = aplicar(plan3, descartar_churn=False, asuntos=asuntos, log=log_salida.append)
        check(rc == 0, "el reparto termina bien (rc 0)", "\n".join(log_salida))
        repartidos = commits()
        hechos_ahora = repartidos[:len(repartidos) - previos]
        check(len(hechos_ahora) == 7, "siete commits: una capa por commit", str(len(hechos_ahora)))
        if len(hechos_ahora) == 7:
            de_arriba = list(reversed(hechos_ahora))  # en orden de commit
            esperados = [
                ({".gitattributes"}, "config"),
                ({"scripts/hola.sh", "scripts/nuevo.py"}, "herramienta"),
                ({"src/Core/Modelo.cs"}, "simulacion"),
                ({"src/App/AntSim.Unity/Assets/Scripts/Nueva.cs"}, "vista"),
                ({"src/App/AntSim.Unity/Assets/Escena.unity"}, "assets"),
                ({"README.md", "docs/guia.md", "docs/notas.txt"}, "docs"),
                ({"artifacts/playpass-hud-icons.txt", "artifacts/volcado.txt"}, "evidencia"),
            ]
            for sha, (rutas_esperadas, nombre) in zip(de_arriba, esperados):
                check(commit_paths(sha) == rutas_esperadas,
                      f"el commit de `{nombre}` trae exactamente sus rutas",
                      f"trajo {sorted(commit_paths(sha))}, esperaba {sorted(rutas_esperadas)}")
            check(asunto(de_arriba[5]) == asuntos["docs"],
                  "el asunto escrito a mano llega al commit", asunto(de_arriba[5]))
            check(asunto(de_arriba[0]) == "eol: las reglas de forma que valen para todo el repo",
                  "y el asunto por omisión es el de la capa", asunto(de_arriba[0]))
            orden_ok = all(sha in repartidos for sha in de_arriba)
            check(orden_ok, "los commits van del índice hacia fuera")
        resto = leer_estado(repo) or []
        check(resto == [], "tras repartir, el árbol queda limpio", str(resto))
        check(hash_de(repo, "src/App/AntSim.Unity/Assets/Escena.unity") is not None,
              "la escena sigue en el árbol")

        # ── El churn: la capa de assets se planta hasta que se descarta ─────
        escribir("src/App/AntSim.Unity/Assets/Escena.unity", "%YAML 1.1\n# tocada otra vez\n")
        modo["valor"] = "churn"
        escribir_stub()
        plan4 = construir_plan(repo)
        rutas4 = {c["nombre"]: rutas_de(c) for c in plan4["capas"]}
        check(rutas4["assets"] == ["src/App/AntSim.Unity/Assets/Escena.unity"],
              "la escena tocada sigue en la capa de assets", str(rutas4))
        previos4 = len(commits())
        log_salida = []
        rc = aplicar(plan4, descartar_churn=False, asuntos={}, log=log_salida.append)
        check(rc == 2, "con churn pendiente el reparto se planta (rc 2)", "\n".join(log_salida))
        check(len(commits()) == previos4,
              "y se planta ANTES de tocar nada: ninguna capa se commitea a medias")

        modo["valor"] = "descartado"
        escribir_stub()
        plan5 = construir_plan(repo)
        log_salida = []
        rc = aplicar(plan5, descartar_churn=True, asuntos={}, log=log_salida.append)
        check(rc == 0, "con --discard-churn el descarte va antes del commit", "\n".join(log_salida))
        check(any("DESCARTADO" in l for l in log_salida),
              "y el reparto cuenta lo que descartó el verificador")

        modo["valor"] = "roto"
        escribir_stub()
        escribir("src/App/AntSim.Unity/Assets/Escena.unity", "%YAML 1.1\n# rota\n")
        plan6 = construir_plan(repo)
        log_salida = []
        rc = aplicar(plan6, descartar_churn=True, asuntos={}, log=log_salida.append)
        check(rc == 2, "si el descarte no deja el árbol en condiciones, no se commitea (rc 2)",
              "\n".join(log_salida))

        # ── El filtro por capa ──────────────────────────────────────────────
        modo["valor"] = "sinmedir"
        escribir_stub()
        plan7 = construir_plan(repo, filtro=["docs"])
        capas7 = [c["nombre"] for c in plan7["capas"] if c["rutas"]]
        check(capas7 == [], "el filtro deja fuera lo que no se pidió", str(capas7))
        aviso7 = churn(repo, descartar=False)
        check(aviso7["rc"] == 3 and not aviso7.get("hay_churn"),
              "un verificador que no puede medir se distingue (rc 3, sin churn que reprochar)",
              str(aviso7))

        # ── El plan revisado, por la puerta de la CLI ───────────────────────
        # Es el camino que se usa de verdad: `--json` para revisarlo con calma y
        # `--apply-plan` para aplicarlo. El seguro tiene que estar ahí, no sólo
        # dentro de una función.
        def silencioso(*argv):
            # main() es la puerta de la CLI: argv[0] es el nombre del programa.
            with contextlib.redirect_stdout(io.StringIO()), \
                    contextlib.redirect_stderr(io.StringIO()):
                return main(["commit-layers.py", *argv])

        escribir("docs/guia.md", "# guía 3\n")
        revisado = os.path.join(tmp, "revisado.json")
        rc = silencioso("--repo", repo, "--json", revisado)
        check(rc == 0, "la CLI imprime el plan y lo guarda (--json)", f"rc={rc}")
        check(os.path.exists(revisado), "el plan queda en el fichero")
        previos8 = len(commits())
        escribir("docs/guia.md", "# guía 4 (otro renglón)\n")
        rc = silencioso("--repo", repo, "--apply-plan", revisado)
        check(rc == 4, "si algo cambió desde el plan, no se aplica (rc 4)", f"rc={rc}")
        check(len(commits()) == previos8, "y no se commitea lo que nadie revisó")
        escribir("docs/guia.md", "# guía 3\n")
        rc = silencioso("--repo", repo, "--apply-plan", revisado)
        check(rc == 0, "con la ruta como estaba, el plan revisado se aplica", f"rc={rc}")
        with open(revisado, encoding="utf-8") as fh:
            capas_plan = [c for c in json.load(fh)["capas"] if c["rutas"]]
        check(len(commits()) == previos8 + len(capas_plan),
              "con un commit por capa del plan (y ni uno más)",
              f"{len(commits()) - previos8} commits para {len(capas_plan)} capas")
        check(asunto(commits()[0]) == "docs: los documentos del árbol",
              "y con el asunto de su capa", asunto(commits()[0]))
        check(commit_paths(commits()[0]) == {"docs/guia.md"},
              "trayendo sólo su ruta", str(sorted(commit_paths(commits()[0]))))
    finally:
        os.environ.pop(CHURN_CMD_ENV, None)
        shutil.rmtree(tmp, ignore_errors=True)

    print("")
    if fallos:
        print(f"✗ selftest: {len(fallos)} de {hechos} comprobaciones fallaron:")
        for f in fallos:
            print(f"   · {f}")
        return 1
    print(f"✓ selftest: {hechos} comprobaciones, todas bien")
    return 0


# ── main ─────────────────────────────────────────────────────────────────────

def main(argv: list[str]) -> int:
    args = argv[1:]
    repo_arg = None
    filtro: list[str] = []
    asuntos: dict[str, str] = {}
    incluir_indice = False
    descartar_churn = False
    aplicar_ya = False
    plan_json = None
    plan_a_aplicar = None
    modo_lista = False
    modo_selftest = False

    i = 0
    while i < len(args):
        a = args[i]
        if a == "--repo":
            i += 1
            if i >= len(args):
                print("✗ --repo necesita un directorio", file=sys.stderr); return 2
            repo_arg = args[i]
        elif a == "--layer":
            i += 1
            if i >= len(args):
                print("✗ --layer necesita un nombre de capa", file=sys.stderr); return 2
            if args[i] not in CAPA_POR_NOMBRE:
                print(f"✗ no conozco la capa «{args[i]}» (--list las lista)", file=sys.stderr)
                return 2
            filtro.append(args[i])
        elif a == "--subject":
            i += 1
            if i >= len(args) or "=" not in args[i]:
                print("✗ --subject necesita la forma capa=texto", file=sys.stderr); return 2
            capa, _, texto = args[i].partition("=")
            if capa not in CAPA_POR_NOMBRE:
                print(f"✗ no conozco la capa «{capa}»", file=sys.stderr); return 2
            asuntos[capa] = texto
        elif a == "--json":
            i += 1
            if i >= len(args):
                print("✗ --json necesita un fichero", file=sys.stderr); return 2
            plan_json = args[i]
        elif a == "--apply-plan":
            i += 1
            if i >= len(args):
                print("✗ --apply-plan necesita un fichero", file=sys.stderr); return 2
            plan_a_aplicar = args[i]
        elif a == "--include-index":
            incluir_indice = True
        elif a == "--discard-churn":
            descartar_churn = True
        elif a == "--apply":
            aplicar_ya = True
        elif a == "--list":
            modo_lista = True
        elif a == "--selftest":
            modo_selftest = True
        elif a in ("-h", "--help"):
            print(USO, file=sys.stderr)
            return 0
        else:
            print(f"✗ opción desconocida: {a}  (--list, --apply, --selftest, …)", file=sys.stderr)
            return 2
        i += 1

    log = print

    if modo_selftest:
        return selftest()
    if modo_lista:
        pintar_lista(log)
        return 0

    repo = repo_de(repo_arg)
    if not repo:
        print(f"✗ {repo_arg or os.getcwd()} no está dentro de un repo git", file=sys.stderr)
        return 3

    # El churn se pregunta SÓLO para informar el plan: `--apply` lo mira (y lo
    # descarta, si toca) dentro de `aplicar`, con el árbol ya leído.
    solochurn = None
    if not aplicar_ya and not plan_a_aplicar and (not filtro or "assets" in filtro):
        solochurn = churn(repo, descartar=False)
        if solochurn["rc"] == 3:
            print(f"  (el verificador de assets no pudo medir el churn: {solochurn.get('nota') or 'rc 3'})",
                  file=sys.stderr)
        if solochurn.get("hay_churn") and not descartar_churn:
            print("  (hay churn de assets pendiente: no entra al reparto; "
                  "usa --discard-churn para descartarlo antes)", file=sys.stderr)

    if plan_a_aplicar:
        try:
            with open(plan_a_aplicar, encoding="utf-8") as fh:
                plan = json.load(fh)
        except (OSError, json.JSONDecodeError) as exc:
            print(f"✗ no pude leer el plan {plan_a_aplicar}: {exc}", file=sys.stderr)
            return 2
        cambiadas = comprobar_plan(plan)
        if cambiadas:
            print("✗ el plan ya no vale: estas rutas cambiaron desde que se hizo:", file=sys.stderr)
            for r in cambiadas:
                print("   " + r, file=sys.stderr)
            print("  vuelve a planear y revísalo antes de commitear.", file=sys.stderr)
            return 4
        with open(plan_a_aplicar, encoding="utf-8") as fh:
            plan = json.load(fh)
        plan.setdefault("repo", repo)
        return aplicar(plan, descartar_churn, asuntos, log)

    plan = construir_plan(repo, filtro=filtro or None, incluir_indice=incluir_indice)
    if plan.get("rc") == 3:
        print(f"✗ {plan['error']}", file=sys.stderr)
        return 3
    if plan.get("rc") == 2:
        print("✗ hay cosas ya preparadas en el índice y el reparto no las mezcla:", file=sys.stderr)
        for r in plan["rutas"]:
            print("   " + r, file=sys.stderr)
        print("  commitéalas tú, o pásame --include-index para que sean la primera capa.", file=sys.stderr)
        return 2

    otros = next(c for c in plan["capas"] if c["nombre"] == "otros")

    if plan_json:
        try:
            with open(plan_json, "w", encoding="utf-8") as fh:
                json.dump(plan, fh, ensure_ascii=False, indent=2)
                fh.write("\n")
        except OSError as exc:
            print(f"✗ no pude escribir el plan {plan_json}: {exc}", file=sys.stderr)
            return 2
        print(f"  · plan guardado en {plan_json} (con el hash de cada ruta)")

    if not aplicar_ya:
        return pintar_plan(plan, log, solochurn)

    return aplicar(plan, descartar_churn, asuntos, log)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
