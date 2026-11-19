# Handoff

**Active handoff (2026-11-19): the pinned break, step 3 of 3.** Steps 1 and 2
landed: the break ends on the clearance sphere at escape speed and the
departure burn and coast are solved from it, and `Plan()` now advertises the
true floor (3.46 d to Mercury at 1 g, 8.03 d to Mars at 0.3 g) with a named
cause for a max-thrust rejection. The design is the follow-up to
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md); read that note's
*The pinned break* and *The floor after the pin* sections first. Step 3 is the
close-out: the documentation pass, visual evidence, and clearing this file. Do
not redesign the profile. If a step cannot proceed without changing the note,
stop and ask.

## Goal

Step 1 made the departure break do only what its name says: it ends on the
clearance sphere at escape speed and the steady burn starts there. Step 2 made
the planner find the true floor and explain a rejection: the advertised minimum
is the shortest trip whose chain closes and whose peak fits the drive, and a
plan over the drive carries an `InfeasibleReason` naming the arc and its
required peak. Step 3 is the documentation pass, the visual evidence, and
clearing this handoff.

## The measurements the plan set out from

Measured at the epoch (2026-11-15) from Gateway to Mercury, 1 g level
(9.70 m/s², ship limit 10.0 m/s²), conic capture, before step 1:

| | 8-day plan, old chain |
|---|---:|
| Sphere of influence | 925 Mm |
| Break end distance from Earth | **4,366 Mm (~4.7 SOI)** |
| Break end speed relative to Earth | 32.2 km/s |
| Break duration | 0.26 d |
| Break peak thrust | **53.2 m/s²** |
| Departure burn | 0.30 d, 250 km/s |
| Brake | 0.56 d, 225 km/s |
| Total delta-v | 1,231 km/s |

Both fixed burns are at the commanded 9.7 m/s²; only the break was over the
limit. The same trip was rejected solely for the break. **The pin changed
that:** the break now ends at 1.02 SOI at 0.919 km/s, the 8-day plan closes at
1 g (break peak 3.02 m/s², total 570 km/s), and the 6-day plan closes at
770 km/s. The measured table is in the design note.

Three defects were found in the same probe; step 2 closed the first two:

- The advertised floor was 12.32 d for Mercury at 1 g. Step 1 moved the raw
  reading to 3.94 d and 9.64 d, still the search's first feasible plan; step 2
  replaced the search and measured the true floors at **3.46 d** and **8.03 d**.
- A plan rejected for exceeding maximum thrust had `IsFeasible == false` but a
  null `InfeasibleReason`, so the nav computer said "Plan infeasible" with no
  cause. Step 2 populates it and shows it in both surfaces.
- The 60-day Mercury plan (outside the 3× window) is still degenerate (peak
  176,855 m/s²) and fails in the coast skeleton with a misleading Lambert
  "needs a transfer short of 180 deg" at a 97.9° transfer angle. It remains out
  of scope and is recorded in the log.

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
| 2 | True minimum search and a reason for the max-thrust rejection | **done** — `tools/check.sh --tests flight` green; the slider floor is 3.46 d (Mercury 1 g) and 8.03 d (Mars 0.3 g); `--tests presentation,smoke` green after the window change |
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

### Step 2 — done (this commit)

`Plan()` searches for the burn–coast–burn floor instead of returning its
growth loop's first feasible plan: half the kinematic estimate, grow to a
feasible duration, one 15% step back to a rejected shorter request, then a
bisection that retries a failing probe 0.2% higher so a non-convergence hole
does not become the floor. The pinned departure solve runs to 48 iterations at
1e-5 m/s (the documented seam level) and the floor search also requires the
chain to close: the near-floor plans can fit the peak while the brake misses
the capture entry by hundreds of thousands of kilometres. Measured at the
epoch from Gateway, conic capture: Mercury at 1 g **3.46 d** (peak 9.70 m/s²,
1,763 km/s; 3.43 d refused because the departure break needs 10.2 m/s²), Mars
at 0.3 g **8.03 d** (peak 5.27 m/s², 1,170 km/s; 7.80 d refused at 58.1 m/s²).
The Mars window is **8.03–24.09 d**. The 8.03 d flight arrives 0.1 m /
1e-4 m/s on the parking orbit. `TrajectoryPlan.EnsureInfeasibleReason` names
the arc and the peak on every returned plan the drive cannot fly, and the
details panel shows it beside "Plan infeasible"; the flight panel's `Note` row
already showed `Failure`. The design note and the log carry the numbers.
Verification: `tools/check.sh --tests flight` passed; `tools/check.sh --tests
presentation,smoke` passed; `.venv/bin/python tools/validate_okf.py` clean.

