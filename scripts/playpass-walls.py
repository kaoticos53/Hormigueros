#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# playpass-walls.py — LAS PAREDES DEL LABERINTO, medidas EN LOS PÍXELES del
# render del juego (F6).
#
# POR QUÉ. `LastWallDraws` se publicaba y nadie lo leía: la escena podía tener el
# `WallMesh` y el `WallMaterial` cableados, el contrato de escena en verde y la
# partida corriendo con un laberinto INVISIBLE (mal alto, escala cero, capa que
# la cámara no ve, material sin instancing). «Terreno con obstáculos» es
# justamente lo que el juego promete, y ningún check de estado lo ve: entre la
# matriz de la pared y el jugador quedan la cámara, la proyección, el rasterizado
# y el orden de dibujo.
#
# CÓMO. La sonda `ProbeWallPixels` renderiza la cámara del juego a una
# RenderTexture con la partida VIVA y guarda el frame entero, la hoja con las
# cajas esperadas (magenta) y los controles (cian), y publica:
#
#   · `cells/wallcells/agree/miss/false` — la comparación CELDA A CELDA del mapa
#     de la cabecera contra el frame: cada celda interior se proyecta con la
#     cámara real y su píxel se clasifica contra el color del material de pared.
#     Esto es «el mapa está en pantalla», no «hay algo oscuro»;
#   · una muestra `R|` por pared (celda del mapa, caja en píxeles, color medido)
#     y un `C|` por zona abierta con holgura, para exigir COLOR y SITIO.
#
# Este medidor NO le cree a la sonda: vuelve al PNG y comprueba, píxel a píxel,
# lo que las líneas dicen. Siete reglas, todas fallo cerrado:
#
#   1. el volcado y el frame existen, el frame mide lo que dice `frame=`, y el
#      frame limpio no lleva ni un píxel de las guías (la medida no se hizo sobre
#      la hoja anotada);
#   2. `draws == rects > 0`: el presenter dibujó TODOS los rectángulos — la
#      primera vez que alguien lee `LastWallDraws`;
#   3. acuerdo celda a celda ≥ 97 % (medido: 100 %), con ≤ 2 % de celdas-muro sin
#      pintar y ≤ 1 % de celdas abiertas pintadas de muro;
#   4. el centro de CADA pared muestreada es del color del material (±12 por
#      canal; el muro es Unlit, así que el frame trae el color exacto) y el de
#      NINGÚN control lo es;
#   5. cada caja de pared está LLENA de color de muro (≥ 85 % de su interior;
#      medido: 94-100 %) — una pared de un píxel o una caja vacía no pasa;
#   6. el tamaño de la caja corresponde al rectángulo (px/celda coherente entre
#      todas las muestras, ±2 px): la ESCALA se mide, no se supone;
#   7. las muestras se reparten por el tablero (≥ 50 % del ancho y del alto), y
#      los controles no tienen ni un píxel de muro (≤ 5 %): un polígono enorme
#      tapando el tablero no pasa por «paredes presentes».
#
# Sin numpy: solo Pillow (PIL) — y con el intérprete del repo (`python`, no
# `python3`, que en esta máquina no tiene Pillow).
#
# Uso:
#   python scripts/playpass-walls.py VOLCADO.txt DIR_EVIDENCIA [--ascii]
#     VOLCADO.txt     la línea `WALL|stage=measure|…` de la sonda
#     DIR_EVIDENCIA   carpeta con los PNG (artifacts/playpass-wall-pixels/: los
#                     conserva `preserve_walls` de playpass-live.sh, porque
#                     `Temp/` lo barre Unity al cerrar el editor)
#   --ascii          imprime el mapa medido en texto (para saber DÓNDE mirar)
#   --selftest       verifica el medidor con imágenes sintéticas (sin editor)
#
# Salida: 0 ok · 1 invariante roto · 2 uso o falta Pillow.
# ─────────────────────────────────────────────────────────────────────────────
import os
import sys

try:
    from PIL import Image
