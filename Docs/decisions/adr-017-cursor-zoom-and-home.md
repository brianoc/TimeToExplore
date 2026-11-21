---
type: Decision Record
title: "ADR-017: zoom toward the pointer, and Space to go home"
description: Anchor the wheel zoom to the point under the pointer by moving the camera's look-at, and make Space ease the view back to the ship in one second.
tags: [decision, camera, interaction, input, zoom]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
---

# Context

[ADR-008](/decisions/adr-008-orbit-camera.md) made the wheel a centred zoom: the
camera sits on a sphere around a look-at point and a notch scales that sphere,
so everything on screen expands or contracts about the centre. That is rarely
what the player wants. They point at a moon, a station, or a patch of empty
ecliptic and expect the wheel to dive at *that*, not at the middle of the
frame.

Space was pause. With zoom-to-pointer, getting lost is easier, and a single key
that returns to the ship is the natural escape. Pause can move.

# Decision

1. **A wheel gesture anchors the point under the pointer on the look-at plane.**
   `CursorZoom.PointerNdc` turns the pointer into normalised device coordinates;
   `CursorPointOnLookAtPlane` returns the world point the ray meets on the plane
   through the look-at point that is perpendicular to the view. The anchor is
   held in ship-relative render units.

2. **The look-at is solved from the anchor every frame of the tween.**
   `CursorZoom.AnchoredLookAtUnits` returns the offset that puts that point under
   the same screen position at the current distance and rotation, at the
   look-at plane's depth. `ShipCameraRig.ApplyZoomAnchor` writes it after the
   distance ease and before the camera is placed. The anchored point therefore
   stays under the pointer while the zoom eases. Nothing rotates: yaw and pitch
   are still never written.

3. **The look-at offset is live at every zoom.** It is capped at 60 AU
   (`CursorZoom.MaxLookAtOffsetM`), and `Space` or a reset unwinds it. A
   cursor-anchored offset is not eased home on its own: the player put that
   point under the pointer, and pulling it back would move it.

4. **Space eases the view home in one second.** `HomeTransition` drives one
   smoothstep over `HomeDurationSeconds`: the distance eases **geometrically**
   (log-space, so a descent from the 80 AU ceiling has an even perceived rate)
   to **half the dominant body's SOI radius** (`LocalScale.RadiusM`) and the
   look-at offset unwinds to zero in metres. Yaw and pitch are kept, so the ship
   returns to the centre of the same remembered angle. The follow flags are
   cleared the way a drag clears them, but `AutoTarget` is kept, so an active
   flight's follow does not re-claim the camera and cancel the transition. A
   wheel or a moving drag cancels it.

5. **Pause moves to `P`.** Space is the camera home. The HUD help line, the
   input map, and the input tests change together.

# Consequences

**Positive**

* The wheel behaves like the player's attention: it dives at what they point
  at, without a new mode or a second camera.
* The local view can be looked at off the ship, which is what makes a
  cursor-anchored zoom meaningful there at all.
* Space is a direct, predictable way home, and its one-second smoothstep makes
  the return readable rather than a teleport.

**Negative**

* A zoom with the cursor away from the centre moves the ship off-centre, and
  the view is no longer always ship-centred. That is the point of the gesture,
  but it is a visible behaviour change from ADR-008.
* The 60 AU cap still applies. A corner-anchored zoom near the outer ceiling can
  ask for more than the cap, and the anchor then shifts at the extreme corner.
  The cap is kept because bounding the render-space offset outranks an exact pin
  at the last notch.
* The offset is real camera state at every zoom, so future camera work has to
  decide what it does with it (a reset, a frame, and Space clear it; an idle
  ship keeps it).
* `P` is a new pause key to learn, and Space no longer pauses.

# Alternatives considered

**Zoom about the screen centre.** Simple, but it ignores where the player is
looking, and the local band is where a moon or the ship itself is most likely to
be under the cursor.

**Orbit toward the pointer instead of offsetting the look-at.** Rotating would
change yaw and pitch, break the remembered-angle round trip, and make the wheel
a rotation gesture. Rejected: the look-at offset is already the camera's
off-centre state.

**Intersect the pointer ray with the ecliptic plane, or with the nearest body.**
A plane intersection is unstable when the view is edge-on; a body hit makes the
zoom depend on what is under the cursor, so the same gesture moves by different
amounts over empty space. The look-at plane is stable, distance-scaled, and
keeps the point exactly under the pointer.

**Put the home key on `H` and keep Space for pause.** Workable, but `H` already
toggles the HUD, and Space is the conventional "return to the subject" key.
Rejected once the pause move was accepted.

# Acceptance

`CameraRigTests` proves the NDC conversion, the anchor round trip (including a
centre pointer leaving the look-at alone), the anchored point's projected NDC on
every frame of a real off-centre zoom, the anchor surviving after the tween, and
Space landing exactly on the home distance with the remembered angle.
`OrbitCameraTests` proves the home transition's one-second smoothstep, its exact
endpoints, its zero-duration case, and its cancellation. `BootstrapSmokeTests`
drives the same through the composition root. `InputMappingTests` and
`InputDeviceTests` pin Space to the home and `P` to pause.

The rendered feel is the human check: one notch at a few pointer positions, and
Space from the outer ceiling and from a panned local view.
