#!/usr/bin/env bash
#
# One-command verification for Time to Explore.
#
#   tools/check.sh          # docs, catalogue, generator determinism, Unity EditMode tests
#   tools/check.sh --fast   # everything except Unity (a few seconds)
#   tools/check.sh --keep   # keep the temporary Unity project and its log
#
# The Unity run happens on a throwaway copy of the project, so it is safe to
# leave the editor open on the real one.
#
# Environment overrides:
#   PYTHON     Python interpreter (default: .venv/bin/python, then python3)
#   UNITY_BIN  Unity executable (default: the editor named in ProjectVersion.txt)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

FAST=0
KEEP=0
for arg in "$@"; do
    case "$arg" in
        --fast) FAST=1 ;;
        --keep) KEEP=1 ;;
        -h|--help)
            sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown option: $arg (try --help)" >&2
            exit 2
            ;;
    esac
done

PYTHON="${PYTHON:-$ROOT/.venv/bin/python}"
if [[ ! -x "$PYTHON" ]]; then
    PYTHON="python3"
fi

WORK=""
cleanup() {
    if [[ -n "$WORK" && -d "$WORK" ]]; then
        if (( KEEP )); then
            echo "Kept temporary project: $WORK"
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

if (( FAST )); then
    step "Fast checks passed; skipping Unity"
    exit 0
fi

# 4. Unity EditMode tests ---------------------------------------------------
step "Unity EditMode tests"

UNITY_BIN="${UNITY_BIN:-}"
if [[ -z "$UNITY_BIN" ]]; then
    VERSION="$(awk '/m_EditorVersion:/ {print $2; exit}' TimeToExplore/ProjectSettings/ProjectVersion.txt)"
    UNITY_BIN="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$UNITY_BIN" ]] || fail "Unity not found at $UNITY_BIN (set UNITY_BIN)"

PROJECT="$WORK/TimeToExplore"
mkdir -p "$PROJECT"
cp -R TimeToExplore/Assets TimeToExplore/Packages TimeToExplore/ProjectSettings "$PROJECT/"
RESULTS="$WORK/results.xml"
UNITY_LOG="$WORK/unity.log"

echo "Unity: $UNITY_BIN"
echo "Test project: $PROJECT"

set +e
"$UNITY_BIN" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT" \
    -runTests \
    -testPlatform EditMode \
    -testResults "$RESULTS" \
    -logFile "$UNITY_LOG"
UNITY_STATUS=$?
set -e

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

printf '\nAll checks passed.\n'
