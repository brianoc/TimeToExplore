---
type: Decision Record
title: "ADR-006: Go To plans are chains of quintic reference arcs"
description: Plan continuous-thrust trips as escape, cruise, and capture arcs that match position, velocity, and acceleration, and fly them with feed-forward plus light feedback.
tags: [decision, simulation, navigation, trajectory]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-16T00:00:00Z }
---

# Context

The player selects a body and presses **Go To**. The ship must leave its current
orbit, cross interplanetary space at 10 m/s², and finish in a stable orbit
around the target, without fuel limits for now.

There is no closed-form solution. The problem is a two-point boundary value
problem with bounded thrust, a moving target, and a gravity field. The design
document sketched a direct shooting method: parameterise six thrust segments,
integrate, measure the endpoint error, and adjust.

That is the right *eventual* answer, because it also minimises propellant. It is
a poor first answer: it needs a robust optimiser, its failure mode is "does not
converge", and it cannot easily guarantee the exact arrival state the game needs.

# Decision

1. **Plan three arcs: departure, cruise, capture.** The departure arc climbs out
   of the departure body's sphere of influence, the cruise arc crosses
   heliocentric space, and the capture arc drops into the target's parking
   orbit. A trip that starts inside the target's sphere of influence is a single
   capture arc.
2. **Each arc is a quintic Hermite curve** matching position, velocity, and
   acceleration at both ends. Six conditions per axis are exactly a quintic.
3. **The commanded thrust is the curve's acceleration minus local gravity,**
   evaluated at the curve's own position. Gravity is therefore feed-forward: the
   arc is the trajectory, not a reference the dynamics fight.
4. **Endpoint accelerations are the local gravity at each end,** so the
   commanded thrust is zero at every seam and at the start and end of the plan.
   The arcs are continuous in position, velocity, and acceleration.
5. **The acceleration and deceleration phases fall out of the curve.** No
   bang-bang switch is needed; the thrust profile is smooth, and the throttle is
   its magnitude.
6. **Time is allocated by the kinematic minimum and grown until it fits.** Each
   arc gets a share proportional to 2√(d/a); the whole plan is lengthened until
   the measured peak thrust is inside the drive, with the arrival date and the
   target's position re-solved each pass.
7. **The endpoint is fixed exactly:** the plan finishes on the parking orbit's
   position and velocity, at the instant the last arc ends. After that the ship
   is in free fall and the drive is off.
8. **An autopilot tracks the plan** with the feed-forward term plus a lightly
   damped position and velocity correction, and clamps to the drive's
   acceleration.
9. **The parking orbit respects a gravity ceiling.** Its radius is
   `max(1.15 R, R + 300 km, sqrt(GM / (0.4 a_max)))`, capped at half the Hill
   radius, because the capture arc cannot command less thrust than the orbit's
   own gravity. The ceiling tracks the drive, not a fixed radius.

# Consequences

**Positive**

* Deterministic: the same ship state and catalogue always produce the same plan.
* The arrival state is exact to machine precision, not to a shooting tolerance.
* The seams are smooth, so the drawn path has no corners and the burn profile
  has no steps.
* Failure is explicit. If the peak thrust cannot be brought under the limit, the
  plan is reported infeasible rather than flown badly.
* The same arc type serves escape, cruise, and capture, and the capital ship's
  path is a polyline sampled from it.
* A **gravity ceiling** on the parking orbit keeps the deep wells flyable. The
  arrival radius is lifted until local gravity is 40% of the drive, which puts
  Jupiter at 177,965 km and makes the capture arc's best peak 7.95 m/s². Every
  body in the catalogue is now reachable on one arc.

**Negative**

* The smooth profile is **35-75% slower than the bang-bang optimum**. Earth-
  Mars at 1.32 AU takes 4.4 days, against 3.3 days for the ideal, and Venus
  takes 2.9 days against 1.6. The flight model's tables are the ideal; the plan
  is what the ship can smoothly fly.
* It does not minimise delta-v, because fuel is not modelled yet. When it is,
  this planner becomes the feasibility check and a shooting or indirect solver
  takes over the optimisation.
* The ceiling buys flyability by **moving the arrival orbit out**, not by
  making the capture cheaper. Earth arrival is at 3,611 km altitude and Saturn's
  parking orbit is inside the ring system the catalogue does not model. A
  transfer conic now flies the same arrival from a lower, more natural orbit for
  less delta-v, at the cost of months of coasting
  ([ADR-014](adr-014-keplerian-coast.md)).
* A capture arc cannot command less thrust than the parking orbit's own gravity,
  so the ceiling is a hard policy constraint for *this* arc rather than a tuning
  value: any future planner must satisfy it or report the arrival infeasible.
  The conic arrival removes the bound because its coast has `a_ref = g`
  pointwise and its burns are impulses; it parks Earth at 7,327 km and Jupiter
  at 80,398 km. The quintic capture remains the working default, so the ceiling
  still applies to it.
* The plan is a curve fixed in time. If the ship is knocked off it, the
  autopilot tracks back, but a large deviation needs a re-plan.

# Alternatives considered

**Direct shooting on the integrated dynamics.** The eventual answer for fuel
optimality. Rejected for the first cut because convergence is not guaranteed and
the endpoint is only as good as the solver's tolerance.

**Kinematic brachistochrone plus a separate capture burn.** Simple, and correct
as an *estimate*, but the arrival velocity does not match the target's, so it
cannot end in a stable orbit without a second solver anyway.

**Patched conics with impulsive burns.** Cheap and familiar, but the premise is
a continuous torch, and the player would see the seams.

**A bang-bang profile with smoothed corners.** Closer to optimal in time, but it
needs a root-find for the switch times on a moving target, and the arrival
velocity still has to be matched separately.

# Revisit when

* fuel and delta-v become real, so time is no longer the only cost,
* the outer giants need a lower and cheaper arrival than the ceiling allows,
  which is the transfer-conic work now in
  [ADR-014](adr-014-keplerian-coast.md),
* the player needs to plan a mid-course correction rather than a fresh Go To,
* the cruise needs to use a gravity assist, which no single arc can express.
