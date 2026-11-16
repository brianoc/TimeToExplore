---
type: Design Document
title: Design pillars
description: The six rules that settle design arguments for Time to Explore.
tags: [vision, pillars, game-design]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Pillars

These are not features. They are the tests a proposed feature has to pass.

## 1. Real distance, real time

The solar system is to scale. There is no tactical warp, no "travel to planet"
menu, and no compressed map that secretly changes the physics. If a journey takes
two days at one gravity, the simulation advances two days.

Time acceleration is a **viewing tool**, not a teleport. It changes how fast the
clock runs, never where the ship is or how much fuel it spent.

**Test:** does the feature preserve the relationship between distance, speed, and
time? If not, it is cosmetic and must be labelled as such.

## 2. The rocket equation is the antagonist

Every fast trip costs delta-v. Every cheap trip costs time. The player should
feel the trade in their hands, not read it in a tooltip.

The default 10 m/s² torch is powerful enough to make the inner system a
days-long trip, and expensive enough that the outer system needs depots,
stations, and planning.

**Test:** can the player explain why this trip is expensive in one sentence?

## 3. A living orrery

Planets, moons, asteroids, and stations move. Launch windows open and close.
A station at a Lagrange point is not parked in space; it is falling around a
moving pair of bodies. The map the player sees at departure is wrong by the time
they arrive unless the navigation computer accounts for motion.

**Test:** would the feature still work if every body were stationary? If yes, it
is not using the orrery.

## 4. Arrival is the hard part

Launch is a number. Arrival is a geometry problem. Matching a planet's velocity,
crossing a sphere of influence, capturing into orbit, and docking without
crashing are the moments where the simulation becomes a game.

**Test:** does the feature give the player a decision at the destination, not just
at the departure?

## 5. Grounded speculation, one big lie

One fantastic assumption: a compact fusion torch can sustain 1 g for days. It is
medium-future technology, not a laboratory stunt, and its waste heat leaves with
the reaction mass, so the ship is a working vessel rather than a radiator farm.
Fuel capacity and consumption are tuned for play. Everything else—masses,
orbits, delta-v, light delay, orbital insertion—is played straight.

**Test:** if we removed the torch drive, would the rest of the scene still be
physically honest?

## 6. Quiet wonder

Space is mostly empty, and that is the point. The game should leave room for the
scale to land: a pale Earth, a sunrise over Titan, the long silence between
burn and capture.

**Test:** does the feature add noise, or does it add weight?
