---
type: Reference
title: Input
description: The input map, the Input System seam, the uGUI input module, why Active Input Handling is New only, and the mouse and wheel rules.
tags: [technical, input, unity, controls, ui]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# Two readers, one contract

The project reads the Input System in exactly two places, and neither reads the
other's mapping:

| File | Responsibility |
|---|---|
| `Assets/Scripts/Player/PlayerControls.cs` | the **gameplay seam**: keyboard and mouse intents for the camera, free flight, hotkeys, world picking, and the flight |
| `Assets/Scripts/UI/UiInput.cs` | the **uGUI bridge**: creates the one `EventSystem` with an `InputSystemUIInputModule` and answers "is the pointer over a panel?" |

`PlayerControls` polls `Keyboard.current` and `Mouse.current` directly and
exposes intents: a hotkey set, a pointer, a left click, a right-drag begin/end,
a drag delta, wheel steps, movement axes, and a speed multiplier. `UiInput` owns
no gameplay mapping: it hands uGUI the Input System's default UI actions
(`Point`, `Click`, `ScrollWheel`, `Navigate`, `Submit`, `Cancel`) so buttons
receive pointer events. No runtime script calls the legacy `UnityEngine.Input`
API.

`InputSeamTests` enforces all of this by scanning the runtime sources: no legacy
input outside `PlayerControls`, and `UnityEngine.InputSystem` named by exactly
those two files. A third call site fails the suite before it can add a second
input dependency.

Direct polling is deliberate. The scenes and the HUD are built at runtime, and a
hand-written `InputActionAsset` would add an asset to load and a layer nothing
else needs. The Input System's own `DefaultInputActions` is a code-built asset
owned by the UI module, so uGUI works without an import. If rebinding or gamepad
support arrives, a generated asset can be loaded at runtime and kept behind the
same seam.

# Active Input Handling is New only

`ProjectSettings/ProjectSettings.asset` sets `activeInputHandler: 1`, Unity's
**Input System Package (New)** option. The IMGUI HUD that forced **Both**
([ADR-011](/decisions/adr-011-input-system.md)) is gone: the HUD is uGUI, whose
`InputSystemUIInputModule` consumes Input System actions directly. Nothing in
the project reads the legacy Input Manager, so the legacy backend is no longer
initialised at all.

`ProjectSettings/InputManager.asset` is still present because Unity maintains it;
it is inert. It is not read, and it is not deleted by hand. The setting and the
two-file allowance are pinned by `InputSeamTests`, and the decision is recorded
in [ADR-013](/decisions/adr-013-ugui-hud.md).

## Restart after changing Active Input Handling

Unity bakes the active backend into compile defines (`ENABLE_INPUT_SYSTEM`,
`ENABLE_LEGACY_INPUT_MANAGER`), and package assemblies keep the branch they were
compiled with until the Editor restarts. If the setting is flipped in
`ProjectSettings.asset` while the Editor is open, the packages can stay on the
old branch while the runtime uses the new one. The symptom is a stream of:

```text
InvalidOperationException: You are trying to read Input using the UnityEngine.Input class,
but you have switched active Input handling to Input System package in Player Settings.
  at UnityEngine.Input.GetButton(...)
  at UnityEngine.Rendering.DebugManager.SampleAction(...)
  at UnityEngine.Rendering.DebugManager.UpdateActions()
  at UnityEngine.Rendering.DebugUpdater.Update()
```

That is URP's runtime **Rendering Debugger**, not project code. Restarting the
Editor recompiles `Unity.RenderPipelines.Core.Runtime` with the new defines and
the exceptions stop. The source guard cannot catch this: the C# is clean and
`activeInputHandler` is correct; the stale artifact lives in
`Library/ScriptAssemblies`.

# The uGUI input path

The HUD builds its Canvas and panels at runtime. The EventSystem is created on
demand:

1. `UiInput.EnsureEventSystem` looks for any `EventSystem` in the scene,
   including inactive ones.
2. If one exists it is reused, so an authored EventSystem is never
   double-equipped, and `sendNavigationEvents` is turned off.
