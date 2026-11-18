using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The third arrival style end to end: the same departure and cruise as the
    /// conic plan, then a coast, a fixed-thrust capture burn, a transfer coast, a
    /// fixed-thrust circularisation, and the parking coast. The throttle steps
    /// between zero and the fixed level; nothing blends.
    /// </summary>
    public class FiniteBurnTripPlannerTests
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
                ArrivalStyle = CaptureStyle.FiniteBurn
            };
        }

        private TrajectoryPlan PlanTo(string targetId)
        {
            return _planner.Plan(FlightFixture.ShipAt(_model, "gateway"), targetId);
        }

        [Test]
        public void AFixedBurnPlanHasTwoFiniteBurnsAndTwoCoasts()
        {
            TrajectoryPlan plan = PlanTo("mars");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs)
                TestContext.WriteLine("  " + arc.Phase + " " + arc +
                                      (arc.IsFiniteBurn ? " [fixed]" : arc.IsCoast ? " [coast]" : ""));

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[2].IsCoast, Is.True);
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[3].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[4].IsCoast, Is.True);
            Assert.That(plan.Arcs[5].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[5].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[6].Phase, Is.EqualTo(FlightPhase.Parking));
            Assert.That(plan.Arcs[6].IsCoast, Is.True);

            Assert.That(plan.ImpulseDeltaVMps, Is.EqualTo(0.0).Within(1e-9),
                "the fixed style has no impulsive burns");
            Assert.That(plan.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(plan.FiniteBurnAccelerationMps2,
                Is.EqualTo(TripPlanner.DefaultFixedBurnAccelerationMps2).Within(1e-6));

            foreach (PlanArc arc in plan.Arcs)
            {
                if (!arc.IsFiniteBurn) continue;
                Assert.That(arc.PeakThrustMps2,
                    Is.EqualTo(TripPlanner.DefaultFixedBurnAccelerationMps2).Within(0.01),
                    "a finite burn is one fixed level");
            }

            Assert.That(plan.Arcs[2].PeakThrustMps2, Is.LessThan(0.01));
            Assert.That(plan.Arcs[4].PeakThrustMps2, Is.LessThan(0.01));
        }

        [Test]
        public void TheFixedBurnSeamsStepInsteadOfBlending()
        {
            TrajectoryPlan plan = PlanTo("mars");

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];

                Assert.That(left.EndJulianDate, Is.EqualTo(right.StartJulianDate));
                MathAssert.AreClose(left.Path.PositionAt(1.0), right.Path.PositionAt(0.0), 1.0);
                MathAssert.AreClose(left.Path.VelocityAt(1.0), right.Path.VelocityAt(0.0), 1.0);

                // A fixed burn meets a coast with an acceleration step of exactly
                // the burn level, not a ramp.
                if (left.IsFiniteBurn || right.IsFiniteBurn)
                {
                    double step = (right.Path.AccelerationAt(0.0)
                                   - left.Path.AccelerationAt(1.0)).Magnitude;
                    Assert.That(step,
                        Is.EqualTo(TripPlanner.DefaultFixedBurnAccelerationMps2).Within(0.02),
                        $"seam {i}: {left.Phase} -> {right.Phase}");
                }
            }
        }

        [Test]
        public void FlyToMarsOnAFixedBurnArrival()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = PlanTo("mars");

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"arrival error: {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s, {report.Substeps} substeps");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(ship.JulianDate, Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(positionError, Is.LessThan(50_000.0));
            Assert.That(velocityError, Is.LessThan(20.0));

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.FiniteBurnRadiusM(mars);
            AssertOrbitAround(mars, ship, radius, 0.02, 0.02);

            double period = TwoBody.PeriodSeconds(mars.Gm, radius);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(mars, ship, radius, 0.03, 0.03);
        }

        [TestCase("earth")]
        [TestCase("venus")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        public void EveryMajorTargetCanBeReachedOnAFixedBurnArrival(string targetId)
        {
            ShipState ship = targetId == "earth"
                ? ShipInMarsOrbit()
                : FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, targetId);

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            BodyRuntime target = _model.Get(targetId);
            double radius = ParkingOrbit.FiniteBurnRadiusM(target);
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
        }

        [TestCase("earth")]
        [TestCase("venus")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        public void TheFixedBurnCaptureIsMuchCheaperThanTheQuinticCapture(string targetId)
        {
            ShipState ship = targetId == "earth"
                ? ShipInMarsOrbit()
                : FlightFixture.ShipAt(_model, "gateway");
            var quinticPlanner = new TripPlanner(_model, _gravity);
            TrajectoryPlan quintic = quinticPlanner.Plan(ship, targetId);
            TrajectoryPlan fixedBurn = _planner.Plan(ship, targetId);

            PlanArc quinticCapture = quintic.ArcForPhase(FlightPhase.Capture);
            BodyRuntime target = _model.Get(targetId);
            TestContext.WriteLine($"{targetId}: quintic capture " +
                                  $"{quinticCapture.DeltaVMps / 1000.0:F1} km/s; fixed " +
                                  $"{fixedBurn.FiniteBurnDeltaVMps / 1000.0:F2} km/s, " +
                                  $"park {ParkingOrbit.FiniteBurnRadiusM(target) / 1000.0:N0} km");

            Assert.That(fixedBurn.IsFeasible, Is.True, fixedBurn.InfeasibleReason);
            Assert.That(fixedBurn.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(fixedBurn.FiniteBurnDeltaVMps, Is.LessThan(quinticCapture.DeltaVMps),
                "the fixed-thrust Oberth capture spends less than the smooth capture");
        }

        [Test]
        public void TheFixedBurnStyleParksBelowTheQuinticCeilingOnJupiter()
        {
            BodyRuntime jupiter = _model.Get("jupiter");
            Assert.That(ParkingOrbit.FiniteBurnRadiusM(jupiter),
                Is.LessThan(ParkingOrbit.RadiusM(jupiter)));
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
