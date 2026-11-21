---
type: Decision Record
title: "ADR-003: Render scale and minimum apparent size"
description: Render at 1 unit = 1,000 km and clamp tiny bodies to a few pixels.
tags: [decision, technical, rendering, scale]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Context

The world must be true scale: planets the right size relative to each other and
to their orbits. But a true-scale solar system is mostly empty, and most of its
objects are sub-pixel from anywhere useful.

At 1 unit = 1 km, Earth is 6,371 units in radius and Neptune's orbit is 4.5
billion units. That is too large for Unity floats even with ship-relative
rendering, because distant bodies and orbit lines still cover billions of units.

At 1 unit = 1,000 km, Earth is 6.37 units and Neptune's orbit is 4.5 million
units. That is workable, but a 2 km station is 0.002 units and a 130 km asteroid
is 0.13 units—invisible at any useful distance.

# Decision

1. **Render scale: 1 Unity unit = 1,000 km** (`unitsPerMetre = 1e-6`).
2. **Minimum apparent size:** if a body subtends less than about 4.5 pixels, scale
   it up until it does. Smooth the scale factor so it does not pop.
3. **True-scale toggle (`T`):** disable the clamp when the player wants to see
   the real size.
4. **Ship-relative rendering** keeps nearby geometry precise (ADR-002).
5. **Camera:** near 0.05 units (50 km), far 20,000,000 units (20 billion km),
   60° FOV, reversed-Z.

The minimum size is a marker, not a physical scale. It is labelled as such in the
HUD and in the design documentation.

# Consequences

**Positive**

* The whole system fits in a single scene and a single camera.
* Planets are large enough to fly around; the Sun and gas giants feel huge.
* Asteroids and stations remain findable instead of disappearing.
* True scale is one key away, so the aid is honest and reversible.

**Negative**

* Distant markers can look larger than they should relative to each other. A
  station and a moon at the same distance can both render at 4.5 px.
* The clamp changes apparent size with distance, which can be confusing during
  approach. Smoothing helps, but the transition is visible.
* The depth range is extreme; reverse-Z is required, and a two-camera fallback
  may be needed on some platforms.
* A 2 km station cube at true scale is invisible beyond a few thousand km, so
  the marker is load-bearing for navigation.

# Alternatives considered

**1 unit = 1 km.** Planets are huge and the outer system is beyond float range.
Rejected.

**1 unit = 100,000 km.** Earth is 0.064 units; planets become dots and the
minimum-size clamp dominates everywhere. Rejected.

**Separate "overview scale" and "local scale".** Compress the outer system in
an overview and switch to true scale near a body. Rejected for v0.1 because it
breaks continuity and creates a visible seam. A future overview could still use
a different camera projection without changing the simulation.

**No minimum size.** Most of the catalogue is invisible, and navigation becomes
guesswork. Rejected.

# Revisit when

* the art pass introduces impostors and billboards,
* a two-camera far field is needed,
* the camera needs a wider range or a second reading,
* station and asteroid markers get distinct shapes or icons.
