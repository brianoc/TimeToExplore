# Handoff

**Step 2b (station Go To, the station warp cap, the report) is done. Step 3 —
the concepts, roadmap, index and ADR — is next.** The plan is
[the station-flights and departure-profile plan](Docs/log.md); the reasoning for
the point target is in the 2a log entry and
[Navigation and arrival](Docs/simulation/navigation-and-arrival.md). History is
in [the update log](Docs/log.md).

## Done in 2b

* **A station is Go-To-able.** `SolarSystemBootstrap.BeginGoTo` and `PlotRoute`
  let `BodyData.IsStation` through the guard that still refuses a genuinely
  massless non-station; the HUD details screen does the same with
  `plottable = (body.Gm > 0.0 || body.Data.IsStation) && !rejected`. A station
  plots, draws every route arc and the arrival ghost, and begins a flight from
  the bootstrap and from the real Route/Begin buttons.
* **The session needed one boundary fix, and a terminal-match step policy.**
  `PlaceAt` already seeds from `ComputeState`; `Plot`/`Begin` already fly
  `PlanFastest`'s station floor; the three-arc Ares Junction → Gateway trip
  (54.16210 d) reaches `FlightStatus.Arrived` on the hold point through
  `Advance`. `FlightSession.ThrustForArc` used to drop the drive on the final
  RK4 stage that lands exactly on the arrival date, losing h/6 of the terminal
  match's command (the whole 0.29 m/s at the default hint, and 3.5 m/s when the
  hint was made finer); it now keeps that stage within `ArrivalToleranceDays`.
  The match also carries `TripPlanner.StationMatchStepSamples = 1,000` (a 4×
  finer hint, 14.4 s against 57.6 s) so its own truncation stays well under the
  contract. Tracked arrival: **0.0254 m / 7.7 × 10⁻⁵ m/s**, 9,198 substeps
  against 8,593 before the policy (+605, 7%); the gate fix alone was 0.13 m /
  3.8 × 10⁻⁴ m/s. There is no parking orbit, no approach, and
  `CaptureEntryRadiusM`/`CaptureInboundSeconds` are 0.
* **`WarpPolicy` station caps.** While a station plan is live and incomplete, a
  frame is capped by the quarters-of-clearance rule to the station's surface and
  by the new `StationHoldFraction` (0.05) of the time to the hold point. The
  caps are released when the plan completes, so the clock is not pinned after
  arrival. Measured with a real Ares Junction → Gateway plan, sampled one day
  before departure (event pacing not binding): 200 km astern closing at 100 m/s
  gives **99.8 s/frame** (field alone 3,709 s), the hold point gives the **1 s**
  floor, and 2 × 10⁹ m away the caps are non-binding (event cap 130,059 s wins).
* **`FlightReport`.** A station plan's terminal finite `Capture` arc reads
  **Match** in the DRIVE summary and `match` in the phase row's drive label, and
  the capture-chain row is suppressed by target kind. The planet report is
  unchanged.
* Planet anchor unchanged: **Gateway → Mars 90 d = 38.9678 km/s / 9 arcs**.

## Next: Step 3 (concepts, roadmap, index, ADR)

Do **not** re-open the planner or the wiring. Sweep the bundle for statements
that 2a and 2b made false, add the station-flights ADR, and record the new
behaviour:

1. **ADR** for the station point target, the hold point and the heliocentric
   start, the two station warp caps, and the terminal-match integration policy
   (the arrival step keeps the burn's last stage; the match runs at 1,000
   samples).
2. **Docs that are now stale** (at least):
   * [Time and scale](Docs/simulation/time-and-scale.md) and
     [The flight session](Docs/technical/flight-session.md) list three warp caps
     and no station cap; the hold-point cap and why clearance cannot see a
     station belong there.
   * [Architecture](Docs/technical/architecture.md) says the warp policy caps by
     clearance/local dynamics/plan event only.
   * [Navigation and arrival](Docs/simulation/navigation-and-arrival.md) and
     [Burn–coast–burn](Docs/simulation/burn-coast-burn.md) describe the arrival
     as a parking orbit and the terminal burn as a brake; a station is the hold
     point and a velocity **match** with no capture chain.
   * [The flight session](Docs/technical/flight-session.md) and
     [Testing and verification](Docs/technical/testing.md) list the report and
     session behaviour; add the station cases.
   * The station pages ([Lagrange stations](Docs/world/lagrange-stations.md),
     [Initial catalogue](Docs/world/initial-catalogue.md)) should say a station
     is a Go To target and that its `gm`/`radiusM` are not physics.
   * [Index](Docs/index.md) and [Roadmap](Docs/production/roadmap.md) need the
     station-flight step.
