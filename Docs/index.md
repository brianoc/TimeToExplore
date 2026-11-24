---
okf_version: "0.2"
---
# Time to Explore — Design Bundle

A working design knowledge base for **Time to Explore**, a grounded science-fiction
solar-system game about flying a real ship through real distances.

The bundle is organised so a reader (human or agent) can start with the vision,
then the simulation rules, then the world and the implementation. Every concept
is a normal Markdown file with YAML frontmatter; links are absolute
bundle-relative paths that stay valid when a document moves.

# Every session

* [Working agreement](/working-agreement.md) — do all work in a linked worktree and keep `main` for merges and human testing; update the docs as changes happen; add tests for every new feature or piece of work; keep the live handoff current while a plan is in progress and clear it when done; publish `main` only once the person confirms the result; finish with screenshots, or a short video when motion matters, in the chat.

# Start here

* [Game vision](/vision/game-vision.md) — the fantasy, the loop, and what the player actually does.
* [Design pillars](/vision/design-pillars.md) — the six rules that settle design arguments.
* [Flight model](/simulation/flight-model.md) — 10 m/s², brachistochrone travel, the delta-v economy, and the first-pass fuel readout.
* [Glossary](/glossary.md) — the shared words: the route families and the family the profile belongs to, the one orbit camera, the thrust and manoeuvre vocabulary, and the arrival geometry.

# Vision

* [Game vision](/vision/game-vision.md) — the player fantasy, core loop, and scope of Time to Explore.
* [Design pillars](/vision/design-pillars.md) — the six rules that settle design arguments for Time to Explore.

# Simulation

* [Orbital mechanics](/simulation/orbital-mechanics.md) — Keplerian ephemerides, frames, and station Lagrange points.
* [Time and scale](/simulation/time-and-scale.md) — units, epoch, time acceleration, and the precision budget.
* [Flight model](/simulation/flight-model.md) — the 10 m/s² torch drive, travel times, the burn–coast–burn profile, and the delta-v economy.
* [Navigation and arrival](/simulation/navigation-and-arrival.md) — how the player plans an intercept, arrives in orbit at a planet or at a station's hold point, and why docking is a later phase.
* [Burn–coast–burn trips](/simulation/burn-coast-burn.md) — the shipped burn-hard, coast-fast profile: brake into a planet's orbit or match a station's hold point, with its short escape departure, the true floor and trip-time window, the Lambert solver, and the four arc ingredients it mixes.
* [Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md) — why a fixed burn integrates the summed field, where it hands over to the shaped ends, and why the capture's entry speed becomes an output instead of a planted 5 km/s.

# World

* [The Sol system](/world/solar-system.md) — the starting system, its scale, its contents, and the data behind it.
* [Initial catalogue](/world/initial-catalogue.md) — the planets and major moons shipped in the first playable.
* [Asteroid catalogue](/world/asteroid-catalogue.md) — the 100 largest main-belt asteroids, with diameters and elements.
* [Lagrange stations](/world/lagrange-stations.md) — the six starting stations and why they sit where they do.

# Technical

* [Architecture](/technical/architecture.md) — how the Unity prototype is structured, from catalogue load to render frame.
* [Input](/technical/input.md) — the input map, the Input System seam and uGUI input module, why Active Input Handling is New only, and the mouse and wheel rules.
* [The flight session](/technical/flight-session.md) — plot a route, press Begin, watch the ship fly: ship ownership, the warp cap, the trajectory line, and the flight panel.
* [Planning performance](/technical/planning-performance.md) — the two-tier model: a fast search-only gravity field in front of the unchanged full-field plan and flight, the 5% thrust screen, the floor cache, and the latest-wins background planner (retimes and route plans).
* [Selection and camera focus](/technical/selection-and-camera.md) — click a body for its details, then plot a route or jump to it, and how the one orbit camera zooms, frames, and returns home.
* [The system map (removed)](/technical/system-map.md) — a historical record of the top-down comparison layer removed on 2026-11-21; the current camera is in Selection and camera focus.
* [Testing and verification](/technical/testing.md) — the one-command check suite, the EditMode and PlayMode test matrices, and numerical reference values.
* [Working in a worktree](/technical/worktrees.md) — one agent, one branch, one worktree; setup, verification, merge hotspots, and cleanup.
* [Data schema](/technical/data-schema.md) — the catalogue format, units, and generation pipeline.
* [Rendering at solar scale](/technical/rendering-scale.md) — ship-relative rendering, camera range, and marker sizing.
* [The sky](/technical/sky.md) — the baked Milky Way band, the procedural stars, the real bright stars behind the constellations, and the galactic frame.

