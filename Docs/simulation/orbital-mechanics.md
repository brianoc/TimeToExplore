---
type: Design Document
title: Orbital mechanics
description: How bodies move, how their positions are propagated, and where the model stops.
tags: [simulation, orbital-mechanics, ephemeris]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
sources:
  - id: jpl-approx
    resource: https://ssd.jpl.nasa.gov/planets/approx_pos.html
    title: Approximate Positions of the Planets
    author: team:jpl-ssd
  - id: jpl-sbdb
    resource: https://ssd-api.jpl.nasa.gov/sbdb_query.api
    title: JPL Small-Body Database Query API
    author: team:jpl-ssd
  - id: jpl-sats
    resource: https://ssd.jpl.nasa.gov/sats/
    title: Planetary Satellite Ephemerides
    author: team:jpl-ssd
---

# Model

Every planet, moon, and asteroid is propagated as a **two-body Keplerian orbit
around its parent**. The Sun is fixed at the origin. There is no N-body
integration and no runtime physics engine involved in the ephemeris.

This is a deliberate first-prototype choice: it is deterministic, cheap, exactly
reproducible, and accurate enough to make launch windows and station geometry
feel real. The consequences are recorded in
[ADR-001](../decisions/adr-001-keplerian-orbits.md).

# Elements

The catalogue stores the classical elements:

| Element | Symbol | Meaning |
|---|---|---|
| Semi-major axis | `aM` | metres; size of the ellipse |
| Eccentricity | `e` | 0 = circle, <1 = ellipse |
| Inclination | `iDeg` | tilt of the orbital plane |
| Longitude of ascending node | `raanDeg` | where the orbit crosses the reference plane |
| Argument of periapsis | `argPeriDeg` | where the orbit is closest to the parent |
| Mean anomaly at epoch | `meanAnomalyDeg` | position along the orbit at the epoch |
| Epoch | `epochJulianDate` | date at which the elements are valid |

The parent's `GM` supplies the gravitational parameter. For a planet or asteroid
the parent is the Sun; for a moon it is the planet.

# Propagation

Given a Julian date `t`:

```text
n = sqrt(GM / a³)                  mean motion
M = M₀ + n (t − t₀)                mean anomaly
E = solve(M = E − e sin E)          eccentric anomaly
r = a (1 − e cos E)                 radius
x = a (cos E − e)
y = a sqrt(1 − e²) sin E
vx = −sqrt(GM a) / r · sin E
vy = sqrt(GM a (1 − e²)) / r · cos E
```

The position and velocity are then rotated from the orbital plane into the
reference frame by `Rz(Ω) · Rx(i) · Rz(ω)`.

The Kepler solver is Newton–Raphson with a bisection fallback. The fallback
matters for high-eccentricity objects such as Nereid (`e = 0.75`), where a bad
initial guess can send Newton's method outside the valid interval.

The bodies only need elements → state. A ship's coast needs **state → state**,
including hyperbolic arrival legs, so `KeplerPropagator` uses the universal
variable formulation with the Stumpff functions `C(z)` and `S(z)`. One solver
covers the ellipse, the parabola, and the hyperbola, and `ConicElements` derives
the conic's shape and true anomaly from the same state. The two paths are
checked against each other in `KeplerPropagatorTests`, and the transfer-conic
capture uses the result ([ADR-014](/decisions/adr-014-keplerian-coast.md)).

# Frames and hierarchy

Bodies are updated parent-first:

```text
Sun (origin)
├── planet
│   └── moon
├── asteroid
└── station (Lagrange point of a pair)
```

A moon's position is its parent's current position plus a planetocentric orbit.
A station's position is recomputed from the current positions of its two
Lagrange partners. This is why the station follows the pair instead of sitting
at a fixed coordinate.

## Moon planes

Moons are split into two groups:

* **Parent-equator frame.** Most major moons orbit near their planet's equator.
  The catalogue stores the planet's IAU north pole (RA/Dec), converts it to
  ecliptic coordinates, and builds an orthonormal basis. This is essential for
  Uranus, whose 98° tilt puts its moons in a nearly perpendicular plane, and for
  Neptune, whose moon Triton is retrograde.
* **Ecliptic frame.** Earth's Moon is defined relative to the ecliptic, because
  its 5.1° inclination is measured that way and it is the most familiar case.

The epoch phase of each moon is authored. The plane, semi-major axis,
eccentricity, and period are real; the exact angle along the orbit at the epoch
is not. The result looks right and produces plausible moon geometry for
stations, but it is not a navigation-grade satellite ephemeris. Replacing it
with JPL Horizons or SPICE kernels is a later step.[^jpl-sats]

# Planet elements

The planet elements come from JPL's approximate-position table, which is a
least-squares fit to the mean ecliptic and equinox of J2000, valid from 1800 to
2050.[^jpl-approx] The catalogue evaluates the fitted elements and their rates
at the game epoch and stores the result as fixed elements.

Accuracy is on the order of arcminutes over the fitted interval—far better than
the game needs for a planet's position, and enough to get launch windows and
arrival geometry right. It is not an ephemeris for real mission design.

The **Earth–Moon barycentre** is used for Earth's heliocentric orbit, as in the
JPL table. The Moon is then placed relative to the Earth's centre. This is a
simplification: the Earth itself wobbles about the barycentre by about 4,700 km.
At the game's scale that is below the radius of Earth and well below the
navigation precision, but a future version can add a proper barycentre node if
docking or lunar departure geometry needs it.

