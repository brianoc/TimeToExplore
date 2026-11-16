---
type: Decision Record
title: "ADR-002: Double precision and ship-relative rendering"
description: Simulate in double-precision metres and render in a ship-relative frame.
tags: [decision, technical, precision, floating-origin]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Context

The simulation spans from a 10-metre ship to Neptune at 4.5 × 10¹² metres. Unity
transforms, physics, and rendering all use 32-bit floats.

At 1 AU, a float's unit in the last place is about 16 km. At Neptune it is about
500,000 km. Either is unusable for a ship, a station, or a docking approach. A
naive "big world" scene also loses depth precision because the camera near and
far planes are forced into an impossible ratio.

# Decision

1. **Simulation state uses `double`.** Positions and velocities are `Vector3d`
   (three doubles) in metres. The core model never stores a position as a
   Unity `Vector3`.
2. **Rendering is ship-relative.** Every frame, the renderer subtracts the ship's
   double position from the body's double position, scales to render units, and
   only then casts to float.
3. **The camera sits at the render origin.** The ship never moves in Unity
   space; the world moves around it. There is no rebase event and no floating
   origin root that gets shifted.

```text
renderPosition = (bodyPositionM − shipPositionM) × unitsPerMetre
```

# Consequences

**Positive**

* Float precision is spent on nearby geometry, where it matters.
* The ship can be anywhere in the system without a rebase hitch.
* No accumulation of float error over long flights.
* The implementation is small: one subtraction, one scale, one conversion.
* The same double state can be saved and reasoned about without a scene.

**Negative**

* Unity physics, colliders, and `Rigidbody` cannot be used for orbital motion.
  Any future local collision or docking simulation must run in a separate
  ship-relative bubble.
* World-space canvases and particles are impractical at solar distances.
* Render transforms are not stable identifiers; they are rebuilt every frame.
* Debugging in the Scene view is less intuitive because the world moves, not
  the camera.

# Alternatives considered

**Floating-origin rebase with float simulation.** Shift the world when the ship
gets far from the origin. This is a common technique, but it still simulates in
float and still needs a rebase event. It is more complex and less accurate than
doing the arithmetic in double.

**Scaled space / separate map scene.** Render the solar system at a compressed
scale and switch to a local scene near a body. Rejected because it breaks the
"one continuous, true-scale world" pillar and creates seams at the transition.

**Keep everything in one Unity scene at 1 unit = 1 km.** Float precision at
Neptune is then hundreds of kilometres, and the camera far plane is enormous.
It works for a map, not for a ship.

# Revisit when

* docking and collision require a physics bubble,
* a target platform has poor double performance (unlikely on desktop),
* the renderer needs to support world-space UI or large-scale particles.

The seam is `Vector3d.ToUnity()` and `Vector3d.FromUnity()`. If the strategy
changes, those conversions and `SolarSystemRenderer.Refresh` are the places to
change, not the orbital model.
