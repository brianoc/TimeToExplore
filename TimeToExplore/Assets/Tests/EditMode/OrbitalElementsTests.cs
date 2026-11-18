using System;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    [Category("core")]
    public class OrbitalElementsTests
    {
        private const double Mu = 1.32712440018e20;
        private const double A = 1.0e11;

        private static OrbitalElements Elements(double eccentricity, double meanAnomalyRad = 0.0)
        {
            return new OrbitalElements
            {
                semiMajorAxisM = A,
                eccentricity = eccentricity,
                inclinationRad = 0.0,
                ascendingNodeRad = 0.0,
                argumentOfPeriapsisRad = 0.0,
                meanAnomalyRad = meanAnomalyRad,
                epochJulianDate = JulianDate.J2000,
                gravitationalParameter = Mu,
                parentEquatorFrame = false
            };
        }

        [Test]
        public void CircularOrbitHasConstantRadiusAndCircularSpeed()
        {
            OrbitalElements elements = Elements(0.0);
            elements.GetState(JulianDate.J2000, out Vector3d position, out Vector3d velocity);

            Assert.That(position.Magnitude, Is.EqualTo(A).Within(1e-6));
            Assert.That(velocity.Magnitude, Is.EqualTo(Math.Sqrt(Mu / A)).Within(1e-6));
            Assert.That(elements.PeriodSeconds,
                Is.EqualTo(OrbitMath.TwoPi * Math.Sqrt(A * A * A / Mu)).Within(1e-6));
        }

        [Test]
        public void PeriapsisAndApoapsisFollowTheEllipseGeometry()
        {
            const double e = 0.5;
            OrbitalElements elements = Elements(e);

            elements.GetState(JulianDate.J2000, out Vector3d periapsis, out Vector3d periapsisVelocity);
            elements.GetState(
                JulianDate.J2000 + elements.PeriodSeconds / 2.0 / JulianDate.SecondsPerDay,
                out Vector3d apoapsis, out Vector3d apoapsisVelocity);

            Assert.That(periapsis.x, Is.EqualTo(A * (1.0 - e)).Within(1e-6));
            Assert.That(apoapsis.x, Is.EqualTo(-A * (1.0 + e)).Within(1e-6));
            Assert.That(periapsisVelocity.Magnitude,
                Is.EqualTo(Math.Sqrt(Mu * (1.0 + e) / (A * (1.0 - e)))).Within(1e-6));
            Assert.That(apoapsisVelocity.Magnitude,
                Is.EqualTo(Math.Sqrt(Mu * (1.0 - e) / (A * (1.0 + e)))).Within(1e-6));
        }

        [Test]
        public void SpecificEnergyAndAngularMomentumAreConserved()
        {
            const double e = 0.6;
            OrbitalElements elements = Elements(e);
            double expectedEnergy = -Mu / (2.0 * A);
            double expectedAngularMomentum = Math.Sqrt(Mu * A * (1.0 - e * e));

            for (int i = 0; i < 16; i++)
            {
                double time = elements.PeriodSeconds * i / 16.0 / JulianDate.SecondsPerDay;
                elements.GetState(JulianDate.J2000 + time, out Vector3d position, out Vector3d velocity);

                double radius = position.Magnitude;
                double energy = 0.5 * velocity.SqrMagnitude - Mu / radius;
                double angularMomentum = Vector3d.Cross(position, velocity).Magnitude;

                Assert.That(energy, Is.EqualTo(expectedEnergy).Within(Math.Abs(expectedEnergy) * 1e-10),
                    $"energy at sample {i}");
                Assert.That(angularMomentum,
                    Is.EqualTo(expectedAngularMomentum).Within(expectedAngularMomentum * 1e-10),
                    $"angular momentum at sample {i}");
            }
        }

        [Test]
        public void StateAfterOnePeriodMatchesTheStartingState()
        {
            const double e = 0.75;
            OrbitalElements elements = Elements(e);
            elements.GetState(JulianDate.J2000, out Vector3d p0, out Vector3d v0);
            elements.GetState(
                JulianDate.J2000 + elements.PeriodSeconds / JulianDate.SecondsPerDay,
                out Vector3d p1, out Vector3d v1);

            MathAssert.AreClose(p1, p0, A * 1e-9);
            MathAssert.AreClose(v1, v0, Math.Sqrt(Mu / A) * 1e-9);
        }

        [Test]
        public void SampledEllipseIsClosedAndSatisfiesTheFocusProperty()
        {
            const double e = 0.5;
            const int samples = 128;
            OrbitalElements elements = Elements(e);
            var points = new Vector3d[samples + 1];

            elements.SampleEllipse(samples, points);

            MathAssert.AreClose(points[0], points[samples], 1e-3);
            // In this parameterisation the primary focus is the origin; the
            // centre of the ellipse is at -a e and the second focus at -2a e.
            var focusA = Vector3d.Zero;
            var focusB = new Vector3d(-2.0 * A * e, 0, 0);
            foreach (Vector3d point in points)
            {
                double sum = Vector3d.Distance(point, focusA) + Vector3d.Distance(point, focusB);
                Assert.That(sum, Is.EqualTo(2.0 * A).Within(A * 1e-9));
            }
        }

        [Test]
        public void TooSmallSampleBufferIsRejected()
        {
            OrbitalElements elements = Elements(0.1);
            var buffer = new Vector3d[4];
            Assert.Throws<ArgumentException>(() => elements.SampleEllipse(4, buffer));
        }

        [Test]
        public void InclinedOrbitLeavesTheReferencePlane()
        {
            OrbitalElements elements = Elements(0.1, Math.PI / 2.0);
            elements.inclinationRad = Math.PI / 4.0;
            elements.GetState(JulianDate.J2000, out Vector3d position, out _);

            Assert.That(Math.Abs(position.z), Is.GreaterThan(0.0));
            Assert.That(position.Magnitude, Is.InRange(A * 0.9, A * 1.1));
        }
    }
}
