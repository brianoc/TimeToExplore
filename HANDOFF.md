# Handoff

**Active plan, started 2026-11-19: full-field burns to the powered-leg floor.**
Steps 1–5 are done; step 6 is next. Step 5 landed in the `work/steering-law`
worktree and merges to `main` when green, per working-agreement rule 1. The
design is recorded in
[Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md);
`FiniteBurnArc` integrates the summed field, the handover band bounds the shaped
ends, every fixed burn is gated on body clearance, the burn–coast–burn's capture
chain enters at the **arrival handover floor**, and both fixed burns keep the
**fixed inertial direction** because the steering measurement found nothing for a
tracking or segmented law to win. Screenshots are waived for this plan.

## Why

A Uranus burn–coast–burn trip read as almost all coast: the conic capture entered
the target's sphere of influence at the planted `TripPlanner.EntrySpeedMps =
5000`, and Uranus's 51.8 million km SOI made the drive-off fall about **104 days**.
The slider only re-times the heliocentric leg, so the fixed capture coast
dominated every duration. It is a *speed* problem, not an arc-boundary problem.

## Design agreed (recorded in step 1)

The reasoning and the numbers are in
[Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md);
this list is the caretaker's short form.

1. **Full-field burns, not patched burns.** *Implemented.* Fixed burns integrate
   the summed gravity field; the reference body is a label. One burn arc may
   cross an SOI; it is never split there.
2. **Handover band.** *Implemented.* `HandoverBand.For(body, a_drive)` returns
   the floor `sqrt(μ / (κ a_drive))` at the parking ceiling's `κ = 0.4` clamped
   up to `R + 100 km`, the ceiling `1.02 × SOI`, validity, and the reason when it
   is empty. **Departure = the ceiling** (unchanged plan) and **arrival = the
   floor** (flown in step 4). An interior solved radius is still open: step 5
   settled the steering law, not the radius rule.
3. **Quintic ends stay.** The break and the conic/hybrid capture keep their
   jobs; only the handover radius and the field changed.
4. **The capture chain takes an entry radius.** *Implemented in step 4, radius
   only.* The burn–coast–burn enters at `max(band floor, conic parking radius)`;
   the coast profile and the powered conic arrival keep the sphere. The **entry
   speed is still the per-plan `EntrySpeedMps`** (5 km/s default); the
   brake-produced residual is the next decision and needs the step-4 numbers.
5. **Steering starts simple.** *Implemented and settled.* **Both fixed burns
   keep the fixed inertial direction**, measured at the epoch against a local
   prograde/retrograde law, 1–128 fixed segments, and a pursuit law. The
   departure burn is prograde by construction; the brake's seam is exact with the
   fixed direction and every alternative leaves 3.1–17.6 km/s or more. Numbers
   in [the concept](/simulation/powered-leg-floor.md#steering-starts-simple).

Open questions: the entry-speed rule, the approach model (see Traps), whether an
interior solved radius is worth it, and whether the handover radius should be a
search input or a certified build.

## Steps (one sub-agent per step, one commit each)

1. **Record the design.** **Done.**
2. **Full-field finite burn.** **Done** — `FiniteBurnArc` takes an optional
   `IGravityField`; every fixed burn in a certified plan is full-field.
3. **Handover band and endpoints.** **Done** — `HandoverBand` computes the floor,
   the ceiling, and the empty-band reason; `FixedBurnClearsBody` gates every
   fixed burn on body clearance. New `HandoverBandTests`.
4. **Capture entry at the handover floor.** **Done** — the burn–coast–burn's
   chain is built at `max(floor, conic parking radius)`; `TransferConic`'s radius
   is the **entry radius**; `TrajectoryPlan.CaptureEntryRadiusM` and
   `CaptureInboundSeconds` report it. Measured at the epoch: the Uranus 1 g
   400-day fall goes from 117.13 days to 31 minutes, the total from 242.1 to
   182.3 km/s, and the floor from 150.73 to 34.00 days (0.3 g: 41.57 days). The
   coast's end / brake's start is **366,505 km (0.0071 SOI)** from Uranus on that
   plan and **1,159,005 km (0.0224 SOI)** at 0.3 g; at the fast floor the brake
   spans the sphere instead (28.5 h of 995 km/s from 51.07 million km). That
   measurement is the next decision's input; the concept records it under "The
   binding question".
5. **Steering experiment.** **Done** — the fixed inertial direction stays at
   both ends. Four plans at the epoch (Uranus 400 d at 1 g and 0.3 g, Mars 90 d
   at 0.3 g, Mercury 8 d at 1 g): each burn's held direction was recovered from
   `AccelerationAt − GravityField`, then the same start state, duration and
   acceleration were re-flown at 2,048+ RK4 steps through the full field with the
   plan's direction, local prograde/retrograde, 1–128 segments, and a pursuit
   law. The fixed law reproduces the plan end state to 0.000 km and 0.000 km/s,
   so the harness and the planner integrate the same field. The departure burn is
   the escape ray, 0.00° from the frame's prograde at every sample, and a local
   law reproduces it to 0.003 km/s. The brakes turn 30–78° of tracking and the
   held direction ends 39.7–87.6° off retrograde; the local-retrograde seam is
   **17.6 / 13.3 / 3.1 / 6.0 km/s**, and re-aiming makes it worse: 15.3 at one
   segment, 22.2 at two, 34.0 at eight and 39.0 at 128 on Uranus 1 g, while
   pursuit reaches 6.1 / 11.0 / 0.14 / 0.006. The 16.85% cosine loss at Uranus
   1 g is 13.68 km/s
   of thrust across the track — the turn the brake exists to make, not a
   spendable penalty. The concept records the whole table and the decision.
6. **Slider and window.** The trip-time window's left end moved (Mars
   6.74–20.21 days, Mercury 3.22–9.65); the HUD copy and the window cache hold in
   the tests, and step 6 should re-check the copy and the floor search's
   conservative bracket (see Traps). *Next.*
7. **Performance.** Full-field probes are slower and less smooth; measure
   `Plan()` and one slider retime before and after. Close out the docs and clear
   this file.

The **cost trim done before step 5** (2026-11-20, worktree) removed two
redundant full-field builds: the full-field retry no longer rebuilds the
duration the certification just built, and a synchronous retime that lands on
the floor returns the window's stored plan. The burn-level case went 173 → 121 s
and the Mars floor test 84 → 69 s with the plan unchanged. A brake-sample trim
was measured and rejected: the 4 s step already caps at 2,048 for every brake
over 2.3 h, and the measured brakes are 6–29 h. The remaining cost — twelve pin
passes at 2,048 brake samples — is step 7's to decide. Measurements in
[planning performance](/technical/planning-performance.md#one-burncoastburn-build-measured-2026-11-20).

## Verification

- `tools/check.sh --tests flight` for each Simulation/TripPlanner step.
- `tools/check.sh --tests presentation,smoke` when the window or HUD changes.
- `tools/check.sh --fast` for a docs-only step.
- `.venv/bin/python tools/validate_okf.py` for every bundle edit.
- Last result (2026-11-20, after step 5): `.venv/bin/python
  tools/validate_okf.py` — 41 concepts, 0 warnings, 0 errors; `tools/check.sh
  --fast` — passed. No Unity suite: step 5 changed docs only. (Step 4's code run:
  `tools/check.sh --tests flight` — passed, 249 tests, 0 failed, 606 s;
  `tools/check.sh --tests flight,presentation,smoke` on the final tree — passed,
  389 tests, 0 failed, 1005 s. Step 3's run was 245 tests, 0 failed, 405 s.)

## Traps

- Do not split burns at the SOI. That is the patched option and it puts the
  seam where the models disagree most.
- **Do not replace the brake's fixed direction with local steering.** Flown over
  the solved burn's own start state, duration and acceleration, a local
  prograde/retrograde law leaves **17.6 km/s** of seam at Uranus 1 g, 13.3 at
  0.3 g, 3.1 at Mars and 6.0 at Mercury, and a pursuit law leaves 6.1 / 11.0 /
  0.14 / 0.006; neither closes the chain. Re-aiming every few segments is
  monotonically worse (15.3 at one, 22.2 at two, 29.2 at four, 34.0 at eight,
  39.0 at 128 on Uranus 1 g), because the schedule follows the velocity as it
  turns instead of the chord the seam needs. The fixed direction is exact and its
  spend is `a·T`, so segmenting pays twelve pin passes for a worse seam. The
  reasoning is in [the concept](/simulation/powered-leg-floor.md#steering-starts-simple).
- Do not "fix" the fall by braking sooner; it lengthens the coast. The win is a
  hotter entry and a deeper brake.
- **The approach model is now the open problem.** On a long trip the coast ends
  deep inside the target's sphere — 0.0071 spheres at Uranus 1 g and 400 days,
  where the target's gravity is 0.043 m/s² and a Sun-only conic is a poor model.
  The seams close, so this is not a build failure: it is the model gap the
  next decision (a planetocentric approach leg, a hotter residual entry, or a
  full-field coast) has to settle. Do not "fix" it by widening the seam gate.
- **The entry speed is still planted.** `EntrySpeedMps` is per plan through
  `FlightSession` but its default is 5 km/s and nothing produces a residual from
  the brake yet. A hotter entry shortens the fall but grows the periapsis burn
  (Uranus 1 g: 31 → 9 minutes, 6.37 → 39.10 km/s from 5 to 50 km/s).
- `EntrySpeedMps` is global today; changing its default moves every conic plan.
  Do not tune it without the per-plan rule.
- The floor search is already noisy near the minimum; a full-field probe makes
  it noisier and slower. Certify the chosen build as the current profile does.
  **The estimate got shorter**, because the capture no longer contributes
  months, so the growth loop now runs to twelve 20% steps; five stopped below the
  Uranus 0.3 g floor and reported the trip infeasible. The search can still be
  conservative: Uranus 1 g returns 34.00 days while a direct 33.0-day build
  closes, because the fast bracket's low end stops the full-field bisection.
  Re-probing below a fast bracket is step 7's cost question. **Uranus is now a
  reachable target**, and its floor search grows from the capture-only estimate
  (13.5 d) to 34–42 d, so a first selection there is a dozen full-field builds:
  a few seconds in a .NET harness and plausibly a minute in the editor. Step 6
  should measure it before shipping a Uranus preview.
- **`Measure` samples uniformly in time now**, with a geometric refinement at a
  conic's ends. A conic is linear in true anomaly, so the old anomaly grid put a
  long transfer's whole final approach in one bin and over-reported the Uranus
  coast's third-body delta-v by two orders. Any third-body number on a conic
  must be checked for convergence before it is believed.
- **The existing anchors moved** with the entry radius: Mercury 8 d 557.0 km/s
  (was 569.9), 12.3 d 376.9 (379.9), Mars 90 d 44.4 (45.4); floors 3.22 and
  6.74 days (were 3.46 and 8.03); windows 3.22–9.65 and 6.74–20.21 days. The
  seam tolerances on the 12.3-day trip are 7.5 × 10⁻² m and 3.7 × 10⁻⁴ m/s (were
  10⁻⁴ m and 10⁻⁸ m/s) because the brake's fixed point now ends at the floor.
  Re-measure; do not preserve them blindly.
- **A slow Mars crossing closes now.** The 200-day 0.3 g build that used to need
  an acceleration is 12.46 km/s, and the slow end is the coast's 179.4° geometry
  at 305 days. The acceleration branch is still in the solver; the test now pins
  the measured slow behaviour rather than that branch.
- The floor is clamped, so it is not `1/sqrt(a)` everywhere. Only an unclamped
  floor scales; Mercury and Mars at 1 g sit on the `R + 100 km` clearance and do
  not move until the level falls far enough. At 1 g on Mercury and Mars the
  clamped floor is *under* the conic periapsis, so the entry is the parking
  radius there.
- **Three catalogue moons have an empty band at every level**: Phobos, Deimos,
  and Proteus. Do not "fix" this by moving the ceiling.
- **Full-field burns are the suite's cost now.** A full-field step is ~50× a
  two-body step: a 90-day Mars build is 0.47 s in a .NET harness and the
  editor's burn–coast–burn class is a ~3-minute silent stretch of the Unity log.
  Unity writes no per-test progress, so a full run is one silent stretch of
  about fifteen minutes: the guard's stall window is now **twenty minutes**, and
  an eight-minute window kills a healthy suite. The flight category is about
  ten. Do not read either as a hang. A build is ~1.2 s per pin pass and the certified
  fixed point runs twelve, so a floor search is tens of seconds. Step 7 owns the
  remaining remedy (a cheaper prepare rule, a coarser brake step, or a looser
  pin tolerance); do not trim the brake samples expecting a win, because the
  4 s target step already caps at 2,048 for these brakes.
- A **body-relative position** on a full-field arc carries the catalogue's
  Julian-date quantisation (0.2–0.9 m at 1.5 AU). Compare inertial states in
  tests, or use a metre-scale tolerance.
- The hybrid capture **solver** stays on the two-body burn on purpose. Its
  reported arc and trim are full-field. Do not thread the field through
  `HybridCapture` without dealing with its search cost.
- `TryCorrectCoastDeparture` targets a millimetre, not 10⁻⁴ m.
