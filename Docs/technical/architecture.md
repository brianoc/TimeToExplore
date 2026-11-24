---
type: Architecture
title: Architecture
description: How the Unity prototype is structured, from catalogue load to render frame.
tags: [technical, unity, architecture]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-24T19:10:00Z }
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
        │              └── TrajectoryLine (one coloured line per arc, arrival ghost)
        ▼
Camera at render origin ◄── ShipController (input only; debug free flight)
        │                        ▲
        │                        │
        │              PlayerControls (the Input System seam)
        ▼
SolarSystemHud (uGUI Canvas)
        │
        └── UiInput (EventSystem + InputSystemUIInputModule)
```

The flight core (`Assets/Scripts/Simulation/`) is deliberately separate from the
presentation layer: it references `Core` and nothing else, has no `MonoBehaviour`
and no Unity physics, and is verified entirely in EditMode tests. The scene flies
it through one plain-C# owner, `FlightSession`; the bootstrap only advances the
clock and asks the session to reach it.

# Assembly layout

The code is split into six assembly definitions — four runtime layers and two
test suites — and the compile graph encodes
the layer rule **Core ← Simulation ← Presentation ← Editor/Tests**: an assembly
may only reference an assembly to its left. The direction is enforced by Unity
(and by `AssemblyLayoutTests`), not by convention.

| Assembly | Folder | References | Platforms |
|---|---|---|---|
| `TimeToExplore.Core` | `Assets/Scripts/Core/TimeToExplore.Core.asmdef` | none | all |
| `TimeToExplore.Simulation` | `Assets/Scripts/Simulation/TimeToExplore.Simulation.asmdef` | `TimeToExplore.Core` | all |
| `TimeToExplore.Presentation` | `Assets/Scripts/TimeToExplore.Presentation.asmdef` | `TimeToExplore.Core`, `TimeToExplore.Simulation`, `Unity.InputSystem`, `UnityEngine.UI` | all |
| `TimeToExplore.Editor` | `Assets/Editor/TimeToExplore.Editor.asmdef` | `TimeToExplore.Core`, `TimeToExplore.Simulation`, `TimeToExplore.Presentation` | Editor only |
| `TimeToExplore.Tests.EditMode` | `Assets/Tests/EditMode/TimeToExplore.Tests.EditMode.asmdef` | Core, Simulation, Presentation, `TimeToExplore.Editor`, URP, Input System, Unity UI, test runner | Editor only |
| `TimeToExplore.Tests.PlayMode` | `Assets/Tests/PlayMode/TimeToExplore.Tests.PlayMode.asmdef` | Core, Simulation, Presentation, Unity UI, test runner | all platforms |

`Presentation` covers `Assets/Scripts/Player/`, `Assets/Scripts/Rendering/`,
`Assets/Scripts/UI/`, and the root `SolarSystemBootstrap` on purpose. Those
folders are a tight cluster — `UI` uses both `Player` and `Rendering`, and the
bootstrap uses all of them — so splitting them would add asmdefs and edges
without protecting a boundary that matters yet. `Core` holds the catalogue,
orbital maths, and the clock; `Simulation` is the plain-C# flight core and sets
`noEngineReferences: true`, so a UnityEngine call there is a compile error; the
editor assembly holds the optional scene builder and can never enter a player
build.

The layer rule has one deliberate consequence: Unity assembly references are
not transitive, so each consumer lists every project assembly it uses. Package
references follow the same rule and are not project edges: `Presentation` adds
`Unity.InputSystem` for the gameplay seam and `UnityEngine.UI` for the uGUI HUD,
and the test assembly lists both as well. The test assembly is the top of the
graph and reads the `.asmdef` files as the source of truth. See
[ADR-012](/decisions/adr-012-assembly-layout.md).

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
     ance, its local orbital timescale, the next plan boundary, and — while a
     station plan is live — the station's own clearance and its hold point.
   * `SimulationClock.Advance` adds `deltaTime × cappedScale` to the Julian date.
   * `FlightSession.Advance` integrates the ship to the clock's date in legs
     split at plan arc boundaries, with a substep budget.
   * If the budget ran out, the clock is clamped back to the date the ship
     reached, so the world never runs ahead of it.
   * `SolarSystemRenderer.Refresh` subtracts the ship position, scales to Unity
     units, applies the neighbourhood aid strength, and updates every view and
     orbit line; `TrajectoryLine` re-projects the plan.
   * The HUD updates the uGUI panels from the model: date, warp, nearest body,
     target, labels, and the flight panel. A left click projects the drawn
     markers and picks a body; the details screen updates from the same model
     state. uGUI owns the pointer over a panel, so a button press never also
     selects the body behind it.
6. **Plot route** calls `FlightSession.PlotAsync`, which solves the floor
   plan and the trip-time window with `TripPlanner.Plan` on a background
   worker and draws the preview when it is ready; the **`ARRIVAL`** row rebuilds
   it against the chosen capture style or burn level on the same worker
   (`FlightSession.SetArrivalStyle` etc.), and its **trip-time slider** requests
   a retime with
   `TripPlanner.PlanAtDuration` inside that profile's window on the worker as
   well; the HUD publishes finished plans on the main thread, and **Begin
   flight** replans and hands the ship to the session. On arrival the plan
   completes, the drive goes off, and the ship coasts in the parking orbit — or
   holds at a station's 500 m hold point with its velocity matched.

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
| `LambertSolver` | `Scripts/Core/LambertSolver.cs` | Zero-rev prograde short-way coast solve: two positions and a time of flight → `v₁`, `v₂`, and the swept angle, with rejected geometry reported rather than thrown |
| `SolarSystemData` | `Scripts/Core/SolarSystemData.cs` | JSON DTOs |

# Flight core

Plain C#, double precision, no Unity dependencies beyond `Vector3d`:

| Type | File | Responsibility |
|---|---|---|
| `ShipState` | `Scripts/Simulation/ShipState.cs` | Ship position, velocity, date, mass bookkeeping |
| `FuelModel` | `Scripts/Simulation/FuelModel.cs` | First-pass propellant readout: the rocket equation at 10,000 km/s, mass ratio and propellant fraction |
| `ThrustCommand` | `Scripts/Simulation/ThrustCommand.cs` | Thrust as an acceleration vector, with clamping |
| `IGravityField` | `Scripts/Simulation/IGravityField.cs` | Prepare and evaluate a gravity field |
| `GravityField` | `Scripts/Simulation/GravityField.cs` | Summed point-mass field over the catalogue, pruned and clamped |
| `ZeroGravityField` | `Scripts/Simulation/ZeroGravityField.cs` | Absent gravity, for kinematic checks |
| `ShipIntegrator` | `Scripts/Simulation/ShipIntegrator.cs` | Adaptive RK4 that advances to a Julian date |
| `TwoBody` | `Scripts/Simulation/TwoBody.cs` | Circular and escape speeds, energy, osculating elements |
| `KeplerPropagator` | `Scripts/Core/KeplerPropagator.cs` | Universal-variable state-vector propagation for ellipses, parabolas, and hyperbolas |
| `ConicElements` | `Scripts/Core/ConicElements.cs` | State → conic, the perifocal basis, and true anomaly ↔ time |
| `IArcPath` | `Scripts/Simulation/IArcPath.cs` | The arc contract: position, velocity, acceleration, and the parameter/time mappings |
| `Quintic` | `Scripts/Simulation/Quintic.cs` | Degree-five curve matching r, v, a at both ends; linear in time |
| `ConicArc` | `Scripts/Simulation/ConicArc.cs` | Keplerian coast around any body, linear in true anomaly, re-added to that body's ephemeris: the planetocentric capture and the heliocentric Lambert cruise |
| `FiniteBurnArc` | `Scripts/Simulation/FiniteBurnArc.cs` | Integrated constant-thrust burn, time-linear table, the radius-crossing solver step, an optional fixed thrust direction, and `SimulateBackward` for the reverse-placed brake; an optional summed-field source integrates the burn in absolute coordinates and decomposes it back into the reference body's frame, so one arc can cross a sphere of influence |
| `TransferConic` | `Scripts/Simulation/TransferConic.cs` | The arrival hyperbola, periapsis and apoapsis burns, transfer ellipse, and circular state |
| `HybridCapture` | `Scripts/Simulation/HybridCapture.cs` | The fixed capture burn, its injection radius and trim, the transfer ellipse, and the split |
| `PathTessellation` | `Scripts/Simulation/PathTessellation.cs` | Curvature-adaptive arc polyline with a scale-free chord tolerance |
| `PlanArc` | `Scripts/Simulation/PlanArc.cs` | One planned leg, its path, thrust profile, delta-v, clearance, and entry impulse |
| `TrajectoryPlan` | `Scripts/Simulation/TrajectoryPlan.cs` | The arc chain, its summary, impulses, and the sampled path |
| `ParkingOrbit` | `Scripts/Simulation/ParkingOrbit.cs` | The quintic orbit with its gravity ceiling, and the lower conic/hybrid orbit |
| `SphereOfInfluence` | `Scripts/Simulation/SphereOfInfluence.cs` | Patched-conic radius, used only for planning waypoints |
| `HandoverBand` | `Scripts/Simulation/HandoverBand.cs` | The handover floor (`0.4` of the drive, clamped to the body clearance), the `1.02 × SOI` ceiling, the two endpoints, and the empty-band reason |
| `TripPlanner` | `Scripts/Simulation/TripPlanner.cs` | The Go To solve (the burn–coast–burn profile with two capture styles for a planet, and a point-target hold-point rendezvous for a station; moving-target loop, thrust check), the burn level, the pinned departure break and forward departure solve, the two-tier break search (central-body-only peak, full-field certification inside 5% of the limit), the coast's Lambert stitching, the reverse-placed brake, the body-clearance gate on every fixed burn, the true floor search with a fast probe mode (128-sample probes, full-field certification, 0.2% retry), and `PlanAtDuration` for the trip-time control |
| `Autopilot` | `Scripts/Simulation/Autopilot.cs` | Feed-forward plus feedback tracking, the flight loop, and the seam impulses |
| `FlightSession` | `Scripts/Simulation/FlightSession.cs` | Owns the ship state, plan, preview, and integrator; probes the profile's slowest accepted trip; builds route plans synchronously or on the background worker — `PlotAsync`, style/burn-level rebuilds, the slider's re-time — with its own planner and field, publishing finished plans on the main thread; coasts an idle ship; the scene's flight loop |
| `WarpPolicy` | `Scripts/Simulation/WarpPolicy.cs` | Caps time warp by clearance, local dynamics, the next plan event, and a station's clearance and hold point while a station plan is live |

The planner crosses on the **burn–coast–burn** profile: a zero-revolution
prograde Lambert conic chosen by `LambertSolver.SolveShortWay`, bookended by a
prograde departure finite burn and a brake. The break is a short prograde quintic
that ends at the parking radius on the ship's own tangent at the planetocentric
escape speed; a ballistic `ConicArc` about the departure body carries that state
to the handover band's ceiling (`1.02 × SOI`), and the departure burn starts at
the conic's ceiling **state** and holds a solved thrust direction, with its
angles, its duration, and the coast's start solved forward. The brake is still
built backwards from the capture chain's entry state with
`FiniteBurnArc.SimulateBackward`. Every seam is continuous and the arrival is
exact; the brake's thrust direction is the change the chain needs, not the
instantaneous velocity. Its fixed burns are integrated through the **summed
field**: `FiniteBurnArc` takes an optional `IGravityField`, runs the RK4 in
absolute heliocentric coordinates, and decomposes the table back into the
reference body's frame, so the body named on the arc is a label for the phase's
frame rather than the physics. The reference-related attitude is unchanged —
prograde and retrograde follow the body-relative velocity, and the pinned
departure burn and the brake hold fixed inertial directions. The floor search's
probes keep the cheap two-body burn; the plan that is reported and flown is the
full-field build. `CaptureStyle` chooses the capture: the conic chain (default)
or the hybrid fixed-thrust chain. A quintic capture survives only for a trip
that starts inside the target's sphere of influence, where there is no crossing
to plan. A **station** target takes the planner's point-target branch instead:
the same departure and coast, then a finite velocity match onto the 500 m hold
point, with no sphere, parking orbit or capture chain
([ADR-019](/decisions/adr-019-station-calls-and-departure.md)).

Gateway→Mars and Mars→Earth are both exercised end to end by the suite, and the
same Mars trip is flown through the scene's frame loop.

`SolarSystemModel.ComputeState` is the bridge: it returns any body's state at an
arbitrary Julian date without mutating the model, so the field and the planner
can evaluate the world inside an integration step.

See [ADR-005](/decisions/adr-005-continuous-gravity.md) for the field,
[ADR-006](/decisions/adr-006-quintic-plan.md) for the plan,
[ADR-014](/decisions/adr-014-keplerian-coast.md) for the conic arrival, and
[ADR-007](/decisions/adr-007-flight-director.md) for ship ownership and the warp
cap. The scene flow is described in [The flight session](/technical/flight-session.md).

# Presentation types

| Type | File | Responsibility |
|---|---|---|
| `BodyView` | `Scripts/Rendering/BodyView.cs` | Primitive, material, scale, spin; the pure true-scale/minimum-size factor |
| `RenderingShaders` | `Scripts/Rendering/RenderingShaders.cs` | Names of the four runtime shaders and the Sun-versus-lit choice |
| `MarkerSizing` | `Scripts/Rendering/MarkerSizing.cs` | The 4.5 px minimum marker radius and its angular conversion |
| `LineWidth` | `Scripts/Rendering/LineWidth.cs` | One apparent width for every line, converted to render units at the camera depth, FOV, and viewport height: 3 px for orbit and plan lines; `SampleWorldWidth` gives each vertex its width bounded to a 2 px distance floor and the line's own pixel width |
| `OrbitColours` | `Scripts/Rendering/OrbitColours.cs` | The planet orbit palette: one warm-orange-to-cool-purple ramp sampled by orbital rank, so each planet keeps one fixed colour |
| `LineCameraClip` | `Scripts/Rendering/LineCameraClip.cs` | Clips a billboarded polyline to the longest run in front of the camera's near plane, so a segment that crosses behind the camera cannot project into a screen-filling polygon |
| `OrbitLine` | `Scripts/Rendering/OrbitLine.cs` | Orbit polyline clipped to the camera plane, with a per-vertex width curve from the shared rule; never sized from the orbit radius |
| `RouteColours` | `Scripts/Rendering/RouteColours.cs` | The route palette by path type: gold for a quintic, sky blue dashed for a conic, red for a fixed burn, with a darker or paler shade inside a run of the same type and the hue held fixed |
| `TrajectoryLine` | `Scripts/Rendering/TrajectoryLine.cs` | One pooled, coloured line per plan arc through `RouteColours`; adaptive polyline; burn, arrival, and target-arrival ghost markers |
| `ShipView` | `Scripts/Rendering/ShipView.cs` | Placeholder ship marker at the origin, aligned with the burn; the bright-green thrust arrow, a fixed apparent length, hidden with the drive off |
| `SolarSystemRenderer` | `Scripts/Rendering/SolarSystemRenderer.cs` | Owns views, orbit lines, and the sun shader global; applies the shared marker minimum |
| `SkyView` | `Scripts/Rendering/SkyView.cs` | Runtime skybox material from the baked band and the star shader; sets and restores the camera clear flags and `RenderSettings.skybox` |
| `ShipController` | `Scripts/Player/ShipController.cs` | Debug free-flight movement; no camera ownership |
| `PlayerControls` | `Scripts/Player/PlayerControls.cs` | The one input seam: device polling, intents, key table, mouse sensitivity, scroll normalisation |
| `OrbitCamera` | `Scripts/Player/OrbitCamera.cs` | Plain-C# orbit state: yaw, pitch, distance, slew, zoom |
| `CursorZoom` | `Scripts/Player/CursorZoom.cs` | Plain-C# pointer anchor: pointer-to-NDC, the look-at-plane point, the anchored look-at, and the 60 AU offset cap |
| `LocalScale` | `Scripts/Player/LocalScale.cs` | Plain-C# dominant-body choice and local radius, used by `Space` home |
| `ShipCameraRig` | `Scripts/Player/ShipCameraRig.cs` | Right-drag orbit, wheel zoom toward the pointer, the one-second home transition, destination and route framing, camera placement, continuous body avoidance |
| `CameraFocusDirector` | `Scripts/Player/CameraFocusDirector.cs` | Runs the debug "Jump (debug)" ship move |
| `FocusTransition` | `Scripts/Player/FocusTransition.cs` | Plain-C# eased ship move |
| `BodyPicker` | `Scripts/UI/BodyPicker.cs` | Screen-space pick against drawn markers: importance-ordered hits and the repeated-click cycle |
| `BodyLabels` | `Scripts/UI/BodyLabels.cs` | Pure label-density rule by body kind and distance |
| `BodyDetails` | `Scripts/UI/BodyDetails.cs` | Details-screen rows and body formatting |
| `FlightReport` | `Scripts/UI/FlightReport.cs` | Flight-panel phase table, preview rows, staleness, countdown, and formatting |
| `SolarSystemHud` | `Scripts/UI/SolarSystemHud.cs` | Runtime uGUI panels, labels, hover, crosshair, details screen, and the route colour legend |
| `UiInput` | `Scripts/UI/UiInput.cs` | The one EventSystem and the Input System UI module; the pointer-over-panel query |
| `SolarSystemBootstrap` | `Scripts/SolarSystemBootstrap.cs` | Composition root, frame order, and hotkeys |
| `SolarSystemSceneBuilder` | `Editor/SolarSystemSceneBuilder.cs` | Optional scene creation menu for the shipped scene |

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

One scene, `Assets/Scenes/SolarSystem.unity`, holds only the camera and the
bootstrap; the rig is added at runtime. `Tools > Time to Explore > Create Solar
System Scene` recreates it. The old comparison scene and its switch were removed
with the system map ([ADR-018](/decisions/adr-018-one-orbit-camera.md)).

# The HUD

The HUD is **uGUI**, built from code by `SolarSystemHud.Initialise`; only the
camera and the bootstrap are serialized, so there are no prefabs or scene UI
objects to keep in sync. The object graph is created once and then only text,
visibility, colours, and anchored positions change. The panel text still comes
from the pure builders (`BodyDetails`, `FlightReport`, `BodyLabels`), which is
why the content is tested without a rendered frame.

| Object | Responsibility |
|---|---|
| `Canvas` | Screen Space Overlay, sorting order 10, drawn above the orrery |
| `CanvasScaler` | Scale With Screen Size, 1920×1080 reference, match 0.5; the IMGUI pixel sizes hold at 1080p and scale with the window |
| `GraphicRaycaster` | turns the pointer into uGUI hits, so panels swallow clicks |
| `EventSystem` + `InputSystemUIInputModule` | created by `UiInput` only when the scene has none; carries the Input System's default UI actions |
| Panel `Image`s | status, details, flight, and route-legend backgrounds; `raycastTarget = true` except the informational legend, which is `false` |
| `Text` widgets | legacy `Text` with `LegacyRuntime.ttf` (no TextMeshPro assets); role-separated colours and sizes match the IMGUI styles |
| Button widgets | `Button.onClick`, with the same labels and the same `interactive` rules as before |
| Label pool | one `Text` + `Shadow` per world label, `raycastTarget = false`, placed from projected screen points |
| Crosshair | a 4×4 non-raycast `Image` at the canvas centre |

The detail and flight rows are pooled: the builders return at most 19 and 17
rows, so the panel creates 20 and 18 row entries and toggles them. Selecting a
new body rewrites the same entries instead of rebuilding the panel. The route
legend is pooled the same way: a fixed row of swatch and label widgets is filled
from the visible plan's arc types (quintic, conic, fixed burn), one row per
type, so the three conic arcs of a coasted arrival share a row while their
shades differ on the route. It is rebuilt from `TrajectoryLine`, not from a
hard-coded list, so the key always matches the drawn colours, and it stacks
above the flight panel in the bottom-left corner.

Panel input is a uGUI question (`EventSystem.current.IsPointerOverGameObject()`)
rather than a rectangle test. That covers the whole panel including its buttons,
and it means a click on a panel cannot reach the world. The click that opens the
details screen is guarded by one frame (`_detailsOpenedThisFrame`): the buttons
are non-interactable during the frame the panel appears, in whichever order the
EventSystem and the HUD update. World picking stays on `PlayerControls`.

`ShowHud` hides the status panel and the route legend; `ShowLabels` hides body,
ship, and trajectory labels. The details screen, flight panel, hover prompt, and
crosshair keep their old visibility rules. See
[ADR-013](/decisions/adr-013-ugui-hud.md).

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
The pipeline is deliberately cheap: MSAA is off, render scale is 1, depth and
opaque copies are off, and shadows are off. HDR is the one exception — it is on
in URP's 32-bit R11G11B10 buffer so the brightest sky stars can write above
white — and post-processing is on, with the pipeline's default volume profile
supplying Bloom and Neutral tonemapping for those stars
([Rendering at solar scale](/technical/rendering-scale.md)). URP's main and
additional light rendering is disabled; the orrery shades itself from
`_SunPosition`, so the light and shadow paths have nothing to do.

Each body gets a Unity primitive:

* sphere for star, planet, moon, asteroid;
* cube for station.

Four small shaders in `Assets/Resources/Shaders/` are looked up at runtime, so
they survive `Shader.Find` in a player build:

| Shader | Used by | Responsibility |
|---|---|---|
| `TimeToExplore/BodyLit` | planets, moons, asteroids, stations | Lambert from the global `_SunPosition`, with `_Ambient` and per-body `_Color` |
| `TimeToExplore/Unlit` | the Sun, the ship marker, the thrust vector, plan markers | flat `_Color`, no lighting |
| `TimeToExplore/Line` | orbit and trajectory `LineRenderer`s | vertex colour, alpha blending, tiled `_MainTex` for dashed arcs, and `fwidth` edge coverage for antialiasing without MSAA |
| `TimeToExplore/Skybox` | the camera's skybox | the baked Milky Way band from `_MainTex` plus three procedural star layers, with per-material band and star gain |

`SkyView` builds the skybox material at runtime and points the camera at it: the
camera clears to `Skybox` and `RenderSettings.skybox` holds the material. Removing
the view restores the previous clear flags and skybox, which is how the tests and
the bootstrap's teardown leave the scene as they found it. The shader is a
background pass with no URP pipeline or `LightMode` tags, because it is drawn by
Unity's own skybox path rather than a forward pass; the full design is in
[The sky](/technical/sky.md).

`BodyView.Refresh` handles:

* ship-relative position,
* true physical radius,
* minimum apparent size (unless true scale is on), from the 4.5 px rule,
* axial tilt and spin,
* station cube rotation.

Orbit lines sample the Kepler ellipse once per frame in the body's element frame,
transform to the parent's frame, add the parent's current position, and then
convert to ship-relative render coordinates. Every orbit line takes its width
from `LineWidth`, which converts the shared 3 px apparent width to render units
at the camera distance and the viewport's FOV and height. Each vertex then gets
its own width bounded to the 2 px distance floor at the far end and to the
line's own pixel count at the near end: the floor keeps the far arc visible, and
the ceiling stops a near arc from becoming a ribbon when the camera passes close
to it. The ellipse is clipped at the camera plane first, so its behind-camera
half is never billboarded. The orbit radius is not an input. Its colour comes
from `OrbitColours`, one fixed warm-to-cool ramp sample per planet.

`TrajectoryLine` is the same idea for a plan, with three differences. Each arc
is its own line, and `RouteColours` gives it a colour and dash style from the
**path type** it carries: gold for a quintic, sky blue dashed for a conic, red
for a fixed burn. Consecutive arcs of one type — the three quintics of the
default plan, the three conics of the coasted arrival, the transfer and parking
conics after the hybrid burn — take the base colour, a darker shade, and a paler
shade in turn, with the hue held, so one run still reads as one family. It
samples each arc once when the plan is made with `PathTessellation`, which
subdivides by curvature until every chord's midpoint is within 5 × 10⁻⁴ of its
own length; uniform sampling in time spent the budget on the straight cruise and
left visible chords in the tight departure and capture turns. Each dashed arc
uses a repeating alpha texture in `LineTextureMode.Tile`. That mode puts one
texture repeat per world unit along the line, so the material's texture scale is
the inverse of the wanted world-space dash period: eight line widths at the
route's anchor depth, one dash and one gap per cycle. The period does not depend
on the arc's length, so every conic draws the same dotted line; the old rule
also multiplied the scale by the arc's whole length, so the repeat count grew
with the square of the length and a long coast became thousands of sub-pixel
dashes and a broken, shimmering smear. Perspective still foreshortens a heavy recession, but the
anchor end holds the same apparent dash at any zoom. It applies the same
camera-plane clip as the orbit lines, then takes
per-vertex widths from the same rule: the far end holds the 2 px floor instead
of fading out, and the near end is capped at the route's 3 px instead of
widening into a ribbon. `TimeToExplore/Line` feathers both
strip edges over one screen pixel with `fwidth`, so geometric edges are
antialiased even though the pipeline runs with MSAA off.

# Input

The project runs on the **Input System** (`com.unity.inputsystem` 1.20.1) with
Active Input Handling set to **New** only. Two files read it and no others:
`PlayerControls` is the gameplay seam that polls `Keyboard.current` and
`Mouse.current`; `UiInput` gives uGUI its pointer and click actions through an
`InputSystemUIInputModule`. Every other script asks the seam for an intent, and
no project code reads the legacy Input Manager. The full rules are in
[Input](/technical/input.md) and [ADR-013](/decisions/adr-013-ugui-hud.md).

The controller is an inspection tool now, not a flight model:

| Input | Action |
|---|---|
| **Plot route** / **Replot route** on the details screen | Plan the route and draw the preview without flying; frames the target |
| **Begin flight** | Replan and fly from the current state and date |
| **Jump (debug)**, `F` | Three-second eased debug teleport to the selected body |
| `X` | Cancel the flight; the ship keeps its state |
| `` ` `` | Toggle debug free flight; cancels the flight |
| Right mouse drag | orbit the camera around the ship, at every zoom |
| Mouse wheel | zoom the camera toward the pointer (1.25× per notch; 2,000 km to 12 billion km) |
| `Space` | ease the camera home to the ship |
| `W A S D` | free flight: move forward/left/back/right |
| `Q E` | free flight: move down/up |
| `Shift` / `Ctrl` | ×10 / ×0.1 speed |
| `,` / `.` | free flight: decrease / increase speed |
| Left click a body | select it and open the details screen |
| `Esc` | close the details screen and cancel a debug jump |
| `1`–`7` | requested time warp 1× to 1,000,000× (the policy may lower it) |
| `P` | pause |
| `Tab` | cycle planet/station target |
| `R` | reset to Gateway and 1× |
| `T` | toggle true scale |
| `O` | toggle orbits |
| `L` | toggle labels |
| `H` | toggle HUD |

