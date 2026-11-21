---
type: Decision Record
title: "ADR-014: Transfer conics and the Oberth capture"
description: Propagate coasts with universal variables, parameterise them by true anomaly, and replace the deep-well capture with a hyperbola, an impulsive periapsis burn, a transfer ellipse, and a circularisation burn.
tags: [decision, simulation, navigation, trajectory, orbital-mechanics]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-18T00:00:00Z }
---

# Context

Go To plans are chains of quintic reference arcs
([ADR-006](adr-006-quintic-plan.md)). The quintic is deterministic and its
arrival is exact, but a continuous-thrust capture cannot command less thrust
than the parking orbit's own gravity: the curve must arrive with the local
gravity as its endpoint acceleration, so the parking radius has to be lifted
until `GM / r²` is 40% of the drive. That is the gravity ceiling. It makes
Jupiter reachable at 177,965 km, but it parks Earth at 9,983 km and Saturn at
97,380 km, inside the ring system the catalogue does not model.

The physically honest arrival is a coast. On a two-body arc the acceleration is
the local gravity *pointwise*, so the commanded thrust is exactly zero at every
point rather than only at the endpoints. A low periapsis then makes the capture
cheap through the Oberth effect: the same delta-v buys more energy change deep
in the well. The design has carried this as the deferred transfer-conic item
since the deep-well measurement.

Two pieces were missing: a state-vector propagator (`OrbitalElements.GetState`
already goes elements → state, and `OrbitMath.SolveKepler` is elliptic-only),
and an arc type that the plan, the renderer, and the autopilot can all carry
without assuming a quintic.

# Decision

1. **Propagate with universal variables.** `KeplerPropagator` solves the
   universal Kepler equation with the Stumpff functions `C(z)` and `S(z)`, so
   one formulation covers the ellipse, the parabola, and the hyperbola. Newton
   with a bracket-and-bisect fallback is deterministic; the parabolic
   singularity is handled by the series for `|z| < 10⁻⁵`. A state → conic →
   state round trip is tested against the existing elements path.
2. **Describe the conic once.** `ConicElements.FromState` derives the
   eccentricity, semi-latus rectum, apsis radii, perifocal basis, true anomaly,
   and the time since periapsis. It converts between true anomaly and time with
   Kepler's equation (bisection for the hyperbolic anomaly, Barker's equation at
   the parabolic boundary).
3. **Make the arc contract explicit.** `IArcPath` supplies
   `PositionAt/VelocityAt/AccelerationAt` and the two mappings between the
   parameter `u` and real time. `Quintic` is linear in time; `ConicArc` is
   linear in **true anomaly**, so sampling is dense at periapsis and sparse on
   the long approach. `PathTessellation`, `PlanArc`, `TrajectoryPlan`,
   `TrajectoryLine`, and `Autopilot` all speak the interface.
4. **Coast in the target's frame.** `ConicArc` stores a planetocentric conic
   and adds the body's ephemeris at each query. Its reference acceleration is
   the body's rail acceleration plus the body's local gravity, which is what
   makes the seam continuous with the cruise. The summed field differs by the
   third-body tide; the autopilot supplies that small correction, and
   `Measure` sees a near-zero thrust on the coast rather than integrating a
   fake burn.
5. **Build the capture as a chain.** The arrival geometry is a prograde
   equatorial hyperbola from the sphere of influence to a low periapsis
   `max(1.05 R, R + 100 km)`, an impulsive retrograde capture burn into a
   transfer ellipse whose apoapsis is the parking radius, and a prograde
   circularisation burn at apoapsis. The capture plane is the parking plane by
   construction, so neither burn needs a plane change.
6. **Model the burns as impulses.** A `PlanArc` carries an entry impulse
   vector. The plan's delta-v is the integrated thrust of the powered arcs plus
   the impulse magnitudes; the coast's delta-v is the small third-body
   integral. `FlightSession` applies an impulse once when it crosses the seam,
   and `Autopilot.Fly` does the same for the headless tests. The seam is
   continuous in position and acceleration, and steps in velocity by exactly
   the planned impulse.
7. **Keep a quintic capture as the default at the time, and make the conic
   arrival an explicit style.** `TripPlanner.ArrivalStyle` defaulted to the
   quintic capture, so the trip-time slider and the measured torch plans were
   unchanged. The conic capture was the cheap, slow alternative. The
   conic capture later became the profile's default when the powered cruise was
   retired; a trip that starts inside the target's sphere of influence still
   has no entry hyperbola and keeps the quintic capture.
8. **The conic parking orbit has no gravity ceiling.** It uses
   `max(1.15 R, R + 300 km)`, capped at half the Hill radius. The ceiling exists
   because a quintic capture cannot fly a deep well; an impulsive capture can.
   Earth returns to 7,327 km, Jupiter to 80,398 km, and Saturn to 66,967 km.
   The periapsis is below the parking radius on every body with room for a
   transfer ellipse; a body without that room reports the arrival infeasible.
