---
type: Architecture
title: Rendering at solar scale
description: How to draw a 10¹³-metre system with float transforms, one camera, and a few thousand triangles.
tags: [technical, rendering, scale, floating-origin]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
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
sitting on it: its position is the orbit offset in render units, and it looks
back at the ship whenever the look-at offset is zero. Orbiting therefore costs
nothing in precision — the world is still projected from the ship — and it is
what lets the player see the plan rather than sit inside it. A cursor-anchored
wheel zoom moves the look-at offset
([ADR-017](../decisions/adr-017-cursor-zoom-and-home.md)), which is still a
camera pose and not a new coordinate space. Movement changes the ship's double
position, which changes every body's relative render position next frame.

See [ADR-002](../decisions/adr-002-double-precision.md) and
[ADR-008](../decisions/adr-008-orbit-camera.md).

# Camera range

| Setting | Value | Why |
|---|---:|---|
| Pipeline | URP 17.3.0, forward | See [ADR-010](../decisions/adr-010-urp-rendering.md) |
| Near clip | 2% of the orbit distance, clamped to 0.0002–0.05 units | 200 km at the closest orbit, 50 km at the default; a fixed 50 km clip would cut a moon open when the camera zooms in |
| Far clip | 20,000,000 units | 20 billion km; beyond Neptune's orbit |
| Field of view | 60° | Comfortable navigation view |
| Clear flags | `Skybox` | The Milky Way and stars; solid black if the sky asset is missing. See [The sky](/technical/sky.md) |
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

Examples with the full aid (on the map, or in the shipped scene while its map
switch is off):

| Body | Distance | True angular size | Scale factor | Rendered size |
|---|---:|---:|---:|---:|
| Earth | 1 AU | 4.3 × 10⁻⁵ rad | ~100× | ~4.5 px |
| Ceres | 3 AU | 1.0 × 10⁻⁶ rad | ~4,000× | ~4.5 px |
| 2 km station | 1.5 million km | 6.7 × 10⁻⁷ rad | ~6,500× | ~4.5 px |

In the map build the aid is **local-aware**. `MarkerSizing.AidStrength` keeps it
at full strength inside the dominant body's sphere of influence and fades it to
true scale by three times that radius along the same log-distance band as the
camera blend. The map weight restores the full aid, so a planet an AU away is a
sub-pixel dot in the local view and a 6 px marker on the map. The neighbourhood
also grows with the camera distance, so zooming out locally does not make the
system vanish. The multiplications above remain the full-aid numbers; a body
outside the local neighbourhood simply has no inflation applied.

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
* Width is one apparent-width rule shared with the plan line. `LineWidth`
  converts a pixel count to render units at the camera's look-at depth.
  **Plan lines stay at 3 px**, while orbit lines blend with the map weight:

  ```text
  orbitPixels = lerp(3 px, 10 px, w)          // LineWidth.OrbitPixels
  width       = 2 × depth × tan(fov / 2) / viewportHeight × orbitPixels
  ```

  The local view is therefore the same fine line as before, and the solar
  system view draws a bold 10 px stroke as the zoom tilts toward the map. The
  blend uses the same `w` as the camera pose, so the lines thicken through the
  zoom tween instead of popping at the map threshold. The viewport height comes
  from the camera, so the apparent width also holds when the camera renders
  into a RenderTexture. The orbit's radius is not an input. The old rule
  multiplied the orbit size, which was only correct at the single distance
  where the whole ellipse was framed; inside the orbit it became a
  screen-filling ribbon. The pixel form is also correct at other fields of view
  and resolutions, and needs no world-unit clamp that breaks at the map ceiling.

  Width is **per vertex**, written into `LineRenderer.widthCurve`. A vertex
  keeps the camera-distance width until perspective would take it below
  `LineWidth.DistanceFloorPixels` (2 px); from there its world width grows with
  its own depth, so the far arc holds 2 px at the planet's 50% alpha instead of
  fading to a sub-pixel thread. The near half is untouched: it keeps the same
  world width and still thickens with proximity. The floor is capped by the
  line's own look-at width, so a line already thinner than 2 px is not
  inflated. The same rule applies to the plan line at its fixed 3 px.
* Colour belongs to the body and does not change with the view.
  `OrbitColours` samples one warm-to-cool ramp by orbital rank, evenly spaced
  rather than by semi-major axis (which would collapse Mercury to Mars into the
  first two percent). Mercury is warm orange, the four inner planets step
  through coral and red, and the outer planets finish in magenta and violet:
  Mercury `#FFA347`, Venus `#FC754D`, Earth `#FA525A`, Mars `#F7578B`,
  Jupiter `#F55CB7`, Saturn `#F261DF`, Uranus `#DC66F0`, Neptune `#B76BED`, all
  at 50% alpha. Moon orbits stay neutral grey so they read as subordinate to
  their planet.

