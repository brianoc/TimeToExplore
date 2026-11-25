---
type: Reference
title: Testing and verification
description: The automated checks agents and humans run before calling work done.
tags: [technical, testing, unity, verification]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-24T22:06:00Z }
---

# One command

```bash
tools/check.sh                    # everything, including both Unity suites
tools/check.sh --fast             # docs, generators, and tooling (a few seconds)
tools/check.sh --tests flight     # the fast gates plus one test category
tools/check.sh --tests playmode   # the PlayMode suite only
tools/check.sh --tests core,smoke # any comma-separated categories
tools/check.sh --reset            # drop the warm test project first
```

The route benchmark is a **separate command**, not a gate:

```bash
tools/bench.sh                    # plot, retime and Begin cost, with deltas
tools/bench.sh --record           # replace the committed baseline
```

It is an editor `-executeMethod` entry point, so `check.sh`'s `-runTests` never
starts it, and running it never fails the build. See
[Planning performance](/technical/planning-performance.md#the-route-benchmark-2026-11-24)
for the numbers and the warm-up rule.

`check.sh` runs nine gates in order:

| Gate | Tool | What it proves |
|---|---|---|
| Design bundle | `tools/validate_okf.py` | OKF v0.2 structure, frontmatter, links, index coverage |
| Catalogue schema | `tools/validate_catalogue.py` | Field types, ids, parents, counts, orbit ranges, epoch maths |
| Trade catalogue | `tools/validate_trade.py` | Commodity ids, positive size, market ids that resolve to a body, unique offers, `buyPrice >= sellPrice >= 0`, and a market for every commodity |
| Catalogue generator | `tools/generate_solar_system.py --offline --output …` | The committed JSON is byte-identical to a fresh generation |
| Sky generator | `tools/generate_skybox.py --out …` | The committed `milky_way.png` is pixel-identical to a fresh generation |
| Bright-star generator | `tools/generate_bright_stars.py --out …` | The committed `bright_stars.json` is byte-identical to a fresh generation |
| Tooling | `tools/test_unity_guard.py` | The guard stops a stuck run and passes through a real exit code |
| Runtime (EditMode) | Unity EditMode suite | Kepler maths, catalogue loading, model propagation, clock, the scene's frame loop |
| Player loop (PlayMode) | Unity PlayMode suite | The runtime-built bootstrap, renderer, trajectory line, and HUD through Unity's own frames |

The EditMode tests carry one category each: `core`, `flight`, `presentation`, or
`smoke`; the PlayMode suite carries `playmode`. `--tests` narrows the Unity gate
to the categories named; the fast gates still run. A `core` or `presentation`
run is a few seconds and the flight category is the bulk of a full run, which
takes about **three minutes** on the warm project (187 s measured on the
first-plot branch). The burn–coast–burn plan is still the expensive part — its
fixed burns integrate the summed field — but a certified build is no longer
fifteen seconds in the editor. The PlayMode suite is separate and takes about
**half a minute** on the warm project. A bare `tools/check.sh` runs both
platforms; the PlayMode invocation still names the `playmode` category, so a
file in that assembly with another category alone does not join a bare run.
`--tests` with only EditMode categories skips the PlayMode launch.

It picks the editor named in `ProjectVersion.txt`; override with `UNITY_BIN`.
Python defaults to `.venv/bin/python`, then `python3`; override with `PYTHON`.

The Unity suite runs in `.cache/test-project`, not in a throwaway copy. `Assets`
is a symlink to the real folder, `Packages` and `ProjectSettings` are refreshed
copies, and the project keeps its own `Library`. The package import is paid once
instead of on every run; `--reset` drops the cache and rebuilds it, which costs
about half a minute on the first run. The cache is gitignored and never contains
edited sources.

Unity runs under `tools/unity_guard.py`, which stops it when it writes nothing
to the log for its stall window, exceeds its time budget, repeats a Unity
cloud-endpoint lookup, or starts shutting down and does not finish. Shutdown
hangs are the known failure: batch Unity can finish the tests, write
`results-<platform>.xml`, and then retry DNS lookups forever. `check.sh` still
reads the results that were written, so a hang at the end does not throw away a
completed run. Logs are `.cache/test-project/unity-editmode.log` and
`unity-playmode.log`. The EditMode stall window is **thirty minutes** because
Unity writes no per-test progress to the log: the whole run is one silent
stretch, and the flight category alone is about 1,400 s. The PlayMode window is
**fifteen minutes**. Their time budgets are forty and twenty minutes. The
windows are deliberately generous rather than tight; the budget still stops a
real hang.

For any other batch run, such as a capture helper or a scene builder, use the
same wrapper:

```bash
python3 tools/unity_guard.py --log /tmp/unity.log -- "$UNITY" …
```

The sky capture is the exception to the `-nographics` rule: `CaptureSky` writes a
bloom pair through HDR and post-processing, which needs the graphics device, so
its command (in [The sky](/technical/sky.md#verification)) deliberately omits
the flag and runs as its own Unity invocation rather than through this suite.

A full run takes about three minutes for EditMode plus half a minute for
PlayMode with the warm project (187 s measured on the first-plot branch); the
`flight` category is the bulk of the EditMode time. `--fast` is the loop to use
while editing docs, Python tools, or catalogue data. The Unity suites are the
gate for any C# change.

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
| `TradeCatalogueTests` | shipped trade JSON: 4 commodities, 8 markets, Earth and Mars as planet markets, every offer resolving, misses, and every rejection rule (duplicate commodity or market, unknown commodity, duplicate offer, zero volume or mass, a negative price, and a sell above the buy) |
| `SolarSystemModelTests` | load, parent-first order, Sun at rest, epoch distances and speeds, Moon frame, stations, orbit sampling |
| `SimulationClockTests` | warp arithmetic, pause, clamping, labels |
| `SelectionAndFocusTests` | details rows and physics, screen-space picking, the star→planet→station→moon→asteroid rank, the ordered stack and its depth tie, the repeated-click cycle, smoothstep/slerp transition, approach offset |
| `SceneSmokeTests` | the shipped scene resolves the bootstrap; the catalogue resource imports; its camera carries `UniversalAdditionalCameraData` with post-processing on (the bloom) and depth/colour copies off, and a black solid-colour clear (the serialized scene; the bootstrap switches the live camera to `Skybox`) |
| `RenderingPipelineTests` | the graphics default is a URP asset; the shipped pipeline and renderer assets are wired together; the renderer carries URP's `PostProcessData`; HDR is on in the 32-bit R11G11B10 buffer and the rest of the cheap settings hold (MSAA 1, scale 1, no depth/opaque copy, no shadows); the default volume profile is the one URP registers, with Bloom over the 1.0 threshold and Neutral Tonemapping; quality levels do not override the default; the body, unlit, and line shaders are found under `Resources`, have no compile errors, declare the URP pipeline and forward-pass tags, and expose the properties the renderer writes; `material.color` still works; `RenderingShaders.ForBody` picks the Sun shader |
| `SkyboxTests` | the committed `Sky/milky_way` texture is 2048 × 1024 with no mip chain, is dark overall and has a bright band covering part of the sky, and the skybox shader is found under `Resources` with no compile errors; `SkyView` points `RenderSettings.skybox` at its material and clears the camera to `Skybox`, `Remove` restores the previous skybox and clear flags and releases the runtime star map while leaving the band asset alone, and a missing texture or shader is refused without half-applying; the bright-star catalogue is every entry inside the V 4.0 cut with unit directions and Sirius at −1.46, the map it builds is 1024 × 512 `RGBA32` with point filtering, no mips, repeat/clamp wrapping and one occupied cell per star, Sirius lands within half a texel of its own direction, the brighter of two stars wins a shared cell, the magnitude and colour codes are monotonic with zero reserved for empty, and the shader's cell count is pinned to `StarCatalogue`'s constants by a source scan |
| `ShipDynamicsTests` | two-body speeds, energy, eccentricity, flight-path angle, thrust clamping |
| `GravityFieldTests` | Newton at 1 AU, Mars dominance, pruning versus the full sum, surface clamp, no mutation of the model, pure versus frame state |
| `PlanGeometryTests` | quintic endpoint conditions and peak factors, parking radius and its gravity ceiling across the catalogue, circular/equatorial/prograde orbits, SOI radii |
| `ShipIntegratorTests` | orbit stability, Kepler agreement, kinematic thrust, the brachistochrone table, step-size independence, collision, step cap |
| `TripPlannerTests` | the profile's arc chain and phases, exact arrival on the conic parking orbit, seam continuity, flight-time envelope, Venus, Jupiter on the conic radius, the Moon as one inside-sphere capture, LEO departure at 1 g as the new short-break case and its 0.3 g refusal at the 2.942 m/s² level, determinism, no mutation, the default conic capture, the fixed-thrust style's 0.3 g level and its clamp to 0.05–1 g, an unknown target refused, and pinned durations: exact flight time at twice the minimum with a gentler burn and less delta-v, a conic plan at 1.5× the fastest time landing exactly there, an infeasible report at 95%, and rejection of a non-positive or non-finite request |
| `StationTripPlannerTests` | a station is a point target: the hold state (arrival state and the last arc inside 1 m and 0.001 m/s), no approach, parking-orbit or capture-chain arc and no arc about the station, the heliocentric starts (first arc a finite burn on the ship's own state, no Departure/Escape), station-to-station, the fastest station route, the unchanged planet anchor, and a station accepted where a genuinely massless non-station is refused |
| `FlightExecutionTests` | plan, fly, and arrive at Mars and Jupiter in stable orbits; feedback from a perturbed start; the Mars→Earth return at the epoch is refused because its short arc is retrograde and the profile has only the prograde branch; the orrery is untouched |
| `FlightSessionTests` | ship ownership, the Go To plan from Gateway, exact arrival through the session, budget clamping, cancel, teleport, a flight through a body, no mutation, the conic capture applying its burns and arriving matched, the hybrid capture stepping through one finite burn and the trim, the session arrival style reaching the planner, a style switch rebuilding the preview at the pinned date while keeping a flight time that fits the new window and leaving the ship still, the station cases (`PlaceAt` at every station, the feasible hold preview at 500 m astern with the station's velocity, and a full Ares Junction → Gateway flight that reaches the tracked hold point at 0.0254 m / 7.7 × 10⁻⁵ m/s with no parking capture), the preview (never moves the ship, one-at-a-time, Begin consumes and replans, Cancel clears, the 1% staleness window, the `[minimum, 3 × minimum]` trip-time window and its clamp, retiming without moving the ship, and Begin keeping the chosen time), the preview-window cache (a repeat plot at the same date returns the same plan, another target misses, a burn-level change misses, and a changed departure date invalidates the cache), the background retime (the newest of two requests made while the first builds is published and the older build is dropped, the worker's plan equals the synchronous plan, the previous preview stays published until the new one is ready, a synchronous retime supersedes an outstanding ask, a discard drops an outstanding request, and Dispose joins the worker and refuses new work), the background preview planner (the async plot and the async style rebuild publish through `TryApplyCompletedPreview`, `APlotAsyncPublishesTheFastestFeasiblePlan` matches the synchronous `Plot` through the shared window cache, the newest async plot wins, a repeat plot to the same target continues the build it finds rather than restarting it, a failed async plot reports and publishes nothing, and a disposed session refuses new work), and the idle coast that keeps the ship moving with the clock |
| `WarpPolicyTests` | clearance, local-dynamics, and plan-event caps, the two station caps (the quarter-rule clearance to the station's surface, the 5% hold-point cap and its release on completion), the 1 s floor, the non-binding far case, scale rounding, the suggested Go To warp |
| `PlanPathTests` | arc boundary walking, a straight arc staying one chord, every adaptive chord on the Mars plan meeting the sagitta tolerance with exact endpoints, the conic coast's flatness and dense periapsis sampling, the periapsis and circularisation as plan boundaries, the hybrid arcs meeting the same flatness tolerance, and one finite burn with the trim and circularisation as the two impulses |
| `RouteColourTests` | exactly three path types, one distinct colour each, solid for a quintic or fixed burn and dashed for a conic, distinct same-hue shades inside a run of one type, and the real profile plans: the escape conic and the conic capture's runs around the departure burn and brake, and the hybrid capture's new conic run after its capture burn |
| `FlightReportTests` | the phase table, live rows, arrival rows, preview and stale rows, the `Fuel` row, a re-timed preview's flight time, later arrival and lower fuel, the profile's break/escape/coast/approach/departure-burn/brake/capture rows with no torch peak, the hybrid capture burn, a station's **Match** phase and drive rows with no capture-chain row, the **Arrived, at the hold point** status and the **Match** next event, status labels, countdown formatting |
| `FuelModelTests` | the rocket equation at the design exhaust velocity (1,000 t dry and a delta-v of 10,000 km/s needs 1,718.28 t), the mass ratio and propellant fraction, propellant rising faster than delta-v, a plan's readout matching its own delta-v and dry mass, and the burn level barely moving the fuel at a fixed trip time (30 days to Mars at 0.3 g against 1 g) |
| `KeplerPropagatorTests` | universal-variable propagation against the elements path on circular and eccentric ellipses, the hyperbolic periapsis and its analytic speed, forward/backward round trip, energy and angular momentum over 40 steps, state ↔ conic elements, a circular conic arc, its two-body reference acceleration, and the parabolic branch: Barker's standard `½ √(p³/μ)` time against universal-variable propagation at 2, 10 and 100 periapsis radii, and the inverse anomaly |
| `LambertSolverTests` | the zero-rev prograde short-way solve: propagating `v₁` onto `r₂`, a 90° circular quarter turn, a Hohmann-like transfer against the vis-viva speeds, a high-energy hyperbolic transfer, the short-way branch returned while the long way is refused, the 180° guard returning no solution instead of throwing, degenerate arguments throwing, and an epoch Earth→Mars coast landing at Mars |
| `TransferConicTests` | the entry radius (the sphere of influence or the handover floor), an inbound coast to a low periapsis, the fall shortening as the entry radius comes in (Uranus 117.1323 d from the sphere against 0.02153 d from the 38,643.0 km floor) and as the entry speed rises (0.00623 d at 50 km/s), the periapsis capture burn being set by the entry speed and not the entry radius (6.3691 km/s at 5 km/s from either radius, 39.0952 at 50 km/s), the capture plane and prograde direction, the retrograde periapsis and prograde apoapsis burns, the conic parking radius below the gravity ceiling, and invalid geometries reported rather than built |
| `FiniteBurnTests` | a constant-magnitude arc starting exactly at its state, accelerating at the fixed level opposite the velocity, the time mapping being the identity, matching the standalone RK4 simulation, the radius-crossing solver matching a post-hoc simulation, and meeting the plan's sagitta tolerance; a full-field burn crossing the sphere of influence matching an independent 20,000-step RK4 of the same field and thrust, its acceleration at a sample being the field plus the thrust rather than the two-body value, a short local burn agreeing with the two-body burn, and the reference body being a label (one inertial path, two local decompositions differing by exactly the two bodies' states); the hybrid capture solver on Venus, Earth, Mars, Jupiter, Saturn, and the Moon with a safe injection radius, the trim landing on the exact transfer ellipse, the circularisation leaving a circular parking orbit, a stronger burn leaving a smaller trim, and the conic parking radius at 0.3, 0.5, and 1.0 g |
| `HandoverBandTests` | the handover band the fixed burns hand over inside: the floor scaling as `1/sqrt(a)` when halved (Earth and Jupiter unclamped, and Mercury's gravity floor under a clamped band), the ceiling being exactly `1.02 × SOI` per body and moving per body (Mercury 114,658.2 km against Uranus 52,795,223.9 km), the departure endpoint being the ceiling and the arrival endpoint the floor, the clamped floor sitting at or above the surface clearance on every planet, moon, and asteroid at 0.3 g and 1 g (Mercury and Mars at 1 g are the named cases, their gravity floors below the surface), the exactly three catalogue bodies whose band is empty at every level (Phobos, Deimos, Proteus) reported with the floor, the clamp, and the ceiling in the reason and refused by the planner before a chain is built, and the body-clearance gate accepting a 1 g burn that crosses Earth's sphere while clearing the planet by 821,258 km and refusing a 30 s burn 60 km up that grazes the surface |
| `BurnCoastBurnTripPlannerTests` | the nine-arc profile — short quintic break, ballistic escape conic, prograde finite departure burn, Lambert coast, planetocentric approach, brake, conic capture — with the break ending at the parking radius on the ship's own prograde tangent at escape speed and the escape conic reaching the 1.02 × SOI ceiling (Mercury 14.1 d at 1 g), the break peaking at the commanded level, the break duration not moving with trip time (Mars 30/40/50 d), the departure burn sized from the velocity change rather than a time share, the handover flown rather than impulsive, the capture chain entering at the arrival handover floor (Uranus 1 g 400 d: 38,643.0 km, a 1,860.2 s fall, a 6.369 km/s periapsis burn, every seam inside 1 km and 1 m/s), the coast ending on the target's handover ceiling (52,795,223.9 km at Uranus, within 10 m) with the planetocentric approach falling from it (7.477 d, 52.8 million km → 374,027 km, no burn, periapsis 84,719 km above the floor) and the short Mercury trip keeping eight arcs because its brake starts outside the ceiling, the brake spending the rest of the handover, continuous position and velocity at every seam, every fixed burn clearing the body it works near, a flown arrival on the conic parking orbit and its three-orbit hold, a slow crossing closing cheaply (200 days to Mars at 11.21 km/s with a 27.2-hour approach) with the slow end set by the 175.6° coast, a time without a prograde short transfer rejected with its 180° reason, and the hybrid arrival keeping all three burns at the level; the two-tier break search — the fast departure-body-only peak against the full-field plan peak on Mercury 14.1/16 d and Mars 20/90 d, the reported break peak at the commanded level, the 5% screen certifying a near-limit fast peak with the full field and never accepting a full-field over-thrust break, and the just-below floors still rejected; every burn in a reported plan integrating the summed field (its acceleration is the field plus the thrust, the non-Sun term is there at each burn, and a Sun-only field moves the plan), the hybrid capture's injection burn included; the floor search returning a real full-field chain inside the drive; the six re-measured fixed-duration anchors (Mars 0.3 g 90 d 38.9678, Uranus 1 g 400 d 183.9153, Mars 0.3 g 20 d 495.9818, Mars 0.3 g 18 d 706.0906, Mercury 1 g 16 d 869.0568, Uranus 1 g 60 d 1,329.6508) and the three old sub-floor anchors refused; the departure-burn-to-coast seam on Uranus 60 d at 1 g; the floor plan at Uranus/Mars/Jupiter 0.3 g being certified against a probe; the Mars 0.3 g floor; the burn-only minima and the short-coast gap at Mercury 1 g, Mars 0.3 g and Jupiter 0.3 g; the propellant ordering (the fast end spends more); the Mars 300 km orbit departure to Earth and the Moon at 60/90/120 d (short quintic break, ballistic Mars-centred escape, the measured 81.045/49.940/35.823 and 83.166/49.644/34.520 km/s bands, the monotone 120 < 90 < 60 d trade, exact arrival and closed seams); and the impulsive-escape sanity (the break's spend is above `v_esc − v_circ` and within 4× of it — 2.02× from the Mars 300 km orbit and 2.98× from Gateway — and never hundreds of km/s) |
| `OrbitCameraTests` | pitch clamp, yaw wrap, zoom limits, shortest turn, slew rate, distance easing, a zoom request that eases over many frames and lands exactly, placing a distance cancelling a pending request, a zero-length frame completing the zoom, the near-clip rule, and the home transition (one second of smoothstep, exact endpoints, a zero-duration landing, and cancellation) |
| `RouteFramingTests` | the bounding frame containing every sampled route point and the arrival ghost, the projected fit and its 12% margin being tighter than the bounding sphere, the route plane's normal and its 18° tilt, a straight route seen side-on rather than along it, the shortest-turn/log-distance transition's midpoints and exact endpoints, the rig easing the whole route on screen and landing the look-at on its centre, a wheel, drag, or Space cancelling the move, and a degenerate route leaving the view alone |
| `CameraRigTests` | the auto-framing distance at arrival and in cruise, the clamps, the follow flags, the captured-once framing distance and its arrival re-capture, the slew-rate turn when the framing target changes, continuous body avoidance, the shared 3 px line width staying a sliver of the distance, the zoom-out-and-back pose round trip, the wheel tween and the 12-billion-km ceiling, the cursor anchor's projected NDC on every frame of a real off-centre zoom, the anchor surviving after the tween, Space's one-second home with the remembered angle, a wheel or drag cancelling it, the button routing (right-drag orbits; a panel press starts nothing; a world press keeps its gesture across a panel), and the near plane |
| `ThrustVectorTests` | the thrust arrow points along the commanded acceleration rather than the velocity, hides at zero and below-threshold thrust, keeps a fixed apparent length as the camera distance doubles, and hides and returns with the ship marker |
| `DrawingRulesTests` | the 4.5 px marker minimum and true-scale override, the viewport angular conversion, the Sun/planet/station always-on labels, the 2-million-km moon and 200,000-km asteroid windows, the selection always labelling, the 3 px line width at every zoom, proportional to camera distance and resolution-independent; the bounded width: the far arc holding 2 px, the near arc capped at the line's own look-at width, a vertex at or behind the camera having no width, a thinner line not being inflated, both real line renderers carrying a width key per vertex, a zoomed-in curved route staying between 2 and 3 px, and the camera-plane clip keeping the near-plane crossing and the longest of two visible runs; and Mercury warm, Neptune cool, eight distinct planet colours in orbital order |
| `SceneFlightTests` | the scene's frame loop: clock, warp cap, session advance, clamp; the full Gateway→Mars acceptance flight and three orbits |
| `BootstrapSmokeTests` | the real composition root: `Awake` builds the world and the camera rig, Go To wires the flight and the trajectory line, cancel and reset behave, the debug jump is refused in flight, a plot draws the preview and arrival ghost without flying and eases the camera to the whole route, the route draws one coloured line per arc with same-type shades and the arrival-style switch redraws the conic and hybrid palettes, the dash period holds across dashed arcs of different lengths, the 64-step trip-time slider requests a retime through the real HUD, holds the handle and the previous full-field preview while the plan lags, publishes the finished plan on the main thread without moving the ship, Begin replans at the current date, the clock runs while idle so a preview can age, discard leaves the ship alone, one wheel gesture through the rig reaches the 12-billion-km ceiling and returns to the exact remembered angle, one wheel gesture eases through the frame loop instead of snapping, the Mars acceptance — select, plot, Begin — arrives on the parking orbit and re-captures the arrival framing distance through the bootstrap's own frame loop, the profile acceptance — plot, Begin, and arrive on the conic parking orbit through the same frame loop — the station acceptance — Gateway from Ares Junction plots, draws its arcs and arrival ghost, and begins the flight — the composition root owning the loaded trade session (four commodities, eight markets, a 40 m³ empty hold), plus the frame loop dropping a frame until the model, clock, and renderer exist and skipping a body view destroyed mid-teardown |
| `InputMappingTests` | the explicit digit table (`1`–`9` → indices 0–8, `0` → 9), Space as the camera home and `P` as pause, every other hotkey toggle, the highest-digit rule, `WASDQE` axes, Shift ×10 and Ctrl ×0.1, comma/period speed steps, the legacy 0.1 mouse sensitivity, macOS ≈ 1 and Windows 120 → 1 scroll normalisation, the screen-to-picking pointer flip, the 4 px click slop that separates a click from a drag, and null devices returning neutral values |
| `InputSeamTests` | a source scan of `Assets/Scripts`: no legacy `UnityEngine.Input` call outside `PlayerControls`, the Input System named by exactly two runtime files (`PlayerControls` and `UiInput`), the seam polling `Keyboard.current`/`Mouse.current` with no `InputActionAsset`, the UI module assigning the default actions without double-equipping an EventSystem, no `OnGUI`/`GUI.*`/`GUIStyle`/`Texture2D.whiteTexture` in the runtime, and `activeInputHandler: 1` pinned |
| `InputDeviceTests` | a virtual keyboard and mouse through `InputTestFixture`: a device-free frame is neutral, key edges feed the hotkey intents (Space home, `P` pause), held keys feed the movement and modifier intents, mouse buttons feed click press/hold/release and right-drag begin/end, and device deltas and scroll pass through the 0.1 and normalisation rules |
| `AssemblyLayoutTests` | the six `.asmdef` files as the source of truth: Core has no references, Simulation references only Core and opts out of UnityEngine references, Presentation references Core + Simulation + the Input System + Unity UI, Editor is Editor-only, the EditMode suite is Editor-only and the PlayMode suite is all-platform, both test suites reference the three runtime assemblies, no assembly references a later layer, every project reference names a known assembly, exactly one asmdef per layer with no stray under `Player`/`Rendering`/`UI`, unique GUIDs, and the compiled assembly names of representative types |
| `SolarSystemHudTests` | the runtime uGUI HUD: one Screen Space Overlay Canvas with the 1920×1080 scaler and a GraphicRaycaster, exactly one EventSystem with an `InputSystemUIInputModule`, no legacy module, and navigation events off so Space/Enter stay gameplay keys, the status text tracking the clock/ship/selection, the details panel opening for Mars with every `BodyDetails` row and closing, the flight panel for a preview and a live plan, the route legend appearing with the profile's *Quintic arc*, *Conic arc*, and *Fixed burn* rows (the escape conic comes before the departure burn), collapsing its conic arcs into one row, stacking above the flight panel, staying non-raycast, hiding with `ShowHud`, and hiding when the route is discarded, the trip-time slider appearing with a preview as **64 whole steps** from the fastest plan, and requesting a retime through its `onValueChanged` while the handle and label keep the player's ask and the finished plan is published on the main thread, the two arrival-style buttons marking the active capture and rebuilding the preview against the chosen style (the trip-time window follows, and switching back restores the conic capture), the `0.3 g burn` button rebuilding the preview with the hybrid finite burn and its impulsive circularisation, the burn-level slider running from 0.05 to 1 g and rebuilding the preview at 0.5 g with the minimum clamped, the profile's own 16.84–50.53 day window and its report rows through the shared level control, the station details screen plotting and beginning Gateway from Ares Junction through the real buttons (its plan shows and its status line reads `at the hold point` on arrival), the details Trade button visible but disabled before arrival and live at the hold point, the market panel opening for Gateway with its four rows and `Trade · Gateway Station`, `+1` adding a water unit and moving the cargo line, `+max` filling the hold to 40 m³, `sell all` returning it to zero, the button hidden for a body with no market (the Moon), `ShowHud`/`ShowLabels`, the one-frame opening guard, the route/begin/jump/close `onClick` handlers reaching the same owner methods, selecting a body starting its plan (`Planning route to …` on the button) and publishing it without framing the camera while the Plot click frames the fresh plan instead of rebuilding it, a Plot click during the wait continuing the selection's plan, and pointer picking skipping a view destroyed mid-teardown |
| `TradeReportTests` | the trade panel's pure text: Gateway's four offers and Helios Watch's three in catalogue order with their prices, an empty list for the Moon, the Sun, an unknown id, and a missing catalogue, the per-unit volume and mass formats without trailing zeros, and the cargo line for an empty and a partly filled hold |

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

# Unity PlayMode suite

The PlayMode suite lives in `Assets/Tests/PlayMode/` behind the all-platform
`TimeToExplore.Tests.PlayMode` assembly and carries the `playmode` category. It
does not build the composition root by hand: entering play mode runs
`SolarSystemBootstrap.AutoCreate`, and the test methods only `yield return null`
into Unity's player loop. Unity calls `Awake`, `Update`, and `LateUpdate`; the
HUD is the runtime-built uGUI canvas; the renderer and trajectory line are the
same `MonoBehaviour`s the player sees. `check.sh` runs it as a second Unity
invocation (`-testPlatform PlayMode`) with its own log and results file.

| File | Covers |
|---|---|
| `BootstrapPlayModeTests` | `RuntimeInitializeOnLoad` creates the `Time to Explore` root; the renderer builds one view per renderable body; every body view and every visible orbit `LineRenderer` carries the frame's model state and `ShowOrbits` reaches the real lines on the next frame; the HUD canvas is live and its status clock is rewritten from the frame clock; a frame probe sees the model advanced in `Update` while the body views still hold the previous frame, then matching in `LateUpdate`, with the clock held between the phases and exactly one step per frame; the real details panel's route button passes its one-frame opening guard, plots through the async planner and the HUD's per-frame pump, and the flight panel and route legend appear; every arc is an enabled `LineRenderer` with positions, one width key per vertex, the arc's own colour, and most of the framed route inside the camera's view; the ghost marks Mars at the arrival date; the 64-step trip-time slider requests a retime through its real `onValueChanged` and the HUD publishes it; `ShowHud` and `ShowLabels` take effect on the following frame; Begin through the real button starts the flight, the clock clamp holds across frames, and Cancel clears the route and the panel |
| `FrameOrderProbe` | a helper that runs after the bootstrap in both phases, so the test can read what `Update` produced before `LateUpdate` drew it |
| `LateUpdateProbe` | a helper that runs a callback at the end of a frame, because a `[UnityTest]` coroutine resumes in the `Update` phase and cannot otherwise read state drawn later in that frame |

The deterministic step comes from the same `DeltaTimeSeconds` seam: the PlayMode
setup supplies one second per frame at 1×. That keeps a preview, its retime, and
the HUD assertions to about half a minute while still using Unity's real frame
order, real `Time.frameCount`, real component lifecycle, and real `LineRenderer`
and `Text` objects. Nothing in the suite calls `Update` or `LateUpdate` by
reflection.

The suite deliberately stops at component state and projected screen positions.
`-nographics` has no rendered pixels to read, and no synthetic pointer is sent
through the `EventSystem`; the real button `UnityEvent`s are invoked after the
HUD makes them interactable. The rendered frame, pointer hit areas, and canvas
scaling away from 1080p remain human checks.

## The captures

`TradeCaptureTests` carries only the `capture` category, so neither the default
`check.sh` run nor `--tests playmode` starts it. It flies the cheap Ares
Junction → Gateway route through the real session, opens the details screen and
the market through their real buttons, buys a few units, and writes a 1600×900
`trade-panel.png`. `RoutePrimeCaptureTests` records a numbered frame sequence
instead: an idle ship, the selection starting Mars's plan, the published route
that leaves the camera alone, and the camera move the Plot click then asks for.
Its frames land in `route-prime-frames/` beside `trade-panel.png` and are
assembled into a clip by hand. Run either by hand without `-nographics`,
because the capture needs a graphics device:

```bash
python3 tools/unity_guard.py --log /tmp/unity-capture.log --timeout-seconds 900 \
  -- "$UNITY" -batchmode -projectPath .cache/test-project/TimeToExplore \
  -runTests -testPlatform PlayMode -testCategory capture \
  -testResults /tmp/results-capture.xml
```

`ScreenCapture.CaptureScreenshot` writes nothing under `-batchmode`, so after
one frame the test falls back to drawing the live canvas through the view
camera into a `Texture2D`. The path is `Path.GetFullPath("trade-panel.png")`
relative to Unity's working directory, which is the test project: the image
lands at `.cache/test-project/TimeToExplore/trade-panel.png` and is copied out
by hand. It is evidence for the session, not a checked-in asset.

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
| Gateway to Mars plan (coast floor, 0.3 g) | 16.84485 days, 1,017.1 km/s, 2.95 m/s² peak |
| Gateway to Mercury plan (coast floor, 1 g) | 14.06070 days, 1,821.1 km/s, 9.70 m/s² peak |
| Gateway to Jupiter plan (coast floor, 0.3 g) | 23.73299 days, 1,899.5 km/s, 2.95 m/s² peak |
| Mars parking orbit (conic) | 3,898 km radius, 3.315 km/s |
| Jupiter parking orbit (gravity ceiling, quintic inside-sphere capture) | 177,965 km radius, 4.0 m/s², 26.7 km/s |
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
| Gateway→Mars arrival | 2026-12-01 11:40:50 UTC, 16.49 days after departure |
| Arrival error through the frame loop | 0.1 m, 0.0001 m/s |
| Three orbits after arrival | 3,898 km, eccentricity 3.12 × 10⁻⁶, drive off |
| Real time | 202.0 s over 12,118 frames |
| Peak substeps in one frame | 14 of the 2,000 budget |
| Warp | x10,000 → x10, auto-limited |
| Deep-space frame cap | 328,932 s (above 1,000,000×) |
| Mars parking-orbit frame cap | 23.5 s (1,000×) |
| Mid-cruise plan-event cap | 10,061 s (100,000×) |
| Starved budget, 2 substeps/frame, 400 frames | reaches 2026-11-15 00:15, clock never ahead of the ship |

`BootstrapSmokeTests` repeats the same flight through the real composition
root: 12,118 frames, 202.0 s, arrival 2026-12-01 11:40:50 UTC, 0.1 m and
0.0001 m/s, 3,898 km and e = 1.95 × 10⁻⁹ at arrival. The shorter
`AProfileFlightArrivesThroughTheBootstrap` run (4,922 frames, 82.0 s) uses the
bootstrap's larger default frame step and reaches the same state at
e = 2.19 × 10⁻⁹. The identical counts are
the point: the camera is a pose, so the flight path does not depend on how the
player found the target.

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
0.3 g with the conic capture (floors and windows are the editor's, the runtime
that ships; the .NET harness can select a different certified point near the
giants' floor — see [planning
performance](/technical/planning-performance.md#the-runtime-divergence-is-a-property-not-a-bug)):

| Quantity | Value |
|---|---|
| Trip-time window (Mars) | 16.84–50.53 days on the coast path |
| Trip-time window (Mercury) | 14.06–34.27 days on the coast path |
| Trip-time window (Uranus) | 57.02–171.07 days at 1 g, 92.13–276.39 days at 0.3 g |
| Trip-time window (Jupiter) | 23.73–71.20 days at 0.3 g on the coast path |
| Nav minimum, Mars at 0.3 g | 16.48669 days (burn-only; the coast floor is 16.84485) |
| Nav minimum, Mercury at 1 g | 13.86287 days (burn-only; the coast floor is 14.06070) |
| Nav minimum, Jupiter at 0.3 g | 22.55555 days (burn-only; the coast floor is 23.73299) |
| True floor, Mars at 0.3 g | 16.84485 days, peak 2.95 m/s², 1,017.1 km/s |
| True floor, Mercury at 1 g | 14.06070 days, peak 9.70 m/s², 1,821.1 km/s |
| True floor, Uranus at 1 g | 57.02223 days, peak 9.70 m/s², 1,417.8 km/s, nine arcs |
| True floor, Uranus at 0.3 g | 92.13088 days, peak 2.95 m/s², 811.1 km/s, nine arcs |
| True floor, Jupiter at 0.3 g | 23.73299 days, peak 2.95 m/s², 1,899.5 km/s |
| Just below the Mercury minimum | 13.8 days refused: below the 13.86287-day burn-only minimum; 8 days is refused with the departure-time reason |
| Just below the Mars minimum | 16.0 days refused: below the 16.48669-day burn-only minimum |
| 90-day Mars plan, total | 38.9678 km/s (direct build, outside the nav window) |
| 90-day break | 928 s, 1.404 km/s, 2.9467 m/s², ending at Gateway's 308,455 km radius |
| 90-day escape conic | 10.74066 days, parabolic, 308,455 → 943,144 km, 0.252 km/s third-body |
| 90-day departure burn | 79.5 min, 14.040 km/s, 2.94 m/s² |
| 90-day Lambert coast | 78.83112 days, 0.039 km/s |
| 90-day approach | 5.64 h, 588,786 → 374,027 km, a two-body conic with no burn |
| 90-day brake | 111.6 min, 19.699 km/s, 2.94 m/s² |
| 90-day capture entry | 6,033 km (the Mars 0.3 g floor), 814.8 s fall, 3.458 + 0.076 km/s |
| Mars 300 km → Earth, 60/90/120 d | 81.045 / 49.940 / 35.823 km/s |
| Mars 300 km → Moon, 60/90/120 d | 83.166 / 49.644 / 34.520 km/s |
| Fixed-duration anchors | Mars 0.3 g 90 d 38.9678; Uranus 1 g 400 d 183.9153; Mars 0.3 g 20 d 495.9818; Mars 0.3 g 18 d 706.0906; Mercury 1 g 16 d 869.0568; Uranus 1 g 60 d 1,329.6508 |
| Refused old anchors | Mercury 1 g 8 d, Mars 0.3 g 10 d and Uranus 1 g 35 d are below the floor |
| Seam error | 55.8 m and 8.6 × 10⁻² m/s at the short Mercury trip; the Mars 90-day seams are 0.0 m and 7.3 × 10⁻⁶ m/s; the gate is 1 km and 1 m/s |
| Arrival error | exact at the reference state; the flown 14.1-day Mercury flight arrives inside 10 m / 0.1 m/s |
| Parabolic time | Barker's branch matches `KeplerPropagator` to 1 m at 2, 10 and 100 periapsis radii; the old branch was √2 too long (17.002 d against 12.022 d at Mars) |

The **station call** has its own anchors, measured at the epoch at 0.3 g (the
measured values are in the 2026-11-24 update log):

| Quantity | Value |
|---|---|
| Hold point | `ComputeState(station, arrivalJd)` offset 500 m astern along the station's velocity (`TripPlanner.StationHoldDistanceM`) |
| Ares Junction → Gateway, 160 d | 12.239 km/s, three arcs: burn 6.206, coast 0.451, match 5.583 |
| Ares Junction → Gateway, fastest | 54.16210 d, 69.541 km/s, three arcs |
| Mars 300 km → Gateway, 120 d | 25.648 km/s, five arcs: break 2.854, escape 0.004, burn 8.300, coast 0.110, match 14.379 |
| Mars 300 km → Gateway, coast floor | 59.57357 d, 76.494 km/s, five arcs |
| Gateway → Ares Junction, 120 d | 24.733 km/s, five arcs: break 1.404, escape 0.252, burn 7.577, coast 0.063, match 15.437 |
| Ares Junction → Earth, 230 d | 24.091 km/s, seven arcs, a heliocentric start |
| Helios Watch → Mars, 200 d | 10.747 km/s, seven arcs, a heliocentric start |
| Station flight, tracked arrival | 0.0254 m and 7.68 × 10⁻⁵ m/s over 9,198 substeps |
| Station warp cap, 200 km astern at 100 m/s | 99.8 s per frame (the gravity field alone allows 3,709 s) |
| Station warp cap, at the hold point | the 1 s floor |
| Station warp cap, 2 × 10⁹ m away | non-binding; the event cap's 130,059 s wins |

The profile's capture chain enters at the arrival handover floor, not the
sphere of influence. The entry, the fall,
and the capture are measured at the epoch against the sphere-of-influence build.
The floor-entry column's Mars and Uranus totals match the shipping anchors; the
sphere-entry column records the earlier build that motivated the floor and is
not a plan the planner returns today.

| Quantity | Sphere entry | Floor entry |
|---|---:|---:|
| Uranus 1 g, 400 d: entry radius | 51,760,023 km | 38,643 km |
| Uranus 1 g, 400 d: inbound fall | 117.13 days | 31 min |
| Uranus 1 g, 400 d: capture Δv | 6.37 km/s | 6.37 km/s |
| Uranus 1 g, 400 d: total | 242.1 km/s | 183.9 km/s |
| Uranus 1 g, 400 d: coast end | 366,505 km (0.0071 SOI) | **52,795,224 km (1.0000 ceiling)** |
| Uranus 1 g, 400 d: approach | — | **7.477 d, 52,795,224 → 374,027 km**, periapsis 84,719 km |
| Uranus 1 g, 400 d: brake start radius | 51.96 million km | 374,027 km (0.0072 SOI) |
| Mars 90 d: coast end / approach / total | 92,790 km / — / 45.4 km/s | **588,786 km / 5.64 h / 38.97 km/s** |
| Mercury 1 g, 14.1 d: fall / total | 6.09 h / 569.9 km/s | 3.3 min / 1,762.3 km/s |
| Mars 0.3 g, 90 d: fall / total | 1.3187 d / 45.4 km/s | 13.6 min / 38.97 km/s |

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

Step 1 changed the sampling a build integrates at, not the reported answers. The
pinned departure solve's trial burns integrate at **128 samples**
(`PinnedDepartureSamples`), the reported departure burn stays at **512**, the
certified brake is **512** (`BrakeSamples`), and a fast probe keeps its own
2,048-sample brake. The coast is re-solved from the reported burn's own end so
the position seam is exact. A coarse solve that stalls retries
once at 512, which is what keeps the Uranus 35 d and 1 g anchor a plan in Mono.
Measured in a warm Release .NET harness on the previous profile, a build is
**2.9–3.9× faster** (Mars 90 d 0.16 → 0.05 s, a Mercury fixed-duration build
0.28 → 0.09, Uranus 400 d 0.18 → 0.06, Mars 20 d 0.23 → 0.06). The physical
departure adds one conic solve and a one-dimensional break bisection per pass;
those timings are not re-profiled here. The six fixed-duration delta-v anchors
are re-measured, not preserved: Mars 0.3 g 90 d 38.9678, Uranus 1 g 400 d
183.9153, Mars 0.3 g 20 d 495.9818, Mars 0.3 g 18 d 706.0906, Mercury 1 g 16 d
869.0568 and Uranus 1 g 60 d 1,329.6508 km/s. Uranus 60 d's
departure-burn-to-coast seam is inside the test's 10 m / 1 cm/s bound.

The break search is measured separately because it used to dominate the profile.
At the epoch on a throwaway copy under `.cache`, Mercury at 1 g: one `Plan()` is
**1.5 s** (36.2 s in the first cut, 12.0 s after the two-tier break search) and
one fixed-duration `PlanAtDuration` is **95 ms** (183 ms in the first cut). The first
selection — floor, window probes, retime — is 1.5 s at Mercury and 0.93 s at
Mars; a repeat selection at the same date/target/level/style is under 0.1 ms.
The floor search probes a fast model (fixed burns at their commanded
acceleration, the break against its own body, a 128-sample burn, a loose pinned
fixed point) and rebuilds and certifies the chosen duration with the full field.
Since step 3 every measured case certifies its **first** candidate, so the
common path is one certified build plus the window build and not a full-field
continuation: the .NET trace is `fast`, `probe 0`, `full 0`, `cert 1` in all
five cases, and Mars's is the same certificate with one `pin retry` (12 → 24
passes) inside its build
([step 4](/technical/planning-performance.md#step-4-the-bracket-was-not-needed-2026-11-20)).
The remaining cost is therefore the editor's per-build price: Mars and Jupiter
are ~3.0 s, and the other cases 1.3–2.3 s, in the warm test project.

The fast peak tracks the full-field peak within the test's 10% bound on the
measured cases; the 5% screen decides near the limit, so the fast number never
becomes a reported peak. The search-input work left the plans alone, and the
physical departure moved the floors; the anchors table above carries the new
numbers. A floor is a certified full-field plan, but the search can be
conservative: the near-floor basin is a set of needles, and a certified build can
close a duration just below the advertised floor while a fast probe refuses it.
The conservatism is a cost shield, not a wrong answer: do not loosen
`DepartureSolveToleranceMps` or widen the gate. Step 1's floors are **14.06 d**
(Mercury 1 g), **16.84 d** (Mars 0.3 g), **23.73 d** (Jupiter 0.3 g),
**57.02 d** (Uranus 1 g) and **92.13 d** (Uranus 0.3 g) in the editor; the .NET
harness was not re-swept. Their windows follow the three-times minimum:
**14.06–34.27**, **16.84–50.53**, **23.73–71.20**, **57.02–171.07** and
**92.13–276.39 d**. Every number is a certified plan; the runtime difference is
the near-floor scatter, not an error.

Those timings are the two-body-burn profile. Since the certified plan's fixed
burns integrate the summed field, the same work in a .NET harness before step 7
was a **0.47 s** 90-day Mars build, **1.1 s** at Mercury 14 d, and a **2.0–2.4 s**
`Plan()` at the Mars and Mercury floors; the editor is several times slower
again, which is why the flight category is about ten minutes
([full-field burns](/simulation/powered-leg-floor.md)). Step 7's per-eighth-step
active-set refresh and 1e-6 m/s pin cut the harness numbers to **0.31 s**,
**0.43 s**, and **1.0–2.5 s**, with the Uranus 1 g floor at 3.6 s instead of
71 s. The burns were slower; the Mars and Mercury floors moved only when the
arrival entry radius did, and the Uranus floor moved again when the cheaper
cadence changed the certified near-floor basin.

The slider's retime and the route planning run off the main thread. On the same
throwaway copy at the epoch, a synchronous `SetPreviewDuration` took 120 ms at
4.5 d and 97 ms at 7.35 d to Mercury at 1 g, and 97–125 ms to Mars at 0.3 g over
9–15 d. With the full-field burns a retime is a **0.7–1.4 s** build in a .NET
harness and a few seconds in the editor, so the latest-wins worker is what
keeps the frame free. Request-to-publish on the background worker was the same
build plus **2–4 ms** of handover: 99–124 ms at Mercury and 100–128 ms at Mars.
The worker's plan equalled the synchronous plan exactly — flight time, arrival
date, peak thrust, delta-v, clearance, and arc count. Twenty requests at 10 ms
intervals (a 200 ms drag) published only the newest plan, 311 ms after the
first request at Mercury and 324 ms at Mars; superseded builds were dropped,
nothing queued, and the drag loop kept its 10 ms cadence because the main
thread only enqueues and pumps. The first selection (floor and window) on this
copy was 1.78 s at Mercury and 0.94 s at Mars, unchanged by the retime work. The
two-tier boundary, the 5% screen and the worker's lifecycle are in
[Planning performance](/technical/planning-performance.md).

The first plot and the style, cruise-profile and burn-level changes are async
too: `FlightSessionTests` covers an async plot publishing the fastest feasible
plan and matching the synchronous `Plot` through the shared window cache, the
newest of two async plots winning, an async plot to an unknown target reporting
the failure and publishing nothing, a refused plot after dispose, and the
async rebuild behind a style switch (`ChangingTheArrivalStyleRebuildsThe
PreviewAndLeavesTheShipStill`); the smoke suites pump the worker until the
route publishes before asserting on the trajectory line and the HUD's route
legend.

The departure burn's delta-v is level-independent (it spends the coast's
departure speed above escape); its duration is `1/level`, so 13.4 km/s is
7.7 hours at 0.05 g, 76 minutes at 0.3 g, and 23 minutes at 1 g. A crossing whose
coast arrives slower than the target's frame would need an acceleration rather
than a brake (the branch is still in the solver, but after the floor entry and
the approach leg a 200-day Mars crossing closes at 11.21 km/s instead, and the
slow end is the coast's own geometry: 305 days is refused at 175.6°); a trip time whose transfer
sweeps 180° or more is rejected with the transfer angle. A plan over the drive carries an
`InfeasibleReason` naming the arc and the peak it needs. The 90-day anchors are
direct `PlanAtDuration` builds: the nav slider's Mars window is 16.84–50.53
days, so 90 days is not reachable from the control. They are measured
at the exact epoch: the long-trip solver is date-fragile, and a one-second shift
can drop a near-floor build into a non-convergence hole.

The camera has these anchors at the epoch: the Moon's SOI radius is 66,194 km
(the design quotes 66,200), so Gateway is inside it and the Moon is the
dominant body there; the zoom ceiling is 12,000,000 units (12 billion km, about
80 AU) and the floor is 0.002 units (2,000 km); the look-at offset cap is 60 AU,
or 8,975,872 units; the near plane is 2% of the distance clamped to 0.0002–0.05
units (200 m–50 km); and the far plane is 20,000,000 units (20 billion km,
about 133 AU).

The drawing rules have these anchors: the marker radius is 4.5 px at every
zoom; a moon labels inside 2 million km and an asteroid inside 200,000 km; the
class rank puts the Sun, planets, and stations at 1 and moons and asteroids at
0; and orbit and plan lines are 3 px through `LineWidth`, which is
`2 × depth × tan(fov/2) / viewportHeight × 3` render units at the camera
distance. Each vertex's width comes from `LineWidth.SampleWorldWidth`, which
bounds the apparent width to `[DistanceFloorPixels, lookAtPixels]`: the far half
of an orbit or route never falls below 2 px, the near half never exceeds the
line's own pixel count, and a thinner line is neither inflated nor thickened. At
60° and 1080p a 3 px line is 0.0032 × depth; at 2160p the world width halves and
the apparent width stays 3 px. The floor tests cover the far end holding 2 px,
the near end capped at the line's own pixel width, the natural narrowing between
them, a thinner line not being inflated, and resolution independence; both real
line renderers are checked for a width key per vertex, and a curved route with
the camera 800 units from its midpoint is checked to stay between 2 and 3 px with
every interior vertex in front of the camera. `LineCameraClip` has direct tests
for the interpolated near-plane crossing and for keeping the longer of two
visible runs. The planet palette is anchored on Mercury `#FFA347` (warm orange)
and Neptune `#B76BED` (cool purple), with eight distinct samples in orbital
order.

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
* The pipeline is deliberately cheap with one exception: HDR is on in URP's
  32-bit R11G11B10 buffer because the brightest sky stars write above white.
  MSAA 1, render scale 1, camera depth and opaque textures off,
  main/additional/soft shadows off, URP main and additional light rendering
  disabled, and intermediate texture mode Auto (with no renderer features,
  nothing is allocated). The renderer carries URP's `PostProcessData`,
  post-processing is on, and the default volume profile holds Bloom
  (threshold 1.0) and Neutral Tonemapping.
* `TimeToExplore/BodyLit`, `TimeToExplore/Unlit`, and `TimeToExplore/Line` all
  resolve through `Shader.Find`, live under `Assets/Resources/Shaders/`,
  declare `RenderPipeline = UniversalPipeline` and a
  `LightMode = UniversalForward` pass, and report no shader compile errors.
* `_Color` is declared by every coloured shader, so `material.color` and the
  renderer's `SetColor("_Color", …)` calls keep working; `_Ambient` is on the
  body shader and `_MainTex` is on the line shader.
* The scene's one camera carries `UniversalAdditionalCameraData` with
  post-processing on and depth texture and colour texture requests off.
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
  that is 0.3° per raw pixel of orbit drag.
* **Wheel.** Windows reports 120 units per notch, macOS about 1.
  `NormaliseScroll` divides by 120 only on `WindowsPlayer`/`WindowsEditor`, so
  one notch is one `ZoomFactorPerStep` (1.25×) step within the single 2,000 km–12 billion km range. The
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
5. Use PlayMode when the assertion depends on Unity's lifecycle or frame order:
   the auto-created bootstrap, an `Update` before `LateUpdate` handoff, the HUD
   building or pumping, or a real `LineRenderer`/`Text` object. Use EditMode for
   everything else; it is much cheaper. Supply the `DeltaTimeSeconds` seam
   rather than waiting real time, and use `FrameOrderProbe` or
   `LateUpdateProbe` to read state at the end of a frame.
6. Give the class a `[Category]` from the list above (`playmode` in the PlayMode
   suite), so focused runs include it.
7. Do not write files under `Assets` from a test: the test project's `Assets` is
   a symlink to the real folder.
8. Run `tools/check.sh` and report the command and result.

# Not covered yet

* Rendered pixels. The PlayMode suite runs under `-nographics`, so it checks the
  real renderer objects, their positions, widths, colours, and projected screen
  positions, not the framebuffer. What a frame looks like, the line shader's
  `fwidth` coverage, and the dash texture still need eyes or a capture run. The
  URP migration has the same gap: `RenderingPipelineTests` proves the pipeline
  and shaders are configured, and the PlayMode suite proves the objects are
  submitted with the right transform, but not the final image.
* A real pointer through the `EventSystem`. The PlayMode suite invokes the real
  `Button.onClick` events after the HUD makes them interactable, so the
  one-frame guard and the handler wiring are covered, but button hit areas,
  hover feedback, pointer-over-panel on the first frame, cursor capture, and
  canvas scaling away from 1080p still need a Play mode pass.
* Screen-space picking at the edges of the view and on a moving camera; the
  pure picker is tested and the PlayMode suite runs the frame loop, but no test
  delivers a real pointer to the projection step.
* Performance budgets for orbit-line updates and per-frame HUD text updates.
* A finite fuel budget and mass-dependent acceleration (the first-pass
  propellant readout exists, but it does not limit or change the ship);
  attitude dynamics, ring/atmosphere arrival models, and a fixed-thrust
  *spiral* cruise. The Lambert coast, the prograde departure burn,
  and the brake are covered; a slow continuous spiral that does not coast on a
  conic is not.
* Docking and the station's final relative approach. A station flight is tested
  to its 500 m hold point with the velocity matched; the last few hundred metres
  are not implemented, so nothing flies them.
* Continuous integration. The repository is under version control and `main` is
  published, but no CI pipeline runs yet; when one does, `tools/check.sh` is its
  single entry point.
