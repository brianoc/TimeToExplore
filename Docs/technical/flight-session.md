---
type: Architecture
title: The flight session
description: How pressing Plot route and Begin flies the ship in the scene, how warp is capped, and what the trajectory line and flight panel show.
tags: [technical, flight, time-warp, rendering, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-23T17:29:55Z }
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
  shaded — plus burn markers at each impulse or burn start, an arrival marker,
  a ghost at the target's arrival position, and a bottom-left route legend;
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
   state and date with the same `PlanFastest` minimum the preview used, so the
   flight flies the plan that was drawn. `FlightSession.Begin` discards the
   preview, so nothing stale is ever flown if the clock has moved.

Once a preview exists, the details footer grows a **trip-time slider**. Its
range is `PreviewMinimumSeconds` to `PreviewMaximumSeconds`, and its value is the
flight time in seconds. The **minimum is the planner's `PlanFastest`**: the
zero-coast burn-only crossing when it is the shorter feasible plan, otherwise
the coast floor. The upper end is still `PlanAtDuration` — three times the
minimum, or the slowest duration the planner still accepts when the cap itself
is refused. At the epoch the Gateway→Mars profile at 0.3 g shows **6.50 to 19.49
days**, from a burn-only minimum of 6.49501 days against the 6.73042-day coast
floor; Mercury's window is **3.04–9.13 days** (3.04470 against 3.21610), and
Jupiter's **12.54–37.63 days** at 0.3 g (12.54280). Uranus keeps the coast floor
as its minimum — the zero-coast crossing does not win there — so its window is
**33.60–100.81 days** at 0.3 g and **33.65–100.96 days** at 1 g. A 90-day Mars
build is outside that window; direct builds at both Uranus maxima are feasible
(eight arcs, **668.12 km/s** at 1 g and **674.01** at 0.3 g).

