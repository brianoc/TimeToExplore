---
type: Architecture
title: Architecture
description: How the Unity prototype is structured, from catalogue load to render frame.
tags: [technical, unity, architecture]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Shape of the prototype

The prototype is a **data-driven orrery** with a thin Unity presentation layer.
The core simulation is plain C# and does not depend on Unity's physics, scene
graph, or transform hierarchy. Unity is used for input, rendering, and HUD.

```text
solar_system.json
        │
        ▼
SolarSystemModel ──► BodyRuntime (positions in metres, doubles)
        │                    │
        │                    ├── Kepler propagation
        │                    └── Lagrange station solve
        │
        ├──► GravityField ──► ShipIntegrator ──► ShipState (double precision)
        │                            │
        │                            ├── TripPlanner ──► TrajectoryPlan
        │                            ├── Autopilot ──► thrust command
        │                            └── FlightSession ──► owns ship + plan + preview + warp
        ▼
SolarSystemRenderer ──► BodyView (spheres / cubes)
        │              ├── OrbitLine (planet and moon ellipses)
        │              └── TrajectoryLine (route path, dashed capture, arrival ghost)
        ▼
Camera at render origin ◄── ShipController (input only; debug free flight)
        │                        ▲
        │                        │
        │              PlayerControls (the Input System seam)
        ▼
SolarSystemHud (IMGUI)
```

The flight core (`Assets/Scripts/Simulation/`) is deliberately separate from the
presentation layer: it references `Core` and nothing else, has no `MonoBehaviour`
and no Unity physics, and is verified entirely in EditMode tests. The scene flies
it through one plain-C# owner, `FlightSession`; the bootstrap only advances the
clock and asks the session to reach it.

# Runtime flow

1. `SolarSystemBootstrap` loads `Resources/solar_system.json`.
2. `SolarSystemModel.Load` builds the body dictionary, resolves parents, sorts
   parent-first, and initialises Kepler elements.
3. The clock starts at `startJulianDate` (2026-11-15), and the ship starts at
   Gateway Station so the default trip is the tested one.
4. `SolarSystemRenderer` creates one `BodyView` per body and orbit lines for
   planets and moons. `TrajectoryLine` starts hidden.
5. Each frame:
   * `SolarSystemModel.Update` recomputes every body's position and velocity at
     the clock date.
   * `WarpPolicy` caps the requested time scale from the nearest body's clear-
     ance, its local orbital timescale, and the next plan boundary.
   * `SimulationClock.Advance` adds `deltaTime × cappedScale` to the Julian date.
   * `FlightSession.Advance` integrates the ship to the clock's date in legs
     split at plan arc boundaries, with a substep budget.
   * If the budget ran out, the clock is clamped back to the date the ship
     reached, so the world never runs ahead of it.
   * `SolarSystemRenderer.Refresh` subtracts the ship position, scales to Unity
     units, applies the neighbourhood aid strength, and updates every view and
     orbit line; `TrajectoryLine` re-projects the plan.
   * The HUD draws date, warp, nearest body, target, and labels, plus the flight
     panel. A left click projects the drawn markers and picks a body; the details
     screen updates from the same model state.
6. **Go To** calls `TripPlanner.Plan`, raises the warp, draws the plan, and hands
   the ship to the session. On arrival the plan completes, the drive goes off,
   and the ship coasts in the parking orbit.

# Core types

| Type | File | Responsibility |
|---|---|---|
| `Vector3d` | `Scripts/Core/Vector3d.cs` | Double-precision vector and Unity conversion |
| `JulianDate` | `Scripts/Core/JulianDate.cs` | JD ↔ UTC and formatting |
| `OrbitMath` | `Scripts/Core/OrbitMath.cs` | Kepler solver, Lagrange points, pole conversion |
| `OrbitalElements` | `Scripts/Core/OrbitalElements.cs` | Elements → position/velocity, ellipse sampling |
| `BodyRuntime` | `Scripts/Core/BodyRuntime.cs` | Per-body state, hierarchy, equatorial basis |
| `SolarSystemModel` | `Scripts/Core/SolarSystemModel.cs` | Catalogue load, ordering, propagation |
| `SimulationClock` | `Scripts/Core/SimulationClock.cs` | Julian date, warp, pause |
| `SolarSystemData` | `Scripts/Core/SolarSystemData.cs` | JSON DTOs |

# Flight core

Plain C#, double precision, no Unity dependencies beyond `Vector3d`:

