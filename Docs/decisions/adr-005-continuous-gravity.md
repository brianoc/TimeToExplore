---
type: Decision Record
title: "ADR-005: the ship flies through a continuous gravity field"
description: Integrate the ship against summed point-mass gravity from every catalogue body, with the planets still on Keplerian rails.
tags: [decision, simulation, gravity, integration]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-16T00:00:00Z }
---

# Context

[Orbital mechanics](../simulation/orbital-mechanics.md) left one question open:
does the ship fly through patched conics, switching frames at each sphere of
influence, or through a continuous field?

The design brief is literal: **gravity affects any object that is not on a fixed
orbit.** Nothing in the game needs the ship to change gravity model when it
crosses an invisible sphere.

# Decision

1. **The ship is integrated against a continuous field.** Its acceleration is
   the vector sum of point-mass gravity from the catalogue bodies at the current
   Julian date.
2. **The bodies stay on their rails.** The field evaluates the existing
   Keplerian ephemerides; it never integrates the planets, and it never mutates
   `SolarSystemModel`. A station's contribution is zero because its `GM` is zero.
3. **The field is pruned, not switched.** Bodies whose acceleration at the ship
   is below 10⁻¹¹ m/s² are dropped, and at most eight bodies are summed. Over a
   two-week cruise that floor is worth about 1 mm/s. The pruned sum is tested
   against the full sum to better than 10⁻⁵ relative.
4. **The ship is always numerically integrated.** There is no "parked on rails"
   state: an idle ship in a parking orbit keeps being integrated, and its orbit
   drifts under the real field. That is honest, and it means arrival has a
   single, unambiguous meaning.
5. **Spheres of influence survive only as planning waypoints.** The planner uses
   the patched-conic radius to decide where the cruise leg hands over to the
   capture leg. It never changes the dynamics.

# Consequences

**Positive**

* There is no seam at an SOI boundary: no position, velocity, or acceleration
  discontinuity to hide, and no frame-switch state to serialise.
* Moons are included for free. A lunar parking orbit feels the Earth's tide and
  the Sun's, so "stable" means stable in the real field.
* The same field serves the ship, future debris, and any projection.
* Collision is a natural consequence: the field clamps at a body's surface and
  the integrator reports the hit.

**Negative**

* Every step costs a handful of ephemeris evaluations. The field is prepared
  once per substep (all 131 massive bodies) and evaluated at four RK4 stage
  times for the active set. A plan takes tens of milliseconds; a three-day
  flight takes a few thousand substeps.
* A deep gravity well is harder to plan through than a patched conic, because
  the field genuinely is strong there. Jupiter's gravity at a surface-radius
  parking orbit is 19.6 m/s², twice the drive, so the arrival orbit is lifted
  until local gravity is 40% of the drive. See the gravity ceiling in
  [navigation and arrival](../simulation/navigation-and-arrival.md#the-deep-well-limit).
* There is no "which body owns the ship" query. Anything that wants one, such as
  the HUD's "orbit of" line, must derive it from the strongest acceleration.

# Alternatives considered

**Patched conics with SOI switching.** Cheaper per step and easier to explain,
but it introduces a discontinuity at every boundary, needs frame state, and
would be visible as a kink in the predicted path. Rejected for the dynamics;
retained for planning waypoints.

**A fixed dominant body per leg.** Simpler than a full patched conic but wrong
near a moon, where the Earth and the Moon both matter.

**Full N-body with the planets integrated too.** Rejected by
[ADR-001](adr-001-keplerian-orbits.md): the ephemerides only need to be accurate,
not dynamical, and integrating 131 bodies for a 15-day trip buys nothing.

# Revisit when

* the per-substep cost shows up in a frame budget,
* the capture solver needs the field's gradient or Hessian,
* a second ship or debris makes the "prepare once per queried position" pattern
  awkward.
