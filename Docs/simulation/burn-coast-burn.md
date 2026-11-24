---
type: Design Document
title: Burn–coast–burn trips
description: The shipped trip profile that burns hard, coasts fast, and brakes into orbit, built from the four existing arc ingredients.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-24T04:15:13Z }
---

# The trip

Leave a planet, cross to another, and park there — or arrive at a station and
hold at its hold point. The ship:

1. breaks out of its parking orbit and climbs ballistically to escape,
2. burns hard for a while and builds speed,
3. coasts with the drive off, still travelling fast,
4. flips and burns hard to slow down,
5. drops into the target's parking orbit, or matches the station's velocity at
   its hold point.

This is the standard **burn–coast–burn** profile, sometimes called a
brachistochrone with a coast. It is not exotic physics. It is the profile most
torch-ship fiction uses: the ship burns hard, coasts fast, and brakes into
orbit. It is the planner's only route; the old all-torch quintic cruise and the
plain Lambert coast were retired once this profile landed.

# Four ingredients, not four journeys

The route colours once made three fixed recipes — all-quintic, conic arrival,
and hybrid arrival — look like three separate kinds of trip. They are really
three **arc shapes**, plus one solver, and a trip can mix them. The nav
computer now asks one question for a planet:

* the **`ARRIVAL`** row chooses the capture — **Conic** or the hybrid
  **X g burn**.

A station has no capture to choose; its plan ends on a hold point.

The crossing is always the profile. It is built from the same ingredients:

| Ingredient | What it is | Drive | Best at |
|---|---|---|---|
| **Quintic** | a powered arc matching position, velocity, and acceleration at both ends; the thrust can point anywhere | continuous | the exact, fiddly ends: the short break out of a parking orbit and a gentle capture |
| **Conic** | a Keplerian free-fall arc | zero | the coast, and the ballistic escape climb out of the departure well |
| **Fixed burn** | a powered arc at a constant commanded acceleration | fixed, e.g. 1 g | the long, visible "burn hard" legs |
| **Lambert** | not an arc: a solver that picks the conic connecting two positions in a chosen time | — | choosing the coast so it arrives on time |

**Lambert is not a slow mode.** It is the coast solver. The player chooses a
trip time; Lambert returns the coast that fits it; the burns supply the speed.
Thirty days to Mars is a fast coast. Two hundred and fifty-nine days is a slow,
cheap one. Same solver, different input.

# The profile's arc chain

```text
leave orbit     escape        burn hard         coast        approach        brake         enter orbit
[ quintic ]  →  [ conic ]  →  [ fixed burn ] → [ conic ]  →  [ conic ]   →  [ fixed burn ] →  [ capture ]
    gold          blue             red           blue          blue             red          blue/gold
```

Every arc type already existed. The planner emits this recipe as its only
crossing for a **planet** destination: there is no profile selector and no
zero-thrust version. A trip that
starts inside the target's sphere of influence has no crossing to plan and is a
single quintic capture instead.

The **escape** is the ballistic planetocentric leg out of the departure body's
well: a `ConicArc` about the departure body from the parking radius to the
handover ceiling (`1.02 × SOI`). It exists because the break is now a short burn,
not a multi-day powered hover: the break raises the ship onto its own prograde
escape state and the free climb does the rest. A long trip gets the minimum-energy
parabolic climb; a trip that leaves the departure less than that gets a hyperbolic
excess speed so the conic still reaches the ceiling in the time available. The
coast absorbs whatever time is left, so the total trip time stays the caller's
input. A fast trip therefore costs more, not less: a larger excess speed raises the
break's delta-v and lengthens its commanded-level burn.

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

## The station terminal

