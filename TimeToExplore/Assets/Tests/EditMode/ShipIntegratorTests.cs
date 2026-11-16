using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    public class ShipIntegratorTests
    {
        private const double Au = 1.495978707e11;
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        [Test]
        public void CircularHeliocentricOrbitIsStableForTwoHundredDays()
        {
            var ship = new ShipState(_model.CurrentJulianDate,
                new Vector3d(Au, 0.0, 0.0),
                new Vector3d(0.0, TwoBody.CircularSpeedMps(_model.Sun.Gm, Au), 0.0));

            double radiusBefore = ship.PositionM.Magnitude;
            double energyBefore = TwoBody.SpecificEnergyJPerKg(
                ship.PositionM, ship.VelocityMps, _model.Sun.Gm);
            Vector3d momentumBefore = Vector3d.Cross(ship.PositionM, ship.VelocityMps);

            var integrator = new ShipIntegrator(FlightFixture.SunOnlyField(_model));
            var report = integrator.Advance(ref ship, ship.JulianDate + 200.0,
                (state, date) => ThrustCommand.None);

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);

            double radiusAfter = ship.PositionM.Magnitude;
            double energyAfter = TwoBody.SpecificEnergyJPerKg(
                ship.PositionM, ship.VelocityMps, _model.Sun.Gm);
            Vector3d momentumAfter = Vector3d.Cross(ship.PositionM, ship.VelocityMps);

            Assert.That(Math.Abs(radiusAfter - radiusBefore) / radiusBefore, Is.LessThan(1e-6));
            Assert.That(Math.Abs(energyAfter - energyBefore) / Math.Abs(energyBefore),
                Is.LessThan(1e-8));
            Assert.That(Math.Abs(momentumAfter.Magnitude - momentumBefore.Magnitude)
                        / momentumBefore.Magnitude, Is.LessThan(1e-8));
            Assert.That(TwoBody.Eccentricity(ship.PositionM, ship.VelocityMps, _model.Sun.Gm),
                Is.LessThan(1e-4));
        }

        [Test]
        public void CoastingShipFollowsTheKeplerianEphemeris()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "earth");
            var integrator = new ShipIntegrator(FlightFixture.SunOnlyField(_model));

            integrator.Advance(ref ship, ship.JulianDate + 30.0,
                (state, date) => ThrustCommand.None, 3600.0);

            _model.ComputeState(_model.Get("earth"), ship.JulianDate,
                out Vector3d position, out Vector3d velocity);

            // The ship and Earth are both on the same two-body ellipse about the
            // Sun, so the numerical integration must reproduce the analytic
            // Kepler propagator from a different code path.
            Assert.That(Vector3d.Distance(ship.PositionM, position), Is.LessThan(5_000.0));
            Assert.That((ship.VelocityMps - velocity).Magnitude, Is.LessThan(0.1));
        }

        [Test]
        public void ConstantThrustMatchesKinematicsInEmptySpace()
        {
            var ship = new ShipState(_model.CurrentJulianDate, Vector3d.Zero, Vector3d.Zero);
            var integrator = new ShipIntegrator(ZeroGravityField.Instance);

            var thrust = new Vector3d(10.0, 0.0, 0.0);
            integrator.Advance(ref ship, ship.JulianDate + 100.0 / JulianDate.SecondsPerDay,
                (state, date) => new ThrustCommand(thrust));

            // A Julian date near 2.4 million has a 40 µs grid, so a 100 s span is
            // only known to about 0.05 s. Half a metre is the grid's resolution.
            Assert.That(ship.PositionM.x, Is.EqualTo(0.5 * 10.0 * 100.0 * 100.0).Within(0.5));
            Assert.That(ship.VelocityMps.x, Is.EqualTo(1_000.0).Within(1e-3));
        }

        [Test]
        public void BrachistochroneFlipReproducesTheFlightModelTable()
        {
            // Earth-Moon separation, one gravity, accelerate to the flip and
            // decelerate the rest of the way.
            double distance = 384_400_000.0;
            double acceleration = 10.0;
            double total = 2.0 * Math.Sqrt(distance / acceleration);
            double half = total * 0.5;

            var ship = new ShipState(_model.CurrentJulianDate, Vector3d.Zero, Vector3d.Zero);
            var integrator = new ShipIntegrator(ZeroGravityField.Instance);

            integrator.Advance(ref ship, ship.JulianDate + half / JulianDate.SecondsPerDay,
                (state, date) => new ThrustCommand(new Vector3d(acceleration, 0.0, 0.0)));

            double peakSpeed = ship.VelocityMps.x;
            Assert.That(peakSpeed, Is.EqualTo(Math.Sqrt(acceleration * distance)).Within(1.0));

            integrator.Advance(ref ship, ship.JulianDate + half / JulianDate.SecondsPerDay,
                (state, date) => new ThrustCommand(new Vector3d(-acceleration, 0.0, 0.0)));

            // Tolerances reflect the 40 µs Julian-date grid over a 12,400 s burn.
            Assert.That(ship.PositionM.x, Is.EqualTo(distance).Within(10.0));
            Assert.That(ship.VelocityMps.x, Is.EqualTo(0.0).Within(1.0));
            Assert.That(total / 3600.0, Is.InRange(3.3, 3.5), "the table says 3.4 hours");
        }

        [Test]
        public void SplittingTheSpanDoesNotChangeTheResult()
        {
            ShipState single = FlightFixture.ShipAt(_model, "earth");
            ShipState split = single;
            double start = single.JulianDate;

            var integrator = new ShipIntegrator(FlightFixture.SunOnlyField(_model));
            double end = start + 1.0;

            integrator.Advance(ref single, end, (state, date) => ThrustCommand.None);

            for (int i = 1; i <= 6; i++)
            {
                integrator.Advance(ref split, start + i / 6.0,
                    (state, date) => ThrustCommand.None);
            }

            Assert.That(Vector3d.Distance(single.PositionM, split.PositionM), Is.LessThan(10.0));
            Assert.That((single.VelocityMps - split.VelocityMps).Magnitude, Is.LessThan(1e-3));
        }

        [Test]
        public void CollisionWithTheMoonIsReported()
        {
            BodyRuntime moon = _model.Get("moon");
            _model.ComputeState(moon, _model.CurrentJulianDate,
                out Vector3d moonPosition, out Vector3d moonVelocity);

            var ship = new ShipState(_model.CurrentJulianDate,
                moonPosition + moon.EquatorX * (moon.RadiusM + 50_000.0),
                moonVelocity - moon.EquatorX * 50.0);

            var integrator = new ShipIntegrator(FlightFixture.FullField(_model));
            IntegrationReport report = integrator.Advance(ref ship,
                ship.JulianDate + 2.0 / 24.0, (state, date) => ThrustCommand.None, 60.0);

            Assert.That(report.CollisionDetected, Is.True);
            Assert.That(report.CollisionBodyId, Is.EqualTo("moon"));
        }

        [Test]
        public void TheStepCapIsRespected()
        {
            var ship = new ShipState(_model.CurrentJulianDate, Vector3d.Zero, Vector3d.Zero);
            var integrator = new ShipIntegrator(ZeroGravityField.Instance)
            {
                MaxStepSeconds = 60.0
            };

            IntegrationReport report = integrator.Advance(ref ship,
                ship.JulianDate + 3600.0 / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(report.Substeps, Is.EqualTo(60));
            Assert.That(report.MaxStepSeconds, Is.EqualTo(60.0).Within(1e-9));
        }
    }
}
