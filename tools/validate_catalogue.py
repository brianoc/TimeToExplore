#!/usr/bin/env python3
"""Validate the generated solar-system catalogue without opening Unity.

This is the fast gate for agents. It checks the documented schema and
invariants in Docs/technical/data-schema.md, then independently propagates the
Keplerian elements at the catalogue epoch and sanity-checks the resulting
distances and speeds. The Unity EditMode suite is the slower, authoritative
counterpart (it also exercises JsonUtility and the runtime model).

Usage:
    python3 tools/validate_catalogue.py
    python3 tools/validate_catalogue.py --path path/to/solar_system.json
"""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
from collections import Counter
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_PATH = ROOT / "TimeToExplore" / "Assets" / "Resources" / "solar_system.json"

AU_M = 149_597_870_700.0
DAY_S = 86_400.0
START_JD = 2_461_359.5
OBLIQUITY = math.radians(23.43928)

KINDS = {"star", "planet", "moon", "asteroid", "station"}
EXPECTED_COUNTS = {"star": 1, "planet": 8, "moon": 22, "asteroid": 100, "station": 6}
POINTS = {"L1", "L2", "L4", "L5"}
PLANES = {"ecliptic", "parentEquator"}
ID_RE = re.compile(r"^[a-z0-9_]+$")

# Independent reference values at the epoch, from JPL mean elements.
EARTH_RADIUS_AU = 0.989277
EARTH_SPEED_KM_S = 30.1059
SIDEREAL_YEAR_DAYS = 365.256363
GATEWAY_L1_KM = 54_841.0


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def norm(a):
    return math.sqrt(dot(a, a))


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def normalize(a):
    n = norm(a)
    return (0.0, 0.0, 0.0) if n <= 1e-30 else scale(a, 1.0 / n)


def wrap_two_pi(angle):
    angle %= 2.0 * math.pi
    return angle + 2.0 * math.pi if angle < 0.0 else angle


def solve_kepler(mean_anomaly, eccentricity):
    m = wrap_two_pi(mean_anomaly)
    e = min(max(eccentricity, 0.0), 0.999999)
    e_anomaly = m if e < 0.8 else math.pi
    for _ in range(40):
        f = e_anomaly - e * math.sin(e_anomaly) - m
        fp = 1.0 - e * math.cos(e_anomaly)
        step = f / fp
        e_anomaly -= step
        if abs(step) < 1e-13:
            return e_anomaly

    lo, hi = 0.0, 2.0 * math.pi
    for _ in range(100):
        mid = 0.5 * (lo + hi)
        if mid - e * math.sin(mid) - m > 0.0:
            hi = mid
        else:
            lo = mid
    return 0.5 * (lo + hi)


def pole_from_ra_dec(ra_deg, dec_deg):
    ra, dec = math.radians(ra_deg), math.radians(dec_deg)
    x_eq = math.cos(dec) * math.cos(ra)
    y_eq = math.cos(dec) * math.sin(ra)
    z_eq = math.sin(dec)
    c, s = math.cos(OBLIQUITY), math.sin(OBLIQUITY)
    return (x_eq, c * y_eq + s * z_eq, -s * y_eq + c * z_eq)


def equator_basis(body):
    pole = pole_from_ra_dec(body["poleRaDeg"], body["poleDecDeg"])
    reference = (0.0, 0.0, 1.0) if abs(pole[2]) < 0.9 else (1.0, 0.0, 0.0)
    ex = normalize(cross(reference, pole))
    ey = normalize(cross(pole, ex))
    return ex, ey, pole


def from_equator(body, v):
    ex, ey, pole = equator_basis(body)
    return (ex[0] * v[0] + ey[0] * v[1] + pole[0] * v[2],
            ex[1] * v[0] + ey[1] * v[1] + pole[1] * v[2],
            ex[2] * v[0] + ey[2] * v[1] + pole[2] * v[2])


def rotate_elements(orbit, x, y):
    cw, sw = math.cos(math.radians(orbit["argPeriDeg"])), math.sin(math.radians(orbit["argPeriDeg"]))
    x1, y1 = cw * x - sw * y, sw * x + cw * y

    ci, si = math.cos(math.radians(orbit["iDeg"])), math.sin(math.radians(orbit["iDeg"]))
    y2, z2 = ci * y1, si * y1

    c_o, s_o = math.cos(math.radians(orbit["raanDeg"])), math.sin(math.radians(orbit["raanDeg"]))
    return (c_o * x1 - s_o * y2, s_o * x1 + c_o * y2, z2)


