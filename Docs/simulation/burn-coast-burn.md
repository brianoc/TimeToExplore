---
type: Design Document
title: Burn–coast–burn trips
description: The shipped trip profile that burns hard, coasts fast, and brakes into orbit, built from the four existing arc ingredients.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
---

# The trip

Leave a planet, cross to another, and park there. The ship:

1. breaks out of its parking orbit,
2. burns hard for a while and builds speed,
3. coasts with the drive off, still travelling fast,
4. flips and burns hard to slow down,
5. drops into the target's parking orbit.

This is the standard **burn–coast–burn** profile, sometimes called a
brachistochrone with a coast. It is not exotic physics. It is the profile most
torch-ship fiction uses, and it sits between the two extremes the planner
already has: the all-torch quintic (fast, ruinously expensive) and the
Hohmann-like coast (slow, cheap). It ships as
`TripPlanner.CruiseStyle.BurnCoastBurn`.

# Four ingredients, not four journeys

The planner started with three fixed recipes: all-quintic, conic arrival, and
hybrid arrival. The route colours made them look like three separate kinds of
trip. They are really three **arc shapes**, plus one solver, and a trip can mix
them. Since this profile landed, the nav computer separates the two questions:

* the **`PROFILE`** row chooses the crossing — **Powered** (the default),
  **Coast**, or **Burn-coast-burn**;
* the **`ARRIVAL`** row chooses the capture — **Quintic capture**, **Conic
  coast**, or the hybrid **X g burn**.

The new cruise profile is built from the same ingredients as the others:

| Ingredient | What it is | Drive | Best at |
|---|---|---|---|
| **Quintic** | a powered arc matching position, velocity, and acceleration at both ends; the thrust can point anywhere | continuous | the exact, fiddly ends: leaving a parking orbit and a gentle capture |
| **Conic** | a Keplerian free-fall arc | zero | the coast |
| **Fixed burn** | a powered arc at a constant commanded acceleration | fixed, e.g. 1 g | the long, visible "burn hard" legs |
| **Lambert** | not an arc: a solver that picks the conic connecting two positions in a chosen time | — | choosing the coast so it arrives on time |

**Lambert is not a slow mode.** It is the coast solver. The player chooses a
trip time; Lambert returns the coast that fits it; the burns supply the speed.
Thirty days to Mars is a fast coast. Two hundred and fifty-nine days is a slow,
cheap one. Same solver, different input.

# The five-arc profile

```text
leave orbit      burn hard        coast           brake          enter orbit
[ quintic ]  →  [ fixed burn ] → [ conic ]  →  [ fixed burn ] → [ capture ]
    gold             red             blue            red          blue/gold
```

Every arc type already existed. The planner now emits this recipe as
`TripPlanner.CruiseStyle.BurnCoastBurn`, and the nav computer's `PROFILE` row
selects it. `CruiseStyle.Coast` is the same coast without the two burns, and it
is reachable on its own.

The capture is deliberately the conic or hybrid capture, not a quintic: a
quintic capture cannot command less thrust than the parking orbit's own gravity,
so it has to park high around the giants. The conic/hybrid capture gets low and
cheap ([ADR-014](/decisions/adr-014-keplerian-coast.md),
[ADR-015](/decisions/adr-015-fixed-thrust-burns.md)). A quintic departure is
fine and is the right tool for the exact orbital break.

# Speed is the input

A straight-line kinematic estimate, ignoring gravity, for Earth–Mars at closest
approach (0.52 AU). It is a rule of thumb, not a plan:

| Total time | Burn at each end (1 g) | Peak speed | Total delta-v |
|---|---:|---:|---:|
| 2.5 d (no coast) | 1.25 d | 1,080 km/s | 2,160 km/s |
| 30 d | 50 min | 30 km/s | 60 km/s |
| 90 d | 17 min | 10 km/s | 20 km/s |
| 259 d | 6 min | 3.5 km/s | 7 km/s |

The coast is what makes the trip cheap. The faster the chosen time, the longer
the burns and the more delta-v they cost. Thirty kilometres per second is about
Earth's orbital speed: a genuinely fast interplanetary hop, not a drift.

# Why this is the right shape