except ModuleNotFoundError:  # Pillow no está en todos los intérpretes
    print("✗ falta Pillow: usa el intérprete del repo (`python`, no `python3`)",
          file=sys.stderr)
    sys.exit(2)

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

FRAME = "antsim-wall-frame.png"
MARKED = "antsim-wall-marked.png"

# Tolerancia por canal contra el color del material. El muro es `Kind.Flat`
# (Unlit): el frame trae el color EXACTO del material, así que esto solo cubre el
# redondeo del framebuffer. El suelo está a 25-30 unidades de cada canal (medido:
# muro 82,74,66 · suelo 107,84,59), de modo que «no se pinta el muro» no puede
# confundirse con «se pinta parecido».
COLOR_TOL = 12

# Fracción del interior de la caja que tiene que estar pintada de muro. Medido en
# la partida canónica: 0,94-1,00 en las 32 muestras. El margen cubre una hormiga
# o un ítem que cruce justo el centro y el antialias del borde.
FILL_MIN = 0.85

# Acuerdo celda a celda. Medido: 100 % (8.836 celdas interiores, 1.202 de muro).
# El margen deja sitio a que un actor tape alguna celda, no a un laberinto que no
# está: sin paredes el acuerdo cae al ~86 % (solo las celdas abiertas acertarían).
AGREE_MIN = 0.97
MISS_MAX = 0.02
FALSE_MAX = 0.01

# Fracción de píxeles de muro permitida dentro de un control (zona abierta con
# dos celdas de holgura). Medido: 0,0.
CONTROL_MAX = 0.05

# Tamaño de caja vs. celdas del rectángulo: tolerancia en píxeles por eje. La
# caja es la envolvente de una esquina flotante, así que ±2 px es redondeo; una
# escala mal aplicada se desvía mucho más (una pared de 29 celdas de 280 px pasa a
# 100 px si el factor es 1/3).
SIZE_TOL = 2

GUIDES = ((255, 0, 255), (0, 255, 255))   # magenta y cian de la hoja anotada


def die(msg):
    print("✗ " + msg, file=sys.stderr)
    return 1


# ── Lectura del volcado ─────────────────────────────────────────────────────
def parse_line(raw):
    """Una línea de la sonda → (campos de cabecera, registros). `R` abre una
    pared y `C` un control; sus campos van detrás, como en el volcado de iconos."""
    head, recs, cur = {}, [], None
    for part in raw.split("|"):
        if not part:
            continue
        if part in ("R", "C"):
            cur = {"k": part}
            recs.append(cur)
            continue
        key, sep, val = part.partition("=")
        if not sep:
            head.setdefault("tags", []).append(part)
            continue
        if cur is None:
            head[key] = val
        else:
            cur[key] = val
    return head, recs


def parse_dump(path):
    """La línea de MEDIDA del volcado (la última `WALL|` válida)."""
    measure = None
    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            raw = raw.strip().replace("\r", "")
            if not raw.startswith("WALL"):
                continue
            head, recs = parse_line(raw)
            if head.get("stage") == "measure" or "stage" not in head:
                measure = (head, recs)
    return measure


def rgb(text):
    vals = [int(v) for v in text.split(",")]
    if len(vals) != 3:
        raise ValueError("color ilegible: " + text)
    return tuple(vals)


def box(text):
    """`X,Y,WxH` → (x, y, w, h)."""
    x, y, size = text.split(",")
    w, h = size.split("x")
    return int(x), int(y), int(w), int(h)


def cellrect(text):
    v = [int(p) for p in text.split(",")]
    if len(v) != 4:
        raise ValueError("rectángulo ilegible: " + text)
    return v


def dist(a, b):
    return max(abs(a[i] - b[i]) for i in range(3))


def is_wall(c, wall):
    return dist(c, wall) <= COLOR_TOL


