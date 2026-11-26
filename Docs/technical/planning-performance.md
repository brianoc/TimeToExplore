---
type: Architecture
title: Planning performance
description: The two-tier planning model — a fast search-only gravity model in front of the unchanged full-field plan and flight — the 5% thrust screen, the floor and retimed-plan caches, the latest-wins background planner (retimes, route plans, and the slider's prefetched neighbours), the warm-started pin, and Begin flying the fresh preview.
tags: [technical, performance, planning, navigation, async]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T10:00:00Z }
---

# The problem

Selecting **Burn-coast-burn** at the epoch froze the nav computer for about
**36 s** at Mercury / 1 g and several seconds at Mars / 0.3 g. Profiling one
`Plan()` found the cost was not the maths of the profile but the **gravity
field**: the break search sampled the quintic at 64 points per probe, and
`GravityField.Prepare` recomputed the ephemeris of every catalogue body (137) at
each point. 5,517 break-peak probes per plan at 4.88 ms each was 26.9 s, 74% of
the call. A direct `PlanAtDuration(8 d)` paid 183 ms for the same reason.

The fix keeps the physics of the plan and the flight exactly as they were and
changes only **what the search uses to steer**. It is described in
[Burn–coast–burn trips](/simulation/burn-coast-burn.md#the-break-search); this
note records the whole shape, its boundary, and the measurements.

**The measurements below are the pre-physical-departure profile's.** They were
taken between 2026-11-20 and 2026-11-21; the departure became a short prograde
break and a ballistic climb on 2026-11-23, which adds one conic solve and a
break bisection per pass and was **not** re-profiled here. The planning structure
and the two-tier boundary are unchanged; the current floors and anchors are in
[Testing and verification](/technical/testing.md#numerical-references-at-the-epoch).

# Two tiers

| Tier | Model | Who sees it |
|---|---|---|
| **Search** | The arc's own central body (`arc.BodyId`) in a body-relative frame: the departure body for the break, the Sun for the heliocentric arcs, the target for capture. The quintic still carries full-field **endpoint** accelerations. A fixed burn is the reference body's two-body burn. | The planner only. It picks a duration. |
| **Plan** | The existing full `GravityField` over the catalogue, sampled as before. Every fixed burn integrates the summed field ([full-field burns](/simulation/powered-leg-floor.md)). | The caller, the nav computer, the trajectory line, and the flight report. |

Step 2 adds a second search quality. A **full-field probe** integrates its fixed
burns through the summed field, so its closure decision is the reported build's,
but keeps a 64-sample departure solve and the cheap probe measurement. It is
still a search input; the certified build remains the result
([step 2](#step-2-the-searches-probes-are-cheap-and-the-answer-is-certified-2026-11-20)).

The boundary is a single rule: **the fast model is a search input and never a
result.** `Plan()` and `PlanAtDuration()` rebuild the chosen duration with the
full field and measure it there, so the returned plan's peak thrust, delta-v,
clearance, seams, and feasibility are full-field numbers. `ShipIntegrator`, the
autopilot, the conic/hybrid capture, and the 0.1 m arrival guarantees are
untouched; the ship is never flown on the search model.

The search curve is deliberately the plan's curve: only the along-arc gravity
subtraction is reduced, and the endpoint accelerations stay full-field. That is
why the reported plans moved by the small amounts in the table below rather
than being re-tuned.

## The 5% screen

A fast estimate must never talk an over-thrust break past the drive. If the
search peak is within `BreakScreenMargin = 0.05` of the search limit
(`MaxAccelerationMps2 * ThrustMargin`, 9.7 m/s² at 1 g — that is, above 95% of
it), the probe is **certified with the full field** instead:

* the field is prepared **once** at the break's start, where the departure body
  and its moons are strongest;
* that selected set is evaluated at the same 64 samples `Measure` uses.

The break only climbs outward, so the prepared set stays a superset of the
bodies that matter along the arc. The certification is the same field the plan
reports, without per-sample catalogue ephemeris. The floor search sits against
the limit, so most of its probes take this branch, not the cheap one; the screen
is a guard, not a licence.

## The floor search and the cache

The true-floor search separates **finding** the floor from **reporting** it.
Every growth, walk-back and bisection probe runs through `Build(..., fast: true)`:
a fixed burn reports its commanded acceleration, a conic coasts, the break is
measured against its own body, the probe burn integrates at 128 samples
(`FastProbeBurnSamples`) with 12 outer passes, and the pinned brake/departure
fixed point stops loosely. Delta-v and clearance are not tracked on a probe.
When the fast answer is refused by the certificate, the search continues above
it with a **full-field probe** (`Build(..., probe: true)`), which integrates the
summed field but solves and measures at probe quality
([step 2](#step-2-the-searches-probes-are-cheap-and-the-answer-is-certified-2026-11-20)).

The bracket is a probe just above the kinematic estimate, a 15% walk-down, and a
bisection to 0.05% (or 30 s). A refused probe is retried 0.2% higher, because
the near-floor basin has narrow non-convergence holes; that retry is the
search's real resolution. The candidate is rebuilt at certified quality with the
full field before it leaves the planner; if the certificate refuses, the search
continues above it and at most three certificates are spent before the last
refusal is reported with its reason. The closure gate is unchanged: 1 km of
position and 1 m/s of velocity. The bracket bounds the scatter, but the common
path no longer pays a continuation: after step 3 every measured case certifies
on its first certificate with **zero** full-field probes, so the bracket's width
buys no time
([step 4](#step-4-the-bracket-was-not-needed-2026-11-20)).

`FlightSession.WindowFor` caches the floor plan and the trip-time window per
`(departure date, target, burn level, cruise style, arrival style)`. A repeat
plot reuses both; a changed departure date, or a place/teleport, clears the
cache. This is what makes a repeat selection free rather than merely fast.

The retimed plans are cached as well, keyed by the same window inputs plus the
requested flight time. A finished background build is stored even when a newer
ask superseded it — the plan is still the right answer for that duration, and it
is exactly the work a returned handle would otherwise repeat. Each entry also
keeps the pin seed its plan settled on, so the next build at a neighbouring
duration starts warm ([warm-starting the pin](#step-3-warm-starting-the-pin-2026-11-24)).
The trip-time handle is **64 integer steps** across the window, so each step is
a distinct trip and exactly one cache slot. The newest **sixty-four** per window are kept,
and a repeat ask is matched when it lies within **0.2% of the window**: that
absorbs the small difference between a requested step and the plan's own total,
while a neighbouring step is about 1.6% away and properly builds. A plan is
about **50 KB**, dominated by the departure burn's and brake's 512-sample
interpolation tables at 24 bytes a sample, so the bound is about **3 MB per
window**. Both the synchronous `SetPreviewDuration` and the slider's
`RequestPreviewDuration` read the cache before queueing work, and a hit is
published directly, so a duration the player has already waited for costs a
dictionary lookup instead of another multi-second build. The cache shares the
window's lifetime: a changed departure date, a place/teleport, or clearing the
windows drops it.

## What is not shared between routes

The break is not a reusable prefix. A route's escape ray, departure-burn
duration, coast, and brake are one fixed point: the reference coast is built at
the requested total, the break is placed on the ray that coast needs, and the
brake is integrated backwards from the arrival the same total puts the target
at. Move the slider and the target has moved, so the ray and both burns move
with it; reusing the break would fly a route the coast no longer joins. The
departure body, its clearance sphere, and the handover bands are cheap and are
recomputed rather than carried between builds.

The cache is therefore the reuse: a whole retimed plan at a step costs a
dictionary lookup, and the window's floor plan and maximum are solved once. The
next lever is not sharing arcs but **warm-starting the outer
break/brake/departure fixed point** with a neighbouring plan's durations and
directions, which cuts the passes without changing the converged plan. Step 3
took it, and it pays on the retime and not on the plot
([warm-starting the pin](#step-3-warm-starting-the-pin-2026-11-24)).

# The background planner

The planner runs background builds for **retimes** and **route plans**:

* A retime is one full-field `PlanAtDuration` build (95–125 ms at the epoch),
  which is over the 33 ms threshold the plan set for staying synchronous.
* A first plot is the floor search (`Plan`) plus the window probes — the
  1.28–3.03 s freeze the editor's first `Plot()` used to produce — and a
  style, cruise-profile or burn-level change is the window floor plus one
  full-field build when the player kept a longer flight time.

Both run on **one managed worker thread** with latest-wins semantics:

1. Moving the slider calls `FlightSession.RequestPreviewDuration(seconds)`. It
   clamps the duration to the preview window, stores it as the newest request
   with a generation number, pulses the worker, and returns. The frame does
   not wait.
2. The worker builds the plan with its **own** `TripPlanner` and `GravityField`.
   The simulation's field keeps a prepared active set and stays on the main
   thread; the worker reads only the catalogue (elements and `ComputeState`),
   never `FlightSession`'s `_gravity`, the integrator, or the autopilot.
   A selection's warm-up goes through `FlightSession.PrimeAsync`, which is the
   same build as `PlotAsync` but leaves the result in a slot nothing reads; the
   style and
   burn-level changes go through `SetArrivalStyle` and `SetBurnLevel` — the same
   request queue.
3. A build that finishes after a newer request arrived is discarded, and the
   newest request is built next. A drag therefore never queues work behind
   stale plans; a plot of another target, a rebuild or a style change also
   makes the outstanding builds stale, so the newest ask wins across the
   pipelines. A repeat plot of the target already queued or building is the
   same ask: it is dropped rather than queued, and a Plot click that arrives
   while a warm-up is building continues that build and reveals its result, so
   a selection and the click behind it share one solve.
4. `TryApplyCompletedRetime` publishes under the request lock, checked against
   the generation. `SolarSystemBootstrap.ApplyRetimeIfReady` is pumped once per
   frame by the HUD and hands the plan to the trajectory line and the report on
   the main thread. `SolarSystemBootstrap.ApplyPreviewIfReady` pumps the
   preview pipeline the same way and redraws the route — a preview publishes
   on screen when it is ready, and a retime never moves the camera or the
   route.

The previous **full-field** preview stays on screen until the replacement
lands. `PendingPreviewSeconds` keeps the handle and its label on the player's
ask rather than snapping back to the older published plan. Nothing fast is
ever shown.

`SetPreviewDuration` remains the synchronous, deterministic path for tests and
callers that need a plan immediately; it builds the identical plan and cancels
any outstanding background request so it cannot be overwritten.

## Lifecycle

`FlightSession` is `IDisposable`. `Dispose` sets the stop flag, drops the queue,
pulses the worker, and joins it for up to `RetimeJoinMilliseconds` (5 s); the
bootstrap disposes the session in `OnDestroy`. A disposed session refuses new
requests, and the worker checks the stop flag before publishing, so no thread
can touch a discarded plan. Plain managed thread only: no JobSystem or Burst.

# Measurements

At the epoch (2026-11-15, JD 2461359.5), Gateway departure, conic capture, on a
throwaway profiling copy under `.cache` (never committed):

| Quantity | Mercury 1 g | Mars 0.3 g |
|---|---:|---:|
| `Plan()` before | 36.2 s | 8.9 s |
| `Plan()` after | **1.44–1.46 s** | **0.85 s** |
| First selection (`Plan` + window + retime) | 1.53–1.78 s | 0.93–0.94 s |
| Repeat selection, same inputs | under 0.1 ms | under 0.1 ms |
| `PlanAtDuration(8 d)`, Mercury | 183 → **95 ms** | — |
| Slider retime (synchronous build) | 93–125 ms | 97–125 ms |
| Background request-to-publish | 99–124 ms | 100–128 ms |
| Handover over the same build | 2–4 ms | 2–3 ms |
| 20 requests at 10 ms (200 ms drag) | 1 plan published, 311 ms from the first | 1 published, 324 ms |

The break-search share fell **8.1×** (26.9 s → 3.3 s), and the floor probe mode
and the pinned convergence removed the rest. The worker's plan equalled the
synchronous plan exactly in flight time, arrival date, peak thrust, delta-v,
clearance, and arc count, so the whole drag published only the newest plan and
the main thread kept its 10 ms cadence.

## What did not change

* **The plans at that step.** Mercury 8 d was 3.02 m/s² and 569.9 km/s, 12.3 d
  1.35 and 379.9, Mars 90 d 0.033 and 45.4. The arrival entry radius moved them
  afterwards to 2.85 / 557.0, 1.30 / 376.9, and 0.033 / 44.4.
* **The floors and the window at that step.** 3.462 d to Mercury at 1 g and
  8.032 d to Mars at 0.3 g; the window was 8.03–24.095 d. They are 3.22 and
  6.74 d and 6.74–20.21 d after the entry change.
* **The seams and the arrival.** Under a millimetre and 1e-5 m/s then; under a
  metre and 3.7 × 10⁻⁴ m/s after the entry change, still well inside the gate.
  The 6.74 d floor flight arrives 0.08 m and 3.7 × 10⁻⁵ m/s on the conic parking
  orbit.
* **The fast-vs-full peak.** +0.18% at 8 d Mercury, +0.41% at 12.3 d, +4.5% on
  the very gentle 90-day Mars break, +0.05% at the Mercury floor — small enough
  that the screen, not the estimate, decides near the limit.

## The full-field burns

The reported plan's fixed burns integrate the summed field, and that was the new
cost floor at step 2. Measured then at the epoch in a .NET harness over the same
sources, with the two-body build it replaced in brackets: a 90-day Mars build
**0.47 s** (0.05 s), the Mercury 8-day build 1.1 s, `Plan()` at the Mars floor
**2.0 s** (0.5 s) and at the Mercury floor 2.4 s, and a slider retime 0.7–1.4 s.
Almost all of it was `GravityField.Prepare`: a full-field burn step re-selected
the active set at every RK4 step, and that evaluates the whole catalogue's
ephemeris. Step 7 measured the cost on builds rather than on steps and cut it by
both levers — the refresh now runs every eighth step and the certified brake pin
stops at 1e-6 m/s — so a fixed-duration build is **2.2–3.7× faster** and a floor
search 2.6–6× faster; the tables are in
[step 7](#step-7-the-levers-measured-2026-11-20). The step-2 plans, floors,
seams, and window did not move ([full-field burns and the powered-leg
floor](/simulation/powered-leg-floor.md)); the step-7 cadence later moved the
Uranus floor by 0.16 d at 1 g and 0.86 d at 0.3 g because it changes the
certified plan the near-floor basin first closes at.

The editor runs the same work several times slower than the harness. In the
Unity suite before step 7 the burn–coast–burn class was a **3-minute** silent
stretch, the near-limit screen sweep (four Mercury builds) ~100 s, the Mars floor
test 69 s, and the whole flight category about 10 minutes, which is why the guard
watches a **twenty-minute** window of log silence. Step 7's after-run is recorded
below.

## One burn–coast–burn build, measured (2026-11-20)

An editor harness on the warm test project, Mars at 0.3 g, Gateway departure,
burn–coast–burn cruise with a conic capture, put hard numbers on the shape of
the cost. A full-field build ran the pinned brake/departure fixed point to its
twelve-pass cap (the certified tolerances were then 1e-7 m/s and 1e-6 s), and
each pass interpolates the brake at 2,048 samples and the departure burn at 512.
That is about **1.2 s per pass, ~15 s per build**. A floor search pays for a
bracket of those builds, so a floor plot was **72 s** and a burn-level change
**80 s**. Step 1 has since cut those two counts — the solve's trial burns to 128
samples and the brake to 512 — and re-solved the coast from the reported burn's
end ([step 1](#step-1-the-builds-own-cost-2026-11-20)).

Two builds in that path were redundant and are gone:

* **The certification the retry repeated.** When the fast floor is optimistic,
  the planner certifies the fast high with one full-field build and then starts
  the full-field search, whose first act was probing that same duration again.
  `SearchFloor` now takes a `tried` map seeded with the certificate (and its own
  probes), so the seeded high is logged with `[cached]` instead of rebuilt. The
  Mars floor test fell from 84 s to **69 s**.
* **A retime that lands on the floor.** `SetPreviewDuration` clamped to
  `PreviewMinimumSeconds` and rebuilt the floor plan that `WindowFor` had already
  solved and stored. It now returns the cached window's plan when the requested
  duration is that plan's duration; a request anywhere else still builds. The
  burn-level case's floor retime fell from 15 s to under 0.1 s.

The same case end to end: **173 s to 121 s** (plot 72 → 56, burn-level change
80 → 64, floor retime 15 → 0), with the floor plan unchanged at 6.737 d, the same
seven arcs, and the same 4.95 m/s² peak.

The **brake sample count is not a free trim.** `BuildBurnCoastBurn` asks for
`FiniteBurnArc.MaximumSamples` (2,048), and the automatic count at the 4 s target
step also clamps to 2,048 for any brake longer than 2 h 17 min. Every measured
brake is 6–29 h (Mercury floor 6.2 h, Mars floor 1 day, Uranus 28.5 h), so
switching to the automatic count would not change any of them. Cutting the cost
means a coarser brake step or a looser pin tolerance, and both change certified
numbers: that is the performance step's decision, not a trim. Step 1 took the
coarser brake step — a fixed 512 samples — kept both tolerances, and re-solved
the coast from the reported burn's end; the measurement is in
[step 1](#step-1-the-builds-own-cost-2026-11-20).

## The Uranus floor and its conservative bracket (measured before step 7)

The floor search's bracket is conservative at the giant planets for a cost
reason, not a physical one. The table below is what **step 6** measured, before
step 7 changed the refresh cadence; the current floors are in
[step 7](#step-7-the-levers-measured-2026-11-20). Measured at the epoch
(2026-11-15) from Gateway in the .NET harness — burn–coast–burn cruise, conic
capture, `TTE_FLOOR=1`, Debug build, the same sources as the suite:

| Case | `Plan()` | Floor | Peak | Direct builds below the floor |
|---|---:|---:|---:|---|
| Uranus, 1 g | **71.1 s** | **33.99769 d**, 2,047.44 km/s | 9.700 m/s² | 33.48773 d closes (5.9 s, 2,081.11 km/s); 33.6577 and 33.8277 d stall at 1.362 × 10⁻⁵ and 1.350 × 10⁻⁵ m/s. On the merged tree the .NET search returns **30.55886 d**, 2,299.29 km/s, seven arcs, and the editor **33.65372 d**, 2,070.02 km/s, eight arcs |
| Uranus, 0.3 g | **59.2 s** | **41.57095 d**, 1,756.59 km/s | 2.942 m/s² | 41.36309 and 40.94738 d close (4.8 s each, 1,767.17 and 1,788.76 km/s); 41.1552 d stalls at 3.039 × 10⁻⁵ m/s |
| Mars, 0.3 g | **27.5 s** | **6.73715 d**, 1,172.48 km/s | 5.137 m/s² | 6.7035 d misses the brake chain by 6,182.2 m / 0.6 m/s, 6.6698 by 1,651.5 m / 0.2, 6.6361 by 9,254.9 m / 0.9; at 6.535 d the break needs 14.2 m/s² |
| Mercury, 1 g | **7.8 s** | **3.21610 d**, 1,765.52 km/s | 9.700 m/s² | 3.1839 d needs a 10.3 m/s² break, rising to 275.0 m/s² at 3.0875 d |

At step 6 the Uranus gap was **0.51 d (1.5%)** at 1 g — 34.00 against 33.49 d
— and **0.62 d (1.5%)** at 0.3 g, 41.57 against 40.95 d; step 7 re-measured both
floors and the 33.49 d probe now stalls, so the current numbers are in
[step 7](#step-7-the-levers-measured-2026-11-20). The lower probes were not
infeasible trips: they are a **pinned-departure stall**. The damped Newton in
`TrySolvePinnedDeparture` cannot improve at the finite-difference resolution, so
the residual stops just above `DepartureSolveToleranceMps = 1e-5` — measured
1.0–3.2 × 10⁻⁵ m/s — and the solve refuses with *"the departure burn cannot be
placed at this trip time"*, leaving the degenerate 176,855 m/s² peak a failed
skeleton reports. Mars and Mercury have no such hole: their low probes fail
honestly on the brake chain or on a break over the drive.

A prototype accepted a stalled solve when `best × coastSeconds ≤ 250 m`, a
quarter of the chain's 1 km gate. It closed the Uranus holes — 33.66 and 33.83 d
became feasible — but each formerly-stalled near-floor build then cost about
**110 s** (the brake and departure burn are **28.8 h** at 2,048 samples), and a
floor search would spend minutes more building feasible plans below the
conservative floor. The hole is therefore a **cost shield**: it rejects those
probes cheaply. The conservative floor is not a purely wrong answer, and
tightening it is step 7's trade, not a free fix.

The window takes its right-hand end from that floor. `MaximumPreviewSeconds` is
3 × the floor. At step 6 that was 34.00–101.99 d at 1 g, and a direct build at
101.99307 d was accepted: seven arcs, 661.35 km/s, a 9.105 h departure burn and a
9.185 h brake, `PlanAtDuration` 2.7 s (the 36 s harness run around it also
reflies both burns under the six alternative steering laws, so that is the
report, not the build). The approach leg and the first-plot work re-measured
the floor, so the window is **33.65–100.96 d** at 1 g and **33.60–100.81 d** at
0.3 g. Direct builds at both maxima are accepted, eight arcs, 668.12 km/s at 1 g
and 674.01 at 0.3 g. The
slider's right end never asks for a refused plan; the current floors and the
re-measurement are in
[the post-close re-measure](#post-close-re-measure-step-4b-merged-2026-11-20).

## Step 7: the levers measured (2026-11-20)

Full-field probes made a first Uranus selection a multi-minute freeze, so step 7
was allowed to trim the burn's cost. Two levers were taken and the third was
measured and rejected:

* **The active set refreshes every eighth RK4 step** — a private
  `FiniteBurnArc.PrepareEverySteps = 8`. `StepFull` prepares at step 0 and every
  eighth step after it and evaluates that set at the four stage times;
  `AccelerationAt` still prepares per query, so the reported acceleration stays
  exact. Eight steps is 32–480 s of burn depending on the arc's sample step.
* **The brake pin stops at 1e-6 m/s** instead of 1e-7. The fixed point's own
  interpolation resolution stalls at about 6.6 × 10⁻⁷ m/s: on the Mars 90-day
  build at 1e-7 the loop never converges and runs its twelve-pass cap, and at
  1e-6 it converges at **pass 8** with the same 6.62 × 10⁻⁷ m/s mismatch. The
  loose stop bounds the brake's start error at about 0.3 m over the longest
  measured brake (82.7 h), far inside the 1 km gate.
* **A coarser brake step was rejected**: the 4 s target step already clamps at
  2,048 samples for every measured brake, so it changes no hot case.

Fixed-duration plans are **identical to four decimals** with both levers on, at
the epoch from Gateway, conic capture (Release .NET, medians of three runs):

| Plan | Δv before → after | Wall before → after |
|---|---|---:|
| Mars 90 d, 0.3 g | 44.3564 km/s (both) | 1.10 → **0.31 s** |
| Mercury 8 d, 1 g | 557.0380 (both) | 1.54 → **0.43 s** |
| Uranus 400 d, 1 g | 182.3393 (both) | 0.98 → **0.40 s** |
| Mars 20 d, 0.3 g | 270.8500 (both) | 1.35 → **0.38 s** |
| Mars 10 d, 0.3 g | 607.8259 (both) | 1.57 → **0.42 s** |
| Uranus 35 d, 1 g | 1984.4479 (both) | 1.41 → **0.63 s** |

The seams stay small. On the Uranus 35-day plan the worst seam after the change
is **0.293 m / 4.2 × 10⁻⁵ m/s** (Capture→Coast) against 4.1 × 10⁻³ m /
1.7 × 10⁻⁶ before, and the 40.71332-day floor plan's worst is **3.07 m /
1.7 × 10⁻⁴ m/s**; both are orders inside the 1 km / 1 m/s chain gate. The
independent-RK4 accuracy check is unchanged: the 80,000 s SOI-crossing burn
against the 20,000-step integration is still **4.12 × 10⁻⁴ m and 6.69 × 10⁻⁹
m/s**, and `FiniteBurnTests.ABurnCrossingTheSphereOfInfluenceCloses` passes.

`Plan()` — the floor search — moved only at Uranus, because the coarser refresh
changes which certified plan the near-floor basin first closes at:

| Case | Floor before → after | Wall before → after |
|---|---|---:|
| Mars, 0.3 g | 6.73715 d (both) | 8.64 → **2.47 s** |
| Mercury, 1 g | 3.21610 d (both) | 2.67 → **1.02 s** |
| Uranus, 1 g | 33.99769 → **34.16041 d** | 22.0 → **3.62 s** |
| Uranus, 0.3 g | 41.57095 → **40.71332 d** | 18.7 → **0.79 s** |

At step 7 the 1 g floor was 0.16 d (0.5%) **more** conservative and the 0.3 g
floor 0.86 d shorter at 1,801.17 km/s. The first-plot work and the approach leg
then re-measured them: on the merged tree the .NET floors are **30.55886 d** and
**40.71332 d**, and the editor's are **33.65372 d** and **33.60438 d** (see
[the post-close re-measure](#post-close-re-measure-step-4b-merged-2026-11-20)).
These are re-measured anchors, not errors: the floor is the first duration the
certified full-field build closes at, and the basin is noisy. `Plan()` is now 2.6–6× faster at Mars, Mercury and Uranus 1 g, and 24× at
Uranus 0.3 g partly because its floor moved.

**At step 7 the 0.3 g floor was a needle**, and that mattered for the slider.
Its .NET plan was certified as feasible at 40.71332 d, and the 1 g floor at
34.16041 d rebuilt normally from a direct request. The 0.3 g one did not: a cold
`PlanAtDuration(40.71332 d)` was refused with *"the departure burn cannot be
placed at this trip time"*, 1.039 × 10⁻⁵ m/s short — 4% over
`DepartureSolveToleranceMps` — because the search's 0.2% retry found a feasible
point only about **40 ms** wide inside a hole that runs to 40.9 d. The returned
plan was still a certified full-field plan and its seams were inside the gate,
but a rebuild at its own duration stalled. The background retime used to rebuild
a floor request and would have published that refusal; it now hands the window's
stored floor plan to the publisher, mirroring `SetPreviewDuration`'s reuse, so a
drag to the left end is free and cannot refuse. Step 1's certified brake now
closes that rebuild (5.1 m / 6.5 × 10⁻⁴ m/s), but the cache reuse still keeps
the drag off the search. The rest of the window keeps the
stored floor plan to the publisher, mirroring `SetPreviewDuration`'s reuse, so a
drag to the left end is free and cannot refuse. Step 1's certified brake now
closes that rebuild (5.1 m / 6.5 × 10⁻⁴ m/s), but the cache reuse still keeps
the drag off the search. The rest of the window keeps the
pre-existing holes (at 0.3 g, 41.6–41.8 d was refused before step 7 too); the
window's right end is a feasible build at both levels.

**The stalled-departure acceptance stays rejected.** Combining this step with
step 6's `best × coastSeconds ≤ 250 m` acceptance collapsed the Uranus floor to
**16.21 d** at 1 g and **25.52 d** at 0.3 g, admitting **5,087 km/s** and
**3,635 km/s** plans: a short coast lets a large residual pass a position budget.
That is worse than conservative. The faster build makes the cost shield cheaper,
but the acceptance needs a residual-scaled rule before it is safe, and it is not
part of this step.

In the Unity suite, `tools/check.sh --tests flight,presentation,smoke` passed in
**379 s** on the warm test project; the same run on a cold cache was **405 s**,
against the recorded **1005 s** at step 4 and **908 s** at step 5. The flight
category alone took **242 s** against the recorded **606 s**. The burn-level
change, the old burn–coast–burn stretch, is now **58.4 s** (123 s at step 5, 166 s
before its trim), the Mars floor test **18.8 s** (68 s), the Mercury floor 10.4 s,
and the longest test anywhere is 59.3 s (the HUD's cruise-profile control), so no
multi-minute silent stretch is left.

## Step 1: the build's own cost (2026-11-20)

The first-plot plan's step 1 makes one **certified full-field** burn–coast–burn
build cheap. The search's logic, its tolerances, the 5% screen, the floor
tolerance, and the 1 km / 1 m/s seam gate are unchanged; what changes is the
resolution the build's own solve integrates at, and the two sample counts that
dominated it.

* **The pinned departure solve's trial burns integrate at 128 samples**
  (`TripPlanner.PinnedDepartureSamples`). That is the solve's resolution, not
  the reported arc's: the damped Newton only has to place the trial burn's end
  velocity on the coast, and 128 samples converge to
  `DepartureSolveToleranceMps` at a quarter of the cost. Both the main pin loop
  and the settle loop pass it.
* **The reported departure burn still integrates at 512** (`Math.Max(512,
  ArcSamples)`), so the arc the caller sees is the resolution it has always had.
* **The coast is re-solved from the reported burn's actual end.** The 128-sample
  trial burn and the 512-sample reported burn end a few metres apart; the
  Lambert solve, the correction, and the conic now start from
  `departureBurn.EndLocalPositionM + sunAtCoastStart` instead of the solve's
  `coastStartPosition`. The position seam is therefore exact, and only the
  velocity seam carries the sample-count difference — metres and milli-metres
  per second, not kilometres.
* **The brake integrates at 512 samples** (`TripPlanner.BrakeSamples`) in
  `BuildBurnCoastBurn` **for the certified build**, replacing the old
  `FiniteBurnArc.MaximumSamples` (2,048). A **fast probe keeps
  `MaximumSamples`**: it is a search input, and trimming its brake would change
  the search rather than just make a build cheap. Every measured certified
  brake is 6–29 h, so the automatic 4 s step also clamped at 2,048; the explicit
  512 is the measured step. `FiniteBurnArc.MaximumSamples` itself and the hybrid
  capture's injection burn are unchanged.
* **A stalled coarse solve retries once at the reported resolution.**
  `TrySolvePinnedDepartureWithFallback` runs the 128-sample solve and, when it
  refuses, the 512-sample one. The tolerance is identical at both; the retry is
  there because a runtime's last-bit libm difference can leave the coarse
  iteration a fraction above the tolerance. Measured at Uranus 35 d and 1 g, the
  Mono build refused at 1.274 × 10⁻⁵ m/s while the .NET build converged, so
  without the retry that fixed-duration anchor would have moved from a plan to a
  refusal. Fast probes get no retry: their cheap decision is the search's, and
  the certified build after it is the authority.

Measured at the epoch (2026-11-15), Gateway departure, conic capture, in the
throwaway Release .NET harness. Builds are medians of five after two warm-up
builds, so tiered JIT is not in the number; the absolute times are about half
the step-7 record, but the before/after pair is the change:

| Plan | Δv before → after | Build before → after | Worst seam |
|---|---|---|---|
| Mars 90 d, 0.3 g | 44.3564 km/s (both) | 0.16 → **0.05 s** | 0.009 m / 1.7 × 10⁻⁵ m/s |
| Mercury 8 d, 1 g | 557.0380 (both) | 0.28 → **0.09 s** | 9.9 m / 4.1 × 10⁻³ m/s |
| Uranus 400 d, 1 g | 182.3393 (both) | 0.18 → **0.06 s** | 0.07 m / 2.2 × 10⁻⁵ m/s |
| Mars 20 d, 0.3 g | 270.8500 (both) | 0.23 → **0.06 s** | 0.02 m / 3.5 × 10⁻⁵ m/s |
| Mars 10 d, 0.3 g | 607.8259 (both) | 0.26 → **0.08 s** | 2.2 m / 1.3 × 10⁻³ m/s |
| Uranus 35 d, 1 g | 1984.4479 (both) | 0.40 → **0.11 s** | 2.2 m / 3.7 × 10⁻⁴ m/s |

The six fixed-duration answers are **identical to four decimals**, and every
seam is metres and milli-metres per second: the worst of the six is Mercury's
9.9 m and 4.1 × 10⁻³ m/s, a hundredfold inside the 1 km / 1 m/s gate. The
departure-burn-to-coast seam on the Uranus 35-day plan is **2.0 × 10⁻⁴ m and
3.4 × 10⁻⁶ m/s**, which is the seam the coast re-solve exists to keep closed.

`Plan()` is the floor search, so its cost is the number of certified builds it
pays for. The probe path is the same one the search used before step 1 — only
the certified builds it pays for got cheaper — so every measured case fell:
Uranus 1 g **3.11 → 0.22 s**, Uranus 0.3 g **0.48 → 0.21 s**, Mars 0.3 g
**1.97 → 0.60 s**, Mercury 1 g **0.56 → 0.25 s**, and Jupiter 0.3 g
**0.59 → 0.22 s**.

The floors are the first duration the certified build closes at, so the cheaper
build can change one where the coarse solve stops stalling. Four are unchanged:
Mars 0.3 g **6.73715 d**, Mercury 1 g **3.21610 d**, Uranus 0.3 g
**40.71332 d**, and Jupiter 0.3 g **13.33520 d**. **Uranus 1 g moves from
34.16041 to 30.55886 d** (2,037 → 2,299.29 km/s): the certified build's
128-sample departure solve converges at durations where the 512-sample one
stalled about 1.1 × 10⁻⁵ m/s above the tolerance, so the cost shield that used
to buy a refusal no longer does. It is a re-measured floor, not an error. The
returned plan is certified — `IsFeasibleAt` holds, the worst seam is **6.7 m /
8.4 × 10⁻⁴ m/s**, and a cold rebuild at 30.55886 d closes at 0.4 m /
1.0 × 10⁻³ m/s — and the window follows: **30.56–91.68 d** at 1 g and
**40.71–122.14 d** at 0.3 g. A direct build at the new 1 g maximum (91.67658 d)
is accepted with seven arcs and 736.02 km/s. Every fixed-duration anchor is
unchanged. Step 3 re-measured the floors and windows as anchors, and the
first-plot plan's close-out records them as final; its last step measured the
bracket and dropped it
([the bracket was not needed](#step-4-the-bracket-was-not-needed-2026-11-20)).

## Step 2: the search's probes are cheap and the answer is certified (2026-11-20)

Step 1 made the **certified** build cheap. Step 2 makes the builds the floor
search spends its time on cheap as well, while keeping the answer certified. The
fast two-body probe is unchanged. A new **full-field probe** quality sits
between it and the certified build:

* its fixed burns integrate the **summed field** (`burnGravity` stays
  `_gravity`), so its closure decision is the one the reported build makes;
* the pinned departure solve integrates its trial burns at **64 samples**
  (`TripPlanner.ProbeDepartureSamples`) and has **no 512 fallback**. The fallback
  protects a certified fixed-duration anchor; a probe is a search decision.
* the brake keeps the certified **512** samples (`BrakeSamples`);
* it measures with `MeasureFast`: a fixed burn reports its commanded
  acceleration, a conic coasts, and a break is measured against its own central
  body. There are no per-sample catalogue `Prepare` calls, and delta-v and
  clearance are not tracked.

The probe keeps the certified fixed-point stops (a probe's decision is meant to
be the reported build's) and skips the three settle passes. The fast two-body
probe keeps its own loose stops, its 2,048-sample brake, and its 128-sample trial
burns. Both probe qualities are search inputs; neither is ever reported.

**The answer is always certified.** `PlanBurnCoastBurnMinimum` rebuilds every
candidate with the default build — solve at 128 with the 512 fallback, brake at
512, full `Measure` — and returns it only when the drive and the chain accept it.
The fast answer is certified first. If that certificate refuses, the search
continues **above** it with full-field probes, seeded at `candidate × 1.2` with
the refused candidate as the low end; the certified refusal is in the search's
`tried` map, so the low probe is `[cached]` and free. The near-floor 0.2% retry is
applied to the seeded bracket before the wide bisection, so a narrow hole costs
one probe instead of a full bisection. At most **three** certificates are spent;
after that the last refusal is reported with its reason, never a probe. No
tolerance and no seam gate moved.

Measured in the warm Release .NET harness (medians of three, epoch, Gateway,
conic capture). Only Mars reaches the continuation path in .NET, because Mono's
last-bit difference is what refuses Jupiter's fast high:

| Case | `Plan()` before → after | Floor | Search |
|---|---:|---:|---|
| Uranus, 0.3 g | 0.213 → **0.230 s** | 40.71332 d (both) | certificate accepted |
| Uranus, 1 g | 0.222 → **0.239 s** | 30.55886 d (both) | certificate accepted |
| Mars, 0.3 g | 0.612 → **0.778 s** | 6.73715 d (both) | 5 probes, 2 certificates |
| Mercury, 1 g | 0.246 → **0.267 s** | 3.21610 d (both) | certificate accepted |
| Jupiter, 0.3 g | 0.224 → **0.240 s** | 13.33520 d (both) | certificate accepted |

The editor is the target, and its first `Plot()` (planner plus window, warm test
project) is where the continuation changes the cost:

| Case | Editor before → after | Floor before → after |
|---|---:|---|
| Uranus, 0.3 g | 1.806 → **1.713 s** | 33.60438 d (both) |
| Uranus, 1 g | 1.230 → **1.232 s** | 31.25256 d (both) |
| Mars, 0.3 g | 4.009 → **4.558 s** | 6.73715 d (both) |
| Mercury, 1 g | 2.288 → **2.356 s** | 3.21610 d (both) |
| Jupiter, 0.3 g | **14.555 → 9.552 s** | **13.27662 → 13.34149 d** |

Jupiter is the multi-million-kilometre case that used to freeze the nav
computer: its fast high is refused by the certificate in Mono, the old search
bisected the near-floor scatter with about fourteen certified builds, and the
new search does the same bracket with full-field probes. Mars is the one
regression, 4.01 → 4.56 s: its certificate refusal costs a second certificate
plus the five-probe continuation, and the step-2 bisection is still 0.05%.
Step 3's coarse growth and 1% bisection is the lever that removes that cost; the
full-field probe is the prerequisite this step added. Uranus and Mercury keep
their single certificate and are unchanged within noise.

The Jupiter floor moved **13.27662 → 13.34149 d** in the editor (+0.49%) because
the probe search certifies a different duration from the scatter; the window
follows, 13.341–40.024 d. It is a re-measured, certified floor, not an error: the
plan is rebuilt full-field, every arc has its measured clearance and delta-v, and
every seam is inside the 1 km / 1 m/s gate. The .NET Jupiter floor is unchanged
at 13.33520 d, so the runtime divergence the plan already records is still there;
Mars (6.73715 d), Mercury (3.21610 d), and Uranus (33.60438 / 31.25256 at that
step; the 1 g floor is re-measured on the merged tree) did not
move. The six fixed-duration Δv anchors are unchanged to four decimals.

The hybrid capture is threaded through the probe too: a probe's injection burn
stays a full-field burn (`fast && !probe` selects the two-body field), while its
measurement is the cheap one. `BuildProbeAtDuration` is a diagnostic the tests
use to show a probe's infinite clearance and zero coast delta-v; `Plan` never
returns it.

## Step 3: why the editor's Jupiter was slow (2026-11-20)

The user asked why Jupiter was still slow. At step 2 its first `Plot()` was
**9.55 s** in the editor against **0.24 s** .NET — a 40× ratio where Mars and
Uranus were ~5.5× — so the editor was not merely doing the same work slower. It
was doing a **different search**.

The trace below is a throwaway timing test on the warm Unity test project (epoch,
Gateway departure, conic capture, 0.3 g) against the Release .NET harness's
`probe plan jupiter 0.3 1`:

| | .NET | Editor |
|---|---|---|
| fast probes | 17 | **18** |
| fast high | **13.33520 d**, certifies at 12 passes | **13.07211 d** |
| certificate at the fast high | accepted | **refused**: *the brake does not close on the capture entry* (28,069.5 m, 1.0 m/s) |
| full-field probes (continuation) | 0 | **14 (+1 cached)** |
| second certificate | — | 13.34149 d, accepted |
| builds in `Plan()` | 18 | **34** |
| `Plan()` | 0.24 s | **9.55 s** |

The 9.55 s is the step-2 measurement; the traced run itself was 10.13 s on the
same code, which is the run the per-build timings below come from.

The editor's own per-build timings make the split unambiguous: the fast search
is **0.48 s** (18 probes at 20–40 ms), the refused twelve-pass certificate is
**1.07 s**, the fourteen full-field probes are **~5.7 s** (0.4–0.7 s each), the
second certificate is ~0.4 s, and the window build is 0.36 s. **The
continuation is the cost**, and it runs because Mono's fast probe accepts
13.07211 d where .NET's refuses it. That is the step-2 runtime divergence, seen
from the other side.

### The refusal is the brake pin's pass cap

The fast high is **not** a departure-solve stall and **not** a peak over the
drive:

* The certified build's **128-sample departure solve converges** at 13.07211 d;
  the 512-sample fallback never runs.
* The plan's peak is **4.6 m/s²** at the certificate, far inside the 9.81 m/s²
  drive; the drive is not the issue.
* The outer brake/departure fixed point runs its **twelve-pass
  `DefaultOuterPasses` cap** with the mismatch still falling about 3× a pass:
  7.29 × 10³ → 39.3 → 12.7 → 4.09 → 1.32 → 0.425 → **0.137 m/s at pass 11**.
  That residual, integrated over the ~356,000 s brake, is the **28 km** the
  chain gate reports. The pin needs about six more passes to reach metres.

The same duration with the cap doubled **certifies**: seven arcs, 2,270.99 km/s,
peak 9.700 m/s² (98.9% of the drive), worst seam **93.35 m / 8.0 × 10⁻³ m/s**
(.NET). A duration nudge is not the fix: direct certified builds at +0.2%
(13.09826 d), +0.5% (13.13747 d), and 13.2 d still miss the chain by 2,421 m,
1,959 m, and 1,520 m at twelve passes, so neither of the fast search's 0.2%
rungs would certify. Near the floor the chain is limited by the pin's
unconverged state, not by the trip time.

### The fix: one solver retry inside the certified build

`Build`'s **certified** burn–coast–burn path retries the build **once** at
`RetryOuterPasses = 24` when the plan refuses at the chain gate after its pin ran
the twelve-pass cap without converging (`BuildResult.PinStalled`). It is a
solver retry, not a relaxed answer: no tolerance, no gate, no search rule, and
no probe changes. A build that converges never pays it, and a probe never
retries. The trace line is `pin retry at … d: 12 -> 24 passes, after …`.

Warm Release .NET harness, `Plan()` (medians of three):

| Case | `Plan()` before → after | Floor before → after | Search |
|---|---:|---:|---|
| Uranus, 0.3 g | 0.230 → **0.226 s** | 40.71332 d (both) | certificate accepted |
| Uranus, 1 g | 0.239 → **0.233 s** | 30.55886 d (both) | certificate accepted |
| Mars, 0.3 g | 0.778 → **0.464 s** | 6.73715 → **6.73042 d** | refusal → pin retry → certificate |
| Mercury, 1 g | 0.267 → **0.261 s** | 3.21610 d (both) | certificate accepted |
| Jupiter, 0.3 g | 0.240 → **0.231 s** | 13.33520 d (both) | certificate accepted |

Editor first `Plot()` in the warm test project (planner plus window; two runs
per case, the first shown):

| Case | Before → after | Floor after | Window after |
|---|---:|---:|---|
| Uranus, 0.3 g | 1.713 → **1.87 s** | 33.60438 d | 33.604–100.813 d |
| Uranus, 1 g | 1.232 → **1.25 s** | 31.25256 d | 31.253–93.758 d |
| Mars, 0.3 g | 4.558 → **3.25 s** | **6.73042 d** | 6.730–20.191 d |
| Mercury, 1 g | 2.356 → **2.42 s** | 3.21610 d | 3.216–9.648 d |
| Jupiter, 0.3 g | **9.552 → 3.07 s** | **13.07211 d** | 13.072–39.216 d |

Uranus and Mercury take the identical path to step 2, so their small differences
are run-to-run spread (a second run gave 1.75, 1.29, and 2.44 s). Jupiter is
**3.1× faster** and Mars **1.4×**, and every case is now under ~3.3 s in the
editor. The editor's Jupiter floor moves **13.34149 → 13.07211 d** because the
retry certifies the fast search's own high — the same duration .NET would refuse
without the retry, and the reason the two runtimes still disagree on which
scatter point is the floor. The .NET Jupiter floor stays 13.33520 d; the .NET
Mars floor moves to **6.73042 d**, the same re-measured duration as the editor's.
Both new floors are certified plans with every arc's clearance and delta-v
measured, and their seams are inside the 1 km / 1 m/s gate (the Jupiter 13.07211
plan: 93.35 m / 8.0 × 10⁻³ m/s in .NET; the flight suite asserts the editor's
floor plans through the same gate).

The six fixed-duration Δv anchors **do not move with the retry or the cheaper
build** — on the merged tree they are Mars 90 d 44.3543 km/s, Mercury 8 d
557.0380, Uranus 400 d 182.3124, Mars 20 d 270.8500, Mars 10 d 607.8259, and
Uranus 35 d 1984.4479, the approach-leg values to four decimals — because every
one of them converges
well before the twelve-pass cap. The window's right end is still a feasible
build at 3 × the floor (39.216 d at Jupiter, 20.191 d at Mars): the retry
changes the floor, not the window rule, and `FlightSession` reuses the stored
floor plan for a request at the floor's own duration.

## Step 4: the bracket was not needed (2026-11-20)

The plan's last step was to replace the 15% walk-down and 0.05% bisection with
coarse growth and a 1% bisection, so that ~7 probes replace ~14. Its premise was
the step-2 trace, where the editor's Jupiter certificate refused and **14
full-field continuation probes** dominated `Plan()`. Step 3 removed that
continuation — the certified build retries a stalled pin and the same duration
then certifies — so the probe count a coarser bracket would remove no longer
exists. The step is **measured and dropped**, not implemented: the search and
its bracket are unchanged.

The evidence is the .NET harness's trace, identical in all five cases:
`fast`, `probe 0`, `full 0`, `cert 1`. One certified build, **zero full-field
probes**, one certificate. Mars is the one exception, and it is inside its
certified build: a `pin retry` (12 → 24 passes) and still one certificate. No
measured case reaches the continuation, so nothing is left for a bracket to
save.

**Historical, pre-crossing.** The table below is the editor's first `Plot()` as
it stood **before the trip-time window solved the zero-coast crossing**. It is
kept as history and its 1.28–3.03 s must not be quoted as the current cost; the
current numbers are in [the route benchmark](#the-route-benchmark-2026-11-24).
The floors and windows in it are also pre-[ADR-019](/decisions/adr-019-station-calls-and-departure.md)
and have been re-baselined.

Final editor first `Plot()` on the warm test project (epoch 2026-11-15, Gateway,
conic capture), the same measurement the plan opened with:

| Case | Original | Final | Floor | Window |
|---|---:|---:|---:|---|
| Uranus 0.3 g (default) | 27.3 s | **1.80 s** | 33.60438 d | 33.60–100.81 d |
| Uranus 1 g | 2.57 s | **1.28 s** | 33.65372 d | 33.65–100.96 d |
| Mars 0.3 g | 13.5 s | **2.99 s** | 6.73042 d | 6.73–20.19 d |
| Mercury 1 g | 4.17 s | **2.29 s** | 3.21610 d | 3.22–9.65 d |
| Jupiter 0.3 g | 33.6 s | **3.03 s** | 13.07211 d | 13.07–39.22 d |

The goal was a certified first plot in roughly 3 s in the editor at both levels,
and the merged tree met it **before the crossing**: the five cases were 1.28–3.03 s
(a second run gave 1.98, 1.30, 3.02, 2.29, and 3.05 s). .NET's `Plan()` on the
same tree is 0.21–0.38 s, so the editor's remaining gap is per-build cost, not
search shape.

**Since step 3 of the burn-only plan the preview window also solves the
moving-target minimum** (`PlanFastest`: the coast floor plus the zero-coast
crossing), so the same five first plots are **2.81, 3.08, 5.07, 3.89, and 4.61 s**
in that order — the added 1–2 s is the crossing, not a longer floor search. Those
are still pre-ADR-019 Editor numbers; the current six-route measurement is in
[the route benchmark](#the-route-benchmark-2026-11-24). The floors and the .NET
`Plan()` costs above are unchanged; Uranus does not win the zero-coast branch but
pays for the attempt.

### What the remaining time is

Mars and Jupiter are ~3.0 s because they are now **one certified build plus
the window build**, paid at the editor's per-build price; the search in front of
them is fast probes that cost milliseconds. Mars alone pays a **pin retry**, and
that makes its certified build cost about twice a converged one: the first
twelve outer passes are repeated by the twenty-four-pass retry, roughly **0.4 s**
of its 3.0 s.

That duplicated work is the last known lever. **Resuming the brake/departure pin
in place from its twelfth-pass state, instead of rebuilding the burn at
twenty-four passes, would remove the twelve repeated passes.** It is an
unmeasured opportunity, not work in this plan: the retry runs only when the pin
stalled, and it is what certifies the current floors (Mars 6.73715 → 6.73042 d,
Jupiter 13.34149 → 13.07211 d) without moving a tolerance or a gate.

### The runtime divergence is a property, not a bug

The .NET harness and the editor disagree on which near-floor point is the floor
at the giant planets: Uranus 0.3 g **40.71 d .NET against 33.60 d in the
editor**, Uranus 1 g 30.56 against 33.65, and Jupiter **13.335 against 13.072**.
Both numbers are **certified full-field plans**: each is rebuilt with the summed
field, every arc carries its measured clearance and delta-v, and every seam is
inside the 1 km / 1 m/s gate. The near-floor basin is a scatter rather than a
boundary — a duration's feasibility is decided by whether the departure Newton
stalls and by how many passes the brake pin needs — so a last-bit libm difference
between Mono and .NET selects a different needle.

That is a characteristic of the basin, not a defect to reconcile. **The editor's
number is the one that ships**, which is why the anchors in
[testing](/technical/testing.md) and the profile docs are its numbers; the .NET
figures stay as the harness's own measurement and as evidence that both points
are certified. Whatever the runtime, a reported floor is a certified full-field
plan, never a probe's answer.

The same basin selects a different Uranus floor on Unity 7's CoreCLR Editor and
on the plain .NET 10 harness; the side-by-side runtimes are in
[the Unity 7 experiment](/technical/unity-7-coreclr.md).
## Post-close re-measure (step 4b merged, 2026-11-20)

The **planetocentric approach leg** landed after the first-plot plan and was
merged on top of it. It changes the arrival the near-floor basin first closes
at, and the first-plot work changes which certified duration the search reaches,
so the floors were re-measured on the merged tree. Measured at the epoch from
Gateway with the conic capture, medians in the warm Release .NET harness and
first `Plot()` in the editor's warm test project:

| Case | .NET `Plan()` | Editor first plot | Editor floor | Editor window |
|---|---:|---:|---:|---:|
| Uranus, 0.3 g | **0.208 s** | **1.80–1.98 s** | **33.60438 d** | 33.60–100.81 d |
| Uranus, 1 g | **0.213 s** | **1.28–1.30 s** | **33.65372 d** | 33.65–100.96 d |
| Mars, 0.3 g | **0.384 s** | **2.99–3.02 s** | **6.73042 d** | 6.73–20.19 d |
| Mercury, 1 g | **0.241 s** | **2.29 s** | **3.21610 d** | 3.22–9.65 d |
| Jupiter, 0.3 g | **0.214 s** | **3.03–3.05 s** | **13.07211 d** | 13.07–39.22 d |

The .NET floors are **30.55886 d** (Uranus 1 g), **40.71332 d** (Uranus 0.3 g),
**6.73042 d** (Mars 0.3 g), **3.21610 d** (Mercury 1 g), and **13.33520 d**
(Jupiter 0.3 g), all seven-arc trips with no approach: their brakes start outside
the ceiling. The editor's 1 g floor is the one floor plan whose brake starts
inside it, so it carries the approach arc and eight arcs. The near-floor basin
is still needle-shaped; the returned duration is a certified plan, not a smooth
boundary, so the background retime keeps reusing the window's stored floor plan.

Direct builds at both Uranus maxima are accepted: eight arcs, **668.12 km/s** at
1 g and **674.01** at 0.3 g, so the slider's right end is feasible. The
fixed-duration plans are re-measured against the approach-leg values, not the
pre-approach ones: **Uranus 400 d at 1 g is 182.3124 km/s** (break 4.048,
departure burn 90.17, brake 81.39, capture 0.001 + 0.010, with a 7.544-day
approach from the 52,795,223.9 km ceiling to 368,111.4 km), **Mars 90 d is
44.3543** (a 6.032-hour approach from 588,785.9 km to 92,922.5 km), Mercury 8 d
is 557.0380, Mars 20 d 270.8500, Mars 10 d 607.8259, and Uranus 35 d 1984.4479.
Every one is identical to four decimals to main's approach-leg value, so the
cheap build moved no fixed-duration answer.

The merged approach leaves Mercury and Mars alone and moves a Uranus floor only
where the brake starts inside the ceiling. The **flight suite** on the merged
tree (`tools/check.sh --tests flight`) passes; the exact run is reported in the
merge's log entry. The tables above are the current anchors; the earlier
sections record how each measurement was reached.

## Where one build's time goes (2026-11-24)

The route-cost plan assumed the outer pin's passes and the 512-sample arcs were
where a build spends its time. This step measured it instead, for the two calls
the player waits on, so that the next step aims at the phase that actually
dominates.

**Machine and runtime.** Apple **M4 Max** (Mac16,5, 64 GB), macOS 27.2.1, Unity
**6000.3.28f1** editor in batch mode (`-nographics`), **Mono**, in the warm
EditMode test project. These are **editor/Mono numbers**, not the Release .NET
harness's, and they do not transfer as absolute times. Two sessions were run;
the one below is the second (warm test project, no suite around it). The first
was **441 ms** and **3.82 s**, about 7% slower in both cases.

**Warm-up rule.** Each case is called once untimed to JIT the path and warm the
field, then three measured runs; the table's wall, allocation, and phases are
the median run. The planner and its `GravityField` are shared by the warm-up and
the runs, so one-time field setup is not charged to a build. One further run per
case with `TripPlanner.Metrics = null` checks the instrumentation itself: **413.7
ms** against 412.5 and **3.560 s** against 3.563, inside the run-to-run spread.

**Allocations.** Mono returns **0** from
`GC.GetAllocatedBytesForCurrentThread` in this editor, so the allocation column
is Unity's **GC Allocated In Frame** profiler counter, read before and after the
synchronous call. No planner worker runs during the measurement, so the delta is
the call's own managed allocation; it includes the returned plan, which the
planner keeps.

**Cases.** Epoch JD 2461359.5, Gateway departure, Mars at 0.3 g, conic capture.
A **retime** is one `PlanAtDuration` at **33.70 d**, mid-window. A **first plot**
is `PlanFastest`: the coast floor plus the burn-only moving target. It does not
include the window's six duration probes, which `FlightSession.WindowFor` adds
on the real plot.

| | Retime, 33.70 d | First plot (`PlanFastest`) |
|---|---:|---:|
| Wall, median of three | **412 ms** | **3.56 s** |
| Allocated (`GC Allocated In Frame`) | **615 KiB** | **31.8 MiB** |
| Certified builds / fast probes / full-field probes | 1 / 0 / 0 | 1 / 21 / 0 |
| Burn-only builds | — | 3 |
| Outer pin passes | **10** | **174** (162 fast, 12 certified) |
| Pin retries | 0 | 0 |
| **Pin loop** | **294 ms, 71.3%** | **1.79 s, 50.3%** |
| — brake integration | 82 ms, 19.9% | 280 ms, 7.8% |
| — pinned departure solve | 165 ms, 39.9% | 739 ms, 20.7% |
| Settle loop | 7 ms, 1.7% | 7 ms, 0.2% |
| Burn-only seam solve | — | 1.04 s, 29.1% |
| **Reported 512-sample arcs** | **2, 1,024 samples** | **8, 4,096 samples** |
| — integrate and tabulate | 17 ms, 4.1% | 69 ms, 1.9% |
| — measure | 17 ms, 4.0% | 68 ms, 1.9% |
| Probe finite-burn arcs | — | 26, 28,288 samples, 0.4% |
| Untimed remainder | 78 ms, 18.8% | ~16% |

The retime is **one certified build with ten outer passes**. The **pin loop is
71% of the wall time**; inside a pass the departure solve takes about 16.5 ms and
the brake about 8.2 ms, and the other ~20 ms is the climb, the approach, and the
seam arithmetic. The two reported arcs that the plan keeps are only **8%** of
the build: integrating and tabulating them is 4.1%, measuring them another 4.0%.
Their two interpolation tables are about 49 KB of the 615 KiB the build
allocates; the rest is the build's own churn. **Warm-starting the pin is
therefore the retime's lever**, and trimming the reported arcs is not.

The first plot is now dominated by its **search**, not its answer: 162 of the
174 outer passes belong to 21 fast probes, and the floor certificate is the
other 12. The coast floor is **61.7%** of the plot and the burn-only crossing
**38.3%**; within those, the pin loop is **50.3%**, the burn-only seam solve
**29.1%**, and all reported arcs, probe arcs included, are under **5%**. The
eight reported arcs are 512 samples each because the certificate and the three
burn-only moving-target passes each build a departure burn and a brake.

The counts are exact; the time split is `Stopwatch` timestamps at phase
boundaries and carries the run-to-run spread above. Work that is not bracketed —
the coast skeleton, the Lambert solve, the capture chain, the conic arcs'
measurement, and the plan's construction — is the remainder. The pin loop's early
refusals add their elapsed time before returning; the settle loop's do not,
though neither occurs in these two cases.

**The plan values did not move.** The retime reports 33.70000 d. The first plot's
coast floor is the live anchor at **16.84485 d**; the plan it returns is the
shorter burn-only crossing at **16.48669 d**, the nav minimum. Both are after
[ADR-019](/decisions/adr-019-station-calls-and-departure.md)'s short escape
break and ballistic climb re-baselined the pre-departure values (Mars 0.3 g
**6.73042 d**, Mars 90 d **44.3543 km/s**) to **16.84485 d** and **38.9678
km/s**. The flight suite enforces the new ones; this step changed no tolerance,
gate, pass cap, or returned plan.

The diagnostic is `TripPlanner.Metrics` and `PlanMetrics`; the measurement is
`RouteBuildMetricsTests`, which prints this split and asserts only structure and
the Mars floor, never a time.

## The route benchmark (2026-11-24)

The route-cost plan's steps 3 and 4 change how a plot, a retime, and Begin are
built, so they need a repeatable instrument that can tell a real change from the
editor's own spread. `tools/bench.sh` is that instrument. It is **not part of
`tools/check.sh`**: it is an editor `-executeMethod` entry point
(`PlanningBenchmark.Run`), and the default check runs `-runTests` only, so a
default check cannot start it. Step 2 also confirmed the default path writes no
benchmark output.

**Command.**

```bash
tools/bench.sh              # run and compare against the committed baseline
tools/bench.sh --record     # run, show the deltas, then replace the baseline
```

**Machine and runtime.** Apple **M4 Max** (16 cores, 64 GB), macOS 27.2.1, Unity
**6000.3.28f1** editor in batch mode (`-nographics`), **Mono**, in the warm test
project. These are editor/Mono numbers. The player's is a Play Mode number with
the editor attached; this step did **not** measure the Play Mode figure, so it
claims no ratio. The player's ~4 s first plot is close to the Earth→Mars plot
below (4.1 s), but the headless Begin there is **9.0 s**, not ~4 s, so the two
numbers are not the same measurement yet.

**Warm-up rule.** One untimed call per case, then three measured runs; the
reported number is the **median of three**. The planner, its `GravityField`, and
the session live across all six routes, so one-time field setup is not charged to
a measured call. Two repeat runs against a baseline recorded in the same session
moved every measured phase by **at most 2.5%**, and most by under 1%: that is
the instrument's noise floor here. A change smaller than a few percent needs
several runs to see. The suite records times only; step 1's split above already
carries the allocation for both calls.

**Cases.** Epoch JD 2461359.5, conic capture. The Earth departures start on the
**Gateway** station, the anchor every earlier measurement uses. The reverses
start in the planet's **conic parking orbit**, with the orbit's phase prograde
relative to the planet's own flight. All routes use the **0.3 g** default except
Venus→Earth and Uranus→Earth at **1 g**: 0.3 g cannot lift out of those wells.
The phases are:

* **Plot** — `PlanFastest` followed by the cold window build
  (`FlightSession.MaximumPreviewSeconds`), the exact pair `WindowFor` runs. It
  bypasses the session's window cache, so it is the player's first plot, not a
  repeat selection.
* **Retime mid / max** — one `PlanAtDuration` at the window's middle and at its
  maximum (the slider's right end).
* **Begin** — plot on the session at the epoch, advance the clock **three
  hours**, then call `FlightSession.Begin` itself. Until
  [step 5](#step-5-begin-flies-the-fresh-preview-2026-11-25) that was
  `PlanFastest` at the moved date followed by `PlanAtDuration` at the window
  maximum; since step 5 it is the reuse decision plus whatever it forces. The
  plot that sets the preview up is untimed: the plot phase already measured it,
  and the session's window cache makes the repeat free.
* **Begin @max** — the same, after a retime of the preview to the window's
  right end, which is the player's longer trip.

All six routes, median milliseconds with the repeat run's delta in each cell and
the sum of the measured phases in the last column:

| Route | Level | Plot | Retime mid | Retime max | Begin | Begin @max | Begin total | Total |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Earth → Mars | 0.3 g | **4,121** (+2.5%) | 468 (+2.1%) | 377 (+2.3%) | **9,024** (+1.2%) | 347 (+1.7%) | **9,370** (+1.2%) | +1.5% |
| Earth → Venus | 0.3 g | **9,663** (+0.7%) | 475 (+1.0%) | 466 (+0.9%) | 4,326 (+0.5%) | 481 (+1.0%) | 4,807 (+0.4%) | +0.6% |
| Earth → Uranus | 0.3 g | **2,046** (+0.4%) | 494 (+0.8%) | 482 (+1.2%) | 1,656 (+0.6%) | 489 (−0.1%) | 2,145 (+0.4%) | +0.5% |
| Mars → Earth | 0.3 g | 396 (+0.5%) | — | — | — | — | — | +0.5% |
| Venus → Earth | 1.0 g | 413 (+0.5%) | — | — | — | — | — | +0.5% |
| Uranus → Earth | 1.0 g | 492 (−0.2%) | — | — | — | — | — | −0.2% |

**The three reverses cannot plot at this epoch**, so their retime and Begin
phases are not measured; a refused plot has no window to slide. The refusals are
recorded, not tuned away:

* **Mars → Earth** refuses in the coast floor search. Its duration seed is
  **4.36 d** (it scales with the square root of the planet-to-planet distance),
  the search grows in twelve 20% steps to **38.84 d**, and every probe's short
  arc is retrograde (`transfer angle 21.0°`). A fixed **90 d** build is
  **feasible** (90.00000 d), so the trip exists and the floor search's growth cap
  cannot reach it. This is a planner gap, not a benchmark input.
* **Venus → Earth** refuses the same way at a **1.0°** short arc, and a fixed
  90 d build is refused too (*the approach conic cannot reach the handover
  ceiling from the brake's start*). No root cause is established.
* **Uranus → Earth** refuses with *no room for the break and the escape climb* at
  every probe up to **143.6 d**, and at 90 d. A departure from Uranus's parking
  orbit must climb through the giant's very large sphere before the departure
  burn, which does not fit a trip of that length.

The Earth plots show the cost the player pays. **Earth → Venus is the slowest at
9.7 s**, and **Begin at Earth → Mars was 2.2× its plot (9.0 s against 4.1 s)**:
the coast floor's certificate refused once at the moved date and the full-field
continuation was paid, where the epoch's plot certifies first time. That second
wait is gone: [step 5](#step-5-begin-flies-the-fresh-preview-2026-11-25) made
Begin fly the drawn plan inside the preview's freshness window. The retimes
are 0.37–0.49 s at every measured point, so a slider drag is an order of
magnitude cheaper than a plot.

**The plans did not move.** Earth → Mars still plots the nav minimum at
**16.48669 d** over the **16.84485 d** coast floor; Earth → Venus is 14.10909 d,
Earth → Uranus 92.13088 d, and the window maxima are 49.46, 42.33, and 276.39 d.
No tolerance, gate, pass cap, seed, or returned plan changed. The one production
edit is visibility: `FlightSession.MaximumPreviewSeconds` is `internal` and the
Simulation assembly grants `InternalsVisibleTo("TimeToExplore.Editor")`, so the
benchmark times the real probe loop instead of a copy that can drift.

The baseline is `tools/planning-benchmark-baseline.json`: machine, Unity version,
date, and the per-route per-phase milliseconds. A run whose machine or Unity
version differs says so and states that its deltas compare the machines, not the
code. The report stays quiet below a **25%** rise and lists every phase at or
above it. The threshold is not a gate in `tools/check.sh`.

`PlanningBenchmarkTests` (the `core` category) covers the parts that must not
drift: every route resolves its bodies and departs from the right place, the
baseline round-trips and refuses a malformed one, a baseline from another machine
or Unity version is called not comparable, the delta is a percentage, only a 25%
rise is a regression, and the table shows each phase's milliseconds and delta,
plus the summed total. It never asserts a time.


## Step 3: warm-starting the pin (2026-11-24)

The pin is the brake/departure fixed point: the brake's duration and thrust
direction, the departure burn's duration and direction. Every build started it
from a **kinematic estimate** — a brake one second long aimed retrograde, and a
departure burn the reference coast's own speed change implies — and walked in
from there. Step 1 measured that walk as **71% of a retime** and **half of a
first plot**, with **162 of the plot's 174 passes** inside the floor search's
fast probes. A neighbouring solved trip already knows the answer, so step 3
starts the pin from it.

`PinSeed` is a value type carrying those four numbers and the duration they
converged at. It deliberately **does not carry the escape ray**: the climb is
derived from the time budget on every pass, and the ray is what decides which
basin the pin settles in. A build that closed its chain returns the pin it
settled on (`BuildResult.Seed`, and `TrajectoryPlan.PinSeed`), so one build can
hand it to the next; a refusal or a stretched plan carries none. `Build(...)`,
both burn–coast–burn builders, and `PlanAtDuration` take an optional seed, and a
seeded pass 0 also **flies the planetocentric approach** when the seeded brake is
long enough to reach the ceiling, instead of dropping it as a cold pass 0 must.

### The guards

A seeded attempt is a **solver budget**, not a relaxed answer. It runs a few
passes — **6** for a certified build (`SeedOuterPasses`) and **2** for a probe
(`ProbeSeedOuterPasses`) — and if it has not produced the answer by then the
build is repeated cold at the normal cap, with the certified path's own
12 → 24-pass retry. The tolerances, the chain gate, and the cold caps do not
move. A certified attempt is accepted when it **closed its chain**: a cold build
whose pin plateaus above its own tolerance is accepted on exactly those terms. A
**probe** is accepted only when its pin **settled**, because a probe's verdict is
what the floor search decides on.

The guards that were needed, and why:

* **The floor search's fast probes are not seeded.** A seed lets a stalled fast
  probe close, and that moves the floor's frontier: measured at Uranus, the fast
  search walked its high from **92.13088 d to 91.07860 d**, the certificate
  refused there, and the continuation found nothing, so `Plan()` returned a
  refusal. **The floors are a hard gate**, so only the **full-field
  continuation** is seeded; it runs above a refused certificate and its probes
  are the expensive ones (64-sample solve, 512-sample field brake). With that
  scope the three measured floors are identical before and after, on the same
  tree either side of the change: Mars **16.84485 d**, Venus **14.31827 d**,
  Uranus **92.13088 d**.
* **No seed from a refusal, a stretch, or across the family boundary.** Only a
  plan that closed its chain carries one. The flight session does not seed the
  **two lowest slider steps** (`SeedExcludedLowFraction`, 2/63, pinned against
  the HUD's 64-step control), because the window's minimum is where the
  burn-only crossing and the short coast meet the coast profile.
* **A seed only starts a nearby trip.** `PinSeed.MaxRelativeGap` is 20% of the
  duration it converged at — the floor search's growth step — and a miss is the
  cold start.

### Where it is used

* `TripPlanner.SearchFloor`'s `Probe(seconds)` closure keeps the last feasible
  probe's seed and hands it to the next **continuation** probe.
* `FlightSession` stores the seed with each `CachedRetime` and with the window's
  floor plan (`PreviewWindow.FloorSeed`). A retime build starts from the
  **nearest** cached entry's seed, else the floor plan's, else cold. The nearest
  entry is the cache's own job; the session does not add matching logic.

### The measurements

Beats at JD 2461359.5 from Gateway, conic capture, 0.3 g, in the warm EditMode
project (editor/Mono). Cold and seeded are the **same duration**, one slider
step apart — a neighbouring cache entry, which is the player's drag.

| Planet | Duration | Cold passes / wall | Seeded passes / wall | Delta-v cold / seeded | Worst arc end |
|---|---:|---|---|---|---|
| Mars | 27.47782 d | 12 / 491 ms | **6 / 330 ms** | 245.7294 / 245.7294 km/s | 1.3 m / 1.9 × 10⁻⁴ m/s |
| Venus | 23.51515 d | 12 / 471 ms | **6 / 340 ms** | 123.2405 / 123.2405 km/s | 0.13 m / 7.2 × 10⁻⁶ m/s |
| Uranus | 153.55147 d | 9 / 482 ms | **6 / 376 ms** | 461.8193 / 461.8193 km/s | 1.2 m / 1.5 × 10⁻⁵ m/s |
| **Total** | | **33 / 1,444 ms** | **18 / 1,046 ms** | | |

That is **45.5% fewer pin passes** for the same three durations, and **28% less
wall time** (1,444 → 1,046 ms; 33%, 28% and 22% per planet). The plans are the
same trip: delta-v equal to four decimals, the same arcs, and every arc end
inside the 1 km / 1 m/s gate. The session's own path measures the same:
`SetPreviewDuration` at one step past a cached plan takes **6** passes against a
cold build's **12**.

The equality sweep covers more than one step. Six slider positions — the
minimum, steps 1, 2, 7, 31 and the maximum — at Mars, Venus and Uranus, cold
against seeded, give identical delta-v at every one. The worst arc-end
difference in the sweep is **120.0 m / 1.3 × 10⁻² m/s** (Mars step 7); Uranus
stays under **5.8 m / 5 × 10⁻⁵ m/s**. A deliberately wrong seed (a brake that
would start before the departure, a burn from no trip) is abandoned after a few
passes and returns the cold plan exactly, with the extra cost of those passes.

**The plot does not win.** Below the floor every fast probe refuses, so there is
nothing to seed from until the first feasible probe — which is the floor itself.
From there, every probe that could be seeded is one whose verdict decides the
floor, and accepting a seed-changed verdict moves it. The continuation is seeded
and settles rarely, so the plot moves only by noise: `RouteBuildMetricsTests`
reports the same **174 pin passes (162 fast, 12 certified)** as step 1 before
the change, and the benchmark's three plots are inside the repeat spread. The
saving this step banks is the retime, where the pin is **71%** of the wall time
and one slider step is enough.

### Benchmark

`tools/bench.sh`, before (2026-11-24) and after (2026-11-25), median of three,
same machine and Unity version. Milliseconds; the delta is after against before.

| Route | Plot | Retime mid | Retime max | Begin | Begin total |
|---|---|---|---|---|---|
| Earth → Mars | 3,913 → **3,861** (−1.3%) | 448 → **441** (−1.6%) | 356 → **352** (−1.1%) | 8,759 → **8,989** (+2.6%) | 9,097 → **9,317** (+2.4%) |
| Earth → Venus | 9,465 → **9,669** (+2.2%) | 468 → **457** (−2.4%) | 459 → **449** (−2.2%) | 4,270 → **4,187** (−1.9%) | 4,743 → **4,650** (−2.0%) |
| Earth → Uranus | 2,028 → **1,989** (−1.9%) | 486 → **477** (−1.9%) | 476 → **466** (−2.1%) | 1,646 → **1,607** (−2.4%) | 2,130 → **2,083** (−2.2%) |
| Mars → Earth | 393 → **383** (−2.5%) | — | — | — | — |
| Venus → Earth | 408 → **399** (−2.2%) | — | — | — | — |
| Uranus → Earth | 491 → **480** (−2.2%) | — | — | — | — |

Every phase is inside the instrument's **2.5%** repeat spread, and the after run
is also about 2–3% faster than the committed baseline on phases this step does
not touch (the three reverse plots). **No phase changed beyond noise.** The
benchmark's retimes sit at the window's middle and maximum — **33.0 d and
49.5 d** at Mars — which is outside the seed's 20% cover, so they are cold by
construction; the player's drag is **1.6%** per step and is the path the pass
counts measure. Reading the benchmark as evidence that the retime did not
improve would be a mistake; reading it as evidence that the plot did not improve
is correct.

## Step 4: prefetching the slider's neighbours (2026-11-25)

The cache made a step the player had already waited for free, and the warm start
made the next step's build cheaper, but a **new** step was still one build away.
This step fills the slots the player is about to ask for, on the same worker, so
a drag publishes from the cache instead of building.

**The policy.** A slider step is `(PreviewMaximumSeconds −
PreviewMinimumSeconds) / 63`, because the HUD offers **64** whole steps across
the window. After a preview publishes, the session queues **two steps each side**
of the published duration; a fresh plot starts at the window minimum, so its two
lower steps clamp away and nothing is built below the minimum. While the player
drags, each accepted ask queues **three steps in the direction of travel**. The
direction is inferred inside `FlightSession`: the sign of the new ask's step
index against the last accepted ask's, so `RequestPreviewDuration` keeps its
signature and the HUD passes nothing extra. The index is clamped to
`0…63`; a step outside the window is skipped, never faked.

**The priority rule.** Prefetch is the **lowest** priority on the one background
worker, which already builds retimes and previews:

* the worker takes a queued retime, then a preview, then **one** prefetch item,
  in that order, and re-checks the queues between items;
* a real request clears the queued prefetch at once — a retime, a rebuild, a
  plot, a synchronous retime, a discard, a style or burn-level change, and any
  departure-date change — so the queue never sits in front of the player's ask;
* a prefetch never publishes and never bumps `_retimeGeneration` or
  `_previewGeneration`. It can only write a cache slot. `IsRetimePending`,
  `PendingPreviewSeconds`, and the published preview are untouched by it.

**The latency tail is one build.** An in-flight build cannot be cancelled, so a
real request that arrives while a prefetch item is building waits for that item
and then its own build. The remainder of the prefetch is at most one build, and a
build's own cost varies with where it sits in the window: measured at Mars, the
neighbour one step above the floor (16.49–17.01 d) takes **942 ms** cold against
**442 ms** at 1.5× the minimum, while the prefetch's own builds in the drag
table's later steps are **343–405 ms**. That is the whole added tail; there is no
second queued prefetch behind it, because the worker takes one item at a time and
any ask clears the rest.

**The inputs are the preview's, frozen.** A prefetch builds from
`_previewDeparture` and from the burn level and arrival style **captured when the
preview was plotted**, not from the live values, and from the window captured at
that publish. A slot filled under different inputs is a slot no retime can read.
Each item also starts from the nearest cached plan's `PinSeed` (step 3's
`RetimeSeed`), so a prefetch costs the warm build; it never starts cold near a
neighbour. Only a **feasible** plan is cached: a step in the short-coast gap that
`PlanAtDuration` refuses leaves no slot, and a later ask builds and reports the
refusal exactly as before.

**The cache bound does not move.** Six extra entries at most (two after a plot,
three after a drag step) against the **64** slots per window, which already
evict the oldest entry. `MaxCachedRetimesPerWindow` stays 64.

### The drag benchmark

`tools/bench.sh` gained a **drag** phase: it plots on the real session, waits for
the plot's own prefetch to settle, then requests **eight consecutive steps from
the window minimum**, each after the previous publish, and times every request to
its publish. The phases are `drag-median` and `drag-worst`; the note records how
many steps were built, how many came from the cache, and how many the prefetch
filled. `TTE_BENCH_NO_PREFETCH=1` times the same drag with prefetch disabled.

The same route set, level, epoch and runtime as the other phases (M4 Max, macOS
27.2.1, Unity 6000.3.28f1 editor in batch mode, Mono, warm test project). Two
runs on 2026-11-25, milliseconds. "Prefetched" counts the neighbours built
*during the drag*; the plot has already queued two more before it starts.

| Route | Prefetch | Median | Worst | Built | From cache | Prefetched |
|---|---|---:|---:|---:|---:|---:|
| Earth → Mars | on | **362** | **376** | **0** | **8** | 6 |
| Earth → Mars | off | 386 | 1,113 | 8 | 0 | 0 |
| Earth → Venus | on | **359** | **405** | **1** | **7** | 5 |
| Earth → Venus | off | 387 | 1,064 | 8 | 0 | 0 |
| Earth → Uranus | on | **363** | **366** | **1** | **7** | 5 |
| Earth → Uranus | off | 372 | 496 | 8 | 0 | 0 |

The eight request-to-publish times show where the win is:

| Route | Prefetch | Steps 1–8 (ms) |
|---|---|---|
| Earth → Mars | on | 1, 0, 376, 372, 374, 362, 360, 360 |
| Earth → Mars | off | 952, 1,113, 388, 382, 386, 371, 365, 368 |
| Earth → Venus | on | 0, 0, 405, 376, 377, 359, 358, 343 |
| Earth → Venus | off | 775, 1,064, 410, 383, 387, 367, 367, 347 |
| Earth → Uranus | on | 0, 0, 366, 360, 362, 363, 363, 365 |
| Earth → Uranus | off | 496, 476, 374, 369, 371, 372, 370, 372 |

**What it buys, and what it does not.** The **first two steps after a plot** are
answered from the cache — 0–1 ms against 775–1,113 ms — and the **worst** step
falls by **62–66%** at Mars and Venus (1,113 → 376 ms and 1,064 → 405 ms) and
**26%** at Uranus (496 → 366 ms). The
expensive step is the one just above the floor, and it is the one the player is
most likely to take first, because the plot starts at the minimum. Every step the
prefetch has not reached still costs about one build: the **median** only moves
2–7%, because a build (≈0.36 s) is about as long as the handle takes to produce
the next step, so the request lands as the prefetch of that same step begins. A
fast continuous drag is unchanged in kind: each request supersedes the last and
the player pays one build after they stop. Six of the eight steps at Mars and
five of the eight at Venus and Uranus were filled by a prefetch *during the
drag*; with the plot's two, seven or eight arrived from the cache, and one step
at Venus and one at Uranus had to build.

**What the player sees.** The handle moves and the route follows on the next
frame for the steps the prefetch has filled — the first two after a plot, and a
small nudge back to a step already visited. The worst-case wait of about a second
at the first step above the floor is gone. The plot itself is unchanged; it pays
two extra background builds (about 1.3 s in total at Mars) **after** the route
appears, on the worker, so the frame does not wait and the plan the plot
published does not move.

**Equality.** Prefetch changes no plan: the tests show a drag to step 1 publishes
the plan built at step 1, a prefetch publishes nothing, and a real retime arriving
mid-prefetch still publishes its own duration, waiting one in-flight build plus
its own. No pin, tolerance, gate, seed, or window rule moved, and the floors are
the step-3 anchors.

## Step 5: Begin flies the fresh preview (2026-11-25)

The benchmark's Begin phase was the largest wait a player met after the plot:
**Earth → Mars 10.1 s against a 4.3 s plot**, Venus 4.6 s, Uranus 1.7 s. The
session already called the preview valid for `PreviewStaleFraction` (1%) of the
flight time — about **4 h** on the Mars minimum — and the benchmark advanced the
clock only **3 h**. The replan was a caution, not a requirement: it reproduced a
plan the HUD was still advertising, and paid a second plot for it.

### Candidate 1, seeding the replan, was measured and dropped

The first try was to carry the preview's pin into Begin's `PlanFastest`, so the
moved-date certificate would close without the full-field continuation. It does
not reach the case:

* At Earth → Mars the preview is the **burn-only crossing**, and that plan carries
  no pin at all (`PinSeed.IsValid == false`); the preview has nothing to offer.
* Taking the **epoch coast floor's** own pin instead (16.84485 d) and seeding the
  moved-date certificate made Begin **slower**: **11,161 ms against 9,785 ms**,
  with **5 seed fallbacks**, and it still spent 3 certificates and 7 full-field
  probes. The pin does not fit the moved-date basin.
* Uranus, whose preview *is* the coast floor and does carry a pin, moved only
  **1,786 → 1,648 ms** — inside the instrument's spread.

Begin's 10.1 s is the coast floor's continuation at the moved date: inside
`PlanFastest` the coast floor alone was **8.3–9.0 s**, with 3 certificates, 22
fast probes, 7 full-field probes and 1 pin retry. No seed avoids that; the
session should not be replanning at all inside its own freshness window.

### The rule

`FlightSession.Begin` now flies `Preview` itself when all of these hold:

* the preview is for the same target and is feasible;
* the plan's drive matches the planner's current level, and no preview rebuild or
  slider retime is waiting to be published;
* the clock is within the session's own freshness window:
  `0 ≤ now − PreviewJulianDate ≤ PreviewStaleAfterDays`.

The date window **is** the deadband. An exact date match would throw the drawing
away on the next frame, and a second position deadband would be redundant: the
only way an idle ship moves between plot and Begin is the coast that same window
bounds, and a place, teleport, cancel, style change or level change discards or
rebuilds the preview. Begin therefore **adopts** `Preview.DepartureState` — the
ship returns to the state the plan was certified from — rather than comparing
the current state with it. There is no second plot and no re-time: the plan, its
delta-v and its arrival date are the ones already on screen. Past the window
Begin replans exactly as before, which is what the `Preview, stale · replot or
begin anyway` note promises.

The clock is not rewound. The flight starts at the plotted departure and the
first `Advance` carries it to the current date, so at most 1% of the trip passes
in that catch-up. The cached windows stay valid in the reuse branch because the
departure did not move; the replan branch still clears them.

### The benchmark

`tools/bench.sh`'s Begin phase now calls `FlightSession.Begin` itself on a
preview plotted at the epoch, three hours before the moved date, so the phase
measures the player's press rather than a copy of the planner call. The plot and
the retime that set the preview up are untimed, and the session's window cache
makes the repeats free. Both runs are on the same machine, Unity version and warm
test project, on 2026-11-25; milliseconds, median of three.

| Route | Plot | Begin | Begin @max | Begin total |
|---|---:|---:|---:|---:|
| Earth → Mars | 4,324 → 4,319 (−0.1%) | **10,064 → 0** | 285 → 0 | **10,353 → 0** |
| Earth → Venus | 10,669 → 10,668 (0.0%) | **4,573 → 0** | 499 → 0 | **5,068 → 0** |
| Earth → Uranus | 2,164 → 2,181 (+0.8%) | **1,743 → 0** | 523 → 0 | **2,266 → 0** |

Every cell that Begin does not touch is inside the **2.5%** repeat spread. The
Begin itself is one plan adoption and an `Autopilot` construction, which the
instrument reports as **0 ms**. The flown plans are the drawn ones:
16.48669 d, 14.10909 d and 92.13088 d, not the replanned 16.59252, 14.23401 and
87.21140 d, and their arrival dates are the previews' — earlier than a replan
would have given, because the fresh minimum also moves with the date.

### What did not move

No floor, anchor, tolerance, gate, seed, cold pass cap, prefetch policy, or
window rule changed. The three floors are still Mars **16.84485 d**, Venus
**14.31827 d**, and Uranus **92.13088 d**; the six fixed-duration Δv anchors are
unchanged. The stale path is the old code, unchanged, so a stale Begin still
flies the fresh minimum rather than the preview's number. The benchmark's
committed baseline was re-recorded for the new phase meaning, so a later run that
reintroduces a Begin replan reports the full rise instead of hiding it under a
stale phase definition.

# Where it lives

| Concern | Code |
|---|---|
| Begin on a fresh preview | `FlightSession.Begin`'s reuse branch, `FlightSession.CanFlyPreview`, `FlightSession.StartFlight`, `TrajectoryPlan.DepartureState` |
| Fast peak, screen, fast build mode | `TripPlanner.BreakPeakMps2`, `BreakScreenMargin`, `Build(..., fast: true)`, `MeasureFast` |
| Full-field probe, certificate, and the continuation | `TripPlanner.Build(..., probe: true)`, `ProbeDepartureSamples`, `MaxFloorCertificates`, `PlanBurnCoastBurnMinimum`'s certificate loop, `BuildProbeAtDuration` (diagnostic) |
| The certified pin's pass cap and its retry | `TripPlanner.DefaultOuterPasses`, `RetryOuterPasses`, `Build`'s certified burn–coast–burn branch, `BuildResult.PinStalled` |
| Warm start | `PinSeed` (its `MaxRelativeGap` and `Covers`), `TripPlanner.SeedOuterPasses`, `ProbeSeedOuterPasses`, `BuildWithRetry`'s seeded attempt, `BuildBurnCoastBurn`'s seeded pass 0 and approach, `BuildResult.Seed`/`Built`/`PinSettled`, `TrajectoryPlan.PinSeed` |
| Floor search, and the builds it must not repeat | `TripPlanner.SearchFloor` (optional `tried` map and `probe` mode), `FastProbeBurnSamples`, `FastProbeOuterPasses` |
| Full-field burns | `FiniteBurnArc.Create`/`Simulate`/`SimulateBackward`/`SimulateUntilRadius` with the optional `IGravityField`, `TripPlanner.BuildBurnCoastBurn`'s `burnGravity`, `PinnedDepartureSamples`, `BrakeSamples`, `TrySolvePinnedDepartureWithFallback`, `AppendHybridCapture` |
| Cache | `FlightSession.WindowFor`, `TryGetCachedWindow`, `TryGetCachedRetime`, `CacheRetime`, `TryGetNearestRetimeSeed`, `RetimeSeed`, `SeedExcludedLowFraction`, `SetPreviewDuration`'s floor reuse |
| Prefetch | `FlightSession.PlotPrefetchSteps`, `DragPrefetchSteps`, `ArmPrefetch`, `ScheduleDragPrefetch`, `SchedulePrefetch`, `EnqueuePrefetch`, `BuildPrefetch`, `PrefetchRequest`, `PrefetchEnabled` (diagnostic), `IsPrefetchPending`/`QueuedPrefetchSeconds` (diagnostics) |
| Retime and route planning | `FlightSession.RequestPreviewDuration`, `PlotAsync`, `SetArrivalStyle`, `SetBurnLevel`, `SetPreviewDuration`, `TryApplyCompletedRetime`, `TryApplyCompletedPreview`, `TakePreviewFailure`, `PendingPreviewSeconds`, `PlannerLoop`, `BuildRetime`, `BuildPlotWindow`, `BuildRebuild`, `StartPlannerWorker` |
| Handover and teardown | `SolarSystemBootstrap.ApplyRetimeIfReady`, `FlightSession.Dispose` |
| Build metrics | `TripPlanner.Metrics`, `PlanMetrics`, `RouteBuildMetricsTests` |

# Verification

`BurnCoastBurnTripPlannerTests` covers the fast-vs-full peak, the unchanged plan
anchors, the 5% screen accepting no over-thrust break, the just-below floors
still rejected, the floor search's returned plan being **certified** (finite
clearance and measured delta-v on every arc, every seam inside the gate) while a
`BuildProbeAtDuration` probe reports an infinite clearance and no coast delta-v,
plus every reported burn's acceleration being the summed field plus its thrust
and a Sun-only field moving the plan.
`FiniteBurnTests` covers the arc through an SOI against an independent RK4, the
two-body agreement where the third bodies are negligible, and the reference body
being a label. `FlightSessionTests` covers the cache and the background planner,
including latest-wins with a first build in flight, background equals
synchronous, the previous preview staying published, a synchronous retime
superseding an outstanding ask, a discard dropping one, a retime to the floor
reusing the window on both the synchronous and the background paths, and
`Dispose` stopping the worker. `FlightSessionTests` also holds Begin's reuse
contract: inside the freshness window it flies the same plan instance from the
plotted departure with `TripPlanner.PlanFastest` never called, past the window it
replans at the current date and flies the fresh minimum, and a Begin for another
target never flies the preview. `BootstrapSmokeTests` pins the same pair through
the real composition root. `SolarSystemHudTests` and `BootstrapSmokeTests`
pump the real handover through the frame loop. `FlightSessionTests` also holds
the prefetch's contract: a plot fills the two steps above it and none below the
minimum, a drag publishes the prefetched neighbour with no build, a prefetch
never publishes by itself, a discard drops the queue, and an ask arriving
mid-prefetch waits for that one build and its own. `WarmStartTests` holds the warm
start's own contract: cold and seeded builds agree to four decimals of delta-v
and inside the 1 km / 1 m/s gate over six slider positions at Mars, Venus and
Uranus, the seeded retime costs fewer pin passes at the same duration, the
session hands the nearest cached plan's seed over, a wrong seed is abandoned and
returns the cold plan, the three floors are the anchors, and the unseeded slider
fraction is the HUD's. `RouteBuildMetricsTests` prints
the retime and first-plot split — pin passes, brake integration, the reported
512-sample arcs, and allocation — and asserts only the counts, the phases
belonging to the call, and the Mars floor; it never asserts a time. See
[Testing and verification](/technical/testing.md#numerical-references-at-the-epoch).

The design and the numbers for the profile are in
[Burn–coast–burn trips](/simulation/burn-coast-burn.md); the slider's behaviour
is in [The flight session](/technical/flight-session.md#plot-route-then-begin).

## Step 6: the departure split search (2026-11-26)

The departure climb split chooses the escape conic's hyperbolic excess speed
that minimises the plan's total delta-v at the requested duration
([ADR-022](/decisions/adr-022-departure-split.md)). Step 2 costed a candidate
with a **fast probe build** (its own pin loop) and eleven of them took about
**0.6 s** per search. That cannot ship: the floor search calls `Build` on every
probe, so the plot would pay it more than twenty times.

**The rank-only cost.** The geometry every candidate shares — the capture chain,
its entry state, the bands and the departure body — is built once. A candidate
is then costed with **one climb solve and one Lambert solve**: the break's
integrated delta-v against the departure body, the departure burn's change from
the conic's ceiling state to the coast's departure velocity, and the brake's
change from the coast's arrival velocity to the chain's entry velocity. The
capture's own cost is common to the candidates and is left out. The grid is
`0, 1, 3, 6, 10, 16` km/s and one refinement round probes the two midpoints
beside the winner. A search is **4–7 ms**, about 1/100th of the full cost, and
it chooses the full search's excess speed or one grid step away at 8, 18, 30 and
90 days and across sixteen route/duration cases.

**The floor search's two retries.** The much shorter fast end exposed two
solver stalls that the old floors did not:

* a refused certificate is retried as a **certified** build 0.2% and 0.5% above
  before a full-field probe bisection. The fast/full gap at the fast end is a
  fraction of a percent (the full-field break is a hair longer, so the climb
  misses the available time by seconds), and a certificate is an answer, not a
  probe. Mars 0.3 g's floor search fell from 9.4 s to 2.9 s with this retry;
* the pinned departure solve retries **once** from a 1% longer burn when its
  damped Newton stalls with a residual inside ten tolerances. The tolerance and
  the chain gate do not move, and the retry is what lets the cold and seeded
  builds agree at Uranus's fast end. It also found a shorter Uranus floor
  (**37.00540 d** at 0.3 g, 28.18209 at 1 g) after the change.

**The plan moves.** Every coast floor fell: Mars 0.3 g 16.84485 → **7.16751 d**
(1,070.1 km/s), Mercury 1 g 14.06070 → **3.67616** (1,649.4), Venus 0.3 g
14.31827 → **4.20901** (588.3), Jupiter 0.3 g 23.73299 → **13.58298** (2,133.8),
Uranus 0.3 g 92.13088 → **37.00540** (2,004.9) and 1 g 57.02223 → **28.18209**
(2,483.4). The window is three times the new floor, not the planner's true limit:
the planner accepts much longer trips in synodic bands, but growing the window to
that limit gave Mars 7.17–294.92 d and Uranus 38.38–2,086 d and took the retime
and drag phases with it. The burn-only crossing keeps the parabolic climb and is
now the **longer** plan, so the coast floor is the nav minimum and the
short-coast gap is empty.

**Benchmark.** `tools/bench.sh`, before (2026-11-25) and after (2026-11-26),
median of three. Milliseconds; the delta is after against before.

| Route | Plot | Retime mid | Retime max | Drag median | Drag worst | Begin total |
|---|---|---|---|---|---|---|
| Earth → Mars | 4,136 → **3,513** (−15.0%) | 473 → **572** (+21.0%) | 379 → **487** (+28.3%) | 377 → **438** (+16.2%) | 392 → **1,204** (+207.1%) | 0.07 → **0.07** (+4.6%) |
| Earth → Venus | 10,323 → **2,798** (−72.9%) | 487 → **571** (+17.3%) | 485 → **467** (−3.7%) | 376 → **438** (+16.4%) | 420 → **562** (+33.9%) | 4.65 → **4.19** (−10.0%) |
| Earth → Uranus | 2,120 → **3,980** (+87.7%) | 512 → **526** (+2.9%) | 501 → **612** (+22.3%) | 379 → **504** (+33.2%) | 382 → **541** (+41.6%) | 0.06 → **0.06** (−2.1%) |
| Venus → Earth | 431 → **10,234** (new) | — → **36** | — → **750** | — | — | — → **4,697** |
| Uranus → Earth | 513 → **39,026** (new) | — → **665** | — → **614** | — | — | — → **3,026** |

The shape is the split's trade, not a regression in the search. The floor is
half the old one, so the fast-end plans have **longer fixed burns** — at Mars the
7.17-day floor's departure burn and brake are 2.0 and 2.1 days, against about 0.4
and 0.5 at the old 16.8-day floor — and one certified build of them costs more.
The retimes at the new window's middle (14.3 d at Mars, against 33.7 before) and
the drag's first steps sit in that regime. The Uranus plot pays a harder floor
search at 37 d (the fast model cannot place the departure burn at many probes
until the bracket reaches the floor). The two reverse routes were **refused** by
the old profile and are now real plots: a new capability, not a slower refusal.
The three-hour `Begin` staleness is the benchmark's stress gap: at a 7.17-day
minimum, 3 h is more than the preview's 1% freshness, so `Begin` replans. The
player's real preview is minutes old.

The committed baseline was re-recorded for the new floors, the new window and
the newly feasible reverses. The search itself is faster; a phase whose duration
changed is compared with a different build. The 25% threshold still guards the
search cost, and the search's own line is the **4–7 ms** rank-only choice, which
is far inside it.
