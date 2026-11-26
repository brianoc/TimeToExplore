---
type: Decision Record
title: "ADR-021: the departure climb split and the 3×-floor trip window"
description: The escape conic's hyperbolic excess is chosen once per build by a rank-only Lambert cost, on by default; a little excess buys a longer coast and shrinks both fixed burns. The burn-only crossing keeps the minimum-energy climb and is now dominated, and the trip window stays three times the split-aware floor.
tags: [decision, simulation, navigation, departure, trajectory, performance, window]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T10:00:00Z }
---

# Context

Every go-to plan starts with a short break and a ballistic escape conic out of
the departure body's well ([ADR-019](/decisions/adr-019-station-calls-and-departure.md)).
The climb took the **minimum-energy parabolic** conic whenever the requested
trip allowed it and solved a hyperbolic excess only when it did not. It never
asked whether a faster climb would cost less overall.

Step 1 measured the trade at a fixed total time, Gateway → Mars at 0.3 g with
the conic capture. At 18 days the parabolic climb plus the two fixed burns came
to **706.091 km/s**; forcing a 10 km/s excess came to **276.099**, because the
climb shortens by 9.7 days, the coast absorbs it, and the departure burn and
brake shrink by more than the break grows. At 90 days the optimum relaxes to
about **1 km/s**. The curve is **not unimodal** — 10 km/s wins at 18 days, 5.25
at 30 and 1 at 90, and 16 is worse than 10 at 18 — so a golden-section search is
wrong.

Step 2 implemented the search behind a flag and found it cost about **0.6 s per
build** (eleven fast probe builds with their own pin loops). That cannot ship:
the floor search calls `Build` per probe, and the plot pays it more than twenty
times. Step 3 had to make the choice cheap before turning it on.

# Decision

1. **The split is chosen once per build, by a rank-only Lambert cost.** The
   geometry every candidate shares — the target's capture chain, its entry
   state, the handover bands and the departure body — is built once. A candidate
   is then costed with **one climb solve and one Lambert solve**:

   * the **break**'s integrated delta-v against the departure body, on the
     quintic the climb solve builds;
   * the **departure burn**'s change from the escape conic's ceiling state to
     the Lambert coast's departure velocity;
   * the **brake**'s change from the coast's arrival velocity to the chain's
     entry velocity.

   The capture chain's own cost is the same for every candidate because the
   arrival date is fixed, so it is left out. The cost is a model, not a plan: no
   arcs are measured, the burns are endpoint velocity changes, and the seed's
   burn and brake duration is common to the grid. A candidate whose climb cannot
   fit the time is skipped.

2. **The grid is `0, 1, 3, 6, 10, 16` km/s, then one refinement round.** The
   grid covers the measured lobes; one round probes the two midpoints on either
   side of the winner. The search is measured at **4–7 ms** per build, and it
   chooses the same excess speed as the full fast-probe search, or one grid step
   away, at 8, 18, 30 and 90 days and across sixteen route/duration cases.
   The same value is held for the whole build: the reference skeleton, the pin
   loop, the settle pass and the short-coast entry all use it. Re-choosing it
   inside the pin loop would move the fixed point.

3. **The burn-only crossing keeps the minimum-energy climb.** A burn-only
   crossing has no requested duration — its total is an output — so there is no
   fixed time to split against, and any excess would only add fuel. With the
   split on the coast paths, the coast floor is now **shorter** than the
   burn-only crossing (Mars 0.3 g: 7.17 d against 16.49 d), so `PlanFastest`
   returns the coast floor and the burn-only branch is **dominated**. It stays in
   the planner as a research entry point and as the short coast's seed.

4. **The trip window stays three times the split-aware floor.** The right-hand
   end is `minimum × 3`, applied to the new minimum. The old Mars end (about
   49.5 d) was three times the *old* 16.84-day floor, not a physical limit: the
   planner genuinely accepts up to ~295 d, and its feasible set is a set of
   synodic bands, not one slow boundary. Growing the window to that limit gave
   Mars 7.17–294.92 d, Uranus 38.38–2,086 d (whose certified max is refused) and
   a benchmark regression of +120% on the Uranus plot and +334% on its
   retime-max. The window therefore keeps the **3× rule applied to the new
   floor**: Mars 7.17–21.50 d, Venus 4.21–12.63 d, Uranus 0.3 g 37.01–111.02 d.
   The longer cheap trips remain buildable by a direct `PlanAtDuration`; they
   are outside the slider, as the 90-day Mars build was before this step.

5. **Stations keep the unforced climb.** A station plan has a velocity match
   instead of a capture chain, so the cheap cost has no chain to price against.
   Extending the split to stations is the next departure-split change, not part
   of this one.

# Consequences

**Measured at the epoch (2026-11-15), Gateway, conic capture, `Plan()` floors:**

