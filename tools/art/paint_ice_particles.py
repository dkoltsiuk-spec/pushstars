"""Paints the small snow and ice-chip sprites for the ice island boss battle.

Output: Assets/_Project/UI/Sprites/IceIsland/*.png (straight alpha, 4x supersampled).
Run from the repo root: python tools/art/paint_ice_particles.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

OUT = "Assets/_Project/UI/Sprites/IceIsland"
SS = 4


def canvas(size):
    return Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))


def finish(img, size, name, blur=0):
    if blur:
        img = img.filter(ImageFilter.GaussianBlur(blur * SS))
    img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def snow_dot():
    size = 32
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    px = img.load()
    for y in range(size):
        for x in range(size):
            d = math.hypot(x + .5 - size / 2, y + .5 - size / 2) / (size / 2)
            a = max(0.0, 1 - d) ** 1.6
            px[x, y] = (255, 255, 255, int(255 * min(1, a * 1.25)))
    img.save(os.path.join(OUT, "snow-dot.png"))


def snowflake():
    size = 64
    img = canvas(size)
    d = ImageDraw.Draw(img)
    c = size * SS / 2
    arm, w = size * SS * .44, int(3.2 * SS)
    for i in range(6):
        a = math.radians(90 + i * 60)
        ex, ey = c + math.cos(a) * arm, c - math.sin(a) * arm
        d.line((c, c, ex, ey), fill=(255, 255, 255, 255), width=w)
        for t, length in ((.55, .28), (.78, .18)):
            bx, by = c + math.cos(a) * arm * t, c - math.sin(a) * arm * t
            for side in (-1, 1):
                b = a + side * math.radians(42)
                d.line((bx, by, bx + math.cos(b) * arm * length, by - math.sin(b) * arm * length),
                       fill=(255, 255, 255, 255), width=int(w * .8))
    r = 4 * SS
    d.ellipse((c - r, c - r, c + r, c + r), fill=(255, 255, 255, 255))
    finish(img, size, "snowflake", blur=.35)


def shard(name, size, points, facet):
    img = canvas(size)
    d = ImageDraw.Draw(img)
    s = size * SS
    poly = [(x * s, y * s) for x, y in points]
    d.polygon(poly, fill=(176, 226, 250, 255))
    d.polygon([(x * s, y * s) for x, y in facet], fill=(236, 250, 255, 255))
    d.line(poly + [poly[0]], fill=(120, 190, 232, 255), width=int(1.4 * SS))
    finish(img, size, name)


def main():
    os.makedirs(OUT, exist_ok=True)
    snow_dot()
    snowflake()
    shard("ice-chip-1", 48, [(.5, .06), (.86, .42), (.6, .94), (.18, .62)], [(.5, .06), (.62, .45), (.18, .62)])
    shard("ice-chip-2", 48, [(.22, .12), (.9, .3), (.7, .88), (.1, .7)], [(.22, .12), (.9, .3), (.48, .46)])
    shard("ice-chunk", 64, [(.36, .04), (.8, .2), (.96, .62), (.62, .96), (.14, .8), (.06, .32)],
          [(.36, .04), (.8, .2), (.52, .5), (.06, .32)])


if __name__ == "__main__":
    main()
