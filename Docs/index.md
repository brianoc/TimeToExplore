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

* [Working agreement](/working-agreement.md) — update the docs as changes happen; add tests for every new feature or piece of work.

# Start here

* [Game vision](/vision/game-vision.md) — the fantasy, the loop, and what the player actually does.
* [Design pillars](/vision/design-pillars.md) — the six rules that settle design arguments.
* [Flight model](/simulation/flight-model.md) — 10 m/s², brachistochrone travel, and the delta-v economy.

# Vision

* [Game vision](/vision/game-vision.md) — the player fantasy, core loop, and scope of Time to Explore.
* [Design pillars](/vision/design-pillars.md) — the six rules that settle design arguments for Time to Explore.

# Simulation

* [Orbital mechanics](/simulation/orbital-mechanics.md) — Keplerian ephemerides, frames, and station Lagrange points.
* [Time and scale](/simulation/time-and-scale.md) — units, epoch, time acceleration, and the precision budget.
* [Flight model](/simulation/flight-model.md) — the 10 m/s² torch drive, travel times, and the two travel regimes.
* [Navigation and arrival](/simulation/navigation-and-arrival.md) — how the player plans an intercept, inserts into orbit, and docks.

# World

* [The Sol system](/world/solar-system.md) — the starting system, its scale, its contents, and the data behind it.
* [Initial catalogue](/world/initial-catalogue.md) — the planets and major moons shipped in the first playable.
* [Asteroid catalogue](/world/asteroid-catalogue.md) — the 100 largest main-belt asteroids, with diameters and elements.
* [Lagrange stations](/world/lagrange-stations.md) — the six starting stations and why they sit where they do.

# Technical

* [Architecture](/technical/architecture.md) — how the Unity prototype is structured, from catalogue load to render frame.
* [Input](/technical/input.md) — the input map, the Input System seam, why Active Input Handling stays on Both, and the mouse and wheel rules.
* [The flight session](/technical/flight-session.md) — plot a route, press Begin, watch the ship fly: ship ownership, the warp cap, the trajectory line, and the flight panel.
* [Selection and camera focus](/technical/selection-and-camera.md) — click a body for its details, then plot a route or jump to it in debug flight.
* [The system map and route preview](/technical/system-map.md) — in progress: the blended top-down map, map drawing and picking, the local aid fade, and Plot route → Begin are built; frame-loop acceptance is next.
* [Testing and verification](/technical/testing.md) — the one-command check suite, test matrix, and numerical reference values.
* [Data schema](/technical/data-schema.md) — the catalogue format, units, and generation pipeline.
* [Rendering at solar scale](/technical/rendering-scale.md) — ship-relative rendering, camera range, and marker sizing.

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
* [ADR-010: Universal Render Pipeline for the prototype](/decisions/adr-010-urp-rendering.md) — move to URP 17.3.0 with three minimal runtime shaders and a deliberately cheap forward renderer. (ADR-009 is reserved for the deferred map-camera decision.)
* [ADR-011: The Input System behind one seam](/decisions/adr-011-input-system.md) — `com.unity.inputsystem` 1.20.1, device polling through `PlayerControls`, and Active Input Handling **Both** because the HUD is IMGUI.
* [ADR-012: An assembly layout that enforces the layering](/decisions/adr-012-assembly-layout.md) — Core, Simulation, and Presentation runtime assemblies plus Editor and test assemblies, with the dependency direction checked by `AssemblyLayoutTests`.
