---
type: Design Document
title: Flight model
description: The 10 m/s² torch drive, brachistochrone travel times, delta-v budgets, and the trip profiles.
tags: [simulation, flight, propulsion, delta-v]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
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

A **powered Go To plan** is three arcs — departure, cruise, capture — each a
quintic curve matching position, velocity, and acceleration at both ends. The
commanded thrust is the curve's acceleration minus the gravity the ship will feel
there, so the curve *is* the trajectory rather than a target the dynamics fight.
Endpoint accelerations are the local gravity, so the thrust is zero at the seams
and at the end of the plan. See
[ADR-006](../decisions/adr-006-quintic-plan.md) and
[navigation and arrival](navigation-and-arrival.md).

A **conic arrival** is the second style (`CaptureStyle.Conic`). The departure and
cruise are still powered quintics; the target leg is a planetocentric hyperbola
with `a_ref = g` pointwise, an impulsive periapsis capture, a transfer ellipse,
and a circularisation burn. The coast's commanded thrust is zero on the two-body
reference, and the autopilot supplies only the third-body tide. The burns are
impulses, so the plan's delta-v is the powered arcs' integrated thrust plus the
burn magnitudes. The style is opt-in because the coast from the sphere of
influence is long. [ADR-014](../decisions/adr-014-keplerian-coast.md) has the
geometry.

