# Handoff

**Active plan, started 2026-11-19: full-field burns to the powered-leg floor.**
Steps 1–3 are done. The design is recorded in
[Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md);
`FiniteBurnArc` integrates the summed field, the handover band bounds the shaped
ends, and every fixed burn is gated on body clearance. Step 4, the capture entry
radius and the residual entry speed, is next. Screenshots are waived for this
plan.

## Why

A Uranus burn–coast–burn trip reads as almost all coast at any trip-time slider
setting. The conic capture always enters the target's sphere of influence at the
planted `TripPlanner.EntrySpeedMps = 5000`, and Uranus's SOI is 51.8 million km,
so the drive-off free-fall to periapsis is about **104 days** (roughly
`r_SOI / v∞`; Earth's 0.92 million km is about 2 days). The slider only re-times
the heliocentric leg, so the fixed capture coast dominates every duration. This
is a *speed* problem, not an arc-boundary problem: braking early makes the fall
longer, and the architecture below is what makes a hot entry and a deep brake
representable.

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
   is empty. Endpoints are decided for this plan: **departure = the ceiling**
   (unchanged plan) and **arrival = the floor** (flown in step 4); an interior
   solved radius waits on the steering measurement.
3. **Quintic ends stay.** The break and the conic/hybrid capture keep their
   jobs; only the handover radius and the field change.
4. **Entry speed becomes an output.** The capture chain gains an entry radius;
   the brake solve sets the residual entry speed instead of the global 5 km/s
   constant (at least as an option). This is what allows a short fall.
5. **Steering starts simple.** A fixed inertial direction, subdivided into a
   few segments if needed. Measure the Δv penalty against a local-prograde law
   before paying for a shooting solve. (The field change kept the existing law:
   the escape ray and handover change are fixed inertial directions, and
   prograde/retrograde follow the reference-relative velocity.)

Open questions: the steering law above, whether an interior solved radius is worth
it, and whether the handover radius should be a search input or a certified build.
The departure/arrival asymmetry is settled for this plan (ceiling and floor).

## Steps (one sub-agent per step, one commit each)

1. **Record the design.** **Done** — the new concept is
   [Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md).
2. **Full-field finite burn.** **Done** — `FiniteBurnArc` takes an optional
   `IGravityField`, integrates absolute coordinates, and decomposes back into the
   reference body's frame; every fixed burn in a certified plan is full-field
   (departure, brake, hybrid injection); the fast probes keep the two-body burn.
   Tests: an SOI-crossing burn against an independent RK4, the two-body agreement
   where the third bodies are negligible, the reference body as a label, and the
   plan's burns carrying the field. The plans and floors did not move; the cost
   did (see Traps).
3. **Handover band and endpoints.** **Done** — `HandoverBand` computes the floor
   (`0.4` of the drive, clamped to `R + 100 km`), the ceiling (`1.02 × SOI`), and
   the empty-band reason; the departure endpoint is the ceiling (the plan is
   unchanged) and the arrival endpoint is the floor, validated but not yet flown.
   `DepartureBurnStaysOutside` became `FixedBurnClearsBody`: every fixed burn
   keeps 100 km above the body it works near, and the SOI is not consulted.
   Tests: floor scaling, ceiling per body, the clamp on every accepted body,
   Phobos/Deimos/Proteus empty and refused, and the gate accepting an SOI-crossing
   burn while refusing a surface graze. New `HandoverBandTests`.
4. **Capture entry radius and entry speed.** Build the capture chain from an
   arbitrary handover radius — the arrival endpoint is
   `max(band floor, what the chain needs)`, because the clamped floor sits below
   the conic periapsis at 1 g on Mercury, Mars, and several small moons; the
   concern is already recorded in Traps. Let the brake set the residual entry
   speed. Tests:
   the Uranus fall shortens as the entry gets hotter, the capture Δv grows as
   predicted, seams stay closed.
5. **Steering experiment.** Fixed direction versus a few fixed segments versus
   local prograde. Record the Δv penalty at the epoch at 0.3 g and 1 g, and pick
   one in the concept.
6. **Slider and window.** The trip-time window's left end now reflects the new
   floor; check the HUD copy and the window cache still hold.
7. **Performance.** Full-field probes are slower and less smooth; use the
   existing fast-probe-then-certify pattern. Measure `Plan()` and one slider
   retime before and after. Close out the docs and clear this file.

## Verification

- `tools/check.sh --tests flight` for each Simulation/TripPlanner step.
- `tools/check.sh --tests presentation,smoke` when the window or HUD changes.
- `tools/check.sh --fast` for a docs-only step.
- `.venv/bin/python tools/validate_okf.py` for every bundle edit.
- Last result (2026-11-19, after step 3): `tools/check.sh --tests flight` —
  passed, 245 tests, 0 failed, 405 s; `.venv/bin/python tools/validate_okf.py` —
  40 concepts, 0 warnings, 0 errors. (The step 2 run was 239 tests, 403 s, and
  `presentation,smoke` 140 tests, 159 s; step 3 touched no UI, so that suite was
  not re-run.)

## Traps

- Do not split burns at the SOI. That is the patched option and it puts the
  seam where the models disagree most.
- Do not "fix" the fall by braking sooner; it lengthens the coast. The win is a
  hotter entry and a deeper brake.
- `EntrySpeedMps` is global today; changing it moves every conic plan. Make the
  replacement per-plan before tuning it.
- The floor search is already noisy near the minimum; a full-field probe makes
  it noisier and slower. Certify the chosen build as the current profile does.
- The existing anchors (Mercury 3.46 d, Mars 8.03 d, the seam tolerances) will
  move when the handover changes. Step 3 changed the rule and left them alone
  (measured: 569.9 / 379.9 / 45.4 km/s, floors 3.46 and 8.03 d); step 4 is the one
  that will move them. Re-measure them; do not preserve them blindly.
- **The arrival floor does not bind below the capture chain.** At 1 g the clamped
  floor is *under* the conic periapsis on Mercury (2,539.7 km against 2,561.7) and
  Mars (3,489.5 against 3,559.0), and on several small moons whose `1.05 R`
  periapsis beats `R + 100 km`. The floor is a lower bound on the handover, not a
  promise the chain can start there: step 4's arrival endpoint must be
  `max(floor, what the chain needs)`, or the chain is handed an entry inside its
  own periapsis.
- **Three catalogue moons have an empty band at every level**: Phobos and Deimos
  (their spheres of influence are inside their own surfaces: 7.2 km against an
  11.3 km radius) and Proteus (a 310.0 km floor against a 280.1 km ceiling). They
  were already unreachable through the conic chain's "the sphere of influence is
  smaller than the periapsis"; the refusal now comes first and names the numbers.
  Do not "fix" this by moving the ceiling.
- **The floor is clamped, so it is not `1/sqrt(a)` everywhere.** Only an unclamped
  floor scales; Mercury and Mars at 1 g sit on the `R + 100 km` clearance and do
  not move until the level falls far enough.
- **Full-field burns are the suite's cost now.** One `Prepare` per RK4 step
  evaluates the whole catalogue, so a full-field step is ~50× a two-body step:
  a 90-day Mars build is 0.47 s in a .NET harness and the editor's
  burn–coast–burn class is a ~4-minute silent stretch of the Unity log. The
  guard's stall window is eight minutes and the suite about nine; do not read
  that as a hang. Step 7 owns the remedy (a cheaper prepare rule, measured
  against the per-step one).
- A **body-relative position** on a full-field arc carries the catalogue's
  Julian-date quantisation (0.2–0.9 m at 1.5 AU, because a date's last place is
  40 µs), while the absolute path does not. Compare inertial states in tests, or
  use a metre-scale tolerance; `TheChainIsContinuous...` reads absolute positions
  and still sees 10⁻⁴ m.
- The hybrid capture **solver** stays on the two-body burn on purpose (measured:
  under 0.2 km and 0.05 m/s of trim against a 5 km injection margin). Its
  reported arc and trim are full-field. Do not thread the field through
  `HybridCapture` without dealing with its search cost.
- `TryCorrectCoastDeparture` targets a millimetre, not 10⁻⁴ m. A weeks-long coast
  at 1.5 AU cannot resolve better than ~10⁻⁴ m, and the tighter target turned
  round-off into a refusal at 0.1 and 0.15 g.
