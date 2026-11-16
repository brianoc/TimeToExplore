using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    public class ShipDynamicsTests
    {
        private const double Au = 1.495978707e11;

        [Test]
        public void CircularSpeedAtLowEarthOrbitMatchesTheTextbookValue()
        {
            double gm = CatalogueFixture.Find("earth").gm;
            double radius = 6_378_000.0 + 300_000.0;

            double speed = TwoBody.CircularSpeedMps(gm, radius);

            Assert.That(speed, Is.InRange(7_700.0, 7_750.0),
                "a 300 km circular orbit is about 7.73 km/s");
        }

        [Test]
        public void EscapeSpeedIsSqrtTwoTimesCircular()
        {
            double gm = CatalogueFixture.Find("earth").gm;
            double radius = 6_371_000.0;

            double circular = TwoBody.CircularSpeedMps(gm, radius);
            double escape = TwoBody.EscapeSpeedMps(gm, radius);

            Assert.That(escape / circular, Is.EqualTo(Math.Sqrt(2.0)).Within(1e-12));
            Assert.That(escape, Is.InRange(11_100.0, 11_250.0),
                "Earth's surface escape speed is 11.19 km/s");
        }

        [Test]
        public void CircularStateHasZeroEccentricityAndTheRadiusAsSemiMajorAxis()
        {
            double gm = CatalogueFixture.Find("mars").gm;
            double radius = 4_000_000.0;
            var position = new Vector3d(radius, 0.0, 0.0);
            var velocity = new Vector3d(0.0, TwoBody.CircularSpeedMps(gm, radius), 0.0);

            double eccentricity = TwoBody.Eccentricity(position, velocity, gm);
            double semiMajorAxis = TwoBody.SemiMajorAxisM(position, velocity, gm);
            double period = TwoBody.PeriodSeconds(gm, semiMajorAxis);

            Assert.That(eccentricity, Is.LessThan(1e-12));
            Assert.That(semiMajorAxis, Is.EqualTo(radius).Within(1e-6));
            Assert.That(period, Is.EqualTo(TwoBody.PeriodSeconds(gm, radius)).Within(1e-9));
        }

        [Test]
        public void EllipticalStateRecoversItsElementsFromVisViva()
        {
            double gm = CatalogueFixture.Find("earth").gm;
            double periapsis = 7_000_000.0;
            double semiMajorAxis = 10_000_000.0;

            // Velocity at periapsis from the vis-viva equation, then recover a and e.
            double speed = Math.Sqrt(gm * (2.0 / periapsis - 1.0 / semiMajorAxis));
            var position = new Vector3d(periapsis, 0.0, 0.0);
            var velocity = new Vector3d(0.0, speed, 0.0);

            Assert.That(TwoBody.SemiMajorAxisM(position, velocity, gm),
                Is.EqualTo(semiMajorAxis).Within(1e-3));
            Assert.That(TwoBody.Eccentricity(position, velocity, gm),
                Is.EqualTo(1.0 - periapsis / semiMajorAxis).Within(1e-9));
            Assert.That(TwoBody.FlightPathAngleRad(position, velocity),
                Is.EqualTo(0.0).Within(1e-12), "periapsis is horizontal");
        }

        [Test]
        public void InclinationFollowsTheAngularMomentum()
        {
            double gm = CatalogueFixture.Find("earth").gm;
            double radius = 7_000_000.0;
            double speed = TwoBody.CircularSpeedMps(gm, radius);

            var equatorial = TwoBody.InclinationRad(
                new Vector3d(radius, 0.0, 0.0), new Vector3d(0.0, speed, 0.0));
            var polar = TwoBody.InclinationRad(
                new Vector3d(radius, 0.0, 0.0), new Vector3d(0.0, 0.0, speed));

            Assert.That(equatorial, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(polar, Is.EqualTo(Math.PI / 2.0).Within(1e-12));
        }

        [Test]
        public void ThrustCommandClampsWithoutChangingDirection()
        {
            var command = new ThrustCommand(new Vector3d(3.0, 0.0, 4.0)); // 5 m/s²
            ThrustCommand clamped = command.ClampedTo(2.0);

            Assert.That(command.MagnitudeMps2, Is.EqualTo(5.0).Within(1e-12));
            Assert.That(clamped.MagnitudeMps2, Is.EqualTo(2.0).Within(1e-12));
            Assert.That(Vector3d.Dot(clamped.Direction, command.Direction),
                Is.EqualTo(1.0).Within(1e-12));
            Assert.That(command.Throttle(10.0), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(ThrustCommand.None.IsActive, Is.False);
        }

        [Test]
        public void ShipStateReportsRelativeMotion()
        {
            var ship = new ShipState(2451545.0, new Vector3d(10.0, 0.0, 0.0), new Vector3d(1.0, 2.0, 0.0));
            var bodyPosition = new Vector3d(4.0, 0.0, 0.0);
            var bodyVelocity = new Vector3d(1.0, 0.0, 0.0);

            Assert.That(ship.DistanceTo(bodyPosition), Is.EqualTo(6.0).Within(1e-12));
            Assert.That(ship.VelocityRelativeTo(bodyVelocity).Magnitude,
                Is.EqualTo(2.0).Within(1e-12));
            Assert.That(ship.TotalMassKg, Is.EqualTo(ShipState.DefaultDryMassKg).Within(1e-9));
        }
    }
}
