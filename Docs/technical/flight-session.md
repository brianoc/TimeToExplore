---
type: Architecture
title: The flight session
description: How pressing Go To flies the ship in the scene, how warp is capped, and what the trajectory line and flight panel show.
tags: [technical, flight, time-warp, rendering, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-16T00:00:00Z }
---

# What this adds to the orrery

Before milestone 2 the scene was an orrery with a debug camera. The ship was
three unrelated writes to one position field, and the flight core existed only
for tests. Now the scene has a ship:

* it starts at Gateway Station, so the default trip is the one the suite
  measures;
* **Go To** on the details screen plans the trip and flies it;
* the plan is drawn in the world, with the capture arc dashed, a flip marker,
  and an arrival marker;
* the flight panel shows the phase table, the countdown, the throttle, and the
  delta-v;
* time warp drops by itself near a body and before each plan boundary;
* after arrival the ship coasts in the parking orbit with the drive off, and the
  player gets the camera back — an orbit camera, not the old fixed view.

`FlightSession` is the single owner of the ship ([ADR-007](/decisions/adr-007-flight-director.md)).
The bootstrap writes no ship position; it only advances the clock and asks the
session to reach it.

# The frame

`SolarSystemBootstrap.Update` runs the same five steps every frame:

1. `Model.Update(Clock.JulianDate)` — the orrery at the frame's start.
2. `WarpPolicy.MaxFrameSeconds(...)` — how much simulated time one frame may
   span at this state, then `ClampScaleIndex` against the player's request.
3. `Clock.Advance(Time.deltaTime)` with the capped scale.
4. `FlightSession.Advance(Clock.JulianDate, SubstepsPerFrame)` — integrate the
   ship, split at plan arc boundaries, stopping on the arrival date.
5. If the session reached an earlier date than the clock asked for, the clock is
   **clamped back** to the session's date. Then `Model.Update` again.

Step 5 is the important one. The integrator has a per-frame substep budget
(2,000 by default). In cruise a 600-second step covers most of an arc; in a Mars
parking orbit the step is about 12 seconds, so a 16,700-second frame would need
roughly 1,400 substeps. When the budget runs out, the ship is left at the last
completed substep and the world follows it, so rendering and physics never
disagree and the next frame resumes from there.

The session also stops at the plan's arrival date even when the clock asks for
longer. That is what makes the arrival exact: the last leg's target is the
arrival instant itself, and the integrator snaps its final step to a requested
date.

# The warp policy

The player's 1–7 keys set a **ceiling**. Three physical limits may lower it, and
the smallest wins:

| Rule | Limit on one frame | Why |
|---|---|---|
| Clearance | 25% of the time to the nearest body's surface at the current relative speed | Do not cross a body between frames |
| Local dynamics | 2% of `sqrt(r³/GM)` for the nearest body | Keep an orbit a curve, not a jump |
| Plan events | 5% of the time to the next arc boundary or the arrival | Land the countdown on the event |

The result is rounded down to the clock's power-of-ten scales, so the HUD always
shows an honest scale. Measured caps at 60 fps:

| State | Cap on one frame | Allowed scale |
|---|---:|---:|
| Deep space, 0.5 AU from Earth | 328,932 s | 1,000,000× |
| Mid-cruise, 173,461 s to the boundary | 8,673 s | 100,000× |
| Mars parking orbit, 3,898 km | 23.5 s | 1,000× |
| Final second before arrival | 1.0 s (floor) | 10× |

Go To also **raises** the warp, to the scale that makes the trip take about 30
real seconds. Gateway→Mars is 4.44 days, so it starts at 10,000× (38 s) and the
policy takes over near Mars. The status panel says `warp held by the flight
policy · auto from x1,000,000` whenever the cap is binding.

# The plan in the world

`TrajectoryLine` samples the plan once, at 64 points per arc, and re-projects it
into the ship-relative frame every frame. It draws

* a **solid** line for the departure and cruise arcs;
* a **dashed** line for the capture arc, using a repeating alpha texture tiled
  along the line, with the dash length tied to the line width so it stays
  readable at any zoom;
* an **orange marker at the flip** — the point where the along-track thrust
  changes sign, which is the top of the arc rather than a bang-bang switch. On
  the Mars plan it is 3.3 hours after departure;
* a **green marker at the arrival point**, on the parking orbit.

Line width follows the **camera distance**, not the path: `0.0015 × camera
distance`, which is about 1.5 pixels at the near end. The earlier rule used the
farthest point of the plan, which made the line 225,000 km thick on a 1 AU trip
and swallowed the view once the camera could orbit. The path is sampled from the
plan, not integrated, so it costs the same whether the ship is next to it or an
AU away.

