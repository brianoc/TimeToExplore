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

Measured at JD 2461359.5, Gateway → Mars, 0.3 g, conic capture, with the real
planner and a forced v∞ (throwaway harness; every plan exact):

| Total | Climb | v∞ | Break Δv | Total Δv | Propellant |
|---:|---:|---:|---:|---:|---:|
| 18 d | 10.74 d | 0 | 1.4 km/s | 706.1 km/s | 73.2 t |
| 18 d | 3.27 d | 3 km/s | 5.0 | 304.5 | 30.9 |
| 18 d | 1.03 d | 10 km/s | 18.6 | **276.1** | **28.0** |
| 90 d | 10.74 d | 0 | 1.4 | 38.97 | 3.9 |
| 90 d | 7.40 d | 1 km/s | 2.0 | **38.54** | 3.9 |

At 18 days the current plan spends 2.5× the fuel it needs. At 90 days the
optimum is about 1 km/s and the gain is 1%. The fast end is where it matters.
The same split also moves the floor: with a good v∞ a Gateway→Mars trip builds
in about 7–8 days, against the current 16.84-day coast floor and 16.49-day
burn-only minimum. The burn-only branch exists only because the coast path was
crippled by the 10.7-day climb, so expect it to lose its reason to exist.

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
  the time; cap the search at a finite value (16 km/s found the best measured
  18-day plan; 24 km/s is worse). If the search is too slow in the fast probes,
  keep the full search for the certified build and use a fixed rule above it;
  decide from step 1's timings.
* **No player control.** The player still chooses the trip time; the planner
  picks the split.

## Steps

1. **Measure the split.** Add an internal `DepartureVInfinityOverrideMps` test
   seam (default `-1`, no returned plan changes) and a `DepartureClimbSplitTests`
   (flight) that sweeps the durations and v∞, prints the table, asserts every
   plan is feasible and exact, and pins the 18-day and 90-day anchors. Sweep
   the floor down to about 5 days to establish the new minimum. Log the
   measurement. Verify `tools/check.sh --tests flight` and `--fast`.
2. **Implement the search behind a flag.** Add the skeleton-cost search and an
   internal `UseDepartureSplit` flag, default **off**, so the tree stays green.
   Tests prove the chosen v∞ matches the measured optimum, the selected plan is
   exact, and the flag off still returns today's plans. Verify flight tests.
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
| `tools/check.sh --tests flight` | not run on this branch yet |
| `tools/check.sh --fast` | not run on this branch yet |
| `tools/check.sh` | not run on this branch yet |
| `tools/bench.sh` | not run on this branch yet |
