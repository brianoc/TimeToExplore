---
type: Decision Record
title: "ADR-013: A runtime uGUI HUD, and Input System New only"
description: Replace the IMGUI HUD with a runtime-built uGUI canvas, give uGUI an InputSystemUIInputModule, and move Active Input Handling from Both to New only.
tags: [decision, ui, input, unity, technical]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# Context

The prototype's HUD was immediate-mode IMGUI: `SolarSystemHud.OnGUI` drew the
status panel, the details screen, the flight panel, the hover prompt, the body and
trajectory labels, and the crosshair with `GUI.Label`, `GUI.Button`, and
`GUI.DrawTexture`. It worked, but it had two costs.

The first was the input backend. Unity documents that under Active Input Handling
**Input System Package (New)**, runtime `OnGUI` methods receive no input events,
so every IMGUI button would stop responding. [ADR-011](/decisions/adr-011-input-system.md)
therefore set **Both**, keeping the legacy backend alive solely to feed IMGUI.
That is a second input backend initialised for the lifetime of the player, and a
HUD that is not really on the Input System even though the gameplay seam is.

The second was the drawing model. IMGUI re-emits every label, style, and texture
quad each frame from a `Rect` layout, with no retained object to inspect. That
made a button's enable state, a panel's bounds, and a click's target invisible to
anything except a rendered frame.

uGUI is bundled with this editor: `com.unity.ugui` 2.0.0 is an offline-resolvable
built-in package, already present transitively through URP. It carries
`UnityEngine.UI` (`Canvas`, `CanvasScaler`, `GraphicRaycaster`, `Text`, `Button`,
`Image`) and gives uGUI its own input module through the Input System package: the
`InputSystemUIInputModule` lives in the `Unity.InputSystem` assembly, behind the
package's `UNITY_INPUT_SYSTEM_ENABLE_UI` define, and needs no separate assembly
reference beyond `UnityEngine.UI`. (`Unity.InputSystem.ForUI` is a different
assembly: it adapts the Input System to the InputForUI provider used by UI
Toolkit, and is not needed here.)

The HUD had to keep its layout, information, colours, interactions, and the
one-frame protection against the click that opens the details screen also
pressing a button in it.

# Decision

1. **Add `com.unity.ugui` 2.0.0 explicitly.** It was already a transitive
   dependency of URP; the manifest now names it so the HUD's package is not an
   accident. `packages-lock.json` records it at depth 0. `Presentation` adds
   `UnityEngine.UI` to its asmdef, and the test assembly adds it too; these are
   package references, not project references, so the layer rule
   **Core ← Simulation ← Presentation ← Editor/Tests** is unchanged.

2. **Build the HUD as uGUI at runtime.** `SolarSystemHud.Initialise` creates one
   `Canvas` (Screen Space Overlay, sorting order 10), a `CanvasScaler`
   (Scale With Screen Size, 1920×1080 reference, match 0.5), and a
   `GraphicRaycaster`. The three panels are anchored to the canvas corners
   (status top-left, details top-right, flight bottom-left). Their `Image`
   backgrounds are raycast targets; labels, the hover prompt, and the crosshair
   are not, so they never swallow a click. The object graph is created once:
   per frame only `Text.text`, colours, `interactable`, `SetActive`, and
   anchored positions change. Detail and flight rows are fixed pools (20 and 18)
   filled from the same pure builders the IMGUI screen used. Body, trajectory,
   ship, and hover labels come from a pooled set of `Text` widgets with a
   `Shadow`, sized by the canvas scale factor.

3. **Use legacy `UnityEngine.UI.Text`, not TextMeshPro.** uGUI ships TMP in the
   same package, but TMP imports font assets and a settings asset. The prototype
   needs a dozen short labels, so the built-in `LegacyRuntime.ttf` loaded through
   `Resources.GetBuiltinResource<Font>` (with a dynamic OS-font fallback) keeps
   the project asset-free and the HUD cheap.

4. **Give uGUI its own Input System module.** `UiInput.EnsureEventSystem` finds
   an existing `EventSystem` (including inactive ones) and uses it, but turns
   `sendNavigationEvents` off so Space, Enter, and the keyboard arrows stay
   gameplay keys instead of re-submitting a selected button. Otherwise it
   creates one with an `InputSystemUIInputModule` and assigns the package's
   `DefaultInputActions`. The HUD checks
   `EventSystem.current.IsPointerOverGameObject()` before world picking and, since
   that answer can lag the EventSystem by one frame, also tests the three panel
   rects immediately with `RectTransformUtility.RectangleContainsScreenPoint`.

5. **Set `activeInputHandler: 1` — Input System Package (New) only.** With IMGUI
   gone, the legacy backend has no consumer. `ProjectSettings/InputManager.asset`
   stays in place because Unity maintains it, but it is inert and no project code
   reads its axes. `PlayerControls` remains the one **gameplay** seam for world
   picking, the camera, free flight, and hotkeys; `UiInput` is the one **UI**
   bridge. `InputSeamTests` allows the Input System to be named by exactly those
   two files and pins `activeInputHandler: 1`.

