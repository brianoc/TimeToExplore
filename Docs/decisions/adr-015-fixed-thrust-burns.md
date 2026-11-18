---
type: Decision Record
title: "ADR-015: Fixed-thrust burns and the hybrid capture"
description: Fly one long fixed-thrust capture burn at a player-set level, then use an impulse trim for exactness, so the parking radius stays at the conic value instead of rising to pay for the finite-burn loss.
tags: [decision, simulation, navigation, propulsion, trajectory]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-18T00:00:00Z }
---

# Context

The conic arrival ([ADR-014](adr-014-keplerian-coast.md)) models its two burns
as instantaneous impulses. That made the delta-v honest and the arrival exact,
but the drive is a throttleable torch: a real capture is a finite burn at some
fixed acceleration. An impulse is a useful planning idealisation, not what the
ship can fly.

The first fixed-thrust style flew both impulses as constant 0.3 g burns and
parked at `1.4 R` to pay for the periapsis loss. That was a working texture, but
it coupled two decisions that should be separate. A finite burn cannot hold the
impulsive style's low periapsis, so the orbit was raised to keep the post-burn
path clear. The player also had no level: the style was 0.3 g or nothing. The
result was a fixed-thrust arrival that always parked higher than the conic one,
even when the player would rather have the low orbit and spend the delta-v.

The design wants a **hybrid**: the fixed burn does the long part of the capture,
and a terminal impulse corrects the burn's end state onto the exact transfer
ellipse. The impulse is the price of exactness, and it decouples the parking
radius from the finite-burn loss.

# Decision

1. **One fixed burn, one trim.** `CaptureStyle.FiniteBurn` is the conic arrival's
   inbound hyperbola, a single retrograde finite burn at the chosen acceleration,
   an impulsive trim at the injection point, the conic transfer ellipse, and the
   conic circularisation. The throttle is zero on a coast and exactly the level
   on the burn, with no ramp.

2. **The level is data.** `TripPlanner.BurnLevelG` is a player-facing value in
   `[0, 1 g]`, clamped to the drive's own thrust margin
   (`MaxAccelerationMps2 * ThrustMargin`). `0 g` selects the pure conic plan and
   has no finite-burn arc; the nav computer labels the control `0.0 g burn`.
   The default is `0.3 g`.

3. **The injection point is a radius.** `HybridCapture` starts at the conic
   capture periapsis, `max(1.05 R, R + 100 km)`, and the fixed burn ends at its
   first crossing. The solver raises the radius in 2% steps when no safe burn
   reaches the nominal one. The whole burn is checked against the 100 km
   clearance, not only its end point.

4. **The trim makes the transfer exact.** At the burn's end state, the transfer
   ellipse has periapsis `r_i` and apoapsis the parking radius, so
   `a_transfer = (r_i + r_park) / 2`. The trim is
   `v_transfer(r_i) − v_end`; it cancels the burn's radial velocity and sets the
   tangential speed. The start offset is searched and the split with the smallest
   trim wins: a burn that starts too early over-brakes, and a burn that starts too
   late leaves too much tangential speed.

5. **Keep the conic parking radius.** `ParkingOrbit.ConicRadiusM` is
   `max(1.15 R, R + 300 km)`, Hill-capped, for the conic and hybrid styles. The
   level no longer forces `1.4 R`. The transfer and circularisation are the conic
   machinery, so the circularisation is an impulse as well as the trim.

6. **The burn is a real plan arc.** `FiniteBurnArc` integrates constant thrust
   along the instantaneous velocity through the two-body field with RK4 and
   interpolates a time-uniform local table with a cubic Hermite. Position and
   velocity are continuous at the coast seams and acceleration steps by exactly
   the burn level. `FiniteBurnArc.SimulateUntilRadius` is the position-conditioned
   solver step: it integrates until the radius first reaches the injection point
   and interpolates the crossing.

