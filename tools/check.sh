#!/usr/bin/env bash
#
# One-command verification for Time to Explore.
#
#   tools/check.sh                       # docs, data, and the full Unity suite
#   tools/check.sh --fast                # everything except Unity (a few seconds)
#   tools/check.sh --tests flight        # the fast gates plus one test category
#   tools/check.sh --tests core,smoke    # any comma-separated categories
#   tools/check.sh --reset               # drop the warm test project first
#   tools/check.sh --keep                # keep the generator temp directory
#
# Test categories: core, flight, presentation, smoke. The Unity suite runs in a
# persistent project under .cache/test-project with a symlinked Assets folder and
# its own Library, so the package import is paid once instead of every run. The
# first run after --reset rebuilds it (adds roughly half a minute).
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
TESTS=""
for arg in "$@"; do
    case "$arg" in
        --fast) FAST=1 ;;
        --keep) KEEP=1 ;;
        --reset) RESET=1 ;;
        --tests=*) TESTS="${arg#*=}" ;;
        --tests) TESTS="__NEXT__" ;;
        -h|--help)
            sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            if [[ "$TESTS" == "__NEXT__" ]]; then
                TESTS="$arg"
            else
                echo "unknown option: $arg (try --help)" >&2
                exit 2
            fi
            ;;
    esac
done
[[ "$TESTS" == "__NEXT__" ]] && { echo "--tests needs a value" >&2; exit 2; }

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

# 3. Generator reproducibility ---------------------------------------------
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

# 4. Sky reproducibility ----------------------------------------------------
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

# 5. Tooling tests ----------------------------------------------------------
step "Tooling tests"
"$PYTHON" -m unittest discover -s tools -p 'test_*.py' -q || fail "tooling tests"

if (( FAST )); then
    printf '\nFast checks passed in %d s; skipping Unity.\n' "$SECONDS"
    exit 0
fi

# 6. Unity EditMode tests ---------------------------------------------------
CATEGORIES=""
if [[ -n "$TESTS" ]]; then
    IFS=',;' read -r -a requested <<< "$TESTS"
    for category in "${requested[@]}"; do
        category="${category//[[:space:]]/}"
        [[ -z "$category" ]] && continue
        case "$category" in
            core|flight|presentation|smoke) ;;
            *) fail "unknown test category: $category (choose core, flight, presentation, smoke)" ;;
        esac
        CATEGORIES="${CATEGORIES:+$CATEGORIES;}$category"
    done
    [[ -z "$CATEGORIES" ]] && fail "--tests needs at least one category"
fi

step "Unity EditMode tests${CATEGORIES:+ ($CATEGORIES)}"

UNITY_BIN="${UNITY_BIN:-}"
if [[ -z "$UNITY_BIN" ]]; then
    VERSION="$(awk '/m_EditorVersion:/ {print $2; exit}' TimeToExplore/ProjectSettings/ProjectVersion.txt)"
    UNITY_BIN="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$UNITY_BIN" ]] || fail "Unity not found at $UNITY_BIN (set UNITY_BIN)"

CACHE="${TTE_TEST_CACHE:-$ROOT/.cache/test-project}"
PROJECT="$CACHE/TimeToExplore"
UNITY_LOG="$CACHE/unity.log"
RESULTS="$CACHE/results.xml"
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
rm -f "$RESULTS" "$UNITY_LOG"

echo "Unity: $UNITY_BIN"
echo "Test project: $PROJECT$([[ -d "$PROJECT/Library" ]] || echo ' (first run: building the asset cache)')"

UNITY_ARGS=(
    -batchmode
    -nographics
    -projectPath "$PROJECT"
    -runTests
    -testPlatform EditMode
    -testResults "$RESULTS"
    -logFile "$UNITY_LOG"
)
[[ -n "$CATEGORIES" ]] && UNITY_ARGS+=(-testCategory "$CATEGORIES")

set +e
"$PYTHON" tools/unity_guard.py \
    --log "$UNITY_LOG" \
    --stall-seconds 480 \
    --timeout-seconds 1800 \
    --shutdown-grace 20 \
    -- "$UNITY_BIN" "${UNITY_ARGS[@]}"
UNITY_STATUS=$?
set -e

if (( UNITY_STATUS == 124 )); then
    echo "The guard stopped Unity; reading whatever results it had already written." >&2
fi

if [[ ! -f "$RESULTS" ]]; then
    echo "--- Unity log tail ---" >&2
    tail -60 "$UNITY_LOG" >&2 || true
    fail "Unity produced no test results (exit $UNITY_STATUS)"
fi

if ! "$PYTHON" - "$RESULTS" <<'PY'
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
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

print(f"Unity EditMode: {len(cases)} tests, {len(failed)} failed, {len(skipped)} skipped")
sys.exit(1 if failed or not cases else 0)
PY
then
    echo "--- Unity log tail ---" >&2
    tail -40 "$UNITY_LOG" >&2 || true
    fail "Unity EditMode tests failed (exit $UNITY_STATUS)"
fi

printf '\nAll checks passed in %d s.\n' "$SECONDS"
