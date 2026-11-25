# Handoff — first-pass trade

**Branch:** `work/trade` in the worktree at
`/Users/brian.oconnor/.local/share/opencode/worktree/6479b2/trade`.
**Status:** step 1 next. Design merged to `main` at `c016ec5`.
**Design:** [ADR-020](/decisions/adr-020-trade-catalogue.md) and
[Trade](/world/trade.md). The plan runs one sub-agent per step; each step is one
commit carrying code, tests, concept updates, a `Docs/log.md` entry, and this
file.

## Steps

1. **Data and loader (`Core`)** — `trade_catalogue.json`, `TradeCatalogue`,
   `TradeCatalogueLoader`, `TradeCatalogueTests` (core), `tools/validate_trade.py`
   and its unit test, wired into `tools/check.sh`.
2. **Cargo and transactions (`Simulation`)** — `CargoHold`, `TradeSession`, the
   arrival gate, `CargoHoldTests` and `TradeSessionTests` (flight).
3. **HUD (`Presentation`)** — a Trade button in the details panel, a Trade panel,
   `TradeSession`/`CargoHold` owned by `SolarSystemBootstrap`, presentation tests.
4. **Close-out** — full `tools/check.sh`, evidence, roadmap, clear this file,
   merge to `main`.

## Fixed decisions

- Commodities and markets are one JSON at
  `TimeToExplore/Assets/Resources/trade_catalogue.json`; no commodity id or price
  in runtime C#. `JsonUtility`, two flat arrays, loaded behind a seam.
- A market row is the only statement that an entity trades. Six stations plus
  Earth and Mars.
- Four commodities: water (1.0 m³, 1,000 kg), food (1.2, 400), machinery (1.5,
  1,200), rare_metals (0.4, 2,200), per unit.
- Integer units, a 40 m³ hold, volume-limited. Mass is derived and displayed;
  flight ignores it.
- Prices are labels. Buying and selling move units only; no balance, no currency
  type. Buy price ≥ sell price ≥ 0 is a data rule.
- Unlimited stock, no persistence.
- Trade is open when `FlightSession.Status == Arrived`, the target is the body,
  and the body has a market. Any accepted planet orbit counts; a station's hold
  point counts. No separate proximity test.

## Layout notes

- `Core` may use Unity (`JsonUtility`, `Resources`); `Simulation` may not
  (`noEngineReferences`); the panel is `Presentation`.
- The details panel's footer is full (route, begin, close, jump). Put the Trade
  button in the footer row or open a **separate** Trade panel; do not squeeze
  four offer rows into the details body. The panel is code-built uGUI in
  `SolarSystemHud`.
- `SolarSystemBootstrap` owns the runtime state (`Model`, `Flight`, `Selected`);
  trade state belongs there too, not in the HUD.

## Verify

```bash
.venv/bin/python tools/validate_okf.py
tools/check.sh --fast
tools/check.sh --tests core       # step 1
tools/check.sh --tests flight     # step 2
tools/check.sh --tests presentation,smoke   # step 3
tools/check.sh                    # close-out
```

## Traps

- Do not commit to `main`; do not run Unity against the main checkout. The human
  has that editor open.
- `.venv` is a symlink to the main checkout's environment.
- `check.sh` runs Unity with `-nographics`, so a rendered screenshot is not
  available from the suite; evidence for step 3 is the panel assertions plus,
  if a graphics-enabled run works, a PNG.
- Add `trade_catalogue.json.meta` if Unity generates one; the JSON is an asset.
- Keep the log entry under `## 2026-11-25` unless the date has moved.

No active blocker.