# ── Medida sobre el PNG ─────────────────────────────────────────────────────
def fill_fraction(img, area, wall, inset=1):
    """Fracción del interior de `area` pintada de muro (inset de 1 px: el canto
    lo mezcla el antialias con el suelo, y ahí el color no decide nada)."""
    x, y, w, h = area
    px = img.load()
    n = hit = 0
    for yy in range(y + inset, y + h - inset):
        for xx in range(x + inset, x + w - inset):
            n += 1
            if is_wall(px[xx, yy], wall):
                hit += 1
    return (hit / n if n else 0.0), n


def guide_pixels(img):
    """Cuántos píxeles del frame son EXACTAMENTE una guía (magenta/cian). El frame
    limpio no puede tener ninguno: si los tiene, la medida se hizo sobre la hoja
    anotada y las cajas estarían dibujadas encima de lo que se mide."""
    px = img.load()
    n = 0
    for y in range(img.height):
        for x in range(img.width):
            if px[x, y] in GUIDES:
                n += 1
    return n


def expected_px(cells, ppc):
    """Píxeles que ocupa un lado de `cells` celdas. La caja es la envolvente de
    dos esquinas flotantes contadas de forma INCLUSIVA (`ceil - floor`), así que
    lleva un píxel de más: medirla con `ppc * cells` a secas marcaba como «escala
    mala» una pared correcta de 29 celdas (279 px medidos, 290 «esperados»)."""
    return ppc * cells + 1


def check_box_geometry(rect, area, ppc):
    """La caja tiene que medir lo que piden las celdas del rectángulo."""
    _, _, w, h = rect
    _, _, bw, bh = area
    return (abs(bw - expected_px(w, ppc)) <= SIZE_TOL
            and abs(bh - expected_px(h, ppc)) <= SIZE_TOL)


