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
so everything on screen expands or contracts about the centre. On the map that
is rarely what the player wants. They point at a moon, a station, or a patch of
empty ecliptic and expect the wheel to dive at *that*, not at the middle of the
frame. The same is true once they have panned: the centre of the screen is a
browsing position, and a centred zoom keeps a point they are not looking at.

The wheel and the pan are already the same state. [ADR-009](/decisions/adr-009-map-camera-pan.md)
gave the map a look-at offset, `ship + pan`, and eased it to zero when the view
returned to the local band. That ease was written for a map-only pan: it
guarantees that zooming back in lands on the ship. It also means a point pinned
under the pointer would drift home after the zoom, and it gives the player no
direct way back if a pan did leave them in the local band off the ship.

Space was pause. With zoom-to-pointer, getting lost is easier, and a single key
that returns to the ship is the natural escape. Pause can move.

# Decision

1. **A wheel gesture anchors the point under the pointer on the look-at plane.**
   `MapView.PointerNdc` turns the pointer into normalised device coordinates;
   `CursorPointOnLookAtPlane` returns the world point the ray meets on the plane
   through the look-at point that is perpendicular to the view. The anchor is
   held in ship-relative render units, exactly like the pan.

2. **The look-at is solved from the anchor every frame of the tween.**
   `MapView.AnchoredLookAtUnits` returns the pan that puts that point under the
   same screen position at the current distance and rotation, at the look-at
   plane's depth. `ShipCameraRig.ApplyZoomAnchor` writes it after the distance
   ease and the blend, and before the camera is placed. The anchored point
   therefore stays under the pointer while the zoom eases and while the pose
   tilts from the local orbit to the top-down map. Nothing rotates: yaw and
   pitch are still never written ([ADR-009](/decisions/adr-009-map-camera-pan.md)).

3. **The pan is live at every zoom.** On the map it is the browsed centre; in
   the local band it is the cursor-anchored offset the wheel put there. The
   camera looks at `ship + pan` at every zoom. ADR-008's "always looks at the
   ship" becomes the `pan = 0` case, and the local view is ship-centred only
   while the pan is zero.

4. **A cursor-anchored pan is not eased home.** The player put that point under
   the pointer; pulling it to the ship after the tween would move it. The
   map-return ease stays for pans that are not anchored, and the anchor is
   dropped by a gesture that actually moves (an orbit drag or an engaged pan),
   a new framing target, a reset, or a new wheel notch. A wheel over a HUD panel
   anchors at the look-at rather than at a point on the panel.

5. **Space eases the view home in one second.** `HomeTransition` drives one
   smoothstep over `HomeDurationSeconds`: the distance eases **geometrically**
   (log-space, so a descent from the 80 AU ceiling to the local band has an even
   perceived rate) to **half the dominant body's SOI radius** (comfortably
   inside the blend band, so the arrival is exactly `w = 0`) and the pan unwinds
   to zero in metres. Yaw and pitch are kept, so the ship returns to the centre
   of the same remembered angle. The follow flags are cleared the way a drag
   clears them, but `AutoTarget` is kept, so an active flight's follow does not
   re-claim the camera and cancel the transition. A wheel or a moving drag
   cancels it.

6. **Pause moves to `P`.** Space is the camera home. The HUD help line, the
   input map, and the input tests change together.

# Consequences

**Positive**

* The wheel behaves like the player's attention: it dives at what they point
  at, on the map and locally, without a new mode or a second camera.
* The anchored point stays put through the blend, so the top-down tilt does not
  slide the view sideways under the pointer.
* The local view can now be looked at off the ship, which is what makes a
  cursor-anchored zoom meaningful there at all.
* Space is a direct, predictable way home, and its one-second smoothstep makes
  the return readable rather than a teleport.

**Negative**

* A zoom with the cursor away from the centre moves the ship off-centre, and
  the local view is no longer always ship-centred. That is the point of the
  gesture, but it is a visible behaviour change from ADR-008.
* The 60 AU pan cap still applies. A corner-anchored zoom near the 12-billion-km
  map ceiling can ask for more than the cap, and the anchor then shifts at the
  extreme corner. The cap is kept because "the player cannot lose the system"
  outranks an exact pin at the last notch.
* The pan is now real camera state at every zoom, so future camera work has to
  decide what it does with a local pan (Space, a reset, and a frame clear it;
  an idle ship keeps it).
* `P` is a new pause key to learn, and Space no longer pauses.

# Alternatives considered

**Zoom about the screen centre unless the map is engaged.** Simple, but it
gives the player two different wheel meanings at the band edge, and the local
band is where a moon or the ship itself is most likely to be under the cursor.

**Orbit toward the pointer instead of panning.** Rotating would change yaw and
pitch, break the remembered-angle round trip, and make the wheel a rotation
gesture. Rejected: the pan is already the camera's off-centre state.

**Intersect the pointer ray with the ecliptic plane, or with the nearest body.**
A plane intersection is unstable when the view is edge-on and jumps when the
map tilts; a body hit makes the zoom depend on what is under the cursor, so the
same gesture moves by different amounts over empty space. The look-at plane is
stable, distance-scaled, and keeps the point exactly under the pointer.

**Keep the map-return ease while zooming.** It fights the anchor: the point
under the pointer drifts to the ship after the tween. Rejected; Space is the
explicit home instead.

**Put the home key on `H` and keep Space for pause.** Workable, but `H` already
toggles the HUD, and Space is the conventional "return to the subject" key.
Rejected once the pause move was accepted.

# Acceptance

`MapViewTests` proves the NDC conversion and the anchor round trip, including
that a centre pointer leaves the look-at alone. `OrbitCameraTests` proves the
home transition's one-second smoothstep, its exact endpoints, its zero-duration
case, and its cancellation. `CameraRigTests` drives a wheel gesture off-centre
through the real rig and checks the anchor's projected NDC on every frame of the
tween, that the pan survives in the local band while anchored, that Space lands
exactly on the local view with the remembered angle, and that a wheel or a drag
cancels the home. `InputMappingTests` and `InputDeviceTests` pin Space to the
home and `P` to pause.

The rendered feel is the human check: one notch at a few pointer positions on
the map and in the local band, and Space from the map ceiling and from a panned
local view.
