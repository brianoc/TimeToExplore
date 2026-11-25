using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using TimeToExplore.UI;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The scene's flight loop, verified without a scene: place the ship, plan,
    /// advance by dates, arrive, and hold the orbit. These are the milestone-2
    /// acceptance tests behind the Go To button.
    /// </summary>
    [Category("flight")]
    public class FlightSessionTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TripPlanner _planner;
        private FlightSession _session;
        private double _epoch;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _planner = new TripPlanner(_model, _gravity);
            _session = new FlightSession(_model, _gravity, _planner);
            _epoch = _model.CurrentJulianDate;
            _session.PlaceAt("gateway", _epoch);
        }

        [TearDown]
        public void TearDown()
        {
            // A retime test starts the worker; every test ends without a thread.
            _session?.Dispose();
        }

        [Test]
        public void ShipStartsAtGatewayInTheGatewayState()
        {
            _model.ComputeState(_model.Get("gateway"), _epoch,
                out Vector3d position, out Vector3d velocity);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.State.JulianDate, Is.EqualTo(_epoch));
            Assert.That(Vector3d.Distance(_session.State.PositionM, position), Is.LessThan(1e-6));
            Assert.That((_session.State.VelocityMps - velocity).Magnitude, Is.LessThan(1e-9));
        }

        [Test]
        public void BeginFliesTheMarsBurnOnlyMinimumAndStartsFlying()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);

            TestContext.WriteLine($"mars begin: {plan.FlightTimeDays:F5} d, " +
                                  $"{plan.DeltaVMps / 1000.0:F0} km/s, " +
                                  $"{plan.PropellantKg / 1000.0:F1} t, " +
                                  $"peak {plan.PeakThrustMps2:F2} m/s²");

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(plan.IsFeasible, Is.True);
            Assert.That(plan.TargetId, Is.EqualTo("mars"));
            Assert.That(plan.Arcs.Count, Is.EqualTo(7),
                "the burn-only crossing is break, escape, departure burn, brake, and the capture chain");
            Assert.That(plan.FlightTimeDays, Is.EqualTo(16.48669).Within(0.001),
                "Begin flies the preview's burn-only minimum, not the 16.84485 d coast floor");
            Assert.That(plan.DeltaVMps / 1000.0, Is.EqualTo(1450.4).Within(2.0));
            Assert.That(plan.PropellantKg / 1000.0, Is.EqualTo(156.1).Within(0.5),
                "the burn-only minimum's delta-v implies 156.1 t of propellant");
            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "the burn-only crossing has no cruise coast");
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[3].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].Phase,
                Is.EqualTo(FlightPhase.Parking));
            Assert.That(_session.Phase, Is.EqualTo(FlightPhase.Departure));

            // Thrust is zero at the seam by construction, so measure it once the
            // ship is a little way into the departure arc.
            _session.Advance(plan.DepartureJulianDate + 0.002, 50_000);
            Assert.That(_session.CurrentThrust.MagnitudeMps2, Is.GreaterThan(0.0));
        }

        /// <summary>
        /// The acceptance that ties Begin to the preview: at Mercury 1 g the
        /// trip-time window's minimum is the 13.86287-day burn-only crossing, so
        /// pressing Begin must fly that plan — no cruise coast, break → escape →
        /// departure burn → brake → capture — and the autopilot must reach the
        /// arrival state, not merely build the arcs.
        /// </summary>
        [Test]
        public void BeginningAtTheBurnOnlyMinimumFliesThePreviewedPlan()
        {
            _session.BurnLevelG = 1.0;
            TrajectoryPlan preview = _session.Plot("mercury", _epoch);
            double minimum = _session.PreviewMinimumSeconds;

            TestContext.WriteLine($"preview minimum {minimum / JulianDate.SecondsPerDay:F6} d");

            Assert.That(minimum / JulianDate.SecondsPerDay,
                Is.EqualTo(13.86287).Within(0.001),
                "the window's minimum is the burn-only crossing");
            Assert.That(preview.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False);

            TrajectoryPlan plan = _session.Begin("mercury", _epoch);

            Assert.That(_session.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(plan.FlightTimeSeconds, Is.EqualTo(minimum).Within(1e-6),
                "the flight is the plan the preview drew, not the coast floor");
            Assert.That(plan.FlightTimeDays, Is.EqualTo(13.86287).Within(0.001));
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(plan.Arcs[2].IsFiniteBurn, Is.True, "the departure burn");
            Assert.That(plan.Arcs[3].IsFiniteBurn, Is.True, "the brake");
            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "break, burn, brake, capture — no cruise coast");
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].Phase,
                Is.EqualTo(FlightPhase.Parking));

            FlightStep step = _session.Advance(plan.ArrivalJulianDate, 200_000);
            double positionError = Vector3d.Distance(_session.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (_session.State.VelocityMps - plan.ArrivalState.VelocityMps)
                .Magnitude;
            TestContext.WriteLine($"burn-only flight arrived {JulianDate.Format(step.ReachedJulianDate)}, " +
                                  $"{positionError:F1} m, {velocityError:F4} m/s, " +
                                  $"{step.Substeps} substeps");

            Assert.That(step.CollisionDetected, Is.False);
            Assert.That(step.BudgetExhausted, Is.False);
            Assert.That(step.Arrived, Is.True);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(positionError, Is.LessThan(10_000.0), "within 10 km of the arrival state");
            Assert.That(velocityError, Is.LessThan(10.0), "within 10 m/s of the arrival state");
        }

        [Test]
        public void AdvancingToTheArrivalDateArrivesMatchedOnTheParkingOrbit()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);

            FlightStep step = _session.Advance(plan.ArrivalJulianDate, 50_000);

            TestContext.WriteLine($"arrived at {JulianDate.Format(step.ReachedJulianDate)}, " +
                                  $"{step.Substeps} substeps");

            Assert.That(step.CollisionDetected, Is.False);
            Assert.That(step.BudgetExhausted, Is.False);
            Assert.That(step.Arrived, Is.True);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(_session.ArrivedJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));

            double positionError = Vector3d.Distance(_session.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (_session.State.VelocityMps - plan.ArrivalState.VelocityMps)
                .Magnitude;
            TestContext.WriteLine($"arrival error {positionError:F1} m, {velocityError:F4} m/s");

            Assert.That(positionError, Is.LessThan(10_000.0), "within 10 km of the orbit");
            Assert.That(velocityError, Is.LessThan(10.0), "within 10 m/s of the orbit");
        }

        /// <summary>
        /// A station is a valid <c>PlaceAt</c> target: the ship starts in the
        /// station's own <see cref="SolarSystemModel.ComputeState"/> state, so a
        /// ship parked at Ares Junction is a legitimate departure for a Go To.
        /// </summary>
        [Test]
        public void PlaceAtAStationSeedsTheShipInTheStationsOwnState()
        {
            foreach (string id in new[]
                     {
                         "gateway", "ares_junction", "helios_watch", "nyx_anchor",
                         "jovian_yard", "titan_reach"
                     })
            {
                _session.PlaceAt(id, _epoch);
                _model.ComputeState(_model.Get(id), _epoch,
                    out Vector3d position, out Vector3d velocity);

                Assert.That(Vector3d.Distance(_session.State.PositionM, position),
                    Is.LessThan(1e-6), id);
                Assert.That((_session.State.VelocityMps - velocity).Magnitude,
                    Is.LessThan(1e-9), id);
                Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle), id);
            }
        }

        /// <summary>
        /// A plot to a station produces the hold-point rendezvous: a feasible
        /// plan with a trip-time window, no capture entry radius and no inbound
        /// fall, and an arrival state on the station's 500 m hold point with the
        /// station's velocity. The preview never moves the ship.
        /// </summary>
        [Test]
        public void PlottingAStationReturnsAFeasibleHoldPreview()
        {
            _session.PlaceAt("ares_junction", _epoch);
            ShipState before = _session.State;

            TrajectoryPlan preview = _session.Plot("gateway", _epoch);

            Assert.That(_session.HasPreview, Is.True);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(preview.IsFeasible, Is.True, preview.InfeasibleReason);
            Assert.That(preview.TargetId, Is.EqualTo("gateway"));
            Assert.That(preview.Arcs.Count, Is.EqualTo(3),
                "no climb: cruise burn, coast, velocity match");
            Assert.That(preview.Arcs[preview.Arcs.Count - 1].Phase,
                Is.EqualTo(FlightPhase.Capture));
            Assert.That(preview.Arcs[preview.Arcs.Count - 1].IsFiniteBurn, Is.True);
            Assert.That(preview.Arcs.Any(arc =>
                    arc.Phase == FlightPhase.Approach || arc.Phase == FlightPhase.Parking),
                Is.False, "a station has no approach or parking orbit");
            Assert.That(preview.CaptureEntryRadiusM, Is.EqualTo(0.0));
            Assert.That(preview.CaptureInboundSeconds, Is.EqualTo(0.0));

            _model.ComputeState(_model.Get("gateway"), preview.ArrivalJulianDate,
                out Vector3d stationPosition, out Vector3d stationVelocity);
            Vector3d hold = stationPosition
                            - stationVelocity.Normalized * TripPlanner.StationHoldDistanceM;
            Assert.That(Vector3d.Distance(preview.ArrivalState.PositionM, hold),
                Is.LessThan(1.0), "the arrival state is the hold point");
            Assert.That((preview.ArrivalState.VelocityMps - stationVelocity).Magnitude,
                Is.LessThan(1e-3), "the arrival velocity matches the station's");

            Assert.That(_session.PreviewMinimumSeconds, Is.GreaterThan(0.0));
            Assert.That(_session.PreviewMaximumSeconds,
                Is.GreaterThanOrEqualTo(_session.PreviewMinimumSeconds));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM),
                Is.Zero, "a preview never moves the ship");
        }

        /// <summary>
        /// Begin a station trip and fly it through <see cref="FlightSession.Advance"/>.
        /// Ares Junction to Gateway is the measured 54.16210-day, three-arc
        /// rendezvous, and the session reaches the hold state with no parking
        /// capture: the terminal arc is the finite velocity match and the status
        /// is <see cref="FlightStatus.Arrived"/>.
        ///
        /// The arrival is the plan's hold contract, not a looser tracked one:
        /// the match arc carries a finer integrator hint
        /// (<see cref="TripPlanner.StationMatchStepSamples"/>) so its own
        /// truncation stays well inside the contract, and the arrival step keeps
        /// the terminal burn's final RK4 stage (<see cref="FlightSession"/>'s
        /// thrust gate) — without that stage the tracked match loses h/6 of the
        /// command and misses by tenths of a metre per second. Every other arc
        /// keeps the default cost.
        /// </summary>
        [Test]
        public void BeginningAStationTripArrivesOnTheHoldState()
        {
            _session.PlaceAt("ares_junction", _epoch);
            TrajectoryPlan plan = _session.Begin("gateway", _epoch);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_session.HasPreview, Is.False);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.TargetId, Is.EqualTo("gateway"));
            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
            Assert.That(plan.Arcs.Any(arc =>
                    arc.Phase == FlightPhase.Approach || arc.Phase == FlightPhase.Parking),
                Is.False, "a station has no parking capture to fall into");

            PlanArc match = plan.Arcs[plan.Arcs.Count - 1];
            Assert.That(match.Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(match.IsFiniteBurn, Is.True);
            Assert.That(match.SuggestedMaxStepSeconds, Is.LessThan(
                    match.DurationSeconds / 250.0),
                "the terminal match carries the finer station step policy");
            Assert.That(match.SuggestedMaxStepSeconds,
                Is.EqualTo(Math.Clamp(match.DurationSeconds / TripPlanner.StationMatchStepSamples,
                    0.5, 600.0)).Within(1e-9));

            FlightStep step = _session.Advance(plan.ArrivalJulianDate, 50_000);

            Assert.That(step.Arrived, Is.True);
            Assert.That(step.BudgetExhausted, Is.False);
            Assert.That(step.CollisionDetected, Is.False);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(_session.ArrivedJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));

            double positionError = Vector3d.Distance(_session.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (_session.State.VelocityMps - plan.ArrivalState.VelocityMps)
                .Magnitude;
            TestContext.WriteLine($"station arrival error {positionError:F4} m, " +
                                  $"{velocityError:E4} m/s, {step.Substeps} substeps, " +
                                  $"match hint {match.SuggestedMaxStepSeconds:F2} s");

            _model.ComputeState(_model.Get("gateway"), plan.ArrivalJulianDate,
                out Vector3d stationPosition, out Vector3d stationVelocity);
            Vector3d hold = stationPosition
                            - stationVelocity.Normalized * TripPlanner.StationHoldDistanceM;
            TestContext.WriteLine($"tracked hold error " +
                $"{Vector3d.Distance(_session.State.PositionM, hold):F4} m, " +
                $"{(_session.State.VelocityMps - stationVelocity).Magnitude:E4} m/s");

            Assert.That(positionError, Is.LessThan(1.0), "the tracked hold point, in metres");
            Assert.That(velocityError, Is.LessThan(1e-3),
                "the tracked match, within a millimetre per second");
            Assert.That(plan.ArrivalState.VelocityMps, Is.EqualTo(stationVelocity)
                .Within(1e-3), "the plan's own hold state matches the station");
            Assert.That(_session.CurrentThrust.IsActive, Is.False, "the drive is off");
        }

        /// <summary>
        /// At a giant the zero-coast crossing does not win, so PlanFastest
        /// returns the coast floor and Begin must fly that: the same rule, the
        /// other branch.
        /// </summary>
        [Test]
        public void BeginningAtACoastFloorTargetFliesTheCoastFloor()
        {
            ShipState departure = _session.State.WithJulianDate(_epoch);
            TrajectoryPlan coastFloor = _planner.Plan(departure, "uranus");

            TrajectoryPlan preview = _session.Plot("uranus", _epoch);
            double minimum = _session.PreviewMinimumSeconds;

            TestContext.WriteLine($"uranus coast floor {coastFloor.FlightTimeDays:F5} d, " +
                                  $"preview minimum {minimum / JulianDate.SecondsPerDay:F5} d");

            Assert.That(preview, Is.SameAs(_session.Preview));
            Assert.That(minimum, Is.EqualTo(coastFloor.FlightTimeSeconds).Within(1e-6),
                "the giant's minimum is the coast floor, not a burn-only crossing");

            TrajectoryPlan plan = _session.Begin("uranus", _epoch);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(plan.FlightTimeSeconds, Is.EqualTo(minimum).Within(1e-6),
                "Begin flies the coast floor the preview showed");
            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.True, "the coast floor has a cruise coast");
        }

        [Test]
        public void TheSessionArrivalStyleReachesThePlanner()
        {
            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.Conic));

            TrajectoryPlan plan = _session.Begin("mars", _epoch);

            Assert.That(plan.Arcs.Count, Is.EqualTo(7));
            Assert.That(plan.Arcs[2].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].Phase,
                Is.EqualTo(FlightPhase.Parking));
        }

        [Test]
        public void ChangingTheArrivalStyleRebuildsThePreviewAndLeavesTheShipStill()
        {
            TrajectoryPlan conic = _session.Plot("mars", _epoch);
            ShipState before = _session.State;

            _session.SetArrivalStyle(CaptureStyle.FiniteBurn);
            TrajectoryPlan hybrid = WaitForPublishedPreview();

            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.FiniteBurn));
            Assert.That(_session.Preview, Is.SameAs(hybrid), "the rebuilt plan is published");
            Assert.That(hybrid, Is.Not.SameAs(conic), "the preview is rebuilt, not relabelled");
            Assert.That(hybrid.TargetId, Is.EqualTo("mars"));
            Assert.That(hybrid.Arcs.Any(arc => arc.Phase == FlightPhase.Coast), Is.True);
            Assert.That(hybrid.Arcs.Any(arc => arc.Phase == FlightPhase.Parking), Is.True);
            Assert.That(hybrid.Arcs.Any(arc => arc.IsFiniteBurn), Is.True);
            Assert.That(_session.PreviewMinimumSeconds, Is.GreaterThan(0.0));
            Assert.That(_session.PreviewMinimumSeconds,
                Is.LessThanOrEqualTo(_session.PreviewDurationSeconds + 1e-3),
                "the trip-time window is the new style's fastest plan");
            Assert.That(_session.PreviewDurationSeconds,
                Is.EqualTo(_session.PreviewMinimumSeconds).Within(1.0),
                "the trip-time window resets to the new style's fastest plan");
            Assert.That(_session.PreviewJulianDate, Is.EqualTo(_epoch));
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.State.JulianDate, Is.EqualTo(before.JulianDate));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM), Is.Zero,
                "changing the arrival style never moves the ship");

            _session.SetArrivalStyle(CaptureStyle.Conic);
            TrajectoryPlan back = WaitForPublishedPreview();

            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.Conic));
            Assert.That(back.Arcs.Count, Is.EqualTo(7),
                "the conic capture is back, on the zero-coast minimum");
            Assert.That(back.Arcs[back.Arcs.Count - 1].Phase,
                Is.EqualTo(FlightPhase.Parking));
        }

        // --- The background preview planner ------------------------------------

        /// <summary>The test-side equivalent of the HUD's per-frame pump for the
        /// plot pipeline: wait for the worker's plan and publish it on this
        /// thread.</summary>
        private TrajectoryPlan WaitForPublishedPreview()
        {
            TrajectoryPlan published = null;
            WaitUntil(() => _session.TryApplyCompletedPreview(out published),
                "no preview was published");
            return published;
        }

        [Test]
        public void ThePreviewMinimumIsTheBurnOnlyPlan()
        {
            _session.BurnLevelG = 1.0;
            var watch = Stopwatch.StartNew();
            TrajectoryPlan preview = _session.Plot("mercury", _epoch);
            watch.Stop();

            ShipState departure = _session.State.WithJulianDate(_epoch);
            TrajectoryPlan fastest = _planner.PlanFastest(departure, "mercury");
            TrajectoryPlan coastFloor = _planner.Plan(departure, "mercury");

            TestContext.WriteLine(
                $"preview minimum {_session.PreviewMinimumSeconds / JulianDate.SecondsPerDay:F5} d, " +
                $"burn-only {fastest.FlightTimeDays:F5} d, " +
                $"coast floor {coastFloor.FlightTimeDays:F5} d, " +
                $"plot {watch.Elapsed.TotalSeconds:F2} s");

            Assert.That(preview, Is.SameAs(_session.Preview));
            Assert.That(_session.PreviewMinimumSeconds,
                Is.EqualTo(fastest.FlightTimeSeconds).Within(1e-6),
                "the minimum is the planner's fastest plan (the burn-only crossing)");
            Assert.That(_session.PreviewMinimumSeconds,
                Is.LessThan(coastFloor.FlightTimeSeconds),
                "and it is below the coast floor: the gap is real");
            Assert.That(preview.FlightTimeSeconds,
                Is.EqualTo(_session.PreviewMinimumSeconds).Within(1e-6));

            Assert.That(preview.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "the preview minimum is the zero-coast crossing");
            Assert.That(preview.Arcs[2].IsFiniteBurn, Is.True);
            Assert.That(preview.Arcs[3].IsFiniteBurn, Is.True);

            // A duration above the coast floor still builds the coast path. The
            // range between the burn-only minimum and the floor is the short-coast
            // regime, which the planner now builds; a retime anywhere in the
            // window is feasible.
            double above = coastFloor.FlightTimeSeconds
                           + 0.5 * JulianDate.SecondsPerDay;
            TrajectoryPlan slower = _session.SetPreviewDuration(above);

            Assert.That(slower.IsFeasible, Is.True, slower.InfeasibleReason);
            Assert.That(slower.FlightTimeSeconds, Is.EqualTo(above).Within(1.0));
            Assert.That(slower.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.True, "a retime above the floor is the coasting profile");
        }

        [Test]
        public void ARetimeInsideTheGapBuildsTheShortCoast()
        {
            _session.BurnLevelG = 1.0;
            _session.Plot("mercury", _epoch);

            double minimum = _session.PreviewMinimumSeconds;
            TrajectoryPlan coastFloor = _planner.Plan(
                _session.State.WithJulianDate(_epoch), "mercury");
            double requested = 0.5 * (minimum + coastFloor.FlightTimeSeconds);
            TrajectoryPlan retimed = _session.SetPreviewDuration(requested);

            TestContext.WriteLine(
                $"gap retime {requested / JulianDate.SecondsPerDay:F5} d against the " +
                $"burn-only minimum {minimum / JulianDate.SecondsPerDay:F5} d and the " +
                $"coast floor {coastFloor.FlightTimeDays:F5} d: " +
                $"feasible {retimed.IsFeasible}, {retimed.FlightTimeDays:F5} d, " +
                $"{retimed.DeltaVMps / 1000.0:F0} km/s, {retimed.Arcs.Count} arcs");

            Assert.That(retimed.IsFeasible, Is.True, retimed.InfeasibleReason);
            Assert.That(retimed.FlightTimeSeconds, Is.EqualTo(requested).Within(1.0),
                "the slider's requested total is the plan's total");
            Assert.That(retimed.FlightTimeSeconds,
                Is.GreaterThan(_session.PreviewMinimumSeconds),
                "the retime is above the burn-only minimum");
            Assert.That(retimed.FlightTimeSeconds,
                Is.LessThan(coastFloor.FlightTimeSeconds),
                "and below the coast floor: it is the gap");
            Assert.That(retimed.Arcs.Count, Is.EqualTo(8),
                "break, escape, departure burn, conic coast, brake, and the capture chain");

            for (int i = 0; i < retimed.Arcs.Count - 1; i++)
            {
                PlanArc left = retimed.Arcs[i];
                PlanArc right = retimed.Arcs[i + 1];
                Assert.That(Vector3d.Distance(left.Path.PositionAt(1.0),
                    right.Path.PositionAt(0.0)), Is.LessThan(1e3), $"seam {i} position");
                Assert.That((left.Path.VelocityAt(1.0) + right.EntryImpulseVectorMps
                    - right.Path.VelocityAt(0.0)).Magnitude, Is.LessThan(1.0),
                    $"seam {i} velocity");
            }
        }

        [Test]
        public void APlotAsyncPublishesTheFastestFeasiblePlan()
        {
            Assert.That(_session.PlotAsync("mars", _epoch), Is.True,
                "a plot is queued, not built");

            TrajectoryPlan published = WaitForPublishedPreview();

            Assert.That(published.TargetId, Is.EqualTo("mars"));
            Assert.That(published.IsFeasible, Is.True);
            Assert.That(_session.Preview, Is.SameAs(published));
            Assert.That(_session.PreviewJulianDate, Is.EqualTo(_epoch));
            Assert.That(_session.PreviewMinimumSeconds, Is.EqualTo(published.FlightTimeSeconds));
            Assert.That(_session.PreviewDurationSeconds, Is.EqualTo(published.FlightTimeSeconds));
            Assert.That(_session.PreviewMaximumSeconds,
                Is.GreaterThan(_session.PreviewMinimumSeconds),
                "the window offers the profile's accepted upper end");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.Plan, Is.Null, "plotting never starts a flight");
        }

        [Test]
        public void APlotAsyncMatchesTheSynchronousPlot()
        {
            _session.PlotAsync("mars", _epoch);
            TrajectoryPlan published = WaitForPublishedPreview();

            TrajectoryPlan sync = _session.Plot("mars", _epoch);

            // The async worker solved the window the synchronous Plot reads
            // back from the shared cache, so the plans are the same instance.
            Assert.That(sync, Is.SameAs(published));
            Assert.That(_session.Preview, Is.SameAs(sync));
        }

        [Test]
        public void TheNewestPlotAsyncWins()
        {
            _session.PlotAsync("mars", _epoch);
            _session.PlotAsync("venus", _epoch);

            TrajectoryPlan published = WaitForPublishedPreview();
            Assert.That(published.TargetId, Is.EqualTo("venus"),
                "the second plot supersedes the first, newest-first");
        }

        /// <summary>
        /// A second plot to the same target continues the build that is already
        /// queued or running. The published departure is the first ask's, so a
        /// selection priming the route and a Plot click behind it pay for one
        /// solve rather than restarting the several-second one.
        /// </summary>
        [Test]
        public void ARepeatPlotToTheSameTargetContinuesTheBuild()
        {
            double later = _epoch + 1.0;

            Assert.That(_session.PlotAsync("mars", _epoch), Is.True, "the plot is queued");
            Assert.That(_session.IsPlotPending("mars"), Is.True);
            Assert.That(_session.IsPlotPending("venus"), Is.False,
                "another target is not queued");

            Assert.That(_session.PlotAsync("mars", later), Is.True,
                "a repeat ask is accepted, not refused");

            TrajectoryPlan published = WaitForPublishedPreview();
            Assert.That(published.TargetId, Is.EqualTo("mars"));
            Assert.That(_session.PreviewJulianDate, Is.EqualTo(_epoch),
                "the first ask's departure is kept, so the solve is not restarted");
            Assert.That(_session.IsPlotPending("mars"), Is.False,
                "nothing is left queued once the plan publishes");
        }

        [Test]
        public void APlotAsyncToAnUnknownTargetReportsTheFailure()
        {
            _session.PlotAsync("planet-x", _epoch);

            WaitUntil(() => !string.IsNullOrEmpty(_session.TakePreviewFailure()),
                "the background plot failure never arrived");
            Assert.That(_session.HasPreview, Is.False,
                "a failed build publishes nothing");
        }

        [Test]
        public void APlotAsyncToADisposedSessionIsRefused()
        {
            _session.Dispose();
            Assert.That(_session.PlotAsync("mars", _epoch), Is.False,
                "a disposed session refuses new work instead of starting a thread");
        }

        [Test]
        public void ArrivalStyleWithoutAPreviewOnlySetsThePlanner()
        {
            Assert.That(_session.SetArrivalStyle(CaptureStyle.FiniteBurn), Is.Null);
            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.FiniteBurn));

            // The burn-only minimum always ends on the conic capture chain, so
            // retime above the floor where the chosen style is visible in the
            // plan Begin flies.
            _session.Plot("mars", _epoch);
            _session.SetPreviewDuration(2.0 * _session.PreviewMinimumSeconds);
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            Assert.That(plan.Arcs.Count, Is.EqualTo(9));

            // A live flight keeps the plan it was begun with.
            TrajectoryPlan live = _session.Plan;
            _session.SetArrivalStyle(CaptureStyle.Conic);
            Assert.That(_session.Plan, Is.SameAs(live));
            Assert.That(_session.Preview, Is.Null);
        }

        [Test]
        public void AdvancingAConicArrivalAppliesTheBurnsAndArrivesMatched()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            var session = new FlightSession(_model, _gravity, planner);
            session.PlaceAt("gateway", _epoch);

            TrajectoryPlan plan = session.Begin("mars", _epoch);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));
            Assert.That(plan.ImpulseDeltaVMps, Is.GreaterThan(0.0));

            FlightStep step = session.Advance(plan.ArrivalJulianDate, 200_000);
            double positionError = Vector3d.Distance(session.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (session.State.VelocityMps - plan.ArrivalState.VelocityMps)
                .Magnitude;
            TestContext.WriteLine($"conic arrival error {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s, {step.Substeps} substeps");

            Assert.That(step.CollisionDetected, Is.False);
            Assert.That(step.BudgetExhausted, Is.False);
            Assert.That(step.Arrived, Is.True);
            Assert.That(session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(positionError, Is.LessThan(10_000.0));
            Assert.That(velocityError, Is.LessThan(10.0));

            BodyRuntime mars = _model.Get("mars");
            double period = ParkingOrbit.PeriodSeconds(mars);
            double requested = session.State.JulianDate + period / JulianDate.SecondsPerDay;
            FlightStep coast = session.Advance(requested, 200_000);

            Assert.That(coast.CollisionDetected, Is.False);
            Assert.That(session.State.JulianDate, Is.EqualTo(requested).Within(1e-6));
            Assert.That(session.Status, Is.EqualTo(FlightStatus.Arrived));
        }

        [Test]
        public void AdvancingAFixedBurnArrivalStepsThroughTheBurnAndArrivesMatched()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.FiniteBurn
            };
            var session = new FlightSession(_model, _gravity, planner);
            session.PlaceAt("gateway", _epoch);

            // The burn-only minimum always keeps the conic capture, so retime
            // above the coast floor to fly the hybrid capture the style asks for.
            session.Plot("mars", _epoch);
            session.SetPreviewDuration(2.0 * session.PreviewMinimumSeconds);
            TrajectoryPlan plan = session.Begin("mars", _epoch);
            Assert.That(plan.Arcs.Count, Is.EqualTo(9));
            Assert.That(plan.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(plan.ImpulseDeltaVMps, Is.GreaterThan(0.0));

            FlightStep step = session.Advance(plan.ArrivalJulianDate, 200_000);
            double positionError = Vector3d.Distance(session.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (session.State.VelocityMps - plan.ArrivalState.VelocityMps)
                .Magnitude;
            TestContext.WriteLine($"fixed burn arrival error {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s, {step.Substeps} substeps");

            Assert.That(step.CollisionDetected, Is.False);
            Assert.That(step.BudgetExhausted, Is.False);
            Assert.That(step.Arrived, Is.True);
            Assert.That(session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(positionError, Is.LessThan(10_000.0));
            Assert.That(velocityError, Is.LessThan(10.0));
        }

        [Test]
        public void TheReachedDateIsClampedToWhatTheBudgetCouldIntegrate()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            double requested = _epoch + 1.0; // one day, more than five substeps can cover

            FlightStep step = _session.Advance(requested, 5);

            Assert.That(step.BudgetExhausted, Is.True, "the budget must be reported");
            Assert.That(step.ReachedJulianDate, Is.LessThan(requested),
                "the ship cannot reach the requested date");
            Assert.That(step.ReachedJulianDate, Is.GreaterThan(_epoch), "but it must make progress");
            Assert.That(_session.State.JulianDate, Is.EqualTo(step.ReachedJulianDate),
                "the session reports the date it actually reached");

            // The caller may resume from there; the plan is still live.
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(plan.ArrivalJulianDate, Is.GreaterThan(step.ReachedJulianDate));
        }

        [Test]
        public void ArrivedShipCoastsAndKeepsItsOrbitWithTheDriveOff()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            _session.Advance(plan.ArrivalJulianDate, 50_000);

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.RadiusM(mars, plan.MaxAccelerationMps2);
            double period = ParkingOrbit.PeriodSeconds(mars, plan.MaxAccelerationMps2);

            double before = _session.State.JulianDate;
            double requested = before + 3.0 * period / JulianDate.SecondsPerDay;
            FlightStep coast = _session.Advance(requested, 50_000);

            Assert.That(coast.CollisionDetected, Is.False);
            Assert.That(_session.State.JulianDate, Is.EqualTo(requested).Within(1e-6),
                "an arrived ship keeps coasting with the clock");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(_session.CurrentThrust.MagnitudeMps2, Is.EqualTo(0.0),
                "the drive is off once the plan completes");

            _model.ComputeState(mars, _session.State.JulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            Vector3d relativePosition = _session.State.PositionM - marsPosition;
            Vector3d relativeVelocity = _session.State.VelocityMps - marsVelocity;

            double eccentricity = TwoBody.Eccentricity(relativePosition, relativeVelocity, mars.Gm);
            TestContext.WriteLine($"after three orbits: r={relativePosition.Magnitude / 1000.0:F0} km, " +
                                  $"e={eccentricity:E3}");

            Assert.That(Math.Abs(relativePosition.Magnitude - radius) / radius, Is.LessThan(0.02));
            Assert.That(eccentricity, Is.LessThan(0.03));
        }

        [Test]
        public void CancelStopsTheFlightAndLeavesTheShipWhereItIs()
        {
            _session.Begin("mars", _epoch);
            _session.Advance(_epoch + 0.1, 50_000);
            double date = _session.State.JulianDate;

            _session.Cancel();

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.Plan, Is.Null);

            FlightStep step = _session.Advance(date + 1.0, 50_000);
            Assert.That(step.ReachedJulianDate, Is.EqualTo(date));
            Assert.That(_session.State.JulianDate, Is.EqualTo(date));
        }

        [Test]
        public void AnIdleSessionDoesNotMoveTheShip()
        {
            FlightStep step = _session.Advance(_epoch + 1.0, 50_000);

            Assert.That(step.ReachedJulianDate, Is.EqualTo(_epoch));
            Assert.That(step.Substeps, Is.EqualTo(0));
            Assert.That(_session.State.JulianDate, Is.EqualTo(_epoch));
        }

        [Test]
        public void TeleportMovesTheShipAndClearsTheVelocity()
        {
            var position = new Vector3d(1.2e11, -3.0e10, 4.0e9);
            _session.Teleport(position, _epoch);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(Vector3d.Distance(_session.State.PositionM, position), Is.EqualTo(0.0));
            Assert.That(_session.State.VelocityMps.Magnitude, Is.EqualTo(0.0));
        }

        [Test]
        public void AFlightThroughABodyIsAbortedRatherThanIntegratedThroughIt()
        {
            _session.PlaceAt("mars", _epoch); // inside Mars, at its centre
            _session.Begin("mars", _epoch);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying),
                "the short capture arc from the centre must be flyable");

            FlightStep step = _session.Advance(_session.Plan.ArrivalJulianDate, 5_000);

            Assert.That(step.CollisionDetected, Is.True);
            Assert.That(step.CollisionBodyId, Is.EqualTo("mars"));
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Aborted));
            Assert.That(_session.Failure, Does.Contain("collision"));
        }

        [Test]
        public void PlottingARouteNeverMovesTheShipOrTakesOwnership()
        {
            ShipState before = _session.State;

            TrajectoryPlan preview = _session.Plot("mars", _epoch);

            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.TargetId, Is.EqualTo("mars"));
            Assert.That(_session.HasPreview, Is.True);
            Assert.That(_session.IsPreviewing, Is.True);
            Assert.That(_session.Plan, Is.Null, "a preview is not a flight plan");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.State.JulianDate, Is.EqualTo(before.JulianDate));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM), Is.Zero);
            Assert.That(_session.State.VelocityMps.Magnitude, Is.EqualTo(before.VelocityMps.Magnitude));
        }

        [Test]
        public void BeginInsideThePreviewWindowFliesTheDrawnPlanAndArrivesExactly()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double later = _epoch + 0.5 * _session.PreviewStaleAfterDays;
            var metrics = new PlanMetrics();
            _planner.Metrics = metrics;
            TrajectoryPlan plan;
            try
            {
                plan = _session.Begin("mars", later);
            }
            finally
            {
                _planner.Metrics = null;
            }

            Assert.That(metrics.FastestCalls, Is.Zero,
                "inside the freshness window Begin must not pay for a second plot");
            Assert.That(plan, Is.SameAs(preview),
                "Begin flies the plan the player was looking at");
            Assert.That(plan.DepartureJulianDate, Is.EqualTo(_epoch).Within(1e-12),
                "and from the departure it was certified at");
            Assert.That(_session.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_session.State.JulianDate, Is.EqualTo(_epoch).Within(1e-12),
                "the ship adopts the preview's own departure state");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));

            FlightStep step = _session.Advance(plan.ArrivalJulianDate, 50_000);

            Assert.That(step.Arrived, Is.True);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(Vector3d.Distance(_session.State.PositionM, plan.ArrivalState.PositionM),
                Is.LessThan(10_000.0));
        }

        [Test]
        public void BeginBeyondThePreviewWindowReplansAtTheCurrentDate()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double later = _epoch + 1.2 * _session.PreviewStaleAfterDays;

            TrajectoryPlan plan = _session.Begin("mars", later);

            Assert.That(plan, Is.Not.SameAs(preview),
                "a preview the session calls stale is replanned, not flown");
            Assert.That(plan.DepartureJulianDate, Is.EqualTo(later).Within(1e-12));
            Assert.That(_session.State.JulianDate, Is.EqualTo(later).Within(1e-12));
            Assert.That(_session.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying), _session.Failure);
        }

        [Test]
        public void BeginningAtTheMinimumInsideTheWindowFliesTheDrawnMinimum()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double previewMinimum = _session.PreviewMinimumSeconds;
            double later = _epoch + 0.5 * _session.PreviewStaleAfterDays;

            TrajectoryPlan plan = _session.Begin("mars", later);

            Assert.That(plan, Is.SameAs(preview));
            Assert.That(plan.FlightTimeSeconds,
                Is.EqualTo(previewMinimum).Within(1e-6),
                "the drawn minimum is flown, not a fresh number");
            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "the drawn minimum is still the no-coast plan");
        }

        [Test]
        public void BeginningAtTheMinimumBeyondTheWindowFliesTheReplannedMinimum()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double previewMinimum = _session.PreviewMinimumSeconds;
            double later = _epoch + 1.2 * _session.PreviewStaleAfterDays;
            TrajectoryPlan replanned = _planner.PlanFastest(
                _session.State.WithJulianDate(later), "mars");

            TestContext.WriteLine($"preview minimum {previewMinimum / JulianDate.SecondsPerDay:F6} d, " +
                                  $"replanned at +1.2 windows " +
                                  $"{replanned.FlightTimeSeconds / JulianDate.SecondsPerDay:F6} d");

            Assert.That(replanned.FlightTimeSeconds,
                Is.Not.EqualTo(previewMinimum).Within(1.0),
                "the fresh minimum really is a different plan from the preview's number");

            TrajectoryPlan plan = _session.Begin("mars", later);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying),
                _session.Failure);
            Assert.That(plan.FlightTimeSeconds, Is.EqualTo(replanned.FlightTimeSeconds).Within(1e-6),
                "a stale preview at its minimum flies the fresh minimum, not the old number");
            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "the fresh minimum is still the no-coast plan");
        }

        [Test]
        public void ABeginForAnotherTargetDoesNotFlyThePreview()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);

            TrajectoryPlan plan = _session.Begin("moon", _epoch);

            Assert.That(plan, Is.Not.SameAs(preview));
            Assert.That(plan.TargetId, Is.EqualTo("moon"));
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying), _session.Failure);
        }

        [Test]
        public void ASecondPlotReplacesTheFirstAndCancelClearsIt()
        {
            _session.Plot("mars", _epoch);
            TrajectoryPlan venus = _session.Plot("venus", _epoch);

            Assert.That(_session.Preview, Is.SameAs(venus), "only one preview exists");
            Assert.That(_session.Preview.TargetId, Is.EqualTo("venus"));

            _session.Cancel();

            Assert.That(_session.HasPreview, Is.False);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
        }

        [Test]
        public void APreviewIsFreshUntilOnePercentOfItsFlightTimeHasPassed()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double window = FlightSession.PreviewStaleFraction * preview.FlightTimeDays;

            Assert.That(window, Is.GreaterThan(0.0));
            Assert.That(_session.IsPreviewStale(_epoch), Is.False);
            Assert.That(_session.IsPreviewStale(_epoch + 0.5 * window), Is.False);
            Assert.That(_session.IsPreviewStale(_epoch + 1.01 * window), Is.True);
            Assert.That(_session.PreviewStaleInSeconds(_epoch + 0.5 * window),
                Is.GreaterThan(0.0));
            Assert.That(_session.PreviewStaleInSeconds(_epoch + 1.01 * window),
                Is.LessThan(0.0));
        }

        [Test]
        public void AnIdleShipCoastsWithTheClockAndKeepsItsPreview()
        {
            _session.Plot("mars", _epoch);
            ShipState before = _session.State;
            double later = _epoch + 1.2 * _session.PreviewStaleAfterDays;

            FlightStep step = _session.Coast(later, 20_000);

            Assert.That(step.ReachedJulianDate, Is.EqualTo(later).Within(1e-9));
            Assert.That(_session.State.JulianDate, Is.EqualTo(later).Within(1e-9));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM),
                Is.GreaterThan(1.0), "the ship moves on its orbit");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.HasPreview, Is.True, "coasting must not discard the preview");
            Assert.That(_session.IsPreviewStale(later), Is.True,
                "1.2 times the preview's one-percent window is stale");
        }

        [Test]
        public void CoastingIsRefusedWhileAFlightOwnsTheShip()
        {
            _session.Begin("mars", _epoch);
            ShipState before = _session.State;

            FlightStep step = _session.Coast(_epoch + 0.01, 20_000);

            Assert.That(step.ReachedJulianDate, Is.EqualTo(before.JulianDate));
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
        }

        [Test]
        public void RetimingThePreviewChangesItsTimeButNotTheShip()
        {
            TrajectoryPlan fastest = _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            ShipState before = _session.State;

            TrajectoryPlan slower = _session.SetPreviewDuration(2.0 * minimum);

            Assert.That(_session.Preview, Is.SameAs(slower));
            Assert.That(slower, Is.Not.SameAs(fastest));
            Assert.That(slower.FlightTimeSeconds, Is.EqualTo(2.0 * minimum).Within(1e-3));
            Assert.That(_session.PreviewDurationSeconds,
                Is.EqualTo(2.0 * minimum).Within(1e-3));
            Assert.That(_session.PreviewMinimumSeconds, Is.EqualTo(minimum));
            Assert.That(_session.PreviewMaximumSeconds,
                Is.EqualTo(minimum * TripPlanner.MaxDurationScale).Within(1e-3));
            Assert.That(_session.PreviewJulianDate, Is.EqualTo(_epoch),
                "the draft stays pinned to the date it was plotted at");
            Assert.That(slower.ArrivalJulianDate,
                Is.GreaterThan(fastest.ArrivalJulianDate),
                "a longer trip arrives later, at a different target position");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.State.JulianDate, Is.EqualTo(before.JulianDate));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM),
                Is.EqualTo(0.0), "retiming never moves the ship");
        }

        [Test]
        public void RetimingBackToTheFloorReusesTheCachedFloorPlan()
        {
            TrajectoryPlan floor = _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;

            TrajectoryPlan slower = _session.SetPreviewDuration(2.0 * minimum);
            Assert.That(slower, Is.Not.SameAs(floor), "a longer trip is a new build");

            TrajectoryPlan back = _session.SetPreviewDuration(minimum);

            Assert.That(back, Is.SameAs(floor),
                "the floor is the plan the window already solved; a retime to it must " +
                "not pay for a second certified build");
            Assert.That(_session.Preview, Is.SameAs(floor));
            Assert.That(back.FlightTimeSeconds, Is.EqualTo(minimum).Within(1e-3));
        }

        /// <summary>
        /// The slider's background path reuses the floor too. A drag to the left
        /// end asks for the duration the window already solved, so the worker
        /// must not rebuild it: a cold build is the profile's most expensive
        /// call, and on a near-floor needle it can refuse the very duration the
        /// window advertises.
        /// </summary>
        [Test]
        public void ABackgroundRetimeToTheFloorReusesTheCachedFloorPlan()
        {
            TrajectoryPlan floor = _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            _session.SetPreviewDuration(2.0 * minimum);

            Assert.That(_session.RequestPreviewDuration(minimum), Is.True);
            Assert.That(_session.TryApplyCompletedRetime(out TrajectoryPlan published),
                Is.True, "a request that lands on the floor is answered from the window");

            Assert.That(published, Is.SameAs(floor),
                "the floor is the plan the window already solved; the worker must " +
                "not pay for a second certified build");
            Assert.That(_session.Preview, Is.SameAs(floor));
            Assert.That(_session.PendingPreviewSeconds, Is.NaN);
        }

        /// <summary>
        /// A duration the player has already waited for is a cache hit. The
        /// point is a drag that goes out and comes back: paying for the same
        /// multi-second build twice is what the cache exists to stop.
        /// </summary>
        [Test]
        public void RetimingBackToAPreviouslyBuiltDurationReusesTheCachedPlan()
        {
            _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            double first = 1.6 * minimum;
            double other = 2.2 * minimum;

            TrajectoryPlan builtOnce = _session.SetPreviewDuration(first);
            TrajectoryPlan different = _session.SetPreviewDuration(other);
            Assert.That(different, Is.Not.SameAs(builtOnce),
                "a different duration is a different trip and a new build");

            TrajectoryPlan back = _session.SetPreviewDuration(first);

            Assert.That(back, Is.SameAs(builtOnce),
                "the duration was already built; returning to it must reuse the plan");
            Assert.That(_session.Preview, Is.SameAs(builtOnce));
            Assert.That(back.FlightTimeSeconds, Is.EqualTo(first).Within(1.0));
        }

        /// <summary>
        /// The slider's background path reuses a previously built duration too.
        /// It is handed to the publisher directly, so no worker is started for
        /// work the session has already paid for.
        /// </summary>
        [Test]
        public void ABackgroundRetimeToAPreviouslyBuiltDurationIsAnsweredFromTheCache()
        {
            _session.Plot("mars", _epoch);
            double chosen = 1.7 * _session.PreviewMinimumSeconds;
            TrajectoryPlan built = _session.SetPreviewDuration(chosen);

            Assert.That(_session.RequestPreviewDuration(chosen), Is.True);
            Assert.That(_session.HasRetimeWorker, Is.False,
                "a cached duration must not queue a background build");
            Assert.That(_session.TryApplyCompletedRetime(out TrajectoryPlan published),
                Is.True, "the cached plan is waiting for the publisher");

            Assert.That(published, Is.SameAs(built));
            Assert.That(_session.Preview, Is.SameAs(built));
            Assert.That(_session.PendingPreviewSeconds, Is.NaN);
        }

        /// <summary>
        /// A continuous slider returns near, not bit-identical to, a value it
        /// visited. The cache matches within a fraction of the window so that a
        /// handle put back near its old position still hits, while a genuinely
        /// different trip still builds.
        /// </summary>
        [Test]
        public void ARepeatAskNearACachedDurationReusesItsPlan()
        {
            _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            double span = _session.PreviewMaximumSeconds - minimum;
            double chosen = 1.8 * minimum;
            TrajectoryPlan built = _session.SetPreviewDuration(chosen);

            TrajectoryPlan near = _session.SetPreviewDuration(
                chosen + 0.001 * span);
            Assert.That(near, Is.SameAs(built),
                "a handle returned within a fraction of the window reuses the plan");

            TrajectoryPlan far = _session.SetPreviewDuration(chosen + 0.02 * span);
            Assert.That(far, Is.Not.SameAs(built),
                "outside the tolerance is a different trip and a new build");
        }

        /// <summary>
        /// Retimed plans belong to their departure date, exactly like the
        /// window. A new date drops them, so plotting back to an old date must
        /// build again rather than hand back a plan made at another departure.
        /// </summary>
        [Test]
        public void AChangedDepartureDateDropsTheCachedRetimes()
        {
            _session.Plot("mars", _epoch);
            double chosen = 1.6 * _session.PreviewMinimumSeconds;
            TrajectoryPlan first = _session.SetPreviewDuration(chosen);

            _session.Plot("mars", _epoch + 0.5);
            _session.Plot("mars", _epoch);
            TrajectoryPlan again = _session.SetPreviewDuration(chosen);

            Assert.That(again, Is.Not.SameAs(first),
                "the date change dropped the retimed plans instead of reading " +
                "one back");
        }

        /// <summary>
        /// The cache bound is a memory trade, so pin the size that justifies it.
        /// A plan is dominated by the finite burns' interpolation tables — a
        /// position and a velocity <see cref="Vector3d"/> per sample, at the
        /// certified 512 samples for the departure burn and the brake. This is
        /// the measurement behind keeping dozens of plans rather than a handful.
        /// </summary>
        [Test]
        public void APreviewPlanCostsAboutFiftyKilobytes()
        {
            TrajectoryPlan plan = _session.Plot("mars", _epoch);
            int elementBytes = Marshal.SizeOf<Vector3d>();

            long tables = 0;
            int burns = 0;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (!(arc.Path is FiniteBurnArc burn)) continue;
                burns++;
                tables += ArrayBytes(burn, "_localPositionsM", elementBytes);
                tables += ArrayBytes(burn, "_localVelocitiesMps", elementBytes);
            }

            TestContext.WriteLine(
                $"plan tables {tables / 1024.0:F1} KiB over {burns} burns and " +
                $"{plan.Arcs.Count} arcs; one cached plan is about " +
                $"{tables / 1024.0:F1} KiB");

            Assert.That(burns, Is.GreaterThanOrEqualTo(2),
                "the profile has a departure burn and a brake");
            Assert.That(tables, Is.GreaterThan(32L * 1024),
                "the finite-burn tables are the plan's memory; this is not an " +
                "empty measure");
            Assert.That(tables, Is.LessThan(128L * 1024),
                "a plan must stay small enough for the retime cache to keep dozens");
        }

        /// <summary>Bytes an array field holds, including the array header and
        /// length word, for the test's memory estimate.</summary>
        private static long ArrayBytes(object owner, string fieldName, int elementBytes)
        {
            FieldInfo field = owner.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(owner) is Array array
                ? (long)elementBytes * array.Length + 32L
                : 0L;
        }

        [Test]
        public void ThePreviewDurationIsClampedToItsWindow()
        {
            _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;

            TrajectoryPlan below = _session.SetPreviewDuration(0.25 * minimum);
            Assert.That(below.FlightTimeSeconds, Is.EqualTo(minimum).Within(1e-3),
                "the handle cannot ask for an infeasible plan");

            TrajectoryPlan above = _session.SetPreviewDuration(10.0 * minimum);
            Assert.That(above.FlightTimeSeconds,
                Is.EqualTo(minimum * TripPlanner.MaxDurationScale).Within(1e-3));
        }

        [Test]
        public void BeginFlightsTheRetimedPreviewAtTheChosenTime()
        {
            _session.Plot("mars", _epoch);
            double chosen = 2.0 * _session.PreviewMinimumSeconds;
            _session.SetPreviewDuration(chosen);

            TrajectoryPlan plan = _session.Begin("mars", _epoch);

            Assert.That(_session.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(plan.FlightTimeSeconds, Is.EqualTo(chosen).Within(1e-3),
                "the flight keeps the time the player chose");
            Assert.That(plan.IsFeasible, Is.True);
        }

        [Test]
        public void ReplottingTheSameRouteReusesTheCachedFloorAndWindow()
        {
            TrajectoryPlan first = _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            double maximum = _session.PreviewMaximumSeconds;

            TrajectoryPlan repeat = _session.Plot("mars", _epoch);

            Assert.That(repeat, Is.SameAs(first),
                "the same departure, target, level, and style reuses the solved window");
            Assert.That(_session.PreviewMinimumSeconds, Is.EqualTo(minimum));
            Assert.That(_session.PreviewMaximumSeconds, Is.EqualTo(maximum));

            TrajectoryPlan otherTarget = _session.Plot("venus", _epoch);
            Assert.That(otherTarget, Is.Not.SameAs(first), "another target misses the cache");
            Assert.That(_session.Preview.TargetId, Is.EqualTo("venus"));
        }

        [Test]
        public void AChangedDepartureDateInvalidatesTheCachedWindow()
        {
            TrajectoryPlan first = _session.Plot("mars", _epoch);

            TrajectoryPlan later = _session.Plot("mars", _epoch + 0.5);
            Assert.That(later, Is.Not.SameAs(first), "a later departure is a new window");

            TrajectoryPlan back = _session.Plot("mars", _epoch);
            Assert.That(back, Is.Not.SameAs(first),
                "the date change dropped the old window instead of reading it back");
        }

        [Test]
        public void ABurnLevelChangeMissesTheWindowCache()
        {
            _session.BurnLevelG = 0.3;
            TrajectoryPlan slow = _session.Plot("mars", _epoch);

            _session.SetBurnLevel(0.5);
            TrajectoryPlan fast = _session.SetPreviewDuration(
                _session.PreviewMinimumSeconds);

            Assert.That(fast, Is.Not.SameAs(slow),
                "a different burn level is a different plan");
            Assert.That(fast.FiniteBurnAccelerationMps2,
                Is.EqualTo(0.5 * TripPlanner.StandardGravityMps2).Within(1e-6));
        }

        [Test]
        public void AnUnknownPreviewTargetIsRejectedAndLeavesTheOldPreviewAlone()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);

            Assert.Throws<ArgumentException>(() => _session.Plot("planet-x", _epoch));
            Assert.That(_session.Preview, Is.SameAs(preview));
        }

        [Test]
        public void PlanningAndFlyingLeaveTheOrreryUntouched()
        {
            double date = _model.CurrentJulianDate;
            Vector3d earth = _model.Get("earth").PositionM;

            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            _session.Advance(plan.ArrivalJulianDate, 50_000);

            Assert.That(_model.CurrentJulianDate, Is.EqualTo(date));
            Assert.That(Vector3d.Distance(_model.Get("earth").PositionM, earth), Is.EqualTo(0.0));
        }

        [Test]
        public void AnUnknownTargetIsRejected()
        {
            Assert.Throws<ArgumentException>(() => _session.Begin("planet-x", _epoch));
        }

        // --- The background retime ---------------------------------------------

        /// <summary>
        /// The slider's path: the newest request wins. The first request is
        /// already building when the second is queued, so the finished first
        /// plan is stale; it must never be published, and the second must be.
        /// </summary>
        [Test]
        public void TheBackgroundRetimePublishesTheNewestRequestAndDropsTheStaleOne()
        {
            TrajectoryPlan initial = _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            double first = 1.25 * minimum;
            double second = 1.75 * minimum;

            Assert.That(_session.RequestPreviewDuration(first), Is.True);
            WaitUntil(() => _session.IsRetimeBuilding,
                "the first retime never started building");

            Assert.That(_session.RequestPreviewDuration(second), Is.True);
            Assert.That(_session.Preview, Is.SameAs(initial),
                "the published preview stays until the newest plan is ready");

            TrajectoryPlan published = WaitForPublishedRetime();
            Assert.That(published.FlightTimeSeconds, Is.EqualTo(second).Within(1e-3),
                "the newest request is the one that reaches the HUD");
            Assert.That(_session.Preview, Is.SameAs(published));
            Assert.That(_session.PreviewDurationSeconds, Is.EqualTo(second).Within(1e-3));
            Assert.That(_session.PendingPreviewSeconds, Is.NaN,
                "nothing is outstanding once the newest plan is published");
            Assert.That(_session.IsRetimePending, Is.False);
        }

        /// <summary>
        /// The worker's plan is the same plan the deterministic synchronous path
        /// builds for the same inputs. Both are full-field builds: the worker
        /// gets its own planner and gravity field, not the flight simulation's.
        /// </summary>
        [Test]
        public void TheBackgroundPlanEqualsTheSynchronousPlan()
        {
            _session.Plot("mars", _epoch);
            double chosen = 1.6 * _session.PreviewMinimumSeconds;

            _session.RequestPreviewDuration(chosen);
            TrajectoryPlan background = WaitForPublishedRetime();

            TrajectoryPlan synchronous = _session.SetPreviewDuration(chosen);

            Assert.That(background.FlightTimeSeconds,
                Is.EqualTo(synchronous.FlightTimeSeconds).Within(1e-6));
            Assert.That(background.ArrivalJulianDate,
                Is.EqualTo(synchronous.ArrivalJulianDate).Within(1e-12));
            Assert.That(background.Arcs.Count, Is.EqualTo(synchronous.Arcs.Count));
            Assert.That(background.PeakThrustMps2,
                Is.EqualTo(synchronous.PeakThrustMps2).Within(1e-9));
            Assert.That(background.DeltaVMps,
                Is.EqualTo(synchronous.DeltaVMps).Within(1e-6));
            Assert.That(background.MinimumClearanceM,
                Is.EqualTo(synchronous.MinimumClearanceM).Within(1.0));
            Assert.That(background.IsFeasible, Is.EqualTo(synchronous.IsFeasible));
            Assert.That(background.PeakThrustMps2,
                Is.LessThanOrEqualTo(background.MaxAccelerationMps2 * (1.0 + 1e-6)),
                "the published plan is the full-field certified one");
        }

        /// <summary>
        /// A pending request does not disturb the published preview: the plan,
        /// its duration, its arrival and its route stay the last full-field
        /// plan until the worker hands over a newer one.
        /// </summary>
        [Test]
        public void ThePreviousPreviewRemainsPublishedUntilTheNewPlanIsReady()
        {
            TrajectoryPlan initial = _session.Plot("mars", _epoch);
            int arcs = initial.Arcs.Count;
            double arrival = initial.ArrivalJulianDate;
            double asked = 2.0 * _session.PreviewMinimumSeconds;

            _session.RequestPreviewDuration(asked);

            Assert.That(_session.Preview, Is.SameAs(initial));
            Assert.That(_session.PreviewDurationSeconds,
                Is.EqualTo(initial.FlightTimeSeconds));
            Assert.That(_session.Preview.ArrivalJulianDate, Is.EqualTo(arrival));
            Assert.That(_session.Preview.Arcs.Count, Is.EqualTo(arcs));
            Assert.That(_session.PendingPreviewSeconds, Is.EqualTo(asked).Within(1e-3),
                "the handle's ask is still visible while the plan lags");
            Assert.That(_session.IsRetimePending, Is.True);

            TrajectoryPlan published = WaitForPublishedRetime();
            Assert.That(published, Is.Not.SameAs(initial));
            Assert.That(_session.Preview, Is.SameAs(published));
            Assert.That(_session.PreviewDurationSeconds, Is.EqualTo(asked).Within(1e-3));
        }

        /// <summary>
        /// Discarding the preview drops an outstanding retime: a plan for the
        /// old preview must never appear over the new state.
        /// </summary>
        [Test]
        public void DiscardingThePreviewDropsAnOutstandingRetime()
        {
            _session.Plot("mars", _epoch);
            _session.RequestPreviewDuration(1.5 * _session.PreviewMinimumSeconds);
            WaitUntil(() => _session.IsRetimeBuilding, "the retime never started building");

            _session.DiscardPreview();

            Assert.That(_session.IsRetimePending, Is.False,
                "a discarded ask can no longer be published");
            Assert.That(_session.PendingPreviewSeconds, Is.NaN);

            // The stale build still finishes on the worker; it must be dropped.
            WaitUntil(() => !_session.IsRetimeBuilding,
                "the discarded build never finished");
            Assert.That(_session.TryApplyCompletedRetime(out _), Is.False);
            Assert.That(_session.HasPreview, Is.False);
        }

        /// <summary>
        /// A synchronous retime is authoritative: it cancels an outstanding ask,
        /// publishes its own plan immediately, and the worker's stale build is
        /// dropped when it lands.
        /// </summary>
        [Test]
        public void ASynchronousRetimeSupersedesAnOutstandingBackgroundAsk()
        {
            _session.Plot("mars", _epoch);
            double minimum = _session.PreviewMinimumSeconds;
            _session.RequestPreviewDuration(1.4 * minimum);
            WaitUntil(() => _session.IsRetimeBuilding, "the retime never started building");

            TrajectoryPlan synchronous = _session.SetPreviewDuration(1.8 * minimum);

            Assert.That(synchronous.FlightTimeSeconds, Is.EqualTo(1.8 * minimum).Within(1e-3));
            Assert.That(_session.Preview, Is.SameAs(synchronous));
            Assert.That(_session.IsRetimePending, Is.False,
                "the background ask was superseded, not left dangling");
            Assert.That(_session.PendingPreviewSeconds, Is.NaN);

            WaitUntil(() => !_session.IsRetimeBuilding,
                "the superseded build never finished");
            Assert.That(_session.TryApplyCompletedRetime(out _), Is.False,
                "the superseded build must not publish over the synchronous plan");
            Assert.That(_session.Preview, Is.SameAs(synchronous));
        }

        /// <summary>
        /// The lifecycle: Dispose joins the worker, refuses new requests, and a
        /// disposed session never publishes. A test that ends with an alive
        /// worker would hang the editor's domain reload, so this is the guard.
        /// </summary>
        [Test]
        public void DisposingTheSessionStopsTheRetimeWorker()
        {
            _session.Plot("mars", _epoch);
            Assert.That(_session.RequestPreviewDuration(
                1.5 * _session.PreviewMinimumSeconds), Is.True);
            Assert.That(_session.HasRetimeWorker, Is.True,
                "the first request starts the worker");

            _session.Dispose();

            Assert.That(_session.HasRetimeWorker, Is.False, "Dispose joins the worker");
            Assert.That(_session.RequestPreviewDuration(
                2.0 * _session.PreviewMinimumSeconds), Is.False,
                "a disposed session refuses new work instead of starting a thread");
            Assert.That(_session.TryApplyCompletedRetime(out _), Is.False);
            Assert.That(_session.HasRetimeWorker, Is.False);
        }

        /// <summary>The test-side equivalent of the HUD's per-frame pump: wait
        /// for a finished plan and publish it on this thread.</summary>
        private TrajectoryPlan WaitForPublishedRetime()
        {
            TrajectoryPlan published = null;
            WaitUntil(() => _session.TryApplyCompletedRetime(out published),
                "no retimed plan was published");
            return published;
        }

        // --- Prefetching the slider's neighbours -------------------------------

        /// <summary>A slider step in the preview's own window: the HUD offers
        /// <see cref="SolarSystemHud.TimeSliderSteps"/> positions, so one step is
        /// the span over the count minus one.</summary>
        private static double StepSeconds(double minimum, double maximum, int step) =>
            minimum + (maximum - minimum) * step / (SolarSystemHud.TimeSliderSteps - 1);

        private void WaitForPrefetchIdle()
        {
            WaitUntil(() => !_session.IsPrefetchPending,
                "the prefetch never settled");
        }

        /// <summary>
        /// A published plot queues the two neighbours above its duration. The
        /// preview starts at the window minimum, so the two below clamp away and
        /// nothing is faked below it.
        /// </summary>
        [Test]
        public void APlotPrefetchesTheTwoStepsAboveIt()
        {
            _session.PlotAsync("mars", _epoch);
            TrajectoryPlan plotted = WaitForPublishedPreview();
            WaitForPrefetchIdle();

            double minimum = _session.PreviewMinimumSeconds;
            double maximum = _session.PreviewMaximumSeconds;
            double first = StepSeconds(minimum, maximum, 1);
            double second = StepSeconds(minimum, maximum, 2);

            TestContext.WriteLine($"plot {plotted.FlightTimeDays:F5} d: " +
                $"{_session.PrefetchBuilds} neighbours prefetched from " +
                $"{minimum / JulianDate.SecondsPerDay:F5} d over " +
                $"{(maximum - minimum) / JulianDate.SecondsPerDay:F5} d");

            Assert.That(_session.PrefetchBuilds, Is.GreaterThanOrEqualTo(2),
                "the plot built its two upper neighbours");
            Assert.That(_session.HasCachedPlanAt(first), Is.True,
                "step 1 is in the cache");
            Assert.That(_session.HasCachedPlanAt(second), Is.True,
                "step 2 is in the cache");
            Assert.That(_session.HasCachedPlanAt(minimum -
                    (maximum - minimum) / (SolarSystemHud.TimeSliderSteps - 1)),
                Is.False, "no plan is invented below the window minimum");
            Assert.That(_session.Preview, Is.SameAs(plotted),
                "prefetching never republishes the preview");
        }

        /// <summary>
        /// The drag's first move publishes from the cache: no build, no wait.
        /// The counters are the evidence, not a time.
        /// </summary>
        [Test]
        public void ADragPublishesThePrefetchedNeighbourWithoutABuild()
        {
            _session.PlotAsync("mars", _epoch);
            TrajectoryPlan plotted = WaitForPublishedPreview();
            WaitForPrefetchIdle();

            double step = StepSeconds(_session.PreviewMinimumSeconds,
                _session.PreviewMaximumSeconds, 1);
            int builds = _session.RetimeBuilds;
            int hits = _session.RetimeCacheHits;

            Assert.That(_session.RequestPreviewDuration(step), Is.True);
            TrajectoryPlan published = WaitForPublishedRetime();

            TestContext.WriteLine($"drag to step 1 at " +
                $"{published.FlightTimeDays:F5} d: builds " +
                $"{_session.RetimeBuilds - builds}, cache hits " +
                $"{_session.RetimeCacheHits - hits}");

            Assert.That(published.FlightTimeSeconds, Is.EqualTo(step).Within(1e-3));
            Assert.That(published, Is.Not.SameAs(plotted));
            Assert.That(_session.RetimeBuilds, Is.EqualTo(builds),
                "the prefetched neighbour needed no build");
            Assert.That(_session.RetimeCacheHits, Is.EqualTo(hits + 1),
                "the ask was answered from the cache");
        }

        /// <summary>
        /// A prefetch fills a cache slot and nothing else: it never publishes a
        /// retime, never publishes a preview, and never leaves an ask
        /// outstanding.
        /// </summary>
        [Test]
        public void APrefetchNeverPublishesByItself()
        {
            _session.PlotAsync("mars", _epoch);
            TrajectoryPlan plotted = WaitForPublishedPreview();
            WaitForPrefetchIdle();

            Assert.That(_session.PrefetchBuilds, Is.GreaterThan(0),
                "the plot queued neighbours to prefetch");
            Assert.That(_session.TryApplyCompletedRetime(out _), Is.False,
                "a prefetched plan is never a published retime");
            Assert.That(_session.TryApplyCompletedPreview(out _), Is.False,
                "a prefetch is not a preview build");
            Assert.That(_session.Preview, Is.SameAs(plotted));
            Assert.That(_session.PreviewDurationSeconds,
                Is.EqualTo(plotted.FlightTimeSeconds));
            Assert.That(_session.IsRetimePending, Is.False);
            Assert.That(_session.PendingPreviewSeconds, Is.NaN);
        }

        /// <summary>
        /// Discarding the preview drops the queued neighbours: they belong to
        /// the preview that is going away, and none of them may publish.
        /// </summary>
        [Test]
        public void DiscardingThePreviewDropsQueuedPrefetch()
        {
            _session.PlotAsync("mars", _epoch);
            WaitForPublishedPreview();

            _session.DiscardPreview();

            Assert.That(_session.QueuedPrefetchCount, Is.Zero,
                "the queued neighbours are dropped with the preview");
            WaitForPrefetchIdle();
            int settled = _session.PrefetchBuilds;

            Assert.That(_session.HasPreview, Is.False);
            Assert.That(_session.TryApplyCompletedRetime(out _), Is.False,
                "a prefetched plan cannot resurrect a discarded preview");
            Assert.That(_session.PreviewDurationSeconds, Is.NaN);
            Assert.That(_session.PrefetchBuilds, Is.EqualTo(settled),
                "no new neighbour is queued after the discard");
        }

        /// <summary>
        /// A real retime that arrives while a neighbour is building waits for
        /// that one build and no more: an in-flight build cannot be cancelled,
        /// so one build is the prefetch's latency tail. That build is a build's
        /// own cost, not the warm retime's — the first step above the floor at
        /// Mars takes about 0.9 s against a mid-window build's 0.44 s. The
        /// queued neighbours are replaced by the ask's own direction at once.
        /// </summary>
        [Test]
        public void ARetimeArrivingMidPrefetchWaitsAtMostOneBuild()
        {
            _session.PlotAsync("mars", _epoch);
            WaitForPublishedPreview();
            WaitUntil(() => _session.IsPrefetchBuilding,
                "the plot's prefetch never started");

            double target = 1.5 * _session.PreviewMinimumSeconds;
            double[] before = _session.QueuedPrefetchSeconds();
            long started = Stopwatch.GetTimestamp();
            Assert.That(_session.RequestPreviewDuration(target), Is.True);

            double[] after = _session.QueuedPrefetchSeconds();
            Assert.That(after, Is.Not.Empty,
                "the ask queues its own neighbours ahead");
            Assert.That(after, Is.All.GreaterThan(target),
                "the stale neighbours are replaced by the new direction's");
            if (before.Length > 0)
            {
                Assert.That(before, Is.All.LessThan(target),
                    "the plot's queued neighbours were below the new ask");
            }

            TrajectoryPlan published = WaitForPublishedRetime();
            double waited = (Stopwatch.GetTimestamp() - started) * 1000.0
                            / Stopwatch.Frequency;

            // The same duration, built on this thread with no worker beside it,
            // and the neighbour the plot had queued: the wait is at most that
            // in-flight prefetch plus this ask's own build.
            ShipState departure = _session.State.WithJulianDate(_epoch);
            double neighbour = StepSeconds(_session.PreviewMinimumSeconds,
                _session.PreviewMaximumSeconds, 1);

            var watch = Stopwatch.StartNew();
            TrajectoryPlan reference = _planner.PlanAtDuration(departure, "mars", target);
            double oneBuild = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            _planner.PlanAtDuration(departure, "mars", neighbour);
            double prefetchBuild = watch.Elapsed.TotalMilliseconds;

            TestContext.WriteLine($"mid-prefetch retime: {waited:F0} ms against a " +
                $"{oneBuild:F0} ms ask build and a {prefetchBuild:F0} ms neighbour " +
                $"({waited / oneBuild:F2} ask builds), queued prefetch " +
                $"{_session.QueuedPrefetchCount}");

            Assert.That(published.FlightTimeSeconds, Is.EqualTo(target).Within(1e-3));
            Assert.That(reference.IsFeasible, Is.True);
            Assert.That(waited,
                Is.LessThanOrEqualTo((prefetchBuild + oneBuild) * 1.25 + 150.0),
                "the ask waits for the in-flight prefetch plus its own build, not a third");
        }

        private static void WaitUntil(Func<bool> condition, string message)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed.TotalSeconds > 20.0) Assert.Fail(message);
                Thread.Sleep(1);
            }
        }
    }
}
