---
type: Design Document
title: Navigation and arrival
description: How the player plans a moving-target intercept, crosses a sphere of influence, inserts into orbit, and docks.
tags: [simulation, navigation, orbital-insertion, docking]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
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
3. **Plan display.** Draw the predicted path, burn windows, closest approach, and
   delta-v remaining.
4. **Execution and correction.** Fly the plan with an autopilot, re-solve
   periodically, and hand control to the player for arrival.

**The trajectory solve exists for every profile.** `TripPlanner` builds a complete
plan, `Autopilot` flies it, and the EditMode suite checks that the ship arrives on
the parking orbit and stays there. In the scene, `FlightSession` owns the plan and
advances the ship each frame, so plotting a route and pressing **Begin flight**
flies it for real ([The flight session](../technical/flight-session.md)). The
porkchop search is not implemented: a plan is "leave now" at the first feasible
trip.
A **transfer-conic arrival** is implemented as an opt-in planner style: it
coasts the inbound leg and captures impulsively at periapsis
([ADR-014](../decisions/adr-014-keplerian-coast.md)). The crossing is always the
**burn–coast–burn** profile: a zero-revolution prograde Lambert coast between a
departure fixed burn and a brake, with a planetocentric approach on the arrival
side. The nav computer's `ARRIVAL` buttons choose **Conic** or the hybrid
**X g burn** capture.

The rest of this document describes what the solver does, then the general
problem it is approximating.

# The implemented plan

A Go To plan is the **burn–coast–burn** chain:

```text
t0 ── break ──► departure burn ──► coast ──► approach ──► brake ──► capture ──► t_f
     planetocentric      heliocentric        planetocentric
```

* **Break** is a quintic that leaves the parking orbit and reaches the departure
  handover ceiling at escape speed.
* **Departure burn** is one prograde fixed burn at the chosen level; it holds
  the escape ray and hands over to the coast.
* **Coast** is a heliocentric zero-revolution Lambert conic with the drive off.
* **Approach** is a planetocentric conic from the target's handover ceiling to
  where the brake begins, present when the brake starts inside the ceiling.
* **Brake** is one retrograde fixed burn that ends on the capture chain's entry
  state.
* **Capture** is the conic or hybrid chain onto the parking orbit.
* A trip that starts inside the target's sphere of influence is a single
  quintic capture arc. Gateway to the Moon is one arc of 1.6 hours.

Every seam is continuous in position and velocity, and the plan finishes at the
parking orbit's position and velocity at the instant the last arc ends, so
arrival is exact rather than approximate. The profile's floor is found by
probing durations; the chosen plan is rebuilt with the full field and certified
before it leaves the planner.

The burn and capture decisions are in
[ADR-014](../decisions/adr-014-keplerian-coast.md) and
[ADR-015](../decisions/adr-015-fixed-thrust-burns.md). The measured flight times
are in the [flight model](flight-model.md#the-burncoastburn-profile-in-numbers).

The planner's arrival is a **conic capture** (`CaptureStyle.Conic`, the
default) or a **hybrid fixed-thrust capture** (`CaptureStyle.FiniteBurn`). Both
take over where the brake hands over at the target's handover floor. The conic
capture is a planetocentric hyperbola from that entry to a low periapsis, an
impulsive retrograde capture burn, a transfer ellipse, and a prograde
circularisation burn at apoapsis. On the two-body reference the coast's
acceleration is the local gravity pointwise, so the commanded thrust is zero;
the autopilot supplies only the small third-body correction. In the nav computer
it is the two-button `ARRIVAL` choice beside the trip-time slider — **Conic** or
the hybrid **X g burn**. Pressing one rebuilds the plotted preview against that
style and resets the slider to the new style's fastest plan, so the player sees
the coast, the periapsis and circularisation burns, the lower parking orbit, and
the flight time and delta-v that come with them. A chosen flight time survives
the switch when it fits the new window. A live flight keeps the plan it was
begun with. [ADR-014](../decisions/adr-014-keplerian-coast.md) records the
geometry and the impulse model.

