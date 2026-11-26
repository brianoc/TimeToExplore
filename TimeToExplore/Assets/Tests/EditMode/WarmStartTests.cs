using System;
using System.Diagnostics;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using TimeToExplore.UI;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The brake/departure pin starts from a neighbouring solved trip instead of
    /// the kinematic estimate (<see cref="PinSeed"/>). These tests hold the two
    /// ends of that trade:
    ///
    /// * a seeded build returns the **cold answer** — same arcs, delta-v to four
    ///   decimals, every seam inside the 1 km / 1 m/s gate — across the slider's
    ///   window, at Mars, Venus and Uranus and the benchmark's 0.3 g level;
    /// * it costs **fewer pin passes** for the same duration, measured with
    ///   <see cref="PlanMetrics"/>;
    /// * a seed that does not fit is abandoned after a few passes and the build
    ///   is repeated cold, so a wrong seed cannot change the answer.
    /// </summary>
    [Category("deep")]
    public class WarmStartTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TripPlanner _planner;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _planner = new TripPlanner(_model, _gravity)
            {
                BurnLevelG = BenchmarkLevelG,
                ArrivalStyle = CaptureStyle.Conic
            };
        }

        /// <summary>The benchmark's level for the three Earth departures.</summary>
        private const double BenchmarkLevelG = 0.3;

        private static readonly string[] Targets = { "mars", "venus", "uranus" };

        private TrajectoryPlan Build(ShipState ship, string targetId, double seconds,
            PinSeed seed, out PlanMetrics metrics)
        {
            metrics = new PlanMetrics();
            _planner.Metrics = metrics;
            metrics.Reset();
            try
            {
                return _planner.PlanAtDuration(ship, targetId, seconds, seed);
            }
            finally
            {
                _planner.Metrics = null;
            }
        }

        /// <summary>The window the player's plot opens: the burn-only/coast
        /// minimum and the slider's right end, from the session's own window.</summary>
        private void Window(ShipState ship, string targetId,
            out double minimum, out double maximum, out TrajectoryPlan floorPlan)
        {
            using (var session = new FlightSession(_model, _gravity,
                       new TripPlanner(_model, _gravity)
                       {
                           BurnLevelG = BenchmarkLevelG,
                           ArrivalStyle = CaptureStyle.Conic
                       }))
            {
                session.PlaceAt("gateway", ship.JulianDate);
                floorPlan = session.Plot(targetId, ship.JulianDate);
                minimum = session.PreviewMinimumSeconds;
                maximum = session.PreviewMaximumSeconds;
            }
        }

        private static double StepSeconds(double minimum, double maximum, int step) =>
            minimum + (maximum - minimum) * step / (SolarSystemHud.TimeSliderSteps - 1);

        /// <summary>The two plans are the same trip: the pin's start changed the
        /// walk to the fixed point, not the fixed point.</summary>
        private static void AssertSamePlan(TrajectoryPlan cold, TrajectoryPlan warm,
            string label)
        {
            Assert.That(warm, Is.Not.Null, label);
            Assert.That(warm.IsFeasible, Is.True, $"{label}: {warm.InfeasibleReason}");
            Assert.That(warm.Arcs.Count, Is.EqualTo(cold.Arcs.Count), label);
            Assert.That(warm.FlightTimeSeconds, Is.EqualTo(cold.FlightTimeSeconds).Within(1e-6),
                label);
            Assert.That(Math.Round(warm.DeltaVMps / 1000.0, 4),
                Is.EqualTo(Math.Round(cold.DeltaVMps / 1000.0, 4)),
                $"{label}: the seeded plan's delta-v is the cold plan's to four decimals");
            Assert.That(warm.PeakThrustMps2,
                Is.EqualTo(cold.PeakThrustMps2).Within(1e-6), label);

            double worstPosition = 0.0;
            double worstVelocity = 0.0;
            for (int i = 0; i < cold.Arcs.Count; i++)
            {
                PlanArc a = cold.Arcs[i];
                PlanArc b = warm.Arcs[i];
                worstPosition = Math.Max(worstPosition,
                    Vector3d.Distance(a.Path.PositionAt(1.0), b.Path.PositionAt(1.0)));
                worstVelocity = Math.Max(worstVelocity,
                    (a.Path.VelocityAt(1.0) - b.Path.VelocityAt(1.0)).Magnitude);
            }

            TestContext.WriteLine($"  {label}: delta-v " +
                $"{cold.DeltaVMps / 1000.0:F4} / {warm.DeltaVMps / 1000.0:F4} km/s, " +
                $"worst arc end {worstPosition:E2} m / {worstVelocity:E2} m/s");
            Assert.That(worstPosition, Is.LessThan(1_000.0),
                $"{label}: every arc end is inside the chain gate's kilometre");
            Assert.That(worstVelocity, Is.LessThan(1.0),
                $"{label}: every arc end is inside the chain gate's metre per second");
        }

        [Test]
        public void ColdAndSeededBuildsAgreeAcrossTheSlidersWindow()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            int[] steps = { 0, 1, 2, 7, 31, SolarSystemHud.TimeSliderSteps - 1 };

            foreach (string targetId in Targets)
            {
                Window(ship, targetId, out double minimum, out double maximum,
                    out TrajectoryPlan floorPlan);
                TestContext.WriteLine($"{targetId} 0.3 g: window " +
                    $"{minimum / JulianDate.SecondsPerDay:F5}-" +
                    $"{maximum / JulianDate.SecondsPerDay:F5} d");

                TrajectoryPlan neighbour = floorPlan;
                foreach (int step in steps)
                {
                    double seconds = StepSeconds(minimum, maximum, step);
                    double days = seconds / JulianDate.SecondsPerDay;

                    TrajectoryPlan cold = Build(ship, targetId, seconds, default,
                        out PlanMetrics coldMetrics);
                    TrajectoryPlan warm = Build(ship, targetId, seconds,
                        neighbour.PinSeed.Nearby(seconds), out PlanMetrics warmMetrics);

                    TestContext.WriteLine($"  step {step,2} {days,9:F5} d: cold " +
                        $"{coldMetrics.PinPasses} passes, seeded " +
                        $"{warmMetrics.PinPasses} passes, seed " +
                        $"{(neighbour.PinSeed.Covers(seconds) ? "yes" : "no")}, " +
                        $"fallbacks {warmMetrics.SeedFallbacks}");

                    AssertSamePlan(cold, warm, $"{targetId} step {step}");
                    neighbour = cold;
                }
            }
        }

        [Test]
        public void ASeededRetimeCutsThePinPassesAtTheSameDuration()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            int sumCold = 0;
            int sumWarm = 0;

            foreach (string targetId in Targets)
            {
                Window(ship, targetId, out double minimum, out double maximum, out _);
                double first = StepSeconds(minimum, maximum, 20);
                double second = StepSeconds(minimum, maximum, 21);

                TrajectoryPlan neighbour = Build(ship, targetId, first, default,
                    out _);

                long coldStart = Stopwatch.GetTimestamp();
                TrajectoryPlan cold = Build(ship, targetId, second, default,
                    out PlanMetrics coldMetrics);
                double coldMs = (Stopwatch.GetTimestamp() - coldStart)
                                * 1000.0 / Stopwatch.Frequency;

                long warmStart = Stopwatch.GetTimestamp();
                TrajectoryPlan warm = Build(ship, targetId, second,
                    neighbour.PinSeed.Nearby(second), out PlanMetrics warmMetrics);
                double warmMs = (Stopwatch.GetTimestamp() - warmStart)
                                * 1000.0 / Stopwatch.Frequency;

                TestContext.WriteLine($"{targetId} 0.3 g at " +
                    $"{second / JulianDate.SecondsPerDay:F5} d: cold " +
                    $"{coldMetrics.PinPasses} pin passes in {coldMs:F0} ms, seeded " +
                    $"{warmMetrics.PinPasses} in {warmMs:F0} ms, fallbacks " +
                    $"{warmMetrics.SeedFallbacks}");

                AssertSamePlan(cold, warm, $"{targetId} retime");
                Assert.That(neighbour.PinSeed.IsValid, Is.True,
                    "a converged full-profile build carries its pin");
                Assert.That(warmMetrics.SeedFallbacks, Is.LessThanOrEqualTo(1),
                    "a neighbouring seed settles inside the seeded pass cap");
                Assert.That(warmMetrics.PinPasses, Is.LessThan(coldMetrics.PinPasses),
                    "the seed removes passes at the same duration");

                sumCold += coldMetrics.PinPasses;
                sumWarm += warmMetrics.PinPasses;
            }

            TestContext.WriteLine($"total pin passes: cold {sumCold}, seeded {sumWarm} " +
                $"({100.0 * (sumCold - sumWarm) / sumCold:F1}% fewer)");
        }

        /// <summary>
        /// The floor is the certified duration the search first closes at, so a
        /// seed must not move it. The suite's Mars anchor is
        /// <see cref="BurnCoastBurnTripPlannerTests"/>'s; this pins the same
        /// number through the warm-enabled search and prints the other two.
        /// </summary>
        [Test]
        public void TheFloorsDoNotMoveWithSeeding()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan mars = _planner.Plan(ship, "mars");
            TrajectoryPlan venus = _planner.Plan(ship, "venus");
            TrajectoryPlan uranus = _planner.Plan(ship, "uranus");

            TestContext.WriteLine($"coast floors: mars {mars.FlightTimeDays:F5} d, " +
                $"venus {venus.FlightTimeDays:F5} d, " +
                $"uranus {uranus.FlightTimeDays:F5} d");
            Assert.That(mars.IsFeasible, Is.True, mars.InfeasibleReason);
            Assert.That(mars.FlightTimeDays, Is.EqualTo(16.84485).Within(0.05),
                "the Mars 0.3 g floor anchor");
            Assert.That(venus.IsFeasible, Is.True, venus.InfeasibleReason);
            Assert.That(uranus.IsFeasible, Is.True, uranus.InfeasibleReason);
            Assert.That(uranus.FlightTimeDays, Is.EqualTo(92.13088).Within(0.05),
                "the Uranus 0.3 g floor anchor: a seed must not move the frontier");
        }

        [Test]
        public void AWrongSeedIsAbandonedAndTheColdAnswerIsReturned()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            const double days = 33.7;
            double seconds = days * JulianDate.SecondsPerDay;

            TrajectoryPlan cold = Build(ship, "mars", seconds, default,
                out PlanMetrics coldMetrics);

            // A brake that would start before the departure and a burn from no
            // trip at all: this seed belongs to nothing, so the pin cannot settle
            // inside the seeded cap.
            var wrong = new PinSeed(seconds,
                40.0 * JulianDate.SecondsPerDay, new Vector3d(1, 2, 3),
                25.0 * JulianDate.SecondsPerDay, new Vector3d(-3, 0, 1),
                hasApproach: true);
            TrajectoryPlan warm = Build(ship, "mars", seconds, wrong,
                out PlanMetrics warmMetrics);

            TestContext.WriteLine($"wrong seed at {days} d: cold " +
                $"{coldMetrics.PinPasses} passes, seeded attempt " +
                $"{warmMetrics.PinPasses} passes, fallbacks " +
                $"{warmMetrics.SeedFallbacks}");

            Assert.That(warmMetrics.SeedFallbacks, Is.GreaterThan(0),
                "a seed that does not settle is abandoned");
            AssertSamePlan(cold, warm, "wrong seed");

            // The failed attempt costs the seeded cap plus the cold build, not a
            // full build per attempt: the seeded passes are the few cheap ones.
            Assert.That(warmMetrics.PinPasses,
                Is.LessThanOrEqualTo(coldMetrics.PinPasses + 4),
                "a bad seed costs a few passes, not a second full build");
        }

        /// <summary>
        /// The session skips a seed for the two lowest slider steps. The guard is
        /// the HUD's own 64-step control, so the two constants must agree.
        /// </summary>
        [Test]
        public void TheUnseededSliderStepsAreTheHudsFirstTwo()
        {
            Assert.That(FlightSession.SeedExcludedLowFraction,
                Is.EqualTo(2.0 / (SolarSystemHud.TimeSliderSteps - 1)).Within(1e-12));
        }

        /// <summary>
        /// The retime path the player actually uses: a plot fills the window and
        /// the next retime starts from the cache. The seeded retime is the cold
        /// plan, and it costs fewer pin passes.
        /// </summary>
        [Test]
        public void TheSessionsRetimeUsesTheCachedNeighboursSeed()
        {
            double epoch = _model.CurrentJulianDate;
            double first;
            double second;
            double coldSeconds;

            TrajectoryPlan cold;
            var coldMetrics = new PlanMetrics();
            var referencePlanner = new TripPlanner(_model, _gravity)
            {
                BurnLevelG = BenchmarkLevelG,
                ArrivalStyle = CaptureStyle.Conic
            };
            using (var reference = new FlightSession(_model, _gravity, referencePlanner))
            {
                ShipState ship = FlightFixture.ShipAt(_model, "gateway");
                reference.PlaceAt("gateway", epoch);
                reference.Plot("mars", epoch);
                first = StepSeconds(reference.PreviewMinimumSeconds,
                    reference.PreviewMaximumSeconds, 20);
                second = StepSeconds(reference.PreviewMinimumSeconds,
                    reference.PreviewMaximumSeconds, 21);
                coldSeconds = second;

                referencePlanner.Metrics = coldMetrics;
                coldMetrics.Reset();
                try
                {
                    cold = reference.SetPreviewDuration(second);
                }
                finally
                {
                    referencePlanner.Metrics = null;
                }
            }

            var metrics = new PlanMetrics();
            TrajectoryPlan warm;
            using (var session = new FlightSession(_model, _gravity, _planner))
            {
                session.BurnLevelG = BenchmarkLevelG;
                session.ArrivalStyle = CaptureStyle.Conic;
                session.PlaceAt("gateway", epoch);
                session.Plot("mars", epoch);
                session.SetPreviewDuration(first);

                _planner.Metrics = metrics;
                metrics.Reset();
                try
                {
                    warm = session.SetPreviewDuration(second);
                }
                finally
                {
                    _planner.Metrics = null;
                }
            }

            TestContext.WriteLine($"session retime at " +
                $"{coldSeconds / JulianDate.SecondsPerDay:F5} d: cold " +
                $"{coldMetrics.PinPasses} pin passes, seeded {metrics.PinPasses}, " +
                $"fallbacks {metrics.SeedFallbacks}");

            Assert.That(metrics.SeedFallbacks, Is.Zero,
                "the session hands the nearest cached plan's pin over");
            Assert.That(metrics.PinPasses, Is.LessThan(coldMetrics.PinPasses),
                "the cached neighbour's seed removes passes");
            AssertSamePlan(cold, warm, "session retime");
        }
    }
}
