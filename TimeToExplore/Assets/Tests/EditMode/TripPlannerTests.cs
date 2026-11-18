using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    public class TripPlannerTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TripPlanner _planner;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _planner = new TripPlanner(_model, _gravity);
        }

        private TrajectoryPlan PlanToMars()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            return _planner.Plan(ship, "mars");
        }

        [Test]
        public void TheDefaultArrivalStyleIsStillTheQuinticCapture()
        {
            Assert.That(_planner.ArrivalStyle, Is.EqualTo(CaptureStyle.Quintic));
        }

        [Test]
        public void AConicPlanAtAPinnedDurationLandsExactlyOnIt()
        {
            _planner.ArrivalStyle = CaptureStyle.Conic;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.Plan(ship, "mars");
            double requested = 1.5 * fastest.FlightTimeSeconds;

            TrajectoryPlan slower = _planner.PlanAtDuration(ship, "mars", requested);

            TestContext.WriteLine($"conic fastest {fastest.FlightTimeDays:F2} d at " +
                                  $"{fastest.PeakThrustMps2:F2}; pinned " +
                                  $"{slower.FlightTimeDays:F2} d at {slower.PeakThrustMps2:F2}");

            Assert.That(slower.FlightTimeSeconds, Is.EqualTo(requested).Within(1e-3));
            Assert.That(slower.IsFeasible, Is.True, slower.InfeasibleReason);
            Assert.That(slower.PeakThrustMps2, Is.LessThan(fastest.PeakThrustMps2));
            Assert.That(slower.DeltaVMps, Is.LessThan(fastest.DeltaVMps));
        }

        [Test]
        public void PlanToMarsHasAFeasibleDepartureCruiseAndCapture()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs) TestContext.WriteLine("  " + arc);

            Assert.That(plan.IsFeasible, Is.True, "the plan exceeds the design acceleration");
            Assert.That(plan.PeakThrustMps2, Is.LessThanOrEqualTo(10.0 * 1.000001));
            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[0].ReferenceBodyId, Is.EqualTo("earth"));
            Assert.That(plan.Arcs[2].ReferenceBodyId, Is.EqualTo("mars"));
            Assert.That(plan.FlightTimeDays, Is.InRange(2.0, 10.0));
            Assert.That(plan.DeltaVMps, Is.GreaterThan(0.0));
        }

        [Test]
        public void PlanStartsAtTheShipState()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            Assert.That(plan.DepartureJulianDate, Is.EqualTo(ship.JulianDate));
            MathAssert.AreClose(plan.Arcs[0].Path.PositionAt(0.0), ship.PositionM, 1e-9);
            MathAssert.AreClose(plan.Arcs[0].Path.VelocityAt(0.0), ship.VelocityMps, 1e-9);
            MathAssert.AreClose(plan.DepartureState.PositionM, ship.PositionM, 1e-9);
        }

        [Test]
        public void PlanArrivesExactlyOnTheParkingOrbit()
        {
            TrajectoryPlan plan = PlanToMars();
            BodyRuntime mars = _model.Get("mars");

            _model.ComputeState(mars, plan.ArrivalJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            Vector3d relativePosition = plan.ArrivalState.PositionM - marsPosition;
            Vector3d relativeVelocity = plan.ArrivalState.VelocityMps - marsVelocity;
            double radius = ParkingOrbit.RadiusM(mars);

            Assert.That(relativePosition.Magnitude, Is.EqualTo(radius).Within(1.0));
            Assert.That(relativeVelocity.Magnitude,
                Is.EqualTo(ParkingOrbit.SpeedMps(mars)).Within(1e-3));
            Assert.That(TwoBody.Eccentricity(relativePosition, relativeVelocity, mars.Gm),
                Is.LessThan(1e-9));
            Assert.That(Math.Abs(Vector3d.Dot(relativePosition, mars.PoleEcliptic)),
                Is.LessThan(10.0), "arrival is in the equatorial plane");
            Assert.That(plan.ArrivalState.JulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-9));
        }

        [Test]
        public void ArcsJoinContinuouslyInPositionVelocityAndAcceleration()
        {
            TrajectoryPlan plan = PlanToMars();

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];

                Assert.That(left.EndJulianDate, Is.EqualTo(right.StartJulianDate),
                    $"seam {i} is a time gap");
                MathAssert.AreClose(left.Path.PositionAt(1.0), right.Path.PositionAt(0.0), 1.0);
                MathAssert.AreClose(left.Path.VelocityAt(1.0), right.Path.VelocityAt(0.0), 1e-3);
                MathAssert.AreClose(left.Path.AccelerationAt(1.0), right.Path.AccelerationAt(0.0), 1e-6);
            }
        }

        [Test]
        public void FlightTimeSitsInsideTheKinematicEnvelope()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            _model.ComputeState(_model.Get("mars"), ship.JulianDate,
                out Vector3d marsPosition, out _);

            double distance = Vector3d.Distance(ship.PositionM, marsPosition);
            double kinematic = 2.0 * Math.Sqrt(distance / 10.0);

            TrajectoryPlan plan = _planner.Plan(ship, "mars");
            TestContext.WriteLine($"kinematic {kinematic / 3600.0:F1} h, plan " +
                                  $"{plan.FlightTimeSeconds / 3600.0:F1} h");

            Assert.That(plan.FlightTimeSeconds, Is.GreaterThan(kinematic * 0.95));
            Assert.That(plan.FlightTimeSeconds, Is.LessThan(kinematic * 4.0));
        }

        [Test]
        public void PlanToVenusIsAlsoFeasible()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "venus");

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True);
            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
            Assert.That(plan.FlightTimeDays, Is.InRange(1.0, 10.0));
            Assert.That(plan.MinimumClearanceM, Is.GreaterThan(0.0),
                "the reference path does not pass through a body");
        }

        [Test]
        public void PlanToJupiterParksAboveTheGravityCeiling()
        {
            // Jupiter's surface-radius parking orbit would sit at 19.6 m/s² --
            // twice the drive -- and no quintic capture arc can be flown there
            // however long it is given, because the curve must arrive with that
            // acceleration. The parking policy therefore lifts the orbit until
            // its gravity is 40% of the drive. See the navigation document.
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "jupiter");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs) TestContext.WriteLine("  " + arc);

            BodyRuntime jupiter = _model.Get("jupiter");
            double parkingRadius = ParkingOrbit.RadiusM(jupiter, _planner.MaxAccelerationMps2);
            double parkingGravity = jupiter.Gm / (parkingRadius * parkingRadius);

            Assert.That(plan.IsFeasible, Is.True, "Jupiter must be reachable by the single arc");
            Assert.That(plan.PeakThrustMps2, Is.LessThanOrEqualTo(10.0 * 1.000001));
            Assert.That(parkingRadius, Is.GreaterThan(jupiter.RadiusM * 2.0),
                "the orbit is pushed well clear of the surface");
            Assert.That(parkingGravity, Is.LessThanOrEqualTo(
                ParkingOrbit.GravityFractionOfMaxAcceleration * 10.0 * (1.0 + 1e-9)));
            Assert.That(parkingRadius, Is.EqualTo(177_965_000.0).Within(100_000.0),
                "Jupiter parks at 2.5 radii, 177,965 km");
            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
        }

        [Test]
        public void AStrongerDriveParksLower()
        {
            // The ceiling is a property of the drive, not the body: doubling the
            // acceleration halves the required radius.
            BodyRuntime jupiter = _model.Get("jupiter");
            double atTen = ParkingOrbit.RadiusM(jupiter, 10.0);
            double atTwenty = ParkingOrbit.RadiusM(jupiter, 20.0);

            Assert.That(atTwenty / atTen, Is.EqualTo(Math.Sqrt(0.5)).Within(1e-9));
            Assert.That(atTwenty, Is.EqualTo(125_840_000.0).Within(100_000.0));
        }

        [Test]
        public void TargetInsideItsOwnSphereOfInfluenceGivesOneArc()
        {
            // Gateway sits 54,841 km from the Moon, inside the Moon's 66,200 km
            // sphere of influence, so the trip is a single capture arc.
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "moon");

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.Arcs.Count, Is.EqualTo(1));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.IsFeasible, Is.True);
            Assert.That(plan.FlightTimeDays, Is.LessThan(1.0));
        }

        [Test]
        public void DepartureFromLowEarthOrbitNeedsADepartureArc()
        {
            ShipState ship = FlightFixture.ShipInEquatorialOrbit(_model, "earth", 400_000.0);
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs) TestContext.WriteLine("  " + arc);

            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[0].ReferenceBodyId, Is.EqualTo("earth"));
            Assert.That(plan.IsFeasible, Is.True);
            Assert.That(plan.FlightTimeDays, Is.LessThan(30.0));
        }

        [Test]
        public void APinnedDurationIsExactAndGentlerThanTheFastestPlan()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.Plan(ship, "mars");
            double requested = 2.0 * fastest.FlightTimeSeconds;

            TrajectoryPlan slower = _planner.PlanAtDuration(ship, "mars", requested);

            TestContext.WriteLine($"fastest {fastest.FlightTimeDays:F2} d at " +
                                  $"{fastest.PeakThrustMps2:F2} m/s², " +
                                  $"{fastest.DeltaVMps / 1000.0:N0} km/s; " +
                                  $"pinned {slower.FlightTimeDays:F2} d at " +
                                  $"{slower.PeakThrustMps2:F2} m/s², " +
                                  $"{slower.DeltaVMps / 1000.0:N0} km/s");

            Assert.That(slower.FlightTimeSeconds, Is.EqualTo(requested).Within(1e-3),
                "the pinned duration is the plan's own flight time");
            Assert.That(slower.DepartureJulianDate, Is.EqualTo(ship.JulianDate).Within(1e-12));
            Assert.That(slower.IsFeasible, Is.True);
            Assert.That(slower.PeakThrustMps2, Is.LessThan(fastest.PeakThrustMps2),
                "more time means a gentler burn");
            Assert.That(slower.DeltaVMps, Is.LessThan(fastest.DeltaVMps),
                "more time also means less delta-v");
            Assert.That(slower.Arcs.Count, Is.EqualTo(fastest.Arcs.Count));
        }

        [Test]
        public void APinnedDurationBelowTheMinimumIsReportedInfeasible()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.Plan(ship, "mars");

            // 95% is below the feasible minimum but still above the arcs'
            // kinematic floor, so the pinned duration is exact and the peak
            // thrust, which rises as 1/T², is over the drive.
            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mars",
                0.95 * fastest.FlightTimeSeconds);

            Assert.That(tooFast.FlightTimeSeconds,
                Is.EqualTo(0.95 * fastest.FlightTimeSeconds).Within(1e-3));
            Assert.That(tooFast.IsFeasible, Is.False,
                "the drive cannot fly the fastest plan at 95% of its time");
        }

        [Test]
        public void APinnedDurationMustBeAPositiveFiniteTime()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _planner.PlanAtDuration(ship, "mars", 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _planner.PlanAtDuration(ship, "mars", double.NaN));
        }

        [Test]
        public void PlanningIsDeterministic()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan first = _planner.Plan(ship, "mars");
            TrajectoryPlan second = _planner.Plan(ship, "mars");

            Assert.That(second.FlightTimeSeconds, Is.EqualTo(first.FlightTimeSeconds).Within(1e-9));
            Assert.That(second.PeakThrustMps2, Is.EqualTo(first.PeakThrustMps2).Within(1e-9));
        }

        [Test]
        public void PlanningDoesNotMutateTheWorldModel()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            double julianDate = _model.CurrentJulianDate;
            Vector3d earthBefore = _model.Get("earth").PositionM;

            _planner.Plan(ship, "mars");

            Assert.That(_model.CurrentJulianDate, Is.EqualTo(julianDate));
            Assert.That(Vector3d.Distance(_model.Get("earth").PositionM, earthBefore),
                Is.EqualTo(0.0));
        }

        [Test]
        public void UnknownOrMasslessTargetsAreRejected()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            Assert.Throws<ArgumentException>(() => _planner.Plan(ship, "not-a-body"));
            Assert.Throws<ArgumentException>(() => _planner.Plan(ship, "gateway"));
        }
    }
}
