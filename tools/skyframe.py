#!/usr/bin/env python3
"""The one place the sky's frame is defined.

Both halves of the sky — the baked Milky Way band and the bright-star
catalogue — have to agree on where a direction of the sky lands in the game's
world frame, or the stars drift out of the dust lanes they belong to. This
module is that agreement: `generate_skybox.py` and `generate_bright_stars.py`
both import it, and neither keeps a private copy of the constants.

The frame is built in three steps.

1. **Equatorial to ecliptic.** Catalogue positions are J2000 right ascension and
   declination. Rotate them through the obliquity of the ecliptic so +Y is the
   ecliptic north pole, which is the frame the orrery and the camera use.
2. **Ecliptic to galactic.** The galactic north pole and centre are also given
   in equatorial coordinates; rotate both the same way and build an orthonormal
   basis from them. This is what puts the band about 60 degrees from the
   ecliptic, as it is in the real sky.
3. **Spin about the ecliptic pole.** The prototype has no sidereal time, so the
   sky does not rotate with a date. The whole sky is spun by one authored angle
   so the galactic centre lands in the opening camera view. That azimuth is art
   direction; the tilt in step 2 is astronomy.
"""

from __future__ import annotations

import math

import numpy as np

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


def equatorial(ra_deg: float, dec_deg: float) -> np.ndarray:
    """Unit vector on the J2000 equatorial sphere."""
    ra = math.radians(ra_deg)
    dec = math.radians(dec_deg)
    return np.array(
        [math.cos(dec) * math.cos(ra), math.cos(dec) * math.sin(ra), math.sin(dec)],
        dtype=np.float64,
    )


def ecliptic_from_equatorial(vector: np.ndarray) -> np.ndarray:
    """Rotate an equatorial unit vector into the ecliptic frame (y = north)."""
    eps = math.radians(OBLIQUITY)
    x_axis = np.array([1.0, 0.0, 0.0])
    y_axis = np.array([0.0, -math.sin(eps), math.cos(eps)])
    z_axis = np.cross(x_axis, y_axis)
    return np.array(
        [vector @ x_axis, vector @ y_axis, vector @ z_axis], dtype=np.float64
    )


def frame_axes() -> np.ndarray:
    """Galactic basis in the unspun ecliptic frame: rows are centre, y, north."""
    north = ecliptic_from_equatorial(equatorial(GALACTIC_NORTH_RA, GALACTIC_NORTH_DEC))
    centre = ecliptic_from_equatorial(
        equatorial(GALACTIC_CENTRE_RA, GALACTIC_CENTRE_DEC)
    )
    x_axis = centre - np.dot(centre, north) * north
    x_axis /= np.linalg.norm(x_axis)
    y_axis = np.cross(north, x_axis)
    y_axis /= np.linalg.norm(y_axis)
    return np.stack([x_axis, y_axis, north])


def spin_back() -> np.ndarray:
    """Rotation taking a world direction into the unspun ecliptic frame.

    Rotating about +Y by ``spin`` adds ``spin`` to a direction's azimuth, so the
    world direction of the galactic centre is the unspun one rotated by
    ``target - centre_azimuth``. ``spin_back`` is the inverse of that, hence the
    reversed sine terms. Getting this sign wrong puts the bulge at
    ``2 * centre_azimuth - target`` instead of the requested azimuth.
    """
    centre_world = frame_axes()[0]
    centre_azimuth = math.degrees(math.atan2(centre_world[2], centre_world[0]))
    spin = math.radians(BULGE_AZIMUTH_DEGREES - centre_azimuth)
    cos_s, sin_s = math.cos(spin), math.sin(spin)
    return np.array(
        [[cos_s, 0.0, sin_s], [0.0, 1.0, 0.0], [-sin_s, 0.0, cos_s]],
        dtype=np.float64,
    )


def galactic_matrix() -> np.ndarray:
    """World (ecliptic, spun) direction -> galactic (x = centre, z = north pole)."""
    return frame_axes() @ spin_back()


def world_from_equatorial(vector: np.ndarray) -> np.ndarray:
    """Equatorial unit vector -> world direction, with the sky's spin applied."""
    return spin_back().T @ ecliptic_from_equatorial(vector)


def sky_uv(direction: np.ndarray) -> tuple[float, float]:
    """Direction -> equirectangular (u, v), matching the shader's SkyUv."""
    x, y, z = direction
    u = math.atan2(z, x) / (2.0 * math.pi) + 0.5
    v = math.asin(max(-1.0, min(1.0, y))) / math.pi + 0.5
    return u, v
