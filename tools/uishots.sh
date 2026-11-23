#!/usr/bin/env bash
# Screenshots of every screen (HUD, pause, settings pages, journal, map, main menu) at six window sizes.
#   tools/uishots.sh [dest]   -> docs/screenshots/ui-res/<WxH>/*.png
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${1:-$ROOT/docs/screenshots/ui-res}"; mkdir -p "$DEST"
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -uishots -demoout="$DEST" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$ROOT/Logs/uishots.log" &
pid=$!; ( sleep 240; kill $pid 2>/dev/null ) & wd=$!
wait $pid; kill $wd 2>/dev/null
cat "$DEST/uishots.txt"
