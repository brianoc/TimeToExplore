using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

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
        public void BeginPlansTheTestedMarsTripAndStartsFlying()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(plan.IsFeasible, Is.True);
            Assert.That(plan.TargetId, Is.EqualTo("mars"));
            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(_session.Phase, Is.EqualTo(FlightPhase.Departure));

            // Thrust is zero at the seam by construction, so measure it once the
            // ship is a little way into the departure arc.
            _session.Advance(plan.DepartureJulianDate + 0.002, 50_000);
            Assert.That(_session.CurrentThrust.MagnitudeMps2, Is.GreaterThan(0.0));
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

        [Test]
        public void TheSessionArrivalStyleReachesThePlanner()
        {
            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.Quintic));
            _session.ArrivalStyle = CaptureStyle.Conic;

            TrajectoryPlan plan = _session.Begin("mars", _epoch);

            Assert.That(plan.Arcs.Count, Is.EqualTo(5));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Parking));
        }

        [Test]
        public void ChangingTheArrivalStyleRebuildsThePreviewAndLeavesTheShipStill()
        {
            TrajectoryPlan quintic = _session.Plot("mars", _epoch);
            double chosen = 2.0 * _session.PreviewMinimumSeconds;
            _session.SetPreviewDuration(chosen);
            ShipState before = _session.State;

            TrajectoryPlan conic = _session.SetArrivalStyle(CaptureStyle.Conic);

            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.Conic));
            Assert.That(_session.Preview, Is.SameAs(conic));
            Assert.That(conic, Is.Not.SameAs(quintic), "the preview is rebuilt, not relabelled");
            Assert.That(conic.TargetId, Is.EqualTo("mars"));
            Assert.That(conic.Arcs.Any(arc => arc.Phase == FlightPhase.Coast), Is.True);
            Assert.That(conic.Arcs.Any(arc => arc.Phase == FlightPhase.Parking), Is.True);
            Assert.That(_session.PreviewMinimumSeconds, Is.GreaterThan(0.0));
            Assert.That(_session.PreviewMinimumSeconds,
                Is.LessThanOrEqualTo(_session.PreviewDurationSeconds + 1e-3),
                "the trip-time window is the new style's fastest plan");
            Assert.That(_session.PreviewDurationSeconds, Is.EqualTo(chosen).Within(1.0),
                "a chosen flight time inside the new window is kept");
            Assert.That(_session.PreviewJulianDate, Is.EqualTo(_epoch));
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.State.JulianDate, Is.EqualTo(before.JulianDate));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM), Is.Zero,
                "changing the arrival style never moves the ship");

            TrajectoryPlan back = _session.SetArrivalStyle(CaptureStyle.Quintic);

            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.Quintic));
            Assert.That(back.Arcs.Count, Is.EqualTo(3), "the quintic capture is back");
            Assert.That(back.Arcs[2].Phase, Is.EqualTo(FlightPhase.Capture));
        }

        [Test]
        public void ArrivalStyleWithoutAPreviewOnlySetsThePlanner()
        {
            Assert.That(_session.SetArrivalStyle(CaptureStyle.Conic), Is.Null);
            Assert.That(_session.ArrivalStyle, Is.EqualTo(CaptureStyle.Conic));

            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Coast));

            // A live flight keeps the plan it was begun with.
            TrajectoryPlan live = _session.Plan;
            _session.SetArrivalStyle(CaptureStyle.Quintic);
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
            Assert.That(plan.Arcs.Count, Is.EqualTo(5));
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

            TrajectoryPlan plan = session.Begin("mars", _epoch);
            Assert.That(plan.Arcs.Count, Is.EqualTo(6));
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
        public void BeginAfterAPlotReplansFromTheCurrentDateAndArrivesExactly()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double later = _epoch + 0.05;

            TrajectoryPlan plan = _session.Begin("mars", later);

            Assert.That(plan, Is.Not.SameAs(preview), "Begin must replan, not fly the preview");
            Assert.That(plan.DepartureJulianDate, Is.EqualTo(later).Within(1e-12));
            Assert.That(_session.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_session.State.JulianDate, Is.EqualTo(later));
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Flying));

            FlightStep step = _session.Advance(plan.ArrivalJulianDate, 50_000);

            Assert.That(step.Arrived, Is.True);
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived));
            Assert.That(Vector3d.Distance(_session.State.PositionM, plan.ArrivalState.PositionM),
                Is.LessThan(10_000.0));
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
            double later = _epoch + 1.2 / 24.0;

            FlightStep step = _session.Coast(later, 20_000);

            Assert.That(step.ReachedJulianDate, Is.EqualTo(later).Within(1e-9));
            Assert.That(_session.State.JulianDate, Is.EqualTo(later).Within(1e-9));
            Assert.That(Vector3d.Distance(_session.State.PositionM, before.PositionM),
                Is.GreaterThan(1.0), "the ship moves on its orbit");
            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_session.HasPreview, Is.True, "coasting must not discard the preview");
            Assert.That(_session.IsPreviewStale(later), Is.True,
                "1.2 hours is past the Mars preview's one-percent window");
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
            _session.CruiseProfile = CruiseStyle.BurnCoastBurn;
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
