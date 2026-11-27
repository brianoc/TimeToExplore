---
type: Decision Record
title: "ADR-026: the trip window probes the coast profile"
description: The trip-time window's upper-end search asks the coast profile at each duration instead of the general planner, so a refused probe no longer pays the short-coast rescue; the window values are unchanged and the reverse-route first plot falls from tens of seconds to a few.
tags: [decision, simulation, navigation, trajectory, performance, window]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T15:00:00Z }
---

# Context

The departure-split follow-ups left two reverse-route first plots slow: the
Editor spent **38,628 ms** on Venus → Earth and **36,600 ms** on Uranus → Earth,
against 2.8–4.7 s on the outbound routes. Both were new capability, not
regressions — the old profile refused them — but a first plot that long is
not usable.

The cost was the trip window, not the floor search. The window's right-hand end
is three times the coast floor, and when the cap is refused,
`MaximumPreviewSeconds` bisects it with six more builds. Every probe called
`PlanAtDuration`, which on a coast refusal tries the short-coast rescue: a
burn-only moving-target solve at the probe's date. Above the coast floor that
rescue has no range to fill — its job is the gap between the full-blast minimum
and the floor — so the probes paid seconds to answer a question they had
already answered.

Measured on Venus → Earth at the epoch, 1 g, conic capture, in the Release
harness: the 41.99559-day cap cost **5.92 s** with the rescue and **0.18 s**
without; the three refused bisection probes cost **2.2–2.5 s** each, and the
accepted probes, where the rescue never ran, already cost **0.17–0.18 s**. The
window reproduced the Editor's 22.31016-day slow end exactly, so the mechanism,
not a runtime difference, was the cause.

# Decision

1. **The window probes the coast profile.** `TripPlanner.PlanCoastAtDuration`
   is the coast build and its reported plan, with no short-coast rescue.
   `FlightSession.MaximumPreviewSeconds` uses it for the cap and every bisection
   probe. Every probe starts at the coast floor and moves slower, so the
   short-coast gap is behind it; the slow end is the coast profile's own limit.
2. **Player-requested durations are unchanged.** `PlanAtDuration` keeps the
   rescue, so the short-coast gap below the floor still fills and a slider
   request at 12 days still gets the coast's reason when the rescue cannot
   build it.
3. **The window values do not move.** Venus → Earth stays 2.04971–22.31016 d and
   Uranus → Earth 64.15472–176.42547 d. The fix removes work, not choices.

# Consequences

* **First plots**: on the same tree and machine state, Venus → Earth falls from
  **53,840 ms to 11,194 ms** (−79.2%); Uranus → Earth moves from the recorded
  **36,600 ms to 10,577 ms** (−71.1%). The committed baseline was taken when
  this machine ran about **39% faster** — the unmodified tree still plots
  Venus → Earth in 53,840 ms, and every unchanged retime and Begin phase moves
  by the same factor — so the two comparisons bracket the same change. The
  remainder is `PlanFastest`; the window probes are a fraction of a second each.
  In the Release harness the Venus → Earth plot falls from about **15.4 s to
  2.99 s**, and Uranus → Earth to **1.84 s**. The Editor's absolute numbers
  differ near the floor basin — its Uranus floor is 64.15472 d against the
  harness's 63.06636 — but the window is no longer where the time goes.
* **A refusal is still a refusal.** A duration the coast profile refuses above
  the floor is refused by the window even if the short-coast seam could build
  it. The window is the coast profile's range; the short coast belongs below
  its floor.
* **The mid-retime refusal is untouched.** Venus → Earth's 12.18-day retime is
  below the floor, so the rescue still runs there and still costs seconds to
  refuse. That is the short coast doing its job on a request the player made,
  not a window probe.

# Alternatives considered

**Keep the rescue on every probe.** Rejected: it is the measured cost, and the
rescue cannot fill a range above the floor.

**Fewer window probes.** Rejected: six probes put the slow end within a few
percent of the planner's limit, which is finer than the slider needs; cutting
them coarsens every route's right-hand end to hide a cost that is now gone.

**Teach the planner the coast floor and skip the rescue above it in
`PlanAtDuration`.** Rejected: `PlanAtDuration` has no floor to compare against,
and a player request above the floor still deserves the short-coast answer if
the coast refuses on geometry.

# Acceptance

`FlightSessionTests.TheWindowProbesTheCoastProfileWithoutTheShortCoastRescue`
plots and probes Venus → Earth at 1 g: the window ends at 22.31016 d, the cap
and six probes are seven certified coast builds, and no burn-only build runs.
The existing short-coast gap test still passes, so the rescue survives below
the floor. `tools/check.sh` is the gate; `tools/bench.sh` records the first
plots.
