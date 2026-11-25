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

## Design settled

* **Objective:** minimise the plan's total delta-v at the requested duration.
  Delta-v is monotone with the propellant readout, so this is the fuel trade.
* **Variable:** the escape conic's hyperbolic excess `v∞`; 0 is the parabolic
  climb. The profile's arcs and seams do not change.
* **Where:** choose the split once per `Build` call from the requested duration,
  then use the same `v∞` in the reference skeleton, the pin loop and the settle
  pass. It must not be re-chosen inside the pin loop, or the fixed point moves.
* **Cost of a candidate:** the cheap skeleton's total delta-v — break, cruise
  entry impulse, arrival trim and capture. A coarse grid (0, 1, 2, 4, 8, 16
  km/s) plus a golden-section refinement is the proposed search.
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
2. **Next — Implement the search behind a flag.** Add the skeleton-cost search
   and an internal `UseDepartureSplit` flag, default **off**, so the tree stays
   green. Tests prove the chosen v∞ matches the measured optimum, the selected
   plan is exact, and the flag off still returns today's plans. Verify with
   `tools/check.sh --tests flight`. Keep the seam for step 3.
3. **Turn it on and re-baseline.** Flip the flag, re-sweep the floors, windows
   and fixed-duration anchors, update every affected test, and update
   [burn–coast–burn](Docs/simulation/burn-coast-burn.md),
   [navigation and arrival](Docs/simulation/navigation-and-arrival.md),
   [the flight model](Docs/simulation/flight-model.md),
   [planning performance](Docs/technical/planning-performance.md) and
   [testing](Docs/technical/testing.md). Add ADR-021 for the split. Fix the
   two wrong propellant rows in the burn–coast–burn table while there (the code
   gives 50.8 t at 20 d and 3.9 t at 90 d). Measure the plot and retime with
   `tools/bench.sh`; the 25% threshold is the regression gate. Verify the full
   `tools/check.sh` and `tools/validate_okf.py`.
4. **Close out.** Add a PlayMode `capture` test that plots Gateway → Mars at 18
   days and writes the HUD screenshot, run it without `-nographics`, and show
   the result in the chat. Clear this handoff to the no-active stub in the same
   commit as the final step, merge the branch to `main`, and remove the
   worktree. Do **not** push; publication waits for the person testing.

## Traps

* The planner has several climb call sites — `BuildCoastSkeleton` (twice),
  `BuildBurnCoastBurn` (pin and settle), the burn-only branch and
  `BuildShortCoast`. They must all use one chosen v∞ or the seams stop closing.
* The planner runs on a latest-wins background worker. Keep the choice
  per-instance; no mutable static state.
* `DepartureVInfinityOverrideMps` is a test-only seam, not a plan input. With it
  at `-1` the branch condition and the solver run exactly as before; the seam
  forces the hyperbolic branch even when the parabolic climb would fit, which is
  what makes the measurement possible. Remove it when the split lands.
* At 18 days the optimum may sit near 10 km/s, but do not assume the grid corner
  is the answer: at 30 days 10 km/s is already worse than 3, and at 90 days it
  is far worse. The search must not be monotone in v∞.
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
| `tools/check.sh --tests flight` | Passed in 1,410 s; EditMode 322 tests, 0 failed, 0 skipped |
| `tools/check.sh --fast` | Passed in 6 s |
| `tools/check.sh` | not run on this branch yet |
| `tools/bench.sh` | not run on this branch yet |
