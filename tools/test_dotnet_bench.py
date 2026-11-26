"""Guards the plain .NET benchmark harness against route drift.

The harness in tools/dotnet-bench duplicates the six routes the Editor
benchmark defines, because it compiles without UnityEditor. A duplicated list
is only honest while it matches, so the tooling suite fails when it does not.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EDITOR = ROOT / "TimeToExplore/Assets/Editor/PlanningBenchmark.cs"
HARNESS = ROOT / "tools/dotnet-bench/Program.cs"


def route_ids(path: Path, constructor: str) -> list[str]:
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(rf'new {constructor}\(\s*"([^"]+)"')
    return pattern.findall(text)


class DotnetBenchRouteTests(unittest.TestCase):
    def test_routes_match_the_editor_benchmark(self) -> None:
        editor = route_ids(EDITOR, "BenchmarkRoute")
        harness = route_ids(HARNESS, "Route")
        self.assertTrue(editor, "no routes found in the Editor benchmark")
        self.assertEqual(editor, harness)

    def test_every_route_has_a_target(self) -> None:
        for route in route_ids(HARNESS, "Route"):
            self.assertTrue(route)


if __name__ == "__main__":
    unittest.main()
