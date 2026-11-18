using System;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The zero-revolution prograde short-way Lambert solver. Every case is
    /// checked by feeding the returned departure velocity back through
    /// <see cref="KeplerPropagator"/>: a legitimate v₁ must land on r₂ after the
    /// requested time of flight. The analytic cases pin the branch and the
    /// speeds; the geometric cases pin the 180° and long-way guards.
    /// </summary>
    [Category("core")]
    public class LambertSolverTests
    {
        private const double SunMu = 1.32712440018e20;
        private const double AuM = 1.495978707e11;

        private static Vector3d OnCircle(double radius, double angleRad) =>
            new Vector3d(radius * Math.Cos(angleRad), radius * Math.Sin(angleRad), 0.0);

        private static double HohmannSeconds(double r1, double r2) =>
            Math.PI * Math.Sqrt(Math.Pow(0.5 * (r1 + r2), 3.0) / SunMu);

        private static void AssertLandsOn(Vector3d departure, Vector3d arrival,
            double timeOfFlightSeconds, LambertSolution solution, double toleranceM,
            double velocityToleranceMps = 1e-6)
        {
            KeplerPropagator.Propagate(departure, solution.DepartureVelocityMps,
                SunMu, timeOfFlightSeconds, out Vector3d position, out Vector3d velocity);

            TestContext.WriteLine($"v₁ = {solution.DepartureVelocityMps.Magnitude:F3} m/s, " +
                                  $"position error {Vector3d.Distance(position, arrival):E2} m");

            Assert.That(Vector3d.Distance(position, arrival), Is.LessThan(toleranceM));
            Assert.That((velocity - solution.ArrivalVelocityMps).Magnitude,
                Is.LessThan(velocityToleranceMps));
        }

        [Test]
        public void PropagatingDepartureVelocityReachesTheArrivalPosition()
        {
            Vector3d departure = OnCircle(AuM, 0.0);
            Vector3d arrival = OnCircle(1.524 * AuM, 135.0 * OrbitMath.Deg2Rad);
            double flight = 180.0 * JulianDate.SecondsPerDay;

            LambertSolution solution = LambertSolver.SolveShortWay(
                departure, arrival, flight, SunMu);

            Assert.That(solution.HasSolution, Is.True);
            Assert.That(solution.TransferAngleRad,
                Is.EqualTo(135.0 * OrbitMath.Deg2Rad).Within(1e-12));
            AssertLandsOn(departure, arrival, flight, solution, 10.0);
        }

        [Test]
        public void QuarterTurnTransferIsTheCircularOrbit()
        {
            double radius = AuM;
            double speed = Math.Sqrt(SunMu / radius);
            double flight = 0.5 * Math.PI * Math.Sqrt(radius * radius * radius / SunMu);

            Vector3d departure = OnCircle(radius, 0.0);
            Vector3d arrival = OnCircle(radius, Math.PI / 2.0);
            LambertSolution solution = LambertSolver.SolveShortWay(
                departure, arrival, flight, SunMu);

            Assert.That(solution.HasSolution, Is.True);
            Assert.That(solution.TransferAngleRad,
                Is.EqualTo(Math.PI / 2.0).Within(1e-12));
            MathAssert.AreClose(solution.DepartureVelocityMps,
                new Vector3d(0.0, speed, 0.0), 1e-6);
            MathAssert.AreClose(solution.ArrivalVelocityMps,
                new Vector3d(-speed, 0.0, 0.0), 1e-6);
            AssertLandsOn(departure, arrival, flight, solution, 1e-3);
        }

        [Test]
        public void HohmannLikeTransferMatchesTheVisVivaSpeeds()
        {
            double r1 = AuM;
            double r2 = 1.524 * AuM;
            double angle = Math.PI - 0.1 * OrbitMath.Deg2Rad;
            double flight = HohmannSeconds(r1, r2);
            double semiMajorAxis = 0.5 * (r1 + r2);
            double expectedDeparture = Math.Sqrt(SunMu * (2.0 / r1 - 1.0 / semiMajorAxis));
            double expectedArrival = Math.Sqrt(SunMu * (2.0 / r2 - 1.0 / semiMajorAxis));

            Vector3d departure = OnCircle(r1, 0.0);
            Vector3d arrival = OnCircle(r2, angle);
            LambertSolution solution = LambertSolver.SolveShortWay(
                departure, arrival, flight, SunMu);

            TestContext.WriteLine($"|v₁| = {solution.DepartureVelocityMps.Magnitude:F3} m/s " +
                                  $"(Hohmann {expectedDeparture:F3}), " +
                                  $"|v₂| = {solution.ArrivalVelocityMps.Magnitude:F3} m/s " +
                                  $"(Hohmann {expectedArrival:F3})");

            Assert.That(solution.HasSolution, Is.True);
            Assert.That(solution.DepartureVelocityMps.Magnitude,
                Is.EqualTo(expectedDeparture).Within(0.05));
            Assert.That(solution.ArrivalVelocityMps.Magnitude,
                Is.EqualTo(expectedArrival).Within(0.05));
            // A 179.9° transfer is ill-conditioned by nature: the bisection in z
            // still leaves about a hundred metres and 10 µm/s on the table.
            AssertLandsOn(departure, arrival, flight, solution, 1_000.0, 1e-4);
        }

        [Test]
        public void HighEnergyTransferConvergesOnAHyperbola()
        {
            Vector3d departure = OnCircle(AuM, 0.0);
            Vector3d arrival = OnCircle(1.524 * AuM, Math.PI / 2.0);
            double flight = 20.0 * JulianDate.SecondsPerDay;

            LambertSolution solution = LambertSolver.SolveShortWay(
                departure, arrival, flight, SunMu);

            Assert.That(solution.HasSolution, Is.True);
            Assert.That(solution.DepartureVelocityMps.Magnitude,
                Is.GreaterThan(Math.Sqrt(2.0 * SunMu / AuM)),
                "a 20-day transfer between 1 and 1.5 AU is faster than escape");

            ConicElements conic = ConicElements.FromState(
                departure, solution.DepartureVelocityMps, SunMu);
            Assert.That(conic.IsHyperbolic, Is.True);

            AssertLandsOn(departure, arrival, flight, solution, 10.0);
        }

        [Test]
        public void ShortWayBranchIsReturnedAndTheLongWayIsRefused()
        {
            double radius = AuM;
            double flight = 0.5 * Math.PI * Math.Sqrt(radius * radius * radius / SunMu);
            Vector3d departure = OnCircle(radius, 0.0);
            Vector3d shortArrival = OnCircle(radius, Math.PI / 2.0);

            LambertSolution shortWay = LambertSolver.SolveShortWay(
                departure, shortArrival, flight, SunMu);
            Assert.That(shortWay.HasSolution, Is.True);
            Assert.That(shortWay.TransferAngleRad, Is.LessThan(Math.PI));

            Vector3d angularMomentum = Vector3d.Cross(
                departure, shortWay.DepartureVelocityMps);
            Assert.That(angularMomentum.z, Is.GreaterThan(0.0),
                "the returned arc is prograde about the ecliptic pole");

            // Rotating the arrival the other way is the same 90° geometry but a
            // 270° prograde sweep: the long way, which this solver does not have.
            Vector3d longArrival = OnCircle(radius, -Math.PI / 2.0);
            LambertSolution longWay = LambertSolver.SolveShortWay(
                departure, longArrival, flight, SunMu);

            Assert.That(longWay.HasSolution, Is.False);
            Assert.That(longWay.TransferAngleRad,
                Is.EqualTo(Math.PI / 2.0).Within(1e-12),
                "the reported angle is still the short geometric angle");
        }

        [Test]
        public void OneHundredEightyDegreeTransferIsGuarded()
        {
            double radius = AuM;
            double flight = Math.PI * Math.Sqrt(radius * radius * radius / SunMu);
            Vector3d departure = OnCircle(radius, 0.0);
            Vector3d arrival = new Vector3d(-radius, 0.0, 0.0);

            LambertSolution solution = default;
            Assert.DoesNotThrow(() => solution = LambertSolver.SolveShortWay(
                departure, arrival, flight, SunMu));

            Assert.That(solution.HasSolution, Is.False,
                "the A term vanishes, so the 180° transfer has no unique plane");
            Assert.That(solution.TransferAngleRad,
                Is.EqualTo(Math.PI).Within(1e-12));
            Assert.That(solution.DepartureVelocityMps.Magnitude, Is.EqualTo(0.0));
            Assert.That(solution.ArrivalVelocityMps.Magnitude, Is.EqualTo(0.0));
            Assert.That(double.IsFinite(solution.DepartureVelocityMps.x), Is.True);
            Assert.That(double.IsFinite(solution.ArrivalVelocityMps.y), Is.True);
            Assert.That(double.IsNaN(solution.DepartureVelocityMps.z), Is.False);
        }

        [Test]
        public void DegenerateArgumentsThrow()
        {
            Vector3d departure = OnCircle(AuM, 0.0);
            Vector3d arrival = OnCircle(1.524 * AuM, 0.3);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LambertSolver.SolveShortWay(departure, arrival, 1.0e6, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LambertSolver.SolveShortWay(Vector3d.Zero, arrival, 1.0e6, SunMu));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LambertSolver.SolveShortWay(departure, Vector3d.Zero, 1.0e6, SunMu));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LambertSolver.SolveShortWay(departure, arrival, 0.0, SunMu));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LambertSolver.SolveShortWay(departure, arrival, double.NaN, SunMu));
        }

        [Test]
        public void EpochEarthToMarsCoastArrivesAtMars()
        {
            const double epoch = 2_461_359.5; // 2026-11-15
            const double flightDays = 100.0;

            SolarSystemModel model = CatalogueFixture.LoadModel();
            model.Update(epoch);
            Vector3d departure = model.Get("earth").PositionM;

            model.Update(epoch + flightDays);
            Vector3d arrival = model.Get("mars").PositionM;

            double flight = flightDays * JulianDate.SecondsPerDay;
            LambertSolution solution = LambertSolver.SolveShortWay(
                departure, arrival, flight, model.Sun.Gm);

            Assert.That(model.Data.epochJulianDate, Is.EqualTo(epoch));
            Assert.That(solution.HasSolution, Is.True);
            TestContext.WriteLine(
                $"Earth→Mars in {flightDays} d: angle " +
                $"{solution.TransferAngleRad * OrbitMath.Rad2Deg:F2}°, " +
                $"v₁ {solution.DepartureVelocityMps.Magnitude / 1000.0:F3} km/s, " +
                $"v₂ {solution.ArrivalVelocityMps.Magnitude / 1000.0:F3} km/s");

            AssertLandsOn(departure, arrival, flight, solution, 10.0);
        }
    }
}
