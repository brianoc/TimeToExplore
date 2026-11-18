---
type: Reference
title: Testing and verification
description: The automated checks agents and humans run before calling work done.
tags: [technical, testing, unity, verification]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# One command

```bash
tools/check.sh          # everything, including Unity EditMode tests
tools/check.sh --fast   # docs, catalogue, generator only (a few seconds)
tools/check.sh --keep   # keep the throwaway Unity project and logs
```

`check.sh` runs five gates in order:

| Gate | Tool | What it proves |
|---|---|---|
| Design bundle | `tools/validate_okf.py` | OKF v0.2 structure, frontmatter, links, index coverage |
| Catalogue schema | `tools/validate_catalogue.py` | Field types, ids, parents, counts, orbit ranges, epoch maths |
| Catalogue generator | `tools/generate_solar_system.py --offline --output …` | The committed JSON is byte-identical to a fresh generation |
| Sky generator | `tools/generate_skybox.py --out …` | The committed `milky_way.png` is pixel-identical to a fresh generation |
| Runtime | Unity EditMode suite | Kepler maths, catalogue loading, model propagation, clock |

The Unity run uses a throwaway copy of `Assets/`, `Packages/`, and
`ProjectSettings/`, so it never locks or disturbs an editor that has the real
project open. It picks the editor named in `ProjectVersion.txt`; override with
`UNITY_BIN=/path/to/Unity`. Python defaults to `.venv/bin/python`, then
`python3`; override with `PYTHON`.

A full run takes roughly a minute after the first import. `--fast` is the loop
to use while editing docs, Python tools, or catalogue data. The Unity suite is
the gate for any C# change.

# Unity EditMode suite

Tests live in `Assets/Tests/EditMode/` behind the `TimeToExplore.Tests.EditMode`
assembly. The runtime is split into `TimeToExplore.Core`,
`TimeToExplore.Simulation`, and `TimeToExplore.Presentation`, with
`TimeToExplore.Editor` for editor tooling; an assembly definition is required
because Unity test assemblies cannot reference the predefined
`Assembly-CSharp`. The suite references all four project assemblies, and
`AssemblyLayoutTests` asserts the graph itself.

