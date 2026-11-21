---
type: Glossary
title: Glossary
description: "The words the design bundle and the code share: camera views, thrust profiles, manoeuvre models, and arrival geometry."
tags: [reference, glossary, camera, propulsion, navigation, terminology]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
---

# Glossary

Terms used across the design bundle and the code. When a document and the code
disagree, the concept that owns the term wins; this page only points at it.

## Local view

The close camera around the ship, below the dominant body's sphere of influence.
The player's yaw and pitch are the angle, the ship sits at the render origin
([ADR-002](/decisions/adr-002-double-precision.md)), and the view is
ship-centred whenever the pan is zero. A wheel zoom anchors the point under the
pointer, so an off-centre zoom can leave another point at the centre; **Space**
eases the view home and unwinds the pan
([ADR-017](/decisions/adr-017-cursor-zoom-and-home.md)). Bodies outside the
local neighbourhood fade to their true-scale dots; the **T** toggle forces true
scale everywhere.

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
10 px coloured stroke without changing hue. In both views a line's far arc
holds a 2 px floor rather than thinning away into the distance.

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
than jumping there. The point under the pointer is anchored on the look-at
plane and the look-at is solved from it every frame, so the point stays under
the pointer while the distance and the map blend move. Because the blend weight
is derived from the camera distance, the solar system view's top-down tilt and
the rotation sweep with the same tween instead of snapping. Directly assigning
a distance (a reset, a snap-to, a test) places the camera immediately.

## Thrust and manoeuvre vocabulary

Five different questions hide under the word "thrust". Keep them apart:

| Axis | Question | Members |
|---|---|---|
| **Thrust profile** | What is the drive doing over time? | continuous (smooth), fixed (finite), zero (coast) |
| **Manoeuvre model** | How is the velocity change represented? | impulse, finite burn |
| **Path shape** | What is the arc? | quintic, conic, finite-burn arc |
| **Arrival choice** | How does the player end the target leg? | Quintic capture, Conic coast, Fixed-thrust arrival |
| **Cruise profile** | How does the ship cross between the spheres of influence? | Powered, Coast, Burn-coast-burn |

An **impulse** is a manoeuvre model, not a thrust profile. It is a Δv applied
at an instant; the ship cannot fly it. The conic style's burns are *modelled*
as impulses; the hybrid style integrates one real capture burn. The terms below
are the agreed vocabulary for the hybrid capture; the linked concepts own the
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
circularisation. A **Lambert coast** is the heliocentric middle chosen by
`LambertSolver` from two positions and a time of flight; it is what
**burn-and-coast** means in the flight model. A quintic with a flat middle is not
a coast.

### Fuel and propellant

