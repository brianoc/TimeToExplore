#!/usr/bin/env bash
#
# One-command verification for Time to Explore.
#
#   tools/check.sh                       # docs, data, and both Unity suites
#   tools/check.sh --fast                # everything except Unity (a few seconds)
#   tools/check.sh --changed             # fast gates plus the categories the change touches
#   tools/check.sh --changed --deep      # add the planner sweeps to the change
#   tools/check.sh --changed=origin/main # compare against another base
#   tools/check.sh --tests flight        # the fast gates plus one test category
#   tools/check.sh --tests core,smoke    # any comma-separated categories
#   tools/check.sh --tests flight-full   # cheap and deep flight checks together
#   tools/check.sh --tests playmode      # the PlayMode suite only
#   tools/check.sh --filter RouteColourTests  # one fixture, through -testFilter
#   tools/check.sh --reset               # drop the warm test project first
#   tools/check.sh --keep                # keep the generator temp directory
#
# `--changed` reads the files changed against main (committed on the branch and
# uncommitted in the tree) and runs only the categories they imply. It refuses
# a path it has no rule for rather than running everything; see
# tools/check_plan.py for the mapping. A docs or tools change stops after the
# fast gates. Add --deep when the change is to the planner maths.
#
# Test categories: core, flight, deep, presentation, smoke are EditMode;
# playmode is the PlayMode suite. `flight` is the cheap half, seconds per test;
# `deep` holds the sweeps that build a whole plan per sample and run for a
# minute or more. Do not name a category with `flight` in it: Unity matches
# -testCategory as a regular expression, so `flight` would select it too. A
# bare run executes both platforms and every category, so the deep tests still
# gate a merge. The Unity suites run in a persistent
# project under .cache/test-project with a symlinked Assets folder and its own
# Library, so the package import is paid once instead of every run. The first
# run after --reset rebuilds it (adds roughly half a minute).
#
# Unity runs under tools/unity_guard.py, which stops it if it stalls, exceeds its
# time budget, or enters the known cloud-endpoint retry loop at shutdown. Results
# it has already written are still used.
#
# Environment overrides:
#   PYTHON          Python interpreter (default: .venv/bin/python, then python3)
#   UNITY_BIN       Unity executable (default: the editor in ProjectVersion.txt)
#   TTE_TEST_CACHE  warm test project (default: .cache/test-project)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

FAST=0
KEEP=0
RESET=0
DEEP=0
CHANGED=0
BASE="main"
FILTER=""
TESTS=""
PENDING=""
for arg in "$@"; do
    if [[ -n "$PENDING" ]]; then
        case "$PENDING" in
            tests) TESTS="$arg" ;;
            filter) FILTER="$arg" ;;
        esac
        PENDING=""
        continue
    fi
    case "$arg" in
        --fast) FAST=1 ;;
        --keep) KEEP=1 ;;
        --reset) RESET=1 ;;
        --deep) DEEP=1 ;;
        --changed) CHANGED=1 ;;
        --changed=*) CHANGED=1; BASE="${arg#*=}" ;;
        --tests=*) TESTS="${arg#*=}" ;;
        --tests) PENDING=tests ;;
        --filter=*) FILTER="${arg#*=}" ;;
        --filter) PENDING=filter ;;
        -h|--help)
            awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
            exit 0
            ;;
        *)
            echo "unknown option: $arg (try --help)" >&2
            exit 2
            ;;
    esac
done
[[ -n "$PENDING" ]] && { echo "--$PENDING needs a value" >&2; exit 2; }
if (( CHANGED )) && [[ -n "$TESTS" ]]; then
    echo "--changed selects the categories itself; do not combine it with --tests" >&2
    exit 2
fi

PYTHON="${PYTHON:-$ROOT/.venv/bin/python}"
if [[ ! -x "$PYTHON" ]]; then
    PYTHON="python3"
fi

WORK=""
LOCK=""
cleanup() {
    if [[ -n "$LOCK" && -d "$LOCK" ]]; then
        rm -rf "$LOCK"
    fi
    if [[ -n "$WORK" && -d "$WORK" ]]; then
        if (( KEEP )); then
            echo "Kept temporary directory: $WORK"
        else
            rm -rf "$WORK"
        fi
    fi
}
trap cleanup EXIT

step() {
    printf '\n==> %s\n' "$*"
}

fail() {
    printf '\nFAILED: %s\n' "$*" >&2
    exit 1
}

SECONDS=0

# 1. Design bundle ----------------------------------------------------------
step "Design bundle (OKF v0.2)"
"$PYTHON" tools/validate_okf.py || fail "design bundle validation"

# 2. Catalogue --------------------------------------------------------------
step "Catalogue schema and epoch maths"
"$PYTHON" tools/validate_catalogue.py || fail "catalogue validation"

# 2b. Planet textures --------------------------------------------------------
step "Planet albedo textures"
"$PYTHON" tools/fetch_planet_textures.py --check || fail "planet texture check"

