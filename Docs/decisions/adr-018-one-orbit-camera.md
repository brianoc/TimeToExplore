---
type: Decision Record
title: "ADR-018: one orbit camera, no system map"
description: Remove the top-down system map and its comparison scene, keep one orbit camera with a single 2,000 km–12 billion km range, keep cursor-anchored zoom and Space home, and smooth the automatic framing.
tags: [decision, camera, interaction, zoom, removal]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T22:00:00Z }
---

# Context

[ADR-009](/decisions/adr-009-map-camera-pan.md) blended the orbit camera into a
top-down map as the zoom crossed the local sphere of influence. It shipped as a
comparison layer behind `SolarSystemBootstrap.EnableMapView`, with a second
scene, a map-only pan, and map-only marker, label, and line rules. In play the
map is not useful: the same wheel can reach the whole system without leaving the
ordinary orbit pose, and the tilt and the pan make the camera change character
under the player. The comparison switch never became a gameplay mode.

The camera also had two rough edges unrelated to the map:

* the automatic framing recomputed its distance from the moving ship every
  frame, so a flight kept re-targeting the zoom; and
* the body-avoidance rescue pulled the camera inward by a fixed fraction per
  iteration, so a body drifting past the orbit could step the view.

# Decision

1. **Delete the system map and everything that existed only for it.** That
   includes `MapView`, `SystemMap.unity`, `EnableMapView`, the map blend, the
   map pan, the comparison scene builder, the map marker/label/line rules, and
   their tests. `system-map.md` is kept only as a superseded historical page so
   older links resolve.
2. **One orbit camera, one range.** `OrbitCamera.MaxDistanceUnits` is
   12 billion km (about 80 AU), the previous map ceiling, now the only ceiling.
   The minimum stays 2,000 km. The camera never tilts, slerps, or changes pose
   with distance; the player's yaw and pitch are the only angles.
3. **Keep cursor-anchored zoom and Space home.** `CursorZoom` owns the
   pointer-to-NDC conversion and the look-at-plane anchor. The look-at offset is
   capped at 60 AU, is kept after the tween, and is unwound only by `Space` or a
   reset. There is no left-drag pan; left-drag never moves the camera.
4. **Capture the framing distance once.** `ShipCameraRig.Frame` captures the
   `clamp(0.6 × ship→target, 3 × radius, 3.3 AU)` distance on the first frame,
   holds it through the flight, and re-captures it once on arrival. A moving
   target no longer re-targets the zoom every frame.
5. **Avoid bodies continuously.** The camera stops at the near intersection of
   its ray with each body's clearance sphere instead of multiplying its distance
   by 0.6 in discrete steps. Grazing a body slides the camera along the orbit.
6. **Local scale is still needed.** `LocalScale` keeps the dominant-body choice
   (the smallest containing SOI, with a 1-million-km deep-space fallback) for
   `Space` home. The map's use of it is gone.

# Consequences

**Positive**

* One camera, one input meaning, and one zoom range. The wheel cannot put the
  player into a different reading of the system.
* The whole catalogue is reachable by wheel without a mode switch.
* A flight holds a stable view, and the arrival settles on the parking orbit
  instead of the distance chasing the ship every frame.
* Orbiting near a body no longer steps the camera inward.
* Less code: no blend, no map pose, no second scene, no map switch, no
  map-only drawing rules.

**Negative**

* The map's top-down reading and browse-to-another-neighbourhood interaction are
  gone. A player who wants the whole system sees it at the remembered angle.
* A cursor-anchored zoom off the centre still moves the ship off-centre. Space
  is the explicit way home.
* The 60 AU look-at cap still makes a corner anchor shift slightly at the outer
  ceiling.
* The arrival re-capture is a deliberate second zoom, eased over a few seconds.
  It is not a snap, but it is a visible change of framing at arrival.

# Alternatives considered

**Keep the map but default it off.** It would remain dead code and a second
pose the camera could still fall into. Rejected: the user asked for it to be
removed, not hidden.

**Keep the map as a separate scene or mode.** Two camera states, two input
routes, and a transition. Rejected for the same reason ADR-009 rejected a second
camera: it duplicates the orbit state and gives the player two views to learn.

**Drop the 12-billion-km ceiling.** Returning to 33 AU would make the outer
planets unreachable in one view. Rejected: "all the way out" should mean the
whole catalogue.

**Remove cursor-anchored zoom and pan entirely.** Simpler, but the player chose
to keep the wheel's pointer anchor. The look-at offset is the mechanism, so it
stays; only the map's left-drag pan is removed.

**Keep per-frame auto-distance re-targeting.** It is smooth in isolation, but it
means the camera is always chasing the ship and the target, and it is one of the
things the player reported as jumpy. Rejected in favour of one capture and one
arrival re-capture.

# Acceptance

`CameraRigTests` proves the single-range round trip, the 12-billion-km ceiling,
the captured framing distance and its arrival re-capture, the slew-rate turn
when the framing target changes, and continuous body avoidance (the camera
slides by at most the ship's own step and stays outside the body).
`BootstrapSmokeTests` proves the same through the real composition root, and the
Gateway→Mars acceptance still arrives on the parking orbit. `DrawingRulesTests`
proves the 4.5 px marker, the label thresholds, and the single 3 px line rule.
`tools/check.sh` is the gate.
