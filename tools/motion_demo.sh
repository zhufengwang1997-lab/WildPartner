#!/usr/bin/env bash
# Hair/skirt check (试玩反馈 1123b-4): records run / turn / jumps / 8 m drop in the Mac build and writes a GIF.
#   tools/motion_demo.sh <out.gif>
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Logs/motiondemo"; rm -rf "$OUT"; mkdir -p "$OUT"
GIF="${1:-$ROOT/Logs/motiondemo.gif}"
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -motiondemo -demoout="$OUT" \
  -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "$OUT/player.log" &
pid=$!; ( sleep 240; kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; kill $watchdog 2>/dev/null
python3 -P - "$OUT" "$GIF" <<'PY'
import sys, glob
from PIL import Image
src, gif = sys.argv[1], sys.argv[2]
files = sorted(glob.glob(src + "/f*.jpg"))[::2]          # 15 fps
frames = [Image.open(f).convert("RGB").crop((230, 90, 630, 470)).resize((320, 304)) for f in files]   # around the witch, small enough for the docs
pal = [f.quantize(colors=96, method=Image.Quantize.MEDIANCUT) for f in frames]
pal[0].save(gif, save_all=True, append_images=pal[1:], duration=67, loop=0, optimize=True)
print(f"GIF {len(frames)} frames -> {gif}")
PY
