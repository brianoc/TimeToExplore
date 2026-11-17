#!/usr/bin/env python3
"""Generate the Milky Way skybox texture for Time to Explore.

The output is an equirectangular (latitude/longitude) RGB image that the
`TimeToExplore/Skybox` shader samples:
`u = atan2(z, x) / 2pi + 0.5`, `v = asin(y) / pi + 0.5`, with `y` the ecliptic
north pole. The image carries only the diffuse glow of the galaxy — star clouds,
dust lanes, and the central bulge. Resolved stars are drawn by the shader, so
they stay one pixel wide at any resolution instead of turning into glitter.

The band is placed with the real galactic frame: the galactic north pole at
RA 192.85948, Dec +27.12825 and the galactic centre at RA 266.40499,
Dec -28.93617, converted through the obliquity of the ecliptic. The whole sky is
then spun about the ecliptic pole by `BULGE_AZIMUTH_DEGREES` so the bulge sits
in the opening camera view. The game has no sidereal time, so that azimuth is a
composition choice, not an astronomical one; the band's 60-degree tilt to the
ecliptic is the part that is real.

Usage:
    python3 tools/generate_skybox.py                 # 2048 x 1024
    python3 tools/generate_skybox.py --width 4096
    python3 tools/generate_skybox.py --check         # report, write nothing

Requires numpy and Pillow (`python3 -m pip install numpy pillow`). The output is
deterministic for a given seed and width.
"""

from __future__ import annotations

import argparse
import math
import os

import numpy as np
from PIL import Image

# --- Galactic frame -------------------------------------------------------

# J2000.0 equatorial coordinates, degrees.
GALACTIC_NORTH_RA = 192.85948
GALACTIC_NORTH_DEC = 27.12825
GALACTIC_CENTRE_RA = 266.40499
GALACTIC_CENTRE_DEC = -28.93617

# Obliquity of the ecliptic, degrees (J2000.0).
OBLIQUITY = 23.4392911

# The opening camera looks along azimuth -90 degrees (yaw 0, pitch 14). Spin the
# whole sky about the ecliptic pole so the galactic centre sits there: the game
# has no sidereal time, so this azimuth is a composition choice. The band's
# 60-degree tilt to the ecliptic is the part that comes from the real sky.
BULGE_AZIMUTH_DEGREES = -86.0

# --- Look -----------------------------------------------------------------

BACKGROUND = np.array([0.0055, 0.0070, 0.0110], dtype=np.float64)
# The band is normalised so its brightest percentile lands on this value. Keep
# it well below 1 so the HUD text and the ship marker stay legible over it.
BAND_PEAK = 0.55
BAND_WARM = np.array([1.00, 0.90, 0.72], dtype=np.float64)
BAND_COOL = np.array([0.80, 0.87, 1.00], dtype=np.float64)


def _equatorial(ra_deg: float, dec_deg: float) -> np.ndarray:
    ra = math.radians(ra_deg)
    dec = math.radians(dec_deg)
    return np.array(
        [math.cos(dec) * math.cos(ra), math.cos(dec) * math.sin(ra), math.sin(dec)],
        dtype=np.float64,
    )


def _ecliptic_from_equatorial(vector: np.ndarray) -> np.ndarray:
    """Rotate an equatorial unit vector into the ecliptic frame (y = north)."""
    eps = math.radians(OBLIQUITY)
    x_axis = np.array([1.0, 0.0, 0.0])
    y_axis = np.array([0.0, -math.sin(eps), math.cos(eps)])
    z_axis = np.cross(x_axis, y_axis)
    return np.array(
        [vector @ x_axis, vector @ y_axis, vector @ z_axis], dtype=np.float64
    )


def galactic_matrix() -> np.ndarray:
    """World (ecliptic) direction -> galactic (x = centre, z = north pole)."""
    north = _ecliptic_from_equatorial(
        _equatorial(GALACTIC_NORTH_RA, GALACTIC_NORTH_DEC)
    )
    centre = _ecliptic_from_equatorial(
        _equatorial(GALACTIC_CENTRE_RA, GALACTIC_CENTRE_DEC)
    )
    x_axis = centre - np.dot(centre, north) * north
    x_axis /= np.linalg.norm(x_axis)
    y_axis = np.cross(north, x_axis)
    y_axis /= np.linalg.norm(y_axis)

    # The unspun sky: where does the galactic centre sit in world azimuth?
    axes = np.stack([x_axis, y_axis, north])
    centre_world = axes[0]
    centre_azimuth = math.degrees(math.atan2(centre_world[2], centre_world[0]))
    spin = math.radians(BULGE_AZIMUTH_DEGREES - centre_azimuth)

    # Sampling direction -> unspun sky direction: rotate by -spin about +Y.
    cos_s, sin_s = math.cos(spin), math.sin(spin)
    spin_back = np.array(
        [[cos_s, 0.0, -sin_s], [0.0, 1.0, 0.0], [sin_s, 0.0, cos_s]]
    )
    return axes @ spin_back


