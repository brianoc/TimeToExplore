---
type: Architecture
title: The flight session
description: How pressing Plot route and Begin flies the ship in the scene, how warp is capped, and what the trajectory line and flight panel show.
tags: [technical, flight, time-warp, rendering, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-16T00:00:00Z }
---

# What this adds to the orrery

Before milestone 2 the scene was an orrery with a debug camera. The ship was
three unrelated writes to one position field, and the flight core existed only
for tests. Now the scene has a ship:

* it starts at Gateway Station, so the default trip is the one the suite
  measures;
* **Plot route** on the details screen plans the trip and draws it as a
  preview; a **trip-time slider** then re-solves that preview at a longer
  duration, and **Begin flight** replans from the current state and flies the
  chosen time;
* the plan is drawn in the world, with coasts dashed, the parking coast green,
  the fixed burn solid, burn markers at each impulse or burn start, a flip
  marker, an arrival marker, and a ghost at the target's arrival position;
* the flight panel shows the phase table, the countdown, the throttle, the
  delta-v, and either the impulsive burn total or the hybrid's fixed-and-trim
  split;
* time warp drops by itself near a body and before each plan boundary, including
  the periapsis and circularisation boundaries of a conic arrival;
* after arrival the ship coasts in the parking orbit with the drive off, and the
  player gets the camera back — an orbit camera, not the old fixed view.

`FlightSession` is the single owner of the ship ([ADR-007](/decisions/adr-007-flight-director.md)).
The bootstrap writes no ship position; it only advances the clock and asks the
session to reach it.

# The frame

`SolarSystemBootstrap.Update` runs the same five steps every frame:

1. `Model.Update(Clock.JulianDate)` — the orrery at the frame's start.
2. `WarpPolicy.MaxFrameSeconds(...)` — how much simulated time one frame may
   span at this state, then `ClampScaleIndex` against the player's request.
3. `Clock.Advance(Time.deltaTime)` with the capped scale.
4. `FlightSession.Advance(Clock.JulianDate, SubstepsPerFrame)` — integrate the
   ship, split at plan arc boundaries, stopping on the arrival date.
5. If a flight is active and the session reached an earlier date than the clock
   asked for, the clock is **clamped back** to the session's date. Then
   `Model.Update` again.

Step 5 is the important one. The integrator has a per-frame substep budget
(2,000 by default). In cruise a 600-second step covers most of an arc; in a Mars
parking orbit the step is about 12 seconds, so a 16,700-second frame would need
roughly 1,400 substeps. When the budget runs out, the ship is left at the last
completed substep and the world follows it, so rendering and physics never
disagree and the next frame resumes from there.

The clamp applies whether a flight is integrating or an idle ship is coasting.
With no active plan the session coasts the ship under gravity with the drive
off, so the clock, the ship, and a plotted preview all advance together and the
orrery keeps moving while the player reads it. Begin then replans from the
current state and date.

The session also stops at the plan's arrival date even when the clock asks for
longer. That is what makes the arrival exact: the last leg's target is the
arrival instant itself, and the integrator snaps its final step to a requested
date.

# The warp policy

The player's 1–7 keys set a **ceiling**. Three physical limits may lower it, and
the smallest wins:

| Rule | Limit on one frame | Why |
|---|---|---|
| Clearance | 25% of the time to the nearest body's surface at the current relative speed | Do not cross a body between frames |
| Local dynamics | 2% of `sqrt(r³/GM)` for the nearest body | Keep an orbit a curve, not a jump |
| Plan events | 5% of the time to the next arc boundary or the arrival | Land the countdown on the event |

The result is rounded down to the clock's power-of-ten scales, so the HUD always
shows an honest scale. Measured caps at 60 fps:

| State | Cap on one frame | Allowed scale |
|---|---:|---:|
| Deep space, 0.5 AU from Earth | 328,932 s | 1,000,000× |
| Mid-cruise, 173,461 s to the boundary | 8,673 s | 100,000× |
| Mars parking orbit, 3,898 km | 23.5 s | 1,000× |
| Final second before arrival | 1.0 s (floor) | 10× |

