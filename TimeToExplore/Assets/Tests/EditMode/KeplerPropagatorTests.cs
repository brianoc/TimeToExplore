using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The state-vector Kepler propagator, the conic elements it feeds, and
    /// the coast arc that uses them. These are the maths the transfer-conic
    /// capture stands on, checked against the existing elements-to-state path
    /// and against the textbook apsidal values.
    /// </summary>
    [Category("flight")]
    public class KeplerPropagatorTests
    {
        private const double SunMu = 1.32712440018e20;
        private const double EarthMu = 3.986004418e14;

        private static OrbitalElements Elements(double eccentricity)
        {
            return new OrbitalElements
            {
                semiMajorAxisM = 1.0e11,
                eccentricity = eccentricity,
                inclinationRad = 0.0,
                ascendingNodeRad = 0.0,
                argumentOfPeriapsisRad = 0.0,
                meanAnomalyRad = 0.3,
                epochJulianDate = JulianDate.J2000,
                gravitationalParameter = SunMu,
                parentEquatorFrame = false
            };
        }

        [TestCase(0.0)]
        [TestCase(0.3)]
        [TestCase(0.7)]
        public void EllipticPropagationMatchesElementsToState(double eccentricity)
        {
            OrbitalElements elements = Elements(eccentricity);
            elements.GetState(JulianDate.J2000, out Vector3d position, out Vector3d velocity);

            double fraction = 0.37;
            double dt = fraction * elements.PeriodSeconds;
            KeplerPropagator.Propagate(position, velocity, SunMu, dt,
                out Vector3d propagatedPosition, out Vector3d propagatedVelocity);

            elements.GetState(JulianDate.J2000 + dt / JulianDate.SecondsPerDay,
                out Vector3d expectedPosition, out Vector3d expectedVelocity);

            TestContext.WriteLine($"e={eccentricity}: position error " +
                                  $"{Vector3d.Distance(propagatedPosition, expectedPosition):E2} m");

            Assert.That(Vector3d.Distance(propagatedPosition, expectedPosition),
                Is.LessThan(10.0), "universal variables match the elliptic propagator");
            Assert.That((propagatedVelocity - expectedVelocity).Magnitude,
                Is.LessThan(1e-3));
        }

        /// <summary>
        /// The parabolic branch of <see cref="ConicElements"/> is Barker's
        /// equation, checked against the universal-variable propagator at the same
        /// radius. The old branch carried an extra √2 in the time scale, which only
        /// showed up once a genuinely parabolic escape started using it: the
        /// forward and inverse maps agreed with each other but not with a
        /// propagation.
        /// </summary>
        [TestCase(2.0)]
        [TestCase(10.0)]
        [TestCase(100.0)]
        public void ParabolicPropagationMatchesTheBarkerTime(double multiple)
        {
            double radius = 7.0e6;
            double periapsisSpeed = Math.Sqrt(2.0 * EarthMu / radius);
            var position = new Vector3d(radius, 0.0, 0.0);
            var velocity = new Vector3d(0.0, periapsisSpeed, 0.0);

            ConicElements elements = ConicElements.FromState(position, velocity, EarthMu);
            Assert.That(elements.IsNearParabolic, Is.True,
                $"the state is parabolic, not e={elements.Eccentricity:F9}");

            double targetRadius = multiple * radius;
            double cos = (elements.SemiLatusRectumM / targetRadius - 1.0)
                         / elements.Eccentricity;
            double anomaly = Math.Acos(cos);
            double seconds = elements.TimeSincePeriapsis(anomaly);

            KeplerPropagator.Propagate(position, velocity, EarthMu, seconds,
                out Vector3d propagatedPosition, out _);

            TestContext.WriteLine($"{multiple} r_p: t {seconds:F3} s " +
                                  $"({seconds / JulianDate.SecondsPerDay:F6} d), " +
                                  $"propagated {propagatedPosition.Magnitude / 1000.0:F3} km " +
                                  $"against {targetRadius / 1000.0:F3} km");

            Assert.That(propagatedPosition.Magnitude,
                Is.EqualTo(targetRadius).Within(1.0),
                "Barker's time reaches the same radius the propagator does");
            Assert.That(elements.TrueAnomalyAtTime(seconds),
                Is.EqualTo(anomaly).Within(1e-9),
                "the inverse Barker map is consistent with the forward one");
        }

        [Test]
        public void HyperbolicPropagationReachesTheAnalyticPeriapsis()
        {
            var position = new Vector3d(1.0e7, 0.0, 0.0);
            var velocity = new Vector3d(-3_000.0, 12_000.0, 0.0);

            ConicElements elements = ConicElements.FromState(position, velocity, EarthMu);
            Assert.That(elements.IsHyperbolic, Is.True);

            double timeToPeriapsis = -elements.TimeSincePeriapsisSeconds;
            Assert.That(timeToPeriapsis, Is.GreaterThan(0.0));

            KeplerPropagator.Propagate(position, velocity, EarthMu, timeToPeriapsis,
                out Vector3d periapsis, out Vector3d periapsisVelocity);

            double radius = periapsis.Magnitude;
            double expectedSpeed = Math.Sqrt(
                2.0 * EarthMu / elements.PeriapsisRadiusM
                + EarthMu / Math.Abs(elements.SemiMajorAxisM));

            TestContext.WriteLine($"e={elements.Eccentricity:F5}, " +
                                  $"r_p={elements.PeriapsisRadiusM / 1000.0:F1} km, " +
                                  $"propagated {radius / 1000.0:F1} km");

            Assert.That(radius, Is.EqualTo(elements.PeriapsisRadiusM).Within(0.01));
            Assert.That(periapsisVelocity.Magnitude, Is.EqualTo(expectedSpeed).Within(1e-3));
            Assert.That(Vector3d.Dot(periapsis, periapsisVelocity),
                Is.EqualTo(0.0).Within(1e-3), "periapsis velocity is transverse");
        }

        [Test]
        public void ForwardAndBackwardPropagationReturnTheState()
        {
            OrbitalElements elements = Elements(0.6);
            elements.GetState(JulianDate.J2000, out Vector3d position, out Vector3d velocity);

            KeplerPropagator.Propagate(position, velocity, SunMu, 2.0e6,
                out Vector3d forwardPosition, out Vector3d forwardVelocity);
            KeplerPropagator.Propagate(forwardPosition, forwardVelocity, SunMu, -2.0e6,
                out Vector3d backPosition, out Vector3d backVelocity);

            Assert.That(Vector3d.Distance(backPosition, position), Is.LessThan(1.0));
            Assert.That((backVelocity - velocity).Magnitude, Is.LessThan(1e-6));
        }

        [Test]
        public void EnergyAndAngularMomentumSurviveManyPropagations()
        {
            OrbitalElements elements = Elements(0.4);
            elements.GetState(JulianDate.J2000, out Vector3d position, out Vector3d velocity);

            double energy = 0.5 * velocity.SqrMagnitude - SunMu / position.Magnitude;
            double angularMomentum = Vector3d.Cross(position, velocity).Magnitude;
            double step = elements.PeriodSeconds / 40.0;

            for (int i = 0; i < 40; i++)
            {
                KeplerPropagator.Propagate(position, velocity, SunMu, step,
                    out position, out velocity);

                double currentEnergy = 0.5 * velocity.SqrMagnitude - SunMu / position.Magnitude;
                double currentAngularMomentum = Vector3d.Cross(position, velocity).Magnitude;

                Assert.That(currentEnergy, Is.EqualTo(energy).Within(Math.Abs(energy) * 1e-9),
                    $"energy after {i + 1} steps");
                Assert.That(currentAngularMomentum, Is.EqualTo(angularMomentum).Within(
                    angularMomentum * 1e-9), $"angular momentum after {i + 1} steps");
            }
        }

        [Test]
        public void ConicElementsRoundTripThroughTheState()
        {
            var position = new Vector3d(7.0e6, 2.0e6, -1.0e6);
            var velocity = new Vector3d(-3_000.0, 9_000.0, 1_500.0);

            ConicElements elements = ConicElements.FromState(position, velocity, EarthMu);
            elements.StateAtTrueAnomaly(elements.TrueAnomalyRad,
                out Vector3d roundTripPosition, out Vector3d roundTripVelocity);

            Assert.That(Vector3d.Distance(roundTripPosition, position), Is.LessThan(1e-6));
            Assert.That((roundTripVelocity - velocity).Magnitude, Is.LessThan(1e-9));
        }

        [Test]
        public void ConicArcIsACircularOrbitAtTheExpectedState()
        {
            SolarSystemModel model = CatalogueFixture.LoadModel();
            BodyRuntime mars = model.Get("mars");
            double radius = 5.0e6;
            double speed = TwoBody.CircularSpeedMps(mars.Gm, radius);
            Vector3d localPosition = mars.EquatorX * radius;
            Vector3d localVelocity = mars.EquatorY * speed;

            double start = model.CurrentJulianDate;
            double duration = 600.0;
            ConicArc arc = ConicArc.Create(model, mars, start, duration,
                localPosition, localVelocity);

            for (int i = 0; i <= 10; i++)
            {
                double u = i / 10.0;
                arc.LocalStateAt(u, out Vector3d r, out Vector3d v);
                Assert.That(r.Magnitude, Is.EqualTo(radius).Within(1e-6),
                    $"radius at u={u}");
                Assert.That(v.Magnitude, Is.EqualTo(speed).Within(1e-6),
                    $"speed at u={u}");
            }

            Assert.That(arc.SecondsAt(0.0), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(arc.SecondsAt(1.0), Is.EqualTo(duration).Within(1e-6));
            Assert.That(arc.ParameterAtSeconds(duration * 0.5), Is.EqualTo(0.5).Within(1e-6));
        }

        [Test]
        public void ConicArcReferenceAccelerationIsTheTwoBodyAcceleration()
        {
            SolarSystemModel model = CatalogueFixture.LoadModel();
            BodyRuntime mars = model.Get("mars");
            double radius = 5.0e6;
            double speed = TwoBody.CircularSpeedMps(mars.Gm, radius);
            Vector3d localPosition = mars.EquatorX * radius;
            Vector3d localVelocity = mars.EquatorY * speed;

            ConicArc arc = ConicArc.Create(model, mars, model.CurrentJulianDate, 600.0,
                localPosition, localVelocity);

            for (int i = 0; i <= 4; i++)
            {
                double u = i / 4.0;
                arc.LocalStateAt(u, out Vector3d r, out _);
                Vector3d expected = r * (-mars.Gm / Math.Pow(r.Magnitude, 3.0));
                Vector3d localAcceleration = arc.AccelerationAt(u)
                                             - model.ComputeAcceleration(mars,
                                                 model.CurrentJulianDate + u * 600.0
                                                 / JulianDate.SecondsPerDay);

                MathAssert.AreClose(localAcceleration, expected, 1e-9);
            }
        }
    }
}