| Type | File | Responsibility |
|---|---|---|
| `ShipState` | `Scripts/Simulation/ShipState.cs` | Ship position, velocity, date, mass bookkeeping |
| `ThrustCommand` | `Scripts/Simulation/ThrustCommand.cs` | Thrust as an acceleration vector, with clamping |
| `IGravityField` | `Scripts/Simulation/IGravityField.cs` | Prepare and evaluate a gravity field |
| `GravityField` | `Scripts/Simulation/GravityField.cs` | Summed point-mass field over the catalogue, pruned and clamped |
| `ZeroGravityField` | `Scripts/Simulation/ZeroGravityField.cs` | Absent gravity, for kinematic checks |
| `ShipIntegrator` | `Scripts/Simulation/ShipIntegrator.cs` | Adaptive RK4 that advances to a Julian date |
| `TwoBody` | `Scripts/Simulation/TwoBody.cs` | Circular and escape speeds, energy, osculating elements |
| `Quintic` | `Scripts/Simulation/Quintic.cs` | Degree-five curve matching r, v, a at both ends |
| `PlanArc` | `Scripts/Simulation/PlanArc.cs` | One planned leg, its thrust profile, delta-v, clearance |
| `TrajectoryPlan` | `Scripts/Simulation/TrajectoryPlan.cs` | The arc chain, its summary, and the sampled path |
| `ParkingOrbit` | `Scripts/Simulation/ParkingOrbit.cs` | The circular orbit a Go To finishes in, with its gravity ceiling |
| `SphereOfInfluence` | `Scripts/Simulation/SphereOfInfluence.cs` | Patched-conic radius, used only for planning waypoints |
| `TripPlanner` | `Scripts/Simulation/TripPlanner.cs` | The Go To solve: three arcs, moving-target loop, thrust check |
| `Autopilot` | `Scripts/Simulation/Autopilot.cs` | Feed-forward plus feedback tracking, and the flight loop |
| `FlightSession` | `Scripts/Simulation/FlightSession.cs` | Owns the ship state, plan, preview, and integrator; coasts an idle ship; the scene's flight loop |
| `WarpPolicy` | `Scripts/Simulation/WarpPolicy.cs` | Caps time warp by clearance, local dynamics, and the next plan event |

Gateway→Mars and Mars→Earth are both exercised end to end by the suite, and the
same Mars trip is flown through the scene's frame loop.

`SolarSystemModel.ComputeState` is the bridge: it returns any body's state at an
arbitrary Julian date without mutating the model, so the field and the planner
can evaluate the world inside an integration step.

See [ADR-005](/decisions/adr-005-continuous-gravity.md) for the field,
[ADR-006](/decisions/adr-006-quintic-plan.md) for the plan, and
[ADR-007](/decisions/adr-007-flight-director.md) for ship ownership and the warp
cap. The scene flow is described in [The flight session](/technical/flight-session.md).

# Presentation types

| Type | File | Responsibility |
|---|---|---|
| `BodyView` | `Scripts/Rendering/BodyView.cs` | Primitive, material, scale, spin; the pure true-scale/minimum-size factor |
| `RenderingShaders` | `Scripts/Rendering/RenderingShaders.cs` | Names of the three runtime shaders and the Sun-versus-lit choice |
| `MarkerSizing` | `Scripts/Rendering/MarkerSizing.cs` | Minimum marker radius: 4.5 px local to 6 px map, the neighbourhood aid fade, and their angular conversion |
| `OrbitLine` | `Scripts/Rendering/OrbitLine.cs` | Screen-width-aware orbit polyline; orbit-sized locally, camera-sized on the map |
| `TrajectoryLine` | `Scripts/Rendering/TrajectoryLine.cs` | Route path, dashed capture, flip, arrival, and target-arrival ghost markers |
| `ShipView` | `Scripts/Rendering/ShipView.cs` | Placeholder ship marker at the origin, aligned with the burn |
| `SolarSystemRenderer` | `Scripts/Rendering/SolarSystemRenderer.cs` | Owns views, orbit lines, sun shader global; applies the per-body aid strength |
| `ShipController` | `Scripts/Player/ShipController.cs` | Debug free-flight movement; no camera ownership |
| `PlayerControls` | `Scripts/Player/PlayerControls.cs` | The one input seam: device polling, intents, key table, mouse sensitivity, scroll normalisation |
| `OrbitCamera` | `Scripts/Player/OrbitCamera.cs` | Plain-C# orbit state: yaw, pitch, distance, slew, zoom |
| `MapView` | `Scripts/Player/MapView.cs` | Plain-C# map state: blend weight, dominant body, top-down pose, map range, near/far rule, pan offset, drag scale, and ease |
| `ShipCameraRig` | `Scripts/Player/ShipCameraRig.cs` | Drag-orbit locally and pan on the map, wheel zoom on both ranges, destination framing, blended pose, camera placement |
| `CameraFocusDirector` | `Scripts/Player/CameraFocusDirector.cs` | Runs the debug "Jump (debug)" ship move |
| `FocusTransition` | `Scripts/Player/FocusTransition.cs` | Plain-C# eased ship move |
| `BodyPicker` | `Scripts/UI/BodyPicker.cs` | Screen-space pick against drawn markers: nearest locally, class-priority on the map |
| `BodyLabels` | `Scripts/UI/BodyLabels.cs` | Pure label density and the map-only ship label |
| `BodyDetails` | `Scripts/UI/BodyDetails.cs` | Details-screen rows and body formatting |
| `FlightReport` | `Scripts/UI/FlightReport.cs` | Flight-panel phase table, preview rows, staleness, countdown, and formatting |
| `SolarSystemHud` | `Scripts/UI/SolarSystemHud.cs` | IMGUI panels, labels, hover, and details screen |
| `SolarSystemBootstrap` | `Scripts/SolarSystemBootstrap.cs` | Composition root, frame order, hotkeys, and the `EnableMapView` comparison switch |
| `SolarSystemSceneBuilder` | `Editor/SolarSystemSceneBuilder.cs` | Optional scene creation menu (shipped scene and system-map scene) |

