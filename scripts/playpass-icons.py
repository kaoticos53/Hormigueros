#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# playpass-icons.py — los iconos del HUD, medidos EN LA TARJETA REAL.
#
# POR QUÉ. La legibilidad de los tres iconos (hormiga, carga, huevo) se verificó
# sobre una muestra del `.ttf` renderizada aparte: eso prueba que el GLIFO es
# legible a 14 px, no que la tarjeta lo pinte así. Aquí se cierra el círculo con
# píxeles del Play pass.
#
# CÓMO, y por qué así. La sonda `ProbeHudIconShot` recorta la tarjeta del frame
# en vivo, y aquí se BUSCA la forma del glifo en esos píxeles (correlación
# cruzada normalizada sobre la tinta del recorte). No se usa la geometría cruda
# que publica la sonda (`cursorPos` / `charWidth` del `TextGenerator`): con los
# runs `<size>` el cursor y el rect del texto no comparten origen con la tinta
# (medido: ventanas con tinta 0,00 justo donde el cursor decía que estaba el
# icono), y perseguir esa convención costó dos rondas de medidas falsas. La
# sonda sigue publicando esos números porque son la prueba de que el run del
# icono MIDE el doble que el cuerpo.
#
# A QUÉ TAMAÑO, y aquí hubo DOS errores de escala, uno por ronda. El primero: se
# comparaba contra el CUERPO del texto (`em = fontSize × escala`); los iconos de
# la tarjeta van en un run `<size=N>` al DOBLE del cuerpo, así que la referencia se
# renderiza a `N × escala` y las ventanas se escalan con esa em — comparar al
# cuerpo mide una diferencia de ESCALA presentándola como diferencia de forma. La
# sonda publica el run de cada icono y este script comprueba además que el icono
# mide al menos el doble que el cuerpo.
#
# El SEGUNDO error de escala estaba escondido dentro de esa corrección: la
# referencia se rasterizaba a `int(round(N × escala))`, píxel ENTERO. Con el
# canvas al 91,1 % el icono vivo mide 28 × 0,911 = 25,508 px y la referencia se
# pintaba a 26 px: un 1,9 % más grande. En un glifo de patas de ~1 px ese 1,9 % no
# es un matiz, es OTRA FORMA: MEDIDO sobre los recortes del HUD en vivo, la MISMA
# ventana de la hormiga puntúa 0,32 contra la referencia a 26 px y 0,73 contra la
# referencia a 25,508 px (medido con supermuestreo 8×; el vecino entero de 25 px da
# 0,81). Y no es cosa de la hormiga: el huevo sube de 0,63 a 0,80 y la carga de
# 0,37 a 0,59-0,70. Por eso la referencia se rasteriza al tamaño EFECTIVO
# FRACCIONARIO (supermuestreo 8× y reducción LANCZOS), que es el tamaño al que el
# motor rasteriza de verdad el glifo; el hinting de FreeType ajusta el AVANCE al
# píxel entero, pero el contorno se dibuja al tamaño fraccionario con antialiased.
# La ventana viva no se redondea en ningún sitio: solo la referencia lo hacía.
#
# QUÉ DECIDE. Que cada icono del HUD se clasifique como SU glifo —comparado con
# los tres glifos renderizados del `.ttf` a SU tamaño— con margen sobre el
# segundo. Es la definición operativa de «a este tamaño se distinguen»: la mera
# presencia de tinta la cumple también una caja de tofu o un icono equivocado.
# Además se publica el parecido CRUZADO entre iconos vivos distintos: si dos
# iconos distintos se parecieran tanto como su propio glifo, no se distinguirían.
#
# Sin numpy: solo Pillow (PIL) — y con `python`, no `python3`, que en esta
# máquina no lo tiene.
#
# Uso:
#   python scripts/playpass-icons.py SALIDA.txt DIR_RECORTES [--out DIR] [--ascii]
#     SALIDA.txt     volcado de ProbeHudIconShot (una línea `HUDICONS|…`)
#     DIR_RECORTES   carpeta con los PNG. El pass los escribe en
#                    artifacts/playpass-hud-icons/ (los conserva `preserve_icons`
#                    de playpass-live.sh, porque `Temp/` lo barre Unity al cerrar
#                    el editor y la hoja de comparación se deja en esta misma
#                    carpeta). La sonda en bruto los deja en …/App/AntSim.Unity/Temp.
#   --ttf RUTA       fuente de referencia (por defecto Assets/Fonts/AntSim.ttf)
#   --out DIR        dónde dejar la hoja de comparación (por defecto DIR_RECORTES)
#   --ascii          imprime los recortes en texto (para saber DÓNDE mirar)
#   --selftest       verifica el parecido sin ficheros del pass
#
# Salida: 0 ok · 1 invariante roto · 2 uso.
# ─────────────────────────────────────────────────────────────────────────────
import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFont
except ModuleNotFoundError:  # Pillow no está en todos los intérpretes
    print("✗ falta Pillow: usa el intérprete del repo (`python`, no `python3`)",
          file=sys.stderr)
    sys.exit(2)

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

