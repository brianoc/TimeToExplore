# Handoff

**Active handoff (2026-11-19): pin the burn–coast–burn departure break to the
sphere of influence.** The design is the follow-up to
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md); read that note and
its *Known gap* section first. Do not redesign the profile in a step; if a step
cannot proceed without changing the note, stop and ask.

## Goal

Make the departure break do only what its name says: leave the parking orbit and
arrive at the departure sphere of influence at escape speed, so the steady 1 g
burn starts at the boundary instead of millions of kilometres outside it. Then
measure whether fast trips — 8 days and 6 days to Mercury at 1 g — close, which
they currently do not because the break is stretched to the burn's backwards
solved start.

## The evidence this starts from

Measured at the epoch (2026-11-15) from Gateway to Mercury, 1 g level
(9.70 m/s², ship limit 10.0 m/s²), conic capture:

| | 8-day plan today |
|---|---:|
| Sphere of influence | 925 Mm |
| Break end distance from Earth | **4,366 Mm (~4.7 SOI)** |
| Break end speed relative to Earth | 32.2 km/s |
| Break duration | 0.26 d |
| Break peak thrust | **53.2 m/s²** |
| Departure burn | 0.30 d, 250 km/s |
| Brake | 0.56 d, 225 km/s |
| Total delta-v | 1,231 km/s |

Both fixed burns are at the commanded 9.7 m/s²; only the break is over the
limit. The same trip is rejected solely for the break. At 6 days the quintic
solve has diverged (3,738 m/s², 8,721 km/s) and is not a real plan at any drive
size; 8 days is the useful target.

Two other defects were found in the same probe and belong to step 2:

- `Plan()` reports a Mercury minimum of 12.32 d at 1 g, but 11.0 d closes and
  10.8 d is refused at the drive limit. The advertised floor is about 1.3 d too
  conservative, so the slider hides the most aggressive burns.
- A plan rejected for exceeding maximum thrust has `IsFeasible == false` but a
  null `InfeasibleReason`, so the nav computer says "Plan infeasible" with no
  cause. The 60-day Mercury plan (outside the 3× window) is degenerate as well
  (peak 176,855 m/s²); out of scope, note it and move on.

## How this runs

A **caretaker session** drives a sequence of **one sub-agent per step**, one at
a time. The caretaker is deliberately light so its context stays clean.

- The caretaker reads this file, the current step, and the sub-agent's one-line
  result plus commit hash. It does not read source files or full test logs.
- Each sub-agent gets a **self-contained prompt**: the step, the files it may
  touch, the design-note path, the constraints below, and the verification
  command. It runs the checks, updates the affected concept and `Docs/log.md`,
  updates this handoff (mark the step done, set the next step), and commits.
- **One sub-agent at a time.** Never start the next step until the previous
  commit is in and the tree is clean.
- A step is done when the **narrow check for that step** is green (see
  *Verification*) and the commit contains the code, the tests, the concept
  update, the log entry, and the handoff update. Run only the necessary band.
- **Stop after two failures on the same step.** Leave the failure and the last
  command in this file and end the run.
- **Stop for any decision the design note does not settle.** In particular do
  not choose the break's end state, change an ADR, or widen the scope
  unilaterally.

## Steps

| Step | Deliverable | Done when |
|---|---|---|
| 0 | Known gap, log entry, this plan | `python3 tools/validate_okf.py` clean (done) |
| 1 | Pin the departure burn's start to the clearance radius in `TripPlanner`; solve the burn duration and the break's own duration; measure 8/6/12.3/90 d | `tools/check.sh --tests flight` green; 8 d closes at 1 g or the step stops and reports |
| 2 | True minimum search and a reason for the max-thrust rejection | `tools/check.sh --tests flight` green; the slider floor is the true floor |
| 3 | Docs pass, log, visual evidence, clear this handoff | `tools/check.sh --fast` and the validator clean |

### Step 0 — done (this commit)

The *Known gap* section in the design note, the log entry, and this plan.

### Step 1 — pin the break

In `TripPlanner.BuildBurnCoastBurn` and its helpers:

- Pin the departure burn's **start** to the clearance radius
  (`DepartureBurnClearance * SOI`, currently 1.02) on the escape ray, at the
  break's end state. The break must end there, not wherever the backwards burn
  lands.
- The break's **duration** is currently inherited from the reference coast
  (`quinticSeconds = referenceCruise.StartJulianDate - t0`). Decouple it: a
  break that only reaches the boundary can take longer, and that longer time is
  the point of the change. Solve the duration that reaches the boundary at the
  chosen end state.
- The departure burn's **duration** is currently
  `(coast departure speed - escape) / a`. With the start pinned, it becomes a
  solved quantity: the burn must start at the boundary and end at the coast's
  start. Solve the burn duration and the coast start together (one root find on
  the coast start distance, as today, with the burn start fixed instead of
  derived).
- Keep the brake, the coast, and the conic/hybrid capture chain untouched. The
  seams must stay continuous (< 1 cm, 1e-5 m/s) and the arrival exact.
- The break's end state is a **design decision**: the natural choice is the
  local escape speed along the escape ray, which makes the break's job constant
  and puts all the speed in the burn. If the note does not settle it, stop and
  ask; do not pick a different speed silently.
- Measure and report, at 1 g and the epoch, for Mercury: 8 d, 6 d, the current
  advertised minimum (12.32 d), and the old 12.3 d case; plus the 90 d Mars
  case. Record days, break duration/peak/delta-v, burn duration/delta-v, coast,
  brake, total delta-v, and feasibility. If 8 d still does not close at 1 g,
  **stop and report the numbers**; do not tune the profile or raise the level to
  make it fit.
