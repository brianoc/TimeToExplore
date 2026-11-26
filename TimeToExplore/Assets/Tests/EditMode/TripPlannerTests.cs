using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    [Category("flight")]
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
        public void TheDefaultArrivalStyleIsTheConicCapture()
        {
            Assert.That(_planner.ArrivalStyle, Is.EqualTo(CaptureStyle.Conic));
        }

        [Test]
        public void TheFixedBurnStyleUsesTheDefaultLevelAndTheConicParkingRadius()
        {
            _planner.ArrivalStyle = CaptureStyle.FiniteBurn;
            TrajectoryPlan plan = PlanToMars();

            BodyRuntime mars = _model.Get("mars");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(_planner.BurnLevelG, Is.EqualTo(TripPlanner.DefaultBurnLevelG));
            Assert.That(_planner.FixedBurnAccelerationMps2,
                Is.EqualTo(TripPlanner.DefaultBurnLevelG
                           * TripPlanner.StandardGravityMps2).Within(1e-12));
            Assert.That(plan.FiniteBurnAccelerationMps2,
                Is.EqualTo(_planner.FixedBurnAccelerationMps2).Within(1e-6));
            Assert.That(ParkingOrbit.ConicRadiusM(mars),
                Is.LessThanOrEqualTo(ParkingOrbit.RadiusM(mars, 10.0)),
                "the hybrid never parks above the quintic ceiling");

            BodyRuntime jupiter = _model.Get("jupiter");
            Assert.That(ParkingOrbit.ConicRadiusM(jupiter),
                Is.LessThan(ParkingOrbit.RadiusM(jupiter, 10.0)),
                "the hybrid parks far below the quintic gravity ceiling");
        }

        [Test]
        public void TheBurnLevelIsClampedToTheControlRangeAndTheDrive()
        {
            _planner.BurnLevelG = 2.0;
            Assert.That(_planner.BurnLevelG, Is.EqualTo(1.0));
            Assert.That(_planner.FixedBurnAccelerationMps2,
                Is.EqualTo(9.7).Within(1e-9),
                "1 g is clamped to the drive's thrust margin");

            _planner.BurnLevelG = -1.0;
            Assert.That(_planner.BurnLevelG,
                Is.EqualTo(TripPlanner.MinimumBurnLevelG));
            Assert.That(_planner.FixedBurnAccelerationMps2,
                Is.EqualTo(TripPlanner.MinimumBurnLevelG
                           * TripPlanner.StandardGravityMps2).Within(1e-12),
                "the profile always has two real burns");
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
            Assert.That(slower.PeakThrustMps2,
                Is.LessThanOrEqualTo(fastest.PeakThrustMps2),
                "the break holds the commanded level; a longer trip is not gentler than it");
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
            Assert.That(plan.Arcs.Count, Is.EqualTo(8));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[0].ReferenceBodyId, Is.EqualTo("earth"));
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].ReferenceBodyId,
                Is.EqualTo("mars"));
            Assert.That(plan.FlightTimeDays, Is.InRange(6.5, 9.0));
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
            double radius = ParkingOrbit.ConicRadiusM(mars);

            Assert.That(relativePosition.Magnitude, Is.EqualTo(radius).Within(1.0));
            Assert.That(relativeVelocity.Magnitude,
                Is.EqualTo(TwoBody.CircularSpeedMps(mars.Gm, radius)).Within(1e-3));
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
                MathAssert.AreClose(left.Path.PositionAt(1.0), right.Path.PositionAt(0.0),
                    1_000.0);
                MathAssert.AreClose(left.Path.VelocityAt(1.0) + right.EntryImpulseVectorMps,
                    right.Path.VelocityAt(0.0), 1.0);
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

            // The escape climb is a physical lower bound the kinematic estimate
            // does not model: the parabolic climb alone is about 10.7 days.
            Assert.That(plan.FlightTimeSeconds, Is.GreaterThan(kinematic * 0.95));
            Assert.That(plan.FlightTimeSeconds,
                Is.LessThan(kinematic * 4.0 + 12.0 * JulianDate.SecondsPerDay));
        }

        [Test]
        public void PlanToVenusIsAlsoFeasible()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "venus");

            TestContext.WriteLine(plan.ToString());
            Assert.That(plan.IsFeasible, Is.True);
            Assert.That(plan.Arcs.Count, Is.InRange(8, 9),
                "the approach leg depends on whether the certified floor's brake starts inside the ceiling");
            Assert.That(plan.FlightTimeDays, Is.InRange(3.5, 6.0));
            Assert.That(plan.MinimumClearanceM, Is.GreaterThan(0.0),
                "the reference path does not pass through a body");
        }

        [Test]
        public void PlanToJupiterParksOnTheConicRadius()
        {
            // The conic capture has no gravity ceiling: the impulsive periapsis
            // burn can fly a deep well, so Jupiter parks at the conic radius
            // rather than at the quintic capture's lifted orbit.
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.Plan(ship, "jupiter");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs) TestContext.WriteLine("  " + arc);

            BodyRuntime jupiter = _model.Get("jupiter");
            double parkingRadius = ParkingOrbit.ConicRadiusM(jupiter);

            Assert.That(plan.IsFeasible, Is.True, "Jupiter must be reachable");
            Assert.That(plan.PeakThrustMps2, Is.LessThanOrEqualTo(10.0 * 1.000001));
            Assert.That(parkingRadius, Is.LessThan(
                ParkingOrbit.RadiusM(jupiter, _planner.MaxAccelerationMps2)),
                "the conic orbit is below the quintic gravity ceiling");
            Assert.That(plan.Arcs.Count, Is.EqualTo(8));
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
            // A 0.3 g drive cannot lift a 400 km Earth orbit with the short
            // break (the local gravity is 8.7 m/s² against 2.94 m/s² of thrust),
            // so this case runs at the drive's own level.
            _planner.BurnLevelG = 1.0;
            ShipState ship = FlightFixture.ShipInEquatorialOrbit(_model, "earth", 400_000.0);
            TrajectoryPlan plan = _planner.Plan(ship, "mars");

            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs) TestContext.WriteLine("  " + arc);

            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[0].ReferenceBodyId, Is.EqualTo("earth"));
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FlightTimeDays, Is.LessThan(60.0));
        }

        [Test]
        public void AWeakDriveCannotUseTheShortBreakFromLowEarthOrbit()
        {
            // A 400 km Earth orbit has 8.7 m/s² of local gravity against 2.94 m/s²
            // of thrust at 0.3 g: the short break cannot lift it, and the refusal
            // names the level it cannot hold. The same orbit at 1 g builds.
            _planner.BurnLevelG = 0.3;
            ShipState ship = FlightFixture.ShipInEquatorialOrbit(_model, "earth", 400_000.0);
            TrajectoryPlan refused = _planner.PlanAtDuration(ship, "mars",
                60.0 * JulianDate.SecondsPerDay);

            TestContext.WriteLine("weak LEO: " + refused.InfeasibleReason);
            Assert.That(refused.IsFeasible, Is.False,
                "0.3 g cannot fly the short break from low Earth orbit");
            Assert.That(refused.InfeasibleReason, Does.Contain("2.942 m/s²"),
                "the reason names the level the break cannot hold");
            Assert.That(refused.InfeasibleReason, Does.Contain("break"),
                "the reason names the arc that is over the drive");

            _planner.BurnLevelG = 1.0;
            TrajectoryPlan built = _planner.PlanAtDuration(ship, "mars",
                60.0 * JulianDate.SecondsPerDay);
            Assert.That(built.IsFeasible, Is.True, built.InfeasibleReason);
            Assert.That(built.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(built.Arcs[0].ReferenceBodyId, Is.EqualTo("earth"));
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
            Assert.That(slower.PeakThrustMps2,
                Is.EqualTo(fastest.PeakThrustMps2).Within(0.01),
                "the commanded-level break is the same peak on both plans");
            Assert.That(slower.DeltaVMps, Is.LessThan(fastest.DeltaVMps),
                "more time also means less delta-v");
            Assert.That(slower.Arcs.Count, Is.GreaterThanOrEqualTo(fastest.Arcs.Count),
                "a slower plan may add the planetocentric approach leg");
        }

        [Test]
        public void APinnedDurationBelowTheMinimumIsReportedInfeasible()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.Plan(ship, "mars");

            // Half the fastest trip is below the split-aware floor: the climb and
            // the burns cannot fit, and the build reports the refusal rather than
            // a stretched plan.
            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mars",
                0.5 * fastest.FlightTimeSeconds);

            Assert.That(tooFast.IsFeasible, Is.False,
                "the drive cannot fly the fastest plan at half its time");
            Assert.That(tooFast.InfeasibleReason, Is.Not.Empty,
                "the refusal carries a cause");
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
        public void UnknownTargetsAreRejected()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            Assert.Throws<ArgumentException>(() => _planner.Plan(ship, "not-a-body"));

            // A station is no longer a "massless target" to refuse: it is a point
            // target with its own plan. The genuinely massless non-station
            // refusal is in StationTripPlannerTests.
            Assert.DoesNotThrow(() =>
                _planner.PlanAtDuration(ship, "ares_junction",
                    120.0 * JulianDate.SecondsPerDay));
        }
    }
}