# 3. Trade catalogue ---------------------------------------------------------
step "Trade catalogue schema and markets"
"$PYTHON" tools/validate_trade.py || fail "trade validation"

# 4. Generator reproducibility ---------------------------------------------
step "Generator reproducibility"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/tte-check.XXXXXX")"
GENERATED="$WORK/solar_system.json"
"$PYTHON" tools/generate_solar_system.py --offline --output "$GENERATED" >/dev/null \
    || fail "catalogue generator"
if ! cmp -s "$GENERATED" TimeToExplore/Assets/Resources/solar_system.json; then
    echo "The committed catalogue differs from a fresh --offline generation." >&2
    diff -u TimeToExplore/Assets/Resources/solar_system.json "$GENERATED" | head -80 >&2 || true
    fail "generator output is not reproducible"
fi
echo "Generator output is byte-identical to the committed catalogue."

# 5. Sky reproducibility ----------------------------------------------------
step "Sky reproducibility"
SKY="$WORK/milky_way.png"
"$PYTHON" tools/generate_skybox.py --out "$SKY" >/dev/null || fail "sky generator"
if ! "$PYTHON" - "$SKY" TimeToExplore/Assets/Resources/Sky/milky_way.png <<'PY'
import sys

import numpy as np
from PIL import Image

fresh = np.asarray(Image.open(sys.argv[1]).convert("RGB"))
committed = np.asarray(Image.open(sys.argv[2]).convert("RGB"))

if fresh.shape != committed.shape:
    print(
        f"committed sky is {committed.shape}, a fresh generation is {fresh.shape}",
        file=sys.stderr,
    )
    sys.exit(1)

different = int(np.count_nonzero(np.any(fresh != committed, axis=-1)))
if different:
    print(
        f"committed sky differs from a fresh generation in {different} pixels",
        file=sys.stderr,
    )
    sys.exit(1)
PY
then
    fail "sky generator output is not reproducible"
fi
echo "Sky output matches the committed texture (2048x1024)."

# 5b. Bright-star catalogue --------------------------------------------------
step "Bright-star catalogue reproducibility"
STARS="$WORK/bright_stars.json"
"$PYTHON" tools/generate_bright_stars.py --out "$STARS" >/dev/null \
    || fail "bright-star generator"
if ! cmp -s "$STARS" TimeToExplore/Assets/Resources/Sky/bright_stars.json; then
    echo "The committed star catalogue differs from a fresh generation." >&2
    diff -u TimeToExplore/Assets/Resources/Sky/bright_stars.json "$STARS" | head -60 >&2 || true
    fail "bright-star catalogue is not reproducible"
fi
echo "Bright-star catalogue is byte-identical to a fresh generation."

# 6. Tooling tests ----------------------------------------------------------
step "Tooling tests"
"$PYTHON" -m unittest discover -s tools -p 'test_*.py' -q || fail "tooling tests"

if (( FAST )); then
    printf '\nFast checks passed in %d s; skipping Unity.\n' "$SECONDS"
    exit 0
fi

# 7. Unity EditMode and PlayMode tests --------------------------------------
if (( CHANGED )); then
    TESTS="$("$PYTHON" tools/check_plan.py --root "$ROOT" --base "$BASE")" \
        || fail "changed-file planning"
    if [[ -z "$TESTS" ]]; then
        printf '\nChanged files need no Unity run; fast checks passed in %d s.\n' "$SECONDS"
        exit 0
    fi
fi
if (( DEEP )); then
    TESTS="${TESTS:+$TESTS,}deep"
fi

CATEGORIES=""
RUN_EDITMODE=1
RUN_PLAYMODE=1
if [[ -n "$TESTS" ]]; then
    RUN_EDITMODE=0
    RUN_PLAYMODE=0
    IFS=',;' read -r -a requested <<< "$TESTS"
    for category in "${requested[@]}"; do
        category="${category//[[:space:]]/}"
        [[ -z "$category" ]] && continue
        case "$category" in
            core|flight|deep|presentation|smoke) RUN_EDITMODE=1 ;;
            flight-full) RUN_EDITMODE=1; category="flight;deep" ;;
            playmode) RUN_PLAYMODE=1 ;;
            *) fail "unknown test category: $category (choose core, flight, deep, flight-full, presentation, smoke, playmode)" ;;
        esac
        CATEGORIES="${CATEGORIES:+$CATEGORIES;}$category"
    done
    [[ -z "$CATEGORIES" ]] && fail "--tests needs at least one category"
elif [[ -n "$FILTER" ]]; then
    # A filter on its own is an EditMode run of one fixture.
    RUN_EDITMODE=1
    RUN_PLAYMODE=0
fi

step "Unity tests${CATEGORIES:+ ($CATEGORIES)}${FILTER:+ [filter: $FILTER]}"

