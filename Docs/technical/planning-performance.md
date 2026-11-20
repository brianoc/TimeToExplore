---
type: Architecture
title: Planning performance
description: The two-tier planning model — a fast search-only gravity model in front of the unchanged full-field plan and flight — the 5% thrust screen, the floor cache, and the latest-wins background retime.
tags: [technical, performance, planning, navigation, async]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-20T00:00:00Z }
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

The bracket is a probe just above the kinematic estimate, a 15% walk-down, and a
bisection to 0.05% (or 30 s). A refused probe is retried 0.2% higher, because
the near-floor basin has narrow non-convergence holes; that retry is the
search's real resolution. The candidate is rebuilt at 512 samples with the full
field and certified before it leaves the planner; if the fast model was
optimistic the search finishes with full-field builds. The closure gate is
unchanged: 1 km of position and 1 m/s of velocity.

`FlightSession.WindowFor` caches the floor plan and the trip-time window per
`(departure date, target, burn level, cruise style, arrival style)`. A repeat
plot reuses both; a changed departure date, or a place/teleport, clears the
cache. This is what makes a repeat selection free rather than merely fast.

# The background retime

The retime is one full-field `PlanAtDuration` build (95–125 ms at the epoch),
which is over the 33 ms threshold the plan set for staying synchronous. It runs
on **one managed worker thread** with latest-wins semantics:

1. Moving the slider calls `FlightSession.RequestPreviewDuration(seconds)`. It
   clamps the duration to the preview window, stores it as the newest request
   with a generation number, pulses the worker, and returns. The frame does not
   wait.
2. The worker builds the plan with its **own** `TripPlanner` and `GravityField`.
   The simulation's field keeps a prepared active set and stays on the main
   thread; the worker reads only the catalogue (elements and `ComputeState`),
   never `FlightSession`'s `_gravity`, the integrator, or the autopilot.
3. A build that finishes after a newer request arrived is discarded, and the
   newest request is built next. A drag therefore never queues work behind
   stale plans.
4. `TryApplyCompletedRetime` publishes under the request lock, checked against
   the generation. `SolarSystemBootstrap.ApplyRetimeIfReady` is pumped once per
   frame by the HUD and hands the plan to the trajectory line and the report on
   the main thread.

The previous **full-field** preview stays on screen until the replacement lands.
`PendingPreviewSeconds` keeps the handle and its label on the player's ask
rather than snapping back to the older published plan. Nothing fast is ever
shown.

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
**80 s**.

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
numbers: that is the performance step's decision, not a trim.

## The Uranus floor and its conservative bracket (measured before step 7)

