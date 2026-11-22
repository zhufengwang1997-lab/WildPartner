"""python3 tools/make_gif.py <frames_dir> <out.gif> <fps> [repeat]  -- loop the PNG frames (sorted) into a GIF."""
import sys, glob
from PIL import Image
d, out, fps = sys.argv[1], sys.argv[2], float(sys.argv[3]); rep = int(sys.argv[4]) if len(sys.argv) > 4 else 1
ims = [Image.open(f).convert("RGB").quantize(128) for f in sorted(glob.glob(d + "/*.png"))] * rep
ims[0].save(out, save_all=True, append_images=ims[1:], duration=int(1000 / fps), loop=0)
print(out, len(ims))