ICONS = {"Ant": 0xE000, "Load": 0xE001, "Egg": 0xE002}
FRAME = "antsim-hud-frame.png"

# Umbrales, MEDIDOS sobre la captura del HUD (no elegidos a gusto). Con la
# correlación normalizada y la referencia al tamaño fraccionario correcto, los
# TRES glifos propios puntúan 0,51 · 0,57 · 0,81 y CUALQUIER glifo ajeno en ese
# mismo sitio queda en 0,00-0,22; una ventana plana da 0,00 por definición. El
# suelo no separa «parecido» de «idéntico» —eso lo hace el MARGEN, que es la
# definición operativa— sino «aquí hay un glifo» de «aquí hay vacío o ruido»: por
# eso se pone entre los dos grupos, no pegado al más alto. El propio glifo más
# flojo es la hormiga (0,51, patas de ~1 px, lo que más cambia entre el
# rasterizador de Unity y el de FreeType: la correlación baja ahí y la silueta
# —IoU 0,79— es la que confirma).
#
# OJO con bajar este suelo para «que pase» un glifo: con la referencia a tamaño
# entero la hormiga daba 0,32 y el suelo de 0,35 la rechazaba, pero el arreglo fue
# rasterizar la referencia al tamaño correcto (0,51), NO aflojar el mínimo:
# aflojarlo habría dejado pasar justo el caso (patas perdidas, glifo ajeno) que
# este paso existe para cazar.
MIN_ICON = 0.35
MARGIN = 0.08
# Tinta mínima dentro de la ventana: 0 es la ventana vacía (icono sin pintar).
MIN_INK = 0.02
# Desalineación probada al encajar el glifo dentro de su ventana.
SEARCH = 3


def die(msg):
    print("✗ " + msg, file=sys.stderr)
    return 1


# ── Lectura del volcado de la sonda ─────────────────────────────────────────
def parse_probe(path):
    head, texts = {}, []
    with open(path, encoding="utf-8") as fh:
        raw = fh.read().strip()
    for part in raw.split("|"):
        if not part:
            continue
        if part.startswith("icon="):
            # Los iconos de una tarjeta van en UN campo, separados por `;`:
            # `icon=<Glifo> units=<n> raw=<x>,<y> w=<n>;icon=…`
            for seg in part.split(";"):
                seg = seg.strip()
                if not seg.startswith("icon="):
                    continue
                bits = seg[5:].split()
                if not bits:
                    continue
                fields = {"name": bits[0]}
                for b in bits[1:]:
                    if "=" in b:
                        k, _, v = b.partition("=")
                        fields[k] = v
                if texts:
                    texts[-1]["icons"].append(fields)
            continue
        if part.startswith("HUDICONS") or "=" not in part:
            continue
        key, _, val = part.partition("=")
        if key == "t":
            texts.append({"name": val, "icons": []})
        elif key in ("path", "file", "font", "em", "crop", "rect", "txt", "scale"):
            if texts:
                texts[-1][key] = val
            else:
                head[key] = val
        else:
            head[key] = val
    return head, texts


def unescape(s):
    return s.replace("\\p", "|").replace("\\n", "\n").replace("\\\\", "\\")


def gray_of(img):
    g = img.convert("L")
    w, h = g.size
    px = list(g.getdata())
    return [[px[y * w + x] for x in range(w)] for y in range(h)]


def similarity(gray_a, ax, ay, gray_b, mask_b):
    """Correlación cruzada NORMALIZADA (Pearson) entre una ventana de `gray_a` y
    `gray_b`, contando SOLO los píxeles de `mask_b`.

    POR QUÉ NO UNA DIFERENCIA DE BRILLOS. La versión anterior medía
    `1 − media|Δcentrado|/180` y estaba SATURADA: la tinta de un glifo es tenue
    —valores centrados de ±20 sobre 255—, así que una ventana PLANA puntuaba
    0,78-0,91 contra CUALQUIER glifo (medido, y es lo que hacía que el
    instrumento diera «0,91» a ventanas con tinta 0,00). Dividir por la
    dispersión de cada lado lo arregla de raíz: la correlación vale 1,00 solo
    cuando la FORMA coincide, no depende del brillo del panel ni del color de la
    tinta, y una ventana sin estructura da 0,00 por definición. Sobre el mismo
    par rasterizado por Unity y por FreeType la correlación queda alta porque el
    antialiased cambia el borde, no la forma."""
    hb, wb = len(mask_b), len(mask_b[0])
    if ax < 0 or ay < 0 or ay + hb > len(gray_a) or ax + wb > len(gray_a[0]):
        return 0.0
    va, vb = [], []
    for yy in range(hb):
        for xx in range(wb):
            if mask_b[yy][xx]:
                va.append(gray_a[ay + yy][ax + xx])
                vb.append(gray_b[yy][xx])
    n = len(va)
    if n < 4:
        return 0.0
    ma, mb = sum(va) / n, sum(vb) / n
    num = sa = sb = 0.0
    for i in range(n):
        da, db = va[i] - ma, vb[i] - mb
        num += da * db
        sa += da * da
        sb += db * db
    if sa < 1e-9 or sb < 1e-9:      # ventana plana (o referencia plana): sin forma
        return 0.0
    return max(0.0, num / (sa ** 0.5 * sb ** 0.5))


