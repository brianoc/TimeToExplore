---
type: World Reference
title: Lagrange stations
description: The six starting stations, their Lagrange points, and their role in the game economy.
tags: [world, stations, lagrange, economy]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-24T04:15:13Z }
---

# The six stations

The only fictional objects in the starting system are six stations, each placed
at a real Lagrange point of a real pair of bodies. They give the map fixed
landmarks, give the delta-v economy somewhere to refuel, and are the first
destinations a station call can reach.

| Station | Pair | Point | Distance at epoch | Size | Stability |
|---|---|---|---:|---:|---|
| Gateway Station | Earth–Moon | L1 | 54,841 km from the Moon | 2.0 km cube | Unstable |
| Helios Watch | Sun–Earth | L1 | 1,475,557 km sunward of Earth | 2.5 km cube | Unstable |
| Nyx Anchor | Sun–Earth | L2 | 1,485,431 km anti-sunward of Earth | 2.0 km cube | Unstable |
| Ares Junction | Sun–Mars | L1 | 1,142,764 km sunward of Mars | 2.2 km cube | Unstable |
| Jovian Transfer Yard | Sun–Jupiter | L4 | 60° ahead of Jupiter; 795.7 million km from it | 3.5 km cube | Stable |
| Titan Reach | Saturn–Titan | L1 | 52,942 km from Titan | 2.0 km cube | Unstable |

Distances vary with the pair's separation, so these are epoch values, not
constants. At the epoch the Moon was near perigee (363,296 km from Earth),
which is why Gateway sits closer to the Moon than the average 58,000 km figure.
The Sun–Jupiter L4 point forms an equilateral triangle with the Sun and
Jupiter; the distance there is the side length of that triangle, not a small
parking offset.

# Individual stations

## Gateway Station — Earth–Moon L1

The oldest and busiest station. It sits between Earth and the Moon, close enough
to both to serve as a transfer hub: lunar surface traffic, Earth-orbit tugs, and
deep-space crews all pass through.

**Role:** crew transfer, lunar logistics, low-gravity habitation, medical
facilities, a small shipyard.

**Gameplay:** the tutorial destination. A short 3.4-hour brachistochrone from
Earth, a gentle first **station call** that ends at the 500 m hold point, and a
place to refuel before the Moon. Docking is a later phase.

## Helios Watch — Sun–Earth L1

A solar observatory and space-weather station, parked sunward of Earth where it
can see the Sun before Earth does. It is the system's early-warning system for
solar storms and the relay for much of the inner-system traffic.

**Role:** solar science, space-weather forecasting, communications relay,
emergency shelter.

**Gameplay:** contracts to deliver instruments and crew; a source of storm
warnings that can delay or endanger a burn.

## Nyx Anchor — Sun–Earth L2

A quiet-sector station on the far side of Earth from the Sun. It services
deep-space telescopes, stores cryogenic instruments, and serves as the last
inner-system stop for outbound traffic.

**Role:** telescope servicing, cryogenic storage, deep-space communications,
long-duration research.

**Gameplay:** a natural departure point for the outer system, and a place where
a ship can be outfitted for a long coast.

## Ares Junction — Sun–Mars L1

Mars approach control. It sits sunward of Mars, where arriving traffic can
rendezvous before committing to orbit or aerobraking. It also holds a propellant
cache for the Mars system.

**Role:** traffic control, propellant cache, transfer rendezvous, Mars orbital
insertion support.

**Gameplay:** the first "real" navigation challenge: a moving target, a long
braking phase, and a station that is not where it was at departure.

## Jovian Transfer Yard — Sun–Jupiter L4

The largest station, built in the leading Trojan swarm. L4 is dynamically
stable, so the yard does not need constant stationkeeping; it has grown into a
construction site, fuel depot, and industrial hub for the outer system.

**Role:** ship construction, fuel production and storage, Trojan resource
processing, outer-system mission support.

**Gameplay:** the gateway to Saturn and beyond. Reaching it is a delta-v
milestone; refuelling here makes the outer planets possible.

## Titan Reach — Saturn–Titan L1

A depot in the Saturn system, parked between Saturn and Titan. Titan's thick
atmosphere makes it the easiest place in the outer system to aerobrake, and the
station supports both atmospheric entry and Saturn-orbit operations.

**Role:** Saturn-system depot, aerobraking support, volatile processing, science
logistics.

**Gameplay:** the deep-frontier station. Reach it and the rest of the outer
system is a refuel away.

# Station calls

