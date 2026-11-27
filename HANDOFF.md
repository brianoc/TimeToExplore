# Handoff

**In progress (2026-11-27): the full-blast minimum-time route.** Make the
trip-time slider's left end the **time-optimal zero-coast crossing** instead of
the split-aware coast floor, while the window's slow end stays three times the
**coast** floor. The task is the third departure-split follow-up in
[the roadmap](Docs/production/roadmap.md); the split it builds on is
[ADR-022](Docs/decisions/adr-022-departure-split.md).

## Design (measured at the epoch, 2026-11-15)

- `TripPlanner.PlanFastest` runs a **five-point escape-excess grid**:
  `48 km/s × √burnLevelG × {0.6, 0.8, 1.0, 1.25, 1.6}`. Each candidate is one
  fixed-date `BuildBurnOnly` at the coast floor's arrival date, so the totals
  compare; the shortest is re-solved through `PlanBurnOnlyMovingTarget` so its
  capture chain belongs to the date it reaches.
- The grid is scaled by √level because the measured optimum does: 0.05 g prefers
  about 10 km/s, 0.3 g about 28, 1 g about 48. The five steps put the chosen
  plan within minutes of a finer scan.
- Gateway → Mars 0.3 g: **6.66965 d / 1,538.1 km/s** at a **26.3 km/s** excess,
  seven arcs, against the coast floor's 7.16751 d / 1,070.1. The gain is 6–14%
  across the measured routes; Mercury 1 g 3.22822, Venus 0.3 g 3.93440,
  Jupiter 0.3 g 12.71122.
- The window maximum stays **3 × the coast floor** (Mars 21.50253 d). `WindowFor`
  gets the floor from a new public `PlanFastest(..., out coastFloor)` overload.
- `PlanAtDuration` fills the window from the full-blast minimum upward. Where the
  split's 16 km/s climb is too long for a request just above the minimum
  (Mercury 1 g at 3.45 d leaves less than the 0.64-day climb), the short-coast
  branch retries the full-blast grid's excesses and takes the first shorter
  climb; the coast absorbs the slack. Every slider duration from the minimum to
  the coast floor builds at Mercury, Mars and Jupiter.
- Uranus 0.3 g has no feasible full-blast candidate and returns the coast floor,
  so the fallback is exercised by the suite.

## Step 1 — planner and window (this commit)

- `TripPlanner.PlanFullBlast`, the grid constants, the `PlanFastest` rewrite and
  the short-coast excess retry.
- `FlightSession.WindowFor` anchors `MaximumPreviewSeconds` to the coast floor.
- Updated EditMode anchors, new full-blast tests, the benchmark plumbing, ADR-025,
  the concept docs, and the log entry.
- Verify: `tools/check.sh` (every gate) — see the log for the result.

## Step 2 — flight acceptance and capture (next)

- Fly the full-blast Mars plan through `FlightSession.Advance` and prove the
  no-coast seam executes (thrust flips at the flip, arrival on the parking
  orbit).
- PlayMode capture of the left end and the short-coast step; write the PNG.
- Re-record `tools/bench.sh` and the player benchmark; update
  [Planning performance](Docs/technical/planning-performance.md) with the plot
  cost of the grid.
- Clear this handoff and mark the roadmap follow-up done.
- Verify: `tools/check.sh` (every gate) and the capture by hand without
  `-nographics`.

## Traps

- **Do not anchor the window to `PlanFastest`'s result.** That shrinks Mars from
  21.50 to 20.01 d and moves the slow end, which the roadmap forbids.
- The near-floor basin is scatter: the Editor and the .NET harness can pick
  different certified plans. Pin Editor values from the suite's own output, not
  from `tools/dotnet-bench`.
- A station has no capture chain, so `PlanFastest` returns its coast floor
  unchanged. Keep the station window test at 54.36495–163.09484 d.
