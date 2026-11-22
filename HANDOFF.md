# Handoff

**Plan: make the brightest stars glow (HDR + bloom). Steps 1–2 done; step 3
(camera post-processing) next.**

Step 1 (post-process data) and step 2 (HDR) are committed on `work/sky-bloom`;
`git log -2` shows them (`Assign URP post-process data to the renderer`, `Turn
on HDR for the brightest stars`). Each commit holds its code, test, concept
update, `Docs/log.md` entry, and this file. Steps 3–6 below are still open.

## Why

A real star now writes up to about 2.4 in HDR units, but HDR is off, so the
display clamps it to flat white and Sirius, Vega, and Rigel all have the same
blobby core. HDR plus a bloom threshold around 1.0 gives the brightest few a
glow and lets the band (peak 0.55) stay out of it. The star shader needs no
change: nothing saturates `gain`. The HUD is a Screen Space Overlay canvas, so
it draws after post-processing and cannot be smeared — that is the usual reason
bloom is a bad idea in a UI-heavy scene, and it does not apply here.

## Steps

1. **Assign post-process data. Done.** `Assets/Settings/TimeToExploreUniversalRenderer.asset`
   now points at the package's `Runtime/Data/PostProcessData.asset`
   (`guid: 41439944d30ece34e96484bdb6645b55`), where it had
   `postProcessData: {fileID: 0}` and therefore no bloom shaders at all.
   `RenderingPipelineTests.TheRendererCarriesThePostProcessData` guards it. No
   visible change: the camera post-processing and the Volume are still off.
2. **HDR on. Done.** `Assets/Settings/TimeToExploreUniversalRenderPipeline.asset`
   has `m_SupportsHDR: 1` and keeps `m_HDRColorBufferPrecision: 0`, URP's
   32-bit R11G11B10 buffer, not 64-bit RGBA16F; MSAA stays off, as do render
   scale, the texture requests, and the lights. `ThePipelineStaysCheap` now
   asserts HDR is on and pins the 32-bit buffer, with the inversion recorded in
   [the log](/Docs/log.md). No visible change: nothing runs without step 3.
3. **Post-processing on the camera.** `SolarSystem.unity` has
   `m_RenderPostProcessing: 0` (line 277), and the runtime camera is built in
   `SolarSystemBootstrap.cs` (~line 156), so set
   `UniversalAdditionalCameraData.renderPostProcessing` there too or the game
   still will not bloom.
4. **A Volume with Bloom.** There is none in the scene and
   `Assets/DefaultVolumeProfile.asset` has `components: []`. Global volume,
   bloom threshold near 1.0, plus a Tonemapping override (Neutral) or the cores
   become white discs.
5. **The cheapness guard.** Done with step 2: `ThePipelineStaysCheap` asserts
   HDR is on and the buffer is 32-bit; the other cheapness rules are untouched.
6. **Retune.** The procedural beacons have gain 2.2, so they bloom alongside the
   real stars; lower their gain so Sirius stays the special one. Check the Sun's
   marker and the orbit lines, which may also exceed 1. Then capture and compare.

## Stop and ask before crossing this line

**Gamma vs Linear.** The project renders in Gamma (`m_ActiveColorSpace: 0`).
Post-processing runs, but HDR and tonemapping are designed for Linear, and a
tonemapper fed gamma values looks muddy. Switching is a different, much larger
change: every material, orbit-line colour, and `BAND_PEAK` would need retuning
and re-shooting. Do this branch in Gamma and show the result first.

## Verification

* While iterating: `tools/check.sh --tests presentation`. Last result on the
  step-2 commit: 0 failed, 0 skipped (`All checks passed in 81 s`, warm cache).
  It also runs the fast gates.
* Fast only: `tools/check.sh --fast` — passed on the step-2 commit (6 s).
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
  still clamps at 1.0. HDR is on now — do not conclude "HDR did not work"
  before step 3 lands.
* Bloom thresholds are in HDR units here: band peak 0.55, catalogue stars up to
  ~2.4, procedural beacons ~2.2.
* The skybox is a `Background`-queue pass, so it does bloom — that is the point.
* Do not run Unity batch against the main checkout; an editor has it open. Use
  the worktree's own `.cache/test-project`.
* Leave `TimeToExplore/ProjectSettings/ProjectSettings.asset` alone: it has
  unrelated local edits in the main checkout only.

## Docs to update

`Docs/technical/rendering-scale.md` (the cost paragraph now explains why HDR is
on; item 7 records that bloom has started),
`Docs/technical/sky.md` (the cost and appearance paragraph that assumes
clipping), and `Docs/technical/testing.md`.
