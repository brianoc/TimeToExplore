# Handoff

**Plan: a burn-only crossing, then a short-coast regime.** Work happens in this
worktree on `work/burn-only-profile`; `main` is untouched. The reasoning is in
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md), [full-field burns
and the powered-leg floor](Docs/simulation/powered-leg-floor.md), and
[ADR-015](Docs/decisions/adr-015-fixed-thrust-burns.md). This handoff records
only what is done, what is next, and the traps.

## Steps

1. **Pin the break — DONE (commit below).** The break's duration is solved so the
   quintic's peak commanded thrust equals `FixedBurnAccelerationMps2` (9.70 m/s²
   at 1 g, 2.942 at 0.3 g), behind the opt-in
   `TripPlanner.PinBreakToCommandAcceleration` (default **false**). The search is
   on the fast central-body peak with the 5% screen; the reported plan is the
   full-field measurement. Tests, concept update, log entry, and this handoff are
   in the same commit. See [the level pin](Docs/simulation/burn-coast-burn.md#the-level-pin-is-opt-in).
2. **Burn-only crossing.** Add the zero-coast seam: integrate the departure burn
   forward from the pinned break end, the brake backward from the capture entry,
   and close them at the flip with no Lambert coast. Keep the existing long-coast
   path; select the burn-only crossing by a planner mode. Tests for a burn-only
   plan; docs and log.
3. **Short coast and the handover.** Lower `MinimumCoastSeconds`, let the floor
   search walk across the burn-only/short-coast boundary, and verify the two
   branches agree on delta-v, seams, and feasibility. Re-sweep the window.

Do not start a step until the previous step's commit is in and the tree is
clean. Stop after two failures on the same step and leave the failure and last
command here.

## Current state

Step 1 is committed. The default path is unchanged: the property is off, and the
existing anchors (`Mercury 8 d 557.0380`, `Mars 90 d 44.3543`, `Uranus 400 d
182.3124`, the other three fixed-duration anchors) still pass. Step 2 has not
started.

### The measured pinned break (epoch 2026-11-15, from Gateway, conic capture)

Printed by `ThePinnedBreakSweepIsRecorded` from `.cache/test-project/results-editmode.xml`.
Every row is a **certified full-field** plan found by `Plan()` with the pin on,
so each floor is the pinned floor, not the documented default floor. Window hi is
the nav computer's probed slow end; all eight cases accepted the 3× cap.

| target | level | floor d | window d | break d | break peak | break Δv | burn d | burn Δv | coast d | approach d | brake d | brake Δv | capture Δv | total Δv | worst seam |
|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| mercury | 1.0 g | 3.217 | 3.217–9.651 | 0.3098 | 9.7000 | 167.877 | 0.9765 | 818.39 | 0.970 | 0 | 1330.7 min | 774.48 | 3.56 | 1764.3 | 147.5 m / 1.57E-02 m/s |
| mercury | 0.3 g | 5.905 | 5.905–17.716 | 0.5618 | 2.9366 | 91.858 | 1.8597 | 472.72 | 1.757 | 0 | 2427.9 min | 428.58 | 3.56 | 996.7 | 641.5 m / 4.78E-02 m/s |
| mars | 1.0 g | 3.856 | 3.856–11.567 | 0.3087 | 9.7000 | 167.806 | 1.1076 | 928.27 | 1.272 | 0 | 1619.5 min | 942.55 | 3.53 | 2042.2 | 131.4 m / 8.66E-02 m/s |
| mars | 0.3 g | 6.864 | 6.864–20.592 | 0.5582 | 2.9407 | 91.917 | 2.0404 | 518.65 | 2.129 | 0 | 3004.4 min | 530.33 | 3.53 | 1144.4 | 298.2 m / 5.18E-02 m/s |
| jupiter | 1.0 g | 9.257 | 9.257–27.771 | 0.3087 | 9.7000 | 167.770 | 1.4353 | 1202.92 | 5.945 | 0 | 2083.5 min | 1212.62 | 17.40 | 2600.7 | 2.0 m / 6.19E-05 m/s |
| jupiter | 0.3 g | 13.713 | 13.713–41.140 | 0.5576 | 2.9408 | 91.818 | 3.7542 | 954.26 | 5.423 | 0 | 5464.8 min | 964.64 | 17.40 | 2028.1 | 172.6 m / 2.85E-03 m/s |
| uranus | 1.0 g | 32.484 | 32.484–97.452 | 0.2566 | 9.7000 | 139.666 | 1.2268 | 1028.16 | 29.675 | 0 | 1776.3 min | 1033.81 | 6.69 | 2208.3 | 3.9 m / 5.22E-04 m/s |
| uranus | 0.3 g | 40.874 | 40.874–122.622 | 0.4614 | 2.9376 | 76.064 | 3.3867 | 860.85 | 33.500 | 0 | 4897.0 min | 864.42 | 6.69 | 1808.0 | 2.0 m / 7.13E-04 m/s |

All delta-v in km/s. No pinned floor plan carries an approach arc; the
pinned breaks are short enough that every brake starts outside the ceiling.

The floors move only under the option, through the near-floor basin: Mercury
1 g keeps 3.217 d, Mars 0.3 g 6.864 against 6.730, Jupiter 0.3 g 13.713 against
13.072, Uranus 0.3 g 40.874 against 33.604, Uranus 1 g 32.484 against 33.654.
The totals at the pins are 1764.3 / 996.7 / 2042.2 / 1144.4 / 2600.7 / 2028.1 /
2208.3 / 1808.0 km/s; against the documented default floor totals that is
Mercury 1766 and Mars 1172 and Jupiter 2271 and Uranus 1 g 2070 and Uranus
0.3 g 2301. Three improve, Uranus 1 g is worse, and the floors are at different
durations — this is a trade, not a strict win.

### Fixed-duration measurements (needed for the default-on decision)

| Plan | Pin | Break d | Break peak | Break Δv | Total Δv |
|---|---|---:|---:|---:|---:|
| Mercury 1 g, 8 d | off | 0.569 | 2.85 | 90.2 | 557.0 |
| Mercury 1 g, 8 d | **on** | 0.3086 | 9.7000 | 167.2 | 615 |
| Mars 0.3 g, 30 d | **on** | 0.563231 | 2.9400 | 92.672 | 232.24 |
| Mars 0.3 g, 90 d | **on** | 0.566942 | 2.9392 | 93.193 | 127.66 |
| Mars 0.3 g, 200 d | **on** | 0.546069 | 2.9419 | 90.011 | 99.38 |
| Mars 0.3 g, 90 d | off | 4.844 | 0.033 | 8.8 | 44.4 |

Kinds are m/s², km/s. The pinned peak sits within 0.2% of the target at 0.3 g
and exactly on it at 1 g (the 5% screen puts the full field in charge there).
The pinned break's delta-v is set by the level and the ray, so it barely moves
with the trip; the trip's saving shows in the coast and the total.

### One deviation from the step instructions — read this before reviewing

The step asked the test to assert the three Mars plans' own Departure arc
`DurationSeconds` are equal within **1e-6** relative. That cannot hold: the
three trips need different escape rays, and the ray is one of the solver's
inputs. Measured, the 90-day ray is **10.9591°** from the 30-day ray and the
200-day ray **27.6717°**, and the reported durations move 0.659% and 3.047% with
them. The test therefore asserts the invariant the design actually states — the
same departure state, level, and **ray** give the same duration — through the
new diagnostic `TripPlanner.MeasurePinnedBreakDurationSeconds`: with the 30-day
ray held fixed and only the cap and seed varying, the three solves agree to
**8.4 × 10⁻⁸** relative (measured 0.563231464 / 0.563231511 / 0.563231460 d).
The reported spread is separately bounded at 5% and printed. No tolerance was
widened to make a failing number pass; the 1e-6 assertion is kept on the fixed-ray
solve. Caretaker: accept this reading, or ask for a solver whose ray is held
across trips (which would change the design sentence in the step).

## Verification

From this worktree only:

```bash
tools/check.sh --fast          # docs, catalogue, generators, tooling
tools/check.sh --tests flight  # the planner/flight EditMode category
tools/check.sh                 # full EditMode suite before the step's commit
```

Last results (this commit):

- `tools/check.sh --fast` — **PASSED** in 6 s (OKF bundle 44 concepts, catalogue
  137 bodies, both generators byte-identical, bright-star catalogue, tooling
  tests).
- `tools/check.sh --tests flight` — **PASSED** in 444 s: EditMode flight
  category, 0 failed, 0 skipped.
- `tools/check.sh` — **PASSED** in 653 s: EditMode 541 tests and PlayMode 6
  tests, 0 failed, 0 skipped.

The first `--tests flight` attempt failed only on the plan-level 1e-6 assertion
described above; after the fixed-ray test landed, the same category passes. The
failure is not a solver failure and no number was invented.

## Traps

- **Never run a command in `/Users/brian.oconnor/Personal/TimeToExplore`.** That
  is the main checkout with the open editor; it also has a modified
  `ProjectSettings.asset` that is not ours. Every command runs in this worktree.
  The main editor's Unity process stays open while the batch suites run; that is
  expected.
- `tools/check.sh` derives its root from its own path, so it is safe here; it
  writes `.cache/test-project` under this worktree.
- The `.venv` is a symlink. Do not `pip install`; it would change the shared venv.
- The pin targets the **commanded level**, not the drive limit. At 1 g the two
  coincide (9.7 m/s² after the 0.97 margin); at 0.3 g the target is 2.942 m/s².
- A short, hard break is not free: its delta-v rises as its peak rises (Mercury
  1 g: 90.2 → 167.2 km/s). Do not adopt the pin on the floor numbers alone.
- The fast search measures the break against the departure body alone. Inside
  `TripPlanner.BreakScreenMargin` (5%) of the drive limit, it certifies with the
  full field. At 1 g the target is that limit, so every pinned 1 g break is
  full-field-certified and reports 9.7000; at 0.3 g the reported peak carries the
  Sun's omitted term (measured 2.9366–2.9419).
- The trip's own escape ray rotates with the arrival date (10.96° at Mars 90 d,
  27.67° at 200 d). That is why the plan-level durations differ; the solver's
  invariance is a **fixed-ray** statement, tested through
  `MeasurePinnedBreakDurationSeconds`.
- Keep the existing default behavior when the opt-in property is off. The
  documented floors and delta-v anchors in
  [Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md) and
  [planning performance](Docs/technical/planning-performance.md) must not move.
  The fixed-duration anchor test still pins six of them.
- Update `Docs/log.md` under the current `YYYY-MM-DD` heading and this handoff in
  the same commit as the work. Run `tools/validate_okf.py` (or `check.sh --fast`)
  after the doc edits.
- Do not merge into `main` and do not push. A finished step is a commit on
  `work/burn-only-profile`.

## Decision left to the caretaker

**Whether `PinBreakToCommandAcceleration` becomes the default.** On: the break
commands the same level as the two fixed burns, the duration stops scaling with
the trip, and the 0.3 g total delta-v at the floors improves for Mercury, Mars,
Jupiter, and Uranus 0.3 g. Off: the break stays gentle and cheap at long trip
times (8.8 km/s against 93.2 on the 90-day Mars build), Uranus 1 g's floor and
total are better under the old rule, and every documented anchor stays put. The
tables above are the measurement; the code default remains **off** until the
caretaker decides.
