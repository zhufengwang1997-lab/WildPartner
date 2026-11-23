#!/usr/bin/env bash
# TC-10 soak test in the Mac build: tools/soak.sh [seconds=600] [label]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SEC="${1:-600}"; LABEL="${2:-mac-$(date +%Y%m%d)}"
OUT="$ROOT/bench_results/soak-$LABEL"; mkdir -p "$OUT"; find "$OUT" -mindepth 1 -delete
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -soak=$SEC -demoout="$OUT" \
  -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$OUT/player.log" &
pid=$!; ( sleep $((SEC + 180)); kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; code=$?; pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null
echo "player exit code $code" > "$OUT/exit.txt"
python3 -P "$ROOT/tools/soak_report.py" "$OUT" "$ROOT/docs/挂机测试-$LABEL.md"