6. **Keep the one-frame opening-click guard.** `OpenDetails` marks the frame it
   opened; the panel's buttons are non-interactable until the next `Update`. The
   order of the EventSystem and the HUD in the frame cannot defeat it, and uGUI's
   own press-on-the-same-object rule gives a second line of defence because the
   panel did not exist when the press landed.

7. **Delete the IMGUI implementation.** No `OnGUI`, `GUI.*`, `GUIStyle`, or
   `Texture2D.whiteTexture` remains under `Assets/Scripts`. There is no hidden
   IMGUI fallback: a source scan fails the suite if one returns.

# Consequences

**Positive**

* The project is on **New only**. One input backend, the supported one, and no
  second initialisation cost.
* The HUD is retained UI: panels, rows, and buttons are objects that can be
  inspected, tested, restyled, and later replaced with prefabs without changing
  the data builders.
* uGUI owns pointer-over-panel. A panel click is swallowed by the raycaster, not
  by a hand-maintained `Rect` list that drifts from the layout.
* The content split is unchanged: `BodyDetails`, `FlightReport`, and
  `BodyLabels` still supply every string, so the panel text is tested without a
  canvas.
* The status, details, and flight text, colours, sizes, and ordering are the
  same values as the IMGUI styles at the 1080p reference.

**Negative**

* **Canvas scaling changes pixels above and below 1080p.** IMGUI used raw screen
  pixels; the uGUI reference keeps the old sizes at 1920×1080 and scales them
  with the window. Panel text and spacing at other resolutions need a human eye.
* **There is no PlayMode click coverage yet.** The EditMode tests invoke
  `Button.onClick` directly and assert the guard, but they do not deliver a real
  pointer through the EventSystem. Button hit areas, hover feedback, the
  pointer-over-panel answer on the first frame, and cursor capture still need a
  Play mode pass.
* **Legacy `Text` is not a text-layout engine.** Long body descriptions are
  clipped to their 30-pixel box, as IMGUI clipped them; there is no TMP
  auto-size, rich text, or SDF crispness. That is acceptable at these sizes and
  keeps the asset pipeline empty.
* **A write of every label and row each frame** costs a string format and a
  `Text` comparison even when nothing changed. The object graph no longer churns,
  but a dirty flag would be the next optimisation.
* **The Font fallback is platform-specific.** `LegacyRuntime.ttf` should exist in
  every Unity 6 editor and player; if it does not, the OS-font fallback needs a
  font name that exists on the target platform.
* The EventSystem is created at runtime, so a scene that intentionally disables
  all UI cannot suppress it without removing the HUD.

# Alternatives considered

**Keep IMGUI and keep Both.** Lowest risk and already green, but it leaves the
legacy backend in the player and keeps the HUD outside the Input System. It also
blocks the New-only setting indefinitely.

**UI Toolkit.** The other supported runtime UI. It would also allow New only, but
its runtime API, USS layout, and panel settings asset are a larger migration for
a prototype HUD, and the project has no UI Toolkit theme or panel assets. uGUI's
`Canvas`/`Text`/`Button` map one-to-one onto the existing IMGUI widgets.

**TextMeshPro instead of legacy Text.** Better text rendering, but it imports a
TMP settings asset and font assets into a project that currently has none, and
these labels are 10–22 px and short. Revisit if the HUD grows long-form text or
needs crisp scaling at high DPI.

**Keep the IMGUI HUD as a fallback behind a flag.** Two implementations, two
visual truths, and the legacy backend would have to stay for the fallback. The
migration is meant to remove that path, so the fallback is a one-way trap.

**Build the panels as prefabs in the scenes.** The scenes deliberately hold only
a camera and the bootstrap, and the HUD depends on runtime model state. Code-built
uGUI keeps the scene empty and makes the object graph reviewable in one file.

**Add a separate `TimeToExplore.UI` assembly for uGUI.** Not yet: the HUD uses
the player seam, the renderer's views, and the flight types, and the existing
Presentation assembly already groups that cluster. `AssemblyLayoutTests` would
need a new layer for no boundary that exists today. Revisit if a second UI
consumer appears.

# Revisit when

* a PlayMode suite can drive real pointer events, at which point button hit
  areas and the pointer-over-panel first frame become automated,
* the HUD needs long-form or high-DPI text, which is the point to adopt TMP,
* the panels are authored as prefabs or Skins, which would move construction out
  of `SolarSystemHud` and into assets,
* the HUD grows a second consumer that does not need `Player` or `Rendering`, or
* the UI needs gamepad navigation and rebinding, which would move the default UI
  actions into a project-owned `InputActionAsset`.
