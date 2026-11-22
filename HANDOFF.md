# Handoff

**Plan: make the brightest stars glow (HDR + bloom). Steps 1–4 done; step 5
(retune the procedural beacons, then capture and compare) next.**

Steps 1–4 are committed on `work/sky-bloom`; `git log -4` shows them (`Assign
URP post-process data to the renderer`, `Turn on HDR for the brightest stars`,
`Run post-processing on the scene and runtime camera`, `Bloom the brightest
stars through the default volume profile`). Each commit holds its code, tests,
concept update, `Docs/log.md` entry, and this file. Steps 5–6 below are still
open.

## Why

A real star now writes up to about 2.4 in HDR units. With HDR off, as it was
before this plan, the display clamps that to flat white and Sirius, Vega, and
Rigel all have the same blobby core. HDR plus a bloom threshold around 1.0
gives the brightest few a glow and lets the band (peak 0.55) stay out of it.
The star shader needs no change: nothing saturates `gain`. The HUD is a Screen Space Overlay canvas, so
it draws after post-processing and cannot be smeared — that is the usual reason
bloom is a bad idea in a UI-heavy scene, and it does not apply here.

## Steps

1. **Assign post-process data. Done.** `Assets/Settings/TimeToExploreUniversalRenderer.asset`
   now points at the package's `Runtime/Data/PostProcessData.asset`
   (`guid: 41439944d30ece34e96484bdb6645b55`), where it had
   `postProcessData: {fileID: 0}` and therefore no bloom shaders at all.
   `RenderingPipelineTests.TheRendererCarriesThePostProcessData` guards it. No
   visible change on its own.
2. **HDR on. Done.** `Assets/Settings/TimeToExploreUniversalRenderPipeline.asset`
   has `m_SupportsHDR: 1` and keeps `m_HDRColorBufferPrecision: 0`, URP's
   32-bit R11G11B10 buffer, not 64-bit RGBA16F; MSAA stays off, as do render
   scale, the texture requests, and the lights. `ThePipelineStaysCheap` now
   asserts HDR is on and pins the 32-bit buffer, with the inversion recorded in
   [the log](/Docs/log.md). No visible change on its own.
3. **Post-processing on the camera. Done.** All three agree:
   `SolarSystem.unity` has `m_RenderPostProcessing: 1`, `SolarSystemSceneBuilder`
   adds and enables `UniversalAdditionalCameraData`, and
   `SolarSystemBootstrap.EnsureCamera` sets
   `ViewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true`.
   Presentation and Editor gained the
   `Unity.RenderPipelines.Universal.Runtime` asmdef reference. No visible change
   until the profile landed in step 4.
4. **Bloom and tonemapping. Done.** `Assets/DefaultVolumeProfile.asset` is the
   bloom home: URP registers it in its global settings, so it is global, needs no
   scene Volume object, and reaches the capture tool and the tests too. Bloom
   threshold 1.0, intensity 0.6, scatter 0.65, Gaussian, no lens dirt; Neutral
   Tonemapping. `RenderingPipelineTests.TheDefaultVolumeProfileBloomsAboveTheBandAndTonemapsTheHighlights`
   guards it. The EditMode assembly gained
   `Unity.RenderPipelines.Core.Runtime` for the volume types.
5. **The cheapness guard.** Done with step 2: `ThePipelineStaysCheap` asserts
   HDR is on and the buffer is 32-bit; the other cheapness rules are untouched.
6. **Retune and show it. Next.** The procedural beacons have gain 2.2, so they
   bloom alongside the real stars; lower their gain so Sirius stays the special
   one. Check the Sun's marker and the orbit lines, which may also exceed 1.
   Then capture and compare — bloom is now a visible change, so this step owes
   screenshots (or a short video), not just a green suite.

## Stop and ask before crossing this line

**Gamma vs Linear.** The project renders in Gamma (`m_ActiveColorSpace: 0`).
Post-processing runs, but HDR and tonemapping are designed for Linear, and a
tonemapper fed gamma values looks muddy. Switching is a different, much larger
change: every material, orbit-line colour, and `BAND_PEAK` would need retuning
and re-shooting. Do this branch in Gamma and show the result first.

## Verification

* While iterating: `tools/check.sh --tests presentation`. Last result on the
  step-4 commit: 0 failed, 0 skipped (`All checks passed in 85 s`, warm cache).
  It also runs the fast gates.
* Fast only: `tools/check.sh --fast` — passed on the step-4 commit (6 s).
* Before finishing: `tools/check.sh` — last full result at `63a5e47` was
  EditMode and PlayMode green, 0 failed, 0 skipped, 598 s. The caretaker runs
  it at the end of the plan.
* Look, do not assume: `Assets/Editor/CaptureSky.cs` renders the real material
  offscreen. The command is in [The sky](/Docs/technical/sky.md#verification);
  it writes Orion, the Plough, and a catalogue-only pass into
  `SKY_CAPTURE_DIR`.

## Traps

* A null `postProcessData` fails silently — HDR and a Volume will appear to do
  nothing. That was step 1's bug, and the new test now catches it.
* HDR alone changes nothing visible without bloom and a tonemapper; the display
  still clamps at 1.0. All of it is wired now, so a missing halo points at the
  profile, the camera flag, or the renderer's `postProcessData` — the three
  silent failures this branch already fixed.
* A URP camera defaults to `renderPostProcessing` off, silently. Keep the scene
  YAML, the scene builder, and `EnsureCamera` in step; `SceneSmokeTests` and
  `BootstrapSmokeTests` guard the first and third.
* The bloom is global through the default profile. Do not add a scene Volume;
  it would double the effect and add an object the tests then have to keep.
* Bloom thresholds are in HDR units here: band peak 0.55, catalogue stars up to
  ~2.4, procedural beacons ~2.2.
* The skybox is a `Background`-queue pass, so it does bloom — that is the point.
* Do not run Unity batch against the main checkout; an editor has it open. Use
  the worktree's own `.cache/test-project`.
* Leave `TimeToExplore/ProjectSettings/ProjectSettings.asset` alone: it has
  unrelated local edits in the main checkout only.

## Docs to update

`Docs/technical/rendering-scale.md` (the cost paragraph now explains why HDR is
on and that one camera runs post-processing; items 4 and 7 record what has
landed and what has not),
`Docs/technical/sky.md` (the cost and appearance paragraph that assumes
clipping), and `Docs/technical/testing.md`.
