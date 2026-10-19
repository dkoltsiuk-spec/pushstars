"""Builds the glow masks that light up the magma golems' cracks (CharacterToon's _EmissionMap).

The mask follows the albedo's UVs: white where the Tripo diffuse paints molten rock, black elsewhere.
The small and big golems paint red cracks on dark stone, so redness (r - b) finds them; the king's
whole body is orange rock, so only its yellow-hot seams ((r + g) / 2 - b) count.

Output: Assets/_Project/Art/Characters/LavaGolems/<Mini|Max|King>/GlowMask.png. It sits beside, not
inside, Textures/, where GolemBossSetup takes the largest texture as the albedo.
Run from the repo root: python tools/art/paint_lava_glow.py
"""
import glob
import os

import numpy as np
from PIL import Image

ROOT = "Assets/_Project/Art/Characters/LavaGolems"
# golem: (metric, threshold, ramp width)
GOLEMS = {"Mini": ("red", .12, .25), "Max": ("red", .1, .2), "King": ("hot", .3, .15)}


def main():
    for golem, (metric, low, width) in GOLEMS.items():
        source = glob.glob(os.path.join(ROOT, golem, "Textures", "*.png"))[0]
        rgb = np.asarray(Image.open(source).convert("RGB")).astype(np.float32) / 255
        r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        heat = r - b if metric == "red" else (r + g) * .5 - b
        mask = np.clip((heat - low) / width, 0, 1) ** 1.5
        grey = (mask * 255 + .5).astype(np.uint8)
        Image.fromarray(np.stack([grey] * 3, axis=-1)).save(os.path.join(ROOT, golem, "GlowMask.png"))


if __name__ == "__main__":
    main()
