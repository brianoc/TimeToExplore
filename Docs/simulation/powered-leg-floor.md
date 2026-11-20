---
type: Design Document
title: Full-field burns and the powered-leg floor
description: Fixed burns integrate the summed gravity field and hand over to the shaped ends inside a band, so a capture can enter hot and deep instead of falling in from the sphere of influence at a planted 5 km/s.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
---

# The problem

A burn–coast–burn trip to Uranus reads as almost all coast at any setting of the
trip-time slider. Two things cause it: the capture always starts at the planted
entry speed, and the slider only re-times the heliocentric leg.

The capture chain always enters the target's sphere of influence at the planted
`TripPlanner.EntrySpeedMps = 5000`. That is a **global input**: every conic and
hybrid capture starts on the SOI sphere with 5 km/s inbound, and the drive-off
fall from there to periapsis is set by the target's gravity, not by the trip
time. At Uranus the SOI is **51.8 million km**, so the fall takes about
**104 days** — of order `r_SOI / v∞`, shortened by the fall's own acceleration.
At Earth, whose SOI is **0.92 million km**, the same entry speed gives about
**2 days**.

| Body | SOI radius | Drive-off fall at 5 km/s |
|---|---:|---:|
| Earth | 0.92 million km | about 2 days |
| Uranus | 51.8 million km | about 104 days |

The slider re-times the heliocentric leg only. It moves the coast, the
departure burn, and the brake, but it cannot move the capture's fall: the entry
state is the same at every duration. On a Uranus trip the fixed fall therefore
dominates every duration the slider can ask for, and the profile reads as coast
at every setting.

This is a **speed** problem, not an arc-boundary problem. The boundary is not in
the wrong place, and braking earlier does not help: braking early leaves less
speed for the fall and makes the coast longer. The win is a **hotter entry and a
deeper brake** — arrive at the target with more speed, at a radius closer in, and
let the brake and the capture spend it where the Oberth effect makes it cheap.

# Full-field burns

A fixed burn **was** planned in the two-body field of one body.
`FiniteBurnArc` integrated constant thrust through `centre.Gm` alone and added
the centre's own ephemeris; the departure burn and the brake were "about the
Sun", the hybrid capture burn about the target. The ship in flight does not
switch frames: it feels the summed gravity of every body at once
([ADR-005](/decisions/adr-005-continuous-gravity.md)). A burn that begins near a
body and ends well outside it was therefore planned in a field that is wrong at
one end, and a level or trip time that would make a burn cross a sphere of
influence was refused rather than flown.

**A fixed burn integrates the summed gravity field.** The reference body becomes
a **label**: it names the phase, the frame the arc is drawn and reported in, and
the ephemeris used to decompose the state — not the physics. One burn arc may
cross a sphere of influence.

Do **not** split a burn at the SOI. Splitting it is the patched option, and it
puts the seam exactly where each two-body model is about 50% wrong: at the
boundary each body's term is comparable to the other's, so neither approximation
is the field. A single summed-field arc has no seam to get wrong. The coast and
the shaped ends are unaffected — a coast is still a conic about one body, and a
quintic still takes its endpoint accelerations from the full field.

The cost is real: a full-field burn is no longer a function of one body's `μ` and
one initial state, so every sample needs the catalogue's ephemeris. It is slower,
and the floor search's near-minimum basin is already noisy. The established
pattern — search with the fast model, rebuild and certify the reported plan with
the full field — is what keeps this affordable, and the plan that is reported and
flown remains the full-field one.

## What is implemented

`FiniteBurnArc` takes an optional `IGravityField`. With one it integrates
constant thrust through the summed field in **absolute heliocentric coordinates**
and decomposes the sampled table back into the reference body's frame, so
`EndLocalPositionM`, `EndLocalVelocityMps`, `LocalStateAt`, and the reported path
are still the phase's body-relative state. The reference body is a **label**: a
second arc over the same inertial start state and the same thrust, labelled with
a different body, follows the identical inertial path and differs only in how its
state is decomposed. `AccelerationAt` reports the arc's true inertial
acceleration — the summed field at that state and date plus the thrust — which
is what the quintic endpoint matching, `Measure`, and the peak rely on.