# Composition and self-installation

`SolarSystemBootstrap` is the composition root. It is deliberately small: it
loads data, creates the model, clock, camera, renderer, flight session,
trajectory line, ship, and HUD, and then forwards the frame.

The bootstrap self-installs with:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void AutoCreate()
```

If the open scene has no bootstrap, pressing Play creates one. If the scene has
one—for example `Assets/Scenes/SolarSystem.unity`, or a scene created by
`Tools > Time to Explore > Create Solar System Scene`—the duplicate is skipped.
The shipped scene is a convenience, not a requirement: the bootstrap still runs
in an empty scene, which keeps the prototype easy to inspect while it is finding
its shape.

Two scenes exist for the system map comparison. `SolarSystem.unity` leaves the
serialized `SolarSystemBootstrap.EnableMapView` switch off and keeps the current
orbit camera; `SystemMap.unity` turns it on and the bootstrap passes it to
`ShipCameraRig.MapEnabled`. Both hold only the camera and the bootstrap; the rig
is added at runtime. `Tools > Time to Explore > Create System Map Scene`
recreates the comparison scene. The switch is a development aid, not a gameplay
mode, and is due to be removed or defaulted on once the map work is done.

# Double precision boundary

The simulation stores positions in metres as doubles. The renderer converts to
Unity coordinates only after subtracting the ship position:

```csharp
Vector3 relative = ((Body.PositionM - shipPositionM) * unitsPerMetre).ToUnity();
```

`ToUnity` maps ecliptic `(x, y, z)` to Unity `(x, z, -y)`, putting ecliptic north
on Unity's up axis and preserving chirality. The inverse, `FromUnity`, converts
camera-relative movement back into an ecliptic displacement.

This is the entire "floating origin" implementation: there is no rebase event,
no moving scene root, and no accumulated offset. The ship is always at the
origin; the world moves around it. See
[ADR-002](../decisions/adr-002-double-precision.md).

# Rendering

The project renders through the **Universal Render Pipeline** (URP 17.3.0,
`com.unity.render-pipelines.universal`). The graphics default is
`Assets/Settings/TimeToExploreUniversalRenderPipeline.asset`, which points at
`Assets/Settings/TimeToExploreUniversalRenderer.asset`; the quality levels keep
their pipeline override empty so the graphics default applies to every level.
The pipeline is deliberately cheap: HDR and MSAA are off, render scale is 1,
depth and opaque copies are off, shadows are off, and no post-processing runs.
URP's main and additional light rendering is disabled; the orrery shades itself
from `_SunPosition`, so the light and shadow paths have nothing to do.

Each body gets a Unity primitive:

* sphere for star, planet, moon, asteroid;
* cube for station.

Three small shaders in `Assets/Resources/Shaders/` are looked up at runtime, so
they survive `Shader.Find` in a player build:

| Shader | Used by | Responsibility |
|---|---|---|
| `TimeToExplore/BodyLit` | planets, moons, asteroids, stations | Lambert from the global `_SunPosition`, with `_Ambient` and per-body `_Color` |
| `TimeToExplore/Unlit` | the Sun, the ship marker, plan markers | flat `_Color`, no lighting |
| `TimeToExplore/Line` | orbit and trajectory `LineRenderer`s | vertex colour, alpha blending, tiled `_MainTex` for the dashed capture |

`BodyView.Refresh` handles:

* ship-relative position,
* true physical radius,
* minimum apparent size (unless true scale is on), from the map blend's
  4.5–6 px rule,
* axial tilt and spin,
* station cube rotation.

Orbit lines sample the Kepler ellipse once per frame in the body's element frame,
transform to the parent's frame, add the parent's current position, and then
convert to ship-relative render coordinates. Locally the line width scales with
the orbit's size, as before; on the map it eases to a fraction of the camera
distance so the line stays roughly a pixel or two wide and a distant orbit can
never become a ribbon at close map zoom.

`TrajectoryLine` is the same idea for a plan: it samples each arc once when the
plan is made, then subtracts the ship position every frame. The capture arc uses
a repeating alpha texture in `LineTextureMode.Tile`, with the texture scale tied
to the line width, so the dashes stay the same apparent length at any zoom.

# Input

The project runs on the **Input System** (`com.unity.inputsystem` 1.20.1) with
Active Input Handling set to **Both**. `PlayerControls` is the one seam that
polls `Keyboard.current` and `Mouse.current`; every other script asks for an
intent. **Both** is required because the HUD is IMGUI: under "Input System
Package (New)" alone, `OnGUI` receives no events and every button stops
working. The legacy backend therefore survives only to feed IMGUI; no project
code reads its axes. The full rules are in [Input](/technical/input.md) and
[ADR-011](/decisions/adr-011-input-system.md).

The controller is an inspection tool now, not a flight model:

| Input | Action |
|---|---|
| **Go To …** on the details screen | Plan and fly to the selected body; raises the warp |
| **Jump (debug)**, `F` | Three-second eased debug teleport to the selected body |
| `X` | Cancel the flight; the ship keeps its state |
| `` ` `` | Toggle debug free flight; cancels the flight |
| Right mouse drag | orbit the camera around the ship; pan on the map |
| Mouse wheel | zoom the camera (1.25× per notch; 12 billion km on the map side) |
| `W A S D` | free flight: move forward/left/back/right |
| `Q E` | free flight: move down/up |
| `Shift` / `Ctrl` | ×10 / ×0.1 speed |
| `,` / `.` | free flight: decrease / increase speed |
| Left click a body | select it and open the details screen |
| `Esc` | close the details screen and cancel a debug jump |
| `1`–`7` | requested time warp 1× to 1,000,000× (the policy may lower it) |
| `Space` | pause |
| `Tab` | cycle planet/station target |
| `R` | reset to Gateway and 1× |
| `T` | toggle true scale |
| `O` | toggle orbits |
| `L` | toggle labels |
| `H` | toggle HUD |

