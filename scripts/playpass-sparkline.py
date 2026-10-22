#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# playpass-sparkline.py — las BARRAS DE DESCARGAS de la gráfica, medidas EN LOS
# PÍXELES DE LA TARJETA REAL.
#
# POR QUÉ. La gráfica de la tarjeta (reserva como área + descargas por ventana
# como barras) se verificó headless sobre el modelo puro
# (`ColonySparklineModel`): eso prueba que la textura sale bien, no que la
# tarjeta la PINTE. Entre el modelo y el jugador quedan el RawImage, su rect en
# pantalla, el tinte de la vista y el framebuffer. Este paso cierra ese tramo con
# píxeles del Play pass.
#
# CÓMO. La sonda `ProbeSparklinePixels` inyecta una serie CONOCIDA en el modelo
# real de cada tarjeta (cuatro ventanas con descargas 1, 3, 5 y 7, separadas por
# ceros), deja que el motor dibuje, y captura el frame; el recorte de la banda de
# la gráfica y el frame entero quedan en PNG. Aquí se CLASIFICAN los píxeles por
# color (ámbar de las barras, verde del área) y se comprueban cuatro cosas que
# solo se pueden ver pintadas:
#
#   · hay EXACTAMENTE una barra por ventana con descargas, y cae en las columnas
#     de SU muestra — el mismo mapeo de columnas que usa el área
#     (`ColonySparklineModel.ColumnIndex`), así que una barra no puede mentir
#     sobre qué reserva explica;
#   · la altura crece con la cuenta (1 < 3 < 5 < 7) y la mayor llega al alto de
#     la banda: una barra de un píxel para «muchas descargas» no pasa;
#   · las barras y el área ARRANCAN EN LA BASE (la ÚLTIMA fila de la banda del
#     recorte, que se guarda como lo ve el jugador): la orientación no se supone,
#     se comprueba — si el PNG viniera del revés, el área quedaría pegada al
#     techo y este paso lo dice;
#   · no hay NI UN píxel ámbar fuera de la banda, y la banda cae DENTRO de la
#     tarjeta: la gráfica no se sale de su sitio ni se pinta en otra parte.
#
# El recorte tiene que ser un trozo LITERAL del frame (se compara byte a byte):
# es lo que convierte «medido en un PNG» en «medido en la pantalla».
#
# Sin numpy: solo Pillow (PIL) — y con `python`, no `python3`, que en esta
# máquina no lo tiene.
#
# Uso:
#   python scripts/playpass-sparkline.py VOLCADO.txt DIR_RECORTES [--out DIR] [--ascii]
#     VOLCADO.txt    líneas `SPARKPREP|…` y `SPARK|stage=measure|…` de la sonda
#     DIR_RECORTES   carpeta con los PNG (artifacts/playpass-sparkline-pixels/:
#                    los conserva `preserve_sparkline` de playpass-live.sh, porque
#                    `Temp/` lo barre Unity al cerrar el editor)
#   --out DIR        dónde dejar la hoja de medida (por defecto DIR_RECORTES)
#   --ascii          imprime la banda en texto (para saber DÓNDE mirar)
#   --selftest       verifica el medidor con imágenes sintéticas (sin editor)
#
# Salida: 0 ok · 1 invariante roto · 2 uso o falta Pillow.
# ─────────────────────────────────────────────────────────────────────────────
import os
import sys

try:
    from PIL import Image, ImageDraw
except ModuleNotFoundError:  # Pillow no está en todos los intérpretes
    print("✗ falta Pillow: usa el intérprete del repo (`python`, no `python3`)",
          file=sys.stderr)
    sys.exit(2)

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

FRAME = "antsim-spark-frame.png"
SHEET = "antsim-spark-compare.png"