- It reuses the arc types, the autopilot, the capture chain, the route colours,
  and the flight report. Nothing about the physics is new.
- It is deterministic and testable at each step, unlike a full optimal-control
  solve.
- It gives the time/delta-v trade a single player control.
- The original design already sketches this as the eventual answer; this is the
  tractable first version, not a detour.

The cost: it is not fuel-optimal, and the seams between a shaped arc, a fixed
burn, and a coast need care. That is an acceptable trade for a first flyable
profile.

# What shipped

1. **The Lambert solver.** In Core as `LambertSolver.SolveShortWay` (zero-rev,
   prograde, short-way). It returns `v₁`, `v₂`, and the swept transfer angle, and
   reports a rejected geometry through `HasSolution == false` rather than
   throwing.
2. **The coast skeleton.** `TripPlanner.CruiseStyle.Coast` retargets the quintic
   departure to the Lambert `v₁`, coasts on a heliocentric `ConicArc`, and hands
   the existing conic or hybrid capture its entry state. The handover is an
   impulse at the target's sphere of influence; at 200 days to Mars it is
   14 km/s in total.
3. **The burns.** `TripPlanner.CruiseStyle.BurnCoastBurn` is a quintic break →
   departure burn → Lambert coast → brake → conic or hybrid capture. The break
   ends on the departure clearance sphere (`1.02 × SOI`) at the local escape
   speed along the escape ray, and the departure burn starts there. The burn
   holds that ray — its prograde direction in the departure body's frame — and
   its duration and the coast's start are solved together: the burn is
   integrated forward, the Lambert coast from its end to the brake's start, and
   the two ray angles and the duration are driven until the burn's end velocity
   is the coast's departure velocity. The brake spends the handover the coast
   skeleton would have made an impulse. Every seam is continuous and the arrival
   is exact. The break's duration is solved as well: it keeps the reference
   coast's departure share while that is gentle enough for the drive, and
   lengthens it to the drive's limit when the share is too short.
4. **The control.** The nav computer's `PROFILE` row selects Powered, Coast, or
   Burn-coast-burn; the trip-time slider is the profile's speed control and its
   window is that profile's own feasible range; the existing burn-level slider
   is the level both burns are planned at.

The measured window at the epoch is **8.03 to 24.09 days** for the
Gateway→Mars burn–coast–burn profile at 0.3 g, against the powered plan's
4.4 days and the cheap coast's 200+. At 1 g the fast end is **3.46 days** to
Mercury. The right-hand end is the three-times cap: with the break gentle, the
fast end moved to 8.03 days and the cap is feasible. The 90-day Mars
plan in the table below is still a valid direct build, but it is outside this
window, so the nav computer's slider cannot ask for it. `Powered` is still the
default, and `0 g` is exactly `CruiseStyle.Coast`.

# The pinned break

The break's end state is a design decision: the point where the departure burn
starts, on the clearance sphere `1.02 × SOI`, at the local escape speed along
the escape ray. The break's only job is to leave the parking orbit and reach
escape; the burn supplies everything above it. The old chain solved the coast's
start and integrated the burn backwards, so at fast trip times the burn began
4.7 SOI out at 32 km/s and the break was fitted to that hard backwards solve.

The escape ray is the outbound asymptote the crossing needs, not the ship's own
relative velocity. A station's orbital direction has no relation to the
transfer, and a burn that follows the heliocentric velocity cannot start at
escape speed: at the boundary the heliocentric velocity is the departure body's
own orbital velocity, so the thrust would point along the body's motion rather
than at the target. Scanning the ray and duration of a velocity-following burn
at 8 days to Mercury found a best seam of 269 km/s; holding the escape ray
closes it below 1e-5 m/s.

The burn's ray angles and duration and the coast's start are one solve: three
equations (the burn's end velocity is the coast's departure velocity) in three
unknowns, with the burn integrated forward from the pinned state and the
Lambert coast running from its end to the brake's start. The departure burn's
duration is therefore no longer the coast's excess speed divided by the
acceleration; it is the duration that lands on the coast. The break's duration
is solved separately against the drive's limit: the reference coast's departure
share is kept while its peak thrust fits, and it is lengthened to the shortest
duration that fits when the share is too short. The burn's speed and the
coast's departure speed still set its delta-v, so the level still scales its
duration as `1/level`.

