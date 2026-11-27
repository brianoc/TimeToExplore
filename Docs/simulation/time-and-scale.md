---
type: Design Document
title: Time and scale
description: Units, epoch, time acceleration, and the precision budget for a true-scale solar system.
tags: [simulation, time, scale, precision]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-24T04:15:13Z }
---

# Why this document exists

A solar system is about 10¹³ metres across. A ship is about 10² metres long. A
single-precision float has about seven significant digits. Those three facts
cannot be reconciled by wishing. This document fixes the units, the epoch, the
time model, and the precision strategy so the rest of the design has a stable
floor.

# Units

| Quantity | Unit | Notes |
|---|---|---|
| Length (simulation) | metre | SI throughout; no AU or km in the core model |
| Length (data file) | metre, except semi-major axes in AU for planets/asteroids | Converted at generation time; see [data schema](../technical/data-schema.md) |
| Time | second | Julian date at the API boundary; seconds inside orbital propagation |
| Mass parameter | m³/s² | `GM`, not mass; avoids needing a separate gravitational constant |
| Angle | radian | Data files use degrees for readability |
| Render length | Unity unit | Default 1 unit = 1,000 km = 10⁶ m |

The render scale is deliberately separate from the simulation scale. The
simulation never knows about Unity units. See
[ADR-003](../decisions/adr-003-render-scale.md).

# Epoch and calendar

The catalogue epoch is **2026-11-15 00:00:00 UTC**, Julian date **2461359.5**.
That is also the default game start.

* Planet elements are evaluated at the epoch from the JPL mean-element rates.
* Asteroid mean anomalies are advanced from their JPL osculation epoch to the
  common epoch.
* Moon phases are authored at the epoch; they are visually plausible but not a
  precise satellite ephemeris.

Julian date is the only time value passed between systems. It is a `double`,
which gives roughly microsecond resolution over the range of the game.

Conversions used by the prototype:

```text
JD(UTC) = DateTime.ToOADate() + 2415018.5
UTC(JD) = DateTime.FromOADate(JD - 2415018.5)
```

UTC versus TT is a difference of tens of seconds. At the navigation fidelity of
this game it is below the noise floor, and ignoring it keeps the code readable.

# Time acceleration

The game cannot ask a player to watch a 15-day transit in real time. Time is
accelerated by a continuous multiplier, not by moving the ship.

`SimulationClock.Scale` is the simulated seconds per real second. A flight
follows [the flight warp curve](/decisions/adr-027-continuous-warp-and-chase-camera.md),
which starts at 1x, rises to a cruise rate, and returns to 1x for the approach.
The target real duration depends only on the plan's flight time:

```text
target seconds = clamp(20 + 17 · ln(flight days / 6.7), 12, 60)
```

| Trip | Flight time | Target real time |
|---|---:|---:|
| Earth–Mars, fastest | 6.7 d | 20 s |
| Earth–Mars, slow end | 21.5 d | 40 s |
| Saturn, fastest | ~20 d | 39 s |
| Saturn, slow end | ~60 d | 57 s |
| Uranus, slow end | 111 d | 60 s (clamped) |

The shape in real time matters as much as the duration. The first and last 40%
of the target duration hold near 1x and climb along a quartic: the warp is about
20x after a second, a few hundred after two, and only crosses into five figures
around the fourth. That keeps the local departure, escape, brake, capture, and
parking on screen, because those are the parts worth watching, while the middle
crosses at a much higher rate. The first second is real time, so the camera can
ease into the chase pose before the system starts moving; the final seconds are
real time for the approach. The cruise rate is chosen from the target and the
quartic ramps' fifth-area, so integrating the curve over the target duration
still advances the flight by its whole planned length.

The manual ladder remains for the player: number keys 1–7 set `1x` to
`1,000,000x`, and `P` pauses. A number key takes the warp off the curve for the
rest of that flight. A new flight gives the clock back to the curve. The HUD
prints the actual scale, so the curve reads as a moving number.

**No proximity caps.** The old policy that dropped the warp near a body, a burn
event, or a station hold point is gone. The one remaining limit is the
flight's **integration budget**: the ship may spend at most 2,000 RK4 substeps
per frame, and if it runs out the clock is clamped to the date the ship actually
reached. The world therefore never runs ahead of the ship. A trip whose warp is
more than the budget can cover simply takes longer in real time than its target.

# Precision budget

## Simulation

`double` is mandatory. At Neptune's distance (4.5 × 10¹² m), a `float` has an
ULP of roughly 500,000 m—half a megametre. Even at 1 AU the ULP is about 16 km,
which is larger than a city and comparable to a small asteroid.

| Type | Significant digits | ULP at 1 AU | ULP at Neptune |
|---|---:|---:|---:|
| float32 | ~7 | ~16 km | ~500,000 km |
| float64 | ~15–16 | ~0.03 mm | ~1 mm |

The core model therefore stores positions in `Vector3d` (three doubles) and
converts to Unity's `Vector3` only in the renderer.

## Rendering

Even with double simulation values, Unity transforms are float. The renderer
therefore subtracts the ship position **before** converting to float:

```text
renderPosition = (bodyPositionM - shipPositionM) * unitsPerMetre
```

Nearby bodies then have small coordinates and full float precision, regardless
of where the ship is in the solar system. Distant bodies lose precision, but
their apparent size is tiny and the error is far below a pixel.

At the default scale (1 unit = 1,000 km), Earth's orbit has a radius of about
149,600 units and Neptune's about 4.5 million units. Float ULP at Neptune's
orbit is about 0.25 unit—250 km—but a body at that distance is a marker a few
pixels wide, so the error is invisible.

## Camera

The prototype camera orbits the ship at the render origin and uses:

* near clip 2% of the orbit distance, clamped to 200 km–50 km,
* far clip 20,000,000 units (20 billion km),
* 60° field of view,
* a skybox clear drawing the generated Milky Way
  ([The sky](../technical/sky.md)).

That near/far ratio is about 4 × 10⁸, which would destroy a conventional depth
buffer. Unity 6 uses a reversed-Z depth buffer on supported platforms, which
makes it workable. If z-fighting appears on a target platform, the fallback is a
two-camera layered scheme: one for nearby geometry and one for the distant
orrery. See [rendering at solar scale](../technical/rendering-scale.md).

# Coordinate frame

The simulation uses the **mean ecliptic and equinox of J2000**:

* origin at the Sun (heliocentric),
* +x toward the vernal equinox,
* +z toward ecliptic north,
* +y completing the right-handed set.

Unity is left-handed, with +y up. The render conversion is:

```text
Unity(x, y, z) = Ecliptic(x, z, -y)
```

This maps ecliptic north to Unity up and preserves chirality. The inverse is
used for ship movement.

Moon orbits are defined in the parent planet's equatorial plane where that
matters (Saturn, Uranus, Neptune), and in the ecliptic for Earth's Moon. The
transformation uses the planet's IAU pole (right ascension and declination
converted to ecliptic coordinates).

# What is deliberately not simulated yet

* Light travel time and relativistic effects (below 5% of light speed in this
  catalogue; the correction is under the navigation error budget).
* Solar radiation pressure, non-spherical gravity, and tidal evolution.
* N-body perturbations. The ephemerides are two-body Keplerian rails.
* Atmospheric drag and aerobraking.
* Time dilation from gravity or speed.

These are candidates for later milestones, not omissions by accident. See
[ADR-001](../decisions/adr-001-keplerian-orbits.md) and the
[roadmap](../production/roadmap.md).