def local_state(orbit, parent_gm, jd):
    a, e = orbit["aM"], orbit["e"]
    dt = (jd - orbit["epochJulianDate"]) * DAY_S
    n = math.sqrt(parent_gm / (a * a * a))
    mean_anomaly = math.radians(orbit["meanAnomalyDeg"]) + n * dt
    e_anomaly = solve_kepler(mean_anomaly, e)

    cos_e, sin_e = math.cos(e_anomaly), math.sin(e_anomaly)
    radius = a * (1.0 - e * cos_e)
    x = a * (cos_e - e)
    y = a * math.sqrt(1.0 - e * e) * sin_e
    speed_scale = math.sqrt(parent_gm * a) / radius
    vx = -speed_scale * sin_e
    vy = speed_scale * math.sqrt(1.0 - e * e) * cos_e

    return rotate_elements(orbit, x, y), rotate_elements(orbit, vx, vy)


def solve_gamma(mu, l1):
    lo, hi = 1e-12, 0.5
    for _ in range(90):
        g = 0.5 * (lo + hi)
        g2, g3, g4, g5 = g * g, g * g * g, g ** 4, g ** 5
        if l1:
            f = g5 - (3.0 - mu) * g4 + (3.0 - 2.0 * mu) * g3 - mu * g2 + 2.0 * mu * g - mu
        else:
            f = g5 + (3.0 - mu) * g4 + (3.0 - 2.0 * mu) * g3 - mu * g2 - 2.0 * mu * g - mu
        if f > 0.0:
            hi = g
        else:
            lo = g
    return 0.5 * (lo + hi)