def directions(width: int) -> tuple[np.ndarray, int]:
    """World unit vectors for an equirectangular grid, row 0 at the top."""
    height = width // 2
    longitude = (np.arange(width) + 0.5) / width * 2.0 * math.pi - math.pi
    latitude = math.pi / 2.0 - (np.arange(height) + 0.5) / height * math.pi
    lon, lat = np.meshgrid(longitude, latitude)
    cos_lat = np.cos(lat)
    dirs = np.stack(
        [cos_lat * np.cos(lon), np.sin(lat), cos_lat * np.sin(lon)], axis=-1
    )
    return dirs, height


# --- Noise ----------------------------------------------------------------


def gaussian_field(rng: np.random.Generator, size: int, beta: float) -> np.ndarray:
    """A periodic, isotropic random field with power spectrum k^-beta."""
    white = rng.standard_normal((size, size, size)).astype(np.float32)
    spectrum = np.fft.rfftn(white)
    kx = np.fft.fftfreq(size)[:, None, None]
    ky = np.fft.fftfreq(size)[None, :, None]
    kz = np.fft.rfftfreq(size)[None, None, :]
    k_squared = kx * kx + ky * ky + kz * kz
    amplitude = np.maximum(k_squared, 1e-12) ** (-beta / 4.0)
    amplitude[0, 0, 0] = 0.0
    field = np.fft.irfftn(spectrum * amplitude, s=(size, size, size), axes=(0, 1, 2))
    field -= field.mean()
    deviation = field.std()
    if deviation > 0.0:
        field /= deviation
    return field.astype(np.float32)


def sample_field(field: np.ndarray, points: np.ndarray) -> np.ndarray:
    """Trilinearly sample a periodic field at `points` (already scaled)."""
    size = field.shape[0]
    q = np.mod(points, size)
    i0 = np.floor(q).astype(np.int64) % size
    fraction = (q - np.floor(q)).astype(np.float32)
    i1 = (i0 + 1) % size

    fx, fy, fz = fraction[..., 0], fraction[..., 1], fraction[..., 2]
    out = np.zeros(points.shape[:-1], dtype=np.float32)
    for dx, wx in ((0, 1.0 - fx), (1, fx)):
        for dy, wy in ((0, 1.0 - fy), (1, fy)):
            for dz, wz in ((0, 1.0 - fz), (1, fz)):
                ix = i1 if dx else i0
                iy = i1 if dy else i0
                iz = i1 if dz else i0
                out += field[ix[..., 0], iy[..., 1], iz[..., 2]] * (wx * wy * wz)
    return out


