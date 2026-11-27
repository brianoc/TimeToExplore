#!/usr/bin/env bash
#
# Run the route planner benchmark on a plain .NET host.
#
# The harness compiles the same Core and Simulation sources the Editor uses,
# with a tiny UnityEngine shim, so its numbers show the runtime without the
# Unity Editor process. It writes the same phase shape as tools/bench.sh.
#
#   tools/dotnet-bench.sh
#   tools/dotnet-bench.sh -c Debug --out build/dotnet-bench-debug.json
#   tools/dotnet-bench.sh --micro
#   tools/dotnet-bench.sh --matrix
#
# The configuration defaults to Release. Debug builds the same sources with
# optimisation disabled, to compare against the Editor's debug-built
# assemblies. --micro runs the small numeric probe instead of the routes.
# --matrix plots every major planet from every other one and writes
# build/plot-matrix.json and build/plot-matrix.md; --runs sets the measured
# repeats (default 3) and --markdown sets the table path.
#
# Environment overrides:
#   DOTNET          dotnet executable (default: dotnet)
#   DOTNET_*        runtime JIT switches are passed through by the host, e.g.
#                   DOTNET_TieredCompilation=0 or DOTNET_TieredPGO=0

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET="${DOTNET:-dotnet}"
OUT="build/dotnet-bench.json"
OUT_SET=""
MARKDOWN="build/plot-matrix.md"
CONFIG="Release"
MODE=""
RUNS="3"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --out)
            OUT="${2:?--out needs a path}"
            OUT_SET="1"
            shift 2
            ;;
        --markdown)
            MARKDOWN="${2:?--markdown needs a path}"
            shift 2
            ;;
        --runs)
            RUNS="${2:?--runs needs a count}"
            shift 2
            ;;
        -c|--configuration)
            CONFIG="${2:?--configuration needs Debug or Release}"
            shift 2
            ;;
        --micro|--matrix)
            MODE="$1"
            shift
            ;;
        -h|--help)
            sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown option: $1 (try --help)" >&2
            exit 2
            ;;
    esac
done

case "$CONFIG" in
    Debug|Release) ;;
    *) echo "configuration must be Debug or Release, got $CONFIG" >&2; exit 2 ;;
esac

if [[ "$MODE" == "--matrix" && -z "$OUT_SET" ]]; then
    OUT="build/plot-matrix.json"
fi

"$DOTNET" build tools/dotnet-bench/DotnetBench.csproj -c "$CONFIG" >/dev/null
if [[ "$MODE" == "--micro" ]]; then
    "$DOTNET" run --project tools/dotnet-bench/DotnetBench.csproj -c "$CONFIG" \
        --no-build -- --micro
elif [[ "$MODE" == "--matrix" ]]; then
    "$DOTNET" run --project tools/dotnet-bench/DotnetBench.csproj -c "$CONFIG" \
        --no-build -- --matrix --out "$OUT" --markdown "$MARKDOWN" --runs "$RUNS"
else
    "$DOTNET" run --project tools/dotnet-bench/DotnetBench.csproj -c "$CONFIG" \
        --no-build -- --out "$OUT"
fi

printf '\nRun JSON: %s\n' "$OUT"
if [[ "$MODE" == "--matrix" ]]; then
    printf 'Table: %s\n' "$MARKDOWN"
fi
