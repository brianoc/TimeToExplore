# Handoff

**Step 0 (baseline and departure design) is done. Step 1, the departure fix, is
next.** The plan is a short escape burn out of the parking orbit, a ballistic
planetocentric conic to the top of the departure sphere, then the existing
departure burn, coast, approach, brake, and capture. History is in
[the update log](Docs/log.md); the concepts the steps must update are
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md),
[Navigation and arrival](Docs/simulation/navigation-and-arrival.md),
[Lagrange stations](Docs/world/lagrange-stations.md), and
[Testing and verification](Docs/technical/testing.md).

## Done

The flight baseline was re-run at HEAD `0657561` with a throwaway `ZzProbe`
EditMode test (deleted), full field, 0.3 g, conic capture. It matches the
caretaker's numbers exactly:

* Gateway→Mars 90 d: **44.354 km/s**, 8 arcs; break 4.8443 d / 8.845 km/s /
  peak 0.0327; departure burn 0.0528 d / 13.418; coast 84.7291 d; approach
  0.2513 d; brake 0.0729 d / 18.522; capture 4.114 + 0.170.
* Mars 300 km orbit→Earth 60/90/120 d: **337.057 / 369.168 / 387.008 km/s**;
  break 4.3153/5.2464/5.7538 d, 267.017/324.301/355.071 km/s; burn
  24.487/11.874/7.531; brake 41.253/28.681/20.074; capture 4.114 + 0.170.
* Mars 300 km orbit→Moon 60/90/120 d: **339.673 / 369.204 / 385.797 km/s**.

The break's delta-v grows with trip time because `BuildCoastSkeleton` gives it
the reference coast's departure *share* (`escapeSeconds = escapeBase * scale`,
`TripPlanner.cs:1650–1697`); the quintic then flies a longer out-and-back loop to
the pinned radial ceiling state. A Mars 300 km break at 4.3 d reproduces the
267 km/s and an overshoot to ~7.4 million km. Do not preserve it.

## Next

1. **Step 1 — the departure fix.** Replace the break in `BuildBurnCoastBurn`
   with a short prograde escape burn at the parking state, then a ballistic
   `ConicArc` about the departure body out to `1.02 × SOI`. The conic is
   parabolic when the trip leaves it 10.74 d from Gateway or 12.02 d from a Mars
   300 km orbit, and hyperbolic with a solved excess speed when the trip is
   shorter. The break's duration seeds at `Δv / FixedBurnAccelerationMps2` and is
   solved for the commanded peak, never for a trip-time share. The coast absorbs
   the residual; the total stays the input. The departure burn starts at the
   conic's ceiling *state*, not at a pinned radial ray; its integration and the
   coast/brake/approach/capture solves stay as they are. This should put
   Gateway→Mars 90 d near **36.9 km/s** (44.354 − 8.845 + 1.405), inside the
   deliberate 36–37 anchor.
2. **Step 2 — stations.** The other stations sit outside their planet's sphere
   (`FindDepartureBody` scans only `Sun.Children`), so the planner currently
   refuses them; Gateway is the only station inside its parent's sphere.
3. **Step 3 — concepts and an ADR.** Update the pinned-break section of
   [Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md#the-pinned-break),
   the implemented-plan section of
   [Navigation and arrival](Docs/simulation/navigation-and-arrival.md#the-implemented-plan),
   and [Testing and verification](Docs/technical/testing.md); record the choice
   in a new ADR and in [the update log](Docs/log.md).

## Verify

```bash
tools/check.sh --fast                 # docs, catalogue, generators, tooling
tools/check.sh --tests flight         # while iterating on the planner
tools/check.sh                        # EditMode + PlayMode before finishing
```

One-off EditMode probe (the warm `.cache/test-project` has `Assets` symlinked):

```bash
R=$PWD; UNITY=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity
.venv/bin/python tools/unity_guard.py --log $R/.cache/probe.log \
  --stall-seconds 1200 --timeout-seconds 1800 --shutdown-grace 20 -- \
  "$UNITY" -batchmode -nographics -projectPath $R/.cache/test-project/TimeToExplore \
  -runTests -testPlatform EditMode -testResults $R/.cache/probe.xml \
  -logFile $R/.cache/probe.log -testFilter ZzProbe
```

The probe file is deleted; recreate it with `[Category("flight")]`,
`CatalogueFixture.LoadModel()`, `FlightFixture.FullField`, and
`PlanAtDuration`. A spurious `.cache is not a valid directory name` line is
harmless. Read the `<output>` elements of `.cache/probe.xml`.

## Traps

* **Do not keep the pinned radial handover literally.** A break that relocates
  to the pin's ray at the parking radius is unphysical: at Mars 300 km the ray
  is 99–118° away, the straight chord passes 1,907–2,401 km from Mars's centre
  (below the 3,390 km surface), and a position-matching quintic to it dives to
  1,411 km and needs 29 m/s² even at 2,000 s. At Gateway the 166.6° relocation
  is 71,607 km laterally and costs *more* when shortened (49.6 km/s in 12 h
  against today's 8.845 in 4.84 d). The escape ray must be the ship's own
  prograde direction, and the burn starts at the conic's ceiling state.
* **`ConicElements`' parabolic `TimeSincePeriapsis` is √2 too long.** Measured
  17.002 d against 12.022 d by `KeplerPropagator` at Mars (15.190 against
  10.741 at Gateway; `ConicElements.cs:183–188`, the `2.0 *` in the Barker
  factor). The new escape conic is exactly parabolic, so fix the branch and add
  a Kepler comparison, or get the duration from a radius crossing.
* **The break must not be stretched to a trip-time share.** Its duration is
  physical (Δv/a), and the conic supplies the time; the coast takes the
  difference. A fast trip gets a hyperbolic conic, not a longer burn.
* **`Plan()` from a planet orbit returns an infeasible 0.00 d plan at
  148,655 m/s².** Use `PlanAtDuration` for orbit departures.
* **A station's velocity in the frame loop is a finite difference.** Use
  `SolarSystemModel.ComputeState` for planning; do not read the view's velocity.
* **The planner assumes a target with an SOI and a parking orbit.** Gateway is
  the only station inside its parent's sphere; the Sun–planet L1/L2/L4 stations
  are outside their planet's SOI.
* **The pinned departure solve is not literally untouched.** Its start state
  has to come from the escape conic instead of `(ray, clearanceRadius,
  escapeSpeed)`; the burn integration, the coast, the approach, the brake, and
  the capture stay. `BuildBurnOnly` / `PlanBurnOnly` and `BuildShortCoast` share
  the same break and pin, so they move in step 1 too.
* **Every arc index and count shifts** if the escape conic gets its own phase.
  `BurnCoastBurnTripPlannerTests`, `PlanPathTests`, `RouteColourTests`,
  `FlightReportTests`, `FlightSessionTests`, `SceneFlightTests`, and
  `BootstrapSmokeTests` pin 6/7/8/9-arc shapes and `Arcs[0..7]`.
* **The five fixed-duration anchors are all Gateway departures and all move.**
  Mercury 8 d 557.038, Uranus 400 d 182.312, Mars 20 d 270.850, Mars 10 d
  607.826, and Uranus 35 d 1984.448 must be re-baselined. The short ones
  (Mercury 8/6/4 d, Mars 10 d) flip to a hyperbolic conic or become
  infeasible; the long ones lose roughly the break's saving. The *arrival*
  values must not move: the capture chain's entry radius and fall, the
  4.114 + 0.170 km/s Mars capture, the 3,457 + 76 m/s Earth capture, and the
  parking orbit are independent of the departure.
