---
type: Architecture
title: The flight session
description: How pressing Plot route and Begin flies the ship in the scene, how warp is capped, and what the trajectory line and flight panel show.
tags: [technical, flight, time-warp, rendering, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
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
* the plan is drawn in the world as one coloured line per arc — gold for a
  quintic, blue dashed for a conic, red for a fixed burn, with same-type runs
  shaded — plus burn markers at each impulse or burn start, a flip marker, an
  arrival marker, a ghost at the target's arrival position, and a bottom-left
  route legend;
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
range is `PreviewMinimumSeconds` to `PreviewMaximumSeconds`, and its value is the
flight time in seconds. For the powered plan the maximum is three times the
minimum; for a coasting profile it is the slowest duration the planner still
accepts, because a long short-way Lambert transfer is rejected once it sweeps
180° or its arrival no longer needs a brake. At the epoch the Gateway→Mars
burn–coast–burn window is **8.03 to 24.09 days**, from the profile's true floor
of 8.03 days at 0.3 g; the Mercury floor at 1 g is 3.46 days. A 90-day Mars build
is outside that window. The floor plan and the window are cached per departure
date, target, burn level, cruise style, and arrival style, so plotting the same
route again at the same date reuses both; a changed departure date, or a
place/teleport, drops the cache.

The slider does **not** block the frame. Moving the handle calls
`FlightSession.RequestPreviewDuration`, which enqueues the newest duration for
one background worker; the worker rebuilds the plan from the stored departure
state with `TripPlanner.PlanAtDuration`, using its own `TripPlanner` and
`GravityField` because the flight simulation's field keeps a prepared active set
and stays on the main thread. A plan that finishes after a newer request arrived
is discarded and the newest request is built next, so a drag never queues work
behind stale plans. The preview stays pinned to `PreviewJulianDate`, and the
published preview — path, flip, arrival marker and ghost, panel flight time,
peak thrust, and delta-v — stays on screen until the HUD's frame calls
`SolarSystemBootstrap.ApplyRetimeIfReady`, which hands the finished plan back on
the main thread. The handle and its label follow the player's ask rather than
the older published plan, so a drag never snaps back. Every published plan is
the full-field build; the fast search model is never shown.
`FlightSession.SetPreviewDuration` remains the synchronous, deterministic path
for tests and callers that need a plan immediately, and builds the same plan as
the worker for the same inputs.

The retime worker has a lifecycle, not just a queue. `FlightSession` is
`IDisposable`: `Dispose` stops the worker, drops the queue, and joins the thread
for up to five seconds, and the bootstrap disposes the session on teardown. A
disposed session refuses new requests, and the worker checks the stop flag
before publishing. The synchronous path is still the equality reference: for the
same duration the worker and `SetPreviewDuration` build the identical plan.
[Planning performance](/technical/planning-performance.md) has the two-tier
model, the 5% screen, the cache, and the measurements.

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

The same footer also exposes the **cruise profile** as a three-button `PROFILE`
row — **Powered** (the default), **Coast**, and **Burn-coast-burn** — and the
**arrival style** as a three-button choice between **Quintic**, **Conic**, and
**X g burn**, plus a **burn-level** slider. Pressing a profile button calls
`FlightSession.SetCruiseProfile`, which sets the planner's `CruiseStyle`,
rebuilds the preview from the pinned departure state, and resets the trip-time
window to the new profile's fastest plan; a coasting profile's right-hand end is
clamped to the durations its Lambert solve accepts. The previous flight time is
kept when it still falls inside that window. The burn-level slider is shown for
the hybrid arrival and for the burn–coast–burn profile, and it is the level both
of that profile's fixed burns are planned at — there is one level control, not
two. The style works the same way: pressing one calls
`FlightSession.SetArrivalStyle`, which sets the planner mode, rebuilds the
preview from the pinned departure state, and resets the trip-time window to the
new style's fastest plan. The active choice's label is gold and its fill
brighter; the route redraws with the coast, burn, and parking orbit, and the
flight panel gains the `Coast`, `Capture`, `Parking`, and `Burns` rows. The
hybrid `Burns` row reads the fixed and trim delta-v at the chosen level; the
conic style's reads `impulsive`. Moving the level slider calls
`FlightSession.SetBurnLevel`, which rounds to 0.05 g and rebuilds the fixed
preview (or the burn–coast–burn preview) from the same pinned state; at 0 g the
arrival is the conic plan and the cruise is the unpowered coast. A live flight is
never restyled: it keeps the plan it was begun with, and a later replan uses the
profile, style, and level currently selected.

Only one preview exists. A second plot replaces it; selecting another body,
`Esc`, **Close**, Cancel, teleport, and reset all discard it. `Begin` consumes
it. While a flight is running the live panel takes over and the route stays
usable.

# The plan in the world

`TrajectoryLine` samples the plan once with `PathTessellation`: each arc is
subdivided by curvature until every chord's midpoint deviates by at most
5 × 10⁻⁴ of the chord length, capped at 4,096 segments per arc. The polyline is
then re-projected into the ship-relative frame every frame. **Each arc is its
own line**, coloured by `RouteColours` from the **type of path** it carries, so
the player reads the physics rather than the flight phase:

| Arc type | Used for | Colour | Style |
|---|---|---|---|
| Quintic (`Quintic`) | departure, cruise, and the continuous quintic capture | warm gold | solid — the drive is on |
| Conic (`ConicArc`) | the conic coast, the transfer ellipse, and the parking orbit | sky blue | dashed — no thrust on the reference path |
| Fixed burn (`FiniteBurnArc`) | the hybrid's one long capture burn, and the burn–coast–burn profile's departure burn and brake | red | solid — the drive is on |

There are no other path types in the project; those three are every
implementation of `IArcPath`. Line style therefore also says *whether the drive
is on*, and colour says *what kind of path flies*.

**A run of the same type takes different shades.** The planner often emits two
or three same-type arcs back to back: the quintic arrival is three quintics, the
conic arrival is three conics (coast, transfer ellipse, parking), and the hybrid
ends with a transfer conic followed by a parking conic. `RouteColours.RunIndex`
counts the preceding arcs of the same type, and the run takes the base colour,
then a darker shade, then a paler shade; the hue never changes, so the arcs
still read as one family. A fixed burn between two conic runs starts the second
run again at the base shade.

Each line is a pooled `LineRenderer` with its own material, so a dashed arc
tiles its repeating alpha texture along its own length, with the dash length
tied to the line width so it stays readable at any zoom. Beyond the lines it
draws

* **burn markers** at the start of each impulsive arc (the conic periapsis
  capture and apoapsis circularisation) and at the hybrid's finite burn;
* an **orange marker at the flip** — the point where the along-track thrust
  changes sign, which is the top of the arc rather than a bang-bang switch. On
  the Mars plan it is 3.3 hours after departure;
* a **green marker at the arrival point**, on the parking orbit;
* a smaller pale-blue **ghost at the target's arrival position**, so the player
  sees where the planet will be when the route meets it. `TARGET AT ARRIVAL`
  labels it, and the ghost is drawn for a preview and a live flight alike.

A **route legend** sits in the bottom-left corner while a route is on screen: one
row per arc type present, with a swatch that carries both the colour and the
solid/dashed style, and the type label — *Quintic arc*, *Conic arc*, *Fixed
burn*. It is built from the trajectory line, so it always matches what is drawn:
the default plan is a single *Quintic arc* row, the conic arrival adds *Conic
arc*, the hybrid adds *Fixed burn*, and the burn–coast–burn profile lists
*Quintic arc*, *Fixed burn*, and *Conic arc* in the order they first appear.
Consecutive same-type arcs share their row; the shade change is a within-family
cue, not a separate legend entry. The legend stacks above the flight panel, which
owns the corner during a route, and it hides with the HUD on `H`.

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
  Departure     2026-11-15 00:00 · 6.6 h · break
  Cruise        2026-11-15 06:35 · 3.9 d · powered
  Capture       2026-11-19 05:25 · 5.1 h · powered
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

Each phase row ends with what the drive is doing on that leg: `break`,
`powered`, `prograde burn`, `drive off`, `brake`, or `capture burn`.

The header shows the target and the status, and the line under it is the
countdown to the next action (`Arrival  T− 12m 04s`). On arrival the status
reads `Arrived, in orbit`, the countdown reads `Arrived`, and the flip reads
`passed`. A conic arrival adds `Coast`, `Capture`, and `Parking` rows and a
`Burns` row with the impulsive total, and its next-event countdown lands on the
periapsis and circularisation boundaries.

A coasting plan has no torch peak to report, so the `Peak thrust` row is
replaced by a `DRIVE` summary in the same space:

```
DRIVE
  Departure burn  13.7 km/s · 1.3 h · 0.3 g
  Coast           drive off · 83.7 d
  Brake           19.1 km/s · 1.8 h · 0.3 g
  Capture         3.5 km/s · 1.4 d
```

The departure burn and the brake are separate rows with their own delta-v and
durations; the coast is explicitly `drive off`; and the brake is described by
what it spends rather than as an anti-velocity burn, because its thrust
direction is the change the chain needs. An unpowered coast also gains a
`Handover` row for the impulse at the target's sphere of influence. The live
throttle row reads `drive off` whenever the drive is not firing.

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
| **Trip-time slider** on a preview | Request a retime at a longer duration on the background worker; the handle and its label follow the drag, the previous full-field preview stays until the new plan lands, and the route, arrival, thrust, and delta-v then update without moving the ship |
| **Powered / Coast / Burn-coast-burn** on a preview | Switch the cruise profile, rebuild the preview against it, and reset the trip-time window to that profile's own feasible range; a coast's line and report read `drive off` |
| **Quintic / Conic / X g burn** and the burn-level slider on a preview | Switch the arrival style or the fixed-burn level, rebuild the preview against it, and reset the trip-time window; the route shows the coast, burn, trim, and parking orbit, and the fixed style steps the throttle instead of blending. The level also drives the burn–coast–burn profile's two burns |
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
the staleness window is one percent of the flight, the powered trip-time window
is `[minimum, 3 × minimum]` and clamps to it, retiming changes the flight time
and arrival without moving the ship, Begin keeps the chosen time, and the preview
window is cached — a repeat plot at the same date returns the same plan, another
target or burn level misses, and a changed departure date invalidates the cache.
It also covers the **background retime**: the second of two requests made while
the first is building is the plan that is published and the older build is
dropped, the worker's plan equals the synchronous plan for the same duration,
the previous preview stays published until the new plan is ready, a synchronous
retime supersedes an outstanding ask, discarding the preview drops one, and
Dispose joins the worker and refuses
new work. `SolarSystemHudTests` covers the profile control and the coasting
window: the
`PROFILE` row defaults to Powered, a switch rebuilds the preview, and the
burn–coast–burn slider ends at the slowest duration the planner accepts rather
than at three times its minimum (8.03–24.09 days at the epoch for Gateway→Mars,
from the 8.03-day true floor).
`TripPlannerTests` covers the pinned duration: exact flight time, a gentler burn
and lower delta-v at twice the minimum, and an infeasible report below it.
`CoastTripPlannerTests` and `BurnCoastBurnTripPlannerTests` cover the two Lambert
profiles, and `LambertSolverTests` covers the solver itself. `WarpPolicyTests`
covers the three caps and the scale rounding. `PlanPathTests` covers the arc
boundaries and the flip. `FlightReportTests` covers the panel rows, the preview
and stale rows, and the countdown format. `OrbitCameraTests` covers the pitch
clamp, yaw wrap, zoom limits, the shortest turn, the slew rate, and the distance
easing. `CameraRigTests` covers the framing distance and the follow flags.
`SceneFlightTests` is the frame-loop acceptance flight. `BootstrapSmokeTests`
runs the real composition root — `Awake`, `Update`, `LateUpdate` — in edit mode,
including drawing the preview and its arrival ghost without flying, Begin
replanning at the current date, discard leaving the ship alone, the full
map-to-arrival acceptance (3,939 frames, 0.0 m and 0.0000 m/s, on the 3,898 km
parking orbit), and the burn–coast–burn profile plotted and flown to the conic
parking orbit through the same loop.

See [Testing and verification](/technical/testing.md) for the suite and the
numerical anchors, and [Navigation and arrival](/simulation/navigation-and-arrival.md)
for what the plan itself is.
