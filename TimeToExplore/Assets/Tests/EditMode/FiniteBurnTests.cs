using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The fixed-thrust arc and the finite capture geometry it feeds. The arc
    /// must be an honest constant-magnitude burn; the capture must clear the
    /// body and reach the higher parking radius the finite burn needs.
    /// </summary>
    public class FiniteBurnArcTests
    {
        private SolarSystemModel _model;
        private BodyRuntime _mars;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _mars = _model.Get("mars");
        }

        [Test]
        public void AFiniteBurnStartsAtItsGivenState()
        {
            var startPosition = new Vector3d(4_000_000.0, 0.0, 0.0);
            var startVelocity = new Vector3d(0.0, 3_200.0, 0.0);
            FiniteBurnArc arc = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, 600.0, startPosition, startVelocity,
                2.0, retrograde: true);

            Assert.That(arc.DurationSeconds, Is.EqualTo(600.0).Within(1e-9));
            MathAssert.AreClose(arc.PositionAt(0.0),
                _mars.PositionM + startPosition, 1e-6);
            MathAssert.AreClose(arc.VelocityAt(0.0),
                _mars.VelocityMps + startVelocity, 1e-6);
        }

        [Test]
        public void AFiniteBurnAcceleratesAtItsFixedLevelOppositeTheVelocity()
        {
            var startPosition = new Vector3d(4_000_000.0, 0.0, 0.0);
            var startVelocity = new Vector3d(0.0, 3_200.0, 0.0);
            FiniteBurnArc arc = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, 120.0, startPosition, startVelocity,
                2.5, retrograde: true);

            Vector3d localGravity = startPosition * (-_mars.Gm
                / Math.Pow(startPosition.Magnitude, 3.0));
            Vector3d rail = _model.ComputeAcceleration(_mars, _model.CurrentJulianDate);
            Vector3d expected = rail + localGravity + new Vector3d(0.0, -2.5, 0.0);
            MathAssert.AreClose(arc.AccelerationAt(0.0), expected, 1e-9);
        }

        [Test]
        public void TheTimeMappingIsTheIdentity()
        {
            FiniteBurnArc arc = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, 900.0,
                new Vector3d(4_000_000.0, 0.0, 0.0),
                new Vector3d(0.0, 3_200.0, 0.0), 2.0, retrograde: true);

            foreach (double u in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                double seconds = arc.SecondsAt(u);
                Assert.That(arc.ParameterAtSeconds(seconds), Is.EqualTo(u).Within(1e-12));
            }
        }

        [Test]
        public void AFiniteBurnMatchesTheStandaloneSimulation()
        {
            var startPosition = new Vector3d(4_000_000.0, 0.0, 0.0);
            var startVelocity = new Vector3d(0.0, 3_200.0, 0.0);
            const double duration = 720.0;
            const double acceleration = 2.0;

            FiniteBurnArc arc = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, duration, startPosition, startVelocity,
                acceleration, retrograde: true, samples: 256);
            FiniteBurnArc.Simulate(_mars, startPosition, startVelocity,
                acceleration, true, duration, 256,
                out Vector3d endPosition, out Vector3d endVelocity, out _);

            MathAssert.AreClose(arc.EndLocalPositionM, endPosition, 1e-6);
            MathAssert.AreClose(arc.EndLocalVelocityMps, endVelocity, 1e-6);
        }

        [Test]
        public void AFiniteBurnTessellatesWithinThePlanTolerance()
        {
            FiniteBurnArc arc = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, 1_200.0,
                new Vector3d(4_000_000.0, 0.0, 0.0),
                new Vector3d(0.0, 3_200.0, 0.0), 2.0, retrograde: true);

            var points = new List<Vector3d>();
            var parameters = new List<double>();
            PathTessellation.Append(arc, points, parameters);

            Assert.That(points.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(points.Count,
                Is.LessThanOrEqualTo(PathTessellation.MaxSegmentsPerArc + 1));

            for (int i = 1; i < points.Count; i++)
            {
                double chord = Vector3d.Distance(points[i - 1], points[i]);
                if (chord <= 0.0) continue;

                double u = 0.5 * (parameters[i - 1] + parameters[i]);
                Vector3d curveMid = arc.PositionAt(u);
                Vector3d chordMid = (points[i - 1] + points[i]) * 0.5;
                double ratio = Vector3d.Distance(curveMid, chordMid) / chord;
                Assert.That(ratio,
                    Is.LessThanOrEqualTo(PathTessellation.MaxSagittaRatio * (1.0 + 1e-9)));
            }
        }
    }

    /// <summary>
    /// The fixed-thrust capture: the inbound coast, the finite periapsis burn,
    /// the transfer coast, the finite circularisation, and the parking orbit.
    /// The important property is that the finite burn does not put the ship
    /// into the body on the way around.
    /// </summary>
    public class FiniteBurnCaptureTests
    {
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        private FiniteBurnCapture Build(string bodyId)
        {
            BodyRuntime target = _model.Get(bodyId);
            double soi = SphereOfInfluence.RadiusM(target);
            double parkingRadius = ParkingOrbit.FiniteBurnRadiusM(target);
            double periapsisRadius = ParkingOrbit.FiniteBurnPeriapsisRadiusM(target);

            TransferConic chain = TransferConic.Build(target,
                new Vector3d(1.0, 0.0, 0.0), soi, 5_000.0,
                parkingRadius, periapsisRadius);
            Assert.That(chain.IsValid, Is.True, chain.InvalidReason);

            ConicArc inbound = ConicArc.Create(_model, target,
                _model.CurrentJulianDate, chain.InboundSeconds,
                chain.EntryLocalPositionM, chain.EntryLocalVelocityMps);

            return FiniteBurnCapture.Build(target, inbound, parkingRadius,
                periapsisRadius, TripPlanner.DefaultFixedBurnAccelerationMps2);
        }

        [TestCase("venus")]
        [TestCase("earth")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        [TestCase("moon")]
        public void EveryMajorTargetHasASafeFixedBurnCapture(string bodyId)
        {
            BodyRuntime target = _model.Get(bodyId);
            FiniteBurnCapture capture = Build(bodyId);

            TestContext.WriteLine($"{bodyId}: burn {capture.BurnSeconds / 60.0:F1} min, " +
                                  $"post periapsis " +
                                  $"{capture.PostBurnPeriapsisRadiusM / 1000.0:N0} km " +
                                  $"(body {target.RadiusM / 1000.0:N0}), " +
                                  $"post apoapsis " +
                                  $"{capture.PostBurnApoapsisRadiusM / 1000.0:N0} km, " +
                                  $"parking {capture.ParkingRadiusM / 1000.0:N0} km, " +
                                  $"circ {capture.CircularisationSeconds:F0} s");

            Assert.That(capture.IsValid, Is.True, capture.InvalidReason);
            Assert.That(capture.BurnSeconds, Is.GreaterThan(0.0));
            Assert.That(capture.TransferSeconds, Is.GreaterThan(0.0));
            Assert.That(capture.CircularisationSeconds, Is.GreaterThan(0.0));
            Assert.That(capture.ParkingSeconds, Is.GreaterThan(0.0));

            Assert.That(capture.PostBurnPeriapsisRadiusM,
                Is.GreaterThan(target.RadiusM + FiniteBurnCapture.MinimumClearanceAltitudeM));
            Assert.That(capture.MinimumRadiusM,
                Is.GreaterThan(target.RadiusM + FiniteBurnCapture.MinimumClearanceAltitudeM));
            Assert.That(capture.PostBurnApoapsisRadiusM,
                Is.EqualTo(capture.ParkingRadiusM).Within(1_000.0));
        }

        [TestCase("venus")]
        [TestCase("earth")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        [TestCase("moon")]
        public void TheCircularisationLeavesANearlyCircularOrbit(string bodyId)
        {
            BodyRuntime target = _model.Get(bodyId);
            FiniteBurnCapture capture = Build(bodyId);
            Assert.That(capture.IsValid, Is.True, capture.InvalidReason);

            FiniteBurnArc.Simulate(target, capture.CircularisationStartLocalPositionM,
                capture.CircularisationStartLocalVelocityMps,
                TripPlanner.DefaultFixedBurnAccelerationMps2, false,
                capture.CircularisationSeconds, 512,
                out Vector3d endPosition, out Vector3d endVelocity, out _);

            double eccentricity = TwoBody.Eccentricity(endPosition, endVelocity, target.Gm);
            double radius = endPosition.Magnitude;
            TestContext.WriteLine($"{bodyId}: circularised e={eccentricity:E3}, " +
                                  $"r={radius / 1000.0:N0} km, " +
                                  $"burn {capture.CircularisationSeconds:F0} s");

            Assert.That(eccentricity, Is.LessThan(0.01),
                $"{bodyId}: e={eccentricity:E3}, r={radius / 1000.0:N0} km");
            Assert.That(radius,
                Is.EqualTo(ParkingOrbit.FiniteBurnRadiusM(target)).Within(
                    0.05 * ParkingOrbit.FiniteBurnRadiusM(target)));
        }
    }
}
