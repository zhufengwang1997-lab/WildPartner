"""Before/after comparison for docs/screenshots/art (画面整改清单):
    python3 tools/before_after.py <out.jpg> <title> <before.png> <after.png> [more before/after pairs...]
Each pair is one row: before | after | docs/reference/style.jpg, labelled."""
import sys
from PIL import Image, ImageDraw, ImageFont
out, title, files = sys.argv[1], sys.argv[2], sys.argv[3:]
W, H = 960, 540
ref = Image.open("docs/reference/style.jpg").convert("RGB").resize((W, H))
try:
    font = ImageFont.truetype("/System/Library/Fonts/Hiragino Sans GB.ttc", 26)
except Exception:
    font = ImageFont.load_default()
rows = len(files) // 2
sheet = Image.new("RGB", (W * 3, 50 + rows * (H + 36)), (245, 245, 245))
d = ImageDraw.Draw(sheet)
d.text((12, 10), title, fill=(20, 20, 20), font=font)
for r in range(rows):
    y = 50 + r * (H + 36)
    for c, (img, lab) in enumerate([(files[2 * r], "改前"), (files[2 * r + 1], "改后"), (None, "参考 style.jpg")]):
        im = ref if img is None else Image.open(img).convert("RGB").resize((W, H))
        sheet.paste(im, (c * W, y + 34))
        d.text((c * W + 10, y + 2), lab + ("" if img is None else "  " + img.split("/")[-1]), fill=(40, 40, 40), font=font)
sheet.save(out, quality=86)
print(out)
