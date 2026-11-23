#!/usr/bin/env bash
# 特效开销清单: run the effect cost bench in the Mac build and write docs/特效开销清单.md
#   tools/fxcost.sh [extra player args, e.g. -tier=Low]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Logs/fxcost"; mkdir -p "$OUT"; find "$OUT" -mindepth 1 -delete
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -fxcost -demoout="$OUT" "$@" \
  -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$OUT/player.log" &
pid=$!; ( sleep 600; kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null
python3 -P "$ROOT/tools/fxcost_report.py" "$OUT/fxcost.csv" "$ROOT/docs/特效开销清单.md"