def smoothstep(edge0: float, edge1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# --- The sky --------------------------------------------------------------


def render(width: int, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    dirs, height = directions(width)
    flat = dirs.reshape(-1, 3).astype(np.float32)

    to_galactic = galactic_matrix().astype(np.float32)
    galactic = flat @ to_galactic.T
    b = np.arcsin(np.clip(galactic[:, 2], -1.0, 1.0))
    l = np.arctan2(galactic[:, 1], galactic[:, 0])
    centre_cos = np.clip(galactic[:, 0], -1.0, 1.0)
    centre_distance = np.arccos(centre_cos)  # radians from the galactic centre

    # Four independent cloud fields, sampled at different scales.
    coarse = gaussian_field(rng, 128, 3.4)
    medium = gaussian_field(rng, 128, 3.2)
    fine = gaussian_field(rng, 160, 2.9)
    dust_field = gaussian_field(rng, 160, 2.7)

    def field_noise(field: np.ndarray, frequency: float) -> np.ndarray:
        return sample_field(field, flat * frequency)

    structure = (
        0.62 * field_noise(coarse, 5.0)
        + 0.26 * field_noise(medium, 13.0)
        + 0.12 * field_noise(fine, 31.0)
    )
    granulation = field_noise(fine, 74.0)
    dust_noise = field_noise(dust_field, 21.0)

    # The galactic plane: a bright core inside a broad halo.
    core = np.exp(-((b / 0.105) ** 2))
    halo = np.exp(-((b / 0.34) ** 2))
    envelope = 0.52 * core + 0.70 * halo

    # Longitude weighting: the bulge dominates, with the Cygnus and Carina
    # star clouds as secondary bright patches.
    def wrapped(angle: np.ndarray) -> np.ndarray:
        return np.mod(angle + math.pi, 2.0 * math.pi) - math.pi

    bulge = np.exp(-((centre_distance / 0.42) ** 2))
    cygnus = np.exp(-((wrapped(l - 1.02) / 0.55) ** 2))
    carina = np.exp(-((wrapped(l + 1.12) / 0.40) ** 2))
    weighting = 0.55 + 1.25 * bulge + 0.32 * cygnus + 0.15 * carina

    # Star clouds break the band into clumps. A multiplicative form cannot go
    # negative or clamp into a flat plateau, so the structure stays soft.
    clouds = np.exp(0.62 * structure)
    grains = np.exp(0.30 * granulation)

    # Fine, texture-resolution speckle: the unresolved star clouds. Generated
    # straight on the image grid so it survives the equirect projection.
    white = rng.standard_normal((height, width)).astype(np.float32)
    speckle = white
    for _ in range(2):
        speckle = (
            speckle
            + np.roll(speckle, 1, axis=1)
            + np.roll(speckle, -1, axis=1)
            + np.roll(speckle, 1, axis=0)
            + np.roll(speckle, -1, axis=0)
        ) / 5.0
    grains *= np.exp(0.55 * speckle.reshape(-1))

    # Dust: soft filaments where the noise crosses zero, strongest in the plane
    # and toward the bulge. It dims the band, it does not carve it out.
    filaments = np.exp(-((dust_noise / 0.62) ** 2))
    wisps = np.exp(-((field_noise(fine, 62.0) / 0.55) ** 2))
    dust_lane = (
        (0.78 * filaments + 0.22 * wisps)
        * np.exp(-((b / 0.135) ** 2))
        * (0.45 + 0.55 * np.exp(-((centre_distance / 0.85) ** 2)))
    )
    transmission = 1.0 - 0.68 * np.clip(dust_lane, 0.0, 1.0) ** 1.3

    luminance = envelope * weighting * clouds * grains * transmission
    # Normalise the band so its bright core has a predictable value: the peak
    # moves with the noise seed, the legibility does not.
    peak = np.percentile(luminance, 99.9)
    if peak > 0.0:
        luminance *= BAND_PEAK / peak

    warmth = np.clip(
        0.08 + 0.92 * np.exp(-((centre_distance / 0.24) ** 2)), 0.0, 1.0
    )[:, None]
    tint = BAND_COOL[None, :] * (1.0 - warmth) + BAND_WARM[None, :] * warmth

    sky = BACKGROUND[None, :] + luminance[:, None] * tint
    # Soft shoulder: keeps the bulge from clipping to a flat white disc.
    sky = sky / (1.0 + 0.55 * sky)
    sky = np.clip(sky, 0.0, 1.0)

    return (sky.reshape(height, width, 3) * 255.0 + 0.5).astype(np.uint8)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--width", type=int, default=2048, help="image width (default 2048)")
    parser.add_argument("--seed", type=int, default=20261115, help="noise seed")
    parser.add_argument(
        "--out",
        default=os.path.join(
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            "TimeToExplore",
            "Assets",
            "Resources",
            "Sky",
            "milky_way.png",
        ),
        help="output PNG",
    )
    parser.add_argument(
        "--check", action="store_true", help="render and report without writing"
    )
    args = parser.parse_args()

    if args.width < 512 or args.width % 2 != 0:
        parser.error("--width must be even and at least 512")

    image = render(args.width, args.seed)

    dark = image[::8, ::8].astype(np.float64)
    luminance = dark @ np.array([0.2126, 0.7152, 0.0722])
    print(f"{args.width}x{args.width // 2}, seed {args.seed}")
    print(
        "luminance  min {:.1f}  mean {:.1f}  p99 {:.1f}  max {:.1f}  (0-255)".format(
            luminance.min(),
            luminance.mean(),
            np.percentile(luminance, 99),
            luminance.max(),
        )
    )

    if args.check:
        return 0

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    Image.fromarray(image, "RGB").save(args.out, optimize=True)
    print(f"wrote {args.out} ({os.path.getsize(args.out) / 1024 / 1024:.1f} MiB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
