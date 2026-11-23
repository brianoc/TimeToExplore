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
2. **Burn-only seam solve — DONE (this commit).** `TripPlanner.PlanBurnOnly`
   builds the pinned break, the capture chain at the arrival handover floor, and
   a two-burn seam with **no coast**: the departure burn forward from where the
   break ends, the brake backward from the chain entry, six unknowns against the
   six seam components. The total time is an **output**. Measured on Mercury at
   1 g: the seam closes, the crossing is **shorter than the floor** and costs
   more delta-v. See [the burn-only
   crossing](Docs/simulation/burn-coast-burn.md#the-burn-only-crossing-experimental).
   It is not wired into `Plan()`, the floor search, or the HUD.
3. **Short coast and the handover — NEXT.** Lower `MinimumCoastSeconds`, let the
   floor search walk across the burn-only/short-coast boundary, verify the two
   branches agree on delta-v, seams, and feasibility, and re-sweep the window.
   The arrival date becomes an unknown (the moving-target loop) so a short coast
   can absorb a chosen trip time.

Do not start a step until the previous step's commit is in and the tree is
clean. Stop after two failures on the same step and leave the failure and last
command here.

## Current state

Steps 1 and 2 are committed on `work/burn-only-profile`. The default path is
unchanged: the pin property is off, `Plan()` and `PlanAtDuration` do not call any
of the new code, and every documented anchor still passes — the anchor test pins
Mercury 8 d 557.0380, Mars 90 d 44.3543, Uranus 400 d 182.3124, and the other
three fixed-duration totals.

### The step-2 measurement (epoch 2026-11-15, Gateway → Mercury at 1 g)

Read from the new test's output in `.cache/test-project/results-editmode.xml`.
The arrival argument is the date of the certified 1 g floor (3.21610 d); the
plan's own arrival comes out of the seam.

| Arc | Duration | Peak | Δv |
|---|---:|---:|---:|
| Break (quintic) | 0.30986 d | 9.7000 m/s² | 167.9 km/s |
| Departure burn | 1.37544 d | 9.7000 | 1,152.7 |
| Brake | 1.32285 d | 9.7000 | 1,108.7 |
| Capture chain (fall + transfer + parking) | 0.03655 d | — | 3.56 (impulse) |
| **Total** | **3.04470 d** | 9.70 | **2,432.9** |

- Seam residual from the solve: **151.4 m / 1.65 × 10⁻³ m/s**. Built plan's
  worst seam (brake rebuilt forward against the chain entry): **102.2 m /
  0.209 m/s**. Both inside the 1 km / 1 m/s gate.
- Six arcs: break → departure burn → brake → capture chain. **No Cruise coast**,
  and the dates are continuous at every seam.
- The brake starts **63,444,313 km** from Mercury, against a **114,658 km**
  ceiling (553 ceilings), so no planetocentric approach leg is needed.
- Break cap: generous by design (twice the kinematic climb to the clearance
  sphere at the commanded acceleration) — **0.5160 d**, break uses 0.30986 d,
  **not binding**. The plan prints `BINDING` in its trace if a future case hits
  it.
- Against the certified floor: **0.17140 d (4 h 7 min) shorter** (3.04470 vs
  3.21610 d) and **667 km/s (38%) more expensive** (2,432.9 vs 1,766 km/s).
- The solver settles in six damped-Newton passes, 0.8 s in the editor. The
  second seed (the straight line between the two ends, brake opposed) is only
  used if the first attempt stalls.

### One deviation from the step-2 instructions — read before reviewing

The step asked for a fixed arrival date **and** for the total time to be an
output. Taken literally, those conflict: with the chain's entry date fixed, the
burns must also fill the fixed gap, which adds a seventh condition to the six
seam components. This implementation keeps both properties the honest way:

- the supplied date fixes the **capture chain's geometry** — its plane, entry
  radius, and fall — and seeds the handover;
- the seam uses the six unknowns the step named, and places the chain's handover
  where the solved brake actually ends. The plan is therefore continuous in
  **date as well as state**, and the total is the sum of the solved durations
  plus the break and capture.

There is **no moving-target loop**: the chain is built once, and nothing is
re-solved after the seam. The alternative (a fixed entry date, states compared
at two different dates) admits a geometric seam that is not a flyable plan; it
was not built. Caretaker: accept this reading, or ask for the literal variant as
a diagnostic.

### The pin stays opt-in (step 1's decision, still in force)

At 0.3 g the pin spends about 92 km/s of break delta-v at long trips against 8.8
on the default 90-day build. Step 2 uses it only inside the burn-only crossing,
where the trip is at the fast end and the break must not be the binding
constraint. The global default decision waits for step 3's short-coast
measurement. The fixed-duration evidence is in
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md#the-level-pin-is-opt-in)
and the eight-case sweep is in the 2026-11-22 log entry.

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
- `tools/check.sh --tests flight` — **PASSED** in 439 s: EditMode flight
  category, 234 tests, 0 failed, 0 skipped. The new test
  `ABurnOnlyCrossingClosesWithoutACoast` passes in 0.8 s.
- `tools/check.sh` — **PASSED** in 637 s on the committed tree: EditMode 542
  tests and PlayMode 6 tests, 0 failed, 0 skipped. The fixed-duration anchor
  test is in that run, so Mercury 8 d 557.0380, Mars 90 d 44.3543, Uranus 400 d
  182.3124 and the other three still pass with the default path untouched.

## Traps

- **Never run a command in `/Users/brian.oconnor/Personal/TimeToExplore`.** That
  is the main checkout with the open editor; it also has a modified
  `ProjectSettings.asset` that is not ours. Every command runs in this worktree.
  The main editor's Unity process stays open while the batch suites run; that is
  expected.
- `tools/check.sh` derives its root from its own path, so it is safe here; it
  writes `.cache/test-project` under this worktree.
- The `.venv` is a symlink. Do not `pip install`; it would change the shared venv.
- **The burn-only crossing's date is an output.** Do not pass its total through
  `PlanAtDuration`, and do not expect the returned arrival date to equal the
  supplied one: the supplied date chooses the capture chain's geometry, and the
  handover lands where the solved brake ends. The measured difference on the
  Mercury case is about 0.2 d.
- The step-2 break cap is deliberately generous and uses the **commanded**
  acceleration, not `MaxAccelerationMps2`: at 0.3 g the climb is slower and a cap
  built from the drive limit would bind. The trace reports `BINDING`; the
  Mercury 1 g case is not binding.
- The seam's largest built residual is the brake's forward rebuild against the
  chain entry (102 m / 0.209 m/s), because the brake is placed backward and
  reported forward. It is inside the gate; do not re-tighten the solver's
  tolerance without re-measuring it.
- `PinBreakToCommandAcceleration` is still off by default. `PlanBurnOnly` pins
  its own break regardless of the property; keep that independent.
- Keep the existing default behavior. The documented floors and delta-v anchors
  in [Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md) and [planning
  performance](Docs/technical/planning-performance.md) must not move. The
  fixed-duration anchor test still pins six of them.
- Update `Docs/log.md` under the current `YYYY-MM-DD` heading and this handoff in
  the same commit as the work. Run `tools/validate_okf.py` (or `check.sh --fast`)
  after the doc edits.
- Do not merge into `main` and do not push. A finished step is a commit on
  `work/burn-only-profile`.

## Step 3 notes

- The next unknown is the **arrival date** (or, equivalently, the total time):
  adding it to the six makes a seven-unknown, seven-equation system, and the
  chain's handover then moves with the answer. That is the moving-target loop the
  step-2 scope deferred.
- A short coast between the burns is the second unknown regime: the same seam
  with a non-negative coast time, and the floor search walking across the
  boundary. Check that both branches agree on delta-v and seams at the crossing
  point rather than assuming they do.
- The burn-only crossing is the fast extreme, not a candidate default: it is
  shorter and much more expensive than the coasting floor. The decision to make
  the pin global still waits on the short-coast numbers.