# Clasificación por COLOR, con los valores que pinta la vista:
#   · barras  → `ColonySparklineBehaviour.DropColor` (0.95, 0.72, 0.25) a alpha
#               255: opacas, así que llegan al frame casi exactas (242, 184, 64);
#   · área    → `NormalColor` (0.35, 0.75, 0.30) a alpha 200 sobre el panel
#               oscuro: se ve verde dominante, más apagado que el color puro.
# Los umbrales separan esos dos tonos del panel (18, 19, 23), del texto claro
# (237, 232, 219 — el azul alto lo descarta) y del acento coral de la tarjeta
# (237, 115, 89 — sin verde suficiente). MEDIDOS sobre la captura del HUD; no son
# un gusto: bajar el azul máximo del ámbar metería el texto claro como «barra».
def is_amber(c):
    r, g, b = c[0], c[1], c[2]
    return r >= 140 and g >= 90 and b <= 140 and (r - b) >= 60 \
        and (g - b) >= 25 and r >= g


def is_reserve(c):
    r, g, b = c[0], c[1], c[2]
    return g >= 70 and g >= r + 18 and g >= b + 18


def die(msg):
    print("✗ " + msg, file=sys.stderr)
    return 1


# ── Lectura del volcado ─────────────────────────────────────────────────────
def parse_line(raw):
    """Una línea de la sonda → (campos de cabecera, tarjetas). `t=` abre una
    tarjeta y sus campos van detrás, como en el volcado de iconos."""
    head, cards = {}, []
    for part in raw.split("|"):
        if not part:
            continue
        key, sep, val = part.partition("=")
        if not sep:
            head.setdefault("tags", []).append(part)
            continue
        if key == "t":
            cards.append({"name": val})
            continue
        if cards:
            cards[-1][key] = val
        else:
            head[key] = val
    return head, cards


def parse_dump(path):
    """Devuelve la línea de MEDIDA (cabecera + tarjetas) y la de `prepare`."""
    prep, measure = None, None
    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            raw = raw.strip().replace("\r", "")
            if not raw:
                continue
            head, cards = parse_line(raw)
            stage = head.get("stage", "prepare" if raw.startswith("SPARKPREP") else "?")
            if stage == "measure" or raw.startswith("SPARK|"):
                measure = (head, cards)
            elif raw.startswith("SPARKPREP"):
                prep = (head, cards)
    return prep, measure


# ── El mapeo de columnas del modelo, duplicado A PROPÓSITO ───────────────────
def column_index(x, width, count):
    """Qué muestra cae en la columna `x`, con la regla de
    `ColonySparklineModel.ColumnIndex` para `count <= width`. Aquí se duplica el
    mapeo para que el analizador pueda exigir que la barra caiga en las columnas
    de SU muestra sin preguntárselo al mismo código que dibujó: si la regla
    cambia, este paso deja de cuadrar y obliga a actualizar los dos lados."""
    last = count - 1
    if width <= 1:
        return last
    return int(round(x * last / (width - 1)))


def sample_spans(width, count):
    """Primera y última COLUMNA de la textura de cada muestra."""
    spans = [[] for _ in range(count)]
    for x in range(width):
        spans[column_index(x, width, count)].append(x)
    return [(s[0], s[-1]) for s in spans]


# ── Medida ──────────────────────────────────────────────────────────────────
def read_rgb(path):
    return Image.open(path).convert("RGB")


def classify(img, inner):
    """Por columna de la banda: filas ámbar (barras) y filas de reserva (área).
    Las filas se devuelven contadas desde 0 = BASE (abajo).

    OJO con la orientación: el PNG se guarda como lo ve el jugador —fila 0 =
    ARRIBA—, pero el volcado declara `row0=top`; la captura de Unity sí indexa
    desde abajo (`GetPixels32`), y `EncodeToPNG` la vuelve a poner del derecho.
    Un análisis que contara la fila 0 del PNG como base mediría la gráfica del
    revés (medido: todas las barras al alto de la banda y el área al 100 %)."""
    ix, iy, iw, ih = inner
    px = img.load()
    amber = [None] * iw
    reserve = [None] * iw
    for x in range(iw):
        rows_a, rows_r = [], []
        for y in range(ih):
            c = px[ix + x, iy + (ih - 1 - y)]
            if is_amber(c):
                rows_a.append(y)
            elif is_reserve(c):
                rows_r.append(y)
        amber[x] = rows_a
        reserve[x] = rows_r
    return amber, reserve