The **thrust law is unchanged**, and that is deliberate. A prograde or retrograde
burn holds the velocity relative to the reference body; the departure burn and
the brake hold a fixed inertial direction (the escape ray and the handover
change). The field is summed; the attitude is still the frame-relative one the
profile already used. Changing the attitude law is
[the steering measurement](#steering-starts-simple), not this change.

**The two-tier boundary stands.** The floor search's probes keep the cheap
two-body burn: a probe only has to decide whether a duration fits, and the
reported plan is rebuilt and certified with the summed field. Every fixed burn in
that certified build is full-field — the departure burn, the brake, and the
hybrid capture's injection arc — and a full-field burn is one arc even when it
crosses a sphere of influence. There is still no split at an SOI.

One boundary is explicit rather than incidental. The **hybrid capture solver**
— the search over the capture burn's start offset and duration, which integrates
the radius crossing hundreds of times — stays on the two-body model as a search
input, like the floor probe. Measured at the epoch, integrating that same burn
full-field moves its end by **1.7 m at Mars, 218 m at Jupiter, and 10 m at the
Moon**, and moves the trim by at most **0.05 m/s**, against an injection margin
of 5 km and a 100 km clearance. The capture arc that is reported, and the trim
recomputed from it, are full-field.

**The active set is refreshed every RK4 step.** `GravityField.AccelerationAt`
evaluates the set selected by the last `Prepare`, so a burn that leaves one
body's vicinity must re-prepare for the set to stay a superset of the bodies that
matter. The arc calls `Prepare` once per step, at the step's start, and evaluates
that set at the four stage times — the same pattern `ShipIntegrator` uses. That
is the correct rule and the expensive one: a full-field step costs about **fifty
times** a two-body step, because `Prepare` evaluates the whole catalogue's
ephemeris. Measured at the epoch, in a .NET harness over the same sources: a
90-day Mars build is **0.47 s** (0.05 s before), the Mercury 8-day build about a
second,
`Plan()` at the Mars floor **2.0 s** (0.5 s before) and at the Mercury floor
2.4 s, and a slider retime 0.7–1.4 s. In the editor the same work is several
times slower again: the burn–coast–burn class is the Unity suite's longest silent
stretch at about **4 minutes**, the near-limit screen sweep (four Mercury builds)
is 80 s, and the Mars floor test 34 s. A cheaper prepare rule — for example
re-selecting every few steps — is allowed if it is measured against this one, and
is [step 7](/technical/planning-performance.md)'s to take.

Also measured: the reported plans **did not move** at their stated precision. The
reason is structural, not luck. A fixed-direction burn's delta-v is set by the
chain (`a T` = the velocity change the coast needs), so the summed field changes
where the burn is and how its attitude is placed, not how much it spends; the
pinned solve absorbs the field difference into the escape ray and the path. The
8-day Mercury plan is still 569.9 km/s, 12.3 days 379.9, Mars 90 days 45.4, and
the floors are still 3.46 and 8.03 days. The seams close to under a millimetre
and 10⁻⁶ m/s, and the Mars 90-day flight arrives 0.08 m and 7 × 10⁻⁵ m/s from
the reference state.

**One consequence to know.** A body-relative position on a full-field arc carries
the catalogue's Julian-date quantisation — about 0.2–0.9 m at 1.5 AU, because a
date's last place is 40 µs — while the absolute path does not. Tests that compare
two models compare inertial states, and a local-state tolerance is metre-scale.

# The handover band

The burn hands over to the shaped ends between a hard **floor** and a hard
**ceiling**:

* the **floor** is `sqrt(μ / (κ a_drive))` with `κ = 0.4`, the parking ceiling's
  constant. `a_drive` is the acceleration the powered leg commands, so the floor
  sits where local gravity is 40% of that acceleration and moves outward as the
  level falls. It is the same rule that sets the quintic parking ceiling
  ([navigation and arrival](/simulation/navigation-and-arrival.md#the-parking-orbit)),
  used here as a floor on the handover;
* the **ceiling** is `1.02 × SOI`, the current clearance sphere. Outside it the
  handover is no longer local to the body, and the shaped end has nothing to
  shape against.

The floor is a **policy**, not a law of physics, in the same sense as the parking
ceiling: it keeps the handover in a region where the ship's own thrust still
dominates the field it is steering through, so the shaped end and the burn's
attitude law remain describable. Inside the band the radius is chosen by a rule;
solving for the best radius inside the band is deferred until measurement shows
the rule costs something (see the open questions).

`HandoverBand.For(body, a_drive)` is that pair of radii and the rule that reads
them: it returns the floor, the ceiling, whether the band is valid, and why not.
It is asked for `TripPlanner.FixedBurnAccelerationMps2` — the player's level,
capped by the drive — so the floor moves with the level.

# The endpoints

The band says where the handover **may** sit. Each end of the trip takes one of
its edges. That choice is a caretaker decision for this plan, not a solved
optimum:

* **Departure: the ceiling.** The break's job is escape, so it ends on
  `1.02 × SOI` at the local escape speed along the escape ray, and the departure
  burn starts there and moves outward. This is the value the profile already
  used — the same double, now read from the band — so the departure plan does
  not move.
* **Arrival: the floor.** The brake ends at the deepest radius where local
  gravity is still 40% of the drive, and the capture chain is handed its entry
  state there. The fall shortens as the radius comes in, which is the point of
  the change. The chain still enters at the target's sphere of influence today;
  connecting the arrival endpoint to it, and to the residual entry speed, is the
  next step of the plan in `HANDOFF.md`.

An **interior** solved radius is still open. If the steering measurement shows
that the rule costs real delta-v or time, the radius becomes a variable inside
the band. Nothing here solves for one.

## The floor is clamped to the body

The gravity floor is inside the body on several body and level pairs, so the
effective floor is

```text
r_floor = max( sqrt(μ / (0.4 a_drive)) , R + 100 km )
```

The 100 km is `HybridCapture.MinimumClearanceAltitudeM`, the altitude the hybrid
capture's burn and transfer already keep. It is well below the 300 km parking
altitude, so the clamp binds only where the gravity floor is deep.

Measured at the epoch at the planner's commanded acceleration (9.70 m/s² at 1 g,
2.941995 m/s² at 0.3 g):

| Body | 0.3 g floor | 1 g floor | Gravity floor at 1 g | Radius |
|---|---:|---:|---:|---:|
| Mercury | 4,326.9 km | **2,539.7 km** (clamped) | 2,382.9 km | 2,439.7 km |
| Mars | 6,032.7 km | **3,489.5 km** (clamped) | 3,322.4 km | 3,389.5 km |
| Moon | 2,041.6 km | **1,837.4 km** (clamped) | 1,124.3 km | 1,737.4 km |
| Earth | 18,404.2 km | 10,135.7 km | 10,135.7 km | 6,371.0 km |
| Jupiter | 328,106.1 km | 180,696.3 km | 180,696.3 km | 69,911.0 km |

Halving the level multiplies an unclamped floor by `sqrt(2)`. A clamped floor does
not move at all until the level falls far enough for the gravity floor to clear
the surface.

**A floor below the capture chain's periapsis does not bind.** At 1 g Mercury's
clamped floor is 2,539.7 km against a conic periapsis of 2,561.7 km, and Mars's is
3,489.5 km against 3,559.0 km; several small moons are the same, because their
`1.05 R` periapsis is above their `R + 100 km` clearance. The floor is a **lower
bound** on where the handover may sit, not a promise that the chain can be handed
the state there: where the chain's own entry needs a deeper or higher radius — a
periapsis, or a parking orbit the chain must start outside — that radius governs,
and the arrival endpoint is the larger of the band floor and what the chain
needs. At the giants the floor is far **above** the parking radius (Uranus at
0.3 g: 70,167.5 km against a 29,166 km conic parking radius), which is the normal
arrival case: the handover is high, and the capture falls in from it.

## The band can be empty

If the clamped floor exceeds the ceiling, the handover does not exist at that
level. The planner refuses the trip with the numbers instead of building a chain
with no band:

* **Phobos and Deimos** have spheres of influence *inside* their own surfaces in
  the catalogue — 7.2 km against an 11.3 km radius, and 8.1 km against 6.2 km — so
  their bands are empty at every level. The reason reads *"the handover band at
  Phobos is empty at 9.70 m/s²: the floor is 111.3 km (the 0.4 km gravity floor is
  inside the body, so it is clamped up to the 111.3 km surface clearance), above
  the 7.4 km ceiling (1.02 × the 7.2 km sphere of influence) by 103.9 km"*. Both
  were already unreachable through the conic chain, which refuses an entry sphere
  smaller than the periapsis; the refusal is now specific and comes before any
  coast is built.
* **Proteus** at Neptune is the same case by a smaller margin: a 310.0 km floor
  against a 280.1 km ceiling.
* No planet and no ordinary moon has an empty band at 0.3 g or 1 g.

# The body-clearance gate

The old rule was that the departure burn stay outside the departure sphere of
influence. That rule is **gone**. A burn may cross a sphere — it integrates the
summed field — but it may not cut a body.

`TripPlanner.FixedBurnClearsBody` samples a fixed arc against a body's ephemeris
and returns the closest radius from its centre. The plan's gate holds the
departure burn clear of the departure body, and the arrival burns — the brake,
then the hybrid capture's injection burn — clear of the target, by
`HybridCapture.MinimumClearanceAltitudeM` (100 km above the surface, the same
clearance the hybrid capture keeps). A rejection names the arc, the body, and how
close it came. Measured at the epoch: a 3-hour burn at 1 g started 1.5 Earth
spheres out and pushed straight at the planet crosses the 924,651 km sphere and
still clears the planet by 821,258 km, and is **accepted**; a 30-second burn
60 km up, thrusting at the surface, grazes to 6,422.3 km (51.3 km altitude)
against a 6,471 km clearance and is **refused**. The 90-day Mars build's departure
burn starts on the ceiling and climbs, so it still stays outside the sphere; that
is a consequence of the pinned endpoint, not the rule.

# The shaped ends stay

The break and the conic/hybrid capture keep their jobs. Only the handover radius
and the field the burn is integrated in change.

The break is still the quintic that leaves the parking orbit and ends on the
handover at the local escape speed along the escape ray; the capture is still the
conic or hybrid chain the profile already uses. The break's duration solve, the
pinned fixed point, the escape-ray attitude, the brake's direction, the trim, the
transfer ellipse, and the circularisation are unchanged. No new arc type is
introduced, and `0 g` still selects `CruiseStyle.Coast` exactly.

# Entry speed is an output

Today the entry speed is an **input**. `EntrySpeedMps = 5000` is global and shapes
every capture; changing it moves every conic plan, including the inner-system
ones where the current entry works.

The capture chain gains an **entry radius** — the handover radius — and the brake
solve sets the **residual entry speed** at that radius instead of accepting the
planted 5 km/s. The entry speed becomes an **output** of the trip: the brake
solve chooses how much of the arrival velocity it removes, and the state it
leaves — radius and residual speed — is what the capture chain is handed.

This is what makes a short fall representable. The fall time is set by the entry
radius and the entry speed, not by the SOI. A brake that ends near the floor with
a hot residual hands the capture a short, fast inbound leg: the ship arrives with
more speed at a radius closer in, and the capture spends it where the Oberth
effect is strongest. The 104-day fall at Uranus is a statement about entering at
51.8 million km at exactly 5 km/s, not a property of Uranus.

Keeping the global constant as an option is allowed — a plan may still plant
5 km/s at the SOI — but a per-plan residual is what the profile needs. Changing
the global value is not the lever: it moves every plan at once.

# Steering starts simple

The burns need an attitude law, because a full-field arc is no longer "prograde
about one body". The first law is a **fixed inertial direction**, which is the
honest model of a real burn: the ship holds one attitude instead of tracking its
velocity. A long burn may be subdivided into a few fixed-direction segments if
one is too crude; each segment is still a fixed inertial direction, not a control
law.

The alternative is a **local-prograde law** — thrust along the instantaneous
velocity or the local frame's prograde — which is natural on a two-body arc but
not obviously cheaper when the field is summed and the burn crosses a boundary.
The penalty of the fixed direction must be measured against the local-prograde
law before any more expensive law is considered. A shooting solve is the last
resort, not the first.

The texture already exists: the brake holds a fixed direction along the change
the chain needs, and the departure burn holds the escape ray. Starting simple
means keeping that and measuring it, not replacing it.

# What this changes

* Burning across a sphere of influence is a supported case, not a rejected one.
  This supersedes the exclusion in
  [burn–coast–burn trips](/simulation/burn-coast-burn.md#not-in-the-first-cut).
  `FiniteBurnArc` now integrates the one arc through the boundary; the fast
  probes keep the two-body burn.
* The `1.02 × SOI` clearance sphere becomes the **ceiling** of the handover band,
  not the definition of the handover radius; the departure endpoint is that
  ceiling, so the departure plan is unchanged.
* The rule that the departure burn stay outside the sphere of influence becomes
  a **body-clearance** gate: every fixed burn must keep 100 km above the body it
  works near, and a burn that crosses a sphere while clearing both bodies is
  valid.
* The global `EntrySpeedMps` stops being the profile's entry condition; the entry
  radius and the residual entry speed are per plan.
* The coast, the quintic, the capture chain, and the `0 g` identity are
  unchanged.

# Open questions

* **The steering law.** Fixed direction, a few fixed segments, or local prograde?
  The delta-v penalty of the simple law is unmeasured; the choice waits on a
  measurement at the epoch at 0.3 g and 1 g.
* **Band versus solved radius.** *Settled for this plan:* the departure endpoint
  is the ceiling and the arrival endpoint is the floor, with the floor clamped to
  the body clearance. *Still open:* whether that rule should give way to an
  interior solved radius, if the steering measurement shows the rule costs real
  delta-v or time.
* **Search input or certified build.** Is the handover radius a free variable the
  floor search explores, or a value computed from the band and then certified
  with the full field like a chosen duration? The floor search is already noisy
  near its minimum, so this is a cost question as well as a design one.

# See also

* [Burn–coast–burn trips](/simulation/burn-coast-burn.md) — the profile, the
  pinned break, and the measured floors this change moves.
* [Navigation and arrival](/simulation/navigation-and-arrival.md) — the capture
  chain, the parking ceiling, and the sphere-of-influence table.
* [The flight model](/simulation/flight-model.md) — the drive, the burn levels,
  and the delta-v economy.
* [ADR-005](/decisions/adr-005-continuous-gravity.md),
  [ADR-014](/decisions/adr-014-keplerian-coast.md), and
  [ADR-015](/decisions/adr-015-fixed-thrust-burns.md) — the summed field, the
  capture chain, and the fixed-thrust burn.
