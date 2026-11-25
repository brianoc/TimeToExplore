#!/usr/bin/env python3
"""Tests for tools/validate_trade.py.

Run with: python3 -m unittest discover -s tools -p 'test_*.py'
"""

import copy
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from validate_trade import check  # noqa: E402

BODY_IDS = {"gateway", "earth"}


def valid_data():
    return {
        "commodities": [
            {"id": "water", "name": "Water", "volumePerUnitM3": 1.0,
             "massPerUnitKg": 1000.0, "description": "Potable water and coolant."},
            {"id": "food", "name": "Food", "volumePerUnitM3": 1.2,
             "massPerUnitKg": 400.0, "description": "Rations and hydroponics supplement."},
        ],
        "markets": [
            {"stationId": "gateway", "offers": [
                {"commodityId": "water", "buyPrice": 8, "sellPrice": 6},
                {"commodityId": "food", "buyPrice": 12, "sellPrice": 9},
            ]},
            {"stationId": "earth", "offers": [
                {"commodityId": "water", "buyPrice": 4, "sellPrice": 3},
            ]},
        ],
    }


class TradeValidationTests(unittest.TestCase):
    def setUp(self):
        self.data = valid_data()

    def assert_error(self, message):
        errors = check(self.data, BODY_IDS)
        self.assertTrue(any(message in error for error in errors),
                        f"expected an error containing {message!r}, got {errors}")

    def test_valid_file_passes(self):
        self.assertEqual(check(self.data, BODY_IDS), [])

    def test_without_body_ids_the_cross_file_rule_is_skipped(self):
        self.data["markets"][0]["stationId"] = "not_a_body"
        self.assertEqual(check(self.data), [])

    def test_duplicate_commodity_is_rejected(self):
        self.data["commodities"].append(copy.deepcopy(self.data["commodities"][0]))
        self.assert_error("duplicate commodity id")

    def test_duplicate_market_is_rejected(self):
        self.data["markets"].append(copy.deepcopy(self.data["markets"][0]))
        self.assert_error("duplicate market stationId")

    def test_unknown_commodity_is_rejected(self):
        self.data["markets"][0]["offers"].append(
            {"commodityId": "unobtainium", "buyPrice": 1, "sellPrice": 1})
        self.assert_error("unknown commodityId")

    def test_unknown_station_is_rejected(self):
        self.data["markets"][1]["stationId"] = "pluto"
        self.assert_error("does not resolve to a body")

    def test_duplicate_offer_in_one_market_is_rejected(self):
        self.data["markets"][0]["offers"].append(
            {"commodityId": "water", "buyPrice": 8, "sellPrice": 6})
        self.assert_error("duplicate commodity in this market")

    def test_zero_volume_is_rejected(self):
        self.data["commodities"][0]["volumePerUnitM3"] = 0.0
        self.assert_error("volumePerUnitM3 must be a positive number")

    def test_negative_mass_is_rejected(self):
        self.data["commodities"][0]["massPerUnitKg"] = -1.0
        self.assert_error("massPerUnitKg must be a positive number")

    def test_negative_buy_price_is_rejected(self):
        self.data["markets"][0]["offers"][0]["buyPrice"] = -1
        self.assert_error("buyPrice must be an integer >= 0")

    def test_negative_sell_price_is_rejected(self):
        self.data["markets"][0]["offers"][0]["sellPrice"] = -1
        self.assert_error("sellPrice must be an integer >= 0")

    def test_fractional_price_is_rejected(self):
        self.data["markets"][0]["offers"][0]["buyPrice"] = 8.5
        self.assert_error("buyPrice must be an integer >= 0")

    def test_sell_above_buy_is_rejected(self):
        self.data["markets"][0]["offers"][0]["sellPrice"] = 9
        self.assert_error("exceeds buyPrice")

    def test_unoffered_commodity_is_rejected(self):
        self.data["commodities"].append(
            {"id": "machinery", "name": "Machinery", "volumePerUnitM3": 1.5,
             "massPerUnitKg": 1200.0, "description": "Spares, pumps and printed parts."})
        self.assert_error("no market offers this commodity")

    def test_empty_commodities_is_rejected(self):
        self.data["commodities"] = []
        self.assert_error("'commodities' must be a non-empty array")


if __name__ == "__main__":
    unittest.main()
