---
type: Reference
title: Testing and verification
description: The automated checks agents and humans run before calling work done.
tags: [technical, testing, unity, verification]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
---

# One command

```bash
tools/check.sh                    # everything, including the Unity suite
tools/check.sh --fast             # docs, generators, and tooling (a few seconds)
tools/check.sh --tests flight     # the fast gates plus one test category
tools/check.sh --tests core,smoke # any comma-separated categories
tools/check.sh --reset            # drop the warm test project first
```

`check.sh` runs six gates in order:

| Gate | Tool | What it proves |
|---|---|---|
| Design bundle | `tools/validate_okf.py` | OKF v0.2 structure, frontmatter, links, index coverage |
| Catalogue schema | `tools/validate_catalogue.py` | Field types, ids, parents, counts, orbit ranges, epoch maths |
| Catalogue generator | `tools/generate_solar_system.py --offline --output …` | The committed JSON is byte-identical to a fresh generation |
| Sky generator | `tools/generate_skybox.py --out …` | The committed `milky_way.png` is pixel-identical to a fresh generation |
| Tooling | `tools/test_unity_guard.py` | The guard stops a stuck run and passes through a real exit code |
| Runtime | Unity EditMode suite | Kepler maths, catalogue loading, model propagation, clock |

The Unity tests carry one category each: `core`, `flight`, `presentation`, or
`smoke`. `--tests` narrows the Unity gate to the categories named; the fast
gates still run. A `core` or `presentation` run is a few seconds, the flight
matrix is about six minutes, and the whole suite about nine. The burn–coast–burn
plan is the expensive part: its fixed burns integrate the summed field, and a
plan build is a few seconds in the editor.

It picks the editor named in `ProjectVersion.txt`; override with `UNITY_BIN`.
Python defaults to `.venv/bin/python`, then `python3`; override with `PYTHON`.

The Unity suite runs in `.cache/test-project`, not in a throwaway copy. `Assets`
is a symlink to the real folder, `Packages` and `ProjectSettings` are refreshed
copies, and the project keeps its own `Library`. The package import is paid once
instead of on every run; `--reset` drops the cache and rebuilds it, which costs
about half a minute on the first run. The cache is gitignored and never contains
edited sources.

Unity runs under `tools/unity_guard.py`, which stops it when it writes nothing
to the log for eight minutes, exceeds a thirty-minute budget, repeats a Unity
cloud-endpoint lookup, or starts shutting down and does not finish. Shutdown
hangs are the known failure: batch Unity can finish the tests, write
`results.xml`, and then retry DNS lookups forever. `check.sh` still reads the
results that were written, so a hang at the end does not throw away a completed
run. The log is `.cache/test-project/unity.log`. The stall window is long
because the runner writes nothing between test classes: the burn–coast–burn
class is one silent stretch of about four minutes now that its burns integrate
the summed field ([Full-field burns](/simulation/powered-leg-floor.md)). For any
other batch run, such as a capture helper or a scene builder, use the same
wrapper:

```bash
python3 tools/unity_guard.py --log /tmp/unity.log -- "$UNITY" …
```

