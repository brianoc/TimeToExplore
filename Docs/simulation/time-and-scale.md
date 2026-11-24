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
accelerated by a multiplier, not by moving the ship.

| Setting | 1 real second equals | 1 frame at 60 fps equals |
|---:|---:|---:|
| 1x | 1 s | 17 ms |
| 10x | 10 s | 167 ms |
| 100x | 1 min 40 s | 1.7 s |
| 1,000x | 16 min 40 s | 16.7 s |
| 10,000x | 2 h 47 m | 2.8 min |
| 100,000x | 1 d 3 h 47 m | 27.8 min |
| 1,000,000x | 11 d 13 h 47 m | 4.6 h |

Reference transit times at 1 g continuous thrust (see
[flight model](flight-model.md)):

| Route | Distance | Burn time | At 1,000x | At 100,000x |
|---|---:|---:|---:|---:|
| Earth–Moon | 0.0026 AU | 3.4 h | 12 s | 0.12 s |
| Earth–Mars | ~0.78 AU | 2.5 d | 3.6 min | 2.2 s |
| Earth–Jupiter | ~4.2 AU | 5.8 d | 8.4 min | 5.0 s |
| Earth–Neptune | ~29 AU | 15.3 d | 22 min | 13 s |

The high end is for crossing the outer system, not for docking. The prototype
lets the player change warp at any time with keys 1–7 and toggle pause with P.

**Automatic drop:** warp now drops by itself when the ship is close to a body,
when a planned burn is due, when a collision is possible, and on the run in to a
station's hold point. The policy is below; warping through a moon at 1,000,000×
is not a gameplay feature.

## Automatic warp drop

The player's setting is a **ceiling**; the flight policy may lower it. Three
ordinary limits set the most simulated time one frame may span. A station plan
adds two more, because a station is a massless point target the gravity field
never selects. The smallest wins:

| Rule | Limit | Measured at 60 fps |
|---|---|---|
| Clearance | 25% of the time to the nearest body's surface at the current relative speed | — |
| Local dynamics | 2% of `sqrt(r³/GM)` for the nearest body | Mars parking orbit: 23.5 s per frame, so 1,000× |
| Plan events | 5% of the time to the next arc boundary or the arrival | mid-cruise: 10,061 s per frame, so 100,000× |
| Station clearance | 25% of the time to the station's catalogue surface at the current relative speed | station plan only; the same quarter rule |
| Station hold | 5% of the time to the 500 m hold point | station plan only, released once the plan completes |

Measured on a real Ares Junction → Gateway plan, sampled a day before departure
so the event pacing is not binding: 200 km astern closing at 100 m/s caps a
frame at **99.8 s** where the gravity field alone allows 3,709 s; at the hold
point the cap is the **1 s floor**; 2 × 10⁹ m away neither station cap binds and
the event cap's 130,059 s wins. The station caps are released when the plan
completes, so the clock is not pinned after arrival.

Deep space, half an AU from Earth, allows 328,932 s per frame, which is above the
top scale. The cap is rounded down to the clock's power-of-ten steps, and the HUD
says `warp held by the flight policy` whenever it is binding. Pressing Go To also
raises the warp to the scale that makes the trip take about 30 real seconds.

The frame also has an **integration budget**: the ship may spend at most 2,000
RK4 substeps per frame. If it runs out, the clock is clamped to the date the ship
actually reached, so the world never runs ahead of the ship. In practice the warp
cap keeps the budget slack: the Mars acceptance flight peaked at 63 substeps in a
frame.

Warping through a moon at 1,000,000× is no longer possible, and neither is
arriving without seeing the final approach — or, at a station, the last few
hundred metres to its hold point.


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
