#!/usr/bin/env bash
# Find per-frame GC allocations: Development build -> soak run with the profiler writing to a file ->
# editor sums GC.Alloc per call path.   tools/allocprof.sh [seconds=90] [outdir=Logs/allocprof]
# Report: <outdir>/alloc.txt (bytes and allocations per frame, main thread, last ~2000 frames).
# The last frames include shutdown, so one-off quit allocations show up averaged over the window.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SEC="${1:-90}"; OUT="${2:-$ROOT/Logs/allocprof}"
mkdir -p "$OUT"
"$ROOT/tools/unity.sh" WildPartner.EditorTools.BuildScript.BuildMac -dev -out Builds/Dev || exit 1
"$ROOT/Builds/Dev/Mac/WildPartner.app/Contents/MacOS/WildPartner" -soak=$SEC -demoout="$OUT" \
  -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$OUT/player.log" \
  -profiler-enable -profiler-log-file "$OUT/cap.raw" -profiler-maxusedmemory 1073741824 &
pid=$!; ( sleep $((SEC + 180)); kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null
"$ROOT/tools/unity.sh" WildPartner.EditorTools.ProfileAllocReport.Run -raw "$OUT/cap.raw" -report "$OUT/alloc.txt" >/dev/null
head -15 "$OUT/alloc.txt"
