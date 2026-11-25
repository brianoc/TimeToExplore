using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.EditorTools;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The route benchmark's own machinery: the route list resolves against the
    /// shipped catalogue, the baseline file parses or fails loudly, the delta is
    /// a percentage, and only a 25% rise is called a regression. No test asserts
    /// a time: the numbers belong to the machine the benchmark runs on.
    /// </summary>
    [Category("core")]
    public class PlanningBenchmarkTests
    {
        private const string Machine = "test-machine";
        private const string UnityVersion = "6000.3.28f1";

        [Test]
        public void EveryRouteResolvesItsBodiesAndDepartsFromTheRightPlace()
        {
            SolarSystemModel model = CatalogueFixture.LoadModel();
            Assert.That(model.CurrentJulianDate,
                Is.EqualTo(BenchmarkRoutes.EpochJulianDate).Within(1e-6),
                "the benchmark pins the catalogue's own epoch");

            Assert.That(BenchmarkRoutes.All, Has.Length.EqualTo(6));
            foreach (BenchmarkRoute route in BenchmarkRoutes.All)
            {
                Assert.That(model.Get(route.TargetId), Is.Not.Null,
                    route.Name + " has a target in the catalogue");
                BodyRuntime departure = model.Get(route.DepartureBodyId);
                Assert.That(departure, Is.Not.Null,
                    route.Name + " has a departure body in the catalogue");

                ShipState ship = route.DepartureState(model,
                    BenchmarkRoutes.EpochJulianDate);
                model.ComputeState(departure, ship.JulianDate,
                    out Vector3d bodyPosition, out Vector3d bodyVelocity);
                Vector3d offset = ship.PositionM - bodyPosition;

                if (route.Kind == BenchmarkDepartureKind.Station)
                {
                    Assert.That(offset.Magnitude, Is.LessThan(1.0),
                        route.Name + " starts on the station");
                    Assert.That(ship.VelocityMps,
                        Is.EqualTo(bodyVelocity), route.Name + " carries the station's velocity");
                }
                else
                {
                    double radius = ParkingOrbit.ConicRadiusM(departure);
                    Assert.That(offset.Magnitude, Is.EqualTo(radius).Within(1.0),
                        route.Name + " starts on the parking radius");
                    Assert.That(radius, Is.GreaterThan(departure.RadiusM),
                        route.Name + " parks above the surface");
                    double speed = TwoBody.CircularSpeedMps(departure.Gm, radius);
                    Assert.That((ship.VelocityMps - bodyVelocity).Magnitude,
                        Is.EqualTo(speed).Within(1e-6),
                        route.Name + " is in a circular orbit at that radius");
                    Assert.That(Vector3d.Dot(offset.Normalized,
                            (ship.VelocityMps - bodyVelocity).Normalized),
                        Is.EqualTo(0.0).Within(1e-9),
                        route.Name + "'s orbit is perpendicular to its radius");
                }
            }
        }

        [Test]
        public void TheSixRoutesAreTheOnesThePlanNames()
        {
            Assert.That(BenchmarkRoutes.All, Has.Length.EqualTo(6));
            var names = new System.Collections.Generic.List<string>();
            foreach (BenchmarkRoute route in BenchmarkRoutes.All)
            {
                names.Add(route.Name);
                Assert.That(route.BurnLevelG,
                    Is.InRange(TripPlanner.MinimumBurnLevelG, TripPlanner.MaxBurnLevelG),
                    route.Name + " uses a level the player can select");
                Assert.That(route.Why, Is.Not.Empty,
                    route.Name + " records why it departs how it does");
            }

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "earth-mars", "earth-venus", "earth-uranus",
                "mars-earth", "venus-earth", "uranus-earth"
            }));
        }

        [Test]
        public void ABaselineRoundTripsThroughItsJson()
        {
            BenchmarkBaseline written = SampleBaseline(1200.0);
            string json = BenchmarkReport.Serialize(written);

            Assert.That(BenchmarkReport.TryParse(json, out BenchmarkBaseline parsed,
                out string error), Is.True, error);
            Assert.That(parsed.machine, Is.EqualTo(Machine));
            Assert.That(parsed.unityVersion, Is.EqualTo(UnityVersion));
            Assert.That(parsed.routes, Has.Length.EqualTo(1));
            Assert.That(BenchmarkReport.LookupMilliseconds(parsed, "earth-mars",
                BenchmarkPhases.Plot), Is.EqualTo(1200.0).Within(0.001));
        }

        [Test]
        public void AMalformedOrForeignBaselineIsRefused()
        {
            Assert.That(BenchmarkReport.TryParse("", out _, out string empty), Is.False);
            Assert.That(empty, Is.Not.Empty);
            Assert.That(BenchmarkReport.TryParse("{", out _, out string broken), Is.False);
            Assert.That(broken, Is.Not.Empty);
            Assert.That(BenchmarkReport.TryParse(
                "{\"schema\":7,\"machine\":\"m\",\"unityVersion\":\"u\"," +
                "\"routes\":[{\"route\":\"r\",\"phases\":[]}]}",
                out _, out string schema), Is.False);
            Assert.That(schema, Does.Contain("schema"));
            Assert.That(BenchmarkReport.TryParse(
                "{\"schema\":1,\"unityVersion\":\"u\",\"routes\":[]}",
                out _, out string missing), Is.False);
            Assert.That(missing, Is.Not.Empty);
        }

        [Test]
        public void TheDeltaIsAPercentageOfTheBaseline()
        {
            Assert.That(BenchmarkReport.PercentChange(110.0, 100.0),
                Is.EqualTo(10.0).Within(1e-9));
            Assert.That(BenchmarkReport.PercentChange(90.0, 100.0),
                Is.EqualTo(-10.0).Within(1e-9));
            Assert.That(BenchmarkReport.PercentChange(0.0, 0.0), Is.EqualTo(0.0));
            Assert.That(BenchmarkReport.PercentChange(5.0, 0.0), Is.NaN,
                "a phase that did not run in the baseline has no percentage");
            Assert.That(BenchmarkReport.PercentChange(double.NaN, 100.0), Is.NaN);
        }

        [Test]
        public void OnlyATwentyFivePercentRiseIsARegression()
        {
            Assert.That(BenchmarkReport.IsRegression(24.9), Is.False);
            Assert.That(BenchmarkReport.IsRegression(25.0), Is.True);
            Assert.That(BenchmarkReport.IsRegression(60.0), Is.True);
            Assert.That(BenchmarkReport.IsRegression(-40.0), Is.False,
                "an improvement is not a regression");
            Assert.That(BenchmarkReport.IsRegression(double.NaN), Is.False);
        }

        [Test]
        public void TheTableShowsEveryPhaseInMillisecondsWithItsDelta()
        {
            BenchmarkBaseline run = SampleBaseline(1200.0);
            BenchmarkBaseline baseline = SampleBaseline(1000.0);

            string table = BenchmarkReport.FormatTable(run, baseline,
                comparable: true, color: false);

            Assert.That(table, Does.Contain("Earth → Mars"));
            Assert.That(table, Does.Contain("1,200 ms"));
            Assert.That(table, Does.Contain("+20.0%"));
            Assert.That(table, Does.Contain("total"),
                "the table ends with the measured phases' summed delta");
            Assert.That(table, Does.Contain("+4.4%"),
                "the total sums every measured phase, drag included");
            Assert.That(table, Does.Contain("No phase regressed"),
                "a 20% rise is below the 25% gate");
        }

        [Test]
        public void ARegressionAtTheThresholdIsShoutedAbout()
        {
            string table = BenchmarkReport.FormatTable(SampleBaseline(1300.0),
                SampleBaseline(1000.0), comparable: true, color: false);

            Assert.That(table, Does.Contain("+30.0%"));
            Assert.That(table, Does.Contain("regression(s) at or above 25%"));
        }

        [Test]
        public void AnotherMachineOrUnityVersionIsNotComparable()
        {
            BenchmarkBaseline baseline = SampleBaseline(1000.0);
            Assert.That(BenchmarkReport.IsComparable(baseline, Machine, UnityVersion),
                Is.True);
            Assert.That(BenchmarkReport.IsComparable(baseline, "other-machine",
                UnityVersion), Is.False);
            Assert.That(BenchmarkReport.IsComparable(baseline, Machine, "6000.3.29f1"),
                Is.False);

            string table = BenchmarkReport.FormatTable(SampleBaseline(1200.0),
                baseline, comparable: false, color: false);
            Assert.That(table, Does.Contain("another machine or Unity version"));
        }

        [Test]
        public void NoBaselineLeavesTheDeltasUnavailable()
        {
            string table = BenchmarkReport.FormatTable(SampleBaseline(1200.0),
                baseline: null, comparable: false, color: false);
            Assert.That(table, Does.Contain("none committed yet"));
            Assert.That(table, Does.Contain("n/a"));
        }

        private static BenchmarkBaseline SampleBaseline(double plotMilliseconds)
        {
            var phases = new BenchmarkPhaseRecord[BenchmarkPhases.All.Length];
            for (int i = 0; i < phases.Length; i++)
            {
                phases[i] = new BenchmarkPhaseRecord
                {
                    phase = BenchmarkPhases.All[i],
                    milliseconds = i == 0 ? plotMilliseconds : 500.0 + i
                };
            }

            return new BenchmarkBaseline
            {
                machine = Machine,
                unityVersion = UnityVersion,
                cpu = "test CPU",
                operatingSystem = "test OS",
                recordedAt = "2026-11-24",
                note = "test",
                routes = new[]
                {
                    new BenchmarkRouteRecord
                    {
                        route = "earth-mars",
                        displayName = "Earth → Mars",
                        burnLevelG = 0.3,
                        floorDays = 16.84485,
                        windowMaxDays = 50.529,
                        note = "test route",
                        phases = phases
                    }
                }
            };
        }
    }
}