Measured at the epoch (2026-11-15) from Gateway at 1 g (9.70 m/s²), conic
capture:

| Target | Days | Break (d) | Break peak (m/s²) | Break Δv (km/s) | Burn (d) | Burn Δv (km/s) | Coast (d) | Brake (d) | Brake Δv (km/s) | Total Δv (km/s) | Feasible |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| Mercury | 8 | 0.553 | 3.02 | 92.9 | 0.307 | 257.1 | 6.594 | 0.258 | 216.3 | 569.9 | yes |
| Mercury | 6 | 0.421 | 5.24 | 122.9 | 0.411 | 344.0 | 4.523 | 0.358 | 299.7 | 770.1 | yes |
| Mercury | 12.32 | 0.823 | 1.35 | 61.6 | 0.207 | 173.2 | 10.834 | 0.168 | 141.0 | 379.3 | yes |
| Mercury | 12.3 | 0.822 | 1.35 | 61.7 | 0.207 | 173.5 | 10.815 | 0.168 | 141.2 | 379.9 | yes |
| Mars | 90 | 4.787 | 0.033 | 9.0 | 0.016 | 13.7 | 83.815 | 0.023 | 19.1 | 45.4 | yes |

The Mars row is a direct build at 90 days; after the pin the nav computer's Mars
window is 8.03–24.09 days, so the slider cannot reach 90 days. At 0.3 g the
90-day build costs 45.4 km/s with the same gentle break (4.79 d, 9.0 km/s,
0.03 m/s²), a 78-minute departure burn, an 83.7-day coast, and a 1.8-hour brake.

The 8-day trip now closes at 1 g: the break's peak is 3.02 m/s² and the plan's
9.70 m/s² peak is the two fixed burns at the commanded level. The break ends at
1.02 SOI at 0.919 km/s, the local escape speed, instead of 4.77 SOI at
32.2 km/s, and the 8-day total falls from 1,231 to 570 km/s. The 6-day trip also
closes, at 770 km/s. The fast Mercury seams are continuous to better than a
millimetre in position and 1e-5 m/s in velocity, and the 8-day flight arrives
0.05 m and 4.4 × 10⁻⁵ m/s from the reference state on the conic parking orbit.

The **break Δv** is the integrated thrust over the quintic, not the speed change
at the boundary. The quintic still cancels the station's motion and flies the
distance, so a gentle break spends tens of kilometres per second even though it
ends at 0.92 km/s. What the pin removes is the 53 m/s² peak and the
four-sphere-long backwards burn.

# The break search

The break's duration is found by a **two-tier** solve. The search itself measures
the quintic against the departure body alone, in that body's frame: the break's
meaningful field is its own central body, and the full catalogue at every sample
is what made the old search cost 4.88 ms per probe. The quintic is still built
with **full-field endpoint accelerations**, exactly as the final plan builds it,
so the search and the plan share one curve; only the along-arc gravity
subtraction is reduced. That choice keeps the final plans closest to the
pre-two-tier plans, which the measured anchors confirm.

The plan that leaves the planner is always rebuilt and measured with the full
field. The fast estimate is a search input only. When the fast peak comes within
**5%** of the search's drive limit — `MaxAccelerationMps2 * ThrustMargin`, 9.7
m/s² at 1 g — the probe is certified with the full field instead. The
certification prepares the field once at the break's start, where the departure
body and its moons are strongest, and evaluates that selected set at the same 64
samples `Measure` uses. The break only climbs outward, so the prepared set stays
a superset of the bodies that matter along it; this is the full field the plan
reports, without recomputing every catalogue ephemeris per sample. A fast
estimate can therefore never talk an over-thrust break past the drive.

The constant is `TripPlanner.BreakScreenMargin`. It is a guard, not a licence:
inside the band the full field decides, and the returned plan's peak is still the
full-field number.

Measured at the epoch, Mercury at 1 g, conic capture, on a throwaway profiling
copy (never committed):