| File | Covers |
|---|---|
| `JulianDateTests` | JD constants, UTC round-trip, formatting |
| `Vector3dTests` | algebra, normalization, ecliptic ↔ Unity conversion |
| `OrbitMathTests` | angle wrapping, Kepler residual, Lagrange roots and geometry, pole conversion |
| `OrbitalElementsTests` | circular and elliptical states, vis-viva, energy and angular momentum, ellipse closure |
| `CatalogueTests` | shipped JSON: counts, ids, names, hierarchy, units, stations, epochs |
| `SolarSystemModelTests` | load, parent-first order, Sun at rest, epoch distances and speeds, Moon frame, stations, orbit sampling |
| `SimulationClockTests` | warp arithmetic, pause, clamping, labels |
| `SelectionAndFocusTests` | details rows and physics, screen-space picking, the map's class priority and the unchanged local nearest-hit rule, smoothstep/slerp transition, approach offset |
| `SceneSmokeTests` | the shipped and comparison scenes resolve the bootstrap; the shipped scene leaves the map switch off, the comparison scene turns it on; the catalogue resource imports; both cameras carry `UniversalAdditionalCameraData` with post-processing and depth/colour copies off and a black solid-colour clear (the serialized scene; the bootstrap switches the live camera to `Skybox`) |
| `RenderingPipelineTests` | the graphics default is a URP asset; the shipped pipeline and renderer assets are wired together; cheap settings (HDR off, MSAA 1, scale 1, no depth/opaque copy, no shadows); quality levels do not override the default; the body, unlit, and line shaders are found under `Resources`, have no compile errors, declare the URP pipeline and forward-pass tags, and expose the properties the renderer writes; `material.color` still works; `RenderingShaders.ForBody` picks the Sun shader |
| `SkyboxTests` | the committed `Sky/milky_way` texture is 2048 × 1024 with no mip chain, is dark overall and has a bright band covering part of the sky, and the skybox shader is found under `Resources` with no compile errors; `SkyView` points `RenderSettings.skybox` at its material and clears the camera to `Skybox`, `Remove` restores the previous skybox and clear flags, and a missing texture or shader is refused without half-applying |
| `ShipDynamicsTests` | two-body speeds, energy, eccentricity, flight-path angle, thrust clamping |
| `GravityFieldTests` | Newton at 1 AU, Mars dominance, pruning versus the full sum, surface clamp, no mutation of the model, pure versus frame state |
| `PlanGeometryTests` | quintic endpoint conditions and peak factors, parking radius and its gravity ceiling across the catalogue, circular/equatorial/prograde orbits, SOI radii |
| `ShipIntegratorTests` | orbit stability, Kepler agreement, kinematic thrust, the brachistochrone table, step-size independence, collision, step cap |
| `TripPlannerTests` | arc chain and phases, exact arrival, seam continuity, flight-time envelope, Venus, Jupiter at the ceiling, the Moon, LEO departure, determinism, no mutation, the default quintic arrival style, and pinned durations: exact flight time at twice the minimum with a gentler burn and less delta-v, a conic plan at 1.5× the fastest time landing exactly there, an infeasible report at 95%, and rejection of a non-positive or non-finite request |
| `FlightExecutionTests` | plan, fly, and arrive at Mars and Jupiter in stable orbits; feedback from a perturbed start; Mars back to Earth; the orrery is untouched |
| `FlightSessionTests` | ship ownership, the Go To plan from Gateway, exact arrival through the session, budget clamping, cancel, teleport, a flight through a body, no mutation, the conic arrival applying its burns and arriving matched, the session arrival style reaching the planner, the preview (never moves the ship, one-at-a-time, Begin consumes and replans, Cancel clears, the 1% staleness window, the `[minimum, 3 × minimum]` trip-time window, retiming without moving the ship, clamping, and Begin keeping the chosen time), and the idle coast that keeps the ship moving with the clock |
| `WarpPolicyTests` | clearance, local-dynamics, and plan-event caps; scale rounding; the suggested Go To warp |
| `PlanPathTests` | arc boundary walking, the flip point's sign change, a straight arc staying one chord, every adaptive chord on the Mars plan meeting the sagitta tolerance with exact endpoints, the conic coast's flatness and dense periapsis sampling, the conic flip staying in the powered arc, and the periapsis and circularisation as plan boundaries |
| `FlightReportTests` | the phase table, live rows, arrival rows, preview and stale rows, a re-timed preview's flight time and later arrival, the conic Coast/Capture/Parking rows and the impulsive `Burns` row, status labels, countdown formatting |
| `KeplerPropagatorTests` | universal-variable propagation against the elements path on circular and eccentric ellipses, the hyperbolic periapsis and its analytic speed, forward/backward round trip, energy and angular momentum over 40 steps, state ↔ conic elements, a circular conic arc, and its two-body reference acceleration |
| `TransferConicTests` | the entry at the sphere of influence, an inbound coast to a low periapsis, the capture plane and prograde direction, the retrograde periapsis and prograde apoapsis burns, the conic parking radius below the gravity ceiling, and invalid geometries reported rather than built |
| `ConicTripPlannerTests` | the conic arc chain, lower parking and cheaper capture than the quintic, seam continuity with velocity steps at the burns, end-to-end conic flights to Mars, Earth, Venus, Jupiter, and Saturn with a stable lower orbit, and the capture cost across all five targets |
| `OrbitCameraTests` | pitch clamp, yaw wrap, zoom limits, shortest turn, slew rate, distance easing, a zoom request that eases over many frames and lands exactly, placing a distance cancelling a pending request, and a zero-length frame completing the zoom |
| `CameraRigTests` | the auto-framing distance at arrival and in cruise, the clamps, the follow flags, the shared 3 px line width staying a sliver of the distance, the map pose and its stored-pose round trip, the wheel tween and the top-down tilt that follows it, the button routing (right-drag orbits at every zoom; left-drag pans only on the map and is inert locally; a pan clears both auto flags), both zoom ceilings, the pan and its 60 AU cap, the exponential pan ease, the near plane at both ends of the blend, auto-framing suspended on the map, and the unchanged local view with the map off |
| `MapViewTests` | the map blend endpoints and smoothstep midpoint, the zoom-out-and-back pose round trip, the Moon-inside-Earth dominant body at Gateway, the deep-space fallback, the pan cap, the extended zoom clamp, the top-down rotation and yaw-turning up vector, the blended rotation's agreement with the effective pitch, the local-to-map near-plane rule, the distance-scaled pan drag, and the pan ease |
| `MapDrawingTests` | the 4.5 px local and 6 px map marker minimum and its smooth blend, the true-scale override, the viewport angular conversion, the neighbourhood aid (full inside the SOI, monotonic to true scale at 3×, restored by the map, widened by the camera distance, disabled without a local scale), the always-on Sun/planet/station labels, the 5× parent-SOI moon window, the hidden full-system moons, the hover/selection-only asteroids, the unchanged local opening-shot labels, the map-only ship label, the line width: 3 px at every zoom, proportional to camera distance, and resolution-independent; and the orbit-line pass: 3 px local to 10 px map with the blend clamped and monotonic, the thick line still resolution-independent, Mercury warm and Neptune cool, all eight planets distinct in orbital order, one planet taking the warm end, and the palette ignoring the view |
| `SceneFlightTests` | the scene's frame loop: clock, warp cap, session advance, clamp; the full Gateway→Mars acceptance flight and three orbits |
| `BootstrapSmokeTests` | the real composition root: `Awake` builds the world and the camera rig, Go To wires the flight and the trajectory line, cancel and reset behave, the debug jump is refused in flight, the map switch reaches the rig and raises the far clip, the same switch gates the neighbourhood aid fade, a plot draws the preview and arrival ghost without flying, the trip-time slider re-times the preview through the real HUD without moving the ship, Begin replans at the current date, the clock runs while idle so a preview can age, discard leaves the ship alone, one wheel gesture through the rig reaches the map ceiling and returns to the exact remembered angle, one wheel gesture eases to the map through the frame loop instead of snapping, and the full map acceptance — zoom out, select Mars, plot, Begin — arrives on the parking orbit through the bootstrap's own frame loop |
| `InputMappingTests` | the explicit digit table (`1`–`9` → indices 0–8, `0` → 9), pause and every hotkey toggle, the highest-digit rule, `WASDQE` axes, Shift ×10 and Ctrl ×0.1, comma/period speed steps, the legacy 0.1 mouse sensitivity, macOS ≈ 1 and Windows 120 → 1 scroll normalisation, the screen-to-picking pointer flip, the 4 px click slop that separates a click from a map pan, and null devices returning neutral values |
| `InputSeamTests` | a source scan of `Assets/Scripts`: no legacy `UnityEngine.Input` call outside `PlayerControls`, the Input System named by exactly two runtime files (`PlayerControls` and `UiInput`), the seam polling `Keyboard.current`/`Mouse.current` with no `InputActionAsset`, the UI module assigning the default actions without double-equipping an EventSystem, no `OnGUI`/`GUI.*`/`GUIStyle`/`Texture2D.whiteTexture` in the runtime, and `activeInputHandler: 1` pinned |
| `InputDeviceTests` | a virtual keyboard and mouse through `InputTestFixture`: a device-free frame is neutral, key edges feed the hotkey intents, held keys feed the movement and modifier intents, mouse buttons feed click press/hold/release and right-drag begin/end, and device deltas and scroll pass through the 0.1 and normalisation rules |
| `AssemblyLayoutTests` | the five `.asmdef` files as the source of truth: Core has no references, Simulation references only Core and opts out of UnityEngine references, Presentation references Core + Simulation + the Input System + Unity UI, Editor and tests are Editor-only and reference the three runtime assemblies, no assembly references a later layer, every project reference names a known assembly, exactly one asmdef per layer with no stray under `Player`/`Rendering`/`UI`, unique GUIDs, and the compiled assembly names of representative types |
| `SolarSystemHudTests` | the runtime uGUI HUD: one Screen Space Overlay Canvas with the 1920×1080 scaler and a GraphicRaycaster, exactly one EventSystem with an `InputSystemUIInputModule`, no legacy module, and navigation events off so Space/Enter stay gameplay keys, the status text tracking the clock/ship/selection, the details panel opening for Mars with every `BodyDetails` row and closing, the flight panel for a preview and a live plan, the trip-time slider appearing with a preview, holding the fastest plan at its left end, and retiming through its `onValueChanged`, `ShowHud`/`ShowLabels`, the one-frame opening guard, and the route/begin/jump/close `onClick` handlers reaching the same owner methods |

