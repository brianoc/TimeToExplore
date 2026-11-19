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
3. **The burns.** `TripPlanner.CruiseStyle.BurnCoastBurn` is quintic break →
   prograde departure burn → Lambert coast → brake → conic or hybrid capture.
   Both burns sit about the Sun. The departure burn spends the coast's departure
   speed above the local escape speed, so its delta-v is independent of the
   level; the brake spends the handover the coast skeleton would have made an
   impulse. Both of the profile's burns are placed by integrating them backwards
   from the state the next arc must begin at, so each seam is continuous and the
   arrival is exact.
4. **The control.** The nav computer's `PROFILE` row selects Powered, Coast, or
   Burn-coast-burn; the trip-time slider is the profile's speed control and its
   window is that profile's own feasible range; the existing burn-level slider
   is the level both burns are planned at.

The measured window at the epoch is **57.44 to 152.57 days** for the
Gateway→Mars burn–coast–burn profile, against the powered plan's 4.4 days and
the cheap coast's 200+. `Powered` is still the default, and `0 g` is exactly
`CruiseStyle.Coast`.

# How it was cut

Two stages, both part of the same implementation:

**Stage A — the coast skeleton.** Add the Lambert solver and build a trip as a
quintic departure retargeted to the Lambert's `v₁`, a heliocentric `ConicArc`
coast, and the existing conic/hybrid capture. Landed as
`TripPlanner.CruiseStyle.Coast`.

**Stage B — the burns.** Add the prograde departure fixed burn and the
fixed-thrust brake, stitch the five arcs, and expose the burn level and the trip
time. Landed as `TripPlanner.CruiseStyle.BurnCoastBurn`, with no impulse at the
target's sphere of influence. Because a burn along the velocity can only change
the speed, the brake's thrust direction is the change the chain needs and
opposes the arrival velocity, not an anti-velocity direction. The nav-computer
control landed in the plan's fourth step.

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
- **Brake and capture split.** The brake spends only the coast's handover and
  hands the capture chain its entry state with no impulse; the split is not
  optimised. The brake's thrust direction follows the change the chain needs
  rather than the velocity alone, because a purely anti-velocity burn cannot
  close the seam.
- **Slow crossings.** A coast that arrives slower than the target's frame needs
  an acceleration rather than a brake, so those trip times are rejected. The
  profile is the fast, expensive end of the trade; the coast remains the cheap,
  slow one.
