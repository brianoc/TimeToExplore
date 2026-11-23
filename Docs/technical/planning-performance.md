---
type: Architecture
title: Planning performance
description: The two-tier planning model — a fast search-only gravity model in front of the unchanged full-field plan and flight — the 5% thrust screen, the floor and retimed-plan caches, and the latest-wins background planner (retimes and route plans).
tags: [technical, performance, planning, navigation, async]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-23T19:20:27Z }
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
is exactly the work a returned handle would otherwise repeat. The newest
**sixty-four** per window are kept, and a repeat ask is matched when it lies
within **0.2% of the window**, because a continuous slider put back near its old
position is not bit-identical; that is under a pixel of the track and far below
a physical difference in the route. A plan is about **50 KB**, dominated by the
departure burn's and brake's 512-sample interpolation tables at 24 bytes a
sample, so the bound is about **3 MB per window**. Both the synchronous
`SetPreviewDuration` and the slider's `RequestPreviewDuration` read the cache
before queueing work, and a hit is published directly, so a duration the player
has already waited for costs a dictionary lookup instead of another multi-second
build. The cache shares the window's lifetime: a changed departure date, a
place/teleport, or clearing the windows drops it.

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
   The first plot goes through `FlightSession.PlotAsync`, and the style and
   burn-level changes through `SetArrivalStyle` and `SetBurnLevel` — the same
   request queue.
3. A build that finishes after a newer request arrived is discarded, and the
   newest request is built next. A drag therefore never queues work behind
   stale plans; a second plot, rebuild or style change also makes the
   outstanding builds stale, so the newest ask wins across the pipelines.
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
and the merged tree meets it: the five cases are 1.28–3.03 s (a second run gave
1.98, 1.30, 3.02, 2.29, and 3.05 s). .NET's `Plan()` on the
same tree is 0.21–0.38 s, so the editor's remaining gap is per-build cost, not
search shape.

**Since step 3 of the burn-only plan the preview window also solves the
moving-target minimum** (`PlanFastest`: the coast floor plus the zero-coast
crossing), so the same five first plots are **2.81, 3.08, 5.07, 3.89, and 4.61 s**
in that order — the added 1–2 s is the crossing, not a longer floor search. The
floors and the .NET `Plan()` costs above are unchanged; Uranus does not win the
zero-coast branch but pays for the attempt.

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


# Where it lives

| Concern | Code |
|---|---|
| Fast peak, screen, fast build mode | `TripPlanner.BreakPeakMps2`, `BreakScreenMargin`, `Build(..., fast: true)`, `MeasureFast` |
| Full-field probe, certificate, and the continuation | `TripPlanner.Build(..., probe: true)`, `ProbeDepartureSamples`, `MaxFloorCertificates`, `PlanBurnCoastBurnMinimum`'s certificate loop, `BuildProbeAtDuration` (diagnostic) |
| The certified pin's pass cap and its retry | `TripPlanner.DefaultOuterPasses`, `RetryOuterPasses`, `Build`'s certified burn–coast–burn branch, `BuildResult.PinStalled` |
| Floor search, and the builds it must not repeat | `TripPlanner.SearchFloor` (optional `tried` map and `probe` mode), `FastProbeBurnSamples`, `FastProbeOuterPasses` |
| Full-field burns | `FiniteBurnArc.Create`/`Simulate`/`SimulateBackward`/`SimulateUntilRadius` with the optional `IGravityField`, `TripPlanner.BuildBurnCoastBurn`'s `burnGravity`, `PinnedDepartureSamples`, `BrakeSamples`, `TrySolvePinnedDepartureWithFallback`, `AppendHybridCapture` |
| Cache | `FlightSession.WindowFor`, `TryGetCachedWindow`, `TryGetCachedRetime`, `CacheRetime`, `SetPreviewDuration`'s floor reuse |
| Retime and route planning | `FlightSession.RequestPreviewDuration`, `PlotAsync`, `SetArrivalStyle`, `SetBurnLevel`, `SetPreviewDuration`, `TryApplyCompletedRetime`, `TryApplyCompletedPreview`, `TakePreviewFailure`, `PendingPreviewSeconds`, `PlannerLoop`, `BuildRetime`, `BuildPlotWindow`, `BuildRebuild`, `StartPlannerWorker` |
| Handover and teardown | `SolarSystemBootstrap.ApplyRetimeIfReady`, `FlightSession.Dispose` |

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
`Dispose` stopping the worker. `SolarSystemHudTests` and `BootstrapSmokeTests`
pump the real handover through the frame loop. See
[Testing and verification](/technical/testing.md#numerical-references-at-the-epoch).

The design and the numbers for the profile are in
[Burn–coast–burn trips](/simulation/burn-coast-burn.md); the slider's behaviour
is in [The flight session](/technical/flight-session.md#plot-route-then-begin).