The suite currently has **401 tests**. It deliberately tests the shipped
catalogue as well as the maths, so a bad regeneration fails before Unity opens
a scene.

EditMode does not advance `Time.deltaTime`, so `SolarSystemBootstrap` exposes a
`DeltaTimeSeconds` seam: the scene leaves its default (`() => Time.deltaTime`)
alone, and the frame-loop acceptance supplies a fixed 1/60 s step. The test
still calls the real `Update`/`LateUpdate`, so the frame order, the warp policy,
the clock clamp, and the arrival are the shipped ones.

# Numerical references at the epoch

These values anchor the tests. They come from JPL mean elements at
2026-11-15 (JD 2461359.5) and are checked in both Python and C#:

| Quantity | Value |
|---|---|
| Earth heliocentric distance | 0.989277 AU |
| Earth orbital speed | 30.106 km/s |
| Earth sidereal period | 365.256 days |
| Moon geocentric distance | 363,296 km |
| Gateway (Earth–Moon L1) | 54,841 km from the Moon, 308,455 km from Earth |

The catalogue validator also checks every planet against vis-viva, every moon
against its eccentricity bounds, and L4/L5 against the equilateral-triangle
property.

The flight core has its own anchors, measured from Gateway Station at the epoch
with a 10 m/s² drive:

