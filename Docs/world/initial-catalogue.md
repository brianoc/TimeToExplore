---
type: Catalogue
title: Initial catalogue
description: The planets and major moons shipped in the first playable, with their physical and orbital data.
tags: [world, catalogue, planets, moons]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Snapshot

The catalogue contains 1 star, 8 planets, 22 major moons, 100 main-belt
asteroids, and 6 stations. This concept lists the planets and moons; the
[asteroid catalogue](asteroid-catalogue.md) lists the belt, and
[Lagrange stations](lagrange-stations.md) lists the stations.

All values are at the catalogue epoch, 2026-11-15. `a` is the semi-major axis,
`e` the eccentricity, `i` the inclination to the ecliptic.

# Planets

| Planet | a (AU) | e | i (°) | Radius (km) | GM (m³/s²) | Rotation (h) | Tilt (°) | Major moons |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Mercury | 0.3871 | 0.2056 | 7.00 | 2,439.7 | 2.203e+13 | 1407.60 | 0.03 | 0 |
| Venus | 0.7233 | 0.0068 | 3.39 | 6,051.8 | 3.249e+14 | -5832.50 | 177.36 | 0 |
| Earth | 1.0000 | 0.0167 | -0.00 | 6,371.0 | 3.986e+14 | 23.93 | 23.44 | 1 |
| Mars | 1.5237 | 0.0934 | 1.85 | 3,389.5 | 4.283e+13 | 24.62 | 25.19 | 2 |
| Jupiter | 5.2029 | 0.0484 | 1.30 | 69,911.0 | 1.267e+17 | 9.93 | 3.13 | 4 |
| Saturn | 9.5363 | 0.0537 | 2.49 | 58,232.0 | 3.793e+16 | 10.66 | 26.73 | 7 |
| Uranus | 19.1886 | 0.0472 | 0.77 | 25,362.0 | 5.794e+15 | -17.24 | 97.77 | 5 |
| Neptune | 30.0700 | 0.0086 | 1.77 | 24,622.0 | 6.837e+15 | 16.11 | 28.32 | 3 |

Notes:

* Negative rotation periods are retrograde: Venus and Uranus turn backwards
  relative to the majority.
* Earth's orbit is the Earth–Moon barycentre orbit. The Moon is placed relative
  to Earth's centre.
* `GM` is used directly by the propagator; it is not a mass.

# Major moons

| Moon | Parent | a (km) | Period (d) | Radius (km) | e | i (°) | Plane |
|---|---|---:|---:|---:|---:|---:|---|
| Moon | Earth | 384,400 | 27.452 | 1,737.4 | 0.0549 | 5.14 | ecliptic |
| Phobos | Mars | 9,376 | 0.319 | 11.3 | 0.0151 | 1.08 | parent equator |
| Deimos | Mars | 23,463 | 1.263 | 6.2 | 0.0002 | 1.79 | parent equator |
| Io | Jupiter | 421,700 | 1.769 | 1,821.6 | 0.0041 | 0.05 | parent equator |
| Europa | Jupiter | 671,034 | 3.552 | 1,560.8 | 0.0090 | 0.47 | parent equator |
| Ganymede | Jupiter | 1,070,412 | 7.155 | 2,634.1 | 0.0013 | 0.20 | parent equator |
| Callisto | Jupiter | 1,882,709 | 16.691 | 2,410.3 | 0.0074 | 0.24 | parent equator |
| Mimas | Saturn | 185,539 | 0.944 | 198.2 | 0.0196 | 1.57 | parent equator |
| Enceladus | Saturn | 238,037 | 1.371 | 252.1 | 0.0047 | 0.02 | parent equator |
| Tethys | Saturn | 294,672 | 1.889 | 531.1 | 0.0001 | 1.09 | parent equator |
| Dione | Saturn | 377,415 | 2.738 | 561.4 | 0.0022 | 0.02 | parent equator |
| Rhea | Saturn | 527,068 | 4.518 | 763.8 | 0.0013 | 0.35 | parent equator |
| Titan | Saturn | 1,221,870 | 15.948 | 2,574.7 | 0.0288 | 0.33 | parent equator |
| Iapetus | Saturn | 3,560,820 | 79.340 | 734.5 | 0.0286 | 15.47 | parent equator |
| Miranda | Uranus | 129,390 | 1.406 | 235.8 | 0.0013 | 4.34 | parent equator |
| Ariel | Uranus | 190,900 | 2.520 | 578.9 | 0.0012 | 0.26 | parent equator |
| Umbriel | Uranus | 266,000 | 4.145 | 584.7 | 0.0039 | 0.13 | parent equator |
| Titania | Uranus | 436,300 | 8.707 | 788.9 | 0.0011 | 0.34 | parent equator |
| Oberon | Uranus | 583,500 | 13.466 | 761.4 | 0.0014 | 0.06 | parent equator |
| Triton | Neptune | 354,759 | 5.877 | 1,353.4 | 0.0000 | 156.88 | parent equator |
| Proteus | Neptune | 117,647 | 1.122 | 210.0 | 0.0005 | 0.52 | parent equator |
| Nereid | Neptune | 5,513,400 | 360.062 | 170.0 | 0.7507 | 7.23 | parent equator |

Notes:

* Periods are derived from the semi-major axis and the parent's `GM`, and match
  the observed values closely.
* Triton's 156.88° inclination makes its orbit retrograde.
* Nereid's 0.75 eccentricity is real and makes it a challenging rendezvous
  target.
* The plane column is the orbit reference plane. "Parent equator" uses the
  planet's IAU pole, which is why Uranus's moons are nearly perpendicular to the
  ecliptic.
* Moon phase at the epoch is authored, not ephemeris-grade. The geometry of the
  orbit is real; the exact position along it is plausible.

# Missing moons

The catalogue keeps the major moons, not every satellite. It omits, for example,
Jupiter's small irregular moons, Saturn's minor ring moons, and Neptune's
distant irregulars. The cutoff is practical: the listed moons are large enough
to be destinations, hazards, or landmarks.

# Adding a body

Add an entry to `MOONS` or `PLANETS` in `tools/generate_solar_system.py` and
regenerate. The Unity importer will pick up the new JSON on the next asset
refresh. See [data schema](../technical/data-schema.md).
