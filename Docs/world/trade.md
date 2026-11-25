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
illustrative spreads. Gateway offers all four commodities — water 8/6, food
12/9, machinery 20/15, rare metals 60/48 — Earth offers all four and cheaper
(4/3, 6/4, 14/10, 60/48), and Helios Watch offers three (10/7, 14/10, 24/18).
Mars, Ares Junction, the Jovian Transfer Yard and Titan Reach sit at the far end
of the same spreads.

# Cargo

`CargoHold` (`Simulation`, plain C# with no Unity reference) holds a capacity in
m³ and a whole-unit count per commodity id. Loading checks volume only:

```
usedM3 = Σ units × volumePerUnitM3  ≤  capacityM3
```

The hold clamps rather than throws at a bad request. `Add` takes at most what the
free volume allows and returns the units actually added; `Remove` takes at most
what the line holds and returns the units actually removed; a zero or negative
request moves nothing. `CanAdd` agrees with `Add`, and `MaxUnits` is the whole
units that fit, with a small epsilon so a 40 m³ hold really takes 100 units of
0.4 m³ and 33 of 1.2 m³. A line that reaches zero is dropped from the view.
`Units` is a read-only map for the HUD, and `UnitsOf` and `VolumeOf` answer a
single commodity.

Mass is derived on demand from units × mass per unit (`MassKg(catalogue)`) and is
**not** stored or read by flight, thrust or the fuel model. One ship, one
constant capacity of 40 m³, until ship selection exists.

`TradeSession.TryBuy` and `TrySell` wrap those two calls: they check the offer
and move units only, so a transaction never changes a price. Whole units only.
The station stock is unlimited, and nothing is persisted: cargo resets with the
session.

# When you can trade

Trading needs no new proximity rule. The flight session already ends a trip in
one of two ways — a parking orbit at a planet or the hold point at a station —
and both mean the ship is parked. So the gate is:

* the flight status is **Arrived**, and
* the arrival target is the selected body, and
* that body has a market.

Any accepted orbit counts at a planet; the hold point counts at a station. A
ship that is merely near a body, or still flying its brake, cannot trade.
`TradeSession.IsOpen(status, targetId, bodyId)` is the pure gate and has no
position or speed test. `IsOpenAt(flight, body)` adds the market check through
`HasMarket`, which is the row in the trade file. `OffersFor(bodyId)` returns the
body's rows, or an empty list when it has no market. Docking, when it arrives,
narrows that gate rather than replacing it. `MaxBuyable` gives the panel's
`+max` in whole units and is zero when the offer is missing.

# The panel

The details screen keeps a **Trade** button in its footer, beside Jump: the two
share the row, half the content width each, and the button is hidden entirely for
a body without a market row. It is visible but non-interactable until the ship
has arrived, so an unvisited market reads as something to come back to rather
than as missing. It never opens a transaction itself.

The **Trade panel** is a second, centred panel (700×300 reference pixels) rather
than a taller details screen. Its title is `Trade · <body name>`, then the hold's
cargo line, then one row per offer:

```
COMMODITY   VOLUME   MASS         BUY  SELL  HELD
Water       1 m³/u   1,000 kg/u     8     6     0   [+1] [+max] [-1] [sell all]
```

and a Close button. The four actions call `TradeSession.TryBuy` and `TrySell`
directly — `+1` and `+max` buy, `-1` and `sell all` sell — and the row's held
count and the cargo line refresh on the same frame. `sell all` sells what the
hold has, not a number the panel guesses. Prices are labels: nothing is charged
([ADR-020](/decisions/adr-020-trade-catalogue.md)). The panel closes with the
details screen, with a new selection, or with its own Close button, and it never
opens for a body without a market.

Rows are pooled and hidden when a market offers fewer commodities, so a fifth
commodity needs no rebuild, only a longer panel. The text is built by
`TradeReport` in `Presentation`: `Rows` (offers in market order), `CargoText`,
`VolumeText` and `MassText`, all pure functions, so the content is tested without
a scene. `SolarSystemBootstrap` owns the state: it loads
`trade_catalogue.json` through `TradeCatalogueLoader` at startup, creates the
40 m³ `CargoHold` and the `TradeSession`, and exposes them as `Trade`. The HUD
only reads and calls them. A missing or invalid trade file logs an error and
disables the bootstrap, the same way a missing `solar_system.json` does.

# Deferred

* **Currency** — prices become charges; a single balance is the obvious first
  form.
* **Station stock** — finite units, restocking, and depletion.
* **Price movement** — supply, demand, distance, or events.
* **Cargo mass in flight** — acceleration, delta-v and fuel change with load.
* **Docking** — the hold point is the current stand-in for a berth.
* **Persistence** — cargo survives a session.
* **Ship selection and multiple holds.**