def analyse(measure, crops, show_ascii=False):
    if measure is None:
        return die("el volcado no trae la línea de MEDIDA de la sonda (`WALL|stage=measure`)")
    head, recs = measure
    if head.get("error"):
        return die("la sonda no pudo medir: " + head["error"])
    if head.get("row0") != "top":
        return die("el volcado no declara `row0=top` (la orientación del PNG): no se "
                   "sabe dónde cae cada celda")
    if not head.get("mesh") or not head.get("mat"):
        return die("el volcado no dice qué malla/material se midió (¿paredes cableadas?)")
    if not os.path.exists(os.path.join(crops, FRAME)):
        return die("falta el frame en vivo %s (¿corrió la sonda en Play?)"
                   % os.path.join(crops, FRAME))

    try:
        W, H = (int(v) for v in head["frame"].split("x"))
        grid = int(head["grid"])
        wall = rgb(head["wallrgb"])
        rects = int(head["rects"])
        draws = int(head["draws"])
        cells = int(head["cells"])
        wallcells = int(head["wallcells"])
        agree = int(head["agree"])
        miss = int(head["miss"])
        falsec = int(head["false"])
    except (KeyError, ValueError) as exc:
        return die("el volcado no trae la geometría/conteos legibles (%s)" % exc)

    frame = Image.open(os.path.join(crops, FRAME)).convert("RGB")
    if frame.size != (W, H):
        return die("el frame mide %d×%d y el volcado dice %d×%d"
                   % (frame.size[0], frame.size[1], W, H))

    walls = [r for r in recs if r["k"] == "R"]
    controls = [r for r in recs if r["k"] == "C"]
    if not walls:
        return die("el volcado no trae ninguna pared muestreada (`R|`)")
    if not controls:
        return die("el volcado no trae ningún control (`C|`): sin control, «hay algo "
                   "oscuro» pasaría por pared")

    print("▸ paredes del laberinto · frame %d×%d px · grid %d · %d rects · "
          "%s/%s (%s)" % (W, H, grid, rects, head.get("mesh"), head.get("mat"),
                          head.get("shader", "?")))
    fails = 0

    # — 1. El frame es el frame, y no lleva guías —
    guides = guide_pixels(frame)
    if guides:
        print("  ✗ el frame tiene %d px de las guías (magenta/cian): la medida se "
              "hizo sobre la hoja anotada" % guides)
        fails += 1
    else:
        print("  ✓ el frame es el render limpio (0 px de guía) y mide %d×%d" % (W, H))

    # — 2. Se dibujaron TODOS los rectángulos (`LastWallDraws`) —
    if rects <= 0:
        print("  ✗ la cabecera no trae ni un rectángulo de pared")
        fails += 1
    elif draws != rects:
        print("  ✗ el presenter dibujó %d de %d rectángulos (`LastWallDraws`) — hay "
              "paredes del mapa que no se dibujan" % (draws, rects))
        fails += 1
    else:
        print("  ✓ el presenter dibuja los %d rectángulos del terreno "
              "(`LastWallDraws` == rects)" % rects)

    # — 3. Acuerdo celda a celda: el mapa está en pantalla —
    if cells <= 0 or wallcells <= 0:
        print("  ✗ la sonda no evaluó celdas (cells=%d, wallcells=%d)" % (cells, wallcells))
        fails += 1
    else:
        open_cells = max(1, cells - wallcells)
        frac_agree = agree / float(cells)
        frac_miss = miss / float(wallcells)
        frac_false = falsec / float(open_cells)
        detalle = ("%d/%d celdas de acuerdo (%.1f %%) · %d celdas-muro sin pintar "
                   "(%.1f %%) · %d celdas abiertas pintadas (%.2f %%)"
                   % (agree, cells, frac_agree * 100, miss, frac_miss * 100,
                      falsec, frac_false * 100))
        if frac_agree >= AGREE_MIN and frac_miss <= MISS_MAX and frac_false <= FALSE_MAX:
            print("  ✓ el mapa está en pantalla: " + detalle)
        else:
            print("  ✗ el mapa NO está en pantalla: " + detalle
                  + " (mínimo %.0f %% de acuerdo, ≤ %.0f %% sin pintar, ≤ %.0f %% "
                  "pintadas de más)" % (AGREE_MIN * 100, MISS_MAX * 100, FALSE_MAX * 100))
            fails += 1

    # — 4. Color: cada pared del color del material, ningún control —
    bad_center = [w for w in walls if dist(rgb(w["rgb"]), wall) > COLOR_TOL]
    if bad_center:
        print("  ✗ %d de %d paredes no tienen el color del muro en su centro "
              "(p. ej. #%s mide %s, el material es %s)"
              % (len(bad_center), len(walls), bad_center[0].get("i"),
                 bad_center[0].get("rgb"), head.get("wallrgb")))
        fails += 1
    else:
        print("  ✓ las %d paredes muestreadas tienen el color del material "
              "(%s ±%d)" % (len(walls), head.get("wallrgb"), COLOR_TOL))

    # — 5 y 6. Sitio y escala: caja llena y del tamaño de sus celdas —
    try:
        parsed = [(w, cellrect(w["cell"]), box(w["box"])) for w in walls]
    except (KeyError, ValueError) as exc:
        return die("una muestra de pared trae geometría ilegible (%s)" % exc)

    # px/celda: mínimos cuadrados por el origen sobre (celdas, px-1) de las dos
    # dimensiones de todas las muestras. Sale del frame, no de una constante del
    # código: si la cámara cambia de encuadre, la regla sigue valiendo y el tamaño
    # se comprueba contra el mapa. Las paredes de 1 celda entran con su peso real,
    # pero no mandan (su caja lleva el píxel inclusivo).
    num = den = 0.0
    for _w, rect, area in parsed:
        for cells_n, px_n in ((rect[2], area[2]), (rect[3], area[3])):
            if cells_n > 0:
                num += cells_n * (px_n - 1)
                den += cells_n * cells_n
    ppc = (num / den) if den else 0.0

    empty, missized, narrow = [], [], []
    for w, rect, area in parsed:
        frac, n = fill_fraction(frame, area, wall)
        if n <= 0:
            empty.append((w.get("i"), area, 0.0))
        elif frac < FILL_MIN:
            empty.append((w.get("i"), area, frac))
        if ppc <= 0 or not check_box_geometry(rect, area, ppc):
            missized.append((w.get("i"), rect, area))

    if empty:
        print("  ✗ %d de %d cajas de pared NO están llenas de muro (p. ej. #%s %s: "
              "%.0f %% del interior; mínimo %.0f %%)"
              % (len(empty), len(parsed), empty[0][0], empty[0][1],
                 empty[0][2] * 100, FILL_MIN * 100))
        fails += 1
    else:
        lo = min(fill_fraction(frame, a, wall)[0] for _w, _r, a in parsed)
        print("  ✓ las %d cajas de pared están llenas de muro (mínimo %.0f %% del "
              "interior)" % (len(parsed), lo * 100))

    if missized:
        print("  ✗ %d cajas no miden lo que piden sus celdas (%.2f px/celda): p. ej. "
              "#%s celdas %s → caja %s"
              % (len(missized), ppc, missized[0][0], missized[0][1], missized[0][2]))
        fails += 1
    else:
        print("  ✓ la escala de las paredes es la del tablero (%.2f px/celda en las "
              "%d muestras, ±%d px)" % (ppc, len(parsed), SIZE_TOL))

    # — 7. Reparto por el tablero y controles limpios —
    xs = [a[0] for _w, _r, a in parsed]
    ys = [a[1] for _w, _r, a in parsed]
    span_x = (max(xs) - min(xs)) / float(W) if xs else 0.0
    span_y = (max(ys) - min(ys)) / float(H) if ys else 0.0
    if span_x < 0.5 or span_y < 0.5:
        print("  ✗ las muestras se concentran (%.0f %% del ancho, %.0f %% del alto): "
              "un muro mal puesto fuera de esa zona pasaría"
              % (span_x * 100, span_y * 100))
        fails += 1
    else:
        print("  ✓ las muestras se reparten por el tablero (%.0f %% del ancho, %.0f %% "
              "del alto)" % (span_x * 100, span_y * 100))

    dirty = []
    for c in controls:
        try:
            area = box(c["box"])
        except (KeyError, ValueError):
            continue
        frac, _n = fill_fraction(frame, area, wall, inset=0)
        if frac > CONTROL_MAX:
            dirty.append((c.get("i"), area, frac))
    if dirty:
        print("  ✗ %d de %d controles (zona abierta) tienen muro dentro (p. ej. #%s: "
              "%.0f %%): el muro no está solo donde dice el mapa"
              % (len(dirty), len(controls), dirty[0][0], dirty[0][2] * 100))
        fails += 1
    else:
        print("  ✓ los %d controles en zona abierta no tienen ni un píxel de muro"
              % len(controls))

    if show_ascii:
        print_ascii(frame, wall, grid)
    print("\n%d pared(es) muestreadas · %d control(es) · %d fallo(s)"
          % (len(walls), len(controls), fails))
    return 1 if fails else 0


