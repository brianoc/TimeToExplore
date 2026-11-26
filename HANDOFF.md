# Handoff — station departure split

**Plan in progress (2026-11-26).** Branch `work/station-departure-split` in this
worktree. The target is the **first** follow-up in
[the roadmap](Docs/production/roadmap.md#follow-ups-from-the-departure-climb-split-2026-11-26):
a station plan used to keep the unforced minimum-energy climb because its
terminal velocity match has no capture chain for the cheap chooser to cost
against. The planet split is [ADR-022](Docs/decisions/adr-022-departure-split.md);
the station profile is [Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md)
and [Navigation and arrival](Docs/simulation/navigation-and-arrival.md).

**Step 2 is committed. Step 3 is next.**

## What is done

- Worktree created from `main` at `3c50440`; branch `work/station-departure-split`.
- **Step 1 (measure and prototype)** adds the station rank-only cost and the
  full station probe behind internal, test-only seams in `TripPlanner`:
  - `DepartureSplitCost.ForCaptureChain` is the old planet path, unchanged;
    `AtStationHold` enters at the hold state at the arrival date and subtracts
    no chain duration.
  - `MeasureStationSplitCandidate` (cheap) and
    `MeasureFullStationSplitCandidateCost` (fast station build plus the break's
    integrated delta-v) are what `StationDepartureSplitTests` (`flight`) grids.
- **Step 2 (wire the chooser)** is in:
  - `ChooseDepartureVInfinityMps` costs a climbing station with the same grid
    and one refinement round, building the hold state, departure band and
    departure body once per search.
  - `Build` passes the chosen value to every station build for that call;
    `BuildStationTrip` holds it through the pin and settle passes.
  - `PlanStationFloor`'s probes go through `Build`, so the bracket, bisection
    and certificate share one rule.
  - The planet path and the heliocentric no-climb cases are unchanged.

## Measured findings (epoch, 0.3 g, conic arrival)

| Route | Days | Cheap v∞ | Full v∞ | Cheap Δv | Full Δv | Cheap ms |
|---|---:|---:|---:|---:|---:|---:|
| Gateway → Ares Junction | 60 | 2.00 | 2.00 | 67.537 | 67.699 | 4.9 |
| Gateway → Ares Junction | 90 | 1.00 | 1.00 | 38.689 | 38.733 | 4.4 |
| Gateway → Ares Junction | 120 | 0.00 | 0.25 | 24.399 | 24.410 | 4.5 |
| Gateway → Ares Junction | 160 | 0.50 | 0.25 | 14.733 | 14.743 | 4.5 |
| Mars 300 km → Gateway | 60 | 2.00 | 1.50 | 68.266 | 67.050 | 4.3 |
| Mars 300 km → Gateway | 90 | 1.00 | 1.50 | 38.166 | 37.769 | 4.4 |
| Mars 300 km → Gateway | 120 | 1.00 | 1.50 | 24.144 | 24.030 | 4.4 |

- v∞ and Δv are km/s; the Δv columns are the candidate cost models, not plan
  totals (the cheap model drops the terminal's own cost because the arrival
  date is fixed).
- The cheap winner is the full winner or **0.5 km/s** from it. The worst
  cheap-versus-full cost gap is **1.216 km/s** (Mars 60 d).
- The common **600 s brake seed is adequate**; the per-candidate brake-duration
  iteration was not needed.
- A cheap search takes **4.3–4.9 ms**, inside the 50 ms budget.
- **Gateway → Helios Watch has no climb.** Helios Watch's parent is Earth and
  `FindDepartureBody` skips the target's parent, so a ship at Gateway has no
  departure body for it. Cheap is infinite at 60/90/120/160 d; the unforced
  three-arc plan is 1.733 / 1.565 / 1.481 / 1.415 km/s.
- Ares Junction → Gateway at 160 d is the heliocentric no-climb control:
  12.2391 km/s, no excess speed.

## Step 2 result — the shipped station numbers

Epoch (2026-11-15), 0.3 g, conic arrival. Plan totals in km/s; chosen v∞ in
parentheses.

| Case | Before | After |
|---|---:|---:|
| Mars 300 km → Gateway, coast floor (`Plan`) | 59.57357 d / 76.4939 | **54.36495 d / 98.4091** (16) |
| Gateway → Ares Junction, 60 d | 71.9303 | **67.8154** (2.0) |
| Gateway → Ares Junction, 90 d | 39.3398 | **38.9403** (1.0) |
| Gateway → Ares Junction, 120 d | 24.7331 | 24.7331 (0.0) |
| Gateway → Ares Junction, 160 d | 15.1026 | **15.0673** (0.5) |
| Mars 300 km → Gateway, 120 d | 25.6479 | **24.3137** (1.0) |
| Ares Junction → Gateway, 160 d | 12.2391 | 12.2391 (no climb) |
| Gateway → Mars, 90 d (planet) | 38.5411 | 38.5411 |
| Gateway → Helios Watch, 60/90/120/160 d | 1.733 / 1.565 / 1.481 / 1.415 | unchanged (no climb) |

- The floor is **faster but costlier**: the shortest trip takes the 16 km/s cap,
  the same fast-end trade the planet floors took. Every fixed duration gets
  cheaper.
- The floor plan is certified: five arcs, every seam under 1 km and 1 m/s, the
  hold state reached at **9.4 × 10⁻³ m and 8.0 × 10⁻⁷ m/s**.
- The chooser takes **4.1–4.5 ms** per search (`LastDepartureSplitSeconds`),
  inside the 50 ms budget.
- `StationDepartureSplitTests` (`flight`) pins all of the above. The suite runs
  in about nine seconds, so the floor test stays in `flight`, not `deep`.

## Step 3 — re-baseline anchors, windows and docs

Publish exactly these moved numbers in [Testing and verification](Docs/technical/testing.md),
the roadmap, the burn–coast–burn and navigation concepts, and ADR-022:

- Station coast floor, Mars 300 km → Gateway: **54.36495 d / 98.4091 km/s**, five
  arcs, 16 km/s excess (old 59.57357 d / 76.494).
- Gateway → Ares Junction: 60 d **67.8154** (2.0), 90 d **38.9403** (1.0),
  120 d **24.7331** (0.0, unmoved), 160 d **15.0673** (0.5).
- Mars 300 km → Gateway at 120 d: **24.3137** (1.0), old 25.6479.
- The trip window is `[minimum, 3 × minimum]`, so the station window becomes
  **54.36–163.09 d** (was 59.57–178.72 d).
- Unchanged and must stay: Ares Junction → Gateway 160 d **12.2391**
  (`PlanFastest` 54.16210 d / 69.541 km/s), Gateway → Mars 90 d **38.5411**,
  Gateway → Helios Watch 1.733 / 1.565 / 1.481 / 1.415.

Also fix the ADR-022 line "Stations keep the unforced climb" and the roadmap's
"station plan keeps the unforced climb" wording; the split now reaches a
climbing station. Leave the Helios Watch/Nyx Anchor no-climb rule alone.

## Step 4 — show the result and close out

Capture a station departure through the real HUD with the new floor and window,
and the flown arrival on the hold point. Run the full gate and clear
`HANDOFF.md` in the same commit.

## Verification

```bash
tools/check.sh --tests flight        # the fast Unity loop (~2 min warm)
tools/check.sh --fast                # bundle and tools only
.venv/bin/python tools/validate_okf.py
tools/check.sh                       # full gate before the final commit (~29 min)
```

Step 1 result: `tools/check.sh --tests flight` passed on the cold worktree in
156 s (the first run paid the import), `tools/check.sh --fast` passed in 6 s, and
`.venv/bin/python tools/validate_okf.py` reported 49 concepts, 0 warnings, 0
errors. `StationDepartureSplitTests` ran four tests in 4.3 s, so it stays in
`flight`, not `deep`.

Step 2 result: `tools/check.sh --tests flight` passed 214 tests with 0 failed
and 0 skipped in 163 s, `tools/check.sh --fast` passed, and
`.venv/bin/python tools/validate_okf.py` reported 49 concepts, 0 warnings and 0
errors. `StationDepartureSplitTests` now runs eight tests in about nine seconds,
including the 1.2 s floor search, so it stays in `flight`.

A targeted test needs the absolute results path: Unity resolves `-testResults`
and `-testFilter` relative to `-projectPath`, so from the worktree root use
`.cache/test-project/TimeToExplore/...` for the outputs.

## Traps

- Do not work in `/Users/brian.oconnor/Personal/TimeToExplore`; that checkout is
  the person's open editor and takes merges only.
- **Helios Watch and Nyx Anchor never climb** from a ship inside Earth's sphere:
  their parent is Earth and `FindDepartureBody` skips the target's parent. The
  station split cannot change those routes. Widening the rule would reach the
  Earth → Moon and Earth → planet plans, so treat it as separate work and say so
  in step 3 if it stays.
- The planet split must not move. `ThePlanetAnchorIsUnmovedByTheStationTarget`
  pins Gateway → Mars 90 d at 38.5411 km/s.
- Ares Junction → Gateway fastest is **54.16210 d / 69.541 km/s** and must stay
  put: it is a heliocentric start with no climb.
- A station plan can add speed at the match, so a retrograde test is wrong here.
- The station floor is **faster but costlier** (54.36 d / 98.409 vs 59.57 d /
  76.494): the shortest trip takes the 16 km/s cap. That is the measured answer,
  not a regression to tune; fixed durations are cheaper. Do not "fix" it by
  loosening a gate or the cap.
- `MeasureStationSplitCandidate` rebuilds the hold state per candidate; it is
  test-only now. The shipped chooser builds the geometry once per search.
- The cheap cost is a ranking model: it omits the terminal's own cost and its
  burns are endpoint velocity changes. Compare winners, not plan totals.
- `LastDepartureSplitSeconds` records the **last** chooser call. A `Plan` floor
  makes one per probe, so read it after a single `PlanAtDuration` when measuring
  the 50 ms budget.
- Keep each step in one commit with code, tests, concept updates, `Docs/log.md`
  and this handoff. Stop after two failures on the same step and leave the
  failure and last command here.
