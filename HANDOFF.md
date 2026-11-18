# Handoff

## Agreed goal (2026-11-18)

Build the **hybrid fixed-thrust capture**:

* the player chooses a fixed burn level `a` on a slider from **0 g to 1 g**;
* the ship does most of the capture as a **fixed-thrust burn at `a`**;
* at a safe **injection point** the plan switches to an **impulse** that sets the
  transfer ellipse to the parking radius exactly;
* the conic transfer and circularisation follow as they do today.

In short: fixed burn for the long, fixed-thrust part; impulsive conic trim for
exactness. At `a = 0` the style degenerates to the existing pure conic arrival.
The point is to decouple the parking radius from the finite-burn loss, so the
0.3 g case no longer forces the higher `1.4R` orbit.

The detailed reasoning is in
[ADR-015](Docs/decisions/adr-015-fixed-thrust-burns.md),
[ADR-014](Docs/decisions/adr-014-keplerian-coast.md),
[navigation and arrival](Docs/simulation/navigation-and-arrival.md), and
[the flight model](Docs/simulation/flight-model.md).

## Glossary agreed

The thrust vocabulary is written down in the
[glossary](Docs/glossary.md#thrust-and-manoeuvre-vocabulary): four axes (thrust
profile, manoeuvre model, path shape, arrival choice), with the drive, burn,
coast, impulse, finite burn, arc/phase/leg, capture geometry, and the planned
hybrid terms. The three rules that settle arguments:

* **Impulse is a manoeuvre model, not a thrust type.** It is an instantaneous
  Δv; the ship cannot fly it. The conic style is *impulsive*; the finite-burn
  style flies real burns.
* **"Fixed-thrust" means constant commanded acceleration.** The code holds
  acceleration, not force; if the fuel model later varies mass, revisit the name.
* **"Equivalent to the impulse" means the same final orbit**, not the same path,
  timing, or Δv.

Next: start the implementation plan at step 1 (level as data).

## Current state

The **0.3 g fixed-thrust arrival** is complete: `CaptureStyle.FiniteBurn`, the
third HUD button, `FiniteBurnArc`, `FiniteBurnCapture`, the `Burns` row, solid
burn lines and markers. `tools/check.sh` passes with **443 tests, 0 failed,
0 skipped**; OKF 37 concepts, 0 warnings/errors.

The departure and cruise are still powered quintics. The Lambert/shooting
burn-and-coast cruise remains deferred and is not part of this goal.

## Implementation plan after the glossary

1. **Level as data.** Replace the single `FixedBurnAccelerationMps2` with a
   player-facing level in `[0, 1 g]`, clamped to
   `MaxAccelerationMps2 * ThrustMargin`. `0 g` selects the pure conic; the
   label reads `0.0 g burn` and there is no finite-burn arc.
2. **Injection point.** Choose a safe radius `r_i` below the parking radius.
   Start with the conic periapsis or a small safe margin above the body; the
   solver may raise it when the chosen level cannot reach it.
3. **Hybrid capture.** Coast the inbound hyperbola, burn retrograde at `a` to
   `r_i`, then apply the impulse
   `Δv = v_transfer(r_i) − v_end` (cancelling the radial component), where
   `a_transfer = (r_i + r_park) / 2`. Coast the transfer ellipse and
   circularise.
4. **Solve the split.** Use the existing start-offset/duration search to put the
   fixed burn's end state at `r_i` and keep the whole path clear of the body.
   Report the finite `Δv`, the impulse `Δv`, and the split in the panel.
5. **Parking radius.** Return it to the conic `1.15R` (or a level-aware value);
   the level should no longer force `1.4R`.
6. **UI.** Add the level slider or a compact selector beside the three arrival
   buttons. Changing the level rebuilds the preview from the pinned departure
   state, like the style switch. The `Burns` row shows the fixed burn and the
   impulse trim.
7. **Tests.** Levels 0 g, 0.3 g, 0.5 g, 1.0 g; 0 g equals the conic plan; each
   level reaches an exact parking orbit; the injection geometry; the fixed burn
   clears the body; the split reaches the panel; the slider rebuilds the
   preview; the final orbit stays stable across the target set.
8. **Docs.** Update ADR-015 or add its follow-up, the navigation and flight
   model sections, the flight session, testing, roadmap, glossary, and log.

## Verification

`tools/check.sh` — currently **443 tests, 0 failed, 0 skipped**. The 1 g scan
quoted in the design discussion is a quick two-body Python check, not the
shipped solver; re-measure through the hybrid solver before recording numbers.

## Traps

* **The terminal impulse is not fixed-thrust.** The plan is a hybrid: a fixed
  burn for most of the capture, then an impulse for exactness. If every burn
  must be at the chosen level, replace the impulse with a short finite trim and
  accept a small residual or a targeting iteration.
* The fixed burn only controls position, not the final velocity. The injection
  point must be reachable and below the parking radius; the impulse cannot move
  the ship to a different position.
* The fixed burn's path can dip below the body even if its end point is safe.
  Check the minimum radius over the whole burn.
* At very low levels the burn may be longer than the inbound coast, or the
  impulse may be large; report infeasible or raise the level rather than
  silently inventing a burn before the sphere-of-influence entry.
* The HUD's three-button row is tight (356 px). A slider plus three buttons
  needs a second row or a compact selector.
* The current `FiniteBurnCapture` keeps the largest safe periapsis and matches
  the post-burn apoapsis. The hybrid needs a different target (the injection
  radius), so make that explicit rather than adding another constant.

## Read first in the new session

* [The design bundle](Docs/index.md)
* [Working agreement](Docs/working-agreement.md)
* [Glossary](Docs/glossary.md)
* [ADR-015](Docs/decisions/adr-015-fixed-thrust-burns.md)
* [Navigation and arrival](Docs/simulation/navigation-and-arrival.md)