def mask_of(gray, thr=None):
    lo = min(min(r) for r in gray)
    hi = max(max(r) for r in gray)
    t = thr if thr is not None else (lo + hi) / 2.0
    return [[v > t for v in row] for row in gray], (lo, hi)


# Supermuestreo de la referencia: se rasteriza el glifo a `size_px × SUPERSAMPLE`
# y se reduce con LANCZOS, que es lo que permite usar el tamaño FRACCIONARIO real
# del icono (25,508 px) en vez de un entero. `ImageFont.truetype` solo acepta
# tamaños enteros, así que sin esto no hay forma de pedirle 25,508 px.
SUPERSAMPLE = 8


def reference_size_px(run, scale, em):
    """El tamaño EFECTIVO, EN PÍXELES, del icono: `run × escala`, y el cuerpo
    como respaldo si el volcado no trae run (anterior a F5.1ter).

    Existe como función para que se pueda FIJAR: el error que dejó la hormiga en
    rojo fue redondear esto (`int(round(…))`, 25,508 → 26 px). El decimal no es
    un adorno de la medida, es el tamaño al que se rasteriza la referencia; ver
    el encabezado y el `--selftest`."""
    return run * scale if run > 0 else em


def render_glyph(ttf, size_px, cp):
    """El glifo del `.ttf` rasterizado a `size_px` px. ADMITE DECIMALES: el
    tamaño que se le pasa es el EFECTIVO del icono (`run × escala`), no su
    redondeo (ver el encabezado: redondearlo costaba 0,4 de correlación a la
    hormiga y no es un matiz, es otra forma). Con `SUPERSAMPLE = 1` cae al
    rasterizado entero de siempre, sin supermuestreo."""
    big = max(1, int(round(float(size_px) * SUPERSAMPLE)))
    font = ImageFont.truetype(ttf, big)
    img = Image.new("L", (big * 3, big * 3), 0)
    ImageDraw.Draw(img).text((big, big), chr(cp), font=font, fill=255)
    box = img.getbbox()
    if box is None:
        return None
    img = img.crop(box)
    if SUPERSAMPLE == 1:
        return img
    return img.resize((max(1, int(round(img.width / SUPERSAMPLE))),
                       max(1, int(round(img.height / SUPERSAMPLE)))),
                      Image.LANCZOS)


def silhouette_iou(win, mask_ref):
    """Solapamiento de SILUETAS (IoU: intersección sobre unión) entre la ventana
    viva y la máscara de la referencia. Complementa a la correlación, y no es
    redundante: el antialiased de dos rasterizadores distintos (el de Unity y
    FreeType) mueve el borde de CADA trazo, así que en un glifo de patas finas
    —la hormiga— la correlación baja aunque la silueta sea la misma. El IoU
    mide qué píxeles son tinta, y eso sí sobrevive al cambio de rasterizador."""
    rh, rw = len(mask_ref), len(mask_ref[0])
    if len(win) != rh or not all(len(r) == rw for r in win):
        return 0.0
    live, _span = mask_of(win)
    inter = union = 0
    for yy in range(rh):
        for xx in range(rw):
            a, b = mask_ref[yy][xx], live[yy][xx]
            if a and b:
                inter += 1
            elif a or b:
                union += 1
    return inter / (inter + union) if (inter + union) else 0.0


