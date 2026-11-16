---
type: Design Document
title: Navigation and arrival
description: How the player plans a moving-target intercept, crosses a sphere of influence, inserts into orbit, and docks.
tags: [simulation, navigation, orbital-insertion, docking]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# The problem

The destination is not where it was when you planned the trip. It has been
falling around the Sun the entire time. A transfer that points at "Mars, now" is
a transfer to empty space.

Navigation is therefore a **moving-target rendezvous**: choose a departure time,
a thrust profile, and an arrival time such that the ship and the target occupy
the same place at the same time—and, for a useful arrival, have the same
velocity.

# Planning pipeline

The navigation computer has four layers, in increasing fidelity:

1. **Porkchop search.** Sample departure and arrival dates. For each pair,
   estimate the delta-v with a Lambert solution (coast) or a brachistochrone
   estimate (torch). Show a heat map of time versus cost.
2. **Trajectory solve.** Refine the chosen pair with a reference trajectory that
   matches the ship and the target.
3. **Plan display.** Draw the predicted path, burn windows, flip point, closest
   approach, and delta-v remaining.
4. **Execution and correction.** Fly the plan with an autopilot, re-solve
   periodically, and hand control to the player for arrival.

**The trajectory solve exists for the torch case.** `TripPlanner` builds a
complete plan, `Autopilot` flies it, and the EditMode suite checks that the ship
arrives on the parking orbit and stays there. In the scene, `FlightSession` owns
the plan and advances the ship each frame, so pressing **Go To** flies it for
real ([The flight session](../technical/flight-session.md)). The porkchop search
is not implemented: Go To plans "leave now" and takes the first feasible trip.
Coasting (Lambert) transfers are not implemented either.

The rest of this document describes what the solver does, then the general
problem it is approximating.

# The implemented plan

A Go To plan is a chain of **quintic reference arcs**, each matching position,
velocity, and acceleration at both ends:

```text
t0 ── departure ──► SOI exit ──── cruise ────► SOI entry ── capture ──► t_f
     planetocentric              heliocentric               planetocentric
```

* **Departure** climbs out of the departure body's sphere of influence along the
  ship's own relative motion.
* **Cruise** crosses heliocentric space from that exit to the target's sphere of
  influence.
* **Capture** drops from the sphere of influence into the parking orbit.
* A trip that starts inside the target's sphere of influence is a single
  capture arc. Gateway to the Moon is one arc of 1.6 hours.

The endpoint accelerations are the local gravity, so the commanded thrust is
zero at every seam and at the end of the plan. The commanded thrust at any
instant is the curve's acceleration minus the local gravity; gravity is
feed-forward, and the curve *is* the trajectory.

Time is allocated between the arcs in proportion to each arc's kinematic minimum
2√(d/a), then grown until the measured peak thrust is inside the drive. The
arrival date is a fixed point, because the target keeps moving while the plan is
being solved. The plan finishes at the parking orbit's position and velocity at
the instant the last arc ends, so arrival is exact rather than approximate.

