---
type: Architecture
title: The flight session
description: How pressing Plot route and Begin flies the ship in the scene, how warp is capped, and what the trajectory line and flight panel show.
tags: [technical, flight, time-warp, rendering, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T11:00:00Z }
---

# What this adds to the orrery

Before milestone 2 the scene was an orrery with a debug camera. The ship was
three unrelated writes to one position field, and the flight core existed only
for tests. Now the scene has a ship:

* it starts at Gateway Station, so the default trip is the one the suite
  measures;
* **Plot route** on the details screen plans the trip and draws it as a
  preview, and selecting a body warms that plan early without showing it, so
  the Plot click only has to draw it; a **trip-time slider** then re-solves
  that preview at a longer duration, and **Begin flight** flies the drawn plan
  while it is fresh and replans once it is stale;
* the plan is drawn in the world as one coloured line per arc — gold for a
  quintic, blue dashed for a conic, red for a fixed burn, with same-type runs
  shaded — plus burn markers at each impulse or burn start, an arrival marker,
  a ghost at the target's arrival position, and a bottom-left route legend;
* the flight panel shows the phase table, the countdown, the throttle, the
  delta-v, and either the impulsive burn total or the hybrid's fixed-and-trim
  split;
* time warp drops by itself near a body and before each plan boundary, including
  the periapsis and circularisation boundaries of a conic arrival, and on the
  run in to a station's hold point;
* after arrival the ship coasts in the parking orbit with the drive off — or
  holds at a station's 500 m hold point with its velocity matched — and the
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
orrery keeps moving while the player reads it. Begin flies the preview from its
plotted departure while the preview is fresh, and replans from the current state
and date once it is stale.

The session also stops at the plan's arrival date even when the clock asks for
longer. That is what makes the arrival exact: the last leg's target is the
arrival instant itself, and the integrator snaps its final step to a requested
date.

# The warp policy

The player's 1–7 keys set a **ceiling**. Three ordinary physical limits may lower
it, and a station plan adds two more; the smallest wins:

| Rule | Limit on one frame | Why |
|---|---|---|
| Clearance | 25% of the time to the nearest body's surface at the current relative speed | Do not cross a body between frames |
| Local dynamics | 2% of `sqrt(r³/GM)` for the nearest body | Keep an orbit a curve, not a jump |
| Plan events | 5% of the time to the next arc boundary or the arrival | Land the countdown on the event |
| Station clearance | 25% of the time to the station's catalogue surface at the current relative speed | A station has no `Gm`, so the gravity field never makes it the nearest body |
| Station hold | 5% of the time to the 500 m hold point | Do not jump over the last few hundred metres |

The result is rounded down to the clock's power-of-ten scales, so the HUD always
shows an honest scale. Measured caps at 60 fps:

| State | Cap on one frame | Allowed scale |
|---|---:|---:|
| Deep space, 0.5 AU from Earth | 328,932 s | 1,000,000× |
| Mid-cruise, 201,223 s to the boundary | 10,061 s | 100,000× |
| Mars parking orbit, 3,898 km | 23.5 s | 1,000× |
| Station approach, 200 km astern closing at 100 m/s | 99.8 s (field alone: 3,709 s) | 1,000× |
| At the station's hold point | 1.0 s (floor) | 10× |
| Final second before arrival | 1.0 s (floor) | 10× |

The station caps are measured on a real Ares Junction → Gateway plan, sampled a
day before departure so the event pacing is not binding; 2 × 10⁹ m away neither
of them binds and the event cap's 130,059 s wins. They are released once the plan
completes, so the clock is not pinned at the hold point after arrival.

Go To also **raises** the warp, to the largest scale the trip can still be
watched at: Gateway→Mars is about 7.17 days, so it starts at 10,000× (about
62 s at that scale; the measured flight takes 131.8 s as the policy slows near
Mars). The status panel says `warp held by the flight policy · auto from
x1,000,000` whenever the cap is binding.

# Plot route, then Begin

