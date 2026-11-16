---
type: Design Document
title: The system map and route preview
description: The next camera and navigation layer — one continuous zoom from the ship to a top-down map of the system, click-to-select and pan on the map, and a plotted route the player can inspect before pressing Begin.
tags: [technical, ui, camera, navigation, design]
status: draft
generated: { by: opencode/qwen4-max, at: 2026-11-16T00:00:00Z }
---

# Why this is next

The orbit camera ([ADR-008](/decisions/adr-008-orbit-camera.md)) lets the player
look at the ship and the plan, but the view is always centred on the ship and
always at the player's angle. Three things are missing:

1. **Read the system as a map.** Zooming out today keeps the ship in the centre
   at an arbitrary angle. The player cannot see the orbital circles laid out
   from above, cannot pan to another part of the system, and cannot browse.
2. **Choose a destination from that map.** The map view is for selecting big
   things — the Sun, planets, stations — and opening their details.
3. **See the route before flying it.** **Go To** plans and departs in one press.
   The player never sees the curve before committing. The design wants a
   plotted route drawn as a smooth curve, a moment to look at it, and a
   **Begin** button.

This document is the design for that work. It is the next milestone
([roadmap](/production/roadmap.md), milestone 2.5). **Step 1, the plain-C#
map state, is implemented; everything visible is not yet.** The implementation
steps at the end are the task list, and each step carries its tests.

# One zoom, two readings of the same camera

The wheel keeps its current meaning: multiplicative zoom, 1.25× per notch.
There is no separate map key and no hard mode switch. Instead the camera
**blends** between two readings as the zoom distance crosses the local sphere
of influence:

* **Local view** — today's orbit camera around the ship: the player's yaw and
  pitch, the ship at the centre.
* **Map view** — top-down, looking along the ecliptic north, the orbital
  circles laid out as circles, panning enabled.

The blend weight comes from the camera distance `d` and a local scale `r`:

```text
r = SOI radius of the body that dominates the ship
    (the smallest SOI that contains the ship — the Moon's inside Earth's;
     fallback: 1 million km when the ship is in deep space)
x = clamp( ln(d / r) / ln(3), 0, 1 )
w = x² × (3 − 2x)                    // smoothstep, as FocusTransition uses
effective pitch = lerp(player pitch, 90°, w)
```

* Below `d = r` the view is exactly today's local view, `w = 0`.
* Above `d = 3r` the view is fully top-down, `w = 1`.
* In between, the pitch eases over with the zoom. The band is wide enough
  (a factor of 3) that a normal wheel gesture crosses it without flicker, so
  no hysteresis is needed.

At the start of the game the ship is at Gateway, 54,841 km from the Moon —
inside the Moon's 66,200 km SOI, which is itself inside Earth's. The dominant
body is therefore the **Moon**, and the map arrives at a camera distance of
about 200,000 km, with the Earth–Moon neighbourhood filling the view. Zooming
further keeps the map reading and opens out to the whole system.

**Zooming back in restores the old angle for free.** The player's yaw and
pitch are never overwritten; the map only blends the effective pose. When `w`
returns to 0 the camera is back at the exact remembered angle. This is the
behaviour the design asks for, and it falls out of the blend rather than
needing a saved-pose restore.

Two camera details the rig must handle:

* **The pole.** `OrbitCamera` clamps pitch to ±85° because
  `Quaternion.LookRotation(-offset, Vector3.up)` degenerates at 90°. The map
  pose bypasses the clamp and uses a fixed screen-up (ecliptic +Z projected)
  when `w` is near 1. Yaw still applies, so the map keeps the player's
  orientation rather than snapping north-up.
* **The far end.** The current zoom maximum, 5 billion km, cannot frame
  Neptune's orbit at 60° FOV: the view half-height there is 2.9 billion km
  against Neptune's 4.5 billion km radius. The map extends the maximum to
  **12 billion km (≈80 AU)**, which frames the whole catalogue with margin.
  The local range (2,000 km up) is unchanged.

