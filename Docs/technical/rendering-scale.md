---
type: Architecture
title: Rendering at solar scale
description: How to draw a 10¹³-metre system with float transforms, one camera, and a few thousand triangles.
tags: [technical, rendering, scale, floating-origin]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# The problem

Unity transforms are float. The solar system is about 10¹³ metres across. If
you put the Sun at the origin and Neptune at 4.5 × 10¹² metres, the float
representation of Neptune's position has an error of hundreds of kilometres
before anything moves. Add a camera and a depth buffer and the problem gets
worse: a 1-metre ship and a 10-billion-kilometre far plane cannot share a
conventional depth range.

The prototype solves this with three rules:

1. **Simulate in doubles, render in floats.**
2. **Keep the camera at the origin; move the world, not the camera.**
3. **Choose a render scale that makes the solar system a few million units wide.**

# Render scale

Default: **1 Unity unit = 1,000 km = 10⁶ metres**, so
`unitsPerMetre = 1e-6`.

| Object | Physical size | Render size |
|---|---:|---:|
| Earth radius | 6,371 km | 6.37 units |
| Moon orbit radius | 384,400 km | 384 units |
| Earth orbit radius | 149.6 million km | 149,600 units |
| Neptune orbit radius | 4.50 billion km | 4.50 million units |
| Sun radius | 696,340 km | 696 units |
| 2 km station cube | 2 km | 0.002 units |

This is a compromise:

* Planets are large enough to fly around and see as spheres.
* The inner system is a few hundred thousand units across, which is comfortable
  for floats.
* The outer system is a few million units across, which is still inside a
  20-million-unit far plane.
* Small objects are invisible at true scale, so the marker system (below) is
  necessary.

The scale is a serialized field (`UnitsPerMeter`) on `SolarSystemRenderer` and
`SolarSystemBootstrap`. Changing it changes the whole render, not the
simulation.

# Ship-relative rendering

Every frame:

```csharp
Vector3 position = ((body.PositionM - ship.PositionM) * unitsPerMetre).ToUnity();
```

The ship is always at Unity's origin. A body 50,000 km away is 50 units away,
regardless of whether the ship is at Earth or Neptune. Float precision is spent
on the distance that matters—the nearby geometry—not on the absolute
heliocentric coordinate.

There is no rebase step and no moving world root. The subtraction happens in
double precision before the cast, so the result is accurate to float precision
at the local distance.

The ship is always at the origin. The camera **orbits** that origin instead of
sitting on it: its position is the orbit offset in render units, and it always
looks back at the ship. Orbiting therefore costs nothing in precision — the
world is still projected from the ship — and it is what lets the player see the
plan rather than sit inside it. Movement changes the ship's double position,
which changes every body's relative render position next frame.

See [ADR-002](../decisions/adr-002-double-precision.md) and
[ADR-008](../decisions/adr-008-orbit-camera.md).

# Camera range

| Setting | Value | Why |
|---|---:|---|
| Near clip | 2% of the orbit distance, clamped to 0.0002–0.05 units | 200 km at the closest orbit, 50 km at the default; a fixed 50 km clip would cut a moon open when the camera zooms in |
| Far clip | 20,000,000 units | 20 billion km; beyond Neptune's orbit |
| Field of view | 60° | Comfortable navigation view |
| Clear flags | Solid black | Space |
| Depth | Unity 6 reversed-Z | Handles the extreme near/far ratio on supported platforms |

The near/far ratio is therefore between about 4 × 10⁸ and 10¹¹, depending on the
orbit. A conventional 24-bit depth buffer would z-fight badly. Reversed-Z, which Unity 6 uses on supported graphics APIs,
distributes precision much better near the camera. If a target platform shows
artefacts, the fallback is a two-camera stack:

* **Near camera:** ship, nearby bodies, stations; normal depth range.
* **Far camera:** the rest of the orrery, rendered at a coarser scale and
  composited behind the near layer.

The prototype does not need that yet. It is recorded here so the limitation is
not rediscovered late.

# Minimum apparent size

At true scale, a 939 km asteroid at 1 AU subtends about 1.3 arcseconds.
That is far below a pixel. The same is true of stations, most moons from a
distance, and the Sun from Neptune.

`BodyView` applies a minimum apparent size:

