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

The reported plan's fixed burns integrate the summed field, and that is the new
cost floor. Measured at the epoch in a .NET harness over the same sources, with
the two-body build it replaced in brackets: a 90-day Mars build **0.47 s**
(0.05 s), the Mercury 8-day build 1.1 s, `Plan()` at the Mars floor **2.0 s**
(0.5 s) and at the Mercury floor 2.4 s, and a slider retime 0.7–1.4 s. The editor
runs the same work several times slower: in the Unity suite the burn–coast–burn
class is a **3-minute** silent stretch, the near-limit screen sweep (four Mercury
builds) is ~100 s, the Mars floor test 69 s, and the whole flight category about
10 minutes, which is why the guard watches for eight minutes of log silence.
Almost all of it is `GravityField.Prepare`: a full-field burn step re-selects the
active set once per RK4 step, and that evaluates the whole catalogue's ephemeris,
so a step costs about fifty times a two-body step. The plans, floors, seams, and
window did not move ([full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md)).
A cheaper prepare rule is the obvious lever for the performance step; it must be
measured against the per-step rule.

## One burn–coast–burn build, measured (2026-11-20)

An editor harness on the warm test project, Mars at 0.3 g, Gateway departure,
burn–coast–burn cruise with a conic capture, put hard numbers on the shape of
the cost. A full-field build runs the pinned brake/departure fixed point to
twelve passes (the certified tolerances are 1e-7 m/s and 1e-6 s), and each pass
interpolates the brake at 2,048 samples and the departure burn at 512. That is
about **1.2 s per pass, ~15 s per build**. A floor search pays for a bracket of
those builds, so a floor plot was **72 s** and a burn-level change **80 s**.

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

## The Uranus floor and its conservative bracket (2026-11-20)

The floor search's bracket is conservative at the giant planets for a cost
reason, not a physical one. Measured at the epoch (2026-11-15) from Gateway in
the .NET harness — burn–coast–burn cruise, conic capture, `TTE_FLOOR=1`, Debug
build, the same sources as the suite:

| Case | `Plan()` | Floor | Peak | Direct builds below the floor |
|---|---:|---:|---:|---|
| Uranus, 1 g | **71.1 s** | **33.99769 d**, 2,047.44 km/s | 9.700 m/s² | 33.48773 d closes (5.9 s, 2,081.11 km/s); 33.6577 and 33.8277 d stall at 1.362 × 10⁻⁵ and 1.350 × 10⁻⁵ m/s. The planetocentric arrival leg moves it to **33.89968 d**, 2,053.82 km/s, eight arcs |
| Uranus, 0.3 g | **59.2 s** | **41.57095 d**, 1,756.59 km/s | 2.942 m/s² | 41.36309 and 40.94738 d close (4.8 s each, 1,767.17 and 1,788.76 km/s); 41.1552 d stalls at 3.039 × 10⁻⁵ m/s |
| Mars, 0.3 g | **27.5 s** | **6.73715 d**, 1,172.48 km/s | 5.137 m/s² | 6.7035 d misses the brake chain by 6,182.2 m / 0.6 m/s, 6.6698 by 1,651.5 m / 0.2, 6.6361 by 9,254.9 m / 0.9; at 6.535 d the break needs 14.2 m/s² |
| Mercury, 1 g | **7.8 s** | **3.21610 d**, 1,765.52 km/s | 9.700 m/s² | 3.1839 d needs a 10.3 m/s² break, rising to 275.0 m/s² at 3.0875 d |

The Uranus gap is **0.51 d (1.5%)** at 1 g — 34.00 against 33.49 d — and
**0.62 d (1.5%)** at 0.3 g, 41.57 against 40.95 d. The lower probes are not
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
3 × the floor, so Uranus at 1 g offered **34.00–101.99 d** before the arrival leg
and offers **33.90–101.70 d** after it. A direct build at the old maximum
(101.99307 d) was accepted: **seven arcs, 661.35 km/s**, a **9.105 h**
departure burn and a **9.185 h** brake. The post-approach build at 101.69 d is
**eight arcs and 661.3 km/s**, and its brake also starts inside the ceiling. Its
`PlanAtDuration` is **2.7 s**; the
36 s harness run around it also reflies both burns under the six alternative
steering laws, so that is the report, not the build. The slider's right end
never asks for a refused plan. All timings here are .NET-harness numbers; the
editor runs this work several times slower, so a first Uranus preview is a
multi-minute freeze until step 7.

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
superseding an outstanding ask, a discard dropping one, and `Dispose` stopping
the worker. `SolarSystemHudTests` and `BootstrapSmokeTests` pump the real
handover through the frame loop. See
[Testing and verification](/technical/testing.md#numerical-references-at-the-epoch).

The design and the numbers for the profile are in
[Burn–coast–burn trips](/simulation/burn-coast-burn.md); the slider's behaviour
is in [The flight session](/technical/flight-session.md#plot-route-then-begin).