| Target | Level | Floor | Total Δv | Window (3×) |
|---|---|---:|---:|---|
| Mercury | 1 g | **3.67616 d** | 1,649.4 km/s | 3.68–11.03 d |
| Mercury | 0.3 g | 6.322 d | 956.6 | 6.32–18.97 |
| Mars | 1 g | 4.23343 d | 1,929.2 | 4.23–12.70 |
| Mars | 0.3 g | **7.16751 d** | 1,070.1 | 7.17–21.50 |
| Venus | 0.3 g | 4.20901 d | 588.3 | 4.21–12.63 |
| Jupiter | 1 g | 9.194 d | 2,665.3 | 9.19–27.58 |
| Jupiter | 0.3 g | 13.58298 d | 2,133.8 | 13.58–40.75 |
| Uranus | 1 g | 28.18209 d | 2,483.4 | 28.18–84.55 |
| Uranus | 0.3 g | 37.00540 d | 2,004.9 | 37.01–111.02 |

Every floor chooses the 16 km/s grid cap; it is the only candidate that fits the
shortest time. The anchors re-baseline: Gateway → Mars 0.3 g is **817.166 km/s
at 8 d** (16 km/s), **276.099 at 18** (10), **244.918 at 20** (8), **154.909 at
30** (6) and **38.541 at 90** (1). The old sub-floor times — Mercury 1 g 8 d,
Mars 0.3 g 10 d, Uranus 1 g 35 d — are now **buildable** at 526.744, 567.752 and
1,951.856 km/s.

* **Search cost:** 4–7 ms per build, against 0.6 s for the full fast-probe
  search. The certified plot still pays the certified build; the chooser itself
  is inside the noise.
* **Exactness:** every chosen plan closes its seams inside the profile's gate
  and arrives on the parking orbit. The worst seam across the measured floors is
  848 m and 0.067 m/s (Mercury 0.3 g).
* **The floor search has two solver retries that the fast end needs.** A refused
  certificate is retried as a certified build 0.2% and 0.5% above before a
  full-field probe bisection, and the pinned departure solve retries once from a
  1% longer burn when its damped Newton stalls within ten tolerances. Neither
  moves a tolerance or the chain gate; together they keep the floor search near
  its old cost and let the cold and seeded builds agree at the fast end. The
  Uranus 0.3 g floor settled at 37.005 d after the second retry (it was 38.378
  before).
* **The short-coast gap is empty at the current epoch.** The branch filled the
  range between a shorter burn-only minimum and a later coast floor; the split
  inverts that, so `PlanAtDuration` refuses below the floor and builds the coast
  above it. The branch stays for a route whose burn-only crossing still wins.
* **The window no longer reaches the 90-day Mars build.** It is a direct
  `PlanAtDuration` call, as it was before the split with the 3× cap.
* **The old trade reverses its sign at the fast end:** a faster trip now costs
  more on the coast path, as it always did, but the *floor* itself is much
  faster and much more expensive (Mars 0.3 g: 16.84 d/1,017 km/s before, 7.17
  d/1,070 km/s now).

# Alternatives considered

**Keep the full fast-probe cost and cache the choice.** Rejected: the choice
depends on the requested duration, and the floor search asks for a new one on
every probe. The rank-only Lambert cost is the same decision at 1/100th the
cost.

**Cost the reference skeleton.** Rejected in step 2: its departure is one fused
powered quintic whose cost falls monotonically as the climb shortens (851.7 →
355.6 km/s from 0 to 16 at 18 d), so it always picks the 16 km/s corner.

**Golden-section search.** Rejected: the curve has several local minima.

**Grow the window to the planner's true slow limit.** Rejected: the feasible set
is a set of synodic bands reaching years, not one boundary. It would put a
2,000-day trip on the slider and take the bench's retime-max and drag phases
with it, for a change whose purpose was to *keep* the existing slow end. The 3×
rule applied to the new floor preserves the same shape at a much faster left
end.

**Let the burn-only crossing take the split.** Rejected: its duration is an
output, so it has no fixed time to trade against; the split there shortens it at
a large fuel cost and makes the minimum a brachistochrone-shaped plan that the
coast floor now beats on fuel.

# Acceptance

`DepartureClimbSplitTests` (`flight`) compares the shipping chooser with the
full step-2 search at 8, 18, 30 and 90 days and pins the chosen excess speed,
the totals, the 50 ms budget, the exact arrival and the closed seams.
`BurnCoastBurnTripPlannerTests` pins the floors, the 3× windows, the new
fixed-duration anchors, the old sub-floor times as buildable, the burn-only
crossing as the longer plan, the empty short-coast gap, the break's
split-dependent escape state and the propellant ordering.
`FlightSessionTests` and `WarmStartTests` fly the coast-floor minimum and prove
the cold and seeded builds agree across the slider. `SolarSystemHudTests`,
`BootstrapSmokeTests`, `FlightReportTests`, `TripPlannerTests`,
`StationTripPlannerTests` and `RouteBuildMetricsTests` pin the HUD window, the
readout and the plan shape. `tools/check.sh` and `tools/bench.sh` are the gates;
the benchmark baseline is re-recorded for the new floors and the newly feasible
reverse routes.

The reasoning is in [Navigation and arrival](/simulation/navigation-and-arrival.md),
the profile in [Burn–coast–burn trips](/simulation/burn-coast-burn.md), the
flight numbers in [The flight model](/simulation/flight-model.md), and the cost
in [Planning performance](/technical/planning-performance.md).
