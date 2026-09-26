"""Paints the meme skull for the aura reward: an emoji-style white skull with a thick black
keyline, soft grey shading and deep sockets. Output: Assets/_Project/Resources/Rewards/AuraSkull.png.
Rendered 4x and downsampled for clean edges."""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import distance_transform_edt

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "Rewards"))
SIZE = 512
SS = 4
N = SIZE * SS


def px(value):
    return value * SS


def blank():
    return Image.new("L", (N, N), 0)


def arr(img):
    return np.asarray(img, dtype=np.float32) / 255


def keyline(mask, width):
    dist = distance_transform_edt(mask < .5)
    return np.maximum(np.clip(width - dist + .5, 0, 1), mask)


def over(canvas, rgb, alpha):
    a = alpha[..., None]
    canvas[..., :3] = np.asarray(rgb, np.float32) * a + canvas[..., :3] * (1 - a)
    canvas[..., 3] = alpha + canvas[..., 3] * (1 - alpha)


def ellipse(cx, cy, rx, ry, angle=0):
    img = blank()
    big = Image.new("L", (N, N), 0)
    ImageDraw.Draw(big).ellipse([px(cx - rx), px(cy - ry), px(cx + rx), px(cy + ry)], fill=255)
    if angle:
        big = big.rotate(angle, center=(px(cx), px(cy)), resample=Image.BICUBIC)
    img.paste(big)
    return img


def rounded(x0, y0, x1, y1, r):
    img = blank()
    ImageDraw.Draw(img).rounded_rectangle([px(x0), px(y0), px(x1), px(y1)], radius=px(r), fill=255)
    return img


def union(*masks):
    return np.maximum.reduce([arr(m) for m in masks])


def main():
    canvas = np.zeros((N, N, 4), np.float32)
    skull = union(ellipse(256, 210, 196, 176), rounded(132, 226, 380, 392, 70), rounded(174, 330, 338, 462, 40))
    # Hollow cheeks under the cheekbones give the face its skull shape.
    skull = np.clip(skull - union(ellipse(104, 372, 46, 44), ellipse(408, 372, 46, 44)), 0, 1)

    shadow = arr(Image.fromarray((keyline(skull, px(15)) * 255).astype(np.uint8)).transform(
        (N, N), Image.AFFINE, (1, 0, px(-7), 0, 1, px(-12))))
    over(canvas, (0, 0, 0), shadow * .5)
    over(canvas, (14, 14, 18), keyline(skull, px(15)))

    t = np.clip((np.arange(N)[:, None] - px(40)) / px(430), 0, 1)
    fill = np.stack([255 - 46 * t, 255 - 46 * t, 255 - 38 * t], axis=-1) * np.ones((N, N, 1))
    canvas[..., :3] = fill * skull[..., None] + canvas[..., :3] * (1 - skull[..., None])

    # Volume: grey on the lower right edge, where an offset copy of the skull no longer covers.
    lit = arr(Image.fromarray((skull * 255).astype(np.uint8)).transform((N, N), Image.AFFINE, (1, 0, px(16), 0, 1, px(20))))
    rim = np.clip(1 - distance_transform_edt(skull > .5) / px(40), 0, 1) ** 1.3
    over(canvas, (150, 150, 168), rim * (1 - lit) * skull * .7)

    ink = (14, 14, 18)
    # Sockets sloping down toward the nose read as a scowl, like the meme skull.
    sockets = blank()
    d = ImageDraw.Draw(sockets)
    for side in (-1, 1):
        pts = [(118, 222), (226, 250), (234, 290), (206, 318), (148, 314), (116, 270)]
        d.polygon([(px(256 + side * (256 - x)), px(y)) for x, y in pts], fill=255)
    sockets = arr(sockets.filter(ImageFilter.GaussianBlur(px(10)))) > .5
    over(canvas, ink, sockets.astype(np.float32))

    nose = blank()
    d = ImageDraw.Draw(nose)
    d.polygon([(px(256), px(372)), (px(228), px(340)), (px(284), px(340))], fill=255)
    d.ellipse([px(228), px(322), px(258), px(352)], fill=255)
    d.ellipse([px(254), px(322), px(284), px(352)], fill=255)
    nose = arr(nose.filter(ImageFilter.GaussianBlur(px(3)))) > .5
    over(canvas, ink, nose.astype(np.float32))

    teeth = blank()
    d = ImageDraw.Draw(teeth)
    d.line([(px(184), px(414)), (px(328), px(414))], fill=255, width=px(8))
    for x in (208, 232, 256, 280, 304):
        d.line([(px(x), px(392)), (px(x), px(448))], fill=255, width=px(7))
    over(canvas, ink, arr(teeth) * skull)

    # Glossy highlight on the upper-left of the cranium.
    spec = arr(ellipse(168, 128, 52, 30, 30).filter(ImageFilter.GaussianBlur(px(6))))
    over(canvas, (255, 255, 255), spec * skull * .9)

    alpha = canvas[..., 3:4]
    rgba = np.concatenate([canvas[..., :3] / np.maximum(alpha, 1e-4), alpha * 255], axis=2)
    img = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8)).resize((SIZE, SIZE), Image.LANCZOS)
    img.save(os.path.join(OUT, "AuraSkull.png"))
    print("wrote AuraSkull.png", img.size)


if __name__ == "__main__":
    main()
