---
type: Roadmap
title: Roadmap
description: What exists in the prototype, what comes next, and the risks that could change the plan.
tags: [production, roadmap, milestones]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Current state — milestone 0

**Done: a true-scale, moving orrery with a debug camera.**

* Generated catalogue: 1 star, 8 planets, 22 major moons, 100 main-belt
  asteroids, 6 Lagrange stations.
* JPL-derived planet and asteroid elements, IAU physical data.
* Keplerian propagation in double-precision SI units.
* Lagrange-point station positions computed from the moving pair.
* Ship-relative rendering at 1 unit = 1,000 km.
* Minimum-apparent-size markers and a true-scale toggle.
* Time warp from 1× to 1,000,000× and pause.
* Planet and moon orbit lines.
* Free-flight debug camera and an IMGUI HUD with labels.
* Left-click body selection with a live details screen (physical, orbital, and
  current data) and a three-second eased debug camera move to the selected body.
* Shipped scene at `Assets/Scenes/SolarSystem.unity`, an editor menu to rebuild
  it, and a bootstrap that also self-installs in an empty scene.
* Automated verification: `tools/check.sh` runs the OKF validator, the catalogue
  validator, a byte-for-byte generator check, and the Unity EditMode suite
  (255 tests).

**Acceptance:** open the project, press Play, see Earth and the Sun, warp time,
watch the planets move, click a planet for its details, and jump to it in debug
flight to see the scale. Gateway Station sits at the Earth–Moon L1 point.

# Milestone 1 — the flight core (done)

**Goal: fly a real 10 m/s² burn from Gateway to Mars and arrive in a stable
orbit, with no scene.**

Done in `Assets/Scripts/Simulation/`, verified headlessly by the EditMode suite:

* `ShipState` in double precision, with mass carried as bookkeeping.
* Continuous gravity from every catalogue body, evaluated without mutating the
  model ([ADR-005](../decisions/adr-005-continuous-gravity.md)).
* Adaptive RK4 that advances to a Julian date, so frame rate and time warp do
  not change the result.
* A Go To planner: escape, cruise, and capture arcs, throttleable thrust, exact
  arrival on the parking orbit
  ([ADR-006](../decisions/adr-006-quintic-plan.md)).
* An autopilot that tracks the plan with light position and velocity feedback.
* A circular equatorial parking orbit per body, with a gravity ceiling that
  lifts the arrival orbit until local gravity is 40% of the drive.
* 51 new tests: field, integrator, quintic, parking orbit and its ceiling, SOI,
  planner, and end-to-end flight to Mars, Jupiter, and back to Earth.

**Acceptance met:** plan Gateway→Mars, fly it, and arrive within 0.01 m and
0.0000 m/s of the parking orbit, then hold the orbit for three orbits at an
eccentricity of about 3 × 10⁻⁶. Venus, Mars, Saturn, Jupiter, the Moon, and a
departure from low Earth orbit are all feasible. Jupiter arrives in 10.3 days
at 177,965 km, where the ceiling puts gravity at 4 m/s² instead of the 19.6
m/s² of a surface-radius orbit.

**Known gap:** the plan is 35–75% slower than the ideal bang-bang profile. Mars
takes 4.4 days for 1.32 AU instead of 3.3.

# Milestone 2 — fly it in the scene (done)

**Goal: press Go To on Mars and watch the ship do it.**

Done in `Assets/Scripts/` and verified through the scene's frame loop:

* `FlightSession` owns the `ShipState`, the active plan, the autopilot, and the
  integrator ([ADR-007](../decisions/adr-007-flight-director.md)). The bootstrap
  advances the clock and asks the session to reach the same date.
* The ship starts at Gateway Station, so the default trip is the tested one.
* **Go To** on the details screen calls `TripPlanner.Plan`, raises the warp to a
  ~30-second trip, and flies it.
* `TrajectoryLine` draws the sampled path: solid departure and cruise, dashed
  capture, a flip marker at the thrust reversal, and an arrival marker.
* The flight panel is a phase table — departure, cruise, capture with dates and
  durations, arrival date, flight time, peak thrust and throttle, delta-v — plus
  the live phase, next-event countdown, throttle, and warp.
* `WarpPolicy` drops the warp by clearance, local orbital timescale, and the next
  plan event, and the status panel says when it is holding the clock.
* The frame has a 2,000-substep budget; if it runs out the clock is clamped to
  the date the ship reached, so the world never runs ahead of it.
* After arrival the drive goes off, the ship coasts, and the ship comes back
  under the player: an orbit camera (right-drag, wheel zoom) that frames the
  destination automatically until they take it ([ADR-008](../decisions/adr-008-orbit-camera.md)).

**Acceptance met:** Gateway → Mars, 4.44 days, arrives 0.0 m and 0.0000 m/s from
the parking orbit, holds 3,898 km at e = 3.07 × 10⁻⁶ for three orbits, and does
it in 65.7 real seconds over 3,939 frames at the suggested 10,000×. Peak 63
substeps in a frame; warp auto-drops to 10× for the last second. No teleport.

**Known gaps:** the warp drop is in power-of-ten steps; the ship is a marker
rather than a model; the camera can still be orbited inside a planet (it is
pulled out, but as a rescue); there is no manual thrust; and a second Go To
replans from scratch rather than correcting the existing plan.

# Milestone 2.5 — the system map and route preview (next)

**Goal: one continuous zoom from the ship to the whole system, a top-down map
the player can pan and click, and a route they can see before they fly it.**