The camera orbits the ship and, in the local band, looks at it, so a flight and
a debug jump can frame their destination without moving the eye into the plan
([ADR-008](/decisions/adr-008-orbit-camera.md)). With the map switch on, the
same rig blends the pose to a top-down system map as the zoom crosses the local
sphere of influence: the map looks at `ship + pan` (the one bend of ADR-008),
right-drag pans, the wheel reaches 12 billion km, and the player's remembered
yaw and pitch are never overwritten. Auto-framing is suspended while the map is
engaged. The full rules are in
[The system map and route preview](/technical/system-map.md).

Free flight is deliberately simple and deliberately separate from the flight
session: it cancels any plan and clears the velocity, because a teleport is not a
trajectory. Selection and the details screen are described in
[Selection and camera focus](/technical/selection-and-camera.md); the flight loop
is in [The flight session](/technical/flight-session.md).

# Performance

At 137 bodies the model is trivial: 137 Kepler solves per frame, each a handful
of trigonometric operations. The expensive parts are rendering and HUD:

* 137 `BodyView` transforms and materials,
* 30 orbit lines × 161 points updated per frame,
* one plan trajectory line, ~192 points, re-projected per frame while a plan is
  live,
* two `OnGUI` panels and up to a few dozen labels.

This is comfortably within a desktop frame budget. If the catalogue grows to
thousands of asteroids, the next step is GPU instancing or a point cloud for the
belt, not a faster Kepler solver.

