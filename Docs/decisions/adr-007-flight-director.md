---
type: Decision Record
title: "ADR-007: One owner for the ship state, and an automatic warp cap"
description: Give the flight session sole ownership of the ship, demote free flight to an explicit debug mode, and cap time warp from clearance, local dynamics, and the next plan event.
tags: [decision, simulation, architecture, time-warp, scene]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-16T00:00:00Z }
---

# Context

Milestone 1 left the ship as three unrelated things at once: a debug camera that
wrote `Ship.PositionM`, a focus transition that also wrote it, and a headless
flight core that nothing in the scene called. Milestone 2 has to press Go To and
watch the ship fly, which means one of those writes must become authoritative.

Two more problems come with a real trajectory:

* **Time warp can outrun the ship.** At 1,000,000× a 60 fps frame spans 4.6
  simulated hours. A frame can cross the whole Mars parking orbit twice, or skip
  the instant a burn ends. The integrator is adaptive and accurate, but the
  player sees jumps and the HUD countdown hits zero without a visible arrival.
* **The integrator can be too slow for one frame.** Near a body the step is about
  ten seconds; a 4.6-hour frame would need 1,600 substeps, and a deep passage
  would need far more. A frame budget must exist, and if it is exhausted the
  world must not run ahead of the ship.

# Decision

1. **`FlightSession` owns the ship.** It holds the `ShipState`, the active
   `TrajectoryPlan`, the `Autopilot`, and the `ShipIntegrator`. It is plain C# in
   `Assets/Scripts/Simulation/`, with no `MonoBehaviour`, so the whole scene
   flight loop is verified in EditMode.
2. **The bootstrap is the only caller.** Each frame it advances the clock, asks
   the session to reach the same Julian date, and clamps the clock back to the
   date the session actually reached. `ShipController` reads the position through
   a provider and reports debug movement through a sink; it never stores a second
   copy.
3. **Free flight is an explicit debug mode** on the backquote key. Entering it
   cancels the flight. It is a teleport, not a trajectory, so it clears the
   velocity. The old "Fly to" action becomes **Jump (debug)**: the same eased
   three-second move, disabled while a flight is running.
4. **Time warp is a ceiling, not a wish.** The player's key press sets the
   requested scale; `WarpPolicy` may only lower it, by the smallest of:
   * a quarter of the time to the nearest body's surface at the current relative
     speed;
   * 2% of the nearest body's orbital timescale `sqrt(r³/GM)`;
   * 5% of the time remaining to the next plan arc boundary or the arrival.
   The HUD says when the policy is holding the clock below the request.
5. **Go To raises the warp itself**, to the power of ten that makes the trip take
   about 30 real seconds. A four-day trip at 1× is not a feature.
6. **The session stops the ship on the arrival date**, even when the clock asks
   for longer, and the clock is clamped back to it. That keeps the matched
   arrival state exact and makes the arrival instant visible for one frame.
7. **The camera keeps the destination framed** for the whole flight rather than
   following the thrust axis. A capture burns retrograde; facing the burn would
   point the camera at empty space at the one moment the planet should fill it.
   The burn direction is on the path line and in the panel instead.

# Consequences

**Positive**

* There is one writer of the ship state, so a flight cannot be teleported out
  from under itself.
* The whole loop — clock, warp cap, ship advance, clamp — is a plain-C# function
  of state, and the acceptance flight is reproduced by an EditMode test.
* The world can never disagree with the ship. If the substep budget runs out,
  the clock stops where the ship stopped and resumes next frame.
* The warp cap is expressed in physical time, not in a hand-tuned per-body
  table, so it follows the catalogue and the drive.
* The arrival is exact (0.0 m, 0.0000 m/s in the frame-loop test) instead of a
  few kilometres off from coasting past the arrival instant.
* Free flight survives for inspection without pretending to be a flight model.

**Negative**

* The warp cap uses the clock's fixed power-of-ten scales, so the drop is in
  steps rather than continuous. A continuous scale would read better.
* The destination-framed camera does not show the capture burn's direction. The
  burn markers and the throttle row are the only cues.
* The session integrates in legs cut at frame boundaries, so the sub-step
  sequence — and therefore the last digits of the trajectory — depends on the
  frame rate. The difference is far below the navigation budget, and the test
  tolerances are scaled accordingly.
* `FlightSession` is now the place every future ship concern lands: fuel,
  attitude, re-planning, and save/load all belong to it or to something it owns.

# Alternatives considered

**Let the debug controller keep the position and have the flight write it too.**
This is what the scene had, and it is exactly the bug: two writers, one frame,
last one wins. Rejected.

**A `MonoBehaviour` flight director.** Fine for Unity wiring, but it would move
the decision logic into a class that EditMode cannot instantiate without a scene.
Kept as a plain class with a thin bootstrap call instead.

**Integrate by `deltaTime` and then snap the ship to the plan.** Frame-rate
dependent, and it throws away the integrator's accuracy. The whole point of
`ShipIntegrator.Advance(..., toJulianDate, ...)` is that it does not care how the
caller got the date.

**No warp cap; let the player be responsible.** The risk table already rates
"warp makes arrival trivial or chaotic" as high. A 4.6-hour frame at 1,000,000×
makes the HUD countdown fiction.

**A chase camera offset.** Rejected for milestone 2: the ship at the render
origin is the whole floating-origin implementation
([ADR-002](adr-002-double-precision.md)), and a chase offset reintroduces the
metre-scale float problem for no navigational gain.

# Revisit when

* warp needs a continuous scale rather than powers of ten,
* the player is allowed to hand-fly, which needs a manual-thrust path that
  suspends the plan without cancelling it,
* fuel makes re-planning mid-flight common, so the session needs to own a plan
  history,
* a PlayMode suite can drive the real `SolarSystemBootstrap` and this ADR's
  frame order should be asserted there too.
