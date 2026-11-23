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
3. **Burn-only minimum — DONE and accepted.** The seam is wrapped in a
   **moving-target loop** (`PlanBurnOnly(ship, target)`), and
   `TripPlanner.PlanFastest` returns the shorter feasible of that crossing and
   the coast floor. `FlightSession`'s preview window uses it, so the trip-time
   slider's minimum is the burn-only total. See [the nav computer's
   minimum](Docs/simulation/burn-coast-burn.md#the-nav-computers-minimum).
4. **Begin flies the previewed plan — DONE (this commit).** `FlightSession.Begin`
   now takes its base plan from `PlanFastest`, so beginning at a burn-only
   minimum flies the no-coast crossing the preview drew; a coast-floor target
   still flies the coast floor, and a chosen longer time still re-times with
   `PlanAtDuration`. A preview at its minimum flies the **fresh** minimum: the
   real-HUD PlayMode test showed the clock moving between plot and Begin, where
   re-timing to the old number landed in the gap and rejected the flight. The
   flight acceptance anchors that began at the old floor are re-baselined. The
   range stays gapped until step 5.
5. **Short coast and the handover — DONE (this commit).** The gap between the
   burn-only minimum and the coast floor is filled by a **short-coast crossing**:
   the burn-only seam with a heliocentric conic coast of duration `c` between the
   departure burn and the brake, the requested total an input and the brake's
   duration the remainder. At `c = 0` it reduces to the burn-only seam. The
   pinned coast path was measured first and did **not** build at any of the three
   gap midpoints: the break needed 68.3 (Mercury 1 g, 3.1 d), 10.0 (Mars 0.3 g,
   6.6 d), and 664.6 m/s² (Jupiter 0.3 g, 12.8 d) against a 9.7 m/s² drive. Every
   slider duration from the minimum to the window's maximum now builds. See
   [the short coast](Docs/simulation/burn-coast-burn.md#the-short-coast).
6. **Fuel versus delta-v — DONE (this commit).** Measured the propellant the
   plans actually burn, from `TrajectoryPlan.PropellantKg` /
   `PropellantFraction`, for the pin on/off at fixed trip times and at each
   floor, and for the burn-only minimum, short-coast midpoint and coast floor.
   The answer: **the fast plans use more propellant, not less** — the Mercury
   1 g burn-only minimum buys 4 h 7 min for 667 km/s and 82.3 t, Mars 0.3 g
   5 h 39 min for 420 km/s and 48.3 t. The pin is a time/fuel trade, not a fuel
   saving at a fixed time. See [fuel versus
   delta-v](Docs/simulation/burn-coast-burn.md#fuel-versus-delta-v). No planner,
   flight-session or behaviour change.

Do not start a step until the previous step's commit is in and the tree is
clean. Stop after two failures on the same step and leave the failure and last
command here.

## Current state

Steps 1–5 are committed and accepted; step 6 is this commit. The product
decision is in force: **the burn-only crossing is the nav computer's minimum
trip time**, `Begin` flies the previewed plan, and the range up to the coast
floor builds as the short coast.
Step 6 answered the fuel question with measured numbers: **the fast plans use
more propellant, not less** (Mercury 1 g 4 h 7 min for 82.3 t; Mars 0.3 g
5 h 39 min for 48.3 t), the short coast buys it back at about 20 t/h at Mercury
and 8.6 t/h at Mars and Jupiter, and the pin is a time/fuel trade rather than a
fuel optimisation.

The plan is complete. The remaining decision is the caretaker's: **whether
`work/burn-only-profile` is ready to merge into `main` for human testing**. No
design change is queued, and no behaviour changed in step 6 — it adds one
diagnostic test and the documentation. `tools/check.sh` is green, so the branch
is ready for that decision.

The coast floor is still the default `Plan()` used by the fixed-duration
anchors; `Plan()`, `PlanFastest`, and the pin's default are unchanged, so every
documented anchor still passes: the anchor test pins Mercury 8 d 557.0380,
Mars 90 d 44.3543, Uranus 400 d 182.3124, and the other three fixed-duration
totals.

### The step-5 measurement (epoch 2026-11-15, Gateway, conic capture)

The pinned coast path was expected to be the smallest change, so it was measured
first. It does not build the gap. `PlanAtDuration` at the midpoints with
`PinBreakToCommandAcceleration` **false / true**:

| Target | Level | Midpoint | Pin | Feasible | Peak | Reason |
|---|---|---:|---|---:|---:|---|
| Mercury | 1 g | 3.1 d | off / on | no / no | 68.3 / 26.9 m/s² | departure break over the 10.0 m/s² drive |
| Mars | 0.3 g | 6.6 d | off / on | no / no | 10.03 / — | break over the drive; pinned path cannot place the burn |
| Jupiter | 0.3 g | 12.8 d | off / on | no / no | 664.6 / 6.99 × 10⁹ m/s² | break over the drive |

The coast path gives the break too little time because it allocates a real coast
(1.03 d at Mercury 3.1 d), so the break has to command far more than the drive.
The short-coast seam closes all three:

| Target | Level | Midpoint | Δv | Coast | Burn | Brake | Worst seam |
|---|---|---:|---:|---:|---:|---:|---:|
| Mercury | 1 g | 3.10 d | **2,023 km/s** | 0.5438 d | 1.1312 d / 948.0 km/s | 1.0786 d / 904.0 | **3.7 m / 4.0 × 10⁻⁵ m/s** |
| Mars | 0.3 g | 6.60 d | **1,333 km/s** | 1.1232 d | 2.4115 d / 613.0 | 2.4576 d / 624.7 | **26.4 m / 7.6 × 10⁻⁵ m/s** |
| Jupiter | 0.3 g | 12.80 d | **2,540 km/s** | 2.4953 d | 4.7611 d / 1,210.2 | 4.8021 d / 1,220.7 | **0.7 m / 7.8 × 10⁻⁷ m/s** |

Each is seven arcs (break → departure burn → conic coast → brake → conic capture)
and its total is exactly the request. The ends are unchanged: Mercury
3.044696 d / 2,433 km/s and the 3.216099 d / 1,766 km/s floor; Mars 6.495014 /
1,592 and 6.730422 / 1,172; Jupiter 12.542796 / 3,109 and 13.072112 / 2,271.

The continuity curve (total → delta-v, km/s):

| Target | Minimum | +60 s | 25% | 50% | 75% | Floor |
|---|---:|---:|---:|---:|---:|---:|
| Mercury 1 g | 2,433 | 2,383 | 2,068 | 1,936 | 1,841 | 1,766 |
| Mars 0.3 g | 1,592 | 1,569 | 1,394 | 1,319 | 1,195 | 1,172 |
| Jupiter 0.3 g | 3,109 | 3,076 | 2,689 | 2,533 | 2,420 | 2,271 |

Delta-v falls monotonically as the coast takes more of the crossing. The branch
switch is not free: the last short-coast probe spends **75 km/s** more than the
Lambert floor at Mercury and **149** at Jupiter, because the short coast is one
conic carrying the whole crossing while the Lambert path chooses the transfer.
The "no hole" sweep builds the minimum, +1 s, +60 s, the quartiles, floor − 1 s,
the floor, and floor + 0.05 d for all three cases, with each plan's total the
request (within the moving-target loop's 2 s at the minimum itself).

Below the minimum the old refusal is unchanged: Mercury 3.0 d needs 44.2 m/s²,
Mars 6.4 d needs 531.1, both above the 10.0 m/s² drive. The slow end still
refuses on the Lambert geometry (Mars 305 d, 179.4°).

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
- `FlightSession.Begin` now solves its base plan with `PlanFastest`, so beginning
  at a burn-only minimum flies the no-coast plan the preview drew. A chosen
  duration above that base still re-times with `PlanAtDuration`.

### The step-4 measurement

- **Mercury 1 g.** Plot at the epoch puts the minimum at **3.044696 d**; Begin
  flies that plan, not the 3.21610 d coast floor. It is six arcs — break →
  departure burn → brake → capture — with **no Cruise coast**, and the autopilot
  reaches the arrival state **0.1 m and 0.0001 m/s** (11,228 substeps, arrival
  2026-11-18 01:04:21 UTC).
- **Mars 0.3 g re-baseline.** Begin now flies **6.49501 d / 1,592 km/s /
  172.6 t** (14.7% of departure mass), six arcs with no cruise coast, instead of
  the **6.73042 d / 1,172 km/s / 124.3 t** coast floor. The arc breakdown from
  `SceneFlightTests`: break 13.39 h / 92 km/s, departure burn 70.09 h / 742,
  brake 71.20 h / 754, capture chain 1.20 h / 3.6.
- **Uranus 0.3 g.** `PlanFastest` still returns the coast floor:
  **33.60438 d** in this test environment. Begin flies that floor, with its
  cruise coast.
- **Flight acceptance.** `SceneFlightTests` and the bootstrap map flight arrive
  at **2026-11-21 11:52:49 UTC** after **6.49501 d**, 6,523 frames and 108.7 s
  real at 10,000×, peak **17** substeps in a frame, **0.1 m / 0.0001 m/s**, and
  a 3,898 km parking orbit at **e = 2.4 × 10⁻⁹**. The profile bootstrap flight
  reaches the same state at e = 1.49 × 10⁻⁸.
- **At-minimum drift.** The real-HUD PlayMode test
  (`PressingBeginInTheRealHudFliesAndCancelClearsTheRoute`) caught Begin
  retiming to the preview's old minimum after the clock had moved; that number
  was a few seconds into the gap and the flight was rejected. Begin now skips
  the retime when the preview sat at its minimum and flies the fresh minimum;
  `BeginningAtTheMinimumAfterTheClockMovesFliesTheReplannedMinimum` pins the
  EditMode side.
- **Tests retimed above the floor.** The tests that exist to show a cruise coast
  or the hybrid capture — the phase table, the conic/hybrid row tests, the
  finite-burn arrival, and the arrival-style-without-a-preview test — now plot,
  re-time to 2× the minimum, then Begin, so they keep their intent. No test was
  deleted, and the coast-floor Begin test is new.
- **Fuel.** `TheFuelReadoutFollowsTheFlyingPlanAfterBegin` pins the panel's
  `Fuel` row to the flying plan: **173 t / 14.7%** for the burn-only minimum
  against the coast floor's 124.3 t.

Step 5 closes the gap: a duration between the burn-only minimum and the coast
floor now builds as the short coast, at the measured delta-v above, so Begin's
retime path and the slider no longer meet a hole.

### The step-6 measurement (epoch 2026-11-15, Gateway, conic capture)

`TheFastPlansSpendMorePropellantNotLess` prints the table from
`TrajectoryPlan.PropellantKg` / `PropellantFraction` (1,000 t dry, 10,000 km/s
exhaust velocity) and takes about **85 s**. The headline rows:

| Plan | Trip (d) | Delta-v (km/s) | Propellant (t) | Share of departure mass |
|---|---:|---:|---:|---:|
| Mercury 1 g, burn-only minimum | 3.04470 | 2,432.8 | 275.4 | 21.60% |
| Mercury 1 g, short coast 3.10 d | 3.10000 | 2,023.4 | 224.3 | 18.32% |
| Mercury 1 g, coast floor | 3.21610 | 1,765.5 | 193.1 | 16.18% |
| Mars 0.3 g, burn-only minimum | 6.49501 | 1,591.9 | 172.6 | 14.72% |
| Mars 0.3 g, short coast 6.60 d | 6.60000 | 1,333.1 | 142.6 | 12.48% |
| Mars 0.3 g, coast floor | 6.73042 | 1,171.6 | 124.3 | 11.06% |
| Jupiter 0.3 g, burn-only minimum | 12.54280 | 3,109.0 | 364.6 | 26.72% |
| Jupiter 0.3 g, short coast 12.80 d | 12.80000 | 2,540.1 | 289.2 | 22.43% |
| Jupiter 0.3 g, coast floor | 13.07211 | 2,271.0 | 255.0 | 20.32% |

- **The answer.** The fast plans use **more** propellant, not less: Mercury
  1 g buys 4 h 7 min for 667 km/s and **82.3 t**; Mars 0.3 g buys 5 h 39 min for
  420 km/s and **48.3 t**; Jupiter 0.3 g buys 12 h 42 min for 838 km/s and
  **109.6 t**. The short coast buys it back at about 20 t/h at Mercury and
  8.6 t/h at Mars and Jupiter.
- **Pin at a fixed time is a cost.** Mercury 8 d 57.3 → 63.5 t (+6.2 t); Mars
  0.3 g pinned 30/90/200 d 23.5 / 12.8 / 10.0 t against the 90-day default
  build's 4.4 t.
- **Pin at a floor is a time/fuel trade, not like-for-like.** Mars 0.3 g
  121.2 against 124.3 t for 3 h 12 min longer (6.86413 against 6.73042 d);
  Jupiter 224.8 against 255.0 t for 15 h 23 min (13.71332 against 13.07211);
  Uranus 0.3 g 198.2 against 258.7 t for 7.27 d (40.87407 against 33.60438);
  Uranus 1 g **247.1 against 230.0 t** although 28 h 4 min shorter (32.48399
  against 33.65372); Mercury is the same certified point (193.0 against 193.1 t
  at 3.21686 against 3.21610 d).
- **The branch switch is a step.** The last short-coast probe spends **75.5
  km/s** more than the Lambert floor at Mercury, **148.6** at Jupiter, and
  **23.7** at Mars. The test asserts these ranges, the family ordering
  (burn-only > short coast > floor in both delta-v and propellant), the pinned
  fractions, and the 3.8–4.4 h / >70 t Mercury answer.

## Verification

From this worktree only:

```bash
tools/check.sh --fast          # docs, catalogue, generators, tooling
tools/check.sh --tests flight  # the planner/flight EditMode category
tools/check.sh                 # full EditMode + PlayMode before the step's commit
```

Last results (this commit):

- `tools/check.sh --fast` — **PASSED** in 6 s (OKF bundle 44 concepts, catalogue
  137 bodies, both generators byte-identical, bright-star catalogue, tooling
  tests).
- `tools/check.sh --tests flight` — **PASSED** in 1,011 s: 252 flight EditMode
  tests, 0 failed, 0 skipped. The new test is
  `TheFastPlansSpendMorePropellantNotLess`, about **85 s** of that run; it
  prints the fuel table and asserts the family ordering, the pinned fractions,
  the pin's floor trade, the 75.5 / 23.7 / 148.6 km/s branch steps, and the
  Mercury 4 h 7 min / 82.3 t answer. No existing test changed.
- `tools/check.sh` — **PASSED** in 1,274 s: EditMode 560 tests and PlayMode 6
  tests, 0 failed, 0 skipped. The fixed-duration anchor test is in that run, so
  Mercury 8 d 557.0380, Mars 90 d 44.3543, Uranus 400 d 182.3124 and the other
  three still pass with `Plan()` untouched, and the real-HUD PlayMode Begin test
  still passes with the at-minimum rule.

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
- **The gap is a buildable family now.** `PlanAtDuration` retries a coast
  refusal as the short coast only when the refusal is a squeeze (the departure
  burn cannot be placed, the chain does not close, or the break is over-thrust)
  or the plan is over-thrust. A Lambert geometry refusal keeps its reason — that
  is the slow end. Do not make the fallback unconditional: Mars 305/400 d must
  still refuse with "no prograde short-way ... 180 deg", and the tests assert it.
- **The short coast never touches `Plan()`.** Its floor search and the fixed
  anchors still call `Build` directly; the fallback lives in `PlanAtDuration`.
  Do not move it into `Build` or into `BuildBurnCoastBurn`: that would let the
  floor search walk below its certified floor and move every documented anchor.
- **At the minimum the branch returns the burn-only plan.** A rebuilt short coast
  at exactly the advertised minimum can sit a hair outside the chain gate
  (Mercury measured 1,359.6 m / 0.8 m/s) because the moving-target loop's own 2 s
  date tolerance moves the chain. The branch returns the plan `PlanFastest`
  advertises instead; the no-hole test probes the minimum and expects that plan.
- **The short coast costs about 4–5 s per gap retime** on top of the failed
  coast attempt and the burn-only seed. It runs on the background retime worker;
  do not put it on the main thread. Above the coast floor the coast path builds,
  so the retime there does not pay for it.
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
- **Begin now uses `PlanFastest`**, and it still retimes with
  `PlanAtDuration` when the preview asked for a longer trip. A preview that sat
  at its minimum flies the fresh minimum, not its recorded number: the clock can
  move between plot and Begin, and the old number can be a few seconds into the
  zero-coast gap. Removing that at-minimum check broke
  `PressingBeginInTheRealHudFliesAndCancelClearsTheRoute`. Do not switch the base
  back to `Plan()`: that reintroduces the preview/flight mismatch at every
  burn-only minimum. The flight acceptance anchors that began at the old floor
  are re-baselined to the burn-only minimum; do not revert them. `Plan()` and
  `PlanAtDuration` stay unchanged.
- `PinBreakToCommandAcceleration` is still off by default. `PlanBurnOnly` pins
  its own break regardless of the property; keep that independent.
- The step-2 break cap is deliberately generous and uses the **commanded**
  acceleration, not `MaxAccelerationMps2`. The trace reports `BINDING`; the
  Mercury 1 g case is not binding.
- The seam's fixed-date build's largest residual is the brake's forward rebuild
  against the chain entry (102 m / 0.209 m/s). The moving-target rebuild closes
  tighter (2.2 m / 9.9 × 10⁻⁶ m/s). Do not re-tighten the solver's tolerance
  without re-measuring.
- Keep the documented floors and delta-v anchors. `Plan()` is untouched; only
  the preview minimum, the trip-time label, the Mars/Mercury/Jupiter window
  ends, and the gap's `PlanAtDuration` branch moved.
- **The fuel diagnostic test costs about 85 s.** It builds ten floors and six
  gap plans through the full-field planner; do not copy it into a per-case loop
  or a hot path. Its floor-trade assertions are the **editor's** certified
  points (Uranus 1 g 32.48399 against 33.65372 d); the .NET harness selects
  different giant floors, so re-measure before porting it there.
- Update `Docs/log.md` under the current `YYYY-MM-DD` heading and this handoff in
  the same commit as the work. Run `tools/validate_okf.py` (or `check.sh --fast`)
  after the doc edits.
- Do not merge into `main` and do not push. A finished step is a commit on
  `work/burn-only-profile`.

## Step 5 notes (done)

- **The pinned coast path was tried first and does not build the gap.** At the
  three midpoints the break needs 68.3 / 10.0 / 664.6 m/s² against a 9.7 m/s²
  drive (pin off), and even pin-on it cannot place the burn at Mars or gets a
  degenerate break at Jupiter. The coast path allocates a real coast, so the
  break is squeezed. The conic seam was the right change.
- **The seam lives in `BuildShortCoast` / `TrySolveShortCoastSeam` /
  `EvaluateShortCoastSeam`**, seeded from `BuildBurnOnly`'s solved ray, burn, and
  brake directions through the new out-parameter overloads of `BuildBurnOnly`
  and `PlanBurnOnlyMovingTarget`. It reuses `BurnOnlyNorm`, the tangents, the
  damped Newton, and the gate; do not fork a second solver.
- **The brake's duration is derived**, not an unknown: it is
  `T − break − burn − c − capture`. The break is solved with the generous
  burn-only cap, so the coast and brake absorb the requested total. A trial that
  leaves the brake less than a second is refused.
- **`c` below one second is stepped, not conic.** `ConicArc` needs a positive
  duration and its end anomaly can wrap to a full revolution at a tiny step, so
  the sub-arc coast uses the two-body series `r + vc + ½ac²`, `v + ac`, which is
  exact to well under a millimetre over a second and makes the branch smooth
  through `c = 0`. The reported plan omits a coast arc shorter than
  `MinimumArcSeconds`.
- **The seam's own conditioning is fine**: the Newton takes 11–12 passes from
  the seed, the worst residual is tens of metres, and the reported plans close
  inside the gate at all probes. The 1 km / 1 m/s gate is unchanged.
## Step 6 notes (done)

All four items are measured; the results are in [the step-6
measurement](#the-step-6-measurement-epoch-2026-11-15-gateway-conic-capture).
The notes below are the brief the step was run against.

- **The numbers.** `TrajectoryPlan.PropellantKg` is
  `DryMassKg * (exp(Δv / 10,000,000) − 1)` and `PropellantFraction` is the same
  without the dry mass. The default dry mass is 1,000 t. Report both Δv and the
  propellant, in kg/t and as a fraction of dry mass and of departure mass.
- **Pin on/off at fixed trip times.** Use the step-1 cases: Mercury 1 g 8 d
  (90.2 → 167.2 km/s of break, 557.0 → 615 total), Mars 0.3 g 30/90/200 d
  (232.2 / 127.7 / 99.4 total pinned against the 90-day default's 44.4), and the
  fixed-duration anchors where relevant. State that the pin spends more at long
  trips and that the 0.3 g floor totals can go the other way.
- **Pin on/off at each floor.** Mercury 1 g (1,764 vs 1,766), Mars 0.3 g (1,144
  vs 1,172, 6.864 vs 6.730 d), Jupiter 0.3 g (2,028 vs 2,271, 13.713 vs 13.07),
  Uranus 1 g (2,208 vs 2,070, 32.484 vs 33.654), Uranus 0.3 g (1,808 vs 2,301,
  40.874 vs 33.604). Do not present a floor comparison as a like-for-like time
  comparison; quote both the time and the fuel.
- **The new family.** Mercury 1 g: burn-only 3.044696 d / 2,433 km/s, short coast
  3.10 d / 2,023, coast floor 3.21610 d / 1,766. Mars 0.3 g: 6.495014 / 1,592,
  6.60 / 1,333, 6.730422 / 1,172. Jupiter 0.3 g: 12.542796 / 3,109, 12.80 /
  2,540, 13.072112 / 2,271. The curve is monotonic in delta-v; the branch switch
  at the old floor still costs 75 km/s at Mercury and 149 at Jupiter, so quote
  the step rather than claiming a perfectly smooth curve.
- **Answer the question directly.** The fast plans use **more** propellant, not
  less: the burn-only minimum buys 4 h 7 min at Mercury for ~667 km/s and about
  48 t more propellant at Mars 0.3 g; the pin is a time/fuel trade, cheaper at
  some 0.3 g floors and dearer at long trips and Uranus 1 g. The short coast lets
  the player buy fuel back as they add time.
- **Test.** Add a diagnostic `[Test]` that prints the table and asserts the
  monotonicity and a few pinned fractions; do not change the planner. Update
  `Docs/simulation/burn-coast-burn.md` (a fuel section beside the pin and the
  short coast), `Docs/log.md`, and this handoff. Run `--fast`, `--tests flight`,
  and the full `check.sh` before the commit.
