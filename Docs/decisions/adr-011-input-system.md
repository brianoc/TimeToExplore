---
type: Decision Record
title: "ADR-011: The Input System behind one seam"
description: Move the project to com.unity.inputsystem 1.20.1, poll devices through PlayerControls, and keep Active Input Handling on Both because the HUD is IMGUI.
tags: [decision, input, unity, technical]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# Context

> **Superseded in part (2026-11-17):** ADR-011's decision item 2 and its
> "Both" consequences are superseded by
> [ADR-013](/decisions/adr-013-ugui-hud.md). The HUD is now uGUI with an
> `InputSystemUIInputModule`, so Active Input Handling is **Input System Package
> (New)** only (`activeInputHandler: 1`) and the legacy backend is no longer
> initialised. Everything else here still stands: package 1.20.1, the
> `PlayerControls` seam, direct device polling, no `InputActionAsset` for
> gameplay, the mouse-sensitivity and wheel conversions, and the test strategy.
> The history below is kept as written.

The prototype read input through the legacy Input Manager:
`Input.GetKey`, `Input.GetAxis("Mouse X")`, `Input.GetMouseButtonDown`,
`Input.mousePosition`, and `Input.mouseScrollDelta`, across
`ShipController`, `ShipCameraRig`, `SolarSystemBootstrap`, and
`SolarSystemHud`. The project's `ProjectSettings.asset` set
`activeInputHandler: 0` (Input Manager only).

Unity 6 still supports that path, but it is the legacy path. The Input System
package is the supported API, it has explicit device types and per-control state,
and it is what future gamepad, rebinding, and multiple-device work will use.
Staying on the old manager would mean a second migration later, exactly as
staying on the built-in render pipeline did.

There is a hard constraint. The entire HUD is IMGUI: `SolarSystemHud.OnGUI`
draws the panels with `GUI.Label`, `GUI.DrawTexture`, and `GUI.Button`. Unity's
documented behaviour is that when Active Input Handling is **Input System
Package (New)**, runtime `OnGUI` methods receive **no input events**, so every
HUD button would stop working. The documented workaround is **Both**.

# Decision

1. **Add `com.unity.inputsystem` 1.20.1** to `Packages/manifest.json` and let
   UPM resolve `packages-lock.json`. This editor's package manifest recommends
   1.20.1 as its minimum, so it matches the editor rather than pinning an older
   release.

2. **Set `activeInputHandler: 2` (Both).** The Input System feeds gameplay; the
   legacy backend exists only to deliver IMGUI events. The project does **not**
   run "New only", and the docs do not claim it does.
   `ProjectSettings/InputManager.asset` stays in place for the legacy backend,
   but no project code reads its axes. No `InputActionAsset` is added: the
   scenes and HUD are built at runtime, and IMGUI does not use actions.

3. **Put every device read behind one seam**, `PlayerControls` in
   `Assets/Scripts/Player/`. It polls `Keyboard.current` and `Mouse.current`
   directly, guards null devices, and exposes intents: a hotkey set (pause,
   scale index, target, focus, cancel, reset, free flight, true scale, orbits,
   labels, HUD), pointer position, left click, right-drag begin/end, drag delta,
   wheel steps, movement axes, speed multiplier, and speed steps. No other
   runtime script mentions the Input System, and none calls the legacy API. A
   source-scanning test enforces both rules, and a project-settings test pins
   `activeInputHandler: 2`.

4. **Preserve the old feel with explicit conversions.**
   * The old `Mouse X`/`Mouse Y` axes applied the Input Manager's 0.1
     sensitivity. The seam multiplies the raw device delta by `0.1`, so the
     rig's 3° per axis unit and 10 px per axis unit keep their meaning.
   * `Mouse.current.scroll.y` is 120 units per notch on Windows and about 1 on
     macOS. The seam divides by 120 only on `WindowsPlayer`/`WindowsEditor`, so
     one notch remains one 1.25× zoom step. `WheelDistanceUnits` is unchanged.
   * Number keys use an explicit `Key.Digit1`…`Key.Digit0` table, not enum
     arithmetic; the bootstrap ignores an index past the clock's last scale, as
     the old loop did.

5. **Test the mapping without a scene.** Every helper takes its device or a key
   predicate explicitly, so mapping and null-device behaviour are pure tests.
   `InputTestFixture` adds virtual device coverage in EditMode. A guard test
   scans `Assets/Scripts` for the legacy API and for Input System use outside
   the seam.

# Consequences

**Positive**

* The project is on the supported input API, with a single place to add
  rebinding, gamepad support, or a device-aware prompt later.
* Behaviour is preserved: orbit and pan rates, wheel steps, cursor capture,
  free-flight axes, modifiers, speed keys, and the full hotkey map are the same.
* The rest of the runtime has no input-backend dependency, so the seam can be
  replaced or tested in isolation. The guard test makes a regression loud.
* Null devices are neutral, so headless batch tests and a machine without a
  keyboard or mouse do not throw.

**Negative**

* **Both** keeps the legacy input backend initialised for the lifetime of the
  player. That is the cost of keeping IMGUI, not a preference.
* IMGUI still receives its events from the legacy backend, so the HUD is not
  truly on the Input System. Its *gameplay* reads are, but its buttons are
  legacy-fed.
* The scroll normalisation is platform-specific by design. A platform that
  reports neither 1 nor 120 per notch needs a new case; it is not derived from a
  device property today.
* Unity documents `InputTestFixture` as a Play mode tool. It works for direct
  state reads in EditMode because the fixture queues events and updates the
  input system itself, but it does not exercise frame order, and the tests say
  so.
* The Input System package is a registry download, so a fresh clone needs the
  package cache or a connection for the first resolve.

# Alternatives considered

**Input System Package (New) only.** The clean endpoint, and what a uGUI or UI
Toolkit HUD would allow. It breaks every IMGUI button today, so it is not
available without rewriting the HUD — out of scope for an input-backend
migration.

**Stay on the legacy Input Manager.** Lowest risk today, but it delays the
supported API and repeats the migration later. The old manager also has no
device model to grow into for gamepad or rebinding work.

**An `InputActionAsset` with action maps.** The canonical Input System pattern,
but this project has no asset pipeline for it: the scenes are built at runtime,
the HUD is IMGUI, and there are no rebindable actions yet. Direct polling is
smaller and keeps the pure mapping testable. An asset can be added behind the
seam when actions earn their keep.

**Rewrite the HUD to uGUI or UI Toolkit in the same change.** It would allow
**New** only and remove the legacy backend, but it is a UI migration with its
own visual and layout risk. The map markers, labels, and hover prompt are
already drawn in screen space; rebuilding them is a separate project.

**Keep reading raw mouse pixels without the 0.1 factor.** Simpler arithmetic,
but it would make the camera orbit ten times faster and a map pan ten times
further. The 0.1 conversion is the compatibility layer, not a new tuning value.

# Revisit when

* the HUD moves to UI Toolkit or uGUI, which allows **New** only and the removal
  of `InputManager.asset` and the legacy backend,
* gamepad or rebinding support is added, which is the point to introduce an
  `InputActionAsset` behind the seam,
* a platform reports wheel units other than 1 or 120 per notch,
* multiple local players or split input devices are needed,
* the project starts a PlayMode suite that can assert cursor capture and button
  behaviour on a real frame.