7. **Expose the level beside the styles.** The details footer keeps the three
   arrival buttons and adds a **burn-level** slider from 0 to 1 g. Changing the
   level rebuilds the preview from the pinned departure state exactly as the
   style switch does, in 0.05 g steps so a drag does not re-solve every pixel.
   The `Burns` row reads the fixed delta-v, the trim delta-v, and the g level; a
   live flight keeps the plan it was begun with.

# Consequences

**Positive**

* The parking radius is the conic one at every level: Mars 3,898 km, Earth
  7,327, Venus 6,960, Jupiter 80,398, Saturn 66,967, the Moon 2,037. Jupiter's
  hybrid parks **17,477 km lower** than the old fixed style and still arrives.
* The arrival is exact. Measured from Gateway at the epoch, the hybrid flights
  arrive 0.0–0.8 m and 0.0000–0.0031 m/s from the reference state and hold
  eccentricities of 2 × 10⁻⁸ to 1.2 × 10⁻⁷ after three orbits.
* The capture is much cheaper than the quintic capture: 4.57 km/s at Mars
  (quintic 84.8), 4.80 at Venus (86.0), 4.92 at Earth (218.3), 18.44 at Jupiter
  (1,088.6), 12.06 at Saturn (1,159.5), 5.07 at the Moon (35).
* The level has a visible meaning. At Mars the fixed burn is 15.3 min at 0.3 g
  and the trim is 1.79 km/s; at 1.0 g the burn is 5.5 min and the trim is
  0.82 km/s. The split is reported, so the player can see what the level buys.
* `0 g` is exactly the conic plan: the same arcs, arrival date, and delta-v. The
  style therefore has a continuous off switch rather than a second code path.

**Negative**

* **The trim is an impulse.** The plan is a hybrid, not a fully flyable burn:
  the terminal correction and the circularisation are instantaneous. If every
  burn must be finite, the trim becomes a short finite burn with a residual or a
  targeting iteration.
* **The fixed share varies by body.** The fixed burn is about 59% of the Mars
  capture at 0.3 g, but only about 18% at Jupiter: the deep well gives the burn
  a large radial velocity to cancel, and the trim does most of the work. The
  hybrid is still cheaper and lower than the old 1.4 R finite style (Jupiter
  18.44 against 19.68), but "the fixed burn does most of the capture" is a Mars
  and Moon statement, not a universal one.
* **The finite-burn loss remains.** The hybrid spends more than the pure impulse
  capture (Mars 4.57 against 3.53). The loss is the price of flying the burn.
* **The departure and cruise still blend.** They are powered quintics; a
  fixed-thrust cruise needs the Lambert and shooting solve, which is the deferred
  burn-and-coast work.

# Alternatives considered

**Keep the single 0.3 g style and park at 1.4 R.** The first implementation.
Simple, but the level was not a player choice and the parking radius was coupled
to the burn. It also parked Jupiter at 97,875 km to keep a 70,577 km periapsis,
which the hybrid no longer needs.

**Fly the circularisation as a second finite burn.** The first implementation
did. It leaves a slightly rounder orbit, but it needs a second burn window and
the handoff's hybrid explicitly returns the transfer and circularisation to the
conic machinery. The impulsive circularisation is cheap (80 m/s at Mars, 0.91
km/s at Jupiter) and keeps the style's one long burn readable.

**Burn until the path's periapsis is the injection point.** That makes the trim
purely tangential, but the finite burn lowers the periapsis below the body at
0.3 g on every major target. Stopping at a safe radius and cancelling the radial
velocity is what keeps the orbit low.

**Use the drive's full 1 g always.** The burn is shorter and the trim is smaller,
but the player loses the texture choice. The level is now the control.

# Revisit when

* the fuel model charges for burn windows, so the fixed burn's duration and the
  impulse trim have different costs;
* the Lambert cruise exists, so the departure and middle of the trip can use the
  same fixed-thrust texture;
* rings, atmospheres, or radiation make the low hybrid periapsis a gameplay
  problem rather than a number in a test;
* the trim's radial loss is modelled as a short finite burn, so the whole
  capture is flyable.
