# Handoff — after the system map and route preview

> **Status (2026-11-17):** **Milestone 2.5 is complete.** The system map blends
> from the orbit camera to a top-down map as the zoom crosses the local sphere
> of influence, the map pans, draws, labels, and picks, the local view stops
> inflating distant bodies, **Plot route → Begin** previews a route with an
> arrival ghost, and the full acceptance runs through the real bootstrap
> ([ADR-009](Docs/decisions/adr-009-map-camera-pan.md),
> [system map](Docs/technical/system-map.md)). The suite is **331 tests**,
> 0 failed, 0 skipped under `tools/check.sh`.
>
> The stack from the previous migration is unchanged: Unity 6000.3.28f1, URP
> 17.3.0, Input System 1.20.1 with Active Input Handling **New** only, uGUI
> 2.0.0, and the **Core ← Simulation ← Presentation ← Editor/Tests** assembly
> rule ([ADR-012](Docs/decisions/adr-012-assembly-layout.md)).

**From:** the system-map acceptance and documentation session.
**Date:** 2026-11-17.
**Project:** Time to Explore, Unity 6000.3.28f1.
**Milestone:** 2.5, the system map and route preview — done.

## Read first

1. `AGENTS.md` and `Docs/working-agreement.md`.
2. `Docs/technical/system-map.md` — the complete design and its as-built steps.
3. `Docs/decisions/adr-009-map-camera-pan.md` — the map camera decision and its
   consequences.
4. `Docs/technical/selection-and-camera.md` — picking, the camera, and the map
   pose.
5. `Docs/technical/flight-session.md` — the frame, the warp cap, the preview,
   and the measured acceptance.
6. `Docs/technical/testing.md` — the one-command suite, the test matrix, and the
   numerical anchors.
7. `Docs/production/roadmap.md` — what is next after milestone 2.5.

## What is done

**System map — steps 1–7** ([The system map](Docs/technical/system-map.md))

* `MapView`, the camera-rig blend, the map drawing/picking rules, the
  neighbourhood-aware aid fade, and Plot route → Begin with the 1% staleness
  window and the arrival ghost are built and tested.
* **Frame-loop acceptance:** `BootstrapSmokeTests` zooms through the rig's real
  wheel routing to the 12-billion-km ceiling and back to the exact remembered
  angle, then starts on the map, selects Mars, plots, presses Begin, and runs the
  bootstrap's own `Update`/`LateUpdate` to arrival. `SolarSystemBootstrap` gained
  a `DeltaTimeSeconds` seam (default `Time.deltaTime`) because EditMode does not
  advance time.
* The bootstrap acceptance reproduces the scene flight exactly: 3,939 frames,
  65.7 real seconds, arrival 2026-11-19 10:29:02 UTC, 0.0 m and 0.0000 m/s, on
  the 3,898 km parking orbit at e = 4.3 × 10⁻⁹.
* `SystemMap.unity` is the comparison scene; `SolarSystem.unity` still leaves
  `EnableMapView` off.

**Earlier migrations (unchanged)**

* URP 17.3.0 with a cheap forward renderer and three runtime shaders
  ([ADR-010](Docs/decisions/adr-010-urp-rendering.md)).
* Input System 1.20.1 behind `PlayerControls` and `UiInput`, New only
  ([ADR-011](Docs/decisions/adr-011-input-system.md),
  [ADR-013](Docs/decisions/adr-013-ugui-hud.md)).
* Layered assemblies enforced by `AssemblyLayoutTests`
  ([ADR-012](Docs/decisions/adr-012-assembly-layout.md)).

## Commits from this session

`7482647` — Accept the system map through the bootstrap frame loop. The docs
commit that adds ADR-009, the concept updates, and this handoff closes the
milestone.

## Next

1. **Default `EnableMapView` on (or remove the switch).** The comparison is done;
   the shipped scene should get the map and the neighbourhood aid fade. Update
   both scenes, `BootstrapSmokeTests`, and the docs that describe the switch.
2. **Bang-bang planner** once fuel makes time and delta-v compete; a transfer
   conic closes part of the 35–75% time gap in delta-v.
3. **Mid-course correction** that keeps the current plan instead of replanning
   from the current state.
4. **Continuous warp** so the drop near a body is smooth rather than a
   power-of-ten step.
5. **PlayMode suite** for the real renderer, pointer hits, wheel feel, and the
   HUD.

## Verification baseline

```text
tools/check.sh --fast
  33 concepts, 0 warnings, 0 errors
  Catalogue OK: 137 bodies at JD 2461359.5
  Generator output byte-identical

tools/check.sh
  Unity EditMode: 331 tests, 0 failed, 0 skipped
  All checks passed.
```

`331 = 320 baseline + 7 SolarSystemHudTests + 2 InputSeamTests guards + 2 map
acceptance tests` (the exact historical split is in the log; the current total
is 331). PyYAML lives in `.venv/bin/python`; `check.sh` prefers it. The Unity run
uses a throwaway copy of `Assets`, `Packages`, and `ProjectSettings`, so it is
safe with the editor open.

## Traps worth knowing

* **Restart the Editor after changing Active Input Handling.** Package
  assemblies keep the branch they were compiled with; a stale SRP Core keeps
  URP's `DebugUpdater` on `Input.GetButton` and throws every frame. See
  [Input](Docs/technical/input.md).
* **The HUD's panel guard is two-layered.** `IsPointerOverGameObject()` can lag
  a frame, so `SolarSystemHud` also tests the three panel rects with
  `RectTransformUtility` before world picking. Keep both if you move panels.
* **uGUI navigation is deliberately off.** Space is pause; do not turn
  `sendNavigationEvents` back on without checking every gameplay key.
* **`Screen.height` is tiny in batch mode.** Test pure pixel/angle functions,
  not raw screen geometry.
* **`Time.deltaTime` is zero in EditMode.** `BodyView`'s marker aid is smoothed;
  settle it explicitly. For frame loops, use
  `SolarSystemBootstrap.DeltaTimeSeconds`; the scene leaves the default alone.
* **The map is a blend band, not a mode.** `w > 0.5` is the input side; markers
  blend continuously. Do not scatter a second threshold.
* **No PlayMode suite yet.** Real pointer clicks, the rendered frame, button hit
  areas, label overlap, and wheel feel still need a human.
* **A map zoom round trip restores the angle, not the distance,** when the
  gesture was clamped at the 12-billion-km ceiling. That is expected.

## Definition of done for the next session

1. Any behaviour change updates the affected `Docs/` concept and adds a dated
   `Docs/log.md` entry.
2. New work has tests; `tools/check.sh` passes with the exact count reported.
3. Commit as `brianoc` (`user.name=brianoc`,
   `user.email=brianoc@users.noreply.github.com`). There is no remote
   configured; commits stay local unless one is added.
4. `Screenshots/` is untracked user material; leave it alone.

## Known limitations

The full list is in
[Architecture](Docs/technical/architecture.md#known-limitations). The ones that
matter most:

* No fuel or delta-v spending; the plan reports delta-v but does not consume it.
* No attitude dynamics or slew limits; thrust direction is instantaneous.
* No mid-flight replan beyond pressing **Replan**.
* No PlayMode suite for rendering, pointer input, or the HUD.
* The map's `EnableMapView` comparison switch still defaults off in the shipped
  scene; it also gates the aid fade.
* The belt is not drawn; 100 asteroid orbits would clutter the map.