```text
angularSize   = radius / distance
minAngular    = fovRadians × minPixels / screenHeight
scaleFactor   = max(1, minAngular / angularSize)
```

With 60° FOV, 4.5 pixels, and a 1080-pixel screen, `minAngular ≈ 0.00436 rad`
(0.25°). A body smaller than that is scaled up until it is about 4.5 pixels
across. The scale factor is smoothed so distant bodies do not pop.

Examples:

| Body | Distance | True angular size | Scale factor | Rendered size |
|---|---:|---:|---:|---:|
| Earth | 1 AU | 4.3 × 10⁻⁵ rad | ~100× | ~4.5 px |
| Ceres | 3 AU | 1.0 × 10⁻⁶ rad | ~4,000× | ~4.5 px |
| 2 km station | 1.5 million km | 6.7 × 10⁻⁷ rad | ~6,500× | ~4.5 px |

This is a **marker aid**, not a physical size. It is essential for playability:
without it, most of the catalogue is invisible. Press `T` to toggle true scale
and see the difference.

The aid has a known consequence: a distant station can look larger than a distant
moon, because both are clamped to the same minimum size. Labels and the HUD
disambiguate. A future pass can use different marker shapes or colours for
stations, asteroids, and moons.

# Orbit lines

Each planet and moon gets a `LineRenderer` with 161 points sampled from its
Kepler ellipse.

* Points are computed in the body's element frame, transformed to the parent's
  equator if needed, then offset by the parent's current position.
* The line is drawn in ship-relative render coordinates, so it moves with the
  world.
* Width is scaled with camera distance:

  ```text
  width = max(parentDistance, orbitRadius) × 0.0015
  ```

  clamped to [0.35, 4000] units. This keeps the line roughly 1–2 pixels wide at
  any zoom.

Asteroid orbits are not drawn by default; 100 ellipses would clutter the view
and cost more than they add. The data is available if a later "belt map" mode
wants them.

# Materials and lighting

`Resources/Shaders/BodyLit.shader` is a minimal built-in-pipeline shader:

* Lambert term from a global `_SunPosition`,
* 5.5% ambient so the night side is not pure black,
* per-body `_Color`,
* no textures, no normal maps, no shadows.

The Sun uses `Unlit/Color`. Stations and asteroids use the same lit shader as
planets; a cube with a day/night terminator reads surprisingly well.

The shader is intentionally simple. It exists to make the orrery legible, not to
be a final planet renderer. A URP/HDRP pass would replace it with a proper
atmosphere/ring/cloud stack and a different far-field strategy.

# What breaks at this scale

* **Shadows.** Unity's shadow cascades cannot span the solar system. Shadows are
  disabled. A future local shadow system can be enabled only near the ship.
* **Physics.** Unity's `Rigidbody` and `CharacterController` use floats and a
  scene-wide physics world. Orbital mechanics is not done with them. Future
  collision and docking should run in a local, ship-relative physics bubble.
* **Particles.** World-space particles at these distances are impractical.
  Engine plumes and dust should be local effects.
* **UI.** Screen-space HUD works; world-space canvases at solar distances do
  not. Body labels are projected manually in `OnGUI`. Body picking is also
  screen-space, against the drawn marker rather than a collider, because the
  minimum-size aid means the visible marker is often much larger than the
  physical body. See [Selection and camera focus](/technical/selection-and-camera.md).
* **Culling and LOD.** Most bodies are tiny markers. A later belt renderer should
  use GPU instancing or a point cloud, not 100 sphere meshes.

# Future rendering work

1. **Two-camera far field** if reverse-Z is insufficient on a target platform.
2. **Billboard/impostor planets** at long range, with a smooth handover to
   spheres up close.
3. **Instanced asteroid belt** with per-instance colour and size.
4. **Starfield** with real bright stars and the Milky Way; currently the
   background is black.
5. **Rings for Saturn and Uranus**, and a tenuous atmosphere shell for Earth,
   Venus, Titan, and the gas giants.
6. **Local lighting** for stations and ships: running lights, docking guides,
   radiators.
7. **Post-processing** for bloom on the Sun and stations, used sparingly.

The scale rules—double simulation, ship-relative render, minimum apparent size—
should survive all of these. They are the load-bearing decisions.
