#!/usr/bin/env bash
# FR-RD-06: GPU Resident Drawer off vs on on the same four views, then docs/GRD评估报告.md
#   tools/grd.sh [label]   (results in bench_results/grd-<label>/)
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LABEL="${1:-mac-$(date +%Y%m%d)}"
OUT="$ROOT/bench_results/grd-$LABEL"; mkdir -p "$OUT"; find "$OUT" -mindepth 1 -delete
for g in off on; do
  "$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -grdbench -demoout="$OUT" -grd=$g \
    -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$OUT/player_$g.log" &
  pid=$!; ( sleep 300; kill $pid 2>/dev/null ) & watchdog=$!
  wait $pid; pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null
done
python3 -P "$ROOT/tools/grd_report.py" "$OUT" "$ROOT/docs/GRD评估报告.md"
