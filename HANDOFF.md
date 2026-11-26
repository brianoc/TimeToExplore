# Handoff — station departure split

**Plan in progress (2026-11-26).** Branch `work/station-departure-split` in this
worktree. The target is the **first** follow-up in
[the roadmap](Docs/production/roadmap.md#follow-ups-from-the-departure-climb-split-2026-11-26):
a station plan currently keeps the unforced minimum-energy climb because its
terminal velocity match has no capture chain for the cheap chooser to cost
against. The planet split is [ADR-022](Docs/decisions/adr-022-departure-split.md);
the station profile is [Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md)
and [Navigation and arrival](Docs/simulation/navigation-and-arrival.md).

## What is done

- Worktree created from `main` at `3c50440`; branch `work/station-departure-split`.
- No code changed yet.

## The shape of the problem

- `TripPlanner.Build` chooses the split once per build for a planet
  (`ChooseDepartureVInfinityMps`) and hands the same value to every planet build
  in that call.
- It returns `-1` for a station (`target.Data.IsStation`), so
  `BuildStationTrip` keeps the unforced climb.
- `PlanStationFloor` calls `BuildStationTrip` directly, not `Build`, so the
  station floor search would not see a split even once the chooser supports it.
- A station plan has a climb only when the ship starts inside a planet's sphere
  (`FindDepartureBody`). Ares Junction → Gateway is a heliocentric start and has
  no climb, so its plan must not move.
- The station's terminal contract is the hold state
  (`StationHoldState`: station state at the arrival date, 500 m astern along its
  velocity, velocity matched). There is no capture chain and no
  `TransferConic`; a rank-only cost can use the hold state as its entry state
  and drop the chain's own duration.
- The station's brake can shed **or add** speed, so the rank cost is a magnitude,
  not a retrograde brake.

## Plan

### Step 1 — measure and prototype the station cost (no plan changes)

Add a station-capable rank-only candidate cost, reachable from tests only, that
uses the hold state at the arrival date as the entry and the station's
velocity-match burn as the brake. Measure it against the full fast-probe search
(`MeasureFullSplitCandidateCost`) on the station routes that have a climb:

- Gateway → Ares Junction and Gateway → Helios Watch at 60, 90, 120 and 160 d;
- Mars 300 km orbit → Gateway at 60, 90 and 120 d;
- a heliocentric start (Ares Junction → Gateway) as the no-climb control.

Record the chosen v∞, the total Δv at the cheap and full choices, the search
cost, and whether the 600 s common brake seed mis-ranks the candidates. If it
does, try one iteration with the candidate's own brake duration before
reporting. No shipping plan may change with the flag off or for station plans in
this step.

**Acceptance:** a measurement test prints the tables; the cheap choice is the
full search's or one grid step away; the search stays inside the 50 ms budget;
`tools/check.sh --tests flight` is green; the findings and the accepted cost
model are written into the handoff and a log entry.

### Step 2 — wire the split into station builds and the station floor

Generalise the rank-only cost so its entry state comes from either the planet
capture chain or the station hold state. Let `ChooseDepartureVInfinityMps` cost
a station target when the ship has a departure climb, hold the choice across
`BuildStationTrip`'s pin and settle passes, and route `PlanStationFloor`'s
probes through the same chooser (either `Build` or an explicit forced value per
probe). A station plan with no climb keeps `-1`.

**Acceptance:** the shipped station plan is the cheap chooser's choice; the
coast floor from Mars orbit to Gateway is faster and cheaper than the unforced
one and certified; every seam closes inside the gate and the arrival is exact;
the planet floors and the heliocentric-start station anchors are unchanged;
tests pin the new numbers. `tools/check.sh --tests flight` is green.

### Step 3 — re-baseline anchors, windows and docs

Re-run the station anchors and the HUD window through the new split. Update the
station call table in [Testing and verification](Docs/technical/testing.md), the
roadmap's station section and follow-up list, the burn–coast–burn and navigation
concepts (a station departure now chooses the split when it has a climb), the
ADR-022 note that stations keep the unforced climb, `Docs/log.md`, and any test
comments that quote the old numbers. Remove the follow-up from the roadmap when
the numbers are settled, or leave a precise note of what remains.

**Acceptance:** no stale pre-split station number in the bundle; the validator
passes; the station window (3 × floor) is pinned where it is observable.

### Step 4 — show the result and close out

Add or extend a capture that shows the affected result: a Gateway → station (or
Mars orbit → Gateway) plot through the real HUD, with the new floor and window
visible, and the flown arrival on the hold point. Run the full gate
(`tools/check.sh`) and put the exact command and result in the log. Clear
`HANDOFF.md` to the no-active-handoff stub in the same commit.

## Verification

```bash
tools/check.sh --tests flight        # the fast Unity loop (~2 min 20 s warm)
tools/check.sh --fast                # bundle and tools only
.venv/bin/python tools/validate_okf.py
tools/check.sh                       # full gate before the final commit (~29 min)
```

A targeted test can be run with Unity and the guard once the warm project
exists:

```bash
python3 tools/unity_guard.py \
  --log .cache/test-project/unity-filter.log \
  --stall-seconds 1800 --timeout-seconds 2400 -- \
  /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath .cache/test-project/TimeToExplore \
  -runTests -testPlatform EditMode \
  -testResults .cache/test-project/results-filter.xml \
  -testFilter TimeToExplore.Tests.EditMode.StationDepartureSplitTests \
  -logFile .cache/test-project/unity-filter.log
```

## Traps

- Do not work in `/Users/brian.oconnor/Personal/TimeToExplore`; that checkout is
  the person's open editor and takes merges only.
- `search /tmp` is not this machine's convention; use
  `/private/var/folders/.../T/opencode` if a scratch file is needed.
- The planet split must not move. `ThePlanetAnchorIsUnmovedByTheStationTarget`
  pins Gateway → Mars 90 d at 38.5411 km/s.
- Ares Junction → Gateway fastest is **54.16210 d / 69.541 km/s** and must stay
  put: it is a heliocentric start with no climb.
- The old station anchors that will move are Mars 300 km → Gateway (120 d,
  25.648 km/s; floor 59.57357 d / 76.494 km/s) and Gateway → Ares Junction
  (120 d, 24.733 km/s). They are documented in `Docs/technical/testing.md`; pin
  the re-baselined values in `StationTripPlannerTests` when the code lands.
- Windows are `[minimum, 3 × minimum]` in `FlightSession`; do not add a separate
  station rule. If the station floor moves, the window moves with it.
- A station plan can add speed at the match, so a retrograde test is wrong here.
- `BuildStationTrip` is called directly by `PlanStationFloor`; a chooser wired
  only into `Build` will not reach the floor search.
- Keep each step in one commit with code, tests, concept updates, `Docs/log.md`
  and this handoff. Stop after two failures on the same step and leave the
  failure and last command here.
