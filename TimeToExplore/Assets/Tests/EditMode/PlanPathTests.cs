using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The two things the trajectory line and the warp policy need from a plan:
    /// where the arc boundaries are, and where the ship turns over.
    /// </summary>
    [Category("flight")]
    public class PlanPathTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TrajectoryPlan _plan;
        private TrajectoryPlan _conicPlan;
        private TrajectoryPlan _fixedBurnPlan;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            var planner = new TripPlanner(_model, _gravity);
            _plan = planner.Plan(FlightFixture.ShipAt(_model, "gateway"), "mars");

            var conicPlanner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            _conicPlan = conicPlanner.Plan(FlightFixture.ShipAt(_model, "gateway"), "mars");

            var fixedPlanner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.FiniteBurn
            };
            _fixedBurnPlan = fixedPlanner.Plan(FlightFixture.ShipAt(_model, "gateway"), "mars");
        }

        [Test]
        public void NextBoundaryWalksTheArcChain()
        {
            Assert.That(_plan.Arcs.Count, Is.EqualTo(8));
            for (int i = 0; i < _plan.Arcs.Count - 1; i++)
            {
                Assert.That(_plan.NextBoundaryJulianDate(_plan.Arcs[i].StartJulianDate),
                    Is.EqualTo(_plan.Arcs[i].EndJulianDate),
                    $"arc {i} ends at the next boundary");
            }
            Assert.That(_plan.NextBoundaryJulianDate(
                    _plan.Arcs[_plan.Arcs.Count - 1].StartJulianDate),
                Is.EqualTo(_plan.Arcs[_plan.Arcs.Count - 1].EndJulianDate));
        }

        [Test]
        public void ThereIsNoBoundaryAfterTheArrival()
        {
            Assert.That(double.IsPositiveInfinity(
                _plan.NextBoundaryJulianDate(_plan.ArrivalJulianDate + 1.0)), Is.True);
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

        [Test]
        public void ConicArcsMeetTheSameFlatnessTolerance()
        {
            foreach (PlanArc arc in _conicPlan.Arcs)
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
                Assert.That(WorstSagittaRatio(arc.Path, points, parameters),
                    Is.LessThanOrEqualTo(PathTessellation.MaxSagittaRatio * (1.0 + 1e-9)));
            }
        }

        [Test]
        public void FixedBurnArcsMeetTheSameFlatnessTolerance()
        {
            foreach (PlanArc arc in _fixedBurnPlan.Arcs)
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
                Assert.That(WorstSagittaRatio(arc.Path, points, parameters),
                    Is.LessThanOrEqualTo(PathTessellation.MaxSagittaRatio * (1.0 + 1e-9)));
            }
        }

        [Test]
        public void TheFixedBurnBoundariesAreOneBurnAndTheTwoImpulses()
        {
            int finite = 0;
            int impulses = 0;
            foreach (PlanArc arc in _fixedBurnPlan.Arcs)
            {
                if (arc.IsFiniteBurn) finite++;
                if (arc.EntryImpulseMps > 0.0) impulses++;
            }

            Assert.That(finite, Is.EqualTo(3),
                "the departure burn, the brake, and the hybrid capture burn");
            Assert.That(impulses, Is.EqualTo(2), "the trim and the circularisation");
            Assert.That(_fixedBurnPlan.ImpulseDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(_fixedBurnPlan.FiniteBurnDeltaVMps, Is.GreaterThan(0.0));
        }

        [Test]
        public void TheCoastSamplesPeriapsisMoreDenselyThanTheFarApproach()
        {
            var planner = new TripPlanner(_model, _gravity);
            TrajectoryPlan slow = planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "mars",
                90.0 * JulianDate.SecondsPerDay);
            PlanArc approach = slow.ArcForPhase(FlightPhase.Approach);
            Assert.That(approach, Is.Not.Null);

            double far = Vector3d.Distance(approach.Path.PositionAt(0.0),
                approach.Path.PositionAt(0.01));
            double near = Vector3d.Distance(approach.Path.PositionAt(0.99),
                approach.Path.PositionAt(1.0));

            TestContext.WriteLine($"far chord {far / 1000.0:N0} km, " +
                                  $"periapsis chord {near / 1000.0:N0} km");

            Assert.That(near, Is.LessThan(far / 10.0),
                "true-anomaly sampling must not cut the periapsis corner");
        }

        [Test]
        public void TheConicBoundariesAreThePeriapsisAndTheCircularisation()
        {
            PlanArc coast = _conicPlan.Arcs.First(arc =>
                arc.Phase == FlightPhase.Coast && arc.ReferenceBodyId == "mars");
            PlanArc capture = _conicPlan.Arcs.First(arc =>
                arc.Phase == FlightPhase.Capture && arc.ReferenceBodyId == "mars");
            PlanArc parking = _conicPlan.ArcForPhase(FlightPhase.Parking);

            Assert.That(_conicPlan.NextBoundaryJulianDate(coast.StartJulianDate),
                Is.EqualTo(coast.EndJulianDate), "the periapsis is a boundary");
            Assert.That(_conicPlan.NextBoundaryJulianDate(capture.StartJulianDate),
                Is.EqualTo(capture.EndJulianDate), "the circularisation is a boundary");
            Assert.That(_conicPlan.NextBoundaryJulianDate(parking.StartJulianDate),
                Is.EqualTo(parking.EndJulianDate), "the arrival is a boundary");
        }

        /// <summary>
        /// Largest midpoint deviation over chord length across the sampled
        /// polyline. The parameters let the test evaluate the true curve at the
        /// same place the sampler tested it.
        /// </summary>
        private static double WorstSagittaRatio(IArcPath path, List<Vector3d> points,
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
