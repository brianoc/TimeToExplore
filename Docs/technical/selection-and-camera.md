---
type: Architecture
title: Selection and camera focus
description: How the game opens on an Earth–Moon shot, how clicking a body opens its details screen, and how the one orbit camera flies to it, zooms toward the pointer, frames a plotted route, and takes the flight chase pose.
tags: [technical, ui, interaction, camera, unity]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T21:20:22Z }
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
   drawn radius, so the minimum-apparent-size aid flows into picking.
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
kind in one place. The hover prompt names the body the **next** click would
select and says how many objects are stacked under the pointer.

The pointer is ignored when the cursor is locked for mouse look, and whenever
`EventSystem.current.IsPointerOverGameObject()` says it is over a raycast-target
panel — the status panel, the details screen, the flight panel, or one of their
buttons. A click on a panel is therefore swallowed by uGUI and never reaches the
world. The click that opens the details screen cannot also press a button in it:
the buttons are non-interactable during the frame the panel appears, whichever
order the EventSystem and the HUD update in. A press over a panel also resets
the click cycle.

# Drawing rules

* **Markers.** `MarkerSizing.MinimumPixels` is 4.5 px at every zoom, so a
  distant planet stays a click target and a label anchor.
  `BodyView.TargetScaleFactor` applies the minimum unless the `T` true-scale
  toggle is on.
* **Labels.** `BodyLabels.ShouldLabel` is the pure rule. Sun, planets, and
  stations always label. A moon labels within 2 million km of the ship and an
  asteroid within 200,000 km; the selection always labels.
* **Orbit lines.** `LineWidth` turns a 3 px count into render units at the
  camera distance, with no orbit-sized term. Each vertex then keeps the natural
  perspective falloff, bounded to 2 px at the far end
  (`DistanceFloorPixels`) and to the line's own pixel count at the near end, so
  the far arc stays visible and a near arc passed by a zoomed-in camera does not
  become a ribbon. Each planet's colour is fixed by `OrbitColours` (warm orange
  Mercury to cool purple Neptune); moons stay neutral grey.

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

A plotted route belongs to the body it targets. Selecting a body warms its
route plan at once — the selection is the destination, and the first solve is
the slow one — but shows nothing: no route line, no flight panel, no slider, and
**Begin flight** stays disabled, so the screen reads as idle. The footer's
**Plot route** adopts the warmed plan and takes the camera, which is why the
route appears in the click's own frame. Selecting another body, `Esc`, or
**Close** discards a plotted route; the warmed plan is replaced by the next
body's. The flight panel switches to the PREVIEW rows while a preview exists,
and **Begin flight** replans from the current state before flying.

# The camera

The view is an **orbit camera** around the ship: right-drag orbits, the wheel
zooms toward the pointer, and the camera looks at `ship + look-at offset` — the
ship itself whenever the offset is zero
([ADR-008](/decisions/adr-008-orbit-camera.md),
[ADR-017](/decisions/adr-017-cursor-zoom-and-home.md)). That keeps the
ship-relative float precision of [ADR-002](/decisions/adr-002-double-precision.md)
— the world is still projected from the ship — while letting the player look at
their own vessel and at the plan from outside.

`OrbitCamera` is plain C# and holds yaw, pitch (±85°), and a distance from 2,000
km to 12 billion km. There is one range and one pose: zooming out never tilts
the camera, changes its rotation, or swaps in a different reading of the
system. The player's yaw and pitch are the only angles, at every distance.

`ShipCameraRig` reads the input and places the camera. It owns the auto-framing,
the cursor anchor, the route transition, and `Space` home.

**The opening view.** The game opens on a wide shot of Earth and the Moon,
with Gateway Station between them. `OpeningFraming` puts the camera on the
perpendicular to the Earth–Moon line, 20° above the ecliptic, on the sunward
side so both discs are lit; the look-at is the midpoint of the pair and the
distance is the nearest that keeps both inside the frame with a 25% margin,
wider than a route fit so the discs sit clear of the HUD's side panels and the
frame edge. The pose is placed, not eased, and it is the player's
from the first frame: no body follow is left running, so nothing pulls the
view off the composition. `R` returns to the same shot. The angle is a
composition choice, not a model of a real observer.

**Framing a body.** A jump frames its destination and eases the orbit distance
to `clamp(0.6 × ship→target, 3 × radius, 3.3 AU)`. The
aim and the distance follow separately, so a manual zoom keeps the destination
centred; only a drag stops the aiming. The framing distance is captured **once**
when the framing starts, not recomputed from the moving ship every frame: during
a flight the view holds a stable distance while the target grows, and the
distance is re-captured once on arrival so the camera settles on the parking
orbit. Both moves ease; neither snaps. If the orbit would put the camera inside
a body, the camera is stopped at the near intersection of its ray with that
body's clearance sphere, so grazing a planet slides the camera along the orbit
instead of stepping it inward.

