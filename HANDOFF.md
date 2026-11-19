# Handoff

**Active handoff (2026-11-19): make the burn–coast–burn profile responsive without
weakening the simulation.** Split planning into a fast, search-only gravity model
and the existing full-field final plan and flight model. Read this file and
[Burn–coast–burn trips](/simulation/burn-coast-burn.md) first. The pinned-break
work is done and committed; do not reopen it.

## Goal

Selecting `BURN-COAST-BURN` currently freezes the nav computer for about **37 s**
at Mercury / 1 g and **9 s** at Mars / 0.3 g. A slider retime costs about
**183 ms**. The work should make the slider responsive (target: retime ≤ 33 ms;
first selection under a second; repeat selection effectively instant) while
keeping the final plan's reported peak, delta-v, clearance, seams, and exact
arrival exactly as they are today.

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
   add threads speculatively.
6. **No visual change intended.** This plan waives the screenshot rule: there is
   no intended visible difference. The evidence is measured timing and tests.

## Steps

| Step | Deliverable | Done when |
|---|---|---|
| 0 | This plan and the working-agreement rule (one commit per step) | `tools/check.sh --fast` and `validate_okf.py` clean (done) |
| 1 | Fast single-central-body peak for the break search; full-field final plan | `--tests flight` green; final plans unchanged within the stated tolerance; break-search share down at least 5×; times reported |
| 2 | Cheaper floor search and pinned solve, plus a window cache | `--tests flight` green; floors 3.46 d / 8.03 d unchanged; first selection < 1 s; repeat instant; exact arrivals still green |
| 3 | Slider responsiveness, async only if the retime is over 33 ms | `--tests presentation,smoke` green; measured retime meets the target or async is covered by tests |
| 4 | Docs, log, clear this handoff | `tools/check.sh --fast`, validator, and the narrow suites green |

### Step 0 — done (this commit)

The working-agreement rule, this plan, and the log entry.

### Step 1 — the fast break peak

In `TripPlanner`:

- Replace the full-field `MakeArc` → `Measure` path inside `BreakPeakMps2` (and
  therefore `SolveBreakSeconds`) with a **central-body-only** peak. It must not
  call `GravityField.Prepare` per sample and must not build a full-field plan.
  The departure body is already a parameter of `BreakPeakMps2`.
- The fast peak may evaluate the quintic cheaply along the arc (one body's
  gravity is one Kepler state, not 137) or use a low-sample bracket plus a 1D
  refinement. It does not need to be an exact calculus solve. It must be
  deterministic and smooth enough for the existing solve.
- Keep the returned plan path exactly as it is: `BuildBurnCoastBurn` still
  builds the final arcs and `Measure` still uses the full field for the plan that
  leaves the planner. The fast estimate is a search input only.
- Use the 5% conservative screen from the design decisions: when the fast peak
  is close to the drive limit, take the longer/gentler duration or certify with
  the full field. Never let the screen accept a plan whose full-field peak is
  over the limit.
- Tests: fast vs full peak within tolerance on Mercury 8 d, Mercury 12.3 d,
  Mars 90 d and the floor; the final plans' peak/delta-v/feasibility unchanged
  within tolerance; the 8 d Mercury plan still closes at 1 g; the floors still
  reject the just-below cases; seams unchanged. Add tests for the 5% screen.
- Measure with the profiling harness (below) and report `Plan()` and
  `PlanAtDuration(8 d)` before/after. The target for this step is at least a 5×
  reduction in the break-search share; the whole `Plan()` need not be under a
  second yet.
- Update the affected concept (flight model and/or architecture) and the log.

### Step 2 — the floor and the pinned solve

- Give the floor search a **fast mode** so its growth, walk-back and bisection
  probes do not pay the full-field `Measure` for every arc. The final candidate
  is rebuilt with the full field and certified before it is returned.
- Reduce the probe count. The current 28 builds are a growth walk, a 15% walk
  back and a 12-step bisection. With the fast model, use a direct bracket and a
  tolerance-based bisection (roughly 6–10 builds), keeping the 0.2% retry for a
  near-floor non-convergence. Do not loosen the 1 km / 1 m/s closure gate to
  advertise a lower floor.
- Reduce the pinned-solve cost without changing the seam: the outer loop always
  runs 12 passes and the settle loop 3. Stop on a measured convergence (brake
  direction/duration change below a named tolerance) instead of the effectively
  unreachable `required.SqrMagnitude > 1e-18`, and/or reduce the probe burn
  sample count while the final arc keeps 512. Whatever is chosen must keep the
  seam tests (< 1 cm, 1e-5 m/s).
- Cache the floor/window per `(epoch, target, level, style)` so a repeat
  selection is free. Invalidate on a changed departure Julian date. Keep the
  cache in `FlightSession` (or the planner) with a clear owner.
- Tests: floor unchanged at 3.46 d (Mercury 1 g) and 8.03 d (Mars 0.3 g); the
  window 8.03–24.09 d; cache hits and invalidation; exact arrivals; `0 g` is
  exactly `Coast`.
- Measure first selection and repeat selection. Target: < 1 s first, effectively
  instant repeat.

### Step 3 — the slider

- Measure `SetPreviewDuration` after steps 1–2. If ≤ 33 ms, keep it synchronous,
  add a regression test that the retimed plan is full-field and the window cache
  is not re-solved, and document the measurement.
- If > 33 ms, run the retime asynchronously: one latest-wins request, a
  cancellation flag checked between probes, the finished plan published back on
  the main thread. Keep the previous preview until the new one is ready. Cover
  the handover with a test that cannot race (or an EditMode test with a
  deterministic fake worker).
- Do not move `GravityField` or the flight path off the main thread. Do not add
  a Unity Job/Burst dependency for this; plain managed worker threads are enough
  unless the step's measurement says otherwise.
- Update the flight-session and testing concepts and the log. Run
  `--tests presentation,smoke`.

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
committed). The previous harness is `.cache/perf-project`; it is stale after the
last commit, so rebuild its `Assets` from the current project and keep its warm
`Library`. The command is:

```bash
Unity -batchmode \
  -projectPath /Users/brian.oconnor/Personal/TimeToExplore/.cache/perf-project/TimeToExplore \
  -executeMethod TimeToExplore.EditorTools.PlanProfilerRunner.Run \
  -logFile /Users/brian.oconnor/Personal/TimeToExplore/.cache/perf-project/perf.log
```

Report `Plan()` and `PlanAtDuration` per call, and the break-search share. A
standalone player is not used (it freezes on this machine); do not use it.

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
- **Do not add async before the measurement says it is needed.** The planner is
  pure managed C#; if threads are needed, use latest-wins and a clear
  cancellation point, and keep the plan handover on the main thread.
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

Start **step 1**: the central-body-only break peak behind the 5% screen, with the
full-field final plan unchanged. Report the before/after break-search share and
the plan numbers.