A **hybrid fixed-thrust arrival style** (`CaptureStyle.FiniteBurn`) is the second
button: the same inbound hyperbola, transfer ellipse, and parking coast, but the
capture is one long finite burn at the player's chosen level, followed by an
impulse trim that puts the burn's end state exactly on the transfer ellipse. The
throttle is exactly zero on a coast and exactly the level on the burn; there is
no blend. The level runs from `0.05 g` to `1 g`, clamped to the drive's thrust
margin. The burn ends at an **injection radius** near the conic periapsis, where
the trim cancels the burn's radial velocity and sets the tangential speed. The
parking radius stays the conic `max(1.15 R, R + 300 km)`, so the level no longer
forces the higher `1.4 R` orbit. Measured captures are 4.57 km/s at Mars, 4.92
at Earth, 18.44 at Jupiter, and 12.06 at Saturn; the flights arrive 0.0–0.8 m
and 0.0000–0.0031 m/s from the reference state.
[ADR-015](../decisions/adr-015-fixed-thrust-burns.md) records the geometry and
the split.

The **crossing** is no longer a player choice. It is always burn–coast–burn: a
quintic break, a prograde departure fixed burn at the same level, a Lambert
coast, a planetocentric approach, and a brake onto the capture's entry state. The
old powered quintic cruise and the plain Lambert coast are retired; the coast is
the profile's private reference skeleton, not a flyable mode. The reference
coast retargets the quintic departure so its endpoint velocity at the departure
sphere of influence is the Lambert `v₁`; a heliocentric `ConicArc` then coasts
about the Sun to the capture chain's entry point with the drive off. The coast is
zero-revolution, prograde, short-way only: a trip time whose transfer sweeps
180° or more, whose plane contains the ecliptic pole, or whose short arc runs
clockwise about the pole has no coast, and the planner rejects it with the
geometry that failed rather than coasting the long way. The Mars→Earth return at
the epoch is a clockwise short arc, so it is refused.

The **burn–coast–burn** trip mixes all four ingredients: a quintic departure, a
fixed burn, a Lambert coast, a brake, and the conic or hybrid capture.
[Burn–coast–burn trips](burn-coast-burn.md) sets out the profile, the
speed/delta-v trade, and how it shipped. The two arrival styles remain capture
recipes; the crossing is the profile and is not a player choice.

The **burn–coast–burn** crossing inserts the two finite burns into a private
reference coast. The
quintic **break** ends on the departure clearance sphere, `1.02 × SOI`, at the
local escape speed along the escape ray, and the departure burn starts there.
The burn holds that ray — its prograde direction in the departure body's frame —
and its two ray angles, its duration, and the coast's start are one forward
solve against the Lambert coast; the burn's speed and the coast's departure
speed set its delta-v, so the level still scales its duration as `1/level`. The
old chain placed the burn by integrating it backwards from the coast's start, so
at fast trip times it began 4.7 SOI out at 32 km/s and the break had to be fitted
to that; the pin removes that backwards departure solve.