def check_peaks(gray, peaks, margenes, refs, show_windows=False):
    """Publica y JUZGA los glifos localizados en una imagen viva: cada uno tiene
    que puntuar más con SU glifo de referencia que con los otros dos, y traer
    tinta. Es la definición operativa de «a este tamaño se distinguen»: la
    presencia de tinta la cumple también una caja de tofu o el icono equivocado."""
    fails, checked = 0, 0
    for name, (score, x, y) in sorted(peaks.items(), key=lambda kv: -kv[1][0]):
        rg, rmask = refs[name]
        rh, rw = len(rg), len(rg[0])
        win = [row[x:x + rw] for row in gray[y:y + rh]]
        ink = 0.0
        if len(win) == rh and all(len(r) == rw for r in win):
            wm, _span = mask_of(win)
            ink = sum(1 for r in wm for v in r if v) / (rh * rw)
        iou = silhouette_iou(win, rmask)
        others = {k: v for k, v in margenes[name].items() if k != name}
        best_other = max(others.values()) if others else 0.0
        margin = score - best_other
        ok = score >= MIN_ICON and margin >= MARGIN and ink >= MIN_INK
        tabla = " ".join(f"{k}={v:.2f}" for k, v in
                         sorted(margenes[name].items(), key=lambda kv: -kv[1]))
        print(f"  {'✓' if ok else '✗'} {name}: correlación {score:.2f} en ({x},{y}) · "
              f"silueta IoU {iou:.2f} · margen {margin:+.2f} · tinta {ink:.2f} "
              f"(mínimos {MIN_ICON:.2f} / {MARGIN:+.2f} / {MIN_INK:.2f}) · {tabla}")
        if not ok:
            fails += 1
        checked += 1
        if show_windows:
            print_ascii(win, label=f"HUD {name} en ({x},{y})")
    return fails, checked


def classify(win_gray, refs, search=SEARCH):
    """Parecido de la ventana viva contra cada glifo de referencia, probando
    desplazamientos pequeños (el encaje no cae siempre en el mismo píxel)."""
    scores = {}
    for name, (rg, rm) in refs.items():
        best = 0.0
        for oy in range(-search, search + 1):
            for ox in range(-search, search + 1):
                s = similarity(win_gray, ox, oy, rg, rm)
                if s > best:
                    best = s
        scores[name] = best
    return scores


def find_icons(frame, refs):
    """Localiza los tres iconos en el frame en vivo.

    Dos decisiones que costaron medidas falsas:
      · El PNG de `ScreenCapture` sale con la fila 0 abajo, así que se prueban
        las DOS orientaciones y gana la que explica mejor los glifos — deducir la
        convención a mano ya dio dos rondas de medidas malas.
      · La búsqueda se limita a la COLUMNA del HUD (el panel de tarjetas está
        pegado al borde izquierdo), en los dos extremos verticales: buscar en
        todo el frame encontraba manchas del TABLERO que se parecían a los tres
        glifos por igual (medido: «Ant» a 0,87 con margen +0,04 en medio del
        terreno — un blob, no un icono).
    """
    h, w = frame.height, frame.width
    col = 560
    bands = [(0, min(h, 440)), (max(0, h - 440), h)]
    best = None
    for flip in (False, True):
        im = frame.transpose(Image.FLIP_TOP_BOTTOM) if flip else frame
        gray = gray_of(im)
        for (y0, y1) in bands:
            sub = [row[:col] for row in gray[y0:y1]]
            jimg = Image.new("RGB", (col, y1 - y0))
            jimg.putdata([(v, v, v) for row in sub for v in row])
            peaks = find_in_frame(jimg, refs)
            if not peaks:
                continue
            mg = verify_identity(sub, peaks, refs)
            score = 0.0
            for name, (s, _x, _y) in peaks.items():
                others = [v for k, v in mg[name].items() if k != name]
                score += s + (s - (max(others) if others else 0.0))
            if best is None or score > best[0]:
                # las coordenadas vuelven al frame completo
                full = {k: (v[0], v[1], v[2] + y0) for k, v in peaks.items()}
                best = (score, full, mg, flip)
    if best is None:
        return None, None, False
    return best[1], best[2], best[3]


def verify_identity(gray, peaks, refs):
    """En el sitio donde CADA glifo encaja mejor, cuánto puntúan los otros dos:
    sin eso, «cobertura alta» no dice que el icono sea ESE icono."""
    out = {}
    for name, (score, x, y) in peaks.items():
        scores = {name: score}
        for other, (rg, rm) in refs.items():
            if other == name:
                continue
            rh, rw = len(rg), len(rg[0])
            anchor = next((xx, yy) for yy, row in enumerate(rm) for xx, v in enumerate(row) if v)
            # La esquina del encaje llevó el ancla a (x, y): se deshace para
            # volver a aplicar cada glifo en el MISMO punto de la imagen.
            ox, oy = x - anchor[0], y - anchor[1]
            scores[other] = similarity(gray, ox, oy, rg, rm)
        out[name] = scores
    return out


