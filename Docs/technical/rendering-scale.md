---
type: Architecture
title: Rendering at solar scale
description: How to draw a 10¹³-metre system with float transforms, one camera, and a few thousand triangles.
tags: [technical, rendering, scale, floating-origin]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T10:00:00Z }
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

Examples with the aid:

| Body | Distance | True angular size | Scale factor | Rendered size |
|---|---:|---:|---:|---:|
| Earth | 1 AU | 4.3 × 10⁻⁵ rad | ~100× | ~4.5 px |
| Ceres | 3 AU | 1.0 × 10⁻⁶ rad | ~4,000× | ~4.5 px |
| 2 km station | 1.5 million km | 6.7 × 10⁻⁷ rad | ~6,500× | ~4.5 px |

The aid is the same at every zoom: there is one camera and one range, and
finding a distant body should not depend on a second view. The multiplications
above are the full-aid numbers at any distance.

This is a **marker aid**, not a physical size. It is essential for playability:
without it, most of the catalogue is invisible. Press `T` to toggle true scale
and see the difference.

The aid has a known consequence: a distant station can look larger than a distant
moon, because both are clamped to the same minimum size. Labels and the HUD
disambiguate. A future pass can use different marker shapes or colours for
stations, asteroids, and moons.

# The ship marker and thrust vector

The ship is a point at the render origin with a placeholder marker: a capsule
kept at a constant **10 px** apparent height. It points along the commanded
thrust while the drive is on and along the velocity when it is off, so the ship
always has a heading even though there is no attitude state yet.

A marker alone cannot show thrust against the direction of travel: during a
brake the capsule points retrograde, but so does a ship coasting backwards. The
`ShipView` therefore draws a second object, a **thrust arrow**, from the ship
along `ThrustCommand.Direction` whenever the commanded acceleration is above
`1e-3 m/s²`:

* The arrow is a **52 px** apparent length at every zoom, with a 12% standoff so
  it clears the capsule. It uses the same pixel-to-world rule as the lines
  (`LineWidth.WorldWidth`), so its size holds with the field of view, the
  resolution, and a RenderTexture viewport.
* Its length is deliberately **fixed**, not proportional to acceleration. The
  question it answers is "which way is the drive pushing?", and a fixed arrow
  is readable at a glance; the HUD carries the magnitude and throttle.
* It is bright green (`#85FF9E`) so it is distinct from the route's gold quintic,
  sky-blue conic, and red fixed-burn arcs, and it is hidden when the drive is
  off or the ship marker is hidden.
* The arrow is a unit-length mesh scaled by the apparent length: a slim shaft
  and a cone head, emitted double-sided because the shared unlit shader culls
  back faces. Direction comes from the transform's rotation, so the mesh is
  independent of the camera pose.

# The focused render frame

The render origin is normally the ship. **`F` moves it to the selected body**,
so the camera can frame a planet at its true size without moving the simulated
ship. The reason is float precision: Mars is 3,390 km across, or 3.39 render
units, but at 1 AU its centre is 150,000 units from a ship-relative origin. A
32-bit float at that magnitude has an ULP around 0.015 units, so the true
sphere is smaller than one representable step and collapses. No amount of
camera zoom can fix that.

While focused, every body position is computed relative to the focused body,
which sits at `(0,0,0)`. The camera keeps the player's yaw and pitch and orbits
that origin at **four radii** (clamped to the orbit range), so the disc fills
about half the view and the atmosphere has room at the limb. The ship marker,
the route line, and the orbit lines belong to the ship frame; they are hidden
until the focus ends. `Space` returns the origin to the ship and lands on its
home view. The switch is a cut rather than a continuous camera move, because
there is no continuous path across the precision gap.

The albedo and atmosphere gates read the **camera** distance, not the origin
distance. In a focused frame the body's origin distance is zero while the
camera is four radii away; the gates must still see the planet as close.

# Orbit lines

Each planet and moon gets a `LineRenderer` with 161 points sampled from its
Kepler ellipse.

* Points are computed in the body's element frame, transformed to the parent's
  equator if needed, then offset by the parent's current position.
* The line is drawn in ship-relative render coordinates, so it moves with the
  world.
