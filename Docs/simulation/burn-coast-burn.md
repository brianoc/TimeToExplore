---
type: Design Document
title: Burn–coast–burn trips
description: The shipped trip profile that burns hard, coasts fast, and brakes into orbit, built from the four existing arc ingredients.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-20T00:00:00Z }
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
torch-ship fiction uses: the ship burns hard, coasts fast, and brakes into
orbit. It is the planner's only route; the old all-torch quintic cruise and the
plain Lambert coast were retired once this profile landed.

# Four ingredients, not four journeys

The route colours once made three fixed recipes — all-quintic, conic arrival,
and hybrid arrival — look like three separate kinds of trip. They are really
three **arc shapes**, plus one solver, and a trip can mix them. The nav
computer now asks one question:

* the **`ARRIVAL`** row chooses the capture — **Conic** or the hybrid
  **X g burn**.

The crossing is always the profile. It is built from the same ingredients:

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

# The profile's arc chain

```text
leave orbit      burn hard        coast         approach        brake         enter orbit
[ quintic ]  →  [ fixed burn ] → [ conic ]  →  [ conic ]   →  [ fixed burn ] →  [ capture ]
    gold             red            blue          blue             red          blue/gold
```

Every arc type already existed. The planner emits this recipe as its only
crossing: there is no profile selector and no zero-thrust version. A trip that
starts inside the target's sphere of influence has no crossing to plan and is a
single quintic capture instead.

