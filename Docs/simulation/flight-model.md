---
type: Design Document
title: Flight model
description: The 10 m/s² torch drive, brachistochrone travel times, delta-v budgets, and the two travel regimes.
tags: [simulation, flight, propulsion, delta-v]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# The one big assumption

The ship can sustain **10 m/s²**—one Earth gravity—for days at a time.

The intended fiction is **medium-future fusion**: a compact torch, in the spirit
of the Epstein drive in *The Expanse*, that a working ship can carry and
service. It is not a laboratory experiment. Its waste heat is coupled into the
reaction mass and leaves with the exhaust, so the ship does not need a
city-sized radiator farm. Fuel capacity, fuel storage, and burn rate are
balance values rather than a modelled reactor.

This is the setting's single admitted impossibility. Everything else in the
flight model follows from it with ordinary mechanics. The design question is not
"is this realistic?" but "what does it cost, and what does that cost make the
player decide?"

See [ADR-004](../decisions/adr-004-torch-acceleration.md).

# Thrust and acceleration

```text
F = m a
```

A 1,000-tonne ship at 10 m/s² needs 10 MN of thrust. That is roughly the thrust
of a Saturn V first stage, in a ship that has to run it for days. The drive is a
torch, not a chemical rocket.

The useful quantity for the player is not thrust. It is **delta-v**: how much
velocity change the ship can still buy.

# Brachistochrone travel

For a constant-acceleration trip, the fastest trajectory is accelerate to the
midpoint, flip, decelerate the rest of the way. Arrival velocity matches the
destination, so the ship can rendezvous rather than fly past.

For distance `d` and acceleration `a`:

```text
t        = 2 sqrt(d / a)        total burn time
v_peak   = sqrt(a d)            speed at the flip
delta-v  = 2 sqrt(a d)          total velocity change
```

At `a = 10 m/s²`, using typical separations:

| Route | Distance | Burn time | Delta-v | Peak speed |
|---|---:|---:|---:|---:|
| Earth–Moon | 384,000 km | 3.4 h | 124 km/s | 62 km/s |
| Earth–Mars | 0.52–2.2 AU | 2.1–4.2 d | 1,400–3,600 km/s | 700–1,800 km/s |
| Earth–Mars (0.78 AU) | 117 million km | 2.5 d | 2,160 km/s | 1,080 km/s |
| Earth–Jupiter | 4.2 AU | 5.8 d | 5,013 km/s | 2,507 km/s |
| Earth–Saturn | 8.5 AU | 8.3 d | 7,132 km/s | 3,566 km/s |
| Earth–Uranus | 18.2 AU | 12.1 d | 10,436 km/s | 5,218 km/s |
| Earth–Neptune | 29.1 AU | 15.3 d | 13,196 km/s | 6,598 km/s |

That table is the design's centre of gravity. At 1 g, the inner system is a
matter of days and the outer system is a matter of weeks. The distances are
enormous; the acceleration is also enormous; the two nearly cancel.

