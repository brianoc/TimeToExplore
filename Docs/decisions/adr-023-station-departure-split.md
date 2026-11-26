---
type: Decision Record
title: "ADR-023: the station departure split"
description: A station with a departure body takes the same excess-speed choice as a planet, entered at the hold state with no capture chain. The Mars orbit floor is faster and costlier, fixed durations are cheaper, and the window stays three times the floor.
tags: [decision, simulation, navigation, stations, departure, trajectory, window]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T15:45:00Z }
---

# Context

[ADR-022](/decisions/adr-022-departure-split.md) chose the escape conic's
hyperbolic excess by cost once per build, but decision 5 left **stations** on the
unforced climb. A station plan has a velocity match onto a hold point instead of a
capture chain, and the planet cost priced its brake against the chain's entry
state. With no chain there was nothing to price, so the chooser returned nothing
and the station kept the minimum-energy parabola whenever the trip allowed it.

That was a gap, not a design choice. The station terminal is the same departure
and coast followed by one match, so the same trade exists: a little excess at
periapsis shortens the climb, lengthens the coast and shrinks the departure burn
and the match. Step 1 measured it and step 2 wired it in; this record settles the
rule and re-baselines the station anchors.

# Decision

1. **A station with a departure body takes the split.** The cost enters at the
   **hold state** at the arrival date — the station's own state 500 m astern
   along its velocity — so it subtracts no chain duration and needs no arrival
   handover band. The break, the departure burn and the match are the same three
   terms as the planet cost, with the same `0, 1, 3, 6, 10, 16` km/s grid, the
   same 600 s brake seed, the same climb solve and one refinement round.
2. **The choice is made once per build and held.** `Build` passes the chosen
   value to every station build for that call; the reference skeleton, the pin
   loop, the settle pass and the certified rebuild all use it. `PlanStationFloor`'s
   bracket and bisection probes go through `Build`, so the probes and the
   certificate share one rule. A heliocentric start still has no candidate and
   returns no chosen value.
3. **The trip window stays three times the split-aware floor**, applied to the
   station minimum exactly as to a planet's. The right-hand cap is probed and used
   when feasible, so the station window is exactly 3× the floor.
4. **A station whose parent is the departure body never climbs.** `FindDepartureBody`
   skips the target's parent, so Helios Watch and Nyx Anchor, both children of
   Earth, have no departure body for a ship inside Earth's sphere. Their plans stay
   the three-arc heliocentric trip. Widening the rule would reach the Earth → Moon
   and Earth → planet plans, so it is separate work.

# Consequences

**Measured at the epoch (2026-11-15), 0.3 g, conic arrival.** Plan totals in km/s;
the chosen excess is in parentheses.

| Case | Before | After |
|---|---:|---:|
| Mars 300 km → Gateway, coast floor (`Plan`) | 59.57357 d / 76.4939 | **54.36495 d / 98.4091** (16) |
| Gateway → Ares Junction, coast floor (`PlanFastest`) | 16.75780 d / 1,033.4 | **7.10572 d / 1,073.7** (16) |
| Gateway → Ares Junction, 60 d | 71.9303 | **67.8154** (2.0) |
| Gateway → Ares Junction, 90 d | 39.3398 | **38.9403** (1.0) |
| Gateway → Ares Junction, 120 d | 24.7331 | 24.7331 (0.0) |
| Gateway → Ares Junction, 160 d | 15.1026 | **15.0673** (0.5) |
| Mars 300 km → Gateway, 120 d | 25.6479 | **24.3137** (1.0) |

* **The floor is faster but costlier.** The shortest certified trip takes the
  grid's 16 km/s cap, the same fast-end trade every planet floor took. It is not a
  regression and must not be tuned away by loosening the cap or a gate.
* **Every fixed duration is cheaper.** The 120-day Gateway → Ares Junction plan is
  unmoved because the chooser chooses 0 km/s there.
* **The station window is 54.36–163.09 d**, against 59.57–178.72 before.
* **The Gateway → Ares Junction window moved to 7.11–21.32 d**, against
  16.76–50.27 d: the split-aware floor is 7.10572 d at the 16 km/s cap. Fixed
  durations above the 3× cap — 60 days and up — are direct `PlanAtDuration`
  builds, not slider positions. That was already true before the split, but the
  cap is lower now.
* **The plan is exact.** The floor is five arcs; every seam closes inside 1 km and
  1 m/s and the hold state is reached at **9.4 × 10⁻³ m and 8.0 × 10⁻⁷ m/s**.
* **The search stays cheap.** A station chooser takes **4.1–4.5 ms**,
  inside the 50 ms budget, because it builds the hold state, the departure band
  and the departure body once per search.
* **Unchanged anchors:** Ares Junction → Gateway at 160 d is 12.2391 km/s and its
  fastest is 54.16210 d / 69.541 km/s; Gateway → Mars at 90 d is 38.5411 km/s; and
  Gateway → Helios Watch is 1.733 / 1.565 / 1.481 / 1.415 km/s at 60 / 90 / 120 /
  160 d.

# Alternatives considered

**Leave stations unforced.** Rejected: every fixed station duration kept paying
the unforced climb, and the planet measurements show the split cheaper there. The
station floor would also stay 5.2 days longer than the plan the profile can build.

**Iterate the brake duration per candidate.** Rejected: the common 600 s seed
ranked the cheap winner within **0.5 km/s** of the full search everywhere, and the
worst cheap-versus-full total gap was **1.216 km/s**, well inside one grid step. A
per-candidate brake iteration would multiply the search cost for no change in the
choice.

**Let the target's parent be the departure body.** Deferred, not rejected: it
would give Helios Watch and Nyx Anchor a climb from Gateway, but the same rule
reaches the Earth → Moon and Earth → planet plans, which is a departure change of
its own. This record keeps the current rule and records the limitation.

# Acceptance

`StationDepartureSplitTests` (`flight`) grids the cheap chooser against the full
fast-probe station search on Gateway → Ares Junction and Mars 300 km → Gateway,
checks the two no-climb cases, and pins the floor, the four Gateway → Ares
Junction anchors, the Mars 120-day anchor, the 50 ms budget, the certified
hold-point arrival, the closed seams and the unmoved planet and heliocentric
anchors. `FlightSessionTests.TheStationWindowIsTheSplitAwareFloorAndThreeTimesIt`
places the ship in a Mars 300 km orbit and pins the preview window at
**54.36–163.09 d**. `StationTripPlannerTests` pins the 120-day plan at 24.314 km/s.

The reasoning is in [Navigation and arrival](/simulation/navigation-and-arrival.md),
the profile in [Burn–coast–burn trips](/simulation/burn-coast-burn.md), and the
planet decision in [ADR-022](/decisions/adr-022-departure-split.md).
