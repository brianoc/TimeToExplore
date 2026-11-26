#!/usr/bin/env python3
"""Fetch and check the planet albedo maps.

The nine 2K equirectangular maps come from Solar System Scope, which publishes
them under CC BY 4.0 and derives them from NASA imagery. They are committed
under Assets/Resources/Planets so the game ships without a download step; this
tool exists to fetch them again and to prove the committed set is intact.

Usage
-----
    python3 tools/fetch_planet_textures.py            # fetch missing maps
    python3 tools/fetch_planet_textures.py --check    # no network, verify files
    python3 tools/fetch_planet_textures.py --force    # re-download everything
    python3 tools/fetch_planet_textures.py --out DIR  # somewhere other than the project

`--check` is the fast gate in tools/check.sh. It reads each file's JPEG header,
so it needs no third-party imaging library.
"""

from __future__ import annotations

import argparse
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT = ROOT / "TimeToExplore" / "Assets" / "Resources" / "Planets"

BASE_URL = "https://www.solarsystemscope.com/textures/download/"
WIDTH = 2048
HEIGHT = 1024
MIN_BYTES = 50_000
USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
)
ATTEMPTS = 3

# The catalogue key is "Planets/<id>_albedo", so the target names must keep the
# same id and one of the two extensions the validator accepts.
MANIFEST = [
    dict(id="mercury", source="2k_mercury.jpg", target="mercury_albedo.jpg"),
    dict(id="venus", source="2k_venus_atmosphere.jpg", target="venus_albedo.jpg"),
    dict(id="earth", source="2k_earth_daymap.jpg", target="earth_albedo.jpg"),
    dict(id="mars", source="2k_mars.jpg", target="mars_albedo.jpg"),
    dict(id="jupiter", source="2k_jupiter.jpg", target="jupiter_albedo.jpg"),
    dict(id="saturn", source="2k_saturn.jpg", target="saturn_albedo.jpg"),
    dict(id="uranus", source="2k_uranus.jpg", target="uranus_albedo.jpg"),
    dict(id="neptune", source="2k_neptune.jpg", target="neptune_albedo.jpg"),
    dict(id="moon", source="2k_moon.jpg", target="moon_albedo.jpg"),
]

ATTRIBUTION = (
    "Planet albedo maps: Solar System Scope (https://www.solarsystemscope.com/textures/), "
    "CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/), based on NASA data."
)

SOF_MARKERS = {0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7,
               0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF}


def jpeg_dimensions(data: bytes) -> tuple[int, int] | None:
    """Return a JPEG's (width, height) from its frame header, or None."""
    if not data.startswith(b"\xff\xd8"):
        return None

    index = 2
    while index + 4 <= len(data):
        if data[index] != 0xFF:
            index += 1
            continue
        marker = data[index + 1]
        if marker == 0xFF:
            index += 1
            continue
        if marker == 0xD9 or marker == 0xDA:
            return None
        if marker == 0x01 or 0xD0 <= marker <= 0xD8:
            index += 2
            continue

        length = int.from_bytes(data[index + 2:index + 4], "big")
        if marker in SOF_MARKERS:
            if index + 9 > len(data):
                return None
            height = int.from_bytes(data[index + 5:index + 7], "big")
            width = int.from_bytes(data[index + 7:index + 9], "big")
            return width, height
        if length < 2:
            return None
        index += 2 + length
    return None


def check_files(out_dir: Path) -> list[str]:
    """Return one message per missing or malformed map in `out_dir`."""
    errors: list[str] = []
    for entry in MANIFEST:
        path = Path(out_dir) / entry["target"]
        if not path.is_file():
            errors.append(f"{path}: missing")
            continue

        data = path.read_bytes()
        if len(data) < MIN_BYTES:
            errors.append(
                f"{path.name}: {len(data)} bytes, expected at least {MIN_BYTES}")
        if not data.startswith(b"\xff\xd8"):
            errors.append(f"{path.name}: not a JPEG")
            continue
        dimensions = jpeg_dimensions(data)
        if dimensions != (WIDTH, HEIGHT):
            errors.append(
                f"{path.name}: {dimensions[0]}x{dimensions[1]}, expected {WIDTH}x{HEIGHT}"
                if dimensions else f"{path.name}: cannot read the JPEG frame header")
    return errors


def download_one(entry: dict, out_dir: Path) -> None:
    """Download one map, retrying, and move it into place atomically."""
    url = BASE_URL + entry["source"]
    target = out_dir / entry["target"]
    partial = target.with_name(target.name + ".part")

    for attempt in range(1, ATTEMPTS + 1):
        request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read()
            if len(data) < MIN_BYTES:
                raise OSError(f"response is only {len(data)} bytes")
            partial.write_bytes(data)
            os.replace(partial, target)
            print(f"  {entry['target']}  {len(data):,} bytes")
            return
        except (urllib.error.URLError, OSError) as exc:
            if partial.exists():
                partial.unlink()
            if attempt == ATTEMPTS:
                raise SystemExit(f"error: cannot download {url}: {exc}") from exc
            pause = 2 * attempt
            print(f"  retry {attempt}/{ATTEMPTS - 1} for {entry['source']} in {pause}s: {exc}",
                  file=sys.stderr)
            time.sleep(pause)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="verify the files on disk and exit; no network")
    parser.add_argument("--force", action="store_true",
                        help="download even when a target already exists")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT,
                        help="target directory (default: %(default)s)")
    args = parser.parse_args()

    if args.check:
        errors = check_files(args.out)
        if errors:
            for error in errors:
                print(f"FAIL {error}", file=sys.stderr)
            return 1
        print(f"Planet textures OK: {len(MANIFEST)} maps at {WIDTH}x{HEIGHT} in {args.out}")
        print(ATTRIBUTION)
        return 0

    args.out.mkdir(parents=True, exist_ok=True)
    for entry in MANIFEST:
        target = args.out / entry["target"]
        if target.is_file() and not args.force:
            print(f"  {entry['target']}  present, kept")
            continue
        download_one(entry, args.out)

    errors = check_files(args.out)
    if errors:
        for error in errors:
            print(f"FAIL {error}", file=sys.stderr)
        return 1
    print(f"Planet textures OK: {len(MANIFEST)} maps at {WIDTH}x{HEIGHT} in {args.out}")
    print(ATTRIBUTION)
    return 0


if __name__ == "__main__":
    sys.exit(main())