| Quantity | Step 1 | After step 2 |
|---|---:|---:|
| One `Plan()` | 12.0 s | **1.5 s** |
| One `PlanAtDuration(8 d)` | 105 ms | **95 ms** |
| One Mars 0.3 g `Plan()` | — | **0.85 s** |
| First selection, Mercury (Plan + window + retime) | — | **1.5 s** |
| First selection, Mars (Plan + window + retime) | — | **0.93 s** |
| Repeat selection, same date/target/level/style | — | **under 0.1 ms** |
| Slider retime | 105 ms | **93–99 ms** |

The break-search cost fell by **8.1×** in step 1. Step 2 cut the rest by
searching the floor on the same fast model and settling the pinned solve on a
physical tolerance instead of a fixed pass count. The pins, floors, and reported
plans did not move: 8 d Mercury is still 3.02 m/s² and 569.9 km/s, 12.3 d is
1.35 and 379.9, Mars 90 d is 0.033 and 45.4, and the floors are still 3.46 d and
8.03 d. The fast peak tracks the full-field peak to 0.18% at 8 d Mercury, 0.41%
at 12.3 d, 4.5% on the very gentle 90-day Mars break, and 0.05% at the Mercury
floor.

# The floor after the pin

With the break gentle, the fast end is no longer set by an over-thrusting
break. `Plan()` searches for the shortest trip whose chain closes and whose
peak fits the drive, and the search now separates **finding** the floor from
**reporting** it. Every growth, walk-back and bisection probe runs through
`Build`'s fast mode: a fixed burn measures its commanded acceleration, a conic
coasts, and a quintic (the break) is measured against its own reference body.
The probe's burn is integrated at 128 samples instead of the final 512, delta-v
and clearance are not tracked, and the pinned brake/departure fixed point stops
on a loose tolerance. The candidate the search returns is **rebuilt at 512
samples with the full field** and certified before it leaves the planner; the
fast numbers are never reported. If the fast model was optimistic, the search
finishes with full-field builds seeded from the fast bracket.

The bracket is found by probing a little above the planner's kinematic estimate,
walking down in 15% steps to the first refused request, then bisecting to 0.05%
(or 30 s, whichever is larger). A refused probe is retried 0.2% higher, because
the near-floor chain has narrow non-convergence holes; that retry is the search's
real resolution. The floors measured after the change are **3.462 d** to Mercury
at 1 g and **8.032 d** to Mars at 0.3 g — still 3.46 and 8.03 to the printed
precision. The near-floor basin is noisy, so the search still pays about twenty
probes; that is the remaining cost in `Plan()` and the trap for the slider step.

Measured at the epoch (2026-11-15) from Gateway, conic capture:

| Target | Level | True floor | Peak | Total Δv | Just below | Why the shorter trip is refused |
|---|---|---:|---:|---:|---|---|
| Mercury | 1 g | **3.46 d** | 9.70 m/s² | 1,760 km/s | 3.43 d | departure break needs 10.2 m/s², above the 10.0 m/s² drive; 3.44–3.45 d also leave the brake 375,000 km short of the capture entry |
| Mars | 0.3 g | **8.03 d** | 5.25 m/s² | 1,170 km/s | 7.80 d | departure break needs 58.1 m/s², above the 10.0 m/s² drive; 8.00 d leaves the brake 7,493 km short of the entry |

The floor search also requires the chain to be **closed**: a plan whose peak
fits but whose brake misses the capture entry is not flyable. The near-floor
trips amplify the brake solve's forward/backward mismatch, so the shortest
plan that fits the peak can end the brake hundreds of thousands of kilometres
from the entry; the planner reports that as a trip time too short for the
profile instead of handing it to the autopilot. The gate is a kilometre of
position and a metre per second of velocity: the 8- and 12-day trips close to
millimetres, the 8.03 d Mars floor's worst seam is under a kilometre, and a
broken brake misses by thousands of kilometres. The 8.03 d flight arrives 0.1 m
and 10⁻⁴ m/s from the reference parking state.

