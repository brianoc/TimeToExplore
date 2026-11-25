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
3. **Warm-start the pin.** Seed each build from the nearest solved neighbour:
   brake duration and direction, departure-burn duration and direction, and the
   approach on pass 0. Two call sites matter, both measured in step 1: the retime
   path, where the pin is 71% of a 412 ms build, and the floor search's probes,
   where **162 of a plot's 174 pin passes** are. `SearchFloor` builds through one
   `Probe(seconds)` closure, so carry the converged state from one probe to the
   next; that needs `BuildResult` to return the pin state it settled on. Never
   seed the escape ray. Fall back to the cold seed when the seeded run does not
   converge, and do not seed the two lowest steps or across the burn-only/coast
   gap.
   Judge the step against `tools/bench.sh` and the committed baseline, not against
   hand figures: Mars plots at 4.1 s, Venus at 9.7 s, Uranus at 2.0 s, the
   retimes at 0.37–0.49 s, and Mars's Begin at 9.0 s. **Done:** see below.
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

Step 2's instrument is **`tools/bench.sh`**; its six-route table and the three
reverse refusals are in
[Planning performance](/technical/planning-performance.md#the-route-benchmark-2026-11-24).
The Earth routes at the epoch are: Earth→Mars **4.1 s plot, 0.46/0.37 s retimes,
9.0 s Begin**; Earth→Venus **9.6 s plot, 0.47/0.46 s, 4.3 s Begin**; Earth→Uranus
**2.0 s plot, 0.49/0.48 s, 1.7 s Begin**. Repeat runs move every measured phase
by **at most 2.5%**, most under 1%, so treat anything under a few percent as
noise.

**Step 3's result** is in
[Planning performance](/technical/planning-performance.md#step-3-warm-starting-the-pin-2026-11-24).
`PinSeed` carries the brake and departure-burn durations and directions and the
duration they converged at; never the escape ray. `TrajectoryPlan.PinSeed`
returns it, `FlightSession` stores it with each `CachedRetime` and with the
window's floor plan, and the retime build starts from the nearest entry's seed.
The retime **wins**: **12 → 6 pin passes** at Mars and Venus and **9 → 6** at
Uranus (33 → 18, 45.5% fewer) for the same plan to four decimals. **The plot does
not win**: below the floor every fast probe refuses, so nothing can seed until
the floor itself, and a seed that changes a near-floor probe's verdict moves the
floor. Seeding the fast probes walked Uranus's high from 92.13088 to 91.07860 d
and the search then found nothing, so **only the full-field continuation probes
are seeded**, a probe must settle its pin to be accepted, and a certified build
may accept a chain-closed plan whose pin plateaued. The floors are unchanged at
Mars **16.84485**, Venus **14.31827**, Uranus **92.13088 d** (measured on the
same tree before and after). `tools/bench.sh` shows no phase beyond noise.

## Traps

- Anchor values: the live Mars 0.3 g coast floor is **16.84485 d** and the Mars
  90 d anchor **38.9678 km/s** after ADR-019's departure break. The
  planning-performance doc's earlier 6.73042 d and 44.3543 km/s are the
  pre-departure values and must not be used as the comparison; `tools/check.sh`
  enforces the live ones.
- The three reverse routes **do not plot at the epoch**: the floor search's twelve
  20% growth steps stop before the profile's floor. Mars→Earth builds at a fixed
  90 d, so this is a search limit, not geometry. It is out of this plan; do not
  "fix" the growth cap inside the warm start.
- `Begin`'s cost does not track the plot. Earth→Mars plots in 4.1 s but Begin
  takes 9.0 s at +3 h, because the coast floor's certificate refuses once and the
  continuation is paid. Measure both.
- The preview starts at the window **minimum**, so a fresh plot has no lower
  side. Prefetch clamps at the window edges; expect the low side to be empty.
- The cache key is the **preview's frozen departure state**
  (`_previewDeparture.JulianDate`), not the live clock. A prefetch must capture
  the same values as the retime request or it fills slots nothing can read.
- Prefetch must not bump `_retimeGeneration` or `_previewGeneration`, must never
  publish, and must be dropped on discard, style change, burn-level change, and
  date change.
- Warm start must not move the anchors: the same plan to four decimals, floors
  unchanged, every seam inside 1 km / 1 m/s. `WarmStartTests` checks **six slider
  positions** at Mars, Venus and Uranus 0.3 g (the minimum, steps 1, 2, 7, 31 and
  the maximum) and the three floors; the flight suite's own anchors still hold
  Mercury and Jupiter.
- The near-floor basin is a needle and the runtime selects it by last-bit libm
  differences. A seeded **probe** that settles at a duration the cold search
  refuses moves the floor: measured at Uranus, the fast search walked its high
  from 92.13088 to 91.07860 d. The guard is the scope — fast probes unseeded, a
  probe accepted only when its pin settled — plus the cold certificate, not a
  tolerance change. See the step-4 traps.
- The player sees **~4 s per plot and another ~4 s at Begin** in Play Mode. The
  current editor record is the step-2 benchmark: Earth→Mars plots in **4.1 s**
  headless and Begin in **9.0 s**, so the plot numbers agree to one decimal but
  Begin does not. **No Play-Mode-versus-headless ratio was measured**; do not
  equate the two, and do not quote the pre-crossing 1.28–3.03 s table.
- **`tools/bench.sh` is the only way to run the benchmark.** It is an editor
  `-executeMethod` (`PlanningBenchmark.Run`), not a test; a default
  `tools/check.sh` writes no benchmark output and never fails on timing. Do not
  add it to `check.sh` or its category allow-list, and do not make the 25%
  threshold a gate.
- **The three reverse routes cannot plot at the epoch** (Mars/Venus/Uranus→Earth
  from the planet's parking orbit). Their retime and Begin phases are therefore
  not measured, and the suite records the refusal with its reason. Do not "fix"
  it by changing the route's level, date or orbit in the benchmark: Mars→Earth
  at **90 d fixed is feasible**, so the coast floor search's growth cap (**4.36 d
  seed, twelve 1.2× steps to 38.84 d**) is the limit there; Venus→Earth refuses
  at a 1.0° short arc and at 90 d too; Uranus→Earth needs more than 143.6 d for
  the break and escape climb. Fixing that is planning work, not this plan's.
- **Begin can be 2.2× the plot.** At Earth→Mars the date three hours later
  refuses one floor certificate and pays the full-field continuation, so a change
  that only speeds the plot may move Begin the other way. Measure Begin, not just
  the plot.
- A full `tools/bench.sh` run costs **about 2 min 40 s** wall clock on this
  machine (157 s measured, including Unity startup); the run prints medians of
  three, so use the deltas, not a single number.
- **The benchmark's retimes cannot see a warm start.** Its retime phases sit at
  the window's middle and maximum — 33.0 d and 49.5 d at Mars — which is outside
  `PinSeed.MaxRelativeGap` (20%), so `Covers` refuses the seed and those builds
  are cold by construction. Do not read their flat deltas as a regression. A
  prefetch's win must be measured on **neighbouring steps** (1.6% of the window)
  or by adding a phase to the instrument, not by changing the existing two.
- **The fast probe search must stay unseeded.** Seeding it let stalled probes
  close below the floor: Uranus's fast high walked 92.13088 → 91.07860 d, the
  certificate refused, and `Plan()` returned a refusal. `SearchFloor` now seeds
  **only** `probe: true` builds, and only while they settle their pin. A prefetch
  must not widen that.
- A seeded certified build accepts a chain-closed plan whose pin plateaued above
  its tolerance (a cold build is accepted on the same terms); a **probe** must
  settle. Keep that distinction: changing it moves floors.
- The two lowest slider steps are never seeded (`SeedExcludedLowFraction`, 2/63,
  pinned against `SolarSystemHud.TimeSliderSteps`), because the window's minimum
  is where the burn-only crossing and the short coast meet the coast profile.
  Prefetching step 1 or 2 must start cold.
- Warm start must not move the floors: Mars **16.84485 d**, Venus **14.31827 d**,
  Uranus **92.13088 d** at 0.3 g, measured on this tree both sides of step 3.
  `WarmStartTests` asserts the Mars and Uranus anchors; a prefetch that seeds
  across families would break them.

## Verification

```bash
tools/check.sh --fast
tools/check.sh --tests flight
tools/bench.sh
```

Report the command and its result; do not write a test count anywhere.

## State

- [x] Step 1 — measure
- [x] Step 2 — benchmark the routes
- [x] Step 3 — warm start
- [ ] Step 4 — prefetch
