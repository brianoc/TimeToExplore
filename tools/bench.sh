#!/usr/bin/env bash
#
# Route planning benchmark — the measuring instrument for plot, retime and Begin
# cost. It is not part of tools/check.sh: that runs the NUnit suites, and this is
# an editor -executeMethod entry point, so a default check cannot start it.
#
#   tools/bench.sh             run and compare against the committed baseline
#   tools/bench.sh --record    run, show the deltas, then replace the baseline
#   tools/bench.sh --color     force ANSI colour (default: on a terminal only)
#   tools/bench.sh --no-color  plain text
#
# Six routes, each through a plot (PlanFastest plus the cold window build), a
# retime at the window middle and at its maximum, and Begin (PlanFastest at a
# date three hours later, then the retime to the window maximum). One untimed
# warm-up per case, then the median of three.
#
# Environment overrides:
#   PYTHON          Python interpreter (default: .venv/bin/python, then python3)
#   UNITY_BIN       Unity executable (default: the editor in ProjectVersion.txt)
#   TTE_TEST_CACHE  warm test project (default: .cache/test-project)
#
# The baseline is tools/planning-benchmark-baseline.json. A run on another
# machine or Unity version says so and its deltas are not meaningful.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RECORD=0
COLOR=""
for arg in "$@"; do
    case "$arg" in
        --record) RECORD=1 ;;
        --color) COLOR=1 ;;
        --no-color) COLOR=0 ;;
        -h|--help)
            sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown option: $arg (try --help)" >&2
            exit 2
            ;;
    esac
done
if [[ -z "$COLOR" ]]; then
    if [[ -t 1 ]]; then COLOR=1; else COLOR=0; fi
fi

PYTHON="${PYTHON:-$ROOT/.venv/bin/python}"
if [[ ! -x "$PYTHON" ]]; then
    PYTHON="python3"
fi

UNITY_BIN="${UNITY_BIN:-}"
if [[ -z "$UNITY_BIN" ]]; then
    VERSION="$(awk '/m_EditorVersion:/ {print $2; exit}' TimeToExplore/ProjectSettings/ProjectVersion.txt)"
    UNITY_BIN="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$UNITY_BIN" ]] || { echo "Unity not found at $UNITY_BIN (set UNITY_BIN)" >&2; exit 1; }

CACHE="${TTE_TEST_CACHE:-$ROOT/.cache/test-project}"
PROJECT="$CACHE/TimeToExplore"
ASSETS="$ROOT/TimeToExplore/Assets"

mkdir -p "$CACHE"
LOCK="$CACHE/.run-lock"
if ! mkdir "$LOCK" 2>/dev/null; then
    if [[ -f "$LOCK/pid" ]] && kill -0 "$(cat "$LOCK/pid")" 2>/dev/null; then
        echo "Another check is using $CACHE (pid $(cat "$LOCK/pid"))." >&2
        exit 2
    fi
    rm -rf "$LOCK"
    mkdir "$LOCK"
fi
echo $$ > "$LOCK/pid"
cleanup() { rm -rf "$LOCK"; }
trap cleanup EXIT

mkdir -p "$PROJECT"
if [[ ! -L "$PROJECT/Assets" || "$(readlink "$PROJECT/Assets")" != "$ASSETS" ]]; then
    rm -rf "$PROJECT/Assets"
    ln -s "$ASSETS" "$PROJECT/Assets"
fi
rm -rf "$PROJECT/Packages" "$PROJECT/ProjectSettings"
cp -R TimeToExplore/Packages "$PROJECT/Packages"
cp -R TimeToExplore/ProjectSettings "$PROJECT/ProjectSettings"

LOG="$CACHE/benchmark-unity.log"
TABLE="$CACHE/benchmark-table.txt"
RESULTS="$CACHE/benchmark-run.json"
BASELINE="$ROOT/tools/planning-benchmark-baseline.json"
rm -f "$LOG" "$TABLE" "$RESULTS"

export TTE_BENCH_BASELINE="$BASELINE"
export TTE_BENCH_RECORD="$RECORD"
export TTE_BENCH_TABLE="$TABLE"
export TTE_BENCH_RESULTS="$RESULTS"
export TTE_BENCH_COLOR="$COLOR"

echo "Unity: $UNITY_BIN"
echo "Test project: $PROJECT$([[ -d "$PROJECT/Library" ]] || echo ' (first run: building the asset cache)')"

set +e
"$PYTHON" tools/unity_guard.py \
    --log "$LOG" \
    --stall-seconds 1200 \
    --timeout-seconds 3600 \
    --shutdown-grace 20 \
    -- "$UNITY_BIN" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT" \
    -executeMethod TimeToExplore.EditorTools.PlanningBenchmark.Run \
    -logFile "$LOG"
STATUS=$?
set -e

if [[ ! -f "$TABLE" ]]; then
    echo "The benchmark wrote no table (Unity exit $STATUS)." >&2
    echo "--- Unity log tail ---" >&2
    tail -60 "$LOG" >&2 || true
    exit 1
fi

cat "$TABLE"
printf '\nTable: %s\nRun JSON: %s\nBaseline: %s\n' \
    "$TABLE" "$RESULTS" "$BASELINE"
if (( RECORD )); then
    printf 'Recorded this run as the baseline.\n'
fi