The **approach** is the planetocentric leg of the final approach: a `ConicArc`
about the target from the handover band's ceiling (`1.02 × SOI`) down to where the
brake begins. It exists because the brake's start is the coast's end, and on a
long trip that end sits deep inside the target's sphere where a Sun-only conic is
a poor model. Its entry state at the ceiling is the coast's arrival state, so the
seam is a state and not a model change; its duration is the free fall's own, so it
is hours at a fast arrival and days at a slow one. A fast trip, whose brake starts
outside the ceiling, has no approach leg and keeps the seven-arc shape
([the arrival leg](/simulation/powered-leg-floor.md#the-arrival-leg-a-planetocentric-approach)).

The capture is deliberately the conic or hybrid capture, not a quintic: a
quintic capture cannot command less thrust than the parking orbit's own gravity,
so it has to park high around the giants. The conic/hybrid capture gets low and
cheap ([ADR-014](/decisions/adr-014-keplerian-coast.md),
[ADR-015](/decisions/adr-015-fixed-thrust-burns.md)). A quintic departure is
fine and is the right tool for the exact orbital break. The quintic capture
survives only for a trip that starts inside the target's sphere of influence,
where there is no crossing at all.

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
   throwing. A short arc that runs clockwise about the ecliptic pole is the
   retrograde branch, which is not implemented, so those dates are rejected as
   well; the Mars→Earth return at the epoch is one.
2. **The reference coast.** `TripPlanner` builds a private coast skeleton: a
   quintic departure retargeted to the Lambert `v₁`, a heliocentric `ConicArc`,
   and the existing conic or hybrid capture. The skeleton supplies the capture
   chain, the handover the brake spends, and a first guess at the outbound
   direction and the burn's size. It is a search input, never a plan the planner
   returns, and it enters at the arrival handover floor.
3. **The burns.** The profile is a quintic break →
   departure burn → Lambert coast → planetocentric approach → brake → conic or
   hybrid capture. The break ends on the departure clearance sphere
   (`1.02 × SOI`) at the local escape speed along the escape ray, and the
   departure burn starts there. The burn holds that ray — its prograde direction
   in the departure body's frame — and its duration and the coast's start are
   solved together: the burn is integrated forward through the summed gravity
   field, the Lambert coast from its end to the arrival handover, and the two ray
   angles and the duration are driven until the burn's end velocity is the
   coast's departure velocity. The coast ends on the target's handover ceiling
   and the approach — a `ConicArc` about the target, built backwards from the
   brake's own start state — covers the deep part of the final approach; the
   brake then starts where the approach ends and spends the rest of the handover
   the coast skeleton would have made an impulse. Every seam is continuous and
   the arrival is exact. The break's duration is solved as well: it keeps the
   reference coast's departure share while that is gentle enough for the drive,
   and lengthens it to the drive's limit when the share is too short.
4. **The controls.** The nav computer's `ARRIVAL` row chooses **Conic** or the
   hybrid **X g burn**; the trip-time slider is the profile's speed control and
   its window is the profile's own feasible range; the burn-level slider is the
   level the departure burn, the brake, and the hybrid capture are planned at.
   Its floor is **0.05 g**: the profile is two real burns around a coast, so it
   has no zero-thrust version.

The measured window at the epoch is **6.73 to 20.19 days** for the
Gateway→Mars profile at 0.3 g. At 1 g the fast end is **3.21610 days** to
Mercury (window 3.22–9.65 days); Jupiter at 0.3 g is **13.07–39.22 days** and
Uranus is **33.60–100.81 days** at 0.3 g and **33.65–100.96 days** at 1 g. The
window moved when the capture started
entering at the handover floor: the old fast ends were 8.03 and 3.46 days, set
by the capture's 1.32-day and 6.1-hour falls. The planetocentric approach leg
moved the Uranus 1 g floor, and so its window: that floor's brake starts inside
the ceiling, so it is the one floor plan that carries an approach arc (eight
arcs), while the 0.3 g floor is still a seven-arc fast trip. The right-hand end is the
three-times cap, and both ends build. The 90-day Mars
plan in the table below is still a valid direct build, but it is outside this
window, so the nav computer's slider cannot ask for it.

# The pinned break

The break's end state is a design decision: the point where the departure burn
starts, on the handover band's **ceiling** at the local escape speed along the
escape ray. The ceiling is `1.02 × SOI`, the clearance sphere the profile always
used; a burn may now start lower inside the band, because it is integrated through
the summed field
([full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md)).
For this plan the departure endpoint *is* the ceiling — the break's only job is to
leave the parking orbit and reach escape, and the burn supplies everything above
it — so the departure plan did not move when the constant became the band's
ceiling. The old chain solved the coast's
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
capture, after the arrival leg became planetocentric. The old totals in the last
column are what the same build cost with the chain entering on the target's
sphere of influence:

| Target | Days | Break (d) | Break peak (m/s²) | Break Δv (km/s) | Burn (d) | Burn Δv (km/s) | Coast (d) | Approach | Brake (min) | Brake Δv (km/s) | Inbound fall | Total Δv (km/s) | Old total | Feasible |
|---|---|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---|
| Mercury | 8 | 0.569 | 2.85 | 90.2 | 0.298 | 250.0 | 6.841 | — | 366.5 | 213.3 | 3.3 min | 557.0 | 569.9 | yes |
| Mercury | 6 | 0.438 | 4.84 | 118.1 | 0.394 | 329.8 | 4.787 | — | 497.6 | 289.6 | 3.3 min | 741.1 | 770.1 | yes |
| Mercury | 12.3 | 0.837 | 1.30 | 60.5 | 0.203 | 170.5 | 11.053 | — | 244.5 | 142.3 | 3.3 min | 376.9 | 379.9 | yes |
| Mars | 90 | 4.844 | 0.033 | 8.8 | 0.053 | 13.4 | 84.729 | 6.03 h | 104.9 | 18.5 | 13.6 min | 44.4 | 45.4 | yes |

The Mars row is at 0.3 g; its two burns are 76.0 and 104.9 minutes at that level.
The 90-day build is a direct `PlanAtDuration` outside the nav computer's window.
The fall column is the capture chain's inbound coast, which used to be 6.09 hours
at Mercury and 1.32 days at Mars: the brake now ends at the handover floor
(2,805.7 km at Mercury, 6,032.7 km at Mars) instead of the sphere (112,410 km and
577,241 km), and the chain's entry speed is unchanged at the planted 5 km/s. The
approach column is the planetocentric leg: it is absent on the fast Mercury
trips, whose brakes start outside the ceiling, and 6.03 hours from the 588,786 km
ceiling to 92,923 km on the 90-day Mars build.

Measured at the epoch from Gateway at 1 g and 400 days to **Uranus**, which is
the case that motivated the change. The middle column is the sphere-of-influence
entry the profile used before the floor; the right column is the floor entry with
the planetocentric approach:

| Quantity | Sphere-of-influence entry (before) | Floor entry, planetocentric approach |
|---|---:|---:|
| Capture entry radius | 51,760,023 km | **38,643 km** |
| Inbound fall | 117.13 days | **31 min** |
| Capture Δv (periapsis) | 6.37 km/s | 6.37 km/s |
| Departure burn / brake | 118.8 / 112.7 km/s | **90.2 / 81.4 km/s** |
| Coast | 277.3 days | 384.8 days |
| Approach | — | **7.54 days, 52.8 million km → 368,111 km** |
| Total | 242.1 km/s | **182.3 km/s** |
| Brake start radius | 51.96 million km (1.004 SOI) | **368,111 km (0.0071 SOI)** |
| Profile floor | 150.73 days | **33.65 days** (editor; 30.56 .NET) |

At 0.3 g the same comparison is a 7.43-day approach from the ceiling to
1,160,664 km against 117.13 days, 79.8 km/s of brake, 180.8 km/s in total, and a
floor of **33.60 days in the editor (40.71 in .NET)** against 160.47. The brake
starts 0.0071 spheres out at 1 g and 0.0224 at 0.3 g because the approach, not
the coast, now flies most of the deep approach; at the fast floor the brake
itself spans the sphere (28.4 hours of 990 km/s from 50.55 million km, 0.976
SOI). Those numbers are in
[the arrival leg](/simulation/powered-leg-floor.md#the-arrival-leg-a-planetocentric-approach).

The 8-day trip at Mercury closes with a break peak of 2.85 m/s²; the plan's
9.70 m/s² peak is the two fixed burns at the commanded level. The break ends at
1.02 SOI at 0.919 km/s, the local escape speed. The seams close to under a
metre and 3.7 × 10⁻⁴ m/s — still three orders inside the 1 km / 1 m/s gate, and
wider than before because the brake's fixed point now ends at the floor — and
the 8-day flight arrives 0.05 m and 4.4 × 10⁻⁵ m/s from the reference state on
the conic parking orbit.

**A slow crossing closes now, and the approach makes it cheaper.** With the fall
gone, a 200-day Mars build at 0.3 g that used to be refused for needing an
acceleration is a **11.86 km/s** plan (2.70 km/s of brake) with the same
6,032.7 km entry, 0.60 km/s less than the deep-coast build: its last 27.3 hours
are a planetocentric approach, and the coast no longer commands 0.90 km/s of
solar tide to hold a heliocentric conic deep in the well. The slow end is set by
the coast instead: at 305 days the prograde short-way Lambert transfer has no
solution (179.4°). The acceleration branch is still in the solver; it did not
trigger at Mars between 200 and 300 days after this change.


The **break Δv** is the integrated thrust over the quintic, not the speed change
at the boundary. The quintic still cancels the station's motion and flies the
distance, so a gentle break spends tens of kilometres per second even though it
ends at 0.92 km/s. What the pin removes is the 53 m/s² peak and the
four-sphere-long backwards burn.

## The level pin is opt-in

The shipped break aims at the drive's **limit**, not at the level the player
chose. `TripPlanner.PinBreakToCommandAcceleration` (default **false**) is the
alternative: the break's duration is solved so the quintic's peak **commanded**
thrust equals `FixedBurnAccelerationMps2` — the level the departure burn, the
brake, and the hybrid capture already command, 9.70 m/s² at 1 g and 2.942 at
0.3 g. The peak falls as the duration grows, so the solve brackets the crossing
from the coast's share and bisects, on the fast central-body model; the plan
that is reported is still the full-field measurement, so its peak can sit a
fraction of a percent from the target at 0.3 g, where the 5% screen leaves the
departure body alone in charge.

The duration is then a property of the departure state, the level, and the
escape ray, not of the trip. With one ray held fixed, 30, 90 and 200 days to
Mars at 0.3 g solve the same duration to **8.4 × 10⁻⁸** relative, and the
available-time cap is nowhere near binding (29.1, 89.5 and 198.7 days). The
plans' own rays are not identical: the outbound asymptote the crossing needs
rotates **10.96°** by 90 days and **27.67°** by 200, and the reported break
durations follow it by 0.66% and 3.05%. That is the ray moving, not the trip
scaling the break.

Measured at the epoch from Gateway, conic capture. The Mercury row is the
8-day plan the table above records; the Mars rows are the pinned break at three
trip times, against the 90-day default build:

| Plan | Pin | Break (d) | Break peak (m/s²) | Break Δv (km/s) | Total Δv (km/s) |
|---|---|---:|---:|---:|---:|
| Mercury, 1 g, 8 d | off | 0.569 | 2.85 | 90.2 | 557.0 |
| Mercury, 1 g, 8 d | **on** | 0.3086 | 9.7000 | 167.2 | 615 |
| Mars, 0.3 g, 30 d | **on** | 0.5632 | 2.9400 | 92.7 | 232.2 |
| Mars, 0.3 g, 90 d | **on** | 0.5669 | 2.9392 | 93.2 | 127.7 |
| Mars, 0.3 g, 200 d | **on** | 0.5461 | 2.9419 | 90.0 | 99.4 |
| Mars, 0.3 g, 90 d | off | 4.844 | 0.033 | 8.8 | 44.4 |

The trade is the point: **a short break at a high level spends more delta-v.**
Pinning Mercury's 8-day break to 1 g nearly doubles its spend, 90.2 → 167.2
km/s, because the quintic has to cancel the station's motion and cross the
sphere in a third of the time. At Mars 0.3 g the pinned break costs 90–93 km/s
at any trip time, against 8.8 for the 4.844-day gentle break the default
builds; the long trip's saving shows in the coast and the total, not in the
break. The level pin is not a fuel optimisation; it is a way to make the departure
burn and the two fixed burns command the same acceleration.

The floors move only when the option is on, and only through the near-floor
basin: Mercury at 1 g stays **3.217 d**, Mars at 0.3 g is **6.864** against
6.730, Jupiter at 0.3 g **13.713** against 13.072, and Uranus at 0.3 g lands at
**40.874** against 33.604 — the pinned break's changed basin selects a different
certified point, not a different profile. The eight-case sweep, its windows,
and the arc breakdown are the measurement the caretaker reviews before deciding
whether the level pin becomes the default; keeping the option off leaves every number
above and every anchor in this document unchanged.

At 1 g the target is the drive's own margin limit (9.7 m/s² of 10.0), so a
certified peak above it would spend the plan's feasibility. The 5% screen puts
the full field in charge at that level, which is why the measured pinned peaks
are 9.7000 m/s², and the solver retargets a fifth of a percent lower once if a
certified peak ever lands above the 10.0 m/s² drive.

# The burns in the summed field

The two fixed burns are integrated through the **summed gravity field**, not a
single body's. `FiniteBurnArc` takes an optional `IGravityField`; with one it runs
the RK4 in absolute heliocentric coordinates and decomposes the table back into
the reference body's frame. The Sun named on the departure burn and the brake is
then a label — the phase, the frame the arc is reported in, and the ephemeris
that decomposes the state — not the physics. One burn arc may cross a sphere of
influence; it is never split at the boundary. The full design and its boundaries
are in
[full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md).

Two things stay as they were. The **attitude** is the profile's: the departure
burn holds the escape ray, the brake holds the handover change, and a
prograde/retrograde burn follows the velocity relative to the reference body.
And the floor probes keep the **two-body burn** as their search model; every
fixed burn in the plan that is reported and flown is the full-field one,
including the hybrid capture's injection arc.

Measured at the epoch, the change is real in the arcs and almost invisible in the
reported numbers. At the 90-day Mars plan's mid-burn the summed field is
**4.4 × 10⁻⁴ m/s²** away from the Sun's rail at the departure burn and
**1.2 × 10⁻⁴ m/s²** at the brake; a short 120 s burn 4,000 km above Mars ends
**0.17 m** from its two-body counterpart. But a fixed-direction burn's delta-v is
`a T` and `T` is set by the velocity change the chain needs, so the pinned solve
absorbs the field difference into the escape ray and the path, not the spend. The
numbers then moved when the capture started entering at the handover floor, not
because of the field: 557.0 km/s at 8 days to Mercury, 376.9 at 12.3, 44.4 at
Mars 90 days, and floors of 3.22 and 6.74 days. The seams close to under a metre
and 3.7 × 10⁻⁴ m/s, and the Mars 90-day flight arrives 0.08 m and 7 × 10⁻⁵ m/s
from the reference state.

The cost is planning time, not delta-v: a full-field step evaluates the summed
field over the catalogue, and the active set refreshes every eighth step, so a
certified build is still several times the two-body build it replaced
([planning performance](/technical/planning-performance.md); step 7 measured the
2.2–3.7× cut from the cadence and the looser pin).

# The burns are gated on body clearance

The profile used to refuse a departure burn that crossed the departure sphere of
influence. That rule is gone: crossing a sphere is a supported case now, because
the burn integrates the summed field. What replaces it is a **body clearance**
gate. `TripPlanner.FixedBurnClearsBody` samples a fixed arc against a body and
returns the closest radius from its centre; the plan holds the departure burn
clear of the departure body, and the brake and the hybrid capture's injection
burn clear of the target, by 100 km above the surface — the same clearance the
hybrid capture keeps. A violation names the arc, the body, and how close it came.
Measured at the epoch: a 3-hour 1 g burn started 1.5 Earth spheres out and pushed
straight at the planet crosses the 924,651 km sphere while clearing the planet by
821,258 km, and is accepted; a 30-second burn 60 km up, thrusting at the surface,
grazes to 6,422.3 km (51.3 km altitude) against a 6,471 km clearance, and is
refused. The 90-day Mars
build's departure burn starts on the ceiling and climbs, so it still stays outside
the sphere — a consequence of the pinned endpoint, not the rule
([the handover band](/simulation/powered-leg-floor.md#the-body-clearance-gate)).

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
plans did not move at that step: 8 d Mercury was 3.02 m/s² and 569.9 km/s, 12.3 d
1.35 and 379.9, Mars 90 d 0.033 and 45.4, and the floors 3.46 d and 8.03 d. (The
arrival entry radius later moved them to 2.85 m/s² and 557.0, 1.30 and 376.9,
0.033 and 44.4, and floors 3.22 d and 6.74 d.) The fast peak tracks the full-field peak to 0.18% at 8 d Mercury, 0.41%
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
(or 30 s, whichever is larger), growing up to twelve 20% steps when the estimate
lands below the floor. A refused probe is retried 0.2% higher, because the
near-floor chain has narrow non-convergence holes; that retry is the search's
real resolution. Since step 3 the certified build retries a stalled brake pin,
so the fast answer certifies on the planner's **first** certificate in every
measured case: the .NET trace is `fast`, `probe 0`, `full 0`, `cert 1`, and Mars
pays one `pin retry` (12 → 24 passes) inside its certificate. The near-floor
basin is still noisy, but the search no longer spends a full-field continuation
on it; the remaining cost is the certified build itself, which is why the editor
is 1.8–3.05 s across the five cases and the .NET harness 0.21–0.38 s. The
coarser growth and 1% bisection the plan held in reserve were measured against
that and dropped, because they would move floors and save nothing
([step 4](/technical/planning-performance.md#step-4-the-bracket-was-not-needed-2026-11-20)).
The approach leg leaves the fast floors alone: every .NET floor is a seven-arc
trip whose brake starts outside the ceiling. The estimate is also shorter now
(the capture no longer contributes months), so the growth loop was widened from
five steps to twelve; at Uranus 0.3 g the five-step growth stopped at 40.3 days
and declared the trip infeasible while 40.95 days built.

Measured at the epoch (2026-11-15) from Gateway, conic capture. The floor is a
certified full-field plan; the editor's number is the one that ships:

| Target | Level | True floor | Peak | Total Δv | Just below | Why the shorter trip is refused |
|---|---|---:|---:|---:|---|---|
| Mercury | 1 g | **3.21610 d** | 9.70 m/s² | 1,766 km/s | 3.19 d | departure break needs 10.1 m/s², above the 10.0 m/s² drive |
| Mars | 0.3 g | **6.73042 d** | 5.14 m/s² | 1,172 km/s | 6.60 d | departure break needs 10.03 m/s²; 6.70 d leaves the brake 8,073 m and 0.8 m/s short of the entry |
| Jupiter | 0.3 g | **13.07 d** in the editor (13.33520 .NET) | 9.70 m/s² | 2,271 km/s (editor) | not re-swept | the editor's fast high certifies after one pin retry; the .NET basin selects a different needle |
| Uranus | 1 g | **33.65 d** in the editor (30.56 .NET) | 9.70 m/s² | 2,070 km/s (editor) | not re-swept | step 1 re-measured the .NET floor lower; the 128-sample certified solve converges where 512 stalled, and both points are certified. The editor's floor is the eight-arc approach plan |
| Uranus | 0.3 g | **33.60 d** in the editor (40.71 .NET) | 2.94 m/s² | 2,301 km/s (editor) | not re-swept | the basin is needles; in .NET the certified build closes 40.5, 40.0, and 39.0 d directly while the fast probes refuse them, so the search is conservative |

The runtime difference at the giants is the near-floor scatter, not a defect: a
duration's feasibility is set by whether the departure Newton stalls and how
many passes the brake pin needs, so a last-bit libm difference selects a
different certified point. The editor's windows are **33.60–100.81 d** (Uranus
0.3 g), **33.65–100.96 d** (Uranus 1 g), **6.73–20.19 d** (Mars 0.3 g),
**3.22–9.65 d** (Mercury 1 g), and **13.07–39.22 d** (Jupiter 0.3 g); the
divergence's measurement is in [planning
performance](/technical/planning-performance.md#the-runtime-divergence-is-a-property-not-a-bug).

The Uranus 0.3 g basin is a set of **needles**, not a boundary. In the .NET
harness the returned floor is **40.71332 d** and the search is conservative: the
fast probes refuse the durations below it while the certified build closes 40.5,
40.0, and 39.0 d directly (worst seams 17.6 m / 1.7 × 10⁻³ m/s or better) and
the floor itself closes at 5.1 m / 6.5 × 10⁻⁴ m/s. The editor's scatter selects
**33.60438 d**, also certified; the two are the same basin read through
different libm last bits. The .NET 1 g floor is **30.55886 d** and rebuilds cold
at 0.4 m / 1.0 × 10⁻³ m/s; the editor's is **33.65372 d** and carries the
approach arc. [Planning
performance](/technical/planning-performance.md#step-1-the-builds-own-cost-2026-11-20)
records the re-measurement and the cache reuse that keeps the background retime
off it.

The floor search also requires the chain to be **closed**: a plan whose peak
fits but whose brake misses the capture entry is not flyable. The near-floor
trips amplify the brake solve's forward/backward mismatch, so the shortest
plan that fits the peak can end the brake hundreds of thousands of kilometres
from the entry; the planner reports that as a trip time too short for the
profile instead of handing it to the autopilot. The gate is a kilometre of
position and a metre per second of velocity: the 8- and 12-day trips close to
centimetres, the 6.73042 d Mars floor's worst seam is 9.8 m and 2.5 × 10⁻² m/s
(and the Mercury floor's 196 m), and a broken brake misses by thousands of
kilometres. The 8.03 d flight
arrived 0.1 m and 10⁻⁴ m/s from the reference parking state.

The pinned solve itself now stops on the brake's own physical mismatch (1e-6 m/s
at the brake's start) rather than the old `required.SqrMagnitude > 1e-18`, which
a real brake can never satisfy. The fixed 12-pass outer loop and the three
settle passes became a convergence loop plus up to three settles only while the
fixed point is still drifting; the final brake and arcs still close to better
than a few metres and 2 × 10⁻⁴ m/s on the tested trips. The Newton Jacobian is
reused for one iteration, and the coast's propagator correction gets sixteen
iterations so the long, low-level burns still close.

The preview's floor plan and trip-time window are cached per departure date,
target, burn level, and arrival style in `FlightSession`. A repeat
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

**Stage A — the reference coast.** Add the Lambert solver and build a trip as a
quintic departure retargeted to the Lambert's `v₁`, a heliocentric `ConicArc`
coast, and the existing conic/hybrid capture. This was first exposed as a
retired plain-coast profile; it is now the profile's private reference skeleton.

**Stage B — the burns.** Add the departure fixed burn and the fixed-thrust
brake, stitch the arcs, and expose the burn level and the trip time. This is the
shipped route, with no impulse at the target's sphere of influence. Because a burn along the velocity can only change
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
- **Burning across a sphere of influence.** *Supported, not excluded.* The first
  cut refused a burn that crossed the SOI because each two-body model stops at
  the boundary and the reference-body handover would sit where both models are
  about half wrong. `FiniteBurnArc` now integrates a fixed burn through the
  summed field, so one arc crosses; splitting the burn at the SOI is the option
  that stays refused. See
  [full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md).

# Settled choices

- **Default profile.** Burn–coast–burn is the only profile; the powered quintic
  cruise and the plain Lambert coast are retired.
- **Control units.** A trip time, matching the existing slider. Time is the
  simpler control, and its window is the profile's own feasible range.
  The profile's right-hand end is probed from what the planner accepts,
  not taken as three times the minimum: a long short-way transfer sweeps past
  180°, and a slow coast would need an acceleration rather than a brake.
- **The pinned break and the escape ray.** The break ends at the handover band's
  ceiling — `1.02 × SOI` — at the local escape speed along the escape ray, and the
  departure burn holds that ray. That is the departure endpoint for this plan: the
  break's job is escape, and the burn starts at the clearance sphere and moves
  outward, so the profile's departure numbers did not move. A velocity-following
  burn cannot close the seam at fast trip times, so the ray, not the heliocentric
  velocity, sets the burn's attitude. The band, the arrival endpoint, and the
  full-field integration are in
  [full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md).
- **Brake and capture split.** The brake spends only the coast's handover and
  hands the capture chain its entry state with no impulse; the split is not
  optimised. The brake's thrust direction follows the change the chain needs
  rather than the velocity alone, because a purely anti-velocity burn cannot
  close the seam.
- **The arrival entry radius.** The brake ends at the arrival handover band's
  floor, raised to the conic parking radius where the floor is deeper than the
  chain's own periapsis, and the capture chain is built from that radius. The
  fall is minutes instead of months (Uranus 1 g: 31 min against 117.13 days) and
  the entry speed is still the per-plan `EntrySpeedMps` — 5 km/s by default —
  because the brake-produced residual is the next decision.
- **Slow crossings.** A coast that arrives slower than the target's frame needs
  an acceleration rather than a brake, so those trip times are rejected. The
  trip-time slider covers the rest of the trade: a longer crossing is slower and
  cheaper.