| Quantity | Value |
|---|---|
| Gateway to Mars plan | 4.44 days, 2,345 km/s, 9.76 m/s² peak |
| Gateway to Venus plan | 2.85 days, 1,171 km/s, 8.42 m/s² peak |
| Gateway to Jupiter plan | 10.32 days, 5,189 km/s, 9.78 m/s² peak |
| Mars parking orbit | 3,898 km radius, 3.315 km/s |
| Jupiter parking orbit | 177,965 km radius, 4.0 m/s², 26.7 km/s |
| Arrival error after the Mars flight | 0.01 m, 0.0000 m/s |
| Mars eccentricity after three orbits | 3.1 × 10⁻⁶ |
| Jupiter eccentricity after three orbits | 1.9 × 10⁻⁵ |
| Gateway to the Moon | 1.56 hours, one arc |
| Coasting ship versus Kepler, 30 days | within 5 km |
| Circular orbit radius drift, 200 days | below 1 part in 10⁶ |

The scene's frame loop has its own acceptance numbers, measured at 60 fps with
the suggested initial warp of 10,000×:

| Quantity | Value |
|---|---|
| Gateway→Mars arrival | 2026-11-19 10:29:02 UTC, 4.44 days after departure |
| Arrival error through the frame loop | 0.0 m, 0.0000 m/s |
| Three orbits after arrival | 3,898 km, eccentricity 3.07 × 10⁻⁶, drive off |
| Real time | 65.7 s over 3,939 frames |
| Peak substeps in one frame | 63 of the 2,000 budget |
| Warp | x10,000 → x10, auto-limited |
| Deep-space frame cap | 328,932 s (above 1,000,000×) |
| Mars parking-orbit frame cap | 23.5 s (1,000×) |
| Mid-cruise plan-event cap | 8,673 s (100,000×) |
| Flip point | 3.3 hours after departure |
| Starved budget, 2 substeps/frame, 400 frames | reaches 2026-11-15 05:53, clock never ahead of the ship |

