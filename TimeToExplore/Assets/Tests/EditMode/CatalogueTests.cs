using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    public class CatalogueTests
    {
        private static SolarSystemFile Data => CatalogueFixture.Data;
        private static Dictionary<string, BodyData> _byId;

        private static Dictionary<string, BodyData> ById
        {
            get
            {
                if (_byId == null)
                    _byId = Data.bodies.ToDictionary(body => body.id, StringComparer.Ordinal);
                return _byId;
            }
        }

        private static readonly HashSet<string> Kinds =
            new HashSet<string>(StringComparer.Ordinal) { "star", "planet", "moon", "asteroid", "station" };

        private static void AssertOneOf(string value, params string[] allowed)
        {
            Assert.That(allowed, Does.Contain(value),
                $"'{value}' is not one of {string.Join(", ", allowed)}");
        }

        [Test]
        public void JsonParsesIntoEveryBody()
        {
            Assert.That(Data, Is.Not.Null);
            Assert.That(Data.bodies, Is.Not.Null);
            Assert.That(Data.bodies.All(body => body != null), Is.True);
        }

        [Test]
        public void ShippedCatalogueHasTheDocumentedBodyCount()
        {
            Assert.That(Data.bodies.Length, Is.EqualTo(137));
        }

        [Test]
        public void KindCountsMatchTheDesign()
        {
            var counts = Data.bodies.GroupBy(body => body.kind)
                .ToDictionary(group => group.Key, group => group.Count());

            Assert.That(counts["star"], Is.EqualTo(1));
            Assert.That(counts["planet"], Is.EqualTo(8));
            Assert.That(counts["moon"], Is.EqualTo(22));
            Assert.That(counts["asteroid"], Is.EqualTo(100));
            Assert.That(counts["station"], Is.EqualTo(6));
        }

        [Test]
        public void EveryKindIsKnown()
        {
            foreach (BodyData body in Data.bodies)
                Assert.That(Kinds.Contains(body.kind), Is.True, body.id);
        }

        [Test]
        public void IdsAndDisplayNamesAreUnique()
        {
            Assert.That(Data.bodies.Select(body => body.id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(Data.bodies.Length), "duplicate id");
            Assert.That(Data.bodies.Select(body => body.name).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(Data.bodies.Length), "duplicate display name");
        }

        [Test]
        public void AsteroidNamesThatCollideWithMoonsAreNumberQualified()
        {
            Assert.That(ById.ContainsKey("ast_52"), Is.True);
            Assert.That(ById["ast_52"].name, Is.EqualTo("52 Europa"));
            Assert.That(ById["ast_85"].name, Is.EqualTo("85 Io"));
            Assert.That(ById["ast_106"].name, Is.EqualTo("106 Dione"));
        }

        [Test]
        public void TheSunIsTheOnlyRootAndIsAStar()
        {
            BodyData[] roots = Data.bodies
                .Where(body => string.IsNullOrEmpty(body.parent))
                .ToArray();

            Assert.That(roots.Length, Is.EqualTo(1));
            Assert.That(roots[0].id, Is.EqualTo("sun"));
            Assert.That(roots[0].kind, Is.EqualTo("star"));
        }

        [Test]
        public void EveryParentResolvesAndTheHierarchyTerminates()
        {
            foreach (BodyData body in Data.bodies)
            {
                if (string.IsNullOrEmpty(body.parent)) continue;

                Assert.That(ById.ContainsKey(body.parent), Is.True,
                    $"{body.id} has unknown parent {body.parent}");
                Assert.That(body.parent, Is.Not.EqualTo(body.id), $"{body.id} is its own parent");

                var seen = new HashSet<string>(StringComparer.Ordinal) { body.id };
                string current = body.parent;
                while (!string.IsNullOrEmpty(current))
                {
                    Assert.That(seen.Add(current), Is.True,
                        $"parent cycle at {body.id}");
                    Assert.That(ById.ContainsKey(current), Is.True,
                        $"{body.id} has a broken ancestor {current}");
                    current = ById[current].parent;
                }
            }
        }

        [Test]
        public void PhysicalQuantitiesAreFiniteAndPositive()
        {
            foreach (BodyData body in Data.bodies)
            {
                Assert.That(body.radiusM, Is.GreaterThan(0.0), $"{body.id} radius");
                Assert.That(double.IsFinite(body.radiusM), Is.True, $"{body.id} radius");
                Assert.That(double.IsFinite(body.gm), Is.True, $"{body.id} gm");
                Assert.That(double.IsFinite(body.rotationPeriodS), Is.True, $"{body.id} rotation");
                Assert.That(double.IsFinite(body.axialTiltDeg), Is.True, $"{body.id} tilt");
                Assert.That(double.IsFinite(body.poleRaDeg), Is.True, $"{body.id} pole RA");
                Assert.That(double.IsFinite(body.poleDecDeg), Is.True, $"{body.id} pole Dec");
                Assert.That(body.poleDecDeg, Is.InRange(-90.0, 90.0), $"{body.id} pole Dec");

                if (body.kind == "station")
                    Assert.That(body.gm, Is.EqualTo(0.0), $"{body.id} station gm");
                else
                    Assert.That(body.gm, Is.GreaterThan(0.0), $"{body.id} gm");

                Assert.That(body.color, Is.Not.Null, $"{body.id} color");
                Assert.That(body.color.Length, Is.EqualTo(3), $"{body.id} color length");
                foreach (float channel in body.color)
                    Assert.That(channel, Is.InRange(0f, 1f), $"{body.id} color channel");

                Assert.That(string.IsNullOrWhiteSpace(body.description), Is.False,
                    $"{body.id} description");
            }
        }

        [Test]
        public void OrbitingBodiesHaveUsableElements()
        {
            foreach (BodyData body in Data.bodies)
            {
                bool shouldOrbit = body.kind == "planet"
                                   || body.kind == "moon"
                                   || body.kind == "asteroid";

                if (!shouldOrbit) continue;

                Assert.That(body.orbit, Is.Not.Null, $"{body.id} orbit object");
                Assert.That(body.orbit.aM, Is.GreaterThan(0.0), $"{body.id} aM");
                Assert.That(body.orbit.e, Is.InRange(0.0, 0.999999), $"{body.id} e");
                Assert.That(double.IsFinite(body.orbit.epochJulianDate), Is.True,
                    $"{body.id} epoch");
                Assert.That(body.orbit.epochJulianDate,
                    Is.EqualTo(Data.startJulianDate).Within(1e-9), $"{body.id} epoch");
                AssertOneOf(body.orbit.plane, "ecliptic", "parentEquator");
                Assert.That(Math.Abs(body.orbit.iDeg), Is.LessThanOrEqualTo(180.0),
                    $"{body.id} inclination");
            }
        }

        [Test]
        public void StationsHaveAValidLagrangeConfiguration()
        {
            foreach (BodyData body in Data.bodies.Where(body => body.kind == "station"))
            {
                Assert.That(body.lagrange, Is.Not.Null, $"{body.id} lagrange");
                Assert.That(ById.ContainsKey(body.lagrange.primary), Is.True,
                    $"{body.id} primary");
                Assert.That(ById.ContainsKey(body.lagrange.secondary), Is.True,
                    $"{body.id} secondary");
                AssertOneOf(body.lagrange.point, "L1", "L2", "L4", "L5");
                Assert.That(body.parent, Is.EqualTo(body.lagrange.secondary),
                    $"{body.id} parent");
            }
        }

        [Test]
        public void EpochAndStartDateAreConsistent()
        {
            Assert.That(Data.epochUtc, Is.EqualTo("2026-11-15T00:00:00Z"));
            Assert.That(Data.epochJulianDate, Is.EqualTo(2461359.5).Within(1e-9));
            Assert.That(Data.startJulianDate, Is.EqualTo(Data.epochJulianDate).Within(1e-9));
            Assert.That(JulianDate.FormatShort(Data.startJulianDate),
                Is.EqualTo("2026-11-15 00:00"));
        }

        [Test]
        public void MoonsUseTheirParentAsTheOrbitReference()
        {
            foreach (BodyData moon in Data.bodies.Where(body => body.kind == "moon"))
            {
                AssertOneOf(ById[moon.parent].kind, "planet", "moon", "asteroid");
                AssertOneOf(moon.orbit.plane, "parentEquator", "ecliptic");
            }

            Assert.That(ById["moon"].orbit.plane, Is.EqualTo("ecliptic"),
                "the Moon is the documented ecliptic-plane exception");
        }
    }
}
