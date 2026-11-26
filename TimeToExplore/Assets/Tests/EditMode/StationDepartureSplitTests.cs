using System;
using System.Diagnostics;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The **station** departure split, measured before it is wired in. A station
    /// plan ends on the hold state instead of a capture chain, so the planet
    /// rank-only cost has no entry state to price the brake against. The
    /// measurement seam
    /// <see cref="TripPlanner.MeasureStationSplitCandidate"/> enters at the hold
    /// state at the arrival date and drops the chain's duration; everything else
    /// is the planet cost. This suite grids it against the full fast-probe
    /// station build on the routes that carry a climb, and checks both no-climb
    /// cases: a heliocentric start, and a target whose parent is the departure
    /// body. No shipping plan changes here: the chooser still leaves a station
    /// unforced, so the tables are measurements, not a chosen plan.
    /// </summary>
    [Category("flight")]
    public class StationDepartureSplitTests
    {
        private const double BurnLevelG = 0.3;
        private const double SecondsPerDay = JulianDate.SecondsPerDay;
        private const double KmPerSecond = 1_000.0;

        /// <summary>The widest step of the coarse grid, 10 → 16 km/s. A cheap
        /// winner farther than this from the full one has been mis-ranked.</summary>
        private const double OneGridStepKms = 6.0;

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
                ArrivalStyle = CaptureStyle.Conic,
                BurnLevelG = BurnLevelG
            };
        }

        [TearDown]
        public void TearDown()
        {
            _planner.DepartureVInfinityOverrideMps = -1.0;
            _planner.UseDepartureSplit = true;
            _planner.Trace = null;
        }

        /// <summary>
        /// The station split sweep: the routes that carry a departure climb, at
        /// the durations the roadmap asks for. For each case the cheap rank-only
        /// search and the full fast-probe search run the shipping grid; the table
        /// is the measured trade and the two searches' agreement.
        /// </summary>
        [Test]
        public void TheCheapStationChooserMatchesTheFullSearchOnTheClimbingRoutes()
        {
            var grid = new[] { 0.0, 1.0, 3.0, 6.0, 10.0, 16.0 };

            TestContext.WriteLine("route | d | cheap v∞ | full v∞ | cheap dv | " +
                                  "full dv | delta | cheap ms");
            foreach (double days in new[] { 60.0, 90.0, 120.0, 160.0 })
            {
                Measure(grid, "gateway → ares_junction", "ares_junction", days,
                    FlightFixture.ShipAt(_model, "gateway"));
            }

            foreach (double days in new[] { 60.0, 90.0, 120.0 })
            {
                Measure(grid, "mars 300 km → gateway", "gateway", days,
                    FlightFixture.ShipInEquatorialOrbit(_model, "mars", 300e3));
            }
        }

        /// <summary>
        /// Gateway → Helios Watch is **not** a climbing route, although the
        /// roadmap lists it as one: Helios Watch's parent is Earth, and
        /// <see cref="TripPlanner.FindDepartureBody"/> skips the target's parent,
        /// so a ship inside Earth's sphere — Gateway — has no departure body for
        /// this target. The cheap cost must be infinite at every grid point and
        /// the plan must be the three-arc heliocentric trip. The full probe still
        /// builds it, because there is no climb to force.
        /// </summary>
        [Test]
        public void AStationWhoseParentIsTheDepartureBodyHasNoClimb()
        {
            var grid = new[] { 0.0, 1.0, 3.0, 6.0, 10.0, 16.0 };
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            TestContext.WriteLine("gateway → helios_watch | d | cheap | full v∞ | " +
                                  "full dv | arcs");
            foreach (double days in new[] { 60.0, 90.0, 120.0, 160.0 })
            {
                double seconds = days * SecondsPerDay;
                double cheapest = double.PositiveInfinity;
                foreach (double vInfinityKms in grid)
                {
                    cheapest = Math.Min(cheapest, _planner.MeasureStationSplitCandidate(
                        ship, "helios_watch", seconds, vInfinityKms * KmPerSecond));
                }

                (double FullVInfinityKms, double FullCostMps) full = Search(grid,
                    v => _planner.MeasureFullStationSplitCandidateCost(ship,
                        "helios_watch", seconds, v * KmPerSecond).CostMps, rounds: 2);

                TrajectoryPlan plan = _planner.PlanAtDuration(ship, "helios_watch",
                    seconds);
                TestContext.WriteLine($"  {days,6:F0} | " +
                                      $"{(double.IsFinite(cheapest) ? "finite" : "∞"),8} | " +
                                      $"{full.FullVInfinityKms,7:F2} | " +
                                      $"{full.FullCostMps / KmPerSecond,7:F3} | " +
                                      $"{plan.Arcs.Count,4}");

                Assert.That(cheapest, Is.EqualTo(double.PositiveInfinity),
                    $"{days:F0} d: the target's parent cannot be the departure body");
                Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
                Assert.That(plan.Arcs.Count, Is.EqualTo(3),
                    $"{days:F0} d: burn, coast, velocity match — no climb");
                Assert.That(plan.DepartureVInfinityMps, Is.EqualTo(-1.0),
                    $"{days:F0} d: no climb, so no excess speed");
            }
        }

        /// <summary>
        /// A heliocentric start has no departure body, so it has no break, no
        /// escape conic and nothing for the split to choose. The cheap cost must
        /// be infinite at every grid point — the no-climb control — and the
        /// shipped station plan must stay the three-arc heliocentric trip. The
        /// full probe still builds it, because there is no climb to force.
        /// </summary>
        [Test]
        public void AHeliocentricStationStartHasNoCheapCandidate()
        {
            var grid = new[] { 0.0, 1.0, 3.0, 6.0, 10.0, 16.0 };
            ShipState ship = FlightFixture.ShipAt(_model, "ares_junction");
            double seconds = 160.0 * SecondsPerDay;

            foreach (double vInfinityKms in grid)
            {
                double cheap = _planner.MeasureStationSplitCandidate(ship,
                    "gateway", seconds, vInfinityKms * KmPerSecond);
                TestContext.WriteLine($"no-climb control at v∞ {vInfinityKms,4:F0} km/s: " +
                                      $"cheap {(double.IsFinite(cheap) ? "finite" : "∞")}");
                Assert.That(cheap, Is.EqualTo(double.PositiveInfinity),
                    "a heliocentric start has no climb to cost");
            }

            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "gateway", seconds);
            TestContext.WriteLine($"no-climb plan: {plan.Arcs.Count} arcs, " +
                                  $"{plan.DeltaVMps / KmPerSecond:F4} km/s, " +
                                  $"v∞ {plan.DepartureVInfinityMps:F1}");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(3),
                "burn, coast, velocity match — no climb");
            Assert.That(plan.DepartureVInfinityMps, Is.EqualTo(-1.0),
                "the chooser does not apply");
        }

        /// <summary>
        /// This step adds measurement seams only. A station plan with a climb
        /// still takes the unforced parabolic climb, and it is the same plan with
        /// the split flag on or off: <see cref="TripPlanner.Build"/> hands a
        /// station a fixed `-1`, so the chooser never sees it.
        /// </summary>
        [Test]
        public void AClimbingStationPlanIsUnchangedByTheSplitFlag()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            double seconds = 120.0 * SecondsPerDay;

            _planner.UseDepartureSplit = true;
            TrajectoryPlan withFlag = _planner.PlanAtDuration(ship,
                "ares_junction", seconds);
            _planner.UseDepartureSplit = false;
            TrajectoryPlan withoutFlag = _planner.PlanAtDuration(ship,
                "ares_junction", seconds);

            TestContext.WriteLine($"station split flag: " +
                                  $"{withFlag.DeltaVMps / KmPerSecond:F4} km/s, " +
                                  $"{withFlag.FlightTimeDays:F5} d, " +
                                  $"{withFlag.Arcs.Count} arcs, " +
                                  $"v∞ {withFlag.DepartureVInfinityMps:F1}");

            Assert.That(withFlag.IsFeasible, Is.True, withFlag.InfeasibleReason);
            Assert.That(withFlag.DepartureVInfinityMps, Is.EqualTo(0.0),
                "a station still keeps the unforced parabolic climb");
            Assert.That(withFlag.DeltaVMps, Is.EqualTo(withoutFlag.DeltaVMps),
                "the flag does not reach a station plan");
            Assert.That(withFlag.FlightTimeDays,
                Is.EqualTo(withoutFlag.FlightTimeDays));
            Assert.That(withFlag.Arcs.Count, Is.EqualTo(withoutFlag.Arcs.Count));
        }

        /// <summary>One route/duration case: both searches, the table row and the
        /// ranking assertions.</summary>
        private void Measure(double[] grid, string route, string targetId, double days,
            ShipState ship)
        {
            double seconds = days * SecondsPerDay;
            var clock = Stopwatch.StartNew();
            (double CheapVInfinityKms, double CheapCostMps) cheap = Search(grid,
                v => _planner.MeasureStationSplitCandidate(ship, targetId, seconds,
                    v * KmPerSecond), rounds: 1);
            double milliseconds = clock.Elapsed.TotalMilliseconds;

            (double FullVInfinityKms, double FullCostMps) full = Search(grid,
                v => _planner.MeasureFullStationSplitCandidateCost(ship, targetId,
                    seconds, v * KmPerSecond).CostMps, rounds: 2);

            string cheapV = double.IsNaN(cheap.CheapVInfinityKms)
                ? "no climb" : $"{cheap.CheapVInfinityKms:F2}";
            string fullV = double.IsNaN(full.FullVInfinityKms)
                ? "none" : $"{full.FullVInfinityKms:F2}";
            string cheapCost = double.IsFinite(cheap.CheapCostMps)
                ? $"{cheap.CheapCostMps / KmPerSecond:F3}" : "∞";
            string fullCost = double.IsFinite(full.FullCostMps)
                ? $"{full.FullCostMps / KmPerSecond:F3}" : "∞";
            string difference = double.IsFinite(cheap.CheapCostMps)
                                && double.IsFinite(full.FullCostMps)
                ? $"{(cheap.CheapCostMps - full.FullCostMps) / KmPerSecond:F3}"
                : "—";

            TestContext.WriteLine($"{route} | {days,6:F0} | {cheapV,9} | {fullV,7} | " +
                                  $"{cheapCost,9} | {fullCost,7} | {difference,7} | " +
                                  $"{milliseconds,6:F1}");

            Assert.That(double.IsNaN(cheap.CheapVInfinityKms), Is.False,
                $"{route} {days:F0} d: the cheap search must find a fit");
            Assert.That(milliseconds, Is.LessThan(50.0),
                $"{route} {days:F0} d: the rank-only search is within its 50 ms budget");
            Assert.That(cheap.CheapVInfinityKms,
                Is.EqualTo(full.FullVInfinityKms).Within(OneGridStepKms),
                $"{route} {days:F0} d: the cheap choice is the full search's or " +
                "one grid step away");
        }

        /// <summary>The shipping search, unchanged: the coarse grid, then
        /// <paramref name="rounds"/> midpoint refinements around the winner. It
        /// returns NaN when no candidate fits.</summary>
        private static (double VInfinityKms, double CostMps) Search(double[] grid,
            Func<double, double> cost, int rounds)
        {
            int bestIndex = -1;
            double bestCost = double.PositiveInfinity;
            double best = 0.0;
            for (int i = 0; i < grid.Length; i++)
            {
                double candidate = cost(grid[i]);
                if (!(candidate < bestCost)) continue;
                bestCost = candidate;
                best = grid[i];
                bestIndex = i;
            }

            if (bestIndex < 0) return (double.NaN, double.PositiveInfinity);

            double low = bestIndex > 0 ? grid[bestIndex - 1] : grid[0];
            double high = bestIndex < grid.Length - 1
                ? grid[bestIndex + 1]
                : grid[grid.Length - 1];
            for (int round = 0; round < rounds; round++)
            {
                double lower = 0.5 * (low + best);
                double upper = 0.5 * (best + high);
                double lowerCost = cost(lower);
                double upperCost = cost(upper);
                if (lowerCost < bestCost && lowerCost <= upperCost)
                {
                    high = best;
                    best = lower;
                    bestCost = lowerCost;
                }
                else if (upperCost < bestCost)
                {
                    low = best;
                    best = upper;
                    bestCost = upperCost;
                }
                else
                {
                    low = lower;
                    high = upper;
                }
            }

            return (best, bestCost);
        }
    }
}
