---
type: Architecture
title: Selection and camera focus
description: How clicking a body opens its details screen and how the camera flies to it.
tags: [technical, ui, interaction, camera, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
---

# Why selection exists

The orrery is readable, but it is not yet legible: a player can see that a
planet is there without learning anything about it. Selection is the first
interaction layer on top of the orrery. It answers two questions:

1. **What is that?** — a details screen with physical and orbital data.
2. **Can I go there?** — **Plot route → Begin flight**, which draws a real
   route before flying it; and **Jump (debug)**, a three-second camera move
   kept for inspection.

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
4. Collect **every** marker whose hit radius contains the pointer.
5. Order the hits by importance: **star, planet, station, moon, asteroid**.
   Camera distance only breaks a tie within one rank, so a nearer station
   beside a planet cannot steal the planet, and a nearer moon cannot steal a
   station.
6. A click selects the most important hit. **Clicking again at the same spot**
   (within the 4 px click slop) selects the next hit in the list and wraps at
   the end, so every body in a stack is reachable without moving the pointer.
   A click elsewhere, or a stack that changes as the camera moves, starts
   again from the most important hit.
7. A marker that is not covered by another is the only hit regardless of rank,
   so a moon or asteroid separated from its parent stays selectable as the
   view zooms into its neighbourhood.

`BodyPicker` owns the rank ladder, `OrderedHits`, and `PickCycle` as pure
functions over a list of `ScreenTarget` values, which keeps them testable
without a camera or a scene. The renderer and camera only feed it coordinates.
`ScreenTarget.Priority` is gone: the rank is derived from the body's catalogue
kind in one place, so the local view and the map share the same rule. The hover
prompt names the body the **next** click would select and says how many objects
are stacked under the pointer.

The pointer is ignored when the cursor is locked for mouse look, and whenever
`EventSystem.current.IsPointerOverGameObject()` says it is over a raycast-target
panel — the status panel, the details screen, the flight panel, or one of their
buttons. A click on a panel is therefore swallowed by uGUI and never reaches the
world. The click that opens the details screen cannot also press a button in it:
the buttons are non-interactable during the frame the panel appears, whichever
order the EventSystem and the HUD update in. A press over a panel also resets
the click cycle.

# Map drawing

The map is a reading surface, so its drawing rules differ from the local view
while the local view keeps its old thresholds:

* **Markers.** `MarkerSizing.MinimumPixels(w)` eases the aid from 4.5 px at
  `w = 0` to 6 px at `w = 1`; the smooth blend avoids a pop at the band edge.
  `MarkerSizing.AidStrength` then fades the aid outside the local neighbourhood
  in the local view, so distant bodies render at true scale there while the map
  restores the full marker. `BodyView.TargetScaleFactor` applies the resulting
  minimum unless `T` true scale is on.
* **Labels.** `BodyLabels.ShouldLabel` is the pure rule. Sun, planets, and
  stations always label. A moon labels on the map when the camera is inside
  5× its parent's SOI (`BodyLabels.InMoonNeighbourhood`), and locally it keeps
  the old 2-million-km threshold. An asteroid labels on the map only when
  hovered or selected; locally it keeps the old 200,000-km threshold.
* **The ship.** `BodyLabels.ShouldLabelShip` names the ten-pixel marker
  "YOU ARE HERE" on the map side only.
* **Orbit lines.** `LineWidth` turns a pixel count into render units at the
  camera distance, with no orbit-sized term: 3 px in the local view, blending to
  10 px in the solar system view. A distant orbit can never become a ribbon.
  Each planet's colour is fixed by `OrbitColours` (warm orange Mercury to cool
  purple Neptune) and is identical in both views; moons stay neutral grey.

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

