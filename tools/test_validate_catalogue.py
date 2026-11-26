#!/usr/bin/env python3
"""Tests for the appearance rules in tools/validate_catalogue.py.

Run with: python3 -m unittest discover -s tools -p 'test_*.py'
"""

import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from validate_catalogue import check_appearance  # noqa: E402


def planet(**overrides):
    body = {
        "id": "earth",
        "kind": "planet",
        "radiusM": 6_371_000.0,
        "albedoTexture": "Planets/earth_albedo",
        "atmosphere": {
            "heightM": 100_000.0,
            "color": [0.35, 0.56, 1.0],
            "intensity": 1.1,
        },
    }
    body.update(overrides)
    return body


class AppearanceTests(unittest.TestCase):
    def assert_error(self, body, message, resources_dir=None):
        errors = check_appearance(body, resources_dir)
        self.assertTrue(any(message in error for error in errors),
                        f"expected an error containing {message!r}, got {errors}")

    def test_a_complete_planet_passes(self):
        self.assertEqual(check_appearance(planet()), [])

    def test_a_body_with_no_appearance_passes(self):
        sun = {"id": "sun", "kind": "star", "radiusM": 696_340_000.0,
               "albedoTexture": None, "atmosphere": None}
        asteroid = {"id": "ast_1", "kind": "asteroid", "radiusM": 500_000.0,
                    "albedoTexture": None, "atmosphere": None}

        self.assertEqual(check_appearance(sun), [])
        self.assertEqual(check_appearance(asteroid), [])

    def test_a_planet_needs_a_texture(self):
        self.assert_error(planet(albedoTexture=None), "needs an albedoTexture")

    def test_the_moon_needs_a_texture(self):
        moon = {"id": "moon", "kind": "moon", "radiusM": 1_737_400.0,
                "albedoTexture": None, "atmosphere": None}
        self.assert_error(moon, "needs an albedoTexture")

    def test_another_moon_may_go_without_a_texture(self):
        moon = {"id": "phobos", "kind": "moon", "radiusM": 11_267.0,
                "albedoTexture": None, "atmosphere": None}
        self.assertEqual(check_appearance(moon), [])

    def test_an_asteroid_must_not_have_a_texture(self):
        asteroid = {"id": "ast_1", "kind": "asteroid", "radiusM": 500_000.0,
                    "albedoTexture": "Planets/ast_1_albedo", "atmosphere": None}
        self.assert_error(asteroid, "must not have an albedoTexture")

    def test_a_station_must_not_have_a_texture(self):
        station = {"id": "gateway", "kind": "station", "radiusM": 1000.0,
                   "albedoTexture": "Planets/gateway_albedo", "atmosphere": None}
        self.assert_error(station, "must not have an albedoTexture")

    def test_a_texture_key_must_name_the_planets_folder(self):
        for key in ("earth_albedo", "Planets/Earth", "Planets/earth albedo",
                    "Sky/earth_albedo", 7):
            with self.subTest(key=key):
                self.assert_error(planet(albedoTexture=key),
                                  "albedoTexture must be null or")

    def test_a_texture_file_must_exist(self):
        with tempfile.TemporaryDirectory() as directory:
            body = planet()
            self.assert_error(body, "has no .jpg or .png", Path(directory))

            (Path(directory) / "Planets").mkdir()
            (Path(directory) / "Planets" / "earth_albedo.jpg").write_bytes(b"")
            self.assertEqual(check_appearance(body, Path(directory)), [])

    def test_a_png_satisfies_the_texture_file_rule(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "Planets").mkdir()
            (Path(directory) / "Planets" / "earth_albedo.png").write_bytes(b"")
            self.assertEqual(check_appearance(planet(), Path(directory)), [])

    def test_atmosphere_must_be_an_object(self):
        self.assert_error(planet(atmosphere="thick"), "atmosphere must be null or an object")

    def test_a_missing_or_nonpositive_height_is_rejected(self):
        for height in (None, 0.0, -1.0):
            with self.subTest(height=height):
                self.assert_error(planet(atmosphere={"heightM": height,
                                                     "color": [0, 0, 0],
                                                     "intensity": 1.0}),
                                  "heightM must be positive")

    def test_a_height_at_six_percent_of_radius_is_rejected(self):
        atmosphere = {"heightM": 0.06 * 6_371_000.0,
                      "color": [0, 0, 0], "intensity": 1.0}
        self.assert_error(planet(atmosphere=atmosphere), "below 6% of radiusM")

    def test_colour_must_be_three_numbers_in_range(self):
        for color in ([1.0, 0.5], [1.0, 0.5, 0.2, 0.1], [1.2, 0.5, 0.2],
                      [0.5, -0.1, 0.2], "blue", [0.5, None, 0.2]):
            with self.subTest(color=color):
                self.assert_error(planet(atmosphere={"heightM": 1000.0,
                                                     "color": color,
                                                     "intensity": 1.0}),
                                  "color must be three numbers in [0, 1]")

    def test_intensity_must_be_inside_zero_and_two(self):
        for intensity in (0.0, -0.5, 2.1, None):
            with self.subTest(intensity=intensity):
                self.assert_error(planet(atmosphere={"heightM": 1000.0,
                                                     "color": [0, 0, 0],
                                                     "intensity": intensity}),
                                  "intensity must be in (0, 2]")

    def test_the_upper_intensity_bound_is_accepted(self):
        atmosphere = {"heightM": 1000.0, "color": [0, 0, 0], "intensity": 2.0}
        self.assertEqual(check_appearance(planet(atmosphere=atmosphere)), [])

    def test_null_atmosphere_is_accepted(self):
        self.assertEqual(check_appearance(planet(atmosphere=None)), [])


if __name__ == "__main__":
    unittest.main()
