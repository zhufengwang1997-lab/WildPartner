#!/usr/bin/env bash
# Thin wrapper around the Unity editor CLI with one automatic retry (see CASE-03: the first build
# after regenerating assets can crash inside a package AssetPostprocessor; the second run succeeds).
#   tools/unity.sh <executeMethod> [extra args...]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity}"
METHOD="$1"; shift
mkdir -p "$ROOT/Logs"
LOG="$ROOT/Logs/$(echo "$METHOD" | sed 's/.*\.//').log"
for attempt in 1 2; do
  "$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod "$METHOD" -logFile "$LOG" "$@"
  code=$?
  [ $code -eq 0 ] && { grep -E "^\[(Build|World|ProjectSetup|Caps|Models|Setup|Map)\]" "$LOG"; exit 0; }
  grep -q "error CS" "$LOG" && { grep "error CS" "$LOG" | sort -u; exit $code; }
  echo "unity exited $code (attempt $attempt), log: $LOG" >&2
done
exit $code
