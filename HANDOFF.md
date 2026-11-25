# Handoff

**Plan in progress (2026-11-25): the departure climb split.** Branch
`work/departure-climb-split`; worktree
`~/.local/share/opencode/worktree/6479b2/departure-climb-split`.

## What this plan fixes

Every long burn–coast–burn plot starts with the same **10.74066-day parabolic
escape** from Gateway (12.02249 days from a Mars 300 km orbit), because
`TryBuildDepartureClimb` takes the minimum-energy climb whenever the requested
time can fit it and only solves a hyperbolic excess when it cannot. It never
asks whether a faster climb would cost less overall
([TripPlanner.cs:3919](TimeToExplore/Assets/Scripts/Simulation/TripPlanner.cs#L3919)).
The climb is part of the physical departure added on 2026-11-23
([log](Docs/log.md), [ADR-019](Docs/decisions/adr-019-station-calls-and-departure.md)).

The coast itself is right: it replaced a 267–355 km/s powered hover with a
1.4–2.9 km/s break. The defect is the **split between the climb and the
heliocentric coast**. At a fixed total trip time, a little hyperbolic excess at
periapsis buys a much longer coast, and the longer coast shrinks both fixed
burns by more than the break grows.

## Step 1 measured (done)

The test-only `TripPlanner.DepartureVInfinityOverrideMps` seam forces one v∞;
default `-1` keeps every plan exactly as it was. `DepartureClimbSplitTests`
(`flight`) builds Gateway → Mars at 0.3 g with conic capture, JD 2461359.5.
Totals are the real planner's, every plan exact and on the Mars parking orbit.

| Total | v∞ | Climb | Break Δv | Depart Δv | Brake Δv | Total Δv | Propellant |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 8 d | 10 km/s | 1.03 d | 18.638 | 418.566 | 434.766 | 875.520 | 91.5 t |
| 18 d | 0 | 10.74 d | 1.404 | 344.516 | 356.384 | **706.091** | 73.2 |
| 18 d | 1 | 7.40 d | 1.982 | 206.570 | 218.244 | 430.494 | 44.0 |
| 18 d | 3 | 3.27 d | 5.038 | 142.074 | 153.798 | 304.503 | 30.9 |
| 18 d | 10 | 1.03 d | 18.638 | 119.782 | 134.128 | **276.099** | **28.0** |
| 30 d | 0 | 10.74 d | 1.404 | 97.038 | 107.755 | 209.985 | 21.2 |
| 30 d | 3 | 3.27 d | 5.038 | 69.391 | 79.743 | **157.771** | 15.9 |
| 30 d | 10 | 1.03 d | 18.638 | 62.198 | 74.352 | 158.745 | 16.0 |
| 90 d | 0 | 10.74 d | 1.404 | 14.040 | 19.699 | 38.968 | 3.9 |
| 90 d | 1 | 7.40 d | 1.982 | 13.709 | 19.108 | **38.541** | 3.9 |
| 90 d | 3 | 3.27 d | 5.038 | 13.587 | 18.407 | 40.677 | 4.1 |
| 90 d | 10 | 1.03 d | 18.638 | 15.177 | 18.043 | 55.462 | 5.6 |

Δv columns are km/s. Worst seam across all twelve: **3.2 m / 0.010 m/s**.
At 18 days the current plan spends 2.5× the fuel of the best forced climb; at
90 days the optimum is about 1 km/s and the gain is 1%. The fast end is where it
matters.

**Floor sweep**, 10 km/s excess, 5–10 days: 8 d **875.520** is feasible, 9 d
692.4, 10 d 583.4; 5, 6 and 7 days are refused — 5 d for the departure burn's
coast geometry, 6 and 7 d for want of room for the break and the escape climb.
So the shortest certified duration found is **8.00 d**, and the fixed burns and
coast, not the climb, now set the fast end. Re-sweep at finer steps in step 3.

The exact output is in the `DepartureClimbSplitTests` run log; the summary is in
[Docs/log.md](Docs/log.md).

## Step 2 measured (done)

`TripPlanner.UseDepartureSplit` (internal, **default off**) chooses the escape
conic's `v∞` by minimising the plan's total delta-v at the requested duration.
With the flag off every plan is step 1's.

* **Search:** grid `0, 1, 2, 3, 6, 10, 16` km/s (cap 16), best point, then two
  bisections between it and its neighbours. A candidate whose climb cannot fit
  the time is skipped. It is a grid because the curve is not unimodal.
* **Cost:** a **fast probe build's** total delta-v plus the break's own
  integrated delta-v against the departure body. The reference skeleton cannot
  be the cost: its departure is one fused powered quintic (`BuildCoastSkeleton`)
  whose delta-v falls monotonically as the climb shortens — measured
  851.7 → 355.6 km/s from `v∞` 0 to 16 at 18 d — so it always picks the 16 km/s
  corner. `MeasureFast` reports a quintic's delta-v as zero, so the break is
  added back (`BreakDeltaVCentralBodyAlongPath`).
* **Once per build:** the same value reaches `BuildCoastSkeleton`, both pin-loop
  climbs, both settle climbs, the burn-only branch and `BuildShortCoast`. The
  seam `DepartureVInfinityOverrideMps` still wins when non-negative.
* **Measured** (Gateway → Mars 0.3 g, conic, JD 2461359.5):

| Total | Chosen `v∞` | Escape | Real total Δv | Propellant | Search's cost |
|---:|---:|---:|---:|---:|---:|
| 18 d | **10 km/s** | 1.027 d | **276.099** | 28.0 t | 275.200 |
| 30 d | **5.25 km/s** | 1.932 d | **154.753** | 15.6 t | 153.874 |
| 90 d | **1 km/s** | 7.405 d | **38.541** | 3.9 t | 37.558 |

The 30-day plan beats step 1's coarse sample (157.771 at 3 km/s): the refined
grid found 5.25. Every chosen plan is exact and on the Mars parking orbit.
**One search costs about 0.6 s** (11 fast probe builds, warm, batch mode).

**Flag off restored:** 18 d **706.091** km/s, 90 d **38.968** km/s. A
heliocentric start (Ares Junction → Gateway, 160 d) is bit-identical either way.
Determinism is tested: two 18-day builds choose the same `v∞` and return the
same plan.

## Step 3 design (settled before turning it on)

* **Make the chooser cheap first.** At 0.6 s the search cannot ship: it runs in
  every `Build`, so the floor search would pay it per probe. Target **≤ 50 ms**
  per search. The first attempt is a **rank-only single-pass** fast build (stop
  the pin after one pass) on a **5–7 point grid**, no refinement until the
  ranking is proven. Check the chosen `v∞` still matches the full search at 8,
  18, 30 and 90 days; if it does not, implement a Lambert-based estimate that
  keeps the fixed burns separate. Do not flip the flag until the cheap chooser
  gives the same answers.
* **Use 3× the new floor (decision, 2026-11-25).** Keep
  `FlightSession.MaximumPreviewSeconds` as it is: the right-hand end is
  `minimum * TripPlanner.MaxDurationScale` (3.0), applied to the new split-aware
  minimum. At the epoch that is about 7.17–21.52 d to Mars, 4.21–12.63 to Venus
  and 38.38–115.13 to Uranus. The old ~49.5 d Mars end was 3× the *old* floor,
  not a physical limit; the planner genuinely accepts up to ~295 d. The cheap
  slow trips therefore leave the slider but remain buildable by direct
  `PlanAtDuration`. This is the smallest change and keeps the benchmark near
  baseline. Record it in ADR-021 and the navigation concept. Do **not** grow the
  window to the planner's true limit in this branch.
* **Stations are a follow-up.** A station plan has a velocity match instead of
  a capture chain, and step 2 left it unforced. Do not expand step 3 to cover
  it; note it in the ADR and the roadmap as the next departure-split change.


## Design settled

* **Objective:** minimise the plan's total delta-v at the requested duration.
  Delta-v is monotone with the propellant readout, so this is the fuel trade.
* **Variable:** the escape conic's hyperbolic excess `v∞`; 0 is the parabolic
  climb. The profile's arcs and seams do not change.
* **Where:** choose the split once per `Build` call from the requested duration,
  then use the same `v∞` in the reference skeleton, the pin loop and the settle
  pass. It must not be re-chosen inside the pin loop, or the fixed point moves.
* **Cost of a candidate:** a **fast probe build's** total delta-v plus the
  break's integrated delta-v. The reference skeleton cannot be the cost: its
  departure is one fused powered quintic whose cost falls monotonically as the
  climb shortens (measured 851.7 → 355.6 km/s from 0 to 16 km/s at 18 d), so it
  always picks the grid corner. The curve is **not unimodal**: 10 km/s wins at
  18 days, 5.25 at 30 and 1 at 90, and 16 is worse than 10 at 18. A
  golden-section search is therefore wrong. Use a coarse grid (0, 1, 2, 3, 6,
  10, 16 km/s), take the best, then refine twice between its neighbours. Keep
  the chosen value for the whole build.
* **Bounds:** the existing climb solver already rejects a v∞ that does not fit
  the time; cap the search at a finite value. The measured table above shows
  the best 18-day plan near 10 km/s and the best 30-day plan near 3; 24 km/s is
  worse. If the search is too slow in the fast probes, keep the full search for
  the certified build and use a fixed rule above it; decide from step 1's
  timings.
* **No player control.** The player still chooses the trip time; the planner
  picks the split.

## Steps

1. **Done — Measure the split.** Added the
   `DepartureVInfinityOverrideMps` seam and `DepartureClimbSplitTests`
   (`flight`); measured the table and the 8.00-day floor above. No returned
   plan changed.
2. **Done — Implement the search behind a flag.** Added the grid search, the
   fast-probe cost with the break added back, and `UseDepartureSplit` (default
   **off**). Chosen 10 / 5.25 / 1 km/s at 18 / 30 / 90 d; totals 276.099 /
   154.753 / 38.541 km/s. One search ≈ **0.6 s**. Flag-off anchors unchanged.
   Verified with `tools/check.sh --tests flight` (326 tests, 0 failed) and
   `tools/check.sh --fast`.
3. **Next — Turn it on and re-baseline.** The cheap chooser is working
   (4–7 ms, matches the full search on the 16 measured route/duration cases);
   the flag, the re-baseline and the docs remain. Keep the window at 3× the new
   floor (the decision above), not the planner's true limit. Re-sweep the
   floors, the windows and the fixed-duration anchors, and update every
   affected test. Update
   [burn–coast–burn](Docs/simulation/burn-coast-burn.md),
   [navigation and arrival](Docs/simulation/navigation-and-arrival.md),
   [the flight model](Docs/simulation/flight-model.md),
   [planning performance](Docs/technical/planning-performance.md) and
   [testing](Docs/technical/testing.md). Add ADR-021 for the split and the
   window change. Fix the two wrong propellant rows in the burn–coast–burn
   table while there (the code gives 50.8 t at 20 d and 3.9 t at 90 d). Measure
   the plot and retime with `tools/bench.sh`; the 25% threshold is the
   regression gate, except that the newly feasible reverse routes are a new
   capability and are re-recorded rather than called regressions. Keep or
   remove the `DepartureVInfinityOverrideMps` seam deliberately; the step-1
   measurement tests may still use it. Verify the full `tools/check.sh` and
   `tools/validate_okf.py`.

## Step 3 stopped and resumed (2026-11-25)

The first attempt made the chooser cheap and flipped the flag, then stopped on
the window. The literal "largest accepted duration" end gave Mars 7.17–294.92 d,
Venus 4.21–109.56 and Uranus 38.38–2,086.28 (whose certified max is refused),
and the bench regressed 12 phases: Uranus plot **+120.8%**, Uranus retime-max
**+333.7%**, Venus→Earth plot **+1,611.8%** and Uranus→Earth **+484.0%** as the
split made previously refused reverses feasible. The `Begin` replan also fired
because 3 h is now more than 1% of a 7.17-day trip.

Decision: keep 3× the new floor. The wide-window experiment is discarded. The
newly feasible reverses stay (a real improvement) and their bench rows are
re-recorded as new plots, not regressions. The 3-hour `Begin` staleness on a
7-day minimum is left as is for this branch; the player's real preview is
minutes old, and the benchmark's 3-hour gap is a stress case.

4. **Close out.** Add a PlayMode `capture` test that plots Gateway → Mars at 18
   days and writes the HUD screenshot, run it without `-nographics`, and show
   the result in the chat. Clear this handoff to the no-active stub in the same
   commit as the final step, merge the branch to `main`, and remove the
   worktree. Do **not** push; publication waits for the person testing.

## Traps

* The planner has several climb call sites — `BuildCoastSkeleton` (twice),
  `BuildBurnCoastBurn` (pin and settle), the burn-only branch and
  `BuildShortCoast`. They must all use one chosen v∞ or the seams stop closing.
* The choice must be made once per build. Re-choosing inside the pin loop or
  the settle pass moves the fixed point and the plans stop converging.
* The planner runs on a latest-wins background worker. Keep the choice
  per-instance; no mutable static state.
* **The `BuildCoastSkeleton` cost is a trap.** It looks like the right cheap
  proxy and is not: its powered departure quintic makes a *faster* climb look
  cheaper all the way to the 16 km/s cap. The cost that works is the fast probe
  plan plus the break's delta-v, because it keeps the break and the two fixed
  burns separate.
* **The search runs inside every `Build`.** With the flag on, the floor search
  pays ~0.6 s per probe, so step 3 must make the chooser cheap before the plot
  is benchmarked. If the cheap ranking moves the chosen `v∞`, stop and report
  rather than shipping a worse split.
* **The window keeps its 3× rule, applied to the new minimum**
  (`FlightSession.MaximumPreviewSeconds`, `TripPlanner.MaxDurationScale`). Do
  not grow it to the planner's true limit. The old ~49.5 d Mars end was 3× the
  old floor; the new end is 3× the new floor, and the cheap slow trips remain
  direct builds.
* **Stations keep the unforced climb.** The split cost is a planet capture
  chain. Gateway → station still starts with the long climb; that is a recorded
  follow-up, not a step-3 failure.
* **Do not chase anchors by pasting the new numbers.** The plans change by
  design; first confirm each new plan is exact (seams and arrival) and that the
  faster/slower trade still orders correctly, then update the pin. Keep the
  relationships, not just the values.
* `DepartureVInfinityOverrideMps` is a test-only seam, not a plan input, and it
  takes precedence over the search. The step-1 measurement tests and the
  step-2 choice tests both use it or rely on it; step 3 decides whether the
  seam stays.
* `TrajectoryPlan.DepartureVInfinityMps` is `-1` without a climb, `0` for the
  parabolic climb and the chosen excess otherwise. Do not read `0` as "no
  split".
* At 18 days the optimum is 10 km/s; do not assume the grid corner is the
  answer. At 30 the refined optimum is **5.25**, not the coarse table's 3, and
  at 90 it is 1. Step 3's anchors must come from the search, not the step-1
  sample.
* Do not touch `work/instant-route-plan`: that worktree has uncommitted work
  from a different task. It is not part of this branch.
* `main` is two merges ahead of `origin/main`. Branch from local `main`, do not
  push, and do not rewrite history.
* The near-floor basin is noisy. Do not treat one refused duration as the
  floor; re-sweep and take the certified plan.
* The props are **not** a budget. `PropellantKg` is the rocket equation against
  1,000 t dry; no tank is enforced.
* The existing anchors are deliberately re-baselined by this change. Do not
  preserve 706.0906 km/s at 18 days or 38.9678 at 90 days if the split moves
  them; update the tests and the docs together.

## Verification

| Command | Last result |
|---|---|
| `tools/check.sh --tests flight` | Passed in 1,364 s; EditMode 326 tests, 0 failed, 0 skipped |
| `tools/check.sh --fast` | Passed in 6 s |
| `tools/check.sh` | not run on this branch yet |
| `tools/bench.sh` | not run on this branch yet |