def runs_of(columns):
    """Tramos de columnas con tinta, permitiendo un hueco de 1-2 px (el filtrado
    de la textura al escalar el canvas puede comerse el borde de una barra; las
    muestras con descargas están separadas por ceros, ~30 px)."""
    runs, start, prev = [], None, None
    for x, rows in enumerate(columns):
        if not rows:
            continue
        if start is None:
            start = x
        elif prev is not None and x - prev > 2:
            runs.append((start, prev))
            start = x
        prev = x
    if start is not None:
        runs.append((start, prev))
    return runs


def tex_to_screen(x_tex, inner, tex_w):
    """Columna de la textura → columna del recorte (el RawImage escala la textura
    al rect de la banda: 406 px de textura caben en ~370 px de pantalla)."""
    return inner[0] + (x_tex + 0.5) * inner[2] / float(tex_w)


def expected_height(count, drops_max, tex_h, inner_h):
    """Alto en píxeles de pantalla de una barra de `count` descargas, con la
    regla del modelo (`top = round(count/max × (h-1))`, filas 0..top) escalada al
    rect de la banda."""
    top = int(round(count / float(drops_max) * (tex_h - 1))) if drops_max > 0 else 0
    return (top + 1) * inner_h / float(tex_h)


def provenance(frame_path, card, crop):
    """El recorte tiene que ser un trozo LITERAL del frame.

    La caja del volcado viene en coordenadas de PANTALLA (origen abajo, como
    `GetPixels32`), mientras que el PNG se guarda del derecho (fila 0 = arriba),
    así que la fila de arriba del recorte en el frame es `alto - by - bh`. Se
    prueban las dos colocaciones (y las dos orientaciones) y vale la que
    coincida byte a byte: la igualdad exacta de bytes es la que convierte
    «medido en un PNG» en «medido en la pantalla»."""
    if not os.path.exists(frame_path):
        return False, "falta el frame en vivo"
    try:
        bx, by, bw, bh = (int(v) for v in card["box"].replace("x", ",").split(","))
    except (KeyError, ValueError):
        return False, "caja ilegible en el volcado"
    frame = read_rgb(frame_path)
    if bx < 0 or by < 0 or bx + bw > frame.width or by + bh > frame.height:
        return False, "la caja se sale del frame"
    for volteado in (False, True):
        fr = frame.transpose(Image.FLIP_TOP_BOTTOM) if volteado else frame
        for top in (frame.height - by - bh, by):
            if top < 0 or top + bh > frame.height:
                continue
            sub = fr.crop((bx, top, bx + bw, top + bh))
            if sub.size == crop.size and sub.tobytes() == crop.tobytes():
                where = ("volteado" if volteado else "tal cual") + \
                        f" en la fila {top} (pantalla y={by})"
                return True, where
    return False, "no coincide con el frame"