Traps carried into step 3:

- **The true floor is set by brake closure just above the drive crossing.**
  Mercury 3.44–3.45 d fits the peak but leaves the brake 375,000 km from the
  entry; Mars 8.00 d misses by 7,493 km. The gate is 1 km position and 1 m/s
  velocity, well above the millimetre seams the tested trips close to. Do not
  loosen it to chase a lower number: the smoke flight fails with a 30,000 km
  arrival error when the advertised plan is broken.
- **The Mars window is 8.03–24.09 d**, not 9.64–28.91; the fast Mercury end is
  3.46 d, not 3.94. Any 57.44/12.32/9.64/28.91 d statement in navigation, the
  flight model, the flight session, testing, architecture, or the roadmap is
  stale and belongs to the step 3 docs pass.
- **`IsFeasible` still reads only the peak**; the reason is populated by
  `EnsureInfeasibleReason` on the plans `Plan()` and `PlanAtDuration()` return.
  A direct `Build` inside the search must stay reason-free so over-thrust
  trials can grow. Do not move the reason into the constructor.
- **The 60-day Mercury plan is still degenerate** with a misleading Lambert
  reason at 97.9°; it is noted in the log, not fixed.
- **The departure burn must hold the escape ray.** A velocity-following burn
  leaves a 269 km/s seam at 8 d; do not "fix" it back to the heliocentric
  velocity. `DepartureBurnStaysOutside` must still pass.

### Step 3 — close out

- Update the design note (it now carries *The pinned break*, the measured
  table, and *The floor after the pin*), navigation and arrival, the flight
  model, the flight session, testing, architecture, and the roadmap as the code
  requires. The window is now **8.03–24.09 d** at Mars and the fast end is
  **3.46 d** at Mercury; the old 57.44/12.32/9.64/28.91 d statements in
  navigation and the flight model are stale. Add the closing log entry.
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

Last run (step 2): `tools/check.sh --tests flight` passed;
`tools/check.sh --tests presentation,smoke` passed; `.venv/bin/python
tools/validate_okf.py` is clean after the concept edit (re-run it in step 3).
The system `python3` lacks PyYAML, so use `.venv/bin/python`.

```bash
tools/check.sh --tests flight                # steps 1–2
tools/check.sh --tests presentation,smoke    # step 3, after the window moved
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
  and a later step must not reintroduce it.
- **The brake is not anti-velocity.** Its direction is the change the chain
  needs; the slow-arrival rejection is deliberate. Do not "fix" it.
- **Capture stays conic/hybrid.** Never use the quintic capture to fit the plan;
  do not raise parking orbits.
- **`0 g` is exactly `Coast`.** `Powered` and `Coast` keep their current
  behaviour; only `BurnCoastBurn` changes.
- **The floor is solved, not read from the growth loop.** `Plan()` starts at
  half the kinematic estimate and brackets the shortest trip whose chain closes
  and whose peak fits the drive; it returns 3.46 d for Mercury at 1 g and 8.03 d
  for Mars at 0.3 g. A pinned time still goes through `PlanAtDuration`.
- **A closed chain is part of feasible.** A near-floor plan can fit the peak
  with a brake hundreds of thousands of kilometres from the capture entry; the
  1 km / 1 m/s gate rejects those. Do not lower it to advertise a shorter trip.
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
Docs/simulation/burn-coast-burn.md first, including *The pinned break* and
*The floor after the pin*. Do
only step <N>: <deliverable>. Touch only: <files>. Follow the constraints in
HANDOFF.md. Add tests for every new behaviour. Iterate and finish with the
narrow check for this step (see Verification), not the full suite. Update the
affected concept and Docs/log.md, mark step <N> done in HANDOFF.md, and commit
everything as one commit starting with `<message>`. Do not start step <N+1>.
Reply with: the commit hash, the exact check command and result, the measured
numbers this step asked for, and any trap the next step needs.
```

## Next action

Start **step 3** with a fresh sub-agent: the documentation pass across the
affected concepts (the window is 8.03–24.09 d at Mars, the fast Mercury end is
3.46 d, and the max-thrust rejection now names its cause), visual evidence of
the longer, gentler break and the nav computer, and clearing this file to the
no-active-handoff stub. The measured numbers and traps are in *Step 2 — done*
above. Do not reopen the profile design.