The arrival has one more leg than a heliocentric plan: **the heliocentric coast
ends on the target's handover ceiling** (`1.02 × SOI`, where the Sun dominates
and its conic is fair), a **planetocentric approach conic** falls from there to
where the brake begins, and then the brake is a fixed-thrust burn whose direction
is the change the chain needs rather than the velocity direction. The approach's
entry state at the ceiling is the coast's arrival state, so the seam is a state
and not a model change; it is built backwards from the brake's own start state,
so its duration is the free fall's. On a long trip it is days — 7.54 days at
Uranus 1 g and 400 days — and on a fast trip the brake starts outside the ceiling
and there is no approach leg at all. The brake is still integrated backwards from
the state the capture chain must begin at, and it spends the rest of the
handover, so the capture chain is handed its entry state with **no impulse**.
That entry is the **arrival handover floor** — `max(band floor, conic parking
radius)` — not the sphere of influence: the brake ends there and the chain's
inbound fall starts there. Measured at the epoch from Gateway to Uranus at 1 g
and 400 days, the chain enters at 38,643 km and falls for 31 minutes, against
117.13 days from the 51.76 million km sphere, and the profile's floor drops from
150.73 to **33.65 days in the editor** and **30.56 days in the .NET harness** on
the merged tree. Both are certified plans; the difference is the near-floor
scatter
([the runtime
divergence](/technical/planning-performance.md#the-runtime-divergence-is-a-property-not-a-bug)).
The editor's floor is the one plan that carries the approach arc: its brake
starts inside the ceiling. The ballistic approach cannot reach the floor itself —
its periapsis is 84,354 km at the 400-day plan — so the brake still spans the
last part of the descent
([the arrival leg](/simulation/powered-leg-floor.md#the-arrival-leg-a-planetocentric-approach)).
Every seam is continuous in position and velocity, and the plan arrives on the
parking orbit exactly. The brake's thrust direction is fixed along the change
the chain needs, because a burn along the instantaneous velocity can only change
the speed; the direction opposes the arrival velocity, and a trip time whose
coast arrives slower than the target's frame would need an acceleration rather
than a brake. (After the floor entry that branch did not trigger at Mars between
200 and 300 days: a 200-day crossing closes at 11.86 km/s — its last 27.3 hours
a planetocentric approach — and the slow end is the short-way coast's own
geometry, refused at 305 days for a 179.4° transfer.)
The departure burn begins on the departure handover band's ceiling, `1.02 × SOI`, and
every fixed burn is now gated on **body clearance**, not on the sphere of
influence: a burn that crosses the boundary while clearing both bodies is valid,
because a full-field burn integrates the summed gravity field
([full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md)).

Measured at the epoch from Gateway to Mars at 90 days and 0.3 g: a 4.84-day
quintic break (8.8 km/s, 0.03 m/s² peak), a 76-minute departure burn of
13.4 km/s, an 84.7-day coast ending on the 588,786 km ceiling, a 6.03-hour
planetocentric approach to 92,923 km, a 1.7-hour brake of 18.5 km/s, and the
3.5 km/s conic capture, 44.4 km/s in total against the coast plan's 14 km/s at
200 days.
The profile's true floor at the epoch is **6.73042 days** to Mars at 0.3 g
(1,171.6 km/s) and **3.21610 days** to Mercury at 1 g (1,765.5 km/s); the Mars
trip-time window ends at **20.19 days**, three times that floor, so the 90-day
plan above is a direct `PlanAtDuration` build outside the nav computer's window.
The capture enters at 6,033 km (the Mars 0.3 g floor) and falls for 13.6
minutes.
The 8-day Mercury flight arrives 0.05 m and 4.4 × 10⁻⁵ m/s from the reference
state and holds the parking orbit. The nav computer exposes the crossing as the
only route and the existing burn-level slider as the level both fixed burns are
planned at; its floor is 0.05 g, so there is no zero-thrust version.

## The trip-time control

The preview carries a trip-time slider whose range is the selected profile's
fastest feasible plan to the slowest one the planner accepts. Each value is a
build at an explicit duration: the arcs, the arrival date, and the intercept all
move, and the panel reports the new flight time, peak thrust, and delta-v. It is
a **duration** control, not a throttle cap, because `MaxAccelerationMps2` also
sets the parking-orbit radius; lowering it would silently move where the ship
parks.

The span starts at the profile's fastest plan and a has a real upper bound: the
prograde short-way Lambert transfer sweeps past 180° as the trip lengthens, and a
slow coast arrives slower than the target's frame, which would need an
acceleration rather than a brake. The right end is therefore the slowest duration
the planner still accepts, found by probing inside the three-times span. At the
epoch the window runs from **6.73 days to 20.19 days** to Mars. The right-hand
end is exactly three times the true floor, so a 90-day Mars build is outside the
control's reach; it can only be built directly with `PlanAtDuration`. The
slider's value line says what the drive is doing — `0.3 g burns · coast` —
instead of quoting a peak throttle for a plan that coasts.

Finding that window is the expensive part of a selection, so `FlightSession`
caches the floor plan and the window per departure date, target, burn level, and
arrival style; plotting the same route again at the same date is
free, and a changed date or a place/teleport clears the cache. Moving the handle
does not block the frame: the newest duration goes to one background worker,
which builds it with its own planner and gravity field; a build superseded by a
newer request is dropped, and the previous full-field preview stays on screen
until the finished plan is handed back on the main thread. The first plot,
the arrival-style change, and the burn-level change go through the same worker —
the route-planning freeze the player used to feel is gone, and the route
redraws when the plan is ready. Every plan the nav computer shows is the
full-field build — the planner's fast search model only chooses a duration.

The arrival buttons and the burn level remain the player's trade: the conic
capture is cheap and the hybrid capture is a real finite burn at the chosen
level, so the arrival is a time/delta-v choice the nav computer exposes as
fast-and-expensive against slow-and-cheap. There is no second crossing to
switch to, so the slider never pretends a different profile is a coast.

## The parking orbit

The arrival state is a **circular orbit in the target's equatorial plane** at

```text
r_park = max( 1.15 R , R + 300 km )
```

capped at half the Hill radius. That is `ParkingOrbit.ConicRadiusM`, the
profile's parking radius. The conic and hybrid captures use it because their
periapsis burn is impulsive and can fly a deep well; the hybrid's finite burn
pays its periapsis loss with the terminal trim, not a higher orbit.

The capture's periapsis is lower still,
`max(1.05 R, R + 100 km)`, which is where the Oberth burn happens.

| Body | Parking radius | Altitude | Why |
|---|---:|---:|---|
| Moon | 2,037 km | 300 km | default |
| Mars | 3,898 km | 508 km | default |
| Venus | 6,960 km | 908 km | default |
| Earth | 7,327 km | 955 km | default |
| Uranus | 29,166 km | 3,804 km | default |
| Neptune | 28,315 km | 3,693 km | default |
| Saturn | 66,967 km | 8,735 km | default |
| Jupiter | 80,398 km | 9,829 km | default |

The old all-torch plan used a higher **gravity ceiling**,
`sqrt(GM / (0.4 a_max))`, because a quintic capture must arrive with the parking
orbit's own gravity. That ceiling still applies to a trip that starts inside the
target's sphere of influence, which is a single quintic capture; the profile
uses the conic radius above.

The orbit is prograde about the body's IAU north pole, and the phase is chosen so
the arrival point is on the side the ship approaches from. Measured: Earth
7,327 km, Venus 6,960, Mars 3,898, Jupiter 80,398, Saturn 66,967, Moon 2,037.
Jupiter's hybrid burn ends at 73,407 km and the trim puts the transfer on an
80,398 km apoapsis; the whole capture was 18.44 km/s at 0.3 g against the old
1.4 R style's 19.68.

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

* **Use a transfer conic for the final approach.** *Implemented as an opt-in
  arrival style.* A coasting arc has `a_ref = g` *pointwise*, so the commanded
  thrust is exactly zero and the endpoint problem disappears entirely; a short
  post-quintic burn at periapsis then does what the Oberth effect wants. The
  conic chain is a hyperbola from the sphere of influence, a capture burn at
  `max(1.05 R, R + 100 km)`, a transfer ellipse, and a circularisation burn at
  the parking radius. Measured captures are 3.5–17.4 km/s against the quintic's
  84–1,159 km/s, and the parking orbit drops to 7,327 km at Earth, 80,398 km at
  Jupiter, and 66,967 km at Saturn. The cost is time: the outer coasts are
  months, which the profile turns into a coast between its burns. The conic
   capture is the default. See
  [ADR-014](../decisions/adr-014-keplerian-coast.md).
* **Split the capture into a shaped spiral** of progressively smaller circular
  orbits. Prototyped and rejected for now: radially aligned waypoints force the
  ship to cancel its tangential velocity at every waypoint, and the whole Mars
  plan stretched from 4.4 to 17.5 days.

The ceiling is a policy, not a law of physics: it chooses where the ship parks so
the planner's curve can describe the arrival. The transfer conic is how a real
ship would arrive cheaply, and it is now the planner's second arrival style.

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

**Implemented.** `LambertSolver.SolveShortWay` in Core answers the zero-rev,
prograde, short-way branch in universal variables, returning `v₁`, `v₂`, and the
swept transfer angle; a geometry outside that branch (a long way, a plane
through the ecliptic pole, or a 180° transfer whose classic `A` term vanishes) is
returned as `HasSolution == false` rather than thrown. The burn–coast–burn
planner uses it to choose the heliocentric `ConicArc` between the departure and
target, bookended by a prograde departure burn and a brake. The trip-time slider
is the speed control. A porkchop search is still absent: Go To
plans "leave now". A shaped quintic with a flat middle would look like a coast
but still command thrust, so it is not used.

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

The quintic capture used for a trip inside the target's sphere of influence is a
continuous-thrust arc rather than a periapsis burn: it shapes a smooth
deceleration into the parking orbit, so the drive is off at the moment the plan
ends. The periapsis-burn arithmetic above is the reference for how much
delta-v a capture costs, and it is what the conic style implements: the
hyperbola, the periapsis burn, the transfer ellipse, and the circularisation are
separate arcs and separate plan boundaries. Four arrival styles exist or are
planned:

* **Propulsive capture.** Burn at periapsis into an ellipse, then circularize.
  Reliable, expensive. *Implemented as the conic style's impulses and as the
  hybrid style's one finite burn plus trim; the quintic arcs are the
  continuous-thrust alternative.*
* **Aerocapture / aerobraking.** Use the atmosphere to shed velocity. Cheap in
  delta-v, expensive in heat shield, timing, and risk. Available at Venus, Earth,
  Mars, and Titan; not at airless bodies. *Not implemented.*
* **Direct descent.** For a surface destination, skip orbit entirely and land.
  The most demanding option, and the one that makes a heat shield a gameplay
  object. *Not implemented.*
* **Continuous-thrust capture.** A smooth quintic arc, constrained by the
  gravity ceiling. *Implemented for a trip that starts inside the target's
  sphere of influence; the profile's default capture is conic.*

The conic style keeps the target's equatorial plane rather than choosing an
approach plane. Its impulses are idealised as instantaneous; the hybrid style
flies the long capture as a finite burn at the player's level and keeps only the
terminal trim and circularisation impulsive. The fixed-thrust departure and
coast are the profile's own, so the remaining fidelity steps are attitude
dynamics, the fuel budget, and an impulsive-capture margin — not a missing
heliocentric coast.

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
* burn start and end countdown,
* delta-v remaining versus delta-v planned,
* the SOI boundary of the target,
* a capture/insertion solution and its cost,
* a docking corridor and closing-rate indicator.

Without these, true scale is just a black screen with dots. The HUD is part of
the simulation, not decoration.

**Implemented:** one coloured route line per arc by path type — gold solid for
a quintic, blue dashed for a conic, red solid for a fixed burn, with consecutive
same-type arcs taking darker or paler shades of the same hue — burn markers at
each impulsive periapsis or circularisation and at each fixed burn start, an
arrival marker at the parking-orbit point, a countdown to the next arc boundary,
a phase table with each leg's start, duration, and what the drive is doing, the
arrival date, planned delta-v and the burn total, **the fuel
readout** — the propellant a journey would burn and its share of the departure
mass, with no tank limit — the live throttle, the burn-level slider, the
trip-time slider, which re-solves the route and its arrival date as it moves,
and the two arrival-style buttons, which rebuild the preview as a cheap conic
capture or the hybrid fixed-thrust arrival. The profile replaces the
peak-throttle row with a `DRIVE` summary: the break, the coast with the drive
off, and the departure burn and brake with their own delta-v and durations. The
brake is reported by what it spends, not described as anti-velocity, because its
direction is the change the chain needs. The hybrid's long burn is a solid red
line with a marker at its start, the terminal trim marks the injection point,
and the `Burns` row reads the fixed and trim delta-v separately. The warp policy
keeps the final approach visible, and the periapsis and circularisation are plan
boundaries in every style; the hybrid's burn window is a boundary too.

**Not implemented:** the target's position at arrival as a separate ghost, the
closest-approach readout, the SOI boundary drawn in the world, delta-v remaining
against a budget (the `Fuel` row shows the propellant a journey would burn, but
there is no tank or budget yet), and the docking corridor.

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
* **Settled:** the plan is flown by an autopilot with light feedback. The old
  all-torch plan was a chain of quintic reference arcs and is retired; shooting
  methods are kept for the fuel-optimal planner.
* **Settled:** a transfer-conic capture is the default arrival: a coasted
  hyperbola, an impulsive Oberth capture, a transfer ellipse, and a
  circularisation burn. It is cheap and slow. The other style is the
  **hybrid**: one long fixed burn at a player-set level from 0.05 to 1 g, an
  impulse trim onto the exact transfer ellipse, and the conic circularisation.
  The nav computer exposes both in the `ARRIVAL` row above the trip-time slider
  and the level slider.
* **Settled:** the crossing is burn–coast–burn only. The powered quintic cruise
  and the unpowered Lambert coast are retired, and the profile's trip-time window
  is its own feasible range. Its right-hand end is the slowest duration the
  planner accepts, not `3 × minimum`.
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