def check_card(card, crops, show_ascii=False):
    fails = 0
    name = card.get("name", "?")
    label = f"{name} (colonia {card.get('colony', '?')})"

    if card.get("error"):
        print(f"  ✗ {label}: la sonda no pudo medir — {card['error']}")
        return 1, []
    if card.get("painted") != "1":
        print(f"  ✗ {label}: la gráfica no llegó a pintarse (`painted≠1`)")
        return 1, []
    if card.get("inside") != "1":
        print(f"  ✗ {label}: la banda NO cae dentro de la tarjeta "
              f"(banda {card.get('inner', '?')} vs tarjeta {card.get('cardrect', '?')})")
        fails += 1
    if card.get("clipped") == "1":
        print(f"  ✗ {label}: la banda se sale del frame capturado — el recorte se "
              f"quedó corto y no se puede medir lo que no está")
        fails += 1

    try:
        tex_w, tex_h = (int(v) for v in card["tex"].split("x"))
        bx, by, bw, bh = (int(v) for v in card["box"].replace("x", ",").split(","))
        inner_t = tuple(int(v) for v in card["inner"].replace("x", ",").split(","))
    except (KeyError, ValueError):
        print(f"  ✗ {label}: el volcado no trae geometría legible")
        return 1, []
    if len(inner_t) != 4 or min(inner_t) < 0 or inner_t[2] < 2 or inner_t[3] < 2:
        print(f"  ✗ {label}: geometría de la banda imposible {inner_t}")
        return 1, []
    # `inner` viene en px de PANTALLA (el borde de abajo de la banda a `iy` de
    # la base del recorte); el PNG está del derecho, así que el borde de ARRIBA
    # dentro del recorte es `bh - iy - ih`. Con márgenes simétricos coincide,
    # pero no se supone: se convierte.
    ix, iy, iw, ih = inner_t
    inner_t = (ix, bh - iy - ih, iw, ih)

    path = os.path.join(crops, card.get("file", ""))
    if not os.path.exists(path):
        print(f"  ✗ {label}: no existe el recorte {card.get('file', '?')}")
        return 1, []
    crop = read_rgb(path)
    if crop.size != (bw, bh):
        print(f"  ✗ {label}: el recorte mide {crop.size[0]}×{crop.size[1]}, "
              f"la caja dice {bw}×{bh}")
        return 1, []

    ok, why = provenance(os.path.join(crops, FRAME), card, crop)
    print(f"  {'✓' if ok else '✗'} {label}: recorte {bw}×{bh} px "
          f"{'es el trozo de frame en ' + card.get('box', '?') if ok else why}")
    if not ok:
        fails += 1

    try:
        pattern = [int(v) for v in card.get("pattern", "").split(",")]
    except ValueError:
        pattern = []
    if not pattern:
        print(f"  ✗ {label}: el volcado no trae el patrón inyectado (`pattern=`)")
        return 1, []
    samples = len(pattern)
    drops_max = max(pattern) if pattern else 0
    expect = sum(1 for d in pattern if d > 0)
    amber, reserve = classify(crop, inner_t)

    # — 1. Una barra por ventana con descargas, en SUS columnas —
    runs = runs_of(amber)
    spans = sample_spans(tex_w, samples)
    # Las columnas de `runs` son LOCALES a la banda (0…iw-1); las esperadas se
    # calculan en esa misma referencia para poder compararlas.
    local = (0, 0, inner_t[2], inner_t[3])
    mapped = []
    bad_runs = 0
    for (x0, x1) in runs:
        mid_tex = (x0 + x1) / 2.0 * tex_w / float(inner_t[2]) - 0.5
        k = column_index(int(max(0, min(tex_w - 1, round(mid_tex)))), tex_w, samples)
        exp_x0 = tex_to_screen(spans[k][0], local, tex_w) - 0.5
        exp_x1 = tex_to_screen(spans[k][1], local, tex_w) + 0.5
        height = max((max(rows) + 1) if rows else 0 for rows in amber[x0:x1 + 1])
        mapped.append((k, x0, x1, height))
        inside_span = x0 >= exp_x0 - 2 and x1 <= exp_x1 + 2
        if pattern[k] <= 0 or not inside_span:
            bad_runs += 1
            print(f"    ✗ barra en columnas {x0}–{x1} (alto {height} px) no cae en una "
                  f"ventana con descargas: muestra {k} → columnas esperadas "
                  f"{exp_x0:.0f}–{exp_x1:.0f}, descargas {pattern[k]}")
    if len(runs) != expect:
        print(f"  ✗ {label}: {len(runs)} barra(s) ámbar, esperadas {expect} "
              f"(una por ventana con descargas del patrón {card.get('pattern', '?')})")
        fails += 1
    elif bad_runs == 0:
        print(f"  ✓ {label}: {len(runs)} barra(s) ámbar, cada una en las columnas "
              f"de su muestra (esperadas {expect})")
    else:
        fails += 1

    # — 2. Altura proporcional a la cuenta, la mayor al alto de la banda —
    if mapped and bad_runs == 0:
        heights = {k: h for (k, _x0, _x1, h) in mapped}
        ordered = sorted(heights)
        by_count = sorted(heights, key=lambda k: pattern[k])
        monotone = all(heights[by_count[i]] <= heights[by_count[i + 1]]
                       for i in range(len(by_count) - 1))
        tallest = max(heights.values()) if heights else 0
        expected_tall = expected_height(drops_max, drops_max, tex_h, inner_t[3])
        tabla = " ".join(f"{pattern[k]}→{heights[k]}px" for k in ordered)
        escala_ok = tallest >= 0.7 * inner_t[3]
        # Contraste: cuentas que difieren tienen que dar alturas distintas (si no,
        # una barra de «1» y otra de «7» se leerían igual).
        distinto = all(abs(heights[a] - heights[b]) >= 1
                       for a in ordered for b in ordered
                       if a < b and abs(pattern[a] - pattern[b]) >= 2)
        cerca = all(abs(heights[k] - expected_height(pattern[k], drops_max, tex_h,
                                                     inner_t[3])) <= 3 for k in ordered)
        if monotone and escala_ok and distinto and cerca:
            print(f"  ✓ {label}: alturas por cuenta {tabla} (la mayor "
                  f"{tallest}/{inner_t[3]} px de la banda, esperada "
                  f"{expected_tall:.0f})")
        else:
            fails += 1
            print(f"  ✗ {label}: alturas por cuenta {tabla} (la mayor {tallest}/"
                  f"{inner_t[3]} px, esperada {expected_tall:.0f}) — "
                  f"monótona={monotone} escala={escala_ok} distinta={distinto} "
                  f"dentro de ±3={cerca}")

    # — 3. Barras y área ARRANCAN EN LA BASE (la orientación no se supone) —
    # OJO: la barra se pinta ENCIMA del área, así que en sus columnas la reserva
    # no se ve. La base se juzga sobre «lo que hay pintado» (área o barra); si no,
    # una tarjeta correcta con barras parecería una gráfica a la que le falta
    # base, y este paso se convertiría en el falso negativo que evita en otros
    # sitios.
    #
    # Y la base se acepta en la fila 0 o la 1 (contadas desde la base): el rect
    # del `RawImage` cae en coordenadas FRACCIONARIAS de pantalla (`floor` para el
    # recorte), así que la primera fila puede ser el borde del panel y no tinta.
    # Exigir exactamente la fila 0 convertía un redondeo de medio píxel en
    # «gráfica del revés»; lo que se persigue es una diferencia de 20 px, no de 1.
    base_rows = (0, 1)
    filled = [(reserve[x] or amber[x]) for x in range(inner_t[2])]
    cols_base = sum(1 for rows in filled if rows and any(r in base_rows for r in rows))
    con_tinta = [x for x, rows in enumerate(filled) if rows]
    cobertura = len(con_tinta) / float(inner_t[2]) if inner_t[2] else 0.0
    bars_base = sum(1 for (x0, x1) in runs
                    if any(rows and min(rows) in base_rows for rows in amber[x0:x1 + 1]))
    if bars_base < len(runs):
        print(f"  ✗ {label}: solo {bars_base} de {len(runs)} barra(s) tocan la base "
              f"(fila 0–1) — ¿la gráfica está del revés?")
        fails += 1
    if cobertura < 0.9 or (con_tinta and cols_base < 0.9 * len(con_tinta)):
        print(f"  ✗ {label}: la gráfica cubre {cobertura:.0%} del ancho y "
              f"{cols_base}/{len(con_tinta)} columnas tocan la base (debe "
              f"llenarse desde abajo)")
        fails += 1
    else:
        print(f"  ✓ {label}: área y barras sobre la base, gráfica en {cobertura:.0%} "
              f"del ancho")

    # — 4. El borde del área sigue la serie de reserva —
    try:
        stock = [float(v) for v in card.get("stock", "").split(",")]
    except ValueError:
        stock = []
    drifts = []
    if len(stock) == samples:
        for k in range(samples):
            # En las muestras con descargas la barra TAPA el área: ahí no se puede
            # leer el borde de la reserva (se lee en las que no tienen barra).
            if pattern[k] > 0:
                continue
            cxk = int(round(tex_to_screen((spans[k][0] + spans[k][1]) / 2.0,
                                          local, tex_w)))
            rows = reserve[max(0, min(inner_t[2] - 1, cxk))]
            if not rows:
                drifts.append((k, -1, stock[k]))
                continue
            medido = (max(rows) + 1) / float(inner_t[3])
            if abs(medido - stock[k]) > 3.0 / inner_t[3]:
                drifts.append((k, medido, stock[k]))
        if len(drifts) > 1:
            fails += 1
            print(f"  ✗ {label}: el borde del área no sigue la reserva en "
                  f"{len(drifts)} muestra(s): "
                  + " ".join(f"#{k} {m:.2f}≠{s:.2f}" for k, m, s in drifts[:4]))
        else:
            print(f"  ✓ {label}: el borde del área sigue la serie de reserva "
                  f"(±3 px)")

    # — 5. Ni un píxel ámbar fuera de la banda (ni fugas ni otra cosa pintada) —
    px = crop.load()
    outside = 0
    for y in range(bh):
        for x in range(bw):
            if inner_t[0] <= x < inner_t[0] + inner_t[2] \
                    and inner_t[1] <= y < inner_t[1] + inner_t[3]:
                continue
            if is_amber(px[x, y]):
                outside += 1
    if outside > 0:
        print(f"  ✗ {label}: {outside} px ámbar FUERA de la banda de la gráfica "
              f"(la barra se sale de su sitio)")
        fails += 1
    else:
        print(f"  ✓ {label}: 0 px ámbar fuera de la banda")

    if show_ascii:
        print_ascii(crop, inner_t, label)
    return fails, mapped


