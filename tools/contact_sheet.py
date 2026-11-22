"""Tile images into one labelled sheet:  python3 tools/contact_sheet.py out.png cols img1 img2 ...  (label = file stem)"""
import sys, os
from PIL import Image, ImageDraw, ImageFont
out, cols, files = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
ims = [Image.open(f).convert("RGB") for f in files]
w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * (h + 28)), (245, 245, 245))
d = ImageDraw.Draw(sheet)
try: font = ImageFont.truetype("/System/Library/Fonts/PingFang.ttc", 20)
except Exception: font = ImageFont.load_default()
for i, (f, im) in enumerate(zip(files, ims)):
    x, y = (i % cols) * w, (i // cols) * (h + 28)
    sheet.paste(im.resize((w, h)), (x, y + 28))
    d.text((x + 8, y + 3), os.path.splitext(os.path.basename(f))[0], fill=(30, 30, 30), font=font)
sheet.save(out)
print(out)