A **station** target takes the same departure and coast and then ends before the
capture: break → escape (only if the ship starts inside a planet's sphere) →
departure burn → coast → **velocity match** → **hold point**. The target is a
massless point, so there is no sphere of influence, handover band, parking orbit,
planetocentric approach or capture chain. The terminal condition is
`SolarSystemModel.ComputeState(station, arrivalJd)` offset 500 m astern along the
station's own velocity (`TripPlanner.StationHoldDistanceM`), with the ship's
velocity matched to the station's. Because a rendezvous may **add** speed as well
as shed it, the final burn is a match and not the planet arrival's retrograde
brake; the planet path's retrograde-only refusal is dropped for a station.

A station outside every sphere — Helios Watch, Nyx Anchor, Ares Junction and the
Jovian Transfer Yard — has no departure or escape arc at all: its plan starts
with a finite burn on the ship's own heliocentric state, and the measured three-arc
Ares Junction → Gateway trip is the reference. Gateway and Titan Reach are inside
a sphere, so they keep the break and the climb and still end on the point target.
Docking is not implemented; the plan ends at the hold point
([ADR-019](/decisions/adr-019-station-calls-and-departure.md)).

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
2. **The reference coast.** `TripPlanner` builds a private coast skeleton: the
   physical departure (the short break and the escape conic) out to the handover
   ceiling, a heliocentric `ConicArc`, and the existing conic or hybrid capture.
   The skeleton supplies the capture chain, the handover the brake spends, and a
   first guess at the departure direction and the burn's size. It is a search
   input, never a plan the planner returns, and it enters at the arrival handover
   floor.
3. **The departure and the burns.** The profile is a short quintic break → a
   ballistic escape conic → departure burn → Lambert coast → planetocentric
   approach → brake → conic or hybrid capture. The break ends at the parking
   radius on the ship's own prograde tangent at the planetocentric escape speed,
   and the escape conic carries that state to the departure ceiling
   (`1.02 × SOI`). The departure burn starts at the conic's ceiling **state**, and
   its thrust direction (two angles) and duration are solved together against the
   coast: the burn is integrated forward through the summed gravity field, the
   Lambert coast from its end to the arrival handover, and the direction and
   duration are driven until the burn's end velocity is the coast's departure
   velocity. The break's duration is its own commanded-level solve, `T_break` at
   the peak the fixed burns command, never a share of the trip. The coast ends on
   the target's handover ceiling and the approach — a `ConicArc` about the target,
   built backwards from the brake's own start state — covers the deep part of the
   final approach; the brake then starts where the approach ends and spends the
   rest of the handover the coast skeleton would have made an impulse. Every seam
   is continuous and the arrival is exact. A **station** plan replaces the final
   approach/brake/capture with one velocity match onto the 500 m hold point
   ([the station terminal](#the-station-terminal)).
4. **The controls.** The nav computer's `ARRIVAL` row chooses **Conic** or the
   hybrid **X g burn**; the trip-time slider is the profile's speed control; the
   burn-level slider is the level the break, the departure burn, the brake, and
   the hybrid capture are planned at. Its floor is **0.05 g**: the profile is two
   real burns around a coast, so it has no zero-thrust version. The slider's
   window is the profile's feasible range on the coast path, except that its
   **left end is the burn-only minimum** when that is shorter (see [the nav
   computer's minimum](#the-nav-computers-minimum)).

The measured **coast** windows at the epoch with the physical departure are
**16.84 to 50.53 days** for the Gateway→Mars profile at 0.3 g and
**14.06 to 34.27 days** for Mercury at 1 g (the three-times cap is 42.18 days,
but the short-way transfer passes 180° first). Mars at 1 g is **14.17–42.51
days**; Jupiter is **19.46–58.38** at 1 g and **23.73–71.20** at 0.3 g; Uranus is
**57.02–171.07** at 1 g and **92.13–276.39** at 0.3 g; Venus is **12.73–38.18** at
1 g and **14.32–42.95** at 0.3 g. The parabolic escape alone is 10.74 days from
Gateway and 12.02 days from a Mars 300 km orbit, so no coast trip can be shorter
than the climb; that is what moved every floor. The right-hand end of the Mars,
Jupiter and Uranus windows is the three-times cap; Mercury's is the Lambert
geometry's own slow end. The 90-day Mars plan in the table below is still a valid
direct build, but it is outside this window, so the nav computer's slider cannot
ask for it. The slider's **minimum** is the burn-only total where it wins and its
right end is three times that minimum, so Mars's slider is 16.49–49.46 days
(16.49 is the burn-only minimum), Mercury's 13.86–34.27 (its coast geometry stops
it before the 41.59-day cap), and Jupiter's 22.56–67.67; Uranus keeps the coast
windows above because the burn-only crossing does not win there.

# The physical departure

The departure is a **short break and a ballistic climb**, not a powered well
crossing. The break's end is the parking radius on the ship's own prograde
tangent, at the planetocentric escape speed `√(2μ/r₀ + v∞²)`; the escape conic
starts there and ends on the handover band's **ceiling** (`1.02 × SOI`) at the
local escape speed along its own asymptote. The ceiling is the clearance sphere
the profile always used; a burn may start lower inside the band, because it is
integrated through the summed field
([full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md)).
The old chain pinned the break's end to a radial ray on that ceiling; that end is
now a **state**, the conic's own ceiling state, and the departure burn's start
position is fixed by it. The old chain solved the coast's start and integrated the
burn backwards, so at fast trip times the burn began 4.7 SOI out at 32 km/s and the
break was fitted to that hard backwards solve.

The escape state is the **ship's own prograde tangent**, not the outbound
asymptote the crossing needs. A station's orbital direction has no relation to the
transfer, so the old profile pinned the break to a radial ray chosen by the coast
solver; that ray is 99–118° from the ship's velocity at a Mars 300 km orbit and
166.6° at Gateway, and a quintic to a fixed radial state flew a huge out-and-back
loop. The new break ends at the parking radius on the ship's own prograde tangent
at the planetocentric escape speed, advanced by the phase the ship sweeps during
the burn. That phase is negligible at Gateway (the Earth-relative angular rate is
3.4 × 10⁻⁶ rad/s, so a 928-second break sweeps 0.003 rad) and real from a Mars
300 km orbit (9.2 × 10⁻⁴ rad/s × 1,832 s = 1.69 rad, 97°): the burn really does
carry the ship a quarter-orbit round before it is fast enough to leave.

The break's duration is its own solve. It seeds at `|Δv| / a`, where `Δv` is the
speed change to the prograde escape state (`√(2μ/r₀ + v∞²) − v_circ`), and the
quintic's peak commanded thrust falls as the duration grows, so the solve grows
or shrinks from the seed and bisects until the peak is
`FixedBurnAccelerationMps2` — 9.70 m/s² at 1 g, 2.9467 at 0.3 g. Nothing about the
trip enters except the available-time cap, and at the documented trip times the
cap is not binding. Measured at the epoch: **928 s / 1.404 km/s** at Gateway and
**1,832 s / 2.854 km/s** from a Mars 300 km orbit, both peaking at the commanded
level. The integral is larger than the speed change because the quintic still
cancels the station's motion and carries the phase.

The **escape conic** is built from that endpoint: a Keplerian state whose
periapsis is the parking radius, so it never dips into the planet. Its duration is
the conic's own radius crossing, not a share of the trip. A long trip gets the
minimum-energy **parabolic** climb — 10.74066 days from Gateway's 308,455 km
radius to Earth's 943,144 km ceiling, and 12.02249 days from a Mars 300 km orbit
to 588,786 km. A trip that leaves the departure less than that gets a
**hyperbolic** excess speed: the flight time falls monotonically as `v∞` rises, so
the solve bisects `v∞` until the conic reaches the ceiling in the time left after
the break and the other legs. A larger `v∞` raises the periapsis speed, so the
break's commanded-level duration and delta-v grow with it. If the break and even
the fastest climb cannot fit the time available, the trip is refused with the
numbers rather than stretched; that is why the old 8-day Mercury, 10-day Mars and
35-day Uranus anchors are now below the floor.

The departure burn starts at the conic's ceiling **state**. Its free variables are
its thrust direction (two angles) and its duration: three equations (the burn's
end velocity is the coast's departure velocity) in three unknowns, with the burn
integrated forward through the summed field and the Lambert coast running from its
end to the brake's start. The burn's position is no longer a free ray on the
sphere. The burn's speed and the coast's departure speed still set its delta-v,
so the level still scales its duration as `1/level`.

Measured at the epoch (2026-11-15) from Gateway to **Mars at 90 days and 0.3 g**,
conic capture. This is a direct `PlanAtDuration` build outside the nav computer's
window:

| Arc | Duration | Peak (m/s²) | Δv (km/s) | Note |
|---|---:|---:|---:|---|
| Break (quintic) | 928 s | 2.9467 | 1.404 | ends at 308,455 km on the prograde tangent |
| Escape (conic) | 10.74066 d | 0.0016 | 0.252 | parabolic, periapsis 308,455 km |
| Departure burn | 79.5 min | 2.9420 | 14.040 | starts at the 943,144 km ceiling state |
| Coast (conic) | 78.83112 d | 0.0004 | 0.039 | heliocentric Lambert |
| Approach (conic) | 5.64 h | 0.0000 | 0.000 | 588,786 km ceiling to 374,027 km |
| Brake | 111.6 min | 2.9420 | 19.699 | retrograde, ends at the chain entry |
| Capture chain | 0.0494 d | — | 3.458 + 0.076 | 6,033 km entry, 814.8 s fall |
| **Total** | **90.00000 d** | **2.9467** | **38.968** | 9 arcs, exact arrival |

The design note's estimate for the same trip was 36.9 km/s: subtract the old
break's 8.845 km/s and add the new break's 1.405, holding the burns fixed. The
measured 38.968 is higher because the climb takes **10.74 days** where the old
break took 4.84, so the fixed 90-day trip leaves the coast 5.9 days shorter and
the two fixed burns grow: 13.418 → 14.040 and 18.522 → 19.699 km/s. That is the
honest consequence of replacing a powered hover with a free climb at a fixed total
time; the old 44.35 km/s anchor is not preserved, and neither is the naive 36.9.

Measured at the epoch from a **Mars 300 km equatorial orbit** at 0.3 g. The break
is 1,832 s (0.02123 d) and 2.854 km/s in every row; the escape is the 12.02249-day
parabolic climb. The capture chain is independent of the departure, so its entry
and fall are the same as the Gateway plans':

| Target | Days | Departure burn (d) | Departure Δv (km/s) | Coast (d) | Approach | Brake (min) | Brake Δv (km/s) | Total Δv (km/s) | Arcs |
|---|---:|---:|---:|---:|---|---:|---:|---:|---:|
| Earth | 60 | 0.11253 | 28.603 | 47.44657 | 3.87 h | 256.6 | 45.286 | **81.045** | 9 |
| Earth | 90 | 0.05162 | 13.120 | 77.32297 | 9.78 h | 168.0 | 29.649 | **49.940** | 9 |
| Earth | 120 | 0.03277 | 8.329 | 107.02647 | 18.23 h | 115.0 | 20.303 | **35.823** | 9 |
| Moon | 60 | 0.11256 | 28.611 | 47.60918 | — | 270.8 | 47.798 | **83.166** | 8 |
| Moon | 90 | 0.05151 | 13.093 | 77.74119 | — | 168.6 | 29.763 | **49.644** | 8 |
| Moon | 120 | 0.03266 | 8.301 | 107.77150 | 42 min | 109.9 | 19.396 | **34.520** | 9 |

The old totals were 337.1 / 369.2 / 387.0 km/s to Earth and 339.7 / 369.2 / 385.8
to the Moon: the old break alone spent 267.0 / 324.3 / 355.1 km/s over four to six
days. The new shape is the same monotone trade in the other direction: a longer
trip spends less, and the fast end is expensive because the short coast forces
large burns, not because the break is stretched. The Earth chain enters at
18,404 km and falls 2,048.7 s; the Moon chain at 2,042 km and 169.3 s. Every seam
closes inside **1.3 m and 2.5 × 10⁻⁴ m/s**, and the arrival is exact.

The **fixed-duration anchors are re-baselined**, not preserved. The old set was
Mercury 1 g 8 d (557.038), Uranus 1 g 400 d (182.312), Mars 0.3 g 20 d (270.850),
Mars 0.3 g 10 d (607.826) and Uranus 1 g 35 d (1,984.448). Three of those times are
now below the departure's floor and are refused; the feasible references measured
at the epoch are:

| Target | Level | Days | Total Δv (km/s) | Note |
|---|---|---:|---:|---|
| Mars | 0.3 g | 90 | 38.9678 | the table above |
| Mars | 0.3 g | 20 | 495.9818 | old anchor time, new value |
| Mars | 0.3 g | 18 | 706.0906 | replacement for the refused 10 d |
| Mercury | 1 g | 16 | 869.0568 | replacement for the refused 8 d |
| Uranus | 1 g | 60 | 1,329.6508 | replacement for the refused 35 d |
| Uranus | 1 g | 400 | 183.9153 | old anchor time, new value |
The three old times that are refused are worth stating plainly: Mercury 1 g 8 d,
Mars 0.3 g 10 d and Uranus 1 g 35 d each ask for less time than the parabolic
climb plus the departure burn and the minimum coast, and the planner reports the
refusal with the departure time rather than inventing a plan. A **0.3 g drive
cannot lift a 400 km Earth orbit at all** with the short break (local gravity
8.7 m/s² against 2.94 m/s² of thrust); the same orbit at 1 g builds a 30–120-day
trip. The short break is a real burn, and a drive weaker than the local gravity
cannot fly it.

Measured at the epoch from Gateway at 1 g and 400 days to **Uranus**. The middle
column is the sphere-of-influence entry the profile used before the floor; the
right column is the floor entry with the planetocentric approach and the physical
departure:

| Quantity | Sphere-of-influence entry (before) | Floor entry, planetocentric approach |
|---|---:|---:|
| Capture entry radius | 51,760,023 km | **38,643 km** |
| Inbound fall | 117.13 days | **31 min** |
| Capture Δv (periapsis) | 6.37 km/s | 6.37 km/s |
| Departure burn / brake | 118.8 / 112.7 km/s | **93.5 / 82.0 km/s** |
| Coast | 277.3 days | 381.5 days |
| Escape | — | **10.74 days, 308,455 → 943,144 km** |
| Approach | — | **7.48 days, 52.8 million km → 374,027 km** |
| Total | 242.1 km/s | **183.9 km/s** |
| Brake start radius | 51.96 million km (1.004 SOI) | **374,027 km (0.0072 SOI)** |
| Profile floor | 150.73 days | **57.02 days** (editor) |

The approach's periapsis is 84,719 km, above the floor, so the brake still spans
the last part of the descent. The brake starts 0.0072 spheres out because the
approach, not the coast, now flies most of the deep approach. Those numbers are in
[the arrival leg](/simulation/powered-leg-floor.md#the-arrival-leg-a-planetocentric-approach).

**A slow crossing closes cheaply.** A 200-day Mars build at 0.3 g is a
**11.21 km/s** plan with the same 6,032.7 km entry; its last 27.2 hours are a
planetocentric approach, and the coast no longer commands solar tide to hold a
heliocentric conic deep in the well. The slow end is set by the coast: at 305 days
the prograde short-way Lambert transfer has no solution (175.6°). The acceleration
branch is still in the solver; it did not trigger at Mars between 20 and 200 days
after this change.

The **break Δv** is the integrated thrust over the quintic, not the speed change at
the boundary. The quintic still cancels the station's motion and carries the
swept phase, so a short break spends 1.4–2.9 km/s even though it ends at the local
escape speed. What the physical departure removes is the multi-day hover: the old
Mars break's 267–355 km/s and the old Gateway break's 8.8 km/s are gone.

# The burn-only crossing

The profile's two burns can be placed **back to back**, with no coast between
them: the departure burn ends where the brake begins, and the flip is a seam
rather than a phase. That is the **burn-only crossing** — the brachistochrone
with the coast taken out. It is the fastest shape this profile can fly at a
given level, and the most expensive. Since step 3 it is the **nav computer's
minimum trip time** whenever it beats the coast floor
(`TripPlanner.PlanFastest`), and **Begin flies it** when the preview's minimum is
that plan. `Plan()` stays on the coast path, so the profile's floors and windows
in this document are the coast floor's; `PlanAtDuration` uses the short coast only
for a duration the coast path refuses between this minimum and the floor.

The seam is solved as one system. The departure climb — the short break and the
escape conic — is fixed by the crossing's available time; the departure burn
starts at the conic's ceiling **state** and integrates forward through the summed
field; the brake integrates **backward** from the capture chain's entry. The
unknowns are the departure burn's thrust direction (two angles), its duration, the
brake's direction (two angles), and the brake's duration: six unknowns for the six
seam components, the forward burn's end position and velocity against the backward
brake's start. The solver is a damped Newton with a finite-difference Jacobian,
seeded by the reference coast's departure direction, the chain's arrival
direction, and a half-and-half kinematic split
(`t_burn = t_brake = sqrt(distance / acceleration)`). A point at the chain gate
(1 km, 1 m/s) is accepted; a failure reports the residual it reached rather than
calling it a solution.

**The total time is an output.** With no coast to absorb a slider value, a
burn-only crossing is one duration per geometry and level: it is the sum of the
solved burn and brake plus the break and capture. The crossing therefore has two
entry points:

* `PlanBurnOnly(ship, target, arrivalJulianDate)` builds the capture chain once
  at the **fixed date** the caller supplies — its handover floor, plane, and
  fall. This is the step-2 measurement below.
* `PlanBurnOnly(ship, target)` **solves the arrival date**: the capture chain is
  rebuilt at `t0 + total`, the seam is solved there, and the plan's own total
  (break + departure burn + brake + capture) becomes the next date. The loop
  stops when the two agree within **2 s**; a pass that overshoots and reverses
  the previous step is halved, so a two-point ring decays instead of ringing. It
  is capped at **8 passes**, and a loop that hits the cap is reported
  **infeasible** with the last step it took — a moving-target plan that never
  converged is never returned as a solution. The seed is the coast floor's own
  total, so the chain starts in the basin the seam was measured in and the
  fixed-date measurement is the first pass.

The chain's handover is then placed where the solved brake actually ends, so the
converged plan is continuous in **date** as well as state: its arrival date is
the date its chain was built at, within the loop's tolerance. Building the chain
at the plan's own arrival date reproduces the same total, which is the
self-consistency the tests assert.

Measured at the epoch from Gateway to **Mercury at 1 g**, the moving-target loop
settles on a total of **13.86287 days** (2,588.5 km/s, seven arcs): break →
escape → departure burn → brake → the conic capture, with **no Cruise coast**.
The rebuilt plan's worst seam is **58 m and 0.401 m/s** (the brake rebuilt
forward against the chain's entry), inside the profile's 1 km / 1 m/s gate. The
brake starts far outside Mercury's 114,658 km ceiling, so the fast case needs no
planetocentric approach. The plan flies: `Begin` at the 1 g minimum puts it in the
air and the autopilot reaches the arrival state inside the test's 10 m / 0.1 m/s
bound.

## The nav computer's minimum

`TripPlanner.PlanFastest(ship, target)` computes both the coast floor and the
moving-target crossing at the same state and returns the **shorter feasible**
one. The two are compared, not assumed; when the zero-coast solve is infeasible,
does not settle, or comes out longer, the coast floor is returned.
`FlightSession`'s preview window uses it for its minimum, so the trip-time
slider's left end is the burn-only total when it wins, and `FlightSession.Begin`
uses it for the flight's base plan: beginning at the preview's minimum flies the
burn-only plan that was drawn, not the coast floor. Measured at the epoch from
Gateway:

| Target | Level | Burn-only minimum | Coast floor | Burn-only Δv | Coast Δv |
|---|---|---:|---:|---:|---:|
| Mercury | 1 g | **13.86287 d** | 14.06070 d | 2,588.5 km/s | 1,821.1 km/s |
| Mercury | 0.3 g | **16.44442 d** | 16.81142 d | 1,441.8 km/s | 1,015.7 km/s |
| Mars | 1 g | **13.94010 d** | 14.16958 d | 2,647.6 km/s | 1,810.4 km/s |
| Mars | 0.3 g | **16.48669 d** | 16.84485 d | 1,450.4 km/s | 1,017.1 km/s |
| Jupiter | 1 g | **17.30657 d** | 19.45997 d | 5,417.8 km/s | 2,436.4 km/s |
| Jupiter | 0.3 g | **22.55555 d** | 23.73299 d | 2,972.8 km/s | 1,899.5 km/s |
| Uranus | 1 g | coast floor | **57.02223 d** | — | 1,417.8 km/s |
| Uranus | 0.3 g | coast floor | **92.13088 d** | — | 811.1 km/s |

At the giants the zero-coast crossing does not win, so the coast floor is the
minimum. The slider's right end is three times the minimum where the coast
geometry allows it: Mercury's slider is **13.86–34.27 d** (the short-way transfer
passes 180° before the 41.59-day cap), Mars's **16.49–49.46**, Jupiter's
**22.56–67.67**, and Uranus's coast **57.02–171.07** at 1 g and
**92.13–276.39** at 0.3 g.

## The gap

The burn-only minimum is below the coast floor, so the range between them is
not a coast trip the Lambert path can build: `BuildBurnCoastBurn` allocates a
coast of at least `MinimumCoastSeconds`, and a duration in the gap leaves the
climb or the burns squeezed. The **short-coast branch** fills it.

## The short coast

The short-coast crossing is the burn-only seam with a **heliocentric conic coast
of duration `c` between the departure burn and the brake**. It is not a Lambert
solve: the coast is the conic the forward burn's end state defines, propagated
for `c` about the Sun, and its end state must equal the backward brake's start
state. The requested total `T` is an input, and the brake's duration is
`T − break − escape − burn − c − capture`. That leaves six unknowns — the
departure burn's thrust direction (two angles), its duration, the coast's
duration, and the brake's direction (two angles) — against the six seam
components. At `c = 0` the conic is a point and the system is the burn-only seam
again, so the family is continuous by construction.

`PlanAtDuration` hands a coast refusal to this branch only when the refusal is
the trip time squeezing the coast path — the departure burn cannot be placed or
the chain does not close — or when the break is over-thrust. A refusal from the
coast's own geometry (no prograde short-way transfer) is a different answer and
keeps its Lambert reason: the slow end is still the coast. `Plan()`, its floor
search, and every fixed-duration anchor never take this branch, and a request
below the burn-only minimum keeps the coast path's refusal. At the minimum
itself the branch returns the **burn-only plan**: the coast is zero, and the
moving-target loop's own two-second date tolerance is enough to leave a rebuilt
brake marginally outside the chain gate, so the advertised minimum is returned
as the plan it is.

Measured at the epoch (2026-11-15) from Gateway, conic capture. The burn-only
minimum and the coast floor are the two ends of each row; the middle row is the
gap's midpoint:

| Target | Level | Burn-only minimum | Gap midpoint | Coast floor | Δv |
|---|---|---:|---:|---:|---:|
| Mercury | 1 g | 13.86287 d, 2,588.5 km/s | **13.96 d** | 14.06070 d, 1,821.1 km/s | **2,021.4 km/s** |
| Mars | 0.3 g | 16.48669 d, 1,450.4 km/s | **16.65 d** | 16.84485 d, 1,017.1 km/s | **1,140.8 km/s** |
| Jupiter | 0.3 g | 22.55555 d, 2,972.8 km/s | **23.10 d** | 23.73299 d, 1,899.5 km/s | **2,189.8 km/s** |

Each midpoint is an eight-arc plan — break → escape → departure burn → conic
coast → brake → the conic capture chain — and its total is exactly the requested
duration. The three values inside the Mercury gap are **13.90 d / 2,220.7 km/s**,
**13.96 d / 2,021.4** and **14.02 d / 1,891.2**; Mars 0.3 g runs **16.55 /
1,248.8**, **16.65 / 1,140.8** and **16.75 / 1,069.6**; Jupiter 0.3 g **22.80 /
2,421.4**, **23.10 / 2,189.8** and **23.50 / 1,989.3**. The delta-v falls smoothly
as the coast takes more of the crossing, and the propellant with it ([fuel versus
delta-v](#fuel-versus-delta-v)); the two branches meet inside the gate at the
floor. The branch switch is not free: the short coast is a single conic that has
to carry the whole crossing, while the floor lets the coast solver choose the
transfer.

The range has **no hole**: every probe from the advertised minimum to the coast
floor and a little above it builds, and each plan's total is the duration asked
for. Below the minimum the refusal stands: Mercury 13.0 d is below the burn-only
crossing, and Mars 16.0 d is below its 16.49-day minimum. The slow end still
refuses on the Lambert geometry (Mars 305 d: 175.6°).

Two smaller consequences are deliberate. The crossing always ends on the
**conic** capture chain (`AppendConicCapture`), so the `ARRIVAL` row does not
change the drawn minimum's shape — a retime above the floor does. And
`FlightSession.Begin` solves its base plan with `PlanFastest`, so beginning at a
burn-only minimum flies the no-coast plan the preview drew; at a coast-floor
target the base is the coast floor. A preview that sat at its minimum flies the
fresh minimum, because the clock may have moved and the old number can sit a few
seconds above the new minimum. A chosen duration above that base retimes with
`PlanAtDuration`, including inside the gap, where it builds the short coast.

The burn-only crossing's total is an output, so its climb uses the time left
after the solved burn and brake; the plan reports whether the available-time cap
bound rather than claiming a break it could not fit.

Against the certified 1 g floor the burn-only crossing is **0.19783 d
(4 h 45 min) shorter** — 13.86287 against 14.06070 d — and **767.4 km/s (42%)
more expensive**, 2,588.5 against 1,821.1 km/s, about **96 t more propellant** at
1,000 t dry. That is the trade the coast makes: the floor spends the difference
coasting for free, and the burn-only crossing spends it thrusting. It is fast
enough to be the nav computer's minimum at a high level, and too expensive to
replace the coast at a fixed trip time.

# Fuel versus delta-v

The short coast and the profile's fast end are argued in delta-v, but the nav
computer quotes **propellant**. Its `Fuel` row is the rocket equation at the
design exhaust velocity, `v_e = 10,000 km/s`, against the ship's 1,000 t dry mass
([the fuel readout](/simulation/flight-model.md#the-fuel-readout)):

```text
m_p = m_dry (exp(delta-v / v_e) - 1)      PropellantFraction = m_p / m_0
```

`TrajectoryPlan.PropellantKg` and `PropellantFraction` are that readout, and
`TheFastPlansSpendMorePropellantNotLess` prints the table from the plans
themselves rather than recomputing it by hand. The exponential is the reason to
measure in propellant: it exaggerates each delta-v difference. At 1,000 t dry,
1,821 km/s is about 200 t, 2,589 km/s about 295 t, and 2,973 km/s about 346 t.

## The new family

The burn-only minimum, the short-coast midpoint and the coast floor, in the same
order as [the nav computer's minimum](#the-nav-computers-minimum) and [the short
coast](#the-short-coast):

| Target | Level | Plan | Trip (d) | Delta-v (km/s) | Propellant (t) | Share |
|---|---|---:|---:|---:|---:|---:|
| Mercury | 1 g | burn-only minimum | 13.86287 | 2,588.5 | 295.4 | 22.80% |
| Mercury | 1 g | short coast | 13.96000 | 2,021.4 | 224.0 | 18.30% |
| Mercury | 1 g | coast floor | 14.06070 | 1,821.1 | 199.8 | 16.65% |
| Mars | 0.3 g | burn-only minimum | 16.48669 | 1,450.4 | 156.1 | 13.50% |
| Mars | 0.3 g | short coast | 16.65000 | 1,140.8 | 120.8 | 10.78% |
| Mars | 0.3 g | coast floor | 16.84485 | 1,017.1 | 107.1 | 9.67% |
| Jupiter | 0.3 g | burn-only minimum | 22.55555 | 2,972.8 | 346.2 | 25.72% |
| Jupiter | 0.3 g | short coast | 23.10000 | 2,189.8 | 244.8 | 19.66% |
| Jupiter | 0.3 g | coast floor | 23.73299 | 1,899.5 | 209.2 | 17.30% |

Propellant falls monotonically as the trip lengthens, in the same order as
delta-v. The short coast is the part the player controls: every hour added
between the minimum and the floor buys propellant back. The measured rate is
about **20 t per hour** at Mercury, **5.7 t per hour** at Mars and **4.8 t per
hour** at Jupiter.

At a **fixed trip time** the same ordering appears from the other side. Measured
at the epoch from Gateway at 0.3 g:

| Mars trip (d) | Delta-v (km/s) | Propellant (t) | Share of departure mass |
|---:|---:|---:|---:|
| 20 | 495.98 | 1,643 | 62.2% |
| 30 | 209.98 | 233.6 | 18.9% |
| 90 | 38.97 | 39.7 | 3.8% |
| 200 | 11.21 | 11.3 | 1.1% |

A fast trip is not just a shorter one: the 20-day crossing is a huge burn at both
ends, while the 200-day crossing is almost all coast. The monotone trade is the
point, and the short-coast branch lets the player walk along it one buildable
duration at a time.

## The answer

**The fast plans use more propellant, not less.** Against the coast floor:

* **Mercury 1 g** — the burn-only minimum buys **4 h 45 min** (13.86287 against
  14.06070 d) for **767 km/s** more and about **96 t** more propellant: 295.4 t
  against 199.8 t, 22.80% against 16.65% of departure mass.
* **Mars 0.3 g** — **8 h 36 min** (16.48669 against 16.84485 d) for **433 km/s**
  and about **49 t**: 156.1 against 107.1 t, 13.50% against 9.67%.
* **Jupiter 0.3 g** — **28 h 16 min** (22.55555 against 23.73299 d) for
  **1,073 km/s** and about **137 t**: 346.2 against 209.2 t, 25.72% against
  17.30%.

That is the trade the whole profile exposes: a coast minute is a minute of free
travel, and thrusting through it is what makes the fast end expensive. The
short-coast branch does not change that — it lets the player spend time to buy
the propellant back, one buildable duration at a time.

The fuel readout is still a **readout, not a budget**: the ship has no finite
tank, its mass does not change in flight, and no plan is refused for fuel. These
figures describe what a future tank would have to hold
([ADR-016](/decisions/adr-016-fuel-readout.md)).

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

Two things stay as they were. The **attitude** is the profile's: the break follows
the ship's own prograde direction, the departure burn holds its solved thrust
direction, the brake holds the handover change, and a prograde/retrograde burn
follows the velocity relative to the reference body. And the floor probes keep the
**two-body burn** as their search model; every fixed burn in the plan that is
reported and flown is the full-field one, including the hybrid capture's injection
arc.

Measured at the epoch, the change is real in the arcs and almost invisible in the
reported numbers. At the 90-day Mars plan's mid-burn the summed field is
**4.4 × 10⁻⁴ m/s²** away from the Sun's rail at the departure burn and
**1.2 × 10⁻⁴ m/s²** at the brake; a short 120 s burn 4,000 km above Mars ends
**0.17 m** from its two-body counterpart. But a fixed-direction burn's delta-v is
`a T` and `T` is set by the velocity change the chain needs, so the departure
solve absorbs the field difference into the burn's thrust direction and the path,
not the spend. The numbers moved with the physical departure, not because of the
field: 1,762.3 km/s at 14.1 days to Mercury, 869.1 at 16, 38.97 at Mars 90 days,
and floors of 14.06 and 16.84 days. The seams close to 55.8 m and
8.6 × 10⁻² m/s at the short Mercury trip; the Mars 90-day flight arrives exactly
(0.0 m and 7.3 × 10⁻⁶ m/s from the reference state).

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
build's departure burn starts on the climb's ceiling and moves outward, so it
stays outside the sphere — a consequence of the endpoint, not the rule
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
| One fixed-duration `PlanAtDuration` | 105 ms | **95 ms** |
| One Mars 0.3 g `Plan()` | — | **0.85 s** |
| First selection, Mercury (Plan + window + retime) | — | **1.5 s** |
| First selection, Mars (Plan + window + retime) | — | **0.93 s** |
| Repeat selection, same date/target/level/style | — | **under 0.1 ms** |
| Slider retime | 105 ms | **93–99 ms** |

The break-search cost fell by **8.1×** in step 1. Step 2 cut the rest by
searching the floor on the same fast model and settling the departure solve on a
physical tolerance instead of a fixed pass count. The physical departure adds one
conic solve and a one-dimensional break bisection per pass; the cost table below
is the previous profile's measurement and was not re-profiled in step 1. The fast
peak tracks the full-field peak closely because the break is short and the
departure body dominates; inside the 5% screen the full field decides, which is
why the near-limit reports are 9.7000 m/s² at 1 g and 2.9467 at 0.3 g.

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
| Mercury | 1 g | **14.06070 d** | 9.70 m/s² | 1,821.1 km/s | 13.8 d | below the 13.86287 d burn-only minimum; the climb and the burns cannot fit |
| Mars | 0.3 g | **16.84485 d** | 2.95 m/s² | 1,017.1 km/s | 16.0 d | below the 16.48669 d burn-only minimum |
| Jupiter | 0.3 g | **23.73299 d** | 2.95 m/s² | 1,899.5 km/s | 22.5 d | below the 22.55555 d burn-only minimum |
| Uranus | 1 g | **57.02223 d** | 9.70 m/s² | 1,417.8 km/s | not re-swept | the editor's certified floor; the .NET harness was not re-swept in step 1 |
| Uranus | 0.3 g | **92.13088 d** | 2.95 m/s² | 811.1 km/s | not re-swept | the editor's certified floor; the near-floor basin is still noisy |

The floors are the editor's certified full-field plans. The .NET harness was not
re-swept in this step; the near-floor scatter is a solver property and not a defect
(a duration's feasibility is set by whether the departure Newton stalls and how
many passes the brake pin needs, so a last-bit libm difference can select a
different certified point). The editor's **coast** windows are **14.06–34.27 d**
(Mercury 1 g), **16.84–50.53 d** (Mars 0.3 g), **23.73–71.20 d** (Jupiter 0.3 g),
**57.02–171.07 d** (Uranus 1 g) and **92.13–276.39 d** (Uranus 0.3 g); the nav
computer asks for the shorter burn-only windows where that crossing wins (see
[the nav computer's minimum](#the-nav-computers-minimum)). The
divergence's measurement is in [planning
performance](/technical/planning-performance.md#the-runtime-divergence-is-a-property-not-a-bug).

The near-floor basin is still a set of **needles**, not a boundary: the certified
build can close a duration just below the advertised floor while a fast probe
refuses it, so the search is conservative. The cache and the background retime
keep that cost off the frame ([planning
performance](/technical/planning-performance.md#step-1-the-builds-own-cost-2026-11-20)).

The floor search also requires the chain to be **closed**: a plan whose peak
fits but whose brake misses the capture entry is not flyable. The near-floor
trips amplify the brake solve's forward/backward mismatch, so the shortest
plan that fits the peak can end the brake hundreds of thousands of kilometres
from the entry; the planner reports that as a trip time too short for the
profile instead of handing it to the autopilot. The gate is a kilometre of
position and a metre per second of velocity: the 14.1- and 16-day trips close to
centimetres to tens of metres, the 16.84485 d Mars floor's worst seam is 26.7 m
and 3.3 × 10⁻² m/s (and the Mercury floor's 109 m), and a broken brake misses by
thousands of kilometres. The 14.1 d flight arrives inside the test's 10 m /
0.1 m/s bound.

The departure solve itself now stops on the brake's own physical mismatch (1e-6 m/s
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
heliocentric velocity; the pinned-break follow-up replaced that with a forward
solve to a radial ceiling pin; the physical-departure follow-up replaced the pin
with the short prograde break and the escape conic, and the burn's start is now
the conic's ceiling state. The nav-computer control landed in the plan's fourth
step.

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
- **The physical departure.** The break is a short commanded-level burn to the
  parking radius on the ship's own prograde tangent at the planetocentric escape
  speed. A ballistic conic carries that state to the handover ceiling — parabolic
  when the trip allows it, hyperbolic when it does not — and the departure burn
  starts at the conic's ceiling state. Its thrust direction and duration are the
  solve's free variables, not an escape ray; the direction the ship arrived with
  has no relation to the transfer, so the break follows the ship's own orbital
  direction and the burn supplies the aimed departure. The band, the arrival
  endpoint, and the full-field integration are in
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