This is the next work. The design is
[The system map and route preview](/technical/system-map.md); its
implementation steps are the task list, each with its tests. In order:

1. The zoom-driven blend from the local orbit view to a top-down map at the
   local sphere of influence, with the remembered angle restored on the way
   back in, and panning on the map. **Steps 1–2 are done:** the plain-C#
   `MapView` holds the blend, the dominant-body choice, the 12-billion-km
   range, the map pose and clipping, and the 60 AU pan cap; `ShipCameraRig`
   blends the pose, routes right-drag to orbit or pan, scales the pan from the
   camera distance, eases it home on re-entry, suspends auto-framing on the
   map, and routes the wheel through the extended range. `SystemMap.unity` and
   the `EnableMapView` switch provide the side-by-side comparison.
2. Map reading rules: bigger markers, label density by zoom, class-priority
   picking, the map-only ship label, and the camera-distance orbit width. **Step
   3 is done:** `MarkerSizing` eases markers from 4.5 to 6 px, `BodyLabels`
   applies the 5× parent-SOI moon window and the hover/selection-only
   asteroids, `BodyPicker` ranks Sun/planet/station above moons and asteroids
   on the map, and `OrbitLine` stops a distant orbit becoming a ribbon at close
   map zoom.
3. The local view stops inflating distant bodies; the marker aid becomes
   neighbourhood-aware. **Next.**
4. **Plot route** draws the plan as a smooth preview curve with an arrival
   ghost; **Begin** replans from the current state and flies it. The preview
   is a promise of shape, not a contract: time passes between Plot and Begin.

**Acceptance:** zoom from a parking orbit to the whole system and back to the
exact prior angle; pan to Jupiter and click it; plot a route, see the curve,
press Begin, and arrive with the existing exactness.

# Milestone 3 — fuel and the delta-v economy

* Charge `PlanArc.DeltaVMps` against a finite budget.
* Make acceleration mass-dependent, or hold it constant by throttling.
* Refuse a plan the budget cannot pay, and offer the cheap coasting alternative.
* Add the fuel UI and the end-of-fuel failure state.

**Open design choice:** constant acceleration with decreasing mass (more honest)
versus constant acceleration with throttling (easier to fly). The current
preference is constant acceleration with throttle, showing the mass flow and
heat cost in the HUD.

# Deferred: transfer-conic capture

Recorded deliberately, not next. The gravity ceiling makes every body reachable
by moving the arrival orbit out; a transfer conic would fly the same arrival
*lower and cheaper*, and it is the physically honest capture. Kept out of
milestone 2 so the scene work is not blocked on a new propagator.

* Add a `ConicArc` that propagates a state vector with Kepler's equation.
  `OrbitalElements.GetState` already goes elements → state; the inverse
  (state → elements, or a universal-variable propagator) does not exist yet.
* Let the capture chain coast. On a conic `a_ref = g` pointwise, so the commanded
  thrust is exactly zero and the quintic endpoint problem disappears entirely.
* Burn at periapsis, then circularise, ending on the parking orbit. That is the
  Oberth-optimal arrival the current spiral only approximates.
* Expected payoffs: Earth arrivals back below the Van Allen belts, Saturn's
  parking orbit out of the ring system, a lower Jupiter orbit, and less capture
  delta-v.
* The gravity ceiling stays in place until this lands; it is what makes the
  current capture flyable, and any future planner must satisfy it or report the
  arrival infeasible.

# Immediate next tasks

1. **Build the system map and route preview (milestone 2.5, above).** This is
   the next work; the design doc's implementation steps are the ordered task
   list.
2. Close the gap to the ideal profile with a bang-bang planner once fuel makes
   time and delta-v compete. A transfer conic (above) closes part of it in
   delta-v rather than time.
3. Add a mid-course correction that keeps the current plan instead of replanning
   from the current state. Begin's replan is the first place this matters: a
   correction would keep the plotted curve instead of solving a new one.
4. Give warp a continuous scale so the drop near a body is smooth rather than a
   step down the power-of-ten ladder.
5. Add a PlayMode suite that drives the real bootstrap and the real renderer, so
   the frame order, the trajectory line, and the HUD are covered end to end.

# Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| The 1 g torch is physically implausible | Certain | Medium | Treat it as the one admitted lie: a mature compact fusion torch whose waste heat leaves with the exhaust; fuel is the balance lever |
| Time warp makes arrival trivial or chaotic | Medium | High | Implemented: the warp policy caps a frame at 25% of the time to the nearest surface, 2% of the local orbital timescale, and 5% of the time to the next plan event, and clamps the clock to the ship when the substep budget runs out |
| True scale is unreadable without UI | High | High | Labels, markers, trajectory lines, target-relative HUD; the flight panel carries the phase table and countdown |
| Float/depth artefacts on a target platform | Medium | Medium | Ship-relative rendering; reversed-Z; two-camera fallback |
| Autopilot removes the gameplay | Medium | High | Autopilot handles long burns; the current build is plan-and-watch, and manual override and fuel are the milestone-3 answer |
| The smooth plan is 35–75% slower than the design's tables | Certain | Medium | Show the plan's real time, not the ideal, in the HUD; close the gap with a bang-bang planner once fuel makes time and delta-v compete |
| The gravity ceiling moves arrival orbits out on the big bodies | Certain | Medium | Accepted: the alternative is an unreachable destination. A transfer-conic capture would restore lower, cheaper arrivals |
| Data accuracy drifts with epoch | Low | Low | Regenerate from JPL; document epoch and accuracy |
| Scope grows beyond a solo project | High | High | Milestone gates; one ship, one system, no combat |
