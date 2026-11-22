# Handoff

**Plan: make the brightest stars glow (HDR + bloom). Not started.**

`main` is at `63a5e47` — the real bright stars are merged and green. This note
is uncommitted on `main`; commit it as the first commit of `work/sky-bloom`,
then keep it current as each step lands.

## Why

A real star now writes up to about 2.4 in HDR units, but HDR is off, so the
display clamps it to flat white and Sirius, Vega, and Rigel all have the same
blobby core. HDR plus a bloom threshold around 1.0 gives the brightest few a
glow and lets the band (peak 0.55) stay out of it. The star shader needs no
change: nothing saturates `gain`. The HUD is a Screen Space Overlay canvas, so
it draws after post-processing and cannot be smeared — that is the usual reason
bloom is a bad idea in a UI-heavy scene, and it does not apply here.

## Setup

Worktree on `work/sky-bloom` from `main`; `ln -s ~/Personal/TimeToExplore/.venv .venv`.
One commit per step: change, tests, concept updates, `Docs/log.md`, and this
file.

## Steps

1. **Assign post-process data.** `Assets/Settings/TimeToExploreUniversalRenderer.asset`
   has `postProcessData: {fileID: 0}` — post-processing is not merely off, the
   renderer has no bloom shaders. Point it at
   `Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset`.
2. **`m_SupportsHDR: 1`** in `Assets/Settings/TimeToExploreUniversalRenderPipeline.asset`
   (line 26). Watch the float16 colour buffer cost.
3. **Post-processing on the camera.** `SolarSystem.unity` has
   `m_RenderPostProcessing: 0` (line 277), and the runtime camera is built in
   `SolarSystemBootstrap.cs` (~line 156), so set
   `UniversalAdditionalCameraData.renderPostProcessing` there too or the game
   still will not bloom.
4. **A Volume with Bloom.** There is none in the scene and
   `Assets/DefaultVolumeProfile.asset` has `components: []`. Global volume,
   bloom threshold near 1.0, plus a Tonemapping override (Neutral) or the cores
   become white discs.
5. **Update the cheapness guard.** `RenderingPipelineTests.ThePipelineStaysCheap`
   (line 60) asserts `pipeline.supportsHDR` is false with the comment "HDR is
   not required". Changing it is a decision, so say so in the log.
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

* While iterating: `tools/check.sh --tests presentation` (about 80 s warm).
* Before finishing: `tools/check.sh` — last full result at `63a5e47` was
  EditMode 535 tests, PlayMode 6 tests, 0 failed, 0 skipped, 598 s.
* Look, do not assume: `Assets/Editor/CaptureSky.cs` renders the real material
  offscreen. The command is in [The sky](/Docs/technical/sky.md#verification);
  it writes Orion, the Plough, and a catalogue-only pass into
  `SKY_CAPTURE_DIR`.

## Traps

* A null `postProcessData` fails silently — HDR and a Volume will appear to do
  nothing.
* HDR alone changes nothing visible without bloom and a tonemapper; the display
  still clamps at 1.0. Do not conclude "HDR did not work" at step 2.
* Bloom thresholds are in HDR units here: band peak 0.55, catalogue stars up to
  ~2.4, procedural beacons ~2.2.
* The skybox is a `Background`-queue pass, so it does bloom — that is the point.
* Do not run Unity batch against the main checkout; an editor has it open. Use
  the worktree's own `.cache/test-project`.

## Docs to update

`Docs/technical/rendering-scale.md` ("HDR off, no bloom" at line 261, the
post-processing item in the future-work list), `Docs/technical/sky.md` (the cost
and appearance paragraph that assumes clipping), and `Docs/technical/testing.md`.