def print_ascii(frame, wall, grid):
    """El mapa MEDIDO, en texto: `#` muro, `.` abierto (bloques de `grid/48`)."""
    px = frame.load()
    step = max(1, grid // 48)
    print("    mapa medido (48×48 bloques de %d celdas)" % step)
    for by in range(0, grid, step):
        row = ""
        for bx in range(0, grid, step):
            # Centro de la celda que representa el bloque, en coordenadas de pantalla
            # proporcionales al frame (el tablero no ocupa el frame entero; el
            # analizador ya validó el acuerdo celda a celda, esto es solo para mirar).
            x = int((bx + step / 2.0) / grid * frame.width)
            y = int((1 - (by + step / 2.0) / grid) * frame.height)
            row += "#" if is_wall(px[min(frame.width - 1, max(0, x)),
                                      min(frame.height - 1, max(0, y))], wall) else "."
        print("    " + row)


# ── Selftest: el medidor, con imágenes sintéticas (sin editor) ───────────────
def _synthetic(tmp, walls=True, size=1.0, giant=False, wrong_color=False,
               draws=None, guides=False, ppc=10):
    """Un frame sintético con un mapa conocido y su volcado. Cada caso rompe una
    regla distinta del medidor; el limpio tiene que pasar."""
    from PIL import ImageDraw

    grid = 24
    wall_rgb = (82, 74, 66)
    floor = (107, 84, 59)
    W = H = grid * ppc
    # Interior repartido por las cuatro esquinas y el centro: la regla del REPARTO
    # (las muestras no pueden concentrarse) tiene que poder cumplirse en el caso
    # limpio — si el mapa sintético fuera una sola mancha, el selftest estaría
    # probando la regla contra un caso imposible.
    rects = [(0, 0, grid, 1), (0, grid - 1, grid, 1), (0, 0, 1, grid),
             (grid - 1, 0, 1, grid), (4, 4, 8, 1), (4, 6, 1, 8),
             (12, 12, 6, 1), (2, 20, 10, 1), (18, 2, 1, 10),
             (2, 14, 1, 8), (16, 18, 4, 1)]
    img = Image.new("RGB", (W, H), floor)
    d = ImageDraw.Draw(img)
    paint = (200, 100, 60) if wrong_color else wall_rgb
    for (x, y, w, h) in rects:
        if not walls:
            continue
        scale = ppc * size
        # Las celdas crecen hacia arriba en el mundo: la fila del PNG es H - y.
        d.rectangle([x * ppc, H - (y + h) * ppc,
                     x * ppc + w * scale - 1, H - y * ppc - 1], fill=paint)
    if giant:
        d.rectangle([0, 0, W - 1, H - 1], fill=paint)

    wallcells = set()
    for (x, y, w, h) in rects:
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                wallcells.add((xx, yy))

    line = ("WALL|stage=measure|frame=%dx%d|row0=top|grid=%d|cell=8|rects=%d"
            "|draws=%d|mesh=WallMesh|mat=WallMat|shader=Universal Render "
            "Pipeline/Unlit|wallrgb=%d,%d,%d|colorspace=gamma"
            % (W, H, grid, len(rects), len(rects) if draws is None else draws,
               wall_rgb[0], wall_rgb[1], wall_rgb[2]))
    # Conteos celda a celda coherentes con lo pintado (los calcula el propio caso:
    # el medidor los compara con el PNG, así que un volcado que miente se cae).
    agree = miss = falsec = cells = 0
    px = img.load()
    for y in range(1, grid - 1):
        for x in range(1, grid - 1):
            is_wall_cell = (x, y) in wallcells
            c = px[x * ppc + ppc // 2, H - (y + 1) * ppc + ppc // 2]
            painted = dist(c, wall_rgb) <= COLOR_TOL
            cells += 1
            if painted == is_wall_cell:
                agree += 1
            elif is_wall_cell:
                miss += 1
            else:
                falsec += 1
    # `wallcells` cuenta SOLO las interiores, como la sonda: el anillo exterior no
    # se evalúa celda a celda (lo tapa el marco del tablero) y mezclarlo con
    # `cells` daba porcentajes imposibles (>100 % de celdas abiertas pintadas).
    wallcells_in = sum(1 for (x, y) in wallcells if 1 <= x < grid - 1 and 1 <= y < grid - 1)
    line += ("|cells=%d|wallcells=%d|agree=%d|miss=%d|false=%d"
             % (cells, wallcells_in, agree, miss, falsec))

    for i, (x, y, w, h) in enumerate(rects):
        if x < 1 or y < 1 or x + w > grid - 1 or y + h > grid - 1:
            continue
        bx, by = x * ppc, H - (y + h) * ppc
        bw, bh = int(round(w * ppc * size)), int(round(h * ppc * size))
        c = px[bx + bw // 2, by + bh // 2]
        line += ("|R|i=%d|cell=%d,%d,%d,%d|box=%d,%d,%dx%d|rgb=%d,%d,%d"
                 % (i, x, y, w, h, bx, by, bw, bh, c[0], c[1], c[2]))
    # Controles: celdas ABIERTAS con holgura 2, elegidas del mapa igual que la
    # sonda (recorrido y reparto), no a mano: un control fijo acababa dentro de una
    # pared cada vez que el mapa sintético crecía.
    candidates = []
    for cy in range(2, grid - 2):
        for cx in range(2, grid - 2):
            if any((cx + dx, cy + dy) in wallcells
                   for dx in range(-2, 3) for dy in range(-2, 3)):
                continue
            candidates.append((cx, cy))
    picked = []
    for i in range(min(4, len(candidates))):
        picked.append(candidates[int(i * len(candidates) / min(4, len(candidates)))])
    for i, (x, y) in enumerate(picked):
        bx, by = x * ppc, H - (y + 1) * ppc
        c = px[bx + ppc // 2, by + ppc // 2]
        line += ("|C|i=%d|cell=%d,%d|box=%d,%d,%dx%d|rgb=%d,%d,%d"
                 % (i, x, y, bx, by, ppc, ppc, c[0], c[1], c[2]))

    if guides:
        d.point((1, 1), fill=(255, 0, 255))

    os.makedirs(tmp, exist_ok=True)
    img.save(os.path.join(tmp, FRAME))
    img.save(os.path.join(tmp, MARKED))
    path = os.path.join(tmp, "dump.txt")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(line + "\n")
    return path


def selftest():
    import io
    import tempfile
    from contextlib import redirect_stdout

    fails = 0
    with tempfile.TemporaryDirectory() as tmp:
        cases = [
            ("limpio", dict(), 0),
            ("sin paredes", dict(walls=False), 1),
            ("paredes a media escala", dict(size=0.4), 1),
            ("pared con otro color", dict(wrong_color=True), 1),
            ("polígono gigante", dict(giant=True), 1),
            ("menos draws que rects", dict(draws=2), 1),
            ("frame con guías", dict(guides=True), 1),
        ]
        for name, kwargs, want in cases:
            case_dir = os.path.join(tmp, name.replace(" ", "-"))
            dump = _synthetic(case_dir, **kwargs)
            try:
                measure = parse_dump(dump)
                buf = io.StringIO()
                with redirect_stdout(buf):
                    got = analyse(measure, case_dir)
            except Exception as exc:  # el medidor no puede reventar con un caso raro
                print("✗ selftest(%s): excepción %r" % (name, exc), file=sys.stderr)
                fails += 1
                continue
            if got != want:
                print("✗ selftest(%s): dio %d, esperado %d" % (name, got, want),
                      file=sys.stderr)
                print(buf.getvalue(), file=sys.stderr)
                fails += 1

        # El volcado mudo y el frame que falta también fallan CERRADO: un paso que
        # no puede medir no puede dar verde.
        case_dir = os.path.join(tmp, "sin-frame")
        dump = _synthetic(case_dir, walls=True)
        os.remove(os.path.join(case_dir, FRAME))
        buf = io.StringIO()
        with redirect_stdout(buf):
            got = analyse(parse_dump(dump), case_dir)
        if got != 1:
            print("✗ selftest(sin frame): dio %d, esperado 1" % got, file=sys.stderr)
            fails += 1

        buf = io.StringIO()
        with redirect_stdout(buf):
            got = analyse(None, tmp)
        if got != 1:
            print("✗ selftest(sin medida): dio %d, esperado 1" % got, file=sys.stderr)
            fails += 1

    if fails == 0:
        print("✓ playpass-walls --selftest ok (limpio pasa · sin paredes, escala, "
              "color, polígono gigante, draws y guías fallan · sin frame/medida "
              "fallan cerrado)")
    return 1 if fails else 0


def main(argv):
    if "--selftest" in argv:
        return selftest()
    args = [a for a in argv[1:] if not a.startswith("--")]
    if len(args) < 2:
        print("uso: playpass-walls.py VOLCADO.txt DIR_EVIDENCIA [--ascii]",
              file=sys.stderr)
        return 2
    dump, crops = args[0], args[1]
    if not os.path.exists(dump):
        return die("no existe el volcado %s" % dump)
    if not os.path.isdir(crops):
        return die("no existe la carpeta de evidencia %s" % crops)
    return analyse(parse_dump(dump), crops, "--ascii" in argv)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
