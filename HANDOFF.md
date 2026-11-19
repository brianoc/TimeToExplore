# Handoff

**Active handoff (2026-11-19): make the burn–coast–burn profile responsive without
weakening the simulation.** Split planning into a fast, search-only gravity model
and the existing full-field final plan and flight model. Read this file and
[Burn–coast–burn trips](/simulation/burn-coast-burn.md) first. The pinned-break
work is done and committed; do not reopen it. **Steps 1, 2, and 3 are done**
(break search 26.9 s → 3.3 s; the floor and pinned solve 12.0 s → 1.5 s, with the
window cached; the slider retimes on a latest-wins background worker without
blocking the frame). **Step 4, the documentation close-out, is next.**

## Goal

At the start of this plan, selecting `BURN-COAST-BURN` froze the nav computer
for about **37 s** at Mercury / 1 g and **9 s** at Mars / 0.3 g, and a slider
retime cost about **183 ms**. Steps 1–2 brought `Plan()` to **1.46 s** (Mercury)
and **0.85 s** (Mars), first selection to 1.52 s and 0.93 s, a repeat selection
to under 0.1 ms, and the retime to 93–99 ms. Step 3 moved the retime off the
main thread: a request returns immediately, the newest request wins, and the
published preview stays until the finished full-field plan is handed back on the
main thread. First selection is still under a second at Mars and about 1.5 s at
Mercury, and the final plan's reported peak, delta-v, clearance, seams, and
exact arrival are unchanged.

## Evidence

Profiled in a throwaway copy of the project under `.cache/perf-project` at the
epoch (2026-11-15), Gateway departure, conic capture, 1 g:

| Per `Plan()` call | Mercury | Mars 0.3 g |
|---|---:|---:|
| Total | 36.2 s | 9.1 s |
| `SolveBreakSeconds` (quintic break peak search) | **26.9 s (74%)** | 4.25 s (47%) |
| Pinned-departure Newton solve | 7.2 s (20%) | 3.07 s (34%) |
| Reference coast | 0.67 s | 0.66 s |
| Reverse brake integration | 0.42 s | 0.31 s |
| Break-peak evaluations | **5,517** | — |
| Cost per break-peak evaluation | **4.88 ms** | — |
| Full builds per floor search | 28 | 27 |
| Outer + settle passes per build | 12 + 3 | 12 + 3 |

The 4.88 ms per peak is not the quintic. `Measure` samples the arc at 64 points
and `GravityField.Prepare` recomputes the ephemeris of **every catalogue body**
(137) at each point. A direct `PlanAtDuration(8 d)` is 186 ms, of which 75 ms is
the same break search.

## Design decisions (settled)

1. **Two-tier planning.** The solver searches with a fast, search-only model: the
   arc's own central body (`arc.BodyId` — the departure body for the break, the
   Sun for the heliocentric arcs, the target for capture), evaluated in a
   body-relative frame. The final plan returned to the caller is rebuilt and
   measured with the existing full `GravityField`.
2. **The flight simulation is untouched.** `ShipIntegrator`, the autopilot, the
   full gravity field, the conic/hybrid capture and the 0.1 m arrival guarantees
   stay exactly as they are. The fast model is never used to fly the ship.
3. **The reported plan is full-field.** `Plan()` and `PlanAtDuration()` return a
   plan whose peak, delta-v and clearance come from the full field. A fast
   search may choose the duration, but it may not be the number the nav computer
   shows.
4. **Conservative screen.** If the fast peak is within **5%** of the drive limit
   (`MaxAccelerationMps2 * (1 + eps)`), certify with the full field or relax the
   duration; a fast estimate must not let an over-thrust plan through. The 5%
   margin is a named constant, documented in the design note.