- Tests: the break's end is at the clearance radius (within a tolerance); the
  burn starts there; the 8 d Mercury plan closes at 1 g; the existing 12.3 d
  Mercury and 90 d Mars plans still close with continuous seams; `0 g` still
  delegates to `Coast`; a level too low for the trip still rejects.

### Step 2 — the floor and the reason

- Make `Plan()`'s minimum search reach the true floor (11.0 d closes at 1 g;
  10.8 d is refused at the drive limit). The growing search currently stops
  early; tighten it or replace it with a bracketed solve. The slider then
  exposes the burns the profile can actually fly.
- Give a plan rejected for `PeakThrustMps2 > MaxAccelerationMps2 * (1 + eps)` an
  `InfeasibleReason` naming the arc and the required peak, so the nav computer
  shows a cause. This is the `IsFeasible` check in `TrajectoryPlan`, not just the
  builder's reason strings.
- Tests: the true floor for Mercury at 1 g is found (and is below the old
  12.32 d); a trip just under the floor is rejected with a non-empty reason; the
  HUD shows the reason. Re-run `--tests flight`; add `--tests presentation,smoke`
  if the window or report changes.
- Do not fix the 60-day degenerate plan unless it is cheap; note it in the log.

### Step 3 — close out

- Update the design note (remove the known gap once it is fixed, record the
  measured break and the new floors), navigation and arrival, the flight model,
  the flight session, testing, architecture, and the roadmap as the code
  requires. Add the log entry.
- Visual evidence: the break is now visibly longer and gentler, so finish with
  screenshots or a short video of the route and the nav computer, in the chat.
  The previous session's method works: a copy of the project with a warm
  `Library` in `.cache`, enter play mode in batch mode, render a clone camera to
  a `RenderTexture`, set the HUD canvas to `ScreenSpaceCamera` with a
  `planeDistance` beyond the near clip, and call the frame work by hand. Do not
  use a standalone player: it freezes about 2 s after launch on this machine
  regardless of what it draws.
- Clear this file to the "no active handoff" stub.

## Verification

```bash
tools/check.sh --tests flight                # steps 1–2
tools/check.sh --tests presentation,smoke    # step 2 if the window/report moves
tools/check.sh --fast                        # step 0 and 3
python3 tools/validate_okf.py                # whenever a concept changes
```

Run only the narrow band for the step. Run the full suite only if the change
crosses categories or a narrow run fails in a way that points elsewhere. Report
the exact command and its result. Do not record a test count in the bundle.

## Constraints and traps

- **One revolution only.** `OrbitMath.SolveKepler` wraps the mean anomaly and
  `ConicArc` assumes a single pass. No multi-rev coasts, no long-way Lambert.
- **Never burn across the SOI.** The boundary is the point of the plan; the
  burn starts outside it and never crosses it. `DepartureBurnStaysOutside` must
  still pass.
- **The clearance solve is not a bisection to tighten.** `TrySolveDepartureExit`
  grows the coast start until the *backwards solved* burn clears 1.02 SOI, and at
  8 d it lands at 4.7 SOI. Pinning is a constraint on the burn's start, not a
  larger clearance search.
- **The brake is not anti-velocity.** Its direction is the change the chain
  needs; the slow-arrival rejection is deliberate. Do not "fix" it.
- **Capture stays conic/hybrid.** Never use the quintic capture to fit the plan;
  do not raise parking orbits.
- **`0 g` is exactly `Coast`.** `Powered` and `Coast` keep their current
  behaviour; only `BurnCoastBurn` changes.
- **The advertised minimum is not the floor.** Until step 2 lands, `Plan()`
  overstates it; measure with `PlanAtDuration`.
- **Do not treat the 4.7 SOI endpoint as physics.** It is an artefact of the
  backwards solve; that is the whole premise of this plan.
- Assembly rules: Core has no engine references; Simulation has
  `noEngineReferences`; no `UnityEngine.Input` or Input System calls outside the
  two seam files.
- Do not force-close an open Unity editor. `tools/check.sh` isolates its run in
  `.cache/test-project`.
- Add tests for every new behaviour. Docs are part of done. No test counts.
- `.cache/shot-project` is a disposable ignored copy of the project with a
  temporary render harness. Do not commit it; it is stale after step 1.

## Sub-agent prompt template

```text
You are step <N> of the pinned-break plan in
/Users/brian.oconnor/Personal/TimeToExplore. Read HANDOFF.md and
Docs/simulation/burn-coast-burn.md first, including the Known gap section. Do
only step <N>: <deliverable>. Touch only: <files>. Follow the constraints in
HANDOFF.md. Add tests for every new behaviour. Iterate and finish with the
narrow check for this step (see Verification), not the full suite. Update the
affected concept and Docs/log.md, mark step <N> done in HANDOFF.md, and commit
everything as one commit starting with `<message>`. Do not start step <N+1>.
Reply with: the commit hash, the exact check command and result, the measured
numbers this step asked for, and any trap the next step needs.
```

## Next action

Start **step 1** with a fresh sub-agent: pin the departure burn's start to the
clearance radius, decouple the break's duration, solve the burn duration and the
coast start, and measure Mercury at 8, 6, 12.32, and 12.3 days and Mars at 90
days at 1 g. The gate is whether 8 days closes at 1 g; if it does not, stop and
report rather than tuning the profile.
