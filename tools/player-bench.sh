#!/usr/bin/env bash
#
# Build and run the CoreCLR player route benchmark.
#
# The player hook only runs with -tteBench, so this is the same macOS player
# the game ships. It writes the same route/phase JSON as tools/dotnet-bench.sh.
#
#   tools/player-bench.sh
#   tools/player-bench.sh --out build/player-bench.json
#
# Environment overrides:
#   UNITY_BIN   Unity executable (default: the editor in ProjectVersion.txt)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

OUT="build/player-bench.json"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --out)
            OUT="${2:?--out needs a path}"
            shift 2
            ;;
        -h|--help)
            sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown option: $1 (try --help)" >&2
            exit 2
            ;;
    esac
done

UNITY_BIN="${UNITY_BIN:-}"
if [[ -z "$UNITY_BIN" ]]; then
    VERSION="$(awk '/m_EditorVersion:/ {print $2; exit}' TimeToExplore/ProjectSettings/ProjectVersion.txt)"
    UNITY_BIN="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$UNITY_BIN" ]] || { echo "Unity not found at $UNITY_BIN (set UNITY_BIN)" >&2; exit 1; }

mkdir -p build/player
APP="$ROOT/build/player/TimeToExplore.app/Contents/MacOS/TimeToExplore"

echo "Unity: $UNITY_BIN"
"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$ROOT/TimeToExplore" \
    -executeMethod TimeToExplore.EditorTools.PlayerBenchmarkBuild.Build \
    -logFile "$ROOT/build/player-build.log"
[[ -x "$APP" ]] || { echo "The player was not built; see build/player-build.log" >&2; exit 1; }

echo "Player: $APP"
"$APP" -tteBench -tteBenchOut "$ROOT/$OUT" -batchmode \
    -logFile "$ROOT/build/player-run.log"

printf '\nRun JSON: %s\nLog: %s\n' "$OUT" "build/player-run.log"