Those figures are the **ideal bang-bang** profile: full thrust, instant flip,
full thrust. The implemented plan flies a smooth arc instead and takes 35–75%
longer — 4.4 days to Mars against 3.3. See
[the flight model in code](#the-flight-model-in-code) below.

If those times feel too short for the intended pacing, acceleration is the
tuning knob. Time scales as `1/sqrt(a)`:

| Acceleration | Earth–Mars | Earth–Neptune |
|---:|---:|---:|
| 10 m/s² (1 g) | 2.5 d | 15.3 d |
| 1 m/s² (0.1 g) | 7.9 d | 48.2 d |
| 0.1 m/s² (0.01 g) | 25 d | 153 d |
| 0.01 m/s² | 79 d | 483 d |

# The flight model in code

The simulation lives in `Assets/Scripts/Simulation/` and is plain C#: no
scene, no `MonoBehaviour`, no Unity physics. That is what lets it be verified in
EditMode tests before the scene uses it.

## Gravity and integration

The ship is a double-precision state vector — heliocentric position, velocity,
Julian date, dry mass, propellant — integrated by fourth-order Runge-Kutta. Its
acceleration is the summed point-mass gravity of the catalogue bodies plus the
commanded thrust. The planets stay on their Keplerian rails; the ship never
perturbs them, and evaluating the field never mutates the model. The model, the
field, and the reasons are in
[ADR-005](../decisions/adr-005-continuous-gravity.md).

The step is bounded by the local dynamical time, about 1% of the orbital
timescale and 1% of the time to cross the local radius: minutes in heliocentric
cruise, about ten seconds in a parking orbit. The integration advances to a
Julian date, not a frame, so the result does not depend on frame rate or time
warp.

## The plan

A **Go To** plan is three arcs — departure, cruise, capture — each a quintic
curve matching position, velocity, and acceleration at both ends. The commanded
thrust is the curve's acceleration minus the gravity the ship will feel there,
so the curve *is* the trajectory rather than a target the dynamics fight.
Endpoint accelerations are the local gravity, so the thrust is zero at the seams
and at the end of the plan. See
[ADR-006](../decisions/adr-006-quintic-plan.md) and
[navigation and arrival](navigation-and-arrival.md).

Because the throttle is the curve's magnitude, the accelerate and decelerate
phases are smooth ramps rather than a step at the flip. That is the price of the
time penalty against the ideal table above.

## Measured plans

At the epoch (2026-11-15, JD 2461359.5), from Gateway Station at the Earth–Moon
L1 point, with a 10 m/s² drive and no fuel limit:

| Target | Separation | Plan | Ideal | Peak thrust | Delta-v |
|---|---:|---:|---:|---:|---:|
| Moon | 55,000 km | 1.6 h | 2.0 h | 9.8 m/s² | 35 km/s |
| Venus | 0.33 AU | 2.9 d | 1.6 d | 8.4 m/s² | 1,171 km/s |
| Mars | 1.32 AU | 4.4 d | 3.3 d | 9.8 m/s² | 2,345 km/s |
| Jupiter | 5.28 AU | 10.3 d | 6.5 d | 9.8 m/s² | 5,189 km/s |
| Saturn | 8.68 AU | 12.9 d | 8.3 d | 9.8 m/s² | 6,515 km/s |

The Moon is the one case where the plan beats the ideal: the ship starts at
Gateway already closing on the Moon at about 1 km/s, so the "ideal" rest-to-rest
figures do not apply.

The Earth–Mars figure is longer than the 2.5-day table entry because the two
planets are 1.32 AU apart at this epoch, not 0.78 AU, and because the plan is
smooth. The capture into Mars orbit is 5.1 hours of the 4.4 days. Saturn's
capture is 52 hours of the 12.9 days, because Saturn's sphere of influence is
54 million km across.

Venus and the giants spend the extra time in the capture: their parking orbits
sit where the drive is a large fraction of local gravity, so the parking radius
is lifted until that gravity is 40% of the drive (see
[navigation and arrival](navigation-and-arrival.md#the-deep-well-limit)). The
higher orbit is easier to fly into, but it is further from the sphere-of-
influence entry, which lengthens the capture leg.

Jupiter is reachable, and this is the case the ceiling exists for. At the
surface-radius orbit the planet's gravity is 19.6 m/s² — twice the drive — and a
smooth capture arc cannot be flown there however long it is given. Lifting the
arrival to 177,965 km (2.5 radii) puts the local gravity at 4 m/s² and the whole
trip at 10.3 days with a 9.8 m/s² peak.

## Tuning the trip in the nav computer

The route preview has one **trip-time slider**. Its range is the fastest
feasible plan — the drive at its 97% margin — to three times that, and every
position is a fresh solve at that duration: the arcs are rebuilt, the arrival
date moves, and the target is met where the ephemeris puts it on that date.

The slider is one honest axis with two costs on it:

| Towards the left (fast) | Towards the right (slow) |
|---|---|
| Peak thrust rises towards 10 m/s² | Peak thrust falls roughly as `1/T²` |
| Delta-v rises roughly as `1/T` | Delta-v falls |
| Arrives sooner | Arrives later |

The current plans already sit at the left end, so there is no "burn harder"
above them. A faster trip needs a stronger drive (a data value) or the
burn-and-coast regime below. The slider therefore only stretches the existing
torch plan: the drive stays on, just more gently. It is **not** a coast. A coast
is a Keplerian conic, not a quintic with a flat middle, which is why it is
deferred behind the transfer-conic work
([navigation and arrival](navigation-and-arrival.md#burn-and-coast-lambert)).

## What the throttle is

The drive is **throttleable from zero to 10 m/s²**. The plan's acceleration
curve is the throttle setting, so the HUD can draw it and the future fuel model
can charge for its integral. Thrust direction is instantaneous: there is no
attitude dynamics or slew limit yet.

# Delta-v is the real currency

The rocket equation:

```text
delta-v = v_e ln(m_0 / m_f)
m_0 / m_f = exp(delta-v / v_e)
```

`v_e` is the effective exhaust velocity. The mass ratio is how much of the ship
must be propellant.

Fuel capacity and consumption are **gameplay balance values**, not a modelled
reactor. The maths below describes what a chosen delta-v budget buys; it does
not dictate a fixed tank size or a canonical ship design.

For a torch with `v_e = 10,000 km/s` (about 3.3% of light speed):

| Route | Delta-v | Mass ratio | Propellant fraction |
|---|---:|---:|---:|
| Earth–Moon | 124 km/s | 1.01 | 1% |
| Earth–Mars (0.78 AU) | 2,160 km/s | 1.24 | 19% |
| Earth–Jupiter | 5,013 km/s | 1.65 | 39% |
| Earth–Saturn | 7,132 km/s | 2.04 | 51% |
| Earth–Uranus | 10,436 km/s | 2.84 | 65% |
| Earth–Neptune | 13,196 km/s | 3.74 | 73% |

A 1,000-tonne ship that can brachistochrone to Neptune must be 730 tonnes
propellant at departure. That is a tanker, not a courier. With a 5,000 km/s
budget it can reach Jupiter one-way, Mars with a reserve, and the Moon
comfortably; the outer system needs depots or a different ship.

That is the intended progression: **the map opens up as fuel infrastructure
improves**, and the Lagrange stations are the infrastructure.

## What the numbers imply for propulsion

`v_e = 10,000 km/s` is far beyond chemical or ion propulsion. The jet power for
a 1,000-tonne ship at 1 g is:

```text
P = ½ F v_e = ½ × 10 MN × 10,000 km/s ≈ 50 TW
```

Fifty terawatts is civilization-scale. A 100-tonne ship still needs 5 TW. The
power source is the handwave, and the design should be honest about that rather
than pretending a chemical rocket can do it.

The canonical reading is a **compact fusion torch**:

* medium-future engineering, mature enough to be serviced on a working ship;
* roughly 3% of light speed in effective exhaust velocity;
* waste heat coupled into the reaction mass, so radiators are visual flavour
  rather than a thermal resource;
* no simulated reactor; fuel capacity and consumption are balance values.

Beamed power and lower acceleration remain coherent alternatives, but they are
not the intended setting. Lower acceleration would turn the inner system into a
weeks-long trip and survives only as a tuning option in the data. The flight
model is written so acceleration is a data value; changing it changes all
travel times without touching the code.

# Two travel regimes

The player should have a real choice, not a single optimal burn.

## Torch brachistochrone

Continuous 1 g, flip at midpoint, arrive matched. Fast, comfortable (1 g), and
enormously expensive in delta-v. This is the "go now" option.

Best for: urgent cargo, crew transfer, military/emergency response, short hops.

## Burn-and-coast

A high-thrust burn near departure, a long Keplerian coast, and a capture burn at
arrival. The coast is free; the burns are not. The trip takes months or years
and depends on launch windows, but costs a fraction of the delta-v.

Best for: bulk cargo, fuel tankers, station resupply, anything where the cargo
does not care about the calendar.

A middle option is a **low-thrust spiral**: continuous milligee acceleration,
efficient but slow. It is a good fit for automated freighters and gives the
world a background traffic pattern that is not player-controlled.

The navigation computer should present all three with time, delta-v, and arrival
date, so the choice is informed. See
[navigation and arrival](navigation-and-arrival.md).

**Implemented so far:** the torch plan and its trip-time control. Burn-and-coast
is not implemented, because the coast has to be a Keplerian conic with the drive
genuinely off; it depends on the transfer-conic arc, not on flattening a
quintic. The slider deliberately stops at the torch plan until that exists.

# Gravity assists and Oberth

At 1 g the ship's own thrust dominates solar gravity, so gravity assists are
mostly a cargo/efficiency tactic rather than a necessity. They still matter for:

* low-thrust freighters,
* ships that have run out of delta-v,
* scenic routes and mission design,
* the "free" velocity change at a planetary flyby.

The first prototype does not plan assists. The navigation model should be able
to use them once the trajectory integrator exists, because a grounded game
should not forbid the one manoeuvre that makes real spaceflight cheap.

# Crew physics

A 1 g burn is also 1 g of felt gravity. That is a neat coincidence:

* no rotating habitation section is needed during powered flight,
* the crew can eat, sleep, and work normally under thrust,
* the flip is a physical event the player can feel in the schedule,
* coasting is microgravity, with its own routines and constraints.

This is a better justification for 10 m/s² than "it is a nice round number": it
is the acceleration that keeps people healthy without artificial gravity.

# Relativity

The fastest midpoint speed in the catalogue is about 6,600 km/s, or 2.2% of
light speed, on a Neptune brachistochrone. The Lorentz factor is 1.00024; the
correction is far below the navigation error budget. Relativity can be ignored
without breaking the feel of the game. A future "relativistic" mode would need
a different time model entirely, and is out of scope.

# Failure modes the design wants

* **Stranded.** You spent too much delta-v and cannot brake at the destination.
  The correct response is a rescue contract, not a reload.
* **Missed window.** The cheap trajectory left without you. Go fast and expensive,
  or wait.
* **Wrong arrival.** You reach the planet with too much relative velocity and
  cannot capture. You fly past, and have to replan from the new orbit.
* **Fuel depot gambit.** You can reach Jupiter if you refuel at the Jovian
  Transfer Yard, but the station is at L4 and the detour costs time.

These are more interesting than a hull-integrity bar.

# Open questions

* Starter ship delta-v: 3,000, 5,000, or 8,000 km/s? This single number sets the
  size of the opening map.
* **Settled:** the drive is throttleable from zero to 10 m/s². The plan's
  acceleration curve is the throttle, and the HUD can draw it.
* Does fuel mass change handling and acceleration, or is acceleration held
  constant by throttling? The former is more honest; the latter is easier to fly.
  The current implementation assumes constant 10 m/s² and carries the mass as
  bookkeeping only.
* How much of the burn does the player manually fly? The implemented autopilot
  flies the whole plan and tracks it with light feedback; manual override and
  arrival hand-over are scene work.
* How visible should the drive be? Radiators and a glowing torch are good
  visuals, but they must not imply a thermal resource the simulation does not
  have.
* How much of the 35–75% gap to the ideal profile should be closed? A
  bang-bang planner with smoothed corners, or a fuel-optimal solve, would buy
  time at the cost of solver complexity. A transfer-conic capture would close
  part of it in delta-v rather than time.
