---
type: Decision Record
title: "ADR-015: Fixed-thrust burns, and a third arrival style"
description: Fly the conic arrival's impulses as finite 0.3 g burns on the same coasts, with a step in throttle and no blend, and park higher to pay for the finite-burn loss.
tags: [decision, simulation, navigation, propulsion, trajectory]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-18T00:00:00Z }
---

# Context

The conic arrival ([ADR-014](adr-014-keplerian-coast.md)) models its two burns
as instantaneous impulses. That made the delta-v honest and the arrival exact,
but the drive is a throttleable torch: a real capture is a finite burn at some
fixed acceleration, and at 0.3 g the Mars capture is about 20 minutes, Jupiter's
is about an hour and a half. An impulse is a useful planning idealisation, not
what the ship can fly.

The smooth quintic plan ramps its throttle from zero to the drive limit. The
design wanted the opposite texture: a burn is a fixed level, a coast is zero, and
the throttle steps between them rather than blending. The conic machinery gives
the coasts and the capture geometry to hang that on.

# Decision

1. **Add `CaptureStyle.FiniteBurn` as a third arrival style.** It is the conic
   arrival's inbound hyperbola and transfer ellipse, with the two impulses flown
   as finite burns. The departure and cruise are still the powered quintics: a
   fixed-thrust *cruise* is the burn-and-coast Lambert problem, which is still
   deferred.
2. **Use one fixed level, 0.3 g.** `TripPlanner.FixedBurnAccelerationMps2` is
   `0.3 × 9.80665 = 2.942 m/s²`, clamped to the drive's own thrust margin. The
   throttle is exactly zero on a coast and exactly that value on a burn; there is
   no ramp. The nav computer's third button is labelled with the level
   (`0.3 g burn`).
3. **Integrate the burn, do not shape it.** `FiniteBurnArc` integrates constant
   thrust along the instantaneous velocity through the body's two-body gravity
   with RK4, stores a time-uniform local table, and interpolates position with a
   cubic Hermite. Its parameter is linear in time. Position and velocity are
   continuous at a seam; acceleration steps by the burn level.
4. **Straddle periapsis.** A finite burn cannot sit at periapsis, so the capture
   burn starts before it and ends after it; the start offset and duration are
   solved so the post-burn orbit's apoapsis is the parking radius. The solver
   searches the offset and keeps the burn with the largest safe post-burn
   periapsis.
5. **Park higher, because the finite burn loses periapsis altitude.** A 0.3 g
   capture burn lowers the periapsis much more than an impulse. The finite style
   uses `max(1.4 R, R + 300 km)` for the parking radius and
   `max(1.2 R, R + 300 km)` for the capture periapsis, both Hill-capped, instead
   of the conic style's `1.15 R` and `1.05 R`. It still parks far below the
   quintic gravity ceiling: Jupiter at 97,875 km against 177,965 km.
6. **Centre the circularisation too.** The prograde burn is centred on the
   transfer apoapsis, and its duration is solved until the end speed is the local
   circular speed. Centring cancels most of the residual radial velocity; the
   measured eccentricities are 10⁻⁵ to 5 × 10⁻⁴, not the few-percent residual a
   burn that starts at the apsis would leave.
7. **Expose it beside the other two styles.** The nav computer's arrival row is
   **Quintic / Conic / 0.3 g burn**. Switching rebuilds the preview from the
   pinned departure state exactly as the conic switch does. The flight panel
   gains a `Burns` row with the finite burn delta-v and its g level, and the
   trajectory line draws finite burns solid with a marker at each burn start.

# Consequences

**Positive**

* The arrival now has the throttle texture the design asked for: 0 g coast,
  0.3 g burn, 0 g transfer, 0.3 g circularisation, 0 g parking. Nothing blends.
* The fixed burn is cheap in delta-v and parks low. Measured from Gateway at the
  epoch: Mars 3.65 km/s (quintic capture 84.8), Venus 4.21 (86.0), Earth 4.38
  (218.3), Jupiter 19.68 (1,088.6), Saturn 10.40 (1,159.5). The finite burn
  spends more than the impulse model (Mars 3.53, Jupiter 17.40) — that is the
  finite-burn loss, and it is the price of being able to fly the burn.
* The flights arrive exactly: 0.1–0.5 m and 0.0001–0.0006 m/s from the reference
  state, and hold the parking orbit with eccentricities of 10⁻⁵ to 5 × 10⁻⁴.
* The burn is a real plan arc, so the integrator, the warp policy, the
  trajectory line, and the autopilot all see its start and end as boundaries.

**Negative**

* **The cruise still blends.** The departure and cruise are powered quintics, so
  their throttle ramps. Only the arrival burns are fixed. A fixed-thrust cruise
  needs a Lambert solve to choose the heliocentric coast and a shooting solve to
  connect a finite departure burn to it; that is the deferred burn-and-coast
  work, not a new toggle.
* **The finite style parks higher than the impulsive conic**, because the burn
  cannot hold the low periapsis. Jupiter's post-burn periapsis is 70,577 km,
  only about 670 km above the cloud tops; Saturn's is 67,524 km. The catalogue
  does not model atmospheres or radiation belts, so nothing stops the ship, but
  a ring/atmosphere model would make the arrival a real choice.
* **The finite burn is slow.** Jupiter's capture burn is 94 minutes and its
  circularisation is 17 minutes; the impulsive style does the same in an
  instant. The capture geometry is solved for a safe orbit, not a minimum-time
  one.
* The burn level is fixed in code, not exposed as a slider. The player chooses
  the style, not the g level.

# Alternatives considered

**Keep the impulse model and only draw a burn window.** Simpler, but the plan's
delta-v would be a lie and the ship's real trajectory would not match the drawn
reference. The whole point of the arc is that the reference is what the drive
flies.

**Use the drive's full 1 g for the finite burn.** The burn is shorter and loses
less periapsis altitude, but it is no longer the 0.3 g texture the design asked
for, and it spends the same delta-v. 0.3 g is the working level; the property is
there if a later balance pass wants another.

**Start the capture burn at periapsis.** It is easier to solve but it turns the
burn point into the new apoapsis once the speed falls below circular, and the
post-burn orbit can drop into the body. Straddling periapsis is the geometry
that keeps the orbit clear.

**A full fixed-thrust burn-coast-burn cruise.** The right long-term answer, but
it is a Lambert plus shooting problem: two positions and a time give the coast,
and the finite burns have to be connected to it. That is a separate milestone,
not an arrival style.

# Revisit when

* the fuel model charges for burn windows rather than impulses, so the finite
  burn's duration is a real cost;
* the Lambert cruise exists, so the departure and middle of the trip can use the
  same fixed-thrust texture as the arrival;
* rings, atmospheres, or radiation make Jupiter's 670 km periapsis a gameplay
  problem rather than a number in a test;
* a player-facing burn-level control is worth the extra UI.
