using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The coast cruise as the Go To planner builds and flies it: a quintic
    /// break out of the departure sphere of influence, a heliocentric Lambert
    /// conic with the drive off, and the conic or hybrid capture chain. The
    /// powered cruise stays the default and is unchanged; these tests cover the
    /// alternative and the switch between them.
    /// </summary>
    [Category("flight")]
    public class CoastTripPlannerTests
    {
        private const double CoastDays = 200.0;

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
                CruiseStyle = CruiseStyle.Coast
            };
        }

        private TrajectoryPlan CoastTo(string targetId, double days) =>
            _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"), targetId,
                days * JulianDate.SecondsPerDay);

        private static void PrintPlan(TrajectoryPlan plan)
        {
            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs)
            {
                TestContext.WriteLine($"  {arc.Phase} {arc.Path.GetType().Name} " +
                                      $"{arc.DurationSeconds / JulianDate.SecondsPerDay:F2} d, " +
                                      $"peak {arc.PeakThrustMps2:F4} m/s², " +
                                      $"dv {arc.DeltaVMps:N0} m/s, " +
                                      $"impulse {arc.EntryImpulseMps:N0} m/s");
            }
        }

        [Test]
        public void ACoastPlanIsAQuinticBreakAHeliocentricCoastAndTheConicCapture()
        {
            TrajectoryPlan plan = CoastTo("mars", CoastDays);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(5));

            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[0].Path, Is.InstanceOf<Quintic>());

            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[1].Path, Is.InstanceOf<ConicArc>());
            var coast = (ConicArc)plan.Arcs[1].Path;
            Assert.That(coast.Centre, Is.SameAs(_model.Sun),
                "the coast is a heliocentric conic about the Sun");
            Assert.That(coast.Elements.IsHyperbolic, Is.False,
                "a 200-day interplanetary coast is an ellipse, not the powered cruise");
            Assert.That(plan.Arcs[1].ReferenceBodyId, Is.EqualTo("sun"));

            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Parking));
            Assert.That(plan.Arcs[2].IsCoast, Is.True);
            Assert.That(plan.Arcs[3].IsCoast, Is.True);
            Assert.That(plan.Arcs[4].IsCoast, Is.True);

            // The capture is conic even though the planner's default arrival
            // style is the quintic capture: a coast cannot feed a quintic.
            Assert.That(plan.Arcs[3].EntryImpulseMps, Is.GreaterThan(0.0));
            Assert.That(plan.ImpulseDeltaVMps, Is.GreaterThan(0.0));
        }

        [Test]
        public void TheCoastCoastsWithTheDriveOff()
        {
            TrajectoryPlan plan = CoastTo("mars", CoastDays);
            PlanArc coast = plan.Arcs[1];

            TestContext.WriteLine($"departure peak {plan.Arcs[0].PeakThrustMps2:F4} m/s², " +
                                  $"coast peak {coast.PeakThrustMps2:E3} m/s², " +
                                  $"coast dv {coast.DeltaVMps:F1} m/s");

            // The coast's reference is two-body about the Sun, so the only
            // measured thrust is the third-body correction: near zero, and far
            // below the departure break's own thrust.
            Assert.That(coast.PeakThrustMps2, Is.LessThan(0.01));
            Assert.That(coast.DeltaVMps, Is.LessThan(1_000.0));
            Assert.That(coast.PeakThrustMps2, Is.LessThan(plan.Arcs[0].PeakThrustMps2));
        }

        [Test]
        public void TheDepartureIsRetargetedToTheLambertDepartureVelocity()
        {
            TrajectoryPlan plan = CoastTo("mars", CoastDays);
            PlanArc departure = plan.Arcs[0];
            PlanArc cruise = plan.Arcs[1];

            Vector3d exit = departure.Path.PositionAt(1.0);
            Vector3d entry = cruise.Path.PositionAt(1.0);
            MathAssert.AreClose(exit, cruise.Path.PositionAt(0.0), 1.0);
            MathAssert.AreClose(departure.Path.VelocityAt(1.0),
                cruise.Path.VelocityAt(0.0), 1e-6);
            MathAssert.AreClose(departure.Path.AccelerationAt(1.0),
                cruise.Path.AccelerationAt(0.0), 1e-6);

            LambertSolution solution = LambertSolver.SolveShortWay(
                exit, entry, cruise.DurationSeconds, _model.Sun.Gm);
            Assert.That(solution.HasSolution, Is.True);

            TestContext.WriteLine($"transfer angle {solution.TransferAngleRad * 180.0 / Math.PI:F1} deg, " +
                                  $"v1 {solution.DepartureVelocityMps.Magnitude / 1000.0:F2} km/s, " +
                                  $"v2 {solution.ArrivalVelocityMps.Magnitude / 1000.0:F2} km/s");

            MathAssert.AreClose(solution.DepartureVelocityMps,
                cruise.Path.VelocityAt(0.0), 1.0);
            MathAssert.AreClose(solution.ArrivalVelocityMps,
                cruise.Path.VelocityAt(1.0), 1.0);
        }

        [Test]
        public void ACoastPlanFliesToTheParkingOrbitAndHoldsIt()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "mars",
                CoastDays * JulianDate.SecondsPerDay);
            PrintPlan(plan);
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

            double period = ParkingOrbit.PeriodSeconds(mars);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(mars, ship, radius, 0.03, 0.03);
        }

        [Test]
        public void ACoastCostsOrdersOfMagnitudeLessThanThePoweredCruise()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            var poweredPlanner = new TripPlanner(_model, _gravity);
            TrajectoryPlan powered = poweredPlanner.Plan(ship, "mars");
            TrajectoryPlan coast = _planner.PlanAtDuration(ship, "mars",
                CoastDays * JulianDate.SecondsPerDay);

            TestContext.WriteLine($"powered {powered.FlightTimeDays:F2} d, " +
                                  $"{powered.DeltaVMps / 1000.0:N0} km/s, " +
                                  $"peak {powered.PeakThrustMps2:F2} m/s²");
            TestContext.WriteLine($"coast   {coast.FlightTimeDays:F2} d, " +
                                  $"{coast.DeltaVMps / 1000.0:N0} km/s, " +
                                  $"break {coast.Arcs[0].DeltaVMps / 1000.0:F1} km/s, " +
                                  $"trim {coast.Arcs[2].EntryImpulseMps / 1000.0:F1} km/s");

            Assert.That(powered.IsFeasible, Is.True, powered.InfeasibleReason);
            Assert.That(coast.IsFeasible, Is.True, coast.InfeasibleReason);
            Assert.That(coast.FlightTimeSeconds, Is.GreaterThan(powered.FlightTimeSeconds),
                "the cheap crossing is the slow one");
            Assert.That(coast.DeltaVMps, Is.LessThan(powered.DeltaVMps / 100.0),
                "the coast replaces the torch's continuous thrust with a free fall");
        }

        [Test]
        public void ThePlannerFindsACoastTimeOnItsOwn()
        {
            TrajectoryPlan plan = _planner.Plan(FlightFixture.ShipAt(_model, "gateway"), "mars");
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(5));
            Assert.That(plan.Arcs[1].Path, Is.InstanceOf<ConicArc>());
            Assert.That(plan.FlightTimeDays, Is.GreaterThan(5.0),
                "a coast is not the torch plan's few-day sprint");
        }

        [Test]
        public void SwitchingTheCruiseStyleAloneDoesNotMoveTheShipAtDeparture()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            double duration = CoastDays * JulianDate.SecondsPerDay;

            var poweredPlanner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            TrajectoryPlan powered = poweredPlanner.PlanAtDuration(ship, "mars", duration);
            TrajectoryPlan coast = _planner.PlanAtDuration(ship, "mars", duration);

            foreach (TrajectoryPlan plan in new[] { powered, coast })
            {
                Assert.That(plan.DepartureJulianDate, Is.EqualTo(ship.JulianDate));
                MathAssert.AreClose(plan.DepartureState.PositionM, ship.PositionM, 1e-6);
                MathAssert.AreClose(plan.DepartureState.VelocityMps, ship.VelocityMps, 1e-9);
                MathAssert.AreClose(plan.Arcs[0].Path.PositionAt(0.0), ship.PositionM, 1e-6);
                MathAssert.AreClose(plan.Arcs[0].Path.VelocityAt(0.0), ship.VelocityMps, 1e-9);
            }

            Assert.That(powered.IsFeasible, Is.True, powered.InfeasibleReason);
            Assert.That(coast.IsFeasible, Is.True, coast.InfeasibleReason);
            Assert.That(coast.Arcs[1].Path, Is.InstanceOf<ConicArc>());
            Assert.That(powered.Arcs[1].Path, Is.InstanceOf<Quintic>());
        }

        [Test]
        public void ACoastTimeWithoutAProgradeShortTransferIsRejectedWithAReason()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "mars",
                400.0 * JulianDate.SecondsPerDay);

            TestContext.WriteLine("reason: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.False);
            Assert.That(plan.InfeasibleReason, Does.Contain("short-way"));
            Assert.That(plan.InfeasibleReason, Does.Contain("180"));
        }

        [Test]
        public void ACoastCanHandOverToTheHybridCapture()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                CruiseStyle = CruiseStyle.Coast,
                ArrivalStyle = CaptureStyle.FiniteBurn,
                BurnLevelG = 0.3
            };
            TrajectoryPlan plan = planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"),
                "mars", CoastDays * JulianDate.SecondsPerDay);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs[1].Path, Is.InstanceOf<ConicArc>());
            Assert.That(plan.FiniteBurnAccelerationMps2, Is.GreaterThan(0.0));

            bool hasFiniteBurn = false;
            foreach (PlanArc arc in plan.Arcs)
                hasFiniteBurn |= arc.IsFiniteBurn;

            Assert.That(hasFiniteBurn, Is.True, "the hybrid arrival keeps its fixed burn");
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].Phase, Is.EqualTo(FlightPhase.Parking));
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