def analyse(head, texts, ttf, out_dir, show_ascii=False, show_radius=False):
    em = float(head.get("em", "14"))          # cuerpo del texto (fontSize × escala)
    scale = float(head.get("scale", "1"))
    # El tamaño del ICONO: el run `<size=N>` que la sonda publica por icono, por la
    # escala del canvas. Es el que manda para la referencia y las ventanas. Un
    # volcado anterior a F5.1ter no trae run: entonces se mide contra el cuerpo,
    # que es como se medía antes (y la comprobación del doble no aplica).
    run = 0.0
    for t in texts:
        for i in t.get("icons", []):
            try:
                run = max(run, float(i.get("size") or 0))
            except ValueError:
                pass
    ipx = reference_size_px(run, scale, em)
    dobla = None if run <= 0 else ipx >= 2 * em - 0.01
    como = (f" · icono {ipx:.3f} px (run <size={run:g}> × escala {scale:g})"
            if run > 0 else " · icono sin run de tamaño en el volcado")
    # El tamaño de la referencia es el FRACCIONARIO (ver el encabezado): el entero
    # solo se publica para poder leerlo.
    print(f"tamaños del HUD: cuerpo {em:.3f} px{como} → referencia renderizada a "
          f"{ipx:.3f} px (supermuestreo {SUPERSAMPLE}×)")

    refs = {}
    for name, cp in ICONS.items():
        g = render_glyph(ttf, ipx, cp)
        if g is None:
            return die(f"la fuente {ttf} no dibuja {name} (U+{cp:04X}) a {ipx:.3f} px")
        gs = gray_of(g)
        refs[name] = (gs, mask_of(gs)[0])

    fails, checked, iconos, recortes = 0, 0, 0, 0
    # El icono tiene que medir AL MENOS el doble que el cuerpo: es la corrección que
    # pide esta medida (a 14 px los tres glifos salían como manchas indistinguibles)
    # y se comprueba sobre el HUD vivo, no sobre el código que lo declara.
    if dobla is False:
        print(f"  ✗ el icono mide {ipx / em:.2f}× el cuerpo: no llega al doble")
        fails += 1
    elif dobla:
        print(f"  ✓ el icono mide {ipx / em:.2f}× el cuerpo (al menos el doble)")
    if show_ascii:
        for name, (rg, _) in refs.items():
            print_ascii(rg, label=f"referencia {name} ({ipx:.3f}px)")

    # Lo que la escena en vivo dice del tipo: si la fuente del HUD no tuviera
    # los glifos, lo de abajo no podría salir bien (y el fallo sería de la
    # ESCENA, no de la fuente). Se publica para saber dónde mirar.
    fam = next((t.get("font") for t in texts if t.get("font")), "?")
    for name, key in (("Ant", "hasAnt"), ("Load", "hasLoad"), ("Egg", "hasEgg")):
        has = next((t.get(key) for t in texts if t.get(key)), head.get(key, "?"))
        print(f"  · la fuente del HUD ({fam}, {head.get('font', '?')} px) tiene "
              f"{name}: {has}")
        if has == "0":
            fails += 1

    frame_path = os.path.join(out_dir, FRAME)
    if not os.path.exists(frame_path):
        return die(f"falta el frame en vivo {frame_path} (¿corrió la sonda en Play?)")
    print(f"\n▸ frame en vivo: {FRAME} ({Image.open(frame_path).size[0]}×"
          f"{Image.open(frame_path).size[1]} px)")
    frame = Image.open(frame_path).convert("RGB")
    # El recorte de la tarjeta que dejó la sonda: la prueba de que el HUD pinta
    # los iconos AL DOBLE y de que cada uno se lee como el suyo. Dentro del
    # recorte se busca la FORMA en los píxeles, igual que en el frame — la
    # geometría cruda de uGUI no se usa: medido, el `cursorPos` del generador con
    # runs `<size>` y el rect del texto no comparten origen con la tinta, y
    # perseguir esa convención costó dos rondas de medidas falsas.
    card, cpeaks = None, None
    card_png = next((t.get("file") for t in texts
                     if t.get("file") and t.get("file") not in ("-", "")), None)
    if card_png and os.path.exists(os.path.join(out_dir, card_png)):
        card = Image.open(os.path.join(out_dir, card_png)).convert("RGB")
        cgray = gray_of(card)
        cpeaks = find_in_frame(card, refs)
        if cpeaks:
            print(f"\n▸ recorte de la tarjeta {card_png} ({card.width}×{card.height} "
                  "px): cada icono buscado por su forma dentro del recorte")
            f, c = check_peaks(cgray, cpeaks, verify_identity(cgray, cpeaks, refs),
                               refs, show_radius)
            fails += f
            checked += c
            iconos += c

    # El recorte TIENE que ser un trozo del frame: es lo que hace que lo medido en
    # el recorte esté medido en el HUD en vivo.
    print("\n▸ ¿el recorte es del frame en vivo?")
    f, c = provenance(frame, texts, out_dir)
    fails += f
    checked += c
    recortes += c
    # Búsqueda libre en el frame: INFORMATIVA, no decide. Con la métrica honesta
    # es un buscador de una aguja (el glifo) en un pajar (el terreno) y deja falsos
    # negativos; el veredicto sale del recorte, que son los mismos píxeles.
    peaks, margenes, flip = find_icons(frame, refs)
    if peaks:
        print("  · búsqueda libre en el frame (informativa, no decide): orientación "
              + ("volteada" if flip else "tal cual") + " · "
              + " ".join(f"{n}={v[0]:.2f}@({v[1]},{v[2]})"
                         for n, v in sorted(peaks.items(), key=lambda kv: -kv[1][0])))

    sheet = os.path.join(out_dir, "antsim-hud-icons-compare.png")
    save_sheet(card, cpeaks, round(ipx), refs, sheet)
    print(f"  hoja de comparación → {sheet}")
    if show_ascii:
        for name, (rg, _) in refs.items():
            print_ascii(rg, label=f"referencia {name} ({ipx:.3f}px)")

    print(f"\n{iconos} glifo(s) clasificados en el HUD en vivo · {recortes} "
          f"recorte(s) verificados como trozo del frame · {fails} fallo(s)")
    return 1 if fails else 0


