---
type: Reference
title: Input
description: The input map, the Input System seam, why Active Input Handling stays on Both, and the mouse and wheel rules.
tags: [technical, input, unity, controls]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# One seam

All device reading lives in `Assets/Scripts/Player/PlayerControls.cs`
(`TimeToExplore.Player`). It polls `Keyboard.current` and `Mouse.current`
directly and exposes intents: a hotkey set, a pointer, a left click, a right-drag
begin/end, a drag delta, wheel steps, movement axes, and a speed multiplier.
No other runtime script mentions the Input System, and no runtime script calls
the legacy `UnityEngine.Input` API. `InputSeamTests` enforces both rules by
scanning the runtime sources, so a new call site fails the suite before it can
add a second input dependency.

Direct polling is deliberate. The scenes and the HUD are built at runtime, and
the HUD is IMGUI, which reads pointer events from the legacy backend rather than
from `InputAction`s. There is no `InputActionAsset`: an action map would add an
asset to load and a layer that nothing else needs. If rebinding or gamepad
support arrives, a generated asset can be loaded at runtime and kept behind the
same seam.

# Active Input Handling is Both

`ProjectSettings/ProjectSettings.asset` sets `activeInputHandler: 2`, which is
Unity's **Both** option. This is not indecision; it is required by the HUD.

The status panel, the details screen, the flight panel, and every button are
IMGUI (`SolarSystemHud.OnGUI`, `GUI.Button`, `GUI.DrawTexture`). Unity's
documented behaviour is that under **Input System Package (New)** alone, runtime
`OnGUI` methods receive no input events, so every button would stop responding.
**Both** keeps the legacy backend alive to feed IMGUI while the Input System
feeds the gameplay seam. `ProjectSettings/InputManager.asset` remains in place
for that backend, but no project code reads its `Mouse X`, `Mouse Y`, or
`Mouse ScrollWheel` axes.

The cost is that a second input backend is still initialised. That is the
documented workaround for IMGUI, and it is recorded in
[ADR-011](/decisions/adr-011-input-system.md). A future HUD migration to UI
Toolkit or uGUI could drop the legacy backend and set **New** only.

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

`Esc` keeps its old order: the bootstrap (execution order −100) cancels the
focus transition first, then the HUD closes the details screen. Both actions
still run in the same frame.

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

# IMGUI pointer space

`Mouse.current.position` uses the screen's bottom-left origin, the same as the
old `Input.mousePosition`. `PlayerControls.ToGuiPointer` converts it to IMGUI's
top-left origin (`y = Screen.height − y`), and the HUD's hit tests and hover
prompt use that.

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
| `ToGuiPointer` | the screen-to-IMGUI flip |

Each read is null-guarded, so a headless batch run and a machine without a
keyboard or mouse return neutral values instead of throwing. `InputMappingTests`
covers the pure helpers and the null case; `InputDeviceTests` feeds a virtual
`Keyboard` and `Mouse` through `InputTestFixture` to prove the device edges
arrive as intents. The fixture is documented as a Play mode tool, but it works
in EditMode for direct state reads because it queues device state and updates
the Input System itself; it does not run the player loop, so frame order is
covered by the source guard and the existing bootstrap tests, not by input
simulation.

The guard test scans `Assets/Scripts` for `UnityEngine.Input` and for legacy
`Input.Get…`/`Input.mouse…` calls, excluding only `PlayerControls.cs`. A second
check asserts that `UnityEngine.InputSystem` appears in exactly one runtime
file. A third reads `ProjectSettings/ProjectSettings.asset` and fails if
`activeInputHandler` is not `2`, so the IMGUI backend cannot be dropped by
accident. See [Testing and verification](/technical/testing.md).

# What still needs a human

The suite proves the mapping, not the feel. In Play mode, check the right-drag
orbit rate and map pan at a few zoom levels, the cursor capture and release,
one wheel notch on the target OS, and every IMGUI button — including that a
click on the details screen still opens it and a drag does not. See
[Selection and camera focus](/technical/selection-and-camera.md) for the camera
rules themselves.
