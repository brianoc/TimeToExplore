#!/usr/bin/env python3
"""Tests for tools/fetch_planet_textures.py. No test touches the network.

Run with: python3 -m unittest discover -s tools -p 'test_*.py'
"""

import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fetch_planet_textures import (  # noqa: E402
    HEIGHT,
    MANIFEST,
    MIN_BYTES,
    WIDTH,
    check_files,
    jpeg_dimensions,
)

EXPECTED = {
    "mercury": "2k_mercury.jpg",
    "venus": "2k_venus_atmosphere.jpg",
    "earth": "2k_earth_daymap.jpg",
    "mars": "2k_mars.jpg",
    "jupiter": "2k_jupiter.jpg",
    "saturn": "2k_saturn.jpg",
    "uranus": "2k_uranus.jpg",
    "neptune": "2k_neptune.jpg",
    "moon": "2k_moon.jpg",
}


def fake_jpeg(width=WIDTH, height=HEIGHT, size=MIN_BYTES):
    """A JPEG header with a SOF0 frame, padded to `size` bytes."""
    frame = (b"\xff\xc0" + (17).to_bytes(2, "big") + b"\x08"
             + height.to_bytes(2, "big") + width.to_bytes(2, "big")
             + b"\x03" + bytes(9))
    body = b"\xff\xd8" + frame
    return body + bytes(max(0, size - len(body)))


class ManifestTests(unittest.TestCase):
    def test_the_nine_expected_maps_are_listed(self):
        self.assertEqual({entry["id"]: entry["source"] for entry in MANIFEST},
                         EXPECTED)

    def test_targets_are_unique_and_named_for_the_catalogue(self):
        targets = [entry["target"] for entry in MANIFEST]
        self.assertEqual(len(targets), len(set(targets)))
        for entry in MANIFEST:
            self.assertEqual(entry["target"], f"{entry['id']}_albedo.jpg")


class CheckTests(unittest.TestCase):
    def test_an_empty_directory_reports_every_missing_map(self):
        with tempfile.TemporaryDirectory() as directory:
            errors = check_files(Path(directory))

        self.assertEqual(len(errors), len(MANIFEST))
        for entry in MANIFEST:
            self.assertTrue(any(entry["target"] in error and "missing" in error
                                for error in errors))

    def test_a_complete_directory_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            for entry in MANIFEST:
                (Path(directory) / entry["target"]).write_bytes(fake_jpeg())
            self.assertEqual(check_files(Path(directory)), [])

    def test_a_small_non_jpeg_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            for entry in MANIFEST:
                (Path(directory) / entry["target"]).write_bytes(b"not a jpeg")
            errors = check_files(Path(directory))

        self.assertTrue(any("not a JPEG" in error for error in errors))
        self.assertTrue(any("expected at least" in error for error in errors))

    def test_the_wrong_dimensions_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            for entry in MANIFEST:
                (Path(directory) / entry["target"]).write_bytes(
                    fake_jpeg(width=1024, height=512))
            errors = check_files(Path(directory))

        self.assertTrue(any("1024x512" in error for error in errors))

    def test_the_jpeg_header_parser_reads_the_frame(self):
        self.assertEqual(jpeg_dimensions(fake_jpeg()), (WIDTH, HEIGHT))
        self.assertEqual(jpeg_dimensions(fake_jpeg(width=10, height=20)), (10, 20))
        self.assertIsNone(jpeg_dimensions(b"not a jpeg"))
        self.assertIsNone(jpeg_dimensions(b"\xff\xd8\xff\xda"))


if __name__ == "__main__":
    unittest.main()