def find_in_frame(frame_img, refs, region=None, top=600):
    """Busca cada glifo de referencia DENTRO del frame en vivo.

    Este es el camino que quedó tras descartar el de la geometría de uGUI: el
    `RectTransform` del texto mide en unidades del canvas, su `GetWorldCorners`
    devuelve otra cosa y el recorte salía desplazado (medido: rect de 406×135
    unidades frente a 401×69 px de recorte, con los iconos fuera por los lados).
    Buscar la forma en los píxeles no necesita ninguna de esas coordenadas.

    El universo de candidatos es la TINTA del frame (pocos miles de píxeles), así
    que el coste es bajo. Devuelve por glifo (parecido, x, y) y deja los mejores
    `top` candidatos para el desempate por identidad.
    """
    gray = gray_of(frame_img)
    if region:
        x0, y0, x1, y1 = region
        gray = [row[x0:x1] for row in gray[y0:y1]]
        off = (x0, y0)
    else:
        off = (0, 0)
    mask, span = mask_of(gray)
    if span[1] - span[0] < 24:
        return None
    ink = [(x, y) for y, row in enumerate(mask) for x, v in enumerate(row) if v]
    peaks = {}
    for name, (rg, rm) in refs.items():
        rh, rw = len(rg), len(rg[0])
        rp = [(x, y) for y, row in enumerate(rm) for x, v in enumerate(row) if v]
        if not rp:
            continue
        anchor = rp[0]
        seen, cands = set(), []
        for (cx, cy) in ink:
            ox, oy = cx - anchor[0], cy - anchor[1]
            if (ox, oy) in seen or ox < 0 or oy < 0 or oy + rh > len(gray) or ox + rw > len(gray[0]):
                continue
            seen.add((ox, oy))
            hit = sum(1 for (px_, py_) in rp if mask[oy + py_][ox + px_])
            cands.append((hit / len(rp), ox, oy))
        cands.sort(reverse=True)
        best = (0.0, 0, 0)
        for cover, ox, oy in cands[:top]:
            s = similarity(gray, ox, oy, rg, rm)
            if s > best[0]:
                best = (s, ox + off[0], oy + off[1])
        peaks[name] = best
    return peaks


