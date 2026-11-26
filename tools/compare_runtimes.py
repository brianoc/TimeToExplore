#!/usr/bin/env python3
"""Compare route-benchmark runs from different runtimes.

    tools/compare_runtimes.py --mono build/mono.json \
        --coreclr build/coreclr.json --dotnet build/dotnet-bench.json

The Unity benchmark (tools/bench.sh) and the plain .NET harness
(tools/dotnet-bench.sh) write the same route/phase shape. The first runtime
named is the reference: later columns show the percentage change against it.
You can name any runtime with ``--run label=path``.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

PHASES = [
    ("plot", "Plot"),
    ("retime-mid", "Retime mid"),
    ("retime-max", "Retime max"),
    ("drag-median", "Drag median"),
    ("drag-worst", "Drag worst"),
    ("begin-min", "Begin"),
]


def load_run(path: str) -> dict:
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    routes = {}
    for route in data.get("routes", []):
        phases = {entry["phase"]: entry["milliseconds"] for entry in route.get("phases", [])}
        routes[route["route"]] = {
            "name": route.get("displayName", route["route"]),
            "phases": phases,
            "floorDays": route.get("floorDays"),
        }
    return {
        "version": data.get("unityVersion") or data.get("runtime") or Path(path).stem,
        "routes": routes,
    }


def percent(reference: float, value: float) -> str:
    if value == 0 or reference == 0:
        return "—"
    change = (value / reference - 1.0) * 100.0
    return f"{change:+.0f}%"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mono", help="Unity Mono benchmark JSON")
    parser.add_argument("--coreclr", help="Unity 7 CoreCLR benchmark JSON")
    parser.add_argument("--dotnet", help="plain .NET harness JSON")
    parser.add_argument("--run", action="append", default=[], metavar="LABEL=PATH",
                        help="any further run, in column order")
    args = parser.parse_args()

    named = []
    for label, path in (("Mono", args.mono), ("Unity 7 CoreCLR", args.coreclr),
                        (".NET 10", args.dotnet)):
        if path:
            named.append((label, path))
    for entry in args.run:
        if "=" not in entry:
            parser.error(f"--run needs LABEL=PATH, got {entry!r}")
        label, path = entry.split("=", 1)
        named.append((label, path))

    if not named:
        parser.error("name at least one run with --mono, --coreclr, --dotnet or --run")

    runs = [(label, load_run(path)) for label, path in named]
    reference = runs[0][1]["routes"]
    route_order = list(reference.keys())

    headers = ["Route", "Phase"] + [label for label, _ in runs]
    headers += [f"{label} vs {runs[0][0]}" for label, _ in runs[1:]]
    print("| " + " | ".join(headers) + " |")
    print("|" + "|".join(["---"] * len(headers)) + "|")

    for route_id in route_order:
        name = reference[route_id]["name"]
        for phase_id, phase_name in PHASES:
            values = []
            for _, run in runs:
                value = run["routes"].get(route_id, {}).get("phases", {}).get(phase_id)
                values.append(value)
            if all(value is None for value in values):
                continue
            cells = [name, phase_name]
            for value in values:
                cells.append("—" if value is None else f"{value:,.0f}")
            base = values[0]
            for value in values[1:]:
                cells.append("—" if value is None or base is None else percent(base, value))
            print("| " + " | ".join(cells) + " |")

    print()
    print("| Route | " + " | ".join(label for label, _ in runs) + " |")
    print("|" + "|".join(["---"] * (len(runs) + 1)) + "|")
    for route_id in route_order:
        cells = [reference[route_id]["name"]]
        for _, run in runs:
            days = run["routes"].get(route_id, {}).get("floorDays")
            cells.append("—" if days in (None, 0.0) else f"{days:.5f} d")
        print("| " + " | ".join(cells) + " |")

    return 0


if __name__ == "__main__":
    sys.exit(main())
