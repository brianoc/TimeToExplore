---
type: Decision Record
title: "ADR-010: Universal Render Pipeline for the prototype"
description: Move the orrery from the built-in pipeline to URP 17.3.0 with three minimal runtime shaders and a deliberately cheap forward renderer.
tags: [decision, rendering, urp, shaders, technical]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# Context

The prototype rendered through the built-in render pipeline. Its only shader,
`BodyLit.shader`, was a CGPROGRAM pass that computed Lambert shading from a
global `_SunPosition`; the Sun, ship, and markers used `Unlit/Color`, and the
orbit and trajectory lines used `Sprites/Default`.

That is a dead end for the next rendering work. Bloom on the Sun, a local
shadow or lighting pass, and a two-camera far field all want a scriptable
pipeline with a defined renderer, camera data, and shader library.
Unity 6 still supports the built-in path, but it is the legacy path, and every
new rendering feature arrives first in URP. Staying on it would have meant
writing the same plumbing by hand in a second migration.

URP 17.3.0 is bundled with Unity 6000.3.28f1, so the package resolves without a
registry download. The prototype's needs are small: 137 primitives, about 30
orbit lines, one plan line, and a HUD. It does not need a heavy pipeline; it
needs a supported one.

The camera is an orbit camera with a cursor-anchored look-at offset
([ADR-008](/decisions/adr-008-orbit-camera.md),
[ADR-017](/decisions/adr-017-cursor-zoom-and-home.md)); this record does not
depend on it.

# Decision

1. **Add `com.unity.render-pipelines.universal` 17.3.0** to
   `Packages/manifest.json` and let UPM resolve `core`, `shadergraph`, and
   `universal-config`.
2. **Ship a project pipeline asset and renderer** under `Assets/Settings/`:
   `TimeToExploreUniversalRenderPipeline.asset` and
   `TimeToExploreUniversalRenderer.asset`. The pipeline asset is assigned as
   `GraphicsSettings.defaultRenderPipeline`. Every quality level keeps an empty
   pipeline override, so the graphics default applies at every level; there is
   no half-assigned combination.
3. **Keep the renderer deliberately cheap.** HDR off, MSAA 1, render scale 1,
   camera depth and opaque textures off, main and additional shadows off, soft
   shadows off, shadow distance 0, URP main and additional lights disabled,
   SRP batcher on, intermediate texture mode Auto (no renderer features, so no
   intermediate texture is allocated), and no post-processing.
   `UniversalAdditionalCameraData` on both scene cameras sets post-processing
   and depth/colour texture requests off and keeps the existing black
   solid-colour clear.
4. **Keep three purpose-built runtime shaders** in
   `Assets/Resources/Shaders/` so `Shader.Find` resolves them in a build:
   * `TimeToExplore/BodyLit` is ported to a `UniversalForward` pass with URP
     includes and the `UniversalPipeline` tag. It keeps the `_Color` and
     `_Ambient` properties and the same global `_SunPosition` maths. The shader
     name and GUID are unchanged, so existing references survive.
   * `TimeToExplore/Unlit` replaces the built-in `Unlit/Color` for the Sun, the
     ship marker, and the plan markers. It keeps `_Color`.
   * `TimeToExplore/Line` replaces `Sprites/Default` for orbit and trajectory
     lines. It reads the `LineRenderer`'s vertex colour, alpha-blends, and
     samples `_MainTex` with `TRANSFORM_TEX` so the tiled dash texture and
     `mainTextureScale` keep working.
5. **Keep the visual result.** The Sun stays unlit, bodies are lit by the global
   sun position with a 5.5% ambient floor, orbit lines keep their colours and
   widths, and the capture arc stays dashed. This is a pipeline migration, not
   an art pass.
6. **Do not add a second render path.** There is one base camera per scene and
   no camera stack, renderer feature, additional light, volume, or post-process
   override.

# Consequences

**Positive**

* The project is on the supported Unity 6 rendering path, with a renderer asset
  and camera data to hang future features on.
* The three shaders are small, readable, and cheap; nothing in the pipeline
  turns on HDR, MSAA, shadows, depth copies, or post-processing by accident.
* The runtime architecture is untouched: `BodyView`, `ShipView`, `OrbitLine`,
  `TrajectoryLine`, and `SolarSystemRenderer` still build materials at runtime
  and still write `_Color` and the global `_SunPosition`.
* `BodyLit` keeps its name and GUID, so the scene and code references do not
  churn.
* The migration is guarded by EditMode tests that check the graphics default,
  the asset wiring, the cheap settings, the shader properties, the pipeline
  tags in the shader source, and the camera data in both scenes.

**Negative**

* URP pulls in transitive packages (`burst`, `mathematics`, `collections`) that
  are not in the editor's built-in package folder. The lock file pins them, but
  a fresh machine needs the package cache or a registry connection for the first
  resolve.
* The built-in `Unlit/Color` and `Sprites/Default` dependencies are gone, so
  any future code that reaches for a built-in shader will silently fail under
  URP. New shaders belong in `Assets/Resources/Shaders/` and should be added to
  `RenderingShaders`.
* `-nographics` batch mode cannot populate pass tags (`Shader.FindPassTagValue`
  reports one pass for every shader), so the pipeline-tag test reads the shader
  source instead. The rendered frame itself still needs a human eye.
* URP's forward renderer adds a little per-frame overhead compared with the
  built-in path. At 137 bodies and a handful of lines it is not measurable in
  practice, but it is not free.
* The project stays in Gamma colour space, so the migration does not change
  colour grading; a later linear-colour pass is a separate decision.

# Alternatives considered

**Stay on the built-in pipeline.** Lowest-risk for this milestone, but it
delays every rendering feature and forces a second migration later. The built-in
path is the legacy path in Unity 6.

**HDRP.** Rich, but far heavier than a 137-body orrery needs, and it would
change the cost model and the shader work substantially.

**URP with the default renderer settings.** The default asset enables HDR,
shadow support, and post-processing hooks that the prototype would never use.
Creating the asset through the editor API let us turn those off explicitly.

**Hand-authored pipeline YAML.** Rejected: a generated asset imported by the
real editor proves the references and serialized version, and the editor writes
the global settings and camera data correctly.

# Revisit when

* bloom, SSAO, or a local light/shadow pass is added (enable only what the
  feature needs, and only near the ship),
* the two-camera far field is needed for depth precision,
* the project moves to linear colour space,
* URP's render graph or a newer URP version becomes the default path,
* a PlayMode suite can assert the rendered frame rather than the pipeline state.