def provenance(frame, texts, out_dir):
    """Cada recorte de tarjeta tiene que ser un trozo LITERAL del frame en vivo: es
    lo que convierte el recorte en prueba y no en un dibujo aparte.

    POR QUÉ ESTO Y NO UNA BÚSQUEDA LIBRE EN EL FRAME. Antes se buscaba cada glifo
    en el frame entero; con la métrica arreglada esa búsqueda deja FALSOS
    NEGATIVOS (encontraba una mancha del terreno con 0,11 de correlación en vez
    del icono), porque el terreno tiene mucha tinta y el glifo es una aguja. El
    recorte, en cambio, SON los mismos píxeles del frame —los escribe el mismo
    `EncodeToPNG` sobre el mismo array—, así que el respaldo correcto es
    comprobarlo: si el recorte es un trozo del frame, todo lo medido en él está
    medido en el frame.

    LA CUENTA DE LA FILA, que no es la del `rect` (y costó un falso negativo):
    el `rect` viene en coordenadas de PANTALLA (origen abajo-izquierda) y el PNG
    del frame guarda las filas al revés (fila 0 = abajo), así que el trozo del
    recorte empieza en `H - y0 - h`, no en `y0`. El CONTENIDO no está espejado
    (medido: el icono vivo puntúa 0,41 contra su referencia recta y 0,34 contra la
    volteada). Las dos convenciones se prueban igualmente —y se publica cuál
    vale— porque deducirlas a mano ya dio dos rondas de medidas falsas.
    """
    fails, checked = 0, 0
    for t in texts:
        rect, file = t.get("rect"), t.get("file")
        if not rect or not file or file in ("-", ""):
            continue
        path = os.path.join(out_dir, file)
        checked += 1
        if not os.path.exists(path):
            print(f"  ✗ {file}: no existe (¿la sonda no lo guardó?)")
            fails += 1
            continue
        card = Image.open(path).convert("RGB")
        try:
            x0, y0 = (int(v) for v in rect.split(",")[:2])
        except ValueError:
            print(f"  ✗ {file}: rect ilegible «{rect}»")
            fails += 1
            continue
        where = None
        for flip in (False, True):
            fr = frame.transpose(Image.FLIP_TOP_BOTTOM) if flip else frame
            for top in (fr.height - y0 - card.height, y0):
                sub = fr.crop((x0, top, x0 + card.width, top + card.height))
                if sub.width == card.width and sub.height == card.height \
                        and sub.tobytes() == card.tobytes():
                    where = ("volteado" if flip else "tal cual") + \
                            (" (fila de pantalla)" if top == y0 else " (fila del PNG)")
                    break
            if where:
                break
        if where is None:
            print(f"  ✗ {file}: NO es un trozo del frame en {rect}")
            fails += 1
        else:
            print(f"  ✓ {file}: es el trozo de {rect} del frame en vivo "
                  f"({card.width}×{card.height} px, {where})")
    return fails, checked


def print_ascii(gray, width=40, label=""):
    h = len(gray)
    lo = min(min(r) for r in gray)
    hi = max(max(r) for r in gray)
    t = (lo + hi) / 2
    print(f"  ┌ {label} ({len(gray[0])}×{h} px, contraste {hi-lo})")
    for row in gray:
        print("  │" + "".join("#" if v > t else "." for v in row))


def as_image(gray):
    """Una matriz de grises a imagen RGB, para poder pegar y ampliar."""
    im = Image.new("RGB", (len(gray[0]), len(gray)))
    for y, row in enumerate(gray):
        for x, v in enumerate(row):
            im.putpixel((x, y), (v, v, v))
    return im