* Width is one apparent-width rule shared with the plan line. `LineWidth`
  converts a pixel count to render units at the camera's look-at depth. Both
  orbit and plan lines are **3 px**:

  ```text
  width = 2 × depth × tan(fov / 2) / viewportHeight × 3
  ```

  The viewport height comes from the camera, so the apparent width also holds
  when the camera renders into a RenderTexture. The orbit's radius is not an
  input. The old rule multiplied the orbit size, which was only correct at the
  single distance where the whole ellipse was framed; inside the orbit it became
  a screen-filling ribbon. The pixel form is also correct at other fields of
  view and resolutions, and needs no world-unit clamp at the outer ceiling.

  Width is **per vertex**, written into `LineRenderer.widthCurve`, and the
  apparent width is bounded at both ends. A vertex is never thinner than
  `LineWidth.DistanceFloorPixels` (2 px), so the far arc holds 2 px at the
  planet's 50% alpha instead of fading to a sub-pixel thread. A vertex is also
  never wider than the line's own pixel count at its own depth, so the near
  half cannot grow without limit: without that ceiling, a route or orbit
  passing close to a zoomed-in camera drew a screen-filling ribbon, because a
  constant world width subtends `pixels × lookAtDepth / depth` and the depth
  can approach zero. The rule is therefore an apparent width clamped to the
  `[2 px, lookAtPixels]` band, with perspective still narrowing the line
  between its bounds. The floor is capped by the line's own look-at width, so a line
  already thinner than 2 px is neither inflated nor thickened. The same rule
  applies to both the plan line and the orbit lines at their shared 3 px.
* **The camera plane is a clip, not a width.** A `LineRenderer` billboards its
  strip toward the view, so a segment with one end behind the camera has no
  consistent screen-space width; Unity still emits its quad, and the projected
  result can fill the frame. `LineCameraClip` keeps the longest contiguous run
  in front of the near plane and interpolates the crossing point, so no drawn
  vertex is behind the camera. A vertex exactly on the plane gets zero width
  rather than the look-at width, which tapers the strip instead of letting the
  near end inherit a world size. This is why a route stays a line when the
  player zooms in past its ship end.
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
and cost more than they add. The data is available if a later "belt view" mode
wants them.

# Materials and lighting

The prototype renders through URP 17.3.0. The geometry is unchanged; only the
shader language and the pipeline asset are new.

Four minimal URP forward shaders live in `Assets/Resources/Shaders/` so a
runtime `Shader.Find` resolves them in a build:

* `TimeToExplore/BodyLit` — Lambert term from a global `_SunPosition`, 5.5%
  ambient so the night side is not pure black, per-body `_Color`, and an albedo
  map (`_MainTex`) that replaces the tint once the detail gate opens. It has no
  normal maps and no shadows. It uses a `UniversalForward` pass and declares
  `RenderPipeline = UniversalPipeline`.
* `TimeToExplore/BodyAtmosphere` — the additive rim shell: transparent queue,
  `Blend SrcAlpha One`, depth write off, back faces culled. Its alpha is a rim
  term times a soft sun term, so the glow is a limb and fades round the
  terminator. It is drawn only above 0.06 rad and only from outside the shell.
* `TimeToExplore/Unlit` — flat `_Color`, used by the Sun, the ship marker, the
  thrust vector, and the plan markers.
* `TimeToExplore/Line` — transparent, vertex-coloured, samples a `_MainTex` with
  `TRANSFORM_TEX` so the capture arc's tiled dash texture still works.

A fifth shader, `TimeToExplore/Skybox`, is a background pass rather than a URP
forward pass: it is queued at `Background`, writes no depth, and draws the Milky
Way texture plus procedural stars. Skybox shaders are exempt from the
`RenderPipeline` and forward-pass tags because Unity draws them through its own
skybox path; everything else about it follows the same rules. See
[The sky](/technical/sky.md).

Stations and asteroids use the same lit shader as planets; a cube with a
day/night terminator reads surprisingly well. `material.color` and `_Color`
remain the contract: every shader that the C# code colours declares `_Color`.

The pipeline asset keeps the cost down: render scale 1, MSAA off, no depth or
opaque texture, no main or additional shadows, no soft shadows, URP main and
additional lights disabled, and no Volume object in the scene. HDR is the
deliberate exception: it is on, in URP's **32-bit R11G11B10** colour buffer
(`m_HDRColorBufferPrecision: 0`) rather than 64-bit RGBA16F, and MSAA stays off.
The sky earns it. It writes up to about 2.4 in HDR units, so clamping at 1.0
makes Sirius, Vega, and Rigel identical white blobs; the band peaks at 0.55 and
stays below the bloom threshold. HDR is what lets the display tell those
brightnesses apart.

