#!/usr/bin/env bash
# Runs the end-to-end smoke test in the Mac build: tools/smoke.sh  (results in Logs/smoke/)
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Logs/smoke"; rm -rf "$OUT"; mkdir -p "$OUT"
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -smoketest -smokeout="$OUT" \
  -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "$OUT/player.log" &
pid=$!; ( sleep 300; kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; code=$?; kill $watchdog 2>/dev/null
cat "$OUT/smoke_result.txt" 2>/dev/null
exit $code
