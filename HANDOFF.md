# Handoff — station departure split

**Plan in progress (2026-11-26).** Branch `work/station-departure-split` in this
worktree. The target is the **first** follow-up in
[the roadmap](Docs/production/roadmap.md#follow-ups-from-the-departure-climb-split-2026-11-26):
a station plan currently keeps the unforced minimum-energy climb because its
terminal velocity match has no capture chain for the cheap chooser to cost
against. The planet split is [ADR-022](Docs/decisions/adr-022-departure-split.md);
the station profile is [Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md)
and [Navigation and arrival](Docs/simulation/navigation-and-arrival.md).

**Step 1 is committed. Step 2 is next.**

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
- **No shipping plan changed.** The chooser still leaves a station unforced, and
  `Build` hands every station build a fixed `-1`.

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

## Step 2 — wire the split into station builds and the station floor

- Let `ChooseDepartureVInfinityMps` cost a **station** target when
  `FindDepartureBody` is non-null, using `DepartureSplitCost.AtStationHold` and
  the planet grid and refinement. Return `-1` when there is no climb or no
  candidate fits.
- `Build` currently returns `BuildWithRetry(..., station: true,
  forcedVInfinityMps: -1.0)` for every station: pass the chosen value instead.
  `BuildStationTrip` already accepts `forcedVInfinityMps` and threads it through
  its pin and settle passes, so hold one choice for the whole build rather than
  re-choosing inside the loop.
- `PlanStationFloor` calls `BuildStationTrip` directly per probe, so route its
  probes through the chooser as well (or through `Build`); a chooser wired only
  into `Build` will not reach the floor search.
- Re-baseline the station anchors when the numbers settle: Mars 300 km →
  Gateway 120 d is **25.648 km/s** today and Gateway → Ares Junction 120 d is
  **24.733**; the cheap winners there are 1.0 and 0.0 km/s while the full probe
  prefers 1.5 and 0.25, so pin the certified plan, not the model's total.

**Acceptance:** the shipped station plan is the cheap chooser's choice; the
coast floor from Mars orbit to Gateway is faster and cheaper than the unforced
one and certified; every seam closes inside the gate and the arrival is exact;
the planet floors and the heliocentric-start station anchors are unchanged;
tests pin the new numbers. `tools/check.sh --tests flight` is green.

## Step 3 — re-baseline anchors, windows and docs

Update the station call table in [Testing and verification](Docs/technical/testing.md),
the roadmap's station section and follow-up list, the burn–coast–burn and
navigation concepts, the ADR-022 note that stations keep the unforced climb,
`Docs/log.md`, and any test comments that quote the old numbers. The trip
window stays `[minimum, 3 × minimum]`, so it moves with the station floor.

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
156 s (the first run paid the import; 210 tests, 0 failed, 0 skipped),
`tools/check.sh --fast` passed in 6 s, and
`.venv/bin/python tools/validate_okf.py` reported 49 concepts, 0 warnings, 0
errors. `StationDepartureSplitTests` ran four tests in 4.3 s, so it stays in
`flight`, not `deep`.

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
- `MeasureStationSplitCandidate` rebuilds the hold state per candidate; the
  shipping chooser should build the geometry once per search, as the planet
  chooser does.
- The cheap cost is a ranking model: it omits the terminal's own cost and its
  burns are endpoint velocity changes. Compare winners, not plan totals.
- Keep each step in one commit with code, tests, concept updates, `Docs/log.md`
  and this handoff. Stop after two failures on the same step and leave the
  failure and last command here.
