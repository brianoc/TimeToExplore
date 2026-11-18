# Handoff

**Active handoff (2026-11-18): burn–coast–burn trips.** The design is settled;
steps 1 and 2 are implemented. The source of truth for the design is
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md). Do not redesign it
in a step; if a step cannot proceed without changing it, stop and ask.

## Goal

A Go To plan the player can fly as: break orbit with a quintic, burn hard at a
commanded acceleration, coast fast on a Lambert conic, brake, and enter the
parking orbit with the conic/hybrid capture. The existing three arrival recipes
stay. This is a fourth recipe built from the same arc types.

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
  update, the log entry, and the handoff update. Run only the necessary band;
  the full suite is the repo default, not this task's default.
- **No screenshots or video this task.** The user asked to skip the visual
  evidence for this plan. Report the check command and result, and the commit,
  instead.
- **Stop after two failures on the same step.** Leave the failure and the last
  command in this file and end the run; do not thrash.
- **Stop for any decision the design note does not settle.** In particular do
  not choose a default profile, change an ADR, or widen the scope unilaterally.

## Steps

| Step | Deliverable | Done when |
|---|---|---|
| 0 | Design note, navigation link, index link, log, this handoff | `python3 tools/validate_okf.py` clean (done) |
| 1 | `LambertSolver` in Core + `LambertSolverTests` | `tools/check.sh --tests core` green (done) |
| 2 | Lambert coast in the planner: `CruiseStyle.Coast` builds quintic departure → Lambert `ConicArc` → existing conic/hybrid capture | `--tests flight` green; a plan closes and arrives (done) |
| 3 | Prograde departure fixed burn + `CruiseStyle.BurnCoastBurn` stitching the five arcs | `--tests flight` green; burn level respected; arrival exact |
| 4 | Nav-computer profile control, speed/time control, panel rows, colours/legend | `--tests presentation smoke` green; bootstrap smoke covers the new profile |
| 5 | Docs pass, log, clear this handoff | `tools/check.sh --fast` and the validator clean; no screenshot this task |

### Step 0 — done (this commit)

The design note, the link from navigation, the index entry, the log entries,
and this plan. No code.

### Step 1 — Lambert solver (done)

Create `TimeToExplore/Assets/Scripts/Core/LambertSolver.cs` and
`TimeToExplore/Assets/Tests/EditMode/LambertSolverTests.cs` (category `core`).
Zero-rev, prograde, short-way only. Universal variables; reuse
`KeplerPropagator.Stumpff`. Return `v₁`, `v₂`, and a clear "no solution" result.
Guard the 180° transfer where the classic `A` term vanishes. Tests: propagate
`v₁` for `Δt` and land on `r₂`; a 90° case; a Hohmann-like case against known
values; a high-energy hyperbolic case; short-way versus long-way; the 180°
guard. No planner or UI change.

Landed as `LambertSolver.SolveShortWay`, returning a `LambertSolution`
(`HasSolution`, `DepartureVelocityMps`, `ArrivalVelocityMps`,
`TransferAngleRad`) and rejecting the 180° geometry without throwing.
`tools/check.sh --tests core`: passed, 0 failed.

### Step 2 — the Lambert coast (done)

`TripPlanner.CruiseStyle { Powered, Coast }`, default `Powered`, separate from
`CaptureStyle`. `Coast` retargets the quintic departure to the Lambert `v₁` at
the departure SOI, coasts on a heliocentric `ConicArc` about the Sun, then runs
the existing conic or hybrid capture. The five-pass arrival loop splits what is
left between departure and coast in proportion to their kinematic floors (the
departure needs its share: a fixed few-hour escape cannot supply a real break
velocity), and the coast's arrival velocity is trimmed onto the chain's entry
state by an impulse at the target SOI. That impulse is the brake's placeholder
for step 3. `LambertSolver` rejections (`HasSolution == false`) become an
infeasible plan with the transfer angle in the reason; there is no long-way
fallback. `CoastTripPlannerTests` covers the chain and types, the unpowered
coast, the `v₁` retarget, the flown arrival and three-orbit hold, the
orders-of-magnitude delta-v drop, the t₀-invariance of the style switch, the
rejected long transfer, and the hybrid handover.
`tools/check.sh --tests flight`: passed, 0 failed.

### Step 3 — the burns