The ship stays at the render origin ([ADR-002](/decisions/adr-002-double-precision.md)).
The map camera is just a large orbit offset, and panning adds a look-at
offset; the world is still projected from the ship in double precision before
the float cast. At map distances the float resolution of the offset is a few
hundred kilometres — coarse, but everything on screen is a marker, so it does
not matter. Local precision is untouched.

# Panning

* In the map (`w > 0.5`), **right-drag pans** instead of orbiting. The button
  keeps its "move the view" meaning; orbiting has no meaning when the pitch is
  locked top-down. Left click stays selection.
* The pan is a look-at offset in render units, capped at about 60 AU so the
  player cannot lose the system. The camera looks at `ship + pan`, not at the
  ship.
* Zooming back into the local band eases the pan to zero: the local view is
  always ship-centred, and the return trip lands on the ship, not on empty
  space where the player left the map.
* Clicking the ship marker recentres the map on the ship.

# What the map draws

The map is for selecting big things. Its rendering rules follow from that:

| Element | Rule |
|---|---|
| Orbit lines | Planets and moons as today (161-point Kepler ellipses). Asteroid orbits stay off; a belt map is later work. |
| Markers | The minimum-apparent-size aid stays, with the map minimum raised to about 6 px so every planet and station is a comfortable click target. |
| Labels | Sun, planets, and stations always. Moon labels appear when the view spans less than about 5× the parent's SOI — zoom into a neighbourhood to read its moons. Asteroids label on hover and selection only. |
| Ship | The ten-pixel marker, with a "you are here" label in the map. |
| Picking | The existing screen-space picker. At full-system zoom, ties resolve by class: Sun, planet, or station wins over a moon or asteroid behind it. Moons and asteroids become selectable as the view zooms into their neighbourhood, where the existing nearest-hit rule already does the right thing. |

The route preview (below) draws on the map with the same camera-distance width
rule as today, so a plotted curve stays about 1.5 px wide at any zoom.

# The local view stops inflating distant bodies

When the player is zoomed in on the ship, distant planets do not need to be
artificially large. Discovery is the map's job. So the marker aid becomes
local-aware:

* In the map (`w` near 1) the aid applies in full, as above.
* In the local view the aid applies to bodies inside the local neighbourhood
  (the dominant SOI) and fades to true scale beyond it. A planet an AU away
  renders as the sub-pixel dot it physically is; the player finds it on the
  map instead.
* The `T` true-scale toggle still overrides everything for inspection.

The fade is a pure function of camera distance, body distance, and `w`, so it
is testable without a scene, like the existing aid.

# Plot a route, then Begin

The details screen footer splits **Go To** into two steps:

1. **Plot route** — runs `TripPlanner.Plan` from the ship's current state at
   the current date, and holds the result as a **preview**. The preview owns
   nothing: `FlightSession` keeps flying or coasting exactly as before
   ([ADR-007](/decisions/adr-007-flight-director.md) is unchanged).
2. **Begin flight** — enabled once a preview exists. Replans from the current
   state at the current date and hands the new plan to the session: the same
   warp raise, warp policy, and flight path as today's Go To.

The preview draws through the existing `TrajectoryLine`: solid departure and
cruise, dashed capture, the orange flip marker, the green arrival marker. The
quintic arcs are already smooth curves; at map zoom they read as the route the
design wants. Add one navigation aid already on the list in
[Navigation and arrival](/simulation/navigation-and-arrival.md): a **ghost
marker at the target's arrival position**, so the player sees where the planet
will be, not where it is.

The flight panel shows the existing phase table with status **Preview**, plus
the flight time, delta-v, arrival date, and a `Plotted T−…` row.

## Time passes between Plot and Begin

The clock keeps running while the player looks at the preview — warp may be
on, and the ship keeps moving in its parking orbit. The preview therefore goes
stale. The design accepts this explicitly:

* **Begin always replans.** The planner is deterministic, and the departure
  state has only moved a little, so the flown curve is close to the preview.
  The preview is a promise of shape, not a contract.
* A **Replot** button refreshes the preview on demand. No automatic replanning
  per frame: the planner is cheap but not free.
