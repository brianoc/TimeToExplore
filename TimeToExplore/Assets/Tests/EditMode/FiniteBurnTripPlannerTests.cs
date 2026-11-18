using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The hybrid fixed-thrust arrival end to end: the same departure and cruise
    /// as the conic plan, then a coast, one fixed-thrust capture burn to the
    /// injection point, an impulse trim, the transfer coast, and the conic
    /// circularisation. The fixed burn's acceleration is the player's level; at
    /// zero the style is the pure conic plan.
    /// </summary>
    [Category("flight")]
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
        public void AHybridPlanHasOneFiniteBurnBetweenTheConicCoasts()
        {
            TrajectoryPlan plan = PlanTo("mars");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs)
                TestContext.WriteLine("  " + arc.Phase + " " + arc +
                                      (arc.IsFiniteBurn ? " [fixed]" : arc.IsCoast ? " [coast]" : ""));

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(6));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[3].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[5].Phase, Is.EqualTo(FlightPhase.Parking));

            Assert.That(plan.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(plan.ImpulseDeltaVMps, Is.GreaterThan(0.0),
                "the trim and the circularisation are impulses");
            Assert.That(plan.FiniteBurnAccelerationMps2,
                Is.EqualTo(TripPlanner.DefaultBurnLevelG
                           * TripPlanner.StandardGravityMps2).Within(1e-6));

            foreach (PlanArc arc in plan.Arcs)
            {
                if (!arc.IsFiniteBurn) continue;
                Assert.That(arc.PeakThrustMps2,
                    Is.EqualTo(plan.FiniteBurnAccelerationMps2).Within(0.01),
                    "a finite burn is one fixed level");
            }

            Assert.That(plan.Arcs[2].PeakThrustMps2, Is.LessThan(0.01));
            Assert.That(plan.Arcs[4].PeakThrustMps2, Is.LessThan(0.01));
            Assert.That(plan.Arcs[4].EntryImpulseMps, Is.GreaterThan(0.0),
                "the transfer coast starts with the trim");
            Assert.That(plan.Arcs[5].EntryImpulseMps, Is.GreaterThan(0.0),
                "the parking coast starts with the circularisation");
        }

        [Test]
        public void TheFiniteBurnSeamsStepInsteadOfBlending()
        {
            TrajectoryPlan plan = PlanTo("mars");

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];

                Assert.That(left.EndJulianDate, Is.EqualTo(right.StartJulianDate));
                MathAssert.AreClose(left.Path.PositionAt(1.0), right.Path.PositionAt(0.0), 1.0);

                if (right.EntryImpulseMps > 0.0)
                {
                    Vector3d step = left.Path.VelocityAt(1.0)
                                    + right.EntryImpulseVectorMps
                                    - right.Path.VelocityAt(0.0);
                    Assert.That(step.Magnitude, Is.LessThan(1e-6),
                        $"seam {i}: the impulse must bridge the velocity step");
                    continue;
                }

                MathAssert.AreClose(left.Path.VelocityAt(1.0), right.Path.VelocityAt(0.0), 1.0);

                // A fixed burn meets a coast with an acceleration step of exactly
                // the burn level, not a ramp.
                if (left.IsFiniteBurn || right.IsFiniteBurn)
                {
                    double step = (right.Path.AccelerationAt(0.0)
                                   - left.Path.AccelerationAt(1.0)).Magnitude;
                    Assert.That(step,
                        Is.EqualTo(plan.FiniteBurnAccelerationMps2).Within(0.02),
                        $"seam {i}: {left.Phase} -> {right.Phase}");
                }
            }
        }

        [Test]
        public void FlyToMarsOnAHybridArrival()
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
            double radius = ParkingOrbit.ConicRadiusM(mars);
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
        public void EveryMajorTargetCanBeReachedOnAHybridArrival(string targetId)
        {
            ShipState ship = targetId == "earth"
                ? ShipInMarsOrbit()
                : FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, targetId);

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(plan.ImpulseDeltaVMps, Is.GreaterThan(0.0));

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

            double period = TwoBody.PeriodSeconds(target.Gm, radius);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(target, ship, radius, 0.03, 0.03);
        }

        [TestCase(0.3)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void EveryLevelReachesAnExactParkingOrbit(double levelG)
        {
            _planner.BurnLevelG = levelG;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FiniteBurnAccelerationMps2,
                Is.EqualTo(Math.Min(levelG * TripPlanner.StandardGravityMps2, 9.7))
                    .Within(1e-6));

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.ConicRadiusM(mars);
            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"{levelG:F1} g: arrival {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s, fixed " +
                                  $"{plan.FiniteBurnDeltaVMps / 1000.0:F2}, trim " +
                                  $"{plan.ImpulseDeltaVMps / 1000.0:F2} km/s");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(positionError, Is.LessThan(50_000.0));
            Assert.That(velocityError, Is.LessThan(20.0));
            AssertOrbitAround(mars, ship, radius, 0.02, 0.02);
        }

        [Test]
        public void ZeroLevelReproducesTheConicPlan()
        {
            _planner.BurnLevelG = 0.0;
            TrajectoryPlan hybrid = PlanTo("mars");

            var conicPlanner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            TrajectoryPlan conic = conicPlanner.Plan(
                FlightFixture.ShipAt(_model, "gateway"), "mars");

            Assert.That(hybrid.IsFeasible, Is.True, hybrid.InfeasibleReason);
            Assert.That(hybrid.FiniteBurnAccelerationMps2, Is.EqualTo(0.0));
            Assert.That(hybrid.FiniteBurnDeltaVMps, Is.EqualTo(0.0));
            Assert.That(hybrid.Arcs.Count, Is.EqualTo(conic.Arcs.Count));
            Assert.That(hybrid.ArrivalJulianDate,
                Is.EqualTo(conic.ArrivalJulianDate).Within(1e-9));
            Assert.That(hybrid.FlightTimeSeconds,
                Is.EqualTo(conic.FlightTimeSeconds).Within(1e-6));
            Assert.That(hybrid.ImpulseDeltaVMps,
                Is.EqualTo(conic.ImpulseDeltaVMps).Within(1e-6));

            for (int i = 0; i < hybrid.Arcs.Count; i++)
            {
                Assert.That(hybrid.Arcs[i].Phase, Is.EqualTo(conic.Arcs[i].Phase));
                Assert.That(hybrid.Arcs[i].IsFiniteBurn, Is.False);
            }
        }

        [TestCase("earth")]
        [TestCase("venus")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        public void TheHybridCaptureIsCheaperThanTheQuinticCapture(string targetId)
        {
            ShipState ship = targetId == "earth"
                ? ShipInMarsOrbit()
                : FlightFixture.ShipAt(_model, "gateway");
            var quinticPlanner = new TripPlanner(_model, _gravity);
            TrajectoryPlan quintic = quinticPlanner.Plan(ship, targetId);
            TrajectoryPlan fixedBurn = _planner.Plan(ship, targetId);

            PlanArc quinticCapture = quintic.ArcForPhase(FlightPhase.Capture);
            BodyRuntime target = _model.Get(targetId);
            double hybridCapture = fixedBurn.FiniteBurnDeltaVMps
                                   + fixedBurn.ImpulseDeltaVMps;
            TestContext.WriteLine($"{targetId}: quintic capture " +
                                  $"{quinticCapture.DeltaVMps / 1000.0:F1} km/s; hybrid " +
                                  $"{hybridCapture / 1000.0:F2} km/s " +
                                  $"({fixedBurn.FiniteBurnDeltaVMps / 1000.0:F2} fixed), " +
                                  $"park {ParkingOrbit.ConicRadiusM(target) / 1000.0:N0} km");

            Assert.That(fixedBurn.IsFeasible, Is.True, fixedBurn.InfeasibleReason);
            Assert.That(hybridCapture, Is.GreaterThan(0.0));
            Assert.That(hybridCapture, Is.LessThan(quinticCapture.DeltaVMps),
                "the hybrid Oberth capture spends less than the smooth capture");
        }

        [Test]
        public void TheHybridStyleParksAtTheConicRadiusOnJupiter()
        {
            BodyRuntime jupiter = _model.Get("jupiter");
            TrajectoryPlan plan = PlanTo("jupiter");

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(ParkingOrbit.ConicRadiusM(jupiter),
                Is.LessThan(ParkingOrbit.RadiusM(jupiter, 10.0)),
                "the hybrid parks below the quintic gravity ceiling");

            _model.ComputeState(jupiter, plan.ArrivalJulianDate,
                out Vector3d jupiterPosition, out Vector3d jupiterVelocity);
            double radius = Vector3d.Distance(plan.ArrivalState.PositionM, jupiterPosition);
            Assert.That(radius,
                Is.EqualTo(ParkingOrbit.ConicRadiusM(jupiter)).Within(
                    0.02 * ParkingOrbit.ConicRadiusM(jupiter)));
        }

        [Test]
        public void TheInjectionSplitReachesThePlan()
        {
            TrajectoryPlan plan = PlanTo("mars");

            PlanArc burn = null;
            PlanArc transfer = null;
            PlanArc parking = null;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.IsFiniteBurn) burn = arc;
                if (arc.Phase == FlightPhase.Coast && arc.EntryImpulseMps > 0.0)
                    transfer = arc;
                if (arc.Phase == FlightPhase.Parking) parking = arc;
            }

            Assert.That(burn, Is.Not.Null);
            Assert.That(transfer, Is.Not.Null);
            Assert.That(parking, Is.Not.Null);

            TestContext.WriteLine($"fixed {burn.DeltaVMps / 1000.0:F2} km/s, " +
                                  $"trim {transfer.EntryImpulseMps / 1000.0:F2} km/s, " +
                                  $"circ {parking.EntryImpulseMps / 1000.0:F2} km/s");

            Assert.That(plan.FiniteBurnDeltaVMps,
                Is.EqualTo(burn.DeltaVMps).Within(1e-6));
            Assert.That(plan.ImpulseDeltaVMps,
                Is.EqualTo(transfer.EntryImpulseMps + parking.EntryImpulseMps)
                    .Within(1e-6));
            Assert.That(transfer.EntryImpulseMps,
                Is.GreaterThan(parking.EntryImpulseMps),
                "the trim is the large impulse");
        }

        private ShipState ShipInMarsOrbit()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            ParkingOrbit.StateAtRadius(mars, marsPosition, marsVelocity, 0.4,
                ParkingOrbit.ConicRadiusM(mars),
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