# The flight panel

`FlightReport` builds the panel as plain `DetailRow` data, the same pattern as
`BodyDetails`, so its content is tested without a scene:

```
PLAN
  Target        Mars
  Departure     2026-11-15 00:00 · 6.6 h
  Cruise        2026-11-15 06:35 · 3.9 d
  Capture       2026-11-19 05:25 · 5.1 h
  Arrival       2026-11-19 10:29
  Flight time   4.4 d
  Peak thrust   9.76 m/s² (98%)
  Delta-v       2,345 km/s
NOW
  Status        Under way
  Phase         Departure
  Next event    Cruise  T− 5.4 h
  Throttle      48% · 4.8 m/s²
  Flip          T− 2.1 h
  Warp          x10,000
```

The header shows the target and the status, and the line under it is the
countdown to the next action (`Arrival  T− 12m 04s`). On arrival the status
reads `Arrived, in orbit`, the countdown reads `Arrived`, and the flip reads
`passed`.

# Input in the scene

| Input | Action |
|---|---|
| **Go To …** on the details screen | Plan and fly to the selected body; raises the warp. Reads **Plan infeasible** and is disabled if the body's last plan was rejected |
| **Jump (debug)** / `F` | Three-second eased debug teleport to the selected body; disabled in flight |
| `X` | Cancel the flight; the ship keeps its state |
| `` ` `` | Toggle debug free flight; cancels the flight |
| `R` | Reset the ship to Gateway and the warp to 1× |
| `1`–`7`, `Space` | Requested warp and pause; the policy may lower the request |
| `Esc` | Close the details screen / cancel a debug jump |
| Right-drag, wheel | Orbit the camera and zoom |
| `W A S D Q E`, `,` `.` | Debug free flight only (move and change speed) |

# The camera

The ship sits at the render origin, so the camera **orbits** that origin and
always looks back at it ([ADR-008](/decisions/adr-008-orbit-camera.md)):

* right-drag turns yaw and pitch, clamped to ±85°;
* the wheel zooms 1.25× per notch, from 2,000 km out to 5 billion km;
* a plan, a debug jump, or a reset frames its destination automatically, and the
  aim and the distance follow separately, so a wheel zoom keeps the planet
  centred and only a drag stops the aiming;
* the auto distance is `clamp(0.6 × ship→target, 3 × radius, 3.3 AU)`, so it
  opens up over a whole plan and closes to three planetary radii at arrival;
* the near plane scales with the orbit (2%, clamped to 200 km–50 km) so close
  inspection does not cut a planet open;
* a ten-pixel ship marker sits at the origin, pointing along the thrust while
  the drive is on and along the velocity when it is off.

The view deliberately does **not** follow the thrust axis: a capture burns
retrograde, and facing the burn would point the camera at empty space at the
moment of arrival.

# Acceptance, measured

Run through the scene's frame loop at 60 fps, without a scene
(`SceneFlightTests`):

| Quantity | Value |
|---|---|
| Departure | Gateway Station, 2026-11-15 00:00 UTC |
| Arrival | 2026-11-19 10:29:02 UTC (4.44 days later) |
| Arrival error | 0.0 m, 0.0000 m/s |
| After three orbits | r = 3,898 km, e = 3.07 × 10⁻⁶, drive off |
| Real time | 65.7 s over 3,939 frames at the suggested 10,000× |
| Peak substeps in one frame | 63 of the 2,000 budget |
| Warp | x10,000 → x10, auto-limited |
| Teleports | none |

The budget never bound, but a starved-budget test with two substeps per frame
proves the clamp: 400 frames reach only 2026-11-15 05:53 and the clock is never
ahead of the ship.

# Verification

`FlightSessionTests` covers ownership, planning, the exact arrival, budget
clamping, cancel, teleport, a flight through a body, and that planning leaves the
orrery untouched. `WarpPolicyTests` covers the three caps and the scale
rounding. `PlanPathTests` covers the arc boundaries and the flip.
`FlightReportTests` covers the panel rows and the countdown format.
`OrbitCameraTests` covers the pitch clamp, yaw wrap, zoom limits, the shortest
turn, the slew rate, and the distance easing. `CameraRigTests` covers the
framing distance and the follow flags. `SceneFlightTests` is the frame-loop
acceptance flight. `BootstrapSmokeTests`
runs the real composition root — `Awake`, `Update`, `LateUpdate` — in edit mode,
so the scene wiring is checked without pressing Play.

See [Testing and verification](/technical/testing.md) for the suite and the
numerical anchors, and [Navigation and arrival](/simulation/navigation-and-arrival.md)
for what the plan itself is.