# Production

* [Roadmap](/production/roadmap.md) — what exists now, what comes next, and the risks.

# Decisions

* [ADR-001: Keplerian on-rails orbits](/decisions/adr-001-keplerian-orbits.md) — two-body propagation instead of N-body integration.
* [ADR-002: Double precision and ship-relative rendering](/decisions/adr-002-double-precision.md) — simulate in double-precision metres and render in a ship-relative frame.
* [ADR-003: Render scale and minimum apparent size](/decisions/adr-003-render-scale.md) — 1 Unity unit = 1,000 km, with tiny bodies clamped to a few pixels.
* [ADR-004: 10 m/s² as the default torch acceleration](/decisions/adr-004-torch-acceleration.md) — keep one Earth gravity and make delta-v the scarce resource.
* [ADR-005: continuous gravity for the ship](/decisions/adr-005-continuous-gravity.md) — summed point-mass field over the on-rails world, and an always-integrated ship.
* [ADR-006: quintic reference arcs for Go To](/decisions/adr-006-quintic-plan.md) — escape, cruise, and capture arcs matched in position, velocity, and acceleration.
* [ADR-007: one owner for the ship, and an automatic warp cap](/decisions/adr-007-flight-director.md) — the flight session owns the ship; warp drops by clearance, local dynamics, and the next plan event.
* [ADR-008: an orbit camera around the ship](/decisions/adr-008-orbit-camera.md) — keep the ship at the render origin, orbit the camera, and size the plan line from the camera distance.
* [ADR-009: a blended system map that looks at ship + pan](/decisions/adr-009-map-camera-pan.md) — superseded: one zoom blended the orbit camera to a top-down map, removed by ADR-018.
* [ADR-010: Universal Render Pipeline for the prototype](/decisions/adr-010-urp-rendering.md) — move to URP 17.3.0 with three minimal runtime shaders and a deliberately cheap forward renderer.
* [ADR-011: The Input System behind one seam](/decisions/adr-011-input-system.md) — `com.unity.inputsystem` 1.20.1, device polling through `PlayerControls`, and Active Input Handling **Both** because the HUD was IMGUI. (Superseded in part by ADR-013.)
* [ADR-012: An assembly layout that enforces the layering](/decisions/adr-012-assembly-layout.md) — Core, Simulation, and Presentation runtime assemblies plus Editor and test assemblies, with the dependency direction checked by `AssemblyLayoutTests`.
* [ADR-013: A runtime uGUI HUD, and Input System New only](/decisions/adr-013-ugui-hud.md) — replace IMGUI with a code-built uGUI canvas, give uGUI an `InputSystemUIInputModule`, and drop the legacy backend.
* [ADR-014: Transfer conics and the Oberth capture](/decisions/adr-014-keplerian-coast.md) — universal-variable propagation, a true-anomaly-parameterised coast, and an impulsive periapsis capture that parks lower and cheaper.
* [ADR-015: Fixed-thrust burns and the hybrid capture](/decisions/adr-015-fixed-thrust-burns.md) — fly one long capture burn at a player-set level, trim its end state with an impulse for exactness, and keep the conic parking radius instead of raising it for the finite-burn loss.
* [ADR-016: Show what a journey costs in fuel, without a tank](/decisions/adr-016-fuel-readout.md) — charge the plan's delta-v through the rocket equation at 10,000 km/s and print the propellant, while the tank stays unlimited and mass constant.
* [ADR-017: Zoom toward the pointer, and Space to go home](/decisions/adr-017-cursor-zoom-and-home.md) — anchor the wheel zoom to the point under the pointer with the look-at offset, and ease the view back to the ship in one second.
* [ADR-018: one orbit camera, no system map](/decisions/adr-018-one-orbit-camera.md) — remove the top-down map and its comparison scene, keep one 2,000 km–12 billion km orbit camera, and capture the framing distance once.
* [ADR-019: station calls and the departure break](/decisions/adr-019-station-calls-and-departure.md) — a station call is a point-target rendezvous ending 500 m astern on a hold point with no capture chain or docking, and a go-to departure is a short prograde escape break and a ballistic climb, never a share of the trip time.
