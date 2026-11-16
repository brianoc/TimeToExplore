---
type: Decision Record
title: "ADR-001: Keplerian on-rails orbits"
description: Use two-body Keplerian propagation for the world instead of N-body integration.
tags: [decision, simulation, orbital-mechanics]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Context

The game needs a solar system that moves realistically over months and years.
The first prototype has 137 bodies: the Sun, eight planets, 22 major moons, 100
main-belt asteroids, and six stations.

The obvious alternatives are:

* integrate everything with Newtonian gravity every frame,
* precompute high-fidelity ephemeris tables,
* use two-body Keplerian elements,
* link a full ephemeris library such as SPICE.

The constraints are a solo/small-team Unity project, deterministic behaviour,
fast iteration, and enough accuracy that launch windows and station geometry
feel real.

# Decision

Every planet, moon, and asteroid is propagated as a **two-body Keplerian orbit
around its parent**. The Sun is fixed at the origin. Bodies are updated
parent-first. Stations are positioned from the current state vectors of their
Lagrange pair.

The ship is *not* on rails. Its trajectory will be integrated numerically under
thrust and gravity. The world is on rails; the player is not.

# Consequences

**Positive**

* Deterministic: a Julian date uniquely determines every position.
* Cheap: 137 Kepler solves per frame is negligible.
* Simple to save: persist the date, not the world.
* Easy to reason about: every body has a clean parent and a set of elements.
* Accurate enough for the game: planet positions are good to arcminutes over
  the fitted interval, which is far below navigation precision.
* No drift or instability over long campaigns.

**Negative**

* No N-body perturbations, so the outer planets and asteroid belt are slightly
  wrong over long spans, and close encounters are not modelled.
* Moon phases are authored, not ephemeris-grade.
* The Earth–Moon barycentre is approximated.
* A station at an unstable Lagrange point is held exactly on the point; there
  is no drift or stationkeeping simulation yet.
* A future binary or close-approach scenario would need a different model.

# Alternatives considered

**Full N-body in Unity.** Rejected for the first prototype: expensive, sensitive
to timestep, non-deterministic across frame rates, and prone to drift over
months of game time. It is also unnecessary for the intended fidelity.

**Precomputed ephemeris tables.** Attractive for accuracy, but large, awkward to
extend, and still needs interpolation. It is a good later upgrade for the Moon
and the gas giants if precision becomes important.

**SPICE kernels.** The right answer for real mission design, but a heavy
dependency and a large data pipeline for a game prototype. It can be added
behind the same `BodyRuntime` interface later.

# Revisit when

* a campaign runs long enough for perturbations to matter,
* the player can visit close binaries or perform precision flybys,
* moon positions need to be accurate for navigation,
* stationkeeping drift becomes a gameplay system.

The `OrbitalElements` and `BodyRuntime` interfaces are the seam: a future
ephemeris source can replace the propagator without touching the renderer, HUD,
or ship.
