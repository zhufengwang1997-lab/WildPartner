#!/usr/bin/env bash
# Clips for 试玩反馈 1123d (river flow, fishing, moonflower night) from the Mac build.
#   tools/showcase.sh [dest_dir]   -> <dest>/6_rabbits.gif, 7_fish.gif, 8_deer.gif, 8_ride.gif + still_*.jpg
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Logs/showcase"; rm -rf "$OUT"; mkdir -p "$OUT"
DEST="${1:-$ROOT/Logs/showcase_out}"; mkdir -p "$DEST"
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -showcase1123d -demoout="$OUT" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$OUT/player.log" &
pid=$!; ( sleep 400; kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; kill $watchdog 2>/dev/null
python3 -P - "$OUT" "$DEST" <<'PY'
import sys, glob, os, shutil
from PIL import Image
src, dest = sys.argv[1], sys.argv[2]
for folder, name in [("river", "2_river_falls_to_lake"), ("fishing", "1_fishing_koi"), ("moon", "3_moonflower_night")]:
    step = 5 if folder == "river" else 3                              # river 6 fps, others 10 fps
    files = sorted(glob.glob(f"{src}/{folder}/f*.jpg"))[::step]
    if not files: print("no frames", folder); continue
    frames = [Image.open(f).convert("RGB").resize((432, 243) if folder == "river" else (480, 270), Image.LANCZOS) for f in files]
    pal = [f.quantize(colors=96, method=Image.Quantize.MEDIANCUT) for f in frames]
    out = f"{dest}/{name}.gif"
    pal[0].save(out, save_all=True, append_images=pal[1:], duration=step * 33, loop=0, optimize=True)
    print(f"GIF {len(frames)} frames {os.path.getsize(out)//1024} KB -> {out}")
for f in glob.glob(f"{src}/still_*.jpg"):
    shutil.copy(f, f"{dest}/{os.path.basename(f)[6:]}")
    print("still", os.path.basename(f)[6:])
PY
