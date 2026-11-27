"""Guards the plain .NET benchmark harness against route drift.

The harness in tools/dotnet-bench duplicates the six routes the Editor
benchmark defines, because it compiles without UnityEditor. A duplicated list
is only honest while it matches, so the tooling suite fails when it does not.
"""

from __future__ import annotations

import re
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ROUTES = ROOT / "TimeToExplore/Assets/Scripts/Simulation/BenchmarkRoutes.cs"
HARNESS = ROOT / "tools/dotnet-bench/Program.cs"
SCRIPT = ROOT / "tools/dotnet-bench.sh"


def route_ids(path: Path, constructor: str) -> list[str]:
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(rf'new {constructor}\(\s*"([^"]+)"')
    return pattern.findall(text)


class DotnetBenchRouteTests(unittest.TestCase):
    def test_routes_match_the_editor_benchmark(self) -> None:
        editor = route_ids(ROUTES, "BenchmarkRoute")
        harness = route_ids(HARNESS, "Route")
        self.assertTrue(editor, "no routes found in the shared benchmark routes")
        self.assertEqual(editor, harness)

    def test_every_route_has_a_target(self) -> None:
        for route in route_ids(HARNESS, "Route"):
            self.assertTrue(route)

    def test_the_harness_refuses_an_unknown_configuration(self) -> None:
        result = subprocess.run(
            ["bash", str(SCRIPT), "--configuration", "Nonsense"],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 2)
        self.assertIn("Debug or Release", result.stderr)


if __name__ == "__main__":
    unittest.main()