`BootstrapSmokeTests` repeats the same flight through the real composition root,
from the system map: 3,939 frames, 65.7 s, arrival 2026-11-19 10:29:02 UTC,
0.0 m and 0.0000 m/s, 3,898 km and e = 4.3 × 10⁻⁹ at arrival. The identical
counts are the point: the map is a camera pose, so the flight path does not
depend on how the player found the target.

The map state has these anchors at the epoch: the Moon's SOI radius is 66,194 km
(the design quotes 66,200), so Gateway is inside it and the Moon is the
dominant body there; the map zoom ceiling is 12,000,000 units (12 billion km,
about 80 AU); the pan cap is 60 AU, or 8,975,872 units; the map pose's up
vector at yaw 0 is `(0, 0, −1)` in Unity space; the map near plane at the
ceiling is 12,000 units (12 million km); and the map far plane is 40,000,000
units (40 billion km, about 267 AU). The pan ease is 3/s, so ten seconds leaves
less than a metre of a 60 AU offset.

The map drawing rules have these anchors: the marker radius is 4.5 px at
`w = 0` and 6 px at `w = 1`; a moon labels while the camera is inside 5× its
parent's SOI, which is 4,620,000 km for Earth's Moon (Earth SOI 924,000 km, so
the boundary is about 4,620 render units) and therefore not at full-system
zoom; the class rank puts the Sun, planets, and stations at 1 and moons and
asteroids at 0; plan lines stay 3 px through `LineWidth`, which is
`2 × depth × tan(fov/2) / viewportHeight × pixels` render units at the camera
distance; and orbit lines use the same formula with the pixel count blended
from 3 px at `w = 0` to 10 px at `w = 1`. At 60° and 1080p a 3 px line is
0.0032 × depth and a 10 px map orbit line is 0.0107 × depth; at 2160p the world
width halves and the apparent widths stay 3 px and 10 px. The planet palette is
anchored on Mercury `#FFA347` (warm orange) and Neptune `#B76BED` (cool purple),
with eight distinct samples in orbital order and no dependence on the blend
weight.

The neighbourhood aid is anchored at Gateway, where the Moon's 66,194 km SOI is
the local scale: the aid is full at the SOI edge, part-way down at 2× (132,388 km),
and exactly zero at 3× (198,582 km). The map weight lerps it back to full, so a
body an AU away is true-scale in the local view and a 6 px marker on the map.
The neighbourhood is the larger of the SOI radius and the camera distance, and a
non-positive or infinite radius disables the fade for the shipped scene.

The route preview uses the same Gateway→Mars plan, 4.44 days: the staleness
window is `PreviewStaleFraction = 0.01` of the flight, about 3,836 s
(1 h 4 m), so the `Plotted T−…` row starts near that value. Begin replans at the
current date rather than flying the preview, and the ghost marker sits at Mars's
position on the arrival date, not at the ship's parking position. While idle the
session coasts the ship under gravity with the drive off, so the ship stays with
Gateway and the clock, and a 1.2-hour coast is already past the Mars window.

The gravity ceiling is asserted for every planet and moon in the catalogue: the
parking gravity is at most 40% of the drive unless the Hill sphere caps the orbit
first. Mars and the Moon are pinned to their old radii, so the ceiling cannot
silently move the shallow wells; Earth, Venus, and the giants are pinned to their
new ones, and Jupiter is asserted **feasible** at 177,965 km.

# Rendering pipeline anchors

The URP migration is checked without rendering a frame:

* `GraphicsSettings.defaultRenderPipeline` is
  `Assets/Settings/TimeToExploreUniversalRenderPipeline.asset`, a
  `UniversalRenderPipelineAsset` whose first renderer is
  `Assets/Settings/TimeToExploreUniversalRenderer.asset`.
* Every quality level's `customRenderPipeline` is empty, so the graphics default
  applies at every level.
