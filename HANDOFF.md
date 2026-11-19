# Handoff

**Active handoff (2026-11-19): the pinned break, step 2 of 3.** Step 1 landed:
the break ends on the clearance sphere at escape speed and the departure burn
and coast are solved from it; 8, 6 and 4 days to Mercury now close at 1 g. The
design is the follow-up to
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md); read that note's
*The pinned break* section first. Step 2 is the floor and the reason below. Do
not redesign the profile in a step; if a step cannot proceed without changing
the note, stop and ask.

## Goal

Step 1 made the departure break do only what its name says: it ends on the
clearance sphere at escape speed and the steady burn starts there. That landed,
and the fast trips close (8 days and 6 days to Mercury at 1 g). Step 2 is to
expose the true floor and give a rejected plan its reason: the planner's minimum
search now finds 3.94 days for Mercury at 1 g and 9.64 days for Mars at 0.3 g,
but it is still the search's first feasible plan rather than a solved minimum,
and a max-thrust rejection still carries a null `InfeasibleReason`.

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
size; 8 days was the useful target. **The pin changed that:** the break now ends
at 1.02 SOI at 0.919 km/s, the 8-day plan closes at 1 g (break peak 3.02 m/s²,
total 570 km/s), and the 6-day plan closes at 770 km/s. The measured table is in
the design note.

Two other defects were found in the same probe and belong to step 2:

- The advertised floor was 12.32 d for Mercury at 1 g, but 11.0 d closed and
  10.8 d was refused at the drive limit. The pin moved the floor much further:
  `Plan()` now reports 3.94 d for Mercury at 1 g and 9.64 d for Mars at 0.3 g,
  because the fast plans are no longer rejected for the break. That is still the
  search's first feasible plan, not a solved minimum, so the floor work remains
  — just around 3.9/9.6 d.
- A plan rejected for exceeding maximum thrust has `IsFeasible == false` but a
  null `InfeasibleReason`, so the nav computer says "Plan infeasible" with no
  cause. The 60-day Mercury plan (outside the 3× window) is still degenerate
  (peak 176,855 m/s²) and now fails in the coast skeleton with a misleading
  Lambert "needs a transfer short of 180 deg" at a 97.9° transfer angle; out of
  scope, note it and move on.

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
| 0 | The gap, log entry, this plan | `python3 tools/validate_okf.py` clean (done) |
| 1 | Pin the departure burn's start to the clearance radius in `TripPlanner`; solve the burn duration and the break's own duration; measure 8/6/12.3/90 d | **done** — `tools/check.sh --tests flight` green; 8 d closes at 1 g |
| 2 | True minimum search and a reason for the max-thrust rejection | `tools/check.sh --tests flight` green; the slider floor is the true floor; `--tests presentation,smoke` green after the window change |
| 3 | Docs pass, log, visual evidence, clear this handoff | `tools/check.sh --fast` and the validator clean |

### Step 0 — done (this commit)

The gap section that step 1 later replaced, the log entry, and this plan.

### Step 1 — done (this commit)

The break ends on `1.02 × SOI` at the local escape speed along the escape ray;
the departure burn holds that ray and its ray angles, its duration, and the
coast are one forward solve, with the brake still integrated backwards from the
entry. The break's duration is solved against the drive's limit. `0 g` still
delegates to `Coast`, the arrival is exact, and the seams are under a
centimetre and 1e-5 m/s. Measured at 1 g from Gateway: Mercury 8 d 570 km/s
(break 0.553 d / 3.02 m/s² / 92.9 km/s; burn 0.307 d / 257.1 km/s; coast
6.594 d; brake 0.258 d / 216.3 km/s), 6 d 770 km/s, 12.32 d 379 km/s, 12.3 d
380 km/s, Mars 90 d 45.4 km/s. The 8-day flight arrives 0.05 m / 4.4e-5 m/s.
Verification: `tools/check.sh --tests flight` passed;
`.venv/bin/python tools/validate_okf.py` clean.

Traps carried into step 2:

- **`Plan()`'s floor moved.** It now reports 3.94 d for Mercury at 1 g and
  9.64 d for Mars at 0.3 g; the old 12.32/57.44 d floors were artefacts of the
  break. The search's first feasible plan is still not a solved minimum, so the
  floor work is still needed, just around 3.9/9.6 d, not 11.
- **The HUD and bootstrap tests assume a 20-day floor.**
  `SolarSystemHudTests.TheCruiseProfileControlDefaultsToPoweredAndRebuildsThePreview`
  and `BootstrapSmokeTests.ABurnCoastBurnFlightArrivesThroughTheBootstrap` assert
  `minimum > 20 d`; the Mars window is now 9.64–28.91 d. They are presentation/
  smoke tests, outside step 1's narrow band, and step 2 must update them with
  the window change.