**Selecting a body warms its plan and shows nothing.** The click (or the target
cycle) starts the solve in the background at once, so the first — and only slow
— one runs while the player reads the details screen. The result is held as a
**prime**: not `Preview`, so no route is drawn, the flight panel stays hidden,
the trip-time slider never appears, and Begin stays disabled. Nothing about the
screen says a plan exists. Selecting the same body again keeps the prime or the
preview that is already published.

The details footer splits the old one-press **Go To** into two actions:

1. **Plot route** calls `SolarSystemBootstrap.PlotRoute`, which asks the session
   for the plan and takes the camera to the finished route. It is data, not a
   flight: the ship, its `Status`, the clock, and the warp are untouched. The
   click **adopts the prime** when one is ready, so the route and its report
   appear in the click's own frame; a prime still building is continued, not
   restarted, so the click only adds the camera move. A prime is refused, and a
   fresh plot built instead, when it no longer fits the controls — another
   target, an infeasible plan, a changed arrival style or drive, or a departure
   the clock has moved past the freshness window. If a fresh preview already
   sits at the planner's fastest trip, the click frames it without replanning.
2. **Begin flight** calls `BeginGoTo`. Inside the preview's own freshness
   window it flies the plan that was drawn, from the departure it was certified
   at, so the flight and the arrival date are exactly what the route and the
   countdown showed. Past that window — `PreviewStaleFraction` of the flight
   time — it replans from the current state and date with the same
   `PlanFastest` minimum the preview used, so nothing stale is ever flown.

Once a preview exists, the details footer grows a **trip-time slider**. Its
handle is one of **64 integer steps**, mapped linearly onto
`PreviewMinimumSeconds` to `PreviewMaximumSeconds`, so each position is a
distinct flight time rather than a continuous value. A step is about a quarter of
a day on the Mars window below, which reads as a smooth drag and puts exactly
one trip in each slot of the retimed-plan cache. The **minimum is the planner's
`PlanFastest`**: the **full-blast** zero-coast crossing when it is shorter,
otherwise the coast floor ([ADR-025](/decisions/adr-025-full-blast-route.md)).
At the epoch the minimum is **6.66965 d / 1,538.1 km/s** to Mars at 0.3 g,
**3.22822 / 2,408.2** to Mercury at 1 g, **12.71122 / 3,053.5** to Jupiter at
0.3 g, and the coast floor **28.18209 / 37.00540 d** to Uranus at 1 g / 0.3 g.
The upper end is still `PlanAtDuration` — three times the **coast** floor, or the
slowest duration the planner still accepts when the cap itself is refused. At
the epoch the windows are **6.67–21.50 days** to Mars at 0.3 g, 3.23–11.03 to
Mercury at 1 g, 12.71–40.75 to Jupiter at 0.3 g, 28.18–84.55 to Uranus at 1 g
and 37.01–111.02 at 0.3 g. The parabolic burn-only crossings remain longer than
their floors (16.48669 days to Mars, 13.86287 to Mercury and 22.55555 to
Jupiter); the full-blast route is the time-optimal shape, not the parabolic one.
A 90-day Mars build is outside that window and the cap.

