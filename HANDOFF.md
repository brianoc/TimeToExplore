# Handoff — system map step 2: rig integration

**From:** the session that landed step 1 (commit `d93aecb`).
**Date:** 2026-11-16.
**Project:** Time to Explore, Unity 6000.3.28f1 (built-in pipeline, old Input Manager).
**Milestone:** 2.5, the system map and route preview.

## Read first

1. `AGENTS.md` and `Docs/working-agreement.md`.
2. `Docs/technical/system-map.md` — the design. Step 1 is done; **step 2 is this
   session**. Do not jump ahead to steps 3–7.
3. `Docs/decisions/adr-008-orbit-camera.md` — the camera contract being extended.
4. `Docs/technical/selection-and-camera.md` — the rig as built.
5. `Assets/Scripts/Player/MapView.cs` and `Assets/Tests/EditMode/MapViewTests.cs`
   — the step-1 API and its tests.
6. `Assets/Scripts/Player/ShipCameraRig.cs`, `OrbitCamera.cs`, and
   `SolarSystemBootstrap.cs` — where the integration lands.

## What step 1 left you

All plain C#, tested, no scene dependency. Commit `d93aecb` adds:

- `MapView.BlendWeight(distanceUnits, localRadiusM)` — `x = clamp(ln(d/r)/ln(3), 0, 1)`,
  `w = x²(3−2x)`.
- `MapView.EffectivePitchDegrees(playerPitch, distanceUnits, localRadiusM)` —
  lerps the player's pitch toward 90°. It **reads** the pose; nothing writes
  `OrbitCamera.YawDegrees` or `PitchDegrees`.
- `MapView.DominantBody(model, shipPositionM)` / `LocalRadiusM(...)` — the
  smallest finite SOI containing the ship, with `DeepSpaceRadiusM = 1e9 m`
  (1 million km) as the fallback. At Gateway this is the Moon (SOI 66,194 km).
- `MapView.MaxDistanceUnits = 12_000_000f` (12 billion km), `MinDistanceUnits`
  = `OrbitCamera.MinDistanceUnits`, `ClampDistanceUnits`, and
  `ZoomDistanceUnits(currentUnits, steps, factor)`.
- Pan state in ecliptic metres: `PanOffsetM`, `PanOffsetUnits` (Unity render
  space), `Pan`, `PanM`, `SetPanM`, `ClearPan`, capped at `MaxPanM = 60 AU`.
- 8 `MapViewTests`; the EditMode suite is **220 tests**, all green.

`OrbitCamera`, the rig, rendering, `FlightSession`, and every scene were left
untouched in step 1.

## Scope for this session: step 2 only

> **Rig integration.** The top-down pose with a fixed up at the pole, input
> routing (right-drag orbits when local, pans when map), the near plane at map
> distances, the pan ease-out on re-entry. Extend `CameraRigTests`.

Deliver the map pose and controls. Do **not** touch map drawing, picking,
marker/label rules, the local aid fade, the route preview, `FlightSession`, or
the renderer; those are steps 3–6.

### Behaviour to implement

1. **Blend the pose.** Per frame, compute `r = MapView.LocalRadiusM(model, ship)`
   and `w = map.BlendWeight(orbit.DistanceUnits, r)`. The effective pitch is
   `map.EffectivePitchDegrees`, clamped only for the local side; the map side
   reaches 90°. The player's stored yaw and pitch are never overwritten, so
   zooming out and back restores the exact remembered pose.
2. **Top-down pose and the pole.** `OrbitCamera.Rotation` uses
   `Quaternion.LookRotation(-OffsetDirection, Vector3.up)`, which is degenerate
   at 90°. Build the map pose with a yaw-dependent horizontal screen-up instead
   (the design calls it "ecliptic +Z projected"; settle the exact vector and
   test it). Yaw must survive, so the map keeps the player's orientation rather
   than snapping north-up.
3. **Input routing.** Right-drag orbits when local and pans when `w > 0.5`.
   The wheel keeps the multiplicative 1.25× per notch. Left click stays
   selection and is out of scope. Choose a pan-per-pixel scale tied to camera
   distance (so the content follows the cursor at any zoom) and keep it a pure,
   testable function.
4. **Look at `ship + pan`.** In the map the camera looks at the pan offset, not
   at the render origin. Keep the pan in double-precision metres and convert
   once at the camera. This bends ADR-008's "always looks at the ship"; ADR-009
   is still deferred to step 7, but note the bend in the rig and the docs.
5. **Pan ease-out on re-entry.** Returning to the local band eases the pan to
   zero so the local view lands on the ship, not empty space. Expose the rate,
   do not snap, and test convergence. Clicking the ship marker to recentre is
   picking work (step 3), not this step.
6. **Map zoom range.** `OrbitCamera.Zoom` and `SmoothDistance` still clamp to
   5,000,000 units, so a direct call stops at 33 AU. Route map-side zoom
   through `MapView.ZoomDistanceUnits` (or add an explicit range parameter),
   and make sure auto-framing cannot silently undo a map zoom.
