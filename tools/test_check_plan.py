#!/usr/bin/env python3
"""Tests for tools/check_plan.py.

Run with: python3 -m unittest discover -s tools -p 'test_*.py'
"""

import os
import subprocess
import sys
import tempfile
import unittest

import check_plan

HERE = os.path.dirname(os.path.abspath(__file__))
PLAN = os.path.join(HERE, "check_plan.py")
ROOT = os.path.dirname(HERE)


class PlanFilesTests(unittest.TestCase):
    def test_docs_and_tools_need_no_unity_run(self):
        files = ["Docs/log.md", "tools/check.sh", "AGENTS.md", "README.md"]
        self.assertEqual(check_plan.plan_files(files, ROOT), set())

    def test_presentation_script_selects_presentation(self):
        files = ["TimeToExplore/Assets/Scripts/Presentation/CameraRig.cs"]
        self.assertEqual(check_plan.plan_files(files, ROOT), {"presentation"})

    def test_core_script_selects_core_and_flight(self):
        files = ["TimeToExplore/Assets/Scripts/Core/Vector3d.cs"]
        self.assertEqual(
            check_plan.plan_files(files, ROOT), {"core", "flight"}
        )

    def test_union_across_areas(self):
        files = [
            "TimeToExplore/Assets/Scripts/Core/Vector3d.cs",
            "TimeToExplore/Assets/Scripts/Presentation/CameraRig.cs",
        ]
        self.assertEqual(
            check_plan.plan_files(files, ROOT), {"core", "flight", "presentation"}
        )

    def test_asmdef_is_a_core_change(self):
        files = ["TimeToExplore/Assets/Scripts/Core/TimeToExplore.Core.asmdef"]
        self.assertEqual(check_plan.plan_files(files, ROOT), {"core"})

    def test_unity_rewritten_asset_is_not_a_source_change(self):
        files = ["TimeToExplore/Assets/UniversalRenderPipelineGlobalSettings.asset"]
        self.assertEqual(check_plan.plan_files(files, ROOT), set())

    def test_assets_fallback_is_presentation_and_smoke(self):
        files = ["TimeToExplore/Assets/Materials/Body.mat"]
        self.assertEqual(
            check_plan.plan_files(files, ROOT), {"presentation", "smoke"}
        )

    def test_unknown_path_is_refused(self):
        with self.assertRaises(check_plan.PlanError):
            check_plan.plan_files(["Builds/somewhere.bin"], ROOT)

    def test_order_is_canonical(self):
        names = check_plan.order({"smoke", "playmode", "core", "flight"})
        self.assertEqual(names, ["core", "flight", "smoke", "playmode"])


class TestFileTests(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.root = self._dir.name

    def tearDown(self):
        self._dir.cleanup()

    def write(self, relative, body):
        path = os.path.join(self.root, relative)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(body)
        return relative

    def test_editmode_fixture_reads_its_category(self):
        name = self.write(
            "TimeToExplore/Assets/Tests/EditMode/RouteTests.cs",
            '[TestFixture]\n[Category("deep")]\npublic class RouteTests {}\n',
        )
        self.assertEqual(check_plan.plan_files([name], self.root), {"deep"})

    def test_editmode_fixture_needs_a_category(self):
        name = self.write(
            "TimeToExplore/Assets/Tests/EditMode/RouteTests.cs",
            "public class RouteTests {}\n",
        )
        with self.assertRaises(check_plan.PlanError):
            check_plan.plan_files([name], self.root)

    def test_playmode_probe_runs_the_playmode_suite(self):
        name = self.write(
            "TimeToExplore/Assets/Tests/PlayMode/FrameOrderProbe.cs",
            "public class FrameOrderProbe {}\n",
        )
        self.assertEqual(check_plan.plan_files([name], self.root), {"playmode"})

    def test_playmode_fixture_keeps_its_own_category(self):
        name = self.write(
            "TimeToExplore/Assets/Tests/PlayMode/CaptureTests.cs",
            '[Category("playmode")]\npublic class CaptureTests {}\n',
        )
        self.assertEqual(check_plan.plan_files([name], self.root), {"playmode"})


class CommandTests(unittest.TestCase):
    def run_plan(self, *args, cwd=ROOT):
        return subprocess.run(
            [sys.executable, PLAN, *args],
            capture_output=True,
            text=True,
            cwd=cwd,
        )

    def test_files_dry_run_prints_the_categories(self):
        result = self.run_plan(
            "--files", "TimeToExplore/Assets/Scripts/Presentation/CameraRig.cs"
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "presentation")

    def test_files_dry_run_reports_fast_only(self):
        result = self.run_plan("--files", "Docs/log.md")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "")

    def test_unknown_path_fails_with_a_useful_message(self):
        result = self.run_plan("--files", "Builds/somewhere.bin")
        self.assertEqual(result.returncode, 1)
        self.assertIn("no category rule", result.stderr)


if __name__ == "__main__":
    unittest.main()
