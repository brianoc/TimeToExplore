---
type: Decision Record
title: "ADR-008: an orbit camera around the ship at the render origin"
description: Keep the ship at the floating origin and orbit the camera around it, with drag-orbit, wheel zoom, destination framing, and a width rule for the plan line.
tags: [decision, camera, rendering, interaction]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-16T00:00:00Z }
---

# Context

Milestone 2 put the camera **at** the ship: the render origin was both the eye
and the subject, and the world was drawn relative to it. That was the simplest
form of the floating-origin rule and it worked while the camera only had to
point at things.

It fails as soon as a trajectory is drawn. The plan starts at the ship, so the
line's first vertex sat exactly on the near plane, and its width was derived
from the *farthest point of the plan*. Gateway→Mars at 1 AU gives a line 225
render units (225,000 km) across; a camera at the origin is inside it. The
player's report was "the camera gets stuck in something yellow".

The player also has no way to look around. Arrival is the one moment where the
view matters most, and it was a fixed pose.

# Decision

1. **The ship stays at the render origin.** No chase offset, no moving world
   root, no change to [ADR-002](adr-002-double-precision.md). The camera now
   *orbits* that origin: its position is the orbit offset in render units and it
   looks back at the ship. Precision is unchanged, because the world is still
   projected from the ship. (The map pan gives the camera a look-at offset,
   [ADR-009](adr-009-map-camera-pan.md), and cursor-anchored zoom makes that
   offset live at every zoom, [ADR-016](adr-016-cursor-zoom-and-home.md); "looks
   at the ship" is the offset-zero case.)
2. **The orbit is the primary control.** Right-drag turns yaw and pitch (±85°),
   the wheel zooms multiplicatively (1.25× per notch, 2,000 km to 5 billion km).
   `OrbitCamera` is plain C#, so the limits, the zoom curve, and the slew are
   tested without a scene.
3. **Framing is automatic until the player takes it.** A Go To, a debug jump, or
   a reset frames its destination. The aim and the distance follow separately,
   so a wheel zoom at the centre keeps the destination centred; only a drag stops
   the aim. (A later change anchors the wheel to the pointer,
   [ADR-016](adr-016-cursor-zoom-and-home.md), so an off-centre notch moves the
   look-at with it.)
4. **The auto distance is `clamp(0.6 × ship→target, 3 × target radius, 3.3 AU)`**,
   eased at 2.5/s. Far out it shows the whole plan; at arrival it sits three
   planetary radii back so the planet fills a useful part of the view.
5. **The near plane scales with the orbit** — 2% of the distance, clamped to
   200 km–50 km. A fixed 50 km clip would cut a moon open at the closest zoom.
6. **The plan line's width follows the camera distance**, not the path length:
   `0.0015 × camera distance`, which is about 1.5 pixels at the near end. The
   far end tapers, which is correct perspective.
7. **A placeholder ship marker sits at the origin**, ten pixels tall at any
   zoom, pointing along the thrust while the drive is on and along the velocity
   when it is off. It gives the orbit camera a subject and shows the burn axis.
8. **The view does not follow the thrust axis.** A capture burns retrograde;
   facing the burn would point the camera at empty space at the moment of
   arrival. The burn direction is on the path line and in the flight panel.
9. **The debug jump is a ship move, not a camera move.** It eases the ship to
   eight radii from the body and the rig frames the body. `FocusTransition` is
   position-only, because the camera is no longer a free pose.

# Consequences

**Positive**

* The player can look at the ship from any angle and zoom from a parking orbit
  to the whole inner system.
* The plan is readable instead of being a wall of colour in front of the lens.
* Arrival is watchable: the camera sits back from the planet and follows it.
* The near plane no longer clips nearby surfaces, so close inspection works.
* The camera has no coordinate-space cost: it is an offset from the origin.

**Negative**

* The camera can still be orbited inside a planet. It is pulled in toward the
  ship when its offset would put it inside a body, but that is a rescue, not a
  physical camera.
* The ship has no model, only a marker; the art pass will replace it.
* Auto-framing decides the view for the player until they touch the mouse, which
  is friendly on the first trip and can feel opinionated later.
* Two follow flags (aim and distance) are more state than a single "auto" bit,
  but a single bit makes a stray wheel notch stop the tracking.
* The camera's yaw/pitch are degrees and its distance is render units, so the
  rig has to convert with `UnitsPerMeter`; the core stays in metres.

# Alternatives considered

**A chase camera offset.** The classic third-person rig. Rejected: it
reintroduces metre-scale float error for nearby geometry and gains nothing over
orbiting the origin, because the ship is already the origin.

**A free-fly camera detached from the ship.** What milestone 0 had. Rejected: it
is a different ship state, and two position owners is what
[ADR-007](adr-007-flight-director.md) removed.

**Keep the camera at the origin and hide the line near the lens.** A band-aid.
It leaves the player unable to see their own ship or the arrival, and the line
still has to have a sane width.

**Clip the line against a small sphere around the camera.** Correct for the
"stuck inside" symptom but not for the width; the line would still be kilometres
thick at close range.

# Revisit when

* the ship gets a real model and needs a chase framing or a cockpit view,
* the player can hand-fly, which wants a look-around that does not fight the
  plan-following,
* the camera needs collision, occlusion, or a smooth transition back from
  auto-framing,
* a PlayMode suite can assert the rendered frame rather than the rig's state.
