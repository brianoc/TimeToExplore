---
type: Glossary
title: Glossary
description: "The words the design bundle and the code share: camera views, thrust profiles, manoeuvre models, and arrival geometry."
tags: [reference, glossary, camera, propulsion, navigation, terminology]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-18T00:00:00Z }
---

# Glossary

Terms used across the design bundle and the code. When a document and the code
disagree, the concept that owns the term wins; this page only points at it.

## Local view

The close camera around the ship, below the dominant body's sphere of influence.
The player's yaw and pitch are the angle, the ship sits at the render origin
([ADR-002](/decisions/adr-002-double-precision.md)), and the view is always
ship-centred. Bodies outside the local neighbourhood fade to their true-scale
dots; the **T** toggle forces true scale everywhere.

The local view is one end of the single zoom, not a mode: it is the camera when
`w = 0` in [the system map](/technical/system-map.md#one-zoom-two-readings-of-the-same-camera).

Related: **orbit camera** ([ADR-008](/decisions/adr-008-orbit-camera.md)) is the
mechanism; **local view** is what the player sees when it is close.

## Solar system view

The top-down reading of the same camera: as the zoom crosses the local sphere of
influence, the pose blends to looking straight down the ecliptic pole, left-drag
pans, and the range opens to 12 billion km. It is also called the **map view**
or **the map** in code and in the system-map concept. Markers keep a 6 px
minimum so every planet and station stays clickable, the ship is labelled
"YOU ARE HERE", and the planet orbit lines thicken from their local 3 px to a
10 px coloured stroke without changing hue.

It is not a second camera and not a coordinate space: the world stays
ship-relative and only the pose changes ([ADR-009](/decisions/adr-009-map-camera-pan.md)).
The solar system view is `w = 1`.

## Zoom blend (`w`)

The continuous weight between the two views, from the camera distance `d` and
the dominant body's sphere-of-influence radius `r`:

```text
x = clamp(ln(d / r) / ln(3), 0, 1)
w = x²(3 − 2x)
```

`w = 0` is the local view, `w = 1` the solar system view. The pitch and the
rotation are read through `w`, so no mode switch and no saved pose are needed.

## Dominant body and neighbourhood

The **dominant body** is the smallest sphere of influence that contains the ship
(the Moon inside the Earth, for example). Its SOI radius is the local scale `r`,
also called the **neighbourhood**. Deep space, outside every SOI, falls back to
1 million km.

## Zoom tween

A wheel notch **requests** a camera distance; the camera eases to it over a
short exponential tween (`OrbitCamera.EaseDistance`, 7/s by default) rather
than jumping there. Because the blend weight is derived from the camera
distance, the solar system view's top-down tilt and the rotation sweep with the
same tween instead of snapping. Directly assigning a distance (a reset, a
snap-to, a test) places the camera immediately.

## Thrust and manoeuvre vocabulary

Four different questions hide under the word "thrust". Keep them apart:

| Axis | Question | Members |
|---|---|---|
| **Thrust profile** | What is the drive doing over time? | continuous (smooth), fixed (finite), zero (coast) |
| **Manoeuvre model** | How is the velocity change represented? | impulse, finite burn |
| **Path shape** | What is the arc? | quintic, conic, finite-burn arc |
| **Arrival choice** | How does the player end the target leg? | Quintic capture, Conic coast, Fixed-thrust arrival |

An **impulse** is a manoeuvre model, not a thrust profile. It is a Δv applied
at an instant; the ship cannot fly it. The conic style's burns are *modelled*
as impulses; the finite-burn style integrates real burns. The terms below are
the agreed vocabulary for the hybrid capture; the linked concepts own the
mechanics and the measured numbers.

### Drive and command

**Drive (torch)** — the main engine. Design acceleration 10 m/s²
(`ShipState.DefaultMaxAccelerationMps2`), throttleable from zero to that level,
with a 3% thrust margin. "Torch" is the fiction; "drive" is the generic.

**Commanded acceleration** — the vector the autopilot asks the drive for, in
m/s². The code's `ThrustCommand` and the HUD's "thrust" are this quantity.
Thrust itself is force (N); with constant mass the two differ by a factor of
mass, and the implementation holds acceleration. See
[the flight model](/simulation/flight-model.md#what-the-throttle-is).

**Throttle** — commanded acceleration divided by the design acceleration, 0..1.
A fraction. Distinguish it from a **burn level**, which is absolute: 0.3 g is
2.94 m/s².

**Burn** — an interval with the drive on. Qualify it: **impulsive**, **finite**,
or **continuous**; **capture**, **circularisation**, **departure**, or **trim**.

**Coast** — an interval with the drive exactly off. A **coast arc** is a
Keplerian `ConicArc`; a **parking coast** is the short one after
circularisation; **burn-and-coast** is the deferred heliocentric regime. A
quintic with a flat middle is not a coast.

### Manoeuvre model

**Impulse** — an instantaneous velocity change, in m/s, applied at a point in
time. It has no duration and no thrust level, and it is not a burn. The plan
carries it as `PlanArc.EntryImpulseVectorMps`; the conic style's capture and
circularisation are impulses
([ADR-014](/decisions/adr-014-keplerian-coast.md)). The ship cannot fly one,
which is why the finite-burn style exists.

**Impulsive burn** — a burn *modelled* as an impulse. Say "modelled as an
impulse", not "the impulse", when the burn is the subject.

**Finite burn** — a burn with a real duration and a commanded level.
`FiniteBurnArc` integrates it through the two-body field. The fixed-thrust
style's two burns are finite.

**Continuous burn** — a burn whose level varies smoothly; the quintic's thrust
profile. It is still a burn when the level is low.

### Path and plan

**Quintic arc** — a fifth-order polynomial reference path, continuous in
position, velocity, and acceleration. The commanded thrust is `a_ref − g`, so
the throttle ramps. Fast, and limited by the gravity ceiling
([ADR-006](/decisions/adr-006-quintic-plan.md)).

**Conic arc** — a Keplerian two-body path. The reference acceleration is the
local gravity pointwise, so the commanded thrust is exactly zero on the
two-body reference; the autopilot only trims the third-body tide. `ConicArc`.

**Finite-burn arc** — an integrated constant-thrust path. `FiniteBurnArc`; the
throttle steps at the seam rather than blending
([ADR-015](/decisions/adr-015-fixed-thrust-burns.md)).

**Arc** — one `PlanArc`: a continuous path with one thrust law. **Phase** — a
`FlightPhase` label: Departure, Cruise, Coast, Capture, Parking. **Leg** —
informal, phase-level portion; an arrival leg can contain several arcs. Use
"arc" for the unit and "phase" for the label.

**Plan** — the ordered arc chain from the ship's state to the parking orbit.
"Delta-v" is the whole plan; "Burns" is the capture-plus-circularisation
subtotal, by whichever model the style uses.

### Arrival

**Arrival style** — the player's choice for how the target leg ends:
`CaptureStyle.Quintic` ("Quintic capture"), `CaptureStyle.Conic` ("Conic
coast"), and `CaptureStyle.FiniteBurn` ("0.3 g burn"). The code type says
capture; prose says arrival. Use "arrival style" in prose.

**Capture** — the target-leg phase from sphere-of-influence entry to the parking
orbit. It is not one burn: the conic and finite styles contain an inbound
coast, a capture burn, a transfer coast, a circularisation burn, and a parking
coast.

**Capture periapsis** — the low periapsis the capture aims for, where the
capture burn happens or straddles. Not the parking radius, and not the planned
injection point.

**Transfer ellipse** — the coast from the capture periapsis to the parking
radius; its apoapsis is the parking radius.

**Circularisation** — the prograde burn at the transfer apoapsis that turns the
ellipse into the circular parking orbit.

**Parking orbit / parking radius** — the final circular orbit and its radius.
The quintic variant is lifted by the gravity ceiling, the conic variant is
natural, and the finite-burn variant is higher to pay for the periapsis loss
([navigation and arrival](/simulation/navigation-and-arrival.md#the-parking-orbit)).

**Sphere of influence (SOI)** — the planning boundary where a cruise arc hands
over to a capture arc. It is not a dynamics boundary: the ship feels every
body's gravity at once.

### The hybrid capture (planned)

These terms belong to the next arrival style; they are agreed here so the
implementation does not invent a second vocabulary.

**Hybrid capture** — a fixed burn for the long part of the capture, then an
impulse for exactness. At zero burn level it degenerates to the conic arrival.

**Injection point** — the radius `r_i` where the fixed burn ends and the trim is
applied. It must be reachable, below the parking radius, and clear of the body.

**Trim (terminal impulse)** — the impulse that corrects the fixed burn's end
state to the exact transfer ellipse. It is an impulse, so it is not itself
flyable; if every burn must be finite, it becomes a short finite trim and
accepts a residual or a targeting iteration.

**Burn split** — the finite Δv against the impulse Δv in a hybrid.

**Finite-burn loss** — the umbrella for two distinct costs: the **Δv penalty**
of a finite burn against the same manoeuvre as an impulse, and the **periapsis
loss** during the burn. It is a like-for-like penalty, not a claim that the
finite style always costs more: the finite style parks higher and flies a
different geometry, and Saturn's measured finite capture (10.40 km/s) is below
the conic one (10.66).

**Straddling periapsis** — centring the finite burn on periapsis so it starts
before and ends after. The **impulse-at-midpoint approximation** treats the
finite burn as an impulse at its midpoint for a first estimate; it is an
approximation, not the solver's model.

**"Equivalent to the impulse"** — the same final orbit, not the same path,
timing, or Δv. Say "reaches the same final orbit" when that is what is meant.

## See also

* [Selection and camera focus](/technical/selection-and-camera.md) — the camera as built, and the debug jump.
* [The system map and route preview](/technical/system-map.md) — the blend, pan, and route preview.
* [ADR-008](/decisions/adr-008-orbit-camera.md) and [ADR-009](/decisions/adr-009-map-camera-pan.md) — why the camera is an orbit and how the map bends it.
* [Navigation and arrival](/simulation/navigation-and-arrival.md) — the capture geometry, parking orbit, and arrival styles as built.
* [The flight model](/simulation/flight-model.md) — the drive, throttle, and the two travel regimes.
* [ADR-006](/decisions/adr-006-quintic-plan.md), [ADR-014](/decisions/adr-014-keplerian-coast.md), and [ADR-015](/decisions/adr-015-fixed-thrust-burns.md) — the quintic, conic, and fixed-thrust decisions.