3. Otherwise a GameObject named `EventSystem` is created with an `EventSystem`
   and an `InputSystemUIInputModule`, `AssignDefaultActions()` gives it the
   Input System's default UI actions, and navigation events are turned off.

Navigation is off because the old IMGUI HUD was pointer-only. It keeps Space
(pause), Enter, and the arrow keys from re-submitting whichever button was last
clicked.

The HUD calls `UiInput.PointerOverUi()` — `EventSystem.current` plus
`IsPointerOverGameObject()` — before world picking and hover. Because that answer
can lag the EventSystem by one frame, the HUD also tests the status, details, and
flight panel rects immediately with `RectTransformUtility`. A panel background is
a raycast target, so pointer-down on it is swallowed by uGUI and the world never
sees the click. Labels, the hover prompt, and the crosshair have
`raycastTarget = false`.

World picking itself is unchanged and still goes through `PlayerControls`: the
HUD reads the pointer and the left-click edge from the seam, projects the drawn
markers, and calls `BodyPicker`. The EventSystem knows about panels; the seam
knows about the world. No device is read twice for the same decision.

# The input map

| Input | Intent | Consumer |
|---|---|---|
| `Space` | pause / resume | `SolarSystemBootstrap` |
| `1`–`7` | requested time warp 1× to 1,000,000× | `SolarSystemBootstrap` |
| `Tab` | cycle planet/station target | `SolarSystemBootstrap` |
| `F` | focus the selected body (debug jump) | `SolarSystemBootstrap` |
| `Esc` | cancel a debug jump; close the details screen | `SolarSystemBootstrap`, `SolarSystemHud` |
| `R` | reset to Gateway and 1× | `SolarSystemBootstrap` |
| `X` | cancel the flight | `SolarSystemBootstrap` |
| `` ` `` | toggle debug free flight | `SolarSystemBootstrap` |
| `T` | toggle true scale | `SolarSystemBootstrap` |
| `O` | toggle orbits | `SolarSystemBootstrap` |
| `L` | toggle labels | `SolarSystemBootstrap` |
| `H` | toggle the HUD | `SolarSystemBootstrap` |
| `W` `S` / `A` `D` / `E` `Q` | free flight: forward-back / left-right / down-up | `ShipController` |
| `Shift` / `Ctrl` | free flight: ×10 / ×0.1 speed | `ShipController` |
| `,` / `.` | free flight: decrease / increase speed | `ShipController` |
| Right mouse drag | orbit the camera; pan on the map side | `ShipCameraRig` |
| Mouse wheel | zoom the camera | `ShipCameraRig` |
| Left click | select the hovered body | `SolarSystemHud` |
| Panel buttons | plot/replan, begin, jump, close | `SolarSystemHud` → `SolarSystemBootstrap` |

`Esc` keeps its old order: the bootstrap (execution order −100) cancels the
focus transition first, then the HUD closes the details screen. Both actions run
in the same frame. uGUI navigation events are off, so `Escape` reaches only the
seam's edge and cannot also be consumed as a UI Cancel action.

Digits are an **explicit table** in `PlayerControls`, not `Key.Digit1 + index`
arithmetic: `1`–`9` and `0` map to indices 0–9. The clock currently has seven
scales, and the bootstrap ignores an index past the end, so pressing `8`, `9`,
or `0` does nothing — exactly as before. If two number keys are down in the same
frame the highest wins, matching the old ascending scale loop.

# Mouse feel

The legacy `Mouse X` and `Mouse Y` axes applied the Input Manager's default
**0.1** sensitivity, so one axis unit was ten pixels. The seam reproduces that:

```csharp
Vector2 axis = rawPixels * PlayerControls.LegacyMouseAxisSensitivity; // 0.1
```

The camera's constants are unchanged, so the feel is unchanged:

* `OrbitDegreesPerMouseUnit = 3` → **0.3° per raw pixel** of drag.
* `PanPixelsPerMouseUnit = 10` → the map content follows the cursor at **1 px
  per raw pixel**.

While dragging, the rig locks and hides the cursor as before, and releasing the
right button unlocks and shows it. The HUD ignores the pointer while the cursor
is locked, so a drag never selects a body.

# Wheel normalisation

`Mouse.current.scroll.y` is not portable: Windows reports **120 units per
notch**, while macOS reports about **1**. `PlayerControls.NormaliseScroll`
divides by 120 only on `WindowsPlayer` and `WindowsEditor`; every other platform
passes the value through. One notch therefore produces one
`ZoomFactorPerStep` step (1.25×) on both ranges, and a trackpad's fractional
scroll is still kept. `ShipCameraRig.WheelDistanceUnits` is untouched, so the
local 5-billion-km clamp and the map's 12-billion-km ceiling still route by the
blend weight.

The wheel belongs to the camera. Free-flight speed uses `,` and `.`; the two
controls cannot fight.

# Screen spaces

Two coordinate systems survive the migration, and they meet in the HUD:

* **Picking** uses top-left screen space, because `BodyPicker` and the drawn
  marker centres were written that way. `PlayerControls.ToGuiPointer` converts
  the Input System's bottom-left pointer with `y = Screen.height − y`.
* **uGUI labels** are placed from the bottom-left screen point directly, divided
  by `Canvas.scaleFactor`, because the Canvas is a 1920×1080 reference scaled to
  the window.

The panels themselves are anchored to the canvas corners (status top-left,
details top-right, flight bottom-left) and never read the pointer.

# Test seam

Every mapping is a pure function that takes its device (or a
`Func<Key, bool>` key predicate) explicitly:

| Helper | Proves |
|---|---|
| `ScaleIndexForDigit` | the number-key table, independent of enum order |
| `ReadHotkeys` | pause, every toggle, digits, and the highest-digit rule |
| `ReadMoveAxes` | `WASDQE` → local `(right, up, forward)` |
| `ReadSpeedMultiplier` | Shift ×10, Ctrl ×0.1, Ctrl wins |
| `ReadSpeedSteps` | period +1, comma −1 |
| `ReadMouseAxis` | the 0.1 sensitivity |
| `NormaliseScroll` | macOS ≈ 1 and Windows 120 → 1 |
| `ToGuiPointer` | the screen-to-picking flip |

Each read is null-guarded, so a headless batch run and a machine without a
keyboard or mouse return neutral values instead of throwing. `InputMappingTests`
covers the pure helpers and the null case; `InputDeviceTests` feeds a virtual
`Keyboard` and `Mouse` through `InputTestFixture` to prove the device edges
arrive as intents. The fixture is documented as a Play mode tool, but it works
in EditMode for direct state reads because it queues device state and updates
the Input System itself; it does not run the player loop, so frame order is
covered by the source guard and the bootstrap tests, not by input simulation.

The guard test scans `Assets/Scripts` for `UnityEngine.Input` and for legacy
`Input.Get…`/`Input.mouse…` calls, excluding only `PlayerControls.cs`. A second
check asserts that `UnityEngine.InputSystem` appears in exactly two runtime
files, `PlayerControls.cs` and `UiInput.cs`. A third reads
`ProjectSettings/ProjectSettings.asset` and pins `activeInputHandler: 1`. A
fourth scans for an `OnGUI` method, `GUI.*` calls, `new GUIStyle`, and
`Texture2D.whiteTexture`, so an IMGUI fallback cannot return. See
[Testing and verification](/technical/testing.md).

# What still needs a human

The suite proves the mapping and the object graph, not the feel. In Play mode,
check the right-drag orbit rate and map pan at a few zoom levels, the cursor
capture and release, one wheel notch on the target OS, every panel button, that
a click on a panel does not also select the body behind it, and that the click
which opens the details screen cannot press a button underneath. The
`EventSystem`'s pointer-over-panel answer depends on the previous frame's
raycast; the immediate panel-rect test covers the first frame, but a real
pointer is still the human check. See
[Selection and camera focus](/technical/selection-and-camera.md) for the camera
rules themselves.
