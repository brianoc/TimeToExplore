---
type: Architecture
title: Selection and camera focus
description: How clicking a body opens its details screen and how the camera flies to it.
tags: [technical, ui, interaction, camera, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Why selection exists

The orrery is readable, but it is not yet legible: a player can see that a
planet is there without learning anything about it. Selection is the first
interaction layer on top of the orrery. It answers two questions:

1. **What is that?** — a details screen with physical and orbital data.
2. **Can I go there?** — **Go To**, which plans a real flight and flies it; and
   **Jump (debug)**, a three-second camera move kept for inspection.

# Picking

Body colliders are deliberately removed when the views are built, and the
minimum-apparent-size aid draws a distant body far larger than its physical
radius. A physics raycast would therefore miss most of what the player sees.
Picking is done in screen space against the **drawn marker**:

1. Project each body's render position with `Camera.WorldToScreenPoint`.
2. Measure the marker radius by projecting a point offset by
   `BodyView.VisualRadiusUnits` along the camera's right axis.
3. Give every marker a minimum hit radius of 9 pixels.
4. Choose the hit with the smallest camera distance, so a near moon wins over
   the planet behind it.

`BodyPicker` owns step 4 as a pure function over a list of `ScreenTarget`
values, which keeps it testable without a camera or a scene. The renderer and
camera only feed it coordinates.

The pointer is ignored when the cursor is locked for mouse look, and while it
is over the status panel or the details screen. The click that opens the
details screen cannot also press a button in it: buttons are disabled for that
frame.

# The details screen

`BodyDetails` builds a list of headed rows; `SolarSystemHud` only draws them.
That split keeps the content testable and means the panel can be restyled
without touching the data.

Sections:

| Section | Contents |
|---|---|
| Now | Distance from the ship, heliocentric distance, light time, heliocentric speed |
| Physical | Radius, mass, surface gravity, escape velocity, rotation, axial tilt |
| Orbit | Parent, semi-major axis, eccentricity, inclination, period, distance from parent |
| Location | For stations: the Lagrange configuration, for example Earth–Moon L1 |

Mass comes from the catalogue's gravitational parameter (`M = GM/G`), so it
stays consistent with the simulation rather than with a second, hand-typed
field. Rotation periods are signed; a negative value is shown as retrograde.

The panel is immediate-mode IMGUI, drawn on the right. It is a **screen**, not
a world-space canvas: world-space UI does not survive solar-scale distances.
The selected body's catalogue colour is used for the accent bar and hover
prompt. The footer holds **Go To …** (the flight), **Jump (debug)** (the eased
teleport), and **Close**.

# The camera

The view is an **orbit camera** around the ship: right-drag orbits, the wheel
zooms, and the camera always looks at the ship at the render origin
([ADR-008](/decisions/adr-008-orbit-camera.md)). That keeps the ship-relative
float precision of [ADR-002](/decisions/adr-002-double-precision.md) — the world
is still projected from the ship — while letting the player look at their own
vessel and at the plan from outside.

`OrbitCamera` is plain C# and holds yaw, pitch (±85°), and a distance from 2,000
km to 5 billion km. `ShipCameraRig` reads the input, places the camera, and does
the automatic framing: a plan, a jump, or a reset turns the view toward its
destination and eases the orbit distance to `clamp(0.6 × ship→target, 3 ×
radius, 3.3 AU)`. The aim and the distance follow separately, so zooming keeps
the destination centred; only a drag stops the aiming. If the orbit would put
the camera inside a body, it is pulled in toward the ship.

The next step for this layer is
[The system map and route preview](/technical/system-map.md): a zoom-driven
blend to a top-down map at the local sphere of influence, panning, and a
plotted route with a Begin button. The plain-C# map state (`MapView`) and its
tests are built; the rig integration, map drawing, and route preview are not
yet.

# The focus transition (debug jump)

The details screen has two actions: **Go To**, which starts a real flight
([The flight session](/technical/flight-session.md)), and **Jump (debug)**, which
keeps the original eased camera move. The jump is explicitly a teleport: it
cancels any running plan and clears the ship's velocity, because it is not a
trajectory through the gravity field.

`CameraFocusDirector` owns a `FocusTransition`. `FocusTransition` is plain C#
with an explicit start position and an eased progress:

```text
p = clamp(elapsed / duration, 0, 1)
eased = p² × (3 − 2p)          // smoothstep: zero velocity at both ends
position = lerp(start, destination, eased)
```

The default duration is **3 seconds** and is serialized on
`CameraFocusDirector`.

The destination is recomputed every frame from the target's current position,
so the jump still arrives if time warp is moving the body. On the final step
the ship is snapped to the exact destination and the camera rig frames the
body; interpolation error does not accumulate.

The camera stops **eight radii** away, or **5,000 km**, whichever is farther,
offset toward the Sun and to one side. The Sun-facing component keeps the
arrival on the lit side. The same offset is used by the `F` key and the
**Jump (debug)** button.

Manual input is disabled for the transition so the player does not fight the
scripted path. `Esc` cancels and restores control; starting a new focus, or
pressing `R`, also cancels. The details screen stays open on arrival. While a
flight is running the jump is disabled: the flight owns the ship.

# Input summary

| Input | Action |
|---|---|
| Left click a body | Select it and open the details screen |
| **Go To …** button | Plan and fly to the selected body |
| **Jump (debug)** button, `F` | Three-second eased debug teleport to the selected body |
| `Close`, `Esc` | Close the details screen; `Esc` also cancels a debug jump |
| Right mouse drag | Orbit the camera around the ship |
| Mouse wheel | Zoom the camera |
| `Tab` | Cycle planet/station targets without opening the screen |

# Verification

`SelectionAndFocusTests` covers:

* every catalogue body produces a complete details list with no `NaN` values;
* Earth's gravity, escape velocity, mass, and light time against reference
  values;
* the picker returns the body under the pointer, ignores empty space, prefers
  the nearer body when markers overlap, and applies the minimum hit radius;
* smoothstep acceleration and deceleration, exact arrival at the duration,
  midpoint position, cancellation, and zero duration;
* the approach offset's eight-radius and 5,000 km rules and its lit-side bias;
* the orbit camera's pitch clamp, yaw wrap, zoom limits, shortest turn, slew
  rate, and distance easing, and the rig's framing distance and follow flags.

See [Testing and verification](/technical/testing.md) for the full suite.
