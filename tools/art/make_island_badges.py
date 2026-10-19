"""Builds the boss badges that island heroes show on their unlock bar.

Each badge is the island king's fight portrait inside a ring of the island's colour, with the
black + white sticker edge the hero head icons carry. Output lands in
Assets/_Project/Resources/AvatarCollection/<Island>IslandBadge.png (512 px, transparent).

    python tools/art/make_island_badges.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
BOSSES = ROOT / "Assets/_Project/Resources/Bosses"
OUT = ROOT / "Assets/_Project/Resources/AvatarCollection"

# name, king portrait, crop of the portrait (head + a bit of shoulder), ring colour, backdrop top/bottom
ISLANDS = [
    ("Forest", "goblin-king", (38, 62, 218, 256), (150, 230, 110), (30, 80, 40), (60, 120, 50)),
    ("Ice", "ice-golem-king", (58, 96, 222, 256), (150, 225, 255), (40, 95, 170), (70, 135, 200)),
    ("Lava", "lava-golem-king", (40, 30, 216, 220), (255, 165, 60), (90, 25, 15), (150, 50, 20)),
]


def badge(portrait, crop, ring_colour, top, bottom):
    s = 4  # supersample, scaled down at the end
    w = 512 * s
    cx, cy, r = 256 * s, 292 * s, 159 * s
    white, black, ring = 10 * s, 12 * s, 14 * s
    canvas = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    def disc(radius, fill):
        draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), fill=fill)

    disc(r, (255, 255, 255, 255))
    disc(r - white, (0, 0, 0, 255))
    disc(r - white - black, ring_colour + (255,))
    inner = r - white - black - ring
    size = 2 * inner
    fill = Image.new("RGBA", (size, size))
    fd = ImageDraw.Draw(fill)
    for y in range(size):
        t = y / size
        fd.line([(0, y), (size, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    face = portrait.crop(crop)
    scale = size / face.width * 1.05
    face = face.resize((int(face.width * scale), int(face.height * scale)), Image.LANCZOS)
    fill.alpha_composite(face, ((size - face.width) // 2, max(0, size - face.height + int(0.06 * inner))))
    mask = Image.new("L", fill.size, 0)
    ImageDraw.Draw(mask).ellipse((0, 0, size - 1, size - 1), fill=255)
    canvas.paste(fill, (cx - inner, cy - inner), mask)
    return canvas.resize((512, 512), Image.LANCZOS)


if __name__ == "__main__":
    for name, king, crop, ring_colour, top, bottom in ISLANDS:
        portrait = Image.open(BOSSES / f"{king}-portrait.png").convert("RGBA")
        badge(portrait, crop, ring_colour, top, bottom).save(OUT / f"{name}IslandBadge.png")
        print(f"{name}IslandBadge.png")
