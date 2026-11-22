"""Recolour Tripo tree textures into extra variants (画面整改 3): a deeper blue-green broadleaf, a round-canopy
autumn broadleaf (the Tripo 'autumn' model came out conifer-shaped) and a fresher birch green. Only leaf-hued
pixels (hue 45-175 deg) are touched, so trunks and branches keep their browns.
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
# round autumn canopy: greens -> orange/red, lighter leaves more golden
variant("Tree_Broadleaf", "Broadleaf_Autumn", lambda h, s, v: ((12 + v * 30) / 360 + (h - 100 / 360) * 0.15, np.clip(s * 1.2 + 0.1, 0, 0.92), np.clip(v * 1.15 + 0.04, 0, 1)))
# birch leaves: lime -> fresh spring green
variant("Tree_Birch", "Birch_Fresh", lambda h, s, v: (h + 18 / 360, s * 0.95, v * 0.9))
