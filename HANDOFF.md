# Handoff — after the URP, Input System, assembly, and uGUI migrations

> **Status (2026-11-17):** the four migrations are landed and committed. The
> project runs **URP 17.3.0**, **Input System 1.20.1** with Active Input
> Handling **New** only, and **uGUI 2.0.0** for the runtime HUD. The runtime is
> split into `TimeToExplore.Core`, `.Simulation`, `.Presentation`, and
> `.Editor`; the rule is **Core ← Simulation ← Presentation ← Editor/Tests**
> ([ADR-012](Docs/decisions/adr-012-assembly-layout.md)). The suite is
> **329 tests**, 0 failed, 0 skipped under `tools/check.sh`.
>
> The Editor has been restarted after the input-backend change. SRP Core
> recompiled without the legacy `Input.GetButton` branch, and the
> `InvalidOperationException` stream from URP's `DebugUpdater` is gone. A
> Play-mode look confirmed the uGUI HUD renders.
>
> Next work is **system map step 6, frame-loop acceptance**, then step 7
> (ADR-009 and the docs pass). Steps 1–5 of the map are done.

**From:** the migration session (URP → Input System → assemblies → uGUI).
**Date:** 2026-11-17.
**Project:** Time to Explore, Unity 6000.3.28f1 (URP 17.3.0, Input System
1.20.1, uGUI 2.0.0, Active Input Handling New only).
**Milestone:** 2.5, the system map and route preview.

## Read first

1. `AGENTS.md` and `Docs/working-agreement.md`.
2. `Docs/technical/architecture.md` — runtime shape, assembly layout, rendering,
   input, and the HUD.
3. `Docs/technical/input.md` — the two input files, New-only, the uGUI module,
   and the Editor-restart gotcha.
4. `Docs/technical/testing.md` — the one-command suite, the test matrix, and the
   numerical anchors.
5. `Docs/technical/system-map.md` — steps 1–5 are done; **step 6 is next**.
6. `Docs/production/roadmap.md` — milestone 2.5 and the deferred work.

## What is done

**Rendering — URP 17.3.0** ([ADR-010](Docs/decisions/adr-010-urp-rendering.md))

* `Assets/Settings/TimeToExploreUniversalRenderPipeline.asset` and
  `TimeToExploreUniversalRenderer.asset`; the pipeline is the graphics default.
* Deliberately cheap: HDR off, MSAA 1, render scale 1, no depth/opaque copies,
  no shadows, no post-processing, intermediate texture mode Auto.
* Three runtime shaders under `Assets/Resources/Shaders/`:
  `TimeToExplore/BodyLit`, `TimeToExplore/Unlit`, `TimeToExplore/Line`.
  `RenderingShaders` centralises the names. `BodyLit` keeps its original GUID.

**Input — Input System 1.20.1** ([ADR-011](Docs/decisions/adr-011-input-system.md),
[ADR-013](Docs/decisions/adr-013-ugui-hud.md))

* `Assets/Scripts/Player/PlayerControls.cs` is the gameplay seam: device polling,
  intents, key table, the legacy 0.1 mouse sensitivity, and wheel normalisation.
* `Assets/Scripts/UI/UiInput.cs` is the uGUI bridge: one `EventSystem` with an
  `InputSystemUIInputModule`, navigation events off, `PointerOverUi`.
* `activeInputHandler: 1`. No runtime code calls `UnityEngine.Input`; only those
  two files name `UnityEngine.InputSystem`. `InputSeamTests` enforces both.
* The HUD is uGUI: one Screen Space Overlay canvas, 1920×1080 `CanvasScaler`,
  runtime-built panels, pooled `Text`, legacy `LegacyRuntime.ttf` (no TMP
  assets), and buttons wired to the same owner methods the IMGUI screen used.
* World picking still goes through `PlayerControls` and `BodyPicker`; panel
  clicks are swallowed by the EventSystem plus an immediate panel-rect guard.

**Assemblies** ([ADR-012](Docs/decisions/adr-012-assembly-layout.md))

* `TimeToExplore.Core` — no project references.
* `TimeToExplore.Simulation` — Core only, `noEngineReferences: true`, so the
  plain-C# flight core cannot take a UnityEngine dependency.
* `TimeToExplore.Presentation` — Core + Simulation + Input System + uGUI.
* `TimeToExplore.Editor` — Editor-only scene builder.
* `AssemblyLayoutTests` reads the `.asmdef` files and fails on a forward
  reference, a stray assembly, or a duplicate GUID.

**System map — steps 1–5** ([The system map](Docs/technical/system-map.md))

* `MapView`, the camera-rig blend, the map drawing/picking rules, the
  neighbourhood-aware aid fade, and Plot route → Begin with the 1% staleness
  window and the arrival ghost are all built and tested.
