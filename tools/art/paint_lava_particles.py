"""Paints the small ember, ash and basalt-chip sprites for the lava island boss battle.

Output: Assets/_Project/UI/Sprites/LavaIsland/*.png (straight alpha, 4x supersampled).
Run from the repo root: python tools/art/paint_lava_particles.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

OUT = "Assets/_Project/UI/Sprites/LavaIsland"
SS = 4
GLOW = (255, 150, 46, 255)


def canvas(size):
    return Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))


def finish(img, size, name, blur=0):
    if blur:
        img = img.filter(ImageFilter.GaussianBlur(blur * SS))
    img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def ember_dot():
    """Hot yellow core fading through orange into a soft transparent rim."""
    size = 32
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    core, rim = (255, 236, 168), (255, 112, 30)
    for y in range(size):
        for x in range(size):
            d = math.hypot(x + .5 - size / 2, y + .5 - size / 2) / (size / 2)
            t = min(1.0, d / .55)
            rgb = tuple(int(core[i] + (rim[i] - core[i]) * t) for i in range(3))
            a = max(0.0, 1 - d) ** 1.4
            px[x, y] = rgb + (int(255 * min(1, a * 1.35)),)
    img.save(os.path.join(OUT, "ember-dot.png"))


def ash_flake():
    size = 32
    img = canvas(size)
    d = ImageDraw.Draw(img)
    s = size * SS
    pts = [(.3, .14), (.74, .22), (.88, .58), (.62, .86), (.22, .78), (.1, .42)]
    d.polygon([(x * s, y * s) for x, y in pts], fill=(132, 122, 120, 255))
    d.polygon([(x * s, y * s) for x, y in [(.3, .14), (.74, .22), (.5, .48), (.1, .42)]], fill=(164, 154, 150, 255))
    finish(img, size, "ash-flake", blur=.5)


def chip(name, size, points, facet, crack):
    """Hot basalt shard: lit facet, glowing crack and a molten rim so it reads on the dark golem too."""
    img = canvas(size)
    d = ImageDraw.Draw(img)
    s = size * SS
    poly = [(x * s, y * s) for x, y in points]
    d.polygon(poly, fill=(96, 68, 60, 255))
    d.polygon([(x * s, y * s) for x, y in facet], fill=(146, 108, 94, 255))
    d.line([(x * s, y * s) for x, y in crack], fill=GLOW, width=int(2.2 * SS), joint="curve")
    d.line(poly + [poly[0]], fill=GLOW, width=int(2.4 * SS), joint="curve")
    finish(img, size, name)


def main():
    os.makedirs(OUT, exist_ok=True)
    ember_dot()
    ash_flake()
    chip("basalt-chip-1", 48, [(.5, .06), (.86, .42), (.6, .94), (.18, .62)], [(.5, .06), (.62, .45), (.18, .62)],
         [(.62, .45), (.56, .7), (.6, .9)])
    chip("basalt-chip-2", 48, [(.22, .12), (.9, .3), (.7, .88), (.1, .7)], [(.22, .12), (.9, .3), (.48, .46)],
         [(.48, .46), (.36, .66), (.2, .7)])
    chip("basalt-chunk", 64, [(.36, .04), (.8, .2), (.96, .62), (.62, .96), (.14, .8), (.06, .32)],
         [(.36, .04), (.8, .2), (.52, .5), (.06, .32)], [(.52, .5), (.66, .66), (.62, .9), (.62, .66), (.84, .6)])


if __name__ == "__main__":
    main()