9. **Draw the coast and the burns distinctly.** The trajectory line keeps the
   solid powered path, draws coasts and the capture in the dashed coast style,
   draws the short parking coast in a green dashed style, and adds a marker at
   each impulsive burn. The flight panel adds the `Coast` and `Parking` phases
   and a `Burns` row. The periapsis and circularisation are plan boundaries, so
   the warp policy and the next-event countdown see them without a new phase.

# Consequences

**Positive**

* The capture is roughly **20–100× cheaper**. Measured from Gateway at the
  epoch, the conic capture burns 3.5–17.4 km/s against the quintic capture's
  84–1,159 km/s:

  | Target | Quintic capture | Conic capture | Conic parking radius |
  |---|---:|---:|---:|
  | Venus | 86.0 km/s | 4.13 km/s | 6,960 km |
  | Mars | 84.8 km/s | 3.53 km/s | 3,898 km |
  | Jupiter | 1,088.6 km/s | 17.40 km/s | 80,398 km |
  | Saturn | 1,159.5 km/s | 10.66 km/s | 66,967 km |
  | Earth (from Mars orbit) | 218.3 km/s | 4.28 km/s | 7,327 km |

* The deep wells park where the design wants them. Earth leaves the ceiling
  radius of 9,983 km for 7,327 km, Jupiter for 80,398 km, Saturn for 66,967 km.
  The final orbit is a real free-fall orbit; the flights hold it with
  eccentricities of 10⁻⁴ or less after three orbits.
* The coast's reference thrust is **zero**; the measured peak on the Mars coast
  is below 0.01 m/s², and the remaining delta-v is the third-body correction.
* The arrival is still exact: the conic flight arrives 0.1–1.1 m and
  0.0001–0.003 m/s from the reference state, because the autopilot tracks the
  conic's position and the impulse is applied at the seam.
* The navigator can draw a genuinely free arc instead of a shaped burn, which
  is the visual and conceptual difference the design asked for.

**Negative**

* **The cheap arrival is slow.** A coast from the sphere of influence to
  periapsis spends months at the outer planets: the conic plans measure 5.6 days
  to Mars, 99.5 days to Jupiter, and 131.5 days to Saturn, against 4.4, 10.3,
  and 12.9 days for the torch capture. That is the burn-and-coast regime, not a
  replacement for the fast plan. The default stays quintic until the nav
  computer offers the time/delta-v choice.
* **The burns are instantaneous.** The drive's finite thrust is not modelled at
  the seam; a 17 km/s capture is an idealisation. The plan's delta-v is the
  right number for the fuel model, and a finite-burn arc is the next fidelity
  step.
* **The total plan delta-v is not always lower.** The capture is dramatically
  cheaper, but the conic plan may spend more on a compressed cruise; for Venus
  the total measured 1,207 km/s against the quintic's 1,171 km/s while the
  capture fell from 86.0 to 4.13 km/s. The honest comparison is the capture,
  and the time/delta-v trade is what the nav computer still has to expose.
* **The outer arrival orbits are inside the ring systems and radiation belts**
  the catalogue does not model. Lower is not automatically safer; a ring-plane
  or atmosphere model would have to choose the arrival.
* The conic arc is not a Lambert solve. The cruise is still a powered quintic,
  so the middle of the trip cannot yet coast; that remains the burn-and-coast
  item.

# Alternatives considered

**`OrbitalElements.FromState` plus a hyperbolic Kepler solver.** It would reuse
the existing element type, but the planner needs a state-vector propagation for
the tests and the conic arc needs both directions of the anomaly mapping. The
universal formulation is one solver and one singularity treatment; the
elements path is still used to parameterise the arc and to check round trips.

**A shaped quintic with a flat middle.** Rejected before this ADR: a quintic
with a flat middle still commands thrust in a gravity field, so it would look
like a coast without being one. The whole point is `a_ref = g` pointwise.

**A finite-thrust burn arc.** More faithful, but it needs a thrust-direction
schedule, an integrated burn path, and a new feasibility check, and it would
make the arrival depend on the drive's acceleration again. The impulse model is
the right first step because it makes the delta-v honest and the arrival exact.
It is the named follow-up.

**A single burn into the circular orbit at the low periapsis.** Simpler, but it
cannot end at the parking radius without a second burn, and the transfer
ellipse's apoapsis circularisation is exactly the cheap geometry. The chain
keeps both burns and reports each as its own plan boundary.

# Revisit when

* fuel makes the time/delta-v choice real, and the nav computer needs an
  arrival-style control next to the trip-time slider;
* the finite burn has to be modelled, because the fuel model charges for a
  burn window rather than an impulse;
* the cruise itself should coast, which is the Lambert half of the
  burn-and-coast regime;
* rings, atmospheres, or radiation make a lower arrival a gameplay cost rather
  than a free improvement.