def print_ascii(crop, inner, label=""):
    """La banda en texto: `.` fondo, `#` área de reserva, `*` barra ámbar. Se
    imprime como se ve en pantalla (la PRIMERA línea es el techo de la banda y la
    ÚLTIMA la base, que es desde donde se cuenta la altura)."""
    px = crop.load()
    ix, iy, iw, ih = inner
    print(f"    {label} — banda {iw}×{ih} px (arriba del texto = techo; la base es la última fila)")
    for y in range(ih):
        row = ""
        for x in range(0, iw, max(1, iw // 72)):
            c = px[ix + x, iy + y]
            row += "*" if is_amber(c) else "#" if is_reserve(c) else "."
        print("    " + row)


def save_sheet(cards, crops, measures, path):
    """La hoja que se mira: cada banda a 4× en orientación de PANTALLA (el
    recorte está en orden de framebuffer: se voltea solo para verlo), con las
    columnas esperadas de cada muestra (cian) y las barras medidas (ámbar)."""
    if not measures:
        return
    zoom = 4
    width = max((crop_w for (_c, _i, crop_w, _h, _m) in measures), default=360) * zoom
    rows = []
    for (card, inner, _w, _h, mapped) in measures:
        img = read_rgb(os.path.join(crops, card.get("file", "")))
        img = img.resize((img.width * zoom, img.height * zoom), Image.NEAREST)
        d = ImageDraw.Draw(img)
        ix, iy, iw, ih = inner
        # El recorte ya está en orientación de pantalla: la banda empieza en `iy`.
        iy_disp = iy * zoom
        tex_w = int(card.get("tex", "406x26").split("x")[0])
        pattern = [int(v) for v in card.get("pattern", "0").split(",")]
        for k in range(len(pattern)):
            spans = sample_spans(tex_w, len(pattern))
            x0 = int(tex_to_screen(spans[k][0], inner, tex_w) * zoom)
            x1 = int((tex_to_screen(spans[k][1], inner, tex_w) + 1) * zoom)
            d.rectangle([x0, iy_disp, x1, iy_disp + ih * zoom - 1],
                        outline=(80, 200, 230))
        for (k, x0, x1, h) in mapped:
            d.rectangle([x0 * zoom, iy_disp, (x1 + 1) * zoom,
                         iy_disp + ih * zoom - 1], outline=(255, 200, 60))
            d.text((x0 * zoom, max(0, iy_disp - 12)),
                   f"{pattern[k]}d", fill=(255, 200, 60))
        rows.append((card.get("name", "?"), img))

    pad, top = 10, 20
    H = top + sum(img.height + 26 for (_n, img) in rows) + pad
    out = Image.new("RGB", (width + pad * 2, H), (18, 18, 22))
    draw = ImageDraw.Draw(out)
    draw.text((pad, 5), "banda de la gráfica a 4× (orientación de pantalla) · "
                        "cian = columnas esperadas de cada muestra · ámbar = "
                        "barras medidas", fill=(225, 225, 225))
    y = top
    for name, img in rows:
        out.paste(img, (pad, y))
        draw.text((pad, y - 11), name, fill=(225, 225, 225))
        y += img.height + 26
    out.save(path)


def analyse(prep, measure, crops, out_dir=None, show_ascii=False):
    if measure is None:
        return die(f"el volcado no trae la línea de MEDIDA de la sonda (`SPARK|stage=measure`)")
    head, cards = measure
    if head.get("error"):
        return die(f"la sonda no pudo medir: {head['error']}")
    if head.get("row0") != "top":
        return die("el volcado no declara `row0=top` (la orientación del PNG): no se "
                   "sabe desde dónde contar las alturas")
    if head.get("injected") == "0" or (prep is not None and prep[0].get("injected") == "0"):
        return die("la sonda no inyectó la serie: no hay nada que medir")
    if not cards:
        return die("el volcado no trae ninguna tarjeta medida")
    if not os.path.exists(os.path.join(crops, FRAME)):
        return die(f"falta el frame en vivo {os.path.join(crops, FRAME)} "
                   f"(¿corrió la sonda en Play?)")

    # El patrón viaja en `prepare`; la medida lo repite por tarjeta para que cada
    # una se juzgue sin depender de la otra línea del volcado.
    if prep is not None and prep[0].get("pattern"):
        head.setdefault("pattern", prep[0]["pattern"])
    if prep is not None and prep[0].get("stock"):
        head.setdefault("stock", prep[0]["stock"])
    if not head.get("pattern"):
        return die("el volcado no trae el patrón inyectado (`pattern=`): no hay contra qué medir las barras")

    print(f"▸ banda de la gráfica en {len(cards)} tarjeta(s) · "
          f"frame {head.get('frame', '?')} px · patrón {head.get('pattern', '?')}")
    fails = 0
    bars = 0
    measures = []
    for card in cards:
        if "pattern" not in card:
            card["pattern"] = head.get("pattern", "")
        if "stock" not in card:
            card["stock"] = head.get("stock", "")
        f, mapped = check_card(card, crops, show_ascii)
        fails += f
        bars += len(mapped)
        try:
            inner = tuple(int(v) for v in card["inner"].replace("x", ",").split(","))
            box = [int(v) for v in card["box"].replace("x", ",").split(",")]
            measures.append((card, inner, box[2], box[3], mapped))
        except (KeyError, ValueError):
            pass

    sheet = os.path.join(out_dir or crops, SHEET)
    try:
        save_sheet(cards, crops, measures, sheet)
        print(f"  hoja de medida → {sheet}")
    except Exception as exc:  # la hoja es evidencia, no el veredicto
        print(f"  (aviso: no se pudo componer la hoja: {exc})", file=sys.stderr)

    print(f"\n{len(cards)} tarjeta(s) · {bars} barra(s) medidas · {fails} fallo(s)")
    return 1 if fails else 0


# ── Selftest: el medidor, con imágenes sintéticas (sin editor) ──────────────
def _synthetic(tmp, bars=True, shift=0, flip=False, leak=False, inside="1"):
    """Una banda sintética con el patrón real de la sonda, su frame y su volcado.
    Sirve para probar el MEDIDOR: cada caso rompe una regla distinta."""
    pattern = [0, 3, 0, 0, 7, 0, 1, 0, 0, 5, 0, 0]
    stock = [0.32, 0.35, 0.38, 0.41, 0.44, 0.47, 0.50, 0.53, 0.56, 0.59, 0.62, 0.66]
    tex_w, tex_h = 406, 26
    bw, bh = 420, 50
    inner = (10, 10, 400, 30)
    img = Image.new("RGB", (bw, bh), (18, 19, 23))
    d = ImageDraw.Draw(img)
    spans = sample_spans(tex_w, len(pattern))
    ix, iy, iw, ih = inner
    # La imagen sintética se dibuja como el PNG real: base en la ÚLTIMA fila de
    # la banda (abajo), techo arriba. `flip` ancla las barras al techo, que es el
    # defecto que el paso tiene que cazar.
    base = iy + ih - 1
    for x in range(iw):
        xt = int(x * tex_w / float(iw))
        k = column_index(min(tex_w - 1, xt), tex_w, len(pattern))
        h = int(round((round(stock[k] * (tex_h - 1)) + 1) * ih / float(tex_h)))
        d.rectangle([ix + x, base - h + 1, ix + x, base], fill=(76, 157, 69))
    if bars:
        for k, count in enumerate(pattern):
            if count <= 0:
                continue
            x0 = int(ix + tex_to_screen(spans[k][0], (0, 0, iw, ih), tex_w))
            x1 = int(ix + tex_to_screen(spans[k][1], (0, 0, iw, ih), tex_w))
            h = int(expected_height(count, max(pattern), tex_h, ih))
            x0 += shift
            x1 += shift
            y0 = iy if flip else base - h + 1
            d.rectangle([x0, y0, x1, y0 + h - 1], fill=(242, 184, 64))
    if leak:
        d.point((2, 2), fill=(242, 184, 64))

    frame = img.copy()
    crop = frame.crop((0, 0, bw, bh))
    os.makedirs(tmp, exist_ok=True)
    frame.save(os.path.join(tmp, FRAME))
    crop.save(os.path.join(tmp, "antsim-spark-card0.png"))
    dump = ("SPARKPREP|cards=1|samples=12|pattern=" + ",".join(str(v) for v in pattern)
            + "|stock=" + ",".join(f"{v:.2f}" for v in stock) + "|injected=1\n"
            + "SPARK|stage=measure|row0=top|frame=420x50|injected=1|cards=1"
            + f"|t=Sparkline_0|colony=0|tex={tex_w}x{tex_h}"
            + f"|box=0,0,{bw}x{bh}|inner={ix},{iy},{iw}x{ih}"
            + f"|cardrect=0,0,{bw}x{bh}|inside={inside}|clipped=0"
            + "|file=antsim-spark-card0.png|painted=1|scale=1\n")
    path = os.path.join(tmp, "dump.txt")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(dump)
    return path


def selftest():
    import tempfile
    fails = 0
    with tempfile.TemporaryDirectory() as tmp:
        cases = [
            ("limpio", dict(), 0),
            ("sin barras", dict(bars=False), 1),
            ("barras corridas", dict(shift=30), 1),
            ("barras del revés", dict(flip=True), 1),
            ("ámbar fuera de la banda", dict(leak=True), 1),
            ("banda fuera de la tarjeta", dict(inside="0"), 1),
        ]
        for name, kwargs, want in cases:
            case_dir = os.path.join(tmp, name.replace(" ", "-"))
            dump = _synthetic(case_dir, **kwargs)
            prep, measure = parse_dump(dump)
            # Silencioso: el selftest solo publica el veredicto de cada caso.
            import io
            from contextlib import redirect_stdout
            buf = io.StringIO()
            with redirect_stdout(buf):
                got = analyse(prep, measure, case_dir)
            if got != want:
                print(f"✗ selftest({name}): dio {got}, esperado {want}",
                      file=sys.stderr)
                print(buf.getvalue(), file=sys.stderr)
                fails += 1

        # La regla de columnas, con números a mano: con 12 muestras en 406 px,
        # la muestra 0 ocupa 0–18, la 5 ocupa 166–202 y la 11 ocupa 387–405.
        spans = sample_spans(406, 12)
        fijas = {0: (0, 18), 5: (166, 202), 11: (387, 405)}
        for k, want in fijas.items():
            if spans[k] != want:
                print(f"✗ selftest(columnas): muestra {k} → {spans[k]}, esperado {want}",
                      file=sys.stderr)
                fails += 1

    if fails == 0:
        print("✓ playpass-sparkline --selftest ok (limpio pasa · sin barras, "
              "corridas, del revés, fuga y fuera de tarjeta fallan · columnas fijas)")
    return 1 if fails else 0


def main(argv):
    if "--selftest" in argv:
        return selftest()
    args = [a for a in argv[1:] if not a.startswith("--")]
    if len(args) < 2:
        print(__doc__ or "uso: playpass-sparkline.py VOLCADO.txt DIR_RECORTES",
              file=sys.stderr)
        return 2
    dump, crops = args[0], args[1]
    if not os.path.exists(dump):
        return die(f"no existe el volcado {dump}")
    if not os.path.isdir(crops):
        return die(f"no existe la carpeta de recortes {crops}")
    prep, measure = parse_dump(dump)
    out_dir = argv[argv.index("--out") + 1] if "--out" in argv else crops
    return analyse(prep, measure, crops, out_dir, "--ascii" in argv)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