3. **Wording that is arguably still planet-only**: `FlightStatus.Arrived` is
   shown as "Arrived, in orbit" (`FlightReport.StatusLabel`, HUD
   `ShipStatusText`), the phase is still `Capture`, and `NextEvent` says
   "Capture" for a station. Step 3 owns whether a station should read
   "holding"/"match". The behaviour is correct; only the label is planet-flavoured.

## Verify

```bash
tools/check.sh --tests flight,presentation,smoke
tools/check.sh --tests playmode
tools/check.sh                 # caretaker's final gate after step 3
tools/check.sh --fast          # docs, catalogue, generators, tooling only
tools/validate_okf.py          # the design bundle
```

One-off EditMode probe (the warm `.cache/test-project` has `Assets` symlinked):

```bash
R=$PWD; UNITY=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity
.venv/bin/python tools/unity_guard.py --log $R/.cache/probe.log \
  --stall-seconds 1200 --timeout-seconds 1800 --shutdown-grace 60 -- \
  "$UNITY" -batchmode -nographics -projectPath $R/.cache/test-project/TimeToExplore \
  -runTests -testPlatform EditMode -testResults $R/.cache/probe.xml \
  -logFile $R/.cache/probe.log -testFilter ZzProbe
```

`TestContext.WriteLine` output is in the XML. A spurious `.cache is not a valid
directory name` line is harmless.

## Traps

* **The hold point is the arrival contract**, 500 m astern of
  `ComputeState(station, arrivalJd)` along the station's velocity
  (`TripPlanner.StationHoldDistanceM`). Never snap it to a parking orbit and
  never derive the station's velocity from the frame loop's finite difference.
* **A station has no Gm, radius or SOI to fake.** The planner skips
  `HandoverBand`, `ParkingOrbit` and `TransferConic`. The warp clearance cap does
  use `radiusM` as the surface distance; at the hold point that clearance is
  negative and clamps to `MinFrameSeconds`, which is why the hold cap exists.
* **The arrival step must keep the terminal burn's last stage.** The final RK4
  stage lands exactly on the arrival date, and `FlightSession.ThrustForArc` used
  to see `Plan.IsComplete` and drop its thrust. That cost h/6 of the match
  command: 0.29 m/s at the default hint and 3.5 m/s at a finer one. The gate now
  allows `ArrivalToleranceDays`, and the tracked arrival is **0.0254 m /
  7.7 × 10⁻⁵ m/s** — do not re-tighten the step policy or remove that tolerance
  without re-measuring both the station flight and the planet arrivals.
* **`StationMatchStepSamples` only overrides the station's last arc.** It is
  1,000 samples (a 4× finer hint) on the terminal match; every planet brake and
  every other arc keeps the default 250-step hint, so the planet flight cost and
  the fixed-duration anchors do not move.
* **A station plan from outside every sphere has no `Departure`/`Escape` arcs**:
  three arcs (cruise burn, coast, match) from Ares Junction to Gateway, and the
  report has no Departure row.
* **The station caps only apply while the plan is incomplete.** After arrival
  the ship is holding, matched, and the event pacing is already infinite; a cap
  that stayed on would pin the clock at 1 s/frame for ever.
* **Arc counts and indices shifted for station and heliocentric plans.** Keep
  the planet anchor (Gateway → Mars 90 d, 38.9678 km/s, 9 arcs) untouched.
* **A station's catalogue `radiusM` is a visual placeholder** (1,000–1,750 m);
  do not treat it as a physical body in the planner or the concepts.