The panel is a runtime-built uGUI overlay on the right, on a canvas scaled from
a 1920×1080 reference. It is a **screen**, not a world-space canvas: world-space
UI does not survive solar-scale distances. `BodyDetails` supplies the rows; the
HUD writes them into a fixed pool of `Text` entries, so the panel is not rebuilt
when the selection changes. The selected body's catalogue colour is used for the
accent bar, the divider, and the hover prompt. The footer holds **Plot route …**,
**Begin flight** (enabled once that body's route is plotted), **Jump (debug)**
(the eased teleport), and **Close**. While a flight is running the primary
button becomes **Replan …**. A button is only interactive once the
opening-frame guard has cleared.

A plotted route belongs to the body it targets. Selecting another body, `Esc`,
or **Close** discards it; plotting the same body again refreshes it. The flight
panel switches to the PREVIEW rows while one exists, and **Begin flight**
replans from the current state before flying.

# The camera

The view is an **orbit camera** around the ship: right-drag orbits, left-drag
pans on the map, the wheel zooms toward the pointer, and the camera looks at
`ship + pan` — the ship itself whenever the pan is zero
([ADR-008](/decisions/adr-008-orbit-camera.md)). That keeps the ship-relative
float precision of [ADR-002](/decisions/adr-002-double-precision.md) — the world
is still projected from the ship — while letting the player look at their own
vessel and at the plan from outside.

`OrbitCamera` is plain C# and holds yaw, pitch (±85°), and a distance from 2,000
km to 5 billion km. `ShipCameraRig` reads the input, places the camera, and does
the automatic framing: a plan, a jump, or a reset turns the view toward its
destination and eases the orbit distance to `clamp(0.6 × ship→target, 3 ×
radius, 3.3 AU)`. The aim and the distance follow separately, so a wheel zoom
at the centre keeps the destination centred; only a drag stops the aiming. If
the orbit would put the camera inside a body, it is pulled in toward the origin
it is looking at.

**Plotting a route frames the route.** A finished preview hands the view to
`RouteFraming`, which fits the sampled path (plus the target's arrival ghost)
into one shot. The look-at point becomes the route's bounding-box centre and
the distance is the smallest that keeps the route's projected outline inside
the tighter of the vertical and horizontal fields of view, with a 12% margin.
Projecting the route rather than its bounding sphere matters for a long, thin
transfer: the sphere fit left half the frame empty, while the projected fit
puts the ends near the margin. The viewing direction comes from the plane the
route lies in; the normal is taken with Newell's method over the ordered path,
so an inclined transfer is seen across its plane rather than edge-on, and a
route too straight to define a plane falls back to a view perpendicular to its
chord. The camera sits 18° off that normal toward the player's current side, so
the picture has depth without foreshortening the curve and the view never flips
through the plane. The fit may sit beyond the wheel's local ceiling for an
outer-system route, so it uses the map's higher ceiling; the next wheel notch
comes back into the local range. `RouteViewTransition` eases the yaw (shortest
turn), the pitch, the distance (log-space, like a zoom), and the pan from the
player's pose to that frame in 1.5 s on one smoothstep. A wheel notch, a moving
drag, a new framing target, Begin, or Space cancels the move and hands the view
back; the route's pan is held while it is the look-at point, so a local route
does not drift back to the ship when the sample lands inside the blend band. A
re-time deliberately does not re-frame: the player may already have moved the
camera, and the slider is a tool for comparing plans, not a second camera
command. A style, cruise-profile, or burn-level change is a new plan and does
re-frame it.

**Zoom is a tween.** A wheel notch does not place the camera: it requests a new
distance (`OrbitCamera.RequestDistance`) and the rig eases the current distance
toward it at `ZoomEaseRatePerSecond` (7/s). The request compounds on a pending
distance, so a continuous gesture keeps its 1.25× per notch even while the
camera is still travelling. The blend reads the eased distance, so entering or
leaving the [solar system view](/technical/system-map.md) sweeps the top-down
tilt and the rotation over the same short tween instead of snapping, and the
player's stored yaw and pitch are still never written. A reset, a snap-to, or a
direct distance assignment places the camera immediately.

**Zoom goes toward the pointer.** The notch anchors the point under the cursor
on the plane through the look-at point, and the rig solves the look-at from that
anchor every frame of the tween (`MapView.CursorPointOnLookAtPlane` and
`AnchoredLookAtUnits`). The anchored point therefore stays under the pointer
while the distance eases and while the pose tilts into the map. This is the pan
doing the work, so the pan is live at every zoom, not only on the map; the ship
drifts off-centre when the zoom is off-centre, which is the point of the
gesture. The 60 AU pan cap still applies, so a corner anchor at the 12-billion-km
ceiling can shift very slightly at the last notch. See
[ADR-017](/decisions/adr-017-cursor-zoom-and-home.md).

**Space goes home.** A one-second smoothstep (`HomeTransition`) eases the
distance geometrically to half the dominant body's SOI radius — the middle of
the local band, so the arrival is exactly `w = 0` — and unwinds the pan to zero.
The log-space distance keeps the perceived zoom rate even on a descent from the
map ceiling. Yaw and pitch are kept, so the ship returns to the centre of the
same remembered angle. The follow flags are cleared as a drag clears them, but
the framing target is kept, so an active flight's follow does not re-claim the
camera and cancel the transition. A wheel notch or a moving drag cancels it.
`P` pauses.

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
  look-at point. That point is always `ship + pan`; the pan is the map's
  browsed centre on the map side and the cursor-anchored offset in the local
  band. It is zero only when the local view has not been zoomed off-centre
  (Space unwinds it).
* **Round trip** — the player's yaw and pitch are never written, so zooming out
  and back returns the exact remembered angle without a saved pose.

The one rule this bends is ADR-008's "the camera always looks at the ship". It
looks at `ship + pan`, and the pan is now live at every zoom: the map's browsed
centre and the local cursor anchor are the same state. The bend is recorded in
[ADR-009](/decisions/adr-009-map-camera-pan.md) for the map and
[ADR-017](/decisions/adr-017-cursor-zoom-and-home.md) for the cursor zoom.

**Map input.** Right-drag orbits at every zoom, local and map. Left-drag pans,
but only while `w > 0.5`; in the local band there is nothing to pan, so the
button is inert. Neither gesture starts if the press lands on a HUD panel: the
panel owns the pointer until the button is released, so the trip-time and
burn-level sliders cannot move the camera beneath them, even when a drag carries
past the panel's edge. A press that starts in the world keeps its gesture when
the pointer crosses a panel. A pan moves the look-at by the distance-scaled pixel
size (`2 · d · tan(fov/2) / screenHeight` per pixel) so the content follows the
cursor at any zoom; the offset is in double-precision metres, capped at 60 AU.
Selection shares the left button: a press that stays inside the 4 px click slop
selects its body on release, while a press that moves further is a pan and
never opens the details screen. A pan that no wheel anchored eases to zero when
the view returns to the local band (3/s), so a framing or a direct distance
change lands on the ship. A pan the wheel anchored under the pointer is kept,
because pulling it home would move the point from under the cursor, and Space is
the explicit way home. A map pan clears the auto-framing flags, as an orbit drag
clears auto-aim. While the map is engaged, auto-aim and auto-distance are
suspended: the map keeps the player's yaw, and a wheel zoom cannot be silently
pulled back to a framed target. The follow flags survive, so zooming back into
the local band resumes the framing.

**Zoom and clipping.** The wheel is still multiplicative (1.25× per notch), but
the distance is eased to the requested value rather than placed (see
[the camera](/technical/selection-and-camera.md#the-camera)); the blend reads
the eased distance, so the top-down tilt arrives with the zoom. The map side
uses `MapView.ZoomDistanceUnits` and reaches 12 billion km; the local side keeps
the 5-billion-km clamp. The near plane is today's
`clamp(0.02 × d, 200 km, 50 km)` at `w = 0`, and blends to one thousandth of the
distance on the map, capped at 12,000 units. The far plane is raised to 40
billion km while the map is enabled. Every one of these is unchanged when the
map switch is off.

**The comparison switch.** `SolarSystemBootstrap.EnableMapView` (default false)
is serialized per scene and passed to the rig. `SolarSystem.unity` leaves it
off; `SystemMap.unity` turns it on. It is a development comparison aid, not a
gameplay mode, and a later step removes it or defaults it on.

Map drawing, picking, the local marker-aid fade, the route preview, and the
frame-loop acceptance are built (steps 3–6): the 6 px map minimum, the
zoom-aware labels, the "YOU ARE HERE" ship label, importance-ordered picking
with repeated-click cycling, the camera-distance orbit width, the
neighbourhood falloff that stops the local view inflating distant bodies, the
**Plot route → Begin flight** preview with its target-arrival ghost, and the
map-to-arrival acceptance through the real bootstrap.

# The focus transition (debug jump)

The details screen's flight actions are **Plot route → Begin flight**, which
plans a real flight ([The flight session](/technical/flight-session.md)), and
**Jump (debug)**, which keeps the original eased camera move. The jump is explicitly a
teleport: it cancels any running plan and clears the ship's velocity, because it
is not a trajectory through the gravity field.

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

Device input reaches the camera and HUD through the
[Input](/technical/input.md) seam; the table below is the behaviour, not a
second mapping.

| Input | Action |
|---|---|
| Left click a body | Select it and open the details screen (release inside the 4 px slop) |
| Left click the same spot again | Cycle to the next body in the stack, in importance order, wrapping at the end |
| Left mouse drag | Pan the map (`w > 0.5`); inert in the local band, and inert when the press began on a panel |
| **Plot route …** button | Plan the route, draw the preview, and ease the camera to the whole route |
| **Begin flight** button | Replan from the current state and fly it |
| **Jump (debug)** button, `F` | Three-second eased debug teleport to the selected body |
| `Close`, `Esc` | Close the details screen and discard a preview; `Esc` also cancels a debug jump |
| Right mouse drag | Orbit the camera around the ship, at every zoom, unless the press began on a panel |
| Mouse wheel | Zoom toward the pointer (local 5 billion km, map 12 billion km), eased to the requested distance |
| `Space` | Ease the camera home to the ship in one second, ending in the local view |
| `P` | Pause or resume the simulation clock |
| `Tab` | Cycle planet/station targets without opening the screen |

# Verification

`SelectionAndFocusTests` covers:

* every catalogue body produces a complete details list with no `NaN` values;
* Earth's gravity, escape velocity, mass, and light time against reference
  values;
* the picker returns the body under the pointer, ignores empty space, prefers
  the nearer body within one rank, and applies the minimum hit radius;
* importance runs star, planet, station, moon, asteroid, and a planet outranks
  a nearer station while a station outranks a nearer moon;
* `OrderedHits` returns the whole stack in that order, breaking a same-rank tie
  by depth, and `PickCycle` walks it, wraps, keeps a single hit at the top, and
  starts again when the pointer moves or the stack changes;
* smoothstep acceleration and deceleration, exact arrival at the duration,
  midpoint position, cancellation, and zero duration;
* the approach offset's eight-radius and 5,000 km rules and its lit-side bias;
* the orbit camera's pitch clamp, yaw wrap, zoom limits, shortest turn, slew
  rate, and distance easing, the zoom request that eases over many frames and
  lands exactly, and the rig's framing distance, follow flags, and the smooth
  top-down tilt that follows a wheel tween;
* the route overview: the bounding frame containing every sampled point and the
  arrival ghost, the projected fit and its 12% margin (tighter than the
  bounding sphere), the plane normal and its 18° tilt (including a straight
  route seen side-on), the shortest-turn/log-distance transition and its exact
  endpoints, the rig's eased arrival with the whole route on screen, the wheel,
  drag, and Space cancels, and an empty route leaving the view alone;
* the cursor anchor: the screen-to-NDC conversion, the look-at-plane point, the
  exact pinned point at a changed distance, and a centre pointer leaving the
  look-at alone; the rig's projected anchor on every frame of a real wheel
  tween, and the anchored pan surviving in the local band;
* the home transition: one second of smoothstep, exact endpoints, a
  zero-duration landing, cancellation, the local arrival at `w = 0`, the
  remembered angle, and a wheel or drag cancelling it;
* the map pose: a finite top-down rotation, a horizontal up that turns with
  yaw, the blended rotation agreeing with the effective pitch, and the exact
  remembered-pose round trip;
* input routing at the 0.5 blend threshold, the multiplicative wheel on both
  sides, the extended map ceiling, the local clamp when the map is off, and the
  suspension of auto-framing on the map;
* the bootstrap's map round trip from the neighbourhood to the 12-billion-km
  ceiling and back to the exact remembered angle, and the full map-to-arrival
  acceptance (select Mars, plot, Begin, 3,939 frames, 0.0 m, 0.0000 m/s);
* the distance-scaled pan (content follows the cursor), the 60 AU cap through
  the rig, the exponential ease home, and the near plane at both ends of the
  blend;
* panel-owned gestures: a press over the HUD starts neither a pan nor an orbit
  and stays the panel's after the pointer leaves it, a world press keeps its
  drag across a panel, and a late panel answer cancels a pan before it engages;
* the neighbourhood marker aid: full inside the SOI, monotonic to true scale at
  3×, restored in full by the map, widened by the camera distance, disabled
  when no local scale is supplied, and still overridden by `T`;
* both scenes loading, the bootstrap switch reaching the rig, and that switch
  gating the aid fade.

`SolarSystemHudTests` covers the uGUI replacement: one overlay Canvas with the
1920×1080 scaler and a raycaster, one EventSystem with an
`InputSystemUIInputModule`, the status text tracking the clock and the ship, the
details rows matching `BodyDetails.Build`, the flight panel for a preview and a
plan, `ShowHud`/`ShowLabels`, the opening-frame guard, and every button's
`onClick` reaching the same owner method. What it cannot cover is a real
pointer: see [Input](/technical/input.md#what-still-needs-a-human).

See [Testing and verification](/technical/testing.md) for the full suite.