def save_sheet(card, cpeaks, size_label, refs, path, S=6):
    """La hoja de comparación: la TARJETA del HUD en vivo con los tres iconos
    LOCALIZADOS marcados, y debajo, icono a icono, el recorte vivo al lado de su
    glifo de referencia — a la MISMA ampliación. Sin la misma escala, «comparar»
    es una ilusión, y sin la referencia al lado, también.

    Se compone desde el RECORTE de la tarjeta y no desde el frame: las posiciones
    son las que la comparación encontró (y juzgó) en esos píxeles, no las de una
    búsqueda libre por el frame entero.

    La referencia es el propio `.ttf` del juego rasterizado a `size_label` px (el
    tamaño del run del icono), así que la fila de abajo responde exactamente a la
    pregunta que importa: ¿lo que el HUD pinta se parece a lo que la fuente dice?
    """
    head = card.convert("RGB").resize((card.width * 3, card.height * 3), Image.NEAREST)
    d = ImageDraw.Draw(head)
    for name, (_score, x, y) in (cpeaks or {}).items():
        rg = refs[name][0]
        d.rectangle([x * 3, y * 3, (x + len(rg[0])) * 3, (y + len(rg)) * 3],
                    outline=(255, 220, 90))
        d.text((x * 3, max(0, y * 3 - 12)), name, fill=(255, 220, 90))

    rows = []
    for name, (rg, _mask) in refs.items():
        live = None
        if card is not None and cpeaks and name in cpeaks:
            _s, cx, cy = cpeaks[name]
            live = [row[cx:cx + len(rg[0])] for row in gray_of(card)[cy:cy + len(rg)]]
            if len(live) != len(rg) or any(len(r) != len(rg[0]) for r in live):
                live = None
        row = Image.new("RGB", (len(rg[0]) * 2 + 24, len(rg)), (18, 18, 22))
        if live:
            row.paste(as_image(live).resize((len(rg[0]) * S // 2, len(rg) * S // 2),
                                           Image.NEAREST), (6, 0))
            row.paste(as_image(rg).resize((len(rg[0]) * S // 2, len(rg) * S // 2),
                                          Image.NEAREST),
                      (len(rg[0]) * S // 2 + 18, 0))
            row = row.resize((row.width * 2, row.height * 2), Image.NEAREST)
        rows.append((name, row))

    pad = 12
    W = max(head.width, sum(r.width + pad for _n, r in rows) + pad)
    H = head.height + 26 + max((r.height for _n, r in rows), default=0) + 26
    out = Image.new("RGB", (W, H), (18, 18, 22))
    out.paste(head, (pad, 22))
    draw = ImageDraw.Draw(out)
    draw.text((pad, 6), f"Tarjeta del HUD en vivo (recorte del frame) — iconos "
                        f"marcados · referencia {size_label} px", fill=(230, 230, 230))
    x, y = pad, head.height + 30
    draw.text((pad, y - 14), "vivo  |  referencia .ttf", fill=(200, 200, 200))
    for name, row in rows:
        out.paste(row, (x, y + 14))
        draw.text((x, y), name, fill=(230, 230, 230))
        x += row.width + pad
    out.save(path)


def selftest():
    """El parecido tiene que reconocer una forma y rechazar otra, sin ficheros
    del pass (aquí no está el `.ttf`)."""
    fails = 0
    size = 14
    font = ImageFont.load_default(size=size)
    imgs = {}
    for ch in ("A", "o"):
        img = Image.new("L", (size * 3, size * 3), 0)
        ImageDraw.Draw(img).text((size, size), ch, font=font, fill=255)
        box = img.getbbox()
        imgs[ch] = img.crop(box)
    side = size * 4
    canvas = [[12] * side for _ in range(side)]
    a = imgs["A"]
    gA = list(a.getdata())
    for y in range(a.height):
        for x in range(a.width):
            canvas[y + 2][x + 3] = gA[y * a.width + x]
    refs = {}
    for ch in ("A", "o"):
        gs = gray_of(imgs[ch])
        refs[ch] = (gs, mask_of(gs)[0])
    scores = classify(canvas, refs)
    if scores["A"] < 0.9 or scores["o"] >= scores["A"]:
        print(f"✗ el parecido no distingue A de o: {scores}", file=sys.stderr)
        fails = 1
    if scores["A"] - scores["o"] < MARGIN:
        print(f"✗ margen insuficiente entre formas distintas: {scores}", file=sys.stderr)
        fails = 1
    # El IoU de una máscara CONSIGO MISMA es 1,00 y no puede pasar de ahí: esta
    # comprobación caza el error de cuenta que llegó a publicar 6,54 (imposible)
    # en la primera versión del IoU.
    gA2, mGA2 = gray_of(imgs["A"]), mask_of(gray_of(imgs["A"]))[0]
    iou = silhouette_iou(gA2, mGA2)
    if not 0.98 <= iou <= 1.0:
        print(f"✗ el IoU de una silueta consigo misma debería ser 1,00: {iou:.2f}",
              file=sys.stderr)
        fails = 1
    # El tamaño de la referencia NO se redondea: es el defecto que dejó la
    # hormiga en rojo (25,508 px comparados contra una referencia de 26 px — un
    # 1,9 % de escala que se lee como diferencia de forma). Se fija el decimal
    # con los números del volcado real, sin ficheros ni capturas.
    ipx = reference_size_px(28, 0.911, 12.759)
    if abs(ipx - 25.508) > 1e-3:
        print(f"✗ el tamaño del icono debe ser el efectivo fraccionario "
              f"(28 × 0,911 = 25,508), no su redondeo: {ipx}", file=sys.stderr)
        fails = 1
    print(f"playpass-icons --selftest (A={scores['A']:.2f} · "
          f"o={scores['o']:.2f} · margen {scores['A'] - scores['o']:+.2f} · "
          f"icono {ipx:.3f} px sin redondear)")
    return fails


def main(argv):
    if "--selftest" in argv:
        return selftest()
    args = [a for a in argv[1:] if not a.startswith("--")]
    if len(args) < 2:
        print(__doc__ or "uso: playpass-icons.py SALIDA.txt DIR_RECORTES", file=sys.stderr)
        return 2
    dump, crop_dir = args[0], args[1]
    ttf = "src/App/AntSim.Unity/Assets/Fonts/AntSim.ttf"
    out_dir = crop_dir
    if "--ttf" in argv:
        ttf = argv[argv.index("--ttf") + 1]
    if "--out" in argv:
        out_dir = argv[argv.index("--out") + 1]
    if not os.path.exists(dump):
        return die(f"no existe el volcado {dump}")
    if not os.path.exists(ttf):
        return die(f"no existe la fuente {ttf}")
    head, texts = parse_probe(dump)
    if not texts:
        return die("el volcado no trae ninguna tarjeta con iconos")
    if not head.get("em"):
        return die("el volcado no trae el tamaño efectivo (`em=`)")
    return analyse(head, texts, ttf, out_dir, "--ascii" in argv,
                   "--windows" in argv)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
