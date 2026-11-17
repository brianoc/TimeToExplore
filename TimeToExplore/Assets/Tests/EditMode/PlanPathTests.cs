using System.Collections.Generic;
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

        [Test]
        public void AStraightArcStaysOneChord()
        {
            Quintic path = Quintic.Create(
                new Vector3d(0.0, 0.0, 0.0), new Vector3d(10.0, 0.0, 0.0), Vector3d.Zero,
                new Vector3d(100.0, 0.0, 0.0), new Vector3d(10.0, 0.0, 0.0), Vector3d.Zero,
                10.0);

            var points = new List<Vector3d>();
            PathTessellation.Append(path, points);

            Assert.That(points.Count, Is.EqualTo(2),
                "adaptive sampling must not subdivide a perfectly straight arc");
        }

        [Test]
        public void EveryAdaptiveChordMeetsTheFlatnessTolerance()
        {
            foreach (PlanArc arc in _plan.Arcs)
            {
                var points = new List<Vector3d>();
                var parameters = new List<double>();
                PathTessellation.Append(arc.Path, points, parameters);

                TestContext.WriteLine(
                    $"{arc.Phase}: {points.Count} points, worst sagitta " +
                    $"{WorstSagittaRatio(arc.Path, points, parameters):E2}");

                Assert.That(points.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(points.Count,
                    Is.LessThanOrEqualTo(PathTessellation.MaxSegmentsPerArc + 1));
                Assert.That(Vector3d.Distance(points[0], arc.Path.PositionAt(0.0)),
                    Is.EqualTo(0.0).Within(1e-9));
                Assert.That(Vector3d.Distance(points[points.Count - 1], arc.Path.PositionAt(1.0)),
                    Is.EqualTo(0.0).Within(1e-9));
                Assert.That(WorstSagittaRatio(arc.Path, points, parameters),
                    Is.LessThanOrEqualTo(PathTessellation.MaxSagittaRatio * (1.0 + 1e-9)));
            }
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

        /// <summary>
        /// Largest midpoint deviation over chord length across the sampled
        /// polyline. The parameters let the test evaluate the true curve at the
        /// same place the sampler tested it.
        /// </summary>
        private static double WorstSagittaRatio(Quintic path, List<Vector3d> points,
            List<double> parameters)
        {
            double worst = 0.0;

            for (int i = 1; i < points.Count; i++)
            {
                double chord = Vector3d.Distance(points[i - 1], points[i]);
                if (chord <= 0.0) continue;

                double u = 0.5 * (parameters[i - 1] + parameters[i]);
                Vector3d curveMid = path.PositionAt(u);
                Vector3d chordMid = (points[i - 1] + points[i]) * 0.5;
                double ratio = Vector3d.Distance(curveMid, chordMid) / chord;
                if (ratio > worst) worst = ratio;
            }

            return worst;
        }
    }
}
