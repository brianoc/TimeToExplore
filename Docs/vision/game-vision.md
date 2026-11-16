---
type: Design Document
title: Game vision
description: The player fantasy, core loop, and scope of Time to Explore.
tags: [vision, game-design]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# One sentence

You own a small torchship in a physically grounded solar system, and every
journey is a negotiation between time, fuel, and the moving geometry of the
planets.

# The fantasy

Not a dogfight in space. Not a galaxy of invented planets. The fantasy is the
feeling of *actually going somewhere*: watching Earth shrink, spending days in a
steady one-gravity burn, flipping over at the midpoint, and matching course with
a station that has been falling around the Sun the whole time you were in transit.

The ship is small. The system is enormous. The numbers are real, or close enough
that the player can feel their shape.

# What the player does

The player is a pilot-owner: part courier, part surveyor, part freight captain.
A typical session is a chain of decisions:

1. **Choose a job.** Carry cargo, deliver a specialist, place a science package,
   relieve a station crew, or chase a survey target.
2. **Choose a trajectory.** Burn hard and arrive in days, or coast efficiently
   and arrive in months. Wait for a launch window, or pay to go now.
3. **Fly the burn.** Manage acceleration, thermal load, and the flip. The ship
   can hold 1 g, but not forever and not for free.
4. **Arrive.** Match velocity, cross the sphere of influence, insert into orbit,
   and dock. Arrival is a skill, not a cutscene.
5. **Recover.** Refuel, repair, sell data, take the next job.

The interesting decisions are made *before* the engine lights: which route,
which window, how much delta-v to spend, and what to abandon if the plan changes.

# Core loop

**Contract → plan → burn/coast → arrive → dock → deliver → refuel → contract.**

The loop is deliberately quiet. Tension comes from finite resources and orbital
geometry, not from enemies. A missed window or a bad fuel estimate is the
antagonist.

# Tonal reference

* The patient, procedural texture of *Kerbal Space Program*'s orbital mechanics,
  without its cartoon tone.
* The working-ship texture of *The Expanse*: crew spaces, maintenance, cramped
  habitation, long watches.
* The scale and silence of *2001* and *Interstellar*, used sparingly.
* The route-planning satisfaction of a freight simulator.

# What "grounded" means here

The game makes **one large speculative assumption**: a ship can sustain about
10 m/s² (one Earth gravity) for days. Everything else follows from known physics:

* Real planet masses, radii, and orbital elements.
* Real distances and light travel times.
* Real Keplerian motion for planets, moons, and asteroids.
* Real delta-v accounting (rocket equation, not magic fuel).
* Real orbital insertion and docking geometry.
* No artificial gravity except thrust; a 1 g burn conveniently *is* 1 g.

The torch is a **medium-future compact fusion drive**, closer to the Epstein
drive of *The Expanse* than to anything on a drawing board. It is assumed to be
mature, maintainable, and compact enough for a working ship. Its waste heat is
carried away by the reaction mass, so thermal management is not a player
resource. Fuel capacity and consumption are gameplay balance values; physics
constrains how a chosen delta-v budget turns into travel time, not how large the
tank "should" be.

Relativity is ignored: the fastest brachistochrone in this catalogue is a few
percent of light speed, where the correction is below the game's navigation
precision. See [flight model](../simulation/flight-model.md).

# Scope for the first playable

| In scope | Out of scope for v0.1 |
|---|---|
| Single player, PC, Unity | Multiplayer |
| The eight planets and major moons | Fictional star systems |
| 100 named main-belt asteroids | Full 1.3-million-object belt |
| Six Lagrange stations | Procedural station interiors |
| Keplerian ephemerides | Full N-body integration |
| One ship class, torch drive | Fleet management |
| Contracts, cargo, fuel, docking | Combat, weapons, piracy |
| Save/load of ship and world state | Persistent economy simulation |

# Open questions

* Is the starter ship's delta-v budget enough for Mars and back, or only a
  one-way trip with a depot stop? This is the main balance lever.
* Does the player fly every burn manually, or command an autopilot and manage
  the plan? The current prototype assumes a capable autopilot with manual
  override.
* How much down-time is fun? A 15-day Neptune transit at 1 g is short in
  narrative time but long in real time if the player refuses time warp.
* Do stations sell fuel, or is fuel produced at planetary surfaces and shipped
  out? The latter creates more freight gameplay but more simulation.
