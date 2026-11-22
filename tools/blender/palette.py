"""Shared colour palette for every procedurally generated asset.

All generated meshes map their UVs onto swatches of ONE 256x256 texture, so the whole world uses a
single Toon material (plus one wind variant). That keeps the SRP Batcher / GPU Resident Drawer happy
(FR-RD-04). Each swatch is a vertical gradient: v=0 bottom colour, v=1 top colour.

    python3 tools/blender/palette.py <out.png>     writes the texture
"""
import sys

GRID = 16          # 16 x 16 swatches
CELL = 16          # pixels per swatch

# name: (bottom rgb, top rgb)  -- sRGB 0..255. Tuned against docs/reference/style.jpg.
SWATCHES = {
    "leaf_green":      ((46, 104, 52),  (138, 190, 66)),
    "leaf_green_dark": ((24, 70, 52),   (70, 128, 64)),
    "leaf_birch":      ((70, 140, 60),  (176, 214, 92)),
    "leaf_autumn":     ((196, 98, 24),  (250, 190, 52)),
    "leaf_autumn2":    ((180, 70, 30),  (236, 150, 40)),
    "leaf_yellow":     ((200, 160, 30), (250, 226, 90)),
    "pine":            ((18, 62, 52),   (52, 112, 70)),
    "trunk_dark":      ((58, 40, 36),   (96, 66, 52)),
    "trunk_birch":     ((206, 204, 196), (240, 240, 232)),
    "birch_mark":      ((40, 38, 36),   (60, 56, 52)),
    "bush":            ((40, 100, 46),  (150, 200, 70)),
    "grass":           ((52, 112, 40),  (196, 222, 84)),
    "grass_dry":       ((120, 130, 50), (220, 210, 110)),
    "flower_white":    ((235, 235, 228), (255, 255, 250)),
    "flower_orange":   ((230, 110, 30), (255, 160, 60)),
    "flower_purple":   ((150, 110, 200), (200, 170, 240)),
    "flower_center":   ((230, 170, 30), (255, 210, 60)),
    "stem":            ((50, 110, 40),  (90, 150, 60)),
    "mushroom_cap":    ((180, 50, 30),  (230, 90, 50)),
    "mushroom_stem":   ((220, 210, 190), (245, 240, 225)),
    "rock":            ((110, 108, 118), (170, 166, 170)),
    "rock_moss":       ((90, 110, 70),  (150, 170, 90)),
    "cliff":           ((120, 112, 112), (176, 168, 160)),
    "stone_ruin":      ((120, 118, 112), (180, 176, 164)),
    "wood":            ((110, 66, 38),  (160, 104, 60)),
    "wood_dark":       ((70, 44, 30),   (110, 72, 46)),
    "wood_light":      ((170, 120, 70), (210, 160, 100)),
    "roof_purple":     ((70, 44, 110),  (120, 84, 168)),
    "roof_blue":       ((40, 70, 150),  (70, 110, 200)),
    "plaster":         ((210, 190, 160), (236, 222, 196)),
    "chimney":         ((150, 140, 130), (196, 188, 176)),
    "banner_purple":   ((80, 40, 120),  (110, 60, 160)),
    "gold":            ((210, 160, 40), (250, 214, 100)),
    "lamp_glow":       ((255, 200, 110), (255, 230, 160)),
    "fire":            ((255, 120, 20), (255, 220, 90)),
    "ash":             ((40, 36, 34),   (70, 64, 60)),
    "metal":           ((60, 60, 66),   (100, 100, 108)),
    "snow":            ((214, 222, 236), (250, 252, 255)),
    "mountain":        ((90, 100, 130), (150, 160, 190)),
    "cloud":           ((220, 226, 240), (255, 255, 255)),
    "owl_body":        ((110, 80, 56),  (170, 130, 90)),
    "owl_belly":       ((210, 190, 160), (240, 226, 200)),
    "owl_eye":         ((250, 200, 40), (255, 230, 90)),
    "black":           ((20, 20, 24),   (30, 30, 36)),
    "koi_body":        ((30, 20, 24),   (60, 30, 36)),
    "koi_red":         ((140, 20, 24),  (200, 40, 40)),
    "koi_fin":         ((110, 20, 30),  (170, 40, 50)),
    "dirt":            ((150, 110, 66), (196, 150, 96)),
    "placeholder":     ((255, 0, 200),  (255, 120, 230)),
    "cloth_white":     ((220, 214, 200), (245, 240, 230)),
}

NAMES = list(SWATCHES.keys())


def uv_rect(name):
    """Returns (u0, v0, u1, v1) of the swatch with a half-texel inset."""
    i = NAMES.index(name)
    cx, cy = i % GRID, i // GRID
    inset = 1.0 / (GRID * CELL)
    u0 = cx / GRID + inset
    u1 = (cx + 1) / GRID - inset
    v0 = 1 - (cy + 1) / GRID + inset
    v1 = 1 - cy / GRID - inset
    return u0, v0, u1, v1


def write_png(path):
    from PIL import Image
    size = GRID * CELL
    img = Image.new("RGB", (size, size), (255, 0, 255))
    px = img.load()
    for i, name in enumerate(NAMES):
        bot, top = SWATCHES[name]
        cx, cy = i % GRID, i // GRID
        for y in range(CELL):
            t = 1 - y / (CELL - 1)       # image y grows downward; top row = top colour
            c = tuple(int(bot[k] + (top[k] - bot[k]) * t) for k in range(3))
            for x in range(CELL):
                px[cx * CELL + x, cy * CELL + y] = c
    img.save(path)


if __name__ == "__main__":
    write_png(sys.argv[1])
