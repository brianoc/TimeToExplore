# Handoff

**Active plan:** route planning cost — measure, warm-start the brake/departure
pin, then prefetch the slider's neighbours.

Branch `work/route-warm-start`, worktree
`/Users/brian.oconnor/.local/share/opencode/worktree/6479b2/route-warm-start`.
Reasoning: [Planning performance](/technical/planning-performance.md) and
[The flight session](/technical/flight-session.md).

## Steps

1. **Measure a build.** Split one retime and one first plot into pin passes,
   reported arcs, brake integration, and allocation. Record the numbers in
   `Docs/technical/planning-performance.md`. No behaviour change.
2. **Warm-start the pin.** Seed the next build from the nearest cached plan:
   brake duration and direction, departure-burn direction and duration, and the
   approach on pass 0. Never seed the escape ray. Fall back to the cold seed if
   the seeded run does not converge, and do not seed the two lowest slider steps
   or across the burn-only/coast gap.
3. **Prefetch the slider's neighbours.** After a plot, build two steps each side
   of the plotted duration; while the player drags, build three steps in the
   drag direction. Prefetch uses the preview's frozen departure state, never
   publishes, and yields to retimes and previews.

Step 3 is the caller's own change to the earlier sketch: at plot, two each side;
on drag, three in the direction of travel.

## Traps

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

## Verification

```bash
tools/check.sh --fast
tools/check.sh --tests flight
```

Report the command and its result; do not write a test count anywhere.

## State

- [ ] Step 1 — measure
- [ ] Step 2 — warm start
- [ ] Step 3 — prefetch
