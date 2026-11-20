---
type: Design Document
title: Full-field burns and the powered-leg floor
description: Fixed burns integrate the summed gravity field and hand over to the shaped ends inside a band, so a capture can enter hot and deep instead of falling in from the sphere of influence at a planted 5 km/s.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-20T00:00:00Z }
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
flown remains the full-field one. The size of that cost in the editor, and the
redundant builds the floor search and the retime used to repeat, are measured in
[planning performance](/technical/planning-performance.md#one-burncoastburn-build-measured-2026-11-20).

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

**The active set is refreshed every eighth RK4 step.** `GravityField.AccelerationAt`
evaluates the set selected by the last `Prepare`, so a burn that leaves one
body's vicinity must re-prepare for the set to stay a superset of the bodies that
matter. The arc calls `Prepare` at the first step and every eighth step after it,
at the step's start, and evaluates that selected set at the four stage times.
Eight steps is **32–480 s of burn** depending on the arc's 4–60 s sample step,
and over that span a burn closes only a small fraction of the distance to any new
body, so the set stays a superset of the bodies that matter. `AccelerationAt`
still prepares **per query**, so the reported acceleration — what the quintic
endpoint matching, `Measure`, and the peak read — is exact. The accuracy check is
unchanged: the 80,000 s SOI-crossing burn matches a 20,000-step RK4 that prepares
every step to **0.41 mm and 6.7 × 10⁻⁹ m/s**, the same numbers as before the
cadence, because the active set is stable over that burn.

A refresh evaluates the whole catalogue's ephemeris, so it is the step's lever.
The step-2 estimate of "about fifty times a two-body step" is superseded by the
build-level measurement: the two step-7 levers together make a
fixed-duration build **2.2–3.7× faster** (Mars 90 d at 0.3 g 1.10 → 0.31 s,
Mercury 8 d at 1 g 1.54 → 0.43 s, Uranus 400 d 0.98 → 0.40 s, Mars 20 d 1.35 →
0.38 s, Mars 10 d 1.57 → 0.42 s, Uranus 35 d 1.41 → 0.63 s). The other lever, a
coarser brake step, was measured and rejected: the 4 s target step already clamps
at 2,048 samples for every measured brake. The measurements are in
[planning performance](/technical/planning-performance.md#step-7-the-levers-measured-2026-11-20).

Also measured: the reported plans **did not move** at their stated precision. The
reason is structural, not luck. A fixed-direction burn's delta-v is set by the
chain (`a T` = the velocity change the coast needs), so the summed field changes
where the burn is and how its attitude is placed, not how much it spends; the
pinned solve absorbs the field difference into the escape ray and the path. The
8-day Mercury plan was then still 569.9 km/s, 12.3 days 379.9, Mars 90 days
45.4, and the floors were then still 3.46 and 8.03 days; the arrival entry
radius moved those in the next step (see [the measurements](#what-the-floor-entry-measured)).
The seams closed to under a millimetre and 10⁻⁶ m/s, and the Mars 90-day flight
arrives 0.08 m and 7 × 10⁻⁵ m/s from the reference state.

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
  the change. **Implemented for the burn–coast–burn**: its chain is built at
  `max(floor, what the chain needs)`, where "what the chain needs" is the conic
  parking radius — an entry at that radius is above the periapsis by
  construction and falls into the orbit the chain ends on. Where the floor is
  above the parking radius (the giants, and any level where the gravity floor
  clears the surface) the floor itself governs. The **coast** profile does not
  take this endpoint: it has no brake to hand over, so it still enters on the
  target's sphere of influence ([the chain's entry contract](#the-chains-entry-radius)).
  Handing the chain the *residual entry speed* as well is the next decision, not
  this one.

An **interior** solved radius is still open. If the steering measurement shows
that the rule costs real delta-v or time, the radius becomes a variable inside
the band. Nothing here solves for one.

## The chain's entry radius

The capture chain takes a **handover radius**, not a sphere of influence. Which
radius each profile passes is deliberate:

| Profile | Entry radius | Why |
|---|---|---|
| Conic or hybrid arrival on the powered cruise | the target's sphere of influence | the quintic cruise hands over where the heliocentric arc ends |
| `CruiseStyle.Coast` | the target's sphere of influence | there is no brake to hand over; the coast profile's anchors are unchanged |
| `CruiseStyle` burn–coast–burn | `max(band floor, conic parking radius)` | the brake ends there, so the chain's fall starts there |

`TransferConic.Build`'s radius argument is therefore the **entry radius**, and
`TransferConic.EntryRadiusM` is its reported name. Only the inbound fall moves
with it: the periapsis burn is `sqrt(v∞² + 2μ/r_p) − v_transfer`, which is set by
the entry (hyperbolic excess) speed and the periapsis radius. The entry radius
buys **time**, not capture delta-v.

`TrajectoryPlan.CaptureEntryRadiusM` and `CaptureInboundSeconds` report the
chain's entry and its fall, so a test or the panel can read where the handover
happened and how long the coast in was; the quintic capture reports zero for
both, because it has no planetocentric entry point.


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

Measured at the epoch, the radius the burn–coast–burn actually enters at:

| Body and level | Band floor | Conic parking radius | Entry used |
|---|---:|---:|---:|
| Mercury, 1 g | 2,539.7 km (clamped, inside the 2,561.7 km periapsis) | 2,805.7 km | **2,805.7 km** |
| Mars, 1 g | 3,489.5 km (clamped, inside the 3,559.0 km periapsis) | 3,897.9 km | **3,897.9 km** |
| Mars, 0.3 g | 6,032.7 km | 3,897.9 km | **6,032.7 km** |
| Uranus, 1 g | 38,643.0 km | 29,166.3 km | **38,643.0 km** |
| Uranus, 0.3 g | 70,167.5 km | 29,166.3 km | **70,167.5 km** |

The clamp binds on Mercury and Mars at 1 g, so the entry is the parking radius
there and the fall is short rather than zero; at 0.3 g on Mars and at both
Uranus levels the floor governs.


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

# What the floor entry measured

Measured at the epoch (2026-11-15), from Gateway, conic capture. The old build
enters on the target's sphere of influence at the planted 5 km/s; the new one
enters at the row's entry radius, with the same 5 km/s.

| Target, level, trip | Entry radius old → new | Inbound fall old → new | Capture Δv | Total old → new | Brake start radius | Brake Δv | Floor old → new |
|---|---|---:|---:|---:|---:|---:|---|
| Uranus, 1 g, 400 d | 51,760,023 km → 38,643 km | 117.13 d → 31 min | 6.37 km/s (both) | 242.1 → 182.3 km/s | 51.96 million km → **366,505 km** | 112.70 → 81.20 km/s | 150.73 d → **34.16 d** |
| Uranus, 0.3 g, 400 d | 51,760,023 km → 70,168 km | 117.13 d → 1.3 h | 6.37 km/s (both) | 242.5 → 180.8 km/s | 52.45 million km → **1,159,005 km** | 112.83 → 79.75 km/s | 160.47 d → **40.71 d** |
| Mercury, 1 g, 8 d | 112,410 km → 2,806 km | 6.09 h → 3.3 min | 3.50 km/s (both) | 569.9 → 557.0 km/s | 2.64 million km → 2.38 million km | 216.29 → 213.30 km/s | 3.46 d → **3.22 d** |
| Mercury, 1 g, 12.3 d | 112,410 km → 2,806 km | 6.09 h → 3.3 min | 3.50 km/s (both) | 379.9 → 376.9 km/s | 1.21 million km → 1.07 million km | 141.17 → 142.29 km/s | — |
| Mars, 0.3 g, 90 d | 577,241 km → 6,033 km | 1.3187 d → 13.6 min | 3.46 km/s (both) | 45.38 → 44.36 km/s | 658,870 km → **92,790 km** | 19.13 → 18.51 km/s | 8.03 d → **6.74 d** |

The trend is the one the design predicted, and it is bigger than the fall alone:
the 104-day Uranus fall is gone, and because the coast absorbs the time the
profile used to spend falling, the floor moves fourfold at Uranus (150.73 → 34.16
days at 1 g) and the 400-day plan is *cheaper* as well as shorter (242 → 182
km/s). A slower profile pays by carrying speed longer: the 400-day coast is 392
days at 1 g and the burn and brake are 90.2 and 81.2 km/s against 118.8 and
112.7.

**The Uranus floors were re-measured in step 7.** The cheaper active-set cadence
([above](#what-is-implemented)) changes the certified plan the near-floor basin
first closes at, so the floor moved: **33.99769 → 34.16041 d** at 1 g (0.16 d
*more* conservative, 0.5%) and **41.57095 → 40.71332 d** at 0.3 g (0.86 d
shorter, and 1,801.17 km/s instead of 1,756.59 because the shorter trip carries
more speed). Mars (6.73715 d) and Mercury (3.21610 d) did not move. These are
re-measured anchors, not errors: a floor is the first duration the certified
full-field build closes at, and the basin was already noisy.

**A hotter entry shortens the fall but grows the capture.** Sweeping the
per-plan entry speed at Uranus 1 g and 400 days, at the floor:

| Entry speed | Inbound fall | Capture Δv | Brake Δv | Total |
|---:|---:|---:|---:|---:|
| 5 km/s | 31.0 min | 6.37 km/s | 81.20 km/s | 182.3 km/s |
| 20 km/s | 19.0 min | 13.82 km/s | 79.93 km/s | 188.5 km/s |
| 50 km/s | 8.97 min | 39.10 km/s | 84.75 km/s | 218.6 km/s |

So the entry speed is not free: the fall falls as the speed rises, but the
periapsis burn grows faster than the brake shrinks. That is why the brake-produced
residual is the next decision rather than a number to plant.

## The binding question: where the coast ends

The brake ends at the floor, and the brake's start is the coast's end. A fixed
burn at `a` covers `Δv / a` seconds, so the distance it spans depends on the trip:
the **long** trips hand the coast a final leg inside the target's sphere, and the
**fast** ones make the brake span the sphere itself.

* **Long trips.** At Uranus 1 g and 400 days the brake is 139.5 min of 81.2 km/s
  and starts **366,505 km** from Uranus — **0.0071 × the sphere of influence**,
  where Uranus's gravity is 0.043 m/s². The coast has ended deep inside the
  target's well. The seams close (the worst is under 0.1 m and 2 × 10⁻⁵ m/s) and
  the reported plan is continuous, but the last part of that coast is a
  heliocentric conic in a region where the target dominates. That is a **model
  gap**, not a seam: the measurement reports it, and the next decision owns it.
  At 0.3 g the same plan's brake is 7.5 h and starts 1,159,005 km out — 0.0224
  spheres, 0.0043 m/s². Mars at 90 d is 0.16 spheres (92,790 km).
* **Fast trips.** At Uranus's 1 g floor (34.16 d) the brake is 28.4 h of 990 km/s
  and starts **50.55 million km** out — 0.976 spheres — so the brake itself
  spans the sphere-to-floor gap. Mercury's floor (3.22 d) puts the brake's start
  at 31.05 million km, **276 spheres** out; Mars's floor (6.74 d) at 48.30
  million km, **83.7 spheres** out. At the fast end the coast ends well outside
  the target and the brake is the long crossing.

The brake is never *wrong* about the field: every fixed burn integrates the
summed field, so the 28.5-hour Uranus brake feels Uranus. What the measurement
shows is the other side: a Sun-only coast cannot describe the final approach when
it ends deep in the well, so a deep floor entry trades the chain's 104-day
planetocentric fall for a heliocentric coast over the same region. The seams
close either way; the models differ. The choice between a planetocentric approach
leg, an entry-speed rule, or a full-field coast is the next decision.

# Entry speed is an output

Today the entry speed is an **input**. `EntrySpeedMps = 5000` is global and shapes
every capture; changing it moves every conic plan, including the inner-system
ones where the current entry works. `FlightSession` already copies it per plan
(the retime's request carries it), so a per-plan value is possible without moving
the default; this step kept the default and the per-plan input exactly as they
were.

The capture chain now gains its **entry radius** — the handover radius — and the
next decision is the **residual entry speed** at that radius. Making it an
**output** of the brake solve means the brake chooses how much of the arrival
velocity it removes, and the state it leaves — radius and residual speed — is what
the chain is handed. The measurements above are what that decision needs: at the
Uranus floor, 5 → 50 km/s moves the fall from 31 to 9 minutes but the capture from
6.4 to 39.1 km/s, and the brake's own delta-v hardly moves, because the brake is
shedding the heliocentric crossing, not the local entry.

This is what makes a short fall representable. The fall time is set by the entry
radius and the entry speed, not by the SOI. A brake that ends near the floor with
a hot residual hands the capture a short, fast inbound leg: the ship arrives with
more speed at a radius closer in, and the capture spends it where the Oberth
effect is strongest. The 104-day fall at Uranus is a statement about entering at
51.8 million km at exactly 5 km/s, not a property of Uranus.

Keeping the global constant as an option is allowed — a plan may still plant
5 km/s at the floor — but a per-plan residual is what the profile needs. Changing
the global value is not the lever: it moves every plan at once.


# Steering starts simple

The burns need an attitude law, because a full-field arc is no longer "prograde
about one body". The first law is a **fixed inertial direction**, which is the
honest model of a real burn: the ship holds one attitude instead of tracking its
velocity. The alternative is a **local-prograde law** — thrust along the
instantaneous velocity or the local frame's prograde — which is natural on a
two-body arc but not obviously cheaper when the field is summed and the burn
crosses a boundary. A long burn could be subdivided into a few fixed-direction
segments if one were too crude; each segment is still a fixed inertial direction,
not a control law. A shooting solve is the last resort, not the first.

Measured at the epoch, the fixed direction is the right law at both ends. It is
exact where it is used, and neither a tracking law nor segmenting recovers
anything. The texture already existed — the brake holds a fixed direction along
the change the chain needs, and the departure burn holds the escape ray — and
the measurement now says keeping it is not a compromise.

## What was measured

Four burn–coast–burn plans were built at the epoch (2026-11-15) from Gateway with
the conic capture: Uranus at 400 days and 1 g and at 0.3 g, Mars at 90 days and
0.3 g, and Mercury at 8 days and 1 g. For each finite-burn arc — the departure
burn and the brake — the held thrust direction was recovered from the arc itself
as `AccelerationAt(mid)` minus `GravityField.AccelerationAt` at the same state
and date, so the measurement reads the attitude the planner actually built rather
than a re-derived one. The **attitude frame** is the departure body (Earth) for
the departure burn and the target for the brake: the tracking direction is the
body-relative velocity, negated for the brake. Each burn was sampled at 400
points for the angle between the held direction and the tracking direction, the
mean cosine across the burn, and the rotation of the tracking direction.

Each alternative law was then flown from the plan's own start state, for the same
duration at the same acceleration, through the full summed field with RK4 at
2,048 steps or better, preparing the field at every step as the independent
reference does. The laws were:

* **fixed** — the plan's own recovered direction, which must reproduce the
  plan's end state;
* **local prograde or retrograde** — along the instantaneous body-relative
  velocity, negated for the brake;
* **N fixed segments**, from one up to 128 — each segment holds the body-relative
  velocity direction sampled from the plan's own burn at that segment's start,
  which is what a few attitude updates during the burn would give;
* **pursuit** — along the remaining velocity-to-go in the frame,
  `v_end − v(t)` with both body-relative, an optimistic local law that knows
  where the burn must finish.

Holding the duration and acceleration fixed is what makes the answer a **seam
error**: it is the state each law leaves where the chain needs the plan's, and
closing that difference needs at least that much impulse. The fixed law
reproduced every burn's end state to 0.000 km and 0.000 km/s, so the harness and
the planner integrate the same field and the same thrust; that check is what
makes the other rows readable.

## The departure burn is already prograde

The departure burn holds the escape ray, and the escape ray **is** the departure
frame's prograde. The burn starts on the clearance sphere at the local escape
speed along the ray, and both the thrust and the body's gravity are radial, so
the body-relative velocity stays on the ray for the whole burn. Measured:

| Case | Burn | Δv | Radius from Earth | Tracking rotation | Held vs tracking |
|---|---:|---:|---:|---:|---:|
| Uranus, 1 g, 400 d | 2.582 h | 90.17 km/s | 943,144 → 1,370,818 km | 0.00° | 0.00° → 0.00° |
| Uranus, 0.3 g, 400 d | 8.524 h | 90.28 km/s | 943,144 → 2,356,578 km | 0.00° | 0.00° → 0.00° |
| Mars, 0.3 g, 90 d | 1.267 h | 13.42 km/s | 943,144 → 977,932 km | 0.00° | 0.00° → 0.00° |
| Mercury, 1 g, 8 d | 7.158 h | 249.96 km/s | 943,144 → 4,187,405 km | 0.00° | 0.00° → 0.00° |

Every case has a mean cosine of 1.000000, so the cosine loss is zero and the
13.4–250.0 km/s the burn spends is all along the tracking direction. A
local-prograde law flown over the same burns reproduces the end state to
**0.003 km/s** at worst (Uranus 0.3 g and Mercury), and its position drifts by at
most 39 km over a burn that ends one to four million kilometres from Earth,
which is the summed field's tidal residual pulling the relative velocity a
fraction of a degree off the ray. Segments are the same: 2, 4 and 8 of them are
all within 0.001 km/s of the fixed law, because there is nothing to track that
the ray does not already track. **The departure burn costs nothing to hold
fixed, and segmenting it buys nothing.**

## The brake is where the laws differ

The brake is the hard case: it is a change in direction as well as in speed, and
the tracking direction rotates through 30–78° over the burn.

| Case | Burn | Δv | Radius from target | Rotation | Held vs retrograde | Mean cos | Cosine loss |
|---|---:|---:|---:|---:|---:|---:|---:|
| Uranus, 1 g, 400 d | 2.325 h | 81.20 km/s | 366,505 → 38,643 km | 78.27° | 9.65° → 87.58° | 0.831505 | 16.85% = 13.68 km/s |
| Uranus, 0.3 g, 400 d | 7.530 h | 79.75 km/s | 1,159,005 → 70,167 km | 75.32° | 7.18° → 82.40° | 0.884338 | 11.57% = 9.22 km/s |
| Mars, 0.3 g, 90 d | 1.747 h | 18.51 km/s | 92,790 → 6,033 km | 30.35° | 9.40° → 39.72° | 0.939318 | 6.07% = 1.12 km/s |
| Mercury, 1 g, 8 d | 6.108 h | 213.30 km/s | 2,378,950 → 2,806 km | 68.13° | 1.50° → 69.64° | 0.979441 | 2.06% = 4.39 km/s |

The seam each law leaves, against the plan's burn end state, in km/s:

| Law | Uranus 1 g | Uranus 0.3 g | Mars 0.3 g | Mercury 1 g |
|---|---:|---:|---:|---:|
| fixed (the plan) | 0.000 | 0.000 | 0.000 | 0.000 |
| local retrograde | 17.598 | 13.272 | 3.060 | 6.012 |
| pursuit | 6.149 | 11.012 | 0.143 | 0.006 |
| 1 fixed segment | 15.325 | 12.176 | 2.979 | 6.018 |
| 2 fixed segments | 22.164 | 17.063 | 3.978 | 8.731 |
| 4 fixed segments | 29.197 | 22.282 | 4.759 | 11.851 |
| 8 fixed segments | 34.044 | 26.447 | 5.249 | 15.099 |
| 128 fixed segments | 39.031 | 31.115 | 5.766 | 22.768 |

and the same table in kilometres:

| Law | Uranus 1 g | Uranus 0.3 g | Mars 0.3 g | Mercury 1 g |
|---|---:|---:|---:|---:|
| fixed (the plan) | 0.000 | 0.000 | 0.000 | 0.000 |
| local retrograde | 60,999 | 143,781 | 9,531 | 61,508 |
| pursuit | 25,268 | 61,064 | 1,104 | 3,890 |
| 1 fixed segment | 58,628 | 140,363 | 9,473 | 61,661 |
| 2 fixed segments | 72,965 | 173,387 | 11,056 | 76,737 |
| 4 fixed segments | 85,091 | 202,210 | 12,272 | 90,419 |
| 8 fixed segments | 93,301 | 222,458 | 13,070 | 101,033 |
| 128 fixed segments | 103,352 | 247,833 | 13,997 | 116,695 |

**The cosine loss is not spendable.** At Uranus 1 g the held direction is 16.85%
off the local retrograde on average, which is 13.68 km/s of the 81.20 km/s the
brake spends. That sounds like a penalty and is not: the brake's job is to remove
the velocity the coast arrives with **and** to turn it into the velocity the
capture chain needs, and the perpendicular component is the turn. A burn aligned
with the retrograde velocity can only remove speed; it leaves the direction
change undone, and the 17.6 km/s seam the local-retrograde law leaves is exactly
that undone change. So the loss is the work, not the waste, and tracking the
velocity cannot recover it.

**Re-aiming makes the seam worse, and more segments make it worse still.** One
segment holds the velocity direction at the burn's start and leaves 15.3 km/s at
Uranus 1 g; two leave 22.2, four 29.2, eight 34.0 and 128 leave 39.0, and the
growth is monotone at every case, in velocity and in position. The angle column
shows why: the tracking direction turns 78.27° over the burn, so a schedule
sampled from the plan's own path walks the thrust off the chord a step at a
time, and the more of that schedule it replays the further off it walks. The
closed-loop **local** law is better than a heavy schedule (17.6 km/s) only
because it follows its own velocity instead of the plan's, and it is still worse
than holding one direction. The **pursuit** law is the best of the local
family, because it aims at the chord instead of at the instantaneous velocity,
which is why it is nearly exact at Mercury (6 m/s, where the brake starts 1.50°
off retrograde and the field contributes little over the burn). It is still not
exact where the field does real work: 6.1 km/s at Uranus 1 g and 11.0 at 0.3 g,
where it aims at the velocity the burn has left to find and ignores that the
field is also delivering part of it. The planner's fixed point already accounts
for that.

## The decision: keep the fixed inertial direction

Both burns keep the fixed direction, at every level and target measured.

* **It is exact at the seam by construction.** The direction is solved, not
  chosen: the brake's is the change the chain needs and the departure burn's is
  the escape ray. Its penalty against any other law is therefore zero, and every
  alternative has a positive one — 17.6 / 13.3 / 3.1 / 6.0 km/s for the local
  retrograde and 6.1 / 11.0 / 0.14 / 0.006 for pursuit.
* **A segmenting solve is a shooting solve with no measured prize.** It would
  re-aim every segment, pay the brake's fixed point for it, and reach a
  seam that is already zero. One segment is 15.3 km/s at Uranus 1 g and more
  segments are worse at every count. Do not pay for it.
* **The spend is `a·T`, and nothing here shortens `T`.** A fixed-magnitude burn's
  delta-v is its acceleration times its duration; changing the attitude changes
  neither. Only a shorter brake would spend less, and it would hand the residual
  speed to the capture chain — that is [the entry-speed
  question](#entry-speed-is-an-output), not the steering law.
* **The departure burn is prograde by construction.** The escape ray is the
  departure frame's velocity direction at every sample, so there is nothing for
  a tracking law or a segment to improve.

Revisit this only on evidence: a burn whose fixed direction cannot be solved to
the seam gate is the one case that would justify a segmenting solve, and then
its segments should aim at the chord the burn must close, not at the local
velocity.

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
* The capture chain takes an **entry radius**. The burn–coast–burn passes the
  arrival handover floor (raised to the parking radius where the floor is deeper
  than the chain's own periapsis); the coast and the powered conic/hybrid arrival
  keep the sphere of influence. Measured at the epoch, the Uranus fall at 1 g
  drops from 117.13 days to 31 minutes and the profile's floor from 150.73 to
  34.16 days.
* The global `EntrySpeedMps` is still the profile's entry condition, now per plan
  through `FlightSession`; making the brake produce the residual entry speed is
  the next decision.
* The coast, the quintic, and the `0 g` identity are unchanged. The coast profile
  keeps its sphere-of-influence entry and its anchors.

# Open questions

* **The entry speed.** The entry *radius* is implemented. Whether the brake
  produces the residual entry speed, and from what rule, waits on the measured
  trade above: a hotter entry shortens the fall but grows the periapsis burn.
* **The approach model.** A deep floor entry leaves the coast ending inside the
  target's sphere — 0.007 spheres at Uranus 1 g and 400 days — where a
  heliocentric conic is a poor model. A planetocentric approach leg, a hotter
  entry, or a full-field coast are the candidates; the measurement is in
  [the binding question](#the-binding-question-where-the-coast-ends).
* **The steering law.** *Settled 2026-11-20:* both fixed burns hold the **fixed
  inertial direction**. Measured at the epoch at 0.3 g and 1 g, the direction is
  exact at the seam, the local retrograde leaves 3.1–17.6 km/s and pursuit leaves
  up to 11.0, and 2, 4 and 8 segments are monotonically worse. The departure
  burn's escape ray is the departure frame's prograde by construction. The
  measurement is in [steering starts simple](#steering-starts-simple).
* **Band versus solved radius.** *Settled for this plan:* the departure endpoint
  is the ceiling and the arrival endpoint is the floor (or the parking radius
  where the floor is deeper), with the floor clamped to the body clearance.
  *Still open:* whether that rule should give way to an interior solved radius,
  if a measurement of the radius rule shows it costs real delta-v or time. The
  steering measurement does not bear on it.
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