**A flight takes the chase pose.** Begin does not frame the destination: it
eases the camera to `FlightFraming`'s chase view, behind the ship along the
route, 20° above it and 30° to one side, starting at 20,000 km
([ADR-027](/decisions/adr-027-continuous-warp-and-chase-camera.md)). The angle is
chosen once from the direction to the destination, not tracked as the route
curves: the ship is the render origin, so holding the pose is enough to follow
it. The move takes 1.2 s, matching the warp curve's own rise from real time.

The distance then follows the frame's travel. In a frame at high warp the ship
can cover more than the 20,000 km view, which made the route line appear to jump
a screen or more every frame. `FollowFlight` grows the chase distance until the
frame's travel is at most 8% of the view, up to 5 million km. The marker is
56 px whenever the flight owns the view, so pulling back still leaves the ship
well framed; its long axis follows the thrust or the direction of travel, and
the thrust arrow scales with it. A right-drag takes the angle and keeps the
distance follow; a wheel takes the distance but keeps the flight view, and the
zoom stays centred on the ship rather than anchoring the point under the
pointer, so the ship never slides aside. A home press, a new frame, or the
arrival ends the flight view. The ordinary orrery view still uses the 10 px
marker.

The destination framing takes over when the ship comes within **ten radii** of a
planet, not only on the arrival date. The chase angle is fixed at departure and
can point back at the planet on the last leg, so the framing starts before the
camera can sit on the surface. At Mars the capture sits 19 Mars radii from the
centre. A station or a small body
keeps its chase until the arrival, because it has no surface to wipe.


