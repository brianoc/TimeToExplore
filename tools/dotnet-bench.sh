#!/usr/bin/env bash
#
# Run the route planner benchmark on a plain .NET host.
#
# The harness compiles the same Core and Simulation sources the Editor uses,
# with a tiny UnityEngine shim, so its numbers show the runtime without the
# Unity Editor process. It writes the same phase shape as tools/bench.sh.
#
#   tools/dotnet-bench.sh
#   tools/dotnet-bench.sh --out build/dotnet-bench.json
#
# Environment overrides:
#   DOTNET          dotnet executable (default: dotnet)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET="${DOTNET:-dotnet}"
OUT="build/dotnet-bench.json"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --out)
            OUT="${2:?--out needs a path}"
            shift 2
            ;;
        -h|--help)
            sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown option: $1 (try --help)" >&2
            exit 2
            ;;
    esac
done

"$DOTNET" build tools/dotnet-bench/DotnetBench.csproj -c Release >/dev/null
"$DOTNET" run --project tools/dotnet-bench/DotnetBench.csproj -c Release --no-build -- --out "$OUT"

printf '\nRun JSON: %s\n' "$OUT"