- **The 60-day Mercury plan fails in the coast skeleton** with a `CoastReason`
  whose transfer angle is 97.9°, so the "needs a transfer short of 180 deg"
  wording is misleading there. It is still degenerate; the reason work should
  cover it.
- **The departure burn must hold the escape ray.** A velocity-following burn
  leaves a 269 km/s seam at 8 d; do not "fix" it back to the heliocentric
  velocity. `DepartureBurnStaysOutside` must still pass.

### Step 2 — the floor and the reason

- Make `Plan()`'s minimum search reach the true floor. It now finds 3.94 d for
  Mercury at 1 g and 9.64 d for Mars at 0.3 g, but that is the search's first
  feasible plan; the true floor is at or below it and the old 10.8/11.0 d probe
  is stale. Tighten the search or replace it with a bracketed solve, and
  re-measure the fast end (3.9 d Mercury and 9.6 d Mars are the new reference).
  The Mars window moved to 9.64–28.91 d at 0.3 g; the slider then exposes the
  burns the profile can actually fly.
- Give a plan rejected for `PeakThrustMps2 > MaxAccelerationMps2 * (1 + eps)` an
  `InfeasibleReason` naming the arc and the required peak, so the nav computer
  shows a cause. This is the `IsFeasible` check in `TrajectoryPlan`, not just the
  builder's reason strings.
- Update `SolarSystemHudTests` and `BootstrapSmokeTests`: both assert the
  profile's minimum is greater than 20 days, which the pinned break made false
  (Mars at 0.3 g is 9.64 d). Their window assertions and any 57-day text in
  presentation and smoke go with this change.
- Tests: the true floor for Mercury at 1 g is found (it is at or below 3.94 d);
  a trip just under the floor is rejected with a non-empty reason; the HUD shows
  the reason. Re-run `--tests flight`; add `--tests presentation,smoke` because
  the window changed.
- Do not fix the 60-day degenerate plan unless it is cheap; its new reason is a
  misleading Lambert "needs a transfer short of 180 deg" at 97.9°, so note it
  in the log.

### Step 3 — close out

- Update the design note (it now carries *The pinned break* and the measured
  table, and *The floor after the pin*), navigation and arrival, the flight
  model, the flight session, testing, architecture, and the roadmap as the code
  requires. The window is now 9.64–28.91 d at Mars and the fast end is 3.94 d at
  Mercury; the old 57.44/12.32 d statements in navigation and the flight model
  are stale. Add the log entry.
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

Last run (step 1): `tools/check.sh --tests flight` passed, and
`.venv/bin/python tools/validate_okf.py` reported 38 concepts, 0 warnings,
0 errors. The system `python3` lacks PyYAML, so use `.venv/bin/python`.

```bash
tools/check.sh --tests flight                # steps 1–2
tools/check.sh --tests presentation,smoke    # step 2, because the window moved
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
- **The clearance solve is not a bisection to tighten.** The old
  `TrySolveDepartureExit` grew the coast start until the *backwards solved* burn
  cleared 1.02 SOI, and at 8 d it landed at 4.7 SOI. Pinning is a constraint on
  the burn's start, not a larger clearance search; that backwards solve is gone,
  and step 2 must not reintroduce it.
- **The brake is not anti-velocity.** Its direction is the change the chain
  needs; the slow-arrival rejection is deliberate. Do not "fix" it.
- **Capture stays conic/hybrid.** Never use the quintic capture to fit the plan;
  do not raise parking orbits.
- **`0 g` is exactly `Coast`.** `Powered` and `Coast` keep their current
  behaviour; only `BurnCoastBurn` changes.
- **The advertised minimum is still not a solved floor.** `Plan()` now returns
  3.94 d for Mercury at 1 g and 9.64 d for Mars at 0.3 g, but that is the search's
  first feasible plan; measure a pinned time with `PlanAtDuration`.
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
Docs/simulation/burn-coast-burn.md first, including *The pinned break*. Do
only step <N>: <deliverable>. Touch only: <files>. Follow the constraints in
HANDOFF.md. Add tests for every new behaviour. Iterate and finish with the
narrow check for this step (see Verification), not the full suite. Update the
affected concept and Docs/log.md, mark step <N> done in HANDOFF.md, and commit
everything as one commit starting with `<message>`. Do not start step <N+1>.
Reply with: the commit hash, the exact check command and result, the measured
numbers this step asked for, and any trap the next step needs.
```

## Next action

Start **step 2** with a fresh sub-agent: make `Plan()`'s minimum search reach the
true floor around 3.9 d Mercury / 9.6 d Mars, give a max-thrust rejection a
reason, and update the presentation and smoke window assertions. The measured
numbers and traps are in *Step 1 — done* above. Do not start step 3.
