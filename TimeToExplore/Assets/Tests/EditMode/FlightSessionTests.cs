using System;
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

            FlightStep coast = _session.Advance(
                _session.State.JulianDate + 3.0 * period / JulianDate.SecondsPerDay, 50_000);

            Assert.That(coast.CollisionDetected, Is.False);
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
    }
}
