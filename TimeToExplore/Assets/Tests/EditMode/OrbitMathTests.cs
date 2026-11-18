using System;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    [Category("core")]
    public class OrbitMathTests
    {
        [TestCase(0.0, 0.0)]
        [TestCase(Math.PI, Math.PI)]
        [TestCase(-Math.PI, Math.PI)]
        [TestCase(3.0 * Math.PI, Math.PI)]
        [TestCase(-2.0 * Math.PI, 0.0)]
        [TestCase(5.0 * Math.PI / 2.0, Math.PI / 2.0)]
        public void WrapTwoPiReturnsTheCanonicalAngle(double input, double expected)
        {
            Assert.That(OrbitMath.WrapTwoPi(input), Is.EqualTo(expected).Within(1e-12));
        }

        [TestCase(0.0, 0.0)]
        [TestCase(Math.PI, Math.PI)]
        [TestCase(-Math.PI, Math.PI)]
        [TestCase(3.0 * Math.PI / 2.0, -Math.PI / 2.0)]
        [TestCase(-3.0 * Math.PI / 2.0, Math.PI / 2.0)]
        public void WrapPiReturnsTheSignedShortestAngle(double input, double expected)
        {
            Assert.That(OrbitMath.WrapPi(input), Is.EqualTo(expected).Within(1e-12));
        }

        [TestCase(0.0)]
        [TestCase(0.1)]
        [TestCase(0.5)]
        [TestCase(0.75)]
        [TestCase(0.95)]
        [TestCase(0.999)]
        public void SolveKeplerSatisfiesKeplersEquation(double eccentricity)
        {
            for (int i = 0; i < 100; i++)
            {
                double meanAnomaly = OrbitMath.TwoPi * i / 100.0;
                double eccentricAnomaly = OrbitMath.SolveKepler(meanAnomaly, eccentricity);

                Assert.That(eccentricAnomaly,
                    Is.InRange(-1e-6, OrbitMath.TwoPi + 1e-6),
                    "Newton may overshoot a boundary by a rounding step");
                double residual = eccentricAnomaly
                                  - eccentricity * Math.Sin(eccentricAnomaly)
                                  - meanAnomaly;
                Assert.That(Math.Abs(residual), Is.LessThan(1e-10),
                    $"e={eccentricity}, M={meanAnomaly}");
            }
        }

        [Test]
        public void LagrangeL1AndL2MatchTheRestrictedThreeBodySolution()
        {
            // Earth-Moon mass ratio, unit separation, primary at the origin.
            const double mu = 0.012155650403206972;
            var primary = Vector3d.Zero;
            var secondary = new Vector3d(1, 0, 0);
            var velocity = new Vector3d(0, 1, 0);

            Vector3d l1 = OrbitMath.ComputeLagrangePoint(
                primary, 1.0, secondary, mu / (1.0 - mu), velocity, LagrangePoint.L1);
            Vector3d l2 = OrbitMath.ComputeLagrangePoint(
                primary, 1.0, secondary, mu / (1.0 - mu), velocity, LagrangePoint.L2);

            // Known CR3BP roots for this mass ratio: gamma_1 = 0.150954..., gamma_2 = 0.167857...
            Assert.That(l1.x, Is.EqualTo(1.0 - 0.15095414236321902).Within(1e-9));
            Assert.That(l2.x, Is.EqualTo(1.0 + 0.16785729227402313).Within(1e-9));
            Assert.That(l1.y, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(l2.z, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void L4AndL5FormEquilateralTriangles()
        {
            var primary = new Vector3d(4, -2, 0);
            var secondary = new Vector3d(7, -2, 0);
            var velocity = new Vector3d(0, 3, 0);

            Vector3d l4 = OrbitMath.ComputeLagrangePoint(
                primary, 1.0, secondary, 0.01, velocity, LagrangePoint.L4);
            Vector3d l5 = OrbitMath.ComputeLagrangePoint(
                primary, 1.0, secondary, 0.01, velocity, LagrangePoint.L5);

            Assert.That(Vector3d.Distance(primary, secondary), Is.EqualTo(3.0).Within(1e-12));
            Assert.That(Vector3d.Distance(primary, l4), Is.EqualTo(3.0).Within(1e-12));
            Assert.That(Vector3d.Distance(secondary, l4), Is.EqualTo(3.0).Within(1e-12));
            Assert.That(Vector3d.Distance(primary, l5), Is.EqualTo(3.0).Within(1e-12));
            Assert.That(Vector3d.Distance(secondary, l5), Is.EqualTo(3.0).Within(1e-12));

            // L4 leads the secondary and L5 trails it in the orbital plane.
            Assert.That(l4.y, Is.GreaterThan(secondary.y));
            Assert.That(l5.y, Is.LessThan(secondary.y));
        }

        [Test]
        public void DegenerateLagrangePairFallsBackToTheSecondary()
        {
            var point = OrbitMath.ComputeLagrangePoint(
                Vector3d.Zero, 1.0, Vector3d.Zero, 0.01, Vector3d.Zero, LagrangePoint.L2);
            MathAssert.AreClose(point, Vector3d.Zero, 0.0);
        }

        [Test]
        public void PoleConversionUsesTheJ2000Obliquity()
        {
            double sin = Math.Sin(OrbitMath.ObliquityJ2000);
            double cos = Math.Cos(OrbitMath.ObliquityJ2000);

            MathAssert.AreClose(OrbitMath.PoleFromRaDec(0, 90), new Vector3d(0, sin, cos), 1e-12);
            MathAssert.AreClose(OrbitMath.PoleFromRaDec(0, 0), new Vector3d(1, 0, 0), 1e-12);
            MathAssert.AreClose(OrbitMath.PoleFromRaDec(90, 0), new Vector3d(0, cos, -sin), 1e-12);

            Vector3d pole = OrbitMath.PoleFromRaDec(123.4, -56.7);
            Assert.That(pole.Magnitude, Is.EqualTo(1.0).Within(1e-12));
        }
    }
}
