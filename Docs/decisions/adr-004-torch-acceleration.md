---
type: Decision Record
title: "ADR-004: 10 m/s² as the default torch acceleration"
description: Keep one Earth gravity as the ship's design acceleration and make delta-v the scarce resource.
tags: [decision, simulation, flight, balance]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Context

The game's premise is realistic travel at a default acceleration of about
10 m/s². The distances are real, so the travel times follow from the
brachistochrone equations:

```text
t = 2 sqrt(d/a)
```

At 10 m/s², Earth–Mars is about 2.5 days and Earth–Neptune about 15 days. Those
are much shorter than a Hohmann transfer (months to years), because the ship is
not coasting; it is burning the whole way and flipping at the midpoint.

The same acceleration is expensive. Earth–Mars costs about 2,160 km/s of
delta-v, and Earth–Neptune about 13,200 km/s. A rocket with a 1,000 km/s exhaust
velocity would need a mass ratio of 8.7 for Mars and 500,000 for Neptune. Even a
10,000 km/s torch needs a mass ratio of 3.7 for Neptune.

# Decision

1. **Keep 10 m/s² as the default design acceleration.** It is one Earth gravity,
   which conveniently provides crew gravity during powered flight.
2. **Make delta-v the scarce resource.** The ship has a finite delta-v budget;
   fast brachistochrone trips spend it, slow coasting trips conserve it.
3. **Offer two travel regimes.** Torch brachistochrone (fast, expensive) and
   burn-and-coast (slow, cheap). The navigation computer presents both.
4. **Use stations as fuel infrastructure.** L1/L4/L2 depots make the outer system
   reachable as the player's budget and the station network grow.
5. **Treat the drive as the setting's one admitted impossibility, resolved in
   fiction as a mature compact fusion torch.** It is medium-future technology at
   ~10,000 km/s effective exhaust, with waste heat carried away by the reaction
   mass. The reactor is not simulated, and fuel capacity and consumption are
   balance values. *The Expanse* is the acknowledged tonal influence.

# Consequences

**Positive**

* The inner system is days away, which keeps a session moving.
* The outer system is weeks away, which gives scale without boredom.
* One gravity is a clean crew-physiology justification for the number.
* Delta-v creates route choice, refuelling, and progression.
* The Lagrange stations become mechanically necessary, not decorative.

**Negative**

* The energy budget is implausible: a 1,000-tonne ship at 1 g and 10,000 km/s
  exhaust implies about 50 TW of jet power. In fiction the waste heat leaves
  with the exhaust, so the ship does not need a radiator farm; the power source
  remains the handwave.
* A player can spend the budget and strand themselves; the game needs rescue and
  clear warnings.
* Long burns need an autopilot and time warp, which risks removing the player
  from the moment-to-moment flight.
* The outer system is only reachable with infrastructure, which constrains the
  opening map.

# Alternatives considered

**Lower acceleration (0.1–1 m/s²).** More physically conservative, but Earth–
Neptune becomes 48–153 days and the game slows to a crawl. Rejected as the
default, kept as a tuning option in the data.

**Unlimited fuel.** Removes the central tension and makes the map a toy.
Rejected.

**Teleport / fast travel.** Breaks the "real distance, real time" pillar.
Rejected.

**Chemical or near-term propulsion.** Cannot sustain 1 g for days. Would force a
different game about coasting and windows, which is a valid game but not this
one.

# Balance levers

* Starter delta-v budget: 3,000 / 5,000 / 8,000 km/s.
* Exhaust velocity: 3,000 / 10,000 / 30,000 km/s.
* Throttle authority and minimum thrust.
* Fuel production and depot placement.
* Cargo mass fraction.
* Time warp limits and auto-drop rules.

These are data values, not hard-coded constants. The flight model should be
written so the pacing can be tuned without rewriting the simulation.

# Revisit when

* playtesting shows the inner system is too fast or the outer system too slow,
* the fuel model makes the game a spreadsheet,
* the art and audio pass changes the feel of a long burn,
* the fusion-torch fiction starts to feel too magical, or too mundane, for the
  intended medium-future tone,
* a lower-acceleration "realistic mode" becomes a desired option.