5. **Async only if measured.** After steps 1–2, measure the retime. If it is
   ≤ 33 ms, keep it synchronous and record the measurement. If it is above
   33 ms, step 3 adds asynchronous latest-wins planning with cancellation; do not
   add threads speculatively. The retime measured 93–99 ms (120 ms at the
   near-floor 4.5 d in step 3's harness), so step 3 added the worker; the
   synchronous `SetPreviewDuration` stays for tests and callers that need an
   immediate plan.
6. **No visual change intended.** This plan waives the screenshot rule: there is
   no intended visible difference. The evidence is measured timing and tests.

## Steps

| Step | Deliverable | Done when |
|---|---|---|
| 0 | This plan and the working-agreement rule (one commit per step) | `tools/check.sh --fast` and `validate_okf.py` clean (done) |
| 1 | Fast single-central-body peak for the break search; full-field final plan | `--tests flight` green; final plans unchanged within the stated tolerance; break-search share down at least 5×; times reported (done: 26.9 s → 3.3 s, 8.1×) |
| 2 | Cheaper floor search and pinned solve, plus a window cache | `--tests flight` green; floors 3.46 d / 8.03 d unchanged; first selection < 1 s; repeat instant; exact arrivals still green (done: floors 3.462 / 8.032 d; first selection 1.5 s Mercury / 0.93 s Mars, repeat < 0.1 ms; retime 95 ms) |
| 3 | Slider responsiveness, async only if the retime is over 33 ms | `--tests presentation,smoke` green; measured retime meets the target or async is covered by tests (done: latest-wins worker; request-to-publish 99–149 ms, no frame block) |
| 4 | Docs, log, clear this handoff | `tools/check.sh --fast`, validator, and the narrow suites green |

### Step 0 — done (this commit)

The working-agreement rule, this plan, and the log entry.

### Step 1 — done (this commit): the fast break peak

`BreakPeakMps2` now measures the quintic against the departure body alone and
keeps full-field endpoint accelerations, so the search curve is the plan's break
arc. `Plan()` and `PlanAtDuration()` still rebuild and measure the returned plan
with the full `GravityField`. `BreakScreenMargin = 0.05`: a fast peak inside 5%
of the search limit (9.7 m/s² at 1 g) is certified with the full field, using one
`Prepare` at the break's start and the same 64 samples `Measure` uses. The
certification is the same field the plan reports, without per-sample catalogue
ephemeris.

Measured at the epoch on the `.cache` copy (Mercury, 1 g): `Plan()` **36.2 →
12.0 s**, `SolveBreakSeconds` **26.9 → 3.3 s (8.1×)**, break-peak cost **4.88 →
0.60 ms**, `PlanAtDuration(8 d)` **183 → 105 ms**. The final plans are unchanged:
8 d Mercury 3.02 m/s² / 92.9 km/s break and 569.9 km/s total; 12.3 d 1.35 / 61.7
and 379.9; Mars 90 d 0.033 / 9.0 and 45.4; floors 3.46 d and 8.03 d; seams under
a millimetre and 1e-5 m/s. Fast-vs-full peak: +0.18%, +0.41%, +4.5%, +0.05% at
the floor. `--tests flight` and `validate_okf.py` green.

**Trap for step 2:** the floor search is inherently near the drive limit, so
almost every break probe takes the 5% certification branch (14,991 of 16,551 on
Mercury). That branch is now cheap (one `Prepare` plus the selected bodies), but
it is still the part of `SolveBreakSeconds` that did not disappear. Reducing the
floor probe count is what will remove the remaining 3.3 s; do not "fix" it by
widening the screen or by letting a fast estimate stand in near the limit. The
certification's one-prepare active set is selected at the break start, where the
departure body and its moons are strongest; the outward break cannot make a new
body significant. `TripPlanner.MeasureBreakPeak` and
`MeasureBreakPeakFullField` are diagnostics for the tests, not a production API.

### Step 2 — done (this commit): the floor and the pinned solve

The floor search now probes `Build(..., fast: true)`: fixed burns report their
commanded acceleration, conics zero, the break its own reference body, the probe
burn integrates at 128 samples, and the pinned fixed point stops loosely. The
chosen duration is rebuilt at 512 samples with the full field and certified; if
the fast model is optimistic, the search finishes with full-field builds. The
bracket is a probe just above the kinematic estimate, a 15% walk-down, and a
0.05% (or 30 s) bisection; the 0.2% retry and the 1 km / 1 m/s gate are
unchanged. The pinned loop stops on the brake's physical 1e-7 m/s mismatch, with
up to three settles only while the ray drifts; the Newton Jacobian is reused for
one iteration and the coast correction runs sixteen iterations. `FlightSession`
caches the floor plan and window per `(date, target, level, cruise, arrival)`;
a repeat plot is free, a changed date (or place/teleport) clears it.

Measured on the `.cache` copy at the epoch: Mercury 1 g `Plan()` **12.0 → 1.46 s**,
`PlanAtDuration(8 d)` **105 → 95 ms**, first selection `Plan + window + retime`
**1.52 s**, repeat **0.09 ms**, retime 93 ms; Mars 0.3 g `Plan()` **0.85 s**,
first selection **0.93 s**, retime 95 ms. Floors **3.462 d** (9.70 m/s²,
1,760 km/s) and **8.032 d** (5.25 m/s², 1,170 km/s); 3.43 / 7.80 d still reject
with their reasons; 8 d, 12.3 d, 90 d, seams, `0 g`, and the 8.03–24.09 d window
are unchanged.

**Trap that drove step 3:** the Mercury first selection is 1.5 s, over the 1 s
target. The cost is not the full field any more; it is the near-floor basin,
which is noisy enough that the search still pays about twenty fast probes even at
the 0.2% retry resolution. Do not fix this by loosening the floor search's
closure gate or tolerance: the returned floor must stay 3.46 d / 1,760 km/s. The
retime was 93–99 ms, so step 3 chose async rather than a faster floor search.
The probe building blocks are
`TripPlanner.Build(..., fast: true)`, `MeasureFast`, `FastProbeBurnSamples = 128`,
`FastProbeOuterPasses = 12`, and `SearchFloor`; the cache lives in
`FlightSession.WindowFor`.

### Step 3 — done (this commit): the slider

The retime now runs on one background worker with **latest-wins** semantics.
Moving the handle calls `FlightSession.RequestPreviewDuration`, which clamps the
duration to the preview window, stores the newest request with a generation
number, and returns. The worker builds it with its **own** `TripPlanner` and
`GravityField`, because the flight simulation's field keeps a prepared active set
and stays on the main thread; the worker reads only the catalogue (its elements
and `ComputeState`) and never the simulation's mutable field or integrator
state. A build that finishes
after a newer request arrived is discarded and the newest request is built next.
`TryApplyCompletedRetime` publishes the finished plan, guarded by the request
lock and checked against the generation, and the HUD calls
`SolarSystemBootstrap.ApplyRetimeIfReady` once per frame to hand it to the
trajectory line and the report on the main thread. The previous full-field
preview stays visible until then, and `PendingPreviewSeconds` keeps the handle
and its label on the player's ask instead of snapping back.

`SetPreviewDuration` is unchanged as the synchronous, deterministic path; it now
also cancels any outstanding background request so it cannot be overwritten.
`FlightSession` is `IDisposable`: `Dispose` sets the stop flag, drops the queue,
pulses the worker, and joins it for up to `RetimeJoinMilliseconds` (5 s); the
bootstrap disposes the session in `OnDestroy`. A disposed session refuses new
requests, and the worker checks the stop flag before publishing, so no thread can
touch a discarded plan.

Measured on the rebuilt `.cache` copy at the epoch (Mercury 1 g, 4.5–10.2 d;
Mars 0.3 g, 9–15 d):

| Quantity | Mercury 1 g | Mars 0.3 g |
|---|---:|---:|
| Synchronous `SetPreviewDuration` | 120 ms (4.5 d), 97 ms (7.35 d) | 97–125 ms |
| Background request-to-publish | 99–124 ms | 100–128 ms |
| Handover over the same build | 2–4 ms | 2–3 ms |
| Worker plan vs synchronous plan | identical | identical |
| 20 requests at 10 ms, first publish | 311 ms | 324 ms |
| Tail after the last request | 111 ms | 124 ms |
| Plans published during the 200 ms drag | 1 (the newest) | 1 (the newest) |

The worker's plan matched the synchronous plan exactly in flight time, arrival
date, peak, delta-v, clearance, and arc count. The drag published only the newest
plan: two intermediate builds were superseded and dropped, nothing queued, and
the 20-tick loop kept its 10 ms cadence (200 ms for 200 ms of requested ticks), so
the frame is not blocked. `FlightSessionTests` now covers latest-wins with the
first build in flight, background-equals-synchronous, the previous preview staying
published, a discard dropping an outstanding request, and Dispose stopping the
worker; the HUD and bootstrap slider tests request the retime, hold the handle
while it lags, and pump the real handover.

**Trap for step 4:** the retime is not ≤ 33 ms and is not meant to be. Do not
chase the threshold by loosening the floor search, the 5% screen, the 1e-7 brake
tolerance, the 16-iteration coast correction, or the closure gate; the profile's
own floor is 3.46 d / 1,760 km/s and the published plan must stay that. The
latency is one full-field `PlanAtDuration` build (99–149 ms at the epoch), and the
only headroom left is inside that build. The *planning* cost is now the slider's
floor: a continuous drag publishes roughly one plan per build once the drag
pauses; the handle is smooth because the frame is free, not because the plan is
fast. The retimer is `FlightSession`'s `_retimeLock`, `_pendingRetime`,
`_completedRetime`, `_retimeGeneration`, `RetimeLoop`, and `BuildRetime`; the
worker's field is created in `StartRetimeWorker` and must never be the
simulation's `_gravity`. The synchronous path is still the reference for equality
tests.

### Step 4 — close out

- Update the flight model, architecture, flight session, testing, navigation and
  roadmap where the fast/full split is visible. Add a short technical note
  (e.g. `Docs/technical/planning-performance.md`) if the two-tier model needs
  more than a paragraph, and link it from the index.
- Log entry. Clear this file to the no-active-handoff stub. No screenshots.
- `tools/check.sh --fast`, `validate_okf.py`, and the narrow suites.

## Verification

```bash
tools/check.sh --tests flight                # steps 1–2
tools/check.sh --tests presentation,smoke    # step 3
tools/check.sh --fast                        # step 0 and 4
python3 tools/validate_okf.py                # whenever a concept changes
```

Use `.venv/bin/python tools/validate_okf.py` when the system Python lacks PyYAML.

Performance measurement uses a throwaway copy under `.cache` (ignored, never
committed). `.cache/perf-project` was rebuilt for step 3 and is stale again after
this commit: `rsync -a --delete TimeToExplore/Assets/ .cache/perf-project/TimeToExplore/Assets/`
and keep its warm `Library`. Step 2's harness is
`TimeToExplore.EditorTools.PlanProfilerRunner`; step 3's is
`TimeToExplore.EditorTools.Step3ProfilerRunner`, added only in the `.cache` copy.
The command is:

```bash
python3 tools/unity_guard.py --log .cache/perf-project/perf-step3.log -- \
  /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -projectPath /Users/brian.oconnor/Personal/TimeToExplore/.cache/perf-project/TimeToExplore \
  -executeMethod TimeToExplore.EditorTools.Step3ProfilerRunner.Run \
  -logFile /Users/brian.oconnor/Personal/TimeToExplore/.cache/perf-project/perf-step3.log
```

The runner writes `TimeToExplore/report-step3.txt` in the throwaway project. It
reports the synchronous retime, request-to-publish, background-versus-synchronous
equality, and the 20-request drag. Report `Plan()` and `PlanAtDuration` per call
and the break-search share too. A standalone player is not used (it freezes on
this machine); do not use it.

## Constraints and traps

- **The final plan is full-field.** Never return the fast estimate's peak,
  delta-v or clearance as the plan's numbers.
- **The flight model is full-field.** No change to `GravityField`,
  `ShipIntegrator`, the autopilot, capture, or the flight tests' full-field
  semantics.
- **Keep the pinned-break geometry.** Break end at `1.02 × SOI`, escape speed
  along the escape ray, burn start there, seams < 1 cm / 1e-5 m/s, 8 d Mercury
  closes at 1 g, floors 3.46 d and 8.03 d.
- **Never burn across the SOI.** `DepartureBurnStaysOutside` must pass.
- **One revolution only.** No multi-rev coasts.
- **`0 g` is exactly `Coast`.** `Powered` and `Coast` keep their behaviour.
- **The 5% screen is a guard, not a licence.** If the fast peak is within the
  margin, certify with the full field or choose a longer duration; do not ship an
  over-thrust plan.
- **Do not loosen the closure gate** to advertise a lower floor.
- **The retime worker is isolated and stoppable.** Plain managed thread only, no
  JobSystem/Burst; the worker gets its own `TripPlanner` and `GravityField` and
  must never touch the simulation's `_gravity`, the integrator, the autopilot, or
  a Unity API. The plan handover stays on the main thread, and `Dispose` must
  leave no alive worker.
- Assembly rules: Core has no engine references; Simulation has
  `noEngineReferences`; no `UnityEngine.Input` or Input System calls outside the
  two seam files.
- Do not force-close an open Unity editor. `tools/check.sh` isolates its run.
- Add tests for every new behaviour. Docs are part of done. No test counts.
- No screenshots for this plan; the evidence is timing and tests.

## Sub-agent prompt template

```text
You are step <N> of the responsive-planning plan in
/Users/brian.oconnor/Personal/TimeToExplore. Read HANDOFF.md and
Docs/simulation/burn-coast-burn.md first. Do only step <N>: <deliverable>.
Touch only: <files>. Follow the design decisions and traps in HANDOFF.md. Keep
the final plan and the flight simulation full-field. Add tests for every new
behaviour. Iterate and finish with the narrow check for this step, not the full
suite. Measure with the `.cache` harness and report the before/after numbers.
Update the affected concept and Docs/log.md, mark step <N> done in HANDOFF.md,
and commit everything as ONE commit starting with `<message>`. Do not start step
<N+1>. Reply with: the commit hash, the exact check command and result, the
measured numbers, and any trap the next step needs.
```

## Next action

Start **step 4**, the documentation close-out. Walk the fast/full split and the
background retime through the remaining concepts — the flight model,
architecture, flight session, testing, navigation and arrival, and the roadmap —
adding a short technical note (`Docs/technical/planning-performance.md`) if the
two-tier model needs more than a paragraph, and link it from the index. The
retime numbers and the lifecycle are in this file and in
[the flight session](/technical/flight-session.md). Do not change the
synchronous path's plan, the 3.46 d / 8.03 d floors, or the 5% screen to chase a
latency budget: the worker is the step-3 answer and its build is one full-field
`PlanAtDuration`. Log entry, clear this file to the no-active-handoff stub, no
screenshots, and run `tools/check.sh --fast`, `validate_okf.py`, and the narrow
suites.
