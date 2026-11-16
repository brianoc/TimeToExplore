using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The two things the trajectory line and the warp policy need from a plan:
    /// where the arc boundaries are, and where the ship turns over.
    /// </summary>
    public class PlanPathTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TrajectoryPlan _plan;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            var planner = new TripPlanner(_model, _gravity);
            _plan = planner.Plan(FlightFixture.ShipAt(_model, "gateway"), "mars");
        }

        [Test]
        public void NextBoundaryWalksTheArcChain()
        {
            Assert.That(_plan.Arcs.Count, Is.EqualTo(3));
            Assert.That(_plan.NextBoundaryJulianDate(_plan.DepartureJulianDate),
                Is.EqualTo(_plan.Arcs[0].EndJulianDate));
            Assert.That(_plan.NextBoundaryJulianDate(_plan.Arcs[0].EndJulianDate),
                Is.EqualTo(_plan.Arcs[1].EndJulianDate));
            Assert.That(_plan.NextBoundaryJulianDate(_plan.Arcs[1].EndJulianDate),
                Is.EqualTo(_plan.Arcs[2].EndJulianDate));
        }

        [Test]
        public void ThereIsNoBoundaryAfterTheArrival()
        {
            Assert.That(double.IsPositiveInfinity(
                _plan.NextBoundaryJulianDate(_plan.ArrivalJulianDate + 1.0)), Is.True);
        }

        [Test]
        public void TheFlipLiesBetweenDepartureAndArrival()
        {
            double flip = _plan.FindFlipJulianDate(_gravity);

            TestContext.WriteLine($"flip at {JulianDate.Format(flip)}, " +
                                  $"{(flip - _plan.DepartureJulianDate) * JulianDate.SecondsPerDay / 3600.0:F1} h in");

            Assert.That(double.IsNaN(flip), Is.False, "a Mars plan accelerates then decelerates");
            Assert.That(flip, Is.GreaterThan(_plan.DepartureJulianDate));
            Assert.That(flip, Is.LessThan(_plan.ArrivalJulianDate));
        }

        [Test]
        public void TheFlipIsWhereTheAlongTrackThrustChangesSign()
        {
            double flip = _plan.FindFlipJulianDate(_gravity);
            const double tenMinutes = 600.0 / JulianDate.SecondsPerDay;

            double before = AlongTrackThrust(_plan, flip - tenMinutes);
            double after = AlongTrackThrust(_plan, flip + tenMinutes);

            TestContext.WriteLine($"along-track thrust: {before:E3} then {after:E3}");
            Assert.That(before, Is.GreaterThan(0.0));
            Assert.That(after, Is.LessThan(0.0));
        }

        private double AlongTrackThrust(TrajectoryPlan plan, double julianDate)
        {
            Vector3d position = plan.ReferencePositionAt(julianDate);
            Vector3d velocity = plan.ReferenceVelocityAt(julianDate);
            Vector3d required = plan.ReferenceAccelerationAt(julianDate);

            _gravity.Prepare(position, julianDate);
            Vector3d thrust = required - _gravity.AccelerationAt(position, julianDate);
            return Vector3d.Dot(velocity, thrust);
        }
    }
}
