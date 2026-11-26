using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// End-to-end checks: plan a trip, fly it with the autopilot, and inspect the
    /// orbit the ship is left in. These are the milestone's acceptance tests.
    /// </summary>
    [Category("deep")]
    public class FlightExecutionTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private ShipIntegrator _integrator;
        private TripPlanner _planner;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _integrator = new ShipIntegrator(_gravity);
            _planner = new TripPlanner(_model, _gravity);
        }

        [Test]
        public void FlyEarthToMarsAndArriveInAStableOrbit()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            TestContext.WriteLine(plan.ToString());
            TestContext.WriteLine("infeasible: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(ship.JulianDate, Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));

            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"arrival error: {positionError:F1} m, {velocityError:F4} m/s");
            TestContext.WriteLine($"substeps: {report.Substeps}, " +
                                  $"step {report.MinStepSeconds:F1}-{report.MaxStepSeconds:F1} s");

            Assert.That(positionError, Is.LessThan(10_000.0), "arrive within 10 km");
            Assert.That(velocityError, Is.LessThan(10.0), "arrive within 10 m/s");

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.ConicRadiusM(mars);
            AssertOrbitAround(mars, ship, radius, 0.01, 0.01);

            // The plan is done, so the drive is off; the ship keeps the orbit.
            double period = TwoBody.PeriodSeconds(mars.Gm, radius);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(mars, ship, radius, 0.02, 0.03);
        }

        [Test]
        public void AutopilotCorrectsAStartingStateThatIsOffThePlan()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            ship.PositionM += new Vector3d(5_000.0, 0.0, 0.0);
            ship.VelocityMps += new Vector3d(0.0, 5.0, 0.0);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            TestContext.WriteLine($"perturbed arrival error: {positionError:F1} m");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(positionError, Is.LessThan(20_000.0));

            BodyRuntime mars = _model.Get("mars");
            AssertOrbitAround(mars, ship, ParkingOrbit.ConicRadiusM(mars), 0.05, 0.05);
        }

        [Test]
        public void MarsToEarthAtTheEpochIsRejectedForARetrogradeTransfer()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            ParkingOrbit.StateAt(mars, marsPosition, marsVelocity, 0.4,
                out Vector3d position, out Vector3d velocity);

            var ship = new ShipState(_model.CurrentJulianDate, position, velocity);
            TrajectoryPlan plan = _planner.Plan(ship, "earth");

            TestContext.WriteLine("reason: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.False,
                "the short Earth arc runs clockwise at this epoch, and the profile only has the prograde branch");
            Assert.That(plan.InfeasibleReason, Does.Contain("prograde"));
            Assert.That(plan.InfeasibleReason, Does.Contain("short-way"));
        }

        [Test]
        public void FlyToJupiterAndArriveInAStableOrbit()
        {
            // The deep-well case: the arrival orbit now sits at 40% of the drive,
            // so the capture arc is flyable. This is the end-to-end proof that
            // parking higher on the giants works.
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "jupiter");

            TestContext.WriteLine(plan.ToString());
            TestContext.WriteLine("infeasible: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double error = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            TestContext.WriteLine($"arrival error: {error:F1} m, {report.Substeps} substeps");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(error, Is.LessThan(10_000.0));

            BodyRuntime jupiter = _model.Get("jupiter");
            double radius = ParkingOrbit.ConicRadiusM(jupiter);
            AssertOrbitAround(jupiter, ship, radius, 1e-3, 0.01);

            // The drive is off, so the orbit must persist.
            double period = TwoBody.PeriodSeconds(jupiter.Gm, radius);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(jupiter, ship, radius, 1e-3, 0.01);
        }

        [Test]
        public void FlightDoesNotDisturbTheOrrery()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            double julianDate = _model.CurrentJulianDate;
            Vector3d earthBefore = _model.Get("earth").PositionM;

            TrajectoryPlan plan = _planner.Plan(ship, "mars");
            var autopilot = new Autopilot(_gravity, plan);
            autopilot.Fly(ref ship, _integrator);

            Assert.That(_model.CurrentJulianDate, Is.EqualTo(julianDate));
            Assert.That(Vector3d.Distance(_model.Get("earth").PositionM, earthBefore),
                Is.EqualTo(0.0));
        }

        private void AssertOrbitAround(BodyRuntime body, ShipState ship, double expectedRadius,
            double eccentricityTolerance, double radiusTolerance)
        {
            _model.ComputeState(body, ship.JulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);

            Vector3d relativePosition = ship.PositionM - bodyPosition;
            Vector3d relativeVelocity = ship.VelocityMps - bodyVelocity;

            double eccentricity = TwoBody.Eccentricity(relativePosition, relativeVelocity, body.Gm);
            double radius = relativePosition.Magnitude;

            TestContext.WriteLine($"{body.Id}: r={radius / 1000.0:F0} km " +
                                  $"(target {expectedRadius / 1000.0:F0} km), e={eccentricity:E3}");

            Assert.That(Math.Abs(radius - expectedRadius) / expectedRadius,
                Is.LessThan(radiusTolerance), "parking radius");
            Assert.That(eccentricity, Is.LessThan(eccentricityTolerance), "eccentricity");
            Assert.That(TwoBody.InclinationRad(relativePosition, relativeVelocity),
                Is.GreaterThan(0.0), "a real orbit, not a radial fall");
        }
    }
}
