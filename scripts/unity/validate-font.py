"""Valida el TTF generado por el juego y saca una muestra para mirarla.

Herramienta de desarrollo (no la ejecuta CI): abre el fichero con fontTools y
con Pillow, comprueba que las tablas y la cmap están donde deben y rasteriza un
cartel de muestra para poder revisar el dibujo de los glifos a ojo.

Uso:
    python scripts/unity/validate-font.py <fuente.ttf> [salida.png]
"""
import sys

from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

SAMPLE = [
    "colonia 0  \u25cf   \u25d0   \u25cb   \u2500\u2500\u2500",
    "adultas 12/40   34/40   7/40",
    "comandos: 0  \u00b7  tick 3950  \u00b7  v\u00d73  \u00b7  8 ms",
    "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
    "abcdefghijklmnopqrstuvwxyz",
    "0123456789 !?\u00a1 #$%&()*+,-./:;<=>@[]^_`{|}~",
    "\u00e1\u00e9\u00ed\u00f3\u00fa\u00f1\u00fc \u00c1\u00c9\u00cd\u00d3\u00da\u00d1\u00dc \u00ab\u00bb \u00aa\u00ba \u00b2 \u00a7",
    "\u2192 \u21bb \u23f8 \u2b07 \u2248 \u2212 \u2713 \u2717 \u25b6 \u25a0 \u2026 \u2020 \u2013 \u2014 \u03b1",
    "\ue000 12   \ue001 5   \ue002 3   \ue000\ue001\ue002",
]


def main() -> int:
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else "font-sample.png"

    font = TTFont(src)
    names = sorted(t for t in font.keys() if t != "GlyphOrder")
    print("tablas:", ", ".join(names))
    print("glyphs:", font["maxp"].numGlyphs)
    print("upem:  ", font["head"].unitsPerEm)
    print("nombres:", [n.toUnicode() for n in font["name"].names if n.nameID in (1, 4, 6)][:4])
    print("advance A:", font["hmtx"]["A"][0], " icono U+E000:", font["hmtx"]["uniE000"][0])

    cmap = font.getBestCmap()
    for cp in [0x41, 0xE9, 0xB7, 0x2500, 0x25CF, 0xE000, 0xE001, 0xE002, 0x2B07]:
        assert cp in cmap, f"la cmap no resuelve U+{cp:04X}"
    print("cmap: ok")

    # Rasteriza a varios tamaños (los del HUD) para ver cómo cae el trazo.
    width, line_h = 1100, 34
    img = Image.new("RGB", (width, line_h * len(SAMPLE) + 20), (12, 13, 17))
    draw = ImageDraw.Draw(img)
    y = 6
    for size in (14, 17, 24):
        f = ImageFont.truetype(src, size)
        draw.text((8, y), f"{size}px  " + SAMPLE[0], font=f, fill=(238, 232, 219))
        y += line_h
    for line in SAMPLE[1:]:
        f = ImageFont.truetype(src, 17)
        draw.text((8, y), line, font=f, fill=(238, 232, 219))
        y += line_h
    # Ampliación 4× de la primera línea: el detalle del trazo, legible.
    big = Image.new("RGB", (width, 120), (12, 13, 17))
    fb = ImageFont.truetype(src, 4 * 24)
    ImageDraw.Draw(big).text((4, 4), "colonia 0 \u25cf\u25d0\u25cb \ue000\ue001\ue002", font=fb,
                             fill=(238, 232, 219))
    canvas = Image.new("RGB", (width, img.height + big.height), (12, 13, 17))
    canvas.paste(img, (0, 0))
    canvas.paste(big, (0, img.height))
    canvas.save(out)
    print("muestra:", out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
