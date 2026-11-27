"""Guards the plain .NET benchmark harness against route drift.

The harness in tools/dotnet-bench duplicates the six routes the Editor
benchmark defines, because it compiles without UnityEditor. A duplicated list
is only honest while it matches, so the tooling suite fails when it does not.
The plot matrix has the same problem with the catalogue's eight planets.
"""

from __future__ import annotations

import json
import re
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ROUTES = ROOT / "TimeToExplore/Assets/Scripts/Simulation/BenchmarkRoutes.cs"
HARNESS = ROOT / "tools/dotnet-bench/Program.cs"
MATRIX = ROOT / "tools/dotnet-bench/Matrix.cs"
SCRIPT = ROOT / "tools/dotnet-bench.sh"
CATALOGUE = ROOT / "TimeToExplore/Assets/Resources/solar_system.json"

MAJOR_PLANETS = [
    "mercury", "venus", "earth", "mars",
    "jupiter", "saturn", "uranus", "neptune",
]


def matrix_planet_ids() -> list[str]:
    text = MATRIX.read_text(encoding="utf-8")
    block = re.search(r"Planets =\s*\{(.*?)\};", text, re.S)
    if block is None:
        return []
    return re.findall(r'\("([a-z]+)",', block.group(1))


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


class DotnetBenchMatrixTests(unittest.TestCase):
    def test_the_matrix_covers_every_major_planet(self) -> None:
        self.assertEqual(matrix_planet_ids(), MAJOR_PLANETS)

    def test_every_matrix_planet_is_a_catalogue_planet(self) -> None:
        catalogue = json.loads(CATALOGUE.read_text(encoding="utf-8"))
        planets = {
            body["id"] for body in catalogue["bodies"] if body.get("kind") == "planet"
        }
        self.assertEqual(planets, set(MAJOR_PLANETS))

    def test_the_matrix_is_wired_into_the_script(self) -> None:
        script = SCRIPT.read_text(encoding="utf-8")
        self.assertIn("--matrix", script)
        self.assertIn("--markdown", script)
        self.assertIn("--runs", script)


if __name__ == "__main__":
    unittest.main()
