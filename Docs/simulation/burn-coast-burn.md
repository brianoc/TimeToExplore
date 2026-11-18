---
type: Design Document
title: Burn–coast–burn trips
description: The trip profile that burns hard, coasts fast, and brakes into orbit, built from the four existing arc ingredients.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-18T00:00:00Z }
---

# The trip

Leave a planet, cross to another, and park there. The ship should be able to:

1. break out of its parking orbit,
2. burn hard for a while and build speed,
3. coast with the drive off, still travelling fast,
4. flip and burn hard to slow down,
5. drop into the target's parking orbit.

This is the standard **burn–coast–burn** profile, sometimes called a
brachistochrone with a coast. It is not exotic physics. It is the profile most
torch-ship fiction uses, and it sits between the two extremes the planner
already has: the all-torch quintic (fast, ruinously expensive) and the
Hohmann-like coast (slow, cheap).

# Four ingredients, not four journeys

The current planner offers three fixed recipes: all-quintic, conic arrival, and
hybrid arrival. The route colours made them look like three separate kinds of
trip. They are really three **arc shapes**, plus one solver, and a trip can mix
them:

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

Every arc type already exists. The current planner simply does not emit this
recipe.

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

# What exists and what is missing

**Exists:** `Quintic`, `ConicArc`, `FiniteBurnArc`, the conic and hybrid
captures, the autopilot, coasts and burns as plan phases, dashed coasts, burn
markers, and the route legend.

**Missing:**

1. **The Lambert solver.** The coast must be chosen, not guessed. This is the
   universal-variable sibling of `KeplerPropagator`. It has landed in Core as
   `LambertSolver.SolveShortWay` (zero-rev, prograde, short-way).
2. **The planner that stitches the arcs.** It chooses when to stop burning,
   where the coast goes, and when to brake, so the chain closes on the moving
   target. This is a small targeting/fixed-point solve, the same pattern as the
   conic planner's five-pass arrival loop. Stage A now does it for the coast
   cruise; the burn timing is still missing.
3. **The departure fixed burn.** The hybrid capture is the arrival version; a
   prograde finite burn out of the parking orbit is its mirror and does not
   exist.
4. **The control.** "How fast do I want to go" is the natural knob. The existing
   trip-time slider is the right shape: short time = long burns, no coast; long
   time = short burns, long coast.

# The first cut

Two stages, both part of the same implementation:

**Stage A — the coast skeleton.** Add the Lambert solver (zero-rev, prograde,
short-way) and build a trip as a quintic departure retargeted to the Lambert's
`v₁`, a heliocentric `ConicArc` coast, and the existing conic/hybrid capture.
This proves the coast arrives and the capture still works. It has landed as
`TripPlanner.CruiseStyle.Coast`, with the powered cruise still the default.

**Stage B — the burns.** Add the prograde departure fixed burn and the
retrograde brake, stitch the five arcs, and expose the burn level and the trip
time. This delivers the trip described at the top.

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
  and starts the steady burn outside.

# Open choices

- **Default profile.** The existing quintic capture stays the default until the
  player has a reason to choose otherwise; with fuel modelled, the coast should
  become the obvious default.
- **Control units.** A trip time (matching the current slider) or a peak speed.
  Time is the simpler first control.
- **Brake and capture split.** The brake can stop at the target's heliocentric
  velocity and hand over to the capture, or do part of the orbit insertion
  itself. The planner should choose the cheaper split.