The **range between a burn-only minimum and the coast floor is a short-coast
regime**, not a hole. `PlanAtDuration` builds a duration the coast path refuses
in that range as the burn-only crossing with a heliocentric conic coast between
the departure burn and the brake: the requested total is an input, and the
coast, the burn's duration, the escape ray, and the brake's direction are the
solve ([the short coast](/simulation/burn-coast-burn.md#the-short-coast)). It is
not a Lambert coast, so the plan's shape is still break → departure burn →
conic coast → brake → capture. At the minimum the planner returns the burn-only
plan itself. Below the minimum the refusal is unchanged, and above the coast
floor the path is the unchanged Lambert profile. Measured at the epoch, Mercury
1 g's 3.10 d gap midpoint builds in **2,023 km/s** with a 0.5438-day coast and a
**3.7 m / 4.0 × 10⁻⁵ m/s** worst seam; Mars 0.3 g's 6.60 d builds in 1,333 km/s,
and Jupiter 0.3 g's 12.80 d in 2,540 km/s. The first selection now pays for the
short-coast seed as well as the floor and the moving-target minimum only when a
slider retime lands in the gap: the seed is the burn-only crossing, and the
short-coast Newton is a few seconds on the background worker. The editor's first
`Plot()` remains **2.81 s at Uranus 0.3 g, 3.08 s at 1 g, 5.07 s at Mars 0.3 g,
3.89 s at Mercury 1 g, and 4.61 s at Jupiter 0.3 g** in the warm test project.
A repeat plot at the same departure date, target, burn level, and arrival style
still reuses the cached plan and window; a changed departure date, or a
place/teleport, drops the cache. Every case still certifies on its **first**
certificate with no continuation probes, so the added plot time is the
moving-target minimum, not a longer floor search; .NET's `Plan()` is 0.21–0.38 s
for the same cases ([planning
performance](/technical/planning-performance.md#step-4-the-bracket-was-not-needed-2026-11-20)).

The slider does **not** block the frame. Moving the handle calls
`FlightSession.RequestPreviewDuration`, which enqueues the newest duration for
one background worker; the worker rebuilds the plan from the stored departure
state with `TripPlanner.PlanAtDuration`, using its own `TripPlanner` and
`GravityField` because the flight simulation's field keeps a prepared active set
and stays on the main thread. A request that lands on the window's floor is the
plan the window already solved, so it is handed to the publisher directly
instead of being rebuilt — a cold build is the profile's most expensive call,
and near a floor needle it can refuse the very duration the window advertises.
A request for a duration the session has already built is answered the same
way: every finished plan is cached by window and requested flight time (the
newest twelve per window, matched within 0.2% of the window so a continuous
handle put back near its old position still hits), so returning to a value the
player already waited for is a dictionary hit, not another multi-second build.
The cache is keyed and dropped with the window, so a changed departure date or
a place/teleport cannot read one back.
A plan that finishes after a newer request arrived is discarded and the newest
request is built next, so a drag never queues work behind stale plans. The
preview stays pinned to `PreviewJulianDate`, and the
published preview — path, arrival marker and ghost, panel flight time,
burn rows, and delta-v — stays on screen until the HUD's frame calls
`SolarSystemBootstrap.ApplyRetimeIfReady`, which hands the finished plan back on
the main thread. The handle and its label follow the player's ask rather than
the older published plan, so a drag never snaps back. Every published plan is
the full-field build; the fast search model is never shown.
`FlightSession.SetPreviewDuration` remains the synchronous, deterministic path
for tests and callers that need a plan immediately, and builds the same plan as
the worker for the same inputs.

The same worker builds **route plans**, not just retimes. `PlotAsync` plots a
route without blocking the main thread: the floor plan and the trip-time window
solve run on the worker with its own planner and gravity field, and the plan is
published with `TryApplyCompletedPreview`, which the HUD pumps once per frame
as part of `SolarSystemBootstrap.ApplyPreviewIfReady` — the route redraws when
the plan is ready, and the previous preview stays on screen until then. A
second plot replaces the first: the newest ask wins. The arrival-style and
burn-level changes go through the same pipeline — `SetArrivalStyle` and
`SetBurnLevel` enqueue a background rebuild of the
published preview (`RequestRebuild`), so the route updates off the main thread
as well, and the published plan is the same full-field build the synchronous
`Plot` produces.

The retime worker has a lifecycle, not just a queue. `FlightSession` is
`IDisposable`: `Dispose` stops the worker, drops the queue, and joins the thread
for up to five seconds, and the bootstrap disposes the session on teardown. A
disposed session refuses new requests (and refuses new preview builds with the
same refusal), and the worker checks the stop flag before publishing. The
synchronous path is still the equality reference: for the same duration the
worker and `SetPreviewDuration` build the identical plan.
[Planning performance](/technical/planning-performance.md) has the two-tier
model, the 5% screen, the cache, and the measurements.

Begin carries the chosen duration into the flight: it solves the fastest plan at
the current date with the planner's **`PlanFastest`** — the same burn-only
minimum or coast floor the preview's window advertises — then re-times it above
that minimum with `PlanAtDuration` when the preview asked for a longer trip.
That keeps Begin's "always replan" rule while making the flight match the plan
that was drawn. A preview that sat at its minimum flies the **fresh** minimum:
the clock may have moved since the plot, and re-timing to the old number by a
few seconds would ask for a duration just above the new minimum, where the
rebuilt plan carries the moving-target loop's own date tolerance. At a
coast-floor target the base is the coast floor; a chosen longer time still
retimes. A request inside the gap now builds the **short coast** rather than
reporting a refusal, so every duration the window offers is flyable; below the
burn-only minimum the planner still refuses with the arc that is over the
drive.

The preview is a promise of shape, not a contract. The clock keeps running,
warp may be raised, and the ship keeps moving, so the panel ages the preview:
after the clock advances `PreviewStaleFraction` (1%) of the planned flight time
since the plot, the status reads `Preview, stale` and a note says **replot or
begin anyway**. **Replot route** refreshes it on demand. There is no automatic
per-frame replanning.

The same footer exposes the **arrival style** as a two-button choice —
**Conic** (the default) and **X g burn** — plus a **burn-level** slider. Pressing
one calls `FlightSession.SetArrivalStyle`, which sets the planner mode, rebuilds
the preview from the pinned departure state, and resets the trip-time window to
the new style's fastest plan. The previous flight time is kept when it still
falls inside that window; a preview that sat **at** its minimum instead lands on
the new style's own minimum, because the two styles' burn-only totals can differ
by seconds and the old number would sit just above the new minimum's own date
tolerance. The zero-coast
minimum always ends on the conic capture chain, so the style's effect on the
drawn route shows on a retime above the floor. The active choice's label is gold
and its fill brighter; the route redraws with the coast, burn, and parking
orbit, and the flight panel gains the `Coast`, `Capture`, `Parking`, and
`Departure burn` / `Brake` rows. The hybrid adds a capture burn and its trim; the
conic style's capture is impulsive.

The burn-level slider is the level the profile's departure burn and brake are
planned at, and the hybrid capture's burn; there is one level control, not two.
Moving it calls `FlightSession.SetBurnLevel`, which rounds to 0.05 g and rebuilds
the preview from the same pinned state. Its floor is 0.05 g, so there is no
zero-thrust version of the profile. A live flight is never restyled: it keeps
the plan it was begun with, and a later replan uses the style and level
currently selected.

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
| Quintic (`Quintic`) | the profile's break out of the departure well, and the single capture of a trip that starts inside the target's sphere of influence | warm gold | solid — the drive is on |
| Conic (`ConicArc`) | the heliocentric coast, the planetocentric approach, the conic capture's transfer ellipse, and the parking orbit | sky blue | dashed — no thrust on the reference path |
| Fixed burn (`FiniteBurnArc`) | the profile's departure burn and brake, and the hybrid's long capture burn | red | solid — the drive is on |

There are no other path types in the project; those three are every
implementation of `IArcPath`. Line style therefore also says *whether the drive
is on*, and colour says *what kind of path flies*.

**A run of the same type takes different shades.** The planner often emits two
or three same-type arcs back to back: the profile's coast and approach are two
conics, its conic capture is another run (inbound, transfer ellipse, parking),
and the hybrid ends with a transfer conic followed by a parking conic.
`RouteColours.RunIndex`
counts the preceding arcs of the same type, and the run takes the base colour,
then a darker shade, then a paler shade; the hue never changes, so the arcs
still read as one family. A fixed burn between two conic runs starts the second
run again at the base shade.

Each line is a pooled `LineRenderer` with its own material, so a dashed arc
tiles its repeating alpha texture along its own length, with the dash length
tied to the line width so it stays readable at any zoom. Beyond the lines it
draws

* **burn markers** at the departure burn, the brake, and (on a hybrid arrival)
  the capture burn, plus each impulsive periapsis or circularisation;
* a **green marker at the arrival point**, on the parking orbit;
* a smaller pale-blue **ghost at the target's arrival position**, so the player
  sees where the planet will be when the route meets it. `TARGET AT ARRIVAL`
  labels it, and the ghost is drawn for a preview and a live flight alike.

A **route legend** sits in the bottom-left corner while a route is on screen: one
row per arc type present, with a swatch that carries both the colour and the
solid/dashed style, and the type label — *Quintic arc*, *Conic arc*, *Fixed
burn*. It is built from the trajectory line, so it always matches what is drawn:
the profile lists *Quintic arc*, *Fixed burn*, and *Conic arc* in the order they
first appear.
Consecutive same-type arcs share their row; the shade change is a within-family
cue, not a separate legend entry. The legend stacks above the flight panel, which
owns the corner during a route, and it hides with the HUD on `H`.

Line width follows the **camera distance**, not the path: `LineWidth` converts
3 screen pixels to render units at the camera's depth. Every vertex then gets
its own width through the `LineRenderer.widthCurve`, bounded to 2 px
(`LineWidth.DistanceFloorPixels`) at the far end and to the route's own 3 px at
the near end. The far end therefore stays readable instead of vanishing, and a
route that passes close to a zoomed-in camera stays a line instead of filling
the screen. The route is also clipped at the camera plane (`LineCameraClip`),
because a billboarded segment with one end behind the camera projects into a
huge polygon however thin its world width is. The earlier rule used the farthest
point of the plan, which made the line 225,000 km thick on a 1 AU trip and
swallowed the view once the camera could orbit; a later world-unit cap silently
faded the line to a third of a pixel at the outer zoom. The path is sampled from
the plan, not integrated, so it costs the same whether the ship is next to it or
an AU away.

# The flight panel

`FlightReport` builds the panel as plain `DetailRow` data, the same pattern as
`BodyDetails`, so its content is tested without a scene:

```
PLAN
  Target        Mars
  Departure     2026-11-15 00:00 · 4.8 d · break
  Cruise        2026-11-19 20:00 · 76 min · prograde burn
  Cruise        2026-11-19 21:16 · 85.0 d · drive off
  Approach      2027-02-12 21:00 · 6.0 h · drive off
  Capture       2027-02-13 03:00 · 1.7 h · brake
  Arrival       2027-02-13 20:00
  Flight time   90.0 d
  DRIVE
  Departure burn 13.4 km/s · 76 min · 0.3 g
  Coast         drive off · 85.0 d
  Approach      drive off · 6.0 h
  Brake         18.5 km/s · 1.7 h · 0.3 g
  Capture       3.5 km/s · 13.6 min
  Delta-v       44 km/s
  Fuel          ...
NOW
  Status        Under way
  Phase         Departure
  Next event    Cruise  T− 5.4 h
  Throttle      12% · 1.2 m/s²
  Warp          x10,000
```

Each phase row ends with what the drive is doing on that leg: `break`,
`powered`, `prograde burn`, `drive off`, `brake`, or `capture burn`.

The `Fuel` row is the first-pass readout: the rocket equation at 10,000 km/s
and the ship's 1,000 t dry mass, as tonnes and a share of the departure mass.
It is derived from the plan's delta-v, so it moves with the trip-time slider
and barely moves when the burn level changes at a fixed trip time. It is not a
budget — the tank is unlimited and the ship's mass does not change in flight
([ADR-016](/decisions/adr-016-fuel-readout.md)).

The header shows the target and the status, and the line under it is the
countdown to the next action (`Arrival  T− 12m 04s`). On arrival the status
reads `Arrived, in orbit` and the countdown reads `Arrived`. The conic capture
adds `Coast`, `Capture`, and `Parking` rows, and its next-event countdown lands
on the periapsis and circularisation boundaries.

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
| **Conic / X g burn** and the burn-level slider on a preview | Switch the arrival style or the burn level, rebuild the preview against it, and reset the trip-time window; the route shows the coast, burn, trim, and parking orbit, and the fixed style steps the throttle instead of blending. The level also drives the profile's two burns and its floor is 0.05 g |
| **Begin flight** | Replan from the current state and date at the chosen duration, then fly; enabled once a preview exists |
| **Replan …** (while flying) | Plan and fly a fresh route from the current state, as the old Go To did |
| **Jump (debug)** / `F` | Three-second eased debug teleport to the selected body; disabled in flight |
| `X` | Cancel the flight and discard any preview; the ship keeps its state |
| `` ` `` | Toggle debug free flight; cancels the flight and preview |
| `R` | Reset the ship to Gateway, discard the preview, and set warp to 1× |
| `1`–`7` | Requested warp; the policy may lower the request |
| `P` | Pause or resume |
| `Space` | Ease the camera home to the ship |
| `Esc` | Close the details screen and discard a preview / cancel a debug jump |
| Right-drag, wheel | Orbit and zoom |
| `W A S D Q E`, `,` `.` | Debug free flight only (move and change speed) |

# The camera

The ship sits at the render origin, so the camera **orbits** that origin and
looks at `ship + look-at offset` — the ship whenever the offset is zero
([ADR-008](/decisions/adr-008-orbit-camera.md),
[ADR-017](/decisions/adr-017-cursor-zoom-and-home.md)):

* right-drag turns yaw and pitch, clamped to ±85°;
* the wheel zooms 1.25× per notch toward the pointer, from 2,000 km out to
  12 billion km;
* a plan, a debug jump, or a reset frames its destination automatically, and the
  aim and the distance follow separately, so a manual zoom keeps the planet
  centred and only a drag stops the aiming;
* the framing distance is captured once when framing starts and re-captured on
  arrival, so the moving ship does not re-target the zoom every frame;
* `Space` eases back to the ship in one second and unwinds the look-at offset;
* the auto distance is `clamp(0.6 × ship→target, 3 × radius, 3.3 AU)`, so it
  opens up over a whole plan and closes to three planetary radii at arrival;
* the near plane scales with the orbit (2%, clamped to 200 m–50 km) so close
  inspection does not cut a planet open;
* a body passing close to the orbit slides the camera in front of it instead of
  stepping the view;
* a ten-pixel ship marker sits at the origin, pointing along the thrust while
  the drive is on and along the velocity when it is off;
* when the drive is on, a bright-green **thrust arrow** extends from the marker
  along the commanded acceleration. It is a fixed apparent length, so it shows
  direction rather than magnitude, and it disappears at zero thrust. During a
  retrograde brake it points visibly opposite the direction of travel, and
  against the route's gold/blue/red arcs its green is a separate reading.

The view deliberately does **not** follow the thrust axis: a capture burns
retrograde, and facing the burn would point the camera at empty space at the
moment of arrival. The flight is unaffected by any camera input: the plan is
still owned by the session.

# Acceptance, measured

Run through the scene's frame loop at 60 fps, without a scene
(`SceneFlightTests`):

| Quantity | Value |
|---|---|
| Departure | Gateway Station, 2026-11-15 00:00 UTC |
| Arrival | 2026-11-21 11:52:49 UTC (6.49501 days later) |
| Plan | the 0.3 g burn-only minimum: 6 arcs, no cruise coast, 1,592 km/s |
| Arrival error | 0.1 m, 0.0001 m/s |
| After three orbits | r = 3,898 km, e = 3.07 × 10⁻⁶, drive off |
| Real time | 108.7 s over 6,523 frames at the suggested 10,000× |
| Peak substeps in one frame | 17 of the 2,000 budget |
| Warp | x10,000 → x10, auto-limited |
| Teleports | none |

The budget never bound, but a starved-budget test with two substeps per frame
proves the clamp: 400 frames reach only 2026-11-15 04:43 and the clock is never
ahead of the ship.

The same flight also runs through the real composition root
(`BootstrapSmokeTests`): select Mars, plot, Begin, and let the bootstrap's own
`Update`/`LateUpdate` run. With one second per frame it arrives at the same
2026-11-21 11:52:49 UTC after 6,523 frames and 108.7 real seconds, with 0.1 m
and 0.0001 m/s and the 3,898 km parking orbit at e = 2.38 × 10⁻⁹. The shorter
`AProfileFlightArrivesThroughTheBootstrap` run (3,248 frames, 54.1 s) uses the
bootstrap's larger default frame step and reaches the same state at
e = 1.49 × 10⁻⁸.

# Verification

`FlightSessionTests` covers ownership, planning, the exact arrival, budget
clamping, cancel, teleport, a flight through a body, that planning leaves the
orrery untouched, and the preview: it never moves the ship, one replaces
another, Begin consumes it and replans at the current date, Cancel clears it,
the staleness window is one percent of the flight, the trip-time window is
`[minimum, 3 × minimum]` and clamps to it, the **minimum is the burn-only plan
with no cruise coast** while a retime above the coast floor still builds the
coasting profile, **a retime inside the gap builds the short coast** (Mercury
1 g at half the gap: 2,023 km/s, 7 arcs, seams inside the gate), retiming changes
the flight time
and arrival without moving the ship, Begin keeps the chosen time, and the preview
window is cached — a repeat plot at the same date returns the same plan, another
target or burn level misses, and a changed departure date invalidates the cache.
A retimed plan is cached by its requested duration too: retiming away and back
returns the same plan, a background ask for an already-built duration publishes
without starting the worker, a repeat within 0.2% of the window reuses it, and a
date change drops it.
Begin itself is pinned to the preview: at Mercury 1 g it flies the **3.044696 d
burn-only minimum** (break → departure burn → brake → capture, no cruise coast)
and the autopilot arrives 0.1 m and 0.0001 m/s from the plan's state, while at
Uranus it flies the **33.60438 d coast floor** because that is the shorter plan
there. A preview at its minimum flies the **fresh** minimum rather than its old
number, because the clock may have moved between plot and Begin; the real-HUD
`PressingBeginInTheRealHudFliesAndCancelClearsTheRoute` is the PlayMode guard,
and `BeginningAtTheMinimumAfterTheClockMovesFliesTheReplannedMinimum` pins the
EditMode side. The Mars anchors moved from the 6.73042 d coast floor to the
**6.49501 d burn-only minimum** (**1,592 km/s**, 6 arcs, 172.6 t of propellant).
It also covers the **background planner**: the second of two requests made while
the first is building is the plan that is published and the older build is
dropped, the worker's plan equals the synchronous plan for the same duration,
the previous preview stays published until the new plan is ready, a synchronous
retime supersedes an outstanding ask, discarding the preview drops one, and
Dispose joins the worker and refuses
new work. `SolarSystemHudTests` covers the arrival buttons and the profile
window: the two-button `ARRIVAL` row defaults to Conic, a switch rebuilds the
preview, the burn-level floor is 0.05 g, the trip-time label reads `burns · no
coast` on the zero-coast minimum and `burns · coast` after a retime, and the
slider runs from the burn-only minimum to three times it
(6.50–19.49 days at the epoch for Gateway→Mars, from the 6.49501-day minimum
against the 6.73042-day coast floor).
`TripPlannerTests` covers the pinned duration: exact flight time, a gentler burn
and lower delta-v at twice the minimum, and an infeasible report below it.
`BurnCoastBurnTripPlannerTests` covers the profile and the Lambert coast: the
six-unknown seam at a fixed date, the **moving-target loop** (a settled arrival
date that reproduces its own total, a stable total across calls, the closed seam,
and no cruise coast), and `PlanFastest` returning the burn-only plan at Mercury
1 g below the coast floor. It also covers the **short coast**: the three gap
midpoints build as break → departure burn → conic coast → brake → capture with
the requested total and every seam inside the gate, the family is continuous
from the minimum through the quartiles to the coast floor, and every slider
duration from the minimum to the floor and a little above it builds — the "no
hole" sweep at Mercury 1 g, Mars 0.3 g, and Jupiter 0.3 g. `LambertSolverTests`
covers the solver itself.
`WarpPolicyTests` covers the three caps and the scale rounding. `PlanPathTests` covers the arc boundaries. `FlightReportTests` covers the panel
rows, the preview and stale rows, and the countdown format. `OrbitCameraTests` covers the pitch
clamp, yaw wrap, zoom limits, the shortest turn, the slew rate, and the distance
easing. `CameraRigTests` covers the framing distance and the follow flags.
`SceneFlightTests` is the frame-loop acceptance flight. `BootstrapSmokeTests`
runs the real composition root — `Awake`, `Update`, `LateUpdate` — in edit mode,
including drawing the preview and its arrival ghost without flying, Begin
replanning at the current date, discard leaving the ship alone, the full
frame-loop acceptance (3,939 frames, 0.0 m and 0.0000 m/s, on the 3,898 km
parking orbit), and the burn–coast–burn profile plotted and flown to the conic
parking orbit through the same loop.

See [Testing and verification](/technical/testing.md) for the suite and the
numerical anchors, and [Navigation and arrival](/simulation/navigation-and-arrival.md)
for what the plan itself is.
