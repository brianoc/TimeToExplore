using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    public class PlanGeometryTests
    {
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        [Test]
        public void QuinticMatchesAllSixEndpointConditions()
        {
            var startPosition = new Vector3d(1.0e10, 2.0e10, -3.0e9);
            var startVelocity = new Vector3d(100.0, -200.0, 300.0);
            var startAcceleration = new Vector3d(1.0, 2.0, 3.0);
            var endPosition = new Vector3d(-4.0e10, 1.0e10, 5.0e9);
            var endVelocity = new Vector3d(-50.0, 400.0, 10.0);
            var endAcceleration = new Vector3d(-3.0, -1.0, 0.5);

            Quintic path = Quintic.Create(startPosition, startVelocity, startAcceleration,
                endPosition, endVelocity, endAcceleration, 100_000.0);

            MathAssert.AreClose(path.PositionAt(0.0), startPosition, 1e-6);
            MathAssert.AreClose(path.VelocityAt(0.0), startVelocity, 1e-9);
            MathAssert.AreClose(path.AccelerationAt(0.0), startAcceleration, 1e-9);

            MathAssert.AreClose(path.PositionAt(1.0), endPosition, 1.0);
            MathAssert.AreClose(path.VelocityAt(1.0), endVelocity, 1e-4);
            MathAssert.AreClose(path.AccelerationAt(1.0), endAcceleration, 1e-6);
        }

        [Test]
        public void RestToRestQuinticIsTheClassicSmoothstep()
        {
            Quintic path = Quintic.RestToRest(
                Vector3d.Zero, new Vector3d(1.0, 0.0, 0.0), 1.0);

            Assert.That(path.PositionAt(0.5).x, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(path.PositionAt(0.25).x,
                Is.EqualTo(10.0 * Math.Pow(0.25, 3) - 15.0 * Math.Pow(0.25, 4)
                           + 6.0 * Math.Pow(0.25, 5)).Within(1e-12));
            Assert.That(path.VelocityAt(0.5).x, Is.EqualTo(1.875).Within(1e-12));
            Assert.That(path.VelocityAt(0.0).x, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(path.VelocityAt(1.0).x, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void RestToRestPeakAccelerationMatchesTheAnalyticFactor()
        {
            double distance = 1.17e11;
            double duration = 2.6e5;
            Quintic path = Quintic.RestToRest(
                Vector3d.Zero, new Vector3d(distance, 0.0, 0.0), duration);

            double peak = path.PeakAccelerationMagnitude(512);

            Assert.That(peak, Is.EqualTo(Quintic.RestToRestPeakFactor * distance
                                            / (duration * duration)).Within(1.0));
        }

        [Test]
        public void PeakAccelerationFallsWithTheSquareOfTheDuration()
        {
            var start = Vector3d.Zero;
            var end = new Vector3d(1.0e10, 0.0, 0.0);

            double shortPeak = Quintic.RestToRest(start, end, 1_000.0).PeakAccelerationMagnitude();
            double longPeak = Quintic.RestToRest(start, end, 2_000.0).PeakAccelerationMagnitude();

            Assert.That(shortPeak / longPeak, Is.EqualTo(4.0).Within(2e-3));
        }

        [Test]
        public void ParkingRadiusClearsTheSurfaceAndStaysInsideTheHillSphere()
        {
            foreach (string id in new[] { "mercury", "earth", "mars", "jupiter", "moon", "titan" })
            {
                BodyRuntime body = _model.Get(id);
                double radius = ParkingOrbit.RadiusM(body);

                Assert.That(radius, Is.GreaterThan(body.RadiusM), $"{id} radius");
                Assert.That(radius, Is.GreaterThanOrEqualTo(body.RadiusM + 1.0));

                if (double.IsFinite(body.HillRadiusM))
                    Assert.That(radius, Is.LessThan(body.HillRadiusM), $"{id} Hill sphere");
            }
        }

        [Test]
        public void EveryParkingOrbitSitsUnderTheGravityCeiling()
        {
            // A capture arc cannot command less thrust than the parking orbit's
            // own gravity, so the radius policy keeps that gravity at 40% of the
            // drive. Only a body whose Hill sphere caps the orbit first is
            // allowed to exceed it; those bodies simply are not reachable.
            const double ceiling = ParkingOrbit.GravityFractionOfMaxAcceleration * 10.0;
            int checkedBodies = 0;

            foreach (BodyRuntime body in _model.Ordered)
            {
                if (body.Gm <= 0.0 || body.RadiusM <= 0.0) continue;
                if (body.Kind != "planet" && body.Kind != "moon") continue;

                double radius = ParkingOrbit.RadiusM(body);
                double gravity = body.Gm / (radius * radius);
                double hill = body.HillRadiusM;
                bool cappedByHill = double.IsFinite(hill)
                                    && radius >= hill * ParkingOrbit.HillFraction * (1.0 - 1e-12);

                if (!cappedByHill)
                    Assert.That(gravity, Is.LessThanOrEqualTo(ceiling * (1.0 + 1e-9)),
                        $"{body.Id} parks in {gravity:F2} m/s²");
                checkedBodies++;
            }

            Assert.That(checkedBodies, Is.GreaterThan(25), "the catalogue has planets and moons");
        }

        [Test]
        public void TheCeilingLeavesShallowWellsWhereTheyWere()
        {
            // Mars and the Moon are already under the ceiling, so their arrival
            // orbits must not move: this keeps the documented values honest.
            Assert.That(ParkingOrbit.RadiusM(_model.Get("mars")),
                Is.EqualTo(3_897_925.0).Within(1.0));
            Assert.That(ParkingOrbit.RadiusM(_model.Get("moon")),
                Is.EqualTo(2_037_400.0).Within(1.0));
        }

        [Test]
        public void TheCeilingLiftsTheTallWells()
        {
            // Earth, Venus, and the four giants were the only bodies whose
            // default orbit sat in gravity above 40% of the drive.
            Assert.That(ParkingOrbit.RadiusM(_model.Get("earth")) / 1000.0,
                Is.EqualTo(9_983.0).Within(2.0));
            Assert.That(ParkingOrbit.RadiusM(_model.Get("venus")) / 1000.0,
                Is.EqualTo(9_012.0).Within(2.0));
            Assert.That(ParkingOrbit.RadiusM(_model.Get("saturn")) / 1000.0,
                Is.EqualTo(97_380.0).Within(50.0));
            Assert.That(ParkingOrbit.RadiusM(_model.Get("neptune")) / 1000.0,
                Is.EqualTo(41_342.0).Within(50.0));
        }

        [Test]
        public void ParkingStateIsACircularEquatorialProgradeOrbit()
        {
            BodyRuntime mars = _model.Get("mars");

            // Work in the body-relative frame: subtracting two heliocentric
            // positions at 1.6 AU loses about 10 µm to double precision.
            ParkingOrbit.StateRelativeTo(mars, 0.7,
                out Vector3d relativePosition, out Vector3d relativeVelocity);

            double radius = ParkingOrbit.RadiusM(mars);
            double speed = ParkingOrbit.SpeedMps(mars);

            Assert.That(relativePosition.Magnitude, Is.EqualTo(radius).Within(1e-6));
            Assert.That(relativeVelocity.Magnitude, Is.EqualTo(speed).Within(1e-9));
            Assert.That(Vector3d.Dot(relativePosition.Normalized, relativeVelocity.Normalized),
                Is.EqualTo(0.0).Within(1e-12), "circular orbits are perpendicular");

            Assert.That(TwoBody.Eccentricity(relativePosition, relativeVelocity, mars.Gm),
                Is.LessThan(1e-12));
            Assert.That(TwoBody.SemiMajorAxisM(relativePosition, relativeVelocity, mars.Gm),
                Is.EqualTo(radius).Within(1e-3));

            Assert.That(Math.Abs(Vector3d.Dot(relativePosition.Normalized, mars.PoleEcliptic)),
                Is.LessThan(1e-12), "the orbit lies in the equatorial plane");
            Assert.That(Math.Abs(Vector3d.Dot(relativeVelocity.Normalized, mars.PoleEcliptic)),
                Is.LessThan(1e-12));
            Assert.That(Vector3d.Dot(
                    Vector3d.Cross(relativePosition, relativeVelocity), mars.PoleEcliptic),
                Is.GreaterThan(0.0), "the orbit is prograde about the north pole");

            Assert.That(ParkingOrbit.PeriodSeconds(mars),
                Is.EqualTo(TwoBody.PeriodSeconds(mars.Gm, radius)).Within(1e-9));

            // The absolute state is the body's state plus the relative one.
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);
            ParkingOrbit.StateAt(mars, bodyPosition, bodyVelocity, 0.7,
                out Vector3d position, out Vector3d velocity);
            Assert.That(Vector3d.Distance(position - bodyPosition, relativePosition),
                Is.LessThan(1e-3));
            Assert.That(Vector3d.Distance(velocity - bodyVelocity, relativeVelocity),
                Is.LessThan(1e-3));
        }

        [Test]
        public void PhaseMovesTheShipAroundTheSameCircle()
        {
            BodyRuntime moon = _model.Get("moon");
            _model.ComputeState(moon, _model.CurrentJulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);

            ParkingOrbit.StateAt(moon, bodyPosition, bodyVelocity, 0.0,
                out Vector3d a, out _);
            ParkingOrbit.StateAt(moon, bodyPosition, bodyVelocity, Math.PI,
                out Vector3d b, out _);

            Assert.That(Vector3d.Distance(a, bodyPosition),
                Is.EqualTo(Vector3d.Distance(b, bodyPosition)).Within(1e-6));
            Assert.That(Vector3d.Distance(a, b),
                Is.EqualTo(2.0 * ParkingOrbit.RadiusM(moon)).Within(1e-3));
        }

        [Test]
        public void SphereOfInfluenceRadiiMatchThePublishedEstimates()
        {
            Assert.That(SphereOfInfluence.RadiusM(_model.Get("earth")) / 1000.0,
                Is.InRange(850_000.0, 1_000_000.0));
            Assert.That(SphereOfInfluence.RadiusM(_model.Get("moon")) / 1000.0,
                Is.InRange(60_000.0, 72_000.0));
            Assert.That(SphereOfInfluence.RadiusM(_model.Get("jupiter")) / 1000.0,
                Is.InRange(45.0e6, 52.0e6));
        }

        [Test]
        public void SphereOfInfluenceContainsItsOwnBodyOnly()
        {
            BodyRuntime earth = _model.Get("earth");
            _model.ComputeState(earth, _model.CurrentJulianDate,
                out Vector3d earthPosition, out _);

            double radius = SphereOfInfluence.RadiusM(earth);
            Assert.That(SphereOfInfluence.Contains(earth,
                earthPosition + earth.EquatorX * (radius * 0.9), earthPosition), Is.True);
            Assert.That(SphereOfInfluence.Contains(earth,
                earthPosition + earth.EquatorX * (radius * 1.1), earthPosition), Is.False);
        }
    }
}