* The pipeline is deliberately cheap: HDR off, MSAA 1, render scale 1, camera
  depth and opaque textures off, main/additional/soft shadows off, URP main
  and additional light rendering disabled, and intermediate texture mode Auto
  (with no renderer features, nothing is allocated).
* `TimeToExplore/BodyLit`, `TimeToExplore/Unlit`, and `TimeToExplore/Line` all
  resolve through `Shader.Find`, live under `Assets/Resources/Shaders/`,
  declare `RenderPipeline = UniversalPipeline` and a
  `LightMode = UniversalForward` pass, and report no shader compile errors.
* `_Color` is declared by every coloured shader, so `material.color` and the
  renderer's `SetColor("_Color", …)` calls keep working; `_Ambient` is on the
  body shader and `_MainTex` is on the line shader.
* Both scene cameras carry `UniversalAdditionalCameraData` with post-processing,
  depth texture, and colour texture requests off.
* `TimeToExplore/Skybox` is deliberately **not** in `RenderingPipelineTests`'
  forward-pass assertion: it is a background pass drawn by Unity's skybox path,
  so it carries no pipeline or `LightMode` tags. `SkyboxTests` proves it compiles
  and that the texture import keeps its uncompressed, non-mipmapped sRGB,
  repeat-wrapped shape. The camera's serialized clear is still solid colour; the
  bootstrap switches it to `Skybox` at runtime.

Batch mode runs with `-nographics`, which does not populate pass tags:
`Shader.FindPassTagValue` reports one pass for every shader, built-in or URP.
The test therefore asserts the pipeline and forward-pass tags in the shader
source, and the rendered frame remains a human check.

# Input anchors

The seam keeps the pre-migration feel with two explicit conversions:

* **Mouse delta.** The legacy `Mouse X`/`Mouse Y` axes applied a 0.1
  sensitivity, so `axis = raw pixels × 0.1`. With `OrbitDegreesPerMouseUnit = 3`
  that is 0.3° per raw pixel of orbit drag; with `PanPixelsPerMouseUnit = 10` it
  is one screen pixel of map pan per raw pixel.
* **Wheel.** Windows reports 120 units per notch, macOS about 1.
  `NormaliseScroll` divides by 120 only on `WindowsPlayer`/`WindowsEditor`, so
  one notch is one `ZoomFactorPerStep` (1.25×) step on both camera ranges. The
  step *requests* the distance; the camera eases to it at
  `ZoomEaseRatePerSecond`, so the input anchors still describe the request, not
  the frame's placement.

The digit table is explicit: `Digit1`–`Digit9` map to indices 0–8 and `Digit0`
to 9. The clock has seven scales, so `8`, `9`, and `0` are mapped but ignored by
the bootstrap, exactly as the old ascending loop did. Two digits in one frame
take the highest index. The pointer conversion is `y = Screen.height − y`.

`InputTestFixture` is documented by Unity as a Play mode tool. It still works
for these EditMode tests because `Press`, `Release`, `Move`, and `Set` queue
device state and the fixture updates the Input System itself; it does not run
the player loop, so it proves the device edges and not frame ordering. The
fixture's own keyboard and mouse are isolated from the machine's devices.

# uGUI HUD anchors

The HUD migration is checked without a rendered frame:

* `SolarSystemHud.Initialise` creates exactly one `Canvas` in Screen Space
  Overlay mode, with `CanvasScaler.ScaleWithScreenSize`, a 1920×1080 reference
  resolution, `matchWidthOrHeight = 0.5`, and a `GraphicRaycaster`.
* Exactly one `EventSystem` exists, it carries an `InputSystemUIInputModule`, and
  it is not the legacy `StandaloneInputModule`. Calling
  `UiInput.EnsureEventSystem` again reports `created: false` and leaves the count
  at one.
* The status clock text is `JulianDate.Format(clock.JulianDate) + "     " +
  clock.Label`; the distance line carries the same `FormatDistance` value and the
  session's ship status; the nearest line names
  `SolarSystemModel.FindNearest`.
