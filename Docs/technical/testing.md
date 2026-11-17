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

`check.sh` runs four gates in order:

| Gate | Tool | What it proves |
|---|---|---|
| Design bundle | `tools/validate_okf.py` | OKF v0.2 structure, frontmatter, links, index coverage |
| Catalogue schema | `tools/validate_catalogue.py` | Field types, ids, parents, counts, orbit ranges, epoch maths |
| Generator | `tools/generate_solar_system.py --offline --output …` | The committed JSON is byte-identical to a fresh generation |
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
assembly. The runtime is in `Assets/Scripts/TimeToExplore.Runtime.asmdef`; an
assembly definition is required because Unity test assemblies cannot reference
the predefined `Assembly-CSharp`.

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
| `SceneSmokeTests` | the shipped and comparison scenes resolve the bootstrap; the shipped scene leaves the map switch off, the comparison scene turns it on; the catalogue resource imports |
| `ShipDynamicsTests` | two-body speeds, energy, eccentricity, flight-path angle, thrust clamping |
| `GravityFieldTests` | Newton at 1 AU, Mars dominance, pruning versus the full sum, surface clamp, no mutation of the model, pure versus frame state |
| `PlanGeometryTests` | quintic endpoint conditions and peak factors, parking radius and its gravity ceiling across the catalogue, circular/equatorial/prograde orbits, SOI radii |
| `ShipIntegratorTests` | orbit stability, Kepler agreement, kinematic thrust, the brachistochrone table, step-size independence, collision, step cap |
| `TripPlannerTests` | arc chain and phases, exact arrival, seam continuity, flight-time envelope, Venus, Jupiter at the ceiling, the Moon, LEO departure, determinism, no mutation |
| `FlightExecutionTests` | plan, fly, and arrive at Mars and Jupiter in stable orbits; feedback from a perturbed start; Mars back to Earth; the orrery is untouched |
| `FlightSessionTests` | ship ownership, the Go To plan from Gateway, exact arrival through the session, budget clamping, cancel, teleport, a flight through a body, no mutation, the preview (never moves the ship, one-at-a-time, Begin consumes and replans, Cancel clears, the 1% staleness window), and the idle coast that keeps the ship moving with the clock |
| `WarpPolicyTests` | clearance, local-dynamics, and plan-event caps; scale rounding; the suggested Go To warp |
| `PlanPathTests` | arc boundary walking and the flip point's sign change |
| `FlightReportTests` | the phase table, live rows, arrival rows, preview and stale rows, status labels, countdown formatting |
| `OrbitCameraTests` | pitch clamp, yaw wrap, zoom limits, shortest turn, slew rate, distance easing |
| `CameraRigTests` | the auto-framing distance at arrival and in cruise, the clamps, the follow flags, the plan line's width-to-distance rule, the map pose and its stored-pose round trip, input routing and both zoom ceilings, the pan and its 60 AU cap, the exponential pan ease, the near plane at both ends of the blend, auto-framing suspended on the map, and the unchanged local view with the map off |
| `MapViewTests` | the map blend endpoints and smoothstep midpoint, the zoom-out-and-back pose round trip, the Moon-inside-Earth dominant body at Gateway, the deep-space fallback, the pan cap, the extended zoom clamp, the top-down rotation and yaw-turning up vector, the blended rotation's agreement with the effective pitch, the local-to-map near-plane rule, the distance-scaled pan drag, and the pan ease |
| `MapDrawingTests` | the 4.5 px local and 6 px map marker minimum and its smooth blend, the true-scale override, the viewport angular conversion, the neighbourhood aid (full inside the SOI, monotonic to true scale at 3×, restored by the map, widened by the camera distance, disabled without a local scale), the always-on Sun/planet/station labels, the 5× parent-SOI moon window, the hidden full-system moons, the hover/selection-only asteroids, the unchanged local opening-shot labels, the map-only ship label, and the orbit-line width at close map zoom, locally, and with distance |
| `SceneFlightTests` | the scene's frame loop: clock, warp cap, session advance, clamp; the full Gateway→Mars acceptance flight and three orbits |
| `BootstrapSmokeTests` | the real composition root: `Awake` builds the world and the camera rig, Go To wires the flight and the trajectory line, cancel and reset behave, the debug jump is refused in flight, the map switch reaches the rig and raises the far clip, the same switch gates the neighbourhood aid fade, a plot draws the preview and arrival ghost without flying, Begin replans at the current date, the clock runs while idle so a preview can age, and discard leaves the ship alone |

The suite currently has **277 tests**. It deliberately tests the shipped
catalogue as well as the maths, so a bad regeneration fails before Unity opens
a scene.

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
asteroids at 0; and the map orbit line is `0.0015 × camera distance`, capped at
20,000 units.

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
  edit mode; what remains is the actual `OnGUI` draw and the rendered frame.
* Screen-space picking at the edges of the view and on a moving camera; the
  pure picker is tested, but the projection step needs a PlayMode scene.
* The trajectory line's rendering and the dash texture; only its data sources
  (the plan's boundaries and flip) are tested.
* Performance budgets for orbit-line updates and HUD allocation.
* Fuel accounting, attitude dynamics, and the transfer-conic capture.
* Continuous integration. The repository is not under version control yet;
  when it is, `tools/check.sh` is the single CI entry point.