The **short-coast branch is active.** It fills the range between the full-blast
minimum and the coast floor: at Mercury 1 g, Mars 0.3 g and Jupiter 0.3 g every
duration from the minimum to the floor is a short coast, and the branch retries
the full-blast excesses when the split's climb is too long for the request. Above
the floor the ordinary Lambert profile answers, and below the minimum the coast
path keeps its refusal. The first selection pays for the coast floor, the five
probe candidates and the certified winner; a slider retime into the short-coast
range pays for its own build. A repeat plot at the same departure date, target,
burn level, and arrival style still reuses the cached plan and window; a changed
departure date, or a place/teleport, drops the cache. The plot cost of the grid
is measured in [planning performance](/technical/planning-performance.md); a
probe candidate is a two-body seam solve and the winner alone is rebuilt
certified, so the grid is not five certified crossings.

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
newest sixty-four per window, matched within 0.2% of the window so the small
difference between a requested step and the plan's own total still hits), so
returning to a step the player already waited for is a dictionary hit, not
another multi-second build. A step is about 1.6% of the window, so a
neighbouring step is a different trip and a new build; the 64 slots hold the 64
steps. Each entry keeps the brake/departure pin its plan settled on, and a new
build starts from the nearest entry's pin (or the window plan's) instead of the
kinematic estimate — **6 pin passes against a cold build's 12** for the same
duration, with the same plan. The two lowest steps are not seeded, because that
is where the burn-only crossing and the short coast meet the coast profile
([warm-starting the pin](/technical/planning-performance.md#step-3-warm-starting-the-pin-2026-11-24)).
The session also **fills the neighbouring steps** on the same worker while the
preview is up: two each side of a published plot and three in the direction the
handle is travelling. Prefetch is the worker's **lowest priority** — a retime, a
rebuild, or a plot clears the queue and is taken first — it never publishes and
never moves the handle, and it is dropped on a discard, a style or burn-level
change, or a departure-date change. It builds from the preview's frozen departure
state and the burn level and arrival style captured when the preview was plotted,
seeded from the nearest cached plan, so a filled slot is the plan the retime
would have built ([prefetching the neighbours](/technical/planning-performance.md#step-4-prefetching-the-sliders-neighbours-2026-11-25)).
The first two steps after a plot publish in a frame or two, and the worst step of
a drag falls from about a second to about 0.4 s; a step the prefetch has not
reached is still one build, because a build is as long as the handle takes to
produce the next step.
A plan costs about **50 KB** — the departure burn's and brake's 512-sample
interpolation tables dominate, at 24 bytes a sample — so the bound is about
3 MB per window. The cache is keyed and dropped with the window, so a changed
departure date or a place/teleport cannot read one back.
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
the plan is ready, and the previous preview stays on screen until then. A second
plot to another target replaces the first: the newest ask wins. A second plot to
the target already queued or building is **the same ask**, so it is continued
and `IsPlotPending` reports the wait.

**A prime is the same build with nowhere to publish.** `PrimeAsync` solves the
window a plot would and leaves the result in `_primedPreview`, which no panel,
the trajectory line, or `Begin` reads; `TryAdoptPrimedPreview` turns it into the
preview on the frame the Plot click asks for it. The two share one request and
one generation, so a plot supersedes a prime and a prime is never adopted across
a newer request. A prime failure is dropped silently: the player asked for
nothing yet. The arrival-style and
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

Begin **flies the drawn plan while it is fresh**. The rule is the session's own
staleness window: while the clock has not advanced `PreviewStaleFraction` (1%) of
the planned flight time past `PreviewJulianDate`, `FlightSession.Begin` takes the
preview as the flight. It adopts the preview's departure state — the ship returns
to the state the plan was certified from — so the plan, its delta-v, and its
arrival date are the ones the route and the countdown already show. There is no
second plot: a Mars minimum plotted in 4.3 s begins in **0 ms** on the benchmark's
instrument.

The date window is the deadband, and it is deliberately not an exact date match:
an exact match would throw the drawing away on the next frame. There is no
separate position deadband, because the only way an idle ship moves between plot
and Begin is the coast that same window bounds, and every other mover — a place,
a teleport, a cancel, an arrival-style or burn-level change — discards or
rebuilds the preview. A guard fails the reuse when the preview is for another
target or is infeasible, when a rebuild or a slider retime has not been
published, or when the planner's drive no longer matches the plan's; Begin then
replans exactly as it used to. The clock is not rewound: the flight starts at the
plotted departure, and the first `Advance` carries it forward to the current
date, at most 1% of the trip.

Past the window Begin **replans** at the current state and date. Its base plan is
`PlanFastest` — the coast floor the preview's window advertises, or the
burn-only crossing on a route where that is shorter — and it re-times above that
minimum with `PlanAtDuration` when the preview asked for a longer trip. A stale
preview that sat at its minimum flies the **fresh** minimum: the clock may have
moved, and the old number can sit a few seconds above the new floor. Most
measured planet routes' minimum is the full-blast crossing, so a Begin from a
stale preview flies the no-coast plan; Uranus at 0.3 g keeps the coast floor.
The status already reads `Preview, stale` and the note says **replot or begin
anyway**, so the replan is what the panel promised. A request below the minimum
is refused with the arc that is over the drive; the first hours above it are the
short-coast range, and above the coast floor every duration builds the coast
path.

The preview is a contract while it is fresh — Begin flies it exactly — and a
promise of shape once it is stale. The clock keeps running, warp may be raised,
and the ship keeps moving, so the panel ages the preview: after the clock
advances `PreviewStaleFraction` (1%) of the planned flight time since the plot,
the status reads `Preview, stale` and a note says **replot or begin anyway**.
**Replot route** refreshes it on demand. There is no automatic per-frame
replanning.

The same footer exposes the **arrival style** as a two-button choice —
**Conic** (the default) and **X g burn** — plus a **burn-level** slider. Pressing
one calls `FlightSession.SetArrivalStyle`, which sets the planner mode, rebuilds
the preview from the pinned departure state, and resets the trip-time window to
the new style's fastest plan. The previous flight time is kept when it still
falls inside that window; a preview that sat **at** its minimum instead lands on
the new style's own minimum, because the two styles' floors can differ by
seconds and the old number would sit just above the new minimum's own date
tolerance. The minimum is a full-blast plan on most routes, so the selected
style's capture chain is on the drawn route even at the floor; the zero-coast
crossing always ends on the conic capture chain, and the coast floor keeps
whichever chain the style builds. The active choice's label is gold
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

Only one preview exists. A second plot of the same body continues the plan in
flight; a plot of another body replaces it, and selecting another body, `Esc`,
**Close**, Cancel, teleport, and reset all discard it. A **prime** is not a
preview and never appears in the panel: it is a finished plan waiting for the
click that asks for it, and a newer request or a discard drops it. `Begin`
consumes the preview. While a flight is running the live panel takes over and
the route stays usable.

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
| Fixed burn (`FiniteBurnArc`) | the profile's departure burn and brake, a station's terminal velocity match, and the hybrid's long capture burn | red | solid — the drive is on |

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
tiles its repeating alpha texture along the path in world units, with the dash
period fixed at eight line widths at the route's anchor depth. The period is
independent of the arc's length, so a long coast and a short parking arc draw
the same dots, and the anchor end stays readable at any zoom. Beyond the lines it
draws

* **burn markers** at the departure burn, the brake, and (on a hybrid arrival)
  the capture burn, plus each impulsive periapsis or circularisation;
* a **green marker at the arrival point**, on the parking orbit or at a station's
  hold point;
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
  Departure     2026-11-15 00:00 · 15 min · break
  Escape        2026-11-15 00:15 · 10.7 d · drive off
  Cruise        2026-11-25 18:02 · 79 min · prograde burn
  Cruise        2026-11-25 19:21 · 78.8 d · drive off
  Approach      2027-02-12 15:18 · 5.6 h · drive off
  Capture       2027-02-12 20:56 · 1.9 h · brake
  Arrival       2027-02-13 00:00
  Flight time   90.0 d
  DRIVE
  Break         1.4 km/s · 928 s · quintic
  Escape        drive off · 10.7 d
  Departure burn 14.0 km/s · 79 min · 0.3 g
  Coast         drive off · 78.8 d
  Approach      drive off · 5.6 h
  Brake         19.7 km/s · 1.9 h · 0.3 g
  Capture       3.5 km/s · 1.2 h
  Delta-v       39 km/s
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
reads `Arrived, in orbit` over a planet and `Arrived, at the hold point` at a
station, and the countdown reads `Arrived`. The conic capture
adds `Coast`, `Capture`, and `Parking` rows, and its next-event countdown lands
on the periapsis and circularisation boundaries. A station plan has no capture
chain: its terminal phase and drive rows read **Match**, the next event reads
`Match  T− …` before it, and the status line above reads `… · at the hold
point`.

A coasting plan has no torch peak to report, so the `Peak thrust` row is
replaced by a `DRIVE` summary in the same space:

```
DRIVE
  Departure burn  14.0 km/s · 79 min · 0.3 g
  Coast           drive off · 78.8 d
  Brake           19.7 km/s · 1.9 h · 0.3 g
  Capture         3.5 km/s · 1.2 h
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
| Left click a body (or `Tab`) | Select it and warm its route plan in the background; nothing is drawn, framed, or listed until the Plot click adopts it, and selecting the same body again keeps it |
| **Plot route** / **Replot route** on the details screen | Adopt the warmed plan and draw it in the click's frame (or frame the fresh fastest preview); continue a warm-up still building. A retimed or stale preview is replanned at its fastest trip |
| **Trip-time slider** on a preview | Choose one of 64 discrete trips and request a retime at it on the background worker; the handle and its label follow the drag, the previous full-field preview stays until the new plan lands, and the route, arrival, thrust, and delta-v then update without moving the ship. The session prefetches the neighbouring steps in the background, lowest priority, so the first moves after a plot publish at once |
| **Conic / X g burn** and the burn-level slider on a preview | Switch the arrival style or the burn level, rebuild the preview against it, and reset the trip-time window; the route shows the coast, burn, trim, and parking orbit, and the fixed style steps the throttle instead of blending. The level also drives the profile's two burns and its floor is 0.05 g. A station plan has no arrival style to switch, but its departure burn and velocity match still scale with the level |
| **Begin flight** | Fly the drawn plan from its own departure while the preview is fresh; replan at the current state and date once it is stale; enabled once a preview exists |
| **Replan …** (while flying) | Plan and fly a fresh route from the current state, as the old Go To did |
| `F` | Ease the camera to the selected body in one second; the ship does not move, and a flight refuses it |
| **Jump (debug)** / `J` | Three-second eased debug teleport of the ship to the selected body; disabled in flight |
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
| Arrival | 2026-11-22 04:01:12 UTC (7.17 days later) |
| Plan | the 0.3 g split-aware coast floor: 8 arcs, one cruise coast, 1,070 km/s |
| Arrival error | 0.1 m, 0.0001 m/s |
| After three orbits | r = 3,898 km, e = 3.05 × 10⁻⁶, drive off |
| Real time | 131.8 s over 7,907 frames at the suggested 10,000× |
| Peak substeps in one frame | 14 of the 2,000 budget |
| Warp | x10,000 → x10, auto-limited |
| Teleports | none |

The budget never bound, but a starved-budget test with two substeps per frame
proves the clamp: 400 frames reach only 2026-11-15 04:47:33 and the clock is
never ahead of the ship.

The same flight also runs through the real composition root
(`BootstrapSmokeTests`): select Mars, plot, Begin, and let the bootstrap's own
`Update`/`LateUpdate` run. It arrives at the same 2026-11-22 04:01:12 UTC after
7,907 frames and 131.8 real seconds, with 0.1 m and 0.0001 m/s and the
3,898 km parking orbit at e = 2.89 × 10⁻⁹. The shorter
`AProfileFlightArrivesThroughTheBootstrap` run (5,421 frames, 90.4 s) uses the
bootstrap's larger default frame step and reaches the same state at
e = 3.35 × 10⁻⁹.

# Verification

`FlightSessionTests` covers ownership, planning, the exact arrival, budget
clamping, cancel, teleport, a flight through a body, that planning leaves the
orrery untouched, and the preview: it never moves the ship, one replaces
another, Begin consumes it — flying it inside the freshness window and
replanning past it — Cancel clears it,
the staleness window is one percent of the flight, the trip-time window is
`[full-blast minimum, 3 × coast floor]` and clamps to it, the **minimum is the
no-coast crossing**, a retime above the minimum is a short coast up to the floor
and the coasting profile above it, and a request below the minimum keeps the
refusal. Retiming changes
the flight time
and arrival without moving the ship, Begin keeps the chosen time, and the preview
window is cached — a repeat plot at the same date returns the same plan, another
target or burn level misses, and a changed departure date invalidates the cache.
A retimed plan is cached by its requested duration too: retiming away and back
returns the same plan, a background ask for an already-built duration publishes
without starting the worker, a repeat within 0.2% of the window reuses it, and a
date change drops it. The bound is measured rather than assumed: a Mars plan's
finite-burn tables are about 50 KiB, so sixty-four plans are a few megabytes.
Begin itself is pinned to the preview: at Mercury 1 g it flies the **3.22822 d
full-blast minimum** (break → escape → departure burn → brake → capture, seven
arcs with no cruise coast) and the autopilot reaches the arrival state inside the
test's bound, while at Uranus it flies the coast floor because that is the
minimum there. Begin flies the **drawn** plan inside the preview's freshness
window —
`BeginInsideThePreviewWindowFliesTheDrawnPlanAndArrivesExactly` pins it to the
same plan instance, the plotted departure, and the plotted arrival, and asserts
the planner was never called — while past the window it replans at the current
date and flies the **fresh** minimum rather than the old number
(`BeginBeyondThePreviewWindowReplansAtTheCurrentDate`,
`BeginningAtTheMinimumBeyondTheWindowFliesTheReplannedMinimum`). A Begin for
another target never flies the preview. The real-HUD
`PressingBeginInTheRealHudFliesAndCancelClearsTheRoute` is the PlayMode guard.
The Mars anchor is the **6.66965 d full-blast minimum** (**1,538.1 km/s**, seven
arcs, 166.3 t of propellant); the 7.16751-day coast floor is the slower plan and
the window's anchor.
It also covers the **background planner**: the second of two requests made while
the first is building is the plan that is published and the older build is
dropped, the worker's plan equals the synchronous plan for the same duration,
the previous preview stays published until the new plan is ready, a synchronous
retime supersedes an outstanding ask, discarding the preview drops one, and
Dispose joins the worker and refuses
new work. `SolarSystemHudTests` covers the arrival buttons and the profile
window: the two-button `ARRIVAL` row defaults to Conic, a switch rebuilds the
preview, the burn-level floor is 0.05 g, the trip-time label reads
`burns · coast` at the floor and after a retime, and the slider runs in **64
whole steps** from the coast floor to three times it (7.17–21.50 days at the
epoch for Gateway→Mars).
`TripPlannerTests` covers the pinned duration: exact flight time, a gentler burn
and lower delta-v at twice the minimum, and an infeasible report below it.
`BurnCoastBurnTripPlannerTests` covers the profile and the Lambert coast: the
six-unknown seam at a fixed date, the **moving-target loop** (a settled arrival
date that reproduces its own total, a stable total across calls, the closed seam,
and no cruise coast), and `PlanFastest` returning the coast floor at Mercury
1 g, Mars 0.3 g and Jupiter 0.3 g, with the burn-only crossing the longer plan.
It also covers the **empty short-coast gap**: the minimum is an ordinary coast
plan, the old gap durations build as long-coast plans, and every slider duration
above the floor builds with its requested total — the "no hole" sweep at Mercury
1 g, Mars 0.3 g, and Jupiter 0.3 g. `LambertSolverTests`
covers the solver itself.
`WarpPolicyTests` covers the clearance, local-dynamics and plan-event caps, the
two station caps and the scale rounding. `PlanPathTests` covers the arc boundaries. `FlightReportTests` covers the panel
rows, the preview and stale rows, the station **Match** and **hold point**
labels, and the countdown format. `OrbitCameraTests` covers the pitch
clamp, yaw wrap, zoom limits, the shortest turn, the slew rate, and the distance
easing. `CameraRigTests` covers the framing distance and the follow flags.
`SceneFlightTests` is the frame-loop acceptance flight. `BootstrapSmokeTests`
runs the real composition root — `Awake`, `Update`, `LateUpdate` — in edit mode,
including drawing the preview and its arrival ghost without flying, Begin
flying the drawn plan inside the freshness window and replanning past it,
discard leaving the ship alone, the full
frame-loop acceptance (7,907 frames, 131.8 s, 0.1 m and 0.0001 m/s, on the
3,898 km parking orbit), and the burn–coast–burn profile plotted and flown to
the conic parking orbit through the same loop.

See [Testing and verification](/technical/testing.md) for the suite and the
numerical anchors, and [Navigation and arrival](/simulation/navigation-and-arrival.md)
for what the plan itself is.