**Plotting a route frames the route.** A finished preview hands the view to
`RouteFraming`, which fits the sampled path (plus the target's arrival ghost)
into one shot. The plan a selection warmed is not drawn at all: the footer's
**Plot route** adopts it, draws it, and takes the camera in the same frame, so
reading a body never changes the picture. The look-at point becomes the route's
bounding-box centre and the distance is the smallest that keeps the route's
projected outline inside
the tighter of the vertical and horizontal fields of view, with a 12% margin.
Projecting the route rather than its bounding sphere matters for a long, thin
transfer: the sphere fit left half the frame empty, while the projected fit
puts the ends near the margin. The viewing direction comes from the plane the
route lies in; the normal is taken with Newell's method over the ordered path,
so an inclined transfer is seen across its plane rather than edge-on, and a
route too straight to define a plane falls back to a view perpendicular to its
chord. The camera sits 18° off that normal toward the player's current side, so
the picture has depth without foreshortening the curve and the view never flips
through the plane. `RouteViewTransition` eases the yaw (shortest turn), the
pitch, the distance (log-space, like a zoom), and the look-at offset from the
player's pose to that frame in 1.5 s on one smoothstep. A wheel notch, a moving
drag, a new framing target, Begin, or Space cancels the move and hands the view
back; the route's look-at offset is held while it is the look-at point, so a
local route does not drift back to the ship. A re-time deliberately does not
re-frame: the player may already have moved the camera, and the slider is a tool
for comparing plans, not a second camera command. A style, cruise-profile, or
burn-level change is a new plan and does re-frame it.

**Zoom is a tween.** A wheel notch does not place the camera: it requests a new
distance (`OrbitCamera.RequestDistance`) and the rig eases the current distance
toward it at `ZoomEaseRatePerSecond` (7/s). The request compounds on a pending
distance, so a continuous gesture keeps its 1.25× per notch even while the
camera is still travelling. The player's yaw and pitch are never written, so
zooming out and back returns the exact remembered angle. A reset, a snap-to, or
a direct distance assignment places the camera immediately.

**Zoom goes toward the pointer.** The notch anchors the point under the cursor
on the plane through the look-at point, and the rig solves the look-at from that
anchor every frame of the tween (`CursorZoom.CursorPointOnLookAtPlane` and
`AnchoredLookAtUnits`). The anchored point therefore stays under the pointer
while the distance eases. This is the look-at offset doing the work, so the ship
drifts off-centre when the zoom is off-centre, which is the point of the
gesture. The 60 AU offset cap still applies, so a corner anchor at the
12-billion-km ceiling can shift very slightly at the last notch. The offset is
kept after the tween; `Space` is the explicit way home.

**Space goes home.** A one-second smoothstep (`HomeTransition`) eases the
distance geometrically to half the dominant body's SOI radius — the middle of
the ship's neighbourhood — and unwinds the look-at offset to zero. The log-space
distance keeps the perceived zoom rate even on a descent from the outer
ceiling. Yaw and pitch are kept, so the ship returns to the centre of the same
remembered angle. The follow flags are cleared as a drag clears them, but the
framing target is kept, so an active flight's follow does not re-claim the
camera and cancel the transition. A wheel notch or a moving drag cancels it.
`P` pauses.

**F frames the selected body in its own render frame.** The ship does not move;
the **render origin** does. While focused, the selected body's position becomes
the origin, so the body sits at `(0,0,0)` at its true size and the camera orbits
it at four radii. That is the difference between pointing at a planet and
framing it: at interplanetary distance a body's true radius on a 1,000 km-unit
grid is far below the float precision available at the ship origin, so no zoom
can resolve its surface from there. The origin switch is a cut; the camera
starts at the wider of the current orbit distance and four radii, then eases to
the four-radius frame. The player's yaw and pitch are kept, so the body is
centred and the camera can be orbited and zoomed around it. `Space` leaves the
focus and lands on the ship's home view; a jump, route frame or reset also
leaves it. The ship marker, route line and orbit lines are hidden while focused.
A running flight owns the camera, so `F` is refused while it flies; `J` is still
the debug jump that moves the ship.

**Near and far planes.** The near plane is `clamp(0.02 × distance, 200 m,
50 km)`, so it scales with the orbit and still lets a moon be inspected at the
closest zoom. The far plane is 20 billion km (about 133 AU), which covers the
12-billion-km camera plus the 60 AU look-at offset and the catalogue's width.

# Input summary

Device input reaches the camera and HUD through the
[Input](/technical/input.md) seam; the table below is the behaviour, not a
second mapping.

| Input | Action |
|---|---|
| Left click a body | Select it, warm its route plan in the background, and open the details screen (release inside the 4 px slop); nothing is drawn until Plot route |
| Left click the same spot again | Cycle to the next body in the stack, in importance order, wrapping at the end |
| **Plot route …** button | Adopt the warmed plan and draw it in the same frame, then ease the camera to the whole route; a warm-up still building is continued. A retimed or stale preview is replanned at the plot's start trip |
| **Begin flight** button | Replan from the current state, fly it, and ease the camera to the chase pose |
| `F` | Ease the camera to the selected body in one second, without moving the ship |
| **Jump (debug)** button, `J` | Three-second eased debug teleport of the ship to the selected body |
| `Close`, `Esc` | Close the details screen and discard a preview; `Esc` also cancels a debug jump |
| Right mouse drag | Orbit the camera around the ship, unless the press began on a panel |
| Mouse wheel | Zoom toward the pointer (2,000 km to 12 billion km), eased to the requested distance |
| `Space` | Ease the camera home to the ship in one second |
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
* the approach offset's eight-radius and 5,000 km rules and its lit-side bias.

`OrbitCameraTests` covers pitch clamp, yaw wrap, zoom limits, shortest turn,
slew rate, distance easing, a request that eases over many frames and lands
exactly, placing a distance cancelling a request, a zero-length frame
completing the zoom, and the home transition's endpoints and cancellation.

`CameraRigTests` covers the framing distance at arrival and in cruise, the
captured-once framing distance and its arrival re-capture, the follow flags,
the shared 3 px line width, the zoom-out-and-back pose round trip, the wheel
tween and the 12-billion-km ceiling, the cursor anchor's projected NDC on every
frame of a real off-centre zoom, the anchor surviving after the tween, Space's
one-second home with the remembered angle, a wheel or drag cancelling it, the
button routing (right-drag orbits; a press on a panel starts nothing; a world
press keeps its gesture across a panel), the slew-rate turn when the framing
target changes, the near plane, and the flight chase
pose: its 20° elevation and 30° side angle, the eased arrival, and the drag and
arrival handbacks.

`RouteFramingTests` covers the route fit, the plane angle, the straight-route
fallback, the transition endpoints, the rig's eased arrival, the cancels, and a
degenerate route.

`OpeningFramingTests` covers the opening pose: the 20° downward angle from
above the ecliptic, both bodies inside the fitted margin, the midpoint look-at,
the sunward side, the station in shot, a coincident pair leaving the view
alone, and the rig handing the pose back to the player after a frame.

`DrawingRulesTests` covers the 4.5 px marker minimum and true-scale override,
the label thresholds, the 3 px line rule at every zoom, the 2 px distance floor
and near cap on both real renderers, the camera-plane clip, and the planet
palette.

`BootstrapSmokeTests` and `SceneFlightTests` run the real composition root:
the opening frame holding through the frame loop, the single-range zoom and
back to the exact angle, and the Gateway→Mars acceptance flight to the parking
orbit.

What the suite cannot cover is the human check: wheel feel at a few pointer
positions, the perceived smoothness of the arrival re-frame, and a real pointer
on a panel. See [Input](/technical/input.md#what-still-needs-a-human).

See [Testing and verification](/technical/testing.md) for the full suite.