# Asteroids

The 100 largest main-belt asteroids use osculating elements and effective
diameters from the JPL Small-Body Database.[^jpl-sbdb] Their mean anomalies are
advanced from the database epoch to the game epoch, so all bodies share one
epoch.

Their positions are accurate enough to make the belt feel populated and to
support a rendezvous with a named object. They are not a substitute for a real
small-body ephemeris; close-approach work should eventually use SPICE or a
dedicated solution.

# Lagrange points

A station at a Lagrange point of a primary/secondary pair is positioned from the
current state vectors:

* **L1** lies between the two bodies, `γr` from the secondary toward the primary.
* **L2** lies beyond the secondary, `γr` away from the primary.
* **L4** and **L5** lie 60° ahead of and behind the secondary in its orbital
  plane.

`γ` is the dimensionless distance from the secondary, solved from the circular
restricted three-body quintic:

```text
L1: γ⁵ − (3−μ)γ⁴ + (3−2μ)γ³ − μγ² + 2μγ − μ = 0
L2: γ⁵ + (3−μ)γ⁴ + (3−2μ)γ³ − μγ² − 2μγ − μ = 0
μ  = m₂ / (m₁ + m₂)
```

A bisection solve over `γ ∈ (0, 0.5)` is stable for every pair in the catalogue.
The Hill approximation `γ ≈ (μ/3)^(1/3)` is within a few percent and is useful
for a quick estimate, but the station positions use the quintic.

L4/L5 use the secondary's angular momentum to define the "ahead" direction, so
the result is correct for retrograde systems as well.

## Stability

* **L1 and L2 are unstable.** A station there needs periodic stationkeeping.
  In the game this is a service cost and a source of missions: deliver
  propellant, relieve a crew, fix a thruster.
* **L4 and L5 are stable** for a mass ratio below about 0.0385. The Sun–Jupiter
  pair qualifies comfortably, which is why the Jovian Transfer Yard sits at L4
  and why the Trojan swarms are there in reality.

The prototype does not simulate stationkeeping drift. Stations stay exactly on
their computed points. Adding a slow drift and a correction budget is a natural
next step once the fuel model exists.

# What is not modelled

* N-body perturbations between planets, moons, and asteroids.
* Non-spherical gravity (`J2`, `J4`) and mascons.
* Tidal evolution and secular changes in semi-major axis.
* Solar radiation pressure and Yarkovsky drift.
* Relativity.
* Atmospheric drag and aerobraking.
* Collisions and fragmentation.

For the player's ship, the model is different: the ship's trajectory **is**
integrated numerically, because continuous thrust does not produce a conic. The
planets stay on their rails while the ship is integrated against their gravity.
That hybrid—**on-rails world, integrated ship**—is the implemented
architecture, and the reasons are in
[ADR-005](../decisions/adr-005-continuous-gravity.md).

# The ship's field

`GravityField` sums point-mass gravity from every catalogue body at the current
Julian date:

```text
a(r, t) = Σ GMᵢ (rᵢ(t) − r) / max(|rᵢ(t) − r|, Rᵢ)³
```

The clamp at each body's radius keeps a collision from producing a singularity:
inside a body the pull falls linearly to zero at the centre, and the integrator
reports the hit. Bodies whose acceleration at the ship is below 10⁻¹¹ m/s² are
dropped, and at most eight are summed; the pruned result is tested against the
full sum to better than 10⁻⁵ relative. Stations have zero `GM` and contribute
nothing.

The field is evaluated without touching `SolarSystemModel`: it re-derives each
body's state at the requested date from the same orbital elements the frame
loop uses. That is what lets the integrator evaluate four RK4 stage times inside
one frame without disturbing the render state. A regression test asserts that
the pure evaluator and the frame update agree at the current date.

# Verification

The prototype catalogue was checked numerically against known values at the
epoch:

| Body | Heliocentric distance | Orbital speed |
|---|---:|---:|
| Mercury | 0.314 AU | 57.9 km/s |
| Venus | 0.721 AU | 35.1 km/s |
| Earth | 0.989 AU | 30.1 km/s |
| Mars | 1.609 AU | 22.8 km/s |
| Jupiter | 5.319 AU | 12.8 km/s |
| Saturn | 9.421 AU | 9.8 km/s |
| Uranus | 19.428 AU | 6.7 km/s |
| Neptune | 29.877 AU | 5.5 km/s |

Moon geocentric distances fall within a few percent of their semi-major axes,
as expected for near-circular orbits. Lagrange distances are in the expected
range at the epoch: Sun–Earth L1 at 1.476 million km, Earth–Moon L1 at
54,841 km from the Moon (the Moon was near perigee), and Sun–Jupiter L4 at
Jupiter's orbital radius, 60° ahead. The station values in the world document
are read from the generated state, not copied from a textbook.

The ship's field is checked separately. A coasting ship started on Earth's state
follows the analytic Kepler ephemeris to within 5 km over 30 days, and a
circular heliocentric orbit holds its radius to one part in 10⁶ over 200 days.
The values and tolerances are in
[testing and verification](../technical/testing.md).

[^jpl-approx]: JPL, *Approximate Positions of the Planets*.
[^jpl-sbdb]: JPL, *Small-Body Database Query API*.
[^jpl-sats]: JPL, *Planetary Satellite Ephemerides*.
