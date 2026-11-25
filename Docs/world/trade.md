---
type: Concept
title: Trade
description: The first-pass trade model — commodities and markets as data, a volume-limited cargo hold, prices that are labels for now, and trading only on arrival.
tags: [world, trade, economy, commodities, cargo, stations]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-25T12:00:00Z }
---

# What exists in the first pass

Trade is a **data-driven market plus a cargo hold**. A commodity is a record in
a JSON file, a market is a list of offers against an entity id, and the player
moves whole units between the station and the ship while parked there.

The first pass deliberately leaves out currency, stock limits, price movement,
cargo mass in flight, docking and persistence. It is there to prove the data
path, the cargo arithmetic and the panel, not to be an economy.

# Commodities are data

No C# file names a commodity. Names, volume, mass and prices live in
`Assets/Resources/trade_catalogue.json`, loaded through
`TradeCatalogueLoader` ([ADR-020](/decisions/adr-020-trade-catalogue.md)). The
format is in [Data schema](/technical/data-schema.md).

A commodity carries:

| Field | Meaning |
|---|---|
| `id` | stable key, lowercase snake_case |
| `name` | display name |
| `volumePerUnitM3` | cargo volume one unit takes |
| `massPerUnitKg` | mass one unit adds, displayed only for now |
| `description` | one line for the panel |

One **unit** is an abstract crate, not a fixed tonnage: volume and mass per unit
are what make commodities differ. The first four:

| Commodity | Volume / unit | Mass / unit |
|---|---:|---:|
| Water | 1.0 m³ | 1,000 kg |
| Food | 1.2 m³ | 400 kg |
| Machinery | 1.5 m³ | 1,200 kg |
| Rare metals | 0.4 m³ | 2,200 kg |

A 40 m³ hold therefore carries 40 water units, 33 food, 26 machinery or 100
rare metals. Rare metals are the dense, compact cargo; food is the bulky one.

# Markets

A **market** is an entity id and a list of offers. Each offer names a commodity
and two prices:

* `buyPrice` — what the station charges the player,
* `sellPrice` — what the station pays the player.

A market row is the only statement that an entity trades. There is no per-body
flag in the solar catalogue and no default market. An entity without a row
cannot be traded with, and an entity with a row need not offer every commodity.

The six stations and the two inner planets (Earth and Mars) carry markets in the
first pass, which is enough to test a short hop and a long one. Offers are
illustrative spreads, for example at Gateway: water 8/6, food 12/9, machinery
20/15; at Earth: water 4/3, food 6/4, rare metals 60/48.

# Cargo

`CargoHold` holds a capacity in m³ and a unit count per commodity. Loading
checks volume only:

```
usedM3 = Σ units × volumePerUnitM3  ≤  capacityM3
```

Mass is derived for the panel (`Σ units × massPerUnitKg`) and is **not** read by
flight, thrust or the fuel model. One ship, one constant capacity of 40 m³,
until ship selection exists.

Purchases add units; sales subtract them. Whole units only. The station stock is
unlimited, and nothing is persisted: cargo resets with the session.

# When you can trade

Trading needs no new proximity rule. The flight session already ends a trip in
one of two ways — a parking orbit at a planet or the hold point at a station —
and both mean the ship is parked. So the gate is:

* the flight status is **Arrived**, and
* the arrival target is the selected body, and
* that body has a market.

Any accepted orbit counts at a planet; the hold point counts at a station. A
ship that is merely near a body, or still flying its brake, cannot trade.
Docking, when it arrives, narrows that gate rather than replacing it.

# The panel

The details panel gains a **Trade** row for any entity with a market. The button
is disabled unless the ship has arrived. Opening it lists one row per offer:

```
Water        1.0 m³/u   1,000 kg/u    buy 8   sell 6
[+1]  [+max]  [-1]  [sell all]
```

plus the hold's used and free volume. Prices are shown but never charged: a
transaction moves units only ([ADR-020](/decisions/adr-020-trade-catalogue.md)).
The panel is deliberately plain.

# Deferred

* **Currency** — prices become charges; a single balance is the obvious first
  form.
* **Station stock** — finite units, restocking, and depletion.
* **Price movement** — supply, demand, distance, or events.
* **Cargo mass in flight** — acceleration, delta-v and fuel change with load.
* **Docking** — the hold point is the current stand-in for a berth.
* **Persistence** — cargo survives a session.
* **Ship selection and multiple holds.**
