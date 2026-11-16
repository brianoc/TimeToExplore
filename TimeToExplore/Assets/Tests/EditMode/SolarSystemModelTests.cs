using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    public class SolarSystemModelTests
    {
        private const double Au = 1.495978707e11;
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        [Test]
        public void LoadBuildsEveryBodyAndStartsAtTheCatalogueEpoch()
        {
            Assert.That(_model.Bodies.Count, Is.EqualTo(CatalogueFixture.Data.bodies.Length));
            Assert.That(_model.Ordered.Count, Is.EqualTo(CatalogueFixture.Data.bodies.Length));
            Assert.That(_model.CurrentJulianDate,
                Is.EqualTo(CatalogueFixture.Data.startJulianDate).Within(1e-9));
            Assert.That(_model.Sun, Is.Not.Null);
            Assert.That(_model.Sun.Id, Is.EqualTo("sun"));
        }

        [Test]
        public void OrderedListPlacesEveryParentBeforeItsChildren()
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _model.Ordered.Count; i++)
                index[_model.Ordered[i].Id] = i;

            foreach (BodyRuntime body in _model.Ordered)
            {
                if (body.Parent == null) continue;
                Assert.That(index[body.Parent.Id], Is.LessThan(index[body.Id]),
                    $"{body.Id} precedes its parent {body.Parent.Id}");
            }
        }

        [Test]
        public void TheSunHasNoOrbitAndSitsAtRestAtTheOrigin()
        {
            BodyRuntime sun = _model.Sun;
            Assert.That(sun.HasOrbit, Is.False,
                "JsonUtility deserializes a null orbit as a default object");
            Assert.That(sun.PositionM.Magnitude, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(sun.VelocityMps.Magnitude, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void EveryBodyHasFinitePositionAndVelocity()
        {
            foreach (BodyRuntime body in _model.Ordered)
            {
                Assert.That(double.IsFinite(body.PositionM.x), Is.True, $"{body.Id} px");
                Assert.That(double.IsFinite(body.PositionM.y), Is.True, $"{body.Id} py");
                Assert.That(double.IsFinite(body.PositionM.z), Is.True, $"{body.Id} pz");
                Assert.That(double.IsFinite(body.VelocityMps.x), Is.True, $"{body.Id} vx");
                Assert.That(double.IsFinite(body.VelocityMps.y), Is.True, $"{body.Id} vy");
                Assert.That(double.IsFinite(body.VelocityMps.z), Is.True, $"{body.Id} vz");
            }
        }

        [Test]
        public void EarthIsAtRoughlyOneAuAndOrbitalSpeedAtTheEpoch()
        {
            BodyRuntime earth = _model.Get("earth");
            double radius = Vector3d.Distance(earth.PositionM, _model.Sun.PositionM);
            double speed = earth.VelocityMps.Magnitude;

            Assert.That(radius / Au, Is.InRange(0.98, 1.0));
            Assert.That(speed, Is.InRange(29_000.0, 31_000.0));
        }

        [Test]
        public void EveryPlanetStaysBetweenItsPerihelionAndAphelion()
        {
            foreach (BodyRuntime planet in _model.Ordered)
            {
                if (planet.Kind != "planet") continue;

                double radius = Vector3d.Distance(planet.PositionM, _model.Sun.PositionM);
                double a = planet.Elements.semiMajorAxisM;
                double e = planet.Elements.eccentricity;

                Assert.That(radius, Is.InRange(a * (1.0 - e) * 0.99, a * (1.0 + e) * 1.01),
                    planet.Id);
            }
        }

        [Test]
        public void PlanetSpeedsAgreeWithVisViva()
        {
            foreach (BodyRuntime planet in _model.Ordered)
            {
                if (planet.Kind != "planet") continue;

                double radius = Vector3d.Distance(planet.PositionM, _model.Sun.PositionM);
                double expected = Math.Sqrt(_model.Sun.Gm * (2.0 / radius - 1.0 / planet.Elements.semiMajorAxisM));
                Assert.That(planet.VelocityMps.Magnitude,
                    Is.EqualTo(expected).Within(expected * 0.01), planet.Id);
            }
        }

        [Test]
        public void EarthPeriodMatchesTheSiderealYear()
        {
            double days = _model.Get("earth").Elements.PeriodSeconds / JulianDate.SecondsPerDay;
            Assert.That(days, Is.EqualTo(365.25636).Within(0.05));
        }

        [Test]
        public void MoonsStayWithinTheirEccentricityBounds()
        {
            foreach (BodyRuntime moon in _model.Ordered)
            {
                if (moon.Kind != "moon") continue;

                double radius = Vector3d.Distance(moon.PositionM, moon.Parent.PositionM);
                double a = moon.Elements.semiMajorAxisM;
                double e = moon.Elements.eccentricity;

                Assert.That(radius, Is.InRange(a * (1.0 - e) * 0.99, a * (1.0 + e) * 1.01),
                    moon.Id);
            }
        }

        [Test]
        public void EveryMoonFitsInsideItsParentsHillSphere()
        {
            foreach (BodyRuntime moon in _model.Ordered)
            {
                if (moon.Kind != "moon") continue;

                Assert.That(moon.Elements.semiMajorAxisM,
                    Is.LessThan(moon.Parent.HillRadiusM), moon.Id);
            }
        }

        [Test]
        public void MoonPositionIsTheParentsPositionPlusTheRotatedOffset()
        {
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime moon = _model.Get("moon");
            moon.Elements.GetState(_model.CurrentJulianDate, out Vector3d localPosition, out Vector3d localVelocity);

            Vector3d offset = moon.Elements.parentEquatorFrame
                ? earth.FromEquator(localPosition)
                : localPosition;
            Vector3d offsetVelocity = moon.Elements.parentEquatorFrame
                ? earth.FromEquator(localVelocity)
                : localVelocity;

            MathAssert.AreClose(moon.PositionM, earth.PositionM + offset, 1e-3);
            MathAssert.AreClose(moon.VelocityMps, earth.VelocityMps + offsetVelocity, 1e-6);
        }

        [Test]
        public void OneMinuteFiniteDifferenceMatchesEarthVelocity()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d p0 = earth.PositionM;
            Vector3d v0 = earth.VelocityMps;

            // A Julian Date double at 2.4 million has roughly 40 microsecond
            // resolution, so a one-second step is too close to the precision
            // floor for a clean finite difference. One minute is comfortable.
            const double stepSeconds = 60.0;
            _model.Update(_model.CurrentJulianDate + stepSeconds / JulianDate.SecondsPerDay);

            Vector3d finiteDifference = (_model.Get("earth").PositionM - p0) / stepSeconds;
            Assert.That(Vector3d.Distance(finiteDifference, v0), Is.LessThan(1.0),
                "one-minute step should still be nearly linear");
        }

        [Test]
        public void OneDayWarpMovesEarthAboutTwoPointSixBillionMetres()
        {
            Vector3d p0 = _model.Get("earth").PositionM;
            _model.Update(_model.CurrentJulianDate + 1.0);
            double moved = Vector3d.Distance(_model.Get("earth").PositionM, p0);

            Assert.That(moved, Is.InRange(2.4e9, 2.8e9));
        }

        [Test]
        public void RepeatedUpdateToTheSameDateIsDeterministic()
        {
            var gateway = _model.Get("gateway");
            _model.Update(_model.CurrentJulianDate);
            Vector3d first = gateway.PositionM;
            _model.Update(_model.CurrentJulianDate);

            Assert.That(gateway.PositionM.x, Is.EqualTo(first.x));
            Assert.That(gateway.PositionM.y, Is.EqualTo(first.y));
            Assert.That(gateway.PositionM.z, Is.EqualTo(first.z));
        }

        [Test]
        public void GatewayStationSitsBetweenEarthAndTheMoon()
        {
            BodyRuntime gateway = _model.Get("gateway");
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime moon = _model.Get("moon");

            double toMoon = Vector3d.Distance(gateway.PositionM, moon.PositionM);
            double toEarth = Vector3d.Distance(gateway.PositionM, earth.PositionM);
            double separation = Vector3d.Distance(earth.PositionM, moon.PositionM);

            Assert.That(toMoon, Is.InRange(4.5e7, 7.0e7));
            Assert.That(toEarth, Is.InRange(2.9e8, 3.3e8));
            Assert.That(toEarth + toMoon, Is.EqualTo(separation).Within(1000.0),
                "L1 should lie on the Earth-Moon line");
        }

        [Test]
        public void AllStationsAreFiniteAndDistinctFromTheirSecondaries()
        {
            foreach (BodyRuntime station in _model.Ordered)
            {
                if (station.Kind != "station") continue;

                Assert.That(double.IsFinite(station.PositionM.x), Is.True, station.Id);
                Assert.That(double.IsFinite(station.PositionM.y), Is.True, station.Id);
                Assert.That(double.IsFinite(station.PositionM.z), Is.True, station.Id);

                BodyRuntime secondary = _model.Get(station.Data.lagrange.secondary);
                double separation = Vector3d.Distance(station.PositionM, secondary.PositionM);
                Assert.That(separation, Is.GreaterThan(0.0), station.Id);
            }
        }

        [Test]
        public void StationVelocityMatchesTheNextStepFiniteDifference()
        {
            var gateway = _model.Get("gateway");
            double jd0 = _model.CurrentJulianDate;
            _model.Update(jd0);
            Vector3d p0 = gateway.PositionM;

            _model.Update(jd0 + 60.0 / JulianDate.SecondsPerDay);
            Vector3d velocity = gateway.VelocityMps;
            Vector3d finiteDifference = (gateway.PositionM - p0) / 60.0;

            Assert.That(Vector3d.Distance(velocity, finiteDifference),
                Is.LessThan(Math.Max(1.0, velocity.Magnitude * 0.01)));
        }

        [Test]
        public void L4StationFormsAnEquilateralTriangle()
        {
            BodyRuntime station = _model.Get("jovian_yard");
            BodyRuntime jupiter = _model.Get("jupiter");
            BodyRuntime sun = _model.Sun;

            double sunToJupiter = Vector3d.Distance(sun.PositionM, jupiter.PositionM);
            double sunToStation = Vector3d.Distance(sun.PositionM, station.PositionM);
            double jupiterToStation = Vector3d.Distance(jupiter.PositionM, station.PositionM);

            Assert.That(sunToStation, Is.EqualTo(sunToJupiter).Within(sunToJupiter * 1e-9));
            Assert.That(jupiterToStation, Is.EqualTo(sunToJupiter).Within(sunToJupiter * 1e-9));
        }

        [Test]
        public void SampledEarthOrbitClosesAndMatchesTheElements()
        {
            BodyRuntime earth = _model.Get("earth");
            const int samples = 128;
            var buffer = new Vector3d[samples + 1];

            Assert.That(_model.SampleOrbitWorld(earth, samples, buffer), Is.True);
            MathAssert.AreClose(buffer[0], buffer[samples], 1.0);

            double a = earth.Elements.semiMajorAxisM;
            double e = earth.Elements.eccentricity;
            foreach (Vector3d point in buffer)
            {
                double radius = Vector3d.Distance(point, _model.Sun.PositionM);
                Assert.That(radius, Is.InRange(a * (1.0 - e) * 0.99, a * (1.0 + e) * 1.01));
            }
        }

        [Test]
        public void StationAndSunHaveNoOrbitToSample()
        {
            var buffer = new Vector3d[8];
            Assert.That(_model.SampleOrbitWorld(_model.Get("gateway"), 7, buffer), Is.False);
            Assert.That(_model.SampleOrbitWorld(_model.Sun, 7, buffer), Is.False);
        }

        [Test]
        public void FindNearestReturnsEachBodyAtItsOwnPosition()
        {
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime nearest = _model.FindNearest(earth.PositionM, body => body.IsRenderable);

            Assert.That(nearest, Is.SameAs(earth));
            Assert.That(_model.FindNearest(new Vector3d(-1e30, 0, 0)), Is.Not.Null);
        }

        [Test]
        public void UnknownBodyLookupReturnsNull()
        {
            Assert.That(_model.Get("not_a_body"), Is.Null);
        }

        [Test]
        public void EquatorBasisIsOrthonormalForEveryBody()
        {
            foreach (BodyRuntime body in _model.Ordered)
            {
                Assert.That(body.PoleEcliptic.Magnitude, Is.EqualTo(1.0).Within(1e-12), body.Id);
                Assert.That(body.EquatorX.Magnitude, Is.EqualTo(1.0).Within(1e-12), body.Id);
                Assert.That(body.EquatorY.Magnitude, Is.EqualTo(1.0).Within(1e-12), body.Id);
                Assert.That(Vector3d.Dot(body.EquatorX, body.EquatorY),
                    Is.EqualTo(0.0).Within(1e-12), body.Id);
                Assert.That(Vector3d.Dot(body.EquatorX, body.PoleEcliptic),
                    Is.EqualTo(0.0).Within(1e-12), body.Id);
                Assert.That(Vector3d.Dot(body.EquatorY, body.PoleEcliptic),
                    Is.EqualTo(0.0).Within(1e-12), body.Id);

                MathAssert.AreClose(body.FromEquator(new Vector3d(1, 0, 0)), body.EquatorX, 1e-12);
                MathAssert.AreClose(body.FromEquator(new Vector3d(0, 1, 0)), body.EquatorY, 1e-12);
                MathAssert.AreClose(body.FromEquator(new Vector3d(0, 0, 1)), body.PoleEcliptic, 1e-12);
            }
        }

        [Test]
        public void SpinAngleRepeatsAfterOneRotation()
        {
            BodyRuntime earth = _model.Get("earth");
            double jd = _model.CurrentJulianDate;
            double periodDays = earth.Data.rotationPeriodS / JulianDate.SecondsPerDay;

            double start = earth.SpinAngleRadians(jd);
            double after = earth.SpinAngleRadians(jd + periodDays);

            Assert.That(MathAssert.WrapPi(after - start), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(earth.SpinAngleRadians(jd), Is.EqualTo(0.0).Within(1e-9),
                "Earth's authored epoch phase starts at zero");
        }

        [Test]
        public void RootBodiesMeasureSpinFromJ2000()
        {
            BodyRuntime sun = _model.Sun;
            Assert.That(sun.SpinAngleRadians(JulianDate.J2000), Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void ElementsUseTheParentsGravitationalParameter()
        {
            Assert.That(_model.Get("earth").Elements.gravitationalParameter,
                Is.EqualTo(_model.Sun.Gm));
            Assert.That(_model.Get("moon").Elements.gravitationalParameter,
                Is.EqualTo(_model.Get("earth").Gm));
        }

        [Test]
        public void LoadingTwiceProducesTheSameState()
        {
            SolarSystemModel second = CatalogueFixture.LoadModel();

            MathAssert.AreClose(second.Get("earth").PositionM,
                _model.Get("earth").PositionM, 0.0);
            MathAssert.AreClose(second.Get("gateway").PositionM,
                _model.Get("gateway").PositionM, 0.0);
        }
    }
}