Go To also **raises** the warp, to the scale that makes the trip take about 30
real seconds. Gateway→Mars is 4.44 days, so it starts at 10,000× (38 s) and the
policy takes over near Mars. The status panel says `warp held by the flight
policy · auto from x1,000,000` whenever the cap is binding.

# Plot route, then Begin

The details footer splits the old one-press **Go To** into two actions:

1. **Plot route** calls `FlightSession.Plot(targetId, Clock.JulianDate)`.
   The planner runs on a copy of the ship state with the requested date, and
   the result is held as `Preview`. It is data, not a flight: the ship, its
   `Status`, the clock, and the warp are untouched.
2. **Begin flight** calls `BeginGoTo`, which always replans from the current
   state and date. `FlightSession.Begin` discards the preview, so the flown
   curve is close to the preview but never stale.

Once a preview exists, the details footer grows a **trip-time slider**. Its
range is `PreviewMinimumSeconds` to `PreviewMaximumSeconds` (three times the
minimum), and its value is the flight time in seconds. A change calls
`FlightSession.SetPreviewDuration`, which rebuilds the plan from the stored
departure state with `TripPlanner.PlanAtDuration`: one build, no new minimum
solve, no ship movement, and no camera move. The preview stays pinned to
`PreviewJulianDate`, so dragging is stable even while the clock runs. The path,
flip, arrival marker and ghost, and the panel's flight time, peak thrust, and
delta-v all update; the arrival date slides and the target moves to meet it.

Begin carries the chosen duration into the flight: it solves the fastest plan at
the current date, then re-times it to the preview's flight time, clamped up to
the new minimum. That keeps Begin's "always replan" rule while honouring the
player's choice.

The preview is a promise of shape, not a contract. The clock keeps running,
warp may be raised, and the ship keeps moving, so the panel ages the preview:
after the clock advances `PreviewStaleFraction` (1%) of the planned flight time
since the plot, the status reads `Preview, stale` and a note says **replot or
begin anyway**. **Replot route** refreshes it on demand. There is no automatic
per-frame replanning.

The same footer also exposes the **arrival style**, as a three-button choice
between **Quintic**, **Conic**, and **X g burn**, plus a **burn-level** slider
shown for the fixed style. The style appears with the preview because it is a
property of the plan, not a global setting the player has to imagine: pressing
one calls `FlightSession.SetArrivalStyle`, which sets the planner mode, rebuilds
the preview from the pinned departure state, and resets the trip-time window to
the new style's fastest plan. The previous flight time is kept when it still
falls inside that window, so switching to a coast shows the same trip stretched
by the capture rather than silently resetting a slider the player had moved. The
active style's label is gold and its fill brighter; the route redraws with the
coast, burn, and parking orbit, and the flight panel gains the `Coast`,
`Capture`, `Parking`, and `Burns` rows. The hybrid `Burns` row reads the fixed
and trim delta-v at the chosen level; the conic style's reads `impulsive`.
Moving the level slider calls `FlightSession.SetBurnLevel`, which rounds to
0.05 g and rebuilds the fixed preview from the same pinned state; at 0 g the
preview is the conic plan. A live flight is never restyled: it keeps the plan it
was begun with, and a later replan uses the style and level currently selected.

Only one preview exists. A second plot replaces it; selecting another body,
`Esc`, **Close**, Cancel, teleport, and reset all discard it. `Begin` consumes
it. While a flight is running the live panel takes over and the route stays
usable.

# The plan in the world

`TrajectoryLine` samples the plan once with `PathTessellation`: each arc is
subdivided by curvature until every chord's midpoint deviates by at most
5 × 10⁻⁴ of the chord length, capped at 4,096 segments per arc. The polyline is
then re-projected into the ship-relative frame every frame. It draws

* a **solid** line for the departure and cruise arcs, and for the hybrid fixed
  burn;
* a **dashed** line for coast arcs and the quintic capture, using a repeating
  alpha texture tiled along the line, with the dash length tied to the line
  width so it stays readable at any zoom;
* a **green dashed** line for the short parking coast after circularisation;
* **burn markers** at the start of each impulsive arc (the conic periapsis
  capture and apoapsis circularisation) and at the hybrid's finite burn;