Asteroid orbits are not drawn by default; 100 ellipses would clutter the view
and cost more than they add. The data is available if a later "belt map" mode
wants them.

# Materials and lighting

The prototype renders through URP 17.3.0. The geometry is unchanged; only the
shader language and the pipeline asset are new.

Three minimal URP shaders live in `Assets/Resources/Shaders/` so a runtime
`Shader.Find` resolves them in a build:

* `TimeToExplore/BodyLit` — Lambert term from a global `_SunPosition`, 5.5%
  ambient so the night side is not pure black, per-body `_Color`, no textures,
  no normal maps, no shadows. It uses a `UniversalForward` pass and declares
  `RenderPipeline = UniversalPipeline`.
* `TimeToExplore/Unlit` — flat `_Color`, used by the Sun, the ship marker, and
  the plan markers.
* `TimeToExplore/Line` — transparent, vertex-coloured, samples a `_MainTex` with
  `TRANSFORM_TEX` so the capture arc's tiled dash texture still works.

A fourth shader, `TimeToExplore/Skybox`, is a background pass rather than a URP
forward pass: it is queued at `Background`, writes no depth, and draws the Milky
Way texture plus procedural stars. Skybox shaders are exempt from the
`RenderPipeline` and forward-pass tags because Unity draws them through its own
skybox path; everything else about it follows the same rules. See
[The sky](/technical/sky.md).

Stations and asteroids use the same lit shader as planets; a cube with a
day/night terminator reads surprisingly well. `material.color` and `_Color`
remain the contract: every shader that the C# code colours declares `_Color`.

The pipeline asset keeps the cost down: render scale 1, MSAA off, HDR off, no
depth or opaque texture, no main or additional shadows, no soft shadows, URP
main and additional lights disabled, and no post-processing volume.
`UniversalAdditionalCameraData` is on both scene cameras with post-processing
and depth/colour texture requests off, and a black serialized clear that the
bootstrap switches to `Skybox` at runtime.

The shader is intentionally simple. It exists to make the orrery legible, not to
be a final planet renderer. A later URP pass can add atmosphere, rings, clouds,
bloom, and the far-field camera stack without changing the scale rules.

# What breaks at this scale

* **Shadows.** Unity's shadow cascades cannot span the solar system. Shadows are
  disabled. A future local shadow system can be enabled only near the ship.
* **Physics.** Unity's `Rigidbody` and `CharacterController` use floats and a
  scene-wide physics world. Orbital mechanics is not done with them. Future
  collision and docking should run in a local, ship-relative physics bubble.
* **Particles.** World-space particles at these distances are impractical.
  Engine plumes and dust should be local effects.
* **Teardown.** The renderer owns one `BodyView` and one `OrbitLine` per body as
  child objects. A scene unload, or a script reload that stops play mode, can
  destroy those children while the bootstrap's `LateUpdate` is still being
  dispatched. A destroyed Unity object compares equal to null, so the render
  frame checks each view and orbit and skips the destroyed ones, and
  `SolarSystemBootstrap.LateUpdate` does nothing until the model, clock, and
  renderer exist. Without those checks a teardown window produces one
  `NullReferenceException` or `MissingReferenceException` per frame, which is
  exactly the flood the editor console showed on 2026-11-18.
* **UI.** Screen-space HUD works; world-space canvases at solar distances do
  not. Body labels are projected manually onto a screen-space uGUI canvas. Body
  picking is also screen-space, against the drawn marker rather than a collider,
  because the minimum-size aid means the visible marker is often much larger than
  the physical body. See [Selection and camera focus](/technical/selection-and-camera.md).
* **Culling and LOD.** Most bodies are tiny markers. A later belt renderer should
  use GPU instancing or a point cloud, not 100 sphere meshes.

# Future rendering work

1. **Two-camera far field** if reverse-Z is insufficient on a target platform.
2. **Billboard/impostor planets** at long range, with a smooth handover to
   spheres up close.
3. **Instanced asteroid belt** with per-instance colour and size.
4. ~~**Starfield** with real bright stars and the Milky Way; currently the
   background is black.~~ Done: a generated Milky Way band plus procedural
   stars ([The sky](/technical/sky.md)). Real bright-star positions are still a
   follow-up.
5. **Rings for Saturn and Uranus**, and a tenuous atmosphere shell for Earth,
   Venus, Titan, and the gas giants.
6. **Local lighting** for stations and ships: running lights, docking guides,
   radiators.
7. **Post-processing** for bloom on the Sun, stations, and the brightest stars,
   used sparingly.

The scale rules—double simulation, ship-relative render, minimum apparent size—
should survive all of these. They are the load-bearing decisions.