A **hybrid fixed-thrust arrival** is the third style (`CaptureStyle.FiniteBurn`).
It uses the same inbound hyperbola, transfer ellipse, and parking coast, but the
long capture is one fixed burn at a player-set level from 0 to 1 g (clamped to
the drive's thrust margin), followed by an impulse trim that puts the burn's end
state exactly on the transfer ellipse. The throttle is exactly zero on a coast
and exactly that level on the burn; the acceleration steps at the seam rather
than blending. `FiniteBurnArc` integrates the constant thrust through the
two-body field and tabulates the local state; `HybridCapture` solves the burn's
start offset, duration, and injection radius, and the trim. The parking radius
is the conic `max(1.15 R, R + 300 km)` at every level, so the level no longer
forces a higher orbit; the trim rather than a higher periapsis pays the
finite-burn loss. At 0 g the style is exactly the conic plan. Measured captures
are 4.57 km/s at Mars, 4.92 at Earth, 18.44 at Jupiter, and 12.06 at Saturn, and
the flights arrive 0.0–0.8 m and 0.0000–0.0031 m/s from the reference state.
The departure and cruise are still powered quintics: a fixed-thrust *cruise*
needs the Lambert solve. [ADR-015](../decisions/adr-015-fixed-thrust-burns.md)
records the decision and the split.

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

### The conic arrival in numbers

The same epoch and ship, with `ArrivalStyle = CaptureStyle.Conic`. The flight
time is dominated by the coast from the sphere of influence, so the outer
arrivals are months; the capture burn is the periapsis and circularisation
total, compared with the quintic capture arc's integrated thrust:

| Target | Conic plan | Conic capture | Quintic capture | Conic parking radius |
|---|---:|---:|---:|---:|
| Venus | 3.69 d | 4.13 km/s | 86.0 km/s | 6,960 km |
| Mars | 5.59 d | 3.53 km/s | 84.8 km/s | 3,898 km |
| Jupiter | 99.47 d | 17.40 km/s | 1,088.6 km/s | 80,398 km |
| Saturn | 131.46 d | 10.66 km/s | 1,159.5 km/s | 66,967 km |
| Earth (from Mars orbit) | 6.18 d | 4.28 km/s | 218.3 km/s | 7,327 km |

The conic flights arrive 0.1–1.1 m and 0.0001–0.003 m/s from the reference
state, and hold the lower parking orbit with eccentricities of 10⁻⁴ or less
after three orbits. The coast's measured peak thrust is below 0.01 m/s²: the
two-body reference is a true coast, and the drive only trims the third-body
tide. The total plan delta-v is usually lower (Jupiter 2,783 km/s against
5,189), but not always: a compressed cruise over a short conic capture can spend
slightly more than the quintic plan even while the capture itself is 20–100×
cheaper. That trade is why the style is a choice, not a replacement.

### The burn–coast–burn profile in numbers

The same epoch and ship, with `CruiseStyle.BurnCoastBurn` at 0.3 g and the conic
capture. The departure burn is about the Sun and the brake is about the Sun as
well: the reference body is a label, each burn integrates the summed field, and
the burns span thousands to millions of kilometres
([full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md)).
The measured plan at 90 days, after the capture started entering at the arrival
handover floor:

| Leg | Duration | Delta-v | Peak thrust |
|---|---:|---:|---:|
| Quintic break | 4.84 d | 8.8 km/s | 0.03 m/s² |
| Departure burn (prograde) | 76 min | 13.4 km/s | 2.94 m/s² |
| Lambert coast | 85.0 d | 0.05 km/s | 0.005 m/s² |
| Brake (fixed thrust) | 1.7 h | 18.5 km/s | 2.94 m/s² |
| Conic capture | 13.6 min | 3.53 km/s (impulse) | 0 |

The total is 44.4 km/s against the coast plan's 14 km/s at 200 days: the burns
buy the speed. The break ends on the departure clearance sphere (`1.02 × SOI`)
at the local escape speed, and the departure burn starts there and holds the
escape ray. The burns are sized from velocity changes, not from a time share. The
departure burn spends the coast's departure speed above the local escape speed,
so its delta-v is the same at every level — 13.4 km/s here — while its duration
scales inversely: 7.7 hours at 0.05 g, 76 minutes at 0.3 g, 23 minutes at 1 g.
The brake spends the handover the coast plan leaves as an impulse and ends at
the arrival handover floor (6,033 km at Mars 0.3 g), so the capture
chain is handed its entry state with no impulse and falls in for 13.6 minutes.
Every seam is continuous to under a metre and 3.7 × 10⁻⁴ m/s, the flight arrives
0.1 m and 0.0001 m/s from the reference state, and the parking orbit holds. At
`0 g` the style is exactly the coast plan: five arcs and the handover impulse.
At non-zero level the arrival adds a planetocentric approach leg; on a fast trip,
whose brake starts outside the target's ceiling, it is absent.

The profile's true floors at the epoch are **3.21610 days** to Mercury at 1 g
(peak 9.70 m/s², 1,766 km/s) and **6.73042 days** to Mars at 0.3 g
(5.14 m/s², 1,171.6 km/s); the Mars trip-time window ends at **20.19 days**,
three times the floor, so the 90-day plan above is a direct `PlanAtDuration`
build outside the nav computer's window. Jupiter at 0.3 g floors at
**13.07211 days** (window 13.07–39.22) and Uranus at **33.60 days** at 0.3 g and
**33.65 days** at 1 g (windows 33.60–100.81 and 33.65–100.96). The .NET harness
selects a different certified point at the giants (Uranus 40.71 and 30.56,
Jupiter 13.33520); the editor's is the one that ships
([the divergence](/technical/planning-performance.md#the-runtime-divergence-is-a-property-not-a-bug)).
Faster crossings are limited by the pinned
break reaching the drive's 10 m/s². The capture enters at the arrival handover
floor rather than the sphere of influence, so the inbound fall is minutes; that
is what moved the floors down from 3.46 and 8.03 days. The slow end is bounded
by the short-way coast's geometry, and a coast that arrives slower than the
target's frame would still need an acceleration rather than a brake. Venus at
11.66 days spends 183 km/s (departure 72, brake 58); Jupiter at 230.6 days
spends 119 km/s (departure 40, brake 53) after its 87-day inbound coast. A plan
the drive cannot fly carries an `InfeasibleReason` naming the arc and the peak it
needs.

The break's duration search is **two-tier**. It measures the quintic against the
departure body alone — the break's own central body — and rebuilds and measures
the final plan with the full field, so the reported peak, delta-v and clearance
stay full-field numbers. When the fast peak is within 5% of the search's drive
limit, the probe is certified with the full field; the certification prepares
the field once at the break's start and evaluates that set along the arc. The
quintic keeps full-field endpoint accelerations, so the search's curve is the
plan's curve.

The floor search uses the same split. Its growth, walk-back and bisection probes
measure fixed burns at their commanded acceleration, conics at zero, and the
break against its own body, with a 128-sample burn and a loose pinned fixed
point; the chosen duration is then rebuilt at 512 samples with the full field and
certified. Previewing the floor and the profile's window is cached in
`FlightSession` per departure date, target, burn level, cruise and arrival style,
so a repeat selection is free; a changed departure date drops the cache.

Measured on a throwaway copy at the epoch: one `Plan()` to Mercury at 1 g fell
from 36.2 s in the first cut to 12.0 s after the two-tier break search and to
**1.5 s** after the floor probe mode and the pinned convergence (Mars 0.3 g is
0.85 s); `PlanAtDuration(8 d)` fell from 183 ms to **95 ms**. The first
selection (floor, window probes, retime) is 1.5 s at Mercury and 0.93 s at Mars;
a repeat selection is under 0.1 ms. The plans, floors and seams are unchanged.
[Burn–coast–burn trips](burn-coast-burn.md) has the detail, and
[planning performance](/technical/planning-performance.md) has the two-tier
boundary, the 5% screen, and the background retime.

## Tuning the trip in the nav computer

The route preview has one **trip-time slider**. Its range starts at the fastest
feasible plan of the selected cruise profile, and every position is a fresh solve
at that duration: the arcs are rebuilt, the arrival date moves, and the target is
met where the ephemeris puts it on that date. The **Powered** profile keeps the
historical window to three times that minimum. **Coast** and
**Burn-coast-burn** have a real upper bound — the short-way Lambert transfer
sweeps past 180°, and a coast that arrives slower than the target's frame would
need an acceleration — so their right-hand end is the slowest duration the
planner still accepts, probed inside the same three-times span. At the epoch the
burn–coast–burn window runs from **6.73 days to 20.19 days** to Mars, so it
begins at the profile's true floor (6.73042 days at 0.3 g), not at the torch
plan's 4.4 days, and it ends at three times that floor. A 90-day Mars plan exists
as a direct `PlanAtDuration` build but the slider cannot reach it.

The slider is one honest axis with two costs on it:

| Towards the left (fast) | Towards the right (slow) |
|---|---|
| Peak thrust rises towards 10 m/s² | Peak thrust falls roughly as `1/T²` |
| Delta-v rises roughly as `1/T` | Delta-v falls |
| Arrives sooner | Arrives later |

The retime itself runs **off the main thread**. Moving the handle enqueues the
newest duration; one background worker builds it with its own `TripPlanner` and
`GravityField`, because the field keeps a prepared active set and the flight
simulation's instance stays on the main thread. A plan that finishes after a
newer request arrived is discarded and the newest request is built next, so a
drag never queues work behind stale plans. The published preview stays on screen
until the finished plan is handed back on the main thread, and every published
plan is the full-field build, never a fast search estimate. `SetPreviewDuration`
remains the synchronous, deterministic path for callers that need a plan
immediately; both paths build the identical plan. At the epoch on the throwaway
copy, one synchronous retime is 120 ms at 4.5 d and 97 ms at 7.35 d to Mercury
1 g, and 97–125 ms to Mars at 0.3 g over 9–15 d; request-to-publish on the
worker is the same build plus **2–4 ms** of handover. Twenty requests at 10 ms
intervals published only the newest plan — 311 ms after the first request at
Mercury, with no backlog — so the handle stays responsive while the route
catches up.

The powered plans already sit at the left end, so there is no "burn harder"
above them. A faster torch trip needs a stronger drive (a data value). The slider
therefore only stretches the existing torch plan: the drive stays on, just more
gently. It is **not** a coast. A coast is a Keplerian conic, not a quintic with a
flat middle; the `PROFILE` buttons choose the unpowered `Coast` or the
`Burn-coast-burn` profile, whose slider is a speed control over its own feasible
range ([navigation and arrival](navigation-and-arrival.md#the-trip-time-control)).

## What the throttle is

The drive is **throttleable from zero to 10 m/s²**. The plan's acceleration
curve is the throttle setting, so the HUD can draw it and the future fuel model
can charge for its integral. Thrust direction is instantaneous: there is no
attitude dynamics or slew limit yet.

The quintic plan uses a smooth ramp. The hybrid fixed-thrust arrival is the
deliberate opposite: a burn is a constant level the player chooses and a coast is
zero, with a step at each seam. The nav computer's third arrival style is where
that texture is exposed.

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

# Travel regimes

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

The navigation computer should present every profile with time, delta-v, and
arrival date, so the choice is informed. See
[navigation and arrival](navigation-and-arrival.md).

**Implemented:** the torch plan and its trip-time control, plus the conic
**arrival** (a coasting hyperbola and an impulsive Oberth capture), the hybrid
fixed-thrust variant (one finite burn at a player-set level, an impulse trim, and
the conic circularisation), and the **burn–coast–burn cruise** (a quintic break,
a prograde fixed burn, a Lambert coast, a brake, and the conic or hybrid
capture). The `PROFILE` row chooses the powered cruise (the default), the
unpowered coast, or the burn–coast–burn profile; the trip-time slider becomes
that profile's speed control and the existing level slider sets both burns'
level. A fixed-thrust *cruise* elsewhere — a slow spiral that does not coast on a
Lambert conic — is still deferred.

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
  time at the cost of solver complexity. The conic arrival closes part of the
  gap in delta-v rather than time, and it is now implemented.
