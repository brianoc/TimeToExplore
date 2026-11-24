# Handoff

**Step 2a (the station point target and the heliocentric departure) is done.
Step 2b — wiring the session, bootstrap, HUD and warp to it — is next.** The plan
is [the station-flights and departure-profile plan](Docs/log.md); the reasoning
for the departure is in
[Burn–coast–burn trips](Docs/simulation/burn-coast-burn.md#the-physical-departure)
and [Navigation and arrival](Docs/simulation/navigation-and-arrival.md#the-implemented-plan).
History is in [the update log](Docs/log.md).

## Done in 2a

* **A station is a plan target.** `RequireTarget` accepts `BodyData.IsStation`
  and still refuses a genuinely massless non-station. `Plan`, `PlanAtDuration`
  and `PlanFastest` dispatch to `BuildStationTrip` / `PlanStationFloor` before any
  sphere, `ParkingOrbit` or `HandoverBand` call. The plan has no capture chain,
  no parking orbit, no handover band, no SOI and no planetocentric approach: the
  terminal condition is the **hold state**, `ComputeState(station, arrivalJd)`
  offset **500 m astern along the station's velocity direction**
  (`TripPlanner.StationHoldDistanceM`), with the station's velocity, and the last
  arc is a finite velocity-match burn (`FlightPhase.Capture`, finite). A
  rendezvous may add or shed speed, so the planet arrival's retrograde-only
  rejection is not applied.
* **A heliocentric start is a plan.** When `FindDepartureBody` returns null, the
  coast path and the burn-only path build the departure burn from the ship's own
  state at t0 with no break and no escape arc; the chain is Cruise burn → Cruise
  conic → arrival leg (planet capture as before, or a station).
* **Measured at the epoch** (0.3 g, conic capture): Mars 300 km → Gateway 120 d
  is 25.648 km/s / 5 arcs (hold seam 0.0006 m / 7.6 × 10⁻⁸ m/s); Gateway → Ares
  Junction 120 d is 24.733 km/s / 5 arcs; Ares Junction → Gateway 160 d is
  12.239 km/s / 3 arcs; Ares Junction → Earth 230 d is 24.091 km/s / 7 arcs;
  Helios Watch → Mars 200 d is 10.747 km/s / 7 arcs. `Plan` Mars 300 km →
  Gateway is 59.57357 d / 76.494 km/s; `PlanFastest` Ares Junction → Gateway is
  54.16210 d / 69.541 km/s. The planet anchor did **not** move: Gateway → Mars
  90 d is still 38.9678 km/s / 9 arcs.
* **Tests** are in `Assets/Tests/EditMode/StationTripPlannerTests.cs`
  (`[Category("flight")]`); `TripPlannerTests.UnknownOrMasslessTargetsAreRejected`
  became `UnknownTargetsAreRejected` because a station is no longer refused.
* The full `Docs/log.md` entry has the per-arc tables and the traps.

## Next: Step 2b (session, bootstrap, HUD, warp)

Do **not** re-open the planner design; the entry points exist:

1. **`FlightSession`.** Preview and `Begin` already use `PlanFastest` +
   `PlanAtDuration`; make a station target survive the preview/Begin path and the
   arrival handover. `TrajectoryPlan.ArrivalState` for a station is the hold
   point, **not** a parking orbit: any code that assumes a parking orbit at the
   arrival (arrival ghost altitude, "parking" labels, the arrival orbit ring)
   needs a station branch. There is no `CaptureEntryRadiusM` /
   `CaptureInboundSeconds` (both zero), and the last arc is labelled
   `FlightPhase.Capture` but is the finite velocity match.
2. **`SolarSystemBootstrap`.** A station target must be selectable/Go-To-able
   through the real route (the `Tab` cycle and the details screen already list
   stations); its catalogue `gm` is 0 and its `radiusM` is a visual placeholder,
   so neither may be read as physics. A ship placed at a station (or at Ares
   Junction / Helios Watch / Nyx Anchor / Jovian Yard / Titan Reach) is now a
   valid departure state for a Go To.
3. **HUD.** The plan summary for a station should read the hold point and the
   velocity match rather than a capture/parking chain; the arc legend and the
   flight report must show a station arrival with no Approach/Parking rows.
   `FlightReport`'s capture-chain row (`chainStart`) needs a station branch: the
   only `Capture` arc is the finite velocity match, and `CaptureEntryRadiusM` /
   `CaptureInboundSeconds` are zero. The route colour and the drawing rules
   already key off `FlightPhase`.
4. **`WarpPolicy`.** A station plan may have no `Departure`, `Escape` or
   `Approach` arc; pace the countdown from `NextBoundaryJulianDate` and the
   phases that actually exist. A heliocentric start's first arc is a `Cruise`
   finite burn, not a `Departure` break.
5. Keep the step-1 anchors and the planet path untouched: re-run the flight
   suite and confirm Gateway → Mars 90 d is still 38.9678 km/s / 9 arcs.

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
  -logFile $R/.cache/probe.log -testFilter StationTripPlannerTests
```

Write the results to an **absolute path outside `.cache`** (`$R/probe.xml`); the
`$R/.cache/probe.xml` form can leave no file behind. `TestContext.WriteLine`
output is in the XML, not the log. A spurious `.cache is not a valid directory
name` line is harmless.

## Traps

* **The hold point is the arrival contract.** It is 500 m astern of the station
  along its velocity, derived from `SolarSystemModel.ComputeState(station, jd)` —
  never from the frame loop's finite-difference velocity. Do not "snap" it to a
  parking orbit or a fixed offset in the ecliptic.
* **A station has no Gm, radius or SOI to fake.** Do not route it through
  `HandoverBand`, `ParkingOrbit` or `TransferConic`; the station path deliberately
  skips all three (and the arrival body-clearance gate skips a null body).
* **The station floor search costs a handful of certified builds.** It brackets
  with fast probes and certifies the feasible end; `PlanFastest` returns that
  floor for a station because the burn-only branch needs a capture chain.
* **A long station trip can genuinely refuse** on the coast's Lambert geometry
  (Ares Junction → Gateway at 300 d, Ares Junction → Earth at 260 d): the reason
  names the transfer angle. Do not widen the gate to hide it; retime.
* **The escape conic's climb is still the planet profile's.** A station
  departure from Gateway / a Mars orbit keeps the step-1 break and escape; only a
  ship outside every sphere starts from its own state.
* **Arc counts and indices shifted for station and heliocentric plans.** Station
  from a planet: 5 arcs (Departure, Escape, Cruise burn, Cruise conic, Capture
  match). Heliocentric to a planet: 7 (no Departure/Escape). Station to station:
  3.
* **`Plan()` from a planet orbit is still the broken path** for planet targets;
  use `PlanAtDuration`. Station floors do build from a planet orbit through
  `PlanStationFloor`.
* **A station's velocity in the frame loop is a finite difference.** Use
  `SolarSystemModel.ComputeState` for planning; do not read the view's velocity.
