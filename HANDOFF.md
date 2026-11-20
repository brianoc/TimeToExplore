# Handoff

**Active plan, started 2026-11-19: full-field burns to the powered-leg floor.**
Steps 1–4 are done. **This plan stays on `main` as the transition to working
agreement rule 1** (work in a worktree): its steps were committed here before
the rule existed, and the next plan starts in a worktree, not this one. The design is recorded in
[Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md);
`FiniteBurnArc` integrates the summed field, the handover band bounds the shaped
ends, every fixed burn is gated on body clearance, and the burn–coast–burn's
capture chain now enters at the **arrival handover floor**. Step 5, the steering
experiment, is next. Screenshots are waived for this plan.

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
   floor** (flown in step 4). An interior solved radius waits on the steering
   measurement.
3. **Quintic ends stay.** The break and the conic/hybrid capture keep their
   jobs; only the handover radius and the field changed.
4. **The capture chain takes an entry radius.** *Implemented in step 4, radius
   only.* The burn–coast–burn enters at `max(band floor, conic parking radius)`;
   the coast profile and the powered conic arrival keep the sphere. The **entry
   speed is still the per-plan `EntrySpeedMps`** (5 km/s default); the
   brake-produced residual is the next decision and needs the step-4 numbers.
5. **Steering starts simple.** A fixed inertial direction, subdivided into a few
   segments if needed. Measure the Δv penalty against a local-prograde law
   before paying for a shooting solve.

Open questions: the entry-speed rule, the approach model (see Traps), the
steering law, whether an interior solved radius is worth it, and whether the
handover radius should be a search input or a certified build.

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
5. **Steering experiment.** Fixed direction versus a few fixed segments versus
   local prograde. Record the Δv penalty at the epoch at 0.3 g and 1 g, and pick
   one in the concept. *Next.*
6. **Slider and window.** The trip-time window's left end moved (Mars
   6.74–20.21 days, Mercury 3.22–9.65); the HUD copy and the window cache hold in
   the tests, and step 6 should re-check the copy and the floor search's
   conservative bracket (see Traps).
7. **Performance.** Full-field probes are slower and less smooth; measure
   `Plan()` and one slider retime before and after. Close out the docs and clear
   this file.

## Verification

- `tools/check.sh --tests flight` for each Simulation/TripPlanner step.
- `tools/check.sh --tests presentation,smoke` when the window or HUD changes.
- `tools/check.sh --fast` for a docs-only step.
- `.venv/bin/python tools/validate_okf.py` for every bundle edit.
- Last result (2026-11-20, after step 4): `tools/check.sh --tests flight` —
  passed, 249 tests, 0 failed, 606 s; `tools/check.sh --tests
  flight,presentation,smoke` on the final tree — passed, 389 tests, 0 failed,
  1005 s; `.venv/bin/python tools/validate_okf.py` — 40 concepts, 0 warnings,
  0 errors. (Step 3's run was 245 tests, 0 failed, 405 s.)

## Traps

- Do not split burns at the SOI. That is the patched option and it puts the
  seam where the models disagree most.
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
  editor's burn–coast–burn class is a ~4-minute silent stretch of the Unity log.
  The guard's stall window is eight minutes and the suite about nine; do not read
  that as a hang. Step 7 owns the remedy.
- A **body-relative position** on a full-field arc carries the catalogue's
  Julian-date quantisation (0.2–0.9 m at 1.5 AU). Compare inertial states in
  tests, or use a metre-scale tolerance.
- The hybrid capture **solver** stays on the two-body burn on purpose. Its
  reported arc and trim are full-field. Do not thread the field through
  `HybridCapture` without dealing with its search cost.
- `TryCorrectCoastDeparture` targets a millimetre, not 10⁻⁴ m.
