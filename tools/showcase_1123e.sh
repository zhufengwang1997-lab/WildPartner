#!/usr/bin/env bash
# Stills and clips for 试玩反馈 1123e from the Mac build.
#   tools/showcase_1123e.sh [dest_dir] [extra player args...]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Logs/showcase"; mkdir -p "$OUT"; find "$OUT" -mindepth 1 -delete
DEST="${1:-$ROOT/Logs/showcase_out}"; mkdir -p "$DEST"; shift || true
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -showcase1123e -demoout="$OUT" "$@" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$OUT/player.log" &
pid=$!; ( sleep 400; kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null
python3 -P - "$OUT" "$DEST" <<'PY'
import sys, glob, os, shutil
from PIL import Image
src, dest = sys.argv[1], sys.argv[2]
for d in sorted(glob.glob(f"{src}/clip_*")):
    name = os.path.basename(d)[5:]
    files = sorted(glob.glob(f"{d}/f*.jpg"))[::3]
    if not files: continue
    frames = [Image.open(f).convert("RGB").resize((480, 270), Image.LANCZOS) for f in files]
    pal = [f.quantize(colors=96, method=Image.Quantize.MEDIANCUT) for f in frames]
    out = f"{dest}/{name}.gif"
    pal[0].save(out, save_all=True, append_images=pal[1:], duration=100, loop=0, optimize=True)
    print(f"GIF {len(frames)} frames {os.path.getsize(out)//1024} KB -> {out}")
for f in glob.glob(f"{src}/still_*.jpg"):
    shutil.copy(f, f"{dest}/{os.path.basename(f)[6:]}")
    print("still", os.path.basename(f)[6:])
PY