The camera orbits the ship and looks at `ship + look-at offset` (the ship itself
when the offset is zero), so a flight and a debug jump can frame their
destination without moving the eye into the plan
([ADR-008](/decisions/adr-008-orbit-camera.md)). The wheel zooms toward the
pointer from 2,000 km to 12 billion km, the player's yaw and pitch are never
overwritten, and `Space` eases the view home
([ADR-017](/decisions/adr-017-cursor-zoom-and-home.md)). The full rules are in
[Selection and camera focus](/technical/selection-and-camera.md#the-camera).

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
* one plan trajectory line, up to a few thousand curvature-adaptive points,
  re-projected per frame while a plan is live,
* one skybox pass: the baked band's texture fetch plus, at each star layer, one
  more fetch where a star exists,
* one uGUI canvas: three panels, up to 37 pooled row texts, four buttons, and up
  to a few dozen world labels.

This is comfortably within a desktop frame budget. If the catalogue grows to
thousands of asteroids, the next step is GPU instancing or a point cloud for the
belt, not a faster Kepler solver.

The flight integration is the other cost, and it is bounded: 2,000 RK4 substeps
per frame, each with four gravity evaluations over at most eight active bodies.
The Mars acceptance flight used 63 substeps in its busiest frame, and the warp
policy is what keeps it there.

Planning a preview is a discrete cost, not a per-frame one. A coasting profile's
trip-time window costs up to six extra `PlanAtDuration` builds at plot time, to
probe the slowest duration the Lambert solve still accepts; each retime is one
build, not a new minimum solve. The burn–coast–burn plot is the
expensive one: the true-floor search probes a fast model — fixed burns at their
commanded acceleration, the break against its own body, a 128-sample burn and a
loose pinned fixed point — and rebuilds and certifies the chosen duration with
the full field at 512 samples. In the common path that certificate accepts the
first candidate, so one plot spends a handful of fast probes — milliseconds
each — plus one certified build and the window build, not a full-field
continuation; the certified build is the cost
([Planning performance](/technical/planning-performance.md#step-4-the-bracket-was-not-needed-2026-11-20)).
A certified build's fixed burns
integrate the summed field one RK4 step at a time and refresh the active set
every eighth step; a refresh evaluates the whole catalogue's ephemeris, and the
cadence cut a build by 2.2–3.7×
([Full-field burns](/simulation/powered-leg-floor.md)). The break search inside
each build is
two-tier: it measures the quintic against the departure body alone as a search
input, certifies probes within 5% of the drive limit with a one-prepare
full-field sweep, and always returns a plan rebuilt and measured with the full
field. The pinned brake/departure fixed point stops on its physical velocity
mismatch instead of a fixed pass count. `FlightSession` caches the floor plan
and the window per departure date, target, burn level, cruise and arrival style,
so a repeat plot is free and a changed date invalidates the cache.

The slider's retime is the other discrete cost, and it no longer runs on the
frame. Moving the handle enqueues the newest duration for one background worker,
which builds it with its own planner and gravity field — the simulation's field
is mutable and stays on the main thread. A superseded build is dropped, the
previous full-field preview stays published until the replacement lands, and the
HUD publishes the finished plan on the main thread. The first plot and the
style, cruise-profile and burn-level changes go through the same worker, so the
route-planning freeze the player used to feel is gone; the first plot lands as
its plan is ready, and the redraw is the only visible effect. At the epoch a
retime is 99–124 ms to Mercury at 1 g and 100–128 ms to Mars at 0.3 g, and
request-to-publish is that build plus 2–4 ms of handover; a 200 ms drag of
twenty requests published one plan, the newest, with nothing queued.

That took one Mercury 1 g `Plan()` from 36.2 s to **1.5 s**, `PlanAtDuration(8 d)`
from 183 ms to **95 ms**, the first selection (floor, window probes, retime) to
1.5 s at Mercury and 0.93 s at Mars, and a repeat selection to under a
millisecond, with the plans, floors and seams unchanged. The two-tier model, its
boundary, and the full measurement set are in
[Planning performance](/technical/planning-performance.md).

The orbit lines are the first thing to optimise: updating 30 × 161 positions per
frame is unnecessary when the camera has not moved and the parent has barely
moved. A dirty-flag or lower update rate (5–10 Hz) is an easy win.

# Testing and verification

Automated verification is one command:

```bash
tools/check.sh                    # docs, catalogue, both generators, Unity EditMode + PlayMode
tools/check.sh --tests core       # one EditMode test category
tools/check.sh --tests playmode   # the PlayMode suite only
tools/check.sh --fast             # skip Unity for a few-second loop
```

The EditMode suite lives in `Assets/Tests/EditMode/` and the PlayMode suite in
`Assets/Tests/PlayMode/`. Both run in `.cache/test-project`, a persistent project
with `Assets` symlinked to the real folder and its own warm `Library`, so they
are safe with the editor open and the package import is paid once. Each EditMode
class carries a `core`, `flight`, `presentation`, or `smoke` category and the
PlayMode class carries `playmode`; `--tests` filters on it. The EditMode suite
covers the Kepler
solver, orbital-element state and conservation laws, Lagrange points, the
catalogue invariants, parent-first propagation, epoch distances and speeds, the
Moon's parent-equator frame, station updates, orbit sampling, the clock, body
details and picking, the focus-transition easing, the orbit camera and its
single-range zoom, the cursor anchor, continuous body avoidance, the drawing
rules, the URP pipeline and shader wiring, the sky's texture and shader, the
flight core (including the Lambert solver and both coast profiles), the input
seam's key table and mouse
conversions, the uGUI HUD's object graph, content, and button wiring, the
assembly layout graph, the scene's frame loop, and the same flight through
the real bootstrap.
See [Testing and verification](/technical/testing.md) for the test matrix,
numerical reference values, and conventions.

The PlayMode suite covers what EditMode cannot: the runtime-created bootstrap
inside Unity's own player loop, simulation before presentation in one frame, the
real renderer's body views and orbit lines, the HUD's frame pump publishing an
async preview into the real trajectory `LineRenderer`s, the trip-time retime,
and Begin and Cancel through the real HUD buttons. It runs under `-nographics`,
so it asserts component state and projected screen positions rather than
framebuffer pixels.

The orbital maths is also checked outside Unity at the epoch against known
values:

* planet heliocentric distances and speeds,
* moon geocentric distances versus semi-major axes,
* Lagrange point distances for all six stations.

`tools/validate_catalogue.py` enforces the catalogue checklist without opening
Unity, and `tools/check.sh` regenerates the catalogue and the sky offline to
prove the committed files are reproducible.

The C# runtime and editor scripts compile against Unity 6.3's assemblies. The
project is intended to be opened in Unity 6.3 (6000.3.28f1) with the Universal
Render Pipeline, the Input System package, uGUI, and Active Input Handling set to
**Input System Package (New)**.

# Known limitations

* No fuel, propellant accounting, or delta-v budget in the plan's cost: the plan
  reports delta-v but does not spend it.
* No attitude dynamics or slew limits for the ship itself; thrust direction is
  instantaneous. The camera slew is decorative only.
* No manual thrust: a flight is plan-and-watch. Free flight is a debug teleport,
  not a hand-flown ship.
* No mid-flight re-plan beyond pressing **Replan** again, which replans from
  the current state and discards the old plan and any preview.
* No multi-revolution Lambert branch, no porkchop departure search, and no
  gravity assists. The coast is zero-rev, prograde, and short-way only.
* No stationkeeping drift.
* No N-body perturbations.
* No save/load.
* The HUD is uGUI built at runtime and updates its text every frame. The object
  graph is stable, but a panel's text is re-evaluated per frame; a dirty flag
  would shave allocations when nothing changed. The PlayMode suite drives the
  frame pump and the button events, but no test sends a real pointer through the
  EventSystem, so button hit areas and canvas scaling still need a human eye.
* The scene is generated at runtime; there is no authored art scene.
* The rendering migration stays on the URP forward path: post-processing is one
  Bloom plus Neutral tonemapping pass from the default volume profile, and there
  is no SSAO, no shadow system, and no two-camera far field. Those remain future
  work, and the shaders are intentionally minimal.
* Warp drops in the clock's power-of-ten steps, not continuously.

# Extension points

* **Fuel model:** charge `PlanArc.DeltaVMps` against a budget, grow the dry mass,
  and re-plan when the budget cannot pay. With fuel modelled, the cheap `Coast`
  becomes the obvious default and the burn–coast–burn profile the expensive one.
* **Porkchop search:** sample departure and arrival dates against
  `LambertSolver.SolveShortWay` and show the time/cost heat map, so the coast
  profiles pick a launch window instead of planning "leave now".
* **Multi-rev coasts:** extend `OrbitMath.SolveKepler` and `ConicArc` to several
  revolutions, then add the multi-rev Lambert branch and the long-way flag.
* **Fixed-thrust spiral:** a continuous low-thrust cruise that does not coast on
  a Lambert conic, for freighters.
* **Manual override:** a thrust path that suspends the plan without cancelling
  it, so the player can hand-fly and then resume or re-plan.
* **Save/load:** persist Julian date, ship state, fuel, and contract state; the
  world itself is reproducible from the catalogue.
* **Visual pass:** replace primitives with meshes, add rings, surface textures,
  and a real bright-star catalogue; keep the scale and floating-origin rules.
