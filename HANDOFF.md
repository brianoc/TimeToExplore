# Handoff

**Active plan:** route planning cost — measure, benchmark the routes, warm-start
the brake/departure pin, then prefetch the slider's neighbours.

Branch `work/route-warm-start`, worktree
`/Users/brian.oconnor/.local/share/opencode/worktree/6479b2/route-warm-start`.
Reasoning: [Planning performance](/technical/planning-performance.md) and
[The flight session](/technical/flight-session.md).

## Steps

1. **Measure a build.** Split one retime and one first plot into pin passes,
   reported arcs, brake integration, and allocation. Record the numbers in
   `Docs/technical/planning-performance.md`. No behaviour change.
2. **Benchmark the routes.** Six routes — Earth→Mars, Earth→Venus, Earth→Uranus
   and the three reverses — each through a plot (`PlanFastest` plus the window
   build, the real first plot), a retime at mid-window and at the maximum, and a
   Begin (`PlanFastest` at a moved date plus the retime). Its own command, a
   baseline file keyed by machine and Unity version, and a percentage delta
   table. Must not run in the default `tools/check.sh`; prove that it does not.
3. **Warm-start the pin.** Seed the next build from the nearest cached plan:
   brake duration and direction, departure-burn direction and duration, and the
   approach on pass 0. Never seed the escape ray. Fall back to the cold seed if
   the seeded run does not converge, and do not seed the two lowest slider steps
   or across the burn-only/coast gap.
4. **Prefetch the slider's neighbours.** After a plot, build two steps each side
   of the plotted duration; while the player drags, build three steps in the
   drag direction. Prefetch uses the preview's frozen departure state, never
   publishes, and yields to retimes and previews.

Step 4 is the caller's own change to the earlier sketch: at plot, two each side;
on drag, three in the direction of travel.

Not in this plan, but the biggest player-visible win if it is ever taken: `Begin`
replans with `PlanFastest` from the current state even when the preview is
seconds old and unmoved, so the player pays the plot twice. Reusing a fresh
preview is a semantics question (arrival date, requested duration, staleness
threshold), not an optimisation.

The split step 1 measured is in
[Planning performance](/technical/planning-performance.md#where-one-builds-time-goes-2026-11-24):
a retime's pin loop is 71% of the build, its reported 512-sample arcs 8%; a first
plot is 62% coast floor and 38% burn-only, with 162 of its 174 pin passes in the
fast probes.

## Traps

- Anchor values: the live Mars 0.3 g coast floor is **16.84485 d** and the Mars
  90 d anchor **38.9678 km/s** after ADR-019's departure break. The
  planning-performance doc's earlier 6.73042 d and 44.3543 km/s are the
  pre-departure values and must not be used as the comparison; `tools/check.sh`
  enforces the live ones.
- The preview starts at the window **minimum**, so a fresh plot has no lower
  side. Prefetch clamps at the window edges; expect the low side to be empty.
- The cache key is the **preview's frozen departure state**
  (`_previewDeparture.JulianDate`), not the live clock. A prefetch must capture
  the same values as the retime request or it fills slots nothing can read.
- Prefetch must not bump `_retimeGeneration` or `_previewGeneration`, must never
  publish, and must be dropped on discard, style change, burn-level change, and
  date change.
- Warm start must not move the anchors: the same plan to four decimals, floors
  unchanged, every seam inside 1 km / 1 m/s. Compare cold and warm over every
  slider step for Mars, Mercury, Jupiter, and both Uranus levels.
- The near-floor basin is a needle and the runtime selects it by last-bit libm
  differences. A seeded build that converges to a different needle is a real
  risk; the guard is the cold fallback, not a tolerance change.
- The player sees **~4 s per plot and another ~4 s at Begin** in Play Mode. The
  doc's current editor record is 2.81–5.07 s after the zero-coast crossing; the
  1.28–3.03 s table is pre-crossing and must not be quoted. A headless benchmark
  will read lower than Play Mode with the editor attached — record both and the
  ratio, and talk to the player in Play Mode numbers.

## Verification

```bash
tools/check.sh --fast
tools/check.sh --tests flight
```

Report the command and its result; do not write a test count anywhere.

## State

- [x] Step 1 — measure
- [ ] Step 2 — benchmark the routes
- [ ] Step 3 — warm start
- [ ] Step 4 — prefetch