7. **Near plane.** Today it is `clamp(0.02 × distance, 0.0002, 0.05)` units.
   At the 12,000,000-unit ceiling that is 50 km against a 20,000,000-unit far
   clip. Choose and test a map-distance rule that keeps depth sane; check the
   far clip and report what you chose.
8. **Keep the local view identical.** At `w = 0` the camera pose, framing,
   near plane, and clamp behaviour must match today's orbit camera.

### Known traps

- `OrbitCamera` clamps the *stored* pitch to ±85°. Only the effective, blended
  pitch may go to 90°.
- `Mathf.Lerp` clamps its `t`, fine; `Quaternion.LookRotation` does not tolerate
  forward parallel to up. Guard the exact-pole case.
- `AvoidBodies` pulls the camera out of bodies and may fight the map pose; check
  it at map distances and at the 60 AU pan limit.
- `ShipCameraRig.HandleInput` clears `AutoDistance` on a wheel notch and
  `AutoAim` on a drag. Decide how auto-framing behaves on the map (the player's
  yaw should remain the map yaw) and cover the choice.
- At 60 AU a float render unit is about 1,000 km; the design accepts that for
  markers. Do not turn the pan into float state.
- The ship stays at the render origin (ADR-002). The map is a camera pose, not
  a new coordinate space.

## The comparison scene the user asked for

The user wants the new camera in a **new scene** so they can compare it with the
current one later. Step 1 had no visible behaviour, so the scene was deferred to
this session. Before finishing:

- Add `Assets/Scenes/SystemMap.unity` (a copy of `SolarSystem.unity`). The
  scene holds only the camera and `SolarSystemBootstrap`; the rig is added at
  runtime.
- Add a scene-serialized switch on `SolarSystemBootstrap` (for example
  `EnableMapView`, default `false`) and pass it to `ShipCameraRig`. Set it true
  in `SystemMap.unity`; leave `SolarSystem.unity` on the current camera. This is
  a development comparison switch, not a gameplay mode.
- Extend `SceneSmokeTests` to load the new scene as well, and keep the existing
  shipped-scene test green.
- If you add an editor menu item, follow `SolarSystemSceneBuilder`; do not
  hand-edit scene YAML without opening the result in batch mode.
- Record the switch and the comparison scene in the log. Remove the switch (or
  default it on) in a later step once the comparison is done.

## Tests to add (minimum)

Extend `CameraRigTests` (and `MapViewTests` if you extract new pure maths):

- **Map pose at `w = 1`:** effective pitch is 90°, the rotation is finite, the
  up vector is horizontal and rotates with yaw.
- **Exact round trip:** zoom out to the map and back to `d = r`; stored yaw and
  pitch are unchanged and the look-at point is the ship again.
- **Input routing:** `w > 0.5` chooses pan, below chooses orbit, as a pure
  decision; the wheel still zooms multiplicatively.
- **Extended zoom:** map-side zoom reaches 12,000,000 units and clamps there;
  the local minimum is still 2,000 km.
- **Pan:** a map drag changes the look-at offset, the 60 AU cap holds through the
  rig, and returning to the local band eases the offset to zero.
- **Near plane:** positive, finite, and follows the chosen map-distance rule.
- The existing 220 tests stay green.

## Definition of done

1. The map pose, routing, map zoom, pan, and ease-out work as above.
2. `Assets/Scenes/SystemMap.unity` exists and loads with the new behaviour;
   `SolarSystem.unity` still plays with the old camera.
3. Docs updated as changes happen:
   - `Docs/technical/system-map.md` — mark step 2 done and note any deviation.
   - `Docs/technical/selection-and-camera.md` — the blended pose, the map input
     table, and the pan rule.
   - `Docs/technical/architecture.md` — the rig's new responsibilities.
   - `Docs/technical/testing.md` — the new tests and the new total.
   - `Docs/production/roadmap.md` — milestone 2.5 progress.
   - `Docs/log.md` — dated **Creation**/**Update** entries under `2026-11-16`
     (or the current date).
4. `tools/check.sh` full run, not just `--fast`, and report the exact count.
5. Commit as the `brianoc` identity (`user.name=brianoc`,
   `user.email=brianoc@users.noreply.github.com`). There is no remote
   configured; the commit stays local unless one is added.

## Baseline verification

At commit `d93aecb`:

```text
tools/check.sh --fast
  27 concepts, 0 warnings, 0 errors
  Catalogue OK: 137 bodies at JD 2461359.5
  Generator output byte-identical

tools/check.sh
  Unity EditMode: 220 tests, 0 failed, 0 skipped
  All checks passed.
```

PyYAML lives in `.venv/bin/python`; `check.sh` already prefers it. The full
Unity run uses a throwaway copy of the project, so it is safe with the editor
open.
