# Handoff

**Plan: a burn-only crossing, then a short-coast regime.** Work happens in this
worktree on `work/burn-only-profile`; `main` is untouched. The reasoning is in
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md), [full-field burns
and the powered-leg floor](Docs/simulation/powered-leg-floor.md), and
[ADR-015](Docs/decisions/adr-015-fixed-thrust-burns.md). This handoff records
only what is done, what is next, and the traps.

## Steps

1. **Pin the break — DONE and accepted.** The break's duration is solved so the
   quintic's peak commanded thrust equals `FixedBurnAccelerationMps2` (9.70 m/s²
   at 1 g, 2.942 at 0.3 g), behind the opt-in
   `TripPlanner.PinBreakToCommandAcceleration` (default **false**). See [the
   level pin](Docs/simulation/burn-coast-burn.md#the-level-pin-is-opt-in).
2. **Burn-only seam solve — DONE and accepted.** The pinned break, the capture
   chain at the arrival handover floor, and a two-burn seam with **no coast**:
   six unknowns against the six seam components at a fixed arrival date. See
   [the burn-only crossing](Docs/simulation/burn-coast-burn.md#the-burn-only-crossing).
3. **Burn-only minimum — DONE (this commit).** The seam is wrapped in a
   **moving-target loop** (`PlanBurnOnly(ship, target)`), and
   `TripPlanner.PlanFastest` returns the shorter feasible of that crossing and
   the coast floor. `FlightSession`'s preview window uses it, so the trip-time
   slider's minimum is the burn-only total. See [the nav computer's
   minimum](Docs/simulation/burn-coast-burn.md#the-nav-computers-minimum).
4. **Short coast and the handover — NEXT.** Lower `MinimumCoastSeconds`, solve
   the short-coast regime between the burn-only point and the coast floor, and
   verify the two branches agree on delta-v, seams, and feasibility. Re-sweep the
   window and then wire `Begin` and the gap into one buildable range.
5. **Fuel versus delta-v — LATER.** Compare `PropellantKg` /
   `PropellantFraction` and delta-v for the pin on/off at fixed trip times and at
   each floor, and for the burn-only versus coast minimum.

Do not start a step until the previous step's commit is in and the tree is
clean. Stop after two failures on the same step and leave the failure and last
command here.

## Current state

Steps 1–3 are committed. The product decision is in force: **the burn-only
crossing is the nav computer's minimum trip time**, while the coast floor is
still the default `Plan()` used by the fixed-duration anchors. `Plan()`,
`PlanAtDuration`, and the pin's default are unchanged, so every documented
anchor still passes: the anchor test pins Mercury 8 d 557.0380, Mars 90 d
44.3543, Uranus 400 d 182.3124, and the other three fixed-duration totals.

The range between the burn-only minimum and the coast floor is a **known gap**:
`PlanAtDuration` cannot build those durations yet, so a slider request there
returns a stretched or infeasible plan and the panel's `Note` says so. Step 4
fills it. Nothing in the window fakes a plan for the gap.

### The step-3 measurement (epoch 2026-11-15, Gateway → Mercury at 1 g)

From `TheBurnOnlyMovingTargetSettles` and `PlanFastestReturnsTheBurnOnlyMinimum`
output in `.cache/test-project/results-editmode.xml`:

- Seed: the coast floor, **3.21610 d**.
- Pass 0: chain at 3.21610 d, plan arrives **3.04470 d**, step **−14,808.9 s**.
- Pass 1: chain at 3.04470 d, plan arrives **3.04470 d**, step **−0.3 s**;
  settled.
- The rebuilt plan's seam: **2.2 m and 9.9 × 10⁻⁶ m/s**.
- Total **3.044696 d**, **2,433 km/s**. The fixed-date build at the plan's own
  arrival agrees to 0.00001 d (3.044696 against 3.044696).
- Planning time: **3.5 s**, including the coast floor the loop seeds from.
  `PlanFastest` (floor + crossing) is 3.4 s on a warm run.

Against the coast floor: **0.17140 d (4 h 7 min) shorter** and **667 km/s
(38%) more expensive** (2,433 against 1,766 km/s). Arc breakdown and seam gate
are the step-2 table in [the burn-only
crossing](Docs/simulation/burn-coast-burn.md#the-burn-only-crossing); the
moving-target plan reproduces it.

Minima the preview now advertises at the epoch, from Gateway:

| Target | Level | Minimum | Coast floor | Window |
|---|---|---:|---:|---|
| Mercury | 1 g | **3.04470 d** (burn-only) | 3.21610 d | 3.04–9.13 d |
| Mars | 0.3 g | **6.49501 d** (burn-only) | 6.73042 d | 6.50–19.49 d |
| Jupiter | 0.3 g | **12.54280 d** (burn-only) | 13.07 d | 12.54–37.63 d |
| Uranus | 0.3 g | coast floor | **33.60 d** | 33.60–100.81 d |
| Uranus | 1 g | coast floor | **33.65 d** | 33.65–100.96 d |

First `Plot()` in the warm test project: **2.81 s** (Uranus 0.3 g), **3.08** (1 g),
**5.07** (Mars 0.3 g), **3.89** (Mercury 1 g), **4.61** (Jupiter 0.3 g) —
against 1.80, 1.28, 2.99, 2.29, 3.03 s before this step. The extra time is the
moving-target solve; the floor search still certifies on its first certificate.

Two deliberate consequences, both recorded in the concept docs:

- The crossing always ends on the **conic** capture chain, so the `ARRIVAL` row
  does not change the drawn minimum's shape. A retime above the floor does.
- `FlightSession.Begin` still replans with `Plan()` and `PlanAtDuration`, so
  beginning at a burn-only minimum flies the coast floor's duration until step 4
  closes the gap. Wiring `Begin` to `PlanFastest` now would move every flight
  acceptance anchor, so it waits.

## Verification

From this worktree only:

```bash
tools/check.sh --fast          # docs, catalogue, generators, tooling
tools/check.sh --tests flight  # the planner/flight EditMode category
tools/check.sh                 # full EditMode + PlayMode before the step's commit
```

Last results (this commit):

- `tools/check.sh --fast` — **PASSED** (OKF bundle 44 concepts, catalogue 137
  bodies, both generators byte-identical, bright-star catalogue, tooling tests).
- `tools/check.sh --tests flight` — **PASSED** in 518 s: flight EditMode
  category, 0 failed, 0 skipped. The three new tests are
  `TheBurnOnlyMovingTargetSettles`, `PlanFastestReturnsTheBurnOnlyMinimum`, and
  `ThePreviewMinimumIsTheBurnOnlyPlan`.
- `tools/check.sh` — **PASSED** in 778 s: EditMode and PlayMode, 0 failed, 0
  skipped. The fixed-duration anchor test is in that run, so Mercury 8 d
  557.0380, Mars 90 d 44.3543, Uranus 400 d 182.3124 and the other three still
  pass with the default path untouched.

## Traps

- **Never run a command in `/Users/brian.oconnor/Personal/TimeToExplore`.** That
  is the main checkout with the open editor; it also has a modified
  `ProjectSettings.asset` that is not ours. Every command runs in this worktree.
  The main editor's Unity process stays open while the batch suites run; that is
  expected.
- `tools/check.sh` derives its root from its own path, so it is safe here; it
  writes `.cache/test-project` under this worktree.
- The `.venv` is a symlink. Do not `pip install`; it would change the shared venv.
- **Every preview plot now pays for `PlanFastest`** — the coast floor plus the
  moving-target crossing, about 1–2 s more. Do not call it per frame or on the
  main thread outside the existing preview worker. The window cache still keys
  on departure date, target, burn level, and arrival style; a repeat plot at the
  same key is free.
- **The gap is not a bug to paper over.** A retime between the burn-only minimum
  and the coast floor comes back stretched or infeasible on purpose. Step 4
  fills it with the short coast; do not make the slider clamp or substitute a
  plan there.
- The **fixed-date** `PlanBurnOnly(ship, target, arrivalJulianDate)` builds the
  chain once and its arrival lands where the solved brake ends (about 0.17 d
  from the supplied date on Mercury). Use the moving-target overload for a plan.
- The moving-target loop stops at **2 s** and **8 passes** and reports an
  unsettled plan as infeasible with its last step. Do not widen the tolerance to
  make a basin pass; fix convergence or report the refusal.
- The crossing always uses the **conic capture chain** (`AppendConicCapture`).
  The hybrid arrival style only shows above the floor.
- A style or level rebuild that is keeping a **minimum** now lands on the new
  minimum (`PreviewRequest.AtMinimum`). The two styles' burn-only totals differ
  by about 19 s on Mars, and the old number would land inside the gap. Removing
  this breaks `ChangingTheArrivalStyleRebuildsThePreviewAndLeavesTheShipStill`.
- `Begin` is unchanged on purpose: it still solves `Plan()` and retimes with
  `PlanAtDuration`. Do not wire it to `PlanFastest` before step 4.
- `PinBreakToCommandAcceleration` is still off by default. `PlanBurnOnly` pins
  its own break regardless of the property; keep that independent.
- The step-2 break cap is deliberately generous and uses the **commanded**
  acceleration, not `MaxAccelerationMps2`. The trace reports `BINDING`; the
  Mercury 1 g case is not binding.
- The seam's fixed-date build's largest residual is the brake's forward rebuild
  against the chain entry (102 m / 0.209 m/s). The moving-target rebuild closes
  tighter (2.2 m / 9.9 × 10⁻⁶ m/s). Do not re-tighten the solver's tolerance
  without re-measuring.
- Keep the documented floors and delta-v anchors. `Plan()` and
  `PlanAtDuration` are untouched in this step; only the preview minimum, the
  trip-time label, and the Mars/Mercury/Jupiter window ends moved.
- Update `Docs/log.md` under the current `YYYY-MM-DD` heading and this handoff in
  the same commit as the work. Run `tools/validate_okf.py` (or `check.sh --fast`)
  after the doc edits.
- Do not merge into `main` and do not push. A finished step is a commit on
  `work/burn-only-profile`.

## Step 4 notes

- **Short coast.** Allow the burn-only seam's coast to be non-negative instead
  of exactly zero, solve the branch, and let the floor search walk across the
  boundary. At the crossing point the two branches must agree on delta-v, seams,
  and feasibility — measure that, do not assume it.
- **One buildable range.** Once the short coast builds, the slider can ask for
  every duration from the minimum upward. Then `Begin` can use `PlanFastest`
  (and retime normally) without moving the acceptance anchors only if the
  anchors themselves are deliberately re-measured; otherwise keep `Begin` on the
  coast path and treat the minimum as preview-only until the anchors are
  re-baselined.
- **Re-sweep.** Re-measure every window end, the first-plot cost, and the
  burn-only-versus-coast delta-v pairs (Mars 1,592 against 1,172 km/s; Mercury
  2,433 against 1,766) at the boundary.
- **Fuel (step 5).** `TrajectoryPlan.PropellantKg` and `PropellantFraction` are
  the numbers; compare the pin on/off at fixed trip times and at each floor, and
  the burn-only versus coast minimum. Do not answer the fuel question from
  delta-v alone.
