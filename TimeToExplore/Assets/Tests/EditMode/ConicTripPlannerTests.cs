using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The conic arrival as the Go To planner builds and flies it: a powered
    /// departure and cruise, then coasted hyperbola, impulsive capture,
    /// transfer ellipse, and circularisation. The quintic capture stays the
    /// default; this suite proves the alternative end to end.
    /// </summary>
    public class ConicTripPlannerTests
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
            _planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
        }

        private TrajectoryPlan PlanTo(string targetId)
        {
            return _planner.Plan(FlightFixture.ShipAt(_model, "gateway"), targetId);
        }

        [Test]
        public void AConicPlanHasACoastCaptureAndParkingInsteadOfAQuinticCapture()
        {
            TrajectoryPlan plan = PlanTo("mars");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs)
                TestContext.WriteLine("  " + arc.Phase + " " + arc);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(5));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Parking));
            Assert.That(plan.Arcs[2].IsCoast, Is.True);
            Assert.That(plan.Arcs[3].IsCoast, Is.True);
            Assert.That(plan.Arcs[4].IsCoast, Is.True);
            Assert.That(plan.Arcs[3].EntryImpulseMps, Is.GreaterThan(0.0));
            Assert.That(plan.ImpulseDeltaVMps, Is.GreaterThan(0.0));

            // The coast's reference is two-body, so the only measured thrust is
            // the third-body correction: near-zero, not the cruise's burn.
            Assert.That(plan.Arcs[2].PeakThrustMps2, Is.LessThan(0.01));
            Assert.That(plan.Arcs[2].DeltaVMps, Is.LessThan(1_000.0));
            Assert.That(plan.Arcs[3].PeakThrustMps2, Is.LessThan(0.01));
        }

        [Test]
        public void TheConicArrivalParksLowerAndCostsLessThanTheQuinticCapture()
        {
            var quinticPlanner = new TripPlanner(_model, _gravity);
            TrajectoryPlan quintic = quinticPlanner.Plan(FlightFixture.ShipAt(_model, "gateway"), "mars");
            TrajectoryPlan conic = PlanTo("mars");

            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, conic.ArrivalJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            Vector3d relativePosition = conic.ArrivalState.PositionM - marsPosition;
            Vector3d relativeVelocity = conic.ArrivalState.VelocityMps - marsVelocity;

            double radius = ParkingOrbit.ConicRadiusM(mars);
            TestContext.WriteLine($"quintic {quintic.FlightTimeDays:F2} d, " +
                                  $"{quintic.DeltaVMps / 1000.0:N0} km/s, " +
                                  $"park {ParkingOrbit.RadiusM(mars) / 1000.0:N0} km");
            TestContext.WriteLine($"conic   {conic.FlightTimeDays:F2} d, " +
                                  $"{conic.DeltaVMps / 1000.0:N0} km/s, " +
                                  $"park {radius / 1000.0:N0} km, " +
                                  $"burns {conic.ImpulseDeltaVMps / 1000.0:F2} km/s");

            Assert.That(relativePosition.Magnitude, Is.EqualTo(radius).Within(1.0));
            Assert.That(relativeVelocity.Magnitude,
                Is.EqualTo(ParkingOrbit.SpeedMps(mars)).Within(1e-3));
            Assert.That(TwoBody.Eccentricity(relativePosition, relativeVelocity, mars.Gm),
                Is.LessThan(1e-9));
            Assert.That(radius, Is.LessThanOrEqualTo(ParkingOrbit.RadiusM(mars) + 1.0),
                "Mars is already under the ceiling, so both arrivals park together");

            Assert.That(conic.DeltaVMps, Is.LessThan(quintic.DeltaVMps),
                "the coast and the Oberth capture spend less delta-v");
            Assert.That(conic.FlightTimeSeconds, Is.GreaterThan(quintic.FlightTimeSeconds),
                "the cheap arrival is slower, not faster");
        }

        [Test]
        public void ArcsJoinInPositionAndAccelerationWithVelocityStepsAtTheBurns()
        {
            TrajectoryPlan plan = PlanTo("mars");
            BodyRuntime mars = _model.Get("mars");

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];

                Assert.That(left.EndJulianDate, Is.EqualTo(right.StartJulianDate));
                MathAssert.AreClose(left.Path.PositionAt(1.0), right.Path.PositionAt(0.0), 1.0);
                MathAssert.AreClose(left.Path.AccelerationAt(1.0),
                    right.Path.AccelerationAt(0.0), 1e-6);

                Vector3d expectedStep = right.Path.VelocityAt(0.0)
                                        - left.Path.VelocityAt(1.0);
                MathAssert.AreClose(right.EntryImpulseVectorMps, expectedStep, 1e-6);
            }

            // The capture burns are at the periapsis and apoapsis, and the
            // cruise-to-coast seam is continuous in velocity.
            Assert.That(plan.Arcs[2].EntryImpulseMps, Is.EqualTo(0.0).Within(1e-6));
            Assert.That(plan.Arcs[3].EntryImpulseMps, Is.GreaterThan(0.0));
            Assert.That(plan.Arcs[4].EntryImpulseMps, Is.GreaterThan(0.0),
                "the transfer ellipse is eccentric, so apoapsis needs circularising");
        }

        [Test]
        public void FlyToMarsOnAConicArrivalAndHoldTheLowerOrbit()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = PlanTo("mars");

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"arrival error: {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s, {report.Substeps} substeps");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(ship.JulianDate, Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(positionError, Is.LessThan(10_000.0));
            Assert.That(velocityError, Is.LessThan(10.0));

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.ConicRadiusM(mars);
            AssertOrbitAround(mars, ship, radius, 0.01, 0.01);

            double period = ParkingOrbit.PeriodSeconds(mars);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(mars, ship, radius, 0.02, 0.03);
        }

        [TestCase("earth")]
        [TestCase("venus")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        public void EveryMajorTargetCanBeReachedOnAConicArrival(string targetId)
        {
            ShipState ship = targetId == "earth"
                ? ShipInMarsOrbit()
                : FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, targetId);

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].Phase, Is.EqualTo(FlightPhase.Parking));

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            BodyRuntime target = _model.Get(targetId);
            double radius = ParkingOrbit.ConicRadiusM(target);
            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"{targetId}: arrival {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s, {report.Substeps} substeps, " +
                                  $"park {radius / 1000.0:N0} km");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(ship.JulianDate, Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(positionError, Is.LessThan(50_000.0));
            Assert.That(velocityError, Is.LessThan(20.0));
            AssertOrbitAround(target, ship, radius, 0.02, 0.02);

            double period = ParkingOrbit.PeriodSeconds(target);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(target, ship, radius, 0.03, 0.03);
        }

        [TestCase("earth")]
        [TestCase("venus")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        public void TheConicCaptureIsCheaperThanTheQuinticCapture(string targetId)
        {
            ShipState ship = targetId == "earth"
                ? ShipInMarsOrbit()
                : FlightFixture.ShipAt(_model, "gateway");
            var quinticPlanner = new TripPlanner(_model, _gravity);
            TrajectoryPlan quintic = quinticPlanner.Plan(ship, targetId);
            TrajectoryPlan conic = _planner.Plan(ship, targetId);

            PlanArc quinticCapture = quintic.ArcForPhase(FlightPhase.Capture);
            BodyRuntime target = _model.Get(targetId);
            TestContext.WriteLine($"{targetId}: quintic capture " +
                                  $"{quinticCapture.DeltaVMps / 1000.0:F1} km/s over " +
                                  $"{quinticCapture.DurationSeconds / 3600.0:F1} h; conic " +
                                  $"capture {conic.ImpulseDeltaVMps / 1000.0:F2} km/s, " +
                                  $"park {ParkingOrbit.ConicRadiusM(target) / 1000.0:N0} km");

            Assert.That(conic.IsFeasible, Is.True, conic.InfeasibleReason);
            Assert.That(conic.ImpulseDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(conic.ImpulseDeltaVMps, Is.LessThan(quinticCapture.DeltaVMps),
                "the Oberth capture spends less than the continuous-thrust capture");
            Assert.That(ParkingOrbit.ConicRadiusM(target),
                Is.LessThanOrEqualTo(ParkingOrbit.RadiusM(target, 10.0) + 1.0));
        }

        private ShipState ShipInMarsOrbit()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            ParkingOrbit.StateAt(mars, marsPosition, marsVelocity, 0.4,
                out Vector3d position, out Vector3d velocity);
            return new ShipState(_model.CurrentJulianDate, position, velocity);
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
        }
    }
}
