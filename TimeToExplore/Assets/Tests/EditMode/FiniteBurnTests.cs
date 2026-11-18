using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The fixed-thrust arc and the hybrid capture geometry it feeds. The arc
    /// must be an honest constant-magnitude burn; the capture must end the burn
    /// at a safe injection radius, trim the end state onto the exact transfer
    /// ellipse, and keep the parking radius at the conic value.
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
        public void TheRadiusCrossingMatchesAPostHocSimulation()
        {
            var startPosition = new Vector3d(4_000_000.0, 0.0, 0.0);
            var startVelocity = new Vector3d(0.0, 3_200.0, 0.0);
            const double targetRadius = 3_700_000.0;
            const double acceleration = 2.0;

            Assert.That(FiniteBurnArc.SimulateUntilRadius(_mars, startPosition,
                startVelocity, acceleration, true, targetRadius, 2_000.0,
                out double crossing, out Vector3d endPosition,
                out Vector3d endVelocity), Is.True);

            Assert.That(crossing, Is.GreaterThan(0.0));
            Assert.That(endPosition.Magnitude,
                Is.EqualTo(targetRadius).Within(100.0));
            FiniteBurnArc.Simulate(_mars, startPosition, startVelocity,
                acceleration, true, crossing, 4096, out _, out _, out double minimum);
            Assert.That(minimum, Is.LessThanOrEqualTo(targetRadius + 1.0));
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
    /// The hybrid capture: the inbound coast, one finite capture burn to the
    /// injection radius, the trim onto the transfer ellipse, the transfer coast,
    /// and the conic circularisation. The important properties are that the
    /// burn clears the body, the trim reaches the exact transfer, and the
    /// parking radius is the conic one at every level.
    /// </summary>
    public class HybridCaptureTests
    {
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        private HybridCapture Build(string bodyId, double levelG)
        {
            BodyRuntime target = _model.Get(bodyId);
            double soi = SphereOfInfluence.RadiusM(target);
            double parkingRadius = ParkingOrbit.ConicRadiusM(target);
            double periapsisRadius = ParkingOrbit.PeriapsisRadiusM(target);

            TransferConic chain = TransferConic.Build(target,
                new Vector3d(1.0, 0.0, 0.0), soi, 5_000.0,
                parkingRadius, periapsisRadius);
            Assert.That(chain.IsValid, Is.True, chain.InvalidReason);

            ConicArc inbound = ConicArc.Create(_model, target,
                _model.CurrentJulianDate, chain.InboundSeconds,
                chain.EntryLocalPositionM, chain.EntryLocalVelocityMps);

            return HybridCapture.Build(target, inbound, parkingRadius,
                periapsisRadius,
                Math.Min(levelG * TripPlanner.StandardGravityMps2, 9.7));
        }

        [TestCase("venus")]
        [TestCase("earth")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        [TestCase("moon")]
        public void EveryMajorTargetHasASafeHybridCapture(string bodyId)
        {
            BodyRuntime target = _model.Get(bodyId);
            HybridCapture capture = Build(bodyId, TripPlanner.DefaultBurnLevelG);

            TestContext.WriteLine($"{bodyId}: burn {capture.BurnSeconds / 60.0:F1} min, " +
                                  $"injection {capture.InjectionRadiusM / 1000.0:N0} km, " +
                                  $"parking {capture.ParkingRadiusM / 1000.0:N0} km, " +
                                  $"fixed {capture.FiniteDeltaVMps / 1000.0:F2} km/s, " +
                                  $"trim {capture.TrimDeltaVMps / 1000.0:F2} km/s, " +
                                  $"circ {capture.CircularisationDeltaVMps / 1000.0:F2} km/s");

            Assert.That(capture.IsValid, Is.True, capture.InvalidReason);
            Assert.That(capture.BurnSeconds, Is.GreaterThan(0.0));
            Assert.That(capture.TransferSeconds, Is.GreaterThan(0.0));
            Assert.That(capture.ParkingSeconds, Is.GreaterThan(0.0));

            Assert.That(capture.InjectionRadiusM, Is.LessThan(capture.ParkingRadiusM),
                "the injection point is below the parking radius");
            Assert.That(capture.MinimumRadiusM,
                Is.GreaterThan(target.RadiusM + HybridCapture.MinimumClearanceAltitudeM),
                "the whole burn clears the body");
            Assert.That(capture.ApoapsisLocalPositionM.Magnitude,
                Is.EqualTo(capture.ParkingRadiusM).Within(1_000.0),
                "the transfer ellipse reaches the parking radius");
        }

        [TestCase("venus")]
        [TestCase("earth")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        [TestCase("moon")]
        public void TheTrimLandsOnTheExactTransferEllipse(string bodyId)
        {
            HybridCapture capture = Build(bodyId, TripPlanner.DefaultBurnLevelG);
            Assert.That(capture.IsValid, Is.True, capture.InvalidReason);

            ConicElements transfer = ConicElements.FromState(
                capture.BurnEndLocalPositionM, capture.TransferStartLocalVelocityMps,
                _model.Get(bodyId).Gm);

            TestContext.WriteLine($"{bodyId}: transfer e={transfer.Eccentricity:F4}, " +
                                  $"peri {transfer.PeriapsisRadiusM / 1000.0:N0} km, " +
                                  $"apo {transfer.ApoapsisRadiusM / 1000.0:N0} km");

            Assert.That(transfer.IsElliptic, Is.True);
            Assert.That(transfer.PeriapsisRadiusM,
                Is.EqualTo(capture.InjectionRadiusM).Within(1.0));
            Assert.That(transfer.ApoapsisRadiusM,
                Is.EqualTo(capture.ParkingRadiusM).Within(1_000.0));

            double expected = (capture.TransferStartLocalVelocityMps
                               - capture.BurnEndLocalVelocityMps).Magnitude;
            Assert.That(capture.TrimDeltaVMps, Is.EqualTo(expected).Within(1e-6));
        }

        [TestCase(0.3)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void TheBurnClearsTheBodyAndTheParkingRadiusStaysConic(double levelG)
        {
            BodyRuntime mars = _model.Get("mars");
            HybridCapture capture = Build("mars", levelG);
            Assert.That(capture.IsValid, Is.True, capture.InvalidReason);

            TestContext.WriteLine($"{levelG:F1} g: fixed {capture.FiniteDeltaVMps / 1000.0:F2} km/s, " +
                                  $"trim {capture.TrimDeltaVMps / 1000.0:F2} km/s, " +
                                  $"burn {capture.BurnSeconds:F0} s, " +
                                  $"injection {capture.InjectionRadiusM / 1000.0:N0} km");

            Assert.That(capture.AccelerationMps2,
                Is.EqualTo(Math.Min(levelG * TripPlanner.StandardGravityMps2, 9.7))
                    .Within(1e-9));
            Assert.That(capture.MinimumRadiusM,
                Is.GreaterThan(mars.RadiusM + HybridCapture.MinimumClearanceAltitudeM));
            Assert.That(capture.ParkingRadiusM,
                Is.EqualTo(ParkingOrbit.ConicRadiusM(mars)).Within(1e-9));
            Assert.That(capture.TrimDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(capture.CircularisationDeltaVMps, Is.GreaterThan(0.0));
        }

        [Test]
        public void AStrongerBurnLeavesASmallerTrimOnMars()
        {
            HybridCapture gentle = Build("mars", 0.3);
            HybridCapture strong = Build("mars", 1.0);

            TestContext.WriteLine($"0.3 g trim {gentle.TrimDeltaVMps / 1000.0:F2}, " +
                                  $"1.0 g trim {strong.TrimDeltaVMps / 1000.0:F2}");

            Assert.That(gentle.IsValid, Is.True, gentle.InvalidReason);
            Assert.That(strong.IsValid, Is.True, strong.InvalidReason);
            Assert.That(strong.TrimDeltaVMps, Is.LessThan(gentle.TrimDeltaVMps),
                "a stronger fixed burn does more of the capture");
            Assert.That(strong.BurnSeconds, Is.LessThan(gentle.BurnSeconds));
        }

        [TestCase("venus")]
        [TestCase("earth")]
        [TestCase("mars")]
        [TestCase("jupiter")]
        [TestCase("saturn")]
        [TestCase("moon")]
        public void TheCircularisationLeavesACircularParkingOrbit(string bodyId)
        {
            BodyRuntime target = _model.Get(bodyId);
            HybridCapture capture = Build(bodyId, TripPlanner.DefaultBurnLevelG);
            Assert.That(capture.IsValid, Is.True, capture.InvalidReason);

            ConicElements parked = ConicElements.FromState(
                capture.ApoapsisLocalPositionM, capture.ParkingLocalVelocityMps,
                target.Gm);

            TestContext.WriteLine($"{bodyId}: parked e={parked.Eccentricity:E3}, " +
                                  $"r={parked.PeriapsisRadiusM / 1000.0:N0} km");

            Assert.That(parked.IsElliptic, Is.True);
            Assert.That(parked.Eccentricity, Is.LessThan(1e-3));
            Assert.That(parked.PeriapsisRadiusM,
                Is.EqualTo(ParkingOrbit.ConicRadiusM(target)).Within(1_000.0));
        }
    }
}
