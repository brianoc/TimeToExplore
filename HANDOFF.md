# Handoff

**Step 1 (the departure fix) is done. Step 2, the stations, is next.** The plan is
[the station-flights and departure-profile plan](Docs/log.md); the reasoning for
the departure is in
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md#the-physical-departure)
and [Navigation and arrival](Docs/simulation/navigation-and-arrival.md#the-implemented-plan).
History is in [the update log](Docs/log.md).

## Done

* **The departure is physical.** The break is a short quintic to the parking
  radius on the ship's own prograde tangent at the planetocentric escape speed;
  a `ConicArc` about the departure body climbs from there to `1.02 × SOI`
  (parabolic when the trip allows it, hyperbolic when it does not); the
  departure burn starts at the conic's ceiling **state** and solves its thrust
  direction and duration against the Lambert coast. `FlightPhase.Escape` is the
  new phase. Break 928 s / 1.404 km/s at Gateway, 1,832 s / 2.854 km/s from a
  Mars 300 km orbit; the escape is 10.74066 d / 12.02249 d parabolic.
* **The old anchor is deliberately gone.** Gateway→Mars 90 d at 0.3 g is
  **38.9678 km/s**, not 44.354 and not the naive 36.9: the escape takes 5.9 days
  more than the old break, so the coast is shorter and the two fixed burns grow.
  The re-baselined anchors are Mars 90 d 38.9678, Uranus 400 d 183.9153, Mars
  20 d 495.9818, Mars 18 d 706.0906, Mercury 16 d 869.0568, Uranus 60 d
  1,329.6508. Mercury 8 d, Mars 10 d and Uranus 35 d are below the floor and are
  now pinned as refusals.
* **The floors/windows moved.** Mercury 1 g 14.06070 d (14.06–34.27), Mars 0.3 g
  16.84485 (16.84–50.53), Jupiter 0.3 g 23.73299 (23.73–71.20), Uranus 1 g
  57.02223 (57.02–171.07) and 0.3 g 92.13088 (92.13–276.39). The burn-only
  minima are 13.86287 / 16.48669 / 22.55555 d at Mercury 1 g, Mars 0.3 g and
  Jupiter 0.3 g. Mars 300 km → Earth/Moon at 60/90/120 d is 81.045/49.940/35.823
  and 83.166/49.644/34.520 km/s.
* **The Barker factor is fixed.** `ConicElements`' parabolic branch is
  `½ √(p³/μ)`, not `½ √(2p³/μ)`; the new escape exposed the √2 error.
  `KeplerPropagatorTests.ParabolicPropagationMatchesTheBarkerTime` guards it.
* The flight suite and the full `tools/check.sh` are green. The capture chain's
  arrival values did not move: Mars entry 6,032.7 km / 814.8 s fall /
  3.458 + 0.076 km/s, Earth 18,404 km / 2,048.7 s / 4.114 + 0.170.
* The suite covers the **Mars 300 km orbit departure to Earth and the Moon at
  60/90/120 d** (`AMarsOrbitDepartureClimbsBallisticallyToEarthAndTheMoon`), the
  **impulsive-escape sanity** (`TheBreakSpendsAnImpulsiveEscapeNotHundredsOfKilometresPerSecond`:
  2.02× the impulsive difference at Mars, 2.98× at Gateway), and the **0.3 g LEO
  refusal** (`AWeakDriveCannotUseTheShortBreakFromLowEarthOrbit`), with the same
  orbit at 1 g building.

## Next

1. **Step 2 — stations.** `TripPlanner.FindDepartureBody` scans only
   `Sun.Children`, so a station outside its parent planet's sphere (the Sun–planet
   L1/L2/L4 stations) is still refused. Extend the departure model so a station's
   own sphere/parking state is a valid departure, without reintroducing a radial
   pin or a time-share break. Gateway is still the only station inside its
   parent's sphere (the Moon's 66,200 km).
2. **Step 3 — concepts and an ADR.** The departure concepts and the re-baselined
   tables are already updated in step 1; step 3 owns the ADR and the remaining
   sweep.

## Verify

```bash
tools/check.sh                 # design bundle, data, tooling, EditMode + PlayMode
tools/check.sh --fast          # docs, catalogue, generators, tooling only
tools/check.sh --tests flight  # the planner suite while iterating
tools/validate_okf.py          # the design bundle
```

One-off EditMode probe (the warm `.cache/test-project` has `Assets` symlinked):

```bash
R=$PWD; UNITY=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity
.venv/bin/python tools/unity_guard.py --log $R/.cache/probe.log \
  --stall-seconds 1800 --timeout-seconds 2400 --shutdown-grace 120 -- \
  "$UNITY" -batchmode -nographics -projectPath $R/.cache/test-project/TimeToExplore \
  -runTests -testPlatform EditMode -testResults $R/probe.xml \
  -logFile $R/.cache/probe.log -testFilter ZzProbe
```

Write the results to an **absolute path outside `.cache`** (`$R/probe.xml`); the
`$R/.cache/probe.xml` form can leave no file behind. `Console.WriteLine` output is
in `probe.log`, not the XML. A spurious `.cache is not a valid directory name`
line is harmless. Delete the probe and its `.meta` before the commit.

## Traps

* **Do not stretch the break to a trip-time share.** Its duration is
  `|Δv|/a` seeded and bisected to the commanded peak. The coast takes the
  residual; a faster trip hyperbolises the escape (larger `v∞`, larger periapsis
  speed, longer break) and costs more.
* **The escape conic's available time** is the coast's end minus the departure
  burn, the reserved brake when there is no approach, and the minimum coast. A
  trip that cannot fit the climb is refused; the old fast anchors are below the
  floor by design. Do not loosen the floor gates to bring them back.
* **The departure burn's start is a state, not a ray.** The conic fixes the
  position and velocity; only the thrust direction and duration are free. Do not
  reintroduce `escapeDirection`, `clearanceRadius`/`escapeSpeed` as free pin
  variables.
* **The break's endpoint rotates with the phase it sweeps.** At Mars 300 km the
  1,832 s break sweeps 1.69 rad; the escape conic is the same conic rotated by
  that phase. Do not compare the climb against an unrotated state.
* **`Plan()` from a planet orbit is still the broken path.** Use
  `PlanAtDuration` for orbit departures. A 0.3 g departure from a 400 km Earth
  orbit is refused (the short break cannot lift it); 1 g builds.
* **`PinBreakToCommandAcceleration` and `MeasurePinnedBreakDurationSeconds` are
  gone.** The break always commands the level. Any test or doc that mentions the
  opt-in pin is stale.
* **Arc counts and indices shifted.** The standard plan is nine arcs
  (Departure, Escape, Cruise burn, Cruise conic, Approach, Capture brake, Coast,
  Capture, Parking), eight without an approach; burn-only is seven; the short
  coast is eight. Route legends are ordered by first appearance: Quintic, Conic,
  Fixed burn.
* **A station's velocity in the frame loop is a finite difference.** Use
  `SolarSystemModel.ComputeState` for planning; do not read the view's velocity.