UNITY_BIN="${UNITY_BIN:-}"
if [[ -z "$UNITY_BIN" ]]; then
    VERSION="$(awk '/m_EditorVersion:/ {print $2; exit}' TimeToExplore/ProjectSettings/ProjectVersion.txt)"
    UNITY_BIN="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$UNITY_BIN" ]] || fail "Unity not found at $UNITY_BIN (set UNITY_BIN)"

CACHE="${TTE_TEST_CACHE:-$ROOT/.cache/test-project}"
PROJECT="$CACHE/TimeToExplore"
ASSETS="$ROOT/TimeToExplore/Assets"

if (( RESET )); then
    echo "Resetting the warm test project at $CACHE"
    rm -rf "$CACHE"
fi

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

mkdir -p "$PROJECT"
if [[ ! -L "$PROJECT/Assets" || "$(readlink "$PROJECT/Assets")" != "$ASSETS" ]]; then
    rm -rf "$PROJECT/Assets"
    ln -s "$ASSETS" "$PROJECT/Assets"
fi
rm -rf "$PROJECT/Packages" "$PROJECT/ProjectSettings"
cp -R TimeToExplore/Packages "$PROJECT/Packages"
cp -R TimeToExplore/ProjectSettings "$PROJECT/ProjectSettings"

echo "Unity: $UNITY_BIN"
echo "Test project: $PROJECT$([[ -d "$PROJECT/Library" ]] || echo ' (first run: building the asset cache)')"

run_unity_suite() {
    local platform="$1" label="$2" stall="$3" budget="$4" filter="${5:-$CATEGORIES}"
    local slug
    slug="$(printf '%s' "$platform" | tr '[:upper:]' '[:lower:]')"
    local UNITY_LOG="$CACHE/unity-$slug.log"
    local RESULTS="$CACHE/results-$slug.xml"

    step "$label${CATEGORIES:+ ($CATEGORIES)}${FILTER:+ [filter: $FILTER]}"
    rm -f "$RESULTS" "$UNITY_LOG"

    local UNITY_ARGS=(
        -batchmode
        -nographics
        -projectPath "$PROJECT"
        -runTests
        -testPlatform "$platform"
        -testResults "$RESULTS"
        -logFile "$UNITY_LOG"
    )
    [[ -n "$filter" ]] && UNITY_ARGS+=(-testCategory "$filter")
    [[ -n "$FILTER" ]] && UNITY_ARGS+=(-testFilter "$FILTER")

    set +e
    "$PYTHON" tools/unity_guard.py \
        --log "$UNITY_LOG" \
        --stall-seconds "$stall" \
        --timeout-seconds "$budget" \
        --shutdown-grace 20 \
        -- "$UNITY_BIN" "${UNITY_ARGS[@]}"
    local UNITY_STATUS=$?
    set -e

    if (( UNITY_STATUS == 124 )); then
        echo "The guard stopped Unity; reading whatever results it had already written." >&2
    fi

    if [[ ! -f "$RESULTS" ]]; then
        echo "--- Unity log tail ---" >&2
        tail -60 "$UNITY_LOG" >&2 || true
        fail "$label produced no test results (exit $UNITY_STATUS)"
    fi

    if ! "$PYTHON" - "$RESULTS" "$label" <<'PY'
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
label = sys.argv[2]
cases = [element for element in root.iter() if element.tag == "test-case"]
failed = [case for case in cases if case.get("result") not in ("Passed", "Skipped", "Inconclusive")]
skipped = [case for case in cases if case.get("result") == "Skipped"]

for case in failed[:20]:
    name = case.get("fullname") or case.get("name") or "<unknown>"
    print(f"FAIL {name} [{case.get('result')}]")
    for tag in ("message", "stack-trace"):
        element = case.find(f".//{tag}")
        if element is not None and element.text:
            print("  " + element.text.strip().splitlines()[0])

print(f"{label}: {len(cases)} tests, {len(failed)} failed, {len(skipped)} skipped")
sys.exit(1 if failed or not cases else 0)
PY
    then
        echo "--- Unity log tail ---" >&2
        tail -40 "$UNITY_LOG" >&2 || true
        fail "$label tests failed (exit $UNITY_STATUS)"
    fi
}

if (( RUN_EDITMODE )); then
    # Unity writes no per-test progress to the log, so the whole EditMode run is
    # one silent stretch. The window covers it; the time budget bounds a hang.
    # The suite grew: the deep flight sweeps alone are about 1,000 s, so the old
    # 1,200 s stall window tripped on a run that was still healthy.
    run_unity_suite EditMode "Unity EditMode" 1800 2400
fi

if (( RUN_PLAYMODE )); then
    # The PlayMode suite starts the player loop and waits for a real preview
    # build, so it has its own log and budget. A bare run still names the
    # playmode category: the trade capture test carries only "capture" and
    # needs a graphics device, so it must not start here.
    run_unity_suite PlayMode "Unity PlayMode" 900 1200 "${CATEGORIES:-playmode}"
fi

printf '\nAll checks passed in %d s.\n' "$SECONDS"
