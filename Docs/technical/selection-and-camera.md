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
   `BodyView.VisualRadiusUnits` along the camera's right axis. This is the
   drawn radius, so the minimum-apparent-size aid — including the map's 6 px
   minimum — flows into picking.
3. Give every marker a minimum hit radius of 9 pixels.
4. Locally, choose the hit with the smallest camera distance, so a near moon
   wins over the planet behind it.
5. On the map, choose the highest class first — Sun, planet, or station above
   moon or asteroid — and use the camera distance only to break a tie within a
   class. A moon or asteroid whose marker does not overlap a major body is the
   only hit, so it stays selectable as the view zooms into its neighbourhood.

`BodyPicker` owns steps 4–5 as a pure function over a list of `ScreenTarget`
values, which keeps it testable without a camera or a scene. The renderer and
camera only feed it coordinates. `ScreenTarget.Priority` carries the class
rank; the local HUD leaves `classPriority` off, so the shipped picker rule is
exactly the pre-map one.

The pointer is ignored when the cursor is locked for mouse look, and while it
is over the status panel or the details screen. The click that opens the
details screen cannot also press a button in it: buttons are disabled for that
frame.

# Map drawing

The map is a reading surface, so its drawing rules differ from the local view
while the local view keeps its old thresholds:

* **Markers.** `MarkerSizing.MinimumPixels(w)` eases the aid from 4.5 px at
  `w = 0` to 6 px at `w = 1`; the smooth blend avoids a pop at the band edge.
  `BodyView.TargetScaleFactor` applies it unless `T` true scale is on.
* **Labels.** `BodyLabels.ShouldLabel` is the pure rule. Sun, planets, and
  stations always label. A moon labels on the map when the camera is inside
  5× its parent's SOI (`BodyLabels.InMoonNeighbourhood`), and locally it keeps
  the old 2-million-km threshold. An asteroid labels on the map only when
  hovered or selected; locally it keeps the old 200,000-km threshold.
* **The ship.** `BodyLabels.ShouldLabelShip` names the ten-pixel marker
  "YOU ARE HERE" on the map side only.
* **Orbit lines.** `OrbitLine.WidthForView` keeps the orbit-sized width at
  `w = 0` and eases to `0.0015 × camera distance` on the map. The map ceiling
  of 20,000 units is about 1.5 px at the 12-billion-km zoom, so the line does
  not thin to nothing and a distant orbit cannot become a ribbon at the
  neighbourhood.

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
zooms, and in the local band the camera looks at the ship at the render origin
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
the camera inside a body, it is pulled in toward the origin it is looking at.

## The blended map pose

`ShipCameraRig.MapEnabled` continues the same camera into the
[system map](/technical/system-map.md). Each frame the rig reads the dominant
body's sphere of influence as the local scale `r` and the blend
`w = smoothstep(x)`, `x = clamp(ln(d/r)/ln(3), 0, 1)`, where `d` is the orbit
distance. The pose becomes:

* **Pitch** — `lerp(player pitch, 90°, w)`. The stored pitch stays inside
  ±85°; only the effective pose reaches the pole.
* **Rotation** — `slerp(local rotation, map rotation, w)`. The map's screen-up
  is the limit of the local view's projected up as the pitch reaches the pole,
  `(-sin yaw, 0, -cos yaw)` in Unity space, so it is horizontal and turns with
  yaw. The two rotations share a right axis, so the slerp is exactly a pitch
  turn.
* **Position** — the orbit offset for the effective pitch, added to the
  look-at point. In the map that point is `ship + pan`; in the local band the
  pan has eased to zero, so it is the ship again.
* **Round trip** — the player's yaw and pitch are never written, so zooming out
  and back returns the exact remembered angle without a saved pose.

The one rule this bends is ADR-008's "the camera always looks at the ship". On
the map it looks at `ship + pan`; ADR-009 is still deferred to the map's
documentation step, and the rig records the bend locally.

**Map input.** Right-drag orbits while `w ≤ 0.5` and pans while `w > 0.5`. A
pan moves the look-at by the distance-scaled pixel size
(`2 · d · tan(fov/2) / screenHeight` per pixel) so the content follows the
cursor at any zoom; the offset is in double-precision metres, capped at 60 AU.
Returning to the local band eases the pan to zero exponentially at 3/s, so the
local view lands on the ship. A map pan clears the auto-framing flags, as an
orbit drag does. While the map is engaged, auto-aim and auto-distance are
suspended: the map keeps the player's yaw, and a wheel zoom cannot be silently
pulled back to a framed target. The follow flags survive, so zooming back into
the local band resumes the framing.

**Zoom and clipping.** The wheel is still multiplicative (1.25× per notch), but
the map side uses `MapView.ZoomDistanceUnits` and reaches 12 billion km; the
local side keeps the 5-billion-km clamp. The near plane is today's
`clamp(0.02 × d, 200 km, 50 km)` at `w = 0`, and blends to one thousandth of the
distance on the map, capped at 12,000 units. The far plane is raised to 40
billion km while the map is enabled. Every one of these is unchanged when the
map switch is off.

**The comparison switch.** `SolarSystemBootstrap.EnableMapView` (default false)
is serialized per scene and passed to the rig. `SolarSystem.unity` leaves it
off; `SystemMap.unity` turns it on. It is a development comparison aid, not a
gameplay mode, and a later step removes it or defaults it on.

The remaining work for this layer is the local marker-aid fade (step 4) and the
plotted route with Begin (step 5). Map drawing and picking (step 3) are built:
the 6 px marker minimum, the zoom-aware labels, the "YOU ARE HERE" ship label,
class-priority picking, and the camera-distance orbit width.

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
| Right mouse drag | Orbit the camera around the ship; pan on the map (`w > 0.5`) |
| Mouse wheel | Zoom the camera (local 5 billion km, map 12 billion km) |
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
  rate, and distance easing, and the rig's framing distance and follow flags;
* the map pose: a finite top-down rotation, a horizontal up that turns with
  yaw, the blended rotation agreeing with the effective pitch, and the exact
  remembered-pose round trip;
* input routing at the 0.5 blend threshold, the multiplicative wheel on both
  sides, the extended map ceiling, the local clamp when the map is off, and the
  suspension of auto-framing on the map;
* the distance-scaled pan (content follows the cursor), the 60 AU cap through
  the rig, the exponential ease home, and the near plane at both ends of the
  blend;
* both scenes loading, and the bootstrap switch reaching the rig.

See [Testing and verification](/technical/testing.md) for the full suite.