* an **orange marker at the flip** — the point where the along-track thrust
  changes sign, which is the top of the arc rather than a bang-bang switch. On
  the Mars plan it is 3.3 hours after departure;
* a **green marker at the arrival point**, on the parking orbit;
* a smaller pale-blue **ghost at the target's arrival position**, so the player
  sees where the planet will be when the route meets it. `TARGET AT ARRIVAL`
  labels it, and the ghost is drawn for a preview and a live flight alike.

Line width follows the **camera distance**, not the path: `LineWidth` converts
3 screen pixels to render units at the camera's depth, so the route stays a
fine line from the departure neighbourhood to the 12-billion-km map ceiling. The
earlier rule used the farthest point of the plan, which made the line
225,000 km thick on a 1 AU trip and swallowed the view once the camera could
orbit; a later world-unit cap silently faded the line to a third of a pixel at
full-map zoom. The path is sampled from the plan, not integrated, so it costs
the same whether the ship is next to it or an AU away.

# The flight panel

`FlightReport` builds the panel as plain `DetailRow` data, the same pattern as
`BodyDetails`, so its content is tested without a scene:

```
PLAN
  Target        Mars
  Departure     2026-11-15 00:00 · 6.6 h
  Cruise        2026-11-15 06:35 · 3.9 d
  Capture       2026-11-19 05:25 · 5.1 h
  Arrival       2026-11-19 10:29
  Flight time   4.4 d
  Peak thrust   9.76 m/s² (98%)
  Delta-v       2,345 km/s
NOW
  Status        Under way
  Phase         Departure
  Next event    Cruise  T− 5.4 h
  Throttle      48% · 4.8 m/s²
  Flip          T− 2.1 h
  Warp          x10,000
```

The header shows the target and the status, and the line under it is the
countdown to the next action (`Arrival  T− 12m 04s`). On arrival the status
reads `Arrived, in orbit`, the countdown reads `Arrived`, and the flip reads
`passed`. A conic arrival adds `Coast`, `Capture`, and `Parking` rows and a
`Burns` row with the impulsive total, and its next-event countdown lands on the
periapsis and circularisation boundaries.

A route preview uses the same plan rows with `PREVIEW` in place of `NOW`:

```
PREVIEW
  Status        Preview
  Plotted       T− 58m 12s
```

`Plotted` counts down the staleness window, not the flight: when it reaches
zero the status becomes `Preview, stale` and the note says **Preview stale ·
replot or begin anyway**. The header line counts down to the planned arrival.

# Input in the scene

