---
type: Decision Record
title: "ADR-016: Show what a journey costs in fuel, without a tank"
description: Charge the plan's delta-v against the rocket equation at a fixed exhaust velocity and print the propellant in the flight panel, while the ship keeps an unlimited tank and a constant mass.
tags: [decision, simulation, fuel, propulsion, ui]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
---

# Context

The flight model has always called delta-v the real currency, but the nav
computer only showed delta-v. A player could see that a faster trip cost more
and had no way to read that cost as propellant. The roadmap's milestone 3 is
the full delta-v economy: a finite tank, a plan refused when the budget cannot
pay, and a failure state when it runs dry.

That milestone needs a number to budget. The first pass should establish the
number and the units before the tank exists, so the later budget can be tuned
against something a player has already seen.

# Decision

1. **Propellant is the rocket equation at one exhaust velocity.**
   `FuelModel.ExhaustVelocityMps` is **10,000 km/s**, the flight model's design
   value (about 3.3% of light speed). For a plan's `DeltaVMps` and the ship's
   dry mass:

   ```text
   m_prop = m_dry (exp(delta-v / v_e) - 1)
   mass ratio = exp(delta-v / v_e)
   propellant fraction = 1 - 1 / mass ratio
   ```

2. **The ship has no tank and no mass change.** `TrajectoryPlan.PropellantKg`
   and `PropellantFraction` are readouts, not a budget. The integration still
   holds acceleration constant, the ship's `PropellantKg` is untouched, and
   every plan remains flyable. Consumption of the tank and mass-dependent
   acceleration stay in milestone 3.

3. **The flight panel prints one `Fuel` row per plan.** It reads the tonnes of
   propellant and the share of the mass at departure, for example
   `264 t · 20.9% of departure mass`. It sits beside `Delta-v`, so the derived
   cost is visible without a second screen, and it updates with the trip-time
   slider because the plan does.

4. **The dry mass is the plan's departure state, defaulting to 1,000 t.**
   `ShipState.DefaultDryMassKg` is the balance value already in the model. A
   caller with a different ship gets its own number.

# Why command thrust barely changes the fuel

The player controls **trip time** and, on the fixed-burn profiles, the **burn
level**. They are not the same kind of cost.

* On the powered quintic the thrust profile *is* the curve: the trip-time
  slider changes the curve, and the delta-v follows. There is no separate
  "thrust" control to compare.
* On the burn–coast–burn profile a fixed burn's delta-v is `a · T`. The
  duration `T` is solved from the velocity change the crossing needs, so
  raising `a` shortens the burn and leaves `a · T` nearly unchanged. Three
  times the level is nearly the same propellant at a fixed trip time; it is
  the same crossing flown with a shorter, harder burn. Measured at the epoch
  from Gateway to Mars at 12 days, 0.3 g spends 485.1 km/s (50 t) against 1 g's
  465.4 km/s (48 t) — a 4.2% difference for three times the level.

Journey time is therefore the fuel lever, and a steep one: the rocket equation
is exponential in delta-v, and the powered plan's delta-v rises roughly as
`1 / T` towards the fast end. A trip near the floor can need several times the
propellant of a gentle one, which is the trade the nav computer already shows
as fast-and-expensive against slow-and-cheap.

# Consequences

**Positive**

* The player can see a journey's propellant before a tank or a failure state
  exists, and the readout is derived from the delta-v the planner already
  certifies rather than from a second trajectory model.
* The fixed-burn profile demonstrates the design's intended surprise: command
  thrust changes the burn's duration, not its cost. The `Fuel` row barely moves
  when the level slider does.
* The balance values are in one place: change the exhaust velocity or the dry
  mass and every plan's readout follows.

**Negative**

* It is an estimate with no consequence: a plan is never refused or stranded
  for fuel, so the "fuel depot gambit" in the flight model is still fiction.
* Mass is held constant, so the readout is the propellant a mass-dropping ship
  *would* need, not a simulation of it. A plan near the mass-ratio limit is
  optimistic about acceleration.
* The single exhaust velocity hides the drive's efficiency trade. A later
  engine choice would need more than one value.

# Alternatives considered

**Wait for the full budget.** It would keep one fuel concept instead of two,
but it delays the number the budget needs and leaves delta-v as an abstract
unit in the meantime.

**Show delta-v only and call it fuel.** Cheap, but it removes the exponential
part, which is the whole reason fuel is a design lever. Two plans with 2,000
and 2,400 km/s look closer than they are once the tank is real.

**Let the burn level change the fuel at a fixed trip time.** That would make
the level slider feel more consequential, but it would be wrong for this
profile: the burn level is a texture control, and the velocity change is set by
the crossing.

# Revisit when

* milestone 3 gives the ship a finite tank and charges `PlanArc.DeltaVMps`
  against it;
* acceleration becomes mass-dependent, so the readout has to be integrated
  rather than evaluated from the plan's final delta-v;
* a second drive or propellant type makes exhaust velocity a ship property
  rather than a constant;
* the autopilot or manual flight can spend propellant outside a plan, so the
  readout needs a live "fuel remaining" row.