* The details panel for Mars shows every `BodyDetails.Build` row label and value,
  the title `Mars`, and the kind `PLANET`, and `CloseDetails` hides it.
* The flight panel is hidden while idle, shows the `PREVIEW` rows after
  `PlotRoute`, and the `PLAN` + `NOW` rows after `BeginGoTo`; the title is
  `FLIGHT  /  MARS`.
* The panel's four buttons are non-interactable on the frame the details screen
  opens and interactive on the next `Update` (except where their rule says
  otherwise). `RouteButton` plots, then replans while flying; `BeginButton`
  starts the plotted flight; `JumpButton` is refused during a flight and starts
  free flight after a cancel; `CloseButton` closes the screen and discards a
  preview.
* `ShowHud` toggles the status panel and `ShowLabels` toggles the whole label
  pool.
* `InputSeamTests` proves the IMGUI path is gone and the backend is New only.

# Precision notes

Julian dates are large doubles. At 2.4 million, one unit in the last place is
about 40 microseconds. A one-second finite difference is therefore too close to
the representation floor to be a clean velocity check; use a minute or more.
Position tolerances near 1 AU can still be sub-millimetre, so tests use
tolerances scaled to the quantity rather than a blanket epsilon.

The same grid sets the floor for the flight core. A 100-second burn is only
known to about 0.05 seconds, so the analytic ½at² check has a half-metre
tolerance, and the twelve-thousand-second brachistochrone check allows ten
metres. That is the calendar's resolution, not the integrator's.

Subtracting two heliocentric positions at 1.6 AU loses about 10 µm. Tests that
care about orbital geometry work in the body-relative frame
(`ParkingOrbit.StateRelativeTo`) rather than subtracting world positions.

`JsonUtility` deserializes a JSON `null` object into a default instance, not a
null reference. Never use `field != null` to decide whether an optional object
was present. Use the authoritative discriminator (`kind` for stations, a
non-empty `parent` for orbits) and keep a regression test for it.

# Adding a test

1. Put pure maths in the relevant existing file.
2. Put catalogue invariants in `CatalogueTests`, runtime behaviour in
   `SolarSystemModelTests`, and flight behaviour in the matching
   `Simulation` suite.
3. Use `CatalogueFixture.LoadModel()` and `FlightFixture` rather than loading
   the JSON or building states by hand.
4. Assert physical relationships (vis-viva, energy, geometry) rather than
   re-running the implementation's own formula.
5. Run `tools/check.sh` and report the test count.

# Not covered yet

* PlayMode tests for the renderer, bootstrap, and HUD. These need a scene and
  graphics, and are the next layer once the presentation code settles. The frame
  loop is covered in EditMode by `SceneFlightTests`, which reproduces the
  bootstrap's order of operations, and the wiring is covered by
  `BootstrapSmokeTests`, which invokes the real `Awake`/`Update`/`LateUpdate` in
  edit mode. `SolarSystemHudTests` invokes the real HUD `Update` and each
  button's `onClick` directly, but no test delivers a real pointer through the
  EventSystem or renders a frame. What remains is the actual canvas render, the
  button hit areas, pointer-over-panel on the first frame, cursor capture, and
  canvas scaling away from 1080p. The URP migration has the same gap:
  `RenderingPipelineTests` proves the pipeline and shaders are configured, but not
  what a frame looks like.
* Screen-space picking at the edges of the view and on a moving camera; the
  pure picker is tested, but the projection step and the pointer-over-panel
  decision need a PlayMode scene.
* The trajectory line's rendered pixels, the shader's `fwidth` coverage, and the
  dash texture. Its data sources (the plan's boundaries, flip, and adaptive
  chord flatness) are tested, but no test renders a frame.
* Performance budgets for orbit-line updates and per-frame HUD text updates.
* Fuel accounting, attitude dynamics, finite-burn arcs, ring/atmosphere arrival
  models, and the Lambert cruise (the conic *arrival* is covered).
* Continuous integration. The repository is not under version control yet;
  when it is, `tools/check.sh` is the single CI entry point.