| Input | Action |
|---|---|
| **Plot route** / **Replot route** on the details screen | Plan the route and draw the preview without flying; frames the target |
| **Trip-time slider** on a preview | Re-solve the preview at a longer duration; updates the route, arrival, thrust, and delta-v without moving the ship |
| **Quintic / Conic / X g burn** and the burn-level slider on a preview | Switch the arrival style or the fixed-burn level, rebuild the preview against it, and reset the trip-time window; the route shows the coast, burn, trim, and parking orbit, and the fixed style steps the throttle instead of blending |
| **Begin flight** | Replan from the current state and date at the chosen duration, then fly; enabled once a preview exists |
| **Replan …** (while flying) | Plan and fly a fresh route from the current state, as the old Go To did |
| **Jump (debug)** / `F` | Three-second eased debug teleport to the selected body; disabled in flight |
| `X` | Cancel the flight and discard any preview; the ship keeps its state |
| `` ` `` | Toggle debug free flight; cancels the flight and preview |
| `R` | Reset the ship to Gateway, discard the preview, and set warp to 1× |
| `1`–`7`, `Space` | Requested warp and pause; the policy may lower the request |
| `Esc` | Close the details screen and discard a preview / cancel a debug jump |
| Right-drag, left-drag, wheel | Orbit, pan the map, and zoom |
| `W A S D Q E`, `,` `.` | Debug free flight only (move and change speed) |

# The camera

The ship sits at the render origin, so the camera **orbits** that origin and
always looks back at it ([ADR-008](/decisions/adr-008-orbit-camera.md)):

* right-drag turns yaw and pitch, clamped to ±85°;
* the wheel zooms 1.25× per notch, from 2,000 km out to 5 billion km;
* a plan, a debug jump, or a reset frames its destination automatically, and the
  aim and the distance follow separately, so a wheel zoom keeps the planet
  centred and only a drag stops the aiming;
* the auto distance is `clamp(0.6 × ship→target, 3 × radius, 3.3 AU)`, so it
  opens up over a whole plan and closes to three planetary radii at arrival;
* the near plane scales with the orbit (2%, clamped to 200 km–50 km) so close
  inspection does not cut a planet open;
* a ten-pixel ship marker sits at the origin, pointing along the thrust while
  the drive is on and along the velocity when it is off.

The view deliberately does **not** follow the thrust axis: a capture burns
retrograde, and facing the burn would point the camera at empty space at the
moment of arrival.

With the map switch on, the same camera continues into the system map: the zoom
blends the pose top-down, left-drag pans the map, the look-at
becomes `ship + pan`, and the wheel reaches 12 billion km
([The system map](/technical/system-map.md),
[ADR-009](/decisions/adr-009-map-camera-pan.md)). The flight is unaffected: the
plan is still owned by the session, and a flight running while the player browses
the map is the same flight with a different camera.

# Acceptance, measured

Run through the scene's frame loop at 60 fps, without a scene
(`SceneFlightTests`):

| Quantity | Value |
|---|---|
| Departure | Gateway Station, 2026-11-15 00:00 UTC |
| Arrival | 2026-11-19 10:29:02 UTC (4.44 days later) |
| Arrival error | 0.0 m, 0.0000 m/s |
| After three orbits | r = 3,898 km, e = 3.07 × 10⁻⁶, drive off |
| Real time | 65.7 s over 3,939 frames at the suggested 10,000× |
| Peak substeps in one frame | 63 of the 2,000 budget |
| Warp | x10,000 → x10, auto-limited |
| Teleports | none |

The budget never bound, but a starved-budget test with two substeps per frame
proves the clamp: 400 frames reach only 2026-11-15 05:53 and the clock is never
ahead of the ship.

The same flight also runs through the real composition root, starting from the
system map (`BootstrapSmokeTests`): select Mars on the map, plot, Begin, and let
the bootstrap's own `Update`/`LateUpdate` run. It arrives at the same
2026-11-19 10:29:02 UTC after 3,939 frames and 65.7 real seconds, with 0.0 m and
0.0000 m/s and the 3,898 km parking orbit at e = 4.3 × 10⁻⁹. The map changes the
camera, not the trajectory.

# Verification

`FlightSessionTests` covers ownership, planning, the exact arrival, budget
clamping, cancel, teleport, a flight through a body, that planning leaves the
orrery untouched, and the preview: it never moves the ship, one replaces
another, Begin consumes it and replans at the current date, Cancel clears it,
the staleness window is one percent of the flight, the trip-time window is
`[minimum, 3 × minimum]`, retiming changes the flight time and arrival without
moving the ship, and Begin keeps the chosen time. `TripPlannerTests` covers the
pinned duration: exact flight time, a gentler burn and lower delta-v at twice
the minimum, and an infeasible report below it. `WarpPolicyTests` covers
the three caps and the scale rounding. `PlanPathTests` covers the arc boundaries
and the flip. `FlightReportTests` covers the panel rows, the preview and stale
rows, and the countdown format. `OrbitCameraTests` covers the pitch clamp, yaw
wrap, zoom limits, the shortest turn, the slew rate, and the distance easing.
`CameraRigTests` covers the framing distance and the follow flags.
`SceneFlightTests` is the frame-loop acceptance flight. `BootstrapSmokeTests`
runs the real composition root — `Awake`, `Update`, `LateUpdate` — in edit mode,
including drawing the preview and its arrival ghost without flying, Begin
replanning at the current date, discard leaving the ship alone, and the full
map-to-arrival acceptance (3,939 frames, 0.0 m and 0.0000 m/s, on the 3,898 km
parking orbit).

See [Testing and verification](/technical/testing.md) for the suite and the
numerical anchors, and [Navigation and arrival](/simulation/navigation-and-arrival.md)
for what the plan itself is.
