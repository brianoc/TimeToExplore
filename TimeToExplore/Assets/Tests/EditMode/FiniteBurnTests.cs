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
    [Category("flight")]
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
        public void TheFullFieldFormsMatchTheArcAndItsRadiusCrossing()
        {
            var gravity = FlightFixture.FullField(_model);
            double julianDate = _model.CurrentJulianDate;
            var startPosition = new Vector3d(4_000_000.0, 0.0, 0.0);
            var startVelocity = new Vector3d(0.0, 3_200.0, 0.0);
            const double duration = 720.0;
            const double acceleration = 2.0;
            const int samples = 256;

            FiniteBurnArc arc = FiniteBurnArc.Create(_model, _mars, julianDate, duration,
                startPosition, startVelocity, acceleration, retrograde: true,
                samples: samples, gravity: gravity);
            FiniteBurnArc.Simulate(_model, gravity, _mars, julianDate, startPosition,
                startVelocity, acceleration, true, duration, samples,
                out Vector3d endPosition, out Vector3d endVelocity, out _);

            MathAssert.AreClose(arc.EndLocalPositionM, endPosition, 1e-6);
            MathAssert.AreClose(arc.EndLocalVelocityMps, endVelocity, 1e-6);

            const double targetRadius = 3_700_000.0;
            Assert.That(FiniteBurnArc.SimulateUntilRadius(_model, gravity, _mars,
                julianDate, startPosition, startVelocity, acceleration, true,
                targetRadius, 2_000.0, out double crossing, out Vector3d crossingPosition,
                out _), Is.True);
            Assert.That(crossing, Is.GreaterThan(0.0).And.LessThan(2_000.0));
            Assert.That(crossingPosition.Magnitude,
                Is.EqualTo(targetRadius).Within(100.0),
                "the summed-field crossing is measured against the reference body");
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

        /// <summary>
        /// A full-field burn may cross a sphere of influence, and it must be the
        /// summed field that is integrated there: one arc, no split at the
        /// boundary. The arc is checked against an independent fine-step RK4 of
        /// the same field and thrust, so a silently two-body arc — or one that
        /// switched model at the boundary — fails.
        /// </summary>
        [Test]
        public void ABurnCrossingTheSphereOfInfluenceCloses()
        {
            var gravity = FlightFixture.FullField(_model);
            BodyRuntime earth = _model.Get("earth");
            double julianDate = _model.CurrentJulianDate;
            _model.ComputeState(earth, julianDate,
                out Vector3d earthPosition, out Vector3d earthVelocity);

            double soi = SphereOfInfluence.RadiusM(earth);
            var radial = new Vector3d(1.0, 0.0, 0.0);
            var tangential = new Vector3d(0.0, 0.0, 1.0);
            var startPosition = earthPosition + radial * 800_000_000.0;
            var startVelocity = earthVelocity + radial * 2_500.0 + tangential * 1_000.0;
            const double duration = 80_000.0;
            const double acceleration = 0.5;

            FiniteBurnArc arc = FiniteBurnArc.Create(_model, earth, julianDate, duration,
                startPosition - earthPosition, startVelocity - earthVelocity,
                acceleration, retrograde: false, samples: 512,
                fixedThrustDirection: radial, gravity: gravity);

            Assert.That(arc.UsesFullField, Is.True);
            Assert.That(arc.Gravity, Is.SameAs(gravity));

            // The burn really crosses the boundary: it starts well inside and
            // ends well outside, with no seam in between.
            double endJulianDate = julianDate + duration / JulianDate.SecondsPerDay;
            _model.ComputeState(earth, endJulianDate, out Vector3d earthAtEnd, out _);
            double startRadius = Vector3d.Distance(startPosition, earthPosition);
            double endRadius = Vector3d.Distance(arc.PositionAt(1.0), earthAtEnd);
            TestContext.WriteLine($"start {startRadius / 1e6:F0} Mm inside the " +
                                  $"{soi / 1e6:F0} Mm sphere, end {endRadius / 1e6:F0} Mm");
            Assert.That(startRadius, Is.LessThan(soi));
            Assert.That(endRadius, Is.GreaterThan(2.0 * soi),
                "the burn crosses the sphere of influence");

            // Independent fine-step RK4 through the same field and thrust.
            Vector3d position = startPosition;
            Vector3d velocity = startVelocity;
            const int steps = 20_000;
            double h = duration / steps;
            for (int i = 0; i < steps; i++)
            {
                double t = julianDate + i * h / JulianDate.SecondsPerDay;
                gravity.Prepare(position, t);
                Vector3d a1 = gravity.AccelerationAt(position, t) + radial * acceleration;
                Vector3d p2 = position + velocity * (0.5 * h);
                Vector3d v2 = velocity + a1 * (0.5 * h);
                Vector3d a2 = gravity.AccelerationAt(p2, t + 0.5 * h / JulianDate.SecondsPerDay)
                              + radial * acceleration;
                Vector3d p3 = position + v2 * (0.5 * h);
                Vector3d v3 = velocity + a2 * (0.5 * h);
                Vector3d a3 = gravity.AccelerationAt(p3, t + 0.5 * h / JulianDate.SecondsPerDay)
                              + radial * acceleration;
                Vector3d p4 = position + v3 * h;
                Vector3d v4 = velocity + a3 * h;
                Vector3d a4 = gravity.AccelerationAt(p4, t + h / JulianDate.SecondsPerDay)
                              + radial * acceleration;
                position += (velocity + v2 * 2.0 + v3 * 2.0 + v4) * (h / 6.0);
                velocity += (a1 + a2 * 2.0 + a3 * 2.0 + a4) * (h / 6.0);
            }

            double positionError = Vector3d.Distance(arc.PositionAt(1.0), position);
            double velocityError = (arc.VelocityAt(1.0) - velocity).Magnitude;
            TestContext.WriteLine($"arc against a 20,000-step RK4: " +
                                  $"{positionError:E3} m, {velocityError:E3} m/s");
            Assert.That(positionError, Is.LessThan(0.05),
                "the arc matches an independent integration of the summed field");
            Assert.That(velocityError, Is.LessThan(1e-6));

            // The acceleration at a sample is the field plus the thrust, and not
            // the two-body value: a burn that quietly ignored the field fails.
            const double u = 0.5;
            Vector3d samplePosition = arc.PositionAt(u);
            double sampleJulianDate = julianDate
                                      + arc.SecondsAt(u) / JulianDate.SecondsPerDay;
            gravity.Prepare(samplePosition, sampleJulianDate);
            Vector3d fieldAcceleration = gravity.AccelerationAt(samplePosition, sampleJulianDate);
            Vector3d arcAcceleration = arc.AccelerationAt(u);

            MathAssert.AreClose(arcAcceleration,
                fieldAcceleration + radial * acceleration, 1e-9);

            FiniteBurnArc twoBody = FiniteBurnArc.Create(_model, earth, julianDate, duration,
                startPosition - earthPosition, startVelocity - earthVelocity,
                acceleration, retrograde: false, samples: 512,
                fixedThrustDirection: radial);
            double twoBodyDifference = (arcAcceleration - twoBody.AccelerationAt(u)).Magnitude;
            TestContext.WriteLine($"acceleration against the two-body value: " +
                                  $"{twoBodyDifference:E3} m/s²");
            Assert.That(twoBodyDifference, Is.GreaterThan(1e-6),
                "the arc is not the two-body burn: the summed field's third bodies show");
        }

        /// <summary>
        /// A short burn close to one body is where the two models must agree: the
        /// summed field reduces to the reference body's local gravity plus its
        /// rail, and every other term is negligible. The inertial states are
        /// compared because a body-relative position also carries the catalogue's
        /// Julian-date quantisation, about 0.2 m at Mars.
        /// </summary>
        [Test]
        public void AShortBurnNearOneBodyAgreesWithTheTwoBodyBurn()
        {
            var gravity = FlightFixture.FullField(_model);
            var startPosition = new Vector3d(4_000_000.0, 0.0, 0.0);
            var startVelocity = new Vector3d(0.0, 3_200.0, 0.0);
            const double duration = 120.0;
            const double acceleration = 2.5;

            FiniteBurnArc twoBody = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, duration, startPosition, startVelocity,
                acceleration, retrograde: true, samples: 512);
            FiniteBurnArc fullField = FiniteBurnArc.Create(_model, _mars,
                _model.CurrentJulianDate, duration, startPosition, startVelocity,
                acceleration, retrograde: true, samples: 512, gravity: gravity);

            Assert.That(twoBody.UsesFullField, Is.False);
            Assert.That(fullField.UsesFullField, Is.True);

            double positionError = Vector3d.Distance(
                fullField.PositionAt(1.0), twoBody.PositionAt(1.0));
            double velocityError = (fullField.VelocityAt(1.0) - twoBody.VelocityAt(1.0)).Magnitude;
            TestContext.WriteLine($"120 s at 4,000 km: {positionError:E3} m, " +
                                  $"{velocityError:E3} m/s");

            // The physics differ only by the third bodies: at Mars the summed
            // field is the rail plus Mars's own local gravity to 3e-7 m/s².
            Vector3d absolute = _mars.PositionM + startPosition;
            gravity.Prepare(absolute, _model.CurrentJulianDate);
            Vector3d summed = gravity.AccelerationAt(absolute, _model.CurrentJulianDate);
            double radius = startPosition.Magnitude;
            Vector3d localGravity = startPosition
                * (-_mars.Gm / (radius * radius * radius));
            Vector3d twoBodyField = _model.ComputeAcceleration(_mars, _model.CurrentJulianDate)
                                    + localGravity;
            double fieldDifference = (summed - twoBodyField).Magnitude;
            TestContext.WriteLine($"field against the two-body field: {fieldDifference:E3} m/s²");
            Assert.That(fieldDifference, Is.LessThan(1e-5),
                "the omitted terms are negligible at this radius");

            Assert.That(velocityError, Is.LessThan(1e-4),
                "the two models agree to a tenth of a millimetre per second");
            Assert.That(positionError, Is.LessThan(1.0),
                "and to a metre of position");
        }

        /// <summary>
        /// The reference body is a label: it names the frame the state is
        /// reported in and the ephemeris used to decompose it, not the physics.
        /// Two burns over one inertial state with one thrust follow the same
        /// inertial path, and their local states differ by exactly the two
        /// bodies' states.
        /// </summary>
        [Test]
        public void TheReferenceBodyIsALabel()
        {
            var gravity = FlightFixture.FullField(_model);
            BodyRuntime sun = _model.Get("sun");
            double julianDate = _model.CurrentJulianDate;
            _model.ComputeState(_mars, julianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);

            var absolutePosition = marsPosition + new Vector3d(20_000_000.0, 0.0, 0.0);
            var absoluteVelocity = marsVelocity + new Vector3d(0.0, 3_000.0, 0.0);
            var thrust = new Vector3d(1.0, 0.0, 0.0);
            const double duration = 400.0;

            FiniteBurnArc aboutSun = FiniteBurnArc.Create(_model, sun, julianDate,
                duration, absolutePosition, absoluteVelocity, 1.0, retrograde: false,
                samples: 512, fixedThrustDirection: thrust, gravity: gravity);
            FiniteBurnArc aboutMars = FiniteBurnArc.Create(_model, _mars, julianDate,
                duration, absolutePosition - marsPosition, absoluteVelocity - marsVelocity,
                1.0, retrograde: false, samples: 512,
                fixedThrustDirection: thrust, gravity: gravity);

            Assert.That(aboutSun.Centre, Is.SameAs(sun));
            Assert.That(aboutMars.Centre, Is.SameAs(_mars));

            foreach (double u in new[] { 0.25, 0.5, 1.0 })
            {
                double path = Vector3d.Distance(
                    aboutSun.PositionAt(u), aboutMars.PositionAt(u));
                TestContext.WriteLine($"u {u:F2}: inertial path difference {path:E3} m");
                Assert.That(path, Is.LessThan(1e-6),
                    "one label is not a different trajectory");
            }

            double localJulianDate = julianDate
                + aboutSun.SecondsAt(0.5) / JulianDate.SecondsPerDay;
            _model.ComputeState(sun, localJulianDate, out Vector3d sunAt, out _);
            _model.ComputeState(_mars, localJulianDate, out Vector3d marsAt, out _);
            aboutSun.LocalStateAt(0.5, out Vector3d sunLocal, out _);
            aboutMars.LocalStateAt(0.5, out Vector3d marsLocal, out _);

            Vector3d offset = marsLocal - sunLocal;
            Vector3d expected = sunAt - marsAt;
            TestContext.WriteLine($"local decomposition differs by {offset.Magnitude:E3} m " +
                                  $"against the two bodies' {expected.Magnitude:E3} m; " +
                                  $"residual {(offset - expected).Magnitude:E3} m");
            Assert.That((offset - expected).Magnitude, Is.LessThan(1e-6),
                "the local states differ by exactly the two bodies' states");
        }
    }

    /// <summary>
    /// The hybrid capture: the inbound coast, one finite capture burn to the
    /// injection radius, the trim onto the transfer ellipse, the transfer coast,
    /// and the conic circularisation. The important properties are that the
    /// burn clears the body, the trim reaches the exact transfer, and the
    /// parking radius is the conic one at every level.
    /// </summary>
    [Category("flight")]
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
