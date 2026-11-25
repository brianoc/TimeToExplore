---
type: Decision Record
title: "ADR-020: Trade data, cargo, and the first-pass market"
description: Commodities and markets live in a separate JSON behind a loader; cargo is volume-limited and integer; prices are labels and transactions move units only; trading is gated on arrival, not on a new proximity test.
tags: [decision, trade, economy, data, cargo, ui]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-25T12:00:00Z }
---

# Context

The prototype has no trade. It also has no economics: no money, no stock, no
cargo, and no concept of a ship that carries anything. Stations became reachable
destinations with [ADR-019](/decisions/adr-019-station-calls-and-departure.md),
so the next step is to give them something to trade and a player something to
carry.

Two things make this awkward if it is done carelessly. First, a commodity named
in C# becomes a code change for every price tweak and every new good, and it
couples the orbital catalogue to the economy. Second, "parked off a station or
orbiting a planet" invites a second definition of arrival, with its own distance
and speed thresholds, that will disagree with the one the flight model uses.

The first pass is a test of the data path, the cargo arithmetic and the panel.
Currency, stock, price movement, cargo mass and persistence are all deliberately
out of scope.

# Decision

1. **Commodities and markets are one separate data file.**
   `Assets/Resources/trade_catalogue.json` holds two flat arrays, `commodities`
   and `markets`, and is loaded through `TradeCatalogueLoader`. No commodity id
   or price appears in a runtime C# file; tests build their own fixtures. The
   file is separate from `solar_system.json` so a price change never regenerates
   the 137-body catalogue, and so the orbital data stays the catalogue
   generator's output.

2. **`JsonUtility` and `Resources`, behind a seam.** The project already loads
   its catalogue this way, and four commodities do not need asynchronous
   loading. Addressables and `StreamingAssets` are deferred, not ruled out: the
   UI reads through the loader, so swapping the source is one file.

3. **A market row is the whole definition of "this entity trades".** There is
   no per-body flag and no default market. The six stations and Earth and Mars
   carry markets first; an entity without a row cannot be traded with, and a row
   need not list every commodity.

4. **The layering follows the assembly graph.** The data types and the loader
   are `Core` (which owns `JsonUtility` loading); `CargoHold` and `TradeSession`
   are plain C# in `Simulation`, which has no Unity reference; the panel is
   `Presentation`. Trade touches neither the planner nor the flight session.

5. **Cargo is integer units limited by volume.** `CargoHold` holds a capacity in
   m³ and a count per commodity, and rejects a load that would exceed the
   capacity. Mass per unit is declared and derived for display; flight ignores
   it. One ship, one 40 m³ hold, until ship selection exists.

6. **Prices are labels in the first pass.** `buyPrice` is what the station
   charges and `sellPrice` what it pays, but a transaction moves units only:
   there is no balance, no currency type and no charge. The fields exist so the
   panel and the later currency step have the right shape.

7. **The trade gate is arrival, not proximity.** Trading requires
   `FlightStatus.Arrived`, the arrival target to be the selected body, and that
   body to have a market. Any accepted planet orbit counts; a station's hold
   point counts. There is no second distance or speed test, and a ship still
   braking cannot trade.

8. **Unlimited stock, no persistence.** A station trades any number of units and
   cargo resets with the session. There is no save system to integrate with.

9. **The panel is part of the details screen.** A `Trade` row appears for any
   market entity and is enabled on arrival. The screen lists each offer with
   volume and mass per unit, the buy and sell price, and the hold's used and
   free volume, with `+1`, `+max`, `-1` and `sell all` actions.

# Consequences

**Positive**

* Commodities, prices and the set of trading entities are content, not code.
  Adding a fifth commodity is a JSON row and a test fixture.
* The trade data cannot break the orbital catalogue, and the catalogue
  generator cannot overwrite prices.
* The gate reuses the flight model's own terminal state, so the button appears
  exactly when the HUD says the ship has arrived, and docking can later narrow
  the same gate.
* `Simulation` stays Unity-free, so the cargo arithmetic is testable without a
  scene and cannot accidentally read the frame clock.
* Removing currency was cheap because the price fields were kept; adding it
  later is a balance and a debit, not a schema change.

**Negative**

* Prices mean nothing until currency lands. Buying is free, and `sellPrice` is
  a display value rather than an incentive.
* Unlimited stock plus free buying makes the market a sandbox: there is no
  scarcity, no arbitrage and no reason to prefer one station's prices.
* Cargo mass is visible in the panel but has no effect, so the hold reads as a
  pure volume puzzle until the flight model consumes mass.
* Trading is only possible after arrival, so there is no long-range price
  comparison or route planning against prices yet.
* One 40 m³ constant is hard-coded for the single ship; a second ship forces a
  spec type.

# Alternatives considered

**Addressables for the trade data.** Rejected for now. It buys remote patching
and async loading for four rows that ship in the build, at the cost of groups,
labels and a build step. The loader seam keeps the door open.

**`StreamingAssets`.** A reasonable middle ground if prices must be patchable
without a client build, but it adds file IO and platform handling for no first-
pass benefit.

**Trade fields inside `solar_system.json`.** Rejected: it couples a generated
orbital file to hand-tuned prices, and every price change would run the
catalogue generator or edit generated output.

**A dictionary-shaped JSON with Newtonsoft.** Rejected at this size. Two flat
arrays fit `JsonUtility` and the existing dependency-free approach; the schema
already documents when to upgrade.

**A currency and a balance in the first pass.** Deferred by the brief. A single
`long` would be the smallest first form, but it drags in pricing sanity,
starting funds and UI, none of which the test needs.

**Finite station stock and restocking.** Deferred. It makes the trade loop a
game rather than a transfer, but it needs a restock clock and a stock model
before it can be balanced.

**A dedicated proximity rule for trading.** Rejected. It would duplicate the
arrival test with its own thresholds and drift from the flight model; the
session's terminal state is already the answer.

# Acceptance

When implemented, `TradeCatalogueTests` will pin the schema and the market
lookup (a missing market, a missing commodity, a duplicate offer, a negative or
non-positive volume or mass). `CargoHoldTests` will pin volume accounting, the
full-hold refusal, partial loads and whole-unit sales. `TradeSessionTests` will
pin that a transaction needs an arrived target with a market and moves units
without a balance. `TradePanelTests` will pin the row content and the
disabled-until-arrived state. `tools/validate_catalogue.py` or a sibling
validator will check the shipped JSON, and `tools/check.sh` is the gate.

The concept is [Trade](/world/trade.md); the file format is in
[Data schema](/technical/data-schema.md).