The pinned solve itself now stops on the brake's own physical mismatch (1e-7 m/s
at the brake's start) rather than the old `required.SqrMagnitude > 1e-18`, which
a real brake can never satisfy. The fixed 12-pass outer loop and the three
settle passes became a convergence loop plus up to three settles only while the
fixed point is still drifting; the final brake and arcs still close to better
than a centimetre and 1e-5 m/s on the tested trips. The Newton Jacobian is
reused for one iteration, and the coast's propagator correction gets sixteen
iterations so the long, low-level burns still close.

The preview's floor plan and trip-time window are cached per departure date,
target, burn level, cruise style, and arrival style in `FlightSession`. A repeat
selection reuses the plan and the window; a changed departure date (or a
teleport/place) drops the cache. A slider retime is one full-field
`PlanAtDuration` build; it runs on a latest-wins background worker so the handle
never blocks the frame, and every published preview is that full-field plan.
[Planning performance](/technical/planning-performance.md) records the two-tier
boundary, the screen, the cache, and the measured timings.

A plan rejected for exceeding the drive now carries an `InfeasibleReason`
naming the arc and the thrust it needs, for example *"the departure break
needs 10.2 m/s², above the 10.0 m/s² drive"*. The nav computer's flight panel
shows it in its `Note` row, and the details panel shows it beside the
"Plan infeasible" route button, so a rejection names its cause rather than
reporting only that the plan is infeasible.

# How it was cut

Two stages, both part of the same implementation:

**Stage A — the coast skeleton.** Add the Lambert solver and build a trip as a
quintic departure retargeted to the Lambert's `v₁`, a heliocentric `ConicArc`
coast, and the existing conic/hybrid capture. Landed as
`TripPlanner.CruiseStyle.Coast`.

**Stage B — the burns.** Add the departure fixed burn and the fixed-thrust
brake, stitch the five arcs, and expose the burn level and the trip time.
Landed as `TripPlanner.CruiseStyle.BurnCoastBurn`, with no impulse at the
target's sphere of influence. Because a burn along the velocity can only change
the speed, the brake's thrust direction is the change the chain needs and
opposes the arrival velocity, not an anti-velocity direction. The first cut
placed the departure burn backwards from the coast and followed the
heliocentric velocity; the pinned-break follow-up replaced that with the
forward-solved escape-ray burn above. The nav-computer control landed in the
plan's fourth step.

# Not in the first cut

- **Multi-rev coasts.** `OrbitMath.SolveKepler` wraps the mean anomaly to one
  revolution and `ConicArc` assumes a single pass, so a multi-rev Lambert would
  collapse silently. Exclude it until both are extended.
- **A porkchop plot.** A departure-date heat map is the natural second control
  and the next follow-up, not part of the first flyable profile.
- **Optimal control.** A shooting or indirect solver that minimises propellant
  remains the eventual answer ([ADR-006](/decisions/adr-006-quintic-plan.md)).
- **Burning across a sphere of influence.** A long burn that crosses the SOI
  needs a reference-body handover. The first cut breaks orbit with the quintic
  and starts the steady burn outside; a level or trip time that would force a
  crossing is rejected.

# Settled choices

- **Default profile.** `Powered` remains the default, matching the planner and
  the nav computer; with fuel modelled, the coast becomes the obvious cheap
  default.
- **Control units.** A trip time, matching the existing slider. Time is the
  simpler control, and its window is the selected profile's own feasible range.
  The coasting profiles' right-hand end is probed from what the planner accepts,
  not taken as three times the minimum: a long short-way transfer sweeps past
  180°, and a slow coast would need an acceleration rather than a brake.
- **The pinned break and the escape ray.** The break ends at `1.02 × SOI` at
  the local escape speed along the escape ray, and the departure burn holds
  that ray. A velocity-following burn cannot close the seam at fast trip times,
  so the ray, not the heliocentric velocity, sets the burn's attitude.
- **Brake and capture split.** The brake spends only the coast's handover and
  hands the capture chain its entry state with no impulse; the split is not
  optimised. The brake's thrust direction follows the change the chain needs
  rather than the velocity alone, because a purely anti-velocity burn cannot
  close the seam.
- **Slow crossings.** A coast that arrives slower than the target's frame needs
  an acceleration rather than a brake, so those trip times are rejected. The
  profile is the fast, expensive end of the trade; the coast remains the cheap,
  slow one.
