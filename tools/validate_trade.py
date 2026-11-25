#!/usr/bin/env python3
"""Validate the hand-edited trade catalogue without opening Unity.

This is the fast gate for the economy data. It checks the documented schema in
Docs/technical/data-schema.md: ids resolve, prices and sizes are sane, and every
market names a body in the orbital catalogue. The Unity EditMode suite
(`TradeCatalogueTests`) is the slower, authoritative counterpart that also
exercises JsonUtility and the runtime loader.

Usage:
    python3 tools/validate_trade.py
    python3 tools/validate_trade.py --path path/to/trade_catalogue.json
    python3 tools/validate_trade.py --system path/to/solar_system.json
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_PATH = ROOT / "TimeToExplore" / "Assets" / "Resources" / "trade_catalogue.json"
DEFAULT_SYSTEM_PATH = ROOT / "TimeToExplore" / "Assets" / "Resources" / "solar_system.json"


def finite(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def integer(value) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def check(data, body_ids=None) -> list[str]:
    """Return every schema error in *data*; an empty list means it is valid.

    *body_ids* is the set of body ids from the orbital catalogue. When it is
    None the cross-file resolution rule is skipped, which is useful to a caller
    that only wants the trade file's own schema.
    """
    errors: list[str] = []

    if not isinstance(data, dict):
        return ["top level: not an object"]

    commodities = data.get("commodities")
    markets = data.get("markets")
    if not isinstance(commodities, list) or not commodities:
        errors.append("top level: 'commodities' must be a non-empty array")
    if not isinstance(markets, list) or not markets:
        errors.append("top level: 'markets' must be a non-empty array")
    if errors:
        return errors

    # --- Commodities -------------------------------------------------------
    commodity_ids: set[str] = set()
    for index, commodity in enumerate(commodities):
        where = f"commodity[{index}]"
        if not isinstance(commodity, dict):
            errors.append(f"{where}: not an object")
            continue

        commodity_id = commodity.get("id")
        if not isinstance(commodity_id, str) or not commodity_id.strip():
            errors.append(f"{where}: missing id")
            continue
        where = commodity_id
        if commodity_id in commodity_ids:
            errors.append(f"{where}: duplicate commodity id")
            continue
        commodity_ids.add(commodity_id)

        for key in ("volumePerUnitM3", "massPerUnitKg"):
            value = commodity.get(key)
            if not finite(value) or value <= 0:
                errors.append(f"{where}: {key} must be a positive number")

    # --- Markets -----------------------------------------------------------
    market_ids: set[str] = set()
    offered: set[str] = set()
    offers = 0
    for index, market in enumerate(markets):
        where = f"market[{index}]"
        if not isinstance(market, dict):
            errors.append(f"{where}: not an object")
            continue

        station_id = market.get("stationId")
        if not isinstance(station_id, str) or not station_id.strip():
            errors.append(f"{where}: missing stationId")
            continue
        where = station_id
        if station_id in market_ids:
            errors.append(f"{where}: duplicate market stationId")
            continue
        market_ids.add(station_id)

        if body_ids is not None and station_id not in body_ids:
            errors.append(f"{where}: stationId does not resolve to a body in solar_system.json")

        market_offers = market.get("offers")
        if not isinstance(market_offers, list):
            errors.append(f"{where}: offers must be an array")
            continue

        seen: set[str] = set()
        for offer_index, offer in enumerate(market_offers):
            offer_where = f"{where}: offer[{offer_index}]"
            if not isinstance(offer, dict):
                errors.append(f"{offer_where}: not an object")
                continue

            commodity_id = offer.get("commodityId")
            if not isinstance(commodity_id, str) or not commodity_id.strip():
                errors.append(f"{offer_where}: missing commodityId")
                continue
            offer_where = f"{where}: offer '{commodity_id}'"

            if commodity_id not in commodity_ids:
                errors.append(f"{offer_where}: unknown commodityId")
                continue
            if commodity_id in seen:
                errors.append(f"{offer_where}: duplicate commodity in this market")
            seen.add(commodity_id)
            offered.add(commodity_id)
            offers += 1

            buy = offer.get("buyPrice")
            sell = offer.get("sellPrice")
            if not integer(buy) or buy < 0:
                errors.append(f"{offer_where}: buyPrice must be an integer >= 0")
            if not integer(sell) or sell < 0:
                errors.append(f"{offer_where}: sellPrice must be an integer >= 0")
            if integer(buy) and integer(sell) and sell > buy:
                errors.append(f"{offer_where}: sellPrice {sell} exceeds buyPrice {buy}")

    # --- Coverage ----------------------------------------------------------
    for commodity_id in sorted(commodity_ids - offered):
        errors.append(f"{commodity_id}: no market offers this commodity")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", type=Path, default=DEFAULT_PATH,
                        help="trade JSON (default: %(default)s)")
    parser.add_argument("--system", type=Path, default=DEFAULT_SYSTEM_PATH,
                        help="orbital catalogue (default: %(default)s)")
    parser.add_argument("--quiet", action="store_true", help="only print failures")
    args = parser.parse_args()

    try:
        data = json.loads(args.path.read_text(encoding="utf-8"))
    except OSError as exc:
        print(f"error: cannot read {args.path}: {exc}", file=sys.stderr)
        return 2
    except json.JSONDecodeError as exc:
        print(f"error: {args.path} is not valid JSON: {exc}", file=sys.stderr)
        return 2

    try:
        system = json.loads(args.system.read_text(encoding="utf-8"))
    except OSError as exc:
        print(f"error: cannot read {args.system}: {exc}", file=sys.stderr)
        return 2
    except json.JSONDecodeError as exc:
        print(f"error: {args.system} is not valid JSON: {exc}", file=sys.stderr)
        return 2

    bodies = system.get("bodies") if isinstance(system, dict) else None
    body_ids = {
        body["id"]
        for body in bodies or []
        if isinstance(body, dict) and isinstance(body.get("id"), str)
    }

    errors = check(data, body_ids)
    if errors:
        for error in errors:
            print(f"FAIL {error}")
        return 1

    commodity_count = len(data["commodities"])
    market_count = len(data["markets"])
    offer_count = sum(len(market.get("offers") or []) for market in data["markets"])
    if not args.quiet:
        print(f"Trade catalogue OK: {commodity_count} commodities, {market_count} markets, "
              f"{offer_count} offers")
    return 0


if __name__ == "__main__":
    sys.exit(main())