The floor search's bracket is conservative at the giant planets for a cost
reason, not a physical one. The table below is what **step 6** measured, before
step 7 changed the refresh cadence; the current floors are in
[step 7](#step-7-the-levers-measured-2026-11-20). Measured at the epoch
(2026-11-15) from Gateway in the .NET harness — burn–coast–burn cruise, conic
capture, `TTE_FLOOR=1`, Debug build, the same sources as the suite:

| Case | `Plan()` | Floor | Peak | Direct builds below the floor |
|---|---:|---:|---:|---|
| Uranus, 1 g | **71.1 s** | **33.99769 d**, 2,047.44 km/s | 9.700 m/s² | 33.48773 d closes (5.9 s, 2,081.11 km/s); 33.6577 and 33.8277 d stall at 1.362 × 10⁻⁵ and 1.350 × 10⁻⁵ m/s. After step 7's cadence and the approach leg together the search returns **33.72 d**, 2,065.6 km/s, eight arcs |
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
report, not the build). Step 7 and the approach leg re-measured the floor, so the window is
**33.72–101.16 d** at 1 g and **42.54–127.62 d** at 0.3 g. Direct builds at both
maxima are accepted, eight arcs, 666.80 km/s at 1 g and 530.62 at 0.3 g. The
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
floor 0.86 d shorter at 1,801.17 km/s. The approach leg then moved them again:
the current floors are **33.72 d** and **42.54 d** (see
[the post-close re-measure](#post-close-re-measure-step-4b-merged-2026-11-20)).
These are re-measured anchors, not errors: the floor is the first duration the
certified full-field build closes at, and the basin is noisy. `Plan()` is now 2.6–6× faster at Mars, Mercury and Uranus 1 g, and 24× at
Uranus 0.3 g partly because its floor moved.

The 0.3 g floor is a **needle**, and that matters for the slider. At step 7 its
plan was certified at 40.71332 d while a cold `PlanAtDuration(40.71332 d)` was
refused, 1.039 × 10⁻⁵ m/s short — 4% over `DepartureSolveToleranceMps` — because
the search's 0.2% retry found a feasible point only about **40 ms** wide inside
a hole. After the approach leg the returned floor is **42.54 d**; the basin is
still needle-shaped, so a cold rebuild at the returned duration can refuse even
though the plan is certified. The returned plan's seams are inside the gate. The background retime used to rebuild a
floor request and would have published that refusal; it now hands the window's
stored floor plan to the publisher, mirroring `SetPreviewDuration`'s reuse, so a
drag to the left end is free and cannot refuse. The rest of the window keeps the
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

## Post-close re-measure (step 4b merged, 2026-11-20)

The **planetocentric approach leg** landed after the plan closed and was merged
on top of step 7. It changes the arrival the near-floor basin first closes at,
so the floors moved again. On the combined build, measured at the epoch from
Gateway with the conic capture:

| Case | Current floor | Total Δv | Window |
|---|---:|---:|---|
| Uranus, 1 g | **33.72 d** | 2,065.6 km/s | 33.72–101.16 d |
| Uranus, 0.3 g | **42.54 d** | 1,708.9 km/s | 42.54–127.62 d |
| Mars, 0.3 g | 6.74 d | 1,172 km/s | 6.74–20.21 d |
| Mercury, 1 g | 3.22 d | 1,766 km/s | 3.22–9.65 d |

Direct builds at both Uranus maxima are accepted: eight arcs, **666.80 km/s** at
1 g and **530.62** at 0.3 g, so the slider's right end is feasible. The
fixed-duration plans are unchanged to the printed precision — Uranus 400 d at
1 g is **182.3 km/s** (break 4.05, departure burn 90.17, brake 81.39, capture
6.37 and 0.32), with a 7.544-day approach from the ceiling to 368,111 km; Mars
90 d is 44.4 and Mercury 8 d 557.0. The near-floor basin is still needle-shaped:
the returned duration is a certified plan, not a smooth boundary, so the
background retime keeps reusing the window's stored floor plan.

The **flight suite** on the combined tree (`tools/check.sh --tests flight`)
passed in **256 s**, faster than step 7's 242 s flight category plus its setup
because the near-floor basin is different, and still well inside the old
multi-minute freeze. The tables above are step 7's measurements **before** the
approach leg; this section carries the current anchors.

# Where it lives

| Concern | Code |
|---|---|
| Fast peak, screen, fast build mode | `TripPlanner.BreakPeakMps2`, `BreakScreenMargin`, `Build(..., fast: true)`, `MeasureFast` |
| Floor search, and the builds it must not repeat | `TripPlanner.SearchFloor` (optional `tried` map), `FastProbeBurnSamples`, `FastProbeOuterPasses` |
| Full-field burns | `FiniteBurnArc.Create`/`Simulate`/`SimulateBackward`/`SimulateUntilRadius` with the optional `IGravityField`, `TripPlanner.BuildBurnCoastBurn`'s `burnGravity`, `AppendHybridCapture` |
| Cache | `FlightSession.WindowFor`, `TryGetCachedWindow`, `SetPreviewDuration`'s floor reuse |
| Retime | `FlightSession.RequestPreviewDuration`, `SetPreviewDuration`, `TryApplyCompletedRetime`, `PendingPreviewSeconds`, `RetimeLoop`, `BuildRetime`, `StartRetimeWorker` |
| Handover and teardown | `SolarSystemBootstrap.ApplyRetimeIfReady`, `FlightSession.Dispose` |

# Verification

`BurnCoastBurnTripPlannerTests` covers the fast-vs-full peak, the unchanged plan
anchors, the 5% screen accepting no over-thrust break, and the just-below floors
still rejected, plus every reported burn's acceleration being the summed field
plus its thrust and a Sun-only field moving the plan.
`FiniteBurnTests` covers the arc through an SOI against an independent RK4, the
two-body agreement where the third bodies are negligible, and the reference body
being a label. `FlightSessionTests` covers the cache and the background retime,
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