A station is a **Go To destination**, but not an orbit: it is a point target.
The planner ends a station call on the **hold point** — the station's own state
at the arrival date, offset **500 m astern along its velocity**, with the ship's
velocity matched (`TripPlanner.StationHoldDistanceM`). There is no sphere of
influence, parking orbit, approach or capture chain, and **docking is not
implemented**: the flight ends at the hold point and the last few hundred metres
are a later docking phase
([ADR-019](/decisions/adr-019-station-calls-and-departure.md)).

Where a station sits decides how the trip **starts**, not how it ends:

| Station | Inside a sphere of influence? | Trip shape |
|---|---|---|
| Gateway | Yes — the Moon's and the Earth's | local break and ballistic climb, then the coast and the velocity match |
| Titan Reach | Yes — Saturn's | the same local departure |
| Helios Watch | No — 1.48 million km from Earth, outside its 924,000 km sphere | heliocentric start: a finite burn on the ship's own state, then the coast and the match |
| Nyx Anchor | No — 1.49 million km from Earth | the same heliocentric start |
| Ares Junction | No — 1.14 million km from Mars, outside its 577,000 km sphere | the same heliocentric start |
| Jovian Transfer Yard | No — 795.7 million km from Jupiter | the same heliocentric start |

A station's catalogue `gm` is **0**, and its catalogue radius (1,000–1,750 m) is
a **visual placeholder**: the renderer draws a cube 2–3.5 km across. It is never
a physical body, never a parking orbit, and never a source of gravity. The
station stays exactly on its computed Lagrange point; drift and stationkeeping
are a later milestone.

# Why Lagrange points?

A Lagrange point is a place where the gravity of two bodies and the rotation of
the pair combine to let a station hold a fixed position relative to both. That
makes it a natural place for infrastructure:

* it is close to both bodies without being in a deep gravity well,
* it has a stable or semi-stable position, so stationkeeping is manageable,
* it is a useful rendezvous point for traffic between the two bodies,
* it is a dramatic location: a station hanging in the combined gravity field of
  a planet and its moon, or a planet and the Sun.

The station positions are computed from the current state vectors, not stored as
fixed coordinates, so they follow the pair as it orbits. The solver uses the
circular restricted three-body quintic for L1/L2 and the 60° geometry for
L4/L5. See [orbital mechanics](../simulation/orbital-mechanics.md).

# Stability and stationkeeping

L1, L2, and L3 are saddle points: unstable. A station there drifts away without
correction. L4 and L5 are stable for a mass ratio below about 0.0385, which the
Sun–Jupiter pair satisfies comfortably.

In the game:

* L1/L2 stations have a **stationkeeping budget**: propellant, thrusters, and
  regular maintenance. This is a source of contracts and a reason for fuel
  deliveries.
* Jovian Transfer Yard at L4 is stable: no stationkeeping cost, which is part of
  why it grew into the largest industrial site.
* A station that runs out of stationkeeping propellant is a mission, not a
  background detail.

The prototype keeps stations exactly on their computed points. Drift and
correction are a later milestone.

# Station services

The intended service set, in rough order of availability:

| Service | Gateway | Helios | Nyx | Ares | Jovian | Titan |
|---|---|---|---|---|---|---|
| Refuel | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Repair | ✓ | — | ✓ | ✓ | ✓ | ✓ |
| Crew change | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Cargo transfer | ✓ | — | ✓ | ✓ | ✓ | ✓ |
| Shipyard | ✓ | — | — | — | ✓ | — |
| Science | — | ✓ | ✓ | ✓ | ✓ | ✓ |
| Aerobraking support | — | — | — | ✓ | — | ✓ |

This table is a design target, not implemented behaviour.

# Visual placeholder

The prototype renders each station as a 2–3.5 km cube using a Unity primitive,
which is twice its 1.0–1.75 km catalogue radius. That is deliberately crude: the
cube is a landmark, not a model. At true scale
it is invisible from a distance, so the renderer applies a minimum apparent size
until the player is close. A later pass will replace the cube with a modular
station mesh, docking ports, radiators, and lights.

# Open questions

* Do stations produce fuel, or only store it? Production at the Jovian yard and
  Titan Reach would make the outer economy self-sustaining; production only at
  Earth would make the frontier dependent.
* Is stationkeeping a simulated resource or a narrative cost? Simulating it
  creates good contracts but adds bookkeeping.
* Should the player be able to buy a berth or build a station? That is a much
  larger economy design.
* How large is a station really? A 2 km cube is a marker; a realistic rotating
  habitat or fuel depot might be 100 m to several km across.