* `SystemMap.unity` is the comparison scene; `SolarSystem.unity` keeps
  `EnableMapView` off.

## Commits from this session

```text
131f91c Add the URP project settings Unity generated on reopen
4aee3d9 Document the input backend restart gotcha
cf48fa9 Move the HUD to uGUI and drop the legacy input backend
dcdb9b0 Split the runtime into layered assemblies
7a2b592 Move input to the Input System behind one seam
a6e1eda Migrate the prototype to URP 17.3.0
```

## Next: system map step 6

> **Frame-loop acceptance.** Extend `BootstrapSmokeTests`: zoom out to the map,
> select Mars, plot, Begin, arrive — through the real bootstrap.

1. Drive the acceptance through `SolarSystemBootstrap`, not a hand-built
   session: zoom to the map side, select Mars, plot, Begin, advance the frame
   loop, and assert arrival on the parking orbit with the existing exactness.
2. Keep the existing `SceneFlightTests` Gateway→Mars numbers green
   (4.44 days, 0.0 m, 0.0000 m/s, three orbits).
3. Then **step 7**: write ADR-009 (the map bends ADR-008's "always looks at the
   ship"), refresh the camera/flight-session concepts, the roadmap acceptance
   numbers, and the log.
4. Acceptance from the design: one wheel gesture zooms from a parking orbit to
   the whole system and back to the exact prior angle; pan to Jupiter, click it,
   plot a route, see the curve and the arrival ghost, press Begin, and arrive
   exactly.

## Verification baseline

```text
tools/check.sh --fast
  32 concepts, 0 warnings, 0 errors
  Catalogue OK: 137 bodies at JD 2461359.5
  Generator output byte-identical

tools/check.sh
  Unity EditMode: 329 tests, 0 failed, 0 skipped
  All checks passed.
```

`329 = 320 baseline + 7 SolarSystemHudTests + 2 InputSeamTests guards`. PyYAML
lives in `.venv/bin/python`; `check.sh` prefers it. The Unity run uses a
throwaway copy of `Assets`, `Packages`, and `ProjectSettings`, so it is safe
with the editor open.

## Traps worth knowing

* **Restart the Editor after changing Active Input Handling.** Package
  assemblies keep the branch they were compiled with. A stale
  `Unity.RenderPipelines.Core.Runtime.dll` keeps URP's `DebugUpdater` on
  `Input.GetButton` and throws every frame. The symptom and stack are in
  [Input](Docs/technical/input.md); the fix is a full Editor restart, not a
  code change.
* **The HUD's panel guard is two-layered.** `IsPointerOverGameObject()` can lag
  a frame, so `SolarSystemHud` also tests the three panel rects with
  `RectTransformUtility` before world picking. Keep both if you move panels.
* **uGUI navigation is deliberately off.** Space is pause; do not turn
  `sendNavigationEvents` back on without checking every gameplay key.
* **`Screen.height` is tiny in batch mode.** Test pure pixel/angle functions,
  not raw screen geometry.
* **`Time.deltaTime` is zero in EditMode.** `BodyView`'s marker aid is smoothed;
  settle it explicitly or run frames before asserting a drawn size.
* **The map is a blend band, not a mode.** `w > 0.5` is the input side; markers
  blend continuously. Do not scatter a second threshold.
* **No PlayMode suite yet.** Real pointer clicks, the rendered frame, button hit
  areas, label overlap, and wheel feel still need a human. `SolarSystemHudTests`
  invokes `onClick` directly and proves the object graph, not the frame.

## Definition of done for the next session

1. Step 6 acceptance passes through the bootstrap, and the existing flight
   numbers are unchanged.
2. Step 7 docs: ADR-009, the camera and flight-session concepts, the roadmap,
   the testing matrix and totals, and a dated `Docs/log.md` entry.
3. `tools/check.sh` full run, with the exact test count reported.
4. Commit as `brianoc` (`user.name=brianoc`,
   `user.email=brianoc@users.noreply.github.com`). There is no remote
   configured; commits stay local unless one is added.
5. `Screenshots/` is untracked user material; leave it alone.

## Known limitations

The full list is in [Architecture](Docs/technical/architecture.md#known-limitations).
The ones that matter for the next session:

* No fuel or delta-v spending; the plan reports delta-v but does not consume it.
* No attitude dynamics or slew limits; thrust direction is instantaneous.
* No mid-flight replan beyond pressing **Replan**.
* No PlayMode suite for rendering, pointer input, or the HUD.
* The map's `EnableMapView` comparison switch is temporary; it also gates the
  aid fade.
* The belt is not drawn; 100 asteroid orbits would clutter the map.
