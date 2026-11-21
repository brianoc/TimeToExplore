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

Two changes came out of that. The **handover floor** puts the chain's entry where
the drive still dominates the field, so the fall is minutes rather than months
([the handover band](#the-handover-band)); and the **planetocentric approach**
takes the deep part of the final approach away from a Sun-only conic
([the arrival leg](#the-arrival-leg-a-planetocentric-approach)). The entry
*speed* is still a planted input ([entry speed is an
output](#entry-speed-is-an-output)).

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

**The two-tier boundary stands, with two probe qualities below it.** The floor
search's first probes keep the cheap two-body burn: a probe only has to decide
whether a duration fits, and the reported plan is rebuilt and certified with the
summed field. Step 2 added a **full-field probe** for the continuation search
after a refused certificate: its burns integrate the summed field, so its
closure decision is the reported build's, while its departure solve (64
samples), measurement (a fixed burn's commanded acceleration, a conic's zero, a
break against its own body), and skipped settle passes keep it cheap. Neither
probe is ever reported. Every fixed burn in the certified build is full-field —
the departure burn, the brake, and the hybrid capture's injection arc — and a
full-field burn is one arc even when it crosses a sphere of influence. There is
still no split at an SOI.

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
coarser brake step, was rejected at step 7 because the 4 s target step already
clamped at 2,048 samples for every measured brake. **Step 1 then took that step
deliberately as a fixed 512** (`TripPlanner.BrakeSamples`) **for the certified
build** — a fast probe keeps `FiniteBurnArc.MaximumSamples`, because the probe's
brake is a search input — and made the pinned departure solve integrate its
**trial burns at 128 samples** (`PinnedDepartureSamples`), while the reported
departure burn stays at **512**.
The solve and the reported arc therefore integrate at different resolutions, so
the coast is re-solved from the **reported** burn's own end: the position seam is
exact and only the velocity seam carries the sample-count difference (metres and
milli-metres per second). The six fixed-duration plans are **identical to four
decimals**, a warm Release .NET build is **2.9–3.9× faster**, and the worst of
the six seams is Mercury's 9.9 m / 4.1 × 10⁻³ m/s. `FiniteBurnArc.MaximumSamples`
itself and the hybrid capture's injection burn are unchanged. The measurements
are in
[planning performance](/technical/planning-performance.md#step-1-the-builds-own-cost-2026-11-20).
**Step 2 adds the full-field probe** the continuation search uses: its burns
integrate the summed field, its departure solve is 64 samples with no 512
fallback, its brake stays at 512, and its measurement is the cheap one. It is a
search input; the certified build is always the answer
([step 2](/technical/planning-performance.md#step-2-the-searches-probes-are-cheap-and-the-answer-is-certified-2026-11-20)).

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

# The arrival leg: a planetocentric approach

The brake ends at the floor, and the brake's start is whatever the coast's end
used to be. On a long trip that end sat **deep inside the target's sphere** —
0.0071 spheres at Uranus 1 g and 400 days — where a Sun-only conic is a poor
model. The fix is a **planetocentric approach leg**: the arrival sequence is

```text
break → departure burn → heliocentric coast → approach conic → brake → capture chain
quintic   fixed burn        Sun conic          target conic    fixed burn  conic chain
```

1. **The heliocentric coast ends on the target's ceiling**, `1.02 × SOI`. At
   Uranus's ceiling — `5.28 × 10¹⁰ m` — the target's gravity is
   `2.08 × 10⁻⁶ m/s²`, about **three and a half times** the Sun's tide across
   the sphere (`5.9 × 10⁻⁷ m/s²`, `2 GM_sun r / a³` with
   `a = 2.87 × 10¹² m`). That is the conventional sphere boundary and no more:
   the body has only just become the larger term, so a Sun-only conic is fair
   *outside* the ceiling and the planetocentric leg takes over *inside* it. It
   is not a region where the target overwhelmingly dominates.
2. **The approach is a `ConicArc` about the target**, from the ceiling to the
   brake's start. Its entry state at the ceiling *is* the coast's arrival state
   relative to the target, so the seam is a state, not a model change.
3. **The brake is unchanged**: a full-field fixed burn whose start is the
   approach's end and whose end is the chain's entry state at the floor.
4. **The capture chain is unchanged**, entering at `max(band floor, conic
   parking radius)` at the per-plan `EntrySpeedMps`.

The approach is built **backwards from the brake's start**, because that is the
state a forward brake must leave from. Propagating the brake's start state
backwards along its target-centred two-body conic to the ceiling gives the
approach's start state and its duration; the coast's Lambert target becomes that
ceiling state, and the fixed point drives the coast's arrival velocity onto it.
The approach's duration is therefore whatever the free fall takes — short at a
fast arrival, long at a slow one — and it is not a solved or chosen quantity.

**The approach does not reach the floor, and that is the honest answer.** A
ballistic conic from the ceiling does not reach the band floor on any of the
measured plans: the conic through the brake's own start has a periapsis of
**84,354 km** at Uranus 1 g, **200,201 km** at 0.3 g and **10,059 km** at Mars
90 days, against floors of 38,643, 70,168 and 6,033 km (and 61,330 km against
2,806 km on the Mercury 8-day plan, whose brake starts outside the ceiling, so
it has no leg). The arrival carries too much angular momentum to fall that far
ballistically, so the brake still spans the last part of the descent — from
368,111 km to 38,643 km at Uranus 1 g. What the approach does is remove the
*heliocentric* model from the deep part of the final approach, not reach the
floor without a burn.

**The fast trips have no approach leg.** Where the brake's start is already
outside the ceiling there is no planetocentric region for a conic to describe:
at Mercury 8 days the brake starts 2,378,950 km out, 20.7 ceilings, and spans
the sphere itself. Those plans keep their original seven arcs and their numbers
are unchanged. A conic about Mercury at twenty spheres would be a worse model
than the heliocentric coast it replaced.

## What the approach measured

At the epoch (2026-11-15), from Gateway, conic capture. Old is the deep
heliocentric coast; new is the approach leg. The coast's end radius is measured
from the target's centre.

| Target, level, trip | Coast end radius old → new | Coast duration old → new | Approach | Brake start | Brake Δv | Total old → new |
|---|---|---|---:|---|---:|---:|---:|
| Uranus, 1 g, 400 d | 366,505 km (0.0071 SOI) → **52,795,224 km** (1.0000 ceiling) | 9,415.60 h → 9,234.54 h | — → **7.544 d**, 80.444 → 80.638 km/s | 366,505 → 368,111 km | 81.203 → 81.391 km/s | 182.339 → **182.312 km/s** |
| Uranus, 0.3 g, 400 d | 1,159,005 km (0.0224 SOI) → 52,795,224 km | 9,403.68 h → 9,225.45 h | — → **7.426 d**, 80.498 → 80.559 | 1,159,005 → 1,160,664 km | 79.749 → 79.809 | 180.842 → 180.834 |
| Mars, 0.3 g, 90 d | 92,790 km (0.161 SOI) → 588,786 km (1.0000) | 2,039.53 h → 2,033.50 h | — → **6.032 h**, 22.853 → 22.870 | 92,790 → 92,923 km | 18.506 → 18.522 | 44.356 → 44.354 |
| Mercury, 1 g, 8 d | 2,378,950 km (20.75 ceilings) | 164.19 h | none | 2,378,950 km | 213.302 | 557.038 (unchanged) |

The capture Δv is unchanged at 6.369 km/s (Uranus), 3.458 (Mars) and 3.498
(Mercury): it is set by the entry speed and the periapsis radius, and the
approach changes neither.

**The approach leg is not more expensive; it is marginally cheaper.** A fixed
burn's spend is `a·T`, so the brake's delta-v is the same to a few tenths of a
percent, and the 7.5 days the approach covers are days the coast no longer
flies. The small saving is the solar tide the old coast was commanding to hold
its heliocentric conic deep in the well: **0.2214 → 0.0069 km/s** of reported
third-body delta-v on the Uranus 1 g coast, 0.0727 → 0.0054 at 0.3 g, and
0.0528 → 0.0340 at Mars 90 days. At the slow end the saving is real and larger:
a 200-day Mars crossing was 12.458 km/s with a 0.901 km/s coast correction and is
**11.855 km/s** with a 27.3-hour approach and a 0.121 km/s coast. The cost of the
change is one more arc, one more seam, and the approach's own small third-body
correction (under 0.01 km/s).

**The seams close.** Coast → approach is **4.38 m and 1 × 10⁻⁶ m/s** at Uranus
1 g (0.53 m at 0.3 g, 2 × 10⁻⁶ m/s at Mars), and approach → brake is
**0.003 m** and 0.000 m/s — the approach is built from the brake's own start
state, so that seam is structural. The larger 4.38 m is the difference between
the coast's element-based propagation and the universal-variable propagator the
Lambert correction uses; it is two hundred times inside the 1 km gate and the
velocity seam is exact.

**The floors are re-measured by both changes, and the approach only moves a
floor whose brake starts inside the ceiling.** On the merged tree the editor's
1 g floor is **33.65372 d** and carries an approach arc (eight arcs, a
33.65–100.96 d window); the .NET 1 g floor is **30.55886 d** with no approach
(seven arcs), where the 128-sample certified solve converges at a duration the
old 512-sample build refused. The Uranus 0.3 g floors are **33.60438 d** in the
editor and **40.71332 d** in .NET, both seven-arc trips. Mercury and Mars are
unchanged because their brakes span the sphere. Measured at the epoch on the
merged tree:

| Floor | .NET harness | Editor | Editor window |
|---|---:|---:|---|
| Uranus, 1 g | **30.55886 d** | **33.65372 d** | 33.65–100.96 d |
| Uranus, 0.3 g | **40.71332 d** | **33.60438 d** | 33.60–100.81 d |
| Mercury, 1 g | **3.21610 d** | **3.21610 d** | 3.22–9.65 d |
| Mars, 0.3 g | **6.73042 d** | **6.73042 d** | 6.73–20.19 d |

## The chain's entry radius

The capture chain takes a **handover radius**, not a sphere of influence. Which
radius each profile passes is deliberate:

| Plan | Entry radius | Why |
|---|---|---|
| Burn–coast–burn, conic or hybrid capture | `max(band floor, conic parking radius)` | the brake ends there, so the chain's fall starts there |
| A trip that starts inside the target's sphere | the sphere of influence | there is no crossing and no brake to hand over |

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

The break and the conic/hybrid capture keep their jobs. Only the handover radius,
the field the burn is integrated in, and the arrival leg change.

The break is still the quintic that leaves the parking orbit and ends on the
handover at the local escape speed along the escape ray; the capture is still the
conic or hybrid chain the profile already uses. The break's duration solve, the
pinned fixed point, the escape-ray attitude, the brake's direction, the trim, the
transfer ellipse, and the circularisation are unchanged. No new arc *type* is
introduced — the approach is a `ConicArc` like the coast.

# What the floor entry measured

Measured at the epoch (2026-11-15), from Gateway, conic capture. The old build
enters on the target's sphere of influence at the planted 5 km/s; the new one
enters at the row's entry radius, with the same 5 km/s.

| Target, level, trip | Entry radius old → new | Inbound fall old → new | Capture Δv | Total old → new | Brake start radius | Brake Δv | Floor old → new |
|---|---|---:|---:|---:|---:|---:|---|
| Uranus, 1 g, 400 d | 51,760,023 km → 38,643 km | 117.13 d → 31 min | 6.37 km/s (both) | 242.1 → 182.3 km/s | 51.96 million km → **366,505 km** | 112.70 → 81.20 km/s | 150.73 d → **30.56 d** |
| Uranus, 0.3 g, 400 d | 51,760,023 km → 70,168 km | 117.13 d → 1.3 h | 6.37 km/s (both) | 242.5 → 180.8 km/s | 52.45 million km → **1,159,005 km** | 112.83 → 79.75 km/s | 160.47 d → **40.71 d** |
| Mercury, 1 g, 8 d | 112,410 km → 2,806 km | 6.09 h → 3.3 min | 3.50 km/s (both) | 569.9 → 557.0 km/s | 2.64 million km → 2.38 million km | 216.29 → 213.30 km/s | 3.46 d → **3.22 d** |
| Mercury, 1 g, 12.3 d | 112,410 km → 2,806 km | 6.09 h → 3.3 min | 3.50 km/s (both) | 379.9 → 376.9 km/s | 1.21 million km → 1.07 million km | 141.17 → 142.29 km/s | — |
| Mars, 0.3 g, 90 d | 577,241 km → 6,033 km | 1.3187 d → 13.6 min | 3.46 km/s (both) | 45.38 → 44.36 km/s | 658,870 km → **92,790 km** | 19.13 → 18.51 km/s | 8.03 d → **6.74 d** |

The trend is the one the design predicted, and it is bigger than the fall alone:
the 104-day Uranus fall is gone, and because the coast absorbs the time the
profile used to spend falling, the floor moves fourfold at Uranus (150.73 → 33.65
days at 1 g in the editor, 30.56 in the .NET harness) and the 400-day plan is
*cheaper* as well as shorter (242 → 182
km/s). A slower profile pays by carrying speed longer: the 400-day coast is 392
days at 1 g and the burn and brake are 90.2 and 81.2 km/s against 118.8 and
112.7.

**The Uranus floors were re-measured three times, and the last word is the
merged tree.** Step 7's cheaper active-set cadence first moved them to
**34.16041 d** at 1 g and **40.71332 d** at 0.3 g; the planetocentric approach
leg changes the arrival the near-floor basin first closes at; and the
first-plot plan's 128-sample certified solve converges at a lower 1 g duration
than the 512-sample build did. On the merged tree the .NET harness returns
**30.55886 d** at 1 g (2,299.29 km/s, seven arcs) and **40.71332 d** at 0.3 g
(1,801.17 km/s, seven arcs), and the editor returns **33.65372 d** at 1 g
(2,070.02 km/s, eight arcs — the one floor plan whose brake starts inside the
ceiling) and **33.60438 d** at 0.3 g (2,300.78 km/s, seven arcs). Mars
(6.73042 d) and Mercury (3.21610 d) did not move. These are re-measured anchors,
not errors: a floor is the first duration the certified full-field build closes
at, and the basin is noisy. Every returned floor is a certified plan.

**Step 1 re-measured them once more, because a cheaper certified build is a
different integration.** With the certified brake at 512 and the departure solve
at 128, the .NET harness floors are **6.73715 d** at Mars 0.3 g, **3.21610 d** at
Mercury 1 g, **30.55886 d** at Uranus 1 g, **40.71332 d** at Uranus 0.3 g, and
**13.33520 d** at Jupiter 0.3 g. Four are unchanged. **Uranus 1 g moves from
34.16041 to 30.55886 d** (2,037 → 2,299.29 km/s): the 128-sample certified solve
converges at durations where the 512-sample one stalled about 1.1 × 10⁻⁵ m/s
above the tolerance, so the cost shield that used to buy a refusal no longer
does. It is a re-measured floor, not an error. The plan is certified —
`IsFeasibleAt` holds, its worst seam is 6.7 m / 8.4 × 10⁻⁴ m/s, and a cold
rebuild at the floor closes at 0.4 m / 1.0 × 10⁻³ m/s — and the 1 g window
becomes **30.56–91.68 d** (a direct build at the maximum is 736.02 km/s). At
0.3 g the returned floor stays 40.71332 d; the certified build now also closes
at 40.5, 40.0, and 39.0 d directly (inside the gate) while the fast probes still
refuse them, so the search is conservative there. Step 3 re-anchored the floors
and windows, and the first-plot plan's close-out records them as final
([step 1](/technical/planning-performance.md#step-1-the-builds-own-cost-2026-11-20)).

**Step 2 changed the search, not the physics, but one editor floor moved.** The
full-field probe quality re-measured the editor's Jupiter floor from **13.27662
to 13.34149 d** (+0.49%): Mono refuses the fast high, and the certified duration
the probe continuation finds is a different point in the near-floor scatter. The
window follows to **13.341–40.024 d**. Every other measured floor is unchanged —
Mars 6.73715 d, Mercury 3.21610 d, Uranus 33.60438 d at 0.3 g and 31.25256 d at
1 g at that step (the 1 g floor is re-measured again on the merged tree, above) —
and the .NET Jupiter floor stays 13.33520 d, so the runtime divergence the
plan records is still there. The moved plan is certified and every seam is
inside the gate; the measurement and the probe/certificate boundary are in
[step 2](/technical/planning-performance.md#step-2-the-searches-probes-are-cheap-and-the-answer-is-certified-2026-11-20).

**Step 3 re-measured the floors once more, because the brake pin's pass cap was
holding them up.** A certified build whose outer brake/departure fixed point
runs its twelve-pass cap with the mismatch still falling refuses at the chain
gate on a residual that the trip time does not deserve (Jupiter 0.3 g at
13.07211 d: 0.137 m/s at pass 11 reads as a 28,069.5 m miss). The certified
build now retries that same fixed point once at **24 passes** when the pin
stalled and the chain gate refused; no tolerance, no gate, and no search rule
moved, and a build that converges never pays it
([the retry](/technical/planning-performance.md#the-fix-one-solver-retry-inside-the-certified-build)).
The floors that move are the ones whose certificate was such a refusal: the
editor's Jupiter floor is **13.34149 → 13.07211 d** (2,270.99 km/s, peak
9.700 m/s², worst seam 93.35 m / 8.0 × 10⁻³ m/s in .NET) with the window
**13.072–39.216 d**, and Mars at 0.3 g is **6.73715 → 6.73042 d** (1,171.61
km/s, seams 9.8 m / 2.5 × 10⁻² m/s) with the window **6.730–20.191 d**, in both
runtimes. Uranus (33.60438 d at 0.3 g; its 1 g floor is re-measured below),
Mercury (3.21610 d),
and the .NET Jupiter floor (13.33520 d) are unchanged, because their
certificates are accepted at twelve passes. All moved plans are certified and
every seam is inside the 1 km / 1 m/s gate; the six fixed-duration Δv anchors
did not move.

**Step 4 closed the first-plot plan with the final floors and windows.** The
editor is the runtime that ships, so its first `Plot()` measurements are the
anchors the docs carry: **Uranus 0.3 g 33.60438 d** (window 33.60–100.81 d),
**Uranus 1 g 33.65372 d** (33.65–100.96 d), **Mars 0.3 g 6.73042 d**
(6.73–20.19 d), **Mercury 1 g 3.21610 d** (3.22–9.65 d), and **Jupiter 0.3 g
13.07211 d** (13.07–39.22 d). Every one is a certified full-field plan: the
returned duration is rebuilt with the summed field, every arc has its measured
clearance and delta-v, and every seam is inside the 1 km / 1 m/s gate. Each of
the five now certifies on its **first** certificate with no continuation probes,
so the floor is a single certified build rather than the end of a search; the
measurements are in [step 4 of planning
performance](/technical/planning-performance.md#step-4-the-bracket-was-not-needed-2026-11-20).

**The runtimes disagree near the needles, and both are right.** The .NET harness
measures **40.71332 d** at Uranus 0.3 g, **30.55886 d** at Uranus 1 g, and
**13.33520 d** at Jupiter, against the editor's 33.60438, 33.65372, and 13.07211.
This is a **property of the near-floor scatter, not a bug**: feasibility there
is decided by whether the departure Newton stalls and by how many passes the
brake pin needs, so a last-bit libm difference between Mono and .NET selects a
different point of a noisy basin. Each reported floor is still a certified
full-field plan inside the gate, and the editor's is the one that ships; the
.NET number is the harness's own measurement, not a competing answer.

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

These numbers are the **floor entry's own measurement** (step 4). The approach
leg moved the brake's start a few hundred kilometres outward at Uranus and left
the entry radius, the fall and the capture's delta-v unchanged. On the merged
tree the floors are **33.65 / 33.60 / 3.21610 / 6.73042 days in the editor** and
**30.56 / 40.71 / 3.21610 / 6.73042 in the .NET harness** at Uranus 1 g, Uranus
0.3 g, Mercury 1 g and Mars 0.3 g; the totals are 182.312 / 180.834 / 557.038 /
44.354 km/s at the fixed-duration anchors. The .NET floors are seven-arc fast
trips with no approach; the editor's 1 g floor is the one floor plan whose brake
starts inside the ceiling, so it carries an approach arc and eight arcs in all.
See [what the approach measured](#what-the-approach-measured).

## The binding question: where the coast ends — resolved

The brake ends at the floor, and the brake's start is the coast's end. A fixed
burn at `a` covers `Δv / a` seconds, so the distance it spans depends on the trip:
the **long** trips hand the coast a final leg inside the target's sphere, and the
**fast** ones make the brake span the sphere itself.

This was the plan's open problem: at Uranus 1 g and 400 days the brake was
139.5 min of 81.2 km/s and started **366,505 km** from Uranus — **0.0071 × the
sphere**, where Uranus's gravity is 0.043 m/s² — so the last part of the coast was
a heliocentric conic in a region where the target dominates. It is **resolved by
[the planetocentric approach leg](#the-arrival-leg-a-planetocentric-approach)**:
the coast now ends on the ceiling, a conic about the target covers the deep part,
and the brake starts where that conic ends. The measured comparison is in
[what the approach measured](#what-the-approach-measured): the leg is 7.54 days at
Uranus 1 g and 400 days, costs 27 m/s *less* in total delta-v than the deep
coast, and removes the 0.221 km/s of solar-tide correction the deep coast was
commanding.

What remains true is the geometry that ruled out the simpler reading — the
approach ending *at the floor* with no brake across it. A ballistic conic from the
ceiling does not reach the floor on any measured plan: its periapsis is above the
floor at Uranus (84,354 km against 38,643 at 1 g; 200,201 against 70,168 at 0.3 g),
Mars (10,059 against 6,033) and Mercury (61,330 against 2,806). The brake
therefore still spans the last part of the descent, and it is the brake, not the
approach, that ends on the floor.

* **Fast trips.** Where the brake's start is outside the ceiling there is no
  planetocentric leg. At the .NET floors the brake starts 50.55 million km from
  Uranus at 30.56 d, 0.976 spheres, and spans the sphere-to-floor gap itself.
  Mercury's floor (3.21610 d) puts the brake's start at 31.05 million km, **276
  spheres** out; Mars's floor (6.73042 d) at 48.30 million km, **83.7 spheres**
  out. At the fast end the coast ends outside the ceiling and the brake is the
  long crossing. The editor's 1 g floor (33.65372 d) is the one floor plan whose
  brake starts inside the ceiling, so it carries an approach arc.
* **Long trips.** The approach absorbs the deep part: 7.54 days at Uranus 1 g and
  400 days, 6.03 hours at Mars 90 days, 27.3 hours at Mars 200 days — and, as
  designed, it is long exactly when the arrival is slow.

The brake is never *wrong* about the field: every fixed burn integrates the summed
field, so even the 28.6-hour Uranus brake feels Uranus. The approach leg fixes the
other side of the model: the coast is now a Sun-only conic only where the Sun
dominates, and the target's own conic describes the well. The remaining choice in
this area is the entry speed, which is a [separate
question](#entry-speed-is-an-output).

# Entry speed is an output

Today the entry speed is an **input**. `EntrySpeedMps = 5000` is global and shapes
every capture; changing it moves every conic plan, including the inner-system
ones where the current entry works. `FlightSession` already copies it per plan
(the retime's request carries it), so a per-plan value is possible without moving
the default; this step kept the default and the per-plan input exactly as they
were.

**The approach leg does not change that.** The planetocentric fall ends where the
brake begins, and the brake still hands the capture chain its entry state at the
per-plan `EntrySpeedMps` — 5 km/s by default. The residual is still *planted*, not
produced: the approach is built from the brake's start and the coast, so no part
of the solve measures the speed at which the ship would actually leave the brake.
The measured tables above are what the residual decision needs; the approach leg
removes the model gap, not the planted input.

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
0.3 g, and Mercury at 8 days and 1 g. The measurement was **re-run after the
approach leg landed** (2026-11-20) on the same four plans: the brake now starts
where the planetocentric approach ends, so the brake rows below are the new
ones, and the decision is unchanged. For each finite-burn arc — the departure
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
| Uranus, 1 g, 400 d | 2.331 h | 81.39 km/s | 368,111 → 38,643 km | 78.25° | 9.62° → 87.54° | 0.831974 | 16.80% = 13.68 km/s |
| Uranus, 0.3 g, 400 d | 7.535 h | 79.81 km/s | 1,160,664 → 70,167 km | 75.32° | 7.17° → 82.39° | 0.884438 | 11.56% = 9.22 km/s |
| Mars, 0.3 g, 90 d | 1.749 h | 18.52 km/s | 92,923 → 6,033 km | 30.35° | 9.39° → 39.71° | 0.939394 | 6.06% = 1.12 km/s |
| Mercury, 1 g, 8 d | 6.108 h | 213.30 km/s | 2,378,950 → 2,806 km | 68.13° | 1.50° → 69.64° | 0.979441 | 2.06% = 4.39 km/s |

The seam each law leaves, against the plan's burn end state, in km/s:

| Law | Uranus 1 g | Uranus 0.3 g | Mars 0.3 g | Mercury 1 g |
|---|---:|---:|---:|---:|
| fixed (the plan) | 0.000 | 0.000 | 0.000 | 0.000 |
| local retrograde | 17.597 | 13.272 | 3.060 | 6.012 |
| pursuit | 6.157 | 11.019 | 0.143 | 0.009 |
| 1 fixed segment | 15.329 | 12.177 | 2.979 | 6.018 |
| 2 fixed segments | 22.166 | 17.063 | 3.978 | 8.731 |
| 4 fixed segments | 29.204 | 22.284 | 4.759 | 11.851 |
| 8 fixed segments | 34.058 | 26.450 | 5.249 | 15.100 |
| 128 fixed segments | 39.054 | 31.120 | 5.767 | 22.768 |

and the same table in kilometres:

| Law | Uranus 1 g | Uranus 0.3 g | Mars 0.3 g | Mercury 1 g |
|---|---:|---:|---:|---:|
| fixed (the plan) | 0.150 | 0.140 | 0.250 | 0.060 |
| local retrograde | 61,131 | 143,882 | 9,539 | 61,508 |
| pursuit | 25,332 | 61,122 | 1,105 | 3,890 |
| 1 fixed segment | 58,763 | 140,466 | 9,481 | 61,661 |
| 2 fixed segments | 73,128 | 173,513 | 11,065 | 76,739 |
| 4 fixed segments | 85,292 | 202,364 | 12,283 | 90,420 |
| 8 fixed segments | 93,523 | 222,628 | 13,082 | 101,034 |
| 128 fixed segments | 103,601 | 248,024 | 14,010 | 116,695 |

**The cosine loss is not spendable.** At Uranus 1 g the held direction is 16.80%
off the local retrograde on average, which is 13.68 km/s of the 81.39 km/s the
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
shows why: the tracking direction turns 78.25° over the burn, so a schedule
sampled from the plan's own path walks the thrust off the chord a step at a
time, and the more of that schedule it replays the further off it walks. The
closed-loop **local** law is better than a heavy schedule (17.6 km/s) only
because it follows its own velocity instead of the plan's, and it is still worse
than holding one direction. The **pursuit** law is the best of the local
family, because it aims at the chord instead of at the instantaneous velocity,
which is why it is nearly exact at Mercury (9 m/s, where the brake starts 1.50°
off retrograde and the field contributes little over the burn). It is still not
exact where the field does real work: 6.2 km/s at Uranus 1 g and 11.0 at 0.3 g,
where it aims at the velocity the burn has left to find and ignores that the
field is also delivering part of it. The planner's fixed point already accounts
for that.

## The decision: keep the fixed inertial direction

Both burns keep the fixed direction, at every level and target measured.

* **It is exact at the seam by construction.** The direction is solved, not
  chosen: the brake's is the change the chain needs and the departure burn's is
  the escape ray. Its penalty against any other law is therefore zero — the
  fixed law reproduces the plan's burn end state to 0.15 m and 0.00 m/s, which
  is the reverse-flow integration's own round-off — and every
  alternative has a positive one — 17.6 / 13.3 / 3.1 / 6.0 km/s for the local
  retrograde and 6.2 / 11.0 / 0.14 / 0.009 for pursuit.
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
  probe keeps the two-body burn, and the full-field probe keeps the summed
  field with a cheap solve and measurement.
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
  **33.65 d in the editor** (33.60 at 0.3 g; the .NET harness measures 30.56 and
  40.71 because the near-floor scatter picks a different certified point).
* The global `EntrySpeedMps` is still the profile's entry condition, now per plan
  through `FlightSession`; making the brake produce the residual entry speed is
  the next decision.
* The arrival leg is **planetocentric from the target's handover ceiling down**.
  The heliocentric coast ends on `1.02 × SOI`, a `ConicArc` about the target
  covers the deep part of the final approach, and the brake starts where that
  conic ends; the fast trips, whose brake starts outside the ceiling, keep their
  original shape.
* The crossing is always burn–coast–burn; the powered quintic cruise, the plain
  coast, and the `0 g` identity are retired.

# Open questions

* **The entry speed.** The entry *radius* is implemented. Whether the brake
  produces the residual entry speed, and from what rule, waits on the measured
  trade above: a hotter entry shortens the fall but grows the periapsis burn.
  The approach leg does not produce it either.
* **The approach model.** *Settled 2026-11-20:* the arrival leg is a
  planetocentric `ConicArc` from the ceiling to the brake's start, built backwards
  from the brake's own start state. Measured at the epoch it is 7.54 days and
  27 m/s cheaper at Uranus 1 g and 400 days, 2 m/s cheaper at Mars 90 days, and
  0.60 km/s cheaper at Mars 200 days, and the floors barely move. The one thing it
  cannot do is reach the band floor without a burn: the ballistic periapsis is
  above the floor on every measured plan, so the brake still spans the last part
  of the descent. The measurement and the numbers are in
  [the arrival leg](#the-arrival-leg-a-planetocentric-approach).
* **The steering law.** *Settled 2026-11-20:* both fixed burns hold the **fixed
  inertial direction**, re-measured with the approach leg's brake. Measured at
  the epoch at 0.3 g and 1 g, the direction is exact at the seam, the local
  retrograde leaves 3.1–17.6 km/s and pursuit leaves up to 11.0, and 2, 4 and 8
  segments are monotonically worse. The departure burn's escape ray is the
  departure frame's prograde by construction. The measurement is in
  [steering starts simple](#steering-starts-simple).
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