The decisions and their trade-offs are in
[ADR-006](../decisions/adr-006-quintic-plan.md). The measured flight times are
in the [flight model](flight-model.md#measured-plans).

## The parking orbit

The arrival state is a **circular orbit in the target's equatorial plane** at

```text
r_park = max( 1.15 R , R + 300 km , sqrt(GM / (0.4 a_max)) )
```

capped at half the Hill radius. The third term is the **gravity ceiling** and it
is new: a quintic capture arc cannot command less thrust than the parking
orbit's own gravity at its endpoint, so the arrival orbit must not sit in
gravity anywhere near the drive. See [the deep-well section](#the-deep-well-limit)
for why.

The ceiling binds only where surface gravity is above 4 m/s², because
`√(GM / (0.4 × 10))` is inside the default radius everywhere else. That is Earth,
Venus, and the four giants:

| Body | Parking radius | Altitude | Gravity there | Why |
|---|---:|---:|---:|---|
| Moon | 2,037 km | 300 km | 1.2 m/s² | default |
| Mars | 3,898 km | 508 km | 2.8 m/s² | default |
| Venus | 9,012 km | 2,960 km | 4.0 m/s² | ceiling |
| Earth | 9,983 km | 3,611 km | 4.0 m/s² | ceiling |
| Uranus | 38,059 km | 12,697 km | 4.0 m/s² | ceiling |
| Neptune | 41,342 km | 16,720 km | 4.0 m/s² | ceiling |
| Saturn | 97,380 km | 39,148 km | 4.0 m/s² | ceiling |
| Jupiter | 177,965 km | 108,054 km | 4.0 m/s² | ceiling |

Two consequences worth noting. Arrival orbits around the big bodies are now
high: Earth arrival is at 3,611 km altitude rather than 955 km, and Saturn's
parking orbit at 97,380 km lies inside the ring system. The catalogue does not
model rings, so nothing collides; a ring-plane model would have to move Saturn's
arrival out to the A-ring gap or beyond.

The orbit is prograde about the body's IAU north pole, and the phase is chosen so
the arrival point is on the side the ship approaches from.

"Stable" is measured in the real field, not a two-body ideal: the ship is left
in free fall with the drive off, and the tests check that the eccentricity stays
small over three orbits. Around Mars the eccentricity is about 3 × 10⁻⁶ after
three orbits; around Jupiter it is 2 × 10⁻⁵.

## The deep-well limit

The reason a deep well used to be unreachable is subtler than "the gravity is
stronger than the drive", and the argument is what sets the ceiling.

At Jupiter's surface-radius parking orbit, 80,398 km, gravity is **19.6 m/s²**
and the circular speed is 39.7 km/s. The *burn* is affordable: a 5 km/s inbound
approach captured directly into that orbit is about 16.7 km/s, or 28 minutes at
10 m/s². The ship can fly it. The **reference curve** cannot describe it.

The plan's endpoint condition is that the curve's acceleration equals local
gravity at the parking orbit, because that is what makes the arrival a free-fall
orbit with the drive off. A cubic-in-parameter acceleration cannot jump to that
value at the end; it ramps into it over the last few percent of the arc. The arc
spans 600 parking radii, so that ramp covers millions of kilometres where the
real gravity is still far weaker than at the parking point. The drive has to
supply the difference, and because the ramp ends at `g_park`, the peak thrust is
**bounded below by `g_park` itself**.

Measured on the Jupiter capture arc at the *old* 80,398 km orbit, varying only
its duration:

| Duration | Shape term 5.77 d/T² | Peak thrust |
|---:|---:|---:|
| 12 h | 148.9 | 151.5 |
| 24 h | 37.2 | 41.1 |
| 48 h | 9.3 | 19.6 |
| 4 d | 2.3 | 19.6 |
| 10 d | 0.4 | 19.6 |
| 30 d | 0.0 | 19.6 |

More time removes the geometric term and stops helping: 19.6 is `g_park`.
Starting the capture closer in does not help either, because the endpoint is
still the parking orbit. Shortening the approach to the point where local
gravity is half the drive still measured 16.96 m/s².

The boundary is therefore the parking orbit's gravity against the drive, not the
size of the sphere of influence. Saturn's sphere of influence is *larger* than
Jupiter's relative to its parking orbit (814 versus 600 radii), and it was
already flyable at 8.5 m/s² while Jupiter's 19.6 m/s² was not.

**The implemented fix is the gravity ceiling.** Parking where `GM / r²` is 40% of
the drive puts Jupiter at 177,965 km, where the capture arc's best measured peak
is **7.95 m/s²** — comfortable for a 10 m/s² drive, and the only reason the
radius is chosen that far out. The full plan is now feasible:

| Target | Plan | Peak thrust | Delta-v | Parking radius |
|---|---:|---:|---:|---:|
| Venus | 2.85 d | 8.4 m/s² | 1,171 km/s | 9,012 km |
| Mars | 4.44 d | 9.8 m/s² | 2,345 km/s | 3,898 km |
| Saturn | 12.9 d | 9.8 m/s² | 6,515 km/s | 97,380 km |
| Jupiter | 10.3 d | 9.8 m/s² | 5,189 km/s | 177,965 km |

Two refinements remain, and they are real ones rather than policy:

* **Use a transfer conic for the final approach.** A coasting arc has
  `a_ref = g` *pointwise*, so the commanded thrust is exactly zero and the
  endpoint problem disappears entirely; a short post-quintic burn at periapsis
  then does what the Oberth effect wants. This is the delta-v-optimal arrival and
  the physically right answer. It needs a state-vector Kepler propagator and a
  new arc type.
* **Split the capture into a shaped spiral** of progressively smaller circular
  orbits. Prototyped and rejected for now: radially aligned waypoints force the
  ship to cancel its tangential velocity at every waypoint, and the whole Mars
  plan stretched from 4.4 to 17.5 days.

The ceiling is a policy, not a law of physics: it chooses where the ship parks so
the planner's curve can describe the arrival. The transfer conic is how a real
ship would arrive cheaply, and it stays on the list.

# Moving-target brachistochrone

The kinematic estimate seeds the solver. Assume constant 1 g, a flip at the
midpoint, and a target moving on a known ephemeris:

```text
repeat:
    r_target = ephemeris(t_arrival)
    d        = |r_target − r_ship|
    t_flight = 2 sqrt(d / a)
    t_arrival = t_depart + t_flight
until t_flight converges
```

This converges quickly for slow-moving targets, but it solves only *position*.
The refined problem is a two-point boundary value problem:

* initial state `(r₀, v₀)` at departure,
* final position `r_t(t_f)` and velocity `v_t(t_f)` at arrival,
* bounded thrust `|a| ≤ 10 m/s²`,
* minimise propellant, or minimise time, or hit a fixed arrival date.

There is no closed form. Two practical answers exist:

* a **reference arc** that satisfies the boundary conditions exactly, with the
  thrust computed as the acceleration the arc needs minus gravity — what the
  prototype does;
* a **direct shooting method** that integrates the ship under thrust plus
  gravity, measures the endpoint error, and adjusts a segmented thrust profile
  with a least-squares step. Slower to converge, but it can minimise fuel.

The reference arc is deterministic and exact; the shooting method is what the
fuel-optimal planner will need. The kinematic brachistochrone is a good initial
guess for either.

# Burn-and-coast: Lambert

For the cheap regime, most of the flight is a Keplerian coast. The classic
**Lambert problem** takes two position vectors and a time of flight and returns
the connecting orbit's velocities:

```text
Lambert(r₁, r₂, Δt, μ) → (v₁, v₂)
```

Then:

* departure burn = `v₁ − v_ship`,
* arrival burn = `v_target − v₂`,
* the coast is propagated with Kepler's equation.

Lambert solutions are fast enough to evaluate thousands of times for a porkchop
plot. They are also the right tool for a ballistic transfer with a short
departure burn and a short capture burn—the "cheap but slow" option in the
[flight model](flight-model.md).

A hybrid plan is common: a high-thrust departure burn, a Lambert coast, and a
high-thrust capture burn. The ship spends most of its delta-v at the two ends,
where the Oberth effect makes it most effective.

# Spheres of influence

Outside a planet's sphere of influence (SOI), the Sun dominates. Inside, the
planet dominates. The patched-conic approximation switches frames at the
boundary.

A good estimate:

```text
r_SOI ≈ a_planet (m_planet / m_Sun)^(2/5)
```

| Body | SOI radius |
|---|---:|
| Mercury | 112,000 km |
| Venus | 616,000 km |
| Earth | 924,000 km |
| Mars | 577,000 km |
| Jupiter | 48.2 million km |
| Saturn | 54.5 million km |
| Uranus | 51.8 million km |
| Neptune | 86.6 million km |
| Moon (Earth) | 66,200 km |

Earth's SOI is about 924,000 km—roughly 1.5 times the Moon's orbit radius.
The Moon is therefore inside Earth's SOI, and a lunar transfer is a
planetocentric problem, not a heliocentric one. This is why patched conics need
a hierarchy, not just one boundary.

The ship's dynamics do **not** switch frames. It feels the summed gravity of
every body at once ([ADR-005](../decisions/adr-005-continuous-gravity.md)), so
crossing a boundary changes nothing. The sphere of influence is a *planning*
device: it is where the cruise arc hands over to the capture arc, and it gives
the player a readable boundary to draw.

Despite that, the ship keeps its heliocentric state throughout. The local state
is `v_ship − v_body`, and the planner uses it to choose the arrival geometry.

# Orbital insertion

Arriving at a planet is not the same as entering orbit. The ship has some
hyperbolic excess velocity `v∞` relative to the planet. To capture, it must shed
enough energy:

```text
v_hyp = sqrt(v∞² + 2μ/r)
v_circ = sqrt(μ/r)
Δv_capture ≈ v_hyp − v_circ        (at the same radius, circular target)
```

A capture burn at low periapsis is cheapest because of the Oberth effect: the
same delta-v buys more energy change deep in the gravity well. The planner should
prefer a low periapsis, then raise the orbit at apoapsis.

The prototype's capture is a continuous-thrust arc rather than a periapsis
burn: it enters the sphere of influence at 5 km/s inbound and shapes a smooth
deceleration into the parking orbit, so the drive is off at the moment the plan
ends. The periapsis-burn arithmetic above is still the reference for how much
delta-v a capture costs, and it is what a transfer-conic capture would
optimise. Three arrival styles:

* **Propulsive capture.** Burn at periapsis into an ellipse, then circularize.
  Reliable, expensive.
* **Aerocapture / aerobraking.** Use the atmosphere to shed velocity. Cheap in
  delta-v, expensive in heat shield, timing, and risk. Available at Venus, Earth,
  Mars, and Titan; not at airless bodies.
* **Direct descent.** For a surface destination, skip orbit entirely and land.
  The most demanding option, and the one that makes a heat shield a gameplay
  object.

The prototype's term "capture" covers the whole sequence into the parking orbit:
the arrival is not split into periapsis and circularisation burns, and the plane
is the target's equator rather than a chosen approach plane. A future version
should model the phases separately because each has a different failure mode.

# Docking

Docking is the final geometry problem:

1. **Plane match.** Be in the station's orbital plane, or close enough to correct.
2. **Phasing.** Arrive behind or ahead of the station and close the along-track
   gap over several orbits.
3. **Approach corridor.** Enter a cone aligned with the docking port.
4. **Relative navigation.** Hold a closing rate of a few metres per second,
   then centimetres per second.
5. **Hard mate.** Align attitude, contact, and latch.

At 1 g, the main engine is far too powerful for the final metres. The ship needs
RCS thrusters with fine throttle authority and a docking autopilot. The player's
job is to authorize the approach, manage closing velocity, and take over if the
autopilot is unhappy.

Docking is where the scale of the game becomes intimate: a 2 km station cube is
a marker from 300,000 km away and a structure filling the view at 100 m.

# Autopilot and player agency

The intended split:

| Level | Player does | Computer does |
|---|---|---|
| Manual | Attitude, throttle, RCS | Nothing but stabilization |
| Assisted | Approves burn, sets throttle | Holds attitude, executes burn, cuts off |
| Planned | Chooses departure/arrival, budget | Solves trajectory, flies it, corrects |
| Automatic | Sets a destination | Does the whole trip, reports anomalies |

The default should be **Planned**, because the interesting decisions are route
and budget, not stick-and-rudder for six days. Manual override must always be
available, and arrival should hand back control early enough to matter.

**Where the prototype is:** **Automatic**. Pressing Go To plans the trip, raises
the warp, flies it, and leaves the ship in the parking orbit; the player watches
and can cancel. Manual override is not available yet — the debug free flight is a
teleport, not a hand-flown ship. Milestone 3 (fuel) is where a manual burn starts
to matter, because it is the first decision the autopilot cannot make for the
player.

An autopilot that can do everything risks turning the game into a spreadsheet.
An autopilot that can do nothing makes 15-day burns unplayable. The compromise
is that the autopilot is good at the long, boring parts and deliberately
conservative near bodies, where the player takes over.

# Navigation aids

The player needs to see:

* the predicted trajectory, with a dashed line through coast arcs and a solid
  line through burns,
* the target's position at arrival, not at the current time,
* closest approach and relative velocity,
* burn start/end and flip countdown,
* delta-v remaining versus delta-v planned,
* the SOI boundary of the target,
* a capture/insertion solution and its cost,
* a docking corridor and closing-rate indicator.

Without these, true scale is just a black screen with dots. The HUD is part of
the simulation, not decoration.

**Implemented:** the sampled trajectory as a solid line for departure and cruise
and a dashed line for the capture arc, an arrival marker at the parking-orbit
point, a flip marker where the along-track thrust changes sign, a countdown to
the next arc boundary, a phase table with each leg's start and duration, the
arrival date, peak thrust, and planned delta-v, and the live throttle. The warp
policy keeps the final approach visible.

**Not implemented:** the target's position at arrival as a separate ghost, the
closest-approach readout, the SOI boundary drawn in the world, delta-v remaining
against a budget (there is no budget yet), and the docking corridor.

# Failure and rescue

The design wants failure to create stories, not reloads:

* **Missed capture.** The ship passes the planet and is now on a heliocentric
  orbit. The player can replan, wait for another encounter, or call for rescue.
* **Fuel exhaustion.** A rescue tug brings propellant, for a price or a favour.
* **Docking collision.** Damage, not destruction, if the closing rate is low.
  A hard hit is a failure state.
* **Warp overshoot.** Prevented by automatic warp drop near bodies and burns,
  and by clamping the clock to the ship when the frame's substep budget runs out.

# Open questions

* **Settled:** the ship flies through a continuous gravity field, not patched
  conics. Spheres of influence are planning waypoints.
* **Settled:** the torch plan is a chain of quintic reference arcs, flown by an
  autopilot with light feedback. Shooting methods are kept for the fuel-optimal
  planner.
* How forgiving is the capture solver? The plan is exact, but a real missed burn
  is fatal; a game needs a margin and a clear warning. The planner currently
  reports infeasibility rather than flying a bad plan.
* Is there a "flight assist" that hides the orbital mechanics entirely for
  casual players, or is the orbital mechanics the point?
* How much time can pass during a close approach before the player notices?
  **Partly settled:** the warp policy caps a frame at 25% of the time to the
  nearest surface and 5% of the time to the next plan event, so the approach is
  paced rather than skipped. The drop is still in power-of-ten steps.
* When does a plan need re-solving? The autopilot corrects a few kilometres, but
  a large deviation or a long wait needs a fresh Go To. The scene currently
  re-plans only when the player presses Go To again.
* How should the porkchop search be presented, and does a "leave now" plan
  (which always exists at 1 g) make the heat map redundant?