**Exhaust velocity** — the effective `v_e` of the torch, **10,000 km/s** (about
3.3% of light speed). A balance value, not a modelled reactor;
`FuelModel.ExhaustVelocityMps`. See
[the flight model](/simulation/flight-model.md#the-fuel-readout).

**Propellant** — the reaction mass a burn spends, quoted in tonnes. The ship's
dry mass is 1,000 t by default (`ShipState.DefaultDryMassKg`).

**Mass ratio** — `m_0 / m_f = exp(delta-v / v_e)`, the factor by which the ship
mass changes over a delta-v. **Propellant fraction** — the propellant as a
share of the departure mass, `1 - 1 / mass ratio`. These are the columns in the
flight model's table.

**Fuel readout** — the flight panel's `Fuel` row: the tonnes a journey would
burn and that share of the departure mass. It is derived from the plan's
delta-v, not simulated: the ship has no finite tank and its mass does not change
in flight yet, so the row is a cost, not a budget
([ADR-016](/decisions/adr-016-fuel-readout.md)).

### Manoeuvre model

**Impulse** — an instantaneous velocity change, in m/s, applied at a point in
time. It has no duration and no thrust level, and it is not a burn. The plan
carries it as `PlanArc.EntryImpulseVectorMps`; the conic style's capture and
circularisation are impulses
([ADR-014](/decisions/adr-014-keplerian-coast.md)). The ship cannot fly one,
which is why the hybrid style flies the long capture as a real burn and keeps
only the trim and circularisation impulsive.

**Impulsive burn** — a burn *modelled* as an impulse. Say "modelled as an
impulse", not "the impulse", when the burn is the subject.

**Finite burn** — a burn with a real duration and a commanded level.
`FiniteBurnArc` integrates it through the two-body field. The hybrid style flies
its capture as one finite burn; the trim and circularisation stay impulses.

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

**Search model** — the fast, search-only gravity model inside the planner: each
arc's own central body in a body-relative frame. It chooses a duration and is
never a result. The break's fast peak is screened against the drive with
`BreakScreenMargin` (5%), and a probe inside that band is certified full-field.
See [planning performance](/technical/planning-performance.md).

**Full-field plan** — the plan the planner returns and the ship flies: rebuilt
and measured with the complete `GravityField` over the catalogue after the
search has chosen a duration. Its peak thrust, delta-v, clearance, and
feasibility are always full-field numbers.

### Arrival

**Arrival style** — the player's choice for how the target leg ends:
`CaptureStyle.Quintic` ("Quintic capture"), `CaptureStyle.Conic` ("Conic
coast"), and `CaptureStyle.FiniteBurn` ("X g burn", the hybrid). The code type
says capture; prose says arrival. Use "arrival style" in prose.

**Cruise profile** — the player's choice for how the ship crosses between the
spheres of influence: `CruiseStyle.Powered` (the default powered quintic),
`CruiseStyle.Coast` (the unpowered Lambert conic), and
`CruiseStyle.BurnCoastBurn` (the quintic break, departure burn, Lambert coast,
brake, and capture). The nav computer exposes it in the `PROFILE` row; an arrival
style is the separate capture choice. `0 g` is exactly the `Coast` profile.

**Break** — the quintic departure arc that leaves the parking orbit and reaches
the departure body's handover ceiling (`1.02 × SOI`, the band's ceiling) at the
local escape speed along the escape ray. The departure burn starts there and holds
the ray; the break's duration is solved against the drive's limit.

**Handover band** — the radial band a fixed burn hands over to a shaped end
inside: the floor `sqrt(μ / (0.4 a_drive))` clamped to the body clearance, and the
ceiling `1.02 × SOI`. The departure endpoint is the ceiling and the arrival
endpoint the floor; a band whose floor passes its ceiling is empty and the trip is
refused.

**Brake** — the finite burn at the end of a burn–coast–burn crossing that spends
the handover the unpowered coast would have made an impulse. Its thrust direction
is the change the chain needs, not anti-velocity.

**Capture** — the target-leg phase from sphere-of-influence entry to the parking
orbit. It is not one burn: the conic and hybrid styles contain an inbound coast,
a capture burn or burn-plus-trim, a transfer coast, a circularisation burn, and a
parking coast.

**Capture periapsis** — the low periapsis the capture aims for, where the conic
capture burn happens. The hybrid's burn ends at an **injection radius** near
this periapsis; it is not the parking radius, and the injection radius may be
raised.

**Transfer ellipse** — the coast from the capture periapsis (or the hybrid's
injection point) to the parking radius; its apoapsis is the parking radius.

**Circularisation** — the prograde burn at the transfer apoapsis that turns the
ellipse into the circular parking orbit.

**Parking orbit / parking radius** — the final circular orbit and its radius.
The quintic variant is lifted by the gravity ceiling; the conic and hybrid
variants use the natural radius. The hybrid does not raise it to pay for the
finite-burn loss; the terminal trim does that
([navigation and arrival](/simulation/navigation-and-arrival.md#the-parking-orbit)).

**Sphere of influence (SOI)** — the planning boundary where a cruise arc hands
over to a capture arc. It is not a dynamics boundary: the ship feels every
body's gravity at once.

### The hybrid capture

**Hybrid capture** — one fixed burn for the long part of the capture, then an
impulse trim for exactness. At zero burn level it degenerates to the conic
arrival.

**Burn level** — the player's fixed-thrust acceleration, from 0 g to 1 g,
clamped to the drive's thrust margin. It is a property of the `FiniteBurn`
arrival style and of the `BurnCoastBurn` profile's two burns, not a fourth
arrival style.

**Injection point** — the radius `r_i` where the fixed burn ends and the trim is
applied. It starts at the conic periapsis, must be below the parking radius and
clear of the body, and the solver may raise it when no safe burn reaches the
nominal one.

**Trim (terminal impulse)** — the impulse that corrects the fixed burn's end
state to the exact transfer ellipse. It is an impulse, so it is not itself
flyable; if every burn must be finite, it becomes a short finite trim and
accepts a residual or a targeting iteration.

**Burn split** — the finite Δv against the impulse Δv (trim plus
circularisation) in a hybrid. The flight panel's `Burns` row reports it.

**Finite-burn loss** — the umbrella for two distinct costs: the **Δv penalty**
of a finite burn against the same manoeuvre as an impulse, and the **periapsis
loss** during the burn. It is a like-for-like penalty, not a claim that the
finite style always costs more: the hybrid parks lower than the old `1.4 R`
finite style and spends less there (Jupiter 18.44 against 19.68), but it still
spends more than the pure impulse capture (Jupiter 17.40). The loss is the price
of flying the burn.

**Straddling periapsis** — centring a finite burn on periapsis so it starts
before and ends after. The first fixed style did this; the hybrid ends the burn
at the injection radius instead, and the trim cancels the radial velocity that
the early end leaves. The **impulse-at-midpoint approximation** treats a finite
burn as an impulse at its midpoint for a first estimate; it is an approximation,
not the solver's model.

**"Equivalent to the impulse"** — the same final orbit, not the same path,
timing, or Δv. Say "reaches the same final orbit" when that is what is meant.

## See also

* [Selection and camera focus](/technical/selection-and-camera.md) — the camera as built, and the debug jump.
* [The system map and route preview](/technical/system-map.md) — the blend, pan, and route preview.
* [ADR-008](/decisions/adr-008-orbit-camera.md) and [ADR-009](/decisions/adr-009-map-camera-pan.md) — why the camera is an orbit and how the map bends it.
* [Navigation and arrival](/simulation/navigation-and-arrival.md) — the capture geometry, parking orbit, and arrival styles as built.
* [The flight model](/simulation/flight-model.md) — the drive, throttle, and the two travel regimes.
* [ADR-006](/decisions/adr-006-quintic-plan.md), [ADR-014](/decisions/adr-014-keplerian-coast.md), and [ADR-015](/decisions/adr-015-fixed-thrust-burns.md) — the quintic, conic, and fixed-thrust decisions.
