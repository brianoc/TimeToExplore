---
type: Decision Record
title: "ADR-027: continuous flight warp and the chase camera"
description: A flight owns its warp as a continuous real-time curve that starts and ends at 1x, and a chase camera that sits behind, above, and to one side of the route; the discrete ladder and the proximity warp caps are removed.
tags: [decision, simulation, time, warp, camera, flight, presentation]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T14:10:00Z }
---

# Context

The clock exposed a power-of-ten ladder (`1x`, `10x`, … `1,000,000x`) and the
flight policy capped the chosen rung by physical limits: clearance to the
nearest body, the local orbital timescale, the next plan event, and a station's
hold point. The player chose a rung; the policy could only lower it. Go To
picked the rung whose trip took about 30 real seconds, so a week-long crossing
ran at a constant scale and a three-week one at the next rung — a 145-second
trip — and the HUD said `warp held by the flight policy` whenever a cap bit.

The camera framed the destination (or the whole planned route) and followed it.
During cruise that is the right reading of a route, but it makes the ship a
speck and the departure or arrival a distant event. There was no shot of the
ship under way.

A flight also has a natural shape in real time: it should pull away slowly while
the view settles, cross the system quickly, and come back to real time for the
approach, because that is where the interesting motion is. A ladder cannot do
that, and a table of times per route would be a second source of truth beside
the planner.

# Decision

1. **The clock's scale is continuous.** `SimulationClock.Scale` is any
   non-negative `double`; `Advance` multiplies by it. The number keys still
   select a fixed scale from a seven-entry manual ladder, and `P` still pauses.
   A digit press takes warp off the flight curve until the next flight begins;
   the HUD's warp label prints the actual scale, so the curve reads as a moving
   number rather than a rung.
2. **A flight follows `FlightWarp`.** The target real duration is
   `20 + 17 · ln(days / 6.7)` seconds, clamped to 12–60 s. A one-week crossing
   takes about 20 s, three weeks about 40 s, two months about 57 s, and the
   outer system tops out at 60 s. The cruise scale is chosen from that target
   and the two ramps' half-area, so integrating the curve over the target
   duration flies exactly the planned flight. Both ends blend over three real
   seconds from 1x to cruise with a smootherstep: the first second is close to
   real time, and the last few seconds are real time again.
3. **The proximity caps are removed.** `WarpPolicy` is deleted. The only limit
   left is the flight's substep budget: if the integrator cannot cover a frame,
   the clock is clamped to the state the ship actually reached, as before. There
   is no separate clearance, orbital-timescale, event, or station-hold rule.
4. **A flight takes the chase pose.** On Begin, `FlightFraming` derives the
   orbit angles from the direction to the destination: the camera sits behind
   the ship, 20° above the route and 30° to one side, at a fixed 20,000 km.
   `ShipCameraRig.FrameFlight` eases yaw, pitch, distance, and the look-at
   offset to that pose over 1.2 s. The angle is chosen once, not tracked as the
   route curves: the ship is the render origin, so holding the pose is enough
   to follow it, and a right-drag or wheel hands the view back. On arrival the
   view hands over to the existing destination framing, so the shot settles on
   the parking orbit instead of holding the cruise pose.

# Consequences

* The HUD's warp read is honest and continuous. It no longer needs the
  `warp held by the flight policy` line, which is gone with the policy.
* The trip is paced by its own duration, so a slow requested crossing (the
  right end of the trip-time window) also reads correctly without a table.
* The substep budget, not the warp cap, is the limit on a very long trip: an
  outer-system crossing at 60 s runs a large simulated span each frame and may
  spend several hundred substeps. The clock clamp keeps the world behind the
  ship if the budget runs out, so the trip stretches rather than skips.
* A manual warp choice lasts for one flight. That is deliberate: the curve is
  the default feel, and an override is a debugging tool rather than a setting
  the next flight inherits.
* The chase distance is fixed because the ship marker has a fixed apparent
  size. It is a composition choice, not a physical one; the marker grows from
  10 px to 56 px as the camera closes inside 100,000 km, so the chase shot has a
  subject, and the thrust arrow grows with it. The art pass can replace both
  with a real ship model.
* A route that curves hard may leave the destination off-centre during cruise.
  The user asked for a fixed angle and a manual orbit; tracking the heading
  would be a second camera mode and was rejected for now.

# Alternatives considered

**Keep the ladder and only remove the caps.** Rejected: a constant rung cannot
start at real time, and the power-of-ten jumps are the thing the request
replaces.

**Fit a curve to a table of per-route real times.** Rejected: the planner
already produces the duration, and a table would drift from it. The logarithmic
target is one formula with the right shape.

**Ramp the warp against simulated progress instead of real time.** Rejected:
the end ramp then returns to 1x with a long simulated tail still to fly, which
stretched a 20-second Mars trip past 400 s. Ramping against real time makes the
integral exact and puts 1x at the frame the flight ends.

**Track the route heading through the flight.** Rejected for now: the request
was a fixed angle, and the ship stays at the render origin, so a fixed pose
already follows it.

# Acceptance

`FlightWarpTests` pins the target durations, the clamped ends, the 1x start and
finish, the cruise plateau, and the integrated duration at 6.67, 21.5, 60, and
111 days. `SceneFlightTests` flies the Gateway → Mars acceptance trip through
the scene's frame order and checks the real duration and the high peak warp.
`CameraRigTests` checks the chase pose's elevation, side angle, and distance,
the eased arrival, and the drag and arrival handbacks. `BootstrapSmokeTests`
checks that Begin takes the camera and the warp curve, and that a preview never
does. `tools/check.sh` is the gate.