A full run takes about nine minutes with the warm project. `--fast` is the loop
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
| `TripPlannerTests` | arc chain and phases, exact arrival, seam continuity, flight-time envelope, Venus, Jupiter at the ceiling, the Moon, LEO departure, determinism, no mutation, the default quintic arrival style, the fixed-thrust style's default 0.3 g level, its clamp to the drive and the control range, and the conic parking radius, and pinned durations: exact flight time at twice the minimum with a gentler burn and less delta-v, a conic plan at 1.5× the fastest time landing exactly there, an infeasible report at 95%, and rejection of a non-positive or non-finite request |
| `FlightExecutionTests` | plan, fly, and arrive at Mars and Jupiter in stable orbits; feedback from a perturbed start; Mars back to Earth; the orrery is untouched |
| `FlightSessionTests` | ship ownership, the Go To plan from Gateway, exact arrival through the session, budget clamping, cancel, teleport, a flight through a body, no mutation, the conic arrival applying its burns and arriving matched, the hybrid arrival stepping through one finite burn and the trim, the session arrival style reaching the planner, a style switch rebuilding the preview at the pinned date while keeping a flight time that fits the new window and leaving the ship still, the preview (never moves the ship, one-at-a-time, Begin consumes and replans, Cancel clears, the 1% staleness window, the powered `[minimum, 3 × minimum]` trip-time window and its clamp, retiming without moving the ship, and Begin keeping the chosen time), the preview-window cache (a repeat plot at the same date returns the same plan, another target misses, a burn-level change misses, and a changed departure date invalidates the cache), the background retime (the newest of two requests made while the first builds is published and the older build is dropped, the worker's plan equals the synchronous plan, the previous preview stays published until the new one is ready, a synchronous retime supersedes an outstanding ask, a discard drops an outstanding request, and Dispose joins the worker and refuses new work), and the idle coast that keeps the ship moving with the clock |
| `WarpPolicyTests` | clearance, local-dynamics, and plan-event caps; scale rounding; the suggested Go To warp |
| `PlanPathTests` | arc boundary walking, the flip point's sign change, a straight arc staying one chord, every adaptive chord on the Mars plan meeting the sagitta tolerance with exact endpoints, the conic coast's flatness and dense periapsis sampling, the conic flip staying in the powered arc, the periapsis and circularisation as plan boundaries, the hybrid arcs meeting the same flatness tolerance, and one finite burn with the trim and circularisation as the two impulses |
| `RouteColourTests` | exactly three path types, one distinct colour each, solid for a quintic or fixed burn and dashed for a conic, distinct same-hue shades inside a run of one type, and the real quintic, conic, and hybrid plans: three gold quintics, three shaded conics, and a red burn that restarts the conic run |
| `FlightReportTests` | the phase table, live rows, arrival rows, preview and stale rows, a re-timed preview's flight time and later arrival, the conic Coast/Capture/Parking rows and the impulsive `Burns` row, the hybrid `Burns` row reading the fixed and trim delta-v at the chosen g level, status labels, countdown formatting |
| `KeplerPropagatorTests` | universal-variable propagation against the elements path on circular and eccentric ellipses, the hyperbolic periapsis and its analytic speed, forward/backward round trip, energy and angular momentum over 40 steps, state ↔ conic elements, a circular conic arc, and its two-body reference acceleration |
| `LambertSolverTests` | the zero-rev prograde short-way solve: propagating `v₁` onto `r₂`, a 90° circular quarter turn, a Hohmann-like transfer against the vis-viva speeds, a high-energy hyperbolic transfer, the short-way branch returned while the long way is refused, the 180° guard returning no solution instead of throwing, degenerate arguments throwing, and an epoch Earth→Mars coast landing at Mars |
| `TransferConicTests` | the entry radius (the sphere of influence for a coast, the handover floor for a burn–coast–burn), an inbound coast to a low periapsis, the fall shortening as the entry radius comes in (Uranus 117.1323 d from the sphere against 0.02153 d from the 38,643.0 km floor) and as the entry speed rises (0.00623 d at 50 km/s), the periapsis capture burn being set by the entry speed and not the entry radius (6.3691 km/s at 5 km/s from either radius, 39.0952 at 50 km/s), the capture plane and prograde direction, the retrograde periapsis and prograde apoapsis burns, the conic parking radius below the gravity ceiling, and invalid geometries reported rather than built |
| `ConicTripPlannerTests` | the conic arc chain, lower parking and cheaper capture than the quintic, seam continuity with velocity steps at the burns, end-to-end conic flights to Mars, Earth, Venus, Jupiter, and Saturn with a stable lower orbit, and the capture cost across all five targets |
| `CoastTripPlannerTests` | the coast chain — quintic break, heliocentric conic, conic capture — with the drive off on the middle arc, the departure retargeted to the Lambert `v₁`, the flown arrival and three-orbit hold, the orders-of-magnitude delta-v drop, the planner finding a coast time on its own, the style switch leaving the ship at t₀, a rejected long transfer with its reason, and the handover to the hybrid capture |
| `FiniteBurnTests` | a constant-magnitude arc starting exactly at its state, accelerating at the fixed level opposite the velocity, the time mapping being the identity, matching the standalone RK4 simulation, the radius-crossing solver matching a post-hoc simulation, and meeting the plan's sagitta tolerance; a full-field burn crossing the sphere of influence matching an independent 20,000-step RK4 of the same field and thrust, its acceleration at a sample being the field plus the thrust rather than the two-body value, a short local burn agreeing with the two-body burn, and the reference body being a label (one inertial path, two local decompositions differing by exactly the two bodies' states); the hybrid capture solver on Venus, Earth, Mars, Jupiter, Saturn, and the Moon with a safe injection radius, the trim landing on the exact transfer ellipse, the circularisation leaving a circular parking orbit, a stronger burn leaving a smaller trim, and the conic parking radius at 0.3, 0.5, and 1.0 g |
| `FiniteBurnTripPlannerTests` | the six-arc hybrid plan with one finite burn between the conic coasts, the trim and circularisation impulses, seam steps of exactly the burn level, end-to-end flights to Mars, Earth, Venus, Jupiter, and Saturn with stable parking orbits, every level from 0.3 to 1.0 g reaching an exact parking orbit, zero level reproducing the conic plan, the cheaper-than-quintic capture across all five targets, the Jupiter parking radius below the quintic ceiling, and the finite/trim split reaching the plan |
| `HandoverBandTests` | the handover band the fixed burns hand over inside: the floor scaling as `1/sqrt(a)` when halved (Earth and Jupiter unclamped, and Mercury's gravity floor under a clamped band), the ceiling being exactly `1.02 × SOI` per body and moving per body (Mercury 114,658.2 km against Uranus 52,795,223.9 km), the departure endpoint being the ceiling and the arrival endpoint the floor, the clamped floor sitting at or above the surface clearance on every planet, moon, and asteroid at 0.3 g and 1 g (Mercury and Mars at 1 g are the named cases, their gravity floors below the surface), the exactly three catalogue bodies whose band is empty at every level (Phobos, Deimos, Proteus) reported with the floor, the clamp, and the ceiling in the reason and refused by the planner before a chain is built, and the body-clearance gate accepting a 1 g burn that crosses Earth's sphere while clearing the planet by 821,258 km and refusing a 30 s burn 60 km up that grazes the surface |
| `BurnCoastBurnTripPlannerTests` | the seven-arc profile — quintic break, prograde finite burn, Lambert coast, brake, conic capture — with the commanded level on both burns, the departure burn sized from the velocity change rather than a time share, the handover flown rather than impulsive, the capture chain entering at the arrival handover floor (Uranus 1 g 400 d: 38,643.0 km, a 1,860.2 s fall, a 6.369 km/s periapsis burn, every seam inside 1 km and 1 m/s) while the coast profile keeps its 577,241 km sphere-of-influence entry and 1.3187-day fall, the brake spending the coast plan's handover, continuous position and velocity at every seam, every fixed burn clearing the body it works near, a flown arrival on the conic parking orbit and its three-orbit hold, `0 g` reproducing the coast plan exactly, a slow crossing closing cheaply (200 days to Mars at 12.46 km/s) with the slow end set by the 179.4° coast, a time without a prograde short transfer rejected with its 180° reason, the hybrid arrival keeping all three burns at the level, and the powered cruise still the default; the two-tier break search — the fast departure-body-only peak against the full-field plan peak on Mercury 8 d and 12.3 d, Mars 90 d and the Mercury floor, the reported break peak and delta-v unchanged, the 5% screen certifying a near-limit fast peak with the full field and never accepting a full-field over-thrust break, and the just-below floors still rejected; every burn in a reported plan integrating the summed field (its acceleration is the field plus the thrust, the non-Sun term is there at each burn, and a Sun-only field moves the plan), the hybrid capture's injection burn included; and the floor search returning a real full-field chain inside the drive, which is the certification that stands in for the fast probes |
| `OrbitCameraTests` | pitch clamp, yaw wrap, zoom limits, shortest turn, slew rate, distance easing, a zoom request that eases over many frames and lands exactly, placing a distance cancelling a pending request, and a zero-length frame completing the zoom |
| `CameraRigTests` | the auto-framing distance at arrival and in cruise, the clamps, the follow flags, the shared 3 px line width staying a sliver of the distance, the map pose and its stored-pose round trip, the wheel tween and the top-down tilt that follows it, the button routing (right-drag orbits at every zoom; left-drag pans only on the map and is inert locally; a pan clears both auto flags), both zoom ceilings, the pan and its 60 AU cap, the exponential pan ease, the near plane at both ends of the blend, auto-framing suspended on the map, and the unchanged local view with the map off |
| `MapViewTests` | the map blend endpoints and smoothstep midpoint, the zoom-out-and-back pose round trip, the Moon-inside-Earth dominant body at Gateway, the deep-space fallback, the pan cap, the extended zoom clamp, the top-down rotation and yaw-turning up vector, the blended rotation's agreement with the effective pitch, the local-to-map near-plane rule, the distance-scaled pan drag, and the pan ease |
| `MapDrawingTests` | the 4.5 px local and 6 px map marker minimum and its smooth blend, the true-scale override, the viewport angular conversion, the neighbourhood aid (full inside the SOI, monotonic to true scale at 3×, restored by the map, widened by the camera distance, disabled without a local scale), the always-on Sun/planet/station labels, the 5× parent-SOI moon window, the hidden full-system moons, the hover/selection-only asteroids, the unchanged local opening-shot labels, the map-only ship label, the line width: 3 px at every zoom, proportional to camera distance, and resolution-independent; and the orbit-line pass: 3 px local to 10 px map with the blend clamped and monotonic, the thick line still resolution-independent, Mercury warm and Neptune cool, all eight planets distinct in orbital order, one planet taking the warm end, and the palette ignoring the view |
| `SceneFlightTests` | the scene's frame loop: clock, warp cap, session advance, clamp; the full Gateway→Mars acceptance flight and three orbits |
| `BootstrapSmokeTests` | the real composition root: `Awake` builds the world and the camera rig, Go To wires the flight and the trajectory line, cancel and reset behave, the debug jump is refused in flight, the map switch reaches the rig and raises the far clip, the same switch gates the neighbourhood aid fade, a plot draws the preview and arrival ghost without flying, the route draws one coloured line per arc with same-type shades and the style switches redraw the quintic, conic, and hybrid palettes, the trip-time slider requests a retime through the real HUD, holds the handle and the previous full-field preview while the plan lags, publishes the finished plan on the main thread without moving the ship, Begin replans at the current date, the clock runs while idle so a preview can age, discard leaves the ship alone, one wheel gesture through the rig reaches the map ceiling and returns to the exact remembered angle, one wheel gesture eases to the map through the frame loop instead of snapping, and the full map acceptance — zoom out, select Mars, plot, Begin — arrives on the parking orbit through the bootstrap's own frame loop, the burn–coast–burn acceptance — select the profile, plot, Begin, and arrive on the conic parking orbit through the same frame loop — plus the frame loop dropping a frame until the model, clock, and renderer exist and skipping a body view destroyed mid-teardown |
| `InputMappingTests` | the explicit digit table (`1`–`9` → indices 0–8, `0` → 9), pause and every hotkey toggle, the highest-digit rule, `WASDQE` axes, Shift ×10 and Ctrl ×0.1, comma/period speed steps, the legacy 0.1 mouse sensitivity, macOS ≈ 1 and Windows 120 → 1 scroll normalisation, the screen-to-picking pointer flip, the 4 px click slop that separates a click from a map pan, and null devices returning neutral values |
| `InputSeamTests` | a source scan of `Assets/Scripts`: no legacy `UnityEngine.Input` call outside `PlayerControls`, the Input System named by exactly two runtime files (`PlayerControls` and `UiInput`), the seam polling `Keyboard.current`/`Mouse.current` with no `InputActionAsset`, the UI module assigning the default actions without double-equipping an EventSystem, no `OnGUI`/`GUI.*`/`GUIStyle`/`Texture2D.whiteTexture` in the runtime, and `activeInputHandler: 1` pinned |
| `InputDeviceTests` | a virtual keyboard and mouse through `InputTestFixture`: a device-free frame is neutral, key edges feed the hotkey intents, held keys feed the movement and modifier intents, mouse buttons feed click press/hold/release and right-drag begin/end, and device deltas and scroll pass through the 0.1 and normalisation rules |
| `AssemblyLayoutTests` | the five `.asmdef` files as the source of truth: Core has no references, Simulation references only Core and opts out of UnityEngine references, Presentation references Core + Simulation + the Input System + Unity UI, Editor and tests are Editor-only and reference the three runtime assemblies, no assembly references a later layer, every project reference names a known assembly, exactly one asmdef per layer with no stray under `Player`/`Rendering`/`UI`, unique GUIDs, and the compiled assembly names of representative types |
| `SolarSystemHudTests` | the runtime uGUI HUD: one Screen Space Overlay Canvas with the 1920×1080 scaler and a GraphicRaycaster, exactly one EventSystem with an `InputSystemUIInputModule`, no legacy module, and navigation events off so Space/Enter stay gameplay keys, the status text tracking the clock/ship/selection, the details panel opening for Mars with every `BodyDetails` row and closing, the flight panel for a preview and a live plan, the route legend appearing with the quintic preview as one *Quintic arc* row, stacking above the flight panel, listing *Conic arc* and *Fixed burn* with the hybrid, collapsing its three conic arcs into one row, staying non-raycast, hiding with `ShowHud`, and hiding when the route is discarded, the trip-time slider appearing with a preview, holding the fastest plan at its left end, and requesting a retime through its `onValueChanged` while the handle and label keep the player's ask and the finished plan is published on the main thread, the arrival-style buttons marking the active capture and rebuilding the preview against the chosen style (the trip-time window follows, and switching back restores the quintic arcs), the third `0.3 g burn` button rebuilding the preview with the hybrid finite burn and its impulsive circularisation, the burn-level slider from 0 to 1 g rebuilding the preview at 0.5 g and degenerating to the conic plan at 0 g, the cruise-profile row defaulting to Powered and rebuilding the preview as the burn–coast–burn plan (its own 6.74–20.21 day window, the shared level control driving both burns, no `% burn` on a coast) or as the unpowered coast, the legend gaining the fixed-burn row for the profile and losing it again on Coast and Powered, `ShowHud`/`ShowLabels`, the one-frame opening guard, the route/begin/jump/close `onClick` handlers reaching the same owner methods, and pointer picking skipping a view destroyed mid-teardown |

The suite deliberately tests the shipped catalogue as well as the maths, so a
bad regeneration fails before Unity opens a scene. Each class carries a
`[Category]` for its area, which is what `--tests` filters on.

Do not record a test count in the design bundle. It drifts with every commit and
is busywork to maintain; report the command and its result instead.

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
| Handover ceiling, Mercury | 114,658.2 km (`1.02 × SOI`) |
| Handover ceiling, Uranus | 52,795,223.9 km (`1.02 × SOI`) |
| Handover floor, Mercury at 1 g | 2,539.7 km (clamped; the gravity floor is 2,382.9 km) |
| Handover floor, Mars at 1 g | 3,489.5 km (clamped; the gravity floor is 3,322.4 km) |

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

The hybrid fixed-thrust arrival at 0.3 g = 2.942 m/s² has these measured
anchors (the capture solver's ideal chain):

| Target | Fixed burn | Injection radius | Parking radius | Fixed Δv | Trim Δv | Circularisation |
|---|---:|---:|---:|---:|---:|---:|
| Moon | 23.2 min | 1,842 km | 2,037 km | 4.10 km/s | 0.93 km/s | 0.04 km/s |
| Mars | 15.3 min | 3,559 km | 3,898 km | 2.70 km/s | 1.79 km/s | 0.08 km/s |
| Venus | 10.3 min | 6,354 km | 6,960 km | 1.81 km/s | 2.83 km/s | 0.16 km/s |
| Earth | 9.9 min | 6,690 km | 7,327 km | 1.74 km/s | 3.01 km/s | 0.17 km/s |
| Saturn | 23.4 min | 61,144 km | 66,967 km | 4.13 km/s | 7.38 km/s | 0.55 km/s |
| Jupiter | 18.8 min | 73,407 km | 80,398 km | 3.33 km/s | 14.20 km/s | 0.91 km/s |

The end-to-end flights arrive 0.0–0.8 m and 0.0000–0.0031 m/s from the reference
state; the parking eccentricities are 2 × 10⁻⁸ to 1.2 × 10⁻⁷ after three orbits.
At Mars the 0.3 g split is 2.70 fixed + 1.87 trim (the flight's arrival date),
and at 1.0 g the fixed burn is 3.22 and the trim is 0.90: a stronger burn leaves
a smaller trim. The hybrid capture is 4.57 km/s at Mars, 4.80 at Venus, 4.92 at
Earth, 12.06 at Saturn, and 18.44 at Jupiter, against the pure impulse model's
3.53, 4.13, 4.28, 10.66, and 17.40. The difference is the finite-burn loss; the
hybrid pays it instead of raising the parking radius.

The burn–coast–burn profile has these anchors at the epoch, from Gateway at
0.3 g with the conic capture:

| Quantity | Value |
|---|---|
| Trip-time window (Mars) | 6.74–20.21 days |
| Trip-time window (Mercury) | 3.22–9.65 days |
| True floor, Mars at 0.3 g | 6.74 days, peak 5.14 m/s², 1,172 km/s |
| True floor, Mercury at 1 g | 3.22 days, peak 9.70 m/s², 1,766 km/s |
| True floor, Uranus at 1 g | 34.00 days, peak 9.70 m/s², 2,047 km/s |
| True floor, Uranus at 0.3 g | 41.57 days, peak 2.94 m/s², 1,757 km/s |
| Just below the Mercury floor | 3.19 days refused: the departure break needs 10.1 m/s², above the 10.0 m/s² drive |
| Just below the Mars floor | 6.60 days refused at 10.03 m/s²; 6.70 days leaves the brake 8,073 m and 0.8 m/s short of the entry |
| 90-day Mars plan, total | 44.4 km/s (direct build, outside the nav window) |
| 90-day quintic break | 4.84 days, 8.8 km/s, 0.03 m/s² peak |
| 90-day departure burn | 76 min, 13.4 km/s, 2.94 m/s² |
| 90-day Lambert coast | 85.0 days, 0.05 km/s, 0.005 m/s² peak |
| 90-day brake | 1.7 h, 18.5 km/s, 2.94 m/s² |
| 90-day capture entry | 6,033 km (the Mars 0.3 g floor), 13.6-minute fall, 3.53 km/s impulse |
| Seam error | under a metre and 3.7 × 10⁻⁴ m/s on the tested trips; the floor gate is 1 km and 1 m/s |
| Arrival error | 0.1 m and 0.0001 m/s |
| Venus at 11.66 days | 183 km/s (departure 72, brake 58) |
| Jupiter at 230.6 days | 119 km/s (departure 40, brake 53) |
| `0 g` | exactly `CruiseStyle.Coast` |

The burn–coast–burn's capture chain enters at the arrival handover floor, not the
sphere of influence, and the coast profile keeps the sphere. The entry, the fall,
and the capture are measured at the epoch against the sphere-of-influence build:

| Quantity | Sphere entry | Floor entry |
|---|---:|---:|
| Uranus 1 g, 400 d: entry radius | 51,760,023 km | 38,643 km |
| Uranus 1 g, 400 d: inbound fall | 117.13 days | 31 min |
| Uranus 1 g, 400 d: capture Δv | 6.37 km/s | 6.37 km/s |
| Uranus 1 g, 400 d: total | 242.1 km/s | 182.3 km/s |
| Uranus 1 g, 400 d: brake start radius | 51.96 million km | 366,505 km (0.0071 SOI) |
| Uranus 0.3 g, 400 d: fall / total / brake start | 117.13 d / 242.5 / 52.45 million km | 1.3 h / 180.8 / 1,159,005 km (0.0224 SOI) |
| Mercury 1 g, 8 d: fall / total | 6.09 h / 569.9 km/s | 3.3 min / 557.0 km/s |
| Mars 0.3 g, 90 d: fall / total | 1.3187 d / 45.4 km/s | 13.6 min / 44.4 km/s |

`TransferConic.Build`'s radius argument is the **entry radius** and
`TransferConic.EntryRadiusM` reports it; the periapsis burn is unchanged by it at
one entry speed. Direct chain tests pin the fall: Uranus at 5 km/s gives
117.1323 days from the sphere and 0.02153 days (1,860 s) from the 38,643.0 km
floor, and 50 km/s at the floor gives 0.00623 days (538 s); the capture is
6.3691 km/s at 5 km/s and 39.0952 at 50 km/s, at either radius.

**Measurement sampling.** `Measure` samples an arc **uniformly in time**, not in
the path's own parameter, and gives a conic's two ends a geometric refinement
(16 points over the outer 1% of the duration). A quintic and a fixed burn are
linear in time so the change is invisible for them; a conic is linear in true
anomaly, so a coarse anomaly grid put the whole final approach of a long transfer
into one bin and over-reported its third-body delta-v by two orders — at Uranus
1 g and 400 days the 64-sample anomaly grid reported 167 km/s for the coast and
the time-uniform grid 11.5, against a converged 0.21. The delta-v is a time
integral and the peak is an endpoint value, so both are now read from a
time-uniform grid. Any measurement of a conic's third-body correction should
converge with the sample count before it is believed.

The full-field burns have their own anchors, measured at the epoch. A burn that
crosses a sphere of influence is checked against an independent 20,000-step RK4
of the same field and thrust: an 80,000 s burn from 800 Mm inside Earth's sphere
agrees to **1.7 mm and 7 × 10⁻⁹ m/s**, and its acceleration at a sample is the
field plus the thrust exactly, while the two-body value differs by
**7.6 × 10⁻⁵ m/s²**. A short 120 s burn 4,000 km above Mars ends **0.17 m and
7.6 × 10⁻⁵ m/s** from its two-body counterpart, with the fields only
**3.3 × 10⁻⁷ m/s²** apart; two arcs over one inertial state labelled with the Sun
and with Mars follow the same path to round-off and differ locally by exactly the
two bodies' 2.4 × 10¹¹ m. In the reported plans the folded terms are
**4.4 × 10⁻⁴ m/s²** at the departure burn's midpoint and **1.2 × 10⁻⁴ m/s²** at
the brake's, and a Sun-only field moves a 90-day Mars plan by 32% of its delta-v.
A body-relative position on a full-field arc carries the catalogue's date
quantisation — about **0.2–0.9 m** at 1.5 AU — so a model-to-model comparison uses
inertial states.

The break search is measured separately because it used to dominate the profile.
At the epoch on a throwaway copy under `.cache`, Mercury at 1 g: one `Plan()` is
**1.5 s** (36.2 s in the first cut, 12.0 s after the two-tier break search) and
one `PlanAtDuration(8 d)` is **95 ms** (183 ms in the first cut). The first
selection — floor, window probes, retime — is 1.5 s at Mercury and 0.93 s at
Mars; a repeat selection at the same date/target/level/style is under 0.1 ms.
The floor search probes a fast model (fixed burns at their commanded
acceleration, the break against its own body, a 128-sample burn, a loose pinned
fixed point) and rebuilds and certifies the chosen duration with the full field;
the near-floor basin still costs about twenty probes, which is the remaining
cost in `Plan()`. The fast peak against the full field is +0.18% at 8 d Mercury,
+0.41% at 12.3 d, +4.5% on the 90-day Mars break, and +0.05% at the Mercury
floor; the 5% screen decides near the limit, so the fast number never becomes a
reported peak. The search-input work left the plans and floors alone; the
arrival entry radius moved them afterwards, and the table above carries the new
numbers. After that change a floor is a certified full-field plan, but the search
can be conservative: at Uranus 1 g it returns 34.00 days while a direct build at
33.0 days also closes, because the fast bracket's low end stops the full-field
bisection. Re-probing below a fast bracket with the full field is [step 7's
cost question](/technical/planning-performance.md) — do not widen the gate.

Those timings are the two-body-burn profile. Since the certified plan's fixed
burns integrate the summed field, the same work in a .NET harness is a **0.47 s**
90-day Mars build, **1.1 s** at Mercury 8 d, and a **2.0–2.4 s** `Plan()` at the
floors; the editor is several times slower again, which is why the suite is about
nine minutes ([full-field burns](/simulation/powered-leg-floor.md)). The burns
were slower; the floors moved only when the arrival entry radius did.

The slider's retime runs off the main thread. On the same throwaway copy at the
epoch, a synchronous `SetPreviewDuration` took 120 ms at 4.5 d and 97 ms at
7.35 d to Mercury at 1 g, and 97–125 ms to Mars at 0.3 g over 9–15 d. With the
full-field burns a retime is a **0.7–1.4 s** build in a .NET harness and a few
seconds in the editor, so the latest-wins worker is what keeps the frame free.
Request-to-publish on the background worker was the same build plus **2–4 ms**
of handover: 99–124 ms at Mercury and 100–128 ms at Mars. The worker's plan
equalled the synchronous plan exactly — flight time, arrival date, peak thrust,
delta-v, clearance, and arc count. Twenty requests at 10 ms intervals (a 200 ms
drag) published only the newest plan, 311 ms after the first request at Mercury
and 324 ms at Mars; superseded builds were dropped, nothing queued, and the drag
loop kept its 10 ms cadence because the main thread only enqueues and pumps. The
first selection (floor and window) on this copy was 1.78 s at Mercury and 0.94 s
at Mars, unchanged by the retime work. The two-tier boundary, the 5% screen and
the worker's lifecycle are in
[Planning performance](/technical/planning-performance.md).

The departure burn's delta-v is level-independent (it spends the coast's
departure speed above escape); its duration is `1/level`, so 13.4 km/s is
7.7 hours at 0.05 g, 76 minutes at 0.3 g, and 23 minutes at 1 g. A crossing whose
coast arrives slower than the target's frame would need an acceleration rather
than a brake (the branch is still in the solver, but after the floor entry a
200-day Mars crossing closes at 12.46 km/s instead, and the slow end is the
coast's own geometry: 305 days is refused at 179.4°); a trip time whose transfer
sweeps 180° or more is rejected with the transfer angle. A plan over the drive carries an
`InfeasibleReason` naming the arc and the peak it needs. The 90-day anchors are
direct `PlanAtDuration` builds: the nav slider's Mars window is 6.74–20.21
days, so 90 days is not reachable from the control. They are measured
at the exact epoch: the long-trip solver is date-fragile, and a one-second shift
can drop the Mars 90-day or Jupiter 230.6-day build into the same non-convergence
hole as the known 60-day Mercury case.

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
5. Give the class a `[Category]` from the list above, so focused runs include it.
6. Do not write files under `Assets` from a test: the test project's `Assets` is
   a symlink to the real folder.
7. Run `tools/check.sh` and report the command and result.

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
* Fuel accounting, attitude dynamics, ring/atmosphere arrival models, and a
  fixed-thrust *spiral* cruise. The Lambert coast, the prograde departure burn,
  and the brake are covered; a slow continuous spiral that does not coast on a
  conic is not.
* Continuous integration. The repository is not under version control yet;
  when it is, `tools/check.sh` is the single CI entry point.