* When the clock has advanced more than about 1% of the planned flight time
  since the plot, the panel flags the preview **stale** (tunable). The player
  can Replot or Begin anyway.

Only one preview exists at a time. Selecting another body replaces it;
`Esc` or **Close** discards it. While a flight is running, the preview footer
is replaced by the live flight panel, and the map stays usable: zoom out and
watch the ship cross the system along the active plan line.

# What this does not change

* **Precision** — the world stays ship-relative; the map is a camera pose, not
  a new coordinate space ([ADR-002](/decisions/adr-002-double-precision.md)).
* **Ship ownership** — a preview is data, not a flight; the session remains the
  one owner ([ADR-007](/decisions/adr-007-flight-director.md)).
* **The planner, autopilot, and warp policy** — untouched. Begin is today's
  Go To with a preview in front of it.
* **The debug jump** — stays for inspection.

ADR-008's "the camera always looks at the ship" is the one settled rule this
design bends: in the map the camera looks at `ship + pan`. Record that as
ADR-009 when the work lands, and update
[Selection and camera focus](/technical/selection-and-camera.md) and
[The flight session](/technical/flight-session.md) with it.

# Implementation steps

Each step is one session-sized piece of work with its tests, in order:

1. **`MapView` (plain C#) — done.** `Assets/Scripts/Player/MapView.cs` holds
   the blend weight from the camera distance and the local scale,
   the dominant-body choice (smallest containing SOI, deep-space fallback),
   the extended map range (2,000 km to 12 billion km), and the capped pan
   offset (60 AU). It reads the player's pose but never writes it, so the
   round trip is a pure blend. Tests: `MapViewTests` covers the endpoints and
   the smoothstep midpoint, the exact round trip, the Moon-inside-Earth choice
   at Gateway, the deep-space fallback, the pan cap, and the zoom clamp.
2. **Rig integration.** The top-down pose with a fixed up at the pole, input
  routing (right-drag orbits when local, pans when map), the near plane at map
  distances, the pan ease-out on re-entry. Extend `CameraRigTests`.
3. **Map drawing and picking.** The 6 px map marker minimum, the label density
  rule, class-priority picking at full-system zoom. Extend the picker tests.
4. **The local aid fade.** The neighbourhood falloff as a pure function;
  tests for full strength inside the SOI, true scale far outside, and the
  `T` override.
5. **Preview and Begin.** `FlightSession` holds a preview without taking
  ownership; Begin replans and flies; the staleness computation; the preview
  rows in `FlightReport`. Tests: a preview never moves the ship; Begin after
  a clock advance replans from the new state and still arrives exactly;
  cancel clears; one preview at a time.
6. **Frame-loop acceptance.** Extend `BootstrapSmokeTests`: zoom out to the
  map, select Mars, plot, Begin, arrive — through the real bootstrap.
7. **Docs.** ADR-009, the camera and flight-session concepts, the roadmap
  acceptance numbers, and the log.

# Acceptance

* One wheel gesture zooms from a parking orbit to the whole system; the view
  arrives top-down with every planet orbit visible. Zooming back in returns
  to the exact angle the player left.
* Pan to Jupiter, click it, read its details. Plot a route: a smooth curve
  draws across the map with the arrival ghost. Begin flies a curve close to
  the preview and arrives on the parking orbit with the existing exactness
  (0.0 m, 0.0000 m/s in the frame-loop test).
* In the local view, a body an AU away is a true-scale dot, not an inflated
  marker.

# Open questions

* **A map shortcut key (`M`)?** Zoom-only first; add the key if the wheel trip
  feels long in play.
* **Pure top-down or a slight tilt?** 90° first. If the map reads flat, end
  the blend at 80–85° instead; the blend makes this a one-number change.
* **Map rotation?** The map keeps the remembered yaw; there is no separate
  rotate control in the first pass.
* **Departure-time choice** (the porkchop search) stays out of scope. Plot
  means "leave now", as Go To does today.
* **The belt.** 100 asteroid orbits on the map would clutter it; a dedicated
  belt rendering pass remains future work
  ([Rendering at solar scale](/technical/rendering-scale.md)).