def lagrange_point(primary_pos, primary_gm, secondary_pos, secondary_gm,
                   secondary_velocity, point):
    rel = sub(secondary_pos, primary_pos)
    distance = norm(rel)
    if distance < 1e-6:
        return secondary_pos

    direction = scale(rel, 1.0 / distance)
    mu = secondary_gm / (primary_gm + secondary_gm)

    if point == "L1":
        return sub(secondary_pos, scale(direction, solve_gamma(mu, True) * distance))
    if point == "L2":
        return add(secondary_pos, scale(direction, solve_gamma(mu, False) * distance))

    angular_momentum = cross(rel, secondary_velocity)
    normal = normalize(angular_momentum) if dot(angular_momentum, angular_momentum) > 1e-12 else (0.0, 0.0, 1.0)
    side = normalize(cross(normal, direction))
    sign = 1.0 if point == "L4" else -1.0
    offset = add(scale(direction, 0.5), scale(side, math.sqrt(3.0) * 0.5 * sign))
    return add(primary_pos, scale(offset, distance))


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", type=Path, default=DEFAULT_PATH,
                        help="catalogue JSON (default: %(default)s)")
    parser.add_argument("--quiet", action="store_true", help="only print failures")
    args = parser.parse_args()

    errors: list[str] = []

    def fail(message: str) -> None:
        errors.append(message)

    try:
        catalogue = json.loads(args.path.read_text(encoding="utf-8"))
    except OSError as exc:
        print(f"error: cannot read {args.path}: {exc}", file=sys.stderr)
        return 2
    except json.JSONDecodeError as exc:
        print(f"error: {args.path} is not valid JSON: {exc}", file=sys.stderr)
        return 2

    # --- Top level ---------------------------------------------------------
    for key in ("name", "epochUtc", "epochJulianDate", "startJulianDate", "sourceNote", "bodies"):
        if key not in catalogue:
            fail(f"top level: missing '{key}'")
    if errors:
        for error in errors:
            print(f"FAIL {error}")
        return 1

    bodies = catalogue["bodies"]
    if not isinstance(bodies, list) or not bodies:
        print("FAIL top level: 'bodies' must be a non-empty array")
        return 1

    start_jd = catalogue["startJulianDate"]
    epoch_jd = catalogue["epochJulianDate"]
    if not finite(start_jd) or not finite(epoch_jd):
        fail("top level: epochJulianDate and startJulianDate must be finite numbers")
    if abs(start_jd - START_JD) > 1e-9:
        fail(f"top level: startJulianDate {start_jd} is not the documented epoch {START_JD}")
    if abs(epoch_jd - start_jd) > 1e-9:
        fail("top level: startJulianDate must equal epochJulianDate")
    if abs(start_jd - START_JD) < 1e-9:
        expected_utc = (datetime(1899, 12, 30, tzinfo=timezone.utc)
                        + timedelta(days=start_jd - 2415018.5))
        if expected_utc.strftime("%Y-%m-%dT%H:%M:%SZ") != catalogue["epochUtc"]:
            fail(f"top level: epochUtc {catalogue['epochUtc']!r} does not match the Julian date")

    if len(bodies) != sum(EXPECTED_COUNTS.values()):
        fail(f"catalogue: expected {sum(EXPECTED_COUNTS.values())} bodies, found {len(bodies)}")

    # --- Per-body schema ---------------------------------------------------
    by_id: dict[str, dict] = {}
    names: dict[str, str] = {}
    for index, body in enumerate(bodies):
        where = f"body[{index}]"
        if not isinstance(body, dict):
            fail(f"{where}: not an object")
            continue

        body_id = body.get("id")
        if not isinstance(body_id, str) or not ID_RE.match(body_id):
            fail(f"{where}: invalid id {body_id!r}")
            continue
        where = body_id
        if body_id in by_id:
            fail(f"{where}: duplicate id")
            continue
        by_id[body_id] = body

        name = body.get("name")
        if not isinstance(name, str) or not name.strip():
            fail(f"{where}: missing display name")
        elif name in names:
            fail(f"{where}: display name {name!r} already used by {names[name]}")
        else:
            names[name] = body_id

        kind = body.get("kind")
        if kind not in KINDS:
            fail(f"{where}: unknown kind {kind!r}")

        for key in ("radiusM", "gm", "rotationPeriodS", "axialTiltDeg", "poleRaDeg", "poleDecDeg"):
            if not finite(body.get(key)):
                fail(f"{where}: {key} must be a finite number")

        if finite(body.get("radiusM")) and body["radiusM"] <= 0:
            fail(f"{where}: radiusM must be positive")
        if finite(body.get("gm")) and body["gm"] < 0:
            fail(f"{where}: gm cannot be negative")
        if finite(body.get("poleDecDeg")) and not -90 <= body["poleDecDeg"] <= 90:
            fail(f"{where}: poleDecDeg must be in [-90, 90]")
        if finite(body.get("axialTiltDeg")) and abs(body["axialTiltDeg"]) > 180:
            fail(f"{where}: axialTiltDeg must be in [-180, 180]")

        color = body.get("color")
        if not isinstance(color, list) or len(color) != 3 or not all(finite(c) and 0 <= c <= 1 for c in color):
            fail(f"{where}: color must be three numbers in [0, 1]")

        if not isinstance(body.get("description"), str) or not body["description"].strip():
            fail(f"{where}: description must be a non-empty string")

        parent = body.get("parent")
        if parent is not None and (not isinstance(parent, str) or not parent):
            fail(f"{where}: parent must be a body id or null")

        orbit = body.get("orbit")
        lagrange = body.get("lagrange")
        should_orbit = kind in {"planet", "moon", "asteroid"}
        if should_orbit:
            if not isinstance(orbit, dict):
                fail(f"{where}: {kind} needs an orbit object")
            if lagrange is not None:
                fail(f"{where}: non-station must have lagrange: null")
        elif kind == "station":
            if orbit is not None:
                fail(f"{where}: station must have orbit: null")
            if not isinstance(lagrange, dict):
                fail(f"{where}: station needs a lagrange object")
        else:  # star / root
            if orbit is not None:
                fail(f"{where}: star must have orbit: null")
            if lagrange is not None:
                fail(f"{where}: star must have lagrange: null")

        if orbit is not None:
            if not finite(orbit.get("aM")) or orbit["aM"] <= 0:
                fail(f"{where}: orbit.aM must be positive")
            if not finite(orbit.get("e")) or not 0 <= orbit["e"] < 1:
                fail(f"{where}: orbit.e must be in [0, 1)")
            if not finite(orbit.get("epochJulianDate")):
                fail(f"{where}: orbit.epochJulianDate must be finite")
            elif abs(orbit["epochJulianDate"] - start_jd) > 1e-9:
                fail(f"{where}: orbit.epochJulianDate must equal startJulianDate")
            if orbit.get("plane") not in PLANES:
                fail(f"{where}: orbit.plane must be one of {sorted(PLANES)}")
            if not finite(orbit.get("iDeg")) or abs(orbit["iDeg"]) > 180:
                fail(f"{where}: orbit.iDeg must be in [-180, 180]")

        if lagrange is not None and kind == "station":
            if lagrange.get("point") not in POINTS:
                fail(f"{where}: lagrange.point must be one of {sorted(POINTS)}")

    if errors:
        for error in errors:
            print(f"FAIL {error}")
        return 1

    # --- Counts and hierarchy ---------------------------------------------
    for body_id, body in by_id.items():
        if body["kind"] != "station":
            continue
        configuration = body["lagrange"]
        if configuration.get("primary") not in by_id:
            fail(f"{body_id}: lagrange.primary does not resolve")
        if configuration.get("secondary") not in by_id:
            fail(f"{body_id}: lagrange.secondary does not resolve")
        if body.get("parent") != configuration.get("secondary"):
            fail(f"{body_id}: station parent must be its Lagrange secondary")

    counts = Counter(body["kind"] for body in bodies)
    for kind, expected in EXPECTED_COUNTS.items():
        if counts[kind] != expected:
            fail(f"catalogue: expected {expected} {kind}s, found {counts[kind]}")

    roots = [body_id for body_id, body in by_id.items() if not body.get("parent")]
    if roots != ["sun"]:
        fail(f"catalogue: the Sun must be the only root, found {roots}")
    elif by_id["sun"]["kind"] != "star":
        fail("catalogue: the Sun must have kind 'star'")
    for body_id, body in by_id.items():
        parent = body.get("parent")
        if parent and parent not in by_id:
            fail(f"{body_id}: parent {parent!r} does not resolve")
        if parent == body_id:
            fail(f"{body_id}: is its own parent")

        seen = {body_id}
        current = parent
        while current:
            if current in seen:
                fail(f"{body_id}: parent cycle through {current}")
                break
            seen.add(current)
            current = by_id[current].get("parent") if current in by_id else None

    if by_id.get("ast_52", {}).get("name") != "52 Europa":
        fail("catalogue: asteroid 52 Europa should be number-qualified")
    if by_id.get("ast_85", {}).get("name") != "85 Io":
        fail("catalogue: asteroid 85 Io should be number-qualified")
    if by_id.get("ast_106", {}).get("name") != "106 Dione":
        fail("catalogue: asteroid 106 Dione should be number-qualified")

    for body_id, body in by_id.items():
        kind = body["kind"]
        if kind == "station":
            if body["gm"] != 0:
                fail(f"{body_id}: station gm must be zero")
        elif body["gm"] <= 0:
            fail(f"{body_id}: gm must be positive")

    # --- Independent propagation at the epoch ------------------------------
    positions: dict[str, tuple] = {}
    velocities: dict[str, tuple] = {}
    in_progress: set[str] = set()

    def heliocentric(body_id: str) -> None:
        if body_id in positions:
            return
        if body_id in in_progress:
            fail(f"{body_id}: cycle while propagating")
            return
        in_progress.add(body_id)

        body = by_id[body_id]
        parent_id = body.get("parent")
        if not parent_id:
            positions[body_id] = (0.0, 0.0, 0.0)
            velocities[body_id] = (0.0, 0.0, 0.0)
        else:
            heliocentric(parent_id)
            if body["kind"] == "station":
                configuration = body["lagrange"]
                primary = by_id[configuration["primary"]]
                secondary = by_id[configuration["secondary"]]
                heliocentric(primary["id"])
                heliocentric(secondary["id"])
                position = lagrange_point(
                    positions[primary["id"]], primary["gm"],
                    positions[secondary["id"]], secondary["gm"],
                    velocities[secondary["id"]], configuration["point"])
                positions[body_id] = position
                velocities[body_id] = (0.0, 0.0, 0.0)
            else:
                local_position, local_velocity = local_state(body["orbit"], by_id[parent_id]["gm"], start_jd)
                if body["orbit"]["plane"] == "parentEquator":
                    local_position = from_equator(by_id[parent_id], local_position)
                    local_velocity = from_equator(by_id[parent_id], local_velocity)
                positions[body_id] = add(positions[parent_id], local_position)
                velocities[body_id] = add(velocities[parent_id], local_velocity)
        in_progress.remove(body_id)

    for body_id in by_id:
        heliocentric(body_id)

    for body_id, position in positions.items():
        if not all(finite(c) for c in position):
            fail(f"{body_id}: propagated position is not finite")
        if not all(finite(c) for c in velocities[body_id]):
            fail(f"{body_id}: propagated velocity is not finite")

    sun_gm = by_id["sun"]["gm"]
    for body_id, body in by_id.items():
        if body["kind"] != "planet":
            continue
        radius = norm(sub(positions[body_id], positions["sun"]))
        expected = math.sqrt(sun_gm * (2.0 / radius - 1.0 / body["orbit"]["aM"]))
        speed = norm(velocities[body_id])
        if abs(speed - expected) > expected * 0.01:
            fail(f"{body_id}: speed {speed:.1f} m/s disagrees with vis-viva {expected:.1f} m/s")

    for body_id, body in by_id.items():
        if body["kind"] != "moon":
            continue
        parent_id = body["parent"]
        radius = norm(sub(positions[body_id], positions[parent_id]))
        a, e = body["orbit"]["aM"], body["orbit"]["e"]
        if not (a * (1.0 - e) * 0.99 <= radius <= a * (1.0 + e) * 1.01):
            fail(f"{body_id}: radius {radius:.0f} m is outside its eccentricity bounds")

    for body_id, body in by_id.items():
        if body["kind"] != "station":
            continue
        secondary_id = body["lagrange"]["secondary"]
        primary_id = body["lagrange"]["primary"]
        separation = norm(sub(positions[secondary_id], positions[primary_id]))
        to_secondary = norm(sub(positions[body_id], positions[secondary_id]))
        if body["lagrange"]["point"] in {"L1", "L2"} and to_secondary >= 0.5 * separation:
            fail(f"{body_id}: L1/L2 is not close to its secondary")
        if body["lagrange"]["point"] in {"L4", "L5"}:
            if abs(norm(sub(positions[body_id], positions[primary_id])) - separation) > separation * 1e-6:
                fail(f"{body_id}: L4/L5 is not equidistant from the primary")
            if abs(to_secondary - separation) > separation * 1e-6:
                fail(f"{body_id}: L4/L5 is not equidistant from the secondary")

    earth = positions.get("earth")
    if earth:
        earth_radius_au = norm(sub(earth, positions["sun"])) / AU_M
        earth_speed_km_s = norm(velocities["earth"]) / 1000.0
        if abs(earth_radius_au - EARTH_RADIUS_AU) > 0.005:
            fail(f"earth: heliocentric distance {earth_radius_au:.6f} AU is unexpected")
        if abs(earth_speed_km_s - EARTH_SPEED_KM_S) > 0.3:
            fail(f"earth: speed {earth_speed_km_s:.4f} km/s is unexpected")
        period_days = 2.0 * math.pi * math.sqrt(
            by_id["earth"]["orbit"]["aM"] ** 3 / sun_gm) / DAY_S
        if abs(period_days - SIDEREAL_YEAR_DAYS) > 0.1:
            fail(f"earth: period {period_days:.3f} d is not a sidereal year")

    gateway = positions.get("gateway")
    if gateway:
        moon_distance_km = norm(sub(gateway, positions["moon"])) / 1000.0
        if abs(moon_distance_km - GATEWAY_L1_KM) > 5_000.0:
            fail(f"gateway: {moon_distance_km:.0f} km from the Moon is not the expected L1 distance")

    if errors:
        for error in errors:
            print(f"FAIL {error}")
        return 1

    if not args.quiet:
        summary = ", ".join(
            f"{EXPECTED_COUNTS[k]} {k}{'' if EXPECTED_COUNTS[k] == 1 else 's'}"
            for k in ("star", "planet", "moon", "asteroid", "station"))
        print(f"Catalogue OK: {len(bodies)} bodies ({summary}) at JD {start_jd:.1f}")
        print(f"  Earth:   {norm(sub(positions['earth'], positions['sun'])) / AU_M:.6f} AU, "
              f"{norm(velocities['earth']) / 1000.0:.3f} km/s")
        print(f"  Gateway: {norm(sub(positions['gateway'], positions['moon'])) / 1000.0:,.0f} km "
              f"from the Moon")
    return 0


if __name__ == "__main__":
    sys.exit(main())