The flight integration is the other cost, and it is bounded: 2,000 RK4 substeps
per frame, each with four gravity evaluations over at most eight active bodies.
The Mars acceptance flight used 63 substeps in its busiest frame, and the warp
policy is what keeps it there.

The orbit lines are the first thing to optimise: updating 30 × 161 positions per
frame is unnecessary when the camera has not moved and the parent has barely
moved. A dirty-flag or lower update rate (5–10 Hz) is an easy win.

# Testing and verification

Automated verification is one command:

```bash
tools/check.sh          # docs, catalogue, generator, Unity EditMode tests
tools/check.sh --fast   # skip Unity for a few-second loop
```

The Unity suite lives in `Assets/Tests/EditMode/` and runs on a throwaway copy
of the project, so it is safe with the editor open. It covers the Kepler
solver, orbital-element state and conservation laws, Lagrange points, the
catalogue invariants, parent-first propagation, epoch distances and speeds, the
Moon's parent-equator frame, station updates, orbit sampling, the clock, body
details and picking, the focus-transition easing, the orbit camera and its map
blend (pose, input routing, pan, extended zoom, near plane), the map drawing
rules and neighbourhood aid fade, the URP pipeline and shader wiring, the flight
core, the input seam's key table and mouse conversions, and the scene's frame
loop.
See [Testing and verification](/technical/testing.md) for the test matrix,
numerical reference values, and conventions.

The orbital maths is also checked outside Unity at the epoch against known
values:

* planet heliocentric distances and speeds,
* moon geocentric distances versus semi-major axes,
* Lagrange point distances for all six stations.

`tools/validate_catalogue.py` enforces the catalogue checklist without opening
Unity, and `tools/check.sh` regenerates the catalogue offline to prove the
committed file is reproducible.

The C# runtime and editor scripts compile against Unity 6.3's assemblies. The
project is intended to be opened in Unity 6.3 (6000.3.28f1) with the Universal
Render Pipeline, the Input System package, and Active Input Handling set to
Both (the HUD is IMGUI).

The next layer is PlayMode tests for the renderer, bootstrap, and HUD; they are
deferred until the presentation code settles.

# Known limitations

* No fuel, propellant accounting, or delta-v budget in the plan's cost: the plan
  reports delta-v but does not spend it.
* No attitude dynamics or slew limits for the ship itself; thrust direction is
  instantaneous. The camera slew is decorative only.
* No manual thrust: a flight is plan-and-watch. Free flight is a debug teleport,
  not a hand-flown ship.
* No mid-flight re-plan beyond pressing **Replan** again, which replans from
  the current state and discards the old plan and any preview.
* No Lambert solver, no coasting transfers, and no gravity assists.
* No stationkeeping drift.
* No N-body perturbations.
* No save/load.
* The HUD is immediate-mode and allocation-heavy; fine for a prototype, but its
  IMGUI dependence is also why Active Input Handling cannot be "New" only.
* The scene is generated at runtime; there is no authored art scene.
* The system map has its camera pose, markers, labels, picking, orbit-line
  width, and neighbourhood-aware marker aid (milestone 2.5, steps 1–4), and the
  Plot route → Begin preview with its target-arrival ghost (step 5). The
  frame-loop acceptance (step 6) is not built yet. The `EnableMapView`
  comparison switch is temporary; it also gates the aid fade so the shipped
  scene keeps its old local look until the switch defaults on.
* The rendering migration stops at the URP forward path: there is no
  post-processing, no SSAO, no shadow system, and no two-camera far field. Those
  remain future work, and the shaders are intentionally minimal.
* Warp drops in the clock's power-of-ten steps, not continuously.

# Extension points

* **Fuel model:** charge `PlanArc.DeltaVMps` against a budget, grow the dry mass,
  and re-plan when the budget cannot pay.
* **Transfer-conic capture:** replace the capture quintic with a coasting arc
  plus a periapsis burn, so arrivals on the giants are lower and cheaper instead
  of parked high by the gravity ceiling.
* **Lambert coasting:** a second planner for the burn-and-coast regime, sharing
  the field and integrator.
* **Manual override:** a thrust path that suspends the plan without cancelling
  it, so the player can hand-fly and then resume or re-plan.
* **Save/load:** persist Julian date, ship state, fuel, and contract state; the
  world itself is reproducible from the catalogue.
* **Visual pass:** replace primitives with meshes, add rings, textures, and a
  starfield; keep the scale and floating-origin rules.
