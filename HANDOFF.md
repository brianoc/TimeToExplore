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

## Step 3 done (2026-11-26)

* **The chooser is cheap.** The candidate cost is a **rank-only Lambert
  estimate**: the shared geometry (capture chain, entry state, bands, departure
  body) is built once, then one climb solve and one Lambert solve per candidate
  price the break, the departure burn and the brake. The grid is
  `0, 1, 3, 6, 10, 16` km/s plus one refinement round. A search is **4–7 ms**
  (target ≤ 50 ms), and `TheCheapChooserMatchesTheFullSearchAtTheAnchorDurations`
  proves it picks the full search's excess speed, or one grid step away, at 8,
  18, 30 and 90 days; the same held across sixteen route/duration cases.
* **The flag is on.** `UseDepartureSplit` defaults **true**, internal so the
  flag-off anchors still pin the pre-split plans. Gateway → Mars 0.3 g:
  **8 d 817.1658 km/s** (v∞ 16), **18 d 276.0994** (10), **20 d 244.9177** (8),
  **30 d 154.9093** (6), **90 d 38.5411** (1). Every plan is exact, on the
  parking orbit, and closes its seams (worst measured floor 848 m / 0.067 m/s).
* **The floors and windows (3× the floor):**

  | Target | Level | Floor | Window |
  |---|---|---:|---|
  | Mercury | 1 g | 3.67616 d | 3.68–11.03 |
  | Mercury | 0.3 g | 6.322 | 6.32–18.97 |
  | Mars | 1 g | 4.23343 | 4.23–12.70 |
  | Mars | 0.3 g | **7.16751** | 7.17–21.50 |
  | Venus | 0.3 g | 4.20901 | 4.21–12.63 |
  | Jupiter | 1 g | 9.194 | 9.19–27.58 |
  | Jupiter | 0.3 g | 13.58298 | 13.58–40.75 |
  | Uranus | 1 g | 28.18209 | 28.18–84.55 |
  | Uranus | 0.3 g | **37.00540** | 37.01–111.02 |

* **The burn-only crossing is dominated.** It keeps the parabolic climb (its
  duration is an output), so it is longer than the coast floor everywhere it
  builds (Mars 0.3 g 16.49 d against 7.17); `PlanFastest` returns the coast
  floor and the short-coast gap is empty. The branch stays for a route where the
  crossing wins.
* **Two solver retries the fast end needs.** A refused certificate is retried as
  a certified build 0.2%/0.5% above before a full-field bisection (Mars floor
  search 9.4 → 2.9 s); the pinned departure Newton retries once from a 1% longer
  burn when it stalls inside ten tolerances. Neither moves a tolerance or the
  gate; the second also lowered the Uranus 0.3 g floor from 38.378 to 37.005.
* **Benchmark re-recorded** (`tools/bench.sh --record`). Plot: Mars −15.0%,
  Venus −72.9%, Uranus **+87.7%** (its 37-day floor search is harder).
  Retime-mid/max: Mars +21.0/+28.3%, Venus +17.3/−3.7%, Uranus +2.9/+22.3%.
  Drag median/worst: Mars +16.2/**+207.1%**, Venus +16.4/+33.9%, Uranus
  +33.2/+41.6%. The fast-end plans have multi-day burns, so one certified build
  costs more; a phase whose duration changed compares a different build. The
  **newly feasible reverses** Venus→Earth and Uranus→Earth are real plots now
  (10.2 s and 39.0 s) and were re-recorded as new rows. `Begin` at the
  minimum replans after the benchmark's 3 h gap because 3 h > 1% of 7.17 d; the
  real preview is minutes old.
* **Verification:** `tools/check.sh --tests flight` → 327 tests, 0 failed. The
  full `tools/check.sh`, `tools/validate_okf.py` and the bench run are in the
  verification table below.

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
3. **Done — Turn it on and re-baseline.** The rank-only chooser (4–7 ms,
   matching the full search on the 16 measured route/duration cases), the flag
   on by default, the new floors and 3× windows, the anchors, every affected
   test, ADR-021 and the concept docs. The burn-only crossing is unsplit and
   dominated; the short-coast gap is empty. Verified with
   `tools/check.sh --tests flight` (327 tests, 0 failed) and the full check and
   bench below.
4. **Next — Close out.** Add a PlayMode `capture` test that plots Gateway → Mars
   at 18 days and writes the HUD screenshot, run it without `-nographics`, and
   show the result in the chat. Clear this handoff to the no-active stub in the
   same commit as the final step, merge the branch to `main`, and remove the
   worktree. Do **not** push; publication waits for the person testing.

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
| `tools/check.sh --tests flight` | Passed in 968 s; EditMode 327 tests, 0 failed, 0 skipped |
| `tools/check.sh --fast` | Passed in 6 s |
| `tools/bench.sh --record` | Re-recorded 2026-11-26; plot Mars −15.0%, Venus −72.9%, Uranus +87.7%; the reverse routes newly feasible |
| `tools/check.sh` | run on the committed tree below |
| `tools/validate_okf.py` | 47 concepts, 0 warnings, 0 errors (before ADR-021; re-run on the commit) |