The bloom itself comes from the pipeline's **default volume profile**,
`Assets/DefaultVolumeProfile.asset`, which URP registers in its global settings
and applies to every camera with post-processing on. That is why the scene needs
no Volume object, and why the offscreen capture tool and the test suites get the
same effect as the game. The profile carries **Bloom** — threshold 1.0, intensity
2.0, scatter 0.65, Gaussian filtering, no lens dirt — and **Neutral Tonemapping**,
which gives the brightest HDR cores a shoulder instead of a clipped white disc.
The intensity is high because a star is only a few pixels wide, so the
downsampled bloom chain has little energy to work with, while the 1.0 threshold
keeps the 0.55 band out of it. The shipped scene has one camera, the runtime
camera is that same camera, and its `UniversalAdditionalCameraData` has
post-processing on but depth and colour texture requests off, with a black
serialized clear that the bootstrap switches to `Skybox` at runtime. The
renderer does carry URP's `PostProcessData` (the package's
`Runtime/Data/PostProcessData.asset`), so the post-processing shaders exist in a
build; a renderer without it skips every effect silently, with no warning. Bloom
on the Sun and stations is still future work.

Nothing else in the frame exceeds 1.0, so the bloom is confined to the sky.
`tools/validate_catalogue.py` keeps every body colour inside [0, 1];
`TimeToExplore/BodyLit` multiplies `_Color` by
`_Ambient + (1 - _Ambient) * ndl` with `ndl <= 1`, so it never exceeds `_Color`;
the Sun is unlit `[1.0, 0.93, 0.72]`; route and orbit colours are at most 1 with
alpha below 1. The band's 0.55 and the washed body colours sit under the
threshold, and only the brightest catalogue stars write above it.

The shader is still intentionally simple: it exists to make the orrery legible,
not to be a final planet renderer. It now samples the nine albedo maps **at
arrival**, when a body's true angular radius passes 0.025 rad, and keeps the
flat catalogue tint below that; the maps are loaded lazily, so a marker costs no
texture fetch. Above a second gate, **0.06 rad**, an atmospheric body also gains
a second sphere at its **real** atmosphere height, drawn additively as a rim
glow with a soft sun term (`TimeToExplore/BodyAtmosphere`). Earth's shell sits
about 100 km up, so its scale is 1.0157. Both passes run in the same URP
forward renderer with no depth texture, and the shell's blend is
`SrcAlpha One` with depth write off. See
[Planet appearance](/technical/planet-appearance.md).

Rings and ray-marched single scattering remain future work; the rim is the
cheap stand-in and keeps the real height so a later scattering pass can replace
it without moving the silhouette. The far-field camera stack and clouds join
them, none of which changes the scale rules.

# Colour space

The project renders in **Linear** colour space
([ADR-021](../decisions/adr-021-linear-colour-space.md)). Every colour in the
source — the catalogue JSON, the C# palette constants, the shader literals — is
authored in sRGB, and converts once, where it enters a shader. The boundary
depends on the consumer, and the difference matters:

* **Material colour properties** (`_Color`) are converted by Unity when it
  submits the material. `BodyView`, `ShipView`, and the trajectory markers pass
  the authored value through untouched; `.linear` there would convert twice and
  leave a body dark and over-saturated.
* **`LineRenderer` vertex colours** are not converted, so `OrbitLine` and
  `TrajectoryLine` apply `.linear` to the palette as they hand it over.
* **Shader literals and floats** are not converted: `BodyLit` runs its authored
  `_Ambient` through `SRGBToLinear`, and the skybox does the same for its star
  tints.
* **The HUD** needs nothing: Unity's `UI/Default` and `UI/Default Font` shaders
  call `UIGammaToLinear` on the vertex colour in a linear project, so the
  palette stays an sRGB palette.

Colour textures import as sRGB — the Milky Way band does. Data textures must be
created `linear: true`, which the bright-star map already is; an sRGB import
would decode its magnitude and position codes as if they were light.

Bloom and Neutral tonemapping now operate on the values they were written for.
Their threshold and gains are the numbers to re-tune after a colour-space or
lighting change, not the authored colours.

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
   background is black.~~ Done: a generated Milky Way band, procedural stars,
   and the real bright-star catalogue ([The sky](/technical/sky.md)).
5. **Rings for Saturn and Uranus**, and ray-marched single scattering to replace
   the rim-shell atmosphere ([ADR-024](/decisions/adr-024-planet-appearance.md)).
   The tier-1 rim is in.
6. **Local lighting** for stations and ships: running lights, docking guides,
   radiators.
7. **Post-processing.** Bloom for the brightest stars is in, through the
   pipeline's default volume profile. Bloom on the Sun and stations is still
   future work, to be used sparingly.

The scale rules—double simulation, ship-relative render, minimum apparent size—
should survive all of these. They are the load-bearing decisions.
