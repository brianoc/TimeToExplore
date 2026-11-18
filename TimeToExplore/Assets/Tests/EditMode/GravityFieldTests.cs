using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    [Category("flight")]
    public class GravityFieldTests
    {
        private const double Au = 1.495978707e11;
        private SolarSystemModel _model;
        private GravityField _field;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _field = FlightFixture.FullField(_model);
        }

        [Test]
        public void TheSunAloneGivesNewtonsLawAtOneAu()
        {
            var sunOnly = FlightFixture.SunOnlyField(_model);
            Assert.That(sunOnly.SourceCount, Is.EqualTo(1));

            var point = new Vector3d(Au, 0.0, 0.0);
            Vector3d acceleration = sunOnly.AccelerationAt(point, _model.CurrentJulianDate);

            double expected = _model.Sun.Gm / (Au * Au);
            Assert.That(acceleration.Magnitude, Is.EqualTo(expected).Within(expected * 1e-12));
            Assert.That(acceleration.x, Is.LessThan(0.0), "gravity pulls toward the Sun");
            Assert.That(acceleration.y, Is.EqualTo(0.0).Within(expected * 1e-12));
        }

        [Test]
        public void NearMarsTheFieldIsDominatedByMars()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            double radius = ParkingOrbit.RadiusM(mars);
            Vector3d point = marsPosition + mars.EquatorX * radius;

            _field.Prepare(point, _model.CurrentJulianDate);
            Vector3d acceleration = _field.AccelerationAt(point, _model.CurrentJulianDate);

            double marsOnly = mars.Gm / (radius * radius);
            Assert.That(acceleration.Magnitude / marsOnly, Is.EqualTo(1.0).Within(1e-3));
            Assert.That(_field.IsActive("mars"), Is.True);
            Assert.That(_field.IsActive("sun"), Is.True);
        }

        [Test]
        public void PruningMatchesTheFullSumToWellInsideTheErrorBudget()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            var points = new[]
            {
                new Vector3d(0.0, Au, 0.0),
                marsPosition + mars.EquatorX * ParkingOrbit.RadiusM(mars),
                new Vector3d(3.0 * Au, 0.5 * Au, 0.1 * Au)
            };

            foreach (Vector3d point in points)
            {
                _field.Prepare(point, _model.CurrentJulianDate);
                Vector3d pruned = _field.AccelerationAt(point, _model.CurrentJulianDate);
                Vector3d full = _field.AccelerationAtFull(point, _model.CurrentJulianDate);

                double relative = (pruned - full).Magnitude / full.Magnitude;
                Assert.That(relative, Is.LessThan(1e-5),
                    $"pruned field differs by {relative:E2} at {point}");
            }
        }

        [Test]
        public void AccelerationIsClampedInsideABodyAndTheCollisionIsFound()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            Vector3d inside = marsPosition + mars.EquatorX * (mars.RadiusM * 0.5);
            _field.Prepare(inside, _model.CurrentJulianDate);
            Vector3d acceleration = _field.AccelerationAt(inside, _model.CurrentJulianDate);

            // Below the surface the field uses the surface radius, so the pull
            // falls linearly to half of surface gravity at half the radius. The
            // Sun adds about 0.1% at Mars, hence the tolerance.
            double expected = 0.5 * mars.Gm / (mars.RadiusM * mars.RadiusM);
            Assert.That(acceleration.Magnitude / expected, Is.EqualTo(1.0).Within(5e-3));
            Assert.That(Vector3d.Dot(acceleration.Normalized, mars.EquatorX),
                Is.LessThan(-0.99), "pull is toward the centre");

            Assert.That(_field.NearestClearanceM, Is.LessThan(0.0));
            Assert.That(_field.FindCollidingBody(inside, _model.CurrentJulianDate).Id,
                Is.EqualTo("mars"));
        }

        [Test]
        public void EvaluatingTheFieldDoesNotDisturbTheModel()
        {
            double julianDate = _model.CurrentJulianDate;
            Vector3d earthBefore = _model.Get("earth").PositionM;

            _field.Prepare(new Vector3d(Au, 0.0, 0.0), julianDate + 5.0);
            _field.AccelerationAt(new Vector3d(Au, 0.0, 0.0), julianDate + 5.0);
            _field.AccelerationAtFull(new Vector3d(Au, 0.0, 0.0), julianDate + 5.0);

            Assert.That(_model.CurrentJulianDate, Is.EqualTo(julianDate));
            Assert.That(Vector3d.Distance(_model.Get("earth").PositionM, earthBefore),
                Is.EqualTo(0.0));
        }

        [Test]
        public void ComputeStateMatchesTheFrameUpdateAtTheCurrentDate()
        {
            double julianDate = _model.CurrentJulianDate;

            foreach (BodyRuntime body in _model.Ordered)
            {
                _model.ComputeState(body, julianDate,
                    out Vector3d position, out Vector3d velocity);

                Assert.That(Vector3d.Distance(position, body.PositionM), Is.LessThan(1e-6),
                    $"{body.Id} position");

                if (body.Kind == "station")
                {
                    // The frame loop derives a station velocity from the previous
                    // frame, which is zero after a single load; the pure evaluator
                    // differentiates the Lagrange point instead.
                    // Station velocities are heliocentric: 30 km/s at Earth,
                    // 13 km/s at Jupiter, 9.6 km/s at Saturn.
                    Assert.That(velocity.Magnitude, Is.InRange(5_000.0, 35_000.0),
                        $"{body.Id} station velocity");
                }
                else
                {
                    Assert.That(Vector3d.Distance(velocity, body.VelocityMps), Is.LessThan(1e-6),
                        $"{body.Id} velocity");
                }
            }
        }

        [Test]
        public void PreparingAFilteredFieldKeepsTheAncestorsItNeeds()
        {
            var moonsOnly = new GravityField(_model, body => body.Kind == "moon");
            Assert.That(moonsOnly.SourceCount, Is.GreaterThan(22),
                "moons pull in their planets and the Sun");

            BodyRuntime moon = _model.Get("moon");
            _model.ComputeState(moon, _model.CurrentJulianDate, out Vector3d moonPosition, out _);
            moonsOnly.Prepare(moonPosition, _model.CurrentJulianDate);
            Assert.That(moonsOnly.IsActive("earth"), Is.True);
        }
    }
}