Add the prograde departure finite burn as the mirror of the hybrid capture, and
`CruiseStyle.BurnCoastBurn`: quintic break → finite burn at the chosen level →
Lambert coast → finite brake → capture. Solve the burn durations and coast so
the chain closes on the moving target. Tests: five-arc chain, burn level
respected, exact arrival, infeasible cases, `0 g` equivalence where meaningful.

### Step 4 — the control

Expose the profile beside the existing arrival buttons; reuse the trip-time
slider as the speed control; add `Coast`/burn rows to the flight report. The
route colours and legend already handle the arc types. Tests: HUD state, profile
switch rebuilds the preview, the legend gains/loses rows, and a bootstrap smoke
flight in the new profile.

### Step 5 — close out

Update the flight model, navigation, flight session, testing, architecture, and
roadmap as the code requires. Add the log entry. Clear this file to the
"no active handoff" stub. No screenshots or video for this task; report the
check result and the commit.

## Verification

```bash
tools/check.sh --tests core                  # step 1: Core and its tests
tools/check.sh --tests flight                # steps 2–3: planner and flight
tools/check.sh --tests presentation smoke    # step 4: HUD and frame loop
tools/check.sh --fast                        # steps 0 and 5: docs only
python3 tools/validate_okf.py                # whenever a concept changes
```

Run only the narrow band for the step. Run the full suite only if the change
crosses categories or a narrow run fails in a way that points elsewhere. Report
the exact command and its result. Do not record a test count in the bundle.

## Constraints and traps

- **Do not force-close an open Unity editor.** `tools/check.sh` isolates its
  run in `.cache/test-project`.
- **One revolution only.** `OrbitMath.SolveKepler` wraps the mean anomaly and
  `ConicArc` assumes a single pass. Multi-rev Lambert is out of scope.
- **`EscapeSpeedMps = 1_000` is a placeholder.** The powered departure still uses
  it. The coast path takes its break velocity from the Lambert `v₁` and must
  never fall back to the placeholder.
- **The quintic capture has a gravity ceiling.** Prefer the conic/hybrid capture
  for the new profile; do not park the giants high to make a quintic fit.
- **The trip-time slider is `[minimum, 3 × minimum]`.** That range is too small
  for a coast window. Recompute it from the new profile's feasible range; do
  not reuse `MaxDurationScale = 3`.
- **`% burn` is meaningless on a coast.** The panel must say what the drive is
  doing, not report a peak throttle as if it were a torch plan.
- **Assembly rules.** Core has no engine references; Simulation has
  `noEngineReferences`; route colour and HUD work belongs in Presentation. Do
  not add `UnityEngine.Input` or Input System calls outside the two seam files.
- **Docs are part of done.** Every behaviour change updates its concept, adds a
  dated `Docs/log.md` entry, and keeps this handoff current in the same commit.

## Sub-agent prompt template

```text
You are step <N> of the burn–coast–burn plan in
/Users/brian.oconnor/Personal/TimeToExplore. Read HANDOFF.md and
Docs/simulation/burn-coast-burn.md first. Do only step <N>: <deliverable>.
Touch only: <files>. Follow the constraints in HANDOFF.md. Add tests for every
new behaviour. Iterate and finish with the narrow check for this step (see
Verification), not the full suite. Update the affected concept and
Docs/log.md, mark step <N> done in HANDOFF.md, and commit everything as one
commit starting with `<message>`. Do not start step <N+1>. Reply with: the
commit hash, the exact check command and result, and any trap the next step
needs.
```

## Next action

Start **step 3** with a fresh sub-agent: add the prograde departure fixed burn
and `CruiseStyle.BurnCoastBurn`, stitching quintic break → finite burn →
Lambert coast → finite brake → conic/hybrid capture. Step 2's coast ends with an
impulse at the target SOI (`EntryImpulseVectorMps` on the inbound coast arc)
because the Lambert `v₂` need not equal the capture chain's entry velocity; the
brake can absorb that trim instead of modelling it as a separate impulse. The
departure fix in step 2 is an *allocation* fix, not a burn: the quintic break
gets a share of the trip proportional to its kinematic floor, and that share
must not be mistaken for the burn duration in step 3. The solver is prograde
short-way only, so a trip time sweeping 180° or more (exactly 180° included)
returns `HasSolution == false`; treat that as an infeasible trip time and clamp
the time window, not as a solver bug. `Plan()` alone finds the fastest feasible
coast (about 14 days to Mars at the epoch, with a huge arrival handover); the
trip-time control in step 4 is what picks a cheaper one.
Verify with `tools/check.sh --tests flight`.
