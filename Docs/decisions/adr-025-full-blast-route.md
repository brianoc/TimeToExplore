---
type: Decision Record
title: "ADR-025: the full-blast minimum-time route"
description: The nav computer's minimum is the fastest zero-coast crossing, chosen from a small escape-excess grid scaled by the square root of the burn level; the trip window keeps its slow end at three times the coast floor.
tags: [decision, simulation, navigation, departure, trajectory, performance, window]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T10:00:00Z }
---

# Context

The split-aware coast floor ([ADR-022](/decisions/adr-022-departure-split.md))
made the burn-only crossing longer than the coast floor at every measured route,
because the crossing kept the minimum-energy **parabolic** climb while the floor
bought a short climb and a longer coast. The nav computer's minimum therefore
sat on the coast path, and the slider's left end was 7.16751 d to Mars at 0.3 g.

The crossing's duration is an **output**, so there is no requested time to trade
an excess speed against — but that does not force the parabolic climb. The
crossing can take the same short, hyperbolic climb the coast path takes, and its
total falls: the escape conic shortens, the two fixed burns grow, and the sum is
shorter than the coast floor at most measured routes.

# Decision

1. **`PlanFastest` searches the full-blast crossing on a small excess grid.**
   The grid is `48 km/s × √burnLevelG × {0.6, 0.8, 1.0, 1.25, 1.6}`. The scale is
   the measured optimum's own: the time-optimal excess is about 10 km/s at
   0.05 g, 28 at 0.3 g and 48 at 1 g, because the climb shortens with excess
   while the burns grow as `Δv/a`. Each candidate is one fixed-date crossing at
   the coast floor's arrival date, so the totals compare; the shortest is
   re-solved through `PlanBurnOnlyMovingTarget` so its capture chain belongs to
   the date it reaches.
2. **A probe ranks the grid; the winner is certified.** Each candidate is a
   two-body seam solve at a quarter of the samples and a looser gate
   (50 km / 50 m/s). The winner is rebuilt with the full field and the 1 km /
   1 m/s gate. The probe's own peak is not a verdict: its coarse arcs can read
   above the drive, while the certified rebuild flies at the commanded level.
3. **The window's slow end stays three times the coast floor.** `PlanFastest`
   returns the coast floor it built alongside the fastest plan, and
   `FlightSession` anchors the maximum probe to that number. Mars keeps
   21.50253 d; only the left end moves to 6.66965 d.
4. **The coast floor remains `Plan()`.** Fixed-duration anchors, the long-trip
   table, and the station profiles are unchanged. A route whose crossing is
   infeasible or no shorter — Uranus at 0.3 g — returns the coast floor.
5. **The short-coast branch fills the window from the minimum upward.** It is no
   longer dormant: from the full-blast minimum to the coast floor a requested
   duration is a burn-only seam with a conic coast. When the split's 16 km/s
   climb is too long for a duration just above the minimum — Mercury 1 g needs
   0.64 d of climb and a 3.45-day request leaves less — the branch retries the
   full-blast grid's excesses and takes the first shorter climb that fits; the
   coast absorbs the slack. A Lambert geometry refusal is tried too, but only a
   **feasible** short coast replaces that answer; otherwise the coast's own
   reason stands. At Mercury 1 g, Mars 0.3 g and Jupiter 0.3 g every slider
   duration from the minimum to the coast floor builds. The extreme reverse
   routes keep a patchy gap — Venus → Earth's 2.05-day minimum against a
   13.99853-day coast floor has durations the short seam cannot build — which is
   the reverse-route follow-up's territory, not a fake plan.

# Consequences

**Measured at the epoch (2026-11-15) from Gateway, 0.3 g unless stated, conic
capture, certified plans:**

| Target | Full blast | Coast floor | Excess | Δv |
|---|---:|---:|---:|---:|
| Mercury, 1 g | **3.22822 d** | 3.67616 d | 48 km/s | 2,408.2 km/s |
| Venus | **3.93440 d** | 4.20901 d | 26.3 km/s | 841.6 km/s |
| Mars | **6.66965 d** | 7.16751 d | 26.3 km/s | 1,538.1 km/s |
| Mars, 1 g | **3.71822 d** | 4.23343 d | 48 km/s | 2,813.2 km/s |
| Jupiter | **12.71122 d** | 13.58298 d | 26.3 km/s | 3,053.5 km/s |
| Uranus | 37.00540 d | 37.00540 d | — | 2,004.9 km/s |

The full-blast plan is seven arcs: break, escape, departure burn, brake, the
capture chain's coast, capture, parking. It has **no cruise coast**. The gain is
6–14% on the measured routes; the price is fuel, as it always is at the fast
end. The window maximum is unchanged at 3× the coast floor.

* **Plot cost.** The grid adds two fixed-date certified builds (the winner's
  confirmation) and five cheap probes to a plot. In the Release harness Earth →
  Mars moves from 536 to 936 ms; the Editor pays roughly four times the
  difference. The old `PlanFastest` also built a fixed-date crossing, so the
  regression is the certified confirmation, not the grid.
* **A probe is not a plan.** `BuildBurnOnly(..., probe: true)` must never be
  returned: its peak is meaningless and its seam is only within the probe gate.
  The grid reads it for its total only.
* **The window's gap is filled.** The short-coast branch builds every duration
  from 6.66965 d to the 7.16751-day coast floor at Mars, and the same range at
  Mercury and Jupiter, after the branch was taught to retry the full-blast
  excesses when the split's climb does not fit. The slider has no dead step.

# Alternatives considered

**Keep the parabolic crossing and leave the minimum on the coast.** Rejected:
that is the state ADR-022 left, and the crossing's shorter total is a real
capability the profile can fly.

**Search the excess with a golden-section.** Rejected: the crossing's time
curve is shallow and its basins are not guaranteed unimodal, the same reason
ADR-022 rejected it for cost.

**Return the fixed-date probe winner directly.** Rejected: the probe is a
two-body, coarse solve; its plan is neither certified nor self-consistent.

**Grow the window from the new minimum.** Rejected: the roadmap keeps the slow
end anchored to the coast floor, so Mars stays at 21.50 d rather than 20.01.

# Acceptance

`BurnCoastBurnTripPlannerTests` pins the full-blast minimums, the seven-arc
shape, the no-coast seam, the grid's best-of choice and the coast-floor fallback
(Uranus). `FlightSessionTests` flies the new minimum, keeps the Mars window at
6.67–21.50 d, and proves the short-coast step above the minimum. The existing
station and coast-floor anchors are unchanged. `tools/check.sh` is the gate.
