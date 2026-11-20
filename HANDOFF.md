# Handoff

**Active plan, started 2026-11-19: full-field burns to the powered-leg floor.**
Steps 1–6 and step 4b are done; step 7 is next. Step 4b (the planetocentric
approach leg) was done in the `work/approach-leg` worktree and merges to `main`
with this handoff, per working-agreement rule 1. The design is recorded in
[Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md);
`FiniteBurnArc` integrates the summed field, the handover band bounds the shaped
ends, every fixed burn is gated on body clearance, the burn–coast–burn's capture
chain enters at the **arrival handover floor**, the arrival carries a
**planetocentric approach from the handover ceiling** so a Sun-only conic no
longer describes the deep part of the final approach, and both fixed burns keep
the **fixed inertial direction** because the steering measurement found nothing
for a tracking or segmented law to win. Screenshots are waived for this plan.

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

4b. **The approach leg.** *Implemented.* The heliocentric coast ends on the
   target's ceiling, a `ConicArc` about the target falls from there to the
   brake's start, and the brake ends at the chain's entry on the floor. Measured
   at the epoch, the leg is 27 m/s **cheaper** at Uranus 1 g and 400 days
   (182.312 against 182.339 km/s) and 0.60 km/s cheaper at a 200-day Mars
   crossing; it removes the 0.221 km/s of solar tide the deep coast commanded.
   The floors barely move: Uranus 1 g 34.00 → 33.90 d, the other three
   unchanged. Numbers in [the concept](/simulation/powered-leg-floor.md#what-the-approach-measured).

Open questions: the entry-speed rule, whether an interior solved radius is worth
it, and whether the handover radius should be a search input or a certified
build.

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
   measurement was step 4b's input; the approach leg now ends the coast on the
   ceiling and the brake starts at 368,111 km (0.0071 SOI) at 1 g.
5. **Steering experiment.** **Done** — the fixed inertial direction stays at
   both ends. Four plans at the epoch (Uranus 400 d at 1 g and 0.3 g, Mars 90 d
   at 0.3 g, Mercury 8 d at 1 g): each burn's held direction was recovered from
   `AccelerationAt − GravityField`, then the same start state, duration and
   acceleration were re-flown at 2,048+ RK4 steps through the full field with the
   plan's direction, local prograde/retrograde, 1–128 segments, and a pursuit
   law. The fixed law reproduced the plan end state to 0.000 km and 0.000 km/s
   (0.15 m on the re-run with the approach leg's brake), so the harness and the
   planner integrate the same field. The departure burn is
   the escape ray, 0.00° from the frame's prograde at every sample, and a local
   law reproduces it to 0.003 km/s. The brakes turn 30–78° of tracking and the
   held direction ends 39.7–87.6° off retrograde; the local-retrograde seam is
   **17.6 / 13.3 / 3.1 / 6.0 km/s**, and re-aiming makes it worse: 15.3 at one
   segment, 22.2 at two, 34.1 at eight and 39.1 at 128 on Uranus 1 g, while
   pursuit reaches 6.2 / 11.0 / 0.14 / 0.009. The 16.80% cosine loss at Uranus
   1 g is 13.68 km/s
   of thrust across the track — the turn the brake exists to make, not a
   spendable penalty. The concept records the whole table and the decision.
6. **Slider and window.** **Done** — re-checked after the floor move: the HUD
   copy and the window cache hold. The `TRIP TIME ×scale` label, the value line,
   and the report rows all derive from the preview, no user-facing string carries
   a floor or window number, and `WindowFor` keys on departure date, target,
   level, cruise, and arrival and drops the cache on a date change; the
   presentation and smoke suites pin the slider range and the profile's own
   window. The floor search's conservative bracket is measured: Uranus 1 g
   returns 34.00 d where a direct **33.49 d** build closes (0.51 d, 1.5%), and
   33.66–33.83 d stall in the pinned-departure solve at 1.0–3.2 × 10⁻⁵ m/s.
   Accepting the stalled solve closed them but cost ~110 s per near-floor build,
   so the conservatism is a **cost shield**, not a wrong answer; step 7 owns the
   trade. The Uranus window's maximum (101.99 d) is feasible. After step 4b the
   1 g floor is **33.90 d** and the window ends at **101.70 d**; only that floor
   moved. Numbers in
   [planning performance](/technical/planning-performance.md#the-uranus-floor-and-its-conservative-bracket-2026-11-20).
7. **Performance.** *Next.* Full-field probes are slower and less smooth; measure
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
- Last result (2026-11-20, after step 4b): `.venv/bin/python
  tools/validate_okf.py` — 41 concepts, 0 warnings, 0 errors; `tools/check.sh
  --tests flight` — passed, 0 failed, 620 s; `tools/check.sh --tests
  presentation,smoke` — passed, 0 failed, 367 s (the plan's arc list and the
  flight report changed, so both ran).
  (Step 6's run: `tools/check.sh --tests presentation,smoke` — passed, 143
  tests, 0 failed, 379 s; `tools/check.sh --fast` — passed. Step 4's code run:
  `tools/check.sh --tests flight` — passed, 249 tests, 0 failed, 606 s;
  `tools/check.sh --tests flight,presentation,smoke` on the final tree —
  passed, 389 tests, 0 failed, 1005 s.)

## Traps

- Do not split burns at the SOI. That is the patched option and it puts the
  seam where the models disagree most.
- **Do not replace the brake's fixed direction with local steering.** Flown over
  the solved burn's own start state, duration and acceleration, a local
  prograde/retrograde law leaves **17.6 km/s** of seam at Uranus 1 g, 13.3 at
  0.3 g, 3.1 at Mars and 6.0 at Mercury, and a pursuit law leaves 6.2 / 11.0 /
  0.14 / 0.009; neither closes the chain. Re-aiming every few segments is
  monotonically worse (15.3 at one, 22.2 at two, 29.2 at four, 34.1 at eight,
  39.1 at 128 on Uranus 1 g), because the schedule follows the velocity as it
  turns instead of the chord the seam needs. The fixed direction is exact and its
  spend is `a·T`, so segmenting pays twelve pin passes for a worse seam. The
  reasoning is in [the concept](/simulation/powered-leg-floor.md#steering-starts-simple).
- Do not "fix" the fall by braking sooner; it lengthens the coast. The win is a
  hotter entry and a deeper brake.
- **The approach leg is settled, and it is not a route to the floor without a
  brake.** The coast ends on the ceiling and a planetocentric `ConicArc` falls
  from there to the brake's start. A ballistic conic from the ceiling does
  **not** reach the band floor on any measured plan — its periapsis is 84,354 km
  at Uranus 1 g (floor 38,643), 200,201 at 0.3 g (70,168), 10,059 at Mars 90 d
  (6,033) and 61,330 at Mercury 8 d (2,806) — so the brake still spans the last
  part of the descent. Do not "fix" that by lowering the entry radius, widening
  the seam gate, or moving the floor. The approach is built **backwards from the
  brake's own start state**: that is what makes approach→brake exact, and it is
  why the fixed point's residual lives at the ceiling seam instead. It converges
  super-linearly (1e-8 m/s in ten passes) once the brake has a real start; the
  first pass must run **without** the leg or the brake is a second long, its
  start is the chain's entry and the conic back out to the ceiling takes 119
  days.
- **The fast trips keep seven arcs on purpose.** Where the brake's start is
  already outside the ceiling (Mercury 8 d: 2,378,950 km, 20.7 ceilings) there is
  no planetocentric region to model. A conic about Mercury at twenty spheres
  would be a worse model than the heliocentric coast. Do not force an approach
  leg there.
- **The coast → approach seam is metres, not millimetres.** Measured 4.38 m and
  1 × 10⁻⁶ m/s at Uranus 1 g (0.53 m at 0.3 g, 2 × 10⁻⁶ m/s at Mars). The metres
  are the difference between the coast's element-based propagation and the
  universal-variable propagator `TryCorrectCoastDeparture` uses; both are correct
  and the gate is 1 km. Do not tighten it by loosening the correction.
- **The slow end got cheaper, not dearer.** A 200-day Mars crossing is
  11.855 km/s against 12.458, because the old deep coast commanded 0.901 km/s of
  solar tide to hold its heliocentric conic in Mars's well; the new approach is
  27.3 h and the coast's correction 0.121. Do not "restore" the 12.46 anchor.
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
  Uranus 0.3 g floor and reported the trip infeasible. The search is
  conservative by **0.51 d (1.5%)** at Uranus 1 g — 34.00 d returned against a
  direct **33.49 d** that closes (33.90 d after the approach leg) — and by
  **0.62 d** at 0.3 g (41.57 against 40.95). The gap is a **pinned-departure stall**, not a physical refusal: the
  damped Newton cannot improve at the finite-difference resolution, so the
  residual stops at 1.0–3.2 × 10⁻⁵ m/s just above
  `DepartureSolveToleranceMps = 1e-5` and `TrySolvePinnedDeparture` refuses.
  Accepting a stalled solve (`best × coast ≤ 250 m`) closed 33.66 and 33.83 d
  but made each formerly-stalled near-floor build **~110 s** — the brake and
  burn are 28.8 h at 2,048 samples — so the conservatism is a **cost shield**,
  not a wrong answer. Step 7 owns the trade. Mars and Mercury fail honestly below their floors (a
  brake miss or a break over the drive), with no stall hole. **Uranus is now a
  reachable target**, and its first selection is the cost: the first `Plan()` is
  **~71 s in the .NET harness** at 1 g and ~59 s at 0.3 g, not a few seconds,
  and the editor is several times slower, so a first Uranus preview freezes the
  UI for minutes until step 7. The window's right end is 3 × the floor,
  **101.70 d** at 1 g after the approach leg (**101.99 d** before it), and a
  direct build there is accepted (eight arcs, 661.3 km/s, **2.7 s**).
- **The HUD copy and the window cache hold** after the floor move (step 6): the
  `TRIP TIME ×scale` label, the value line, and the report rows derive from the
  preview, and `WindowFor`/`TryGetCachedWindow` key on departure date, target,
  level, cruise, and arrival with a date change dropping the cache. Do not
  reintroduce a floor or window number as a UI literal; the tests pin the
  profile's own window.
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
