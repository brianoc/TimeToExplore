---
type: Decision Record
title: "ADR-009: a blended system map that looks at ship + pan"
description: Keep the orbit camera, blend it to a top-down map as the zoom crosses the local sphere of influence, and let the map look at a capped pan offset instead of at the ship.
tags: [decision, camera, navigation, interaction, technical]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# Context

[ADR-008](/decisions/adr-008-orbit-camera.md) settled the camera for the ship:
the ship stays at the render origin, the camera orbits it, and it always looks
back at it. That is exactly right while the camera only has to show the ship, a
plan, and an arrival. It is not enough for the system map
([The system map and route preview](/technical/system-map.md)): a map must be
read from above, and the player must be able to browse to another part of the
system without flying there.

Panning creates the conflict. An orbit camera that always looks at the ship
cannot move its centre: the look-at *is* the ship. Browsing needs a second
look-at rule, and choosing that rule is this record's decision.

The other question is shape. A separate map camera, scene, or input mode would
duplicate the orbit state, need a transition, and break the design's one
continuous wheel gesture. It would also give the player two views to learn rather
than one that changes reading as it pulls back.

# Decision

1. **The map is a blend of the orbit camera, not a second camera.**
   `MapView` derives a weight `w` from the camera distance `d` and the dominant
   body's sphere-of-influence radius `r`
   (`x = clamp(ln(d/r)/ln(3), 0, 1)`, `w = x²(3 − 2x)`). The effective pitch
   lerps from the player's pitch to 90°, the rotation slerps to a top-down pose,
   and the camera position is built from the effective pitch. At `w = 0` the
   pose is exactly ADR-008's; at `w = 1` it is the map.

2. **The map looks at `ship + pan`.** This is the one deliberate bend of
   ADR-008. The pan is a look-at offset held in double-precision ecliptic metres,
   capped at 60 AU, and converted to render units once at the camera. In the
   local band the pan has eased to zero, so the camera looks at the ship again
   and ADR-008 holds without an exception.

3. **Input is routed by the blend at a strict `w > 0.5`.** Right-drag orbits
   locally and pans on the map. The wheel stays multiplicative at 1.25× per
   notch on both ranges; the map side extends the ceiling from the local
   5 billion km to 12 billion km. The boundary is one threshold, so a gesture
   cannot flip mid-drag.

4. **The player's yaw and pitch are never written.** The map pose is computed
   from them every frame. Zooming out and back therefore restores the exact
   remembered angle without a saved pose or a restore step.

5. **The world stays ship-relative.** The pan is a camera look-at offset, not a
   ship movement and not a second coordinate space; the world is still projected
   from the ship before the float cast ([ADR-002](/decisions/adr-002-double-precision.md)).
   The map does not take ownership of the ship, and Plot route stays data while
   Begin replans ([ADR-007](/decisions/adr-007-flight-director.md)).

6. **Clipping follows the blend.** The near plane keeps ADR-008's local rule at
   `w = 0` and blends to one thousandth of the map distance, capped at 12,000
   units. The far plane is raised to 40 billion km while the map is enabled so
   the ceiling plus a 60 AU pan and the catalogue's 60 AU width fit.

7. **The comparison switch is temporary.** `SolarSystemBootstrap.EnableMapView`
   gates the map per scene while the old local view is being compared. It
   defaults off for `SolarSystem.unity` and on for `SystemMap.unity`; it is not a
   gameplay mode, and a later change removes it or defaults it on.

# Consequences

**Positive**

* The player can read the whole system top-down, pan to another neighbourhood,
  and select a body from the map.
* One camera, one orbit state, and one input path serve both readings. There is
  no mode switch, no second scene camera, and no transition to interrupt the
  zoom.
* The remembered-angle round trip falls out of the blend: no pose is saved,
  restored, or overwritten.
* Precision is untouched: the ship is still the render origin and the pan is a
  camera offset converted once.
* A plotted route and its arrival ghost are readable at map scale, which is the
  point of the preview.

**Negative**

* ADR-008's "always looks at the ship" now carries a documented map exception;
  a reader has to hold both records.
* The pan is real camera state. Future camera work — chase framing, a cockpit,
  a cinematic jump — has to decide whether it respects the pan or clears it.
* The 12-billion-km zoom ceiling and the 60 AU pan cap are gameplay limits, not
  physical ones; both are constants that a future design may need to move.
* The map keeps the player's yaw rather than snapping north-up, so orbit lines
  are not axis-aligned. That is intentional orientation preservation, but it is
  a choice a first-time player may not predict.
* While `EnableMapView` is off, the shipped scene keeps the old camera and the
  old full-strength marker aid. The comparison switch is a temporary asymmetry.
* There is still no PlayMode coverage for wheel feel, real pointer hits, or the
  rendered map frame.

# Acceptance

Through the real bootstrap (`BootstrapSmokeTests`):

* One wheel gesture, routed notch by notch through the rig, climbs from the
  Gateway neighbourhood to the 12-billion-km map ceiling; the pose is top-down
  and the far clip is 40 billion km. The reverse gesture returns `w = 0` with the
  exact remembered yaw, pitch, and rotation.
* From that map, selecting Mars, plotting, and pressing Begin flies the
  Gateway→Mars plan through the bootstrap's own `Update`/`LateUpdate`:
  3,939 frames, 65.7 real seconds, arrival 2026-11-19 10:29:02 UTC, 0.0 m and
  0.0000 m/s from the plan, on the 3,898 km parking orbit at
  e = 4.3 × 10⁻⁹. These are the same numbers as the scene flight, because the
  map changes the camera and not the trajectory.

# Alternatives considered

**A separate map camera or scene.** Two poses, two input routes, and a
transition or switch between them. Rejected: it duplicates the orbit state and
breaks the one-gesture zoom that makes the map feel like the same view pulled
back.

**Fixed north-up map rotation.** Simpler to read at a glance, but it discards the
player's orientation at the band edge. Rejected for the first pass: the
yaw-preserving pole is a smaller change and keeps the round trip exact. A tilt
or north-up option is a later one-number change.

**Pan the ship instead of the camera.** The camera could keep looking at the
ship while the ship moves to the point of interest. Rejected: it breaks
ADR-007's single owner, constructs a fake ship position, and spends precision
on a camera gesture.

**A free-fly map camera.** Detach the camera from the ship and fly it. Rejected:
a second control mode and a second coordinate space, for a view that is meant
to stay ship-relative and readable.

**Keep looking at the ship and only rotate the map.** Cheap, but it cannot
answer "what is over there?" without flying there. Rejected: browsing is the
map's job.

# Revisit when

* a map shortcut key (`M`), a tilt instead of 90°, or map rotation is added;
* the ship gets a model and wants a chase or cockpit view over the pan;
* `EnableMapView` is removed or defaults on and the comparison asymmetry goes;
* a PlayMode suite can assert the rendered map, real pointer hits, and wheel
  feel rather than the rig's state.
