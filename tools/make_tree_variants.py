"""Recolour Tripo tree textures into extra variants (画面整改 3; trees replaced by the user 2026-11-23): a deeper
blue-green broadleaf and an orange-red autumn maple beside the golden one. Only leaf pixels are touched (green
leaves: hue 45-175 deg; autumn leaves: bright, saturated gold), so trunks and branches keep their browns.
Usage: python3 tools/make_tree_variants.py   (writes Assets/_Game/Models/Tripo/Variants/*.png)"""
import colorsys, os
import numpy as np
from PIL import Image

SRC = "Assets/_Game/Models/Tripo"
DST = SRC + "/Variants"

def hsv_arrays(img):
    a = np.asarray(img.convert("RGB")).astype(np.float32) / 255.0
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1); d = mx - mn + 1e-6
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6.0
    s = np.where(mx > 0, (mx - mn) / (mx + 1e-6), 0); v = mx
    return h, s, v

def to_rgb(h, s, v):
    i = np.floor(h * 6) % 6; f = h * 6 - np.floor(h * 6)
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    r = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [v, q, p, p, t, v])
    g = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [t, v, v, q, p, p])
    b = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [p, p, t, v, v, q])
    return Image.fromarray((np.stack([r, g, b], -1).clip(0, 1) * 255).astype(np.uint8))

def leaf_mask(h, s):
    deg = h * 360
    m = np.clip((deg - 45) / 15, 0, 1) * np.clip((175 - deg) / 15, 0, 1)
    return m * np.clip((s - 0.12) / 0.1, 0, 1)

def variant(src, out, fn):
    h, s, v = hsv_arrays(Image.open(f"{SRC}/{src}_BaseColor.png"))
    m = leaf_mask(h, s)
    h2, s2, v2 = fn(h, s, v)
    h = h * (1 - m) + h2 * m; s = s * (1 - m) + s2 * m; v = v * (1 - m) + v2 * m
    to_rgb(h % 1.0, np.clip(s, 0, 1), np.clip(v, 0, 1)).save(f"{DST}/{out}.png")
    print("wrote", out)

os.makedirs(DST, exist_ok=True)
# deeper, slightly blue-green canopy like the shaded trees in the reference
variant("Tree_Broadleaf", "Broadleaf_Deep", lambda h, s, v: (np.full_like(h, 128 / 360) + (h - 100 / 360) * 0.4, s * 0.82, v * 0.8))
# autumn maple: golden leaves -> orange-red (bright saturated gold only; the brown bark is darker / duller)
def gold_mask(h, s, v):
    deg = h * 360
    return np.clip((deg - 22) / 8, 0, 1) * np.clip((72 - deg) / 8, 0, 1) * np.clip((s - 0.35) / 0.1, 0, 1) * np.clip((v - 0.42) / 0.1, 0, 1)

h, s, v = hsv_arrays(Image.open(f"{SRC}/Tree_Autumn_BaseColor.png"))
m = gold_mask(h, s, v)
h2 = h - 24 / 360 * (0.6 + 0.4 * v); s2 = np.clip(s * 1.08, 0, 0.95); v2 = v * 0.95
to_rgb((h * (1 - m) + h2 * m) % 1.0, s * (1 - m) + s2 * m, v * (1 - m) + v2 * m).save(f"{DST}/Autumn_Red.png")
print("wrote Autumn_Red")
