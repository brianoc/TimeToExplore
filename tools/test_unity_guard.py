#!/usr/bin/env python3
"""Tests for tools/unity_guard.py.

Run with: python3 -m unittest discover -s tools -p 'test_*.py'
"""

import os
import subprocess
import sys
import tempfile
import unittest

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "unity_guard.py")
PYTHON = sys.executable


class GuardTests(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.log = os.path.join(self._dir.name, "unity.log")

    def tearDown(self):
        self._dir.cleanup()

    def guard(self, body, *args, timeout=20):
        """Run *body* as the guarded command and return the completed process."""
        command = [PYTHON, GUARD, "--log", self.log, "--poll-seconds", "0.05", *args,
                   "--", PYTHON, "-c", body]
        return subprocess.run(command, capture_output=True, text=True, timeout=timeout)

    def test_exit_code_is_passed_through(self):
        result = self.guard("import sys; sys.exit(7)", "--stall-seconds", "10",
                            "--timeout-seconds", "10")
        self.assertEqual(result.returncode, 7)

    def test_progress_keeps_the_run_alive(self):
        body = (
            "import time\n"
            f"log = open({self.log!r}, 'a')\n"
            "for _ in range(8):\n"
            "    log.write('still working\\n'); log.flush(); time.sleep(0.1)\n"
        )
        result = self.guard(body, "--stall-seconds", "1", "--timeout-seconds", "10")
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_a_silent_run_is_stopped(self):
        result = self.guard("import time; time.sleep(30)", "--stall-seconds", "0.6",
                            "--timeout-seconds", "10")
        self.assertEqual(result.returncode, 124)
        self.assertIn("wrote nothing to the log", result.stderr)
        self.assertIn(self.log, result.stderr)

    def test_the_time_budget_is_a_hard_stop(self):
        result = self.guard("import time; time.sleep(30)", "--stall-seconds", "10",
                            "--timeout-seconds", "0.6")
        self.assertEqual(result.returncode, 124)
        self.assertIn("time budget", result.stderr)

    def test_a_cloud_retry_loop_is_stopped(self):
        body = (
            "import time\n"
            f"log = open({self.log!r}, 'w')\n"
            "for _ in range(40):\n"
            "    log.write('Curl error 6: Could not resolve: cdp.cloud.unity3d.com:443\\n')\n"
            "log.flush(); time.sleep(30)\n"
        )
        result = self.guard(body, "--stall-seconds", "10", "--timeout-seconds", "30")
        self.assertEqual(result.returncode, 124)
        self.assertIn("cloud lookup", result.stderr)

    def test_a_shutdown_that_does_not_finish_is_stopped(self):
        body = (
            "import time\n"
            f"log = open({self.log!r}, 'a')\n"
            "log.write('Application is shutting down...\\n'); log.flush()\n"
            "time.sleep(30)\n"
        )
        result = self.guard(body, "--shutdown-grace", "0.5", "--stall-seconds", "10",
                            "--timeout-seconds", "30")
        self.assertEqual(result.returncode, 124)
        self.assertIn("shutdown did not finish", result.stderr)


if __name__ == "__main__":
    unittest.main()
