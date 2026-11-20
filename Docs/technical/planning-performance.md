---
type: Architecture
title: Planning performance
description: The two-tier planning model — a fast search-only gravity model in front of the unchanged full-field plan and flight — the 5% thrust screen, the floor cache, and the latest-wins background retime.
tags: [technical, performance, planning, navigation, async]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
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
class is a **4-minute** silent stretch, the near-limit screen sweep (four Mercury
builds) is 80 s, the Mars floor test 34 s, and the whole suite about 9 minutes,
which is why the guard watches for eight minutes of log silence. Almost all of it
is `GravityField.Prepare`: a full-field burn step re-selects the active set once
per RK4 step, and that evaluates the whole catalogue's ephemeris, so a step costs
about fifty times a two-body step. The plans, floors, seams, and window did not
move ([full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md)).
A cheaper prepare rule is the obvious lever for the performance step; it must be
measured against the per-step rule.

# Where it lives

| Concern | Code |
|---|---|
| Fast peak, screen, fast build mode | `TripPlanner.BreakPeakMps2`, `BreakScreenMargin`, `Build(..., fast: true)`, `MeasureFast` |
| Floor search | `TripPlanner.SearchFloor`, `FastProbeBurnSamples`, `FastProbeOuterPasses` |
| Full-field burns | `FiniteBurnArc.Create`/`Simulate`/`SimulateBackward`/`SimulateUntilRadius` with the optional `IGravityField`, `TripPlanner.BuildBurnCoastBurn`'s `burnGravity`, `AppendHybridCapture` |
| Cache | `FlightSession.WindowFor` |
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
