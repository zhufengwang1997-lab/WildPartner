#!/usr/bin/env bash
# UpscaleBench end to end (one command, PRD 6.6): run the bench in the Mac build, compute image quality, write the report.
#   tools/bench.sh [label] [extra player args, e.g. -bench-scene=SC-02]
# Results: bench_results/<label>/ (bench.csv, quality.csv, PNG captures, FLIP heat maps); report docs/超分评估报告-<label>.md
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LABEL="${1:-mac-$(date +%Y%m%d)}"; shift || true
OUT="$ROOT/bench_results/$LABEL"; mkdir -p "$OUT"; find "$OUT" -mindepth 1 -delete
PY="${PY:-$HOME/wildpartner-venv/bin/python}"
"$ROOT/Builds/Mac/WildPartner.app/Contents/MacOS/WildPartner" -bench -capture="$OUT" "$@" \
  -screen-fullscreen 0 -screen-width 2560 -screen-height 1440 -logFile "$OUT/player.log" &
pid=$!; ( sleep 2400; kill $pid 2>/dev/null ) & watchdog=$!
wait $pid; pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null
"$PY" "$ROOT/Tools~/quality_eval.py" "$OUT" "$OUT" --out "$OUT/quality.csv" --heatmaps "$OUT/flip"
"$PY" "$ROOT/Tools~/bench_report.py" "$OUT" "$ROOT/docs/超分评估报告-$LABEL.md"
